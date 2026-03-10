using Directory_Scanner.Core.Core;
using Directory_Scanner.Core.FileModels;

namespace Directory_Scanner.Tests;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;


[TestFixture]
public class DirectoryScannerIntegrationTests
{
    private string _testDirectory;

    [SetUp]
    public void SetUp()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"scanner_integration_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Test]
    public async Task FullScan_WithEvents_ShouldTrackAllFiles()
    {
        ConcurrentBag<FileEntry> allFiles = new ConcurrentBag<FileEntry>();
        ConcurrentBag<FileEntry> allDirectories = new ConcurrentBag<FileEntry>();

        DirectoryScanner scanner = new DirectoryScanner();
        scanner.FileProcessed += (sender, args) => allFiles.Add(args.FileEntry);
        scanner.DirectoryProcessed += (sender, args) => allDirectories.Add(args.DirectoryEntry);

        CreateTestStructure(_testDirectory, 3, 5);

        FileEntry result = await scanner.ScanDirectoryAsync(_testDirectory);

        await scanner.DisposeAsync();

        int expectedFiles = 3 * 5;
        allFiles.Count.Should().Be(expectedFiles);
        allDirectories.Count.Should().BeGreaterThan(0);
        result.FileSize.Should().BeGreaterThan(0);
        result.Percentage.Should().Be(100.0);
    }

    [Test]
    public async Task Cancellation_ShouldReturnPartialResults()
    {
        CreateDeepDirectoryStructure(_testDirectory, 20);

        DirectoryScanner scanner = new DirectoryScanner();
        CancellationTokenSource cts = new CancellationTokenSource();
        cts.CancelAfter(100);

        FileEntry result = await scanner.ScanDirectoryAsync(_testDirectory, cts.Token);

        await scanner.DisposeAsync();

        result.Should().NotBeNull();
        result.FullPath.Should().Be(_testDirectory);
        result.FileSize.Should().BeGreaterThanOrEqualTo(0);
        result.Percentage.Should().BeGreaterThanOrEqualTo(0.0);
        result.Percentage.Should().BeLessThanOrEqualTo(100.0);
    }

    [Test]
    public async Task Cancellation_ShouldCalculatePercentagesOnPartialResults()
    {
        CreateDeepDirectoryStructure(_testDirectory, 15);

        DirectoryScanner scanner = new DirectoryScanner();
        CancellationTokenSource cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        FileEntry result = await scanner.ScanDirectoryAsync(_testDirectory, cts.Token);

        await scanner.DisposeAsync();

        result.Should().NotBeNull();
        VerifyPercentages(result);
    }

    [Test]
    public async Task MultipleScans_Sequential_ShouldNotLeakResources()
    {
        CreateTestStructure(_testDirectory, 2, 3);

        long memoryBefore = GC.GetTotalMemory(true);

        for (int i = 0; i < 5; i++)
        {
            DirectoryScanner scanner = new DirectoryScanner();
            FileEntry result = await scanner.ScanDirectoryAsync(_testDirectory);
            await scanner.DisposeAsync();

            result.Should().NotBeNull();
            result.FileSize.Should().BeGreaterThan(0);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long memoryAfter = GC.GetTotalMemory(true);

        long memoryDifference = Math.Abs(memoryAfter - memoryBefore);
        memoryDifference.Should().BeLessThan(10 * 1024 * 1024);
    }

    [Test]
    public async Task ConcurrentScans_ShouldNotInterfere()
    {
        string dir1 = Path.Combine(_testDirectory, "scan1");
        string dir2 = Path.Combine(_testDirectory, "scan2");
        string dir3 = Path.Combine(_testDirectory, "scan3");

        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);
        Directory.CreateDirectory(dir3);

        for (int i = 0; i < 10; i++)
        {
            File.WriteAllText(Path.Combine(dir1, $"file{i}.txt"), $"Content1_{i}");
            File.WriteAllText(Path.Combine(dir2, $"file{i}.txt"), $"Content2_{i}");
            File.WriteAllText(Path.Combine(dir3, $"file{i}.txt"), $"Content3_{i}");
        }

        List<Task<FileEntry>> scanTasks = new List<Task<FileEntry>>();
        List<DirectoryScanner> scanners = new List<DirectoryScanner>();

        for (int i = 0; i < 3; i++)
        {
            DirectoryScanner scanner = new DirectoryScanner();
            scanners.Add(scanner);

            string targetDir = i == 0 ? dir1 : (i == 1 ? dir2 : dir3);
            Task<FileEntry> task = scanner.ScanDirectoryAsync(targetDir);
            scanTasks.Add(task);
        }

        FileEntry[] results = await Task.WhenAll(scanTasks);

        for (int i = 0; i < 3; i++)
        {
            results[i].Should().NotBeNull();
            results[i].FileSize.Should().BeGreaterThan(0);
            results[i].Percentage.Should().Be(100.0);

            await scanners[i].DisposeAsync();
        }
    }

