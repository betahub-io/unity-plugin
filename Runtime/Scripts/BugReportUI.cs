using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.IO;
using System.Collections;
using UnityEngine.Events;
using System.Collections.Generic;
using UnityEngine.Serialization;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BetaHub
{
    public enum MediaUploadType
    {
        /// <summary>
        /// The media will be uploaded in the background without blocking the process
        /// </summary>
        UploadInBackground,
        /// <summary>
        /// The process will wait until the media has finished uploading before continuing
        /// </summary>
        WaitForUpload
    }

    [RequireComponent(typeof(CustomFieldValidator))]
    public class BugReportUI : MonoBehaviour
    {
        public const string DEMO_PROJECT_ID = "pr-5287510306";

        private static BugReportUI _instance;
        
        [FormerlySerializedAs("bugReportPanel")]
        public GameObject BugReportPanel;

        [FormerlySerializedAs("descriptionField")]
        public TMP_InputField DescriptionField;

        [FormerlySerializedAs("stepsField")]
        public TMP_InputField StepsField;
        
        public Toggle IncludeVideoToggle;
        public Toggle IncludeScreenshotToggle;
        public Toggle IncludePlayerLogToggle;

        [FormerlySerializedAs("submitButton")]
        public Button SubmitButton;

        [FormerlySerializedAs("closeButton")]
        public Button CloseButton;

        [FormerlySerializedAs("messagePanel")]
        public GameObject MessagePanel;

        [FormerlySerializedAs("messagePanelUI")]
        public MessagePanelUI MessagePanelUI;

        [Tooltip("Upload in background : The media will be uploaded in the background without blocking the process" +
            " \n Wait for upload : The process will wait until the media has finished uploading before continuing")]
        [FormerlySerializedAs("mediaUploadType")]
        public MediaUploadType MediaUploadType;

        [FormerlySerializedAs("reportSubmittedUI")]
        public ReportSubmittedUI ReportSubmittedUI;

        [FormerlySerializedAs("submitEndpoint")]
        public string SubmitEndpoint = "https://app.betahub.io";

        [FormerlySerializedAs("projectID")]
        public string ProjectID;

        [FormerlySerializedAs("authToken")]
        public string AuthToken;

        // Device authentication manager for user login flow
        [Tooltip("Device Authentication (Optional). If set, the user may be authenticated via device auth.")]
        [SerializeField] private DeviceAuthManager deviceAuthManager;

        // Geolocation provider for collecting location data
        [Tooltip("Geolocation Provider (Optional). If set, location data will be included in bug reports.")]
        [SerializeField] private GeolocationProvider geolocationProvider;

        // Latency provider for collecting network latency data
        [Tooltip("Latency Provider (Optional). If set, network latency data will be included in bug reports.")]
        [SerializeField] private LatencyProvider latencyProvider;

        // Custom field validator for validating provider requirements
        private CustomFieldValidator customFieldValidator;

        // If set, this email address will be used as the default email address of the reporter.
        // This is a hidden field since it's purpose is to be pre-filled programmatically by the developer if the user is somehow already logged in with a specific email address.
        [HideInInspector, FormerlySerializedAs("defaultEmailAddress")]
        public string DefaultEmailAddress;

    #if ENABLE_INPUT_SYSTEM
        [FormerlySerializedAs("shortcutAction")]
        public InputAction ShortcutAction = new InputAction("BugReportShortcut", binding: "<Keyboard>/f12");
    #else
        [FormerlySerializedAs("shortcutKey")]
        public KeyCode ShortcutKey = KeyCode.F12;
    #endif

        [FormerlySerializedAs("includePlayerLog")]
        public bool IncludePlayerLog = true;

        [FormerlySerializedAs("includeVideo")]
        public bool IncludeVideo = true;

        [Tooltip("The release ID to associate the bug report with. If not set (0), the bug report will be " +
            "associated with the latest release. Requires the AuthToken to have a permission of creating new releases.")]
        public int ReleaseId = 0;
        
        [Tooltip("The release label to associate the bug report with. If not created yet on the backend, " +
            "a new release will be created with this label. Requires the AuthToken to have a permission of creating new releases. " +
            "If not set (null), the bug report will be associated with the latest release.")]
        public string ReleaseLabel = "";

        public UnityEvent OnBugReportWindowShown;
        public UnityEvent OnBugReportWindowHidden;

        private List<Issue.ScreenshotFileReference> _screenshots = new List<Issue.ScreenshotFileReference>();
        private List<Issue.LogFileReference> _logFiles = new List<Issue.LogFileReference>();

        private static Logger _logger;
        private bool _cursorStateChanged;
        private CursorLockMode _previousCursorLockMode;

        private GameRecorder _gameRecorder;

        // we keep track of the issues to not record the video when any of the issues are being uploaded
        private List<Issue> _issues = new List<Issue>();

        private bool _uiWasVisible = false;

#if !DISABLE_BETAHUB_LOGGER
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitializeLogger()
        {
            _logger = new Logger();
        }
#endif
        
        void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        void OnEnable()
        {
    #if ENABLE_INPUT_SYSTEM
            if (IsNewInputSystemEnabled())
            {
               ShortcutAction.Enable();
               ShortcutAction.performed += OnShortcutActionPerformed;
            }
    #endif
        }

        void OnDisable()
        {
    #if ENABLE_INPUT_SYSTEM
            if (IsNewInputSystemEnabled())
            {
                ShortcutAction.performed -= OnShortcutActionPerformed;
                ShortcutAction.Disable();
            }
    #endif
        }
        
        void Start()
        {
            _gameRecorder = GetComponent<GameRecorder>();
            
            // Get the required CustomFieldValidator component (automatically added by RequireComponent)
            customFieldValidator = GetComponent<CustomFieldValidator>();
            
            BugReportPanel.SetActive(false);
            SubmitButton.onClick.AddListener(SubmitBugReport);
            CloseButton.onClick.AddListener(() =>
            {
                BugReportPanel.SetActive(false);
            });

            if (string.IsNullOrEmpty(ProjectID))
            {
                Debug.LogError("Project ID is not set. I won't be able to submit bug reports.");
            }

#if DISABLE_BETAHUB_LOGGER
            Debug.LogWarning("DISABLE_BETAHUB_LOGGER is enabled. Player logs will not be captured for bug reports. Remove this scripting define symbol to enable logging.");
#endif

            // Check for auth token if not using device auth
            if (string.IsNullOrEmpty(AuthToken))
            {
                Debug.LogWarning("Auth token is not set. Bug reports will only work if device authentication is available and user is signed in.");
            }

            // auth token must start with tkn- if provided
            if (!string.IsNullOrEmpty(AuthToken) && !AuthToken.StartsWith("tkn-"))
            {
                Debug.LogError("Auth token must start with tkn-. I won't be able to submit bug reports.");
            }

            var gameRecorder = GetComponent<GameRecorder>();
            if (gameRecorder == null)
            {
                Debug.LogWarning("GameRecorder component is not attached to the same GameObject as BugReportUI. Video won't be recorded.");
                IncludeVideoToggle.interactable = false;
            }
            else
            {
                // update {TIME} in toggle label
                int recordingDurationSeconds = gameRecorder.RecordingDuration;
                var textComponent = IncludeVideoToggle.GetComponentInChildren<Text>();

                if (textComponent != null)
                {
                    textComponent.text = textComponent.text.Replace("{TIME}", recordingDurationSeconds.ToString() + " Seconds");
                }
                else
                {
                    Debug.LogWarning("Could not find Text component in IncludeVideoToggle. Make sure the text is set in the inspector.");
                }
            }

            // Validate custom fields for providers
            ValidateProviderCustomFields();

            DontDestroyOnLoad(gameObject);
        }

        void Update()
        {
            if (_gameRecorder != null)
            {
                if (SholdBeRecordingVideo() && (!_gameRecorder.IsRecording || _gameRecorder.IsPaused))
                {
                    _gameRecorder.StartRecording();
                }
                else if (!SholdBeRecordingVideo() && (_gameRecorder.IsRecording && !_gameRecorder.IsPaused))
                {
                    _gameRecorder.PauseRecording();
                }
            }

            if (UiIsVisible())
            {
                if (!_uiWasVisible)
                {
                    OnBugReportWindowShown?.Invoke();
                    
                    // Start latency testing when bug report window is shown
                    if (latencyProvider != null && latencyProvider.EnableLatency)
                    {
                        latencyProvider.StartLatencyTest();
                    }
                    
                    ModifyCursorState();
                }
                _uiWasVisible = true;
            }
            else if (!UiIsVisible())
            {
                if (_uiWasVisible)
                {
                    OnBugReportWindowHidden?.Invoke();
                    RestoreCursorState();
                }
                _uiWasVisible = false;
            }
            
    #if !ENABLE_INPUT_SYSTEM
            if (ShortcutKey != KeyCode.None && Input.GetKeyDown(ShortcutKey))
            {
                StartCoroutine(CaptureScreenshotAndShowUI());
            }
    #endif
        }

        private bool SholdBeRecordingVideo()
        {
            // if true, the report is being uploaded, some processes should be paused
            return IncludeVideo && !UiIsVisible() && !_issues.Exists(issue => !issue.IsMediaUploadComplete);
        }

        private bool UiIsVisible()
        {
            return BugReportPanel.activeSelf || MessagePanelUI.gameObject.activeSelf || ReportSubmittedUI.gameObject.activeSelf;
        }

        private void ModifyCursorState()
        {
            if (!Cursor.visible || Cursor.lockState != CursorLockMode.None)
            {
                _cursorStateChanged = true;
                _previousCursorLockMode = Cursor.lockState;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void RestoreCursorState()
        {
            if (_cursorStateChanged)
            {
                Cursor.lockState = _previousCursorLockMode;
                Cursor.visible = false;
                _cursorStateChanged = false;
            }
        }

    #if ENABLE_INPUT_SYSTEM
        void OnShortcutActionPerformed(InputAction.CallbackContext context)
        {
            StartCoroutine(CaptureScreenshotAndShowUI());
        }

        bool IsNewInputSystemEnabled()
        {
            // Check if the new input system is enabled
            return InputSystem.settings != null;
        }
    #endif

        IEnumerator CaptureScreenshotAndShowUI()
        {
            // Capture screenshot
            string screenshotPath = Application.persistentDataPath + "/screenshot.png";
            ScreenCapture.CaptureScreenshot(screenshotPath);

            AddScreenshot(screenshotPath, true);

            // wait for another frame
            yield return null;

            // reset the fields
            DescriptionField.text = "";
            StepsField.text = "";
            IncludeVideoToggle.isOn = true;
            IncludeScreenshotToggle.isOn = true;
            IncludePlayerLogToggle.isOn = true;

            #if BETAHUB_DEBUG
                Debug.Log("BETAHUB_DEBUG: Prefilling description and steps for reproduce for faster testing");
                // prefill the description and steps for reproduce for faster testing
                DescriptionField.text = "The game is crashing when I press the sounds setting button. It happens on the main menu and on the settings menu.";
                StepsField.text = "1. Go to the main menu\n2. Press the settings button\n3. Press the sounds button\n4. Crash the game";
            #endif
            
            BugReportPanel.SetActive(true);
        }

        // Sets screenshot path to be uploaded. Useful on manual invocation of bug report UI.
        public void AddScreenshot(string path, bool removeAfterUpload)
        {
            _screenshots.Add(new Issue.ScreenshotFileReference { path = path, removeAfterUpload = removeAfterUpload });
        }

        public void AddLogFile(string path, bool removeAfterUpload)
        {
            _logFiles.Add(new Issue.LogFileReference { path = path, removeAfterUpload = removeAfterUpload });
        }

        void SubmitBugReport()
        {
            StartCoroutine(SubmitBugReportCoroutine());
        }

        private IEnumerator SubmitBugReportCoroutine()
        {
            string description = DescriptionField.text;
            string steps = StepsField.text;

            // Filter screenshots and logs based on toggle state
            List<Issue.ScreenshotFileReference> screenshots = null;
            List<Issue.LogFileReference> logFiles = null;
            GameRecorder gameRecorder = null;
            
            if (IncludeScreenshotToggle.isOn && _screenshots.Count > 0)
            {
                // copy to new list
                screenshots = new List<Issue.ScreenshotFileReference>(_screenshots);
            }
            
            if (IncludePlayerLogToggle.isOn)
            {
                logFiles = new List<Issue.LogFileReference>(_logFiles);
                
                // Add logger log file if it exists
                if (IncludePlayerLog && _logger != null && !string.IsNullOrEmpty(_logger.LogPath) && File.Exists(_logger.LogPath))
                {
                    // skip if size over 200MB
                    if (new FileInfo(_logger.LogPath).Length < 200 * 1024 * 1024)
                    {
                        logFiles.Add(new Issue.LogFileReference { logger = _logger, removeAfterUpload = false });
                    }
                }
            }
            
            if (IncludeVideo && IncludeVideoToggle.isOn)
            {
                gameRecorder = _gameRecorder;
            }
            
            // Get geolocation and latency data if providers are available
            var customFieldsData = new System.Collections.Generic.Dictionary<string, string>();
            
            // Get geolocation and ASN data
            if (geolocationProvider != null && (geolocationProvider.EnableGeolocation || geolocationProvider.EnableAsnCollection))
            {
                bool locationReceived = false;
                string locationError = null;
                
                yield return geolocationProvider.GetLocationDataAsync(
                    (locationData) => {
                        if (locationData != null)
                        {
                            if (geolocationProvider.EnableGeolocation && !string.IsNullOrEmpty(locationData.country))
                            {
                                customFieldsData["country"] = locationData.country;
                                Debug.Log("BugReportUI: Including location data in bug report: " + locationData.country);
                            }
                            
                            if (geolocationProvider.EnableAsnCollection && !string.IsNullOrEmpty(locationData.asn))
                            {
                                customFieldsData["asn"] = locationData.asn;
                                Debug.Log("BugReportUI: Including ASN data in bug report: " + locationData.asn);
                            }
                        }
                        locationReceived = true;
                    },
                    (error) => {
                        locationError = error;
                        locationReceived = true;
                        Debug.LogWarning("BugReportUI: Failed to get location/ASN data: " + error);
                    }
                );
                
                // Wait for location request to complete (success or failure)
                while (!locationReceived)
                {
                    yield return null;
                }
            }
            
            // Get latency data (stop any ongoing test and get current results)
            if (latencyProvider != null && latencyProvider.EnableLatency)
            {
                // Stop the latency test and get whatever results we have
                latencyProvider.StopLatencyTest();
                var latencyData = latencyProvider.GetCurrentLatencyData();
                
                if (latencyData != null && latencyData.HasSuccessfulResults())
                {
                    customFieldsData["latency"] = latencyData.GetFormattedLatency();
                    Debug.Log("BugReportUI: Including latency data in bug report: " + latencyData.GetFormattedLatency());
                }
                else
                {
                    Debug.LogWarning("BugReportUI: No latency data available for bug report.");
                }
            }
            
            // Log custom fields if any
            if (customFieldsData.Count > 0)
            {
                var fieldsList = new System.Collections.Generic.List<string>();
                foreach (var kvp in customFieldsData)
                {
                    fieldsList.Add($"{kvp.Key}={kvp.Value}");
                }
                Debug.Log("BugReportUI: Final custom fields: " + string.Join(", ", fieldsList.ToArray()));
            }
            
            // Create Issue instance and post it with authentication
            string effectiveAuthToken = GetEffectiveAuthToken();
            Issue issue = new Issue(SubmitEndpoint, ProjectID, effectiveAuthToken, MessagePanelUI, ReportSubmittedUI, gameRecorder);
            _issues.Add(issue);


            Action<ErrorMessage> onIssueError = (ErrorMessage errorMessage) =>
            {
                try {
                    // error, get ready to try again
                    SubmitButton.interactable = true;
                    SubmitButton.GetComponentInChildren<TMP_Text>().text = "Submit";

                    if (errorMessage.exception != null)
                    {
                        Debug.LogError("Error submitting bug report: " + errorMessage.exception);
                        MessagePanelUI.ShowMessagePanel("Error", "Error submitting bug report. Please try again later.");
                    }
                    else if (!string.IsNullOrEmpty(errorMessage.error))
                    {
                        MessagePanelUI.ShowMessagePanel("Error", errorMessage.error);
                    }
                    else
                    {
                        MessagePanelUI.ShowMessagePanel("Error", "Unknown error submitting bug report. Please try again later.");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError("Error displaying error message: " + e);
                }
            };


            CoroutineUtils.StartThrowingCoroutine(this,
            issue.PostIssue(description, steps, screenshots, logFiles, ReleaseId, ReleaseLabel, false,
                (issueId) => // successful post
                {
                    SubmitButton.interactable = true;
                    SubmitButton.GetComponentInChildren<TMP_Text>().text = "Submit";

                    // Clear lists after successful upload
                    _screenshots.Clear();
                    _logFiles.Clear();

                    // Check if user is authenticated via device auth
                    if (IsUserAuthenticatedViaDeviceAuth())
                    {
                        // For authenticated users: publish immediately and show thanks
                        StartCoroutine(PublishIssueAndShowThanks(issue));
                    }
                    else
                    {
                        // For non-authenticated users: show email UI which handles publishing
                        string effectiveEmail = GetEffectiveEmailAddress();
                        ReportSubmittedUI.Show(issue, effectiveEmail);
                    }

                    // hide bug report panel
                    BugReportPanel.SetActive(false);
                },
                MediaUploadType,
                (error) =>
                {
                    onIssueError(new ErrorMessage { error = error });
                },
                customFieldsData.Count > 0 ? customFieldsData : null
            ),
            (ex) => // done
            {
                if (ex != null)
                {
                    onIssueError(new ErrorMessage { exception = ex });
                }
            });

            SubmitButton.interactable = false;
            SubmitButton.GetComponentInChildren<TMP_Text>().text = "Submitting...";
        }

        class ErrorMessage {
            public string error;
            public Exception exception;
        }

        class IssueResponse {
            public string id;
        }

        #region Device Authentication Integration

        private bool IsDeviceAuthAvailable()
        {
            return deviceAuthManager != null && !string.IsNullOrEmpty(ProjectID);
        }

        private string GetEffectiveAuthToken()
        {
            // Check if user is authenticated via device auth at submit time
            if (deviceAuthManager != null && deviceAuthManager.IsAuthenticated())
            {
                string jwtToken = deviceAuthManager.JwtToken;
                if (!string.IsNullOrEmpty(jwtToken))
                {
                    // Return AuthToken,JwtToken format when signed in
                    return string.IsNullOrEmpty(AuthToken) ? jwtToken : $"{AuthToken},{jwtToken}";
                }
            }

            // Fall back to configured auth token only
            return AuthToken;
        }

        private string GetEffectiveEmailAddress()
        {
            // If user is authenticated via device auth, don't use email address
            // Backend already knows who the reporter is from the JWT token
            if (deviceAuthManager != null && deviceAuthManager.IsAuthenticated())
            {
                // Return null/empty - backend knows the user from JWT token
                return null;
            }

            // Fall back to configured default email for non-authenticated users
            return DefaultEmailAddress;
        }

        public void SetDeviceAuthManager(DeviceAuthManager authManager)
        {
            deviceAuthManager = authManager;
        }

        public void SetGeolocationProvider(GeolocationProvider provider)
        {
            geolocationProvider = provider;
        }

        public void SetLatencyProvider(LatencyProvider provider)
        {
            latencyProvider = provider;
        }

        private bool IsUserAuthenticatedViaDeviceAuth()
        {
            return deviceAuthManager != null && deviceAuthManager.IsAuthenticated();
        }

        private IEnumerator PublishIssueAndShowThanks(Issue issue)
        {
            // Publish the issue immediately (no email needed - backend knows user from JWT)
            var publishCoroutine = issue.Publish(false); // false = don't email the report
            yield return publishCoroutine;
            
            // Check if publishing was successful by checking if the coroutine completed without errors
            // Note: Issue.Publish() should handle its own errors internally, so we assume success here
            MessagePanelUI.ShowMessagePanel("Thank You", "Your bug report has been submitted successfully. Thank you for helping us improve the game!");
        }

        void OnDestroy()
        {
            // Cleanup handled automatically since we don't subscribe to events
        }

        private void ValidateProviderCustomFields()
        {
            // Skip validation if we don't have the necessary credentials
            if (string.IsNullOrEmpty(SubmitEndpoint) || string.IsNullOrEmpty(ProjectID))
            {
                Debug.LogWarning("BugReportUI: Cannot validate custom fields - missing endpoint or project ID");
                return;
            }

            string effectiveAuthToken = GetEffectiveAuthToken();
            if (string.IsNullOrEmpty(effectiveAuthToken))
            {
                Debug.LogWarning("BugReportUI: Cannot validate custom fields - no auth token available");
                return;
            }

            // Collect required fields from all attached providers
            var requiredFields = new List<CustomFieldRequirement>();

            if (geolocationProvider != null && geolocationProvider.RequiredCustomFields != null)
            {
                requiredFields.AddRange(geolocationProvider.RequiredCustomFields);
            }

            if (latencyProvider != null && latencyProvider.RequiredCustomFields != null)
            {
                requiredFields.AddRange(latencyProvider.RequiredCustomFields);
            }

            // Run validation if we have any required fields
            if (requiredFields.Count > 0 && customFieldValidator != null)
            {
                customFieldValidator.ValidateCustomFields(SubmitEndpoint, ProjectID, effectiveAuthToken, requiredFields);
            }
        }

        #endregion
    }
}