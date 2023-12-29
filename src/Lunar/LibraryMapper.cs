using System.ComponentModel;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Lunar.Extensions;
using Lunar.FileResolution;
using Lunar.Helpers;
using Lunar.Native.Enums;
using Lunar.Native.PInvoke;
using Lunar.Native.Structs;
using Lunar.PortableExecutable;
using Lunar.Remote;

namespace Lunar;

/// <summary>
/// Provides the functionality to map a DLL from disk or memory into a process
/// </summary>
public class LibraryMapper
{
    /// <summary>
    /// The base address of the DLL in the process
    /// </summary>
    public nint DllBaseAddress { get; private set; }

    private readonly Memory<byte> _dllBytes;
    private readonly FileResolver _fileResolver;
    private nint _ldrEntryAddress;
    private readonly MappingFlags _mappingFlags;
    private readonly PEImage _peImage;
    private readonly ProcessContext _processContext;

    /// <summary>
    /// Initialises an instances of the <see cref="LibraryMapper"/> class with the functionality to map a DLL from memory into a process
    /// </summary>
    public LibraryMapper(Process process, Memory<byte> dllBytes, MappingFlags mappingFlags = MappingFlags.None)
    {
        if (process.HasExited)
        {
            throw new ArgumentException("The provided process is not currently running");
        }

        if (dllBytes.IsEmpty)
        {
            throw new ArgumentException("The provided DLL bytes were empty");
        }

        _dllBytes = dllBytes.ToArray();
        _fileResolver = new FileResolver(process, null);
        _mappingFlags = mappingFlags;
        _peImage = new PEImage(dllBytes);
        _processContext = new ProcessContext(process);
    }

    /// <summary>
    /// Initialises an instances of the <see cref="LibraryMapper"/> class with the functionality to map a DLL from disk into a process
    /// </summary>
    public LibraryMapper(Process process, string dllFilePath, MappingFlags mappingFlags = MappingFlags.None)
    {
        if (process.HasExited)
        {
            throw new ArgumentException("The provided process is not currently running");
        }

        if (!File.Exists(dllFilePath))
        {
            throw new ArgumentException("The provided file path did not point to a valid file");
        }

        var dllBytes = File.ReadAllBytes(dllFilePath);

        _dllBytes = dllBytes.ToArray();
        _fileResolver = new FileResolver(process, Path.GetDirectoryName(dllFilePath));
        _mappingFlags = mappingFlags;
        _peImage = new PEImage(dllBytes);
        _processContext = new ProcessContext(process);
    }

    /// <summary>
    /// Maps the DLL into the process
    /// </summary>
    public void MapLibrary()
    {
        if (DllBaseAddress != 0)
        {
            return;
        }

        var cleanupStack = new Stack<Action>();

        try
        {
            AllocateImage();
            cleanupStack.Push(FreeImage);

            AllocateLoaderEntry();
            cleanupStack.Push(FreeLoaderEntry);

            LoadDependencies();
            cleanupStack.Push(FreeDependencies);

            BuildImportAddressTable();
            RelocateImage();
            MapHeaders();
            MapSections();

            InsertExceptionHandlers();
            cleanupStack.Push(RemoveExceptionHandlers);

            InitialiseTlsData();
            cleanupStack.Push(ReleaseTlsData);

            CallInitialisationRoutines(DllReason.ProcessAttach);
            EraseHeaders();
        }
        catch
        {
            while (cleanupStack.TryPop(out var cleanupRoutine))
            {
                Executor.IgnoreExceptions(cleanupRoutine);
            }

            throw;
        }
    }

    /// <summary>
    /// Unmaps the DLL from the process
    /// </summary>
    public void UnmapLibrary()
    {
        if (DllBaseAddress == 0)
        {
            return;
        }

        var topLevelException = default(Exception);

        try
        {
            CallInitialisationRoutines(DllReason.ProcessDetach);
        }
        catch (Exception exception)
        {
            topLevelException ??= exception;
        }

        try
        {
            ReleaseTlsData();
        }
        catch (Exception exception)
        {
            topLevelException ??= exception;
        }

        try
        {
            RemoveExceptionHandlers();
        }
        catch (Exception exception)
        {
            topLevelException ??= exception;
        }

        try
        {
            FreeDependencies();
        }
        catch (Exception exception)
        {
            topLevelException ??= exception;
        }

        try
        {
            FreeImage();
        }
        catch (Exception exception)
        {
            topLevelException ??= exception;
        }

        try
        {
            FreeLoaderEntry();
        }
        catch (Exception exception)
        {
            topLevelException ??= exception;
        }

        if (topLevelException is not null)
        {
            throw topLevelException;
        }
    }

