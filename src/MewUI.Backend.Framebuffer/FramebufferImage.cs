using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.Framebuffer;

internal sealed class FramebufferImage : IImage
{
    private byte[]? _pixels;

    private FramebufferImage(int pixelWidth, int pixelHeight, byte[] pixels)
    {
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        _pixels = pixels;
    }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

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
        CopyRows(locked.Buffer, locked.StrideBytes, pixels, locked.PixelWidth * 4, locked.PixelWidth * 4, locked.PixelHeight);
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

        var premul = GC.AllocateUninitializedArray<byte>(expected);
        if (sourcePremultiplied)
        {
            bgra.AsSpan().CopyTo(premul);
        }
        else
        {
            for (int i = 0; i < expected; i += 4)
            {
                byte a = bgra[i + 3];
                premul[i + 0] = Premultiply(bgra[i + 0], a);
                premul[i + 1] = Premultiply(bgra[i + 1], a);
                premul[i + 2] = Premultiply(bgra[i + 2], a);
                premul[i + 3] = a;
            }
        }

        return new FramebufferImage(width, height, premul);
    }

    private static byte Premultiply(byte c, byte a)
        => a == 255 ? c : (byte)((c * a + 127) / 255);

    private static void CopyRows(byte[] source, int sourceStride, byte[] destination, int destinationStride, int rowBytes, int height)
    {
        for (int y = 0; y < height; y++)
        {
            source.AsSpan(y * sourceStride, rowBytes)
                .CopyTo(destination.AsSpan(y * destinationStride, rowBytes));
        }
    }
}
