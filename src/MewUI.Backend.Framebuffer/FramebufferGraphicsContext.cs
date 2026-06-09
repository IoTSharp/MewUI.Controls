using System.Numerics;
using System.Runtime.InteropServices;

using Aprillz.MewUI.Rendering.Gdi.Rendering;
using Aprillz.MewUI.Rendering.Gdi.Sdf;
using Aprillz.MewUI.Rendering.FreeType;

namespace Aprillz.MewUI.Rendering.Framebuffer;

internal sealed class FramebufferGraphicsContext : GraphicsContextBase
{
    private readonly FramebufferRenderSurface _surface;
    private readonly Stack<State> _states = new();
    private byte[] _pixels = [];
    private Matrix3x2 _transform = Matrix3x2.Identity;
    private RectI _clip;
    private byte[]? _clipMask;
    private const int FillSupersample = 2;
    private const int EdgeSupersample = 3;

    private float _globalAlpha = 1f;
    private bool _textPixelSnap = true;

    public FramebufferGraphicsContext(FramebufferRenderSurface surface)
    {
        _surface = surface;
    }

    public override double DpiScale => _surface.DpiScale;

    public override ImageScaleQuality ImageScaleQuality { get; set; } = ImageScaleQuality.Default;

    public override float GlobalAlpha
    {
        get => _globalAlpha;
        set => _globalAlpha = Math.Clamp(value, 0f, 1f);
    }

    public override bool TextPixelSnap
    {
        get => _textPixelSnap;
        set => _textPixelSnap = value;
    }

    protected override void OnBeginFrame(IRenderTarget target)
    {
        if (!ReferenceEquals(target, _surface))
        {
            throw new ArgumentException("FramebufferGraphicsContext can only render to its owning surface.", nameof(target));
        }

        _pixels = _surface.PixelBuffer;
        _transform = Matrix3x2.CreateScale((float)_surface.DpiScale);
        _clip = new RectI(0, 0, _surface.PixelWidth, _surface.PixelHeight);
        _clipMask = null;
        _states.Clear();
    }

    protected override void OnEndFrame()
    {
        _surface.IncrementVersion();
        _pixels = [];
    }

    protected override void OnDispose()
    {
        _states.Clear();
    }

    public override void Clear(Color color)
    {
        var c = ToPremul(ApplyGlobalAlpha(color));
        for (int y = 0; y < _surface.PixelHeight; y++)
        {
            int row = y * _surface.StrideBytes;
            for (int x = 0; x < _surface.PixelWidth; x++)
            {
                int i = row + x * 4;
                _pixels[i + 0] = c.B;
                _pixels[i + 1] = c.G;
                _pixels[i + 2] = c.R;
                _pixels[i + 3] = c.A;
            }
        }
    }

    protected override void SaveCore()
        => _states.Push(new State(_transform, _clip, _clipMask, _globalAlpha, _textPixelSnap));

    protected override void RestoreCore()
    {
        if (_states.Count == 0)
        {
            return;
        }

        var state = _states.Pop();
        _transform = state.Transform;
        _clip = state.Clip;
        _clipMask = state.ClipMask;
        _globalAlpha = state.GlobalAlpha;
        _textPixelSnap = state.TextPixelSnap;
    }

    protected override void SetClipCore(Rect rect)
        => _clip = _clip.Intersect(ToPixelBounds(rect));

    protected override void SetClipRoundedRectCore(Rect rect, double radiusX, double radiusY)
    {
        if (IntersectRoundedRectClipMask(rect, radiusX, radiusY))
        {
            return;
        }

        usingPath(PathGeometry.FromRoundedRect(rect, radiusX, radiusY), path => IntersectClipMask(FlattenPath(path), FillRule.NonZero));
    }

    protected override void SetClipPathCore(PathGeometry path)
        => IntersectClipMask(FlattenPath(path), path.FillRule);

    protected override void TranslateCore(double dx, double dy)
        => _transform = Matrix3x2.CreateTranslation((float)dx, (float)dy) * _transform;

    protected override void RotateCore(double angleRadians)
        => _transform = Matrix3x2.CreateRotation((float)angleRadians) * _transform;

    protected override void ScaleCore(double sx, double sy)
        => _transform = Matrix3x2.CreateScale((float)sx, (float)sy) * _transform;

    protected override void SetTransformCore(Matrix3x2 matrix)
        => _transform = matrix * Matrix3x2.CreateScale((float)_surface.DpiScale);

    protected override Matrix3x2 GetTransformCore()
    {
        var inverseDpi = Matrix3x2.CreateScale((float)(1.0 / _surface.DpiScale));
        return _transform * inverseDpi;
    }

    protected override void ResetTransformCore()
        => _transform = Matrix3x2.CreateScale((float)_surface.DpiScale);

    protected override void ResetClipCore()
    {
        _clipMask = null;
        _clip = new RectI(0, 0, _surface.PixelWidth, _surface.PixelHeight);
    }

    protected override void DrawLineCore(Point start, Point end, Color color, double thickness = 1)
        => DrawTransformedLine(start, end, ApplyGlobalAlpha(color), thickness * DpiScale);

    protected override void DrawRectangleCore(Rect rect, Color color, double thickness, bool strokeInset)
    {
        if (strokeInset)
        {
            double half = QuantizeHalfStroke(thickness, DpiScale);
            rect = rect.Deflate(new Thickness(half));
        }

        if (DrawRoundedRectangleSdf(rect, 0, 0, ApplyGlobalAlpha(color), thickness))
        {
            return;
        }

        DrawLineCore(rect.TopLeft, rect.TopRight, color, thickness);
        DrawLineCore(rect.TopRight, rect.BottomRight, color, thickness);
        DrawLineCore(rect.BottomRight, rect.BottomLeft, color, thickness);
        DrawLineCore(rect.BottomLeft, rect.TopLeft, color, thickness);
    }

    protected override void FillRectangleCore(Rect rect, Color color)
    {
        color = ApplyGlobalAlpha(color);
        if (FillAxisAlignedRectangle(rect, color))
        {
            return;
        }

        if (FillRoundedRectangleSdf(rect, 0, 0, color))
        {
            return;
        }

        var points = TransformRect(rect);
        FillPolygon(points, color, FillRule.NonZero);
    }

