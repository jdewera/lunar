using Lunar.Native.Enums;

namespace Lunar.PortableExecutable.Records;

internal record Relocation(RelocationType Type, int Offset);