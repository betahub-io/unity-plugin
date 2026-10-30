#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BetaHub.Tests
{
    // Plays the example scene a new studio opens first: it holds the widget and nothing else that
    // reports bugs, and the widget opens. An error logged while loading or opening fails the test.
    public class ExampleScenePlayTests
    {
        // the workspace links the package's Samples~ in as Samples
        private const string ScenePath = "Assets/Samples/ExampleScene/SampleScene.unity";
        private const int SettleFrames = 5;

        private Scene _scene;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(ScenePath);
            Assert.IsTrue(_scene.isLoaded, "Example scene not loaded from " + ScenePath);
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
        public IEnumerator OpensTheWidget()
        {
            var widgets = Object.FindObjectsOfType<BugReportWidget>();
            Assert.AreEqual(1, widgets.Length, "BugReportWidget instances");
            Assert.IsEmpty(Object.FindObjectsOfType<BugReportUI>(), "the legacy form is gone");
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
