// ScannerEventHandlingService.cs

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Directory_Scanner.Core.FileModels;
using Directory_Scanner.Core.ScannerEventArgs;
using Directory_Scanner.UI.Context;
using Directory_Scanner.UI.Model;

namespace Directory_Scanner.UI.ScannerEventHandler;

public sealed class ScannerEventHandlingService
{
    private readonly ScannerEventContext _context;
    private readonly ConcurrentDictionary<string, FileEntryViewModel> _viewModelCache;
    private readonly ConcurrentDictionary<string, List<FileEntryViewModel>> _pendingChildren;

    public ScannerEventHandlingService(ScannerEventContext context)
    {
        _context = context;
        _viewModelCache = new ConcurrentDictionary<string, FileEntryViewModel>();
        _pendingChildren = new ConcurrentDictionary<string, List<FileEntryViewModel>>();
    }

    public void Clear()
    {
        _viewModelCache.Clear();
        _pendingChildren.Clear();
    }

    public void HandleStartProcessingDirectory(object? sender, StartProcessingDirectoryEventArgs e)
    {
        FileEntryViewModel viewModel = CreateAndCacheViewModel(e.DirectoryEntry);
        
        string? parentPath = GetParentPath(e.DirectoryEntry.FullPath);
        
        AddDirectoryToTree(viewModel, parentPath);
        
        ProcessPendingChildren(viewModel, e.DirectoryEntry.FullPath);
    }

    public void HandleFileProcessed(object? sender, FileProcessedEventArgs e)
    {
        FileEntryViewModel viewModel = CreateAndCacheViewModel(e.FileEntry);
        
        string? parentPath = GetParentPath(e.FileEntry.FullPath);
        
        if (parentPath != null)
        {
            TryAddChild(parentPath, viewModel);
        }
    }

    public void HandleDirectoryProcessed(object? sender, DirectoryProcessedEventArgs e)
    {
        if (_viewModelCache.TryGetValue(e.DirectoryEntry.FullPath, out FileEntryViewModel? viewModel))
        {
            HandleDirectoryProcessedCore(viewModel, e);
        }
    }

    public void HandleProcessingCompleted(object? sender, ProcessingCompletedEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(ExpandRootIfExists);
    }

    private void HandleDirectoryProcessedCore(FileEntryViewModel viewModel, DirectoryProcessedEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(viewModel.RaiseSizeChanged);
        if (e.DirectoryEntry.FullPath == _context.SelectedPathGetter())
        {
            Application.Current.Dispatcher.Invoke(() => _context.TotalSizeSetter(e.DirectoryEntry.FileSize));
        }
    }

    private FileEntryViewModel CreateAndCacheViewModel(FileEntry entry)
    {
        FileEntryViewModel viewModel = new FileEntryViewModel(entry);
        
        _viewModelCache.TryAdd(entry.FullPath, viewModel);
        
        return viewModel;
    }

    private void AddDirectoryToTree(FileEntryViewModel viewModel, string? parentPath)
    {
        if (_context.RootItems.Count == 0)
        {
            AddRootItem(viewModel);
        }
        else
        {
            if (parentPath != null)
            {
                TryAddChild(parentPath, viewModel);
            }
        }
    }

    private void AddRootItem(FileEntryViewModel viewModel)
    {
        Application.Current.Dispatcher.Invoke(() => _context.RootItems.Add(viewModel));
    }

    private void TryAddChild(string parentPath, FileEntryViewModel child)
    {
        if (_viewModelCache.TryGetValue(parentPath, out FileEntryViewModel? parent))
        {
            Application.Current.Dispatcher.Invoke(() => parent.Children.Add(child));
        }
        else
        {
            AddOrUpdateEntry(parentPath, child);
        }
    }

    private void AddOrUpdateEntry(string parentPath, FileEntryViewModel child)
    {
        List<FileEntryViewModel> AddNewDirectoryEntryValueFactory(string _) => [child];

        List<FileEntryViewModel> UpdateExistedDirectoryValueFactory(string _, List<FileEntryViewModel> existingList)
        {
            existingList.Add(child);

            return existingList;
        }

        _pendingChildren.AddOrUpdate(parentPath, AddNewDirectoryEntryValueFactory, UpdateExistedDirectoryValueFactory);
    }

    private void ProcessPendingChildren(FileEntryViewModel viewModel, string directoryPath)
    {
        if (_pendingChildren.TryRemove(directoryPath, out List<FileEntryViewModel>? children))
        {
            AddChildrenToViewModel(viewModel, children);
        }
    }

    private static void AddChildrenToViewModel(FileEntryViewModel viewModel, List<FileEntryViewModel> children)
    {
        foreach (FileEntryViewModel child in children)
        {
            Application.Current.Dispatcher.Invoke(() => viewModel.Children.Add(child));
        }
    }

    private void ExpandRootIfExists()
    {
        ObservableCollection<FileEntryViewModel> rootItems = _context.RootItems;
        if (rootItems.Count > 0)
        {
            rootItems[0].IsExpanded = true;
        }
    }

    private static string? GetParentPath(string fullPath)
    {
        return Directory.GetParent(fullPath)?.FullName;
    }
}