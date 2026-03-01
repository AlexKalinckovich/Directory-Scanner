using Directory_Scanner.Core.Core;
using Directory_Scanner.Core.FileModels;
using Directory_Scanner.Core.ScannerEventArgs;
using FluentAssertions;
using NUnit.Framework;

namespace Directory_Scanner.Tests
{
    [TestFixture]
    public class ErrorHandlingTests
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
        public void ScanDirectoryAsync_WithNullPath_ShouldThrowArgumentException()
        {
            DirectoryScanner scanner = new DirectoryScanner();
            Func<Task> act = async () => await scanner.ScanDirectoryAsync(null!);
            act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("*Path cannot be null or empty*");
        }

        [Test]
        public void ScanDirectoryAsync_WithEmptyPath_ShouldThrowArgumentException()
        {
            DirectoryScanner scanner = new DirectoryScanner();
            Func<Task> act = async () => await scanner.ScanDirectoryAsync("   ");
            act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("*Path cannot be null or empty*");
        }

        [Test]
        public void ScanDirectoryAsync_WithNonExistentPath_ShouldThrowDirectoryNotFoundException()
        {
            var scanner = new DirectoryScanner();
            var badPath = Path.Combine(_rootPath, "ghost");
            Func<Task> act = async () => await scanner.ScanDirectoryAsync(badPath);
            act.Should().ThrowAsync<DirectoryNotFoundException>()
                .WithMessage($"*Directory not found: {badPath}*");
        }

        [Test]
        public async Task AccessDenied_ShouldMarkDirectoryStateAsAccessDenied()
        {
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows))
            {
                Assert.Ignore("Test only runs on Windows");
                return;
            }

            var denyDir = _rootDir.CreateSubdirectory("deny");
            var filePath = Path.Combine(denyDir.FullName, "secret.txt");
            await File.WriteAllTextAsync(filePath, "data");

            try
            {
                
                var dirSecurity = denyDir.GetAccessControl();
                var currentUser = System.Security.Principal.WindowsIdentity.GetCurrent().User;
                if (currentUser == null) Assert.Ignore("No current user");
                dirSecurity.AddAccessRule(
                    new System.Security.AccessControl.FileSystemAccessRule(
                        currentUser,
                        System.Security.AccessControl.FileSystemRights.Read,
                        System.Security.AccessControl.AccessControlType.Deny));
                denyDir.SetAccessControl(dirSecurity);

                var scanner = new DirectoryScanner();
                DirectoryProcessedEventArgs? processed = null;
                scanner.DirectoryProcessed += (s, e) =>
                {
                    if (e.DirectoryEntry.FileName == "deny")
                        processed = e;
                };

                await scanner.ScanDirectoryAsync(_rootPath);

                processed.Should().NotBeNull();
                processed?.DirectoryEntry.FileState.Should().Be(FileState.AccessDenied);
                processed?.DirectoryEntry.FileSize.Should().Be(0);
            }
            finally
            {
                try
                {
                    var dirSecurity = denyDir.GetAccessControl();
                    var currentUser = System.Security.Principal.WindowsIdentity.GetCurrent().User;
                    if (currentUser != null)
                    {
                        dirSecurity.RemoveAccessRule(
                            new System.Security.AccessControl.FileSystemAccessRule(
                                currentUser,
                                System.Security.AccessControl.FileSystemRights.Read,
                                System.Security.AccessControl.AccessControlType.Deny));
                        denyDir.SetAccessControl(dirSecurity);
                    }
                }
                catch { }
            }
        }
    }
}