    private void AllocateImage()
    {
        DllBaseAddress = _processContext.Process.AllocateBuffer(_peImage.Headers.PEHeader!.SizeOfImage, ProtectionType.ReadOnly);
    }

    private void AllocateLoaderEntry()
    {
        if (_peImage.Headers.PEHeader!.ThreadLocalStorageTableDirectory.Size == 0)
        {
            return;
        }

        if (_processContext.Architecture == Architecture.X86)
        {
            _ldrEntryAddress = _processContext.Process.AllocateBuffer(Unsafe.SizeOf<LdrDataTableEntry32>(), ProtectionType.ReadWrite);
            var loaderEntry = new LdrDataTableEntry32 { DllBase = (int) DllBaseAddress };
            _processContext.Process.WriteStruct(_ldrEntryAddress, loaderEntry);
        }
        else
        {
            _ldrEntryAddress = _processContext.Process.AllocateBuffer(Unsafe.SizeOf<LdrDataTableEntry64>(), ProtectionType.ReadWrite);
            var loaderEntry = new LdrDataTableEntry64 { DllBase = DllBaseAddress };
            _processContext.Process.WriteStruct(_ldrEntryAddress, loaderEntry);
        }
    }

    private void BuildImportAddressTable()
    {
        foreach (var importDescriptor in _peImage.ImportDirectory.GetImportDescriptors())
        {
            foreach (var (functionName, functionOrdinal, functionOffset) in importDescriptor.Functions)
            {
                // Write the function address into the import address table

                var functionAddress = functionName is null ? _processContext.GetFunctionAddress(importDescriptor.Name, functionOrdinal) : _processContext.GetFunctionAddress(importDescriptor.Name, functionName);
                MemoryMarshal.Write(_dllBytes.Span[functionOffset..], in functionAddress);
            }
        }
    }

    private void CallInitialisationRoutines(DllReason reason)
    {
        if (_mappingFlags.HasFlag(MappingFlags.SkipInitRoutines))
        {
            return;
        }

        // Call the entry point of any TLS callbacks

        foreach (var callbackAddress in _peImage.TlsDirectory.GetTlsCallbacks().Select(callBack => DllBaseAddress + callBack.RelativeAddress))
        {
            _processContext.CallRoutine(callbackAddress, CallingConvention.StdCall, DllBaseAddress, reason, 0);
        }

        if ((_peImage.Headers.CorHeader?.Flags.HasFlag(CorFlags.ILOnly) ?? false) || _peImage.Headers.PEHeader!.AddressOfEntryPoint == 0)
        {
            return;
        }

        // Call the DLL entry point

        var entryPointAddress = DllBaseAddress + _peImage.Headers.PEHeader!.AddressOfEntryPoint;

        if (!_processContext.CallRoutine<bool>(entryPointAddress, CallingConvention.StdCall, DllBaseAddress, reason, 0))
        {
            throw new ApplicationException($"Failed to call the DLL entry point with {reason:G}");
        }
    }

    private void EraseHeaders()
    {
        if (!_mappingFlags.HasFlag(MappingFlags.DiscardHeaders))
        {
            return;
        }

        _processContext.Process.WriteSpan(DllBaseAddress, new byte[_peImage.Headers.PEHeader!.SizeOfHeaders].AsSpan());
    }

    private void FreeDependencies()
    {
        foreach (var (dependencyName, _) in _peImage.ImportDirectory.GetImportDescriptors())
        {
            // Free the dependency using the Windows loader

            var dependencyAddress = _processContext.GetModuleAddress(dependencyName);

            if (!_processContext.CallRoutine<bool>(_processContext.GetFunctionAddress("kernel32.dll", "FreeLibrary"), CallingConvention.StdCall, dependencyAddress))
            {
                throw new ApplicationException($"Failed to free the dependency {dependencyName} from the process");
            }
        }

        _processContext.ClearModuleCache();
    }

