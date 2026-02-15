
namespace Directory_Scanner.Core.FileModels;

public sealed class FileEntry
{
    
    public FileType FileType { get; }
    public string FileName { get; }
    public string FullPath { get; }
    public long FileSize { get; set; }
    
    public FileState State { get; set; }
    
    private List<FileEntry>? _subDirectories;

    public IReadOnlyList<FileEntry> SubDirectories => _subDirectories ??= new List<FileEntry>();
    
    public FileEntry(FileType fileType, string fileName, string fullPath, long fileSize = 0)
    {
        FileType = fileType;
        FileName = fileName;
        FullPath = fullPath;
        FileSize = fileSize;
    }
    
    public void UpdateFileSize(FileEntry child)
    {
        
        if (child.FileType == FileType.File)
        {
            FileSize += child.FileSize;
        }
    }

    public void AddSubDirectoryChild(FileEntry child)
    {
        _subDirectories ??= new List<FileEntry>();
        _subDirectories.Add(child);
    }

    public override string ToString()
    {
        return $"\n ============== \n FileType: {FileType} FileName: {FileName} \n FileSize:{FileSize} \n State:{State} \n FilePath:{FullPath} \n" +
               $"\n =============== \n";
    }
}