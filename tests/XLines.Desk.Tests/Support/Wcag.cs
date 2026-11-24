using System.Globalization;
using System.Text.RegularExpressions;

namespace XLines.Desk.Tests.Support;

/// <summary>
/// WCAG 2.x contrast, and the compositing it depends on, for every test that measures a colour
/// the stylesheets declare.
///
/// <para>
/// Written out rather than borrowed from MudColor on purpose: this is the file a reviewer opens to
/// check a number, and it should read as the WCAG 2.x formula and nothing else. Compositing happens
/// in gamma-encoded sRGB — the space the channels are already in, as a browser does it — and
/// linearisation comes after. Doing it the other way round is a plausible-looking mistake that
/// shifts every ratio in the suite.
/// </para>
/// </summary>
public static class Wcag
{
    /// <summary>
    /// A colour as <c>#RRGGBB</c>, <c>rgb(r, g, b)</c> or <c>rgba(r, g, b, a)</c>: its channels and alpha.
    /// Anything else fails the test that handed it over, naming the value.
    /// </summary>
    public static (double R, double G, double B, double A) Parse(string colour)
    {
        colour = colour.Trim();

        if (colour.StartsWith('#'))
        {
            var hex = colour[1..];
            Assert.True(hex.Length == 6, $"expected #RRGGBB, got {colour}");
            return (Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex[2..4], 16), Convert.ToInt32(hex[4..6], 16), 1);
        }

        var rgba = Regex.Match(colour, @"^rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([\d.]+)\s*)?\)$");
        Assert.True(rgba.Success, $"not a colour this calculator reads: {colour}");

        double Channel(int i) => double.Parse(rgba.Groups[i].Value, CultureInfo.InvariantCulture);

        return (Channel(1), Channel(2), Channel(3), rgba.Groups[4].Success ? Channel(4) : 1);
    }

    /// <summary>
    /// Contrast of a possibly translucent ink over an opaque ground: the ink is composited onto the
    /// ground first, then both are measured.
    /// </summary>
    public static double Contrast(string ink, string ground)
    {
        var (br, bg, bb, _) = Parse(ground);
        var (r, g, b, a) = Parse(ink);

        return Ratio(Luminance(r * a + br * (1 - a), g * a + bg * (1 - a), b * a + bb * (1 - a)), Luminance(br, bg, bb));
    }

    /// <summary>
    /// Contrast of a possibly translucent ink over a translucent wash laid on an opaque surface:
    /// the wash is composited onto the surface first, then the ink onto that, and the ink is
    /// measured against the composited wash.
    /// </summary>
    public static double Contrast(string ink, string wash, string surface)
    {
        var (sr, sg, sb, _) = Parse(surface);
        var (wr, wg, wb, wa) = Parse(wash);
        var (r, g, b, a) = Parse(ink);

        var br = wr * wa + sr * (1 - wa);
        var bg = wg * wa + sg * (1 - wa);
        var bb = wb * wa + sb * (1 - wa);

        return Ratio(Luminance(r * a + br * (1 - a), g * a + bg * (1 - a), b * a + bb * (1 - a)), Luminance(br, bg, bb));
    }

    /// <summary>A translucent colour laid over an opaque one, as a <c>#RRGGBB</c> the calculator reads.</summary>
    public static string Composite(string top, string under)
    {
        var (r, g, b, a) = Parse(top);
        var (br, bg, bb, _) = Parse(under);

        int Mix(double c, double u) => (int)Math.Round(c * a + u * (1 - a));

        return $"#{Mix(r, br):X2}{Mix(g, bg):X2}{Mix(b, bb):X2}";
    }

    /// <summary>The relative luminance of a colour's channels, its alpha ignored.</summary>
    public static double Luminance(string colour)
    {
        var (r, g, b, _) = Parse(colour);
        return Luminance(r, g, b);
    }

    public static double Luminance(double r, double g, double b)
        => 0.2126 * Linear(r) + 0.7152 * Linear(g) + 0.0722 * Linear(b);

    /// <summary>One 0–255 sRGB channel, linearised.</summary>
    public static double Linear(double channel)
    {
        var c = channel / 255d;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary>The OKLab distance between two colours, each laid on <paramref name="panel"/> first.</summary>
    public static double OkDistance(string one, string two, string panel)
    {
        var (pr, pg, pb, _) = Parse(panel);

        (double L, double A, double B) Lab(string colour)
        {
            var (r, g, b, a) = Parse(colour);
            var lr = Linear(r * a + pr * (1 - a));
            var lg = Linear(g * a + pg * (1 - a));
            var lb = Linear(b * a + pb * (1 - a));

            var l = Math.Cbrt(0.4122214708 * lr + 0.5363325363 * lg + 0.0514459929 * lb);
            var m = Math.Cbrt(0.2119034982 * lr + 0.6806995451 * lg + 0.1073969566 * lb);
            var s = Math.Cbrt(0.0883024619 * lr + 0.2817188376 * lg + 0.6299787005 * lb);

            return (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
                    1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
                    0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
        }

        var x = Lab(one);
        var y = Lab(two);

        return Math.Sqrt(Math.Pow(x.L - y.L, 2) + Math.Pow(x.A - y.A, 2) + Math.Pow(x.B - y.B, 2));
    }

    private static double Ratio(double one, double two) => (Math.Max(one, two) + 0.05) / (Math.Min(one, two) + 0.05);
}
