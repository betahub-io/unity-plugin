using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace BetaHub.Tests
{
    // The example scene is the first thing a new studio runs, so it shows the default form.
    [TestFixture]
    public class ExampleSceneTests
    {
        // com.unity.ugui's script GUIDs
        private const string EventSystemGuid = "76c392e42b5098c458856cdf6ecaaaa1";
        private const string StandaloneInputModuleGuid = "4f231c4fb786f3946a6b90b886c48677";

        private static readonly Regex MetaGuid = new Regex(@"^guid: ([0-9a-f]{32})", RegexOptions.Multiline);

        [Test]
        public void OpensTheWidget_NotTheLegacyForm()
        {
            string scene = File.ReadAllText(ScenePath());

            Assert.IsTrue(scene.Contains("m_SourcePrefab: {fileID: 100100000, guid: " + PrefabGuid("BugReportWidget") + ", type: 3}"), "The BugReportWidget prefab is not in the scene.");
            Assert.IsFalse(scene.Contains(PrefabGuid("BugReportingFormCanvas")), "The legacy BugReportingFormCanvas is still in the scene.");
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

        private static string PrefabGuid(string prefab)
        {
            string meta = Path.Combine(PluginRoot(), "Runtime", "Prefabs", prefab + ".prefab.meta");
            var match = MetaGuid.Match(File.ReadAllText(meta));
            Assert.IsTrue(match.Success, "No guid in " + meta);
            return match.Groups[1].Value;
        }

        // Samples~ in the package; the Unity workspace links it in as Samples.
        private static string ScenePath()
        {
            var candidates = new[] { "Samples~", "Samples" }
                .Select(folder => Path.Combine(PluginRoot(), folder, "ExampleScene", "SampleScene.unity"))
                .ToArray();
            string found = candidates.FirstOrDefault(File.Exists);
            Assert.IsNotNull(found, "Example scene not found at:\n" + string.Join("\n", candidates));
            return found;
        }

        private static string PluginRoot([CallerFilePath] string thisFile = "")
        {
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile), "..", ".."));
        }
    }
}
