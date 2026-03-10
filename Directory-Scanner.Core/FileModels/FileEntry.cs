namespace Directory_Scanner.Core.FileModels;

public sealed class FileEntry
{
    public FileType FileType { get; }
    public string FileName { get; }
    public string? ParentPath { get; }
    public string FullPath { get; }
    public long FileSize { get; set; }
    public FileState FileState { get; set; }
    private List<FileEntry>? _subDirectories;
    public IReadOnlyList<FileEntry> SubDirectories => _subDirectories ??= new List<FileEntry>();

    public FileEntry(DirectoryInfo directoryInfo)
    {
        FullPath = directoryInfo.FullName;
        FileName = directoryInfo.Name;
        ParentPath = directoryInfo.Parent?.FullName ?? string.Empty;
        FileSize = 0;
        FileType = FileType.Directory;
        FileState = FileState.Ok;
    }

    public FileEntry(FileInfo fileInfo)
    {
        FullPath = fileInfo.FullName;
        FileName = fileInfo.Name;
        ParentPath = fileInfo.DirectoryName;
        FileSize = fileInfo.Length;
        FileType = FileType.File;
        FileState = FileState.Ok;
    }

    public void AddSubDirectoryChild(FileEntry child)
    {
        _subDirectories ??= new List<FileEntry>();
        _subDirectories.Add(child);
    }

    
}