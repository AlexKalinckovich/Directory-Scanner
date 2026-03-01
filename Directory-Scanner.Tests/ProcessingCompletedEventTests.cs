using Directory_Scanner.Core.Core;
using Directory_Scanner.Core.ScannerEventArgs;
using FluentAssertions;
using NUnit.Framework;

namespace Directory_Scanner.Tests
{
    [TestFixture]
    public class ProcessingCompletedEventTests
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
        public async Task ShouldFireOnceAtEnd()
        {
            _rootDir.CreateSubdirectory("sub");
            var scanner = new DirectoryScanner();
            var fired = 0;
            ProcessingCompletedEventArgs? args = null;
            scanner.ProcessingCompleted += (s, e) =>
            {
                fired++;
                args = e;
            };

            var result = await scanner.ScanDirectoryAsync(_rootPath);

            fired.Should().Be(1);
            args.Should().NotBeNull();
            args?.FileEntry.Should().BeSameAs(result);
        }
    }
}