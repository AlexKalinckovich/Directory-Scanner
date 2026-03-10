using Directory_Scanner.Core.Core;
using Directory_Scanner.Core.FileModels;
using FluentAssertions;
using NUnit.Framework;

namespace Directory_Scanner.Tests;

[TestFixture]
public class DirectoryScannerTests
{
    private string _testDirectory;
    private DirectoryScanner _scanner;

    [SetUp]
    public void SetUp()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"scanner_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        _scanner = new DirectoryScanner();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _scanner.DisposeAsync();

        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Test]
    public async Task ScanDirectoryAsync_EmptyDirectory_ShouldReturnRootEntry()
    {
        FileEntry result = await _scanner.ScanDirectoryAsync(_testDirectory);

        result.Should().NotBeNull();
        result.FullPath.Should().Be(_testDirectory);
        result.FileType.Should().Be(FileType.Directory);
    }

    [Test]
    public async Task ScanDirectoryAsync_WithFiles_ShouldReturnCorrectFileSize()
    {
        string file1Path = Path.Combine(_testDirectory, "file1.txt");
        string file2Path = Path.Combine(_testDirectory, "file2.txt");

        File.WriteAllText(file1Path, "Hello");
        File.WriteAllText(file2Path, "World!!!");

        FileEntry result = await _scanner.ScanDirectoryAsync(_testDirectory);

        long expectedSize = new FileInfo(file1Path).Length + new FileInfo(file2Path).Length;
        result.FileSize.Should().Be(expectedSize);
        result.Percentage.Should().Be(100.0);
    }

    [Test]
    public async Task ScanDirectoryAsync_WithSubdirectories_ShouldBuildHierarchy()
    {
        string subDir1 = Path.Combine(_testDirectory, "sub1");
        string subDir2 = Path.Combine(_testDirectory, "sub2");

        Directory.CreateDirectory(subDir1);
        Directory.CreateDirectory(subDir2);

        File.WriteAllText(Path.Combine(subDir1, "file1.txt"), "Content1");
        File.WriteAllText(Path.Combine(subDir2, "file2.txt"), "Content2");

        FileEntry result = await _scanner.ScanDirectoryAsync(_testDirectory);

        result.SubDirectories.Count.Should().Be(2);
        result.FileSize.Should().BeGreaterThan(0);
        result.Percentage.Should().Be(100.0);
    }

    [Test]
    public async Task ScanDirectoryAsync_InvalidPath_ShouldThrowArgumentException()
    {
        Func<Task> act = async () => await _scanner.ScanDirectoryAsync("");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Test]
    public async Task ScanDirectoryAsync_NonExistentPath_ShouldThrowDirectoryNotFoundException()
    {
        string nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        Func<Task> act = async () => await _scanner.ScanDirectoryAsync(nonExistentPath);

        await act.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [Test]
    public async Task ScanDirectoryAsync_Cancellation_ShouldReturnPartialResults()
    {
        CreateDeepDirectoryStructure(_testDirectory, 10);

        CancellationTokenSource cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        FileEntry result = await _scanner.ScanDirectoryAsync(_testDirectory, cts.Token);

        result.Should().NotBeNull();
        result.FullPath.Should().Be(_testDirectory);
        result.FileSize.Should().BeGreaterThanOrEqualTo(0);
        result.Percentage.Should().BeGreaterThanOrEqualTo(0.0);
        result.Percentage.Should().BeLessThanOrEqualTo(100.0);
    }

    [Test]
    public async Task ScanDirectoryAsync_CancellationBeforeStart_ShouldReturnEmptyResults()
    {
        CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel();

        FileEntry result = await _scanner.ScanDirectoryAsync(_testDirectory, cts.Token);

        result.Should().NotBeNull();
        result.FullPath.Should().Be(_testDirectory);
        result.FileType.Should().Be(FileType.Directory);
        result.SubDirectories.Count.Should().Be(0);
        result.FileSize.Should().Be(0);
        result.Percentage.Should().Be(0.0);
    }

    [Test]
    public async Task ScanDirectoryAsync_Events_ShouldBeRaised()
    {
        List<FileEntry> processedFiles = new List<FileEntry>();
        List<FileEntry> processedDirs = new List<FileEntry>();
        FileEntry? completedEntry = null;

        _scanner.FileProcessed += (sender, args) => processedFiles.Add(args.FileEntry);
        _scanner.DirectoryProcessed += (sender, args) => processedDirs.Add(args.DirectoryEntry);
        _scanner.ProcessingCompleted += (sender, args) => completedEntry = args.FileEntry;

        string file1 = Path.Combine(_testDirectory, "file1.txt");
        string file2 = Path.Combine(_testDirectory, "file2.txt");

        File.WriteAllText(file1, "Content1");
        File.WriteAllText(file2, "Content2");

        await _scanner.ScanDirectoryAsync(_testDirectory);

        processedFiles.Count.Should().Be(2);
        processedDirs.Count.Should().BeGreaterThan(0);
        completedEntry.Should().NotBeNull();
    }

    [Test]
    public async Task ScanDirectoryAsync_MultipleDirectories_ShouldNotDeadlock()
    {
        for (int i = 0; i < 5; i++)
        {
            string subDir = Path.Combine(_testDirectory, $"dir{i}");
            Directory.CreateDirectory(subDir);

            for (int j = 0; j < 10; j++)
            {
                File.WriteAllText(Path.Combine(subDir, $"file{j}.txt"), $"Content{j}");
            }
        }

        FileEntry result = await _scanner.ScanDirectoryAsync(_testDirectory);

        result.Should().NotBeNull();
        result.SubDirectories.Count.Should().Be(5);
    }

    [Test]
    public async Task ScanDirectoryAsync_LargeFileCount_ShouldComplete()
    {
        for (int i = 0; i < 100; i++)
        {
            File.WriteAllText(Path.Combine(_testDirectory, $"file{i}.txt"), $"Content{i}");
        }

        FileEntry result = await _scanner.ScanDirectoryAsync(_testDirectory);

        result.FileSize.Should().BeGreaterThan(0);
        result.Percentage.Should().Be(100.0);
    }

    [Test]
    public async Task ScanDirectoryAsync_ConcurrentScans_ShouldNotInterfere()
    {
        string dir1 = Path.Combine(_testDirectory, "scan1");
        string dir2 = Path.Combine(_testDirectory, "scan2");

        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);

        File.WriteAllText(Path.Combine(dir1, "file1.txt"), "Content1");
        File.WriteAllText(Path.Combine(dir2, "file2.txt"), "Content2");

        DirectoryScanner scanner1 = new DirectoryScanner();
        DirectoryScanner scanner2 = new DirectoryScanner();

        Task<FileEntry> task1 = scanner1.ScanDirectoryAsync(dir1);
        Task<FileEntry> task2 = scanner2.ScanDirectoryAsync(dir2);

        await Task.WhenAll(task1, task2);

        task1.Result.FileSize.Should().BeGreaterThan(0);
        task2.Result.FileSize.Should().BeGreaterThan(0);

        await scanner1.DisposeAsync();
        await scanner2.DisposeAsync();
    }

    [Test]
    public async Task ScanDirectoryAsync_DeepNesting_ShouldHandleCorrectly()
    {
        CreateDeepDirectoryStructure(_testDirectory, 5);

        FileEntry result = await _scanner.ScanDirectoryAsync(_testDirectory);

        result.Should().NotBeNull();
        result.FileSize.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task DisposeAsync_ShouldNotThrow()
    {
        DirectoryScanner scanner = new DirectoryScanner();

        Func<ValueTask> act = async () => await scanner.DisposeAsync();

        act.Should().NotThrow();
    }

    [Test]
    public void Dispose_ShouldNotThrow()
    {
        DirectoryScanner scanner = new DirectoryScanner();

        Action act = () => scanner.Dispose();

        act.Should().NotThrow();
    }

    [Test]
    public async Task ScanDirectoryAsync_FileSizeCalculation_ShouldBeAccurate()
    {
        string subDir = Path.Combine(_testDirectory, "sub");
        Directory.CreateDirectory(subDir);

        byte[] content1 = new byte[1024];
        byte[] content2 = new byte[2048];

        File.WriteAllBytes(Path.Combine(_testDirectory, "file1.bin"), content1);
        File.WriteAllBytes(Path.Combine(subDir, "file2.bin"), content2);

        FileEntry result = await _scanner.ScanDirectoryAsync(_testDirectory);

        long expectedSize = content1.Length + content2.Length;
        result.FileSize.Should().Be(expectedSize);
        result.Percentage.Should().Be(100.0);
    }

    [Test]
    public async Task ScanDirectoryAsync_NoBlocking_ShouldCompleteWithinTime()
    {
        for (int i = 0; i < 50; i++)
        {
            string subDir = Path.Combine(_testDirectory, $"dir{i}");
            Directory.CreateDirectory(subDir);

            for (int j = 0; j < 20; j++)
            {
                File.WriteAllText(Path.Combine(subDir, $"file{j}.txt"), $"Content{j}");
            }
        }

        CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        FileEntry result = await _scanner.ScanDirectoryAsync(_testDirectory, cts.Token);

        result.Should().NotBeNull();
    }

    [Test]
    public async Task Dispose_MultipleTimes_ShouldNotThrow()
    {
        DirectoryScanner scanner = new DirectoryScanner();

        scanner.Dispose();

        Action act = () => scanner.Dispose();

        act.Should().NotThrow();

        await scanner.DisposeAsync();
    }

    [Test]
    public async Task DisposeAsync_ThenDispose_ShouldNotThrow()
    {
        DirectoryScanner scanner = new DirectoryScanner();

        await scanner.DisposeAsync();

        Action act = () => scanner.Dispose();

        act.Should().NotThrow();
    }

    [Test]
    public async Task ScanDirectoryAsync_PercentageCalculation_ShouldBeCorrect()
    {
        string subDir = Path.Combine(_testDirectory, "sub");
        Directory.CreateDirectory(subDir);

        byte[] content1 = new byte[100];
        byte[] content2 = new byte[200];

        File.WriteAllBytes(Path.Combine(_testDirectory, "file1.bin"), content1);
        File.WriteAllBytes(Path.Combine(subDir, "file2.bin"), content2);

        FileEntry result = await _scanner.ScanDirectoryAsync(_testDirectory);

        result.Percentage.Should().Be(100.0);

        foreach (FileEntry child in result.SubDirectories)
        {
            if (child.FileType == FileType.File)
            {
                double expectedPercentage = (double)child.FileSize / result.FileSize * 100.0;
                child.Percentage.Should().BeApproximately(expectedPercentage, 0.01);
            }
        }
    }

    [Test]
    public async Task ScanDirectoryAsync_PercentageSum_ShouldBeApproximately100()
    {
        for (int i = 0; i < 10; i++)
        {
            File.WriteAllText(Path.Combine(_testDirectory, $"file{i}.txt"), new string('x', (i + 1) * 100));
        }

        FileEntry result = await _scanner.ScanDirectoryAsync(_testDirectory);

        double totalPercentage = 0.0;

        foreach (FileEntry child in result.SubDirectories)
        {
            if (child.FileType == FileType.File)
            {
                totalPercentage += child.Percentage;
            }
        }

        totalPercentage.Should().BeApproximately(100.0, 0.1);
    }

    private void CreateDeepDirectoryStructure(string basePath, int depth)
    {
        string currentPath = basePath;

        for (int i = 0; i < depth; i++)
        {
            currentPath = Path.Combine(currentPath, $"level{i}");
            Directory.CreateDirectory(currentPath);
            File.WriteAllText(Path.Combine(currentPath, $"file{i}.txt"), $"Content{i}");
        }
    }
}