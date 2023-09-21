using Lunar.Native.Enums;

namespace Lunar.PortableExecutable.Records;

internal sealed record Relocation
{
    internal int Offset { get; init; }

    internal RelocationType Type { get; init; }
}