#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.EventSystems;
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
        private const string LightThemePath = "Assets/Runtime/UI/Themes/BugReportTheme-Light.uss";
        private const int SettleFrames = 3;
        private const string RowFocusedClass = "bh-row--focused";
        private const string FieldFocusedClass = "bh-fld--focused";
        private const string FieldEditingClass = "bh-fld--editing";
        private const string RowsTwoClass = "bh-rows--two";
        private const string KeyCapClass = "bh-key";
        private const string XboxPadClass = "bh-pad--xbox";
        private const string GlowClass = "bh-glow";
        private const string BoxClass = "bh-box";
        private const float LayoutTolerance = 0.5f;

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

#if !ENABLE_LEGACY_INPUT_MANAGER
        // UI Toolkit reads the legacy Input Manager unless an EventSystem feeds it.
        [UnityTest]
        public IEnumerator NewInputSystemOnly_AddsAnEventSystemWhileOpen()
        {
            Assert.IsNull(Object.FindObjectOfType<EventSystem>(), "the test scene starts without one");
            _widget.ApplyPreview(new WidgetPreviewOptions { State = WidgetPreviewState.Compose, AttachmentCount = 3, FocusedAttachment = -1 });
            yield return null;
            Assert.IsNotNull(Object.FindObjectOfType<EventSystem>(), "added on open");
            Assert.IsNotNull(Object.FindObjectOfType<UnityEngine.InputSystem.UI.InputSystemUIInputModule>(), "fed by the Input System");

            _widget.ApplyPreview(WidgetPreviewState.None, false);
            yield return null;
            Assert.IsNull(Object.FindObjectOfType<EventSystem>(), "removed on close");
        }
