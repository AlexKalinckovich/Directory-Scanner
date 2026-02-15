using System.Collections.ObjectModel;
using Directory_Scanner.UI.Model;

namespace Directory_Scanner.UI.Context;

public readonly struct ScannerEventContext
{
    public ObservableCollection<FileEntryViewModel> RootItems { get; }
    public Func<string> SelectedPathGetter { get; }
    public Action<long> TotalSizeSetter { get; }
    
    public ScannerEventContext(
        ObservableCollection<FileEntryViewModel> rootItems,
        Func<string> selectedPathGetter,
        Action<long> totalSizeSetter)
    {
        RootItems = rootItems;
        SelectedPathGetter = selectedPathGetter;
        TotalSizeSetter = totalSizeSetter;
    }
    
}