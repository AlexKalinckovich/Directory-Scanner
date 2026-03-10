using Directory_Scanner.Core.FileModels;
using Directory_Scanner.UI.Converters;
using Directory_Scanner.UI.Model;

namespace Directory_Scanner.Tests.ViewModel;

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

[TestFixture]
public class FileEntryToViewModelConverterTests
{
    private string _testDirectory;

    [SetUp]
    public void SetUp()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"converter_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Test]
    public void ConvertToViewModels_NullEntry_ShouldReturnEmptyCollection()
    {
        ObservableCollection<FileEntryViewModel> result = FileEntryToViewModelConverter.ConvertToViewModels(null);

        result.Should().NotBeNull();
        result.Count.Should().Be(0);
    }

    [Test]
    public void ConvertToViewModels_SingleFile_ShouldCreateViewModel()
    {
        string filePath = Path.Combine(_testDirectory, "file.txt");
        File.WriteAllText(filePath, "Test content");

        FileInfo fileInfo = new FileInfo(filePath);
        FileEntry fileEntry = new FileEntry(fileInfo);

        ObservableCollection<FileEntryViewModel> result = FileEntryToViewModelConverter.ConvertToViewModels(fileEntry);

        result.Count.Should().Be(1);
        result[0].Name.Should().Be("file.txt");
        result[0].Size.Should().Be(fileInfo.Length);
        result[0].Type.Should().Be(FileType.File);
    }

    [Test]
    public void ConvertToViewModels_SingleDirectory_ShouldCreateViewModel()
    {
        DirectoryInfo dirInfo = new DirectoryInfo(_testDirectory);
        FileEntry dirEntry = new FileEntry(dirInfo);

        ObservableCollection<FileEntryViewModel> result = FileEntryToViewModelConverter.ConvertToViewModels(dirEntry);

        result.Count.Should().Be(1);
        result[0].Name.Should().Be(Path.GetFileName(_testDirectory));
        result[0].Type.Should().Be(FileType.Directory);
    }

    [Test]
    public void ConvertToViewModels_DirectoryWithFiles_ShouldIncludeChildren()
    {
        string file1Path = Path.Combine(_testDirectory, "file1.txt");
        string file2Path = Path.Combine(_testDirectory, "file2.txt");

        File.WriteAllText(file1Path, "Content1");
        File.WriteAllText(file2Path, "Content2");

        DirectoryInfo dirInfo = new DirectoryInfo(_testDirectory);
        FileEntry dirEntry = new FileEntry(dirInfo);

        FileInfo file1 = new FileInfo(file1Path);
        FileInfo file2 = new FileInfo(file2Path);

        FileEntry fileEntry1 = new FileEntry(file1);
        FileEntry fileEntry2 = new FileEntry(file2);

        dirEntry.AddSubDirectoryChild(fileEntry1);
        dirEntry.AddSubDirectoryChild(fileEntry2);

        ObservableCollection<FileEntryViewModel> result = FileEntryToViewModelConverter.ConvertToViewModels(dirEntry);

        result.Count.Should().Be(1);
        result[0].Children.Count.Should().Be(2);
        result[0].Children[0].Name.Should().Be("file1.txt");
        result[0].Children[1].Name.Should().Be("file2.txt");
    }