#endif

        [UnityTest]
        public IEnumerator FullScreenLog_ScrollsWithTheRightStick()
        {
            _pad = InputSystem.AddDevice<Gamepad>();
            _widget.ApplyPreview(new WidgetPreviewOptions { State = WidgetPreviewState.Preview, AttachmentCount = 3, FocusedAttachment = -1 });
            for (int i = 0; i < SettleFrames; i++) yield return null;
            var scroll = _root.Q("bh-preview-pane").Q<ScrollView>();
            Assert.IsNotNull(scroll, "the full-screen log scrolls");
            float end = scroll.verticalScroller.value;

            InputSystem.QueueStateEvent(_pad, new GamepadState { rightStick = Vector2.up });
            for (int i = 0; i < SettleFrames * 2; i++) yield return null;
            InputSystem.QueueStateEvent(_pad, new GamepadState());
            yield return null;
            Assert.Less(scroll.verticalScroller.value, end - 1f, "stick up goes back");
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
        public IEnumerator Fields_StopAtTheirLimits_AndLockWhileSending()
        {
            Assert.AreEqual(BugReportWidgetModel.DescriptionLimit, _root.Q<TextField>("bh-description").maxLength);
            Assert.AreEqual(BugReportWidgetModel.StepsLimit, _root.Q<TextField>("bh-steps").maxLength);
            Assert.AreEqual(BugReportWidgetModel.EmailLimit, _root.Q<TextField>("bh-email").maxLength);

            _widget.ApplyPreview(new WidgetPreviewOptions { State = WidgetPreviewState.Sending, AttachmentCount = 3, FocusedAttachment = -1 });
            yield return null;
            Assert.IsFalse(_root.Q<TextField>("bh-description").focusable, "no typing into a report already on its way");
            _widget.ApplyPreview(new WidgetPreviewOptions { State = WidgetPreviewState.Compose, AttachmentCount = 3, FocusedAttachment = -1 });
            yield return null;
            Assert.IsTrue(_root.Q<TextField>("bh-description").focusable);
        }

        [UnityTest]
        public IEnumerator Preview_LeavesTheConfiguredThemeAndTermsAlone()
        {
            var assigned = _widget.Theme;
            var other = AssetDatabase.LoadAssetAtPath<StyleSheet>(LightThemePath);
            Assert.IsNotNull(other, LightThemePath);
            _widget.ApplyPreview(new WidgetPreviewOptions { State = WidgetPreviewState.Compose, AttachmentCount = 3, FocusedAttachment = -1, Terms = true, Theme = other });
            yield return null;

            Assert.IsFalse(_root.Q("bh-terms").ClassListContains("bh-hidden"), "the preview shows the terms row");
            Assert.IsFalse(_widget.RequireTermsAcceptance, "without turning the setting on");
            Assert.AreSame(assigned, _widget.Theme, "and without replacing the assigned theme");
        }

        [UnityTest]
        public IEnumerator DisablingWhileOpen_ClosesTheWindowForTheGame()
        {
            int hidden = 0;
            _widget.OnBugReportWindowHidden.AddListener(() => hidden++);
            _widget.ApplyPreview(new WidgetPreviewOptions { State = WidgetPreviewState.Compose, AttachmentCount = 3, FocusedAttachment = -1 });
            yield return null;
            yield return null;

            _instance.SetActive(false);
            yield return null;
            Assert.AreEqual(1, hidden, "recording resumes and the cursor comes back");
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

        [UnityTest]
        public IEnumerator Show_PutsKeyboardFocusInWhatHappened_AndTakesTyping()
        {
            _widget.Show();
            for (int i = 0; i < SettleFrames + 2; i++) yield return null;

            var description = _root.Q<TextField>("bh-description");
            var focused = _root.panel.focusController.focusedElement as VisualElement;
            Assert.IsTrue(focused != null && (focused == description || description.Contains(focused)),
                "keyboard focus is in What happened, not " + (focused == null ? "nowhere" : focused.GetType().Name + " #" + focused.name));

            TypeCharacter('a');
            yield return null;
            Assert.AreEqual("a", description.value, "a typed key lands in the field");

            _widget.Hide();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ClickingTheFieldBeingTyped_StillTakesTyping()
        {
            _widget.Show();
            for (int i = 0; i < SettleFrames + 2; i++) yield return null;

            // a mouse click has already put the caret in the field by the time ClickEvent arrives
            var field = _root.Q("bh-fld-description");
            using (var click = ClickEvent.GetPooled())
            {
                click.target = field;
                field.SendEvent(click);
            }
            for (int i = 0; i < SettleFrames; i++) yield return null;

            TypeCharacter('a');
            yield return null;
            Assert.AreEqual("a", _root.Q<TextField>("bh-description").value, "a typed key lands in the clicked field");
        }

        [UnityTest]
        public IEnumerator Caret_HasTheTextColor()
        {
            // Unity's default runtime theme draws a near-black caret for its light fields; ours are dark.
            var description = _root.Q<TextField>("bh-description");
            yield return null;
            Assert.AreEqual(description.Q("unity-text-input").resolvedStyle.color, description.textSelection.cursorColor, "caret color");
        }

        [UnityTest]
        public IEnumerator LongText_StaysInsideTheColumn_BelowTheHeader()
        {
            _widget.Show();
            for (int i = 0; i < SettleFrames + 2; i++) yield return null;
            _root.Q<TextField>("bh-description").value = Lines(30);
            _root.Q<TextField>("bh-steps").value = Lines(30);
            for (int i = 0; i < SettleFrames; i++) yield return null;

            // containers shrink while their content spills out, so measure the content itself
            var column = _root.Q("bh-left").worldBound;
            var subtitle = _root.Q("bh-subtitle").worldBound;
            var descriptionLabel = _root.Q("bh-description-label").worldBound;
            var steps = _root.Q<TextField>("bh-steps").worldBound;
            Assert.LessOrEqual(subtitle.yMax, descriptionLabel.yMin + LayoutTolerance, "the header keeps its room");
            Assert.LessOrEqual(steps.yMax, column.yMax + LayoutTolerance, "Steps to reproduce ends inside the column");
        }

        [UnityTest]
        public IEnumerator LongText_ScrollsInItsField_AndShowsItsBeginningAfterwards()
        {
            _widget.Show();
            for (int i = 0; i < SettleFrames + 2; i++) yield return null;
            var description = _root.Q<TextField>("bh-description");
            foreach (char c in Lines(12))
            {
                TypeCharacter(c);
                yield return null;
            }
            for (int i = 0; i < SettleFrames; i++) yield return null;

            var scroll = description.Q<ScrollView>();
            Assert.IsNotNull(scroll, "What happened scrolls inside");
            Assert.Greater(scroll.scrollOffset.y, 0f, "the caret at the end is in view");

            Press(KeyCode.Tab);
            for (int i = 0; i < SettleFrames; i++) yield return null;
            Assert.AreEqual(0f, scroll.scrollOffset.y, "after leaving, the field shows its first lines");
        }

        [UnityTest]
        public IEnumerator NearTheLimit_TheTagCountsCharacters()
        {
            _widget.Show();
            for (int i = 0; i < SettleFrames + 2; i++) yield return null;
            _root.Q<TextField>("bh-description").value = new string('x', 1700);
            yield return null;
            Assert.AreEqual("1,700 / 2,000", _root.Q<Label>("bh-description-tag").text);
        }

        [UnityTest]
        public IEnumerator ReturningToAField_AddsToItsText()
        {
            _widget.Show();
            for (int i = 0; i < SettleFrames + 2; i++) yield return null;
            TypeCharacter('a');
            TypeCharacter('b');
            yield return null;

            Press(KeyCode.Tab);
            for (int i = 0; i < SettleFrames; i++) yield return null;
            Press(KeyCode.Tab);
            for (int i = 0; i < SettleFrames; i++) yield return null;
            TypeCharacter('c');
            yield return null;
            Assert.AreEqual("abc", _root.Q<TextField>("bh-description").value, "coming back does not select the text for replacing");
        }

        private static string Lines(int count) =>
            string.Join("\n", Enumerable.Range(1, count).Select(i => "line " + i + " of a long report"));

        [UnityTest]
        public IEnumerator Hover_MarksRowsFieldsAndPrompts_WhileTheMouseIsInUse()
        {
            _widget.ApplyPreview(new WidgetPreviewOptions { State = WidgetPreviewState.Compose, AttachmentCount = 3, FocusedAttachment = -1 });
            yield return null;
            var rows = _root.Q("bh-rows");
            var stepsBox = _root.Q<TextField>("bh-steps").parent;
            var prompt = _root.Q("bh-prompts-left")[0];
            var plainRow = rows[2].resolvedStyle.backgroundColor;
            var plainBox = stepsBox.resolvedStyle.borderTopColor;
            var plainPrompt = prompt.resolvedStyle.backgroundColor;

            MovePointer(rows[1].worldBound.center);
            yield return null;
            Assert.AreNotEqual(plainRow, rows[1].resolvedStyle.backgroundColor, "a hovered attachment row");
            MovePointer(stepsBox.worldBound.center);
            yield return null;
            Assert.AreNotEqual(plainBox, stepsBox.resolvedStyle.borderTopColor, "a hovered field");
            MovePointer(prompt.worldBound.center);
            yield return null;
            Assert.AreNotEqual(plainPrompt, prompt.resolvedStyle.backgroundColor, "a hovered prompt");

            // a cursor left resting on a row must not mark it while a controller moves the focus
            _widget.ApplyPreview(new WidgetPreviewOptions { State = WidgetPreviewState.Compose, AttachmentCount = 3, FocusedAttachment = -1, Glyphs = GlyphSet.Xbox });
            yield return null;
            MovePointer(rows[1].worldBound.center);
            yield return null;
            Assert.AreEqual(plainRow, rows[1].resolvedStyle.backgroundColor, "no hover mark in controller mode");
        }

        [UnityTest]
        public IEnumerator ClickingAPromptWhileTyping_WorksTheFirstTime()
        {
            _widget.Show();
            for (int i = 0; i < SettleFrames + 2; i++) yield return null;
            VisualElement next = null;
            foreach (var item in _root.Q("bh-prompts-left").Children())
            {
                if (item.Q<Label>(className: "bh-p-label").text == _widget.Strings.NextField) next = item;
            }
            Assert.IsNotNull(next, "the Next field prompt while typing What happened");

            var at = next.worldBound.center;
            SendPointer(PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = at, button = 0, clickCount = 1 }));
            yield return null;
            SendPointer(PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = at, button = 0, clickCount = 1 }));
            for (int i = 0; i < SettleFrames; i++) yield return null;

            Assert.IsTrue(_root.Q("bh-fld-steps").ClassListContains(FieldEditingClass), "one click moved typing to Steps to reproduce");
        }

        [UnityTest]
        public IEnumerator ClickingFullScreenUnderThePane_OpensThePreview()
        {
            _widget.ApplyPreview(new WidgetPreviewOptions { State = WidgetPreviewState.Compose, AttachmentCount = 3, FocusedAttachment = 1 });
            yield return null;
            var at = _root.Q("bh-cap-right").worldBound.center;
            SendPointer(PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = at, button = 0, clickCount = 1 }));
            yield return null;
            SendPointer(PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = at, button = 0, clickCount = 1 }));
            for (int i = 0; i < SettleFrames; i++) yield return null;

            Assert.IsFalse(_root.Q("bh-preview").ClassListContains("bh-hidden"), "the full-screen preview is open");
        }

        [UnityTest]
        public IEnumerator FullScreenLog_OpensAtTheEnd_AndScrollsWithPageKeys()
        {
            _widget.ApplyPreview(new WidgetPreviewOptions { State = WidgetPreviewState.Preview, AttachmentCount = 3, FocusedAttachment = -1 });
            for (int i = 0; i < SettleFrames; i++) yield return null;
            var scroll = _root.Q("bh-preview-pane").Q<ScrollView>();
            Assert.IsNotNull(scroll, "the full-screen log scrolls");
            float end = scroll.verticalScroller.highValue;
            Assert.Greater(end, 0f, "the sample log is longer than the pane");
            Assert.AreEqual(end, scroll.verticalScroller.value, 1f, "it opens at the newest lines");

            Press(KeyCode.PageUp);
            yield return null;
            Assert.Less(scroll.verticalScroller.value, end - 1f, "PgUp goes back");
            Press(KeyCode.PageDown);
            yield return null;
            Assert.AreEqual(end, scroll.verticalScroller.value, 1f, "PgDn comes back to the end");
        }

        private void SendPointer(EventBase evt)
        {
            using (evt)
            {
                _root.panel.visualTree.SendEvent(evt);
            }
        }

        // Moves the pointer without a delta, so the widget doesn't take it as switching to the mouse.
        private void MovePointer(Vector2 panelPosition)
        {
            var move = new Event { type = EventType.MouseMove, mousePosition = panelPosition };
            using (var evt = PointerMoveEvent.GetPooled(move))
            {
                _root.panel.visualTree.SendEvent(evt);
            }
        }

        // The panel hands a key to its focused element, as the runtime event system does.
        private void TypeCharacter(char c)
        {
            using (var evt = KeyDownEvent.GetPooled(c, KeyCode.None, EventModifiers.None))
            {
                _root.panel.visualTree.SendEvent(evt);
            }
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
