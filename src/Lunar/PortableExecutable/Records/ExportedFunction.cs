namespace Lunar.PortableExecutable.Records;

internal record ExportedFunction(int RelativeAddress, string? ForwarderString);