    [Test]
    public void ConvertToViewModels_NestedDirectories_ShouldBuildFullTree()
    {
        string subDir1 = Path.Combine(_testDirectory, "sub1");
        string subDir2 = Path.Combine(subDir1, "sub2");

        Directory.CreateDirectory(subDir1);
        Directory.CreateDirectory(subDir2);

        File.WriteAllText(Path.Combine(subDir1, "file1.txt"), "Content1");
        File.WriteAllText(Path.Combine(subDir2, "file2.txt"), "Content2");

        DirectoryInfo rootDirInfo = new DirectoryInfo(_testDirectory);
        FileEntry rootEntry = new FileEntry(rootDirInfo);

        DirectoryInfo subDir1Info = new DirectoryInfo(subDir1);
        DirectoryInfo subDir2Info = new DirectoryInfo(subDir2);

        FileEntry subEntry1 = new FileEntry(subDir1Info);
        FileEntry subEntry2 = new FileEntry(subDir2Info);

        FileInfo file1 = new FileInfo(Path.Combine(subDir1, "file1.txt"));
        FileInfo file2 = new FileInfo(Path.Combine(subDir2, "file2.txt"));

        FileEntry fileEntry1 = new FileEntry(file1);
        FileEntry fileEntry2 = new FileEntry(file2);

        subEntry1.AddSubDirectoryChild(fileEntry1);
        subEntry2.AddSubDirectoryChild(fileEntry2);
        subEntry1.AddSubDirectoryChild(subEntry2);
        rootEntry.AddSubDirectoryChild(subEntry1);

        ObservableCollection<FileEntryViewModel> result = FileEntryToViewModelConverter.ConvertToViewModels(rootEntry);

        result.Count.Should().Be(1);
        result[0].Children.Count.Should().Be(1);
        result[0].Children[0].Children.Count.Should().Be(2);
    }

    [Test]
    public void ConvertToViewModel_EmptyDirectory_ShouldHaveNoChildren()
    {
        DirectoryInfo dirInfo = new DirectoryInfo(_testDirectory);
        FileEntry dirEntry = new FileEntry(dirInfo);

        FileEntryViewModel result = FileEntryToViewModelConverter.ConvertToViewModel(dirEntry);

        result.Should().NotBeNull();
        result.Children.Count.Should().Be(0);
    }

    [Test]
    public void ConvertToViewModel_NullEntry_ShouldReturnNull()
    {
        FileEntryViewModel result = FileEntryToViewModelConverter.ConvertToViewModel(null);

        result.Should().BeNull();
    }

    [Test]
    public async Task ConvertToViewModelsAsync_NullEntry_ShouldReturnEmptyCollection()
    {
        ObservableCollection<FileEntryViewModel> result = await FileEntryToViewModelConverter.ConvertToViewModelsAsync(null);

        result.Should().NotBeNull();
        result.Count.Should().Be(0);
    }

    [Test]
    public async Task ConvertToViewModelsAsync_WithProgress_ShouldReportProgress()
    {
        string subDir = Path.Combine(_testDirectory, "sub");
        Directory.CreateDirectory(subDir);

        for (int i = 0; i < 10; i++)
        {
            File.WriteAllText(Path.Combine(_testDirectory, $"file{i}.txt"), $"Content{i}");
        }

        DirectoryInfo dirInfo = new DirectoryInfo(_testDirectory);
        FileEntry dirEntry = new FileEntry(dirInfo);

        for (int i = 0; i < 10; i++)
        {
            FileInfo file = new FileInfo(Path.Combine(_testDirectory, $"file{i}.txt"));
            FileEntry fileEntry = new FileEntry(file);
            dirEntry.AddSubDirectoryChild(fileEntry);
        }

        double lastProgress = 0.0;
        Progress<double> progress = new Progress<double>(value => lastProgress = value);

        ObservableCollection<FileEntryViewModel> result = await FileEntryToViewModelConverter.ConvertToViewModelsAsync(dirEntry, progress);

        result.Count.Should().Be(1);
        result[0].Children.Count.Should().Be(10);
        lastProgress.Should().BeGreaterThan(0.0);
        lastProgress.Should().BeLessThanOrEqualTo(100.0);
    }

    [Test]
    public async Task ConvertToViewModelsAsync_LargeTree_ShouldComplete()
    {
        for (int i = 0; i < 5; i++)
        {
            string subDir = Path.Combine(_testDirectory, $"dir{i}");
            Directory.CreateDirectory(subDir);

            for (int j = 0; j < 20; j++)
            {
                File.WriteAllText(Path.Combine(subDir, $"file{j}.txt"), $"Content{j}");
            }
        }

        DirectoryInfo dirInfo = new DirectoryInfo(_testDirectory);
        FileEntry dirEntry = new FileEntry(dirInfo);

        BuildFileEntries(dirInfo, dirEntry);

        ObservableCollection<FileEntryViewModel> result = await FileEntryToViewModelConverter.ConvertToViewModelsAsync(dirEntry);

        result.Count.Should().Be(1);
        result[0].Children.Count.Should().Be(5);
    }

