using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace Lunar.SymbolResolution;

internal static class SymbolDownloader
{
    internal static string FindOrDownloadNtdllSymbols(Architecture architecture)
    {
        var systemDirectoryPath = architecture == Architecture.X86 ? Environment.GetFolderPath(Environment.SpecialFolder.SystemX86) : Environment.SystemDirectory;
        var filePath = Path.Combine(systemDirectoryPath, "ntdll.dll");

        using var peReader = new PEReader(File.OpenRead(filePath));
        var codeViewEntry = peReader.ReadDebugDirectory().First(entry => entry.Type == DebugDirectoryEntryType.CodeView);
        var pdbData = peReader.ReadCodeViewDebugDirectoryData(codeViewEntry);

        // Check if the correct PDB version is already cached to avoid duplicate downloads

        var cacheDirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lunar", "Dependencies");
        var cacheDirectory = Directory.CreateDirectory(cacheDirectoryPath);
        var pdbFilePath = Path.Combine(cacheDirectory.FullName, $"{pdbData.Path.Replace(".pdb", string.Empty)}-{pdbData.Guid:N}.pdb");
        var pdbFile = new FileInfo(pdbFilePath);

        if (pdbFile.Exists && pdbFile.Length != 0)
        {
            return pdbFilePath;
        }

        // Delete any outdated PDB versions

        foreach (var file in cacheDirectory.EnumerateFiles().Where(file => file.Name.StartsWith(pdbData.Path)))
        {
            try
            {
                file.Delete();
            }

            catch (IOException)
            {
                // Ignore
            }
        }

        // Download the PDB from the Microsoft symbol server

        using var httpClient = new HttpClient();
        using var response = httpClient.GetAsync(new Uri($"https://msdl.microsoft.com/download/symbols/{pdbData.Path}/{pdbData.Guid:N}{pdbData.Age}/{pdbData.Path}"), HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Failed to download required symbol file: {pdbData.Path} with status code {response.StatusCode}");
        }

        if (response.Content.Headers.ContentLength is null)
        {
            throw new HttpRequestException($"Failed to retrieve content headers for required symbol file: {pdbData.Path}");
        }

        using var contentStream = response.Content.ReadAsStream();
        using var fileStream = new FileStream(pdbFilePath, FileMode.Create);

        var copyBuffer = new byte[65536];
        var bytesRead = 0d;

        while (true)
        {
            var blockSize = contentStream.Read(copyBuffer);

            if (blockSize == 0)
            {
                break;
            }

            bytesRead += blockSize;

            var progressPercentage = bytesRead / response.Content.Headers.ContentLength.Value * 100;
            var progress = progressPercentage / 2;
            Console.Write($"\rDownloading required symbol file: {pdbData.Path} - [{new string('=', (int) progress)}{new string(' ', 50 - (int) progress)}] - {(int) progressPercentage}%");

            fileStream.Write(copyBuffer, 0, blockSize);
        }

        return pdbFilePath;
    }
}