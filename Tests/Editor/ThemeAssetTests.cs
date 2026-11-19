using System;
using NUnit.Framework;
using UnityEngine;

namespace BetaHub.Tests
{
    [TestFixture]
    public class ThemeAssetTests
    {
        [Test]
        public void Defaults_WriteTheBuiltInDarkTheme()
        {
            var dark = BugReportThemeLibrary.Default;
            var fromAsset = BugReportThemeAsset.SpecFor(dark.Name, BugReportThemeAsset.DefaultAccent,
                BugReportThemeAsset.DefaultNeutralTint, BugReportThemeAsset.DefaultMode, BugReportThemeAsset.DefaultRadius);

            Assert.AreEqual(ThemeUssWriter.Write(dark), ThemeUssWriter.Write(fromAsset));
        }

        [Test]
        public void SpecFor_TurnsColorsIntoHex_AndKeepsTheRest()
        {
            var spec = BugReportThemeAsset.SpecFor("Studio", new Color(0.31f, 0.55f, 1f), new Color(0.11f, 0.13f, 0.19f), ThemeMode.Light, 6f);

            Assert.AreEqual("Studio", spec.Name);
            Assert.AreEqual("#4f8cff", spec.Accent);
            Assert.AreEqual("#1c2130", spec.NeutralTint);
            Assert.AreEqual(ThemeMode.Light, spec.Mode);
            Assert.AreEqual(6f, spec.Radius);
        }

        [Test]
        public void SpecFor_RejectsARadiusOutOfRange()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => BugReportThemeAsset.SpecFor("x", Color.white, Color.black, ThemeMode.Dark, -1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => BugReportThemeAsset.SpecFor("x", Color.white, Color.black, ThemeMode.Dark, BugReportThemeAsset.MaxRadius + 1f));
        }

        [Test]
        public void StylesheetSitsNextToTheAsset()
        {
            Assert.AreEqual("Assets/Themes/Studio Theme.uss", BugReportThemeAsset.StylesheetPathFor("Assets/Themes/Studio Theme.asset"));
            Assert.Throws<ArgumentException>(() => BugReportThemeAsset.StylesheetPathFor("Assets/Themes/Studio.uss"));
        }
    }
}