    protected override void DrawRoundedRectangleCore(Rect rect, double radiusX, double radiusY, Color color, double thickness = 1)
    {
        if (DrawRoundedRectangleSdf(rect, radiusX, radiusY, ApplyGlobalAlpha(color), thickness))
        {
            return;
        }

        usingPath(PathGeometry.FromRoundedRect(rect, radiusX, radiusY), path => DrawPath(path, color, thickness));
    }

    protected override void FillRoundedRectangleCore(Rect rect, double radiusX, double radiusY, Color color)
    {
        if (FillRoundedRectangleSdf(rect, radiusX, radiusY, ApplyGlobalAlpha(color)))
        {
            return;
        }

        usingPath(PathGeometry.FromRoundedRect(rect, radiusX, radiusY), path => FillPath(path, color, FillRule.NonZero));
    }

    protected override void DrawEllipseCore(Rect bounds, Color color, double thickness = 1)
    {
        if (DrawEllipseSdf(bounds, ApplyGlobalAlpha(color), thickness))
        {
            return;
        }

        usingPath(PathGeometry.FromEllipse(bounds), path => DrawPath(path, color, thickness));
    }

    protected override void FillEllipseCore(Rect bounds, Color color)
    {
        if (FillEllipseSdf(bounds, ApplyGlobalAlpha(color)))
        {
            return;
        }

        usingPath(PathGeometry.FromEllipse(bounds), path => FillPath(path, color, FillRule.NonZero));
    }

    public override void DrawPath(PathGeometry path, Color color, double thickness = 1)
    {
        RecordDrawPath();
        var contours = FlattenPath(path);
        foreach (var contour in contours)
        {
            for (int i = 1; i < contour.Points.Count; i++)
            {
                DrawDeviceLine(contour.Points[i - 1], contour.Points[i], ApplyGlobalAlpha(color), thickness * DpiScale);
            }

            if (contour.Closed && contour.Points.Count > 1)
            {
                DrawDeviceLine(contour.Points[^1], contour.Points[0], ApplyGlobalAlpha(color), thickness * DpiScale);
            }
        }
    }

    public override void FillPath(PathGeometry path, Color color)
        => FillPath(path, color, path.FillRule);

    public override void FillPath(PathGeometry path, Color color, FillRule fillRule)
    {
        RecordFillPath();
        var contours = FlattenPath(path);
        FillContours(contours, ApplyGlobalAlpha(color), fillRule);
    }

    public override void FillRectangle(Rect rect, IBrush brush)
    {
        if (brush is ILinearGradientBrush linear)
        {
            FillGradient(rect, linear);
            return;
        }

        if (brush is IRadialGradientBrush radial)
        {
            FillGradient(rect, radial);
            return;
        }

        base.FillRectangle(rect, brush);
    }

    public override TextLayout? CreateTextLayout(ReadOnlySpan<char> text, TextFormat format, in TextLayoutConstraints constraints)
        => FramebufferText.CreateLayout(text, format, constraints);

    public override void DrawTextLayout(ReadOnlySpan<char> text, TextFormat format, TextLayout layout, Color color)
        => DrawTextRuns(text, layout.EffectiveBounds, format, layout, color);

