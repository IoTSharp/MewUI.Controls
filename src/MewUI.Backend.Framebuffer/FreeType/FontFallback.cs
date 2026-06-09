namespace Aprillz.MewUI.Rendering.FreeType;

internal static class FontFallback
{
    internal static int Version
    {
        get
        {
            var hash = new HashCode();
            foreach (var family in Aprillz.MewUI.Rendering.FontFallback.FallbackChain)
            {
                hash.Add(family, StringComparer.OrdinalIgnoreCase);
            }

            return hash.ToHashCode();
        }
    }

    internal static string[] GetChainSnapshot()
        => Aprillz.MewUI.Rendering.FontFallback.FallbackChain.ToArray();
}
