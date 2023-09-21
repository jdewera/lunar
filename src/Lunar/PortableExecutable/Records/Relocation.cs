using Lunar.Native.Enums;

namespace Lunar.PortableExecutable.Records;

internal record Relocation(int Offset, RelocationType Type);