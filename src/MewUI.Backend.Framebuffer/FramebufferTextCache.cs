using Aprillz.MewUI.Rendering.FreeType;

namespace Aprillz.MewUI.Rendering.Framebuffer;

internal sealed class FramebufferTextCache
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<FramebufferTextCacheKey, LinkedListNode<CacheEntry>> _map = new();
    private readonly LinkedList<CacheEntry> _lru = new();
    private long _bytes;

    public bool TryGetOrCreate(
        FramebufferTextCacheKey key,
        int maxEntries,
        long maxBytes,
        int maxAreaPixels,
        Func<FramebufferCachedTextBitmap> create,
        out FramebufferCachedTextBitmap bitmap)
    {
        if (maxEntries <= 0 || maxBytes <= 0 || maxAreaPixels <= 0 || key.WidthPx * key.HeightPx > maxAreaPixels)
        {
            bitmap = default!;
            return false;
        }

        lock (_syncRoot)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                bitmap = node.Value.Bitmap;
                return true;
            }
        }

        var created = create();
        long bytes = created.Bytes;
        if (bytes > maxBytes)
        {
            bitmap = created;
            return true;
        }

        lock (_syncRoot)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _lru.Remove(existing);
                _lru.AddFirst(existing);
                bitmap = existing.Value.Bitmap;
                return true;
            }

            var entry = new CacheEntry(key, created, bytes);
            var node = new LinkedListNode<CacheEntry>(entry);
            _lru.AddFirst(node);
            _map.Add(key, node);
            _bytes += bytes;
            Trim(maxEntries, maxBytes);
        }

        bitmap = created;
        return true;
    }

    public void Clear()
    {
        lock (_syncRoot)
        {
            _map.Clear();
            _lru.Clear();
            _bytes = 0;
        }
    }

    private void Trim(int maxEntries, long maxBytes)
    {
        while ((_map.Count > maxEntries || _bytes > maxBytes) && _lru.Last is { } node)
        {
            _lru.RemoveLast();
            _map.Remove(node.Value.Key);
            _bytes -= node.Value.Bytes;
        }
    }

    private sealed record CacheEntry(FramebufferTextCacheKey Key, FramebufferCachedTextBitmap Bitmap, long Bytes);
}

internal sealed record FramebufferCachedTextBitmap(int WidthPx, int HeightPx, byte[] PremultipliedPixels)
{
    public long Bytes => PremultipliedPixels.LongLength;
}

internal readonly record struct FramebufferTextCacheKey(
    string Text,
    string FontPath,
    int FontSizePx,
    int FontWeight,
    bool Italic,
    uint ColorArgb,
    int WidthPx,
    int HeightPx,
    int HAlign,
    int VAlign,
    int Wrapping,
    int Trimming)
{
    public static FramebufferTextCacheKey Create(
        string text,
        FreeTypeFont font,
        Color color,
        int widthPx,
        int heightPx,
        TextAlignment horizontalAlignment,
        TextAlignment verticalAlignment,
        TextWrapping wrapping,
        TextTrimming trimming)
    {
        uint colorArgb = ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
        return new FramebufferTextCacheKey(
            text,
            font.FontPath,
            font.PixelHeight,
            (int)font.Weight,
            font.IsItalic,
            colorArgb,
            widthPx,
            heightPx,
            (int)horizontalAlignment,
            (int)verticalAlignment,
            (int)wrapping,
            (int)trimming);
    }
}
