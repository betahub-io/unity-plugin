using System.Globalization;
using System.Text;

namespace BetaHub
{
    public enum ThemeMode
    {
        Dark,
        Light,
    }

    /// <summary>The colors plus radius that style every screen, as hex strings ready for USS.</summary>
    public struct ThemeTokens
    {
        public string Ground, Surface, Line, Text, Muted, Accent, OnAccent, Ok, Warn, Error;
        /// <summary>Derived: text field fill, its focused fill, a focused row, and the prompt bar.</summary>
        public string Field, FieldHi, SurfaceHi, Bar;
        public float GroundAlpha;
        public float Radius;
    }

    /// <summary>Inputs a studio edits: one accent, a neutral tint, a mode and a radius.</summary>
    public struct ThemeSpec
    {
        public string Name;
        public string Accent;
        public string NeutralTint;
        public ThemeMode Mode;
        public float Radius;

        public ThemeSpec(string name, string accent, string neutralTint, ThemeMode mode, float radius)
        {
            Name = name; Accent = accent; NeutralTint = neutralTint; Mode = mode; Radius = radius;
        }
    }

    /// <summary>The shipped built-in themes. Dark and Light are the recipe's own output, so the
    /// Generate button and the defaults agree by construction.</summary>
    public static class BugReportThemeLibrary
    {
        // The shipped blue-black neutral hue, so warm accents sit on cool greys.
        public const string CoolNeutral = "#1c2131";

        public static readonly ThemeSpec[] BuiltIns =
        {
            new ThemeSpec("Dark",    "#ff7a59", CoolNeutral, ThemeMode.Dark,  10f),
            new ThemeSpec("Light",   "#e0603f", CoolNeutral, ThemeMode.Light, 10f),
            new ThemeSpec("Neutral", "#c9ced8", CoolNeutral, ThemeMode.Dark,  10f),
            new ThemeSpec("Ocean",   "#4f8cff", "#4f8cff",   ThemeMode.Dark,  10f),
            new ThemeSpec("Forest",  "#4fb37a", "#4fb37a",   ThemeMode.Dark,  10f),
            new ThemeSpec("Violet",  "#9b7bff", "#9b7bff",   ThemeMode.Dark,  10f),
        };
    }

    /// <summary>
    /// Turns one accent plus a neutral tint into a full, contrast-checked token set. Neutrals take the
    /// tint's hue at low saturation and fixed lightness steps; the accent, on-accent and semantic
    /// colors are nudged until they clear their WCAG minimum, so any accent yields a usable theme.
    /// </summary>
    public static class ThemeRecipe
    {
        private const float ComponentMin = 3f;   // accent and semantic marks against their ground
        private const float OnAccentMin = 4.5f;  // text on the accent button
        private const float MutedMin = 4.5f;     // secondary text against surface
        private const float TextMin = 7f;        // body text against surface
        private const int MaxSteps = 40;
        private const float Step = 3f;

        private const string OkSeed = "#5bc489";
        private const string WarnSeed = "#e9b949";
        private const string ErrSeed = "#e5484d";
        private const float DarkBackdropAlpha = 0.82f;
        private const float LightBackdropAlpha = 0.86f;
        private const string Black = "#000000";
        private const string White = "#ffffff";

        // Weights of the first color in the derived mixes, as in the design of record.
        private const float FieldSink = 0.62f;
        private const float FieldHiSink = 0.88f;
        private const float FieldHiTint = 0.05f;
        private const float SurfaceHiWeight = 0.84f;
        private const float BarSink = 0.60f;

        public static ThemeTokens Derive(string accentHex, string neutralTintHex, ThemeMode mode, float radius)
        {
            float ah = ColorMath.ToHsl(ColorMath.FromHex(accentHex)).h;
            float nh = ColorMath.ToHsl(ColorMath.FromHex(neutralTintHex)).h;
            bool dark = mode == ThemeMode.Dark;

            // The backdrop lets the paused game show through; the left half is darkened further by the layout.
            var t = new ThemeTokens { Radius = radius, GroundAlpha = dark ? DarkBackdropAlpha : LightBackdropAlpha };
            if (dark)
            {
                t.Ground = Hsl(nh, 18, 13);
                t.Surface = Hsl(nh, 16, 21);
                t.Line = Hsl(nh, 14, 30);
                t.Text = Hsl(nh, 14, 91);
                t.Muted = Hsl(nh, 8, 66);
            }
            else
            {
                t.Ground = Hsl(nh, 24, 98);
                t.Surface = Hsl(nh, 18, 94);
                t.Line = Hsl(nh, 14, 84);
                t.Text = Hsl(nh, 26, 13);
                t.Muted = Hsl(nh, 8, 40);
            }

            // Guarantee the fixed-step neutrals actually clear, for any hue.
            t.Text = EnsureContrast(t.Text, t.Surface, TextMin, dark);
            t.Muted = EnsureContrast(t.Muted, t.Surface, MutedMin, dark);

            // Accent must read as a control against the ground.
            t.Accent = EnsureContrast(accentHex, t.Ground, ComponentMin, dark);
            t.OnAccent = PickOnAccent(ah, t.Accent);

            t.Ok = EnsureContrast(OkSeed, t.Surface, ComponentMin, dark);
            t.Warn = EnsureContrast(WarnSeed, t.Surface, ComponentMin, dark);
            t.Error = EnsureContrast(ErrSeed, t.Surface, ComponentMin, dark);

            // Fields sink below the ground in dark themes and are paper white in light ones, so an
            // empty field still reads as a field.
            t.Field = dark ? Mix(t.Ground, Black, FieldSink) : White;
            t.FieldHi = dark ? Mix(t.Ground, Black, FieldHiSink) : Mix(t.Accent, White, FieldHiTint);
            t.SurfaceHi = Mix(t.Surface, t.Text, SurfaceHiWeight);
            t.Bar = dark ? Mix(t.Ground, Black, BarSink) : t.Surface;
            return t;
        }

