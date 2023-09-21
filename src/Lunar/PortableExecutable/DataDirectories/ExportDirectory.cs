using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using Lunar.Native.Structs;
using Lunar.PortableExecutable.Records;

namespace Lunar.PortableExecutable.DataDirectories;

internal class ExportDirectory : DataDirectoryBase
{
    internal ExportDirectory(Memory<byte> imageBytes, PEHeaders headers) : base(imageBytes, headers, headers.PEHeader!.ExportTableDirectory) { }

    internal ExportedFunction? GetExportedFunction(string name)
    {
        if (!IsValid)
        {
            return null;
        }

        // Search the name table for the function

        var exportDirectory = MemoryMarshal.Read<ImageExportDirectory>(ImageBytes.Span[DirectoryOffset..]);

        var low = 0;
        var high = exportDirectory.NumberOfNames - 1;

        while (low <= high)
        {
            var middle = (low + high) / 2;

            // Read the current name

            var currentNameOffsetOffset = RvaToOffset(exportDirectory.AddressOfNames) + sizeof(int) * middle;
            var currentNameOffset = RvaToOffset(MemoryMarshal.Read<int>(ImageBytes.Span[currentNameOffsetOffset..]));
            var currentNameLength = ImageBytes.Span[currentNameOffset..].IndexOf(byte.MinValue);
            var currentName = Encoding.UTF8.GetString(ImageBytes.Span.Slice(currentNameOffset, currentNameLength));

            if (name.Equals(currentName, StringComparison.OrdinalIgnoreCase))
            {
                // Read the ordinal

                var ordinalOffset = RvaToOffset(exportDirectory.AddressOfNameOrdinals) + sizeof(short) * middle;
                var ordinal = MemoryMarshal.Read<short>(ImageBytes.Span[ordinalOffset..]) + exportDirectory.Base;

                return GetExportedFunction(ordinal);
            }

            if (string.CompareOrdinal(name, currentName) < 0)
            {
                high = middle - 1;
            }
            else
            {
                low = middle + 1;
            }
        }

        return null;
    }

    internal ExportedFunction? GetExportedFunction(int ordinal)
    {
        if (!IsValid)
        {
            return null;
        }

        // Read the function address

        var exportDirectory = MemoryMarshal.Read<ImageExportDirectory>(ImageBytes.Span[DirectoryOffset..]);

        if ((ordinal -= exportDirectory.Base) >= exportDirectory.NumberOfFunctions)
        {
            return null;
        }

        var functionAddressOffset = RvaToOffset(exportDirectory.AddressOfFunctions) + sizeof(int) * ordinal;
        var functionAddress = MemoryMarshal.Read<int>(ImageBytes.Span[functionAddressOffset..]);

        // Check if the function is forwarded

        var lowerBound = Headers.PEHeader!.ExportTableDirectory.RelativeVirtualAddress;
        var upperBound = lowerBound + Headers.PEHeader!.ExportTableDirectory.Size;

        if (functionAddress < lowerBound || functionAddress > upperBound)
        {
            return new ExportedFunction(null, functionAddress);
        }

        // Read the forwarder string

        var forwarderStringOffset = RvaToOffset(functionAddress);
        var forwarderStringLength = ImageBytes.Span[forwarderStringOffset..].IndexOf(byte.MinValue);
        var forwarderString = Encoding.UTF8.GetString(ImageBytes.Span.Slice(forwarderStringOffset, forwarderStringLength));

        return new ExportedFunction(forwarderString, functionAddress);
    }
}