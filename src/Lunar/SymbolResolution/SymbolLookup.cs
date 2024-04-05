using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lunar.Native;
using Lunar.Native.Enums;
using Lunar.Native.PInvoke;
using Lunar.Native.Structs;
using Microsoft.Win32.SafeHandles;

namespace Lunar.SymbolResolution;

internal class SymbolLookup
{
    private readonly string _pdbFilePath;
    private readonly Dictionary<string, int> _symbolCache;

    internal SymbolLookup(Architecture architecture)
    {
        _pdbFilePath = SymbolDownloader.FindOrDownloadNtdllSymbols(architecture);
        _symbolCache = new Dictionary<string, int>();
    }

    internal int GetOffset(string symbolName)
    {
        if (_symbolCache.TryGetValue(symbolName, out var offset))
        {
            return offset;
        }

        var currentProcessHandle = new SafeProcessHandle(-1, false);

        // Load the PDB into a native symbol handler
        if (!Dbghelp.SymSetOptions(SymbolOptions.UndecorateName).HasFlag(SymbolOptions.UndecorateName))
        {
            throw new Win32Exception();
        }

        if (!Dbghelp.SymInitialize(currentProcessHandle, 0, false))
        {
            throw new Win32Exception();
        }

        try
        {
            const int pseudoAddress = 0x1000;

            var pdbFileSize = new FileInfo(_pdbFilePath).Length;
            var symbolTableAddress = Dbghelp.SymLoadModule(currentProcessHandle, 0, _pdbFilePath, 0, pseudoAddress, (int)pdbFileSize, 0, 0);

            if (symbolTableAddress == 0)
            {
                throw new Win32Exception();
            }

            // Retrieve the symbol info
            var symbolInfoBytes = (stackalloc byte[(Unsafe.SizeOf<SymbolInfo>() + sizeof(char) * Constants.MaxSymbolName + sizeof(long) - 1) / sizeof(long)]);
            var symbolInfo = new SymbolInfo { SizeOfStruct = Unsafe.SizeOf<SymbolInfo>(), MaxNameLen = Constants.MaxSymbolName };
            MemoryMarshal.Write(symbolInfoBytes, in symbolInfo);

            if (!Dbghelp.SymFromName(currentProcessHandle, symbolName, out Unsafe.As<byte, SymbolInfo>(ref symbolInfoBytes[0])))
            {
                throw new Win32Exception();
            }

            symbolInfo = MemoryMarshal.Read<SymbolInfo>(symbolInfoBytes);
            offset = (int)(symbolInfo.Address - pseudoAddress);
            _symbolCache.Add(symbolName, offset);
        }
        finally
        {
            Dbghelp.SymCleanup(currentProcessHandle);
        }

        return offset;
    }
}