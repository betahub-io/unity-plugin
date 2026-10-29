#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace BetaHub.Tests
{
    // Instantiates the shipped widget prefab and walks every preview state with 3 and 9 attachments,
    // drives focus with the keyboard, and opens the real form once, so a broken UXML name, a missing
    // component or a null reference in the view shows up without a manual play session.
    public class WidgetPrefabSmokeTests
    {
        private const string PrefabPath = "Assets/Runtime/Prefabs/BugReportWidget.prefab";
        private const int SettleFrames = 3;
        private const string RowFocusedClass = "bh-row--focused";
        private const string FieldFocusedClass = "bh-fld--focused";
        private const string RowsTwoClass = "bh-rows--two";
        private const string GlowClass = "bh-glow";
        private const string BoxClass = "bh-box";

        private GameObject _instance;
        private BugReportWidget _widget;
        private VisualElement _root;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, "Prefab missing at " + PrefabPath + ". Run BetaHub > Build Widget Assets.");
            _instance = Object.Instantiate(prefab);
            _widget = _instance.GetComponent<BugReportWidget>();
            _root = _instance.GetComponent<UIDocument>().rootVisualElement;
            for (int i = 0; i < SettleFrames; i++) yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(_instance);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Prefab_HasAllComponents()
        {
            Assert.IsNotNull(_instance.GetComponent<UIDocument>(), "UIDocument");
            Assert.IsNotNull(_instance.GetComponent<BetaHubReporter>(), "BetaHubReporter");
            Assert.IsNotNull(_instance.GetComponent<CustomFieldValidator>(), "CustomFieldValidator");
            Assert.IsNotNull(_instance.GetComponent<GameRecorder>(), "GameRecorder");
            Assert.IsNotNull(_widget, "BugReportWidget");
            Assert.IsNotNull(_widget.Theme, "Theme stylesheet assigned");
            Assert.IsNotNull(_instance.GetComponent<UIDocument>().panelSettings, "PanelSettings assigned");
            Assert.IsNotNull(_instance.GetComponent<UIDocument>().visualTreeAsset, "UXML assigned");
            Assert.IsFalse(_widget.IsVisible, "widget starts hidden");
            yield return null;
        }

        [UnityTest]
        public IEnumerator EveryPreviewState_With3And9Attachments()
        {
            foreach (int count in new[] { 3, 9 })
            {
                foreach (WidgetPreviewState state in System.Enum.GetValues(typeof(WidgetPreviewState)))
                {
                    if (state == WidgetPreviewState.None) continue;
                    foreach (bool signedIn in new[] { false, true })
                    {
                        _widget.ApplyPreview(new WidgetPreviewOptions { State = state, AttachmentCount = count, SignedIn = signedIn, FocusedAttachment = -1 });
                        yield return null;
                        bool expectOverlay = state != WidgetPreviewState.Toast;
                        Assert.AreEqual(expectOverlay, _widget.IsVisible, $"{state} with {count} attachments");
                    }
                }
                Assert.AreEqual(count, _root.Q("bh-rows").childCount, "one row per attachment");
                Assert.AreEqual(count >= BugReportWidgetModel.TwoColumnThreshold, _root.Q("bh-rows").ClassListContains(RowsTwoClass), "columns for " + count);
            }

            _widget.ApplyPreview(WidgetPreviewState.None, false);
            yield return null;
            Assert.IsFalse(_widget.IsVisible, "None hides the overlay");
        }

        [UnityTest]
        public IEnumerator Keyboard_MovesFocusThroughFieldsIntoTheGrid()
        {
            _widget.ApplyPreview(new WidgetPreviewOptions { State = WidgetPreviewState.Compose, AttachmentCount = 9, FocusedAttachment = -1 });
            yield return null;
            Assert.IsTrue(_root.Q("bh-fld-description").ClassListContains(FieldFocusedClass), "starts on the description");

            var path = new System.Collections.Generic.List<string>();
            foreach (var key in new[] { KeyCode.DownArrow, KeyCode.DownArrow, KeyCode.RightArrow, KeyCode.DownArrow })
            {
                Press(key);
                yield return null;
                path.Add(FocusedName());
            }
            CollectionAssert.AreEqual(new[] { "steps", "row0", "row1", "row3" }, path, "focus path: " + string.Join(", ", path));

            Press(KeyCode.Return);       // leave it out
            yield return null;
            StringAssert.Contains("8 OF 9", _root.Q<Label>("bh-group-count").text);

            Press(KeyCode.P);            // full screen
            yield return null;
            Assert.IsFalse(_root.Q("bh-preview").ClassListContains("bh-hidden"), "P opens the full-screen preview");
            Press(KeyCode.Escape);
            yield return null;
            Assert.IsTrue(_root.Q("bh-preview").ClassListContains("bh-hidden"), "Esc returns to the form");
            Assert.IsTrue(_widget.IsVisible);
        }

        [UnityTest]
        public IEnumerator TypingGlow_SitsBehindTheFieldBox()
        {
            var glows = _root.Query<VisualElement>(className: GlowClass).ToList();
            Assert.AreEqual(3, glows.Count, "description, steps and email");
            foreach (var glow in glows)
            {
                var parent = glow.parent;
                Assert.IsFalse(parent.ClassListContains(BoxClass), "a glow inside the box is drawn over the field");
                int next = parent.IndexOf(glow) + 1;
                Assert.IsTrue(next < parent.childCount && parent[next].ClassListContains(BoxClass), "the glow comes right before its box");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Show_OpensTheRealFormWithTheCollectedList()
        {
            _widget.Show();
            for (int i = 0; i < SettleFrames + 2; i++) yield return null;

            Assert.IsTrue(_widget.IsVisible, "Show() opens the form");
            Assert.GreaterOrEqual(_root.Q("bh-rows").childCount, 1, "at least the screenshot row");
            StringAssert.Contains("Send report", _root.Q("bh-prompts-right").Q<Label>(className: "bh-p-label").text);

            _widget.Hide();
            yield return null;
            Assert.IsFalse(_widget.IsVisible, "Hide() closes the form");
        }

        private string FocusedName()
        {
            if (_root.Q("bh-fld-description").ClassListContains(FieldFocusedClass)) return "description";
            if (_root.Q("bh-fld-steps").ClassListContains(FieldFocusedClass)) return "steps";
            var rows = _root.Q("bh-rows");
            for (int i = 0; i < rows.childCount; i++)
            {
                if (rows[i].ClassListContains(RowFocusedClass)) return "row" + i;
            }
            return "none";
        }

        private void Press(KeyCode key)
        {
            var overlay = _root.Q("bh-overlay");
            using (var evt = KeyDownEvent.GetPooled('\0', key, EventModifiers.None))
            {
                evt.target = overlay;
                overlay.SendEvent(evt);
            }
        }
    }
}
#endif
