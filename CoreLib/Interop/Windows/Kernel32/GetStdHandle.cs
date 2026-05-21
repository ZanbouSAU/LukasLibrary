// CoreLib/Interop/Windows/Kernel32/GetStdHandle.cs

using System.Runtime.InteropServices;

namespace CoreLib.Interop.Windows.Kernel32;

internal static partial class Kernel32
{
    [LibraryImport("kernel32.dll", SetLastError = true)]
#if !NO_SUPPRESS_GC_TRANSITION
    [SuppressGCTransition]
#endif
    internal static unsafe partial int GetStdHandle(int nStdHandle);
}