using System.Collections.Generic;

namespace BetaHub
{
    /// <summary>Which optional data the reporter wants in this particular report.</summary>
    public struct BugReportOptions
    {
        public bool IncludeScreenshots;
        public bool IncludeLogs;
        public bool IncludeRecording;

        public static BugReportOptions All => new BugReportOptions
        {
            IncludeScreenshots = true,
            IncludeLogs = true,
            IncludeRecording = true,
        };
    }

    /// <summary>Release the report should be associated with. Default associates with the latest release.</summary>
    public struct ReleaseTarget
    {
        public int Id;
        public string Label;

        public static ReleaseTarget Latest => new ReleaseTarget();

        public bool HasId => Id > 0;
        public bool HasLabel => !string.IsNullOrEmpty(Label);
    }

    /// <summary>The plugin's own player log, as seen by the host at submit time.</summary>
    public sealed class PlayerLogSource
    {
        public readonly Issue.LogFileReference Reference;
        public readonly long SizeBytes;

        public PlayerLogSource(Issue.LogFileReference reference, long sizeBytes)
        {
            Reference = reference;
            SizeBytes = sizeBytes;
        }
    }

    /// <summary>Everything needed to create and upload one report. Built by <see cref="BugReportSession"/>.</summary>
    public sealed class BugReportDraft
    {
        public string Description;
        public string Steps;
        public List<Issue.ScreenshotFileReference> Screenshots = new List<Issue.ScreenshotFileReference>();
        public List<Issue.LogFileReference> LogFiles = new List<Issue.LogFileReference>();
        public List<Issue.VideoFileReference> Videos = new List<Issue.VideoFileReference>();
        public bool IncludeRecording;
        public Dictionary<string, string> CustomFields = new Dictionary<string, string>();
    }
}
