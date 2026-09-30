using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BetaHub
{
    public enum MediaUploadType
    {
        /// <summary>The media will be uploaded in the background without blocking the process</summary>
        UploadInBackground,
        /// <summary>The process will wait until the media has finished uploading before continuing</summary>
        WaitForUpload
    }

    /// <summary>
    /// Headless bug reporting entry point. Holds configuration, owns the recorder policy and the
    /// logger, and turns <see cref="Submit"/> calls into <see cref="ReportSubmission"/> handles.
    /// Any UI, the shipped one or your own, talks to this component.
    /// </summary>
    [RequireComponent(typeof(CustomFieldValidator))]
    [AddComponentMenu("BetaHub/BetaHub Reporter")]
    public class BetaHubReporter : MonoBehaviour, ICoroutineRunner
    {
        public const string DEMO_PROJECT_ID = "pr-5287510306";
        private const string DefaultEndpoint = "https://app.betahub.io";
        private const string GeolocationCountryField = "country";
        private const string GeolocationAsnField = "asn";
        private const string LatencyField = "latency";

        private static BetaHubReporter _instance;
        private static Logger _logger;

        public static Logger Logger => _logger;

        [Tooltip("BetaHub server URL.")]
        public string SubmitEndpoint = DefaultEndpoint;

        [Tooltip("Project ID from your BetaHub project settings, e.g. pr-1234567890.")]
        public string ProjectID;

        [Tooltip("Project auth token, starts with tkn-. Optional when device authentication is used.")]
        public string AuthToken;

        [Tooltip("Upload in background : The media will be uploaded in the background without blocking the process" +
            " \n Wait for upload : The process will wait until the media has finished uploading before continuing")]
        public MediaUploadType MediaUploadType;

        [Tooltip("The release ID to associate the bug report with. If not set (0), the bug report will be " +
            "associated with the latest release. Requires the AuthToken to have a permission of creating new releases.")]
        public int ReleaseId = 0;

        [Tooltip("The release label to associate the bug report with. If not created yet on the backend, " +
            "a new release will be created with this label. Requires the AuthToken to have a permission of creating new releases. " +
            "If not set (null), the bug report will be associated with the latest release.")]
        public string ReleaseLabel = "";

        [Tooltip("Attach the plugin's own player log to reports.")]
        public bool IncludePlayerLog = true;

        [Tooltip("Record gameplay video and attach it to reports. Requires a GameRecorder on this GameObject.")]
        public bool IncludeVideo = true;

        // Pre-filled programmatically by the developer if the user is already known by email.
        [HideInInspector]
        public string DefaultEmailAddress;

        [Tooltip("Device Authentication (Optional). If set, the user may be authenticated via device auth.")]
        [SerializeField] private DeviceAuthManager deviceAuthManager;

        [Tooltip("Geolocation Provider (Optional). If set, location data will be included in bug reports.")]
        [SerializeField] private GeolocationProvider geolocationProvider;

        [Tooltip("Latency Provider (Optional). If set, network latency data will be included in bug reports.")]
        [SerializeField] private LatencyProvider latencyProvider;

        private IBetaHubLog _log;
        private BugReportSession _session;
        private GameRecorder _recorder;
        private CustomFieldValidator _customFieldValidator;
        private string _configurationError;
        private bool _reportWindowVisible;
        private readonly List<ReportSubmission> _submissions = new List<ReportSubmission>();

        /// <summary>True while the user is signed in through device authentication.</summary>
        public bool IsUserAuthenticated => deviceAuthManager != null && deviceAuthManager.IsAuthenticated();

        /// <summary>Email to pre-fill for anonymous reporters. Null when the backend already knows the user.</summary>
        public string EffectiveEmailAddress => IsUserAuthenticated ? null : DefaultEmailAddress;

        public bool HasRecorder => _recorder != null;
        public GameRecorder Recorder => _recorder;
        public bool IsReportWindowVisible => _reportWindowVisible;
        public bool HasActiveUploads => _submissions.Exists(s => s.IsUploadInProgress);
        public bool IsConfigured => _configurationError == null;
        /// <summary>First fatal configuration problem found at startup, or null.</summary>
        public string ConfigurationError => _configurationError;
        /// <summary>Display name of the signed-in user, or null when anonymous.</summary>
        public string UserDisplayName => IsUserAuthenticated ? deviceAuthManager.UserDisplayName : null;

#if !DISABLE_BETAHUB_LOGGER
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitializeLogger()
        {
            _logger = new Logger();
        }
#endif

        public static void PauseLogger()
        {
#if !DISABLE_BETAHUB_LOGGER
            _logger?.PauseLogging();
#endif
        }

        public static void ResumeLogger()
        {
#if !DISABLE_BETAHUB_LOGGER
            _logger?.ResumeLogging();
#endif
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            _log = new UnityBetaHubLog(this);
            _session = new BugReportSession(_log);
        }

        void Start()
        {
            if (_session == null) return; // duplicate instance scheduled for destruction in Awake

            _recorder = GetComponent<GameRecorder>();
            _customFieldValidator = GetComponent<CustomFieldValidator>();

            var diagnostics = ReporterConfiguration.Validate(SubmitEndpoint, ProjectID, AuthToken,
                hasDeviceAuth: deviceAuthManager != null, hasRecorder: _recorder != null);
            foreach (var diagnostic in diagnostics)
            {
                Report(diagnostic);
            }
            _configurationError = ReporterConfiguration.FirstError(diagnostics);

#if DISABLE_BETAHUB_LOGGER
            _log.Warning("DISABLE_BETAHUB_LOGGER is defined. Player logs will not be captured for bug reports. Remove the scripting define symbol to enable logging.");
#endif

            ValidateProviderCustomFields();
            DontDestroyOnLoad(gameObject);
        }

        void Update()
        {
            if (_recorder == null) return;

            bool shouldRecord = BugReportSession.ShouldRecord(IncludeVideo, _reportWindowVisible, HasActiveUploads);
            if (shouldRecord && (!_recorder.IsRecording || _recorder.IsPaused))
            {
                _recorder.StartRecording();
            }
            else if (!shouldRecord && _recorder.IsRecording && !_recorder.IsPaused)
            {
                _recorder.PauseRecording();
            }
        }

        // --- UI hooks ---

        /// <summary>Call when your report form becomes visible. Pauses recording and starts latency sampling.</summary>
        public void NotifyReportWindowShown()
        {
            _reportWindowVisible = true;
            if (latencyProvider != null && latencyProvider.EnableLatency)
            {
                latencyProvider.StartLatencyTest();
            }
        }

        /// <summary>Call when your report form is hidden again. Recording resumes if allowed.</summary>
        public void NotifyReportWindowHidden()
        {
            _reportWindowVisible = false;
        }

        // --- attachments and custom fields ---

        public void AddScreenshot(string path, bool removeAfterUpload) => _session.AddScreenshot(path, removeAfterUpload);
        public void AddLogFile(string path, bool removeAfterUpload) => _session.AddLogFile(path, removeAfterUpload);
        /// <summary>Adds a video to the next report. Always uploaded, regardless of the recording option.</summary>
        public void AddVideo(string path, bool removeAfterUpload) => _session.AddVideo(path, removeAfterUpload);
        public void ClearAttachments() => _session.ClearAttachments();

        /// <summary>Static custom field included in every report. Must be tester_settable in your project.</summary>
        public void SetCustomField(string name, string value) => _session.SetCustomField(name, value);
        public void RemoveCustomField(string name) => _session.RemoveCustomField(name);
        /// <summary>Custom field evaluated at submit time. Overrides a static field of the same name.</summary>
        public void RegisterCustomFieldProvider(string name, Func<string> valueGetter) => _session.RegisterCustomFieldProvider(name, valueGetter);
        public void UnregisterCustomFieldProvider(string name) => _session.UnregisterCustomFieldProvider(name);

        // --- providers ---

        public void SetDeviceAuthManager(DeviceAuthManager authManager) => deviceAuthManager = authManager;
        public void SetGeolocationProvider(GeolocationProvider provider) => geolocationProvider = provider;
        public void SetLatencyProvider(LatencyProvider provider) => latencyProvider = provider;

        // --- submission ---

        /// <summary>
        /// Starts a bug report submission. Returns immediately with a handle; subscribe to
        /// <see cref="ReportSubmission.StateChanged"/> to follow progress. When the configuration is
        /// unusable the handle is already in the Failed state and carries the reason.
        /// </summary>
        public ReportSubmission Submit(string description, string steps, BugReportOptions options)
        {
            if (_configurationError != null)
            {
                return ReportSubmission.CreateFailed(_configurationError, _log);
            }

            string authToken = GetEffectiveAuthToken();
            if (string.IsNullOrEmpty(authToken))
            {
                return ReportSubmission.CreateFailed(
                    "No auth token available. Set Auth Token on the BetaHubReporter component or sign the user in through device authentication.", _log);
            }

            if (HasActiveUploads)
            {
                _log.Info("A previous report is still uploading. Recording stays paused until it finishes.");
            }

            var client = new Issue(SubmitEndpoint, ProjectID, authToken, options.IncludeRecording ? _recorder : null);
            var submission = new ReportSubmission(client, this, _log);
            submission.DraftCreated += _ => _session.ClearAttachments();
            _submissions.Add(submission);
            _submissions.RemoveAll(s => s.IsFinished);

            StartCoroutine(PrepareAndStart(submission, description, steps, options));
            return submission;
        }

        private IEnumerator PrepareAndStart(ReportSubmission submission, string description, string steps, BugReportOptions options)
        {
            var extraFields = new Dictionary<string, string>();
            yield return CollectGeolocation(extraFields);
            CollectLatency(extraFields);

            BugReportDraft draft;
            try
            {
                var effectiveOptions = options;
                effectiveOptions.IncludeRecording = options.IncludeRecording && IncludeVideo;
                draft = _session.BuildReport(description, steps, effectiveOptions, _recorder != null, GetPlayerLogSource(), extraFields);
            }
            catch (Exception ex)
            {
                submission.Fail("Could not build the report: " + ex.Message);
                yield break;
            }

            if (draft.CustomFields.Count > 0)
            {
                _log.Info("Custom fields: " + string.Join(", ", FormatFields(draft.CustomFields)));
            }

            var release = new ReleaseTarget { Id = ReleaseId, Label = ReleaseLabel };
            submission.Start(draft, release, MediaUploadType, autoPublish: IsUserAuthenticated);
        }

        private PlayerLogSource GetPlayerLogSource()
        {
            if (!IncludePlayerLog || _logger == null || string.IsNullOrEmpty(_logger.LogPath) || !File.Exists(_logger.LogPath))
            {
                return null;
            }
            var reference = new Issue.LogFileReference { logger = _logger, path = _logger.LogPath, removeAfterUpload = false };
            return new PlayerLogSource(reference, new FileInfo(_logger.LogPath).Length);
        }

        private IEnumerator CollectGeolocation(Dictionary<string, string> fields)
        {
            if (geolocationProvider == null || !(geolocationProvider.EnableGeolocation || geolocationProvider.EnableAsnCollection))
            {
                yield break;
            }

            bool done = false;
            yield return geolocationProvider.GetLocationDataAsync(
                data =>
                {
                    if (data != null)
                    {
                        if (geolocationProvider.EnableGeolocation && !string.IsNullOrEmpty(data.country))
                        {
                            fields[GeolocationCountryField] = data.country;
                        }
                        if (geolocationProvider.EnableAsnCollection && !string.IsNullOrEmpty(data.asn))
                        {
                            fields[GeolocationAsnField] = data.asn;
                        }
                    }
                    done = true;
                },
                error =>
                {
                    _log.Warning("Location/ASN data unavailable, report continues without it: " + error);
                    done = true;
                });

            while (!done)
            {
                yield return null;
            }
        }

        private void CollectLatency(Dictionary<string, string> fields)
        {
            if (latencyProvider == null || !latencyProvider.EnableLatency) return;

            latencyProvider.StopLatencyTest();
            var latency = latencyProvider.GetCurrentLatencyData();
            if (latency != null && latency.HasSuccessfulResults())
            {
                fields[LatencyField] = latency.GetFormattedLatency();
            }
            else
            {
                _log.Warning("No latency data available, report continues without it.");
            }
        }

        private string GetEffectiveAuthToken()
        {
            if (IsUserAuthenticated)
            {
                string jwtToken = deviceAuthManager.JwtToken;
                if (!string.IsNullOrEmpty(jwtToken))
                {
                    return string.IsNullOrEmpty(AuthToken) ? jwtToken : $"{AuthToken},{jwtToken}";
                }
            }
            return AuthToken;
        }

        private void ValidateProviderCustomFields()
        {
            if (_configurationError != null || _customFieldValidator == null) return;

            string authToken = GetEffectiveAuthToken();
            if (string.IsNullOrEmpty(authToken)) return;

            var requiredFields = new List<CustomFieldRequirement>();
            if (geolocationProvider != null && geolocationProvider.RequiredCustomFields != null)
            {
                requiredFields.AddRange(geolocationProvider.RequiredCustomFields);
            }
            if (latencyProvider != null && latencyProvider.RequiredCustomFields != null)
            {
                requiredFields.AddRange(latencyProvider.RequiredCustomFields);
            }

            if (requiredFields.Count > 0)
            {
                _customFieldValidator.ValidateCustomFields(SubmitEndpoint, ProjectID, authToken, requiredFields);
            }
        }

        private void Report(ReporterDiagnostic diagnostic)
        {
            switch (diagnostic.Severity)
            {
                case DiagnosticSeverity.Error: _log.Error(diagnostic.Message); break;
                case DiagnosticSeverity.Warning: _log.Warning(diagnostic.Message); break;
                default: _log.Info(diagnostic.Message); break;
            }
        }

        private static IEnumerable<string> FormatFields(Dictionary<string, string> fields)
        {
            foreach (var kvp in fields)
            {
                yield return $"{kvp.Key}={kvp.Value}";
            }
        }

        void ICoroutineRunner.Run(IEnumerator routine)
        {
            StartCoroutine(routine);
        }
    }
}
