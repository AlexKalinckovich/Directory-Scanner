using System.Collections.ObjectModel;
using Directory_Scanner.Core.FileModels;

namespace Directory_Scanner.UI.Model;

public class FileEntryViewModel : ViewModelBase
{
    private readonly FileEntry _model;
    private bool _isExpanded;
    private double _percent;
    private string _sizeText;

    public FileEntryViewModel(FileEntry model)
    {
        _model = model;
        Children = new ObservableCollection<FileEntryViewModel>();
        _isExpanded = false;
        _percent = model.Percentage;
        _sizeText = FormatSize(model.FileSize);
    }

    public string Name => _model.FileName;

    public long Size => _model.FileSize;

    public string SizeText => _sizeText;

    public FileType Type => _model.FileType;

    public string? ParentPath => _model.ParentPath;

    public string FullPath => _model.FullPath;

    public FileState State => _model.FileState;

    public ObservableCollection<FileEntryViewModel> Children { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public double Percent
    {
        get => _percent;
        set => SetProperty(ref _percent, value);
    }

    public void UpdateFromModel()
    {
        _percent = _model.Percentage;
        _sizeText = FormatSize(_model.FileSize);
        
        OnPropertyChanged(nameof(Size));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(Percent));
    }

    public void RefreshChildren()
    {
        Children.Clear();

        foreach (FileEntry child in _model.SubDirectories)
        {
            FileEntryViewModel childViewModel = new FileEntryViewModel(child);
            Children.Add(childViewModel);
        }
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