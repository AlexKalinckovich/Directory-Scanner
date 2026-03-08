using System.Collections.Concurrent;
using System.Diagnostics;
using Directory_Scanner.Core.FileModels;
using Directory_Scanner.Core.ScannerEventArgs;

namespace Directory_Scanner.Core.Core;

public sealed class DirectoryScanner
{
    private readonly int _maxConcurrency = Environment.ProcessorCount * 2;
    private readonly SemaphoreSlim _semaphore;

    public event EventHandler<StartProcessingDirectoryEventArgs>? StartProcessingDirectory;
    public event EventHandler<DirectoryProcessedEventArgs>? DirectoryProcessed;
    public event EventHandler<FileProcessedEventArgs>? FileProcessed;
    public event EventHandler<ProcessingCompletedEventArgs>? ProcessingCompleted;

    public DirectoryScanner()
    {
        _semaphore = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
    }

    public async Task<FileEntry> ScanDirectoryAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        AssertPathNotNullOrEmpty(rootPath);
        
        DirectoryInfo rootDir = new DirectoryInfo(rootPath);
        
        AssertRootDirectoryExists(rootPath, rootDir);

        FileEntry rootEntry = new FileEntry(rootDir);
        
        await ProcessDirectoryAsync(rootDir, rootEntry, cancellationToken);
        
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

    private async Task ProcessDirectoryAsync(DirectoryInfo dirInfo, FileEntry dirEntry, CancellationToken cancellationToken)
    {
        OnStartProcessingDirectory(dirEntry);
        
        cancellationToken.ThrowIfCancellationRequested();
            
        long totalFileSize = await FileCalculationInSeparateThread(dirInfo, dirEntry, cancellationToken);
            
        List<Task> subDirTasks = await TryEnqueueSubdirectories(dirInfo, dirEntry, cancellationToken);
        
        await Task.WhenAll(subDirTasks).ConfigureAwait(false);
        
        long subDirTotal = dirEntry.SubDirectories.Sum((FileEntry sd) => sd.FileSize);
        
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
            Debug.WriteLine("DEBUG!!!!!!!!!!!:" + e.Message);
            dirEntry.FileState = FileState.AccessDenied;
            totalFileSize = 0;
        }
        catch (IOException e)
        {
            Debug.WriteLine("DEBUG!!!!!!!!!!!:" + e.Message);
            dirEntry.FileState = FileState.IoError;
            totalFileSize = 0;
        }
        catch (Exception e)
        {
            Debug.WriteLine("DEBUG!!!!!!!!!!!:" + e.Message);
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

    private async ValueTask<List<Task>> TryEnqueueSubdirectories(DirectoryInfo dirInfo, FileEntry dirEntry, CancellationToken cancellationToken)
    {
        List<Task> subDirectoriesProcessTasks = [];
        try
        {
            subDirectoriesProcessTasks = await EnqueueSubdirectories(dirInfo, dirEntry, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException e)
        {
            Debug.WriteLine("DEBUG!!!!!!!!!!!:" + e.Message);
            dirEntry.FileState = FileState.AccessDenied;
        }
        catch (IOException e)
        {
            Debug.WriteLine("DEBUG!!!!!!!!!!!:" + e.Message);
            dirEntry.FileState = FileState.IoError;
        }
        catch (Exception e)
        {
            Debug.WriteLine("DEBUG!!!!!!!!!!!:" + e.Message);
            dirEntry.FileState = FileState.UnknownError;
        }

        return subDirectoriesProcessTasks;
    }

    private async ValueTask<List<Task>> EnqueueSubdirectories(DirectoryInfo dirInfo, FileEntry dirEntry, CancellationToken cancellationToken)
    {
        List<Task> subDirectoriesProcessTasks = new List<Task>();
        foreach (DirectoryInfo subDir in dirInfo.EnumerateDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileEntry subDirEntry = new FileEntry(subDir);
                
            dirEntry.AddSubDirectoryChild(subDirEntry);
                
            await AddStartedProcessDirectoryInSeparateThread(cancellationToken, subDirectoriesProcessTasks, subDir, subDirEntry);
        }
        return subDirectoriesProcessTasks;
    }

    private async Task AddStartedProcessDirectoryInSeparateThread(CancellationToken cancellationToken,
        List<Task> subDirectoriesProcessTasks, DirectoryInfo subDir, FileEntry subDirEntry)
    {
        try
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            subDirectoriesProcessTasks.Add(Task.Run(
                () => ProcessDirectoryAsync(subDir, subDirEntry, cancellationToken), cancellationToken)
            );
        }
        finally
        {
            _semaphore.Release();   
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