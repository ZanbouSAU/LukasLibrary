// CoreLib/Interop/Unix/System.Native/Write.cs

using System;
using System.Runtime.InteropServices;

namespace CoreLib.Interop.Unix.System.Native;

internal static unsafe partial class Sys
{
    [LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
    internal static partial int Write(IntPtr fd, byte* buf, int count);
}