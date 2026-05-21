// CoreLib/ConsolePal.cs

using System;
using System.Runtime.InteropServices;
using CoreLib.Interop.Unix.System.Native;
using CoreLib.Interop.Windows.Kernel32;

namespace CoreLib;

/// <summary>
/// 控制台平台抽象层（Platform Abstraction Layer），
/// 封装了不同操作系统（Windows 和 Unix/Linux/macOS）下
/// 标准 I/O 句柄的获取方式和底层读写系统调用，
/// 为上层的 <see cref="StreamReader"/> 和 <see cref="StreamWriter"/> 提供统一的 I/O 接口。
/// <para>
/// 在 Windows 上，使用 Win32 API（<c>GetStdHandle</c>、<c>ReadFile</c>、<c>WriteFile</c>）；
/// 在 Unix 系统上，使用 POSIX 系统调用（文件描述符整数 + <c>read</c>/<c>write</c>）。
/// </para>
/// </summary>
public static unsafe class ConsolePal
{
    /// <summary>
    /// 标准输入的系统标识符。
    /// Windows 下为 <c>-10</c>（<c>STD_INPUT_HANDLE</c>），Unix 下为文件描述符 <c>0</c>。
    /// </summary>
    private static readonly int Stdin = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? -10 : 0;

    /// <summary>
    /// 标准输出的系统标识符。
    /// Windows 下为 <c>-11</c>（<c>STD_OUTPUT_HANDLE</c>），Unix 下为文件描述符 <c>1</c>。
    /// </summary>
    private static readonly int Stdout = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? -11 : 1;

    /// <summary>
    /// 标准错误的系统标识符。
    /// Windows 下为 <c>-12</c>（<c>STD_ERROR_HANDLE</c>），Unix 下为文件描述符 <c>2</c>。
    /// </summary>
    private static readonly int Stderr = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? -12 : 2;

    /// <summary>
    /// 标准输入（stdin）的底层句柄。
    /// Windows 下通过 <see cref="Kernel32.GetStdHandle"/> 获取真实的 HANDLE；
    /// Unix 下直接使用文件描述符整数（0）包装为 <see cref="IntPtr"/>。
    /// </summary>
    internal static readonly IntPtr StdInHandle = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? Kernel32.GetStdHandle(Stdin)
        : Stdin;

    /// <summary>
    /// 标准输出（stdout）的底层句柄。
    /// Windows 下通过 <see cref="Kernel32.GetStdHandle"/> 获取真实的 HANDLE；
    /// Unix 下直接使用文件描述符整数（1）包装为 <see cref="IntPtr"/>。
    /// </summary>
    internal static readonly IntPtr StdOutHandle = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? Kernel32.GetStdHandle(Stdout)
        : Stdout;

    /// <summary>
    /// 标准错误（stderr）的底层句柄。
    /// Windows 下通过 <see cref="Kernel32.GetStdHandle"/> 获取真实的 HANDLE；
    /// Unix 下直接使用文件描述符整数（2）包装为 <see cref="IntPtr"/>。
    /// </summary>
    internal static readonly IntPtr StdErrHandle = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? Kernel32.GetStdHandle(Stderr)
        : Stderr;

    /// <summary>
    /// 从指定的文件描述符或句柄中读取最多 <paramref name="count"/> 字节的数据到缓冲区。
    /// <para>
    /// 在 Unix 系统上，调用 <see cref="Sys.Read"/> 执行 <c>read(2)</c> 系统调用；
    /// 在 Windows 上，调用 <see cref="Kernel32.ReadFile"/> 执行同步文件读取，
    /// 若 <c>ReadFile</c> 返回 0（失败），则返回 <c>-1</c> 表示错误。
    /// </para>
    /// </summary>
    /// <param name="fd">输入句柄（Unix 文件描述符或 Windows HANDLE）。</param>
    /// <param name="buffer">指向接收数据的缓冲区的非托管指针。</param>
    /// <param name="count">请求读取的最大字节数。</param>
    /// <returns>
    /// 实际读取的字节数（大于 0）；
    /// 若到达流末尾，返回 0；
    /// 若发生错误，返回 <c>-1</c>。
    /// </returns>
    internal static int Read(IntPtr fd, byte* buffer, int count)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Sys.Read(fd, buffer, count);
        
        if (Kernel32.ReadFile(fd, buffer, count, out var bytesWritten, IntPtr.Zero) == 0)
            return -1;
        
        return bytesWritten;
    }

    /// <summary>
    /// 将缓冲区中 <paramref name="count"/> 字节的数据写入指定的文件描述符或句柄。
    /// <para>
    /// 在 Unix 系统上，调用 <see cref="Sys.Write"/> 执行 <c>write(2)</c> 系统调用；
    /// 在 Windows 上，调用 <see cref="Kernel32.WriteFile"/> 执行同步文件写入，
    /// 若 <c>WriteFile</c> 返回 0（失败），则返回 <c>-1</c> 表示错误。
    /// </para>
    /// </summary>
    /// <param name="fd">输出句柄（Unix 文件描述符或 Windows HANDLE）。</param>
    /// <param name="buffer">指向待写入数据的缓冲区的非托管指针。</param>
    /// <param name="count">要写入的字节数。</param>
    /// <returns>
    /// 实际写入的字节数（大于 0）；
    /// 若发生错误，返回 <c>-1</c>。
    /// </returns>
    internal static int Write(IntPtr fd, byte* buffer, int count)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Sys.Write(fd, buffer, count);
        
        if (Kernel32.WriteFile(fd, buffer, count, out var bytesWritten, IntPtr.Zero) == 0)
            return -1;
        
        return bytesWritten;
    }
}
