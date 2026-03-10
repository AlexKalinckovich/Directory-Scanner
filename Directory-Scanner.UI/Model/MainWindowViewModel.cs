using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Directory_Scanner.Core.Core;
using Directory_Scanner.Core.FileModels;
using Directory_Scanner.UI.Command;
using Directory_Scanner.UI.Context;
using Directory_Scanner.UI.Converter;
using Directory_Scanner.UI.Converters;
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
    private Stopwatch _stopwatch;

    public ObservableCollection<FileEntryViewModel> RootItems { get; private set; }

    public ICommand StartScanCommand { get; }

    public ICommand CancelScanCommand { get; }

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

        ScannerEventContext context = new ScannerEventContext(RootItems);

        _eventHandlingService = new ScannerEventHandlingService(context);
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

            await SwapToFinalViewModelsAsync();
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
    }

    private void SubscribeToScannerEvents()
    {
        _scanner.StartProcessingDirectory += _eventHandlingService.HandleStartProcessingDirectory;
        _scanner.FileProcessed += _eventHandlingService.HandleFileProcessed;
        _scanner.DirectoryProcessed += _eventHandlingService.HandleDirectoryProcessed;
        _scanner.ProcessingCompleted += _eventHandlingService.HandleProcessingCompleted;
    }

    private void UnsubscribeFromScannerEvents()
    {
        _scanner.StartProcessingDirectory -= _eventHandlingService.HandleStartProcessingDirectory;
        _scanner.FileProcessed -= _eventHandlingService.HandleFileProcessed;
        _scanner.DirectoryProcessed -= _eventHandlingService.HandleDirectoryProcessed;
        _scanner.ProcessingCompleted -= _eventHandlingService.HandleProcessingCompleted;
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
        IsScanning = false;
        _cts?.Dispose();
        _cts = null;
        Console.WriteLine(_stopwatch.Elapsed);
    }

    private async Task SwapToFinalViewModelsAsync()
    {
        _eventHandlingService.MarkSwapPending();

        await Task.Delay(50);

        FileEntry? rootEntry = _eventHandlingService.GetRootEntry();

        if (rootEntry == null)
        {
            return;
        }

        ObservableCollection<FileEntryViewModel> finalViewModels =
            await FileEntryToViewModelConverter.ConvertToViewModelsAsync(
                rootEntry,
                new Progress<double>(value => { }));

        Application.Current.Dispatcher.Invoke(() =>
        {
            RootItems.Clear();

            foreach (FileEntryViewModel viewModel in finalViewModels)
            {
                RootItems.Add(viewModel);
            }

            OnPropertyChanged(nameof(RootItems));
        });

        _eventHandlingService.Clear();
    }

    public void Cleanup()
    {
        _scanner?.Dispose();
        _eventHandlingService?.Dispose();
        _cts?.Dispose();
    }
}