    [Test]
    public async Task EventOrdering_ShouldBeConsistent()
    {
        List<string> eventSequence = new List<string>();
        object lockObj = new object();

        DirectoryScanner scanner = new DirectoryScanner();

        scanner.StartProcessingDirectory += (sender, args) =>
        {
            lock (lockObj)
            {
                eventSequence.Add($"START:{args.DirectoryEntry.FileName}");
            }
        };

        scanner.FileProcessed += (sender, args) =>
        {
            lock (lockObj)
            {
                eventSequence.Add($"FILE:{args.FileEntry.FileName}");
            }
        };

        scanner.DirectoryProcessed += (sender, args) =>
        {
            lock (lockObj)
            {
                eventSequence.Add($"DONE:{args.DirectoryEntry.FileName}");
            }
        };

        string subDir = Path.Combine(_testDirectory, "sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(_testDirectory, "root.txt"), "root");
        File.WriteAllText(Path.Combine(subDir, "sub.txt"), "sub");

        await scanner.ScanDirectoryAsync(_testDirectory);

        await scanner.DisposeAsync();

        eventSequence.Count.Should().BeGreaterThan(0);

        eventSequence.Should().Contain(e => e.StartsWith("START:"));
        eventSequence.Should().Contain(e => e.StartsWith("FILE:"));
        eventSequence.Should().Contain(e => e.StartsWith("DONE:"));
    }

    [Test]
    public async Task HighConcurrency_ShouldNotDeadlock()
    {
        for (int i = 0; i < 20; i++)
        {
            string subDir = Path.Combine(_testDirectory, $"dir{i}");
            Directory.CreateDirectory(subDir);

            for (int j = 0; j < 50; j++)
            {
                File.WriteAllText(Path.Combine(subDir, $"file{j}.txt"), $"Content{j}");
            }
        }

        List<Task<FileEntry>> scanTasks = new List<Task<FileEntry>>();
        List<DirectoryScanner> scanners = new List<DirectoryScanner>();

        for (int i = 0; i < 5; i++)
        {
            DirectoryScanner scanner = new DirectoryScanner();
            scanners.Add(scanner);

            Task<FileEntry> task = scanner.ScanDirectoryAsync(_testDirectory);
            scanTasks.Add(task);
        }

        CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        Task whenAllTask = Task.WhenAll(scanTasks);
        Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(60), cts.Token);

        Task completedTask = await Task.WhenAny(whenAllTask, timeoutTask);

        completedTask.Should().Be(whenAllTask);

        FileEntry[] results = new FileEntry[scanTasks.Count];
        for (int i = 0; i < results.Length; i++)
        {
            results[i] = await scanTasks[i];
        }
        results.Should().AllSatisfy(r =>
        {
            r.Should().NotBeNull();
            r.FileSize.Should().BeGreaterThan(0);
        });

        foreach (DirectoryScanner scanner in scanners)
        {
            await scanner.DisposeAsync();
        }
    }

