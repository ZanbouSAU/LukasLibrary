// CoreLib/Interop/Unix/System.Native/Read.cs

using System;
using System.Runtime.InteropServices;

namespace CoreLib.Interop.Unix.System.Native;

internal static unsafe partial class Sys
{
    [LibraryImport("libc", EntryPoint = "read", SetLastError = true)]
    internal static partial int Read(IntPtr fd, byte* buf, int count);
}