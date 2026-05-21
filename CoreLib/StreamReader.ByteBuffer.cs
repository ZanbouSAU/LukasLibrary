// CoreLib/StreamReader.ByteBuffer.cs

namespace CoreLib;

/// <summary>
/// <see cref="StreamReader"/> 的字节缓冲区分部类，
/// 定义了用于暂存从底层输入句柄读取的原始 UTF-8 字节数据的缓冲区字段。
/// </summary>
public unsafe partial class StreamReader
{
    /// <summary>
    /// 指向非托管字节缓冲区的指针，用于暂存从底层输入读取的原始字节数据（UTF-8 编码）。
    /// 由构造函数分配，由析构函数或 <see cref="DisposeCore"/> 释放。
    /// </summary>
    private byte* _byteBuffer = null;

    /// <summary>字节缓冲区的总容量（以字节数为单位），在构造时确定，运行期间不变。</summary>
    private int _byteSize;

    /// <summary>
    /// 字节缓冲区的当前解码起始位置偏移。
    /// 指向下一次解码操作应从何处开始读取字节。
    /// 在 <see cref="FillByteBuffer"/> 滚动操作后重置为 0。
    /// </summary>
    private int _bytePos;

    /// <summary>
    /// 字节缓冲区中当前有效字节的总数（从偏移 0 开始计）。
    /// 在 <see cref="FillByteBuffer"/> 成功读取后递增。
    /// 当 <see cref="_bytePos"/> 达到此值时，表示所有已读字节均已被解码，需要再次填充。
    /// </summary>
    private int _byteLen;
}
