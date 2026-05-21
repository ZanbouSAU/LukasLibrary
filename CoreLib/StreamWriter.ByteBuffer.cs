// CoreLib/StreamWriter.ByteBuffer.cs

using System;
using System.IO;

namespace CoreLib;

/// <summary>
/// <see cref="StreamWriter"/> 的字节缓冲区分部类，
/// 负责维护 UTF-8 字节缓冲区的字段，以及实现将字节缓冲区数据
/// 通过 <see cref="ConsolePal.Write"/> 写入底层系统输出句柄的逻辑。
/// </summary>
public unsafe partial class StreamWriter
{
    /// <summary>
    /// 指向非托管字节缓冲区的指针（UTF-8 编码），用于暂存待写入底层输出的字节数据。
    /// 由构造函数分配，由析构函数或 <see cref="DisposeCore"/> 释放。
    /// </summary>
    private byte* _byteBuffer = null;

    /// <summary>字节缓冲区的总容量（以字节数为单位）。</summary>
    private int _byteSize;

    /// <summary>字节缓冲区当前已使用的字节数（写入位置偏移）。</summary>
    private int _bytePos;

    /// <summary>
    /// 将字节缓冲区（<see cref="_byteBuffer"/>）中从偏移 0 到 <see cref="_bytePos"/> 的所有字节，
    /// 通过 <see cref="ConsolePal.Write"/> 写入底层输出句柄。
    /// <para>
    /// 若 <see cref="_bytePos"/> 为 0，则直接返回，不发起系统调用。
    /// 若系统调用返回 0 或负值（写入失败），则抛出 <see cref="IOException"/>。
    /// 若仅部分数据被写出（<c>written &lt; _bytePos</c>），则将未写出的数据移动到缓冲区头部，
    /// 更新 <see cref="_bytePos"/> 以保留未写出的部分；
    /// 若全部数据都写出，则将 <see cref="_bytePos"/> 归零。
    /// </para>
    /// </summary>
    /// <exception cref="IOException">当底层写入操作失败（返回值 ≤ 0）时抛出。</exception>
    private void ByteFlush()
    {
        if (_bytePos == 0)
            return;
        
        var written = ConsolePal.Write(_outputHandle, _byteBuffer, _bytePos);

        if (written <= 0)
        {
            throw new IOException("Failed to write to console (stdout/stderr).");
        }

        if (written < _bytePos)
        {
            var remaining = _bytePos - written;
            
            new Span<byte>(_byteBuffer + written, remaining)
                .CopyTo(new Span<byte>(_byteBuffer, remaining));
            
            _bytePos = remaining;
        }
        else
        {
            _bytePos = 0;
        }
    }
}