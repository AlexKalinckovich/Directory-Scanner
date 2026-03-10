using System.Collections.Concurrent;
using System.Diagnostics;
using Directory_Scanner.Core.FileModels;
using Directory_Scanner.Core.ScannerEventArgs;

namespace Directory_Scanner.Core.Core;

public sealed class DirectoryScanner : IDisposable
{
    private const int InitialDelay = 10;
    private const int ContinueDelay = 50;
    private readonly int _maxConcurrency;
    private readonly SemaphoreSlim _semaphore;
    private readonly ConcurrentQueue<DirectoryWorkItem> _directoryQueue;
    private readonly EventDispatcher _eventDispatcher;
    private readonly DirectorySizeCalculator _sizeCalculator;
    private bool _isDisposed;

    public event EventHandler<StartProcessingDirectoryEventArgs>? StartProcessingDirectory
    {
        add => _eventDispatcher.StartProcessingDirectory += value;
        remove => _eventDispatcher.StartProcessingDirectory -= value;
    }

    public event EventHandler<DirectoryProcessedEventArgs>? DirectoryProcessed
    {
        add => _eventDispatcher.DirectoryProcessed += value;
        remove => _eventDispatcher.DirectoryProcessed -= value;
    }

    public event EventHandler<FileProcessedEventArgs>? FileProcessed
    {
        add => _eventDispatcher.FileProcessed += value;
        remove => _eventDispatcher.FileProcessed -= value;
    }

    public event EventHandler<ProcessingCompletedEventArgs>? ProcessingCompleted
    {
        add => _eventDispatcher.ProcessingCompleted += value;
        remove => _eventDispatcher.ProcessingCompleted -= value;
    }

    public DirectoryScanner()
    {
        _maxConcurrency = Environment.ProcessorCount * 2;
        _semaphore = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
        _directoryQueue = new ConcurrentQueue<DirectoryWorkItem>();
        _eventDispatcher = new EventDispatcher();
        _sizeCalculator = new DirectorySizeCalculator();
        _isDisposed = false;
    }

    public DirectoryScanner(int maxConcurrency)
    {
        _maxConcurrency = maxConcurrency;
        _semaphore = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
        _directoryQueue = new ConcurrentQueue<DirectoryWorkItem>();
        _eventDispatcher = new EventDispatcher();
        _sizeCalculator = new DirectorySizeCalculator();
        _isDisposed = false;
    }

    public async Task<FileEntry> ScanDirectoryAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        AssertPathNotNullOrEmpty(rootPath);
        
        DirectoryInfo rootDir = new DirectoryInfo(rootPath);
        
        AssertRootDirectoryExists(rootPath, rootDir);

        FileEntry rootEntry = new FileEntry(rootDir);

        if (!cancellationToken.IsCancellationRequested)
        {
            await ProcessDirectoryQueueAsync(rootDir, rootEntry, cancellationToken);

            _sizeCalculator.CalculateDirectorySizes(rootEntry);

            _eventDispatcher.EnqueueProcessingCompleted(rootEntry);

            await _eventDispatcher.WaitForCompletionAsync();
        }
        else
        {
            _sizeCalculator.CalculateDirectorySizes(rootEntry);
        }

