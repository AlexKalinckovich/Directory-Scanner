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
        
        await ProcessDirectoryAsync(rootDir, rootEntry, cancellationToken).ConfigureAwait(false);
        
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
        Task<long> totalFileSize;
        List<Task> subDirTasks;
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            totalFileSize = FileCalculationInSeparateThread(dirInfo, dirEntry, cancellationToken);
            
            subDirTasks = TryEnqueueSubdirectories(dirInfo, dirEntry, cancellationToken);
            
            subDirTasks.Add(totalFileSize);
        }
        finally
        {
            _semaphore.Release();
        }

        await Task.WhenAll(subDirTasks).ConfigureAwait(false);
        
        long localFileTotal = totalFileSize.Result;

        long subDirTotal = dirEntry.SubDirectories.Sum((FileEntry sd) => sd.FileSize);
        
        dirEntry.FileSize = localFileTotal + subDirTotal;
        
        OnDirectoryProcessed(dirEntry);
    }

    private Task<long> FileCalculationInSeparateThread(DirectoryInfo dirInfo, FileEntry dirEntry, CancellationToken cancellationToken)
    {
        Task<long> result;
        try
        {
            _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            result = Task.Run(() => TryProcessFiles(dirInfo, dirEntry, cancellationToken), cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
        return result;
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

    private List<Task> TryEnqueueSubdirectories(DirectoryInfo dirInfo, FileEntry dirEntry, CancellationToken cancellationToken)
    {
        List<Task> subDirectoriesProcessTasks;
        try
        {
            subDirectoriesProcessTasks = EnqueueSubdirectories(dirInfo, dirEntry, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException e)
        {
            Debug.WriteLine("DEBUG!!!!!!!!!!!:" + e.Message);
            dirEntry.FileState = FileState.AccessDenied;
            subDirectoriesProcessTasks = [];
        }
        catch (IOException e)
        {
            Debug.WriteLine("DEBUG!!!!!!!!!!!:" + e.Message);
            dirEntry.FileState = FileState.IoError;
            subDirectoriesProcessTasks = [];
        }
        catch (Exception e)
        {
            Debug.WriteLine("DEBUG!!!!!!!!!!!:" + e.Message);
            dirEntry.FileState = FileState.UnknownError;
            subDirectoriesProcessTasks = [];
        }

        return subDirectoriesProcessTasks;
    }

    private List<Task> EnqueueSubdirectories(DirectoryInfo dirInfo, FileEntry dirEntry, CancellationToken cancellationToken)
    {
        List<Task> subDirectoriesProcessTasks = new List<Task>();
        foreach (DirectoryInfo subDir in dirInfo.EnumerateDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileEntry subDirEntry = new FileEntry(subDir);
                
            dirEntry.AddSubDirectoryChild(subDirEntry);
                
            AddStartedProcessDirectoryInSeparateThread(cancellationToken, subDirectoriesProcessTasks, subDir, subDirEntry);
        }
        return subDirectoriesProcessTasks;
    }

    private void AddStartedProcessDirectoryInSeparateThread(CancellationToken cancellationToken,
        List<Task> subDirectoriesProcessTasks, DirectoryInfo subDir, FileEntry subDirEntry)
    {
        try
        {
            _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
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