using Directory_Scanner.Core.Core;
using Directory_Scanner.Core.FileModels;
using NUnit.Framework;

namespace Directory_Scanner.Tests;

[TestFixture]
public class DirectoryScannerEventTests
{
    private string _testRoot;
    private DirectoryScanner _scanner;
    private List<string> _startedDirectories;
    private List<string> _processedDirectories;
    private List<string> _processedFiles;
    private FileEntry _completedRoot;
    private List<string> _eventOrder;

    [SetUp]
    public void SetUp()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testRoot);
        _scanner = new DirectoryScanner();
        _startedDirectories = new List<string>();
        _processedDirectories = new List<string>();
        _processedFiles = new List<string>();
        _eventOrder = new List<string>();

        _scanner.StartProcessingDirectory += (s, e) =>
        {
            _startedDirectories.Add(e.DirectoryEntry.FullPath);
            _eventOrder.Add($"Start:{e.DirectoryEntry.FullPath}");
        };
        _scanner.DirectoryProcessed += (s, e) =>
        {
            _processedDirectories.Add(e.DirectoryEntry.FullPath);
            _eventOrder.Add($"Processed:{e.DirectoryEntry.FullPath}");
        };
        _scanner.FileProcessed += (s, e) =>
        {
            _processedFiles.Add(e.FileEntry.FullPath);
            _eventOrder.Add($"File:{e.FileEntry.FullPath}");
        };
        _scanner.ProcessingCompleted += (s, e) =>
        {
            _completedRoot = e.FileEntry;
            _eventOrder.Add("Completed");
        };
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_testRoot, true);
        }
        catch
        {
            Console.WriteLine("Failed to delete test directory");
        }
    }

    private void CreateFile(string relativePath, long size = 0)
    {
        string fullPath = Path.Combine(_testRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        using (var fs = new FileStream(fullPath, FileMode.CreateNew))
        {
            if (size > 0)
                fs.SetLength(size);
        }
    }

    private void CreateDirectory(string relativePath)
    {
        Directory.CreateDirectory(Path.Combine(_testRoot, relativePath));
    }

    [Test]
    public async Task AllEvents_AreRaisedAtLeastOnce()
    {
        CreateDirectory("sub");
        CreateFile("sub/file.txt", 10);
        await _scanner.ScanDirectoryAsync(_testRoot);

        Assert.That(_startedDirectories.Count, Is.GreaterThan(0));
        Assert.That(_processedDirectories.Count, Is.GreaterThan(0));
        Assert.That(_processedFiles.Count, Is.GreaterThan(0));
        Assert.That(_completedRoot, Is.Not.Null);
    }

    [Test]
    public async Task StartProcessingDirectory_RaisedForEachDirectory()
    {
        CreateDirectory("a");
        CreateDirectory("a/b");
        CreateDirectory("c");
        await _scanner.ScanDirectoryAsync(_testRoot);

        string rootPath = _testRoot;
        string aPath = Path.Combine(rootPath, "a");
        string bPath = Path.Combine(aPath, "b");
        string cPath = Path.Combine(rootPath, "c");

        Assert.That(_startedDirectories, Has.Count.EqualTo(4));
        Assert.That(_startedDirectories, Does.Contain(rootPath));
        Assert.That(_startedDirectories, Does.Contain(aPath));
        Assert.That(_startedDirectories, Does.Contain(bPath));
        Assert.That(_startedDirectories, Does.Contain(cPath));
    }

    [Test]
    public async Task DirectoryProcessed_RaisedForEachDirectory()
    {
        CreateDirectory("x");
        CreateDirectory("x/y");
        await _scanner.ScanDirectoryAsync(_testRoot);

        string rootPath = _testRoot;
        string xPath = Path.Combine(rootPath, "x");
        string yPath = Path.Combine(xPath, "y");

        Assert.That(_processedDirectories, Has.Count.EqualTo(3));
        Assert.That(_processedDirectories, Does.Contain(rootPath));
        Assert.That(_processedDirectories, Does.Contain(xPath));
        Assert.That(_processedDirectories, Does.Contain(yPath));
    }

    [Test]
    public async Task FileProcessed_RaisedForEachFile()
    {
        CreateFile("f1.txt", 1);
        CreateFile("sub/f2.txt", 2);
        CreateFile("sub/sub/f3.txt", 3);
        await _scanner.ScanDirectoryAsync(_testRoot);

        string f1 = Path.Combine(_testRoot, "f1.txt");
        string f2 = Path.Combine(_testRoot, "sub", "f2.txt");
        string f3 = Path.Combine(_testRoot, "sub", "sub", "f3.txt");

        Assert.That(_processedFiles, Has.Count.EqualTo(3));
        Assert.That(_processedFiles, Does.Contain(f1));
        Assert.That(_processedFiles, Does.Contain(f2));
        Assert.That(_processedFiles, Does.Contain(f3));
    }

    [Test]
    public async Task ProcessingCompleted_RaisedOnceWithRoot()
    {
        CreateDirectory("d");
        CreateFile("d/f.txt", 5);
        await _scanner.ScanDirectoryAsync(_testRoot);

        Assert.That(_completedRoot, Is.Not.Null);
        Assert.That(_completedRoot.FullPath, Is.EqualTo(_testRoot));
        int completedCount = _eventOrder.Count(e => e == "Completed");
        Assert.That(completedCount, Is.EqualTo(1));
    }

    [Test]
    public async Task DirectoryProcessed_RaisedAfterChildrenProcessed()
    {
        CreateDirectory("parent");
        CreateDirectory("parent/child");
        CreateFile("parent/child/file.txt", 10);
        await _scanner.ScanDirectoryAsync(_testRoot);

        string childPath = Path.Combine(_testRoot, "parent", "child");
        string parentPath = Path.Combine(_testRoot, "parent");
        string rootPath = _testRoot;

        int childProcessedIndex = _eventOrder.FindIndex(e => e == $"Processed:{childPath}");
        int parentProcessedIndex = _eventOrder.FindIndex(e => e == $"Processed:{parentPath}");
        int rootProcessedIndex = _eventOrder.FindIndex(e => e == $"Processed:{rootPath}");

        Assert.That(childProcessedIndex, Is.LessThan(parentProcessedIndex));
        Assert.That(parentProcessedIndex, Is.LessThan(rootProcessedIndex));
    }

    [Test]
    public async Task StartProcessingDirectory_RaisedBeforeProcessingChildren()
    {
        CreateDirectory("a");
        CreateDirectory("a/b");
        CreateFile("a/b/f.txt", 7);
        await _scanner.ScanDirectoryAsync(_testRoot);

        string aPath = Path.Combine(_testRoot, "a");
        string bPath = Path.Combine(aPath, "b");
        string fPath = Path.Combine(bPath, "f.txt");

        int aStart = _eventOrder.FindIndex(e => e == $"Start:{aPath}");
        int bStart = _eventOrder.FindIndex(e => e == $"Start:{bPath}");
        int fFile = _eventOrder.FindIndex(e => e == $"File:{fPath}");
        int aProcessed = _eventOrder.FindIndex(e => e == $"Processed:{aPath}");

        Assert.That(aStart, Is.LessThan(bStart));
        Assert.That(bStart, Is.LessThan(fFile));
        Assert.That(fFile, Is.LessThan(aProcessed));
    }

    [Test]
    public async Task EventOrder_StartThenFilesThenProcessedThenCompleted()
    {
        CreateFile("root.txt", 3);
        CreateDirectory("sub");
        CreateFile("sub/file.txt", 4);
        await _scanner.ScanDirectoryAsync(_testRoot);

        string rootPath = _testRoot;
        string subPath = Path.Combine(rootPath, "sub");
        string rootFile = Path.Combine(rootPath, "root.txt");
        string subFile = Path.Combine(subPath, "file.txt");

        int rootStart = _eventOrder.FindIndex(e => e == $"Start:{rootPath}");
        int subStart = _eventOrder.FindIndex(e => e == $"Start:{subPath}");
        int rootFileEvent = _eventOrder.FindIndex(e => e == $"File:{rootFile}");
        int subFileEvent = _eventOrder.FindIndex(e => e == $"File:{subFile}");
        int subProcessed = _eventOrder.FindIndex(e => e == $"Processed:{subPath}");
        int rootProcessed = _eventOrder.FindIndex(e => e == $"Processed:{rootPath}");
        int completed = _eventOrder.FindIndex(e => e == "Completed");

        Assert.That(rootStart, Is.LessThan(subStart));
        Assert.That(subStart, Is.LessThan(rootFileEvent));
        Assert.That(rootFileEvent, Is.LessThan(subFileEvent));
        Assert.That(subFileEvent, Is.LessThan(subProcessed));
        Assert.That(subProcessed, Is.LessThan(rootProcessed));
        Assert.That(rootProcessed, Is.LessThan(completed));
    }

    [Test]
    public async Task StartProcessingDirectory_RaisedEvenIfAccessDenied()
    {
        string inaccessible = Path.Combine(_testRoot, "restricted");
        Directory.CreateDirectory(inaccessible);

        try
        {
            File.SetAttributes(inaccessible, FileAttributes.ReadOnly);
            await _scanner.ScanDirectoryAsync(_testRoot);
        }
        catch (UnauthorizedAccessException)
        {
        }
        finally
        {
            File.SetAttributes(inaccessible, FileAttributes.Normal);
        }

        Assert.That(_startedDirectories, Does.Contain(inaccessible));
        Assert.That(_processedDirectories, Does.Contain(inaccessible));
    }

    [Test]
    public async Task Cancellation_StopsEventRaising()
    {
        CreateDirectory("d1");
        CreateDirectory("d1/d2");
        CreateFile("d1/d2/f.txt", 15);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        try
        {
            await _scanner.ScanDirectoryAsync(_testRoot, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        int startCount = _startedDirectories.Count;
        int processedCount = _processedDirectories.Count;
        int fileCount = _processedFiles.Count;

        Assert.That(startCount + processedCount + fileCount, Is.GreaterThan(0));
        Assert.That(_eventOrder.Last(), Is.Not.EqualTo("Completed"));
    }
}