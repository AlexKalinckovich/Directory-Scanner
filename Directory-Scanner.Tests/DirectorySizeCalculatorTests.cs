using Directory_Scanner.Core.Core;
using Directory_Scanner.Core.FileModels;
using FluentAssertions;
using NUnit.Framework;

namespace Directory_Scanner.Tests;

[TestFixture]
public class DirectorySizeCalculatorTests
{
    private DirectorySizeCalculator _calculator;

    [SetUp]
    public void SetUp()
    {
        _calculator = new DirectorySizeCalculator();
    }

    [Test]
    public void CalculateDirectorySizes_SingleFile_ShouldSetCorrectSizeAndPercentage()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"test_file_{Guid.NewGuid()}.txt");
        File.WriteAllText(tempFile, "Hello World");

        try
        {
            FileInfo fileInfo = new FileInfo(tempFile);
            FileEntry fileEntry = new FileEntry(fileInfo);

            _calculator.CalculateDirectorySizes(fileEntry);

            fileEntry.FileSize.Should().Be(fileInfo.Length);
            fileEntry.Percentage.Should().Be(100.0);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Test]
    public void CalculateDirectorySizes_EmptyDirectory_ShouldHaveZeroSizeAndPercentage()
    {
        DirectoryInfo dirInfo = new DirectoryInfo(Path.GetTempPath());
        FileEntry dirEntry = new FileEntry(dirInfo);

        _calculator.CalculateDirectorySizes(dirEntry);

        dirEntry.FileSize.Should().Be(0);
        dirEntry.Percentage.Should().Be(0.0);
    }

    [Test]
    public void CalculateDirectorySizes_DirectoryWithFiles_ShouldSumFileSizesAndCalculatePercentage()
    {
        string testDir = Path.Combine(Path.GetTempPath(), $"test_dir_{System.Guid.NewGuid()}");
        Directory.CreateDirectory(testDir);

        try
        {
            string file1Path = Path.Combine(testDir, "file1.txt");
            string file2Path = Path.Combine(testDir, "file2.txt");

            File.WriteAllText(file1Path, "12345");
            File.WriteAllText(file2Path, "1234567890");

            DirectoryInfo dirInfo = new DirectoryInfo(testDir);
            FileEntry dirEntry = new FileEntry(dirInfo);

            FileInfo file1 = new FileInfo(file1Path);
            FileInfo file2 = new FileInfo(file2Path);

            FileEntry fileEntry1 = new FileEntry(file1);
            FileEntry fileEntry2 = new FileEntry(file2);

            dirEntry.AddSubDirectoryChild(fileEntry1);
            dirEntry.AddSubDirectoryChild(fileEntry2);

            _calculator.CalculateDirectorySizes(dirEntry);

            long expectedSize = file1.Length + file2.Length;
            dirEntry.FileSize.Should().Be(expectedSize);
            dirEntry.Percentage.Should().Be(100.0);

            double expectedPercentage1 = (double)file1.Length / expectedSize * 100.0;
            double expectedPercentage2 = (double)file2.Length / expectedSize * 100.0;

            fileEntry1.Percentage.Should().BeApproximately(expectedPercentage1, 0.01);
            fileEntry2.Percentage.Should().BeApproximately(expectedPercentage2, 0.01);
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
    }

    [Test]
    public void CalculateDirectorySizes_NestedDirectories_ShouldCalculateRecursiveSumAndPercentage()
    {
        string rootDir = Path.Combine(Path.GetTempPath(), $"root_{System.Guid.NewGuid()}");
        string subDir1 = Path.Combine(rootDir, "sub1");
        string subDir2 = Path.Combine(rootDir, "sub2");

        Directory.CreateDirectory(subDir1);
        Directory.CreateDirectory(subDir2);

        try
        {
            string file1Path = Path.Combine(subDir1, "file1.txt");
            string file2Path = Path.Combine(subDir2, "file2.txt");

            File.WriteAllText(file1Path, "12345");
            File.WriteAllText(file2Path, "1234567890");

            DirectoryInfo rootDirInfo = new DirectoryInfo(rootDir);
            FileEntry rootEntry = new FileEntry(rootDirInfo);

            DirectoryInfo subDir1Info = new DirectoryInfo(subDir1);
            DirectoryInfo subDir2Info = new DirectoryInfo(subDir2);

            FileEntry subEntry1 = new FileEntry(subDir1Info);
            FileEntry subEntry2 = new FileEntry(subDir2Info);

            FileInfo file1 = new FileInfo(file1Path);
            FileInfo file2 = new FileInfo(file2Path);

            FileEntry fileEntry1 = new FileEntry(file1);
            FileEntry fileEntry2 = new FileEntry(file2);

            subEntry1.AddSubDirectoryChild(fileEntry1);
            subEntry2.AddSubDirectoryChild(fileEntry2);

            rootEntry.AddSubDirectoryChild(subEntry1);
            rootEntry.AddSubDirectoryChild(subEntry2);

            _calculator.CalculateDirectorySizes(rootEntry);

            long expectedSize = file1.Length + file2.Length;
            rootEntry.FileSize.Should().Be(expectedSize);
            rootEntry.Percentage.Should().Be(100.0);

            double expectedSub1Percentage = (double)file1.Length / expectedSize * 100.0;
            double expectedSub2Percentage = (double)file2.Length / expectedSize * 100.0;

            subEntry1.Percentage.Should().BeApproximately(expectedSub1Percentage, 0.01);
            subEntry2.Percentage.Should().BeApproximately(expectedSub2Percentage, 0.01);

            fileEntry1.Percentage.Should().BeApproximately(expectedSub1Percentage, 0.01);
            fileEntry2.Percentage.Should().BeApproximately(expectedSub2Percentage, 0.01);
        }
        finally
        {
            if (Directory.Exists(rootDir))
            {
                Directory.Delete(rootDir, true);
            }
        }
    }

    [Test]
    public void CalculateDirectorySizes_DeepNesting_ShouldHandleCorrectly()
    {
        string level0 = Path.Combine(Path.GetTempPath(), $"l0_{System.Guid.NewGuid()}");
        string level1 = Path.Combine(level0, "l1");
        string level2 = Path.Combine(level1, "l2");

        Directory.CreateDirectory(level2);

        try
        {
            string filePath = Path.Combine(level2, "file.txt");
            File.WriteAllText(filePath, "1234567890");

            DirectoryInfo level0Info = new DirectoryInfo(level0);
            FileEntry entry0 = new FileEntry(level0Info);

            DirectoryInfo level1Info = new DirectoryInfo(level1);
            FileEntry entry1 = new FileEntry(level1Info);

            DirectoryInfo level2Info = new DirectoryInfo(level2);
            FileEntry entry2 = new FileEntry(level2Info);

            FileInfo file = new FileInfo(filePath);
            FileEntry fileEntry = new FileEntry(file);

            entry2.AddSubDirectoryChild(fileEntry);
            entry1.AddSubDirectoryChild(entry2);
            entry0.AddSubDirectoryChild(entry1);

            _calculator.CalculateDirectorySizes(entry0);

            entry0.FileSize.Should().Be(file.Length);
            entry1.FileSize.Should().Be(file.Length);
            entry2.FileSize.Should().Be(file.Length);

            entry0.Percentage.Should().Be(100.0);
            entry1.Percentage.Should().Be(100.0);
            entry2.Percentage.Should().Be(100.0);
            fileEntry.Percentage.Should().Be(100.0);
        }
        finally
        {
            if (Directory.Exists(level0))
            {
                Directory.Delete(level0, true);
            }
        }
    }

    [Test]
    public void CalculateDirectorySizes_MultipleFilesInMultipleDirs_ShouldSumAllAndCalculatePercentages()
    {
        string rootDir = Path.Combine(Path.GetTempPath(), $"root_{System.Guid.NewGuid()}");
        Directory.CreateDirectory(rootDir);

        try
        {
            long totalExpectedSize = 0;
            List<FileEntry> allFileEntries = new List<FileEntry>();
            List<FileEntry> allSubEntries = new List<FileEntry>();

            DirectoryInfo rootDirInfo = new DirectoryInfo(rootDir);
            FileEntry rootEntry = new FileEntry(rootDirInfo);  

            for (int i = 0; i < 5; i++)
            {
                string subDirPath = Path.Combine(rootDir, $"dir{i}");
                Directory.CreateDirectory(subDirPath);

                DirectoryInfo subDirInfo = new DirectoryInfo(subDirPath);
                FileEntry subEntry = new FileEntry(subDirInfo);

                for (int j = 0; j < 3; j++)
                {
                    string filePath = Path.Combine(subDirPath, $"file{j}.txt");
                    File.WriteAllText(filePath, new string('x', (i + 1) * 10));

                    FileInfo file = new FileInfo(filePath);
                    FileEntry fileEntry = new FileEntry(file);
                    subEntry.AddSubDirectoryChild(fileEntry);

                    totalExpectedSize += file.Length;
                    allFileEntries.Add(fileEntry);
                }

                rootEntry.AddSubDirectoryChild(subEntry);  
                allSubEntries.Add(subEntry);
            }

            _calculator.CalculateDirectorySizes(rootEntry);  

            rootEntry.FileSize.Should().Be(totalExpectedSize);  
            rootEntry.Percentage.Should().Be(100.0);

            foreach (FileEntry subEntry in allSubEntries)
            {
                subEntry.Percentage.Should().BeGreaterThan(0);
            }

            foreach (FileEntry fileEntry in allFileEntries)
            {
                double expectedPercentage = (double)fileEntry.FileSize / totalExpectedSize * 100.0;
                fileEntry.Percentage.Should().BeApproximately(expectedPercentage, 0.01);
            }
        }
        finally
        {
            if (Directory.Exists(rootDir))
            {
                Directory.Delete(rootDir, true);
            }
        }
    }

    [Test]
    public void CalculateDirectorySizes_NullSubDirectories_ShouldNotThrow()
    {
        DirectoryInfo dirInfo = new DirectoryInfo(Path.GetTempPath());
        FileEntry dirEntry = new FileEntry(dirInfo);

        Action act = () => _calculator.CalculateDirectorySizes(dirEntry);

        act.Should().NotThrow();
        dirEntry.FileSize.Should().Be(0);
        dirEntry.Percentage.Should().Be(0.0);
    }

    [Test]
    public void CalculateDirectorySizes_ZeroTotalSize_ShouldSetPercentageToZero()
    {
        DirectoryInfo dirInfo = new DirectoryInfo(Path.GetTempPath());
        FileEntry dirEntry = new FileEntry(dirInfo);

        FileEntry childEntry = new FileEntry(dirInfo);
        childEntry.FileSize = 0;
        dirEntry.AddSubDirectoryChild(childEntry);

        _calculator.CalculateDirectorySizes(dirEntry);

        dirEntry.Percentage.Should().Be(0.0);
        childEntry.Percentage.Should().Be(0.0);
    }

    [Test]
    public void CalculateDirectorySizes_LargeFiles_ShouldCalculatePercentageAccurately()
    {
        string testDir = Path.Combine(Path.GetTempPath(), $"large_test_{System.Guid.NewGuid()}");
        Directory.CreateDirectory(testDir);

        try
        {
            string file1Path = Path.Combine(testDir, "large1.bin");
            string file2Path = Path.Combine(testDir, "large2.bin");

            byte[] content1 = new byte[1024 * 1024];
            byte[] content2 = new byte[2 * 1024 * 1024];

            File.WriteAllBytes(file1Path, content1);
            File.WriteAllBytes(file2Path, content2);

            DirectoryInfo dirInfo = new DirectoryInfo(testDir);
            FileEntry dirEntry = new FileEntry(dirInfo);

            FileInfo file1 = new FileInfo(file1Path);
            FileInfo file2 = new FileInfo(file2Path);

            FileEntry fileEntry1 = new FileEntry(file1);
            FileEntry fileEntry2 = new FileEntry(file2);

            dirEntry.AddSubDirectoryChild(fileEntry1);
            dirEntry.AddSubDirectoryChild(fileEntry2);

            _calculator.CalculateDirectorySizes(dirEntry);

            long expectedSize = file1.Length + file2.Length;
            dirEntry.FileSize.Should().Be(expectedSize);

            double expectedPercentage1 = (double)file1.Length / expectedSize * 100.0;
            double expectedPercentage2 = (double)file2.Length / expectedSize * 100.0;

            fileEntry1.Percentage.Should().BeApproximately(expectedPercentage1, 0.01);
            fileEntry2.Percentage.Should().BeApproximately(expectedPercentage2, 0.01);

            expectedPercentage1.Should().BeApproximately(33.33, 0.01);
            expectedPercentage2.Should().BeApproximately(66.67, 0.01);
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
    }

    [Test]
    public void CalculateDirectorySizes_PercentageSum_ShouldBeApproximately100()
    {
        string testDir = Path.Combine(Path.GetTempPath(), $"sum_test_{System.Guid.NewGuid()}");
        Directory.CreateDirectory(testDir);

        try
        {
            DirectoryInfo dirInfo = new DirectoryInfo(testDir);
            FileEntry dirEntry = new FileEntry(dirInfo);

            double totalPercentage = 0.0;

            for (int i = 0; i < 10; i++)
            {
                string filePath = Path.Combine(testDir, $"file{i}.txt");
                File.WriteAllText(filePath, new string('x', (i + 1) * 100));

                FileInfo file = new FileInfo(filePath);
                FileEntry fileEntry = new FileEntry(file);
                dirEntry.AddSubDirectoryChild(fileEntry);
            }

            _calculator.CalculateDirectorySizes(dirEntry);

            foreach (FileEntry child in dirEntry.SubDirectories)
            {
                totalPercentage += child.Percentage;
            }

            totalPercentage.Should().BeApproximately(100.0, 0.1);
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
    }
}