using System.Reflection.PortableExecutable;
using Lunar.PortableExecutable.DataDirectories;

namespace Lunar.PortableExecutable;

internal class PEImage
{
    internal ExportDirectory ExportDirectory { get; }
    internal PEHeaders Headers { get; }
    internal ImportDirectory ImportDirectory { get; }
    internal RelocationDirectory RelocationDirectory { get; }
    internal ResourceDirectory ResourceDirectory { get; }
    internal TlsDirectory TlsDirectory { get; }

    internal PEImage(Memory<byte> imageBytes)
    {
        using var reader = new PEReader(new MemoryStream(imageBytes.ToArray()));

        if (reader.PEHeaders.PEHeader is null || !reader.PEHeaders.IsDll)
        {
            throw new BadImageFormatException("The provided file was not a valid DLL");
        }

        ExportDirectory = new ExportDirectory(imageBytes, reader.PEHeaders);
        Headers = reader.PEHeaders;
        ImportDirectory = new ImportDirectory(imageBytes, reader.PEHeaders);
        RelocationDirectory = new RelocationDirectory(imageBytes, reader.PEHeaders);
        ResourceDirectory = new ResourceDirectory(imageBytes, reader.PEHeaders);
        TlsDirectory = new TlsDirectory(imageBytes, reader.PEHeaders);
    }
}