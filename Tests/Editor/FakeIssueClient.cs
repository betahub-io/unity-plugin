using System;
using System.Collections;
using System.Collections.Generic;

namespace BetaHub.Tests
{
    public class FakeIssueClient : IIssueClient
    {
        public const string CreatedIssueId = "12345";
        public const string CreatedIssueUrl = "https://app.betahub.io/issues/12345";
        private const float HalfWay = 0.5f;

        public string DraftError;
        public string PublishError;
        public string EmailError;
        public bool HoldMedia;
        /// <summary>Stops forever after the first upload reports half its progress.</summary>
        public bool HoldAfterFirstHalf;
        public readonly HashSet<string> FailUploads = new HashSet<string>();
        /// <summary>Uploads the client skips without reporting anything, like a buggy client would.</summary>
        public readonly HashSet<string> SilentUploads = new HashSet<string>();
        /// <summary>Reports progress for an id that is not in the draft.</summary>
        public string ExtraProgressId;

        public readonly List<string> Calls = new List<string>();
        public BugReportDraft LastDraft;
        public ReleaseTarget LastRelease;

        public string IssueId { get; private set; }
        public string IssueUrl { get; private set; }

        public IEnumerator CreateDraft(BugReportDraft draft, ReleaseTarget release, Action<string> onError)
        {
            Calls.Add("draft");
            LastDraft = draft;
            LastRelease = release;
            yield return null;
            if (DraftError != null)
            {
                onError(DraftError);
                yield break;
            }
            IssueId = CreatedIssueId;
            IssueUrl = CreatedIssueUrl;
        }

        public IEnumerator UploadMedia(BugReportDraft draft, Action<string, AttachmentUploadStatus, float> onProgress)
        {
            Calls.Add("media");
            while (HoldMedia)
            {
                yield return null;
            }

            if (ExtraProgressId != null)
            {
                onProgress(ExtraProgressId, AttachmentUploadStatus.Uploading, HalfWay);
            }

            foreach (var upload in draft.Uploads)
            {
                if (SilentUploads.Contains(upload.AttachmentId)) continue;

                onProgress(upload.AttachmentId, AttachmentUploadStatus.Uploading, 0f);
                yield return null;
                onProgress(upload.AttachmentId, AttachmentUploadStatus.Uploading, HalfWay);
                while (HoldAfterFirstHalf)
                {
                    yield return null;
                }
                yield return null;
                bool failed = FailUploads.Contains(upload.AttachmentId);
                onProgress(upload.AttachmentId, failed ? AttachmentUploadStatus.Failed : AttachmentUploadStatus.Sent, 1f);
            }
            Calls.Add("media-done");
        }

        public IEnumerator SetReporterEmail(string email, Action<string> onError)
        {
            Calls.Add("email:" + email);
            yield return null;
            if (EmailError != null)
            {
                onError(EmailError);
            }
        }

        public IEnumerator Publish(bool emailMyReport, Action<string> onError)
        {
            Calls.Add("publish:" + emailMyReport);
            yield return null;
            if (PublishError != null)
            {
                onError(PublishError);
            }
        }
    }
}
