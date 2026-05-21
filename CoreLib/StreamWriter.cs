// CoreLib/StreamWriter.cs

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace CoreLib;

/// <summary>
/// 面向控制台的高性能流写入器，继承自 <see cref="TextWriter"/>，实现了 <see cref="IDisposable"/>。
/// 内部使用非托管内存（通过 <see cref="NativeMemory"/>）维护两级缓冲区：
/// <list type="bullet">
///   <item><description>字符缓冲区（UTF-16）：用于暂存未编码的字符数据，减少编码转换频率。</description></item>
///   <item><description>字节缓冲区（UTF-8）：用于暂存已编码的字节数据，减少系统调用次数。</description></item>
/// </list>
/// 所有写入操作均通过 <see cref="_lock"/> 保证线程安全。
/// 本类为 <c>partial class</c>，字符缓冲区字段定义于 <c>StreamWriter.CharBuffer.cs</c>，
/// 字节缓冲区字段及其刷新逻辑定义于 <c>StreamWriter.ByteBuffer.cs</c>。
/// </summary>
public unsafe partial class StreamWriter : TextWriter, IDisposable
{
    /// <summary>是否启用字符缓冲区。默认为 <c>true</c>。</summary>
    private bool _enableCharBuffer = true;

    /// <summary>是否启用字节缓冲区。默认为 <c>true</c>。</summary>
    private bool _enableByteBuffer = true;

    /// <summary>是否在写入行尾时自动触发刷新。默认为 <c>false</c>。</summary>
    private bool _enableAutoFlush;

    /// <summary>标记当前实例是否已被释放，防止重复释放和释放后使用。</summary>
    private bool _disposed;

    /// <summary>用于保护所有写入操作和状态变更的互斥锁，确保线程安全。</summary>
    private readonly Lock _lock = new();

    /// <summary>底层输出句柄（文件描述符或 Windows HANDLE），用于系统级 I/O 调用。</summary>
    private readonly IntPtr _outputHandle;

    /// <summary>
    /// 私有构造函数，初始化写入器的输出句柄及两级缓冲区。
    /// 字符缓冲区和字节缓冲区均使用 <see cref="NativeMemory.Alloc(nuint)"/> 在非托管堆上分配，
    /// 以避免 GC 移动内存导致的固定（pin）开销。
    /// </summary>
    /// <param name="outputHandle">底层输出句柄（如 stdout 或 stderr 的句柄/文件描述符）。</param>
    /// <param name="charSize">字符缓冲区的初始大小（以字符数为单位），默认为 512。</param>
    /// <param name="byteSize">字节缓冲区的初始大小（以字节数为单位），默认为 4096。</param>
    private StreamWriter(IntPtr outputHandle, int charSize = 512, int byteSize = 4096)
    {
        _outputHandle = outputHandle;
        _charSize = charSize;
        _charBuffer = (char*)NativeMemory.Alloc((nuint)_charSize * sizeof(char));

        _byteSize = byteSize;
        _byteBuffer = (byte*)NativeMemory.Alloc((nuint)_byteSize);
    }

    /// <summary>
    /// 析构函数（终结器），作为 <see cref="Dispose"/> 未被调用时的安全兜底。
    /// 负责释放字符缓冲区和字节缓冲区所占用的非托管内存，防止内存泄漏。
    /// 依次执行：刷新字符缓冲区、刷新字节缓冲区（忽略刷新时的异常），
    /// 然后标记为已释放，最后释放字符和字节缓冲区的非托管内存。
    /// 推荐通过 <see cref="Dispose"/> 或 <c>using</c> 语句显式释放以确保数据完整性。
    /// </summary>
    ~StreamWriter()
    {
        try
        {
            if (!_disposed)
            {
                CharFlush();
                ByteFlush();
            }
        }
        catch { /* 释放时忽略异常 */ }
        finally
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
    }

