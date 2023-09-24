using System.Runtime.InteropServices;

namespace Lunar.Native.Structs;

[StructLayout(LayoutKind.Explicit, Size = 2000)]
internal readonly record struct Peb64([field: FieldOffset(0x68)] long ApiSetMap);