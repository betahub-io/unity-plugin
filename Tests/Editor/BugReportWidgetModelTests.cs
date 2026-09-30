using NUnit.Framework;

namespace BetaHub.Tests
{
    [TestFixture]
    public class BugReportWidgetModelTests
    {
        // --- which screen for which submission state ---

        // A plain test with a table: NUnit3TestAdapter 3.11 drops [TestCase] methods with enum arguments.
        [Test]
        public void ScreenFor_MapsEverySubmissionState()
        {
            var expected = new[]
            {
                (ReportSubmissionState.Preparing, WidgetScreen.Sending),
                (ReportSubmissionState.CreatingDraft, WidgetScreen.Sending),
                (ReportSubmissionState.UploadingMedia, WidgetScreen.Uploading),
                (ReportSubmissionState.AwaitingPublish, WidgetScreen.Sent),
                (ReportSubmissionState.Publishing, WidgetScreen.Sent),
                (ReportSubmissionState.Published, WidgetScreen.Sent),
                (ReportSubmissionState.Failed, WidgetScreen.Compose),
            };
            foreach (var (state, screen) in expected)
            {
                Assert.AreEqual(screen, BugReportWidgetModel.ScreenFor(state), state.ToString());
            }
            Assert.AreEqual(expected.Length, System.Enum.GetValues(typeof(ReportSubmissionState)).Length, "every state is covered");
        }

        // --- send button gating ---

        [Test]
        public void CanSend_RequiresNonBlankDescription()
        {
            Assert.IsFalse(BugReportWidgetModel.CanSend("", termsRequired: false, termsAccepted: false, configured: true));
            Assert.IsFalse(BugReportWidgetModel.CanSend("   \n", false, false, true));
            Assert.IsTrue(BugReportWidgetModel.CanSend("It broke", false, false, true));
        }

        [Test]
        public void CanSend_TermsGateOnlyWhenRequired()
        {
            Assert.IsFalse(BugReportWidgetModel.CanSend("It broke", termsRequired: true, termsAccepted: false, configured: true));
            Assert.IsTrue(BugReportWidgetModel.CanSend("It broke", termsRequired: true, termsAccepted: true, configured: true));
        }

        [Test]
        public void CanSend_NeverWhenNotConfigured()
        {
            Assert.IsFalse(BugReportWidgetModel.CanSend("It broke", false, false, configured: false));
        }

        // --- text helpers ---

        [Test]
        public void FormatCounter_ShowsUsedOverLimit()
        {
            Assert.AreEqual("102 / 2000", BugReportWidgetModel.FormatCounter(102, 2000));
        }

        [Test]
        public void FormatDuration_UsesMinutesAndSeconds()
        {
            Assert.AreEqual("1:00", BugReportWidgetModel.FormatDuration(60));
            Assert.AreEqual("0:45", BugReportWidgetModel.FormatDuration(45));
            Assert.AreEqual("2:05", BugReportWidgetModel.FormatDuration(125));
        }

        [Test]
        public void FormatFileSize_PicksUnit()
        {
            Assert.AreEqual("312 KB", BugReportWidgetModel.FormatFileSize(312L * 1024));
            Assert.AreEqual("1.2 MB", BugReportWidgetModel.FormatFileSize((long)(1.2 * 1024 * 1024)));
            Assert.AreEqual("900 B", BugReportWidgetModel.FormatFileSize(900));
        }

        [Test]
        public void LogTail_ReturnsLastNonEmptyLines()
        {
            string log = "a\nb\n\nc\nd\n";
            Assert.AreEqual("b\nc\nd", BugReportWidgetModel.LogTail(log, 3));
        }

        [Test]
        public void LogTail_HandlesShortAndEmptyInput()
        {
            Assert.AreEqual("only", BugReportWidgetModel.LogTail("only", 4));
            Assert.AreEqual("", BugReportWidgetModel.LogTail("", 4));
            Assert.AreEqual("", BugReportWidgetModel.LogTail(null, 4));
        }

        [Test]
        public void Truncate_KeepsWithinLimitWithEllipsis()
        {
            Assert.AreEqual("abcdef", BugReportWidgetModel.Truncate("abcdef", 6));
            Assert.AreEqual("abcde…", BugReportWidgetModel.Truncate("abcdefgh", 6));
        }

        // --- header text for the compose screen ---

        [Test]
        public void ComposeHeader_DefaultIsTitleOnly()
        {
            var header = BugReportWidgetModel.ComposeHeader(configured: true, isDevelopmentBuild: false, configurationError: null, submissionError: null, signedInAs: null);
            Assert.AreEqual(BugReportWidgetModel.HeaderKind.Normal, header.Kind);
            Assert.IsNull(header.Subtitle);
        }

        [Test]
        public void ComposeHeader_SignedInShowsReporterName()
        {
            var header = BugReportWidgetModel.ComposeHeader(true, false, null, null, signedInAs: "Kasia_P");
            Assert.AreEqual(BugReportWidgetModel.HeaderKind.Normal, header.Kind);
            StringAssert.Contains("Kasia_P", header.Subtitle);
        }

        [Test]
        public void ComposeHeader_SubmissionErrorSwapsTitle()
        {
            var header = BugReportWidgetModel.ComposeHeader(true, false, null, submissionError: "No connection", signedInAs: null);
            Assert.AreEqual(BugReportWidgetModel.HeaderKind.Error, header.Kind);
            StringAssert.Contains("No connection", header.Subtitle);
        }

        [Test]
        public void ComposeHeader_MisconfigurationIsDevOnlyDetail()
        {
            var dev = BugReportWidgetModel.ComposeHeader(configured: false, isDevelopmentBuild: true, configurationError: "Project ID is not set. Fix it.", submissionError: null, signedInAs: null);
            var player = BugReportWidgetModel.ComposeHeader(configured: false, isDevelopmentBuild: false, configurationError: "Project ID is not set. Fix it.", submissionError: null, signedInAs: null);

            Assert.AreEqual(BugReportWidgetModel.HeaderKind.Warning, dev.Kind);
            StringAssert.Contains("Project ID", dev.Subtitle);
            Assert.AreEqual(BugReportWidgetModel.HeaderKind.Warning, player.Kind);
            StringAssert.DoesNotContain("Project ID", player.Subtitle);
        }
    }
}
