using Directory_Scanner.Core.FileModels;

namespace Directory_Scanner.Core.ScannerEventArgs;

public class ProcessingCompletedEventArgs : EventArgs
{
    public FileEntry FileEntry { get; }
    public ProcessingCompletedEventArgs(FileEntry fileEntry) => FileEntry = fileEntry;
}