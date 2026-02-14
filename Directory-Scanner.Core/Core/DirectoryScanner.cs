using Directory_Scanner.Core.Event;
using Directory_Scanner.Core.FileModels;

namespace Directory_Scanner.Core.Core;

public class DirectoryScanner
{
    private readonly int _maxConcurrency = Environment.ProcessorCount * 2;
    private readonly SemaphoreSlim _semaphore;

    public event EventHandler<FileProcessedEventArgs>? FileProcessed;
    public event EventHandler<ProcessingCompletedEventArgs>? ProcessingCompleted;

    public DirectoryScanner()
    {
        _semaphore = new SemaphoreSlim(initialCount: _maxConcurrency, maxCount: _maxConcurrency);
    }

    public async Task<FileEntry> ScanDirectoryAsync(
        string rootPath,
        CancellationToken cancellationToken = default,
        TaskCreationOptions options = TaskCreationOptions.None)
    {
        AssertPathNotNullOrEmpty(rootPath);

        DirectoryInfo rootDir = new DirectoryInfo(rootPath);
        
        AssertRootDirectoryExists(rootPath, rootDir);

        FileEntry rootEntry = new FileEntry(FileType.Directory, rootDir.Name, rootDir.FullName);

        await ScanDirectoryInternalAsync(rootDir, rootEntry, cancellationToken, options).ConfigureAwait(false);

        OnProcessingCompleted(rootEntry);
        
        return rootEntry;
    }

    private static void AssertPathNotNullOrEmpty(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Path cannot be null or empty", nameof(rootPath));
    }

    private static void AssertRootDirectoryExists(string rootPath, DirectoryInfo rootDir)
    {
        if (!rootDir.Exists)
            throw new DirectoryNotFoundException($"Directory not found: {rootPath}");
    }

    private async Task ScanDirectoryInternalAsync(
        DirectoryInfo directory,
        FileEntry parentEntry,
        CancellationToken cancellationToken,
        TaskCreationOptions options)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        List<Task> tasks = new List<Task>();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            TryEnumerateFiles(directory, parentEntry, cancellationToken);
            
            TryEnumerateSubdirectories(directory, parentEntry, tasks, cancellationToken, options);
        }
        finally
        {
            _semaphore.Release();
        }

        if (tasks.Count > 0)
            await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private void TryEnumerateFiles(DirectoryInfo directory, FileEntry parentEntry, CancellationToken cancellationToken)
    {
        try
        {
            EnumerateFiles(directory, parentEntry, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            parentEntry.State = FileState.AccessDenied;
        }
    }

    private void EnumerateFiles(DirectoryInfo directory, FileEntry parentEntry, CancellationToken cancellationToken)
    {
        foreach (FileInfo file in directory.EnumerateFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileEntry fileEntry = CreateFileEntry(file);
            
            parentEntry.AddChild(fileEntry);
            
            OnFileProcessed(fileEntry);
        }
    }

    private FileEntry CreateFileEntry(FileInfo file)
    {
        return new FileEntry(FileType.File, file.Name, file.FullName, file.Length)
        {
            State = FileState.Ok
        };
    }

    private void TryEnumerateSubdirectories(
        DirectoryInfo directory,
        FileEntry parentEntry,
        List<Task> tasks,
        CancellationToken cancellationToken,
        TaskCreationOptions options)
    {
        try
        {
            EnumerateSubdirectories(directory, parentEntry, tasks, cancellationToken, options);
        }
        catch (UnauthorizedAccessException)
        {
            parentEntry.State = FileState.AccessDenied;
        }
    }

    private void EnumerateSubdirectories(
        DirectoryInfo directory,
        FileEntry parentEntry,
        List<Task> tasks,
        CancellationToken cancellationToken,
        TaskCreationOptions options)
    {
        foreach (DirectoryInfo subDir in directory.EnumerateDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileEntry dirEntry = new FileEntry(FileType.Directory, subDir.Name, subDir.FullName);
            
            parentEntry.AddChild(dirEntry);

            tasks.Add(CreateSubtask(subDir, dirEntry, cancellationToken, options));
        }
    }

    private Task CreateSubtask(
        DirectoryInfo subDir,
        FileEntry dirEntry,
        CancellationToken cancellationToken,
        TaskCreationOptions options)
    {
        if (options == TaskCreationOptions.None)
            return ScanDirectoryInternalAsync(subDir, dirEntry, cancellationToken, options);

        return Task.Factory.StartNew(
            () => ScanDirectoryInternalAsync(subDir, dirEntry, cancellationToken, options),
            cancellationToken,
            options,
            TaskScheduler.Default).Unwrap();
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