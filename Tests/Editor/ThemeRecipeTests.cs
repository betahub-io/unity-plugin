using NUnit.Framework;

namespace BetaHub.Tests
{
    [TestFixture]
    public class ColorMathTests
    {
        private const float Tol = 0.05f;

        [Test]
        public void BlackOnWhiteIsMaxContrast()
        {
            float r = ColorMath.ContrastRatio(ColorMath.FromHex("#000000"), ColorMath.FromHex("#ffffff"));
            Assert.AreEqual(21f, r, 0.1f);
        }

        [Test]
        public void SameColorIsOne()
        {
            Assert.AreEqual(1f, ColorMath.ContrastRatio(ColorMath.FromHex("#3498db"), ColorMath.FromHex("#3498db")), 0.001f);
        }

        [Test]
        public void ContrastIsSymmetric()
        {
            var a = ColorMath.FromHex("#1b1e27");
            var b = ColorMath.FromHex("#ff7a59");
            Assert.AreEqual(ColorMath.ContrastRatio(a, b), ColorMath.ContrastRatio(b, a), 0.0001f);
        }

        [Test]
        public void HexRoundTrips()
        {
            foreach (var hex in new[] { "#000000", "#ffffff", "#ff7a59", "#1b1e27", "#4f8cff" })
            {
                Assert.AreEqual(hex, ColorMath.ToHex(ColorMath.FromHex(hex)), hex);
            }
        }

        [Test]
        public void HslRoundTripsApproximately()
        {
            var rgb = ColorMath.FromHex("#4fb37a");
            var (h, s, l) = ColorMath.ToHsl(rgb);
            var back = ColorMath.FromHsl(h, s, l);
            Assert.AreEqual(rgb.R, back.R, Tol);
            Assert.AreEqual(rgb.G, back.G, Tol);
            Assert.AreEqual(rgb.B, back.B, Tol);
        }
    }

    [TestFixture]
    public class ThemeRecipeTests
    {
        // WCAG-style minimums each derived pair must clear.
        private const float TextMin = 7f;      // body text, AAA
        private const float MutedMin = 4.5f;   // secondary text, AA
        private const float ComponentMin = 3f; // UI component / large text

        private static float C(string a, string b) => ColorMath.ContrastRatio(ColorMath.FromHex(a), ColorMath.FromHex(b));

        private static void AssertGuarantees(string name, ThemeTokens t)
        {
            Assert.GreaterOrEqual(C(t.Text, t.Ground), TextMin, name + " text/ground");
            Assert.GreaterOrEqual(C(t.Text, t.Surface), TextMin, name + " text/surface");
            Assert.GreaterOrEqual(C(t.Muted, t.Surface), MutedMin, name + " muted/surface");
            Assert.GreaterOrEqual(C(t.OnAccent, t.Accent), MutedMin, name + " on-accent/accent");
            Assert.GreaterOrEqual(C(t.Accent, t.Ground), ComponentMin, name + " accent/ground");
            Assert.GreaterOrEqual(C(t.Ok, t.Surface), ComponentMin, name + " ok/surface");
            Assert.GreaterOrEqual(C(t.Warn, t.Surface), ComponentMin, name + " warn/surface");
            Assert.GreaterOrEqual(C(t.Error, t.Surface), ComponentMin, name + " error/surface");
        }

        [Test]
        public void EveryBuiltInPassesEveryContrastCheck()
        {
            foreach (var spec in BugReportThemeLibrary.BuiltIns)
            {
                var t = ThemeRecipe.Derive(spec.Accent, spec.NeutralTint, spec.Mode, spec.Radius);
                AssertGuarantees(spec.Name, t);
            }
        }

        [Test]
        public void SixBuiltInsShip_OneLight()
        {
            // Amber was dropped (2026-09-29): on a full-screen form a second light theme only adds a bright TV at night.
            var names = System.Array.ConvertAll(BugReportThemeLibrary.BuiltIns, s => s.Name);
            CollectionAssert.AreEquivalent(new[] { "Dark", "Light", "Neutral", "Ocean", "Forest", "Violet" }, names);
            Assert.AreEqual(1, System.Array.FindAll(BugReportThemeLibrary.BuiltIns, s => s.Mode == ThemeMode.Light).Length);
        }

        [Test]
        public void ShippedDarkAndLightComeFromTheRecipe()
        {
            var dark = System.Array.Find(BugReportThemeLibrary.BuiltIns, s => s.Name == "Dark");
            var light = System.Array.Find(BugReportThemeLibrary.BuiltIns, s => s.Name == "Light");
            Assert.AreEqual("#ff7a59", dark.Accent.ToLowerInvariant());
            Assert.AreEqual(ThemeMode.Dark, dark.Mode);
            Assert.AreEqual("#e0603f", light.Accent.ToLowerInvariant());
            Assert.AreEqual(ThemeMode.Light, light.Mode);
        }

        [Test]
        public void DarkModeGroundIsDarkerThanText()
        {
            var t = ThemeRecipe.Derive("#ff7a59", "#1c2131", ThemeMode.Dark, 10f);
            var (_, _, gl) = ColorMath.ToHsl(ColorMath.FromHex(t.Ground));
            var (_, _, tl) = ColorMath.ToHsl(ColorMath.FromHex(t.Text));
            Assert.Less(gl, tl);
        }

        [Test]
        public void LightModeGroundIsLighterThanText()
        {
            var t = ThemeRecipe.Derive("#e0603f", "#1c2131", ThemeMode.Light, 10f);
            var (_, _, gl) = ColorMath.ToHsl(ColorMath.FromHex(t.Ground));
            var (_, _, tl) = ColorMath.ToHsl(ColorMath.FromHex(t.Text));
            Assert.Greater(gl, tl);
        }

        [Test]
        public void RadiusPassesThrough()
        {
            var t = ThemeRecipe.Derive("#4f8cff", "#4f8cff", ThemeMode.Dark, 4f);
            Assert.AreEqual(4f, t.Radius, 0.001f);
        }

        [Test]
        public void AnyAccentProducesAUsableSet()
        {
            var t = ThemeRecipe.Derive("#c0392b", "#c0392b", ThemeMode.Dark, 10f);
            AssertGuarantees("arbitrary", t);
            foreach (var hex in new[] { t.Ground, t.Surface, t.Line, t.Text, t.Muted, t.Accent, t.OnAccent, t.Ok, t.Warn, t.Error })
            {
                StringAssert.IsMatch("^#[0-9a-fA-F]{6}$", hex);
            }
        }
    }
}
