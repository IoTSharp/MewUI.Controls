using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.Framebuffer;

public sealed class FramebufferFrameComposer : IDisposable
{
    private readonly IGraphicsFactory _graphicsFactory;
    private readonly Dictionary<SurfaceKey, LinkedListNode<SurfaceEntry>> _surfaces = new();
    private readonly LinkedList<SurfaceEntry> _surfaceLru = new();
    private long _surfaceBytes;
    private bool _disposed;

    public FramebufferFrameComposer(IGraphicsFactory graphicsFactory)
    {
        _graphicsFactory = graphicsFactory ?? throw new ArgumentNullException(nameof(graphicsFactory));
    }

    public static Bgra32PixelBuffer DecodeBgra32FrameFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return DecodeBgra32Frame(File.ReadAllBytes(path), path);
    }

    public static Bgra32PixelBuffer DecodeBgra32Frame(byte[] encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        return DecodeBgra32Frame(encoded, debugName: null);
    }

    public bool TryRenderRegionOnto(
        Bgra32PixelBuffer destination,
        int destinationX,
        int destinationY,
        int width,
        int height,
        Action<IGraphicsContext> render)
    {
        ArgumentNullException.ThrowIfNull(destination.Data);
        return TryRenderRegionOnto(
            destination.Data,
            destination.WidthPx,
            destination.HeightPx,
            destination.StrideBytes,
            destinationX,
            destinationY,
            width,
            height,
            render);
    }

    public bool TryRenderRegionOnto(
        Span<byte> destination,
        int destinationPixelWidth,
        int destinationPixelHeight,
        int destinationStrideBytes,
        int destinationX,
        int destinationY,
        int width,
        int height,
        Action<IGraphicsContext> render)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(render);

        if (width <= 0 || height <= 0)
        {
            return true;
        }

        ValidateDestination(destination, destinationPixelWidth, destinationPixelHeight, destinationStrideBytes, destinationX, destinationY, width, height);

        using var surfaceLease = GetSurface(width, height);
        var surface = surfaceLease.Surface;
        if (surface is not ICpuPixelSurface cpuSurface)
        {
            return false;
        }

        using var context = _graphicsFactory.CreateContext(surface);
        context.BeginFrame(surface);
        try
        {
            context.Clear(Color.Transparent);
            render(context);
        }
        finally
        {
            context.EndFrame();
        }

        FramebufferPixelOperations.BlendPremultipliedRegion(
            cpuSurface.GetReadOnlyPixelSpan(),
            cpuSurface.StrideBytes,
            destination,
            destinationStrideBytes,
            destinationX,
            destinationY,
            width,
            height);
        return true;
    }

    public int CachedSurfaceCount => _surfaces.Count;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var entry in _surfaceLru)
        {
            entry.Surface.Dispose();
        }

        _surfaces.Clear();
        _surfaceLru.Clear();
        _surfaceBytes = 0;
    }

    private static Bgra32PixelBuffer DecodeBgra32Frame(byte[] encoded, string? debugName)
    {
        if (!ImageDecoders.TryDecode(encoded, out var bitmap))
        {
            throw debugName is null
                ? new InvalidOperationException("Failed to decode image bytes.")
                : new InvalidOperationException($"Failed to decode image {debugName}.");
        }

        return bitmap;
    }

    private SurfaceLease GetSurface(int width, int height)
    {
        var key = new SurfaceKey(width, height);
        if (_surfaces.TryGetValue(key, out var cachedNode))
        {
            _surfaceLru.Remove(cachedNode);
            _surfaceLru.AddFirst(cachedNode);
            return new SurfaceLease(cachedNode.Value.Surface, ownsSurface: false);
        }

        var descriptor = RenderSurfaceDescriptor.CpuPixels(width, height, premultiplied: true, debugName: "framebuffer-region");
        var surface = _graphicsFactory.CreateSurface(descriptor);
        var bytes = checked((long)width * height * FramebufferPixelOperations.Bgra32BytesPerPixel);
        var (maxEntries, maxBytes) = GetCacheLimits();
        if (maxEntries <= 0 || maxBytes <= 0 || bytes > maxBytes)
        {
            return new SurfaceLease(surface, ownsSurface: true);
        }

        var node = new LinkedListNode<SurfaceEntry>(new SurfaceEntry(key, surface, bytes));
        _surfaceLru.AddFirst(node);
        _surfaces.Add(key, node);
        _surfaceBytes += bytes;
        TrimSurfaceCache(maxEntries, maxBytes);
        return new SurfaceLease(surface, ownsSurface: false);
    }

    private (int MaxEntries, long MaxBytes) GetCacheLimits()
    {
        if (_graphicsFactory is FramebufferGraphicsFactory framebuffer)
        {
            var options = framebuffer.Options;
            return (options.RegionSurfaceCacheMaxEntries, options.RegionSurfaceCacheMaxBytes);
        }

        var defaults = new FramebufferOptions();
        return (defaults.RegionSurfaceCacheMaxEntries, defaults.RegionSurfaceCacheMaxBytes);
    }

    private void TrimSurfaceCache(int maxEntries, long maxBytes)
    {
        while ((_surfaces.Count > maxEntries || _surfaceBytes > maxBytes) && _surfaceLru.Last is { } node)
        {
            _surfaceLru.RemoveLast();
            _surfaces.Remove(node.Value.Key);
            _surfaceBytes -= node.Value.Bytes;
            node.Value.Surface.Dispose();
        }
    }

    private static void ValidateDestination(
        ReadOnlySpan<byte> destination,
        int pixelWidth,
        int pixelHeight,
        int strideBytes,
        int x,
        int y,
        int width,
        int height)
    {
        if (pixelWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        }

        if (pixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelHeight));
        }

        if (strideBytes < checked(pixelWidth * FramebufferPixelOperations.Bgra32BytesPerPixel))
        {
            throw new ArgumentOutOfRangeException(nameof(strideBytes));
        }

        if (x < 0 || y < 0 || width < 0 || height < 0 || x + width > pixelWidth || y + height > pixelHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Region must be clipped to the destination frame.");
        }

        int rowBytes = checked(pixelWidth * FramebufferPixelOperations.Bgra32BytesPerPixel);
        int required = checked((pixelHeight - 1) * strideBytes + rowBytes);
        if (destination.Length < required)
        {
            throw new ArgumentException("The destination buffer is too small for the supplied dimensions and stride.", nameof(destination));
        }
    }

    private readonly record struct SurfaceKey(int Width, int Height);

    private sealed record SurfaceEntry(SurfaceKey Key, IRenderSurface Surface, long Bytes);

    private readonly struct SurfaceLease : IDisposable
    {
        public SurfaceLease(IRenderSurface surface, bool ownsSurface)
        {
            Surface = surface;
            OwnsSurface = ownsSurface;
        }

        public IRenderSurface Surface { get; }

        private bool OwnsSurface { get; }

        public void Dispose()
        {
            if (OwnsSurface)
            {
                Surface.Dispose();
            }
        }
    }
}
