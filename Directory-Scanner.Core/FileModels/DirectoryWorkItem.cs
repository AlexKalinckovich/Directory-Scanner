namespace Directory_Scanner.Core.FileModels;

public readonly struct DirectoryWorkItem
{
    public DirectoryInfo DirInfo { get; }
    public FileEntry DirEntry { get; }

    public DirectoryWorkItem(DirectoryInfo dirInfo, FileEntry dirEntry)
    {
        DirInfo = dirInfo;
        DirEntry = dirEntry;
    }
}