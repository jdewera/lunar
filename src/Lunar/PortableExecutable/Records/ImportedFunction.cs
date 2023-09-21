namespace Lunar.PortableExecutable.Records;

internal record ImportedFunction
{
    internal string? Name { get; init; }

    internal int Offset { get; init; }

    internal int Ordinal { get; init; }
}