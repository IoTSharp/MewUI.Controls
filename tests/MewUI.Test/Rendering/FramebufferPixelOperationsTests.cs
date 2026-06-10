using Aprillz.MewUI;
using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Framebuffer;

namespace MewUI.Test.Rendering;

[TestClass]
public sealed class FramebufferPixelOperationsTests
{
    [TestMethod]
    public void CopyRows_CopiesOnlyRequestedRowBytes()
    {
        byte[] source =
        [
            1, 2, 3, 4, 99, 99,
            5, 6, 7, 8, 99, 99
        ];
        byte[] destination =
        [
            0, 0, 0, 0, 77, 77,
            0, 0, 0, 0, 77, 77
        ];

        FramebufferPixelOperations.CopyRows(source, sourceStrideBytes: 6, destination, destinationStrideBytes: 6, rowBytes: 4, height: 2);

        CollectionAssert.AreEqual(new byte[]
        {
            1, 2, 3, 4, 77, 77,
            5, 6, 7, 8, 77, 77
        }, destination);
    }

    [TestMethod]
    public void FillRectanglePremultiplied_ClipsAndPremultipliesColor()
    {
        var pixels = new byte[4 * 4 * 4];

        FramebufferPixelOperations.FillRectanglePremultiplied(
            pixels,
            pixelWidth: 4,
            pixelHeight: 4,
            strideBytes: 16,
            x: 2,
            y: 2,
            width: 4,
            height: 4,
            Color.FromArgb(128, 100, 50, 20));

        AssertPixel(pixels, 4, 0, 0, 0, 0, 0, 0);
        AssertPixel(pixels, 4, 2, 2, b: 10, g: 25, r: 50, a: 128, tolerance: 1);
        AssertPixel(pixels, 4, 3, 3, b: 10, g: 25, r: 50, a: 128, tolerance: 1);
    }

    [TestMethod]
    public void BlendPremultipliedRegion_BlendsTransparentAndOpaquePixels()
    {
        byte[] source =
        [
            0, 0, 128, 128,
            255, 0, 0, 255
        ];
        byte[] destination =
        [
            100, 100, 100, 255,
            100, 100, 100, 255
        ];

        FramebufferPixelOperations.BlendPremultipliedRegion(
            source,
            sourceStrideBytes: 8,
            destination,
            destinationStrideBytes: 8,
            destinationX: 0,
            destinationY: 0,
            width: 2,
            height: 1);

        AssertPixel(destination, 2, 0, 0, b: 50, g: 50, r: 178, a: 255, tolerance: 1);
        AssertPixel(destination, 2, 1, 0, b: 255, g: 0, r: 0, a: 255);
    }

    [TestMethod]
    public void FrameComposer_ReusesSurfaceAndBlendsRegionOntoDestination()
    {
        var factory = FramebufferGraphicsFactory.Instance;
        using var composer = new FramebufferFrameComposer(factory);
        var frame = new Bgra32PixelBuffer(4, 4, new byte[4 * 4 * 4]);

        composer.TryRenderRegionOnto(
            frame,
            destinationX: 1,
            destinationY: 1,
            width: 2,
            height: 2,
            context => context.FillRectangle(new Rect(0, 0, 2, 2), Color.Red));

        AssertPixel(frame.Data, 4, 0, 0, b: 0, g: 0, r: 0, a: 0);
        AssertPixel(frame.Data, 4, 1, 1, b: 0, g: 0, r: 255, a: 255);
        AssertPixel(frame.Data, 4, 2, 2, b: 0, g: 0, r: 255, a: 255);
    }

    private static void AssertPixel(byte[] pixels, int width, int x, int y, byte b, byte g, byte r, byte a, byte tolerance = 0)
    {
        int offset = (y * width + x) * 4;
        AssertWithin(b, pixels[offset + 0], tolerance, "B");
        AssertWithin(g, pixels[offset + 1], tolerance, "G");
        AssertWithin(r, pixels[offset + 2], tolerance, "R");
        AssertWithin(a, pixels[offset + 3], tolerance, "A");
    }

    private static void AssertWithin(byte expected, byte actual, byte tolerance, string channel)
    {
        int delta = Math.Abs(expected - actual);
        Assert.IsLessThanOrEqualTo(tolerance, delta, $"{channel}: expected {expected}, actual {actual}");
    }
}
