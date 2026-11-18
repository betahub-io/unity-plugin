using System;
using System.Collections.Generic;
using System.Globalization;

namespace BetaHub
{
    /// <summary>A bug goes to BetaHub's issues, a suggestion to its feature requests.</summary>
    public enum ReportType
    {
        Bug,
        Suggestion,
    }

    /// <summary>The items in the form's left column, top to bottom.</summary>
    public enum WidgetField
    {
        Type,
        Description,
        Steps,
        Terms,
    }

    /// <summary>The header's tabs: the report form, and its attachments full screen.</summary>
    public enum WidgetTab
    {
        Report,
        Attachments,
    }

    /// <summary>What has focus on the sent screen.</summary>
    public enum SentFocus
    {
        Email,
        Link,
    }

    public enum WidgetScreen
    {
        Compose,
        /// <summary>The attachments tab: one attachment full screen over the form.</summary>
        Preview,
        Sending,
        Uploading,
        Sent,
    }

    /// <summary>What a row or prompt value is coloured as.</summary>
    public enum RowTone
    {
        Accent,
        Muted,
        Ok,
        Error,
    }

    public struct RowStatus
    {
        public string Text;
        public RowTone Tone;
        /// <summary>0 to 1 while uploading, otherwise -1: no progress bar.</summary>
        public float Progress;
    }

    /// <summary>The verbs of the prompt bar. The input layer maps each one to a key or button per device.</summary>
    public enum PromptAction
    {
        Confirm,
        Back,
        Alt,
        Send,
        Next,
        Navigate,
        /// <summary>Shift+Tab. Keyboard only.</summary>
        Prev,
        /// <summary>The arrow keys or the d-pad: a hint in the bar, not something to press there.</summary>
        Move,
        /// <summary>Left and right on the report type switch.</summary>
        ChangeType,
        /// <summary>The header's tabs: 1 and 2 on a keyboard, the shoulder buttons on a pad.</summary>
        TabReport,
        TabAttachments,
        /// <summary>The full-screen log, a page towards its oldest or its newest line.</summary>
        ScrollUp,
        ScrollDown,
    }

    public enum WidgetFocusKind
    {
        Type,
        Field,
        Terms,
        Attachment,
    }

    public struct Prompt
    {
        public PromptAction Action;
        public string Label;
        public bool Primary;
        public bool Disabled;
        /// <summary>Shows a spinner instead of a glyph; not pressable.</summary>
        public bool Busy;
    }

    public sealed class PromptBar
    {
        public readonly List<Prompt> Left = new List<Prompt>();
        public readonly List<Prompt> Right = new List<Prompt>();
    }

    /// <summary>The texts that change with the report type. See <see cref="BugReportWidgetModel.TextsFor"/>.</summary>
    public struct TypeTexts
    {
        public string Title;
        public string DescriptionLabel;
        public string DescriptionPrompt;
        public string Send;
        public string Link;
        public string SentWith;
        public string SentTitle;
    }

    /// <summary>Everything the prompt bar depends on.</summary>
    public struct PromptContext
    {
        public WidgetScreen Screen;
        public WidgetFocusKind Focus;
        public ReportType Type;
        /// <summary>A text field has the keyboard.</summary>
        public bool Typing;
        public bool Gamepad;
        /// <summary>The focused attachment, or the previewed one, is included.</summary>
        public bool FocusedIncluded;
        public bool TermsAccepted;
        public bool CanSend;
        public bool HasError;
        public bool Configured;
        /// <summary>Sent screen: an anonymous player can still leave an email (not after a suggestion or a saved one).</summary>
        public bool CanEmail;
        public SentFocus SentFocus;
        public bool HasAttachments;
        /// <summary>The full-screen preview shows a log.</summary>
        public bool PreviewIsLog;
        /// <summary>Where that log is scrolled: nothing older, nothing newer left to show.</summary>
        public bool LogAtTop;
        public bool LogAtEnd;
    }

    /// <summary>
    /// Pure decisions behind <see cref="BugReportWidget"/>: screens, headers, row values, the prompt bar,
    /// send gating and small text formatters. Free of UnityEngine so it runs in the fast test loop.
    /// </summary>
    public static class BugReportWidgetModel
    {
        public const int DescriptionLimit = 2000;
        public const int StepsLimit = 1000;
        /// <summary>RFC 5321: the longest address a mail server must accept.</summary>
        public const int EmailLimit = 254;
        public const int LogTailLines = 14;
        public const string Ellipsis = "…";
        public const string SeeConsoleSuffix = " (see console)";
        private const long KiloByte = 1024;
        private const long MegaByte = 1024 * 1024;
        private const float NoProgress = -1f;
        private const int PercentScale = 100;
        private const double PercentEpsilon = 1e-4;

