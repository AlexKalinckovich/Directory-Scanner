using System.Collections.Concurrent;
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

        FileEntry rootEntry = new FileEntry(FileType.Directory, rootDir.Name, rootDir.FullName);
        
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
        long time = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        List<Task> subDirTasks;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            subDirTasks = TryEnqueueSubdirectories(dirInfo, dirEntry, cancellationToken);
            
            subDirTasks.Add(Task.Run(() => TryProcessFiles(dirInfo, dirEntry, cancellationToken), cancellationToken));
        }
        finally
        {
            long timeRelease = DateTimeOffset.Now.ToUnixTimeMilliseconds() - time;
            if (Math.Abs(timeRelease) > 100)
            {
                Console.WriteLine($"Processing directory {dirInfo.FullName} is too long to be processed: {timeRelease}");
            }
            _semaphore.Release();
        }

        await Task.WhenAll(subDirTasks).ConfigureAwait(false);
        
        UpdateTotalDirectorySize(dirEntry);
        
        OnDirectoryProcessed(dirEntry);
    }

    private static void UpdateTotalDirectorySize(FileEntry dirEntry)
    {
        foreach (FileEntry child in dirEntry.SubDirectories)
        {
            dirEntry.FileSize += child.FileSize;
        }
    }

    private void TryProcessFiles(DirectoryInfo dirInfo, FileEntry dirEntry, CancellationToken cancellationToken)
    {
        try
        {
            ProcessFiles(dirInfo, dirEntry, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            dirEntry.State = FileState.AccessDenied;
        }
    }

    private void ProcessFiles(DirectoryInfo dirInfo, FileEntry dirEntry, CancellationToken cancellationToken)
    {
        long totalFileSize = 0;
        foreach (FileInfo file in dirInfo.EnumerateFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileEntry fileEntry = new FileEntry(FileType.File, file.Name, file.FullName, file.Length)
            {
                State = FileState.Ok
            };

            OnFileProcessed(fileEntry);
            
            totalFileSize += file.Length;
        }
        dirEntry.FileSize += totalFileSize;
    }

    private List<Task> TryEnqueueSubdirectories(DirectoryInfo dirInfo, FileEntry dirEntry, CancellationToken cancellationToken)
    {
        List<Task> subDirectoriesProcessTasks;
        try
        {
            subDirectoriesProcessTasks = EnqueueSubdirectories(dirInfo, dirEntry, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            dirEntry.State = FileState.AccessDenied;
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
            
            FileEntry subDirEntry = new FileEntry(FileType.Directory, subDir.Name, subDir.FullName)
            {
                State = FileState.Ok
            };
                
            dirEntry.AddSubDirectoryChild(subDirEntry);
                
            subDirectoriesProcessTasks.Add(Task.Run(() => ProcessDirectoryAsync(subDir, subDirEntry, cancellationToken), cancellationToken));
        }
        return subDirectoriesProcessTasks;
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