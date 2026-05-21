// CoreLib/StreamReader.cs

using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CoreLib;

/// <summary>
/// 面向控制台的高性能流读取器，继承自 <see cref="TextReader"/>，实现了 <see cref="IDisposable"/>。
/// 内部使用非托管内存维护两级缓冲区：
/// <list type="bullet">
///   <item><description>字节缓冲区：从底层输入句柄读取原始 UTF-8 字节数据。</description></item>
///   <item><description>字符缓冲区：将 UTF-8 字节解码为 UTF-16 字符，供上层逐字符或逐行读取。</description></item>
/// </list>
/// 所有读取操作均通过 <see cref="_lock"/> 保证线程安全。
/// 本类为 <c>partial class</c>，字符缓冲区字段定义于 <c>StreamReader.CharBuffer.cs</c>，
/// 字节缓冲区字段定义于 <c>StreamReader.ByteBuffer.cs</c>。
/// </summary>
public unsafe partial class StreamReader : TextReader, IDisposable
{
    /// <summary>标记当前实例是否已被释放，防止重复释放和释放后使用。</summary>
    private bool _disposed;

    /// <summary>用于保护所有读取操作的互斥锁，确保多线程环境下的数据一致性。</summary>
    private readonly Lock _lock = new();

    /// <summary>底层输入句柄（文件描述符或 Windows HANDLE），用于系统级读取调用。</summary>
    private readonly IntPtr _inputHandle;

    /// <summary>
    /// UTF-8 解码器，用于将从底层读取的字节流解码为 UTF-16 字符序列。
    /// 保持状态以正确处理跨缓冲区边界的多字节字符序列。
    /// </summary>
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();

    /// <summary>
    /// 私有构造函数，初始化读取器的输入句柄及两级缓冲区。
    /// 字符缓冲区和字节缓冲区均使用 <see cref="NativeMemory.Alloc(nuint)"/> 在非托管堆上分配，
    /// 避免 GC 移动导致的内存固定开销。
    /// </summary>
    /// <param name="inputHandle">底层输入句柄（如 stdin 的句柄/文件描述符）。</param>
    /// <param name="charSize">字符缓冲区的初始大小（以字符数为单位），默认为 512。</param>
    /// <param name="byteSize">字节缓冲区的初始大小（以字节数为单位），默认为 4096。</param>
    private StreamReader(IntPtr inputHandle, int charSize = 512, int byteSize = 4096)
    {
        _inputHandle = inputHandle;
        _charSize = charSize;
        _charBuffer = (char*)NativeMemory.Alloc((nuint)_charSize * sizeof(char));

        _byteSize = byteSize;
        _byteBuffer = (byte*)NativeMemory.Alloc((nuint)_byteSize);
    }

    /// <summary>
    /// 析构函数（终结器），作为 <see cref="Dispose"/> 未被调用时的安全兜底。
    /// 负责释放字符缓冲区和字节缓冲区所占用的非托管内存，防止内存泄漏。
    /// 注意：析构函数不加锁也不检查 <see cref="_disposed"/>，
    /// 仅检查指针是否非空以保证安全释放。
    /// </summary>
    ~StreamReader()
    {
        if (_charBuffer != null)
        {
            NativeMemory.Free(_charBuffer);
            _charBuffer = null;
        }
        
        if (_byteBuffer != null)
        {
            NativeMemory.Free(_byteBuffer);
            _byteBuffer = null;
        }
    }

