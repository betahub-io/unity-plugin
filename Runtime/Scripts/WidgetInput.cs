using System;
using System.Collections.Generic;

namespace BetaHub
{
    /// <summary>Which glyphs the prompt bar draws: the last device the player used.</summary>
    public enum GlyphSet
    {
        Keyboard,
        Xbox,
        PlayStation,
        Switch,
    }

    /// <summary>A face button by position, the way the Input System names them.</summary>
    public enum PadButton
    {
        South,
        East,
        West,
        North,
    }

    public enum GlyphPartKind
    {
        Key,
        Face,
        Dpad,
    }

    /// <summary>One piece of a prompt glyph: a key cap, a face button or the d-pad.</summary>
    public struct GlyphPart
    {
        public GlyphPartKind Kind;
        public string Text;
        /// <summary>Face buttons only.</summary>
        public PadButton Button;
    }

    /// <summary>
    /// Prompt verbs on a controller. Positions stay the same across pads: back on the right, full screen
    /// on the left, send on top. Nintendo swaps the bottom and right buttons, so A confirms on a Switch.
    /// </summary>
    public static class PadLayout
    {
        private static readonly string[] XboxLabels = { "A", "B", "X", "Y" };
        private static readonly string[] PlayStationLabels = { "✕", "○", "□", "△" };
        private static readonly string[] SwitchLabels = { "B", "A", "Y", "X" };

        public static PadButton ButtonFor(GlyphSet set, PromptAction action)
        {
            if (set == GlyphSet.Keyboard) throw new ArgumentOutOfRangeException(nameof(set), set, "The keyboard has no face buttons.");
            bool nintendo = set == GlyphSet.Switch;
            switch (action)
            {
                case PromptAction.Confirm:
                case PromptAction.Next:
                    return nintendo ? PadButton.East : PadButton.South;
                case PromptAction.Back:
                    return nintendo ? PadButton.South : PadButton.East;
                case PromptAction.Alt:
                    return PadButton.West;
                case PromptAction.Send:
                    return PadButton.North;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, "Not a face button action.");
            }
        }

        public static PromptAction ActionFor(GlyphSet set, PadButton button)
        {
            bool nintendo = set == GlyphSet.Switch;
            switch (button)
            {
                case PadButton.South: return nintendo ? PromptAction.Back : PromptAction.Confirm;
                case PadButton.East: return nintendo ? PromptAction.Confirm : PromptAction.Back;
                case PadButton.West: return PromptAction.Alt;
                case PadButton.North: return PromptAction.Send;
                default: throw new ArgumentOutOfRangeException(nameof(button), button, null);
            }
        }

        public static string Label(GlyphSet set, PadButton button)
        {
            switch (set)
            {
                case GlyphSet.Xbox: return XboxLabels[(int)button];
                case GlyphSet.PlayStation: return PlayStationLabels[(int)button];
                case GlyphSet.Switch: return SwitchLabels[(int)button];
                default: throw new ArgumentOutOfRangeException(nameof(set), set, "The keyboard has no face buttons.");
            }
        }
    }

    public static class GlyphSets
    {
        private static readonly string[] PlayStationNames = { "DualShock", "DualSense", "PlayStation" };
        private static readonly string[] SwitchNames = { "Switch", "NPad" };

        /// <summary>
        /// Glyphs for a gamepad from its Input System layout and the layouts it is based on.
        /// Anything not PlayStation or Switch, Steam Deck included, gets Xbox letters.
        /// </summary>
        public static GlyphSet ForLayouts(IEnumerable<string> layoutChain)
        {
            if (layoutChain == null) throw new ArgumentNullException(nameof(layoutChain));
            foreach (var layout in layoutChain)
            {
                if (ContainsAny(layout, PlayStationNames)) return GlyphSet.PlayStation;
                if (ContainsAny(layout, SwitchNames)) return GlyphSet.Switch;
            }
            return GlyphSet.Xbox;
        }

        private static bool ContainsAny(string text, string[] names)
        {
            foreach (var name in names)
            {
                if (text.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }
    }

    public static class PadNavigation
    {
        public const float Deadzone = 0.5f;

        /// <summary>The stick or d-pad as one focus move: the stronger axis, once it passes the deadzone.</summary>
        public static FocusMove? DirectionOf(float x, float y)
        {
            if (Math.Max(Math.Abs(x), Math.Abs(y)) < Deadzone) return null;
            if (Math.Abs(x) > Math.Abs(y)) return x > 0 ? FocusMove.Right : FocusMove.Left;
            return y > 0 ? FocusMove.Up : FocusMove.Down;
        }
    }

    /// <summary>Turns a held direction into focus moves: one on press, then repeats after a delay.</summary>
    public sealed class NavRepeat
    {
        public const float FirstDelay = 0.4f;
        public const float Interval = 0.12f;

        private FocusMove? _held;
        private float _nextAt;
        private bool _suppressed;

        /// <summary>The move to make now for the direction held, or null.</summary>
        public FocusMove? Step(FocusMove? held, float now)
        {
            if (held == null)
            {
                _held = null;
                _suppressed = false;
                return null;
            }
            if (_suppressed) return null;
            if (held != _held)
            {
                _held = held;
                _nextAt = now + FirstDelay;
                return held;
            }
            if (now < _nextAt) return null;
            _nextAt = now + Interval;
            return held;
        }

        /// <summary>A direction already held when the form opened does nothing until released.</summary>
        public void Suppress(FocusMove? held)
        {
            _held = held;
            _suppressed = held != null;
        }
    }
}
