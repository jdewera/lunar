using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Lunar.Native.PInvoke;

internal static partial class Kernel32
{
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWow64Process(SafeProcessHandle processHandle, [MarshalAs(UnmanagedType.Bool)] out bool isWow64Process);
}