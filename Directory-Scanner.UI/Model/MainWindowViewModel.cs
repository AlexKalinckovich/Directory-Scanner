using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Directory_Scanner.Core.Core;
using Directory_Scanner.Core.Event;
using Directory_Scanner.Core.FileModels;
using Directory_Scanner.Core.Model;
using Directory_Scanner.UI.Command;

namespace Directory_Scanner.UI.Model;

public class MainWindowViewModel : ViewModelBase
{
    private readonly DirectoryScanner _scanner;
    private readonly ConcurrentDictionary<string, FileEntryViewModel> _viewModelCache;
    private readonly ConcurrentDictionary<string, List<FileEntryViewModel>> _pendingChildren;
    private string _selectedPath = string.Empty;
    private bool _isScanning;
    private CancellationTokenSource? _cts;

    public MainWindowViewModel()
    {
        _scanner = new DirectoryScanner();
        _viewModelCache = new ConcurrentDictionary<string, FileEntryViewModel>();
        _pendingChildren = new ConcurrentDictionary<string, List<FileEntryViewModel>>();
        RootItems = new ObservableCollection<FileEntryViewModel>();
        
        StartScanCommand = new RelayCommand(ExecuteStartScan, CanExecuteStartScan);
        CancelScanCommand = new RelayCommand(ExecuteCancelScan, _ => IsScanning);
    }

    public ObservableCollection<FileEntryViewModel> RootItems { get; }
    public ICommand StartScanCommand { get; }
    public ICommand CancelScanCommand { get; }

    public string SelectedPath
    {
        get => _selectedPath;
        set => SetProperty(ref _selectedPath, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set => SetProperty(ref _isScanning, value);
    }

    private bool CanExecuteStartScan(object? parameter) => !IsScanning && Directory.Exists(SelectedPath);
    
    private void ExecuteStartScan(object? parameter)
    {
        if (!CanExecuteStartScan(parameter)) return;
        
        MarkScanningStarted();
        ClearState();
        _cts = new CancellationTokenSource();
        Task.Run(() => ScanAsync(_cts.Token), _cts.Token);
    }

    private void ClearState()
    {
        RootItems.Clear();
        _viewModelCache.Clear();
        _pendingChildren.Clear();
    }

    private void MarkScanningStarted() => IsScanning = true;

    private void ExecuteCancelScan(object? parameter) => _cts?.Cancel();

    private async Task ScanAsync(CancellationToken token)
    {
        _scanner.FileProcessed += OnFileProcessed;
        _scanner.ProcessingCompleted += OnProcessingCompleted;

        var rootDir = new DirectoryInfo(SelectedPath);
        var rootEntry = new FileEntry(FileType.Directory, rootDir.Name, rootDir.FullName);
        var rootViewModel = new FileEntryViewModel(rootEntry);
        _viewModelCache.TryAdd(rootEntry.FullPath, rootViewModel);
        AddRootItem(rootViewModel);

        try
        {
            await _scanner.ScanDirectoryAsync(SelectedPath, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        { }
        finally
        {
            _scanner.FileProcessed -= OnFileProcessed;
            _scanner.ProcessingCompleted -= OnProcessingCompleted;
            IsScanning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void OnFileProcessed(object? sender, FileProcessedEventArgs e)
    {
        FileEntry entry = e.FileEntry;
        FileEntryViewModel viewModel = new FileEntryViewModel(entry);
        
        _viewModelCache.TryAdd(entry.FullPath, viewModel);
        
        string? parentPath = GetParentPath(entry.FullPath);
        if (parentPath == null)
        {
            AddRootItem(viewModel);
        }
        else
        {
            TryAddChild(parentPath, viewModel);
        }
        
        ProcessPendingChildren(entry.FullPath, viewModel);
    }

    private void TryAddChild(string parentPath, FileEntryViewModel viewModel)
    {
        if (_viewModelCache.TryGetValue(parentPath, out FileEntryViewModel? parent))
        {
            AddChildItem(parent, viewModel);
        }
        else
        {
            AddToPending(parentPath, viewModel);
        }
    }

    private void AddToPending(string parentPath, FileEntryViewModel viewModel)
    {
        _pendingChildren.AddOrUpdate(
            parentPath,
            _ => new List<FileEntryViewModel> { viewModel },
            (_, list) => { list.Add(viewModel); return list; });
    }

    private void ProcessPendingChildren(string path, FileEntryViewModel parentViewModel)
    {
        if (_pendingChildren.TryRemove(path, out List<FileEntryViewModel>? children))
        {
            foreach (FileEntryViewModel child in children)
            {
                AddChildItem(parentViewModel, child);
            }
        }
    }

    private void AddChildItem(FileEntryViewModel parent, FileEntryViewModel child)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            parent.Children.Add(child);
            parent.RaiseSizeChanged();
        });
    }

    private void OnProcessingCompleted(object? sender, ProcessingCompletedEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (RootItems.Count > 0)
            {
                
                RootItems[0].IsExpanded = true;
                foreach (FileEntryViewModel rootItem in RootItems)
                {
                    long fileSize = 0;
                    foreach (FileEntryViewModel fileEntryViewModel in rootItem.Children)
                    {
                        fileSize += fileEntryViewModel.Size;
                    }
                    rootItem.Size = fileSize;
                }
            }
        });
    }

    private void AddRootItem(FileEntryViewModel viewModel) =>
        Application.Current.Dispatcher.Invoke(() => RootItems.Add(viewModel));

    private static string? GetParentPath(string fullPath) =>
        Directory.GetParent(fullPath)?.FullName;
    

}