        public enum HeaderKind { Normal, Warning, Error }

        public struct Header
        {
            public HeaderKind Kind;
            public string Title;
            public string Subtitle;
            /// <summary>The report and attachments tabs stand in the title's place.</summary>
            public bool Tabs;
        }

        /// <summary>From six attachments the list goes to two columns, so nine fit without scrolling.</summary>
        public const int TwoColumnThreshold = 6;

        public static int ColumnCountFor(int itemCount) => itemCount >= TwoColumnThreshold ? 2 : 1;

        /// <summary>
        /// A report waiting for the player's email (or for media before that) when the form is closed:
        /// nobody can type one any more, so it publishes without it instead of staying a draft.
        /// </summary>
        public static bool ShouldPublishWithoutEmail(ReportSubmissionState state, bool formVisible)
        {
            return !formVisible && (state == ReportSubmissionState.UploadingMedia || state == ReportSubmissionState.AwaitingPublish);
        }

        public static WidgetScreen ScreenFor(ReportSubmissionState state)
        {
            switch (state)
            {
                case ReportSubmissionState.Preparing:
                case ReportSubmissionState.CreatingDraft:
                    return WidgetScreen.Sending;
                case ReportSubmissionState.UploadingMedia:
                    return WidgetScreen.Uploading;
                case ReportSubmissionState.AwaitingPublish:
                case ReportSubmissionState.Publishing:
                case ReportSubmissionState.Published:
                    return WidgetScreen.Sent;
                case ReportSubmissionState.Failed:
                    return WidgetScreen.Compose;
                default:
                    throw new ArgumentOutOfRangeException(nameof(state), state, null);
            }
        }

        /// <summary>Share of a field's limit from which its tag shows the character count instead of its note.</summary>
        public const double CounterThreshold = 0.8;

