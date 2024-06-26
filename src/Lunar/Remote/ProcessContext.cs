﻿using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Lunar.Extensions;
using Lunar.FileResolution;
using Lunar.Helpers;
using Lunar.Native;
using Lunar.Native.Enums;
using Lunar.Native.PInvoke;
using Lunar.PortableExecutable;
using Lunar.PortableExecutable.Records;
using Lunar.Remote.Records;
using Lunar.Shellcode;
using Lunar.Shellcode.Records;
using Lunar.SymbolResolution;

namespace Lunar.Remote;

internal class ProcessContext
{
    internal Architecture Architecture { get; }
    internal Process Process { get; }

    private readonly ApiSetMap _apiSetMap;
    private readonly Dictionary<string, Module> _moduleCache;
    private readonly SymbolLookup _symbolLookup;

    internal ProcessContext(Process process)
    {
        _apiSetMap = new ApiSetMap();
        _moduleCache = new Dictionary<string, Module>(StringComparer.OrdinalIgnoreCase);
        _symbolLookup = new SymbolLookup(process.GetArchitecture());

        Architecture = process.GetArchitecture();
        Process = process;
    }

    internal void CallRoutine(nint routineAddress, CallingConvention callingConvention, params dynamic[] arguments)
    {
        Span<byte> shellcodeBytes;

        if (Architecture == Architecture.X86)
        {
            var descriptor = new CallDescriptor<int>(routineAddress, callingConvention, Array.ConvertAll(arguments, argument => (int)argument), null);
            shellcodeBytes = Assembler.AssembleCall32(descriptor);
        }
        else
        {
            var descriptor = new CallDescriptor<long>(routineAddress, callingConvention, Array.ConvertAll(arguments, argument => (long)argument), null);
            shellcodeBytes = Assembler.AssembleCall64(descriptor);
        }

        ExecuteShellcode(shellcodeBytes);
    }

    internal T CallRoutine<T>(nint routineAddress, CallingConvention callingConvention, params dynamic[] arguments) where T : unmanaged
    {
        var returnSize = typeof(T) == typeof(nint) ? Architecture == Architecture.X86 ? sizeof(int) : sizeof(long) : Unsafe.SizeOf<T>();
        var returnAddress = Process.AllocateBuffer(returnSize, ProtectionType.ReadWrite);

        try
        {
            Span<byte> shellcodeBytes;

            if (Architecture == Architecture.X86)
            {
                var descriptor = new CallDescriptor<int>(routineAddress, callingConvention, Array.ConvertAll(arguments, argument => (int)argument), returnAddress);
                shellcodeBytes = Assembler.AssembleCall32(descriptor);
            }
            else
            {
                var descriptor = new CallDescriptor<long>(routineAddress, callingConvention, Array.ConvertAll(arguments, argument => (long)argument), returnAddress);
                shellcodeBytes = Assembler.AssembleCall64(descriptor);
            }

            ExecuteShellcode(shellcodeBytes);

            if (typeof(T) != typeof(nint))
            {
                return Process.ReadStruct<T>(returnAddress);
            }

            var pointer = Architecture == Architecture.X86 ? Process.ReadStruct<int>(returnAddress) : (nint)Process.ReadStruct<long>(returnAddress);
            return Unsafe.As<nint, T>(ref pointer);
        }
        finally
        {
            Executor.IgnoreExceptions(() => Process.FreeBuffer(returnAddress));
        }
    }

    internal void ClearModuleCache()
    {
        _moduleCache.Clear();
    }

    internal nint GetFunctionAddress(string moduleName, string functionName)
    {
        var (moduleAddress, peImage) = GetModule(moduleName, null);
        var function = peImage.ExportDirectory.GetExportedFunction(functionName);

        if (function is null)
        {
            throw new ApplicationException($"Failed to find the function {functionName} in the module {moduleName.ToLower()}");
        }

        return function.ForwarderString is null ? moduleAddress + function.RelativeAddress : ResolveForwardedFunction(function.ForwarderString, null);
    }

    internal nint GetFunctionAddress(string moduleName, int functionOrdinal)
    {
        var (moduleAddress, peImage) = GetModule(moduleName, null);
        var function = peImage.ExportDirectory.GetExportedFunction(functionOrdinal);

        if (function is null)
        {
            throw new ApplicationException($"Failed to find the function #{functionOrdinal} in the module {moduleName.ToLower()}");
        }

        return function.ForwarderString is null ? moduleAddress + function.RelativeAddress : ResolveForwardedFunction(function.ForwarderString, null);
    }

    internal nint GetModuleAddress(string moduleName)
    {
        return GetModule(moduleName, null).Address;
    }

    internal nint GetNtdllSymbolAddress(string symbolName)
    {
        return GetModule("ntdll.dll", null).Address + _symbolLookup.GetOffset(symbolName);
    }

    internal void RecordModuleLoad(nint moduleAddress, string moduleFilePath)
    {
        _moduleCache.TryAdd(Path.GetFileName(moduleFilePath), new Module(moduleAddress, new PEImage(File.ReadAllBytes(moduleFilePath))));
    }

