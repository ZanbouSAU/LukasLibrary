// CoreLib/TextReader.cs

using System;
using System.Text;

namespace CoreLib;

/// <summary>
/// 文本读取器的抽象基类，定义了从底层输入流读取文本数据的核心接口。
/// 提供了逐字符读取（<see cref="Read()"/>）、Span字符读取（<see cref="Read(Span{char})"/>）、
/// Span字节读取（<see cref="Read(Span{byte})"/>）和按行读取（<see cref="ReadLine"/>）两种方式。
/// 派生类只需实现 <see cref="Read()"/> 方法，即可自动获得完整的按行读取能力。
/// </summary>
public abstract class TextReader
{
    /// <summary>
    /// 重新设置内部字符缓冲区和字节缓冲区的大小。
    /// 调用此方法前应先刷新现有缓冲区，调用后将重新分配指定大小的内存。
    /// </summary>
    /// <param name="charSize">新的字符缓冲区大小（以字符数为单位），默认值为 512。必须大于 0。</param>
    /// <param name="byteSize">新的字节缓冲区大小（以字节数为单位），默认值为 4096。必须大于 0。</param>
    public abstract void SetBufferSize(int charSize = 512, int byteSize = 4096);
    
    /// <summary>
    /// 从输入流中读取下一个字符。
    /// 基类的默认实现始终返回 <c>-1</c>（表示流结束），派生类应重写此方法。
    /// </summary>
    /// <returns>
    /// 读取到的字符的 Unicode 码点（范围 0~65535）；
    /// 若已到达流的末尾，则返回 <c>-1</c>。
    /// </returns>
    public virtual int Read() => -1;
    
    /// <summary>
    /// 从当前读取器中读取字符到指定的字符跨度中。
    /// 这是基类的默认实现，派生类应重写以提供更高效的实现。
    /// </summary>
    /// 
    /// <param name="buffer">要将字符读取到的跨度。</param>
    /// <returns>写入到 <paramref name="buffer"/> 的字符数。如果已到达流末尾，则返回 0。</returns>
    public virtual int Read(Span<char> buffer)
    {
        if (buffer.IsEmpty)
            return 0;

        var totalRead = 0;

        for (var i = 0; i < buffer.Length; i++)
        {
            var ch = Read();
            if (ch == -1)
                break;

            buffer[i] = (char)ch;
            totalRead++;
        }

        return totalRead;
    }
    
    /// <summary>
    /// 从当前读取器中读取原始字节到指定的字节跨度中（基类默认不支持）。
    /// 默认实现直接返回 0，派生类可根据需要重写。
    /// </summary>
    /// 
    /// <param name="buffer">目标字节跨度。</param>
    /// <returns>实际读取的字节数。基类默认返回 0。</returns>
    public virtual int Read(Span<byte> buffer)
    {
        return 0;
    }

    /// <summary>
    /// 从输入流中读取一行文本。
    /// 行结束符可以是 <c>\n</c>（LF）或 <c>\r\n</c>（CRLF），两者均被正确处理。
    /// 返回的字符串不包含行结束符本身；若行末为 <c>\r\n</c>，则 <c>\r</c> 也会被去除。
    /// <para>
    /// 此方法通过反复调用 <see cref="Read()"/> 实现，使用 <see cref="StringBuilder"/>
    /// 拼接字符，直到遇到换行符或流结束为止。
    /// </para>
    /// </summary>
    /// <returns>
    /// 读取到的一行文本（不含行结束符）；
    /// 若在读取第一个字符前就已到达流末尾，则返回 <c>null</c>。
    /// </returns>
    public string? ReadLine()
    {
        var ch = Read();
        if (ch == -1)
            return null;

        var builder = new StringBuilder();
        do
        {
            if (ch == '\n')
            {
                if (builder.Length > 0 && builder[^1] == '\r')
                {
                    builder.Length--;
                }
                break;
            }
            
            builder.Append((char)ch);
            ch = Read();
        } while (ch != -1);

        if (builder.Length > 0 && builder[^1] == '\r')
        {
            builder.Length--;
        }
        
        return builder.ToString();
    }
}