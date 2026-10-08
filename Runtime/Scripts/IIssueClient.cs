using System;
using System.Collections;

namespace BetaHub
{
    /// <summary>
    /// HTTP surface used by <see cref="ReportSubmission"/>. Each call reports failure through
    /// <c>onError</c>; a call that finishes without invoking it succeeded.
    /// </summary>
    public interface IIssueClient
    {
        string IssueId { get; }
        string IssueUrl { get; }

        IEnumerator CreateDraft(BugReportDraft draft, ReleaseTarget release, Action<string> onError);
        /// <summary>
        /// Uploads <see cref="BugReportDraft.Uploads"/> in order. Reports each item through
        /// <paramref name="onProgress"/> (id, status, 0..1) and ends every item as Sent or Failed;
        /// a failed item does not stop the others.
        /// </summary>
        IEnumerator UploadMedia(BugReportDraft draft, Action<string, AttachmentUploadStatus, float> onProgress);
        IEnumerator SetReporterEmail(string email, Action<string> onError);
        IEnumerator Publish(bool emailMyReport, Action<string> onError);
    }
}
