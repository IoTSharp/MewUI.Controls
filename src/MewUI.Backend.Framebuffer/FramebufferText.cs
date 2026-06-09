using Aprillz.MewUI.Rendering.FreeType;

namespace Aprillz.MewUI.Rendering.Framebuffer;

internal static class FramebufferText
{
    private const string Ellipsis = "...";

    public static TextLayout CreateLayout(ReadOnlySpan<char> text, TextFormat format, in TextLayoutConstraints constraints)
    {
        string value = text.ToString();
        double maxWidth = NormalizeConstraint(constraints.Bounds.Width);
        double maxHeight = NormalizeConstraint(constraints.Bounds.Height);
        double measuredWidth = 0;
        int lineCount = 0;

        TextLayoutUtils.EnumerateLines(
            value,
            maxWidth >= int.MaxValue ? 0 : (int)Math.Ceiling(maxWidth),
            format.Wrapping,
            span => MeasureText(span, format.Font).Width,
            line =>
            {
                if (format.Trimming != TextTrimming.None && maxWidth < double.MaxValue && line.Width > maxWidth)
                {
                    var lineText = value.AsSpan(line.Start, line.Length);
                    line = TextLayoutUtils.TrimLineWithEllipsis(lineText, line.Start, maxWidth, span => MeasureText(span, format.Font).Width);
                }

                lineCount++;
                measuredWidth = Math.Max(measuredWidth, line.Width);
            });

        double lineHeight = GetLineHeight(format.Font);
        double contentHeight = Math.Max(lineHeight, lineCount * lineHeight);
        return new TextLayout
        {
            MeasuredSize = new Size(Math.Min(measuredWidth, maxWidth), Math.Min(contentHeight, maxHeight)),
            EffectiveBounds = constraints.Bounds,
            EffectiveMaxWidth = maxWidth,
            ContentHeight = contentHeight,
        };
    }

    public static Size MeasureText(ReadOnlySpan<char> text, IFont font)
    {
        if (font is FreeTypeFont freeTypeFont)
        {
            var px = FreeTypeText.Measure(text, freeTypeFont);
            double scale = GetFontPixelScale(freeTypeFont);
            return new Size(px.Width / scale, px.Height / scale);
        }

        double width = 0;
        foreach (char ch in text)
        {
            width += GetAdvance(ch, font);
        }

        return new Size(Math.Ceiling(width), GetLineHeight(font));
    }

    public static Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
    {
        if (font is FreeTypeFont freeTypeFont)
        {
            double scale = GetFontPixelScale(freeTypeFont);
            int maxWidthPx = maxWidth <= 0 || double.IsNaN(maxWidth) || double.IsInfinity(maxWidth)
                ? 0
                : Math.Max(1, (int)Math.Ceiling(maxWidth * scale));
            var px = FreeTypeText.Measure(text, freeTypeFont, maxWidthPx, TextWrapping.Wrap);
            return new Size(px.Width / scale, px.Height / scale);
        }

        var format = new TextFormat
        {
            Font = font,
            HorizontalAlignment = TextAlignment.Left,
            VerticalAlignment = TextAlignment.Top,
            Wrapping = TextWrapping.Wrap,
            Trimming = TextTrimming.None,
        };
        var layout = CreateLayout(text, format, new TextLayoutConstraints(new Rect(0, 0, maxWidth, double.PositiveInfinity)));
        return layout.MeasuredSize;
    }

    public static IReadOnlyList<TextRun> BuildRuns(ReadOnlySpan<char> text, Rect bounds, TextFormat format, TextLayout layout)
    {
        string value = text.ToString();
        var lines = new List<LineSegment>();
        TextLayoutUtils.EnumerateLines(
            value,
            layout.EffectiveMaxWidth >= int.MaxValue ? 0 : (int)Math.Ceiling(layout.EffectiveMaxWidth),
            format.Wrapping,
            span => MeasureText(span, format.Font).Width,
            line =>
            {
                if (format.Trimming != TextTrimming.None && layout.EffectiveMaxWidth < double.MaxValue && line.Width > layout.EffectiveMaxWidth)
                {
                    var lineText = value.AsSpan(line.Start, line.Length);
                    line = TextLayoutUtils.TrimLineWithEllipsis(lineText, line.Start, layout.EffectiveMaxWidth, span => MeasureText(span, format.Font).Width);
                }

                lines.Add(line);
            });

        double lineHeight = GetLineHeight(format.Font);
        double contentHeight = Math.Max(lineHeight, lines.Count * lineHeight);
        double y = bounds.Y;
        if (format.VerticalAlignment == TextAlignment.Center)
        {
            y += Math.Max(0, (bounds.Height - contentHeight) / 2.0);
        }
        else if (format.VerticalAlignment == TextAlignment.Right)
        {
            y += Math.Max(0, bounds.Height - contentHeight);
        }

        var runs = new List<TextRun>(lines.Count);
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            string lineText = value.Substring(line.Start, line.Length);
            if (format.Trimming != TextTrimming.None && line.Start + line.Length < value.Length)
            {
                lineText += Ellipsis;
            }

            double lineWidth = MeasureText(lineText, format.Font).Width;
            double x = bounds.X;
            if (format.HorizontalAlignment == TextAlignment.Center)
            {
                x += Math.Max(0, (bounds.Width - lineWidth) / 2.0);
            }
            else if (format.HorizontalAlignment == TextAlignment.Right)
            {
                x += Math.Max(0, bounds.Width - lineWidth);
            }

            runs.Add(new TextRun(lineText, x, y + i * lineHeight));
        }

        return runs;
    }

    internal static double GetLineHeight(IFont font)
        => Math.Max(1, font.Ascent + font.Descent + font.InternalLeading);

    internal static double GetFontPixelScale(IFont font)
        => font is FreeTypeFont freeTypeFont && freeTypeFont.Size > 0
            ? Math.Max(0.0001, freeTypeFont.PixelHeight / freeTypeFont.Size)
            : 1.0;

    internal static double GetAdvance(char ch, IFont font)
    {
        if (ch == '\t')
        {
            return font.Size * 1.6;
        }

        if (char.IsWhiteSpace(ch))
        {
            return font.Size * 0.34;
        }

        if (char.GetUnicodeCategory(ch) is System.Globalization.UnicodeCategory.OtherLetter)
        {
            return font.Size;
        }

        return char.IsUpper(ch) ? font.Size * 0.66 : font.Size * 0.56;
    }

    private static double NormalizeConstraint(double value)
        => double.IsNaN(value) || double.IsInfinity(value) || value <= 0 ? double.MaxValue : value;

    internal readonly record struct TextRun(string Text, double X, double Y);
}
