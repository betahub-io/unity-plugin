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
        private const string HiddenClass = "bh-hidden";
        private const string CheckOnClass = "bh-check--on";
        private const string TabOnClass = "bh-tab--on";
        private const string PromptDisabledClass = "bh-p--disabled";
        private const string DpadClass = "bh-dpad";
        private const float LayoutTolerance = 0.5f;
        // Opening the form reads the project's minimums; nothing listens here, so the read fails at once
        // and the tests never reach BetaHub.
        private const string UnreachableEndpoint = "http://127.0.0.1:9";

        private GameObject _instance;
        private BugReportWidget _widget;
        private VisualElement _root;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, "Prefab missing at " + PrefabPath + ". Run BetaHub > Build Widget Assets.");
            _instance = Object.Instantiate(prefab);
            _instance.GetComponent<BetaHubReporter>().SubmitEndpoint = UnreachableEndpoint;
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

            Press(KeyCode.P);            // the attachments tab
            yield return null;
            Assert.IsFalse(_root.Q("bh-preview").ClassListContains("bh-hidden"), "P opens the full-screen preview");
            Press(KeyCode.P);
            yield return null;
            Assert.IsTrue(_root.Q("bh-preview").ClassListContains("bh-hidden"), "the key that opened it closes it");
            Assert.AreEqual("row3", FocusedName(), "on the attachment it showed");
            Press(KeyCode.P);
            yield return null;
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
        public IEnumerator Gamepad_ShoulderButtons_SwitchTheTabs()
        {
            _widget.ApplyPreview(new WidgetPreviewOptions { State = WidgetPreviewState.Compose, AttachmentCount = 3, FocusedAttachment = -1 });
            yield return null;
            _pad = InputSystem.AddDevice<Gamepad>();

            yield return Tap(GamepadButton.RightShoulder);
            Assert.IsTrue(PreviewOpen, "RB opens the attachments tab");
            var shoulders = _root.Q("bh-preview-tabs").Query(className: DpadClass).ToList().Select(chip => ((Label)chip).text).ToArray();
            CollectionAssert.AreEqual(new[] { "LB", "RB" }, shoulders, "the shoulders either side of the tabs");

            yield return Tap(GamepadButton.LeftShoulder);
            Assert.IsFalse(PreviewOpen, "LB goes back to the report");
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
            Press(KeyCode.Tab, EventModifiers.Shift);
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
                if (item.Q<Label>(className: "bh-p-label").text == _widget.Strings.Next) next = item;
            }
            Assert.IsNotNull(next, "the Next prompt while typing What happened");

            var at = next.worldBound.center;
            SendPointer(PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = at, button = 0, clickCount = 1 }));
            yield return null;
            SendPointer(PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = at, button = 0, clickCount = 1 }));
            for (int i = 0; i < SettleFrames; i++) yield return null;

            Assert.IsTrue(_root.Q("bh-fld-steps").ClassListContains(FieldEditingClass), "one click moved typing to Steps to reproduce");
        }

        [UnityTest]
        public IEnumerator ClickingAttachmentsUnderThePane_OpensThePreview()
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

            Assert.AreEqual((false, true), (PromptOff(_widget.Strings.OlderLines), PromptOff(_widget.Strings.NewerLines)), "nothing newer at the end");

            Press(KeyCode.PageUp);
            yield return null;
            Assert.Less(scroll.verticalScroller.value, end - 1f, "PgUp goes back");
            Assert.IsFalse(PromptOff(_widget.Strings.NewerLines), "newer lines again");
            for (int i = 0; i < 20 && scroll.verticalScroller.value > 0f; i++)
            {
                Press(KeyCode.PageUp);
                yield return null;
            }
            Assert.AreEqual((true, false), (PromptOff(_widget.Strings.OlderLines), PromptOff(_widget.Strings.NewerLines)), "nothing older at the top");

            // the sample log is under two pages, so the middle is reached by the scrollbar
            scroll.verticalScroller.value = end / 2f;
            yield return null;
            Assert.AreEqual((false, false), (PromptOff(_widget.Strings.OlderLines), PromptOff(_widget.Strings.NewerLines)), "both ways open mid-log");

            for (int i = 0; i < 20 && scroll.verticalScroller.value < end - 1f; i++)
            {
                Press(KeyCode.PageDown);
                yield return null;
            }
            Assert.AreEqual(end, scroll.verticalScroller.value, 1f, "PgDn comes back to the end");
        }

        // --- tester report on the 2.0 form: tabs, checkboxes, the type switch, Tab order, the sent screen ---

        [UnityTest]
        public IEnumerator Tab_GoesThroughEveryItem_AndRoundAgain()
        {
            _widget.ApplyPreview(new WidgetPreviewOptions { State = WidgetPreviewState.Compose, AttachmentCount = 3, FocusedAttachment = -1, Terms = true });
            yield return null;
            Assert.AreEqual("description", FocusedName());

            var path = new System.Collections.Generic.List<string>();
            for (int i = 0; i < 8; i++)
            {
                Press(KeyCode.Tab);
                for (int f = 0; f < SettleFrames; f++) yield return null;
                path.Add(FocusedName() + (Editing() != null ? "*" : ""));
            }
            // * typing: Tab into a text field types there
            CollectionAssert.AreEqual(new[] { "steps*", "terms", "row0", "row1", "row2", "type", "description*", "steps*" }, path,
                "Tab path: " + string.Join(", ", path));

            Press(KeyCode.Tab, EventModifiers.Shift);
            for (int f = 0; f < SettleFrames; f++) yield return null;
            Assert.AreEqual("description*", FocusedName() + (Editing() != null ? "*" : ""), "Shift+Tab goes back");
        }

        [UnityTest]
        public IEnumerator ArrowsLeaveATextField_FromItsFirstOrLastCharacter()
        {
            _widget.Show();
            for (int i = 0; i < SettleFrames + 2; i++) yield return null;
            TypeCharacter('a');
            TypeCharacter('b');
            yield return null;

            Press(KeyCode.DownArrow, target: Focused());
            for (int i = 0; i < SettleFrames; i++) yield return null;
            Assert.AreEqual("steps", Editing(), "down from the last character goes on to the next field");

            Press(KeyCode.UpArrow, target: Focused());
            for (int i = 0; i < SettleFrames; i++) yield return null;
            Assert.AreEqual("description", Editing(), "up from the first character goes back");
            Assert.AreEqual("ab", _root.Q<TextField>("bh-description").value);
        }

        [UnityTest]
        public IEnumerator Keys1And2_SwitchTheTabs_AlsoOnAzerty_ButTypeWhileWriting()
        {
            _widget.ApplyPreview(new WidgetPreviewOptions { State = WidgetPreviewState.Compose, AttachmentCount = 3, FocusedAttachment = -1 });
            yield return null;
            Assert.IsTrue(Tab(0).ClassListContains(TabOnClass), "the report tab is open");

            Press(KeyCode.Alpha2);
            yield return null;
            Assert.IsTrue(PreviewOpen, "2 opens the attachments tab");
            Assert.IsTrue(Tab(1, "bh-preview-tabs").ClassListContains(TabOnClass));
            Press(KeyCode.Alpha1);
            yield return null;
            Assert.IsFalse(PreviewOpen, "1 goes back to the report");

            PressCharacter('é');
            yield return null;
            Assert.IsTrue(PreviewOpen, "é is AZERTY's 2");
            PressCharacter('&');
            yield return null;
            Assert.IsFalse(PreviewOpen, "& is AZERTY's 1");

            _widget.ApplyPreview(WidgetPreviewState.None, false);
            yield return null;
            _widget.Show();
            for (int i = 0; i < SettleFrames + 2; i++) yield return null;
            TypeCharacter('2');
            yield return null;
            Assert.IsFalse(PreviewOpen, "while typing, 2 is a character");
            Assert.AreEqual("2", _root.Q<TextField>("bh-description").value);
        }

        [UnityTest]
        public IEnumerator ClickingTheTabs_SwitchesThem()
        {
            _widget.ApplyPreview(new WidgetPreviewOptions { State = WidgetPreviewState.Compose, AttachmentCount = 3, FocusedAttachment = -1 });
            yield return null;
            Assert.AreEqual("3", Tab(1).Q(className: "bh-tab-badge").Q<Label>().text, "the badge counts what is included");

            yield return Click(Tab(1));
            Assert.IsTrue(PreviewOpen, "the attachments tab opens the preview");
            yield return Click(Tab(0, "bh-preview-tabs"));
            Assert.IsFalse(PreviewOpen, "the report tab goes back to the form");
        }

        [UnityTest]
        public IEnumerator ClickingARow_SelectsIt_ItsCheckbox_IncludesOrLeavesOut()
        {
            _widget.ApplyPreview(new WidgetPreviewOptions { State = WidgetPreviewState.Compose, AttachmentCount = 3, FocusedAttachment = -1, Terms = true });
            yield return null;
            var row = _root.Q("bh-rows")[1];

            yield return Click(row.Q<Label>(className: "bh-oname"));
            Assert.AreEqual("row1", FocusedName(), "a click on the row selects it");
            StringAssert.Contains("3 OF 3", _root.Q<Label>("bh-group-count").text, "and changes nothing");

            yield return Click(row.Q(className: "bh-check"));
            StringAssert.Contains("2 OF 3", _root.Q<Label>("bh-group-count").text, "the checkbox leaves it out");
            Assert.IsFalse(row.Q(className: "bh-check").ClassListContains(CheckOnClass));

            yield return Click(_root.Q<Label>("bh-terms-label"));
            Assert.AreEqual("terms", FocusedName());
            Assert.IsFalse(_root.Q("bh-terms-check").ClassListContains(CheckOnClass), "a click on the terms row only selects it");
            yield return Click(_root.Q("bh-terms-check"));
            Assert.IsTrue(_root.Q("bh-terms-check").ClassListContains(CheckOnClass), "its checkbox accepts");

            Press(KeyCode.Alpha2);
            yield return null;
            var previewRow = _root.Q("bh-preview-list")[0];
            yield return Click(previewRow.Q(className: "bh-check"));
            StringAssert.Contains("SCREENSHOT", _root.Q<Label>("bh-preview-subtitle").text.ToUpperInvariant(), "its checkbox also shows the row");
            Assert.IsFalse(previewRow.Q(className: "bh-check").ClassListContains(CheckOnClass), "and leaves it out");
        }

        [UnityTest]
        public IEnumerator TypeSwitch_ClickOrLeftAndRight_PickTheType()
        {
            _widget.ApplyPreview(new WidgetPreviewOptions { State = WidgetPreviewState.Compose, AttachmentCount = 3, FocusedAttachment = -1 });
            yield return null;

            yield return Click(_root.Q("bh-type-suggestion"));
            Assert.AreEqual("type", FocusedName());
            Assert.AreEqual("SUGGEST AN IDEA", Tab(0).Q<Label>(className: "bh-tab-text").text);
            Assert.IsTrue(_root.Q("bh-fld-steps").ClassListContains(HiddenClass), "a suggestion has no steps");
            Assert.AreEqual(1, _root.Q("bh-rows").childCount, "and takes only the screenshot");
            Assert.IsNotNull(PromptLabeled(_widget.Strings.BugOrSuggestion), "the switch's prompt");

            Press(KeyCode.LeftArrow);
            yield return null;
            Assert.AreEqual("REPORT A BUG", Tab(0).Q<Label>(className: "bh-tab-text").text, "left picks Bug");
            Assert.AreEqual(3, _root.Q("bh-rows").childCount);
            Press(KeyCode.RightArrow);
            yield return null;
            Assert.AreEqual("SUGGEST AN IDEA", Tab(0).Q<Label>(className: "bh-tab-text").text, "right picks Suggestion");

            yield return Click(_root.Q<Label>("bh-type-label"));
            Assert.AreEqual("SUGGEST AN IDEA", Tab(0).Q<Label>(className: "bh-tab-text").text, "a click beside the switch only selects the row");
        }

        [UnityTest]
        public IEnumerator Suggestion_WaitsForTheMinimumLength()
        {
            _widget.ApplyPreview(new WidgetPreviewOptions { State = WidgetPreviewState.Compose, AttachmentCount = 3, FocusedAttachment = -1, Type = ReportType.Suggestion });
            yield return null;
            _root.Q<TextField>("bh-description").value = "A map";
            yield return null;
            Assert.AreEqual("5 / 80 min", _root.Q<Label>("bh-description-tag").text);
            Assert.IsTrue(_root.Q("bh-prompts-right")[0].ClassListContains(PromptDisabledClass), "Send waits for the minimum");

            _root.Q<TextField>("bh-description").value = new string('x', 80);
            yield return null;
            Assert.IsFalse(_root.Q("bh-prompts-right")[0].ClassListContains(PromptDisabledClass));
        }

        // BetaHub refuses a bug report under 50 characters unless the project says otherwise; the
        // inspector's minimum stands in while the project's own can't be read.
        [UnityTest]
        public IEnumerator Bug_WaitsForItsMinimumLength()
        {
            _widget.ApplyPreview(new WidgetPreviewOptions { State = WidgetPreviewState.Compose, AttachmentCount = 3, FocusedAttachment = -1 });
            yield return null;
            _root.Q<TextField>("bh-description").value = "A map";
            yield return null;
            Assert.AreEqual("5 / 50 min", _root.Q<Label>("bh-description-tag").text);
            Assert.IsTrue(_root.Q("bh-prompts-right")[0].ClassListContains(PromptDisabledClass), "Send waits for the minimum");

            _root.Q<TextField>("bh-description").value = new string('x', 50);
            yield return null;
            Assert.AreEqual(_widget.Strings.Required, _root.Q<Label>("bh-description-tag").text);
            Assert.IsFalse(_root.Q("bh-prompts-right")[0].ClassListContains(PromptDisabledClass));
        }

        // A suggestion is no report of something that happened.
        [UnityTest]
        public IEnumerator Suggestion_AsksForTheIdea()
        {
            _widget.ApplyPreview(new WidgetPreviewOptions { State = WidgetPreviewState.Compose, AttachmentCount = 3, FocusedAttachment = -1, Type = ReportType.Suggestion });
            yield return null;
            Assert.AreEqual("YOUR IDEA", _root.Q<Label>("bh-description-label").text);
            Assert.AreEqual("Describe your idea", _root.Q<Label>("bh-description-prompt").text);
            Assert.IsNotNull(PromptLabeled(_widget.Strings.SendSuggestion), "Send suggestion");
            Assert.IsNull(PromptLabeled(_widget.Strings.SendReport));
        }

        [UnityTest]
        public IEnumerator SentScreen_ArrowsAndTab_MoveBetweenTheEmailAndTheLink()
        {
            _widget.ApplyPreview(new WidgetPreviewOptions { State = WidgetPreviewState.Sent, AttachmentCount = 3, FocusedAttachment = -1 });
            yield return null;
            var link = _root.Q("bh-link");
            Assert.IsTrue(_root.Q("bh-fld-email").ClassListContains(FieldFocusedClass), "the email first");

            Press(KeyCode.DownArrow);
            yield return null;
            Assert.IsTrue(link.ClassListContains(RowFocusedClass), "down to the link");
            Assert.IsFalse(_root.Q("bh-fld-email").ClassListContains(FieldFocusedClass));
            Assert.IsNotNull(PromptLabeled(_widget.Strings.CopyLink), "Enter copies it");

            Press(KeyCode.UpArrow);
            yield return null;
            Assert.IsTrue(_root.Q("bh-fld-email").ClassListContains(FieldFocusedClass), "up to the email");
            Press(KeyCode.Tab);
            yield return null;
            Assert.IsTrue(link.ClassListContains(RowFocusedClass), "Tab moves too");
        }

        // A suggestion publishes on its own and takes no email.
        [UnityTest]
        public IEnumerator SentSuggestion_HasNoEmailStep()
        {
            _widget.ApplyPreview(new WidgetPreviewOptions { State = WidgetPreviewState.Sent, AttachmentCount = 3, FocusedAttachment = -1, Type = ReportType.Suggestion });
            yield return null;
            Assert.AreEqual("SUGGESTION SENT", _root.Q<Label>("bh-title").text);
            Assert.IsTrue(_root.Q("bh-fld-email").ClassListContains(HiddenClass), "no email field");
            Assert.IsTrue(_root.Q("bh-sent-note").ClassListContains(HiddenClass), "nor its note");
            Assert.AreEqual(0, _root.Q("bh-prompts-right").childCount, "no Email me a link");
            Assert.AreEqual(_widget.Strings.BackToGame, _root.Q("bh-prompts-left")[0].Q<Label>(className: "bh-p-label").text, "going back leads");
            Assert.AreEqual("Suggestion link", _root.Q<Label>("bh-link-label").text);
            Assert.AreEqual("SENT WITH THIS SUGGESTION", _root.Q<Label>("bh-sent-with-label").text);
        }

        // A report still uploading shows a toast; opening a new form hides it and its ring.
        [UnityTest]
        public IEnumerator OpeningTheForm_HidesTheUploadToast()
        {
            _widget.ApplyPreview(new WidgetPreviewOptions { State = WidgetPreviewState.Toast, AttachmentCount = 3, FocusedAttachment = -1 });
            yield return null;
            Assert.IsFalse(_root.Q("bh-toast").ClassListContains(HiddenClass), "the toast is up");

            _widget.Show();
            Assert.IsTrue(_root.Q("bh-toast").ClassListContains(HiddenClass), "gone in the frame that is captured, or it is in the shot");
            for (int i = 0; i < SettleFrames + 2; i++) yield return null;
            Assert.IsTrue(_widget.IsVisible);
            var toast = _root.Q("bh-toast");
            Assert.IsTrue(toast.ClassListContains(HiddenClass), "nothing of it over the form");
            Assert.AreEqual(DisplayStyle.None, toast.resolvedStyle.display);
            Assert.IsTrue(toast.Contains(_root.Q("bh-ring")), "the ring is drawn inside the toast, so it goes with it");
        }

        private bool PreviewOpen => !_root.Q("bh-preview").ClassListContains(HiddenClass);

        private VisualElement Tab(int index, string host = "bh-tabs") => _root.Q(host).Query(className: "bh-tab").ToList()[index];

        private VisualElement PromptLabeled(string label)
        {
            foreach (var side in new[] { "bh-prompts-left", "bh-prompts-right" })
            {
                foreach (var item in _root.Q(side).Children())
                {
                    if (item.Q<Label>(className: "bh-p-label")?.text == label) return item;
                }
            }
            return null;
        }

        private bool PromptOff(string label)
        {
            var prompt = PromptLabeled(label);
            Assert.IsNotNull(prompt, "prompt " + label);
            return prompt.ClassListContains(PromptDisabledClass);
        }

        private IEnumerator Click(VisualElement element)
        {
            var at = element.worldBound.center;
            SendPointer(PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = at, button = 0, clickCount = 1 }));
            yield return null;
            SendPointer(PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = at, button = 0, clickCount = 1 }));
            for (int i = 0; i < SettleFrames; i++) yield return null;
        }

        private VisualElement Focused() => _root.panel.focusController.focusedElement as VisualElement;

        private string Editing()
        {
            if (_root.Q("bh-fld-description").ClassListContains(FieldEditingClass)) return "description";
            if (_root.Q("bh-fld-steps").ClassListContains(FieldEditingClass)) return "steps";
            return null;
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
            if (_root.Q("bh-type").ClassListContains(RowFocusedClass)) return "type";
            if (_root.Q("bh-terms").ClassListContains(RowFocusedClass)) return "terms";
            if (_root.Q("bh-fld-description").ClassListContains(FieldFocusedClass)) return "description";
            if (_root.Q("bh-fld-steps").ClassListContains(FieldFocusedClass)) return "steps";
            var rows = _root.Q("bh-rows");
            for (int i = 0; i < rows.childCount; i++)
            {
                if (rows[i].ClassListContains(RowFocusedClass)) return "row" + i;
            }
            return "none";
        }

        private void Press(KeyCode key, EventModifiers modifiers = EventModifiers.None, VisualElement target = null)
        {
            target = target ?? _root.Q("bh-overlay");
            using (var evt = KeyDownEvent.GetPooled('\0', key, modifiers))
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        // A key that types a character and has no KeyCode of its own, such as AZERTY's é.
        private void PressCharacter(char c)
        {
            var overlay = _root.Q("bh-overlay");
            using (var evt = KeyDownEvent.GetPooled(c, KeyCode.None, EventModifiers.None))
            {
                evt.target = overlay;
                overlay.SendEvent(evt);
            }
        }
    }
}
#endif