    internal string ResolveModuleName(string moduleName, string? parentName)
    {
        if (moduleName.StartsWith("api-ms") || moduleName.StartsWith("ext-ms"))
        {
            return _apiSetMap.ResolveApiSet(moduleName, parentName) ?? moduleName;
        }

        return moduleName;
    }

    private void ExecuteShellcode(Span<byte> shellcodeBytes)
    {
        // Execute the shellcode in the process
        var shellcodeAddress = Process.AllocateBuffer(shellcodeBytes.Length, ProtectionType.ExecuteRead);

        try
        {
            Process.WriteSpan(shellcodeAddress, shellcodeBytes);
            var status = Ntdll.RtlCreateUserThread(Process.SafeHandle, 0, false, 0, 0, 0, shellcodeAddress, 0, out var threadHandle, 0);

            if (!status.IsSuccess())
            {
                throw new Win32Exception(Ntdll.RtlNtStatusToDosError(status));
            }

            using (threadHandle)
            {
                if (Kernel32.WaitForSingleObject(threadHandle, int.MaxValue) == -1)
                {
                    throw new Win32Exception();
                }
            }
        }
        finally
        {
            Executor.IgnoreExceptions(() => Process.FreeBuffer(shellcodeAddress));
        }
    }

    private Module GetModule(string moduleName, string? parentName)
    {
        moduleName = ResolveModuleName(moduleName, parentName);

        if (_moduleCache.TryGetValue(moduleName, out var module))
        {
            return module;
        }

        // Query the process for its module address list
        var moduleAddressListBytes = (stackalloc byte[nint.Size]);
        var moduleType = Architecture == Architecture.X86 ? ModuleType.X86 : ModuleType.X64;

        if (!Kernel32.EnumProcessModulesEx(Process.SafeHandle, out moduleAddressListBytes[0], moduleAddressListBytes.Length, out var sizeNeeded, moduleType))
        {
            throw new Win32Exception();
        }

        if (sizeNeeded > moduleAddressListBytes.Length)
        {
            // Reallocate the module address buffer
            moduleAddressListBytes = sizeNeeded <= 1024 ? stackalloc byte[sizeNeeded] : new byte[sizeNeeded];

            if (!Kernel32.EnumProcessModulesEx(Process.SafeHandle, out moduleAddressListBytes[0], moduleAddressListBytes.Length, out sizeNeeded, moduleType))
            {
                throw new Win32Exception();
            }
        }

        // Search for the module
        var moduleFilePathBytes = (stackalloc byte[Encoding.Unicode.GetMaxByteCount(Constants.MaxPath)]);

        foreach (var address in MemoryMarshal.Cast<byte, nint>(moduleAddressListBytes))
        {
            moduleFilePathBytes.Clear();

            // Retrieve the module file path
            if (!Kernel32.GetModuleFileNameEx(Process.SafeHandle, address, out moduleFilePathBytes[0], Encoding.Unicode.GetCharCount(moduleFilePathBytes)))
            {
                throw new Win32Exception();
            }

            var moduleFilePath = Encoding.Unicode.GetString(moduleFilePathBytes).TrimEnd('\0');

            if (Architecture == Architecture.X86)
            {
                moduleFilePath = moduleFilePath.Replace("System32", "SysWOW64", StringComparison.OrdinalIgnoreCase);
            }

            if (moduleName.Equals(Path.GetFileName(moduleFilePath), StringComparison.OrdinalIgnoreCase))
            {
                _moduleCache.TryAdd(moduleName, new Module(address, new PEImage(File.ReadAllBytes(moduleFilePath))));
            }
            else
            {
                continue;
            }

            return _moduleCache[moduleName];
        }

        throw new ApplicationException($"Failed to find the module {moduleName.ToLower()} in the process");
    }

    private nint ResolveForwardedFunction(string forwarderString, string? parentName)
    {
        while (true)
        {
            var forwardedData = forwarderString.Split(".");
            var (moduleAddress, peImage) = GetModule($"{forwardedData[0]}.dll", parentName);

            ExportedFunction? forwardedFunction;

            if (forwardedData[1].StartsWith('#'))
            {
                var functionOrdinal = int.Parse(forwardedData[1].Replace("#", string.Empty));
                forwardedFunction = peImage.ExportDirectory.GetExportedFunction(functionOrdinal);
            }
            else
            {
                forwardedFunction = peImage.ExportDirectory.GetExportedFunction(forwardedData[1]);
            }

            if (forwardedFunction is null)
            {
                throw new ApplicationException($"Failed to find the function {forwardedData[1]} in the module {forwardedData[0].ToLower()}.dll");
            }

            if (forwardedFunction.ForwarderString is null)
            {
                return moduleAddress + forwardedFunction.RelativeAddress;
            }

            forwarderString = forwardedFunction.ForwarderString;
            parentName = ResolveModuleName($"{forwardedData[0]}.dll", parentName);
        }
    }
}