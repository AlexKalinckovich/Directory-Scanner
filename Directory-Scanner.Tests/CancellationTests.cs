using Directory_Scanner.Core.Core;
using FluentAssertions;
using NUnit.Framework;

namespace Directory_Scanner.Tests
{
    [TestFixture]
    public class CancellationTests
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
        public async Task ScanDirectoryAsync_ShouldStopWhenCancelled()
        {
            
            for (int i = 0; i < 1000; i++)
            {
                var file = Path.Combine(_rootPath, $"f{i}.txt");
                await File.WriteAllTextAsync(file, "data");
            }

            var scanner = new DirectoryScanner();
            var cts = new CancellationTokenSource();
            var processedFiles = 0;

            scanner.FileProcessed += (s, e) =>
            {
                processedFiles++;
                if (processedFiles == 50)
                    cts.Cancel();
            };

            Func<Task> act = async () => await scanner.ScanDirectoryAsync(_rootPath, cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            processedFiles.Should().BeLessThan(1000);
        }
    }
}