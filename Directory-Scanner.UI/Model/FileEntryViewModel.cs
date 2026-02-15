using System.Collections.ObjectModel;
using Directory_Scanner.Core.FileModels;

namespace Directory_Scanner.UI.Model;

public class FileEntryViewModel : ViewModelBase
{
    private readonly FileEntry _model;
    private bool _isExpanded;

    public FileEntryViewModel(FileEntry model)
    {
        _model = model;
        Children = new ObservableCollection<FileEntryViewModel>();
    }

    public string Name => _model.FileName;
    public long Size => _model.FileSize;
    public ObservableCollection<FileEntryViewModel> Children { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public void RaiseSizeChanged() => OnPropertyChanged(nameof(Size));
}