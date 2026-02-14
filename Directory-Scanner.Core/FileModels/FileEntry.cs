
namespace Directory_Scanner.Core.FileModels;

public class FileEntry
{
    public FileType FileType { get; }
    public string FileName { get; }
    public string FullPath { get; }
    public long FileSize { get; private set; }
    
    public FileState State { get; set; }
    public List<FileEntry> Children { get; }
    
    public FileEntry(FileType fileType, string fileName, string fullPath, long fileSize = 0)
    {
        FileType = fileType;
        FileName = fileName;
        FullPath = fullPath;
        FileSize = fileSize;
        Children = new List<FileEntry>();
    }
    
    public void AddChild(FileEntry child)
    {
        Children.Add(child);
        
        if (child.FileType == FileType.File)
        {
            FileSize += child.FileSize;
        }
    }

    public override string ToString()
    {
        return $"\n ============== \n FileType: {FileType} FileName: {FileName} \n FileSize:{FileSize} \n State:{State} \n FilePath:{FullPath} \n" +
               $"\n =============== \n";
    }
}