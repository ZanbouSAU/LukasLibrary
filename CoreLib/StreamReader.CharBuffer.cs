// CoreLib/StreamReader.CharBuffer.cs

namespace CoreLib;

/// <summary>
/// <see cref="StreamReader"/> 的字符缓冲区分部类，
/// 定义了用于暂存已解码 UTF-16 字符数据的缓冲区字段。
/// </summary>
public unsafe partial class StreamReader
{
    /// <summary>
    /// 指向非托管字符缓冲区的指针（UTF-16 编码），用于暂存从字节缓冲区解码得到的字符数据。
    /// 由构造函数通过 <see cref="System.Runtime.InteropServices.NativeMemory.Alloc(nuint)"/> 分配，由析构函数或 <see cref="DisposeCore"/> 释放。
    /// </summary>
    private char* _charBuffer = null!;

    /// <summary>字符缓冲区的总容量（以字符数为单位），在构造时确定，运行期间不变。</summary>
    private int _charSize;

    /// <summary>
    /// 字符缓冲区的当前读取位置偏移（从 0 开始）。
    /// 每次调用 <see cref="Read()"/> 后递增，在 <see cref="FillCharBuffer"/> 时重置为 0。
    /// </summary>
    private int _charPos;

    /// <summary>
    /// 字符缓冲区中当前有效字符的数量（上限）。
    /// 由 <see cref="FillCharBuffer"/> 在解码后设置，
    /// 当 <see cref="_charPos"/> 达到此值时，需要重新填充缓冲区。
    /// </summary>
    private int _charLen;
}