    /// <summary>
    /// 释放当前实例所占用的非托管资源（字符缓冲区和字节缓冲区）。
    /// 调用此方法后，读取器将不再可用，后续读取操作将返回 <c>-1</c>（已释放）。
    /// 同时调用 <see cref="GC.SuppressFinalize"/> 阻止析构函数执行，避免重复释放。
    /// </summary>
    public void Dispose()
    {
        DisposeCore();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 释放核心逻辑，在持有 <see cref="_lock"/> 的情况下执行，确保线程安全。
    /// 若已被释放（<see cref="_disposed"/> 为 <c>true</c>），则直接返回，实现幂等性。
    /// 否则将 <see cref="_disposed"/> 置为 <c>true</c>，然后依次释放字符缓冲区和字节缓冲区的非托管内存。
    /// </summary>
    private void DisposeCore()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
        
            if (_charBuffer != null)
            {
                NativeMemory.Free(_charBuffer);
                _charBuffer = null;
            }
        
            if (_byteBuffer != null)
            {
                NativeMemory.Free(_byteBuffer);
                _byteBuffer = null;
            }
        }
    }
    
    /// <summary>
    /// 从输入流中读取字符到指定的 <see cref="Span{char}"/> 缓冲区中。
    /// 重写基类 <see cref="Read(Span{char})"/> 方法以支持高性能读取（减少中间分配）。
    /// 操作在 <see cref="_lock"/> 保护下执行，确保线程安全。
    /// </summary>
    /// 
    /// <para>
    /// 优先消耗当前字符缓冲区（<c>_charBuffer</c>）中的数据；
    /// 当字符缓冲区耗尽时，调用 <see cref="FillCharBuffer"/> 进行字节填充与解码，然后继续复制。
    /// 整个过程无额外 char[] 分配（除内部缓冲区复用外）。
    /// </para>
    /// 
    /// <param name="buffer">目标 <see cref="Span{char}"/>，用于存放读取到的字符。</param>
    /// <returns>
    /// 实际成功读取并写入 <paramref name="buffer"/> 的字符数；
    /// 若流已结束、实例已释放或 <paramref name="buffer"/> 为空，则返回 <c>0</c>。
    /// </returns>
    public override int Read(Span<char> buffer)
    {
        if (buffer.IsEmpty)
            return 0;

        lock (_lock)
        {
            if (_disposed)
                return 0;

            var totalRead = 0;

            while (!buffer.IsEmpty)
            {
                if (_charPos >= _charLen && !FillCharBuffer())
                    break;

                var available = _charLen - _charPos;
                var toCopy = Math.Min(available, buffer.Length);

                for (var i = 0; i < toCopy; i++)
                {
                    buffer[i] = _charBuffer[_charPos + i];
                }

                _charPos += toCopy;
                buffer = buffer[toCopy..];
                totalRead += toCopy;
            }

            return totalRead;
        }
    }
    
    /// <summary>
    /// 从底层字节缓冲区中读取原始字节到指定的 <see cref="Span{byte}"/> 中（绕过字符解码）。
    /// 此方法直接操作字节层，适用于需要获取原始输入字节的场景（如二进制协议或自定义解析）。
    /// 操作在 <see cref="_lock"/> 保护下执行，确保线程安全。
    /// </summary>
    /// 
    /// <para>
    /// <b>注意：</b>如果与字符读取（<see cref="Read()"/> 或 <see cref="Read(Span{char})"/>）混合使用，
    /// 可能会导致 <see cref="_decoder"/> 状态不一致或已解码数据被跳过，请谨慎混用。
    /// </para>
    /// 
    /// <param name="buffer">目标 <see cref="Span{byte}"/>，用于存放读取到的原始字节。</param>
    /// <returns>
    /// 实际成功读取的字节数；
    /// 若流已结束、实例已释放或 <paramref name="buffer"/> 为空，则返回 <c>0</c>。
    /// </returns>
    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty)
            return 0;

        lock (_lock)
        {
            if (_disposed)
                return 0;

            var totalRead = 0;

            while (!buffer.IsEmpty)
            {
                if (_bytePos >= _byteLen && !FillByteBuffer())
                    break;

                var available = _byteLen - _bytePos;
                var toCopy = Math.Min(available, buffer.Length);

                for (var i = 0; i < toCopy; i++)
                {
                    buffer[i] = _byteBuffer[_bytePos + i];
                }

                _bytePos += toCopy;
                buffer = buffer[toCopy..];
                totalRead += toCopy;
            }

            return totalRead;
        }
    }

    /// <summary>
    /// 从底层输入句柄读取数据以填充字节缓冲区（<c>_byteBuffer</c>）。
    /// <para>
    /// 若字节缓冲区中有未被解码的剩余数据（<see cref="_bytePos"/> &gt; 0），
    /// 则先将其移动到缓冲区头部（滚动操作），释放尾部空间用于新数据的读取。
    /// 然后通过 <see cref="ConsolePal.Read"/> 向底层发起一次读取调用，
    /// 将新数据追加到缓冲区已有数据之后。
    /// </para>
    /// </summary>
    /// <returns>
    /// 若成功读取到至少 1 字节的新数据，返回 <c>true</c>；
    /// 若底层返回 0 或负值（流结束或读取失败），返回 <c>false</c>。
    /// </returns>
    private bool FillByteBuffer()
    {
        if (_bytePos > 0)
        {
            _byteLen -= _bytePos;
            if (_byteLen > 0)
            {
                Buffer.MemoryCopy(
                    _byteBuffer + _bytePos,
                    _byteBuffer,
                    _byteSize, 
                    _byteLen);
            }
            
            _bytePos = 0;
        }

        var space = _byteSize - _byteLen;
        var n = ConsolePal.Read(_inputHandle, _byteBuffer + _byteLen, space);
        
        if (n <= 0)
            return false;

        _byteLen += n;
        return true;
    }

    /// <summary>
    /// 使用 <see cref="_decoder"/> 将字节缓冲区中未解码的字节序列解码为 UTF-16 字符，
    /// 并填充字符缓冲区（<c>_charBuffer</c>）。
    /// <para>
    /// 若字节缓冲区中已无可读字节，则调用 <see cref="FillByteBuffer"/> 从底层读取新数据。
    /// 解码成功后，<see cref="_bytePos"/> 推进到 <see cref="_byteLen"/>（消耗所有字节），
    /// <see cref="_charPos"/> 重置为 0，<see cref="_charLen"/> 设置为解码得到的字符数。
    /// </para>
    /// </summary>
    /// <returns>
    /// 若成功解码出至少 1 个字符，返回 <c>true</c>；
    /// 若字节缓冲区填充失败或解码结果为空，返回 <c>false</c>。
    /// </returns>
    private bool FillCharBuffer()
    {
        if (_bytePos >= _byteLen && !FillByteBuffer())
            return false;

        var charCount = _decoder.GetChars(
            _byteBuffer + _bytePos,
            _byteLen - _bytePos,
            _charBuffer, 
            _charSize,
            false);

        _bytePos = _byteLen;
        _charPos = 0;
        _charLen = charCount;
        return charCount > 0;
    }

    /// <summary>
    /// 从输入流中读取下一个字符，重写基类 <see cref="TextReader.Read"/> 方法。
    /// 操作在 <see cref="_lock"/> 保护下执行，确保线程安全。
    /// <para>
    /// 若实例已被释放，直接返回 <c>-1</c>。
    /// 若字符缓冲区中无可用字符，则调用 <see cref="FillCharBuffer"/> 填充缓冲区；
    /// 若填充失败（流已结束），返回 <c>-1</c>。
    /// 否则从字符缓冲区中取出当前位置的字符，推进 <see cref="_charPos"/>，并返回该字符的 Unicode 码点。
    /// </para>
    /// </summary>
    /// <returns>
    /// 读取到的字符的 Unicode 码点（范围 0~65535）；
    /// 若流已结束或实例已被释放，则返回 <c>-1</c>。
    /// </returns>
    public override int Read()
    {
        lock (_lock)
        {
            if (_disposed)
                return -1;
        
            if (_charPos >= _charLen && !FillCharBuffer())
                return -1;

            return _charBuffer[_charPos++];
        }
    }
    
    /// <summary>
    /// 重新设置字符缓冲区和字节缓冲区的大小。
    /// 执行步骤：
    /// <list type="number">
    ///   <item><description>验证参数有效性，大小必须大于 0；</description></item>
    ///   <item><description>分配新的字符缓冲区，若失败则不影响现有状态；</description></item>
    ///   <item><description>分配新的字节缓冲区，若失败则释放已分配的字符缓冲区并重新抛出异常；</description></item>
    ///   <item><description>释放旧缓冲区并更新所有相关字段。</description></item>
    /// </list>
    /// </summary>
    /// <param name="charSize">新的字符缓冲区大小（字符数），默认为 512。必须大于 0。</param>
    /// <param name="byteSize">新的字节缓冲区大小（字节数），默认为 4096。必须大于 0。</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// 当 <paramref name="charSize"/> 或 <paramref name="byteSize"/> 小于 1 时抛出。
    /// </exception>
    public override void SetBufferSize(int charSize = 512, int byteSize = 4096)
    {
        if (charSize < 1)
            throw new ArgumentOutOfRangeException(nameof(charSize), charSize, "The character buffer size must be greater than 0.");
        if (byteSize < 1)
            throw new ArgumentOutOfRangeException(nameof(byteSize), byteSize, "The byte buffer size must be greater than 0.");

        if (_disposed) return;

        lock (_lock)
        {
            var newCharBuffer = (char*)NativeMemory.Alloc((nuint)charSize * sizeof(char));
            byte* newByteBuffer;
            try
            {
                newByteBuffer = (byte*)NativeMemory.Alloc((nuint)byteSize);
            }
            catch
            {
                NativeMemory.Free(newCharBuffer);
                throw;
            }
            
            if (_charBuffer != null) NativeMemory.Free(_charBuffer);
            if (_byteBuffer != null) NativeMemory.Free(_byteBuffer);
            
            _charBuffer = newCharBuffer;
            _byteBuffer = newByteBuffer;
            _charSize = charSize;
            _byteSize = byteSize;
            _charPos = 0;
            _bytePos = 0;
        }
    }

    /// <summary>
    /// 创建一个绑定到标准输入（stdin）的 <see cref="StreamReader"/> 实例。
    /// 使用 <see cref="ConsolePal.StdInHandle"/> 作为底层输入句柄。
    /// </summary>
    /// <returns>绑定到 stdin 的新 <see cref="StreamReader"/> 实例。</returns>
    public static StreamReader CreateForIn() => new(ConsolePal.StdInHandle);
}