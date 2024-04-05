using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lunar.Native.PInvoke;
using Lunar.Native.Structs;

namespace Lunar.FileResolution;

internal class ApiSetMap
{
    private readonly nint _address;

    internal ApiSetMap()
    {
        _address = GetLocalAddress();
    }

    internal string? ResolveApiSet(string apiSet, string? parentName)
    {
        // Read the namespace
        var @namespace = Marshal.PtrToStructure<ApiSetNamespace>(_address);

        // Hash the API set without the patch number and suffix
        var charactersToHash = apiSet[..apiSet.LastIndexOf('-')];
        var apiSetHash = charactersToHash.Aggregate(0, (currentHash, character) => currentHash * @namespace.HashFactor + char.ToLower(character));

        // Search the namespace for the corresponding hash entry
        var low = 0;
        var high = @namespace.Count - 1;

        while (low <= high)
        {
            var middle = (low + high) / 2;

            // Read the hash entry
            var hashEntryAddress = _address + @namespace.HashOffset + Unsafe.SizeOf<ApiSetHashEntry>() * middle;
            var hashEntry = Marshal.PtrToStructure<ApiSetHashEntry>(hashEntryAddress);

            if (apiSetHash == hashEntry.Hash)
            {
                // Read the namespace entry name
                var namespaceEntryAddress = _address + @namespace.EntryOffset + Unsafe.SizeOf<ApiSetNamespaceEntry>() * hashEntry.Index;
                var namespaceEntry = Marshal.PtrToStructure<ApiSetNamespaceEntry>(namespaceEntryAddress);
                var namespaceEntryNameAddress = _address + namespaceEntry.NameOffset;
                var namespaceEntryName = Marshal.PtrToStringUni(namespaceEntryNameAddress, namespaceEntry.NameLength / sizeof(char));

                // Ensure the correct hash bucket is being used
                if (!charactersToHash.Equals(namespaceEntryName[..namespaceEntryName.LastIndexOf('-')]))
                {
                    break;
                }

                // Read the default value entry name
                var valueEntryAddress = _address + namespaceEntry.ValueOffset;
                var valueEntry = Marshal.PtrToStructure<ApiSetValueEntry>(valueEntryAddress);
                var valueEntryNameAddress = _address + valueEntry.ValueOffset;
                var valueEntryName = Marshal.PtrToStringUni(valueEntryNameAddress, valueEntry.ValueCount / sizeof(char));

                if (parentName is null || valueEntry.ValueCount == 1)
                {
                    return valueEntryName;
                }

                // Search for an alternative host using the parent
                for (var i = namespaceEntry.ValueCount - 1; i >= 0; i--)
                {
                    // Read the alias value entry name
                    valueEntryAddress = _address + namespaceEntry.ValueOffset + Unsafe.SizeOf<ApiSetValueEntry>() * i;
                    valueEntry = Marshal.PtrToStructure<ApiSetValueEntry>(valueEntryAddress);
                    var valueEntryAliasNameAddress = _address + valueEntry.NameOffset;
                    var valueEntryAliasName = Marshal.PtrToStringUni(valueEntryAliasNameAddress, valueEntry.NameLength / sizeof(char));

                    if (parentName.Equals(valueEntryAliasName, StringComparison.OrdinalIgnoreCase))
                    {
                        // Read the value entry name
                        valueEntryNameAddress = _address + valueEntry.ValueOffset;
                        valueEntryName = Marshal.PtrToStringUni(valueEntryNameAddress, valueEntry.ValueCount / sizeof(char));
                        break;
                    }
                }

                return valueEntryName;
            }

            if ((uint)apiSetHash < (uint)hashEntry.Hash)
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

    private static nint GetLocalAddress()
    {
        var pebAddress = Ntdll.RtlGetCurrentPeb();
        return (nint)Marshal.PtrToStructure<Peb64>(pebAddress).ApiSetMap;
    }
}