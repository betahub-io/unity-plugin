using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;

namespace BetaHub.Tests
{
    [TestFixture]
    public class ReportSubmissionTests
    {
        private const string Email = "tester@example.com";

        private FakeBetaHubLog _log;
        private FakeIssueClient _client;
        private ManualCoroutineRunner _runner;
        private ReportSubmission _submission;
        private List<ReportSubmissionState> _observedStates;

        [SetUp]
        public void SetUp()
        {
            _log = new FakeBetaHubLog();
            _client = new FakeIssueClient();
            _runner = new ManualCoroutineRunner();
            _submission = new ReportSubmission(_client, _runner, _log);
            _observedStates = new List<ReportSubmissionState>();
            _submission.StateChanged += s => _observedStates.Add(s.State);
        }

        private static BugReportDraft EmptyDraft() => new BugReportDraft { Description = "d", Steps = "s" };

        private void Start(MediaUploadType uploadType = MediaUploadType.UploadInBackground, bool autoPublish = false)
        {
            _submission.Start(EmptyDraft(), ReleaseTarget.Latest, uploadType, autoPublish);
        }

        [Test]
        public void NewSubmission_IsPreparing()
        {
            Assert.AreEqual(ReportSubmissionState.Preparing, _submission.State);
            Assert.IsNull(_submission.IssueId);
        }

        [Test]
        public void CreateFailed_IsFailedWithErrorAndLogs()
        {
            var failed = ReportSubmission.CreateFailed("Project ID is not set", _log);

            Assert.AreEqual(ReportSubmissionState.Failed, failed.State);
            Assert.AreEqual("Project ID is not set", failed.Error);
            Assert.AreEqual(1, _log.Errors.Count);
        }

        [Test]
        public void BackgroundUpload_ReachesAwaitingPublishBeforeMediaCompletes()
        {
            _client.HoldMedia = true;
            Start();
            _runner.Step(); // CreateDraft yields
            _runner.Step(); // CreateDraft completes, UploadMedia starts
            _runner.Step();

            Assert.AreEqual(ReportSubmissionState.AwaitingPublish, _submission.State);
            Assert.AreEqual(FakeIssueClient.CreatedIssueId, _submission.IssueId);
            Assert.IsFalse(_submission.IsMediaUploadComplete);
        }

        [Test]
        public void WaitForUpload_StaysInUploadingMediaUntilMediaCompletes()
        {
            _client.HoldMedia = true;
            Start(MediaUploadType.WaitForUpload);
            for (int i = 0; i < 5; i++) _runner.Step();

            Assert.AreEqual(ReportSubmissionState.UploadingMedia, _submission.State);

            _client.HoldMedia = false;
            _runner.RunUntilIdle();

            Assert.AreEqual(ReportSubmissionState.AwaitingPublish, _submission.State);
            Assert.IsTrue(_submission.IsMediaUploadComplete);
        }

        [Test]
        public void Publish_AfterMediaComplete_PublishesImmediately()
        {
            Start();
            _runner.RunUntilIdle();
            Assert.AreEqual(ReportSubmissionState.AwaitingPublish, _submission.State);

            _submission.Publish(emailMyReport: true);
            _runner.RunUntilIdle();

            Assert.AreEqual(ReportSubmissionState.Published, _submission.State);
            CollectionAssert.Contains(_client.Calls, "publish:True");
        }

        [Test]
        public void Publish_BeforeMediaComplete_WaitsForMedia()
        {
            _client.HoldMedia = true;
            Start();
            for (int i = 0; i < 3; i++) _runner.Step();
            Assert.AreEqual(ReportSubmissionState.AwaitingPublish, _submission.State);

            _submission.Publish(false);
            for (int i = 0; i < 5; i++) _runner.Step();

            Assert.AreEqual(ReportSubmissionState.Publishing, _submission.State);
            CollectionAssert.DoesNotContain(_client.Calls, "publish:False");

            _client.HoldMedia = false;
            _runner.RunUntilIdle();

            Assert.AreEqual(ReportSubmissionState.Published, _submission.State);
            Assert.Less(_client.Calls.IndexOf("media-done"), _client.Calls.IndexOf("publish:False"));
        }

        [Test]
        public void AutoPublish_SkipsAwaitingPublish()
        {
            Start(autoPublish: true);
            _runner.RunUntilIdle();

            Assert.AreEqual(ReportSubmissionState.Published, _submission.State);
            CollectionAssert.DoesNotContain(_observedStates, ReportSubmissionState.AwaitingPublish);
            CollectionAssert.Contains(_client.Calls, "publish:False");
        }

        [Test]
        public void DraftFailure_EndsInFailedWithErrorAndLog()
        {
            _client.DraftError = "Invalid token";
            Start();
            _runner.RunUntilIdle();

            Assert.AreEqual(ReportSubmissionState.Failed, _submission.State);
            Assert.AreEqual("Invalid token", _submission.Error);
            CollectionAssert.DoesNotContain(_client.Calls, "media");
            Assert.AreEqual(1, _log.Errors.Count);
        }

