// CoreLib/StreamWriter.CharBuffer.cs

using System;
using System.Buffers;
using System.Text.Unicode;

namespace CoreLib;

/// <summary>
/// <see cref="StreamWriter"/> 的字符缓冲区分部类，
/// 负责维护 UTF-16 字符缓冲区的字段，以及实现字符数据的暂存、
/// UTF-16 到 UTF-8 的编码转换、以及字符缓冲区的刷新逻辑。
/// </summary>
public unsafe partial class StreamWriter
{
    /// <summary>
    /// 指向非托管字符缓冲区的指针（UTF-16 编码），用于暂存待编码的字符数据。
    /// 由构造函数通过 <see cref="System.Runtime.InteropServices.NativeMemory.Alloc(nuint)"/> 分配，
    /// 由析构函数或 <see cref="DisposeCore"/> 释放。
    /// </summary>
    private char* _charBuffer = null!;

    /// <summary>字符缓冲区的总容量（以字符数为单位）。</summary>
    private int _charSize;

    /// <summary>字符缓冲区当前已使用的字符数（写入位置偏移）。</summary>
    private int _charPos;

    /// <summary>
    /// 将 UTF-16 字符序列逐段编码为 UTF-8，并将结果写入字节缓冲区（<c>_byteBuffer</c>）。
    /// 当字节缓冲区空间不足时，自动调用 <see cref="ByteFlush"/> 刷新到底层输出后继续编码。
    /// </summary>
    /// <remarks>
    /// 针对 <see cref="Utf8.FromUtf16"/> 的四种返回状态，处理策略如下：
    /// <list type="table">
    ///   <listheader><term>状态</term><description>处理方式</description></listheader>
    ///   <item>
    ///     <term><see cref="OperationStatus.Done"/></term>
    ///     <description>本轮编码正常完成，继续循环处理剩余字符。</description>
    ///   </item>
    ///   <item>
    ///     <term><see cref="OperationStatus.DestinationTooSmall"/></term>
    ///     <description>目标字节缓冲区剩余空间不足，已在进入 <c>switch</c> 前触发过 <see cref="ByteFlush"/>，直接 <c>continue</c> 重试。</description>
    ///   </item>
    ///   <item>
    ///     <term><see cref="OperationStatus.NeedMoreData"/></term>
    ///     <description>
    ///       遇到不完整的 UTF-16 代理对（孤立的高代理项），理论上不应在此场景中发生。
    ///       采用容错策略：向输出写入 Unicode 替换字符 <c>U+FFFD</c>（UTF-8 编码为 <c>0xEF 0xBF 0xBD</c>），
    ///       并跳过该问题字符（前进 1 个 <c>char</c>）后继续处理。
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <term><see cref="OperationStatus.InvalidData"/></term>
    ///     <description>
    ///       遇到无效的 UTF-16 数据（如孤立的低代理项）。
    ///       同样写入替换字符 <c>U+FFFD</c> 并跳过该问题字符，保证输出流的完整性。
    ///     </description>
    ///   </item>
    /// </list>
    /// </remarks>
    /// <param name="chars">指向待编码字符序列起始位置的非托管指针。</param>
    /// <param name="length">要编码的字符数。若为 0，则立即返回。</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// 当 <see cref="Utf8.FromUtf16"/> 返回未预期的枚举值时抛出。
    /// </exception>
    private void ConvertUtf16ToByteBuffer(char* chars, int length)
    {
        if (length == 0) return;

        var p = chars;
        var remaining = length;

        while (remaining > 0)
        {
            if (_bytePos >= _byteSize)
                ByteFlush();

            var status = Utf8.FromUtf16(
                new ReadOnlySpan<char>(p, remaining),
                new Span<byte>(_byteBuffer + _bytePos, _byteSize - _bytePos),
                out var charsRead,
                out var bytesWritten);

            _bytePos += bytesWritten;
            p += charsRead;
            remaining -= charsRead;

            switch (status)
            {
                case OperationStatus.Done:
                    break;
                case OperationStatus.DestinationTooSmall:
                    ByteFlush();
                    continue;
                case OperationStatus.NeedMoreData:
                    var replacementSpan = "\ufffd"u8;

                    if (_bytePos + replacementSpan.Length > _byteSize)
                        ByteFlush();
                    
                    replacementSpan.CopyTo(new Span<byte>(_byteBuffer + _bytePos, replacementSpan.Length));
                    _bytePos += replacementSpan.Length;

                    p += 1;
                    remaining -= 1;
                    break;
                case OperationStatus.InvalidData:
                    replacementSpan = "\ufffd"u8;
                    
                    if (_bytePos + replacementSpan.Length > _byteSize)
                        ByteFlush();
                    
                    replacementSpan.CopyTo(new Span<byte>(_byteBuffer + _bytePos, replacementSpan.Length));
                    _bytePos += replacementSpan.Length;

                    p += 1;
                    remaining -= 1;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(chars), status, $"未知状态：{status}");
            }
            break;
        }
    }

    /// <summary>
    /// 将字符数据写入字符缓冲区或直接进行 UTF-16→UTF-8 编码转换，
    /// 并可选地在末尾追加换行符。
    /// <para>
    /// 当字符缓冲区已启用（<see cref="_enableCharBuffer"/> 为 <c>true</c>）时：
    /// 字符数据以 UTF-16 形式暂存于字符缓冲区中，待缓冲区填满时才调用
    /// <see cref="CharFlush"/> 进行批量编码转换；
    /// 若 <paramref name="isLine"/> 为 <c>true</c>，则在数据写入后向缓冲区追加 <c>'\n'</c>，
    /// 若开启自动刷新则立即刷新。
    /// </para>
    /// <para>
    /// 当字符缓冲区已禁用时，直接调用 <see cref="WriteUtf16Core"/> 进行编码转换。
    /// </para>
    /// </summary>
    /// <param name="chars">指向待写入字符序列起始位置的非托管指针。</param>
    /// <param name="length">要写入的字符数。若为 0，则立即返回。</param>
    /// <param name="isLine">若为 <c>true</c>，则在写入内容后追加换行符 <c>'\n'</c>；默认为 <c>false</c>。</param>
    private void WriteChars(char* chars, int length, bool isLine = false)
    {
        if (length == 0) return;

        if (!_enableCharBuffer)
        {
            WriteUtf16Core(chars, length, isLine);
            return;
        }

        var p = chars;
        var remaining = length;

        while (remaining > 0)
        {
            var canCopy = Math.Min(remaining, _charSize - _charPos);

            new ReadOnlySpan<char>(p, canCopy)
                .CopyTo(new Span<char>(_charBuffer + _charPos, canCopy));

            _charPos += canCopy;
            p += canCopy;
            remaining -= canCopy;

            if (_charPos >= _charSize)
                CharFlush();
        }

        if (!isLine) return;

        if (_charPos >= _charSize)
            CharFlush();

        _charBuffer[_charPos++] = '\n';

        if (_charPos >= _charSize)
            CharFlush();
        
        if (_enableAutoFlush)
            CharFlush();
    }

    /// <summary>
    /// 在禁用字符缓冲区的情况下，直接将 UTF-16 字符序列编码为 UTF-8 并写入字节缓冲区，
    /// 并在需要时追加换行符（<c>0x0A</c>）。
    /// 若开启了自动刷新且 <paramref name="isLine"/> 为 <c>true</c>，则在追加换行符后
    /// 立即调用 <see cref="CharFlush"/> 触发字节缓冲区的刷新。
    /// </summary>
    /// <param name="chars">指向待编码字符序列起始位置的非托管指针。</param>
    /// <param name="length">要处理的字符数。</param>
    /// <param name="isLine">若为 <c>true</c>，则向字节缓冲区追加换行字节（<c>0x0A</c>）并视情况刷新。</param>
    private void WriteUtf16Core(char* chars, int length, bool isLine)
    {
        ConvertUtf16ToByteBuffer(chars, length);

        if (!isLine) return;

        if (_bytePos >= _byteSize)
            ByteFlush();

        _byteBuffer[_bytePos++] = 0x0A;

        if (_bytePos > 0)
            ByteFlush();
        
        if (_enableAutoFlush)
            CharFlush();
    }

    /// <summary>
    /// 将字符缓冲区（UTF-16）中的所有待处理字符转换为 UTF-8 字节并写入字节缓冲区，
    /// 然后刷新字节缓冲区到底层输出。
    /// <para>
    /// 执行条件：当 <see cref="_charPos"/> 大于 0（有待处理字符）且对象未被释放。
    /// 转换完成后 <see cref="_charPos"/> 归零，表示字符缓冲区已清空。
    /// 若转换后字节缓冲区有数据，则调用 <see cref="ByteFlush"/> 将其写出。
    /// </para>
    /// </summary>
    private void CharFlush()
    {
        if (_charPos == 0 || _disposed)
            return;

        ConvertUtf16ToByteBuffer(_charBuffer, _charPos);

        _charPos = 0;

        if (_bytePos > 0)
            ByteFlush();
    }
}