    [Test]
    public void ConvertToViewModels_PreservesFileSize()
    {
        string filePath = Path.Combine(_testDirectory, "largefile.bin");
        byte[] content = new byte[1024 * 1024];
        File.WriteAllBytes(filePath, content);

        FileInfo fileInfo = new FileInfo(filePath);
        FileEntry fileEntry = new FileEntry(fileInfo);

        ObservableCollection<FileEntryViewModel> result = FileEntryToViewModelConverter.ConvertToViewModels(fileEntry);

        result[0].Size.Should().Be(fileInfo.Length);
        result[0].Size.Should().Be(1024 * 1024);
    }

    [Test]
    public void ConvertToViewModels_PreservesFileType()
    {
        DirectoryInfo dirInfo = new DirectoryInfo(_testDirectory);
        FileEntry dirEntry = new FileEntry(dirInfo);

        string filePath = Path.Combine(_testDirectory, "file.txt");
        File.WriteAllText(filePath, "Test");
        FileInfo fileInfo = new FileInfo(filePath);
        FileEntry fileEntry = new FileEntry(fileInfo);

        dirEntry.AddSubDirectoryChild(fileEntry);

        ObservableCollection<FileEntryViewModel> result = FileEntryToViewModelConverter.ConvertToViewModels(dirEntry);

        result[0].Type.Should().Be(FileType.Directory);
        result[0].Children[0].Type.Should().Be(FileType.File);
    }

    [Test]
    public void ConvertToViewModels_PreservesFullPath()
    {
        string filePath = Path.Combine(_testDirectory, "file.txt");
        File.WriteAllText(filePath, "Test");

        FileInfo fileInfo = new FileInfo(filePath);
        FileEntry fileEntry = new FileEntry(fileInfo);

        ObservableCollection<FileEntryViewModel> result = FileEntryToViewModelConverter.ConvertToViewModels(fileEntry);

        result[0].FullPath.Should().Be(fileInfo.FullName);
    }

    [Test]
    public void ConvertToViewModels_PreservesParentPath()
    {
        string subDir = Path.Combine(_testDirectory, "sub");
        Directory.CreateDirectory(subDir);

        string filePath = Path.Combine(subDir, "file.txt");
        File.WriteAllText(filePath, "Test");

        FileInfo fileInfo = new FileInfo(filePath);
        FileEntry fileEntry = new FileEntry(fileInfo);

        ObservableCollection<FileEntryViewModel> result = FileEntryToViewModelConverter.ConvertToViewModels(fileEntry);

        result[0].ParentPath.Should().Be(subDir);
    }

    [Test]
    public async Task ConvertToViewModelsAsync_ProgressNeverExceeds100()
    {
        for (int i = 0; i < 50; i++)
        {
            File.WriteAllText(Path.Combine(_testDirectory, $"file{i}.txt"), $"Content{i}");
        }

        DirectoryInfo dirInfo = new DirectoryInfo(_testDirectory);
        FileEntry dirEntry = new FileEntry(dirInfo);

        for (int i = 0; i < 50; i++)
        {
            FileInfo file = new FileInfo(Path.Combine(_testDirectory, $"file{i}.txt"));
            FileEntry fileEntry = new FileEntry(file);
            dirEntry.AddSubDirectoryChild(fileEntry);
        }

        double maxProgress = 0.0;
        Progress<double> progress = new Progress<double>(value =>
        {
            if (value > maxProgress)
            {
                maxProgress = value;
            }
        });

        await FileEntryToViewModelConverter.ConvertToViewModelsAsync(dirEntry, progress);

        maxProgress.Should().BeLessThanOrEqualTo(100.0);
    }

