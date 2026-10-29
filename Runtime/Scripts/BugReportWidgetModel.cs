using System;
using System.Collections.Generic;

namespace BetaHub
{
    public enum WidgetScreen
    {
        Compose,
        /// <summary>One attachment full screen over the form.</summary>
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
    }

    public enum WidgetFocusKind
    {
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

    /// <summary>Everything the prompt bar depends on.</summary>
    public struct PromptContext
    {
        public WidgetScreen Screen;
        public WidgetFocusKind Focus;
        /// <summary>A text field has the keyboard.</summary>
        public bool Typing;
        public bool Gamepad;
        /// <summary>The focused attachment, or the previewed one, is included.</summary>
        public bool FocusedIncluded;
        public bool TermsAccepted;
        public bool CanSend;
        public bool HasError;
        public bool Configured;
        public bool SignedIn;
        /// <summary>Sent screen: an anonymous player can still leave an email.</summary>
        public bool CanEmail;
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

        public static bool CanSend(string description, bool termsRequired, bool termsAccepted, bool configured)
        {
            if (!configured) return false;
            if (string.IsNullOrWhiteSpace(description) || description.Length > DescriptionLimit) return false;
            if (termsRequired && !termsAccepted) return false;
            return true;
        }

        // --- headers ---

        /// <summary>
        /// Header of the compose screen. Errors and misconfiguration replace the title instead of adding
        /// a banner, so the screen never changes shape.
        /// </summary>
        public static Header ComposeHeader(bool configured, bool isDevelopmentBuild, string configurationError,
            string submissionError, string signedInAs, BugReportWidgetStrings s)
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
            if (!string.IsNullOrEmpty(submissionError))
            {
                return new Header { Kind = HeaderKind.Error, Title = s.ErrorTitle, Subtitle = string.Format(s.ErrorSubtitle, submissionError) };
            }
            return new Header
            {
                Kind = HeaderKind.Normal,
                Title = s.Title,
                Subtitle = string.IsNullOrEmpty(signedInAs) ? s.Subtitle : string.Format(s.SubtitleSignedIn, signedInAs),
            };
        }

        public static Header UploadingHeader(BugReportWidgetStrings s)
        {
            return new Header { Kind = HeaderKind.Normal, Title = s.UploadingTitle, Subtitle = s.UploadingSubtitle };
        }

        public static Header SentHeader(string issueId, bool signedIn, BugReportWidgetStrings s)
        {
            return new Header
            {
                Kind = HeaderKind.Normal,
                Title = s.SentTitle,
                Subtitle = string.Format(signedIn ? s.SentSignedIn : s.SentAnonymous, issueId ?? ""),
            };
        }

        // --- attachment rows ---

        public static RowStatus ComposeRowStatus(bool included, BugReportWidgetStrings s)
        {
            return included
                ? new RowStatus { Text = s.Included, Tone = RowTone.Accent, Progress = NoProgress }
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

        public static PromptBar Prompts(PromptContext c, BugReportWidgetStrings s)
        {
            var bar = new PromptBar();
            switch (c.Screen)
            {
                case WidgetScreen.Preview:
                    bar.Left.Add(P(PromptAction.Navigate, s.Item));
                    bar.Left.Add(P(PromptAction.Confirm, c.FocusedIncluded ? s.LeaveOut : s.Include));
                    bar.Left.Add(P(PromptAction.Back, s.BackToForm));
                    break;

                case WidgetScreen.Sending:
                    bar.Right.Add(new Prompt { Action = PromptAction.Send, Label = s.Sending, Busy = true });
                    break;

                case WidgetScreen.Uploading:
                    bar.Left.Add(P(PromptAction.Back, s.BackToGameUploading));
                    break;

                case WidgetScreen.Sent:
                    if (!c.SignedIn && c.CanEmail)
                    {
                        bar.Left.Add(P(PromptAction.Confirm, s.Write));
                        bar.Left.Add(P(PromptAction.Alt, s.CopyLink));
                        bar.Left.Add(P(PromptAction.Back, s.BackToGame));
                        bar.Right.Add(new Prompt { Action = PromptAction.Send, Label = s.EmailMeALink, Primary = true });
                    }
                    else
                    {
                        bar.Left.Add(P(PromptAction.Alt, s.CopyLink));
                        bar.Right.Add(new Prompt { Action = PromptAction.Back, Label = s.BackToGame, Primary = true });
                    }
                    break;

                default:
                    AddComposePrompts(bar, c, s);
                    break;
            }
            return bar;
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
                if (!c.Gamepad) bar.Left.Add(P(PromptAction.Next, s.NextField));
            }
            else if (c.Focus == WidgetFocusKind.Attachment)
            {
                bar.Left.Add(P(PromptAction.Confirm, c.FocusedIncluded ? s.LeaveOut : s.Include));
                bar.Left.Add(P(PromptAction.Alt, s.FullScreen));
                bar.Left.Add(P(PromptAction.Back, s.Close));
            }
            else if (c.Focus == WidgetFocusKind.Terms)
            {
                bar.Left.Add(P(PromptAction.Confirm, c.TermsAccepted ? s.Withdraw : s.Accept));
                bar.Left.Add(P(PromptAction.Alt, s.ReadTerms));
                bar.Left.Add(P(PromptAction.Back, s.Close));
            }
            else
            {
                bar.Left.Add(P(PromptAction.Confirm, s.Write));
                bar.Left.Add(P(PromptAction.Back, s.Close));
            }

            bar.Right.Add(new Prompt
            {
                Action = PromptAction.Send,
                Label = c.HasError && c.Configured ? s.TryAgain : s.SendReport,
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
        private static readonly string[] UpDown = { "↑", "↓" };
        private const string DpadUpDown = "▲ ▼";

        public static GlyphPart[] For(GlyphSet set, PromptAction action)
        {
            if (set == GlyphSet.Keyboard)
            {
                var keys = Keyboard(action);
                var parts = new GlyphPart[keys.Length];
                for (int i = 0; i < keys.Length; i++) parts[i] = new GlyphPart { Kind = GlyphPartKind.Key, Text = keys[i] };
                return parts;
            }
            if (action == PromptAction.Navigate)
            {
                return new[] { new GlyphPart { Kind = GlyphPartKind.Dpad, Text = DpadUpDown } };
            }
            var button = PadLayout.ButtonFor(set, action);
            return new[] { new GlyphPart { Kind = GlyphPartKind.Face, Text = PadLayout.Label(set, button), Button = button } };
        }

        public static string[] Keyboard(PromptAction action)
        {
            switch (action)
            {
                case PromptAction.Confirm: return Enter;
                case PromptAction.Back: return Escape;
                case PromptAction.Alt: return PreviewKey;
                case PromptAction.Send: return CtrlEnter;
                case PromptAction.Next: return Tab;
                case PromptAction.Navigate: return UpDown;
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
