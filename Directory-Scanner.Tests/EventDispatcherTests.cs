using Directory_Scanner.Core.Core;
using Directory_Scanner.Core.FileModels;
using FluentAssertions;
using NUnit.Framework;

namespace Directory_Scanner.Tests;

[TestFixture]
public class EventDispatcherTests
{
    private EventDispatcher _dispatcher;
    private string _testDirectory;

    [SetUp]
    public void SetUp()
    {
        _dispatcher = new EventDispatcher();
        _testDirectory = Path.Combine(Path.GetTempPath(), $"event_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dispatcher.StopAsync();
        _dispatcher.Dispose();

        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Test]
    public async Task EnqueueFileProcessed_ShouldInvokeEvent()
    {
        List<FileEntry> receivedEntries = new List<FileEntry>();
        _dispatcher.FileProcessed += (sender, args) => receivedEntries.Add(args.FileEntry);

        string filePath = Path.Combine(_testDirectory, "file.txt");
        await File.WriteAllTextAsync(filePath, "Test content");
        FileInfo fileInfo = new FileInfo(filePath);
        FileEntry entry = new FileEntry(fileInfo);

        _dispatcher.EnqueueFileProcessed(entry);

        await Task.Delay(50);

        receivedEntries.Count.Should().Be(1);
        receivedEntries[0].FullPath.Should().Be(entry.FullPath);
    }

    [Test]
    public async Task EnqueueStartProcessing_ShouldInvokeEvent()
    {
        List<FileEntry> receivedEntries = new List<FileEntry>();
        _dispatcher.StartProcessingDirectory += (sender, args) => receivedEntries.Add(args.DirectoryEntry);

        DirectoryInfo dirInfo = new DirectoryInfo(_testDirectory);
        FileEntry entry = new FileEntry(dirInfo);

        _dispatcher.EnqueueStartProcessing(entry);

        await Task.Delay(50);

        receivedEntries.Count.Should().Be(1);
    }

    [Test]
    public async Task EnqueueDirectoryProcessed_ShouldInvokeEvent()
    {
        List<FileEntry> receivedEntries = new List<FileEntry>();
        _dispatcher.DirectoryProcessed += (sender, args) => receivedEntries.Add(args.DirectoryEntry);

        DirectoryInfo dirInfo = new DirectoryInfo(_testDirectory);
        FileEntry entry = new FileEntry(dirInfo);

        _dispatcher.EnqueueDirectoryProcessed(entry);

        await Task.Delay(50);

        receivedEntries.Count.Should().Be(1);
    }

    [Test]
    public async Task EnqueueProcessingCompleted_ShouldInvokeEvent()
    {
        List<FileEntry> receivedEntries = new List<FileEntry>();
        _dispatcher.ProcessingCompleted += (sender, args) => receivedEntries.Add(args.FileEntry);

        DirectoryInfo dirInfo = new DirectoryInfo(_testDirectory);
        FileEntry entry = new FileEntry(dirInfo);

        _dispatcher.EnqueueProcessingCompleted(entry);

        await Task.Delay(50);

        receivedEntries.Count.Should().Be(1);
    }

    [Test]
    public async Task MultipleEvents_ShouldProcessAllInOrder()
    {
        List<string> eventOrder = new List<string>();

        _dispatcher.FileProcessed += (sender, args) => eventOrder.Add("File");
        _dispatcher.StartProcessingDirectory += (sender, args) => eventOrder.Add("Start");
        _dispatcher.DirectoryProcessed += (sender, args) => eventOrder.Add("Directory");

        DirectoryInfo dirInfo = new DirectoryInfo(_testDirectory);
        FileEntry dirEntry = new FileEntry(dirInfo);

        string filePath = Path.Combine(_testDirectory, "file.txt");
        await File.WriteAllTextAsync(filePath, "Test");
        FileInfo fileInfo = new FileInfo(filePath);
        FileEntry fileEntry = new FileEntry(fileInfo);

        _dispatcher.EnqueueStartProcessing(dirEntry);
        _dispatcher.EnqueueFileProcessed(fileEntry);
        _dispatcher.EnqueueDirectoryProcessed(dirEntry);

        await Task.Delay(100);

        eventOrder.Count.Should().Be(3);
        eventOrder[0].Should().Be("Start");
        eventOrder[1].Should().Be("File");
        eventOrder[2].Should().Be("Directory");
    }

    [Test]
    public async Task StopAsync_ShouldStopProcessing()
    {
        int eventCount = 0;
        _dispatcher.FileProcessed += (sender, args) => Interlocked.Increment(ref eventCount);

        for (int i = 0; i < 10; i++)
        {
            string filePath = Path.Combine(_testDirectory, $"file{i}.txt");
            File.WriteAllText(filePath, $"Content{i}");
            FileInfo fileInfo = new FileInfo(filePath);
            FileEntry entry = new FileEntry(fileInfo);
            _dispatcher.EnqueueFileProcessed(entry);
        }

        await _dispatcher.StopAsync();

        int finalCount = eventCount;

        await Task.Delay(100);

        eventCount.Should().Be(finalCount);
    }

    [Test]
    public async Task HighVolumeEvents_ShouldNotDeadlock()
    {
        int processedCount = 0;
        _dispatcher.FileProcessed += (sender, args) => Interlocked.Increment(ref processedCount);

        List<Task> enqueueTasks = new List<Task>();

        for (int i = 0; i < 1000; i++)
        {
            int index = i;
            Task task = Task.Run(() =>
            {
                string filePath = Path.Combine(_testDirectory, $"file{index}.txt");
                File.WriteAllText(filePath, $"Content{index}");
                FileInfo fileInfo = new FileInfo(filePath);
                FileEntry entry = new FileEntry(fileInfo);
                _dispatcher.EnqueueFileProcessed(entry);
                return Task.CompletedTask;
            });
            enqueueTasks.Add(task);
        }

        await Task.WhenAll(enqueueTasks);
        await Task.Delay(500);

        processedCount.Should().Be(1000);
    }

    [Test]
    public void Dispose_ShouldNotThrow()
    {
        EventDispatcher dispatcher = new EventDispatcher();

        Action act = () => dispatcher.Dispose();

        act.Should().NotThrow();
    }

    [Test]
    public async Task Dispose_AfterStopAsync_ShouldNotThrow()
    {
        EventDispatcher dispatcher = new EventDispatcher();

        await dispatcher.StopAsync();

        Action act = () => dispatcher.Dispose();

        act.Should().NotThrow();
    }

    [Test]
    public async Task MultipleDispose_ShouldNotThrow()
    {
        EventDispatcher dispatcher = new EventDispatcher();

        dispatcher.Dispose();

        Action act = () => dispatcher.Dispose();

        act.Should().NotThrow();

        await dispatcher.StopAsync();
    }
}