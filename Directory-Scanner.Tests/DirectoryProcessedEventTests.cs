using Directory_Scanner.Core.Core;
using Directory_Scanner.Core.FileModels;
using Directory_Scanner.Core.ScannerEventArgs;
using FluentAssertions;
using NUnit.Framework;

namespace Directory_Scanner.Tests;

[TestFixture]
public class DirectoryProcessedEventTests
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
    public async Task ShouldFireAfterDirectoryIsProcessed()
    {
        var sub = _rootDir.CreateSubdirectory("sub");
        var fileInSub = Path.Combine(sub.FullName, "file.txt");
        await File.WriteAllTextAsync(fileInSub, "content");

        var scanner = new DirectoryScanner();
        var processed = new List<DirectoryProcessedEventArgs>();
        scanner.DirectoryProcessed += (s, e) => processed.Add(e);

        CancellationTokenSource cancellationTokenSource = new();
        await scanner.ScanDirectoryAsync(_rootPath, cancellationTokenSource.Token);

        processed.Should().HaveCount(2);
        var subEvent = processed.First(e => e.DirectoryEntry.FileName == "sub");
        subEvent.DirectoryEntry.FileSize.Should().Be(7); 
        subEvent.DirectoryEntry.FileState.Should().Be(FileState.Ok);
    }

    [Test]
    public async Task RootDirectorySizeShouldIncludeAllSubItems()
    {
        DirectoryInfo sub = _rootDir.CreateSubdirectory("sub");
        
        string file1 = Path.Combine(_rootPath, "root.dat");
        string file2 = Path.Combine(sub.FullName, "sub.dat");
        
        await File.WriteAllBytesAsync(file1, new byte[100]);
        await File.WriteAllBytesAsync(file2, new byte[200]);

        var scanner = new DirectoryScanner();
        DirectoryProcessedEventArgs? rootEvent = null;
        scanner.DirectoryProcessed += (s, e) =>
        {
            if (e.DirectoryEntry.FileName == _rootDir.Name)
                rootEvent = e;
        };

        await scanner.ScanDirectoryAsync(_rootPath);

        rootEvent.Should().NotBeNull();
        rootEvent?.DirectoryEntry.FileSize.Should().Be(300);
    }
}