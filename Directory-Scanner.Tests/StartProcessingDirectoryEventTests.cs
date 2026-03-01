using Directory_Scanner.Core.Core;
using Directory_Scanner.Core.FileModels;
using Directory_Scanner.Core.ScannerEventArgs;
using FluentAssertions;
using NUnit.Framework;

namespace Directory_Scanner.Tests;

[TestFixture]
public class StartProcessingDirectoryEventTests
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
    public async Task ShouldFireForRootDirectory()
    {
        var scanner = new DirectoryScanner();
        var events = new List<StartProcessingDirectoryEventArgs>();
        scanner.StartProcessingDirectory += (s, e) => events.Add(e);

        await scanner.ScanDirectoryAsync(_rootPath);

        events.Should().ContainSingle();
        var args = events[0];
        args.DirectoryEntry.FileName.Should().Be(_rootDir.Name);
        args.DirectoryEntry.FullPath.Should().Be(_rootPath);
        args.DirectoryEntry.FileSize.Should().Be(0);
        args.DirectoryEntry.FileType.Should().Be(FileType.Directory);
        args.DirectoryEntry.FileState.Should().Be(FileState.Ok);
    }

    [Test]
    public async Task ShouldFireForEachSubdirectory()
    {
        
        DirectoryInfo sub1 = _rootDir.CreateSubdirectory("sub1");
        DirectoryInfo sub2 = _rootDir.CreateSubdirectory("sub2");
        DirectoryInfo subSub = sub1.CreateSubdirectory("subsub");

        DirectoryScanner scanner = new DirectoryScanner();
        List<StartProcessingDirectoryEventArgs> events = new List<StartProcessingDirectoryEventArgs>();
        scanner.StartProcessingDirectory += (s, e) => events.Add(e);

        
        await scanner.ScanDirectoryAsync(_rootPath);

        
        var expectedPaths = new HashSet<string>
        {
            _rootPath,
            sub1.FullName,
            sub2.FullName,
            subSub.FullName
        };

        var actualPaths = events.Select(e => e.DirectoryEntry.FullPath).ToHashSet();

        actualPaths.Should().BeEquivalentTo(expectedPaths);
    }
}