        private static string Mix(string a, string b, float weightOfA)
        {
            return ColorMath.ToHex(ColorMath.Mix(ColorMath.FromHex(a), ColorMath.FromHex(b), weightOfA));
        }

        private static string Hsl(float h, float s, float l) => ColorMath.ToHex(ColorMath.FromHsl(h, s, l));

        // Nudge the foreground lightness (brighter in dark themes, darker in light themes) until it
        // clears the target contrast against the fixed background.
        private static string EnsureContrast(string fgHex, string bgHex, float target, bool dark)
        {
            var bg = ColorMath.FromHex(bgHex);
            var fg = ColorMath.FromHex(fgHex);
            var (h, s, l) = ColorMath.ToHsl(fg);
            for (int i = 0; i < MaxSteps; i++)
            {
                if (ColorMath.ContrastRatio(ColorMath.FromHsl(h, s, l), bg) >= target)
                    return ColorMath.ToHex(ColorMath.FromHsl(h, s, l));
                l += dark ? Step : -Step;
                if (l >= 100f || l <= 0f) break;
            }
            return ColorMath.ToHex(ColorMath.FromHsl(h, s, System.Math.Max(0f, System.Math.Min(100f, l))));
        }

        // On-accent is a hue-tinted near-black or near-white for looks, but only if it clears the
        // minimum; otherwise fall back to whichever pure black or white reads best on the accent.
        private static string PickOnAccent(float hue, string accentHex)
        {
            var accent = ColorMath.FromHex(accentHex);
            foreach (var tinted in new[] { Hsl(hue, 40, 8), Hsl(hue, 20, 97) })
            {
                if (ColorMath.ContrastRatio(ColorMath.FromHex(tinted), accent) >= OnAccentMin) return tinted;
            }
            return ColorMath.ContrastRatio(ColorMath.FromHex("#000000"), accent)
                 >= ColorMath.ContrastRatio(ColorMath.FromHex("#ffffff"), accent) ? "#000000" : "#ffffff";
        }
    }

    /// <summary>
    /// Writes a theme as a USS file of <c>--bh-*</c> variables on the widget root. The layout
    /// stylesheet only reads these variables, so a theme is one small generated file.
    /// </summary>
    public static class ThemeUssWriter
    {
        public const string RootClass = "bh-root";
        private const string FilePrefix = "BugReportTheme-";
        private const string FileExtension = ".uss";

        public static string FileNameFor(string themeName) => FilePrefix + themeName + FileExtension;

        public static string Write(ThemeSpec spec)
        {
            var t = ThemeRecipe.Derive(spec.Accent, spec.NeutralTint, spec.Mode, spec.Radius);
            var ground = ColorMath.FromHex(t.Ground);
            var uss = new StringBuilder();
            uss.Append("/* BetaHub theme \"").Append(spec.Name).Append("\": accent ").Append(spec.Accent)
               .Append(", neutral tint ").Append(spec.NeutralTint).Append(", ").Append(spec.Mode).Append(".\n")
               .Append("   Generated by ThemeRecipe. Do not edit; change the theme and regenerate. */\n");
            uss.Append('.').Append(RootClass).Append(" {\n");
            Var(uss, "ground", t.Ground);
            Var(uss, "surface", t.Surface);
            Var(uss, "surface-hi", t.SurfaceHi);
            Var(uss, "line", t.Line);
            Var(uss, "text", t.Text);
            Var(uss, "muted", t.Muted);
            Var(uss, "accent", t.Accent);
            Var(uss, "on-accent", t.OnAccent);
            Var(uss, "ok", t.Ok);
            Var(uss, "warn", t.Warn);
            Var(uss, "err", t.Error);
            Var(uss, "field", t.Field);
            Var(uss, "field-hi", t.FieldHi);
            Var(uss, "bar", t.Bar);
            Var(uss, "scrim", string.Format(CultureInfo.InvariantCulture, "rgba({0}, {1}, {2}, {3:0.00})",
                Channel(ground.R), Channel(ground.G), Channel(ground.B), t.GroundAlpha));
            Var(uss, "radius", t.Radius.ToString("0.##", CultureInfo.InvariantCulture) + "px");
            uss.Append("}\n");
            return uss.ToString();
        }

        private static void Var(StringBuilder uss, string name, string value)
        {
            uss.Append("  --bh-").Append(name).Append(": ").Append(value).Append(";\n");
        }

        private static int Channel(float v) => (int)System.Math.Round(System.Math.Max(0f, System.Math.Min(1f, v)) * 255f);
    }
}
