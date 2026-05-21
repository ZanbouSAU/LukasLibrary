// CoreLib/TextWriter.cs

using System;
using System.Buffers;

namespace CoreLib;

/// <summary>
/// 文本写入器的抽象基类，定义了将文本数据写入底层输出流所需的核心接口。
/// 子类需实现底层的字节写入 (<see cref="Write(ReadOnlySpan{byte}, bool)"/>) 和
/// 字符写入 (<see cref="Write(ReadOnlySpan{char}, bool)"/>) 方法，
/// 以及缓冲区控制相关的抽象方法。
/// 基类在此之上提供了针对各种常见数据类型的具体写入方法，
/// 并实现了高性能的泛型格式化写入，优先使用栈分配或数组池以减少堆内存分配。
/// </summary>
public abstract class TextWriter
{
    /// <summary>
    /// 将所有内部缓冲区的数据刷新到底层输出设备。
    /// 派生类必须实现此方法，确保字符缓冲区和字节缓冲区中的所有待写数据都被写出。
    /// </summary>
    public abstract void Flush();

    /// <summary>
    /// 重新设置内部字符缓冲区和字节缓冲区的大小。
    /// 调用此方法前应先刷新现有缓冲区，调用后将重新分配指定大小的内存。
    /// </summary>
    /// <param name="charSize">新的字符缓冲区大小（以字符数为单位），默认值为 512。必须大于 0。</param>
    /// <param name="byteSize">新的字节缓冲区大小（以字节数为单位），默认值为 4096。必须大于 0。</param>
    public abstract void SetBufferSize(int charSize = 512, int byteSize = 4096);

    /// <summary>
    /// 启用或禁用字符级缓冲区。
    /// 字符缓冲区用于在编码转换前暂存 UTF-16 字符数据；
    /// 禁用后，字符数据将直接被转换并写入字节缓冲区或底层输出。
    /// </summary>
    /// <param name="enableCharBuffer">
    /// 若为 <c>true</c>，则启用字符缓冲；
    /// 若为 <c>false</c>，则禁用字符缓冲。
    /// </param>
    public abstract void EnableCharBuffer(bool enableCharBuffer);

    /// <summary>
    /// 启用或禁用字节级缓冲区。
    /// 字节缓冲区用于在系统调用前暂存已编码的 UTF-8 字节数据，以减少系统调用次数；
    /// 禁用后，每次写入都会直接调用底层 I/O 接口。
    /// </summary>
    /// <param name="enableByteBuffer">
    /// 若为 <c>true</c>，则启用字节缓冲；
    /// 若为 <c>false</c>，则禁用字节缓冲。
    /// </param>
    public abstract void EnableByteBuffer(bool enableByteBuffer);

    /// <summary>
    /// 启用或禁用自动刷新模式。
    /// 启用后，每次写入行结束符时，缓冲区将自动被刷新至底层输出，
    /// 适合需要实时查看输出的交互式场景。
    /// </summary>
    /// <param name="enableAutoFlush">
    /// 若为 <c>true</c>，则启用自动刷新；
    /// 若为 <c>false</c>，则禁用自动刷新。
    /// </param>
    public abstract void EnableAutoFlush(bool enableAutoFlush);

    /// <summary>
    /// 将 UTF-8 字节序列写入底层输出流，可选择追加换行符。
    /// 这是所有字节写入操作的核心抽象方法，派生类须实现对字节缓冲区和底层 I/O 的操作逻辑。
    /// </summary>
    /// <param name="value">要写入的只读字节序列（应为合法的 UTF-8 编码数据）。</param>
    /// <param name="isLine">若为 <c>true</c>，则在写入内容后追加换行符（<c>0x0A</c>）；默认为 <c>false</c>。</param>
    public abstract void Write(ReadOnlySpan<byte> value, bool isLine = false);

    /// <summary>
    /// 将 Unicode 字符序列（UTF-16）写入底层输出流，可选择追加换行符。
    /// 实现时需将 UTF-16 字符编码转换为 UTF-8 字节后再写入。
    /// 这是所有字符写入操作的核心抽象方法。
    /// </summary>
    /// <param name="value">要写入的只读字符序列（UTF-16 编码）。</param>
    /// <param name="isLine">若为 <c>true</c>，则在写入内容后追加换行符；默认为 <c>false</c>。</param>
    public abstract void Write(ReadOnlySpan<char> value, bool isLine = false);

