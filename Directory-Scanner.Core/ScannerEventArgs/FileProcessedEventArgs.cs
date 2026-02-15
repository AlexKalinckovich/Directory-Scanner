using Directory_Scanner.Core.FileModels;

namespace Directory_Scanner.Core.ScannerEventArgs;

public class FileProcessedEventArgs : EventArgs
{
    public FileEntry FileEntry { get; }
    public FileProcessedEventArgs(FileEntry fileEntry) => FileEntry = fileEntry;
}