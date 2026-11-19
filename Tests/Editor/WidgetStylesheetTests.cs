using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace BetaHub.Tests
{
    // UI Toolkit drops a rule with an unknown pseudo-class and only warns when the sheet loads,
    // so an unsupported selector silently changes the layout.
    [TestFixture]
    public class WidgetStylesheetTests
    {
        private static readonly HashSet<string> SupportedPseudoClasses = new HashSet<string>
        {
            "hover", "active", "inactive", "focus", "disabled", "enabled", "checked", "root", "selected",
        };

        private static readonly Regex Comment = new Regex(@"/\*.*?\*/", RegexOptions.Singleline);
        private static readonly Regex Selector = new Regex(@"([^{}]+)\{");
        private static readonly Regex PseudoClass = new Regex(@":([a-zA-Z-]+)");

        [Test]
        public void UsesOnlyPseudoClassesUiToolkitSupports()
        {
            string uiFolder = UiFolder();
            Assert.IsTrue(Directory.Exists(uiFolder), "UI folder not found: " + uiFolder);
            var sheets = Directory.GetFiles(uiFolder, "*.uss", SearchOption.AllDirectories);
            Assert.IsNotEmpty(sheets);

            var unsupported = new List<string>();
            foreach (var sheet in sheets)
            {
                string text = Comment.Replace(File.ReadAllText(sheet), "");
                foreach (Match selector in Selector.Matches(text))
                {
                    foreach (Match pseudo in PseudoClass.Matches(selector.Groups[1].Value))
                    {
                        if (!SupportedPseudoClasses.Contains(pseudo.Groups[1].Value))
                        {
                            unsupported.Add(Path.GetFileName(sheet) + ": " + selector.Groups[1].Value.Trim());
                        }
                    }
                }
            }

            Assert.IsEmpty(unsupported, string.Join("\n", unsupported.Distinct()));
        }

        private static string UiFolder([CallerFilePath] string thisFile = "")
        {
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile), "..", "..", "Runtime", "UI"));
        }
    }
}
