using Directory_Scanner.Core.Core;
using Directory_Scanner.Core.FileModels;
using Directory_Scanner.Core.ScannerEventArgs;
using FluentAssertions;
using NUnit.Framework;

namespace Directory_Scanner.Tests
{
    [TestFixture]
    public class FileProcessedEventTests
    {
        private string _rootPath;
        private DirectoryInfo _rootDir;

        [SetUp]
        public void Setup()
        {
            _rootPath = Path.Combine(Path.GetTempPath(), "ScannerTests_" + Guid.NewGuid().ToString("N"));
            _rootDir = new DirectoryInfo(_rootPath);
            _rootDir.Create();
        }

        [TearDown]
        public void Cleanup()
        {
            if (_rootDir.Exists)
                _rootDir.Delete(true);
        }

        [Test]
        public async Task ShouldFireForEachFileInRoot()
        {
            var file1 = Path.Combine(_rootPath, "a.txt");
            var file2 = Path.Combine(_rootPath, "b.dat");
            await File.WriteAllTextAsync(file1, "hello");
            await File.WriteAllTextAsync(file2, new string('X', 200));

            var scanner = new DirectoryScanner();
            var events = new List<FileProcessedEventArgs>();
            scanner.FileProcessed += (s, e) => events.Add(e);

            await scanner.ScanDirectoryAsync(_rootPath);

            events.Should().HaveCount(2);
            events.Select(e => e.FileEntry.FileName).Should().BeEquivalentTo("a.txt", "b.dat");
        }

        [Test]
        public async Task ShouldReportCorrectFileSize()
        {
            var filePath = Path.Combine(_rootPath, "data.bin");
            await File.WriteAllBytesAsync(filePath, new byte[1234]);

            var scanner = new DirectoryScanner();
            FileProcessedEventArgs? captured = null;
            scanner.FileProcessed += (s, e) => captured = e;

            await scanner.ScanDirectoryAsync(_rootPath);

            captured.Should().NotBeNull();
            captured?.FileEntry.FileSize.Should().Be(1234);
            captured?.FileEntry.FileType.Should().Be(FileType.File);
            captured?.FileEntry.FileState.Should().Be(FileState.Ok);
        }

        [Test]
        public async Task ShouldFireForFilesInSubdirectories()
        {
            var sub = _rootDir.CreateSubdirectory("sub");
            var file1 = Path.Combine(_rootPath, "root.txt");
            var file2 = Path.Combine(sub.FullName, "sub.txt");
            await File.WriteAllTextAsync(file1, "root");
            await File.WriteAllTextAsync(file2, "sub");

            var scanner = new DirectoryScanner();
            var events = new List<FileProcessedEventArgs>();
            scanner.FileProcessed += (s, e) => events.Add(e);

            await scanner.ScanDirectoryAsync(_rootPath);

            events.Should().HaveCount(2);
            events.Select(e => e.FileEntry.FullPath).Should().Contain(file1).And.Contain(file2);
        }
    }
}