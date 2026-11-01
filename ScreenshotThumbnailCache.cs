using System.Collections.Concurrent;
using Avalonia.Media.Imaging;

namespace PCL.Extensions.ScreenshotManager;

internal sealed class ScreenshotThumbnailCache : IDisposable
{
    private readonly ConcurrentDictionary<string, Task<Bitmap?>> _thumbnails = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _decodeGate = new(4, 4);
    private readonly int _width;

    public ScreenshotThumbnailCache(int width)
    {
        _width = Math.Clamp(width, 120, 640);
    }

    public Task<Bitmap?> GetAsync(ScreenshotItem item)
    {
        return _thumbnails.GetOrAdd(item.Path, path => LoadAsync(path));
    }

    private async Task<Bitmap?> LoadAsync(string path)
    {
        try
        {
            await _decodeGate.WaitAsync();
            try
            {
                return await Task.Run(() =>
                {
                    using var stream = File.OpenRead(path);
                    return Bitmap.DecodeToWidth(stream, _width, BitmapInterpolationMode.HighQuality);
                });
            }
            finally
            {
                _decodeGate.Release();
            }
        }
        catch
        {
            _thumbnails.TryRemove(path, out _);
            return null;
        }
    }

    public void Dispose()
    {
        _decodeGate.Dispose();
    }
}