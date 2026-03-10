using System.Collections.ObjectModel;
using Directory_Scanner.Core.FileModels;
using Directory_Scanner.UI.Model;

namespace Directory_Scanner.UI.Context;

public sealed class ScannerEventContext
{
    private FileEntry? _rootEntry;

    public ObservableCollection<FileEntryViewModel> RootItems { get; }

    public Func<string> SelectedPathGetter { get; }

    public Action<long> TotalSizeSetter { get; }

    public ScannerEventContext(
        ObservableCollection<FileEntryViewModel> rootItems,
        Func<string>? selectedPathGetter = null,
        Action<long>? totalSizeSetter = null)
    {
        RootItems = rootItems;
        SelectedPathGetter = selectedPathGetter ?? (() => string.Empty);
        TotalSizeSetter = totalSizeSetter ?? (_ => { });
    }

    public void SetRootEntry(FileEntry rootEntry)
    {
        _rootEntry = rootEntry;
    }

    public FileEntry? GetRootEntry()
    {
        return _rootEntry;
    }
}