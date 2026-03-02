using Directory_Scanner.Core.FileModels;
using Directory_Scanner.Core.ScannerEventArgs;

namespace Directory_Scanner.Core.Core;

public sealed class DirectoryScannerV3
{
    private readonly int _maxConcurrency = Environment.ProcessorCount * 2;
    private readonly SemaphoreSlim _semaphore;

    public event EventHandler<StartProcessingDirectoryEventArgs>? StartProcessingDirectory;
    public event EventHandler<DirectoryProcessedEventArgs>? DirectoryProcessed;
    public event EventHandler<FileProcessedEventArgs>? FileProcessed;
    public event EventHandler<ProcessingCompletedEventArgs>? ProcessingCompleted;

    public DirectoryScannerV3()
    {
        _semaphore = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
    }

    public async Task<FileEntry> ScanDirectoryAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Path cannot be null or empty", nameof(rootPath));

        DirectoryInfo rootDir = new DirectoryInfo(rootPath);
        if (!rootDir.Exists)
            throw new DirectoryNotFoundException($"Directory not found: {rootPath}");

        FileEntry rootEntry = new FileEntry(rootDir);
        await ProcessDirectoryAsync(rootDir, rootEntry, cancellationToken).ConfigureAwait(false);
        OnProcessingCompleted(rootEntry);
        return rootEntry;
    }

    private async Task ProcessDirectoryAsync(DirectoryInfo dirInfo, FileEntry dirEntry, CancellationToken cancellationToken)
    {
        OnStartProcessingDirectory(dirEntry);

        long localFileSize = await CalculateSumOfFileSizes(dirInfo, dirEntry, cancellationToken);

        List<Task> subDirTasks = StartProcessingSubDirs(dirInfo, dirEntry, cancellationToken);

        await Task.WhenAll(subDirTasks).ConfigureAwait(false);

        long subDirTotal = dirEntry.SubDirectories.Sum((FileEntry sub) => sub.FileSize);
        
        dirEntry.FileSize = localFileSize + subDirTotal;

        OnDirectoryProcessed(dirEntry);
    }

    private List<Task> StartProcessingSubDirs(DirectoryInfo dirInfo, FileEntry dirEntry, CancellationToken cancellationToken)
    {
        List<Task> subDirTasks = new List<Task>();
        try
        {
            foreach (DirectoryInfo subDir in dirInfo.EnumerateDirectories())
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                FileEntry subDirEntry = new FileEntry(subDir);
                
                dirEntry.AddSubDirectoryChild(subDirEntry);
                
                subDirTasks.Add(ProcessDirectoryAsync(subDir, subDirEntry, cancellationToken));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            dirEntry.FileState = FileState.AccessDenied;
        }
        catch (IOException)
        {
            dirEntry.FileState = FileState.IoError;
        }
        catch (Exception)
        {
            dirEntry.FileState = FileState.UnknownError;
        }
        
        return subDirTasks;
    }

    private async Task<long> CalculateSumOfFileSizes(DirectoryInfo dirInfo, FileEntry dirEntry,
        CancellationToken cancellationToken)
    {
        long localFileSize = 0;
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            localFileSize = ProcessFiles(dirInfo, dirEntry, cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }

        return localFileSize;
    }

    private long ProcessFiles(DirectoryInfo dirInfo, FileEntry dirEntry, CancellationToken cancellationToken)
    {
        long total = 0;
        try
        {
            foreach (FileInfo fileInfo in dirInfo.EnumerateFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileEntry fileEntry = new FileEntry(fileInfo);
                OnFileProcessed(fileEntry);
                total += fileInfo.Length;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            dirEntry.FileState = FileState.AccessDenied;
        }
        catch (IOException)
        {
            dirEntry.FileState = FileState.IoError;
        }
        catch (Exception)
        {
            dirEntry.FileState = FileState.UnknownError;
        }
        return total;
    }

    private void OnStartProcessingDirectory(FileEntry dirEntry) =>
        StartProcessingDirectory?.Invoke(this, new StartProcessingDirectoryEventArgs(dirEntry));

    private void OnDirectoryProcessed(FileEntry dirEntry) =>
        DirectoryProcessed?.Invoke(this, new DirectoryProcessedEventArgs(dirEntry));

    private void OnFileProcessed(FileEntry fileEntry) =>
        FileProcessed?.Invoke(this, new FileProcessedEventArgs(fileEntry));

    private void OnProcessingCompleted(FileEntry rootEntry) =>
        ProcessingCompleted?.Invoke(this, new ProcessingCompletedEventArgs(rootEntry));
}