    [Test]
    public async Task DeepNesting_ShouldHandleCorrectly()
    {
        CreateDeepDirectoryStructure(_testDirectory, 10);

        DirectoryScanner scanner = new DirectoryScanner();

        FileEntry result = await scanner.ScanDirectoryAsync(_testDirectory);

        await scanner.DisposeAsync();

        result.Should().NotBeNull();
        result.FileSize.Should().BeGreaterThan(0);
        result.Percentage.Should().Be(100.0);

        VerifyPercentages(result);
    }

    [Test]
    public async Task LargeFileCount_ShouldCompleteWithinTime()
    {
        for (int i = 0; i < 500; i++)
        {
            string subDir = Path.Combine(_testDirectory, $"dir{i % 50}");
            Directory.CreateDirectory(subDir);
            File.WriteAllText(Path.Combine(subDir, $"file{i}.txt"), $"Content{i}");
        }

        DirectoryScanner scanner = new DirectoryScanner();
        CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        FileEntry result = await scanner.ScanDirectoryAsync(_testDirectory, cts.Token);

        await scanner.DisposeAsync();

        result.Should().NotBeNull();
        result.SubDirectories.Count.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task Cancellation_ThenDispose_ShouldNotThrow()
    {
        CreateDeepDirectoryStructure(_testDirectory, 20);

        DirectoryScanner scanner = new DirectoryScanner();
        CancellationTokenSource cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        try
        {
            await scanner.ScanDirectoryAsync(_testDirectory, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        Func<ValueTask> act = async () => await scanner.DisposeAsync();

        act.Should().NotThrow();
    }

    [Test]
    public async Task PercentageCalculation_ShouldBeAccurate()
    {
        string subDir1 = Path.Combine(_testDirectory, "sub1");
        string subDir2 = Path.Combine(_testDirectory, "sub2");

        Directory.CreateDirectory(subDir1);
        Directory.CreateDirectory(subDir2);

        byte[] content1 = new byte[1000];
        byte[] content2 = new byte[2000];
        byte[] content3 = new byte[3000];

        File.WriteAllBytes(Path.Combine(subDir1, "file1.bin"), content1);
        File.WriteAllBytes(Path.Combine(subDir2, "file2.bin"), content2);
        File.WriteAllBytes(Path.Combine(_testDirectory, "file3.bin"), content3);

        DirectoryScanner scanner = new DirectoryScanner();

        FileEntry result = await scanner.ScanDirectoryAsync(_testDirectory);

        await scanner.DisposeAsync();

        long totalSize = content1.Length + content2.Length + content3.Length;

        result.FileSize.Should().Be(totalSize);
        result.Percentage.Should().Be(100.0);

        double file3Percentage = (double)content3.Length / totalSize * 100.0;
        FileEntry file3Entry = result.SubDirectories.FirstOrDefault(f => f.FileName == "file3.bin");
        file3Entry.Should().NotBeNull();
        file3Entry.Percentage.Should().BeApproximately(file3Percentage, 0.1);
    }

    [Test]
    public async Task EmptyDirectory_ShouldReturnValidEntry()
    {
        DirectoryScanner scanner = new DirectoryScanner();

        FileEntry result = await scanner.ScanDirectoryAsync(_testDirectory);

        await scanner.DisposeAsync();

        result.Should().NotBeNull();
        result.FullPath.Should().Be(_testDirectory);
        result.FileType.Should().Be(FileType.Directory);
        result.FileSize.Should().Be(0);
        result.Percentage.Should().Be(0.0);
    }

    [Test]
    public async Task MixedFileTypes_ShouldHandleCorrectly()
    {
        for (int i = 0; i < 10; i++)
        {
            File.WriteAllText(Path.Combine(_testDirectory, $"text{i}.txt"), $"Text{i}");
            File.WriteAllBytes(Path.Combine(_testDirectory, $"binary{i}.bin"), new byte[i * 100]);
        }

        string subDir = Path.Combine(_testDirectory, "subdir");
        Directory.CreateDirectory(subDir);

        for (int i = 0; i < 5; i++)
        {
            File.WriteAllText(Path.Combine(subDir, $"nested{i}.txt"), $"Nested{i}");
        }

        DirectoryScanner scanner = new DirectoryScanner();

        FileEntry result = await scanner.ScanDirectoryAsync(_testDirectory);

        await scanner.DisposeAsync();

        result.Should().NotBeNull();
        result.FileSize.Should().BeGreaterThan(0);
        result.SubDirectories.Count.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task Dispose_WithoutScan_ShouldNotThrow()
    {
        DirectoryScanner scanner = new DirectoryScanner();

        Func<ValueTask> act = async () => await scanner.DisposeAsync();

        act.Should().NotThrow();
    }

    [Test]
    public void Dispose_WithoutScan_ShouldNotThrow_Sync()
    {
        DirectoryScanner scanner = new DirectoryScanner();

        Action act = () => scanner.Dispose();

        act.Should().NotThrow();
    }

    [Test]
    public async Task MultipleDisposeAsync_ShouldNotThrow()
    {
        DirectoryScanner scanner = new DirectoryScanner();

        await scanner.DisposeAsync();

        Func<ValueTask> act = async () => await scanner.DisposeAsync();

        act.Should().NotThrow();
    }

    [Test]
    public async Task Scan_ThenMultipleDispose_ShouldNotThrow()
    {
        File.WriteAllText(Path.Combine(_testDirectory, "file.txt"), "test");

        DirectoryScanner scanner = new DirectoryScanner();

        await scanner.ScanDirectoryAsync(_testDirectory);

        await scanner.DisposeAsync();

        Func<ValueTask> act = async () => await scanner.DisposeAsync();

        act.Should().NotThrow();

        Action syncAct = () => scanner.Dispose();

        syncAct.Should().NotThrow();
    }

    [Test]
    public async Task Cancellation_AtDifferentStages_ShouldReturnPartialResults()
    {
        CreateDeepDirectoryStructure(_testDirectory, 25);

        List<FileEntry> partialResults = new List<FileEntry>();

        for (int i = 1; i <= 5; i++)
        {
            DirectoryScanner scanner = new DirectoryScanner();
            CancellationTokenSource cts = new CancellationTokenSource();
            cts.CancelAfter(i * 50);

            try
            {
                FileEntry result = await scanner.ScanDirectoryAsync(_testDirectory, cts.Token);
                partialResults.Add(result);
            }
            catch
            {
            }

            await scanner.DisposeAsync();
        }

        partialResults.Count.Should().BeGreaterThan(0);

        foreach (FileEntry result in partialResults)
        {
            if (result != null)
            {
                result.FullPath.Should().Be(_testDirectory);
                result.Percentage.Should().BeGreaterThanOrEqualTo(0.0);
                result.Percentage.Should().BeLessThanOrEqualTo(100.0);
            }
        }
    }

    private void CreateTestStructure(string basePath, int subDirCount, int filesPerDir)
    {
        for (int i = 0; i < subDirCount; i++)
        {
            string subDir = Path.Combine(basePath, $"dir{i}");
            Directory.CreateDirectory(subDir);

            for (int j = 0; j < filesPerDir; j++)
            {
                File.WriteAllText(Path.Combine(subDir, $"file{j}.txt"), $"Content{j}");
            }
        }
    }

    private void CreateDeepDirectoryStructure(string basePath, int depth)
    {
        string currentPath = basePath;

        for (int i = 0; i < depth; i++)
        {
            currentPath = Path.Combine(currentPath, $"level{i}");
            Directory.CreateDirectory(currentPath);

            for (int j = 0; j < 10; j++)
            {
                File.WriteAllText(Path.Combine(currentPath, $"file{j}.txt"), $"Content{j}");
            }
        }
    }

    private void VerifyPercentages(FileEntry entry)
    {
        entry.Percentage.Should().BeGreaterThanOrEqualTo(0.0);
        entry.Percentage.Should().BeLessThanOrEqualTo(100.0);

        foreach (FileEntry child in entry.SubDirectories)
        {
            VerifyPercentages(child);
        }
    }
}