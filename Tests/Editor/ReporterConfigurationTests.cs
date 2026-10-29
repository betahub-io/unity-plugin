using System.Linq;
using NUnit.Framework;

namespace BetaHub.Tests
{
    [TestFixture]
    public class ReporterConfigurationTests
    {
        private const string Endpoint = "https://app.betahub.io";
        private const string ProjectId = "pr-1";
        private const string ValidToken = "tkn-abc";

        // 1.x kept the project settings on BugReportUI; an upgraded scene still carries them there.
        [Test]
        public void LegacySettings_WinOverTheDemoProject_OrAnEmptyOne()
        {
            Assert.IsTrue(ReporterConfiguration.ShouldAdoptLegacySettings("pr-studio", BetaHubReporter.DEMO_PROJECT_ID));
            Assert.IsTrue(ReporterConfiguration.ShouldAdoptLegacySettings("pr-studio", ""));
        }

        [Test]
        public void LegacySettings_NeverOverrideTheStudiosOwnProject()
        {
            Assert.IsFalse(ReporterConfiguration.ShouldAdoptLegacySettings("pr-old", "pr-new"));
            Assert.IsFalse(ReporterConfiguration.ShouldAdoptLegacySettings("", BetaHubReporter.DEMO_PROJECT_ID));
            Assert.IsFalse(ReporterConfiguration.ShouldAdoptLegacySettings(null, BetaHubReporter.DEMO_PROJECT_ID));
            Assert.IsFalse(ReporterConfiguration.ShouldAdoptLegacySettings(BetaHubReporter.DEMO_PROJECT_ID, BetaHubReporter.DEMO_PROJECT_ID));
        }

        [Test]
        public void Validate_AllGood_ReturnsNoDiagnostics()
        {
            var diagnostics = ReporterConfiguration.Validate(Endpoint, ProjectId, ValidToken, hasDeviceAuth: false, hasRecorder: true);

            Assert.IsEmpty(diagnostics);
        }

        [Test]
        public void Validate_MissingProjectId_IsErrorWithHint()
        {
            var diagnostics = ReporterConfiguration.Validate(Endpoint, "", ValidToken, false, true);

            var error = diagnostics.Single(d => d.Severity == DiagnosticSeverity.Error);
            StringAssert.Contains("Project ID", error.Message);
            StringAssert.Contains("BetaHubReporter", error.Message);
        }

        [Test]
        public void Validate_MissingTokenWithoutDeviceAuth_IsError()
        {
            var diagnostics = ReporterConfiguration.Validate(Endpoint, ProjectId, "", hasDeviceAuth: false, hasRecorder: true);

            Assert.AreEqual(DiagnosticSeverity.Error, diagnostics.Single().Severity);
        }

        [Test]
        public void Validate_MissingTokenWithDeviceAuth_IsWarning()
        {
            var diagnostics = ReporterConfiguration.Validate(Endpoint, ProjectId, "", hasDeviceAuth: true, hasRecorder: true);

            Assert.AreEqual(DiagnosticSeverity.Warning, diagnostics.Single().Severity);
        }

        [Test]
        public void Validate_MalformedToken_IsError()
        {
            var diagnostics = ReporterConfiguration.Validate(Endpoint, ProjectId, "abc", false, true);

            var error = diagnostics.Single();
            Assert.AreEqual(DiagnosticSeverity.Error, error.Severity);
            StringAssert.Contains(ReporterConfiguration.AuthTokenPrefix, error.Message);
        }

        [Test]
        public void Validate_MissingEndpoint_IsError()
        {
            var diagnostics = ReporterConfiguration.Validate("", ProjectId, ValidToken, false, true);

            Assert.AreEqual(DiagnosticSeverity.Error, diagnostics.Single().Severity);
        }

        [Test]
        public void Validate_NoRecorder_IsWarning()
        {
            var diagnostics = ReporterConfiguration.Validate(Endpoint, ProjectId, ValidToken, false, hasRecorder: false);

            var warning = diagnostics.Single();
            Assert.AreEqual(DiagnosticSeverity.Warning, warning.Severity);
            StringAssert.Contains("GameRecorder", warning.Message);
        }

        [Test]
        public void FirstError_ReturnsNullWhenOnlyWarnings()
        {
            var diagnostics = ReporterConfiguration.Validate(Endpoint, ProjectId, ValidToken, false, hasRecorder: false);

            Assert.IsNull(ReporterConfiguration.FirstError(diagnostics));
        }

        [Test]
        public void FirstError_ReturnsErrorMessage()
        {
            var diagnostics = ReporterConfiguration.Validate(Endpoint, "", ValidToken, false, true);

            StringAssert.Contains("Project ID", ReporterConfiguration.FirstError(diagnostics));
        }
    }
}
