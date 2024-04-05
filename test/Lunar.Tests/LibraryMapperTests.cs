using System.Diagnostics;
using Xunit;

namespace Lunar.Tests;

public class LibraryMapperTests : IDisposable
{
    private const string _baseTestBinaryDirectoryPath = @"..\..\..\..\..\bin\TestBinaries";
    private Process _process = null!;

    public void Dispose()
    {
        _process.Kill();
        _process.Dispose();
    }

    [Theory]
    [InlineData("Basic.dll")]
    [InlineData("Exception.dll")]
    [InlineData("StaticTls.dll")]
    [InlineData("TlsCallback.dll")]
    public void MapLibrary_MapsX64Library(string dllName)
    {
        // Arrange
        var testBinaryDirectoryPath = Path.Combine(_baseTestBinaryDirectoryPath, "x64", "Release");
        var dllFilePath = Path.Combine(testBinaryDirectoryPath, dllName);

        CreateTestProcess(testBinaryDirectoryPath);

        var libraryMapper = new LibraryMapper(_process, dllFilePath);

        // Act
        libraryMapper.MapLibrary();

        // Assert
        Assert.NotEqual(0, libraryMapper.DllBaseAddress);
    }

    [Theory]
    [InlineData("Basic.dll")]
    [InlineData("Exception.dll")]
    [InlineData("StaticTls.dll")]
    [InlineData("TlsCallback.dll")]
    public void UnmapLibrary_UnmapsX64Library(string dllName)
    {
        // Arrange
        var testBinaryDirectoryPath = Path.Combine(_baseTestBinaryDirectoryPath, "x64", "Release");
        var dllFilePath = Path.Combine(testBinaryDirectoryPath, dllName);

        CreateTestProcess(testBinaryDirectoryPath);

        var libraryMapper = new LibraryMapper(_process, dllFilePath);

        // Act
        libraryMapper.MapLibrary();
        libraryMapper.UnmapLibrary();

        // Assert
        Assert.Equal(0, libraryMapper.DllBaseAddress);
    }

    [Theory]
    [InlineData("Basic.dll")]
    [InlineData("Exception.dll")]
    [InlineData("StaticTls.dll")]
    [InlineData("TlsCallback.dll")]
    public void MapLibrary_MapsX86Library(string dllName)
    {
        // Arrange
        var testBinaryDirectoryPath = Path.Combine(_baseTestBinaryDirectoryPath, "x86", "Release");
        var dllFilePath = Path.Combine(testBinaryDirectoryPath, dllName);

        CreateTestProcess(testBinaryDirectoryPath);
        Thread.Sleep(10);

        var libraryMapper = new LibraryMapper(_process, dllFilePath);

        // Act
        libraryMapper.MapLibrary();

        // Assert
        Assert.NotEqual(0, libraryMapper.DllBaseAddress);
    }

    [Theory]
    [InlineData("Basic.dll")]
    [InlineData("Exception.dll")]
    [InlineData("StaticTls.dll")]
    [InlineData("TlsCallback.dll")]
    public void UnmapLibrary_UnmapsX86Library(string dllName)
    {
        // Arrange
        var testBinaryDirectoryPath = Path.Combine(_baseTestBinaryDirectoryPath, "x86", "Release");
        var dllFilePath = Path.Combine(testBinaryDirectoryPath, dllName);

        CreateTestProcess(testBinaryDirectoryPath);
        Thread.Sleep(10);

        var libraryMapper = new LibraryMapper(_process, dllFilePath);

        // Act
        libraryMapper.MapLibrary();
        libraryMapper.UnmapLibrary();

        // Assert
        Assert.Equal(0, libraryMapper.DllBaseAddress);
    }

    private void CreateTestProcess(string testBinaryDirectoryPath)
    {
        _process = new Process { StartInfo = { FileName = Path.Combine(testBinaryDirectoryPath, "Target.exe"), UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden } };
        _process.Start();
    }
}