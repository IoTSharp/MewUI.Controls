using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.Framebuffer;

internal sealed class FramebufferImage : IImage
{
    private byte[]? _pixels;

    private FramebufferImage(int pixelWidth, int pixelHeight, byte[] pixels, bool isOpaque)
    {
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        IsOpaque = isOpaque;
        _pixels = pixels;
    }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    internal bool IsOpaque { get; }

    internal ReadOnlySpan<byte> Pixels => _pixels ?? throw new ObjectDisposedException(nameof(FramebufferImage));

    public static FramebufferImage FromFile(string path)
    {
        var data = File.ReadAllBytes(path);
        return FromBytes(data);
    }

    public static FramebufferImage FromBytes(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!ImageDecoders.TryDecode(data, out var bitmap))
        {
            throw new InvalidOperationException("Failed to decode image bytes.");
        }

        return FromBgra(bitmap.WidthPx, bitmap.HeightPx, bitmap.Data, sourcePremultiplied: false);
    }

    public static FramebufferImage FromPixelSource(IPixelBufferSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var locked = source.Lock();

        var pixels = GC.AllocateUninitializedArray<byte>(checked(locked.PixelWidth * locked.PixelHeight * 4));
        FramebufferPixelOperations.CopyRows(locked.Buffer, locked.StrideBytes, pixels, locked.PixelWidth * 4, locked.PixelWidth * 4, locked.PixelHeight);
        return FromBgra(locked.PixelWidth, locked.PixelHeight, pixels, source.IsPremultiplied);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _pixels, null);
    }

    internal static FramebufferImage FromBgra(int width, int height, byte[] bgra, bool sourcePremultiplied)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        int expected = checked(width * height * 4);
        if (bgra.Length != expected)
        {
            throw new ArgumentException("Invalid BGRA buffer length.", nameof(bgra));
        }

        var isOpaque = FramebufferPixelOperations.IsFullyOpaque(bgra);
        var premul = FramebufferPixelOperations.ToPremultipliedBgra32(bgra, sourcePremultiplied, hasAlpha: !isOpaque);

        return new FramebufferImage(width, height, premul, isOpaque);
    }

    internal static FramebufferImage FromPremultipliedBgraNoCopy(int width, int height, byte[] premultipliedBgra, bool isOpaque)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        ArgumentNullException.ThrowIfNull(premultipliedBgra);
        int expected = checked(width * height * 4);
        if (premultipliedBgra.Length != expected)
        {
            throw new ArgumentException("Invalid BGRA buffer length.", nameof(premultipliedBgra));
        }

        return new FramebufferImage(width, height, premultipliedBgra, isOpaque);
    }
}
