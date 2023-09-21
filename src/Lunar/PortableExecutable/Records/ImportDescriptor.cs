namespace Lunar.PortableExecutable.Records;

internal record ImportDescriptor
{
    internal IEnumerable<ImportedFunction> Functions { get; init; }

    internal string Name { get; init; }
}