using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Directory_Scanner.Core.FileModels;
using Directory_Scanner.Core.ScannerEventArgs;
using Directory_Scanner.UI.Context;
using Directory_Scanner.UI.Model;

namespace Directory_Scanner.UI.ScannerEventHandler;

public sealed class ScannerEventHandlingService : IDisposable
{
    private const int BatchSize = 100;
    private const int FlushIntervalMs = 200;

    private readonly ScannerEventContext _context;
    private readonly ConcurrentDictionary<string, FileEntryViewModel> _viewModelCache;
    private readonly ConcurrentQueue<FileEntryViewModel> _orphanedFiles;
    private readonly List<FileEntryViewModel> _pendingFileChildren;
    private readonly object _batchLock;
    private readonly DispatcherTimer _flushTimer;
    private readonly Dispatcher _dispatcher;
    private int _pendingCount;
    private bool _disposed;
    private bool _isSwapPending;
    private int _uiUpdateCount;
    private readonly object _uiUpdateLock;

    public ScannerEventHandlingService(ScannerEventContext context)
    {
        _context = context;
        _viewModelCache = new ConcurrentDictionary<string, FileEntryViewModel>();
        _orphanedFiles = new ConcurrentQueue<FileEntryViewModel>();
        _pendingFileChildren = new List<FileEntryViewModel>();
        _batchLock = new object();
        _uiUpdateLock = new object();
        _dispatcher = Application.Current.Dispatcher;
        _pendingCount = 0;
        _uiUpdateCount = 0;

        _flushTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(FlushIntervalMs)
        };
        _flushTimer.Tick += (s, e) => FlushPendingFiles(synchronous: false);
        _flushTimer.Start();
        _isSwapPending = false;
    }

    public void Clear()
    {
        FlushPendingFiles(synchronous: true);
        _viewModelCache.Clear();
        _orphanedFiles.Clear();
        lock (_batchLock)
        {
            _pendingCount = 0;
        }
        lock (_uiUpdateLock)
        {
            _uiUpdateCount = 0;
        }
        _isSwapPending = false;
    }

    public void MarkSwapPending()
    {
        _isSwapPending = true;
        _flushTimer.Stop();
    }

    public bool IsSwapPending => _isSwapPending;

    public void HandleStartProcessingDirectory(object? sender, StartProcessingDirectoryEventArgs e)
    {
        if (_isSwapPending)
        {
            return;
        }

        FileEntry directoryEntry = e.DirectoryEntry;
        FileEntryViewModel viewModel = CreateAndCacheViewModel(directoryEntry);
        string? parentPath = directoryEntry.ParentPath;

        if (_context.RootItems.Count == 0)
        {
            _dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => _context.RootItems.Add(viewModel)));
        }
        else if (parentPath != null)
        {
            if (_viewModelCache.TryGetValue(parentPath, out FileEntryViewModel? parent))
            {
                _dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() => parent.Children.Add(viewModel)));
            }
        }

        TryAttachOrphanedFiles(parentPath);
    }

    public void HandleFileProcessed(object? sender, FileProcessedEventArgs e)
    {
        if (_isSwapPending)
        {
            return;
        }

        FileEntryViewModel viewModel = new FileEntryViewModel(e.FileEntry);
        _viewModelCache.TryAdd(e.FileEntry.FullPath, viewModel);

        string? parentPath = e.FileEntry.ParentPath;

        if (parentPath != null && _viewModelCache.TryGetValue(parentPath, out FileEntryViewModel? parent))
        {
            lock (_batchLock)
            {
                _pendingFileChildren.Add(viewModel);
                _pendingCount++;

                if (_pendingCount >= BatchSize)
                {
                    FlushPendingFilesLocked();
                    _pendingCount = 0;
                }
            }
        }
        else
        {
            _orphanedFiles.Enqueue(viewModel);
        }
    }

    public void HandleDirectoryProcessed(object? sender, DirectoryProcessedEventArgs e)
    {
        if (_isSwapPending)
        {
            return;
        }

        if (_viewModelCache.TryGetValue(e.DirectoryEntry.FullPath, out FileEntryViewModel? viewModel))
        {
            if (e.DirectoryEntry.FullPath == _context.SelectedPathGetter())
            {
                _dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() => _context.TotalSizeSetter(e.DirectoryEntry.FileSize)));
            }
        }

        TryAttachOrphanedFiles(e.DirectoryEntry.FullPath);
    }

    public void HandleProcessingCompleted(object? sender, ProcessingCompletedEventArgs e)
    {
        _context.SetRootEntry(e.FileEntry);
        FlushPendingFiles(synchronous: true);
    }

    private void TryAttachOrphanedFiles(string? parentPath)
    {
        if (parentPath == null)
            return;

        List<FileEntryViewModel> toAttach = new List<FileEntryViewModel>();

        int count = Math.Min(_orphanedFiles.Count, 50);
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
            _dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() =>
                {
                    foreach (FileEntryViewModel child in toAttach)
                    {
                        parent.Children.Add(child);
                    }
                }));
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
        int count;

        lock (_batchLock)
        {
            if (_pendingFileChildren.Count == 0)
                return;

            toAdd = new List<FileEntryViewModel>(_pendingFileChildren);
            _pendingFileChildren.Clear();
            count = _pendingCount;
            _pendingCount = 0;
        }

        if (synchronous)
        {
            AddGroupedChildrenDirect(toAdd);
        }
        else
        {
            ThrottledUiUpdate(toAdd);
        }
    }

    private void FlushPendingFilesLocked()
    {
        List<FileEntryViewModel> toAdd = new List<FileEntryViewModel>(_pendingFileChildren);
        _pendingFileChildren.Clear();

        ThrottledUiUpdate(toAdd);
    }

    private void ThrottledUiUpdate(List<FileEntryViewModel> toAdd)
    {
        lock (_uiUpdateLock)
        {
            _uiUpdateCount++;
        }

        _dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => AddGroupedChildrenDirect(toAdd)));
    }

    private void AddGroupedChildrenDirect(List<FileEntryViewModel> toAdd)
    {
        Dictionary<string, List<FileEntryViewModel>> grouped = GroupFilesByDir(toAdd);

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

    public FileEntry? GetRootEntry()
    {
        return _context.GetRootEntry();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _flushTimer.Stop();
            _disposed = true;
        }
    }
}