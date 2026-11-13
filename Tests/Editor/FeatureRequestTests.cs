using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;

namespace BetaHub.Tests
{
    [TestFixture]
    public class FeatureRequestTests
    {
        private const string Endpoint = "https://app.betahub.io";
        private const string ProjectId = "pr-123";
        private const string Token = "tkn-abc";

        private static FeatureRequest NewClient() => new FeatureRequest(Endpoint, ProjectId, Token);

        private static void Drain(IEnumerator routine)
        {
            while (routine.MoveNext()) { }
        }

        [Test]
        public void ScopedIdFromUrl_IsTheLastSegment()
        {
            Assert.AreEqual("45", FeatureRequest.ScopedIdFromUrl("https://app.betahub.io/projects/pr-123/feature_requests/45"));
        }

        [Test]
        public void ScopedIdFromUrl_NoSegment_IsNull()
        {
            Assert.IsNull(FeatureRequest.ScopedIdFromUrl(null));
            Assert.IsNull(FeatureRequest.ScopedIdFromUrl(""));
            Assert.IsNull(FeatureRequest.ScopedIdFromUrl("https://app.betahub.io/projects/pr-123/feature_requests/"));
            Assert.IsNull(FeatureRequest.ScopedIdFromUrl("no-slash"));
        }

        [Test]
        public void Constructor_RequiresEndpointProjectAndToken()
        {
            Assert.Throws<ArgumentException>(() => new FeatureRequest("", ProjectId, Token));
            Assert.Throws<ArgumentException>(() => new FeatureRequest(Endpoint, "", Token));
            Assert.Throws<ArgumentException>(() => new FeatureRequest(Endpoint, ProjectId, ""));
        }

        [Test]
        public void CreateDraft_RefusesAnythingButTheScreenshot()
        {
            var draft = new BugReportDraft { Description = "An idea" };
            draft.Uploads.Add(new ReportUpload { AttachmentId = BuiltInAttachments.PlayerLog, Kind = UploadKind.Log, Path = "/tmp/a.log" });

            Assert.Throws<ArgumentException>(() => Drain(NewClient().CreateDraft(draft, ReleaseTarget.Latest, _ => { })));
        }

        [Test]
        public void UploadMedia_AScreenshotThatDidNotGoOut_Failed()
        {
            var draft = new BugReportDraft { Description = "An idea" };
            draft.Uploads.Add(new ReportUpload { AttachmentId = BuiltInAttachments.Screenshot, Kind = UploadKind.Screenshot, Path = "/tmp/shot.png" });
            var reports = new List<(string, AttachmentUploadStatus)>();

            Drain(NewClient().UploadMedia(draft, (id, status, _) => reports.Add((id, status))));

            CollectionAssert.AreEqual(new[] { (BuiltInAttachments.Screenshot, AttachmentUploadStatus.Failed) }, reports);
        }

        [Test]
        public void SetReporterEmail_IsRefused()
        {
            string error = null;

            Drain(NewClient().SetReporterEmail("a@b.c", e => error = e));

            Assert.AreEqual(FeatureRequest.NoEmailError, error);
        }

        [Test]
        public void Publish_BeforeTheDraft_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => Drain(NewClient().Publish(false, _ => { })));
        }
    }
}
