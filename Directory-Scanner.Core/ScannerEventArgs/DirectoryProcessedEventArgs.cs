using Directory_Scanner.Core.FileModels;

namespace Directory_Scanner.Core.ScannerEventArgs;

public class DirectoryProcessedEventArgs : EventArgs
{
    public FileEntry DirectoryEntry { get; }
    
    public DirectoryProcessedEventArgs(FileEntry directoryEntry) => DirectoryEntry = directoryEntry;
}