        [Test]
        public void PublishFailure_EndsInFailed()
        {
            _client.PublishError = "Server down";
            Start();
            _runner.RunUntilIdle();
            _submission.Publish(false);
            _runner.RunUntilIdle();

            Assert.AreEqual(ReportSubmissionState.Failed, _submission.State);
            Assert.AreEqual("Server down", _submission.Error);
        }

        [Test]
        public void Publish_BeforeDraftExists_IsRejectedWithWarning()
        {
            _submission.Publish(false);
            _runner.RunUntilIdle();

            Assert.AreEqual(ReportSubmissionState.Preparing, _submission.State);
            Assert.AreEqual(1, _log.Warnings.Count);
            CollectionAssert.IsEmpty(_client.Calls);
        }

        [Test]
        public void Publish_Twice_SecondIsIgnoredWithWarning()
        {
            Start();
            _runner.RunUntilIdle();
            _submission.Publish(false);
            _submission.Publish(false);
            _runner.RunUntilIdle();

            Assert.AreEqual(1, _client.Calls.FindAll(c => c.StartsWith("publish")).Count);
            Assert.AreEqual(1, _log.Warnings.Count);
        }

        [Test]
        public void SetReporterEmail_ThenPublish_RunsInOrder()
        {
            Start();
            _runner.RunUntilIdle();

            _submission.SetReporterEmail(Email);
            _submission.Publish(true);
            _runner.RunUntilIdle();

            Assert.AreEqual(ReportSubmissionState.Published, _submission.State);
            Assert.Less(_client.Calls.IndexOf("email:" + Email), _client.Calls.IndexOf("publish:True"));
        }

        [Test]
        public void SetReporterEmail_Failure_RaisesEventAndLogsError_ButDoesNotFailSubmission()
        {
            _client.EmailError = "Invalid email";
            string reported = null;
            _submission.ReporterEmailFailed += e => reported = e;
            Start();
            _runner.RunUntilIdle();

            _submission.SetReporterEmail(Email);
            _runner.RunUntilIdle();

            Assert.AreEqual("Invalid email", reported);
            Assert.AreEqual(1, _log.Errors.Count);
            Assert.AreEqual(ReportSubmissionState.AwaitingPublish, _submission.State);
        }

        [Test]
        public void SetReporterEmail_BeforeDraftExists_IsRejectedWithWarning()
        {
            _submission.SetReporterEmail(Email);
            _runner.RunUntilIdle();

            CollectionAssert.IsEmpty(_client.Calls);
            Assert.AreEqual(1, _log.Warnings.Count);
        }

        [Test]
        public void Fail_FromOutside_MovesToFailedAndLogs()
        {
            _submission.Fail("Exception in provider");

            Assert.AreEqual(ReportSubmissionState.Failed, _submission.State);
            Assert.AreEqual("Exception in provider", _submission.Error);
            Assert.AreEqual(1, _log.Errors.Count);
        }

        [Test]
        public void StateChanged_FiresForEveryTransition()
        {
            Start();
            _runner.RunUntilIdle();
            _submission.Publish(false);
            _runner.RunUntilIdle();

            CollectionAssert.AreEqual(new[]
            {
                ReportSubmissionState.CreatingDraft,
                ReportSubmissionState.AwaitingPublish,
                ReportSubmissionState.Publishing,
                ReportSubmissionState.Published,
            }, _observedStates);
        }

        [Test]
        public void Start_Twice_IsRejectedWithWarning()
        {
            Start();
            Start();
            _runner.RunUntilIdle();

            Assert.AreEqual(1, _client.Calls.FindAll(c => c == "draft").Count);
            Assert.AreEqual(1, _log.Warnings.Count);
        }

        // --- per-attachment upload state ---

        private const string ShotId = BuiltInAttachments.Screenshot;
        private const string ClipId = BuiltInAttachments.Recording;
        private const string LocationId = BuiltInAttachments.Location;

        private static BugReportDraft DraftWithAttachments()
        {
            var draft = EmptyDraft();
            draft.Uploads.Add(new ReportUpload { AttachmentId = ShotId, Kind = UploadKind.Screenshot, Path = "/tmp/s.png" });
            draft.Uploads.Add(new ReportUpload { AttachmentId = ClipId, Kind = UploadKind.Recording });
            draft.InlineAttachmentIds.Add(LocationId);
            return draft;
        }

        private AttachmentUploadState StateOf(string id)
        {
            Assert.IsTrue(_submission.TryGetUploadState(id, out var state), $"no upload state for {id}");
            return state;
        }

