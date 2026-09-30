#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace BetaHub.Tests
{
    // Instantiates the shipped widget prefab and walks every preview state, so a broken UXML name,
    // a missing component or a null reference in the view shows up without a manual play session.
    public class WidgetPrefabSmokeTests
    {
        private const string PrefabPath = "Assets/Runtime/Prefabs/BugReportWidget.prefab";
        private const int SettleFrames = 3;

        [UnityTest]
        public IEnumerator Prefab_HasAllComponentsAndEveryPreviewStateApplies()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, "Prefab missing at " + PrefabPath + ". Run BetaHub > Build Widget Assets.");

            var instance = Object.Instantiate(prefab);
            try
            {
                Assert.IsNotNull(instance.GetComponent<UIDocument>(), "UIDocument");
                Assert.IsNotNull(instance.GetComponent<BetaHubReporter>(), "BetaHubReporter");
                Assert.IsNotNull(instance.GetComponent<CustomFieldValidator>(), "CustomFieldValidator");
                Assert.IsNotNull(instance.GetComponent<GameRecorder>(), "GameRecorder");
                var widget = instance.GetComponent<BugReportWidget>();
                Assert.IsNotNull(widget, "BugReportWidget");
                Assert.IsNotNull(instance.GetComponent<UIDocument>().panelSettings, "PanelSettings assigned");
                Assert.IsNotNull(instance.GetComponent<UIDocument>().visualTreeAsset, "UXML assigned");

                for (int i = 0; i < SettleFrames; i++) yield return null;
                Assert.IsFalse(widget.IsVisible, "widget starts hidden");

                foreach (WidgetPreviewState state in System.Enum.GetValues(typeof(WidgetPreviewState)))
                {
                    if (state == WidgetPreviewState.None) continue;
                    widget.ApplyPreview(state, signedIn: false);
                    yield return null;
                    Assert.IsTrue(widget.IsVisible, state + " shows the overlay");
                    widget.ApplyPreview(state, signedIn: true);
                    yield return null;
                }

                widget.ApplyPreview(WidgetPreviewState.None, false);
                yield return null;
                Assert.IsFalse(widget.IsVisible, "None hides the overlay");

                widget.Show();
                for (int i = 0; i < SettleFrames; i++) yield return null;
                Assert.IsTrue(widget.IsVisible, "Show() opens the form");
                widget.Hide();
                yield return null;
                Assert.IsFalse(widget.IsVisible, "Hide() closes the form");
            }
            finally
            {
                Object.Destroy(instance);
            }
        }
    }
}
#endif