    private void FreeImage()
    {
        try
        {
            _processContext.Process.FreeBuffer(DllBaseAddress);
        }
        finally
        {
            DllBaseAddress = 0;
        }
    }

    private void FreeLoaderEntry()
    {
        if (_peImage.Headers.PEHeader!.ThreadLocalStorageTableDirectory.Size == 0)
        {
            return;
        }

        try
        {
            _processContext.Process.FreeBuffer(_ldrEntryAddress);
        }
        finally
        {
            _ldrEntryAddress = 0;
        }
    }

    private void InitialiseTlsData()
    {
        if (_peImage.Headers.PEHeader!.ThreadLocalStorageTableDirectory.Size == 0)
        {
            return;
        }

        var status = _processContext.CallRoutine<NtStatus>(_processContext.GetNtdllSymbolAddress("LdrpHandleTlsData"), CallingConvention.FastCall, _ldrEntryAddress);

        if (!status.IsSuccess())
        {
            throw new Win32Exception(Ntdll.RtlNtStatusToDosError(status));
        }
    }

    private void InsertExceptionHandlers()
    {
        if (!_processContext.CallRoutine<bool>(_processContext.GetNtdllSymbolAddress("RtlInsertInvertedFunctionTable"), CallingConvention.FastCall, DllBaseAddress, _peImage.Headers.PEHeader!.SizeOfImage))
        {
            throw new ApplicationException("Failed to insert exception handlers");
        }
    }

    private void LoadDependencies()
    {
        var activationContext = new ActivationContext(_peImage.ResourceDirectory.GetManifest(), _processContext.Architecture);

        foreach (var (dependencyName, _) in _peImage.ImportDirectory.GetImportDescriptors())
        {
            // Write the dependency file path into the process

            var dependencyFilePath = _fileResolver.ResolveFilePath(_processContext.ResolveModuleName(dependencyName, null), activationContext);

            if (dependencyFilePath is null)
            {
                throw new FileNotFoundException($"Failed to resolve the dependency file path for {dependencyName}");
            }

            var dependencyFilePathAddress = _processContext.Process.AllocateBuffer(Encoding.Unicode.GetByteCount(dependencyFilePath), ProtectionType.ReadOnly);

            try
            {
                _processContext.Process.WriteString(dependencyFilePathAddress, dependencyFilePath);

                // Load the dependency using the Windows loader

                var dependencyAddress = _processContext.CallRoutine<nint>(_processContext.GetFunctionAddress("kernel32.dll", "LoadLibraryW"), CallingConvention.StdCall, dependencyFilePathAddress);

                if (dependencyAddress == 0)
                {
                    throw new ApplicationException($"Failed to load the dependency {dependencyName} into the process");
                }

                _processContext.RecordModuleLoad(dependencyAddress, dependencyFilePath);
            }
            finally
            {
                Executor.IgnoreExceptions(() => _processContext.Process.FreeBuffer(dependencyFilePathAddress));
            }
        }
    }

    private void MapHeaders()
    {
        var headerBytes = _dllBytes.Span[.._peImage.Headers.PEHeader!.SizeOfHeaders];
        _processContext.Process.WriteSpan(DllBaseAddress, headerBytes);
    }

