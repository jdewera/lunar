using System.Runtime.InteropServices;

namespace Lunar.Native.Structs;

[StructLayout(LayoutKind.Explicit, Size = 20)]
internal readonly record struct ImageImportDescriptor
{
    [field: FieldOffset(0x0)]
    internal int OriginalFirstThunk { get; init; }

    [field: FieldOffset(0xC)]
    internal int Name { get; init; }

    [field: FieldOffset(0x10)]
    internal int FirstThunk { get; init; }
}