    protected override void DrawTextCore(ReadOnlySpan<char> text, Rect bounds, IFont font, Color color,
        TextAlignment horizontalAlignment = TextAlignment.Left,
        TextAlignment verticalAlignment = TextAlignment.Top,
        TextWrapping wrapping = TextWrapping.NoWrap,
        TextTrimming trimming = TextTrimming.None)
    {
        var format = new TextFormat
        {
            Font = font,
            HorizontalAlignment = horizontalAlignment,
            VerticalAlignment = verticalAlignment,
            Wrapping = wrapping,
            Trimming = trimming,
        };
        var layout = FramebufferText.CreateLayout(text, format, new TextLayoutConstraints(bounds));
        DrawTextRuns(text, bounds, format, layout, color);
    }

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font)
        => FramebufferText.MeasureText(text, font);

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
        => FramebufferText.MeasureText(text, font, maxWidth);

    public override void DrawImage(IImage image, Point location)
    {
        if (image is not FramebufferImage framebufferImage)
        {
            return;
        }

        DrawImageCore(framebufferImage, new Rect(location.X, location.Y, framebufferImage.PixelWidth / DpiScale, framebufferImage.PixelHeight / DpiScale),
            new Rect(0, 0, framebufferImage.PixelWidth, framebufferImage.PixelHeight));
    }

    protected override void DrawImageCore(IImage image, Rect destRect)
    {
        if (image is FramebufferImage framebufferImage)
        {
            DrawImageCore(framebufferImage, destRect, new Rect(0, 0, framebufferImage.PixelWidth, framebufferImage.PixelHeight));
        }
    }

    protected override void DrawImageCore(IImage image, Rect destRect, Rect sourceRect)
    {
        if (image is FramebufferImage framebufferImage)
        {
            DrawImageCore(framebufferImage, destRect, sourceRect);
        }
    }

    private void DrawImageCore(FramebufferImage image, Rect destRect, Rect sourceRect)
    {
        if (TryBlitOpaqueImage(image, destRect, sourceRect))
        {
            return;
        }

        var fullDest = ToPixelBounds(destRect);
        var dest = fullDest.Intersect(_clip);
        if (dest.IsEmpty || fullDest.IsEmpty || destRect.Width == 0 || destRect.Height == 0)
        {
            return;
        }

        if (!Matrix3x2.Invert(_transform, out var inverseTransform))
        {
            return;
        }

        var pixels = image.Pixels;
        bool linear = ImageScaleQuality is ImageScaleQuality.Default or ImageScaleQuality.Normal or ImageScaleQuality.HighQuality;
        for (int y = dest.Y; y < dest.Bottom; y++)
        {
            for (int x = dest.X; x < dest.Right; x++)
            {
                var sample = SampleImagePixel(pixels, image.PixelWidth, image.PixelHeight, destRect, sourceRect, inverseTransform, x, y, linear);
                if (sample.A != 0)
                {
                    BlendPremulPixel(x, y,
                        ApplyPremulAlpha(sample.B),
                        ApplyPremulAlpha(sample.G),
                        ApplyPremulAlpha(sample.R),
                        ApplyAlpha(sample.A));
                }
            }
        }
    }

    private bool TryBlitOpaqueImage(FramebufferImage image, Rect destRect, Rect sourceRect)
    {
        if (!image.IsOpaque || _clipMask is not null || GlobalAlpha < 0.999f)
        {
            return false;
        }

        if (!IsWholeImageSource(image, sourceRect) ||
            !TryGetAxisAlignedDeviceRect(destRect, out var deviceRect, out _, out _) ||
            !IsInteger(deviceRect.X) ||
            !IsInteger(deviceRect.Y) ||
            !IsInteger(deviceRect.Width) ||
            !IsInteger(deviceRect.Height) ||
            (int)MathF.Round(deviceRect.Width) != image.PixelWidth ||
            (int)MathF.Round(deviceRect.Height) != image.PixelHeight)
        {
            return false;
        }

        var fullDest = ToPixelBounds(deviceRect);
        var dest = fullDest.Intersect(_clip);
        if (dest.IsEmpty)
        {
            return true;
        }

        var source = image.Pixels;
        int sourceX = dest.X - fullDest.X;
        int sourceY = dest.Y - fullDest.Y;
        int rowBytes = checked(dest.Width * 4);
        for (int y = 0; y < dest.Height; y++)
        {
            source.Slice(((sourceY + y) * image.PixelWidth + sourceX) * 4, rowBytes)
                .CopyTo(_pixels.AsSpan((dest.Y + y) * _surface.StrideBytes + dest.X * 4, rowBytes));
        }

        return true;
    }

    private static bool IsWholeImageSource(FramebufferImage image, Rect sourceRect)
        => NearlyEqual((float)sourceRect.X, 0) &&
           NearlyEqual((float)sourceRect.Y, 0) &&
           NearlyEqual((float)sourceRect.Width, image.PixelWidth) &&
           NearlyEqual((float)sourceRect.Height, image.PixelHeight);

    private static bool IsInteger(float value)
        => MathF.Abs(value - MathF.Round(value)) <= 0.001f;

    private void DrawTextRuns(ReadOnlySpan<char> text, Rect bounds, TextFormat format, TextLayout layout, Color color)
    {
        if (format.Font is FreeTypeFont freeTypeFont)
        {
            DrawFreeTypeText(text, bounds, format, layout, color, freeTypeFont);
            return;
        }

        // Match the MewVG X11 backend: if the system font stack could not
        // produce a FreeType font, keep layout measurable but skip glyph drawing.
    }

    private void DrawFreeTypeText(ReadOnlySpan<char> text, Rect bounds, TextFormat format, TextLayout layout, Color color, FreeTypeFont font)
    {
        if (text.IsEmpty || color.A == 0)
        {
            return;
        }

        var deviceBounds = ToPixelBounds(bounds).Intersect(_clip);
        if (deviceBounds.IsEmpty)
        {
            return;
        }

        var fullDeviceBounds = ToPixelBounds(bounds);
        int widthPx = Math.Max(1, fullDeviceBounds.Width);
        int heightPx = Math.Max(1, fullDeviceBounds.Height);

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            double scale = Math.Max(DpiScale, FramebufferText.GetFontPixelScale(font));
            var measured = layout.MeasuredSize;
            widthPx = Math.Max(1, (int)Math.Ceiling(measured.Width * scale));
            heightPx = Math.Max(1, (int)Math.Ceiling(measured.Height * scale));
            bounds = new Rect(bounds.X, bounds.Y, widthPx / scale, heightPx / scale);
        }

        var bitmap = FreeTypeText.Rasterize(
            text,
            font,
            widthPx,
            heightPx,
            color,
            format.HorizontalAlignment,
            format.VerticalAlignment,
            format.Wrapping,
            format.Trimming);

        using var image = FramebufferImage.FromBgra(bitmap.WidthPx, bitmap.HeightPx, bitmap.Data, sourcePremultiplied: false);
        DrawImageCore(image, bounds, new Rect(0, 0, bitmap.WidthPx, bitmap.HeightPx));
    }

    private void FillGradient(Rect rect, ILinearGradientBrush brush)
    {
        var bounds = ToPixelBounds(rect).Intersect(_clip);
        if (bounds.IsEmpty || brush.Stops.Count == 0)
        {
            return;
        }

        var start = TransformPoint(brush.StartPoint);
        var end = TransformPoint(brush.EndPoint);
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        double lenSq = dx * dx + dy * dy;
        if (lenSq <= 0.0001)
        {
            FillRectangleCore(rect, brush.Stops[^1].Color);
            return;
        }

        for (int y = bounds.Y; y < bounds.Bottom; y++)
        {
            for (int x = bounds.X; x < bounds.Right; x++)
            {
                double t = ((x - start.X) * dx + (y - start.Y) * dy) / lenSq;
                BlendPixel(x, y, SampleGradient(brush.Stops, ApplySpread(t, brush.SpreadMethod)));
            }
        }
    }

    private void FillGradient(Rect rect, IRadialGradientBrush brush)
    {
        var bounds = ToPixelBounds(rect).Intersect(_clip);
        if (bounds.IsEmpty || brush.Stops.Count == 0)
        {
            return;
        }

        var center = TransformPoint(brush.Center);
        double radius = Math.Max(brush.RadiusX, brush.RadiusY) * DpiScale;
        if (radius <= 0.0001)
        {
            FillRectangleCore(rect, brush.Stops[^1].Color);
            return;
        }

        for (int y = bounds.Y; y < bounds.Bottom; y++)
        {
            for (int x = bounds.X; x < bounds.Right; x++)
            {
                double dx = x - center.X;
                double dy = y - center.Y;
                double t = Math.Sqrt(dx * dx + dy * dy) / radius;
                BlendPixel(x, y, SampleGradient(brush.Stops, ApplySpread(t, brush.SpreadMethod)));
            }
        }
    }

    private void FillPolygon(Vector2[] points, Color color, FillRule fillRule)
    {
        var contour = new Contour(points.ToList(), Closed: true);
        FillContours([contour], color, fillRule);
    }

    private bool FillAxisAlignedRectangle(Rect rect, Color color)
    {
        if (color.A == 0)
        {
            return true;
        }

        if (!TryGetAxisAlignedDeviceRect(rect, out var deviceRect, out _, out _))
        {
            return false;
        }

        var bounds = ToPixelBounds(deviceRect).Intersect(_clip);
        if (bounds.IsEmpty)
        {
            return true;
        }

        var premul = ToPremul(color);
        if (_clipMask is null && premul.A == 255)
        {
            uint packed = (uint)(premul.B | (premul.G << 8) | (premul.R << 16) | (premul.A << 24));
            for (int y = bounds.Y; y < bounds.Bottom; y++)
            {
                var row = _pixels.AsSpan(y * _surface.StrideBytes + bounds.X * 4, bounds.Width * 4);
                MemoryMarshal.Cast<byte, uint>(row).Fill(packed);
            }

            return true;
        }

        for (int y = bounds.Y; y < bounds.Bottom; y++)
        {
            int maskRow = y * _surface.PixelWidth;
            int pixel = y * _surface.StrideBytes + bounds.X * 4;
            for (int x = bounds.X; x < bounds.Right; x++, pixel += 4)
            {
                byte sb = premul.B;
                byte sg = premul.G;
                byte sr = premul.R;
                byte sa = premul.A;

                if (_clipMask is not null)
                {
                    byte clipAlpha = _clipMask[maskRow + x];
                    if (clipAlpha == 0)
                    {
                        continue;
                    }

                    if (clipAlpha != 255)
                    {
                        sb = ScaleByte(sb, clipAlpha);
                        sg = ScaleByte(sg, clipAlpha);
                        sr = ScaleByte(sr, clipAlpha);
                        sa = ScaleByte(sa, clipAlpha);
                    }
                }

                if (sa == 255)
                {
                    _pixels[pixel + 0] = sb;
                    _pixels[pixel + 1] = sg;
                    _pixels[pixel + 2] = sr;
                    _pixels[pixel + 3] = 255;
                    continue;
                }

                int inv = 255 - sa;
                _pixels[pixel + 0] = (byte)(sb + (_pixels[pixel + 0] * inv + 127) / 255);
                _pixels[pixel + 1] = (byte)(sg + (_pixels[pixel + 1] * inv + 127) / 255);
                _pixels[pixel + 2] = (byte)(sr + (_pixels[pixel + 2] * inv + 127) / 255);
                _pixels[pixel + 3] = (byte)(sa + (_pixels[pixel + 3] * inv + 127) / 255);
            }
        }

        return true;
    }

    private void FillContours(List<Contour> contours, Color color, FillRule fillRule)
    {
        if (contours.Count == 0 || color.A == 0)
        {
            return;
        }

        var bounds = GetContoursBounds(contours).Intersect(_clip);
        if (bounds.IsEmpty)
        {
            return;
        }

        for (int y = bounds.Y; y < bounds.Bottom; y++)
        {
            for (int x = bounds.X; x < bounds.Right; x++)
            {
                float coverage = GetFillCoverage(contours, x, y, fillRule);
                if (coverage > 0)
                {
                    BlendPixel(x, y, color, coverage);
                }
            }
        }
    }

    private bool FillRoundedRectangleSdf(Rect rect, double radiusX, double radiusY, Color color)
    {
        if (color.A == 0 || !TryGetAxisAlignedDeviceRect(rect, out var deviceRect, out var scaleX, out var scaleY))
        {
            return false;
        }

        var bounds = InflateToPixelBounds(deviceRect, 1).Intersect(_clip);
        if (bounds.IsEmpty)
        {
            return true;
        }

        float width = Math.Max(0.001f, deviceRect.Width);
        float height = Math.Max(0.001f, deviceRect.Height);
        var sdf = new RoundedRectSdf(width, height,
            (float)Math.Max(0, radiusX * scaleX),
            (float)Math.Max(0, radiusY * scaleY));
        float centerX = deviceRect.X + width * 0.5f;
        float centerY = deviceRect.Y + height * 0.5f;

        for (int y = bounds.Y; y < bounds.Bottom; y++)
        {
            for (int x = bounds.X; x < bounds.Right; x++)
            {
                float sx = x + 0.5f - centerX;
                float sy = y + 0.5f - centerY;
                float dist = sdf.GetSignedDistance(sx, sy);
                byte alpha = dist <= -0.5f ? color.A :
                    dist >= 0.5f ? (byte)0 :
                    SampleSdfCoverage(p => sdf.GetSignedDistance(p.X - centerX, p.Y - centerY), x, y, color.A);

                if (alpha != 0)
                {
                    BlendPixel(x, y, color.WithAlpha(alpha));
                }
            }
        }

        return true;
    }

    private bool DrawRoundedRectangleSdf(Rect rect, double radiusX, double radiusY, Color color, double thickness)
    {
        if (color.A == 0 || thickness <= 0 || !TryGetAxisAlignedDeviceRect(rect, out var deviceRect, out var scaleX, out var scaleY))
        {
            return false;
        }

        var bounds = InflateToPixelBounds(deviceRect, (float)(thickness * DpiScale + 2)).Intersect(_clip);
        if (bounds.IsEmpty)
        {
            return true;
        }

        float stroke = Math.Max(1f, (float)(thickness * DpiScale));
        float width = Math.Max(0.001f, deviceRect.Width);
        float height = Math.Max(0.001f, deviceRect.Height);
        var outer = new RoundedRectSdf(width, height,
            (float)Math.Max(0, radiusX * scaleX),
            (float)Math.Max(0, radiusY * scaleY));
        float innerW = Math.Max(0, width - stroke * 2);
        float innerH = Math.Max(0, height - stroke * 2);
        RoundedRectSdf? inner = innerW > 0 && innerH > 0
            ? new RoundedRectSdf(innerW, innerH,
                Math.Max(0, (float)(radiusX * scaleX) - stroke),
                Math.Max(0, (float)(radiusY * scaleY) - stroke))
            : null;

        float centerX = deviceRect.X + width * 0.5f;
        float centerY = deviceRect.Y + height * 0.5f;
        for (int y = bounds.Y; y < bounds.Bottom; y++)
        {
            for (int x = bounds.X; x < bounds.Right; x++)
            {
                byte alpha = SampleStrokeCoverage(
                    sx => outer.GetSignedDistance(sx.X, sx.Y),
                    inner is null ? null : sx => inner.GetSignedDistance(sx.X, sx.Y),
                    x, y, centerX, centerY, color.A);
                if (alpha != 0)
                {
                    BlendPixel(x, y, color.WithAlpha(alpha));
                }
            }
        }

        return true;
    }

    private bool FillEllipseSdf(Rect rect, Color color)
    {
        if (color.A == 0 || !TryGetAxisAlignedDeviceRect(rect, out var deviceRect, out _, out _))
        {
            return false;
        }

        var bounds = InflateToPixelBounds(deviceRect, 1).Intersect(_clip);
        if (bounds.IsEmpty)
        {
            return true;
        }

        var sdf = EllipseSdf.FromBounds(deviceRect.X, deviceRect.Y, deviceRect.Width, deviceRect.Height);
        var sampler = new SupersampleEdgeSampler(EdgeSupersample, color.A);
        for (int y = bounds.Y; y < bounds.Bottom; y++)
        {
            for (int x = bounds.X; x < bounds.Right; x++)
            {
                float dist = sdf.GetSignedDistance(x + 0.5f, y + 0.5f);
                byte alpha = dist <= -0.5f ? color.A :
                    dist >= 0.5f ? (byte)0 :
                    sampler.SampleEllipseEdge(x, y, sdf);
                if (alpha != 0)
                {
                    BlendPixel(x, y, color.WithAlpha(alpha));
                }
            }
        }

        return true;
    }

    private bool DrawEllipseSdf(Rect rect, Color color, double thickness)
    {
        if (color.A == 0 || thickness <= 0 || !TryGetAxisAlignedDeviceRect(rect, out var deviceRect, out _, out _))
        {
            return false;
        }

        var bounds = InflateToPixelBounds(deviceRect, (float)(thickness * DpiScale + 2)).Intersect(_clip);
        if (bounds.IsEmpty)
        {
            return true;
        }

        float stroke = Math.Max(1f, (float)(thickness * DpiScale));
        var outer = EllipseSdf.FromBounds(deviceRect.X, deviceRect.Y, deviceRect.Width, deviceRect.Height);
        RectF innerRect = new(deviceRect.X + stroke, deviceRect.Y + stroke,
            Math.Max(0, deviceRect.Width - stroke * 2),
            Math.Max(0, deviceRect.Height - stroke * 2));
        EllipseSdf? inner = innerRect.Width > 0 && innerRect.Height > 0
            ? EllipseSdf.FromBounds(innerRect.X, innerRect.Y, innerRect.Width, innerRect.Height)
            : null;

        for (int y = bounds.Y; y < bounds.Bottom; y++)
        {
            for (int x = bounds.X; x < bounds.Right; x++)
            {
                byte alpha = SampleStrokeCoverage(
                    p => outer.GetSignedDistance(p.X, p.Y),
                    inner is null ? null : p => inner.GetSignedDistance(p.X, p.Y),
                    x, y, 0, 0, color.A);
                if (alpha != 0)
                {
                    BlendPixel(x, y, color.WithAlpha(alpha));
                }
            }
        }

        return true;
    }

    private static byte SampleSdfCoverage(Func<Vector2, float> distance, int x, int y, byte sourceAlpha)
    {
        int covered = 0;
        for (int sy = 0; sy < EdgeSupersample; sy++)
        {
            float py = y + (sy + 0.5f) / EdgeSupersample;
            for (int sx = 0; sx < EdgeSupersample; sx++)
            {
                float px = x + (sx + 0.5f) / EdgeSupersample;
                if (distance(new Vector2(px, py)) <= 0)
                {
                    covered++;
                }
            }
        }

        return (byte)((sourceAlpha * covered + 4) / 9);
    }

    private static byte SampleStrokeCoverage(
        Func<Vector2, float> outerDistance,
        Func<Vector2, float>? innerDistance,
        int x,
        int y,
        float centerX,
        float centerY,
        byte sourceAlpha)
    {
        int covered = 0;
        for (int sy = 0; sy < EdgeSupersample; sy++)
        {
            float py = y + (sy + 0.5f) / EdgeSupersample - centerY;
            for (int sx = 0; sx < EdgeSupersample; sx++)
            {
                float px = x + (sx + 0.5f) / EdgeSupersample - centerX;
                var p = new Vector2(px, py);
                if (outerDistance(p) > 0)
                {
                    continue;
                }

                if (innerDistance is not null && innerDistance(p) <= 0)
                {
                    continue;
                }

                covered++;
            }
        }

        return (byte)((sourceAlpha * covered + 4) / 9);
    }

    private bool TryGetAxisAlignedDeviceRect(Rect rect, out RectF deviceRect, out float scaleX, out float scaleY)
    {
        var points = TransformRect(rect);
        deviceRect = default;
        scaleX = scaleY = 1;

        bool axisAligned =
            NearlyEqual(points[0].Y, points[1].Y) &&
            NearlyEqual(points[2].Y, points[3].Y) &&
            NearlyEqual(points[0].X, points[3].X) &&
            NearlyEqual(points[1].X, points[2].X);
        if (!axisAligned)
        {
            return false;
        }

        float left = points.Min(p => p.X);
        float top = points.Min(p => p.Y);
        float right = points.Max(p => p.X);
        float bottom = points.Max(p => p.Y);
        if (right <= left || bottom <= top)
        {
            return false;
        }

        deviceRect = new RectF(left, top, right - left, bottom - top);
        scaleX = rect.Width == 0 ? 1 : Math.Abs(deviceRect.Width / (float)rect.Width);
        scaleY = rect.Height == 0 ? 1 : Math.Abs(deviceRect.Height / (float)rect.Height);
        return true;
    }

    private static bool NearlyEqual(float a, float b)
        => MathF.Abs(a - b) <= 0.001f;

    private static RectI InflateToPixelBounds(RectF rect, float pad)
    {
        int left = (int)MathF.Floor(rect.X - pad);
        int top = (int)MathF.Floor(rect.Y - pad);
        int right = (int)MathF.Ceiling(rect.Right + pad);
        int bottom = (int)MathF.Ceiling(rect.Bottom + pad);
        return new RectI(left, top, right - left, bottom - top);
    }

    private static RectI ToPixelBounds(RectF rect)
    {
        int left = (int)MathF.Floor(rect.X);
        int top = (int)MathF.Floor(rect.Y);
        int right = (int)MathF.Ceiling(rect.Right);
        int bottom = (int)MathF.Ceiling(rect.Bottom);
        return new RectI(left, top, right - left, bottom - top);
    }

    private void IntersectClipMask(List<Contour> contours, FillRule fillRule)
    {
        if (contours.Count == 0)
        {
            _clip = RectI.Empty;
            _clipMask = new byte[_surface.PixelWidth * _surface.PixelHeight];
            return;
        }

        var shapeBounds = GetContoursBounds(contours);
        var newClip = _clip.Intersect(shapeBounds);
        var nextMask = new byte[_surface.PixelWidth * _surface.PixelHeight];
        if (!newClip.IsEmpty)
        {
            byte[]? previousMask = _clipMask;
            RectI previousClip = _clip;
            for (int y = newClip.Y; y < newClip.Bottom; y++)
            {
                for (int x = newClip.X; x < newClip.Right; x++)
                {
                    byte shapeAlpha = CoverageToByte(GetFillCoverage(contours, x, y, fillRule));
                    if (shapeAlpha == 0)
                    {
                        continue;
                    }

                    int mi = y * _surface.PixelWidth + x;
                    byte existingAlpha = previousMask is null
                        ? (previousClip.Contains(x, y) ? (byte)255 : (byte)0)
                        : previousMask[mi];
                    nextMask[mi] = Math.Min(existingAlpha, shapeAlpha);
                }
            }
        }

        _clip = newClip;
        _clipMask = nextMask;
    }

    private bool IntersectRoundedRectClipMask(Rect rect, double radiusX, double radiusY)
    {
        if (!TryGetAxisAlignedDeviceRect(rect, out var deviceRect, out var scaleX, out var scaleY))
        {
            return false;
        }

        var shapeBounds = InflateToPixelBounds(deviceRect, 1);
        var newClip = _clip.Intersect(shapeBounds);
        var nextMask = new byte[_surface.PixelWidth * _surface.PixelHeight];
        if (!newClip.IsEmpty)
        {
            byte[]? previousMask = _clipMask;
            RectI previousClip = _clip;
            float width = Math.Max(0.001f, deviceRect.Width);
            float height = Math.Max(0.001f, deviceRect.Height);
            float centerX = deviceRect.X + width * 0.5f;
            float centerY = deviceRect.Y + height * 0.5f;
            var sdf = new RoundedRectSdf(width, height,
                (float)Math.Max(0, radiusX * scaleX),
                (float)Math.Max(0, radiusY * scaleY));

            for (int y = newClip.Y; y < newClip.Bottom; y++)
            {
                for (int x = newClip.X; x < newClip.Right; x++)
                {
                    float sx = x + 0.5f - centerX;
                    float sy = y + 0.5f - centerY;
                    float dist = sdf.GetSignedDistance(sx, sy);
                    byte shapeAlpha = dist <= -0.5f ? (byte)255 :
                        dist >= 0.5f ? (byte)0 :
                        SampleSdfCoverage(p => sdf.GetSignedDistance(p.X - centerX, p.Y - centerY), x, y, 255);
                    if (shapeAlpha == 0)
                    {
                        continue;
                    }

                    int mi = y * _surface.PixelWidth + x;
                    byte existingAlpha = previousMask is null
                        ? (previousClip.Contains(x, y) ? (byte)255 : (byte)0)
                        : previousMask[mi];
                    nextMask[mi] = Math.Min(existingAlpha, shapeAlpha);
                }
            }
        }

        _clip = newClip;
        _clipMask = nextMask;
        return true;
    }

    private List<Contour> FlattenPath(PathGeometry path)
    {
        var contours = new List<Contour>();
        List<Vector2>? current = null;
        Vector2 currentPoint = default;
        Vector2 startPoint = default;

        foreach (var command in path.Commands)
        {
            switch (command.Type)
            {
                case PathCommandType.MoveTo:
                    if (current is { Count: > 0 })
                    {
                        contours.Add(new Contour(current, Closed: false));
                    }
                    currentPoint = TransformPoint(command.X0, command.Y0);
                    startPoint = currentPoint;
                    current = [currentPoint];
                    break;
                case PathCommandType.LineTo:
                    current ??= [];
                    currentPoint = TransformPoint(command.X0, command.Y0);
                    current.Add(currentPoint);
                    break;
                case PathCommandType.BezierTo:
                    current ??= [currentPoint];
                    var c1 = TransformPoint(command.X0, command.Y0);
                    var c2 = TransformPoint(command.X1, command.Y1);
                    var end = TransformPoint(command.X2, command.Y2);
                    FlattenCubic(current, currentPoint, c1, c2, end, 16);
                    currentPoint = end;
                    break;
                case PathCommandType.Close:
                    if (current is { Count: > 0 })
                    {
                        if (Vector2.DistanceSquared(current[^1], startPoint) > 0.01f)
                        {
                            current.Add(startPoint);
                        }
                        contours.Add(new Contour(current, Closed: true));
                        current = null;
                        currentPoint = startPoint;
                    }
                    break;
            }
        }

        if (current is { Count: > 0 })
        {
            contours.Add(new Contour(current, Closed: false));
        }

        return contours;
    }

    private void DrawTransformedLine(Point start, Point end, Color color, double thickness)
        => DrawDeviceLine(TransformPoint(start), TransformPoint(end), color, thickness);

    private void DrawDeviceLine(Vector2 start, Vector2 end, Color color, double thickness)
    {
        if (color.A == 0 || thickness <= 0)
        {
            return;
        }

        float radius = Math.Max(0.5f, (float)thickness * 0.5f);
        int left = (int)MathF.Floor(MathF.Min(start.X, end.X) - radius - 1);
        int top = (int)MathF.Floor(MathF.Min(start.Y, end.Y) - radius - 1);
        int right = (int)MathF.Ceiling(MathF.Max(start.X, end.X) + radius + 1);
        int bottom = (int)MathF.Ceiling(MathF.Max(start.Y, end.Y) + radius + 1);
        var bounds = new RectI(left, top, right - left, bottom - top).Intersect(_clip);
        if (bounds.IsEmpty)
        {
            return;
        }

        var lineSdf = new LineSdf(start.X, start.Y, end.X, end.Y, (float)thickness);
        var sampler = new SupersampleEdgeSampler(EdgeSupersample, color.A);
        float halfThicknessSq = MathF.Pow(Math.Max(0.5f, (float)thickness * 0.5f), 2);
        for (int y = bounds.Y; y < bounds.Bottom; y++)
        {
            for (int x = bounds.X; x < bounds.Right; x++)
            {
                float dist = lineSdf.GetSignedDistance(x + 0.5f, y + 0.5f);
                byte alpha = dist <= -0.5f ? color.A :
                    dist >= 0.5f ? (byte)0 :
                    sampler.SampleLineEdge(x, y, lineSdf, halfThicknessSq);
                if (alpha != 0)
                {
                    BlendPixel(x, y, color.WithAlpha(alpha));
                }
            }
        }
    }

    private Vector2[] TransformRect(Rect rect)
        => [TransformPoint(rect.TopLeft), TransformPoint(rect.TopRight), TransformPoint(rect.BottomRight), TransformPoint(rect.BottomLeft)];

    private Vector2 TransformPoint(Point point)
        => TransformPoint(point.X, point.Y);

    private Vector2 TransformPoint(double x, double y)
        => Vector2.Transform(new Vector2((float)x, (float)y), _transform);

    private RectI ToPixelBounds(Rect rect)
    {
        var points = TransformRect(rect);
        float left = points.Min(p => p.X);
        float top = points.Min(p => p.Y);
        float right = points.Max(p => p.X);
        float bottom = points.Max(p => p.Y);
        int x = (int)MathF.Floor(left);
        int y = (int)MathF.Floor(top);
        return new RectI(x, y, (int)MathF.Ceiling(right) - x, (int)MathF.Ceiling(bottom) - y);
    }

    private void BlendPixel(int x, int y, Color color)
    {
        var premul = ToPremul(color);
        BlendPremulPixel(x, y, premul.B, premul.G, premul.R, premul.A);
    }

    private void BlendPixel(int x, int y, Color color, float coverage)
    {
        coverage = Math.Clamp(coverage, 0, 1);
        if (coverage <= 0)
        {
            return;
        }

        var premul = ToPremul(color);
        BlendPremulPixel(x, y,
            ScaleByte(premul.B, coverage),
            ScaleByte(premul.G, coverage),
            ScaleByte(premul.R, coverage),
            ScaleByte(premul.A, coverage));
    }

    private void BlendPremulPixel(int x, int y, byte sb, byte sg, byte sr, byte sa)
    {
        if (sa == 0 || !_clip.Contains(x, y))
        {
            return;
        }

        if (_clipMask is not null)
        {
            byte clipAlpha = _clipMask[y * _surface.PixelWidth + x];
            if (clipAlpha == 0)
            {
                return;
            }

            if (clipAlpha != 255)
            {
                sb = ScaleByte(sb, clipAlpha);
                sg = ScaleByte(sg, clipAlpha);
                sr = ScaleByte(sr, clipAlpha);
                sa = ScaleByte(sa, clipAlpha);
            }
        }

        int i = y * _surface.StrideBytes + x * 4;
        if (sa == 255)
        {
            _pixels[i + 0] = sb;
            _pixels[i + 1] = sg;
            _pixels[i + 2] = sr;
            _pixels[i + 3] = 255;
            return;
        }

        int inv = 255 - sa;
        _pixels[i + 0] = (byte)(sb + (_pixels[i + 0] * inv + 127) / 255);
        _pixels[i + 1] = (byte)(sg + (_pixels[i + 1] * inv + 127) / 255);
        _pixels[i + 2] = (byte)(sr + (_pixels[i + 2] * inv + 127) / 255);
        _pixels[i + 3] = (byte)(sa + (_pixels[i + 3] * inv + 127) / 255);
    }

    private Color ApplyGlobalAlpha(Color color)
    {
        if (_globalAlpha >= 0.999f)
        {
            return color;
        }

        return color.WithAlpha((byte)Math.Clamp(Math.Round(color.A * _globalAlpha), 0, 255));
    }

    private byte ApplyAlpha(byte alpha)
        => _globalAlpha >= 0.999f ? alpha : (byte)Math.Clamp(Math.Round(alpha * _globalAlpha), 0, 255);

    private byte ApplyPremulAlpha(byte value)
        => _globalAlpha >= 0.999f ? value : (byte)Math.Clamp(Math.Round(value * _globalAlpha), 0, 255);

    private static PremulColor ToPremul(Color color)
    {
        byte a = color.A;
        return new PremulColor(
            Premultiply(color.B, a),
            Premultiply(color.G, a),
            Premultiply(color.R, a),
            a);
    }

    private static byte Premultiply(byte c, byte a)
        => a == 255 ? c : (byte)((c * a + 127) / 255);

    private static byte ScaleByte(byte value, float scale)
        => (byte)Math.Clamp(MathF.Round(value * scale), 0, 255);

    private static byte ScaleByte(byte value, byte scale)
        => scale == 255 ? value : (byte)((value * scale + 127) / 255);

    private static byte CoverageToByte(float coverage)
        => (byte)Math.Clamp(MathF.Round(Math.Clamp(coverage, 0, 1) * 255), 0, 255);

    private static PremulColor SampleNearestPremul(ReadOnlySpan<byte> pixels, int width, int height, double sx, double sy)
    {
        int srcX = Math.Clamp((int)Math.Floor(sx), 0, width - 1);
        int srcY = Math.Clamp((int)Math.Floor(sy), 0, height - 1);
        int si = (srcY * width + srcX) * 4;
        return new PremulColor(pixels[si], pixels[si + 1], pixels[si + 2], pixels[si + 3]);
    }

    private static PremulColor SampleImagePixel(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        Rect destRect,
        Rect sourceRect,
        Matrix3x2 inverseTransform,
        int x,
        int y,
        bool linear)
    {
        int coveredSamples = 0;
        int b = 0;
        int g = 0;
        int r = 0;
        int a = 0;

        for (int sy = 0; sy < FillSupersample; sy++)
        {
            float py = y + (sy + 0.5f) / FillSupersample;
            for (int sx = 0; sx < FillSupersample; sx++)
            {
                float px = x + (sx + 0.5f) / FillSupersample;
                var local = Vector2.Transform(new Vector2(px, py), inverseTransform);
                double u = (local.X - destRect.X) / destRect.Width;
                double v = (local.Y - destRect.Y) / destRect.Height;
                if (u < 0 || u >= 1 || v < 0 || v >= 1)
                {
                    continue;
                }

                double imageX = sourceRect.X + u * sourceRect.Width;
                double imageY = sourceRect.Y + v * sourceRect.Height;
                var sample = linear
                    ? SampleBilinearPremul(pixels, width, height, imageX - 0.5, imageY - 0.5)
                    : SampleNearestPremul(pixels, width, height, imageX, imageY);

                b += sample.B;
                g += sample.G;
                r += sample.R;
                a += sample.A;
                coveredSamples++;
            }
        }

        if (coveredSamples == 0)
        {
            return default;
        }

        const int totalSamples = FillSupersample * FillSupersample;
        return new PremulColor(
            (byte)((b + totalSamples / 2) / totalSamples),
            (byte)((g + totalSamples / 2) / totalSamples),
            (byte)((r + totalSamples / 2) / totalSamples),
            (byte)((a + totalSamples / 2) / totalSamples));
    }

    private static PremulColor SampleBilinearPremul(ReadOnlySpan<byte> pixels, int width, int height, double sx, double sy)
    {
        sx = Math.Clamp(sx, 0, width - 1);
        sy = Math.Clamp(sy, 0, height - 1);

        int x0 = (int)Math.Floor(sx);
        int y0 = (int)Math.Floor(sy);
        int x1 = Math.Min(width - 1, x0 + 1);
        int y1 = Math.Min(height - 1, y0 + 1);
        double tx = sx - x0;
        double ty = sy - y0;

        int i00 = (y0 * width + x0) * 4;
        int i10 = (y0 * width + x1) * 4;
        int i01 = (y1 * width + x0) * 4;
        int i11 = (y1 * width + x1) * 4;

        return new PremulColor(
            LerpByte(Bilerp(pixels[i00], pixels[i10], pixels[i01], pixels[i11], tx, ty)),
            LerpByte(Bilerp(pixels[i00 + 1], pixels[i10 + 1], pixels[i01 + 1], pixels[i11 + 1], tx, ty)),
            LerpByte(Bilerp(pixels[i00 + 2], pixels[i10 + 2], pixels[i01 + 2], pixels[i11 + 2], tx, ty)),
            LerpByte(Bilerp(pixels[i00 + 3], pixels[i10 + 3], pixels[i01 + 3], pixels[i11 + 3], tx, ty)));
    }

    private static double Bilerp(byte c00, byte c10, byte c01, byte c11, double tx, double ty)
    {
        double top = c00 + (c10 - c00) * tx;
        double bottom = c01 + (c11 - c01) * tx;
        return top + (bottom - top) * ty;
    }

    private static byte LerpByte(double value)
        => (byte)Math.Clamp(Math.Round(value), 0, 255);

    private static Color SampleGradient(IReadOnlyList<GradientStop> stops, double t)
    {
        if (stops.Count == 1)
        {
            return stops[0].Color;
        }

        GradientStop left = stops[0];
        GradientStop right = stops[^1];
        for (int i = 1; i < stops.Count; i++)
        {
            if (t <= stops[i].Offset)
            {
                left = stops[i - 1];
                right = stops[i];
                break;
            }
        }

        double span = right.Offset - left.Offset;
        double local = span <= 0 ? 0 : (t - left.Offset) / span;
        return left.Color.Lerp(right.Color, local);
    }

    private static double ApplySpread(double t, SpreadMethod spread)
        => spread switch
        {
            SpreadMethod.Repeat => t - Math.Floor(t),
            SpreadMethod.Reflect => Math.Abs((t - Math.Floor(t / 2) * 2) - 1),
            _ => Math.Clamp(t, 0, 1),
        };

    private static RectI GetContoursBounds(List<Contour> contours)
    {
        float left = float.MaxValue;
        float top = float.MaxValue;
        float right = float.MinValue;
        float bottom = float.MinValue;
        foreach (var contour in contours)
        {
            foreach (var p in contour.Points)
            {
                left = MathF.Min(left, p.X);
                top = MathF.Min(top, p.Y);
                right = MathF.Max(right, p.X);
                bottom = MathF.Max(bottom, p.Y);
            }
        }

        if (left == float.MaxValue)
        {
            return RectI.Empty;
        }

        int x = (int)MathF.Floor(left) - 1;
        int y = (int)MathF.Floor(top) - 1;
        return new RectI(x, y, (int)MathF.Ceiling(right) - x + 1, (int)MathF.Ceiling(bottom) - y + 1);
    }

    private static float GetFillCoverage(List<Contour> contours, int x, int y, FillRule fillRule)
    {
        int covered = 0;
        const float step = 1f / FillSupersample;
        const float halfStep = step * 0.5f;

        for (int sy = 0; sy < FillSupersample; sy++)
        {
            float py = y + halfStep + sy * step;
            for (int sx = 0; sx < FillSupersample; sx++)
            {
                float px = x + halfStep + sx * step;
                if (Contains(contours, px, py, fillRule))
                {
                    covered++;
                }
            }
        }

        return covered / (float)(FillSupersample * FillSupersample);
    }

    private static bool Contains(List<Contour> contours, float x, float y, FillRule fillRule)
    {
        int winding = 0;
        bool inside = false;
        foreach (var contour in contours)
        {
            var points = contour.Points;
            if (points.Count < 3)
            {
                continue;
            }

            for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
            {
                var pi = points[i];
                var pj = points[j];
                bool crosses = (pi.Y > y) != (pj.Y > y);
                if (!crosses)
                {
                    continue;
                }

                float xAtY = (pj.X - pi.X) * (y - pi.Y) / (pj.Y - pi.Y) + pi.X;
                if (x < xAtY)
                {
                    inside = !inside;
                    winding += pj.Y > pi.Y ? 1 : -1;
                }
            }
        }

        return fillRule == FillRule.EvenOdd ? inside : winding != 0;
    }

    private static void FlattenCubic(List<Vector2> output, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, int steps)
    {
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            float mt = 1 - t;
            var p = mt * mt * mt * p0 +
                    3 * mt * mt * t * p1 +
                    3 * mt * t * t * p2 +
                    t * t * t * p3;
            output.Add(p);
        }
    }

    private static void usingPath(PathGeometry path, Action<PathGeometry> action)
        => action(path);

    private readonly record struct State(Matrix3x2 Transform, RectI Clip, byte[]? ClipMask, float GlobalAlpha, bool TextPixelSnap);

    private readonly record struct PremulColor(byte B, byte G, byte R, byte A);

    private readonly record struct RectF(float X, float Y, float Width, float Height)
    {
        public float Right => X + Width;

        public float Bottom => Y + Height;
    }

    private sealed record Contour(List<Vector2> Points, bool Closed);

    private readonly record struct RectI(int X, int Y, int Width, int Height)
    {
        public static readonly RectI Empty = new(0, 0, 0, 0);

        public int Right => X + Width;

        public int Bottom => Y + Height;

        public bool IsEmpty => Width <= 0 || Height <= 0;

        public bool Contains(int x, int y)
            => x >= X && x < Right && y >= Y && y < Bottom;

        public RectI Intersect(RectI other)
        {
            int x = Math.Max(X, other.X);
            int y = Math.Max(Y, other.Y);
            int right = Math.Min(Right, other.Right);
            int bottom = Math.Min(Bottom, other.Bottom);
            return right > x && bottom > y ? new RectI(x, y, right - x, bottom - y) : Empty;
        }
    }
}
