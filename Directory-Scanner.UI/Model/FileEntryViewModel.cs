using System.Collections.ObjectModel;
using Directory_Scanner.Core.FileModels;
using Directory_Scanner.Core.Model;

namespace Directory_Scanner.UI.Model;

public class FileEntryViewModel : ViewModelBase
{
    private readonly FileEntry _model;
    private bool _isExpanded;
    private long _totalFileSize;
    public string Name => _model.FileName;
    public string FullPath => _model.FullPath;
    public long Size
    {
        get => _model.FileSize;
        set => SetProperty(ref _totalFileSize, value);
    }

    public FileType Type => _model.FileType;
    public FileState State => _model.State;
    public ObservableCollection<FileEntryViewModel> Children { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public void RaiseSizeChanged() => OnPropertyChanged(nameof(Size));
    
    public FileEntryViewModel(FileEntry model)
    {
        _model = model;
        Children = new ObservableCollection<FileEntryViewModel>();
    }
}