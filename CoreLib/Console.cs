// CoreLib/Console.cs

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CoreLib;

/// <summary>
/// 提供标准输入、标准输出和标准错误流的访问入口，
/// 是对底层 <see cref="StreamReader"/> 和 <see cref="StreamWriter"/> 的高层封装。
/// 所有属性和方法均为线程安全，采用双重检查锁定（Double-Check Locking）模式
/// 实现流的懒加载初始化。
/// </summary>
public static class Console
{
    /// <summary>标准输入流，延迟初始化，访问时由 <see cref="InLock"/> 保护。</summary>
    private static TextReader? _in;

    /// <summary>标准输出流，延迟初始化，访问时由 <see cref="OutLock"/> 保护。</summary>
    private static TextWriter? _out;

    /// <summary>标准错误流，延迟初始化，访问时由 <see cref="ErrorLock"/> 保护。</summary>
    private static TextWriter? _error;

    /// <summary>用于保护 <see cref="_in"/> 字段的互斥锁。</summary>
    private static readonly Lock InLock = new();

    /// <summary>用于保护 <see cref="_out"/> 字段的互斥锁。</summary>
    private static readonly Lock OutLock = new();

    /// <summary>用于保护 <see cref="_error"/> 字段的互斥锁。</summary>
    private static readonly Lock ErrorLock = new();

    /// <summary>
    /// 静态构造函数，在类型首次被使用时自动执行。
    /// 向当前应用程序域的 <see cref="AppDomain.ProcessExit"/> 事件注册处理程序，
    /// 确保进程退出前将所有缓冲区的数据刷新到底层输出流，防止数据丢失。
    /// 刷新过程中产生的任何异常均被静默忽略，以避免影响正常的进程退出流程。
    /// </summary>
    static Console()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            var outWriter = _out;
            var errorWriter = _error;
        
            if (outWriter != null)
            {
                try
                {
                    outWriter.Flush();
                }
                catch { /* 进程退出时忽略异常 */ }
            }
        
