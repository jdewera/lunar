using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Lunar.Native.Structs;
using Lunar.PortableExecutable.Records;

namespace Lunar.PortableExecutable.DataDirectories;

internal class ImportDirectory : DataDirectoryBase
{
    internal ImportDirectory(Memory<byte> imageBytes, PEHeaders headers) : base(imageBytes, headers, headers.PEHeader!.ImportTableDirectory) { }

    internal IEnumerable<ImportDescriptor> GetImportDescriptors()
    {
        if (!IsValid)
        {
            yield break;
        }

        for (var i = 0;; i++)
        {
            // Read the descriptor

            var descriptorOffset = DirectoryOffset + Unsafe.SizeOf<ImageImportDescriptor>() * i;
            var descriptor = MemoryMarshal.Read<ImageImportDescriptor>(ImageBytes.Span[descriptorOffset..]);

            if (descriptor.FirstThunk == 0)
            {
                break;
            }

            // Read the name

            var nameOffset = RvaToOffset(descriptor.Name);
            var nameLength = ImageBytes.Span[nameOffset..].IndexOf(byte.MinValue);
            var name = Encoding.UTF8.GetString(ImageBytes.Span.Slice(nameOffset, nameLength));

            // Read the functions imported under the descriptor

            var offsetTableOffset = RvaToOffset(descriptor.FirstThunk);
            var thunkTableOffset = descriptor.OriginalFirstThunk == 0 ? offsetTableOffset : RvaToOffset(descriptor.OriginalFirstThunk);
            var functions = GetImportedFunctions(offsetTableOffset, thunkTableOffset);

            yield return new ImportDescriptor(name, functions);
        }
    }

    private ImportedFunction GetImportedFunction(int thunk)
    {
        // Read the ordinal

        var ordinalOffset = RvaToOffset(thunk);
        var ordinal = MemoryMarshal.Read<short>(ImageBytes.Span[ordinalOffset..]);

        // Read the name

        var nameOffset = ordinalOffset + sizeof(short);
        var nameLength = ImageBytes.Span[nameOffset..].IndexOf(byte.MinValue);
        var name = Encoding.UTF8.GetString(ImageBytes.Span.Slice(nameOffset, nameLength));

        return new ImportedFunction(name, ordinal, 0);
    }

    private IEnumerable<ImportedFunction> GetImportedFunctions(int offsetTableOffset, int thunkTableOffset)
    {
        for (var i = 0;; i ++)
        {
            if (Headers.PEHeader!.Magic == PEMagic.PE32)
            {
                // Read the thunk

                var thunkOffset = thunkTableOffset + sizeof(int) * i;
                var thunk = MemoryMarshal.Read<int>(ImageBytes.Span[thunkOffset..]);

                if (thunk == 0)
                {
                    break;
                }

                // Check if the function is imported via ordinal

                var functionOffset = offsetTableOffset + sizeof(int) * i;

                if ((thunk & int.MinValue) != 0)
                {
                    var ordinal = thunk & ushort.MaxValue;
                    yield return new ImportedFunction(null, ordinal, functionOffset);
                }
                else
                {
                    yield return GetImportedFunction(thunk) with { Offset = functionOffset };
                }
            }
            else
            {
                // Read the thunk

                var thunkOffset = thunkTableOffset + sizeof(long) * i;
                var thunk = MemoryMarshal.Read<long>(ImageBytes.Span[thunkOffset..]);

                if (thunk == 0)
                {
                    break;
                }

                // Check if the function is imported via ordinal

                var functionOffset = offsetTableOffset + sizeof(long) * i;

                if ((thunk & long.MinValue) != 0)
                {
                    var ordinal = thunk & ushort.MaxValue;
                    yield return new ImportedFunction(null, (int) ordinal, functionOffset);
                }
                else
                {
                    yield return GetImportedFunction((int) thunk) with { Offset = functionOffset };
                }
            }
        }
    }
}