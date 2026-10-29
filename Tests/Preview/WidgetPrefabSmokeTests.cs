#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

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
        private const string KeyCapClass = "bh-key";
        private const string XboxPadClass = "bh-pad--xbox";
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
#if ENABLE_INPUT_SYSTEM
            if (_pad != null) InputSystem.RemoveDevice(_pad);
            _pad = null;
#endif
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

#if ENABLE_INPUT_SYSTEM
        [UnityTest]
        public IEnumerator Gamepad_DrivesTheForm_AndSwapsTheGlyphs()
        {
            _widget.ApplyPreview(new WidgetPreviewOptions { State = WidgetPreviewState.Compose, AttachmentCount = 9, FocusedAttachment = -1 });
            yield return null;
            Assert.IsNotNull(PromptsLeft.Q(className: KeyCapClass), "keyboard glyphs until a pad is used");

            _pad = InputSystem.AddDevice<Gamepad>();
            yield return Tap(GamepadButton.DpadDown);
            Assert.AreEqual("steps", FocusedName());
            Assert.IsNotNull(PromptsLeft.Q(className: XboxPadClass), "a generic pad gets Xbox letters");
            Assert.IsNull(PromptsLeft.Q(className: KeyCapClass));

            yield return Tap(GamepadButton.DpadRight);
            Assert.AreEqual("row0", FocusedName());
            yield return Tap(GamepadButton.South);
            StringAssert.Contains("8 OF 9", _root.Q<Label>("bh-group-count").text, "A leaves it out");
            yield return Tap(GamepadButton.West);
            Assert.IsFalse(_root.Q("bh-preview").ClassListContains("bh-hidden"), "X opens the full-screen preview");
            yield return Tap(GamepadButton.East);
            Assert.IsTrue(_root.Q("bh-preview").ClassListContains("bh-hidden"), "B returns to the form");
            Assert.IsTrue(_widget.IsVisible);

            Press(KeyCode.UpArrow);
            yield return null;
            Assert.IsNotNull(PromptsLeft.Q(className: KeyCapClass), "a key press brings the keyboard glyphs back");
        }

        [UnityTest]
        public IEnumerator Gamepad_WritesThroughThePlatformKeyboard()
        {
            var keyboard = new FakeTextEntry();
            _widget.TextEntry = keyboard;
            _widget.ApplyPreview(new WidgetPreviewOptions { State = WidgetPreviewState.Compose, AttachmentCount = 3, FocusedAttachment = -1 });
            yield return null;

            _pad = InputSystem.AddDevice<Gamepad>();
            yield return Tap(GamepadButton.South);
            yield return null;
            Assert.AreEqual(1, keyboard.Opened, "A on a field opens the platform keyboard");
            Assert.AreEqual("What happened", keyboard.Request.Label);
            Assert.IsTrue(keyboard.Request.Multiline);
            Assert.AreEqual(BugReportWidgetModel.DescriptionLimit, keyboard.Request.MaxLength);
            Assert.Greater(keyboard.Request.FieldRect.width, 0f, "the field's place on screen");

            keyboard.Done("Typed on the platform keyboard");
            yield return null;
            Assert.AreEqual("Typed on the platform keyboard", _root.Q<TextField>("bh-description").value);
            Assert.IsFalse(_root.Q("bh-fld-description").ClassListContains("bh-fld--editing"), "back to browsing");
            Assert.AreEqual(0, keyboard.Closed, "it closed itself");
        }

        private InputDevice _pad;
        private VisualElement PromptsLeft => _root.Q("bh-prompts-left");

        private IEnumerator Tap(GamepadButton button)
        {
            InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(button));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(_pad, new GamepadState());
            yield return null;
        }

        private sealed class FakeTextEntry : ITextEntryProvider
        {
            public int Opened, Closed;
            public TextEntryRequest Request;
            private System.Action<string> _done;

            public bool Open(TextEntryRequest request, System.Action<string> done)
            {
                Opened++;
                Request = request;
                _done = done;
                return true;
            }

            public void Close() => Closed++;
            public void Done(string text) => _done(text);
        }
#endif

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
