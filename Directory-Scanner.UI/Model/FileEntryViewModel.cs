using System.Collections.ObjectModel;
using Directory_Scanner.Core.FileModels;

namespace Directory_Scanner.UI.Model;

public class FileEntryViewModel : ViewModelBase
{
    private readonly FileEntry _model;
    private bool _isLoading;
    private string _sizeText;
    private long _size;
    private double _percent;

    public FileEntryViewModel(FileEntry model)
    {
        _model = model;
        Children = new ObservableCollection<FileEntryViewModel>();
        _isLoading = false;
        _size = model.FileSize;
        _sizeText = FormatSize(model.FileSize);
        _percent = model.Percentage;
    }

    public string Name => _model.FileName;

    public string FullPath => _model.FullPath;

    public string? ParentPath => _model.ParentPath;

    public FileType Type => _model.FileType;

    public long Size
    {
        get => _size;
        set => SetProperty(ref _size, value);
    }

    public string SizeText
    {
        get => _sizeText;
        set => SetProperty(ref _sizeText, value);
    }

    public double Percent
    {
        get => _percent;
        set => SetProperty(ref _percent, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public ObservableCollection<FileEntryViewModel> Children { get; }

    public void MarkAsLoading()
    {
        IsLoading = true;
        SizeText = "Loading...";
    }

    public void UpdateFromModel()
    {
        _percent = _model.Percentage;
        _size = _model.FileSize;
        _sizeText = FormatSize(_model.FileSize);
        IsLoading = false;

        OnPropertyChanged(nameof(Size));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(Percent));
        OnPropertyChanged(nameof(IsLoading));
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:F2} KB";
        }

        if (bytes < 1024 * 1024 * 1024)
        {
            return $"{bytes / (1024.0 * 1024.0):F2} MB";
        }

        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }
}