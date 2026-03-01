using Directory_Scanner.Core.Core;
using FluentAssertions;
using NUnit.Framework;

namespace Directory_Scanner.Tests;

[TestFixture]
public class EdgeCaseTests
{
    private string _rootPath;
    private DirectoryInfo _rootDir;

    [SetUp]
    public void Setup()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "ScannerTests_" + Guid.NewGuid().ToString("N"));
        _rootDir = new DirectoryInfo(_rootPath);
        _rootDir.Create();
    }

    [TearDown]
    public void Cleanup()
    {
        if (_rootDir.Exists)
            _rootDir.Delete(true);
    }

    [Test]
    public async Task DeepNestedDirectory_ShouldNotStackOverflow()
    {
        const int depth = 200;
        var current = _rootDir;
        for (int i = 0; i < depth; i++)
        {
            current = current.CreateSubdirectory($"level{i}");
        }

        var scanner = new DirectoryScanner();
        var startCount = 0;
        scanner.StartProcessingDirectory += (s, e) => Interlocked.Increment(ref startCount);

        await scanner.ScanDirectoryAsync(_rootPath);

        startCount.Should().Be(depth + 1);
    }

    [Test]
    public async Task EmptyDirectory_ShouldCompleteSuccessfully()
    {
        var scanner = new DirectoryScanner();
        var startCount = 0;
        scanner.StartProcessingDirectory += (s, e) => startCount++;

        var result = await scanner.ScanDirectoryAsync(_rootPath);

        result.Should().NotBeNull();
        startCount.Should().Be(1);
        result.FileSize.Should().Be(0);
    }

    [Test]
    public async Task DirectoryWithLongPaths_ShouldBeHandled()
    {
        
        var longName = new string('A', 240); 
        var longDir = _rootDir.CreateSubdirectory(longName);
        var filePath = Path.Combine(longDir.FullName, "file.txt");
        await File.WriteAllTextAsync(filePath, "content");

        var scanner = new DirectoryScanner();
        var startEvents = 0;
        scanner.StartProcessingDirectory += (s, e) => startEvents++;

        await scanner.ScanDirectoryAsync(_rootPath);

        startEvents.Should().Be(2); 
    }
}