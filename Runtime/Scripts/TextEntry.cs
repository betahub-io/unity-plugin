using System;
using UnityEngine;

namespace BetaHub
{
    /// <summary>What <see cref="BugReportWidget"/> asks of a platform keyboard.</summary>
    public struct TextEntryRequest
    {
        /// <summary>The field's label, e.g. "What happened", for the keyboard's title.</summary>
        public string Label;
        public string Text;
        public bool Multiline;
        public int MaxLength;
        /// <summary>The field on screen in pixels, top-left origin, so a floating keyboard can keep clear of it.</summary>
        public Rect FieldRect;
    }

    /// <summary>
    /// The platform's on-screen keyboard for controller players: Steam Deck's floating keyboard, a console's
    /// text entry. Put a component that implements it on the widget's GameObject, or set
    /// <see cref="BugReportWidget.TextEntry"/>. Without one, controller players type on a hardware keyboard
    /// or open Steam's keyboard themselves (Steam + X).
    /// </summary>
    public interface ITextEntryProvider
    {
        /// <summary>
        /// Shows the keyboard when a controller player starts writing in a field; false when none can be shown.
        /// A modal keyboard calls <paramref name="done"/> with the final text. A floating one that types into
        /// the focused field calls it with null when it closes. Call it on the main thread.
        /// </summary>
        bool Open(TextEntryRequest request, Action<string> done);

        /// <summary>The player left the field another way: hide the keyboard if it is still up.</summary>
        void Close();
    }
}
