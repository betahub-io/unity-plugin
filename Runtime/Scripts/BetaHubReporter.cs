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
        private const string ScreenshotFileName = "screenshot.png";
        // ScreenCapture writes the file at the end of the frame; two frames is what the views always waited.
        private const int ScreenshotFrames = 2;

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
        private bool _duplicate;
        private bool _reportWindowVisible;
        private readonly List<ReportSubmission> _submissions = new List<ReportSubmission>();

        /// <summary>True while the user is signed in through device authentication.</summary>
        public bool IsUserAuthenticated => deviceAuthManager != null && deviceAuthManager.IsAuthenticated();

        /// <summary>Email to pre-fill for anonymous reporters. Null when the backend already knows the user.</summary>
        public string EffectiveEmailAddress => IsUserAuthenticated ? null : DefaultEmailAddress;

        public bool HasRecorder => _recorder != null;
        /// <summary>The frame captured by the last <see cref="CaptureScreenshot"/>, or null.</summary>
        public string ScreenshotPath { get; private set; }
        public GameRecorder Recorder => _recorder;
        public bool IsReportWindowVisible => _reportWindowVisible;
        public bool HasActiveUploads => _submissions.Exists(s => s.IsUploadInProgress);
        public bool IsConfigured => ConfigurationError == null;
        /// <summary>First fatal configuration problem as the settings stand now, or null.</summary>
        public string ConfigurationError => ReporterConfiguration.FirstError(ValidateConfiguration());
        public DeviceAuthManager DeviceAuthManager => deviceAuthManager;
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
                _duplicate = true;
                Destroy(gameObject);
                return;
            }
            _instance = this;
            EnsureSession();
        }

        void Start()
        {
            if (_duplicate) return;

            _recorder = GetComponent<GameRecorder>();
            _customFieldValidator = GetComponent<CustomFieldValidator>();

            // Logged once; Submit checks again, so settings assigned later from code still count.
            foreach (var diagnostic in ValidateConfiguration())
            {
                Report(diagnostic);
            }

#if DISABLE_BETAHUB_LOGGER
            Log.Warning("DISABLE_BETAHUB_LOGGER is defined. Player logs will not be captured for bug reports. Remove the scripting define symbol to enable logging.");
#endif

            ValidateProviderCustomFields();
            DontDestroyOnLoad(gameObject);
        }

        // A second prefab after a scene load destroys itself in Awake; scripts holding it still reach the one that stays.
        // The session is made on first use, so calls from another component's Awake work too.
        private BugReportSession Session => (_instance != null && _instance != this ? _instance : this).EnsureSession();
        private IBetaHubLog Log => _log ??= new UnityBetaHubLog(this);

        private BugReportSession EnsureSession() => _session ??= new BugReportSession(Log);

        private List<ReporterDiagnostic> ValidateConfiguration()
        {
            return ReporterConfiguration.Validate(SubmitEndpoint, ProjectID, AuthToken,
                hasDeviceAuth: deviceAuthManager != null, hasRecorder: _recorder != null);
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

        /// <summary>
        /// Captures the current frame as the report screenshot, replacing the previous one. Run it
        /// before your form becomes visible so the form is not in the shot.
        /// </summary>
        public IEnumerator CaptureScreenshot()
        {
            string path = Path.Combine(Application.persistentDataPath, ScreenshotFileName);
            ScreenCapture.CaptureScreenshot(path);
            for (int i = 0; i < ScreenshotFrames; i++)
            {
                yield return null;
            }

            if (!File.Exists(path))
            {
                Log.Warning("Screenshot capture produced no file at " + path + ". The report goes without it.");
                path = null;
            }
            ScreenshotPath = path;
            Session.SetFrameScreenshot(path);
        }

        /// <summary>
        /// Everything that can travel with the next report, all rows included unless a provider said
        /// otherwise. Call when your form opens, after <see cref="CaptureScreenshot"/>; let the player
        /// untick rows, then pass the set to <see cref="Submit(string, string, AttachmentSet)"/>.
        /// </summary>
        public AttachmentSet CollectAttachments()
        {
            var sources = new AttachmentSources
            {
                RecordingSeconds = _recorder != null && IncludeVideo ? _recorder.RecordingDuration : 0,
                PlayerLog = GetPlayerLogSource(),
                LocationCountry = geolocationProvider != null && geolocationProvider.EnableGeolocation,
                LocationNetwork = geolocationProvider != null && geolocationProvider.EnableAsnCollection,
                Latency = latencyProvider != null && latencyProvider.EnableLatency,
                FileSize = FileSizeOrUnknown,
            };
            return Session.CollectAttachments(sources);
        }

        /// <summary>Latency measured so far while the form is open, one entry per host. Empty without a LatencyProvider.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> LatencyPreview()
        {
            var result = new List<KeyValuePair<string, string>>();
            var data = latencyProvider != null && latencyProvider.EnableLatency ? latencyProvider.GetCurrentLatencyData() : null;
            if (data?.hostResults == null) return result;
            foreach (var host in data.hostResults)
            {
                result.Add(new KeyValuePair<string, string>(host.Key, host.Value.GetFormattedLatency()));
            }
            return result;
        }

        /// <summary>Adds a screenshot to the next report as its own row.</summary>
        public void AddScreenshot(string path, bool removeAfterUpload) => Session.AddScreenshot(path, removeAfterUpload);
        public void AddLogFile(string path, bool removeAfterUpload) => Session.AddLogFile(path, removeAfterUpload);
        /// <summary>Adds a video to the next report as its own row, separate from the gameplay recording.</summary>
        public void AddVideo(string path, bool removeAfterUpload) => Session.AddVideo(path, removeAfterUpload);
        /// <summary>Adds any file, such as a save, to the next report. Up to 50 MB; <paramref name="name"/> defaults to the file name.</summary>
        public void AddFile(string path, string name = null, bool removeAfterUpload = false) => Session.AddFile(path, name, removeAfterUpload);
        /// <summary>Forgets added files and the captured screenshot. Happens on its own once a report is created.</summary>
        public void ClearAttachments() => Session.ClearAttachments();

        /// <summary>Asked for rows each time <see cref="CollectAttachments"/> runs.</summary>
        public void RegisterAttachmentProvider(IAttachmentProvider provider) => Session.RegisterAttachmentProvider(provider);
        public void UnregisterAttachmentProvider(IAttachmentProvider provider) => Session.UnregisterAttachmentProvider(provider);

        /// <summary>Static custom field included in every report. Must be tester_settable in your project.</summary>
        public void SetCustomField(string name, string value) => Session.SetCustomField(name, value);
        public void RemoveCustomField(string name) => Session.RemoveCustomField(name);
        /// <summary>Custom field evaluated at submit time. Overrides a static field of the same name.</summary>
        public void RegisterCustomFieldProvider(string name, Func<string> valueGetter) => Session.RegisterCustomFieldProvider(name, valueGetter);
        public void UnregisterCustomFieldProvider(string name) => Session.UnregisterCustomFieldProvider(name);

        // --- providers ---

        public void SetDeviceAuthManager(DeviceAuthManager authManager) => deviceAuthManager = authManager;
        public void SetGeolocationProvider(GeolocationProvider provider) => geolocationProvider = provider;
        public void SetLatencyProvider(LatencyProvider provider) => latencyProvider = provider;

        // --- submission ---

        /// <summary>
        /// For the legacy three-toggle form: collects the attachments, leaves out what the toggles
        /// exclude and submits. New UIs call <see cref="CollectAttachments"/> and the set overload.
        /// </summary>
        public ReportSubmission Submit(string description, string steps, BugReportOptions options)
        {
            var attachments = CollectAttachments();
            BugReportSession.ApplyOptions(attachments, options);
            return Submit(description, steps, attachments);
        }

        /// <summary>
        /// Starts a bug report submission with the included rows of <paramref name="attachments"/>.
        /// Returns immediately with a handle; subscribe to <see cref="ReportSubmission.StateChanged"/>
        /// and <see cref="ReportSubmission.AttachmentUploadChanged"/> to follow progress. When the
        /// configuration is unusable the handle is already in the Failed state and carries the reason.
        /// </summary>
        public ReportSubmission Submit(string description, string steps, AttachmentSet attachments)
        {
            if (attachments == null) throw new ArgumentNullException(nameof(attachments));

            string configurationError = ConfigurationError;
            if (configurationError != null)
            {
                return ReportSubmission.CreateFailed(configurationError, Log);
            }

            string authToken = GetEffectiveAuthToken();
            if (string.IsNullOrEmpty(authToken))
            {
                return ReportSubmission.CreateFailed(
                    "No auth token available. Set Auth Token on the BetaHubReporter component or sign the user in through device authentication.", Log);
            }

            if (HasActiveUploads)
            {
                Log.Info("A previous report is still uploading. Recording stays paused until it finishes.");
            }

            var client = new Issue(SubmitEndpoint, ProjectID, authToken, _recorder);
            var submission = new ReportSubmission(client, this, Log);
            submission.DraftCreated += _ => Session.ClearAttachments();
            _submissions.Add(submission);
            _submissions.RemoveAll(s => s.IsFinished);

            StartCoroutine(PrepareAndStart(submission, description, steps, attachments));
            return submission;
        }

        private IEnumerator PrepareAndStart(ReportSubmission submission, string description, string steps, AttachmentSet attachments)
        {
            // Location is fetched only when the player left it in: a left-out row sends no request.
            var extraFields = new Dictionary<string, string>();
            if (attachments.IsIncluded(BuiltInAttachments.Location))
            {
                yield return CollectGeolocation(extraFields);
            }
            CollectLatency(extraFields, attachments.IsIncluded(BuiltInAttachments.Latency));

            BugReportDraft draft;
            try
            {
                draft = Session.BuildReport(description, steps, attachments, extraFields);
            }
            catch (Exception ex)
            {
                submission.Fail("Could not build the report: " + ex.Message);
                yield break;
            }

            if (draft.CustomFields.Count > 0)
            {
                Log.Info("Custom fields: " + string.Join(", ", FormatFields(draft.CustomFields)));
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
            // The logger holds recent lines in memory; without a flush a quiet game reports 0 B.
            _logger.ForceFlush();
            return new PlayerLogSource(_logger.LogPath, new FileInfo(_logger.LogPath).Length);
        }

        private static long FileSizeOrUnknown(string path)
        {
            try
            {
                return File.Exists(path) ? new FileInfo(path).Length : AttachmentSources.UnknownSize;
            }
            catch (Exception)
            {
                return AttachmentSources.UnknownSize;
            }
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
                    Log.Warning("Location/ASN data unavailable, report continues without it: " + error);
                    done = true;
                });

            while (!done)
            {
                yield return null;
            }
        }

        private void CollectLatency(Dictionary<string, string> fields, bool include)
        {
            if (latencyProvider == null || !latencyProvider.EnableLatency) return;

            latencyProvider.StopLatencyTest();
            if (!include) return;

            var latency = latencyProvider.GetCurrentLatencyData();
            if (latency != null && latency.HasSuccessfulResults())
            {
                fields[LatencyField] = latency.GetFormattedLatency();
            }
            else
            {
                Log.Warning("No latency data available, report continues without it.");
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
            if (!IsConfigured || _customFieldValidator == null) return;

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
                case DiagnosticSeverity.Error: Log.Error(diagnostic.Message); break;
                case DiagnosticSeverity.Warning: Log.Warning(diagnostic.Message); break;
                default: Log.Info(diagnostic.Message); break;
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
