using System.Runtime.InteropServices;

namespace Lunar.Shellcode.Records;

internal record CallDescriptor<T>(nint Address, CallingConvention CallingConvention, IList<T> Arguments, nint? ReturnAddress);