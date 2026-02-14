using Directory_Scanner.Core.FileModels;

namespace Directory_Scanner.Core.Event;

public class ProcessingCompletedEventArgs : EventArgs
{
    public FileEntry FileEntry { get; }
    public ProcessingCompletedEventArgs(FileEntry fileEntry) => FileEntry = fileEntry;
}