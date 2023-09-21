namespace Lunar.PortableExecutable.Records;

internal record ImportedFunction(string? Name, int Offset, int Ordinal);