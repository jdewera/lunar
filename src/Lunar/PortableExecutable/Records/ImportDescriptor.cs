namespace Lunar.PortableExecutable.Records;

internal record ImportDescriptor(IEnumerable<ImportedFunction> Functions, string Name);