    [Test]
    public async Task ConvertToViewModelsAsync_DeepNesting_ShouldHandleCorrectly()
    {
        string currentPath = _testDirectory;
        List<string> allPaths = new List<string>();

        for (int i = 0; i < 10; i++)
        {
            currentPath = Path.Combine(currentPath, $"level{i}");
            Directory.CreateDirectory(currentPath);
            File.WriteAllText(Path.Combine(currentPath, $"file{i}.txt"), $"Content{i}");
            allPaths.Add(currentPath);
        }

        DirectoryInfo rootDirInfo = new DirectoryInfo(_testDirectory);
        FileEntry rootEntry = new FileEntry(rootDirInfo);

        BuildDeepFileEntriesChain(rootEntry, allPaths, _testDirectory);

        ObservableCollection<FileEntryViewModel> result = await FileEntryToViewModelConverter.ConvertToViewModelsAsync(rootEntry);

        result.Count.Should().Be(1);
        VerifyDepth(result[0], 10);
    }


    [Test]
    public void ConvertToViewModels_ObservableCollection_ShouldBeModifiable()
    {
        DirectoryInfo dirInfo = new DirectoryInfo(_testDirectory);
        FileEntry dirEntry = new FileEntry(dirInfo);

        ObservableCollection<FileEntryViewModel> result = FileEntryToViewModelConverter.ConvertToViewModels(dirEntry);

        Action act = () => result.Add(new FileEntryViewModel(dirEntry));

        act.Should().NotThrow();
    }

    [Test]
    public async Task ConvertToViewModelsAsync_NoProgress_ShouldNotThrow()
    {
        DirectoryInfo dirInfo = new DirectoryInfo(_testDirectory);
        FileEntry dirEntry = new FileEntry(dirInfo);

        Func<Task> act = async () => await FileEntryToViewModelConverter.ConvertToViewModelsAsync(dirEntry, null);

        await act.Should().NotThrowAsync();
    }

    private void BuildFileEntries(DirectoryInfo dirInfo, FileEntry dirEntry)
    {
        foreach (DirectoryInfo subDir in dirInfo.EnumerateDirectories())
        {
            FileEntry subEntry = new FileEntry(subDir);

            foreach (FileInfo file in subDir.EnumerateFiles())
            {
                FileEntry fileEntry = new FileEntry(file);
                subEntry.AddSubDirectoryChild(fileEntry);
            }

            dirEntry.AddSubDirectoryChild(subEntry);
        }
    }

    private void BuildDeepFileEntries(DirectoryInfo dirInfo, FileEntry dirEntry, int depth)
    {
        if (depth <= 0)
        {
            return;
        }

        foreach (DirectoryInfo subDir in dirInfo.EnumerateDirectories())
        {
            FileEntry subEntry = new FileEntry(subDir);

            foreach (FileInfo file in subDir.EnumerateFiles())
            {
                FileEntry fileEntry = new FileEntry(file);
                subEntry.AddSubDirectoryChild(fileEntry);
            }

            dirEntry.AddSubDirectoryChild(subEntry);

            BuildDeepFileEntries(subDir, subEntry, depth - 1);
        }
    }

    private void BuildDeepFileEntriesChain(FileEntry rootEntry, List<string> allPaths, string basePath)
    {
        FileEntry currentEntry = rootEntry;
        string currentPath = basePath;

        foreach (string path in allPaths)
        {
            DirectoryInfo dirInfo = new DirectoryInfo(path);
            FileEntry dirEntry = new FileEntry(dirInfo);

            FileInfo fileInfo = new FileInfo(Path.Combine(path, $"file{Path.GetFileName(path)}.txt"));
            if (fileInfo.Exists)
            {
                FileEntry fileEntry = new FileEntry(fileInfo);
                dirEntry.AddSubDirectoryChild(fileEntry);
            }

            currentEntry.AddSubDirectoryChild(dirEntry);
            currentEntry = dirEntry;
        }
    }

    private void VerifyDepth(FileEntryViewModel viewModel, int expectedDepth)
    {
        int actualDepth = 0;
        FileEntryViewModel current = viewModel;

        while (current.Children.Count > 0)
        {
            actualDepth++;
            current = current.Children[0];
        }

        actualDepth.Should().Be(expectedDepth);
    }
}