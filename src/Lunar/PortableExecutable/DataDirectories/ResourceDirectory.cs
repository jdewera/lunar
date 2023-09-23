using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Lunar.Native;
using Lunar.Native.Structs;

namespace Lunar.PortableExecutable.DataDirectories;

internal class ResourceDirectory : DataDirectoryBase
{
    internal ResourceDirectory(Memory<byte> imageBytes, PEHeaders headers) : base(imageBytes, headers, headers.PEHeader!.ResourceTableDirectory) { }

    internal XDocument? GetManifest()
    {
        if (!IsValid)
        {
            return null;
        }

        // Search the resource directory for the manifest entry

        var resourceDirectory = MemoryMarshal.Read<ImageResourceDirectory>(ImageBytes.Span[DirectoryOffset..]);
        var resourceCount = resourceDirectory.NumberOfIdEntries + resourceDirectory.NumberOfNameEntries;

        for (var i = 0; i < resourceCount; i++)
        {
            var baseEntryOffset = DirectoryOffset + Unsafe.SizeOf<ImageResourceDirectory>();

            // Read the first level resource entry

            var firstLevelEntryOffset = baseEntryOffset + Unsafe.SizeOf<ImageResourceDirectoryEntry>() * i;
            var firstLevelEntry = MemoryMarshal.Read<ImageResourceDirectoryEntry>(ImageBytes.Span[firstLevelEntryOffset..]);

            if (firstLevelEntry.Id != Constants.ManifestResourceId)
            {
                continue;
            }

            // Read the second level resource entry

            var secondLevelEntryOffset = baseEntryOffset + (firstLevelEntry.OffsetToData & int.MaxValue);
            var secondLevelEntry = MemoryMarshal.Read<ImageResourceDirectoryEntry>(ImageBytes.Span[secondLevelEntryOffset..]);

            if (secondLevelEntry.Id != Constants.DllManifestId)
            {
                continue;
            }

            // Read the third level resource entry

            var thirdLevelEntryOffset = baseEntryOffset + (secondLevelEntry.OffsetToData & int.MaxValue);
            var thirdLevelEntry = MemoryMarshal.Read<ImageResourceDirectoryEntry>(ImageBytes.Span[thirdLevelEntryOffset..]);

            // Read the manifest

            var manifestEntryOffset = DirectoryOffset + thirdLevelEntry.OffsetToData;
            var manifestEntry = MemoryMarshal.Read<ImageResourceDataEntry>(ImageBytes.Span[manifestEntryOffset..]);
            var manifestOffset = RvaToOffset(manifestEntry.OffsetToData);
            var manifest = Encoding.UTF8.GetString(ImageBytes.Span.Slice(manifestOffset, manifestEntry.Size));

            // Sanitise the manifest to ensure it can be parsed correctly

            manifest = Regex.Replace(manifest, @"\""\""([\d\w\.]*)\""\""", @"""$1""", RegexOptions.Compiled | RegexOptions.Multiline);
            manifest = Regex.Replace(manifest, @"^\s+$[\r\n]*", string.Empty, RegexOptions.Compiled | RegexOptions.Multiline);
            manifest = manifest.Replace("SXS_ASSEMBLY_NAME", @"""""");
            manifest = manifest.Replace("SXS_ASSEMBLY_VERSION", @"""""");
            manifest = manifest.Replace("SXS_PROCESSOR_ARCHITECTURE", @"""""");

            return XDocument.Parse(manifest);
        }

        return null;
    }
}