using System.Collections.Concurrent;
using System.Diagnostics;
using Directory_Scanner.Core.FileModels;
using Directory_Scanner.Core.ScannerEventArgs;

namespace Directory_Scanner.Core.Core;



public sealed class DirectoryScanner
{
    private const int InitialDelay = 10;
    private const int ContinueDelay = 50;
    private readonly int _maxConcurrency = Environment.ProcessorCount * 2;
    private readonly SemaphoreSlim _semaphore;
    private readonly ConcurrentQueue<DirectoryWorkItem> _directoryQueue;
    
    public event EventHandler<StartProcessingDirectoryEventArgs>? StartProcessingDirectory;
    public event EventHandler<DirectoryProcessedEventArgs>? DirectoryProcessed;
    public event EventHandler<FileProcessedEventArgs>? FileProcessed;
    public event EventHandler<ProcessingCompletedEventArgs>? ProcessingCompleted;

    public DirectoryScanner()
    {
        _semaphore = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
        _directoryQueue = new ConcurrentQueue<DirectoryWorkItem>();
    }

    public async Task<FileEntry> ScanDirectoryAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        AssertPathNotNullOrEmpty(rootPath);
        
        DirectoryInfo rootDir = new DirectoryInfo(rootPath);
        AssertRootDirectoryExists(rootPath, rootDir);

        FileEntry rootEntry = new FileEntry(rootDir);
        
        await ProcessDirectoryQueueAsync(rootDir, rootEntry, cancellationToken);
        
        OnProcessingCompleted(rootEntry);
        
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
        _directoryQueue.Enqueue(new DirectoryWorkItem(rootDir, rootEntry));
        
        List<Task> workerTasks = new List<Task>();
        for (int i = 0; i < _maxConcurrency; i++)
        {
            workerTasks.Add(WorkerTaskAsync(cancellationToken));
        }
        
        await Task.WhenAll(workerTasks).ConfigureAwait(false);
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
        await Task.Delay(InitialDelay, cancellationToken).ConfigureAwait(false);
        
        if (_directoryQueue.TryDequeue(out _))
        {
            return false;
        }
        
        await Task.Delay(ContinueDelay, cancellationToken).ConfigureAwait(false);
        
        if (_directoryQueue.TryDequeue(out _))
        {
            return false;
        }
        
        await Task.Delay(ContinueDelay, cancellationToken).ConfigureAwait(false);
        
        return !_directoryQueue.TryDequeue(out _);
    }

    private async Task ProcessSingleDirectoryAsync(DirectoryInfo dirInfo, FileEntry dirEntry, CancellationToken cancellationToken)
    {
        OnStartProcessingDirectory(dirEntry);
        
        cancellationToken.ThrowIfCancellationRequested();
        
        long totalFileSize = await FileCalculationInSeparateThread(dirInfo, dirEntry, cancellationToken);
        
        await EnqueueSubdirectoriesAsync(dirInfo, dirEntry, cancellationToken);
        
        long subDirTotal = dirEntry.SubDirectories.Sum(sd => sd.FileSize);
        dirEntry.FileSize = totalFileSize + subDirTotal;
        
        OnDirectoryProcessed(dirEntry);
    }

    private async ValueTask<long> FileCalculationInSeparateThread(DirectoryInfo dirInfo, FileEntry dirEntry, CancellationToken cancellationToken)
    {
        try
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            
            return await Task.Run(() => TryProcessFiles(dirInfo, dirEntry, cancellationToken), cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private long TryProcessFiles(DirectoryInfo dirInfo, FileEntry dirEntry, CancellationToken cancellationToken)
    {
        long totalFileSize;
        try
        {
            totalFileSize = ProcessFiles(dirInfo, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException e)
        {
            Debug.WriteLine("DEBUG!!!!!!!!!!!: " + e.Message);
            dirEntry.FileState = FileState.AccessDenied;
            totalFileSize = 0;
        }
        catch (IOException e)
        {
            Debug.WriteLine("DEBUG!!!!!!!!!!!: " + e.Message);
            dirEntry.FileState = FileState.IoError;
            totalFileSize = 0;
        }
        catch (Exception e)
        {
            Debug.WriteLine("DEBUG!!!!!!!!!!!: " + e.Message);
            dirEntry.FileState = FileState.UnknownError;
            totalFileSize = 0;
        }
        
        return totalFileSize;
    }

    private long ProcessFiles(DirectoryInfo dirInfo, CancellationToken cancellationToken)
    {
        long totalFileSize = 0;
        foreach (FileInfo fileInfo in dirInfo.EnumerateFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileEntry fileEntry = new FileEntry(fileInfo);

            OnFileProcessed(fileEntry);
            
            totalFileSize += fileInfo.Length;
        }
        return totalFileSize;
    }

    private async Task EnqueueSubdirectoriesAsync(DirectoryInfo dirInfo, FileEntry dirEntry, CancellationToken cancellationToken)
    {
        try
        {
            foreach (DirectoryInfo subDir in dirInfo.EnumerateDirectories())
            {
                cancellationToken.ThrowIfCancellationRequested();

                FileEntry subDirEntry = new FileEntry(subDir);
                
                dirEntry.AddSubDirectoryChild(subDirEntry);
                
                _directoryQueue.Enqueue(new DirectoryWorkItem(subDir, subDirEntry));
            }
        }
        catch (UnauthorizedAccessException e)
        {
            Debug.WriteLine("DEBUG!!!!!!!!!!!: " + e.Message);
            dirEntry.FileState = FileState.AccessDenied;
        }
        catch (IOException e)
        {
            Debug.WriteLine("DEBUG!!!!!!!!!!!: " + e.Message);
            dirEntry.FileState = FileState.IoError;
        }
        catch (Exception e)
        {
            Debug.WriteLine("DEBUG!!!!!!!!!!!: " + e.Message);
            dirEntry.FileState = FileState.UnknownError;
        }
    }

    private void OnStartProcessingDirectory(FileEntry dirEntry)
    {
        StartProcessingDirectory?.Invoke(this, new StartProcessingDirectoryEventArgs(dirEntry));
    }

    private void OnDirectoryProcessed(FileEntry dirEntry)
    {
        DirectoryProcessed?.Invoke(this, new DirectoryProcessedEventArgs(dirEntry));
    }

    private void OnFileProcessed(FileEntry fileEntry)
    {
        FileProcessed?.Invoke(this, new FileProcessedEventArgs(fileEntry));
    }

    private void OnProcessingCompleted(FileEntry rootEntry)
    {
        ProcessingCompleted?.Invoke(this, new ProcessingCompletedEventArgs(rootEntry));
    }
}