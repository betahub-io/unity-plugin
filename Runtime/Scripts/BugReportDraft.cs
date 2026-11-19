using System.Collections.Generic;

namespace BetaHub
{
    /// <summary>
    /// The three toggles of the legacy form. <see cref="BugReportSession.ApplyOptions"/> maps them
    /// onto an <see cref="AttachmentSet"/>; new UIs work with the set directly.
    /// </summary>
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

    /// <summary>The plugin's own player log, as seen by the host when the form opens.</summary>
    public sealed class PlayerLogSource
    {
        public readonly string Path;
        public readonly long SizeBytes;

        public PlayerLogSource(string path, long sizeBytes)
        {
            Path = path;
            SizeBytes = sizeBytes;
        }
    }

    public enum UploadKind
    {
        Screenshot,
        Log,
        Video,
        /// <summary>The recorder's last minute, saved at upload time.</summary>
        Recording,
        File,
    }

    /// <summary>One upload the client performs after the draft exists, tied to its attachment row.</summary>
    public sealed class ReportUpload
    {
        public string AttachmentId;
        public UploadKind Kind;
        public string Path;
        public bool RemoveAfterUpload;
        /// <summary>Display name stored with File uploads.</summary>
        public string Name;
        /// <summary>In-memory content for File uploads without a <see cref="Path"/>.</summary>
        public byte[] Content;
        public string FileName;
    }

    /// <summary>Everything needed to create and upload one report. Built by <see cref="BugReportSession"/>.</summary>
    public sealed class BugReportDraft
    {
        public string Description;
        public string Steps;
        public List<ReportUpload> Uploads = new List<ReportUpload>();
        /// <summary>Included attachments that travel inside the draft request itself, as custom fields.</summary>
        public List<string> InlineAttachmentIds = new List<string>();
        public Dictionary<string, string> CustomFields = new Dictionary<string, string>();
    }
}
