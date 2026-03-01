using System.Collections.ObjectModel;
using System.Windows.Input;
using Directory_Scanner.Core.Core;
using Directory_Scanner.UI.Command;
using Directory_Scanner.UI.Context;
using Directory_Scanner.UI.FileHelper;
using Directory_Scanner.UI.ScannerEventHandler;

namespace Directory_Scanner.UI.Model
{
    public sealed class MainWindowViewModel : ViewModelBase
    {
        public ObservableCollection<FileEntryViewModel> RootItems { get; }
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

        private readonly DirectoryScanner _scanner;
        
        private readonly ScannerEventHandlingService _eventHandlingService;
        
        private string _selectedPath = string.Empty;
        
        private bool _isScanning;
        
        private long _totalSize;
        
        private CancellationTokenSource? _cts;
        public MainWindowViewModel()
        {
            _scanner = new DirectoryScanner();
            RootItems = new ObservableCollection<FileEntryViewModel>();

            ScannerEventContext context = new ScannerEventContext(
                RootItems,
                selectedPathGetter: () => SelectedPath,
                totalSizeSetter: (long size) => TotalSize = size);

            _eventHandlingService = new ScannerEventHandlingService(context);

            StartScanCommand = new RelayCommand(ExecuteStartScan, CanExecuteStartScan);
            CancelScanCommand = new RelayCommand(ExecuteCancelScan, (object? _) => IsScanning);
        }

        private bool CanExecuteStartScan(object? parameter)
        {
            return !IsScanning;
        }

        private void ExecuteStartScan(object? parameter)
        {
            string? pathName = FileUtils.PickFolder();
            if (pathName != null && CanExecuteStartScan(parameter))
            {
                IsScanning = true;
                
                _selectedPath = pathName;
                
                ClearState();
                
                SubscribeToScannerEvents();
                
                RunScanAsync();
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

        private void RunScanAsync()
        {
            _cts = new CancellationTokenSource();
            Task.Run(() => _scanner.ScanDirectoryAsync(SelectedPath, _cts.Token), _cts.Token)
                .ContinueWith(OnScanCompleted, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void OnScanCompleted(Task task)
        {
            UnsubscribeFromScannerEvents();
            
            _eventHandlingService.Clear();
            
            IsScanning = false;
            
            _cts?.Dispose();
            
            _cts = null;
        }
    }
}