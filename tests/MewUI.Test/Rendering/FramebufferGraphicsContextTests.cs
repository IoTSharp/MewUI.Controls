using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Framebuffer;
using System.Numerics;

namespace MewUI.Test.Rendering;

[TestClass]
public sealed class FramebufferGraphicsContextTests
{
    [TestMethod]
    public void Clear_WritesPremultipliedBgraPixels()
    {
        using var surface = CreateSurface(2, 2);
        using var context = FramebufferGraphicsFactory.Instance.CreateContext(surface);

        context.BeginFrame(surface);
        context.Clear(Color.FromArgb(255, 10, 20, 30));
        context.EndFrame();

        AssertPixel(surface, 0, 0, b: 30, g: 20, r: 10, a: 255);
        AssertPixel(surface, 1, 1, b: 30, g: 20, r: 10, a: 255);
    }

    [TestMethod]
    public void FillRectangle_AlphaBlendsOverExistingPixels()
    {
        using var surface = CreateSurface(4, 4);
        using var context = FramebufferGraphicsFactory.Instance.CreateContext(surface);

        context.BeginFrame(surface);
        context.Clear(Color.Black);
        context.FillRectangle(new Rect(1, 1, 2, 2), Color.FromArgb(128, 255, 0, 0));
        context.EndFrame();

        AssertPixel(surface, 2, 2, b: 0, g: 0, r: 128, a: 255, tolerance: 1);
    }

    [TestMethod]
    public void DrawImage_MapsSourceRectFromUnclippedDestination()
    {
        using var source = CreateSurface(2, 2);
        WritePixel(source, 0, 0, b: 0, g: 0, r: 255, a: 255);
        WritePixel(source, 1, 0, b: 0, g: 255, r: 0, a: 255);
        WritePixel(source, 0, 1, b: 255, g: 0, r: 0, a: 255);
        WritePixel(source, 1, 1, b: 255, g: 255, r: 255, a: 255);

        using var image = FramebufferGraphicsFactory.Instance.CreateImageView((IRenderSurface)source);
        using var target = CreateSurface(1, 1);
        using var context = FramebufferGraphicsFactory.Instance.CreateContext(target);

        context.ImageScaleQuality = ImageScaleQuality.Fast;
        context.BeginFrame(target);
        context.Clear(Color.Transparent);
        context.DrawImage(image, new Rect(0, 0, 1, 1), new Rect(1, 0, 1, 1));
        context.EndFrame();

        AssertPixel(target, 0, 0, b: 0, g: 255, r: 0, a: 255);
    }

    [TestMethod]
    public void FillPath_FillsSimpleClosedPath()
    {
        using var surface = CreateSurface(4, 4);
        using var context = FramebufferGraphicsFactory.Instance.CreateContext(surface);

        var path = new PathGeometry();
        path.MoveTo(1, 1);
        path.LineTo(3, 1);
        path.LineTo(3, 3);
        path.LineTo(1, 3);
        path.Close();

        context.BeginFrame(surface);
        context.Clear(Color.Transparent);
        context.FillPath(path, Color.Blue);
        context.EndFrame();

        AssertPixel(surface, 2, 2, b: 255, g: 0, r: 0, a: 255);
    }

    [TestMethod]
    public void SetClipRoundedRect_AppliesSoftwareMask()
    {
        using var surface = CreateSurface(6, 6);
        using var context = FramebufferGraphicsFactory.Instance.CreateContext(surface);

        context.BeginFrame(surface);
        context.Clear(Color.Transparent);
        context.SetClipRoundedRect(new Rect(1, 1, 4, 4), 2, 2);
        context.FillRectangle(new Rect(0, 0, 6, 6), Color.Red);
        context.EndFrame();

        AssertLessThan(surface, 1, 1, channelOffset: 3, upperExclusive: 255);
        AssertPixel(surface, 3, 3, b: 0, g: 0, r: 255, a: 255);
    }

    [TestMethod]
    public void SetClipPath_AppliesSoftwareMask()
    {
        using var surface = CreateSurface(5, 5);
        using var context = FramebufferGraphicsFactory.Instance.CreateContext(surface);

        var clip = new PathGeometry();
        clip.MoveTo(0, 0);
        clip.LineTo(4, 0);
        clip.LineTo(0, 4);
        clip.Close();

        context.BeginFrame(surface);
        context.Clear(Color.Transparent);
        context.SetClipPath(clip);
        context.FillRectangle(new Rect(0, 0, 5, 5), Color.Blue);
        context.EndFrame();

        AssertPixel(surface, 1, 1, b: 255, g: 0, r: 0, a: 255);
        AssertPixel(surface, 3, 3, b: 0, g: 0, r: 0, a: 0);
    }

    [TestMethod]
    public void DrawImage_UsesAffineTransformInsteadOfDestinationAabb()
    {
        using var source = CreateSurface(1, 1);
        WritePixel(source, 0, 0, b: 0, g: 0, r: 255, a: 255);

        using var image = FramebufferGraphicsFactory.Instance.CreateImageView((IRenderSurface)source);
        using var target = CreateSurface(7, 5);
        using var context = FramebufferGraphicsFactory.Instance.CreateContext(target);

        context.ImageScaleQuality = ImageScaleQuality.Fast;
        context.BeginFrame(target);
        context.Clear(Color.Transparent);
        context.SetTransform(new Matrix3x2(1, 0, 1, 1, 0, 0));
        context.DrawImage(image, new Rect(1, 1, 2, 2));
        context.EndFrame();

        AssertPixel(target, 2, 2, b: 0, g: 0, r: 0, a: 0);
        AssertPixel(target, 4, 2, b: 0, g: 0, r: 255, a: 255);
    }

    private static FramebufferRenderSurface CreateSurface(int width, int height)
        => new(width, height, dpiScale: 1.0, RenderPixelFormat.Bgra8888Premultiplied, SurfaceCapabilities.Premultiplied);

    private static void WritePixel(FramebufferRenderSurface surface, int x, int y, byte b, byte g, byte r, byte a)
    {
        var pixels = surface.GetWritablePixelSpan();
        int i = y * surface.StrideBytes + x * 4;
        pixels[i + 0] = b;
        pixels[i + 1] = g;
        pixels[i + 2] = r;
        pixels[i + 3] = a;
    }

    private static void AssertPixel(FramebufferRenderSurface surface, int x, int y, byte b, byte g, byte r, byte a, byte tolerance = 0)
    {
        var pixels = surface.GetReadOnlyPixelSpan();
        int i = y * surface.StrideBytes + x * 4;
        AssertWithin(b, pixels[i + 0], tolerance, "B");
        AssertWithin(g, pixels[i + 1], tolerance, "G");
        AssertWithin(r, pixels[i + 2], tolerance, "R");
        AssertWithin(a, pixels[i + 3], tolerance, "A");
    }

    private static void AssertLessThan(FramebufferRenderSurface surface, int x, int y, int channelOffset, byte upperExclusive)
    {
        var pixels = surface.GetReadOnlyPixelSpan();
        int i = y * surface.StrideBytes + x * 4 + channelOffset;
        Assert.IsLessThan(upperExclusive, pixels[i], $"Expected channel {channelOffset} at ({x},{y}) to be < {upperExclusive}, actual {pixels[i]}.");
    }

    private static void AssertWithin(byte expected, byte actual, byte tolerance, string channel)
    {
        int delta = Math.Abs(expected - actual);
        Assert.IsLessThanOrEqualTo(tolerance, delta, $"{channel}: expected {expected}, actual {actual}");
    }
}
