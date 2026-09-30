using System;
using System.Collections.Generic;
using System.Text;

namespace BetaHub
{
    public enum WidgetScreen
    {
        Compose,
        Sending,
        Uploading,
        Sent,
    }

    /// <summary>
    /// Pure decisions behind <see cref="BugReportWidget"/>: which screen a submission state maps to,
    /// when Send is allowed, and the small text formatters. Kept free of UnityEngine so it runs in EditMode tests.
    /// </summary>
    public static class BugReportWidgetModel
    {
        public const int DescriptionLimit = 2000;
        public const int StepsLimit = 1000;
        public const int LogTailLines = 4;
        public const string Ellipsis = "…";
        public const string PlayerUnavailableMessage = "Bug reporting isn't available right now.";
        public const string SeeConsoleSuffix = " (see console)";
        private const long KiloByte = 1024;
        private const long MegaByte = 1024 * 1024;

        public enum HeaderKind { Normal, Warning, Error }

        public struct Header
        {
            public HeaderKind Kind;
            public string Subtitle;
        }

        public const int TwoColumnThreshold = 7;

        /// <summary>One column up to six attachments, two from seven, so the panel widens instead of scrolling.</summary>
        public static int ColumnCountFor(int itemCount) => itemCount >= TwoColumnThreshold ? 2 : 1;

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
            if (string.IsNullOrWhiteSpace(description)) return false;
            if (termsRequired && !termsAccepted) return false;
            return true;
        }

        public static string FormatCounter(int used, int limit) => $"{used} / {limit}";

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

        /// <summary>
        /// Header treatment of the compose screen. Errors and misconfiguration replace the subtitle instead
        /// of adding a banner, so the panel never changes shape.
        /// </summary>
        public static Header ComposeHeader(bool configured, bool isDevelopmentBuild, string configurationError, string submissionError, string signedInAs)
        {
            if (!configured)
            {
                return new Header
                {
                    Kind = HeaderKind.Warning,
                    Subtitle = isDevelopmentBuild && !string.IsNullOrEmpty(configurationError)
                        ? configurationError + SeeConsoleSuffix
                        : PlayerUnavailableMessage,
                };
            }
            if (!string.IsNullOrEmpty(submissionError))
            {
                return new Header { Kind = HeaderKind.Error, Subtitle = submissionError };
            }
            return new Header
            {
                Kind = HeaderKind.Normal,
                Subtitle = string.IsNullOrEmpty(signedInAs) ? null : "Reporting as " + signedInAs,
            };
        }
    }
}
