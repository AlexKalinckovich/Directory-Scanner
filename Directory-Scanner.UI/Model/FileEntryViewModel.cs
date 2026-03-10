using System.Collections.ObjectModel;
using Directory_Scanner.Core.FileModels;

namespace Directory_Scanner.UI.Model;

public class FileEntryViewModel : ViewModelBase
{
    internal readonly FileEntry _model;
    private bool _isExpanded;
    private double _percent;

    public FileEntryViewModel(FileEntry model)
    {
        _model = model;
        Children = new ObservableCollection<FileEntryViewModel>();
    }

    public string Name => _model.FileName;
    
    public long Size => _model.FileSize;
    
    public FileType Type => _model.FileType;
    
    public string? ParentPath => _model.ParentPath;
    
    public string FullPath => _model.FullPath;
    
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

    public void RaiseSizeChanged() => OnPropertyChanged(nameof(Size));

    
    
    
    public void UpdateSizeFromModel()
    {
        OnPropertyChanged(nameof(Size));
    }

    
    
    
    
    public long CalculateTotalSize()
    {
        long totalSize = Size;

        foreach (FileEntryViewModel child in Children)
        {
            totalSize += child.CalculateTotalSize();
        }

        return totalSize;
    }

    
    
    
    public void UpdateSizeFromChildren()
    {
        long childrenTotal = 0;

        foreach (FileEntryViewModel child in Children)
        {
            child.UpdateSizeFromChildren();
            childrenTotal += child.Size;
        }
        
        _model.FileSize += childrenTotal;
        
        OnPropertyChanged(nameof(Size));
    }
}