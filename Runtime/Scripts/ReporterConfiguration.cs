using System.Collections.Generic;

namespace BetaHub
{
    public enum DiagnosticSeverity
    {
        Info,
        Warning,
        Error,
    }

    public struct ReporterDiagnostic
    {
        public DiagnosticSeverity Severity;
        public string Message;

        public ReporterDiagnostic(DiagnosticSeverity severity, string message)
        {
            Severity = severity;
            Message = message;
        }
    }

    /// <summary>Startup validation of <see cref="BetaHubReporter"/> settings, kept pure for testing.</summary>
    public static class ReporterConfiguration
    {
        public const string AuthTokenPrefix = "tkn-";
        private const string ComponentName = nameof(BetaHubReporter);

        public static List<ReporterDiagnostic> Validate(string endpoint, string projectId, string authToken,
            bool hasDeviceAuth, bool hasRecorder)
        {
            var diagnostics = new List<ReporterDiagnostic>();

            if (string.IsNullOrEmpty(endpoint))
            {
                diagnostics.Add(new ReporterDiagnostic(DiagnosticSeverity.Error,
                    $"Submit Endpoint is not set. Set it on the {ComponentName} component (default: https://app.betahub.io)."));
            }

            if (string.IsNullOrEmpty(projectId))
            {
                diagnostics.Add(new ReporterDiagnostic(DiagnosticSeverity.Error,
                    $"Project ID is not set. Copy it from your BetaHub project settings into the {ComponentName} component. Reports cannot be submitted."));
            }

            if (string.IsNullOrEmpty(authToken))
            {
                if (hasDeviceAuth)
                {
                    diagnostics.Add(new ReporterDiagnostic(DiagnosticSeverity.Warning,
                        $"Auth Token is not set. Reports will only be submitted while the user is signed in through device authentication. Set a token on the {ComponentName} component to allow anonymous reports."));
                }
                else
                {
                    diagnostics.Add(new ReporterDiagnostic(DiagnosticSeverity.Error,
                        $"Auth Token is not set and no DeviceAuthManager is assigned. Set a token on the {ComponentName} component or assign a DeviceAuthManager. Reports cannot be submitted."));
                }
            }
            else if (!authToken.StartsWith(AuthTokenPrefix))
            {
                diagnostics.Add(new ReporterDiagnostic(DiagnosticSeverity.Error,
                    $"Auth Token must start with '{AuthTokenPrefix}'. Use a project auth token from BetaHub, not a personal or JWT token. Reports cannot be submitted."));
            }

            if (!hasRecorder)
            {
                diagnostics.Add(new ReporterDiagnostic(DiagnosticSeverity.Warning,
                    $"No GameRecorder component on the {ComponentName} GameObject. Reports will not include video. Add a GameRecorder to enable it."));
            }

            return diagnostics;
        }

        /// <returns>The first error message, or null when the configuration is usable.</returns>
        public static string FirstError(IEnumerable<ReporterDiagnostic> diagnostics)
        {
            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    return diagnostic.Message;
                }
            }
            return null;
        }
    }
}