    /// <summary>
    /// 将实现了 <see cref="IUtf8SpanFormattable"/> 接口的值高效写入输出流。
    /// 采用三级内存策略以最小化堆内存分配：
    /// <list type="number">
    ///   <item><description>优先尝试 256 字节的栈分配缓冲区（<c>stackalloc</c>）；</description></item>
    ///   <item><description>若不足，尝试 2048 字节的栈分配缓冲区；</description></item>
    ///   <item><description>若仍不足，从 <see cref="ArrayPool{T}"/> 租用 4096 字节的数组；</description></item>
    ///   <item><description>最终回退：调用 <see cref="object.ToString"/> 方法并以字符串形式写入。</description></item>
    /// </list>
    /// </summary>
    /// <typeparam name="T">要写入的值的类型，必须实现 <see cref="IUtf8SpanFormattable"/>。</typeparam>
    /// <param name="value">要写入的值。</param>
    /// <param name="isLine">若为 <c>true</c>，则在写入内容后追加换行符；默认为 <c>false</c>。</param>
    public void Write<T>(T value, bool isLine = false) where T : IUtf8SpanFormattable
    {
        Span<byte> bytes = stackalloc byte[256];
        if (value.TryFormat(bytes, out var written, default, null))
        {
            Write(bytes[..written], isLine);
            return;
        }
        
        const int maxStackAlloc = 2048;
        if (maxStackAlloc > 256)
        {
            Span<byte> largerStackBuffer = stackalloc byte[maxStackAlloc];
            if (value.TryFormat(largerStackBuffer, out written, default, null))
            {
                Write(largerStackBuffer[..written], isLine);
                return;
            }
        }
        
        byte[]? rentedBuffer = null;
        try
        {
            const int defaultRentSize = 4096;
            rentedBuffer = ArrayPool<byte>.Shared.Rent(defaultRentSize);
            var pooledSpan = rentedBuffer.AsSpan(0, defaultRentSize);
        
            if (value.TryFormat(pooledSpan, out written, default, null))
            {
                Write(pooledSpan[..written], isLine);
                return;
            }
            
            Write(value.ToString(), isLine);
        }
        finally
        {
            if (rentedBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }
    }

    /// <summary>
    /// 将任意对象写入输出流，可选择追加换行符。
    /// 内部通过调用 <see cref="object.ToString"/> 获取字符串表示后再写入。
    /// 若 <paramref name="value"/> 为 <c>null</c>，则不写入任何内容。
    /// </summary>
    /// <param name="value">要写入的对象，可以为 <c>null</c>。</param>
    /// <param name="isLine">若为 <c>true</c>，则在写入内容后追加换行符；默认为 <c>false</c>。</param>
    public void Write(object? value, bool isLine = false)
    {
        if (value is null)
            return;
        Write(value.ToString(), isLine);
    }

    /// <summary>
    /// 将字符串写入输出流，可选择追加换行符。
    /// 内部将字符串转为 <see cref="ReadOnlySpan{T}"/> 后调用字符写入抽象方法。
    /// 若 <paramref name="value"/> 为 <c>null</c>，则不写入任何内容。
    /// </summary>
    /// <param name="value">要写入的字符串，可以为 <c>null</c>。</param>
    /// <param name="isLine">若为 <c>true</c>，则在写入内容后追加换行符；默认为 <c>false</c>。</param>
    public void Write(string? value, bool isLine = false)
    {
        if (value is null)
            return;
        Write(value.AsSpan(), isLine);
    }

    /// <summary>
    /// 将字符数组写入输出流，可选择追加换行符。
    /// 内部将数组转为 <see cref="ReadOnlySpan{T}"/> 后调用字符写入抽象方法。
    /// </summary>
    /// <param name="value">要写入的字符数组。</param>
    /// <param name="isLine">若为 <c>true</c>，则在写入内容后追加换行符；默认为 <c>false</c>。</param>
    public void Write(char[] value, bool isLine = false) => Write(value.AsSpan(), isLine);

    /// <summary>
    /// 将 UTF-8 字节序列写入输出流，并追加换行符。
    /// 等同于以 <c>isLine = true</c> 调用 <see cref="Write(ReadOnlySpan{byte}, bool)"/>。
    /// </summary>
    /// <param name="value">要写入的只读字节序列（UTF-8 编码）。</param>
    public void WriteLine(ReadOnlySpan<byte> value) => Write(value, true);

    /// <summary>
    /// 将 Unicode 字符序列写入输出流，并追加换行符。
    /// 等同于以 <c>isLine = true</c> 调用 <see cref="Write(ReadOnlySpan{char}, bool)"/>。
    /// </summary>
    /// <param name="value">要写入的只读字符序列（UTF-16 编码）。</param>
    public void WriteLine(ReadOnlySpan<char> value) => Write(value, true);

    /// <summary>
    /// 将实现了 <see cref="IUtf8SpanFormattable"/> 接口的值写入输出流，并追加换行符。
    /// </summary>
    /// <typeparam name="T">要写入的值的类型，必须实现 <see cref="IUtf8SpanFormattable"/>。</typeparam>
    /// <param name="value">要写入的值。</param>
    public void WriteLine<T>(T value) where T : IUtf8SpanFormattable => Write(value, true);

    /// <summary>
    /// 将任意对象写入输出流，并追加换行符。
    /// 若 <paramref name="value"/> 为 <c>null</c>，则仅写入换行符（等同于调用无参 <see cref="WriteLine()"/>）。
    /// </summary>
    /// <param name="value">要写入的对象，可以为 <c>null</c>。</param>
    public void WriteLine(object? value)
    {
        if (value is null)
        {
            WriteLine();
            return;
        }
        Write(value.ToString(), true);
    }

    /// <summary>
    /// 将字符串写入输出流，并追加换行符。
    /// 若 <paramref name="value"/> 为 <c>null</c>，则仅写入换行符（等同于调用无参 <see cref="WriteLine()"/>）。
    /// </summary>
    /// <param name="value">要写入的字符串，可以为 <c>null</c>。</param>
    public void WriteLine(string? value)
    {
        if (value is null)
        {
            WriteLine();
            return;
        }
        Write(value.AsSpan(), true);
    }

    /// <summary>
    /// 将字符数组写入输出流，并追加换行符。
    /// </summary>
    /// <param name="value">要写入的字符数组。</param>
    public void WriteLine(char[] value) => Write(value, true);

    /// <summary>
    /// 向输出流写入一个空行，即仅写入换行符（<c>0x0A</c>）。
    /// 内部通过传入空的 <see cref="ReadOnlySpan{Byte}"/> 并设置 <c>isLine = true</c> 实现。
    /// </summary>
    public void WriteLine()
    {
        Write(ReadOnlySpan<byte>.Empty, isLine: true);
    }
}
