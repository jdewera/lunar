using System.Runtime.InteropServices;

namespace Lunar.Native.PInvoke;

internal static partial class Ntdll
{
    [LibraryImport("ntdll.dll")]
    internal static partial nint RtlGetCurrentPeb();
}