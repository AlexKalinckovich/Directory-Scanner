using Directory_Scanner.Core.FileModels;

namespace Directory_Scanner.Core.ScannerEventArgs;

public class StartProcessingDirectoryEventArgs : EventArgs
{
    public FileEntry DirectoryEntry { get; }
    public StartProcessingDirectoryEventArgs(FileEntry directoryEntry) => DirectoryEntry = directoryEntry;
}