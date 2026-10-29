using System;
using System.Linq;
using NUnit.Framework;

namespace BetaHub.Tests
{
    [TestFixture]
    public class BugReportWidgetGamepadTests
    {
        private static readonly PadButton[] Faces = { PadButton.South, PadButton.East, PadButton.West, PadButton.North };

        // --- which physical button does what ---

        [Test]
        public void ButtonFor_PutsConfirmAtTheBottom_ExceptOnSwitch()
        {
            foreach (var set in new[] { GlyphSet.Xbox, GlyphSet.PlayStation })
            {
                Assert.AreEqual(PadButton.South, PadLayout.ButtonFor(set, PromptAction.Confirm), set.ToString());
                Assert.AreEqual(PadButton.East, PadLayout.ButtonFor(set, PromptAction.Back), set.ToString());
                Assert.AreEqual(PadButton.West, PadLayout.ButtonFor(set, PromptAction.Alt), set.ToString());
                Assert.AreEqual(PadButton.North, PadLayout.ButtonFor(set, PromptAction.Send), set.ToString());
            }
            Assert.AreEqual(PadButton.East, PadLayout.ButtonFor(GlyphSet.Switch, PromptAction.Confirm));
            Assert.AreEqual(PadButton.South, PadLayout.ButtonFor(GlyphSet.Switch, PromptAction.Back));
            Assert.AreEqual(PadButton.West, PadLayout.ButtonFor(GlyphSet.Switch, PromptAction.Alt));
            Assert.AreEqual(PadButton.North, PadLayout.ButtonFor(GlyphSet.Switch, PromptAction.Send));
        }