    private void MapSections()
    {
        var sectionHeaders = _peImage.Headers.SectionHeaders.AsEnumerable();

        if (_peImage.Headers.CorHeader is null || !_peImage.Headers.CorHeader.Flags.HasFlag(CorFlags.ILOnly))
        {
            sectionHeaders = sectionHeaders.Where(sectionHeader => !sectionHeader.SectionCharacteristics.HasFlag(SectionCharacteristics.MemDiscardable));
        }

        foreach (var sectionHeader in sectionHeaders)
        {
            var sectionAddress = DllBaseAddress + sectionHeader.VirtualAddress;

            // Map the raw section if not empty

            if (sectionHeader.SizeOfRawData > 0)
            {
            var sectionBytes = _dllBytes.Span.Slice(sectionHeader.PointerToRawData, sectionHeader.SizeOfRawData);
            _processContext.Process.WriteSpan(sectionAddress, sectionBytes);
            }

            // Determine the protection to apply to the section

            ProtectionType sectionProtection;

            if (sectionHeader.SectionCharacteristics.HasFlag(SectionCharacteristics.MemExecute))
            {
                if (sectionHeader.SectionCharacteristics.HasFlag(SectionCharacteristics.MemWrite))
                {
                    sectionProtection = sectionHeader.SectionCharacteristics.HasFlag(SectionCharacteristics.MemRead) ? ProtectionType.ExecuteReadWrite : ProtectionType.ExecuteWriteCopy;
                }
                else
                {
                    sectionProtection = sectionHeader.SectionCharacteristics.HasFlag(SectionCharacteristics.MemRead) ? ProtectionType.ExecuteRead : ProtectionType.Execute;
                }
            }
            else if (sectionHeader.SectionCharacteristics.HasFlag(SectionCharacteristics.MemWrite))
            {
                sectionProtection = sectionHeader.SectionCharacteristics.HasFlag(SectionCharacteristics.MemRead) ? ProtectionType.ReadWrite : ProtectionType.WriteCopy;
            }
            else
            {
                sectionProtection = sectionHeader.SectionCharacteristics.HasFlag(SectionCharacteristics.MemRead) ? ProtectionType.ReadOnly : ProtectionType.NoAccess;
            }

            if (sectionHeader.SectionCharacteristics.HasFlag(SectionCharacteristics.MemNotCached))
            {
                sectionProtection |= ProtectionType.NoCache;
            }

            // Calculate the aligned section size

            var sectionAlignment = _peImage.Headers.PEHeader!.SectionAlignment;
            var alignedSectionSize = Math.Max(sectionHeader.SizeOfRawData, sectionHeader.VirtualSize);
            alignedSectionSize = alignedSectionSize + sectionAlignment - 1 - (alignedSectionSize + sectionAlignment - 1) % sectionAlignment;

            // Adjust the protection of the aligned section

            _processContext.Process.ProtectBuffer(sectionAddress, alignedSectionSize, sectionProtection);
        }
    }

    private void RelocateImage()
    {
        // Calculate the delta from the preferred base address and perform the needed relocations

        if (_processContext.Architecture == Architecture.X86)
        {
            var delta = (uint) DllBaseAddress - (uint) _peImage.Headers.PEHeader!.ImageBase;

            foreach (var relocation in _peImage.RelocationDirectory.GetRelocations())
            {
                if (relocation.Type != RelocationType.HighLow)
                {
                    continue;
                }

                var relocationValue = MemoryMarshal.Read<uint>(_dllBytes.Span[relocation.Offset..]) + delta;
                MemoryMarshal.Write(_dllBytes.Span[relocation.Offset..], in relocationValue);
            }
        }
        else
        {
            var delta = (ulong) DllBaseAddress - _peImage.Headers.PEHeader!.ImageBase;

            foreach (var relocation in _peImage.RelocationDirectory.GetRelocations())
            {
                if (relocation.Type != RelocationType.Dir64)
                {
                    continue;
                }

                var relocationValue = MemoryMarshal.Read<ulong>(_dllBytes.Span[relocation.Offset..]) + delta;
                MemoryMarshal.Write(_dllBytes.Span[relocation.Offset..], in relocationValue);
            }
        }
    }

    private void ReleaseTlsData()
    {
        if (_peImage.Headers.PEHeader!.ThreadLocalStorageTableDirectory.Size == 0)
        {
            return;
        }

        var status = _processContext.CallRoutine<NtStatus>(_processContext.GetNtdllSymbolAddress("LdrpReleaseTlsEntry"), CallingConvention.FastCall, _ldrEntryAddress, 0);

        if (!status.IsSuccess())
        {
            throw new Win32Exception(Ntdll.RtlNtStatusToDosError(status));
        }
    }

    private void RemoveExceptionHandlers()
    {
        if (!_processContext.CallRoutine<bool>(_processContext.GetNtdllSymbolAddress("RtlRemoveInvertedFunctionTable"), CallingConvention.FastCall, DllBaseAddress))
        {
            throw new ApplicationException("Failed to remove exception handlers");
        }
    }
}