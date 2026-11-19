#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BetaHub.Tests
{
    // Plays the demo a new studio opens first: the game builds itself from code, and the widget opens.
    // An error logged while loading or playing fails the test.
    public class DemoScenePlayTests
    {
        // the workspace links the package's Samples~ in as Samples
        private const string ScenePath = "Assets/Samples/Demo/BugReportDemo.unity";
        // the sample's script compiles into the game's assembly, out of the tests' reach
        private const string GameTypeName = "DemoGame";
        // four walls, five coins and the player
        private const int GameSprites = 10;
        private const int SettleFrames = 5;

        private Scene _scene;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(ScenePath);
            Assert.IsTrue(_scene.isLoaded, "Demo scene not loaded from " + ScenePath);
            for (int i = 0; i < SettleFrames; i++) yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // the reporter moves itself to DontDestroyOnLoad, so unloading the scene leaves it behind
            foreach (var reporter in Object.FindObjectsOfType<BetaHubReporter>()) Object.Destroy(reporter.gameObject);
            if (_scene.isLoaded) yield return SceneManager.UnloadSceneAsync(_scene);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheGameBuildsItself_AndTheWidgetOpens()
        {
            Assert.AreEqual(1, Object.FindObjectsOfType<MonoBehaviour>().Count(b => b.GetType().Name == GameTypeName), "DemoGame instances");
            Assert.AreEqual(GameSprites, Object.FindObjectsOfType<SpriteRenderer>().Length, "the game's sprites");
            var widgets = Object.FindObjectsOfType<BugReportWidget>();
            Assert.AreEqual(1, widgets.Length, "BugReportWidget instances");
            Assert.AreEqual(1, Object.FindObjectsOfType<BetaHubReporter>().Length, "BetaHubReporter instances");

            widgets[0].Show();
            for (int i = 0; i < SettleFrames; i++) yield return null;
            Assert.IsTrue(widgets[0].IsVisible, "Show() opens the form");

            widgets[0].Hide();
            yield return null;
            Assert.IsFalse(widgets[0].IsVisible, "Hide() closes the form");
        }
    }
}
#endif