        [Test]
        public void ButtonFor_RejectsWhatIsNoFaceButton()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PadLayout.ButtonFor(GlyphSet.Xbox, PromptAction.Navigate));
            Assert.Throws<ArgumentOutOfRangeException>(() => PadLayout.ButtonFor(GlyphSet.Keyboard, PromptAction.Confirm));
        }

        [Test]
        public void ActionFor_IsTheInverse_ForEveryPad()
        {
            foreach (var set in new[] { GlyphSet.Xbox, GlyphSet.PlayStation, GlyphSet.Switch })
            {
                foreach (var action in new[] { PromptAction.Confirm, PromptAction.Back, PromptAction.Alt, PromptAction.Send })
                {
                    Assert.AreEqual(action, PadLayout.ActionFor(set, PadLayout.ButtonFor(set, action)), set + " " + action);
                }
            }
        }

        [Test]
        public void Labels_FollowEachPlatform_BottomRightLeftTop()
        {
            Assert.AreEqual("A B X Y", string.Join(" ", Faces.Select(b => PadLayout.Label(GlyphSet.Xbox, b))));
            Assert.AreEqual("✕ ○ □ △", string.Join(" ", Faces.Select(b => PadLayout.Label(GlyphSet.PlayStation, b))));
            Assert.AreEqual("B A Y X", string.Join(" ", Faces.Select(b => PadLayout.Label(GlyphSet.Switch, b))));
        }

        // --- what the prompt bar draws ---

        [Test]
        public void Glyphs_KeyboardShowsKeyCaps()
        {
            Assert.AreEqual("Key:Ctrl Key:Enter", Describe(PromptGlyphs.For(GlyphSet.Keyboard, PromptAction.Send)));
            Assert.AreEqual("Key:↑ Key:↓", Describe(PromptGlyphs.For(GlyphSet.Keyboard, PromptAction.Navigate)));
        }

        [Test]
        public void Glyphs_PadsShowOneFaceButton_AndTheDpadToNavigate()
        {
            Assert.AreEqual("Face:Y@North", Describe(PromptGlyphs.For(GlyphSet.Xbox, PromptAction.Send)));
            Assert.AreEqual("Face:✕@South", Describe(PromptGlyphs.For(GlyphSet.PlayStation, PromptAction.Confirm)));
            Assert.AreEqual("Face:A@East", Describe(PromptGlyphs.For(GlyphSet.Switch, PromptAction.Confirm)));
            Assert.AreEqual("Face:A@South", Describe(PromptGlyphs.For(GlyphSet.Xbox, PromptAction.Next)));
            Assert.AreEqual("Dpad:▲ ▼", Describe(PromptGlyphs.For(GlyphSet.PlayStation, PromptAction.Navigate)));
        }

        // --- which glyphs a connected controller gets ---

        [Test]
        public void GlyphSetFor_ReadsTheLayoutChain()
        {
            Assert.AreEqual(GlyphSet.PlayStation, GlyphSets.ForLayouts(new[] { "DualShock4GamepadHID", "DualShockGamepad", "Gamepad" }));
            Assert.AreEqual(GlyphSet.PlayStation, GlyphSets.ForLayouts(new[] { "DualSenseGamepadHID", "DualShockGamepad", "Gamepad" }));
            Assert.AreEqual(GlyphSet.Switch, GlyphSets.ForLayouts(new[] { "SwitchProControllerHID", "Gamepad" }));
            Assert.AreEqual(GlyphSet.Switch, GlyphSets.ForLayouts(new[] { "NPad", "Gamepad" }));
            Assert.AreEqual(GlyphSet.Xbox, GlyphSets.ForLayouts(new[] { "XInputControllerWindows", "XInputController", "Gamepad" }));
        }

        [Test]
        public void GlyphSetFor_UnknownPadsGetXboxLetters()
        {
            Assert.AreEqual(GlyphSet.Xbox, GlyphSets.ForLayouts(new[] { "WebGLGamepad", "Gamepad" }));
            Assert.AreEqual(GlyphSet.Xbox, GlyphSets.ForLayouts(new string[0]));
            Assert.Throws<ArgumentNullException>(() => GlyphSets.ForLayouts(null));
        }

        // --- stick and d-pad into focus moves ---

        [Test]
        public void DirectionOf_TakesTheDominantAxisPastTheDeadzone()
        {
            Assert.AreEqual(FocusMove.Up, PadNavigation.DirectionOf(0f, 0.8f));
            Assert.AreEqual(FocusMove.Down, PadNavigation.DirectionOf(0.2f, -0.9f));
            Assert.AreEqual(FocusMove.Right, PadNavigation.DirectionOf(0.9f, 0.3f));
            Assert.AreEqual(FocusMove.Left, PadNavigation.DirectionOf(-0.6f, 0.1f));
            Assert.IsNull(PadNavigation.DirectionOf(0.3f, 0.2f));
        }

        [Test]
        public void Repeat_StepsOnPress_ThenAfterADelay_ThenSteadily()
        {
            var repeat = new NavRepeat();
            Assert.AreEqual(FocusMove.Down, repeat.Step(FocusMove.Down, 0f));
            Assert.IsNull(repeat.Step(FocusMove.Down, NavRepeat.FirstDelay - 0.01f));
            Assert.AreEqual(FocusMove.Down, repeat.Step(FocusMove.Down, NavRepeat.FirstDelay));
            Assert.IsNull(repeat.Step(FocusMove.Down, NavRepeat.FirstDelay + NavRepeat.Interval - 0.01f));
            Assert.AreEqual(FocusMove.Down, repeat.Step(FocusMove.Down, NavRepeat.FirstDelay + NavRepeat.Interval));
        }

        [Test]
        public void Repeat_ReleaseOrANewDirectionStepsAtOnce()
        {
            var repeat = new NavRepeat();
            repeat.Step(FocusMove.Down, 0f);
            Assert.AreEqual(FocusMove.Right, repeat.Step(FocusMove.Right, 0.05f));
            Assert.IsNull(repeat.Step(null, 0.1f));
            Assert.AreEqual(FocusMove.Right, repeat.Step(FocusMove.Right, 0.15f));
        }

        [Test]
        public void Repeat_IgnoresADirectionHeldWhenTheFormOpened_UntilReleased()
        {
            var repeat = new NavRepeat();
            repeat.Suppress(FocusMove.Up);
            Assert.IsNull(repeat.Step(FocusMove.Up, 0f));
            Assert.IsNull(repeat.Step(FocusMove.Up, 5f));
            Assert.IsNull(repeat.Step(null, 5.1f));
            Assert.AreEqual(FocusMove.Up, repeat.Step(FocusMove.Up, 5.2f));
        }

        private static string Describe(GlyphPart[] parts)
        {
            return string.Join(" ", parts.Select(p => p.Kind == GlyphPartKind.Face ? $"Face:{p.Text}@{p.Button}" : $"{p.Kind}:{p.Text}"));
        }
    }
}
