namespace Lunar.PortableExecutable.Records;

internal record ImportDescriptor(string Name, IEnumerable<ImportedFunction> Functions);