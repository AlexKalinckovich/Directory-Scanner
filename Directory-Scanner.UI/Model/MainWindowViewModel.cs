using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Directory_Scanner.Core.Core;
using Directory_Scanner.Core.FileModels;
using Directory_Scanner.UI.Command;
using Directory_Scanner.UI.Converter;
using Directory_Scanner.UI.FileHelper;

namespace Directory_Scanner.UI.Model;

public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly DirectoryScanner _scanner;
    private readonly AsyncRelayCommand _startScanCommand;
    private readonly RelayCommand _cancelScanCommand;
    private CancellationTokenSource? _cts;
    private Stopwatch _stopwatch;
    private bool _isScanning;
    private bool _isCancelled;
    private bool _isDisposed;
    private string _scanPath = string.Empty;
    private string _statusMessage = string.Empty;

    public ObservableCollection<FileEntryViewModel> RootItems { get; }

    public MainWindowViewModel()
    {
        _scanner = new DirectoryScanner();
        RootItems = new ObservableCollection<FileEntryViewModel>();
        _startScanCommand = new AsyncRelayCommand(ExecuteStartScanAsync, _ => !IsScanning);
        _cancelScanCommand = new RelayCommand(ExecuteCancelScan, _ => IsScanning);
    }

    public bool IsScanning
    {
        get => _isScanning;
        set => SetProperty(ref _isScanning, value);
    }

    public bool IsCancelled
    {
        get => _isCancelled;
        set => SetProperty(ref _isCancelled, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public ICommand StartScanCommand => _startScanCommand;

    public ICommand CancelScanCommand => _cancelScanCommand;

    private async Task ExecuteStartScanAsync(object? parameter)
    {
        string? path = FileUtils.PickFolder();

        if (path == null)
        {
            return;
        }

        await InitializeScanAsync(path);
    }

    private async Task InitializeScanAsync(string path)
    {
        _stopwatch = Stopwatch.StartNew();
        _scanPath = path;
        IsScanning = true;
        IsCancelled = false;
        StatusMessage = "Scanning...";

        ClearState();

        _cts ??= new CancellationTokenSource();
        try
        {
            RootItems.Add(new FileEntryViewModel(new FileEntry(new DirectoryInfo(_scanPath))));
            FileEntry rootEntry = await Task.Run(async () => await _scanner.ScanDirectoryAsync(path, _cts.Token));
            await UpdateRootItemsAsync(rootEntry);
            StatusMessage = "Scan completed";
        }
        catch (OperationCanceledException)
        {
            IsCancelled = true;
            StatusMessage = "Scan cancelled - showing partial results";
            await ClearRootItemsAsync(_cts.Token);
        }
        finally
        {
            CompleteScan();
        }
    }

    private void ExecuteCancelScan(object? parameter)
    {
        _cts?.Cancel();
    }

    private void ClearState()
    {
        RootItems.Clear();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
    }

    private async Task UpdateRootItemsAsync(FileEntry rootEntry)
    {
        ObservableCollection<FileEntryViewModel> viewModels = await Task.Run(
            async () => await FileEntryToViewModelConverter.ConvertToViewModelsAsync(rootEntry)
        );
        
        InvokeOnUiThread(() =>
        {
            RootItems.Clear();

            foreach (FileEntryViewModel viewModel in viewModels)
            {
                RootItems.Add(viewModel);
            }
        });
    }

    private async Task ClearRootItemsAsync(CancellationToken token)
    {
        InvokeOnUiThread(() =>
        {
            RootItems.Clear();
        });

        await Task.Delay(100, token);

        InvokeOnUiThread(() =>
        {
            OnPropertyChanged(nameof(RootItems));
        });
    }

    private void CompleteScan()
    {
        IsScanning = false;
        _cts?.Dispose();
        _cts = null;
        Console.WriteLine(_stopwatch.Elapsed);
    }

    private void InvokeOnUiThread(Action action)
    {
        Application.Current?.Dispatcher.Invoke(action);
    }

    public void Dispose()
    {
        Dispose(true);
    }

    private void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (disposing)
        {
            _scanner.Dispose();
            _cts?.Dispose();
        }

        _isDisposed = true;
    }
}