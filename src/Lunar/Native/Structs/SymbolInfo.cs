using System.Runtime.InteropServices;

namespace Lunar.Native.Structs;

[StructLayout(LayoutKind.Explicit, Size = 88)]
internal readonly record struct SymbolInfo
{
    [field: FieldOffset(0x0)]
    internal int SizeOfStruct { get; init; }

    [field: FieldOffset(0x38)]
    internal long Address { get; init; }

    [field: FieldOffset(0x50)]
    internal int MaxNameLen { get; init; }
}