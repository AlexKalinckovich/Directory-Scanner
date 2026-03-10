using System.Collections.Generic;
using Directory_Scanner.Core.FileModels;

namespace Directory_Scanner.Core.Core;

public sealed class DirectorySizeCalculator
{
    public void CalculateDirectorySizes(FileEntry rootEntry)
    {
        long totalSize = CalculateSizeRecursive(rootEntry);
        CalculatePercentagesRecursive(rootEntry, totalSize);
    }

    private long CalculateSizeRecursive(FileEntry entry)
    {
        if (entry.FileType == FileType.File)
        {
            return entry.FileSize;
        }

        long totalSize = 0;
        IReadOnlyList<FileEntry> subDirectories = entry.SubDirectories;

        foreach (FileEntry subDir in subDirectories)
        {
            long subSize = CalculateSizeRecursive(subDir);
            totalSize += subSize;
        }

        entry.FileSize = totalSize;

        return totalSize;
    }

    private void CalculatePercentagesRecursive(FileEntry entry, long totalSize)
    {
        if (totalSize <= 0)
        {
            entry.Percentage = 0.0;
        }
        else
        {
            entry.Percentage = (double)entry.FileSize / totalSize * 100.0;
        }

        IReadOnlyList<FileEntry> subDirectories = entry.SubDirectories;

        foreach (FileEntry subDir in subDirectories)
        {
            CalculatePercentagesRecursive(subDir, totalSize);
        }
    }
}