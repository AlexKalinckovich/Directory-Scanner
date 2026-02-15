using Directory_Scanner.Core.FileModels;
using Directory_Scanner.Core.ScannerEventArgs;

namespace Directory_Scanner.Core.Core;

public class DirectoryScannerV2
{
    private const int InitialPendingTaskCapacity = 100;
    
    private readonly string _rootDirectoryPath;

    private DirectoryInfo _rootDirectoryInfo;
    
    private readonly int _maxConcurrency = Environment.ProcessorCount * 2;
    
    private readonly SemaphoreSlim _semaphore;

    private readonly List<Task> _pendingTasks;
    
    public event EventHandler<FileProcessedEventArgs>? FileProcessed;
    
    public event EventHandler<StartProcessingDirectoryEventArgs>? StartProcessingDirectoryEvent; 
    
    public event EventHandler<ProcessingCompletedEventArgs>? ProcessingCompleted;

    
    
    public DirectoryScannerV2(string rootDirectoryPath)
    {
        _semaphore = new SemaphoreSlim(initialCount: _maxConcurrency, maxCount: _maxConcurrency);
        
        _rootDirectoryPath = rootDirectoryPath;
        
        AssertPathNotNullOrEmpty();
        
        _rootDirectoryInfo = new DirectoryInfo(_rootDirectoryPath);
        
        AssertRootDirectoryExists();
        
        _pendingTasks = new List<Task>(InitialPendingTaskCapacity);
    }

    public async Task ScanDirectoryAsync()
    {
        FileEntry rootEntry = new FileEntry(FileType.Directory, _rootDirectoryInfo.Name, _rootDirectoryInfo.FullName);
        StartProcessingDirectoryEvent?.Invoke(this, new StartProcessingDirectoryEventArgs(rootEntry));
    }

    private void EnumerateEntryDirectory(in DirectoryInfo directoryInfo)
    {
        DirectoryInfo info = directoryInfo;
        
        _pendingTasks.Add(Task.Run(() => EnumerateDirectory(info)));
        _pendingTasks.Add(Task.Run(() => EnumerateFiles(info)));
        
        Task.WaitAll(_pendingTasks.ToArray());
        
       
    }

    private void EnumerateFiles(in DirectoryInfo directoryInfo)
    {
        foreach (FileInfo fileInfo in directoryInfo.EnumerateFiles())
        {
            FileInfo info = fileInfo;
            _pendingTasks.Add(Task.Run(() => ProcessFileInfo(info)));
        }
    }

    private void ProcessFileInfo(in FileInfo fileInfo)
    {
        FileEntry fileEntry = new FileEntry(FileType.File, fileInfo.Name, fileInfo.FullName, fileInfo.Length);
        FileProcessed?.Invoke(this, new FileProcessedEventArgs(fileEntry));
    }
    
    private void EnumerateDirectory(in DirectoryInfo directoryInfo)
    {
        foreach (DirectoryInfo directory in directoryInfo.EnumerateDirectories())
        {
            DirectoryInfo info = directory;
            _pendingTasks.Add(Task.Run(() => EnumerateEntryDirectory(info)));
        }
    }
    private void AssertPathNotNullOrEmpty()
    {
        if (string.IsNullOrWhiteSpace(_rootDirectoryPath))
            throw new ArgumentException("Path cannot be null or empty", nameof(_rootDirectoryPath));
    }

    private void AssertRootDirectoryExists()
    {
        if (!_rootDirectoryInfo.Exists)
            throw new DirectoryNotFoundException($"Directory not found: {_rootDirectoryPath}");
    }
}