        /// <summary>
        /// The tag on a text field's label: its required/optional note, or the count near the limit, since the
        /// field stops taking text there.
        /// </summary>
        public static string FieldTag(string note, int length, int limit, BugReportWidgetStrings s)
        {
            if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit), limit, "A field limit must be at least 1.");
            if (length < limit * CounterThreshold) return note;
            return string.Format(CultureInfo.InvariantCulture, s.CharacterCount,
                length.ToString("N0", CultureInfo.InvariantCulture), limit.ToString("N0", CultureInfo.InvariantCulture));
        }

        public static bool IsAtLimit(int length, int limit) => length >= limit;

        /// <param name="minLength">Characters the description needs, spaces around it not counted: the project's minimum for the report type.</param>
        public static bool CanSend(string description, bool termsRequired, bool termsAccepted, bool configured, int minLength)
        {
            if (!configured) return false;
            if (string.IsNullOrWhiteSpace(description) || description.Length > DescriptionLimit) return false;
            if (description.Trim().Length < minLength) return false;
            if (termsRequired && !termsAccepted) return false;
            return true;
        }

        /// <summary>The description's tag: how far to the minimum while below it, then the usual note or count.</summary>
        public static string DescriptionTag(string description, int minLength, BugReportWidgetStrings s)
        {
            string text = description ?? "";
            int trimmed = text.Trim().Length;
            if (trimmed < minLength)
            {
                return string.Format(CultureInfo.InvariantCulture, s.DescriptionMinimum,
                    trimmed.ToString("N0", CultureInfo.InvariantCulture), minLength.ToString("N0", CultureInfo.InvariantCulture));
            }
            return FieldTag(s.Required, text.Length, DescriptionLimit, s);
        }

        /// <summary>The left column for a report type, top to bottom: a suggestion has no steps.</summary>
        public static List<WidgetField> FieldsFor(ReportType type, bool termsRequired)
        {
            var fields = new List<WidgetField> { WidgetField.Type, WidgetField.Description };
            if (type == ReportType.Bug) fields.Add(WidgetField.Steps);
            if (termsRequired) fields.Add(WidgetField.Terms);
            return fields;
        }

        /// <summary>The words that follow the report type: a suggestion is no report of something that happened.</summary>
        public static TypeTexts TextsFor(ReportType type, BugReportWidgetStrings s)
        {
            if (type == ReportType.Suggestion)
            {
                return new TypeTexts
                {
                    Title = s.SuggestTitle, DescriptionLabel = s.SuggestionLabel, DescriptionPrompt = s.SuggestionPrompt,
                    Send = s.SendSuggestion, Link = s.SuggestionLink, SentWith = s.SentWithSuggestion, SentTitle = s.SuggestionSentTitle,
                };
            }
            return new TypeTexts
            {
                Title = s.Title, DescriptionLabel = s.DescriptionLabel, DescriptionPrompt = s.DescriptionPrompt,
                Send = s.SendReport, Link = s.ReportLink, SentWith = s.SentWith, SentTitle = s.SentTitle,
            };
        }

        // --- headers ---

        /// <summary>
        /// Header of the compose screen. Errors and misconfiguration replace the title instead of adding
        /// a banner, so the screen never changes shape.
        /// </summary>
        public static Header ComposeHeader(bool configured, bool isDevelopmentBuild, string configurationError,
            string submissionError, string signedInAs, ReportType type, BugReportWidgetStrings s)
        {
            if (!configured)
            {
                return new Header
                {
                    Kind = HeaderKind.Warning,
                    Title = s.UnavailableTitle,
                    Subtitle = isDevelopmentBuild && !string.IsNullOrEmpty(configurationError)
                        ? configurationError + SeeConsoleSuffix
                        : s.UnavailableForPlayers,
                };
            }
            // The tabs take the title's place, so a failed send says so in the subtitle.
            if (!string.IsNullOrEmpty(submissionError))
            {
                return new Header { Kind = HeaderKind.Error, Title = TextsFor(type, s).Title, Subtitle = string.Format(s.ErrorSubtitle, submissionError.Trim()), Tabs = true };
            }
            return new Header
            {
                Kind = HeaderKind.Normal,
                Title = TextsFor(type, s).Title,
                Subtitle = string.IsNullOrEmpty(signedInAs) ? s.Subtitle : string.Format(s.SubtitleSignedIn, signedInAs),
                Tabs = true,
            };
        }

        public static Header UploadingHeader(BugReportWidgetStrings s)
        {
            return new Header { Kind = HeaderKind.Normal, Title = s.UploadingTitle, Subtitle = s.UploadingSubtitle };
        }

        public static Header SentHeader(string issueId, bool signedIn, ReportType type, BugReportWidgetStrings s)
        {
            return new Header
            {
                Kind = HeaderKind.Normal,
                Title = TextsFor(type, s).SentTitle,
                Subtitle = string.Format(signedIn ? s.SentSignedIn : s.SentAnonymous, issueId ?? ""),
            };
        }

        // --- attachment rows ---

        /// <summary>The row's checkbox shows it is included; only leaving something out is spelt out.</summary>
        public static RowStatus ComposeRowStatus(bool included, BugReportWidgetStrings s)
        {
            return included
                ? new RowStatus { Text = "", Tone = RowTone.Accent, Progress = NoProgress }
                : new RowStatus { Text = s.LeftOut, Tone = RowTone.Muted, Progress = NoProgress };
        }

        public static RowStatus UploadRowStatus(bool included, AttachmentUploadState state, BugReportWidgetStrings s)
        {
            if (!included) return ComposeRowStatus(false, s);
            switch (state.Status)
            {
                case AttachmentUploadStatus.Uploading:
                    return new RowStatus { Text = FormatPercent(state.Progress, s), Tone = RowTone.Accent, Progress = state.Progress };
                case AttachmentUploadStatus.Sent:
                    return new RowStatus { Text = s.Sent, Tone = RowTone.Ok, Progress = NoProgress };
                case AttachmentUploadStatus.Failed:
                    return new RowStatus { Text = s.Failed, Tone = RowTone.Error, Progress = NoProgress };
                default:
                    return new RowStatus { Text = s.Queued, Tone = RowTone.Muted, Progress = NoProgress };
            }
        }

        /// <summary>Whole percent, rounded down, so 100 only ever means done. The epsilon keeps float
        /// noise (0.45f * 100 = 44.99999 on Mono) from dropping a whole point.</summary>
        public static int Percent(float progress) => (int)Math.Floor(Math.Max(0f, Math.Min(1f, progress)) * PercentScale + PercentEpsilon);

        public static string FormatPercent(float progress, BugReportWidgetStrings s) => string.Format(s.Percent, Percent(progress));

        // --- prompt bar ---

        /// <summary>The prompt bar. Back is always the first prompt, so it never moves between screens.</summary>
        public static PromptBar Prompts(PromptContext c, BugReportWidgetStrings s)
        {
            var bar = new PromptBar();
            switch (c.Screen)
            {
                case WidgetScreen.Preview:
                    bar.Left.Add(P(PromptAction.Back, s.BackToReport));
                    bar.Left.Add(P(PromptAction.Navigate, s.Item));
                    if (c.PreviewIsLog)
                    {
                        bar.Left.Add(new Prompt { Action = PromptAction.ScrollUp, Label = s.OlderLines, Disabled = c.LogAtTop });
                        bar.Left.Add(new Prompt { Action = PromptAction.ScrollDown, Label = s.NewerLines, Disabled = c.LogAtEnd });
                    }
                    bar.Left.Add(P(PromptAction.Confirm, c.FocusedIncluded ? s.LeaveOut : s.Include));
                    break;

                case WidgetScreen.Sending:
                    bar.Right.Add(new Prompt { Action = PromptAction.Send, Label = s.Sending, Busy = true });
                    break;

                case WidgetScreen.Uploading:
                    bar.Left.Add(P(PromptAction.Back, s.BackToGameUploading));
                    break;

                case WidgetScreen.Sent:
                    AddSentPrompts(bar, c, s);
                    break;

                default:
                    AddComposePrompts(bar, c, s);
                    break;
            }
            return bar;
        }

        private static void AddSentPrompts(PromptBar bar, PromptContext c, BugReportWidgetStrings s)
        {
            if (!c.CanEmail)
            {
                bar.Left.Add(new Prompt { Action = PromptAction.Back, Label = s.BackToGame, Primary = true });
                bar.Left.Add(P(PromptAction.Alt, s.CopyLink));
                return;
            }
            if (c.Typing)
            {
                bar.Left.Add(P(PromptAction.Back, s.Done));
            }
            else
            {
                bar.Left.Add(P(PromptAction.Back, s.BackToGame));
                bar.Left.Add(P(PromptAction.Move, s.Move));
                if (c.SentFocus == SentFocus.Link)
                {
                    bar.Left.Add(P(PromptAction.Confirm, s.CopyLink));
                }
                else
                {
                    bar.Left.Add(P(PromptAction.Confirm, s.Write));
                    bar.Left.Add(P(PromptAction.Alt, s.CopyLink));
                }
            }
            bar.Right.Add(new Prompt { Action = PromptAction.Send, Label = s.EmailMeALink, Primary = true });
        }

        private static void AddComposePrompts(PromptBar bar, PromptContext c, BugReportWidgetStrings s)
        {
            if (!c.Configured)
            {
                bar.Left.Add(P(PromptAction.Back, s.Close));
            }
            else if (c.Typing)
            {
                bar.Left.Add(P(PromptAction.Back, s.Done));
                if (!c.Gamepad)
                {
                    bar.Left.Add(P(PromptAction.Next, s.Next));
                    bar.Left.Add(P(PromptAction.Prev, s.Previous));
                }
            }
            else
            {
                bar.Left.Add(P(PromptAction.Back, s.Close));
                bar.Left.Add(P(PromptAction.Move, s.Move));
                switch (c.Focus)
                {
                    case WidgetFocusKind.Attachment:
                        bar.Left.Add(P(PromptAction.Confirm, c.FocusedIncluded ? s.LeaveOut : s.Include));
                        bar.Left.Add(P(PromptAction.Alt, s.AttachmentsTab));
                        break;
                    case WidgetFocusKind.Type:
                        bar.Left.Add(P(PromptAction.ChangeType, s.BugOrSuggestion));
                        break;
                    case WidgetFocusKind.Terms:
                        bar.Left.Add(P(PromptAction.Confirm, c.TermsAccepted ? s.Withdraw : s.Accept));
                        bar.Left.Add(P(PromptAction.Alt, s.ReadTerms));
                        break;
                    default:
                        bar.Left.Add(P(PromptAction.Confirm, s.Write));
                        if (c.HasAttachments) bar.Left.Add(P(PromptAction.Alt, s.AttachmentsTab));
                        break;
                }
            }

            bar.Right.Add(new Prompt
            {
                Action = PromptAction.Send,
                Label = c.HasError && c.Configured ? s.TryAgain : TextsFor(c.Type, s).Send,
                Primary = c.CanSend,
                Disabled = !c.CanSend,
            });
        }

        private static Prompt P(PromptAction action, string label) => new Prompt { Action = action, Label = label };

        // --- text helpers ---

        public static string FormatDuration(int seconds)
        {
            if (seconds < 0) seconds = 0;
            return $"{seconds / 60}:{seconds % 60:00}";
        }

        public static string FormatFileSize(long bytes)
        {
            if (bytes >= MegaByte) return $"{bytes / (double)MegaByte:0.#} MB";
            if (bytes >= KiloByte) return $"{bytes / KiloByte} KB";
            return $"{bytes} B";
        }

        /// <summary>Last <paramref name="lines"/> non-empty lines of a log, joined with newlines.</summary>
        public static string LogTail(string log, int lines)
        {
            if (string.IsNullOrEmpty(log) || lines <= 0) return string.Empty;
            var kept = new List<string>();
            var all = log.Split('\n');
            for (int i = all.Length - 1; i >= 0 && kept.Count < lines; i--)
            {
                string line = all[i].TrimEnd('\r');
                if (line.Length > 0) kept.Add(line);
            }
            kept.Reverse();
            return string.Join("\n", kept);
        }

        public static string Truncate(string text, int maxLength)
        {
            if (text == null) return string.Empty;
            if (maxLength <= 0) return string.Empty;
            if (text.Length <= maxLength) return text;
            return text.Substring(0, maxLength - Ellipsis.Length) + Ellipsis;
        }
    }

    /// <summary>Key caps shown for each prompt. Controller glyph sets join this in the input layer.</summary>
    public static class PromptGlyphs
    {
        private static readonly string[] Enter = { "Enter" };
        private static readonly string[] Escape = { "Esc" };
        private static readonly string[] PreviewKey = { "P" };
        private static readonly string[] CtrlEnter = { "Ctrl", "Enter" };
        private static readonly string[] Tab = { "Tab" };
        private static readonly string[] ShiftTab = { "Shift", "Tab" };
        private static readonly string[] UpDown = { "↑", "↓" };
        private static readonly string[] Arrows = { "↑", "↓", "←", "→" };
        private static readonly string[] LeftRight = { "←", "→" };
        private static readonly string[] ReportTabKey = { "1" };
        private static readonly string[] AttachmentsTabKey = { "2" };
        private static readonly string[] PageUp = { "PgUp" };
        private static readonly string[] PageDown = { "PgDn" };
        private const string DpadUpDown = "▲ ▼";
        private const string DpadAll = "▲ ▼ ◀ ▶";
        private const string DpadLeftRight = "◀ ▶";
        private const string RightStickUp = "R ▲";
        private const string RightStickDown = "R ▼";

        public static GlyphPart[] For(GlyphSet set, PromptAction action)
        {
            if (set == GlyphSet.Keyboard)
            {
                var keys = Keyboard(action);
                var parts = new GlyphPart[keys.Length];
                for (int i = 0; i < keys.Length; i++) parts[i] = new GlyphPart { Kind = GlyphPartKind.Key, Text = keys[i] };
                return parts;
            }
            switch (action)
            {
                case PromptAction.Navigate: return Chip(DpadUpDown);
                case PromptAction.Move: return Chip(DpadAll);
                case PromptAction.ChangeType: return Chip(DpadLeftRight);
                case PromptAction.ScrollUp: return Chip(RightStickUp);
                case PromptAction.ScrollDown: return Chip(RightStickDown);
                case PromptAction.TabReport: return Chip(PadLayout.Shoulder(set, left: true));
                case PromptAction.TabAttachments: return Chip(PadLayout.Shoulder(set, left: false));
            }
            var button = PadLayout.ButtonFor(set, action);
            return new[] { new GlyphPart { Kind = GlyphPartKind.Face, Text = PadLayout.Label(set, button), Button = button } };
        }

        private static GlyphPart[] Chip(string text) => new[] { new GlyphPart { Kind = GlyphPartKind.Dpad, Text = text } };

        public static string[] Keyboard(PromptAction action)
        {
            switch (action)
            {
                case PromptAction.Confirm: return Enter;
                case PromptAction.Back: return Escape;
                case PromptAction.Alt: return PreviewKey;
                case PromptAction.Send: return CtrlEnter;
                case PromptAction.Next: return Tab;
                case PromptAction.Prev: return ShiftTab;
                case PromptAction.Navigate: return UpDown;
                case PromptAction.Move: return Arrows;
                case PromptAction.ChangeType: return LeftRight;
                case PromptAction.TabReport: return ReportTabKey;
                case PromptAction.TabAttachments: return AttachmentsTabKey;
                case PromptAction.ScrollUp: return PageUp;
                case PromptAction.ScrollDown: return PageDown;
                default: throw new ArgumentOutOfRangeException(nameof(action), action, null);
            }
        }
    }

    public enum FocusMove
    {
        Up,
        Down,
        Left,
        Right,
    }

    /// <summary>
    /// Where focus is on the compose screen: fields on the left (description, steps, optional terms),
    /// attachments on the right in one or two columns. Up and Down walk the left column and flow into
    /// the list; Left and Right jump between the sides and remember where you were on each.
    /// </summary>
    public sealed class WidgetFocus
    {
        private int _fieldCount, _attachmentCount, _columns;
        private int _lastField, _lastAttachment;

        public bool OnAttachment { get; private set; }
        public int Index { get; private set; }
        /// <summary>The attachment focus last left: where the attachments tab opens from a field.</summary>
        public int LastAttachment => _lastAttachment;

        public WidgetFocus(int fieldCount, int attachmentCount, int columns)
        {
            Resize(fieldCount, attachmentCount, columns);
            FocusField(0);
        }

        public void Resize(int fieldCount, int attachmentCount, int columns)
        {
            if (fieldCount < 1) throw new ArgumentOutOfRangeException(nameof(fieldCount), fieldCount, "At least one field.");
            if (attachmentCount < 0) throw new ArgumentOutOfRangeException(nameof(attachmentCount), attachmentCount, null);
            if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns), columns, null);
            _fieldCount = fieldCount;
            _attachmentCount = attachmentCount;
            _columns = columns;
            _lastField = Math.Min(_lastField, fieldCount - 1);
            _lastAttachment = Math.Max(0, Math.Min(_lastAttachment, attachmentCount - 1));

            if (OnAttachment && attachmentCount == 0) FocusField(fieldCount - 1);
            else if (OnAttachment) FocusAttachment(Math.Min(Index, attachmentCount - 1));
            else FocusField(Math.Min(Index, fieldCount - 1));
        }

        public void FocusField(int index)
        {
            if (index < 0 || index >= _fieldCount) throw new ArgumentOutOfRangeException(nameof(index), index, null);
            OnAttachment = false;
            Index = index;
            _lastField = index;
        }

        public void FocusAttachment(int index)
        {
            if (index < 0 || index >= _attachmentCount) throw new ArgumentOutOfRangeException(nameof(index), index, null);
            OnAttachment = true;
            Index = index;
            _lastAttachment = index;
        }

        /// <returns>True when focus moved.</returns>
        public bool Move(FocusMove move)
        {
            bool wasAttachment = OnAttachment;
            int was = Index;
            if (OnAttachment) MoveInAttachments(move);
            else MoveInFields(move);
            return wasAttachment != OnAttachment || was != Index;
        }

        /// <summary>Tab and Shift+Tab: every field, then every attachment, and round again.</summary>
        public void Step(int delta)
        {
            int count = _fieldCount + _attachmentCount;
            int current = OnAttachment ? _fieldCount + Index : Index;
            int target = ((current + delta) % count + count) % count;
            if (target < _fieldCount) FocusField(target);
            else FocusAttachment(target - _fieldCount);
        }

        private void MoveInFields(FocusMove move)
        {
            switch (move)
            {
                case FocusMove.Up:
                    if (Index > 0) FocusField(Index - 1);
                    break;
                case FocusMove.Down:
                    if (Index < _fieldCount - 1) FocusField(Index + 1);
                    else if (_attachmentCount > 0) FocusAttachment(0);
                    break;
                case FocusMove.Right:
                    if (_attachmentCount > 0) FocusAttachment(_lastAttachment);
                    break;
            }
        }

        private void MoveInAttachments(FocusMove move)
        {
            int column = Index % _columns;
            int row = Index / _columns;
            int lastRow = (_attachmentCount - 1) / _columns;
            switch (move)
            {
                case FocusMove.Up:
                    if (row > 0) FocusAttachment(Index - _columns);
                    else FocusField(_fieldCount - 1);
                    break;
                case FocusMove.Down:
                    if (Index + _columns < _attachmentCount) FocusAttachment(Index + _columns);
                    else if (row < lastRow) FocusAttachment(_attachmentCount - 1);
                    break;
                case FocusMove.Left:
                    if (column > 0) FocusAttachment(Index - 1);
                    else FocusField(_lastField);
                    break;
                case FocusMove.Right:
                    if (column < _columns - 1 && Index + 1 < _attachmentCount) FocusAttachment(Index + 1);
                    break;
            }
        }
    }
}
