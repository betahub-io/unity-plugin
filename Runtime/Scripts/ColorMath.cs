using System;
using System.Globalization;

namespace BetaHub
{
    /// <summary>A color as three [0,1] channels. Small and Unity-free so the theme recipe is testable.</summary>
    public readonly struct Rgb
    {
        public readonly float R, G, B;
        public Rgb(float r, float g, float b) { R = r; G = g; B = b; }
    }

    /// <summary>
    /// Hex/HSL conversion and WCAG contrast, used by the theme recipe and its tests. Kept minimal and
    /// dependency-free; matches the WCAG 2.1 relative-luminance definition.
    /// </summary>
    public static class ColorMath
    {
        public static Rgb FromHex(string hex)
        {
            hex = hex.TrimStart('#');
            int r = int.Parse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int g = int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int b = int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new Rgb(r / 255f, g / 255f, b / 255f);
        }

        public static string ToHex(Rgb c)
        {
            int r = Clamp255(c.R), g = Clamp255(c.G), b = Clamp255(c.B);
            return "#" + r.ToString("x2", CultureInfo.InvariantCulture)
                       + g.ToString("x2", CultureInfo.InvariantCulture)
                       + b.ToString("x2", CultureInfo.InvariantCulture);
        }

        private static int Clamp255(float v) => Math.Max(0, Math.Min(255, (int)Math.Round(v * 255f)));

        // Returns (h in [0,360), s in [0,100], l in [0,100]).
        public static (float h, float s, float l) ToHsl(Rgb c)
        {
            float max = Math.Max(c.R, Math.Max(c.G, c.B));
            float min = Math.Min(c.R, Math.Min(c.G, c.B));
            float l = (max + min) / 2f;
            float h = 0f, s = 0f;
            float d = max - min;
            if (d > 1e-6f)
            {
                s = l > 0.5f ? d / (2f - max - min) : d / (max + min);
                if (max == c.R) h = (c.G - c.B) / d + (c.G < c.B ? 6f : 0f);
                else if (max == c.G) h = (c.B - c.R) / d + 2f;
                else h = (c.R - c.G) / d + 4f;
                h /= 6f;
            }
            return (h * 360f, s * 100f, l * 100f);
        }

        public static Rgb FromHsl(float h, float s, float l)
        {
            h = ((h % 360f) + 360f) % 360f / 360f;
            s = Math.Max(0f, Math.Min(100f, s)) / 100f;
            l = Math.Max(0f, Math.Min(100f, l)) / 100f;
            if (s <= 1e-6f) return new Rgb(l, l, l);
            float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
            float p = 2f * l - q;
            return new Rgb(HueToChannel(p, q, h + 1f / 3f), HueToChannel(p, q, h), HueToChannel(p, q, h - 1f / 3f));
        }

        private static float HueToChannel(float p, float q, float t)
        {
            if (t < 0f) t += 1f;
            if (t > 1f) t -= 1f;
            if (t < 1f / 6f) return p + (q - p) * 6f * t;
            if (t < 1f / 2f) return q;
            if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
            return p;
        }

        /// <summary>sRGB channel mix, like CSS <c>color-mix(in srgb, a weight, b)</c>.</summary>
        public static Rgb Mix(Rgb a, Rgb b, float weightOfA)
        {
            float w = Math.Max(0f, Math.Min(1f, weightOfA));
            return new Rgb(a.R * w + b.R * (1f - w), a.G * w + b.G * (1f - w), a.B * w + b.B * (1f - w));
        }

        public static float ContrastRatio(Rgb a, Rgb b)
        {
            float la = RelativeLuminance(a);
            float lb = RelativeLuminance(b);
            float hi = Math.Max(la, lb), lo = Math.Min(la, lb);
            return (hi + 0.05f) / (lo + 0.05f);
        }

        private static float RelativeLuminance(Rgb c)
        {
            return 0.2126f * Linear(c.R) + 0.7152f * Linear(c.G) + 0.0722f * Linear(c.B);
        }

        private static float Linear(float channel)
        {
            return channel <= 0.03928f ? channel / 12.92f : (float)Math.Pow((channel + 0.055f) / 1.055f, 2.4);
        }
    }
}
