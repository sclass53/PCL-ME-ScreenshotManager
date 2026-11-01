using System.Globalization;
using PCL.Extensions.Contracts;

namespace PCL.Extensions.ScreenshotManager;

internal static class ScreenshotScanner
{
    private static readonly string[] ImageExtensions =
        [".png", ".jpg", ".jpeg", ".bmp", ".webp", ".tif", ".tiff"];

    public static async Task<ScreenshotScanResult> ScanAsync(IExtensionContext context, CancellationToken cancellationToken)
    {
        var includeShared = context.Settings.Get("include-shared", true);
        var dateFromFilename = context.Settings.Get("date-from-filename", true);
        IReadOnlyList<LauncherInstance> instances;
        try
        {
            instances = await context.Launcher.GetInstancesAsync(cancellationToken);
        }
        catch
        {
            instances = [];
        }

        return await Task.Run(() => Scan(instances, includeShared, dateFromFilename, cancellationToken), cancellationToken);
    }

    internal static ScreenshotScanResult Scan(
        IReadOnlyList<LauncherInstance> instances,
        bool includeShared,
        bool dateFromFilename,
        CancellationToken cancellationToken)
    {
        var items = new List<ScreenshotItem>();
        var groups = new List<ScreenshotGroup>();
        var errors = new List<string>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sharedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var instance in instances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var versionId = instance.Directory;
            var versionName = string.IsNullOrWhiteSpace(instance.Name) ? Path.GetFileName(instance.Directory) : instance.Name;
            var before = items.Count;

            AddDirectory(Path.Combine(instance.Directory, "screenshots"), versionId, versionName);
            AddDirectory(Path.Combine(instance.Directory, ".minecraft", "screenshots"), versionId, versionName);

            var versionsRoot = Directory.GetParent(instance.Directory)?.FullName;
            var launcherRoot = versionsRoot is null ? null : Directory.GetParent(versionsRoot)?.FullName;
            if (launcherRoot is not null)
            {
                sharedRoots.Add(launcherRoot);
            }

            var added = items.Count - before;
            if (added > 0)
            {
                groups.Add(new ScreenshotGroup(versionId, versionName, false, added));
            }
        }

        if (sharedRoots.Count == 0)
        {
            var fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ".minecraft");
            if (Directory.Exists(fallback))
            {
                sharedRoots.Add(fallback);
            }
        }

        if (includeShared)
        {
            var beforeShared = items.Count;
            foreach (var root in sharedRoots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddDirectory(Path.Combine(root, "screenshots"), null, null);
            }

            var sharedCount = items.Count - beforeShared;
            if (sharedCount > 0)
            {
                groups.Add(new ScreenshotGroup(null, "Shared", true, sharedCount));
            }
        }

        groups.Sort((left, right) =>
        {
            if (left.IsShared != right.IsShared)
            {
                return left.IsShared ? -1 : 1;
            }

            return string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
        });
        items.Sort((left, right) => right.TakenAt.CompareTo(left.TakenAt));

        return new ScreenshotScanResult
        {
            Items = items,
            Groups = groups,
            Errors = errors
        };

        void AddDirectory(string directory, string? versionId, string? versionName)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            try
            {
                var options = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = false,
                    ReturnSpecialDirectories = false
                };

                foreach (var path in Directory.EnumerateFiles(directory, "*", options))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var extension = Path.GetExtension(path);
                    if (!ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var fullPath = Path.GetFullPath(path);
                    if (!seenPaths.Add(fullPath))
                    {
                        continue;
                    }

                    long size = 0;
                    try
                    {
                        size = new FileInfo(fullPath).Length;
                    }
                    catch
                    {
                        // Metadata is best-effort; the tile still shows the image.
                    }

                    items.Add(new ScreenshotItem
                    {
                        Path = fullPath,
                        FileName = Path.GetFileName(fullPath),
                        TakenAt = ResolveDate(fullPath, dateFromFilename),
                        Size = size,
                        VersionId = versionId,
                        VersionName = versionName
                    });
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{directory}: {ex.Message}");
            }
        }
    }

    internal static DateTime ResolveDate(string path, bool dateFromFilename)
    {
        if (dateFromFilename)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.Length >= 19
                && DateTime.TryParseExact(name[..19], "yyyy-MM-dd_HH.mm.ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var exact))
            {
                return exact;
            }

            if (name.Length >= 10
                && DateTime.TryParseExact(name[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dateOnly))
            {
                return dateOnly;
            }
        }

        try
        {
            return File.GetLastWriteTime(path);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }
}