using System;
using System.Collections;
using System.Collections.Generic;

namespace BetaHub.Tests
{
    public class FakeIssueClient : IIssueClient
    {
        public const string CreatedIssueId = "12345";
        public const string CreatedIssueUrl = "https://app.betahub.io/issues/12345";

        public string DraftError;
        public string PublishError;
        public string EmailError;
        public bool HoldMedia;

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

        public IEnumerator UploadMedia(BugReportDraft draft)
        {
            Calls.Add("media");
            while (HoldMedia)
            {
                yield return null;
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
