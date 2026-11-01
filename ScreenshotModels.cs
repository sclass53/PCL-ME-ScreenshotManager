namespace PCL.Extensions.ScreenshotManager;

internal sealed class ScreenshotItem
{
    public required string Path { get; init; }
    public required string FileName { get; init; }
    public required DateTime TakenAt { get; init; }
    public required long Size { get; init; }
    public string? VersionId { get; init; }
    public string? VersionName { get; init; }
    public bool IsShared => VersionId is null;

    public string DisplayName => System.IO.Path.GetFileNameWithoutExtension(FileName);
}

internal sealed record ScreenshotGroup(string? Id, string Name, bool IsShared, int Count);

internal sealed class ScreenshotScanResult
{
    public required IReadOnlyList<ScreenshotItem> Items { get; init; }
    public required IReadOnlyList<ScreenshotGroup> Groups { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
}