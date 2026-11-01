using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Extensions.Contracts;
using PCL.Extensions.ScreenshotManager;

namespace ScreenshotManager.Tests;

[TestClass]
public sealed class ScreenshotScannerTest
{
    [TestMethod]
    public void ResolveDate_ParsesMinecraftFilename()
    {
        var date = ScreenshotScanner.ResolveDate(@"C:\shots\2025-11-01_12.34.56.png", true);
        Assert.AreEqual(new DateTime(2025, 11, 1, 12, 34, 56), date);
    }

    [TestMethod]
    public void ResolveDate_ParsesDateOnlyFilename()
    {
        var date = ScreenshotScanner.ResolveDate(@"C:\shots\2025-11-01.png", true);
        Assert.AreEqual(new DateTime(2025, 11, 1), date);
    }

    [TestMethod]
    public void Scan_FindsVersionAndSharedScreenshots()
    {
        var root = Path.Combine(Path.GetTempPath(), "screenshot-scan-" + Guid.NewGuid().ToString("N"));
        try
        {
            var versionDir = Path.Combine(root, "versions", "1.20.1");
            Directory.CreateDirectory(Path.Combine(versionDir, "screenshots"));
            Directory.CreateDirectory(Path.Combine(root, "screenshots"));
            File.WriteAllText(Path.Combine(versionDir, "screenshots", "2025-11-01_12.34.56.png"), "x");
            File.WriteAllText(Path.Combine(root, "screenshots", "shared.png"), "x");
            var instance = new LauncherInstance(versionDir, "1.20.1", versionDir, "1.20.1", false);

            var result = ScreenshotScanner.Scan([instance], includeShared: true, dateFromFilename: true,
                CancellationToken.None);

            Assert.HasCount(1, result.Groups.Where(group => !group.IsShared));
            Assert.AreEqual("1.20.1", result.Groups.First(group => !group.IsShared).Name);
            Assert.HasCount(1, result.Groups.Where(group => group.IsShared));
            var shared = result.Items.Single(item => item.IsShared);
            Assert.AreEqual("shared.png", shared.FileName);
            var version = result.Items.Single(item => !item.IsShared);
            Assert.AreEqual(new DateTime(2025, 11, 1, 12, 34, 56), version.TakenAt);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public void Scan_IgnoresMissingFoldersAndNonImages()
    {
        var root = Path.Combine(Path.GetTempPath(), "screenshot-scan-" + Guid.NewGuid().ToString("N"));
        try
        {
            var versionDir = Path.Combine(root, "versions", "1.20.1", "screenshots");
            Directory.CreateDirectory(versionDir);
            File.WriteAllText(Path.Combine(versionDir, "notes.txt"), "x");
            var instance = new LauncherInstance(Path.GetDirectoryName(versionDir)!, "1.20.1",
                Path.GetDirectoryName(versionDir)!, "1.20.1", false);

            var result = ScreenshotScanner.Scan([instance], includeShared: false, dateFromFilename: true,
                CancellationToken.None);

            Assert.HasCount(0, result.Items);
            Assert.HasCount(0, result.Groups);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}