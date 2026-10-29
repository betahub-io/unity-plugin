using System;
using UnityEngine;

namespace BetaHub
{
    /// <summary>
    /// A studio theme for <see cref="BugReportWidget"/>: one accent, a neutral tint, a mode and a radius,
    /// run through the same recipe as the built-in themes. The editor writes it as a stylesheet with the
    /// same name next to the asset; assign that stylesheet to the widget's Theme field.
    /// </summary>
    [CreateAssetMenu(menuName = "BetaHub/Bug Report Theme", fileName = "BugReportTheme")]
    public sealed class BugReportThemeAsset : ScriptableObject
    {
        public const float MaxRadius = 24f;
        private const string AssetExtension = ".asset";
        private const string StylesheetExtension = ".uss";

        public static Color DefaultAccent => ToColor(BugReportThemeLibrary.Default.Accent);
        public static Color DefaultNeutralTint => ToColor(BugReportThemeLibrary.Default.NeutralTint);
        public static ThemeMode DefaultMode => BugReportThemeLibrary.Default.Mode;
        public static float DefaultRadius => BugReportThemeLibrary.Default.Radius;

        [Tooltip("Buttons, focus and highlights. Made lighter or darker if needed so it reads on the background.")]
        public Color Accent = DefaultAccent;

        [Tooltip("The hue of the greys: background, fields, lines and text.")]
        public Color NeutralTint = DefaultNeutralTint;

        public ThemeMode Mode = DefaultMode;

        [Range(0f, MaxRadius), Tooltip("Corner radius of fields and rows, in pixels at 1080p.")]
        public float Radius = DefaultRadius;

        public ThemeSpec ToSpec() => SpecFor(name, Accent, NeutralTint, Mode, Radius);

        public static ThemeSpec SpecFor(string name, Color accent, Color neutralTint, ThemeMode mode, float radius)
        {
            if (radius < 0f || radius > MaxRadius) throw new ArgumentOutOfRangeException(nameof(radius), radius, "Radius must be between 0 and " + MaxRadius + ".");
            return new ThemeSpec(name, ToHex(accent), ToHex(neutralTint), mode, radius);
        }

        public static string StylesheetPathFor(string assetPath)
        {
            if (assetPath == null || !assetPath.EndsWith(AssetExtension, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Expected a .asset path, got: " + assetPath, nameof(assetPath));
            }
            return assetPath.Substring(0, assetPath.Length - AssetExtension.Length) + StylesheetExtension;
        }

        private static string ToHex(Color color) => ColorMath.ToHex(new Rgb(color.r, color.g, color.b));

        private static Color ToColor(string hex)
        {
            var rgb = ColorMath.FromHex(hex);
            return new Color(rgb.R, rgb.G, rgb.B);
        }
    }
}
