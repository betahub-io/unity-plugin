using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace BetaHub.Tests
{
    // The demo is the first thing a new studio runs, in whatever project they have: it opens the
    // widget and refers to nothing the package doesn't ship.
    [TestFixture]
    public class DemoSceneTests
    {
        private const string SampleFolder = "Demo";
        private const string SceneFile = "BugReportDemo.unity";
        // com.unity.ugui's script GUIDs
        private const string EventSystemGuid = "76c392e42b5098c458856cdf6ecaaaa1";
        private const string StandaloneInputModuleGuid = "4f231c4fb786f3946a6b90b886c48677";
        // Unity's built-in resources, in every project
        private static readonly string[] BuiltInGuids = { "0000000000000000e000000000000000", "0000000000000000f000000000000000" };
        // what the package ships; the Unity workspace links Samples~ in as Samples
        private static readonly string[] ShippedFolders = { "Runtime", "Editor", "Samples~", "Samples" };

        private static readonly Regex MetaGuid = new Regex(@"^guid: ([0-9a-f]{32})", RegexOptions.Multiline);
        private static readonly Regex ReferencedGuid = new Regex(@"guid: ([0-9a-f]{32})");

        [Test]
        public void OpensTheWidget()
        {
            string scene = File.ReadAllText(ScenePath());
            Assert.IsTrue(scene.Contains("m_SourcePrefab: {fileID: 100100000, guid: " + PrefabGuid("BugReportWidget") + ", type: 3}"), "The BugReportWidget prefab is not in the scene.");
        }

        [Test]
        public void HasNoEventSystem_SoItRunsWithEitherInputHandler()
        {
            // A StandaloneInputModule throws when only the new Input System is active, and any
            // EventSystem stops the widget from adding the one it needs there.
            string scene = File.ReadAllText(ScenePath());

            Assert.IsFalse(scene.Contains(EventSystemGuid), "The scene has an EventSystem.");
            Assert.IsFalse(scene.Contains(StandaloneInputModuleGuid), "The scene has a StandaloneInputModule.");
        }

        // The 1.x example scene was made in a URP 2D project and kept references to its pipeline,
        // a local test script and a material that never shipped: a fresh project showed missing scripts.
        [Test]
        public void RefersOnlyToWhatThePackageShips()
        {
            var shipped = new HashSet<string>(BuiltInGuids);
            foreach (string folder in ShippedFolders.Select(f => Path.Combine(PluginRoot(), f)).Where(Directory.Exists))
            {
                foreach (string meta in Directory.EnumerateFiles(folder, "*.meta", SearchOption.AllDirectories))
                {
                    var match = MetaGuid.Match(File.ReadAllText(meta));
                    if (match.Success) shipped.Add(match.Groups[1].Value);
                }
            }

            var missing = ReferencedGuid.Matches(File.ReadAllText(ScenePath())).Cast<Match>()
                .Select(m => m.Groups[1].Value).Where(guid => !shipped.Contains(guid)).Distinct().ToList();
            Assert.IsEmpty(missing, "The demo scene refers to assets the package doesn't ship.");
        }

        private static string PrefabGuid(string prefab)
        {
            string meta = Path.Combine(PluginRoot(), "Runtime", "Prefabs", prefab + ".prefab.meta");
            var match = MetaGuid.Match(File.ReadAllText(meta));
            Assert.IsTrue(match.Success, "No guid in " + meta);
            return match.Groups[1].Value;
        }

        private static string ScenePath()
        {
            var candidates = new[] { "Samples~", "Samples" }
                .Select(folder => Path.Combine(PluginRoot(), folder, SampleFolder, SceneFile))
                .ToArray();
            string found = candidates.FirstOrDefault(File.Exists);
            Assert.IsNotNull(found, "Demo scene not found at:\n" + string.Join("\n", candidates));
            return found;
        }

        private static string PluginRoot([CallerFilePath] string thisFile = "")
        {
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile), "..", ".."));
        }
    }
}
