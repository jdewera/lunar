using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lunar.Native.Enums;
using Lunar.Native.Structs;
using Lunar.PortableExecutable.Records;

namespace Lunar.PortableExecutable.DataDirectories;

internal class RelocationDirectory : DataDirectoryBase
{
    internal RelocationDirectory(Memory<byte> imageBytes, PEHeaders headers) : base(imageBytes, headers, headers.PEHeader!.BaseRelocationTableDirectory) { }

    internal IEnumerable<Relocation> GetRelocations()
    {
        if (!IsValid)
        {
            yield break;
        }

        var currentOffset = DirectoryOffset;
        var maxOffset = DirectoryOffset + Headers.PEHeader!.BaseRelocationTableDirectory.Size;

        while (currentOffset < maxOffset)
        {
            // Read the relocation block

            var relocationBlock = MemoryMarshal.Read<ImageBaseRelocation>(ImageBytes.Span[currentOffset..]);

            if (relocationBlock.SizeOfBlock == 0)
            {
                break;
            }

            var relocationCount = (relocationBlock.SizeOfBlock - Unsafe.SizeOf<ImageBaseRelocation>()) / sizeof(short);

            for (var i = 0; i < relocationCount; i++)
            {
                // Read the relocation

                var relocationOffset = currentOffset + Unsafe.SizeOf<ImageBaseRelocation>() + sizeof(short) * i;
                var relocation = MemoryMarshal.Read<short>(ImageBytes.Span[relocationOffset..]);
                var type = (ushort) relocation >> 12;
                var offset = relocation & 0xFFF;

                yield return new Relocation(RvaToOffset(relocationBlock.VirtualAddress) + offset, (RelocationType) type);
            }

            currentOffset += relocationBlock.SizeOfBlock;
        }
    }
}