            if (errorWriter != null)
            {
                try
                {
                    errorWriter.Flush();
                }
                catch { /* 进程退出时忽略异常 */ }
            }
        };
    }

    /// <summary>
    /// 获取标准输入流（stdin）。
    /// 首次访问时使用双重检查锁定模式进行懒加载，通过
    /// <see cref="StreamReader.CreateForIn"/> 创建底层读取器。
    /// 后续访问通过 <see cref="Volatile.Read{T}"/> 以最低开销读取已初始化的实例。
    /// </summary>
    /// <value>当前关联的标准输入 <see cref="TextReader"/> 实例。</value>
    public static TextReader In
    {
        get
        {
            // ReSharper disable once InconsistentlySynchronizedField
            var result = Volatile.Read(ref _in);
            if (result != null)
                return result;
            lock (InLock)
            {
                result = _in;
                if (result != null)
                    return result;
                var reader = StreamReader.CreateForIn();
                Volatile.Write(ref _in, reader);
                return reader;
            }
        }
    }

    /// <summary>
    /// 获取标准输出流（stdout）。
    /// 首次访问时使用双重检查锁定模式进行懒加载，通过
    /// <see cref="StreamWriter.CreateForOut"/> 创建底层写入器。
    /// 后续访问通过 <see cref="Volatile.Read{T}"/> 以最低开销读取已初始化的实例。
    /// </summary>
    /// <value>当前关联的标准输出 <see cref="TextWriter"/> 实例。</value>
    public static TextWriter Out
    {
        get
        {
            // ReSharper disable once InconsistentlySynchronizedField
            var result = Volatile.Read(ref _out);
            if (result != null)
                return result;
            lock (OutLock)
            {
                result = _out;
                if (result != null)
                    return result;
                var writer = StreamWriter.CreateForOut();
                Volatile.Write(ref _out, writer);
                return writer;
            }
        }
    }

    /// <summary>
    /// 获取标准错误流（stderr）。
    /// 首次访问时使用双重检查锁定模式进行懒加载，通过
    /// <see cref="StreamWriter.CreateForError"/> 创建底层写入器。
    /// 后续访问通过 <see cref="Volatile.Read{T}"/> 以最低开销读取已初始化的实例。
    /// </summary>
    /// <value>当前关联的标准错误 <see cref="TextWriter"/> 实例。</value>
    public static TextWriter Error
    {
        get
        {
            // ReSharper disable once InconsistentlySynchronizedField
            var result = Volatile.Read(ref _error);
            if (result != null)
                return result;
            lock (ErrorLock)
            {
                result = _error;
                if (result != null)
                    return result;
                var writer = StreamWriter.CreateForError();
                Volatile.Write(ref _error, writer);
                return writer;
            }
        }
    }

    /// <summary>
    /// 将标准输入流替换为指定的 <see cref="TextReader"/>。
    /// 在持有 <see cref="InLock"/> 的情况下原子地完成替换，
    /// 并尝试释放旧的读取器（若其实现了 <see cref="IDisposable"/>）。
    /// 释放旧读取器时产生的异常会被静默忽略。
    /// </summary>
    /// <param name="newIn">用于替换标准输入的新 <see cref="TextReader"/> 实例，不能为 <c>null</c>。</param>
    public static void SetIn(TextReader newIn)
    {
        lock (InLock)
        {
            var old = _in;
            _in = newIn;
            try
            {
                (old as IDisposable)?.Dispose();
            }
            catch { /* 释放时忽略异常 */ }
        }
    }

    /// <summary>
    /// 将标准输出流替换为指定的 <see cref="TextWriter"/>。
    /// 在持有 <see cref="OutLock"/> 的情况下原子地完成替换，
    /// 并尝试释放旧地写入器（若其实现了 <see cref="IDisposable"/>）。
    /// 释放旧写入器时产生的异常会被静默忽略。
    /// </summary>
    /// <param name="newOut">用于替换标准输出的新 <see cref="TextWriter"/> 实例，不能为 <c>null</c>。</param>
    public static void SetOut(TextWriter newOut)
    {
        lock (OutLock)
        {
            var old = _out;
            _out = newOut;
            try
            {
                (old as IDisposable)?.Dispose();
            }
            catch { /* 释放时忽略异常 */ }
        }
    }

    /// <summary>
    /// 将标准错误流替换为指定的 <see cref="TextWriter"/>。
    /// 在持有 <see cref="ErrorLock"/> 的情况下原子地完成替换，
    /// 并尝试释放旧地写入器（若其实现了 <see cref="IDisposable"/>）。
    /// 释放旧写入器时产生的异常会被静默忽略。
    /// </summary>
    /// <param name="newError">用于替换标准错误的新 <see cref="TextWriter"/> 实例，不能为 <c>null</c>。</param>
    public static void SetError(TextWriter newError)
    {
        lock (ErrorLock)
        {
            var old = _error;
            _error = newError;
            try
            {
                (old as IDisposable)?.Dispose();
            }
            catch { /* 释放时忽略异常 */ }
        }
    }

    /// <summary>
    /// 将标准输出和标准错误流的所有内部缓冲区数据刷新到底层输出设备。
    /// 当两者指向同一个对象时，只执行一次刷新以避免重复操作。
    /// 标注 <see cref="MethodImplOptions.NoInlining"/> 以确保此方法不会被 JIT 内联，
    /// 从而保证调用栈的可观察性。
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void FlushBuffer()
    {
        var outWriter = Out;
        var errorWriter = Error;

        if (ReferenceEquals(outWriter, errorWriter))
        {
            outWriter.Flush();
        }
        else
        {
            outWriter.Flush();
            errorWriter.Flush();
        }
    }
    
    /// <summary>
    /// 同时设置标准输入流的字符缓冲区与字节缓冲区大小。
    /// 当两者指向同一个对象时，只执行一次设置以避免重复操作。
    /// 调用此方法会触发现有缓冲区的刷新，并重新分配指定大小的新缓冲区。
    /// </summary>
    /// <param name="charSize">字符缓冲区的容量（以字符数为单位），默认值为 512。必须大于 0。</param>
    /// <param name="byteSize">字节缓冲区的容量（以字节数为单位），默认值为 4096。必须大于 0。</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void SetInBufferSize(int charSize = 512, int byteSize = 4096)
    {
        var inReader = In;
        
        inReader.SetBufferSize(charSize, byteSize);
    }

    /// <summary>
    /// 同时设置标准输出和标准错误流的字符缓冲区与字节缓冲区大小。
    /// 当两者指向同一个对象时，只执行一次设置以避免重复操作。
    /// 调用此方法会触发现有缓冲区的刷新，并重新分配指定大小的新缓冲区。
    /// </summary>
    /// <param name="charSize">字符缓冲区的容量（以字符数为单位），默认值为 512。必须大于 0。</param>
    /// <param name="byteSize">字节缓冲区的容量（以字节数为单位），默认值为 4096。必须大于 0。</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void SetOutBufferSize(int charSize = 512, int byteSize = 4096)
    {
        var outWriter = Out;
        var errorWriter = Error;

        if (ReferenceEquals(outWriter, errorWriter))
        {
            outWriter.SetBufferSize(charSize, byteSize);
        }
        else
        {
            outWriter.SetBufferSize(charSize, byteSize);
            errorWriter.SetBufferSize(charSize, byteSize);
        }
    }

    /// <summary>
    /// 启用或禁用标准输出和标准错误流的字符级缓冲区。
    /// 禁用字符缓冲后，写入的字符数据将跳过字符缓冲区，直接进行 UTF-16 到 UTF-8 的转换并写入字节缓冲区。
    /// 当两者指向同一个对象时，只执行一次设置。
    /// </summary>
    /// <param name="enableCharBuffer">
    /// 若为 <c>true</c>，则启用字符缓冲区；
    /// 若为 <c>false</c>，则禁用字符缓冲区，数据将直接转换为字节。
    /// </param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void EnableCharBuffer(bool enableCharBuffer)
    {
        var outWriter = Out;
        var errorWriter = Error;

        if (ReferenceEquals(outWriter, errorWriter))
        {
            outWriter.EnableCharBuffer(enableCharBuffer);
        }
        else
        {
            outWriter.EnableCharBuffer(enableCharBuffer);
            errorWriter.EnableCharBuffer(enableCharBuffer);
        }
    }

    /// <summary>
    /// 启用或禁用标准输出和标准错误流的字节级缓冲区。
    /// 禁用字节缓冲后，每次写入的字节数据将不经缓冲，直接调用底层系统调用写入输出句柄。
    /// 当两者指向同一个对象时，只执行一次设置。
    /// </summary>
    /// <param name="enableByteBuffer">
    /// 若为 <c>true</c>，则启用字节缓冲区；
    /// 若为 <c>false</c>，则禁用字节缓冲区，每次写入均直接落盘。
    /// </param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void EnableByteBuffer(bool enableByteBuffer)
    {
        var outWriter = Out;
        var errorWriter = Error;

        if (ReferenceEquals(outWriter, errorWriter))
        {
            outWriter.EnableByteBuffer(enableByteBuffer);
        }
        else
        {
            outWriter.EnableByteBuffer(enableByteBuffer);
            errorWriter.EnableByteBuffer(enableByteBuffer);
        }
    }

    /// <summary>
    /// 启用或禁用标准输出和标准错误流的自动刷新模式。
    /// 启用后，每次写入行结束符（换行）时，缓冲区将自动刷新到底层设备，
    /// 适用于对实时性有要求的交互式场景。
    /// 当两者指向同一个对象时，只执行一次设置。
    /// </summary>
    /// <param name="enableAutoFlush">
    /// 若为 <c>true</c>，则在每次写入行尾时自动刷新；
    /// 若为 <c>false</c>，则仅在显式调用 <see cref="FlushBuffer"/> 或缓冲区满时刷新。
    /// </param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void EnableAutoFlush(bool enableAutoFlush)
    {
        var outWriter = Out;
        var errorWriter = Error;

        if (ReferenceEquals(outWriter, errorWriter))
        {
            outWriter.EnableAutoFlush(enableAutoFlush);
        }
        else
        {
            outWriter.EnableAutoFlush(enableAutoFlush);
            errorWriter.EnableAutoFlush(enableAutoFlush);
        }
    }

    /// <summary>
    /// 从标准输入流中读取下一个字符。
    /// </summary>
    /// <returns>
    /// 以整数形式返回读取到的字符的 Unicode 码点；
    /// 若已到达流的末尾，则返回 <c>-1</c>。
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Read() => In.Read();

    /// **Summary:**

    /// 从标准输入流中读取字符到指定的 <see cref="Span{char}"/> 缓冲区中。
    /// <para>
    /// 内部通过调用 <c>In.Read(buffer)</c> 实现。
    /// </para>
    /// <para>
    /// 使用 <see cref="MethodImplOptions.NoInlining"/> 标记，防止 JIT 将此方法内联，
    /// 以确保在某些特殊场景下（如重定向输入、单元测试替换 <see cref="In"/>）行为的一致性。
    /// </para>
    /// 
    /// <param name="buffer">目标字符跨度，用于接收从标准输入读取的字符。</param>
    /// <returns>
    /// 实际成功读取并写入到 <paramref name="buffer"/> 的字符数量。
    /// 若已到达输入流末尾，则返回 <c>0</c>。
    /// </returns>
    /// <remarks>
    /// 此方法是 .NET 中推荐的高性能输入方式，配合 <c>stackalloc</c> 可实现零分配读取。
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Read(Span<char> buffer) => In.Read(buffer);


    /// **Summary:**

    /// 从标准输入流中读取原始字节到指定的 <see cref="Span{byte}"/> 缓冲区中。
    /// <para>
    /// 内部直接调用 <c>In.Read(buffer)</c> 实现字节级读取（绕过字符解码）。
    /// </para>
    /// <para>
    /// 使用 <see cref="MethodImplOptions.NoInlining"/> 标记，防止 JIT 内联，
    /// 保证在输入重定向或替换 <see cref="In"/> 时的行为一致性。
    /// </para>
    /// 
    /// <param name="buffer">目标字节跨度，用于接收从标准输入读取的原始字节。</param>
    /// <returns>
    /// 实际成功读取的字节数量。
    /// 若已到达输入流末尾，则返回 <c>0</c>。
    /// </returns>
    /// <remarks>
    /// <b>注意：</b>此方法直接读取底层字节，通常与字符读取（<see cref="Read()"/>、<see cref="ReadLine()"/>）混用时需要谨慎，
    /// 可能导致解码器状态不一致。建议在需要原始字节数据时单独使用。
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Read(Span<byte> buffer) => In.Read(buffer);

    /// <summary>
    /// 从标准输入流中读取一行文本（以 <c>\n</c> 或 <c>\r\n</c> 为行结束符）。
    /// 返回的字符串不包含行结束符本身。
    /// </summary>
    /// <returns>
    /// 读取到的文本行字符串；
    /// 若已到达流的末尾且没有可读内容，则返回 <c>null</c>。
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string? ReadLine() => In.ReadLine();

    /// <summary>
    /// 将实现了 <see cref="IUtf8SpanFormattable"/> 接口的值写入标准输出流。
    /// 内部通过零拷贝的栈上缓冲区或 <see cref="System.Buffers.ArrayPool{T}"/> 租用缓冲区
    /// 将值直接格式化为 UTF-8 字节序列，避免产生中间字符串分配。
    /// </summary>
    /// <typeparam name="T">要写入的值的类型，必须实现 <see cref="IUtf8SpanFormattable"/>。</typeparam>
    /// <param name="value">要写入的值。</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Write<T>(T value) where T : IUtf8SpanFormattable => Out.Write(value);

    /// <summary>
    /// 将任意对象写入标准输出流。
    /// 内部通过调用对象的 <see cref="object.ToString"/> 方法获取字符串表示后再写入。
    /// 若 <paramref name="value"/> 为 <c>null</c>，则不写入任何内容。
    /// </summary>
    /// <param name="value">要写入的对象，可以为 <c>null</c>。</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Write(object? value) => Out.Write(value);

    /// <summary>
    /// 将字符串写入标准输出流。
    /// 若 <paramref name="value"/> 为 <c>null</c>，则不写入任何内容。
    /// </summary>
    /// <param name="value">要写入的字符串，可以为 <c>null</c>。</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Write(string? value) => Out.Write(value);

    /// <summary>
    /// 将字符数组写入标准输出流。
    /// </summary>
    /// <param name="value">要写入的字符数组。</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Write(char[] value) => Out.Write(value);

    /// <summary>
    /// 将 UTF-8 字节序列写入标准输出流，可选择在末尾追加换行符。
    /// </summary>
    /// <param name="value">要写入的只读字节序列（UTF-8 编码）。</param>
    /// <param name="isLine">若为 <c>true</c>，则在写入内容后追加换行符 <c>\n</c>（0x0A）；默认为 <c>false</c>。</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Write(ReadOnlySpan<byte> value, bool isLine = false) => Out.Write(value, isLine);

    /// <summary>
    /// 将 Unicode 字符序列写入标准输出流，可选择在末尾追加换行符。
    /// 写入时会将字符数据从 UTF-16 编码转换为 UTF-8 编码。
    /// </summary>
    /// <param name="value">要写入的只读字符序列（UTF-16 编码）。</param>
    /// <param name="isLine">若为 <c>true</c>，则在写入内容后追加换行符；默认为 <c>false</c>。</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Write(ReadOnlySpan<char> value, bool isLine = false) => Out.Write(value, isLine);

    /// <summary>
    /// 将实现了 <see cref="IUtf8SpanFormattable"/> 接口的值写入标准输出流，并追加换行符。
    /// </summary>
    /// <typeparam name="T">要写入的值的类型，必须实现 <see cref="IUtf8SpanFormattable"/>。</typeparam>
    /// <param name="value">要写入的值。</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void WriteLine<T>(T value) where T : IUtf8SpanFormattable => Out.WriteLine(value);

    /// <summary>
    /// 将任意对象的字符串表示写入标准输出流，并追加换行符。
    /// 若 <paramref name="value"/> 为 <c>null</c>，则仅写入换行符。
    /// </summary>
    /// <param name="value">要写入的对象，可以为 <c>null</c>。</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void WriteLine(object? value) => Out.WriteLine(value);

    /// <summary>
    /// 将字符串写入标准输出流，并追加换行符。
    /// 若 <paramref name="value"/> 为 <c>null</c>，则仅写入换行符。
    /// </summary>
    /// <param name="value">要写入的字符串，可以为 <c>null</c>。</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void WriteLine(string? value) => Out.WriteLine(value);

    /// <summary>
    /// 将字符数组写入标准输出流，并追加换行符。
    /// </summary>
    /// <param name="value">要写入的字符数组。</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void WriteLine(char[] value) => Out.WriteLine(value.AsSpan());

    /// <summary>
    /// 将 UTF-8 字节序列写入标准输出流，并追加换行符。
    /// </summary>
    /// <param name="value">要写入的只读字节序列（UTF-8 编码）。</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void WriteLine(ReadOnlySpan<byte> value) => Out.WriteLine(value);

    /// <summary>
    /// 将 Unicode 字符序列写入标准输出流，并追加换行符。
    /// </summary>
    /// <param name="value">要写入的只读字符序列（UTF-16 编码）。</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void WriteLine(ReadOnlySpan<char> value) => Out.WriteLine(value);

    /// <summary>
    /// 向标准输出流写入一个空行（仅写入换行符）。
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void WriteLine() => Out.WriteLine();
}
