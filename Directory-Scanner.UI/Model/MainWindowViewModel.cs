using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Directory_Scanner.Core.Core;
using Directory_Scanner.Core.FileModels;
using Directory_Scanner.Core.ScannerEventArgs;
using Directory_Scanner.UI.Command;
using Directory_Scanner.UI.Context;
using Directory_Scanner.UI.FileHelper;
using Directory_Scanner.UI.ScannerEventHandler;

namespace Directory_Scanner.UI.Model;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly DirectoryScanner _scanner;
    private readonly ScannerEventHandlingService _eventHandlingService;
    private string _selectedPath = string.Empty;
    private bool _isScanning;
    private long _totalSize;
    private CancellationTokenSource? _cts;
    
    public ObservableCollection<FileEntryViewModel> RootItems { get; }
    public ICommand StartScanCommand { get; }
    public ICommand CancelScanCommand { get; }
    private Stopwatch _stopwatch;
    private Timer? _sizeRecalculationTimer;
    private readonly Lock _timerLock = new Lock();
    private bool _pendingRecalculation;
    private bool _timerInitialized;

    public string SelectedPath
    {
        get => _selectedPath;
        set => SetProperty(ref _selectedPath, value);
    }

    private bool IsScanning
    {
        get => _isScanning;
        set => SetProperty(ref _isScanning, value);
    }

    public long TotalSize
    {
        get => _totalSize;
        private set => SetProperty(ref _totalSize, value);
    }



    public MainWindowViewModel()
    {
        _scanner = new DirectoryScanner();
        RootItems = new ObservableCollection<FileEntryViewModel>();

        ScannerEventContext context = new ScannerEventContext(
            RootItems,
            selectedPathGetter: () => SelectedPath,
            totalSizeSetter: (long size) => TotalSize = size);

        _eventHandlingService = new ScannerEventHandlingService(context);

        _eventHandlingService.FileProcessed += OnFileProcessedForRecalculation;

        StartScanCommand = new AsyncRelayCommand(ExecuteStartScan, CanExecuteStartScan);
        CancelScanCommand = new RelayCommand(ExecuteCancelScan, (object? _) => IsScanning);
    }

    private bool CanExecuteStartScan(object? parameter)
    {
        return !IsScanning;
    }

    private async Task ExecuteStartScan(object? parameter)
    {
        string? pathName = FileUtils.PickFolder();
        _stopwatch = Stopwatch.StartNew();
        
        if (pathName != null && CanExecuteStartScan(parameter))
        {
            IsScanning = true;
            _selectedPath = pathName;
            ClearState();
            SubscribeToScannerEvents();
            await RunScanAsync();
        }
    }

    private void ExecuteCancelScan(object? parameter)
    {
        _cts?.Cancel();
    }

    private void ClearState()
    {
        RootItems.Clear();
        TotalSize = 0;
        _eventHandlingService.Clear();
        
        StopTimer();
    }

    private void SubscribeToScannerEvents()
    {
        _scanner.StartProcessingDirectory += _eventHandlingService.HandleStartProcessingDirectory;
        _scanner.FileProcessed += _eventHandlingService.HandleFileProcessed;
        _scanner.DirectoryProcessed += _eventHandlingService.HandleDirectoryProcessed;
        _scanner.ProcessingCompleted += HandleProcessingCompleted;
    }

    private void OnFileProcessedForRecalculation(object? sender, FileProcessedEventArgs e)
    {
        RestartTimer();
    }

    private void InitializeTimer()
    {
        lock (_timerLock)
        {
            if (!_timerInitialized)
            {
                _sizeRecalculationTimer = new Timer(
                    OnSizeRecalculationTimerElapsed,
                    null,
                    Timeout.Infinite,
                    Timeout.Infinite);
                _timerInitialized = true;
            }
        }
    }

    private void RestartTimer()
    {
        InitializeTimer();
        
        lock (_timerLock)
        {
            _sizeRecalculationTimer?.Change(200, Timeout.Infinite);
        }
    }

    private void StopTimer()
    {
        lock (_timerLock)
        {
            _sizeRecalculationTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _pendingRecalculation = false;
        }
    }

    private void OnSizeRecalculationTimerElapsed(object? state)
    {
        if (_pendingRecalculation)
            return;

        _pendingRecalculation = true;

        try
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                RecalculateAllSizes();
                _pendingRecalculation = false;
            }));
        }
        catch
        {
            _pendingRecalculation = false;
        }
    }

    private void RecalculateAllSizes()
    {
        if (RootItems.Count != 0)
        {
            foreach (FileEntryViewModel rootItem in RootItems)
            {
                RecalculateSizeRecursive(rootItem);
            }

            TotalSize = _eventHandlingService.Size;

            if (TotalSize > 0)
            {
                CalculatePercentagesIterative(TotalSize);
            }
        }
    }

    private void RecalculateSizeRecursive(FileEntryViewModel viewModel)
    {
        foreach (FileEntryViewModel child in viewModel.Children)
        {
            RecalculateSizeRecursive(child);
        }

        if (viewModel.Type == FileType.Directory)
        {
            long childrenTotalSize = 0;
            
            foreach (FileEntryViewModel child in viewModel.Children)
            {
                childrenTotalSize += child.Size;
            }

            viewModel._model.FileSize = childrenTotalSize;

            viewModel.RaiseSizeChanged();
        }
    }

    private void CalculatePercentagesIterative(long totalSize)
    {
        Queue<FileEntryViewModel> queue = new Queue<FileEntryViewModel>();

        foreach (FileEntryViewModel rootItem in RootItems)
        {
            queue.Enqueue(rootItem);
        }

        while (queue.Count > 0)
        {
            FileEntryViewModel current = queue.Dequeue();

            current.Percent = totalSize > 0 ? ((double)current.Size / totalSize) * 100 : 0;

            foreach (FileEntryViewModel child in current.Children)
            {
                queue.Enqueue(child);
            }
        }
    }

    public void HandleProcessingCompleted(object? sender, ProcessingCompletedEventArgs e)
    {
        _eventHandlingService.HandleProcessingCompleted(sender, e);
        
        RecalculateAllSizes();
        
        StopTimer();
    }

    private void UnsubscribeFromScannerEvents()
    {
        _scanner.StartProcessingDirectory -= _eventHandlingService.HandleStartProcessingDirectory;
        _scanner.FileProcessed -= _eventHandlingService.HandleFileProcessed;
        _scanner.DirectoryProcessed -= _eventHandlingService.HandleDirectoryProcessed;
        _scanner.ProcessingCompleted -= HandleProcessingCompleted;
        _eventHandlingService.FileProcessed -= OnFileProcessedForRecalculation;
    }

    private async Task RunScanAsync()
    {
        _cts = new CancellationTokenSource();
        try
        {
            await _scanner.ScanDirectoryAsync(SelectedPath, _cts.Token);
        }
        finally
        {
            OnScanCompleted();
        }
    }

    private void OnScanCompleted()
    {
        UnsubscribeFromScannerEvents();
        _eventHandlingService.Clear();
        IsScanning = false;
        _cts?.Dispose();
        _cts = null;
        Console.WriteLine(_stopwatch.Elapsed);
    }
}