    /// <summary>
    /// 释放当前实例所占用的资源，包括刷新所有缓冲区数据并释放非托管内存。
    /// 调用此方法后，写入器将不再可用，后续写入操作将抛出 <see cref="ObjectDisposedException"/>。
    /// 同时调用 <see cref="GC.SuppressFinalize"/> 以阻止析构函数的执行，避免重复释放。
    /// </summary>
    public void Dispose()
    {
        DisposeCore();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 释放核心逻辑，在持有 <see cref="_lock"/> 的情况下执行以确保线程安全。
    /// 依次执行：刷新字符缓冲区、刷新字节缓冲区（忽略刷新时的异常），
    /// 然后标记为已释放，最后释放字符和字节缓冲区的非托管内存。
    /// </summary>
    private void DisposeCore()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            try
            {
                CharFlush();
            }
            catch { /* 释放时忽略异常 */ }

            try
            {
                ByteFlush();
            }
            catch { /* 释放时忽略异常 */ }
            
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
    /// 创建一个绑定到标准输出（stdout）的 <see cref="StreamWriter"/> 实例。
    /// 使用 <see cref="ConsolePal.StdOutHandle"/> 作为底层输出句柄。
    /// </summary>
    /// <returns>绑定到 stdout 的新 <see cref="StreamWriter"/> 实例。</returns>
    public static StreamWriter CreateForOut() => new(ConsolePal.StdOutHandle);

    /// <summary>
    /// 创建一个绑定到标准错误（stderr）的 <see cref="StreamWriter"/> 实例。
    /// 使用 <see cref="ConsolePal.StdErrHandle"/> 作为底层输出句柄。
    /// </summary>
    /// <returns>绑定到 stderr 的新 <see cref="StreamWriter"/> 实例。</returns>
    public static StreamWriter CreateForError() => new(ConsolePal.StdErrHandle);

    /// <summary>
    /// 将 UTF-8 字节序列写入输出流，可选择在末尾追加换行符（<c>0x0A</c>）。
    /// <para>写入逻辑如下：</para>
    /// <list type="bullet">
    ///   <item><description>若 <see cref="_enableByteBuffer"/> 为 <c>true</c> 且数据量小于缓冲区，则先将数据放入缓冲区；</description></item>
    ///   <item><description>若数据量超过缓冲区大小，则先刷新缓冲区再直接写入，跳过缓冲；</description></item>
    ///   <item><description>若 <c>isLine</c> 为 <c>true</c>，换行符将与内容一起合并写入，
    ///     对于小数据使用栈分配，对于大数据使用非托管内存临时缓冲；</description></item>
    ///   <item><description>若开启了自动刷新，行写入后会立即刷新字节缓冲区。</description></item>
    /// </list>
    /// 若实例已被释放，则抛出 <see cref="ObjectDisposedException"/>。
    /// </summary>
    /// <param name="value">要写入的只读字节序列（应为合法的 UTF-8 编码数据）。</param>
    /// <param name="isLine">若为 <c>true</c>，则在写入内容后追加换行符；默认为 <c>false</c>。</param>
    /// <exception cref="ObjectDisposedException">当写入器已被释放时抛出。</exception>
    public override void Write(ReadOnlySpan<byte> value, bool isLine = false)
    {
        lock (_lock)
        {
            if (!_disposed)
            {
                void ProcessData(ReadOnlySpan<byte> data)
                {
                    if (data.IsEmpty) return;

                    if (_enableByteBuffer)
                    {
                        if (data.Length >= _byteSize)
                        {
                            ByteFlush();

                            ref var reference = ref MemoryMarshal.GetReference(data);
                            var ptr = (byte*)Unsafe.AsPointer(ref reference);
                            ConsolePal.Write(_outputHandle, ptr, data.Length);
                            return;
                        }

                        if (_bytePos + data.Length > _byteSize)
                        {
                            ByteFlush();
                        }

                        data.CopyTo(new Span<byte>(_byteBuffer + _bytePos, data.Length));
                        _bytePos += data.Length;
                    }
                    else
                    {
                        ByteFlush();

                        ref var reference = ref MemoryMarshal.GetReference(data);
                        var ptr = (byte*)Unsafe.AsPointer(ref reference);
                        ConsolePal.Write(_outputHandle, ptr, data.Length);
                    }
                }

                if (isLine)
                {
                    var lineLength = value.Length + 1;

                    if (lineLength <= _byteSize && lineLength <= 4096)
                    {
                        Span<byte> lineBuffer = stackalloc byte[lineLength];
                        value.CopyTo(lineBuffer);
                        lineBuffer[value.Length] = 0x0A;
                        ProcessData(lineBuffer);
                    }
                    else
                    {
                        var lineBuffer = (byte*)NativeMemory.Alloc((nuint)lineLength);
                        try
                        {
                            value.CopyTo(new Span<byte>(lineBuffer, value.Length));
                            lineBuffer[value.Length] = 0x0A;
                            var lineData = new ReadOnlySpan<byte>(lineBuffer, lineLength);
                            ProcessData(lineData);
                        }
                        finally
                        {
                            NativeMemory.Free(lineBuffer);
                        }
                    }

                    if (_enableAutoFlush)
                        ByteFlush();
                }
                else
                {
                    ProcessData(value);
                }
            }
            else
            {
                throw new ObjectDisposedException(nameof(StreamWriter));
            }
        }
    }

    /// <summary>
    /// 将 Unicode 字符序列（UTF-16）写入输出流，可选择追加换行符。
    /// 内部通过固定（<c>fixed</c>）字符序列的内存地址后，
    /// 委托给私有方法 <see cref="WriteChars"/> 完成实际的字符缓冲区写入和 UTF-16→UTF-8 编码转换。
    /// 若 <paramref name="value"/> 为空且 <paramref name="isLine"/> 为 <c>true</c>，则仅写入换行符。
    /// </summary>
    /// <param name="value">要写入的只读字符序列（UTF-16 编码）。</param>
    /// <param name="isLine">若为 <c>true</c>，则在写入内容后追加换行符；默认为 <c>false</c>。</param>
    public override void Write(ReadOnlySpan<char> value, bool isLine = false)
    {
        if (value.IsEmpty)
        {
            if (isLine)
                WriteLine();
            return;
        }

        lock (_lock)
        {
            fixed (char* chars = value)
            {
                WriteChars(chars, value.Length, isLine);
            }
        }
    }

    /// <summary>
    /// 将字符缓冲区和字节缓冲区中所有待写数据刷新到底层输出设备。
    /// 若实例已被释放，则直接返回，不抛出异常。
    /// 此方法线程安全，通过 <see cref="_lock"/> 保护。
    /// </summary>
    public override void Flush()
    {
        if (_disposed) return;

        lock (_lock)
        {
            CharFlush();
            ByteFlush();
        }
    }

    /// <summary>
    /// 重新设置字符缓冲区和字节缓冲区的大小。
    /// 执行步骤：
    /// <list type="number">
    ///   <item><description>验证参数有效性，大小必须大于 0；</description></item>
    ///   <item><description>刷新现有缓冲区中的所有数据；</description></item>
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
            CharFlush();
            ByteFlush();
            
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
    /// 启用或禁用字符级缓冲区（UTF-16 暂存区）。
    /// 若实例已被释放，则直接返回。
    /// 修改操作在 <see cref="_lock"/> 保护下执行，确保线程安全。
    /// </summary>
    /// <param name="enableCharBuffer">
    /// 若为 <c>true</c>，则启用字符缓冲；
    /// 若为 <c>false</c>，则禁用字符缓冲，字符数据将直接转换为 UTF-8 字节。
    /// </param>
    public override void EnableCharBuffer(bool enableCharBuffer)
    {
        if (_disposed)
            return;

        lock (_lock)
        {
            _enableCharBuffer = enableCharBuffer;
        }
    }

    /// <summary>
    /// 启用或禁用字节级缓冲区（UTF-8 暂存区）。
    /// 若实例已被释放，则直接返回。
    /// 修改操作在 <see cref="_lock"/> 保护下执行，确保线程安全。
    /// </summary>
    /// <param name="enableByteBuffer">
    /// 若为 <c>true</c>，则启用字节缓冲；
    /// 若为 <c>false</c>，则每次写入均直接发起系统调用。
    /// </param>
    public override void EnableByteBuffer(bool enableByteBuffer)
    {
        if (_disposed)
            return;

        lock (_lock)
        {
            _enableByteBuffer = enableByteBuffer;
        }
    }

    /// <summary>
    /// 启用或禁用自动刷新模式。
    /// 启用后，每次调用以 <c>isLine = true</c> 写入的方法完成后，
    /// 将自动触发字节缓冲区的刷新，确保行数据立即输出。
    /// 若实例已被释放，则直接返回。
    /// 修改操作在 <see cref="_lock"/> 保护下执行，确保线程安全。
    /// </summary>
    /// <param name="enableAutoFlush">
    /// 若为 <c>true</c>，则启用自动刷新；
    /// 若为 <c>false</c>，则禁用自动刷新。
    /// </param>
    public override void EnableAutoFlush(bool enableAutoFlush)
    {
        if (_disposed)
            return;

        lock (_lock)
        {
            _enableAutoFlush = enableAutoFlush;
        }
    }
}