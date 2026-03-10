using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Threading;
using Directory_Scanner.Core.FileModels;
using Directory_Scanner.Core.ScannerEventArgs;
using Directory_Scanner.UI.Context;
using Directory_Scanner.UI.Model;

namespace Directory_Scanner.UI.ScannerEventHandler;

public sealed class ScannerEventHandlingService : IDisposable
{
    private long _size = 0;
    public long Size => _size;
    
    private readonly ScannerEventContext _context;
    private readonly ConcurrentDictionary<string, FileEntryViewModel> _viewModelCache;
    private readonly ConcurrentQueue<FileEntryViewModel> _orphanedFiles;
    private readonly List<FileEntryViewModel> _pendingFileChildren;
    private readonly object _batchLock;
    private readonly Timer _flushTimer;
    private readonly Dispatcher _dispatcher;
    private bool _disposed;

    public event EventHandler<FileProcessedEventArgs>? FileProcessed;

    public ScannerEventHandlingService(ScannerEventContext context)
    {
        _context = context;
        _viewModelCache = new ConcurrentDictionary<string, FileEntryViewModel>();
        _orphanedFiles = new ConcurrentQueue<FileEntryViewModel>();
        _pendingFileChildren = new List<FileEntryViewModel>();
        _batchLock = new object();
        _dispatcher = Application.Current.Dispatcher;
        _flushTimer = new Timer(FlushPendingFiles, null, 0, 100);
    }

    public void Clear()
    {
        FlushPendingFiles(synchronous: true);
        _viewModelCache.Clear();
        _orphanedFiles.Clear();
        Interlocked.Exchange(ref _size, 0);
    }

    public void HandleStartProcessingDirectory(object? sender, StartProcessingDirectoryEventArgs e)
    {
        FileEntry directoryEntry = e.DirectoryEntry;
        FileEntryViewModel viewModel = CreateAndCacheViewModel(directoryEntry);
        string? parentPath = directoryEntry.ParentPath;

        if (_context.RootItems.Count == 0)
        {
            _dispatcher.Invoke(() => _context.RootItems.Add(viewModel));
        }
        else if (parentPath != null)
        {
            if (_viewModelCache.TryGetValue(parentPath, out FileEntryViewModel? parent))
            {
                _dispatcher.Invoke(() => parent.Children.Add(viewModel));
            }
        }

        TryAttachOrphanedFiles(parentPath);
    }

    public void HandleFileProcessed(object? sender, FileProcessedEventArgs e)
    {
        FileEntryViewModel viewModel = new FileEntryViewModel(e.FileEntry);
        _viewModelCache.TryAdd(e.FileEntry.FullPath, viewModel);
        
        Interlocked.Add(ref _size, e.FileEntry.FileSize);
        
        string? parentPath = e.FileEntry.ParentPath;
        
        if (parentPath != null && _viewModelCache.TryGetValue(parentPath, out FileEntryViewModel? parent))
        {
            lock (_batchLock)
            {
                _pendingFileChildren.Add(viewModel);
            }
        }
        else
        {
            _orphanedFiles.Enqueue(viewModel);
        }
        
        FlushPendingFiles(synchronous: false);

        FileProcessed?.Invoke(sender, e);
    }

    public void HandleDirectoryProcessed(object? sender, DirectoryProcessedEventArgs e)
    {
        if (_viewModelCache.TryGetValue(e.DirectoryEntry.FullPath, out FileEntryViewModel? viewModel))
        { 
            _dispatcher.Invoke(viewModel.RaiseSizeChanged);
            
            if (e.DirectoryEntry.FullPath == _context.SelectedPathGetter())
            {
                _dispatcher.Invoke(() => _context.TotalSizeSetter(e.DirectoryEntry.FileSize));
            }
        }

        TryAttachOrphanedFiles(e.DirectoryEntry.FullPath);
    }

    public void HandleProcessingCompleted(object? sender, ProcessingCompletedEventArgs e)
    {
        FlushPendingFiles(synchronous: true);
    }

    private void TryAttachOrphanedFiles(string? parentPath)
    {
        if (parentPath == null)
            return;

        List<FileEntryViewModel> toAttach = new List<FileEntryViewModel>();
        
        int count = _orphanedFiles.Count;
        for (int i = 0; i < count; i++)
        {
            if (_orphanedFiles.TryDequeue(out FileEntryViewModel? orphan))
            {
                if (orphan.ParentPath == parentPath)
                {
                    toAttach.Add(orphan);
                }
                else
                {
                    _orphanedFiles.Enqueue(orphan);
                }
            }
        }

        if (toAttach.Count > 0 && _viewModelCache.TryGetValue(parentPath, out FileEntryViewModel? parent))
        {
            _dispatcher.Invoke(() =>
            {
                foreach (FileEntryViewModel child in toAttach)
                {
                    parent.Children.Add(child);
                }
            });
        }
    }

    private FileEntryViewModel CreateAndCacheViewModel(FileEntry entry)
    {
        FileEntryViewModel viewModel = new FileEntryViewModel(entry);
        _viewModelCache.TryAdd(entry.FullPath, viewModel);
        return viewModel;
    }

    private void FlushPendingFiles(object? state)
    {
        FlushPendingFiles(synchronous: false);
    }

    private void FlushPendingFiles(bool synchronous)
    {
        List<FileEntryViewModel> toAdd;
        lock (_batchLock)
        {
            if (_pendingFileChildren.Count == 0)
                return;
            toAdd = new List<FileEntryViewModel>(_pendingFileChildren);
            _pendingFileChildren.Clear();
        }

        Dictionary<string, List<FileEntryViewModel>> grouped = GroupFilesByDir(toAdd);
        UpdateUi(synchronous, grouped);
    }

    private static Dictionary<string, List<FileEntryViewModel>> GroupFilesByDir(List<FileEntryViewModel> toAdd)
    {
        Dictionary<string, List<FileEntryViewModel>> grouped = new Dictionary<string, List<FileEntryViewModel>>();
        foreach (FileEntryViewModel vm in toAdd)
        {
            string? parentPath = vm.ParentPath;
            if (parentPath != null)
            {
                if (!grouped.TryGetValue(parentPath, out List<FileEntryViewModel>? list))
                {
                    list = new List<FileEntryViewModel>();
                    grouped[parentPath] = list;
                }
                list.Add(vm);
            }
        }
        return grouped;
    }

    private void UpdateUi(bool synchronous, Dictionary<string, List<FileEntryViewModel>> grouped)
    {
        if (synchronous)
        {
            _dispatcher.Invoke(() => AddGroupedChildren(grouped));
        }
        else
        {
            _dispatcher.Invoke(() => AddGroupedChildren(grouped));
        }
    }

    private void AddGroupedChildren(Dictionary<string, List<FileEntryViewModel>> grouped)
    {
        foreach (KeyValuePair<string, List<FileEntryViewModel>> pair in grouped)
        {
            if (_viewModelCache.TryGetValue(pair.Key, out FileEntryViewModel? parent))
            {
                foreach (FileEntryViewModel child in pair.Value)
                {
                    parent.Children.Add(child);
                }
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _flushTimer.Dispose();
            _disposed = true;
        }
    }
}