        return rootEntry; 
    }

    private static void AssertPathNotNullOrEmpty(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be null or empty", nameof(path));
        }
    }

    private static void AssertRootDirectoryExists(string path, DirectoryInfo dirInfo)
    {
        if (!dirInfo.Exists)
        {
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        }
    }

    private async Task ProcessDirectoryQueueAsync(DirectoryInfo rootDir, FileEntry rootEntry, CancellationToken cancellationToken)
    {
        DirectoryWorkItem rootWorkItem = new DirectoryWorkItem(rootDir, rootEntry);
        _directoryQueue.Enqueue(rootWorkItem);

        List<Task> workerTasks = CreateWorkerTasks(cancellationToken);

        try
        {
            await Task.WhenAll(workerTasks);
        }
        catch (OperationCanceledException)
        { }
    }

    private List<Task> CreateWorkerTasks(CancellationToken cancellationToken)
    {
        List<Task> workerTasks = new List<Task>(_maxConcurrency);

        for (int i = 0; i < _maxConcurrency; i++)
        {
            Task workerTask = WorkerTaskAsync(cancellationToken);
            
            workerTasks.Add(workerTask);
        }

        return workerTasks;
    }

    private async Task WorkerTaskAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_directoryQueue.TryDequeue(out DirectoryWorkItem workItem))
                {
                    await ProcessWorkItemAsync(workItem, cancellationToken);
                }
                else if (await ShouldWorkerExitAsync(cancellationToken))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ProcessWorkItemAsync(DirectoryWorkItem workItem, CancellationToken cancellationToken)
    {
        try
        {
            await ProcessSingleDirectoryAsync(workItem.DirInfo, workItem.DirEntry, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Worker error: {ex.Message}");
            workItem.DirEntry.FileState = FileState.UnknownError;
        }
    }

    private async Task<bool> ShouldWorkerExitAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return true;
        }

        await Task.Delay(InitialDelay, cancellationToken);

        if (_directoryQueue.TryDequeue(out _))
        {
            return false;
        }

        await Task.Delay(ContinueDelay, cancellationToken);

        if (_directoryQueue.TryDequeue(out _))
        {
            return false;
        }

        await Task.Delay(ContinueDelay, cancellationToken);

        bool isEmpty = !_directoryQueue.TryDequeue(out _);

        return isEmpty;
    }

    private async Task ProcessSingleDirectoryAsync(DirectoryInfo dirInfo, FileEntry dirEntry, CancellationToken cancellationToken)
    {
        _eventDispatcher.EnqueueStartProcessing(dirEntry);

        cancellationToken.ThrowIfCancellationRequested();

        await ProcessFilesAsync(dirInfo, dirEntry, cancellationToken);

        EnqueueSubdirectoriesAsync(dirInfo, dirEntry, cancellationToken);

        _eventDispatcher.EnqueueDirectoryProcessed(dirEntry);
    }

    private async Task ProcessFilesAsync(DirectoryInfo dirInfo, FileEntry dirEntry, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            ProcessFiles(dirInfo, dirEntry, cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private void ProcessFiles(DirectoryInfo dirInfo, FileEntry dirEntry, CancellationToken cancellationToken)
    {
        IEnumerable<FileInfo> files = dirInfo.EnumerateFiles();

        foreach (FileInfo fileInfo in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileEntry fileEntry = new FileEntry(fileInfo);

            dirEntry.AddSubDirectoryChild(fileEntry);

            _eventDispatcher.EnqueueFileProcessed(fileEntry);
        }
    }

    private void EnqueueSubdirectoriesAsync(DirectoryInfo dirInfo, FileEntry dirEntry, CancellationToken cancellationToken)
    {
        try
        {
            IEnumerable<DirectoryInfo> subDirs = dirInfo.EnumerateDirectories();

            foreach (DirectoryInfo subDir in subDirs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                EnqueueSingleSubdirectory(subDir, dirEntry);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            HandleAccessDenied(dirEntry, ex);
        }
        catch (IOException ex)
        {
            HandleIoError(dirEntry, ex);
        }
        catch (Exception ex)
        {
            HandleUnknownError(dirEntry, ex);
        }

    }

    private void EnqueueSingleSubdirectory(DirectoryInfo subDir, FileEntry dirEntry)
    {
        FileEntry subDirEntry = new FileEntry(subDir);

        dirEntry.AddSubDirectoryChild(subDirEntry);

        DirectoryWorkItem workItem = new DirectoryWorkItem(subDir, subDirEntry);

        _directoryQueue.Enqueue(workItem);
    }

    private static void HandleAccessDenied(FileEntry entry, Exception ex)
    {
        Debug.WriteLine("DEBUG!!!!!!!!!!!:  " + ex.Message);
        entry.FileState = FileState.AccessDenied;
    }

    private static void HandleIoError(FileEntry entry, Exception ex)
    {
        Debug.WriteLine("DEBUG!!!!!!!!!!!:  " + ex.Message);
        entry.FileState = FileState.IoError;
    }

    private static void HandleUnknownError(FileEntry entry, Exception ex)
    {
        Debug.WriteLine("DEBUG!!!!!!!!!!!:  " + ex.Message);
        entry.FileState = FileState.UnknownError;
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        await _eventDispatcher.StopAsync();

        _semaphore.Dispose();

        _eventDispatcher.Dispose();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        _semaphore.Dispose();

        _eventDispatcher.Dispose();
    }
}