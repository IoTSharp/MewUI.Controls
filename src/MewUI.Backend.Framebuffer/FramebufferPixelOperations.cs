using System.Runtime.InteropServices;

using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.Framebuffer;

public static class FramebufferPixelOperations
{
    public const int Bgra32BytesPerPixel = 4;

    public static void CopyRows(
        ReadOnlySpan<byte> source,
        int sourceStrideBytes,
        Span<byte> destination,
        int destinationStrideBytes,
        int rowBytes,
        int height)
    {
        if (sourceStrideBytes < rowBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceStrideBytes));
        }

        if (destinationStrideBytes < rowBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(destinationStrideBytes));
        }

        if (rowBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rowBytes));
        }

        if (height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (height == 0 || rowBytes == 0)
        {
            return;
        }

        EnsureSpanContainsRows(source.Length, sourceStrideBytes, rowBytes, height, nameof(source));
        EnsureSpanContainsRows(destination.Length, destinationStrideBytes, rowBytes, height, nameof(destination));

        for (int y = 0; y < height; y++)
        {
            source.Slice(y * sourceStrideBytes, rowBytes)
                .CopyTo(destination.Slice(y * destinationStrideBytes, rowBytes));
        }
    }

    public static byte[] CopySurfacePixels(ICpuPixelSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        var destination = GC.AllocateUninitializedArray<byte>(checked(surface.PixelWidth * surface.PixelHeight * Bgra32BytesPerPixel));
        CopyRows(
            surface.GetReadOnlyPixelSpan(),
            surface.StrideBytes,
            destination,
            surface.PixelWidth * Bgra32BytesPerPixel,
            surface.PixelWidth * Bgra32BytesPerPixel,
            surface.PixelHeight);
        return destination;
    }

    public static void FillRectangleBgra32(
        Span<byte> pixels,
        int pixelWidth,
        int pixelHeight,
        int strideBytes,
        int x,
        int y,
        int width,
        int height,
        byte b,
        byte g,
        byte r,
        byte a)
    {
        if (pixelWidth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        }

        if (pixelHeight < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelHeight));
        }

        int minimumStride = checked(pixelWidth * Bgra32BytesPerPixel);
        if (strideBytes < minimumStride)
        {
            throw new ArgumentOutOfRangeException(nameof(strideBytes));
        }

        EnsureSpanContainsRows(pixels.Length, strideBytes, minimumStride, pixelHeight, nameof(pixels));

        var left = Math.Clamp(x, 0, pixelWidth);
        var top = Math.Clamp(y, 0, pixelHeight);
        var right = Math.Clamp(x + width, 0, pixelWidth);
        var bottom = Math.Clamp(y + height, 0, pixelHeight);
        if (left >= right || top >= bottom)
        {
            return;
        }

        if (!BitConverter.IsLittleEndian)
        {
            FillRectangleBgra32Bytes(pixels, strideBytes, left, top, right, bottom, b, g, r, a);
            return;
        }

        uint packed = (uint)b | ((uint)g << 8) | ((uint)r << 16) | ((uint)a << 24);
        int rowPixelCount = right - left;
        for (int row = top; row < bottom; row++)
        {
            int offset = row * strideBytes + left * Bgra32BytesPerPixel;
            MemoryMarshal.Cast<byte, uint>(pixels.Slice(offset, rowPixelCount * Bgra32BytesPerPixel)).Fill(packed);
        }
    }

    public static void FillRectanglePremultiplied(
        Span<byte> pixels,
        int pixelWidth,
        int pixelHeight,
        int strideBytes,
        int x,
        int y,
        int width,
        int height,
        Color color)
    {
        byte a = color.A;
        FillRectangleBgra32(
            pixels,
            pixelWidth,
            pixelHeight,
            strideBytes,
            x,
            y,
            width,
            height,
            Premultiply(color.B, a),
            Premultiply(color.G, a),
            Premultiply(color.R, a),
            a);
    }

    public static void BlendPremultipliedRegion(
        ReadOnlySpan<byte> source,
        int sourceStrideBytes,
        Span<byte> destination,
        int destinationStrideBytes,
        int destinationX,
        int destinationY,
        int width,
        int height)
    {
        BlendPremultipliedRegion(
            source,
            sourceStrideBytes,
            sourceX: 0,
            sourceY: 0,
            destination,
            destinationStrideBytes,
            destinationX,
            destinationY,
            width,
            height);
    }

    public static void BlendPremultipliedRegion(
        ReadOnlySpan<byte> source,
        int sourceStrideBytes,
        int sourceX,
        int sourceY,
        Span<byte> destination,
        int destinationStrideBytes,
        int destinationX,
        int destinationY,
        int width,
        int height)
    {
        if (sourceStrideBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceStrideBytes));
        }

        if (destinationStrideBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(destinationStrideBytes));
        }

        if (width < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (width == 0 || height == 0)
        {
            return;
        }

        int rowBytes = checked(width * Bgra32BytesPerPixel);
        int sourceOffset = checked(sourceY * sourceStrideBytes + sourceX * Bgra32BytesPerPixel);
        int destinationOffset = checked(destinationY * destinationStrideBytes + destinationX * Bgra32BytesPerPixel);

        EnsureSpanContainsRows(source.Length - sourceOffset, sourceStrideBytes, rowBytes, height, nameof(source));
        EnsureSpanContainsRows(destination.Length - destinationOffset, destinationStrideBytes, rowBytes, height, nameof(destination));

        var sourceRegion = source.Slice(sourceOffset);
        var destinationRegion = destination.Slice(destinationOffset);
        for (int y = 0; y < height; y++)
        {
            int sourceRow = y * sourceStrideBytes;
            int destinationRow = y * destinationStrideBytes;
            for (int x = 0; x < width; x++)
            {
                int src = sourceRow + x * Bgra32BytesPerPixel;
                byte sa = sourceRegion[src + 3];
                if (sa == 0)
                {
                    continue;
                }

                int dst = destinationRow + x * Bgra32BytesPerPixel;
                BlendPremultipliedPixel(destinationRegion, dst, sourceRegion[src + 0], sourceRegion[src + 1], sourceRegion[src + 2], sa);
            }
        }
    }

    public static void BlendPremultipliedPixel(Span<byte> destination, int destinationOffset, byte sb, byte sg, byte sr, byte sa)
    {
        if (sa == 0)
        {
            return;
        }

        if (sa == 255)
        {
            destination[destinationOffset + 0] = sb;
            destination[destinationOffset + 1] = sg;
            destination[destinationOffset + 2] = sr;
            destination[destinationOffset + 3] = 255;
            return;
        }

        int inv = 255 - sa;
        destination[destinationOffset + 0] = (byte)(sb + (destination[destinationOffset + 0] * inv + 127) / 255);
        destination[destinationOffset + 1] = (byte)(sg + (destination[destinationOffset + 1] * inv + 127) / 255);
        destination[destinationOffset + 2] = (byte)(sr + (destination[destinationOffset + 2] * inv + 127) / 255);
        destination[destinationOffset + 3] = (byte)(sa + (destination[destinationOffset + 3] * inv + 127) / 255);
    }

    public static byte[] ToPremultipliedBgra32(ReadOnlySpan<byte> bgra, bool sourcePremultiplied, bool hasAlpha = true)
    {
        if (bgra.Length % Bgra32BytesPerPixel != 0)
        {
            throw new ArgumentException("BGRA32 buffers must contain whole pixels.", nameof(bgra));
        }

        var premultiplied = GC.AllocateUninitializedArray<byte>(bgra.Length);
        if (sourcePremultiplied || !hasAlpha)
        {
            bgra.CopyTo(premultiplied);
            return premultiplied;
        }

        for (int i = 0; i < bgra.Length; i += Bgra32BytesPerPixel)
        {
            byte a = bgra[i + 3];
            premultiplied[i + 0] = Premultiply(bgra[i + 0], a);
            premultiplied[i + 1] = Premultiply(bgra[i + 1], a);
            premultiplied[i + 2] = Premultiply(bgra[i + 2], a);
            premultiplied[i + 3] = a;
        }

        return premultiplied;
    }

    public static bool IsFullyOpaque(ReadOnlySpan<byte> bgra)
    {
        if (bgra.Length % Bgra32BytesPerPixel != 0)
        {
            throw new ArgumentException("BGRA32 buffers must contain whole pixels.", nameof(bgra));
        }

        for (int i = 3; i < bgra.Length; i += Bgra32BytesPerPixel)
        {
            if (bgra[i] != 255)
            {
                return false;
            }
        }

        return true;
    }

    internal static byte Premultiply(byte c, byte a)
        => a == 255 ? c : (byte)((c * a + 127) / 255);

    private static void FillRectangleBgra32Bytes(
        Span<byte> pixels,
        int strideBytes,
        int left,
        int top,
        int right,
        int bottom,
        byte b,
        byte g,
        byte r,
        byte a)
    {
        for (int row = top; row < bottom; row++)
        {
            int offset = row * strideBytes + left * Bgra32BytesPerPixel;
            for (int column = left; column < right; column++)
            {
                pixels[offset + 0] = b;
                pixels[offset + 1] = g;
                pixels[offset + 2] = r;
                pixels[offset + 3] = a;
                offset += Bgra32BytesPerPixel;
            }
        }
    }

    private static void EnsureSpanContainsRows(int spanLength, int strideBytes, int rowBytes, int height, string parameterName)
    {
        if (height <= 0)
        {
            return;
        }

        int requiredLength = checked((height - 1) * strideBytes + rowBytes);
        if (spanLength < requiredLength)
        {
            throw new ArgumentException("The buffer is too small for the supplied stride, row length, and height.", parameterName);
        }
    }
}