        [Test]
        public void Start_MarksEveryIncludedAttachmentQueued()
        {
            _submission.Start(DraftWithAttachments(), ReleaseTarget.Latest, MediaUploadType.UploadInBackground, false);

            foreach (var id in new[] { ShotId, ClipId, LocationId })
            {
                Assert.AreEqual(AttachmentUploadStatus.Queued, StateOf(id).Status, id);
                Assert.AreEqual(0f, StateOf(id).Progress, id);
            }
            Assert.AreEqual(0f, _submission.UploadProgress);
        }

        [Test]
        public void TryGetUploadState_UnknownId_ReturnsFalse()
        {
            _submission.Start(DraftWithAttachments(), ReleaseTarget.Latest, MediaUploadType.UploadInBackground, false);

            Assert.IsFalse(_submission.TryGetUploadState("left-out", out _));
            Assert.IsFalse(_submission.TryGetUploadState(null, out _));
        }

        [Test]
        public void InlineAttachments_AreSentWithTheDraft()
        {
            _client.HoldMedia = true;
            _submission.Start(DraftWithAttachments(), ReleaseTarget.Latest, MediaUploadType.UploadInBackground, false);
            for (int i = 0; i < 3; i++) _runner.Step();

            Assert.AreEqual(AttachmentUploadStatus.Sent, StateOf(LocationId).Status);
            Assert.AreEqual(AttachmentUploadStatus.Queued, StateOf(ShotId).Status);
        }

        [Test]
        public void UploadProgress_FollowsTheClient_ItemByItem()
        {
            var seen = new List<string>();
            _submission.AttachmentUploadChanged += (s, id) =>
            {
                var st = StateOf(id);
                seen.Add($"{id}:{st.Status}:{st.Progress.ToString(CultureInfo.InvariantCulture)}");
            };
            _submission.Start(DraftWithAttachments(), ReleaseTarget.Latest, MediaUploadType.UploadInBackground, false);
            _runner.RunUntilIdle();

            CollectionAssert.AreEqual(new[]
            {
                $"{LocationId}:Sent:1",
                $"{ShotId}:Uploading:0",
                $"{ShotId}:Uploading:0.5",
                $"{ShotId}:Sent:1",
                $"{ClipId}:Uploading:0",
                $"{ClipId}:Uploading:0.5",
                $"{ClipId}:Sent:1",
            }, seen);
            Assert.AreEqual(1f, _submission.UploadProgress);
        }

        [Test]
        public void UploadProgress_AveragesAllItems()
        {
            _client.HoldAfterFirstHalf = true;
            _submission.Start(DraftWithAttachments(), ReleaseTarget.Latest, MediaUploadType.UploadInBackground, false);
            for (int i = 0; i < 10; i++) _runner.Step();

            // location sent (1), screenshot halfway (0.5), clip queued (0)
            Assert.AreEqual(AttachmentUploadStatus.Uploading, StateOf(ShotId).Status);
            Assert.AreEqual(0.5f, _submission.UploadProgress, 0.001f);
        }

        [Test]
        public void FailedUpload_MarksTheItem_ButTheReportCompletes()
        {
            _client.FailUploads.Add(ShotId);
            _submission.Start(DraftWithAttachments(), ReleaseTarget.Latest, MediaUploadType.UploadInBackground, autoPublish: true);
            _runner.RunUntilIdle();

            Assert.AreEqual(AttachmentUploadStatus.Failed, StateOf(ShotId).Status);
            Assert.AreEqual(AttachmentUploadStatus.Sent, StateOf(ClipId).Status);
            Assert.AreEqual(ReportSubmissionState.Published, _submission.State);
            Assert.AreEqual(1f, _submission.UploadProgress, "a failed item is finished, not pending");
        }

        [Test]
        public void UploadTheClientNeverReported_IsMarkedFailedWithWarning()
        {
            _client.SilentUploads.Add(ClipId);
            _submission.Start(DraftWithAttachments(), ReleaseTarget.Latest, MediaUploadType.UploadInBackground, false);
            _runner.RunUntilIdle();

            Assert.AreEqual(AttachmentUploadStatus.Failed, StateOf(ClipId).Status);
            Assert.AreEqual(1, _log.Warnings.Count);
            StringAssert.Contains(ClipId, _log.Warnings[0]);
        }

        [Test]
        public void ProgressForAnUnknownId_IsIgnoredWithWarning()
        {
            _client.ExtraProgressId = "ghost";
            _submission.Start(DraftWithAttachments(), ReleaseTarget.Latest, MediaUploadType.UploadInBackground, false);
            _runner.RunUntilIdle();

            Assert.IsFalse(_submission.TryGetUploadState("ghost", out _));
            Assert.AreEqual(1, _log.Warnings.Count);
            StringAssert.Contains("ghost", _log.Warnings[0]);
        }

        [Test]
        public void NoAttachments_ProgressIsCompleteOnceMediaFinishes()
        {
            Start();
            Assert.AreEqual(0f, _submission.UploadProgress);

            _runner.RunUntilIdle();

            Assert.AreEqual(1f, _submission.UploadProgress);
        }
    }
}
