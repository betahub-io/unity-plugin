using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace BetaHub.Tests
{
    [TestFixture]
    public class BugReportSessionTests
    {
        private const string Description = "It crashed";
        private const string Steps = "1. Do it";

        private FakeBetaHubLog _log;
        private BugReportSession _session;

        private static readonly Issue.LogFileReference PlayerLogReference = new Issue.LogFileReference { path = "/tmp/BH_Player.log" };

        [SetUp]
        public void SetUp()
        {
            _log = new FakeBetaHubLog();
            _session = new BugReportSession(_log);
        }

        // --- recorder policy ---

        [Test]
        public void ShouldRecord_WhenIdleAndVideoEnabled_ReturnsTrue()
        {
            Assert.IsTrue(BugReportSession.ShouldRecord(videoEnabled: true, reportWindowVisible: false, uploadInProgress: false));
        }

        [Test]
        public void ShouldRecord_WhenReportWindowVisible_ReturnsFalse()
        {
            Assert.IsFalse(BugReportSession.ShouldRecord(videoEnabled: true, reportWindowVisible: true, uploadInProgress: false));
        }

        [Test]
        public void ShouldRecord_WhenUploadInProgress_ReturnsFalse()
        {
            Assert.IsFalse(BugReportSession.ShouldRecord(videoEnabled: true, reportWindowVisible: false, uploadInProgress: true));
        }

        [Test]
        public void ShouldRecord_WhenVideoDisabled_ReturnsFalse()
        {
            Assert.IsFalse(BugReportSession.ShouldRecord(videoEnabled: false, reportWindowVisible: false, uploadInProgress: false));
        }

        // --- BuildReport: attachments and toggles ---

        [Test]
        public void BuildReport_CopiesDescriptionAndSteps()
        {
            var draft = _session.BuildReport(Description, Steps, BugReportOptions.All, recorderAvailable: false, playerLog: null);

            Assert.AreEqual(Description, draft.Description);
            Assert.AreEqual(Steps, draft.Steps);
        }

        [Test]
        public void BuildReport_IncludesScreenshotsOnlyWhenEnabled()
        {
            _session.AddScreenshot("/tmp/shot.png", removeAfterUpload: true);

            var included = _session.BuildReport(Description, Steps, BugReportOptions.All, false, null);
            var excluded = _session.BuildReport(Description, Steps, new BugReportOptions { IncludeScreenshots = false, IncludeLogs = true, IncludeRecording = true }, false, null);

            Assert.AreEqual(1, included.Screenshots.Count);
            Assert.AreEqual("/tmp/shot.png", included.Screenshots[0].path);
            Assert.IsTrue(included.Screenshots[0].removeAfterUpload);
            Assert.AreEqual(0, excluded.Screenshots.Count);
        }

        [Test]
        public void BuildReport_IncludesDeveloperLogFilesOnlyWhenLogsEnabled()
        {
            _session.AddLogFile("/tmp/custom.log", removeAfterUpload: false);

            var included = _session.BuildReport(Description, Steps, BugReportOptions.All, false, null);
            var excluded = _session.BuildReport(Description, Steps, new BugReportOptions { IncludeScreenshots = true, IncludeLogs = false, IncludeRecording = true }, false, null);

            Assert.AreEqual(1, included.LogFiles.Count);
            Assert.AreEqual(0, excluded.LogFiles.Count);
        }

        [Test]
        public void BuildReport_IncludesPlayerLogWhenUnderCap()
        {
            var playerLog = new PlayerLogSource(PlayerLogReference, BugReportSession.MaxPlayerLogBytes - 1);

            var draft = _session.BuildReport(Description, Steps, BugReportOptions.All, false, playerLog);

            Assert.AreEqual(1, draft.LogFiles.Count);
            Assert.AreEqual(PlayerLogReference.path, draft.LogFiles[0].path);
        }

        [Test]
        public void BuildReport_ExcludesPlayerLogAtOrOverCap_AndWarns()
        {
            var playerLog = new PlayerLogSource(PlayerLogReference, BugReportSession.MaxPlayerLogBytes);

            var draft = _session.BuildReport(Description, Steps, BugReportOptions.All, false, playerLog);

            Assert.AreEqual(0, draft.LogFiles.Count);
            Assert.AreEqual(1, _log.Warnings.Count);
            StringAssert.Contains("log", _log.Warnings[0].ToLowerInvariant());
        }

        [Test]
        public void BuildReport_ExcludesPlayerLogWhenLogsDisabled()
        {
            var playerLog = new PlayerLogSource(PlayerLogReference, 10);
            var options = new BugReportOptions { IncludeScreenshots = true, IncludeLogs = false, IncludeRecording = true };

            var draft = _session.BuildReport(Description, Steps, options, false, playerLog);

            Assert.AreEqual(0, draft.LogFiles.Count);
        }

        [Test]
        public void BuildReport_IncludesRecordingOnlyWhenEnabledAndRecorderAvailable()
        {
            var withRecorder = _session.BuildReport(Description, Steps, BugReportOptions.All, recorderAvailable: true, playerLog: null);
            var noRecorder = _session.BuildReport(Description, Steps, BugReportOptions.All, recorderAvailable: false, playerLog: null);
            var disabled = _session.BuildReport(Description, Steps, new BugReportOptions { IncludeScreenshots = true, IncludeLogs = true, IncludeRecording = false }, recorderAvailable: true, playerLog: null);

            Assert.IsTrue(withRecorder.IncludeRecording);
            Assert.IsFalse(noRecorder.IncludeRecording);
            Assert.IsFalse(disabled.IncludeRecording);
        }

        [Test]
        public void BuildReport_AlwaysIncludesDeveloperVideos()
        {
            _session.AddVideo("/tmp/clip.mp4", removeAfterUpload: true);
            var options = new BugReportOptions { IncludeScreenshots = false, IncludeLogs = false, IncludeRecording = false };

            var draft = _session.BuildReport(Description, Steps, options, false, null);

            Assert.AreEqual(1, draft.Videos.Count);
        }

        [Test]
        public void ClearAttachments_RemovesEverything()
        {
            _session.AddScreenshot("/tmp/shot.png", true);
            _session.AddLogFile("/tmp/custom.log", true);
            _session.AddVideo("/tmp/clip.mp4", true);

            _session.ClearAttachments();
            var draft = _session.BuildReport(Description, Steps, BugReportOptions.All, false, null);

            Assert.AreEqual(0, draft.Screenshots.Count);
            Assert.AreEqual(0, draft.LogFiles.Count);
            Assert.AreEqual(0, draft.Videos.Count);
        }

        // --- custom fields ---

        [Test]
        public void CollectCustomFields_ProviderOverridesStaticValue()
        {
            _session.SetCustomField("level", "static");
            _session.RegisterCustomFieldProvider("level", () => "dynamic");

            var fields = _session.CollectCustomFields();

            Assert.AreEqual("dynamic", fields["level"]);
        }

        [Test]
        public void CollectCustomFields_ProviderReturningNull_KeepsStaticValue()
        {
            _session.SetCustomField("level", "static");
            _session.RegisterCustomFieldProvider("level", () => null);

            var fields = _session.CollectCustomFields();

            Assert.AreEqual("static", fields["level"]);
        }

        [Test]
        public void CollectCustomFields_ThrowingProvider_IsSkippedAndWarned()
        {
            _session.SetCustomField("ok", "1");
            _session.RegisterCustomFieldProvider("broken", () => throw new InvalidOperationException("boom"));

            var fields = _session.CollectCustomFields();

            Assert.AreEqual(1, fields.Count);
            Assert.AreEqual(1, _log.Warnings.Count);
            StringAssert.Contains("broken", _log.Warnings[0]);
            StringAssert.Contains("boom", _log.Warnings[0]);
        }

        [Test]
        public void SetCustomField_RejectsInvalidInputWithWarning()
        {
            _session.SetCustomField("", "x");
            _session.SetCustomField("name", null);

            Assert.AreEqual(0, _session.CollectCustomFields().Count);
            Assert.AreEqual(2, _log.Warnings.Count);
        }

        [Test]
        public void RemoveCustomField_AndUnregisterProvider_Work()
        {
            _session.SetCustomField("a", "1");
            _session.RegisterCustomFieldProvider("b", () => "2");

            _session.RemoveCustomField("a");
            _session.UnregisterCustomFieldProvider("b");

            Assert.AreEqual(0, _session.CollectCustomFields().Count);
        }

        [Test]
        public void BuildReport_MergesExtraFieldsOverUserFields()
        {
            _session.SetCustomField("country", "user");
            var extra = new Dictionary<string, string> { { "country", "PL" }, { "latency", "20ms" } };

            var draft = _session.BuildReport(Description, Steps, BugReportOptions.All, false, null, extra);

            Assert.AreEqual("PL", draft.CustomFields["country"]);
            Assert.AreEqual("20ms", draft.CustomFields["latency"]);
        }
    }
}
