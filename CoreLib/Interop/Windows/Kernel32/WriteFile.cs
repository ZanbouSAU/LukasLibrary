// CoreLib/Interop/Windows/Kernel32/WriteFile.cs

using System;
using System.Runtime.InteropServices;

namespace CoreLib.Interop.Windows.Kernel32;

internal static unsafe partial class Kernel32
{
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static unsafe partial int WriteFile(
        IntPtr handle,
        byte* bytes,
        int numberBytesToWrite,
        out int numBytesWritten,
        IntPtr mustBeZero);
}