using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using UnityEngine.Events;
using UnityEngine.Serialization;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BetaHub
{
    /// <summary>
    /// The shipped bug report form. Pure view: collects input, shows progress and results, and
    /// delegates everything else to the <see cref="BetaHubReporter"/> on the same GameObject.
    /// Use it as a starting point for your own form, or replace it entirely.
    /// </summary>
    [RequireComponent(typeof(BetaHubReporter))]
    public class BugReportUI : MonoBehaviour
    {
        private const string SubmitLabel = "Submit";
        private const string SubmittingLabel = "Submitting...";
        private const string UploadingLabel = "Uploading...";
        private const string RecordingDurationPlaceholder = "{TIME}";

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

        [FormerlySerializedAs("reportSubmittedUI")]
        public ReportSubmittedUI ReportSubmittedUI;

    #if ENABLE_INPUT_SYSTEM
        [FormerlySerializedAs("shortcutAction")]
        public InputAction ShortcutAction = new InputAction("BugReportShortcut", binding: "<Keyboard>/f12");
    #else
        [FormerlySerializedAs("shortcutKey")]
        public KeyCode ShortcutKey = KeyCode.F12;
    #endif

        public UnityEvent OnBugReportWindowShown;
        public UnityEvent OnBugReportWindowHidden;

        // 1.x kept these on BugReportUI. Hidden fields under the old names still load an upgraded scene's
        // values, which Awake hands to the reporter so its reports don't go to the demo project.
        [SerializeField, HideInInspector] private string SubmitEndpoint;
        [SerializeField, HideInInspector] private string ProjectID;
        [SerializeField, HideInInspector] private string AuthToken;
        [SerializeField, HideInInspector] private DeviceAuthManager deviceAuthManager;

        private BetaHubReporter _reporter;
        private ReportSubmission _activeSubmission;
        private TMP_Text _submitButtonLabel;
        private bool _uiWasVisible;
        private bool _cursorStateChanged;
        private CursorLockMode _previousCursorLockMode;

        void Awake()
        {
            // before the reporter's Start validates its settings
            var reporter = GetComponent<BetaHubReporter>();
            if (reporter != null) AdoptLegacySettings(reporter);
        }

        private void AdoptLegacySettings(BetaHubReporter reporter)
        {
            if (ReporterConfiguration.ShouldAdoptLegacySettings(ProjectID, reporter.ProjectID))
            {
                reporter.ProjectID = ProjectID;
                reporter.AuthToken = AuthToken;
                if (!string.IsNullOrEmpty(SubmitEndpoint)) reporter.SubmitEndpoint = SubmitEndpoint;
                Debug.LogWarning(UnityBetaHubLog.Prefix + "Project ID, Auth Token and Submit Endpoint moved from BugReportUI to BetaHubReporter in 2.0. " +
                    "Using the values still stored on BugReportUI; set them on BetaHubReporter and save the scene.", this);
            }
            if (deviceAuthManager != null && reporter.DeviceAuthManager == null)
            {
                reporter.SetDeviceAuthManager(deviceAuthManager);
                Debug.LogWarning(UnityBetaHubLog.Prefix + "The Device Auth Manager moved from BugReportUI to BetaHubReporter in 2.0. " +
                    "Using the one still assigned on BugReportUI; assign it on BetaHubReporter and save the scene.", this);
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
            _reporter = GetComponent<BetaHubReporter>();
            _submitButtonLabel = SubmitButton.GetComponentInChildren<TMP_Text>();

            BugReportPanel.SetActive(false);
            SubmitButton.onClick.AddListener(SubmitBugReport);
            CloseButton.onClick.AddListener(() => BugReportPanel.SetActive(false));

            if (_reporter == null)
            {
                Debug.LogError(UnityBetaHubLog.Prefix + "BugReportUI needs a BetaHubReporter component on the same GameObject. Add one, or use the shipped prefab. The form will not submit.", this);
                SubmitButton.interactable = false;
                return;
            }

            var recorder = GetComponent<GameRecorder>();
            if (recorder == null)
            {
                IncludeVideoToggle.interactable = false;
            }
            else
            {
                var toggleLabel = IncludeVideoToggle.GetComponentInChildren<Text>();
                if (toggleLabel != null)
                {
                    toggleLabel.text = toggleLabel.text.Replace(RecordingDurationPlaceholder, recorder.RecordingDuration + " Seconds");
                }
            }
        }

        void Update()
        {
            bool visible = UiIsVisible();
            if (visible && !_uiWasVisible)
            {
                OnBugReportWindowShown?.Invoke();
                _reporter?.NotifyReportWindowShown();
                ModifyCursorState();
            }
            else if (!visible && _uiWasVisible)
            {
                OnBugReportWindowHidden?.Invoke();
                _reporter?.NotifyReportWindowHidden();
                RestoreCursorState();
            }
            _uiWasVisible = visible;

    #if !ENABLE_INPUT_SYSTEM
            if (ShortcutKey != KeyCode.None && Input.GetKeyDown(ShortcutKey))
            {
                StartCoroutine(CaptureScreenshotAndShowUI());
            }
    #endif
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
            return InputSystem.settings != null;
        }
    #endif

        /// <summary>Captures a screenshot of the current frame and opens the form. Bound to the shortcut.</summary>
        public void Show()
        {
            StartCoroutine(CaptureScreenshotAndShowUI());
        }

        IEnumerator CaptureScreenshotAndShowUI()
        {
            if (_reporter == null) yield break;

            yield return _reporter.CaptureScreenshot();

            DescriptionField.text = "";
            StepsField.text = "";
            IncludeVideoToggle.isOn = true;
            IncludeScreenshotToggle.isOn = true;
            IncludePlayerLogToggle.isOn = true;

            #if BETAHUB_DEBUG
                Debug.Log("BETAHUB_DEBUG: Prefilling description and steps for reproduce for faster testing");
                DescriptionField.text = "The game is crashing when I press the sounds setting button. It happens on the main menu and on the settings menu.";
                StepsField.text = "1. Go to the main menu\n2. Press the settings button\n3. Press the sounds button\n4. Crash the game";
            #endif

            BugReportPanel.SetActive(true);
        }

        void SubmitBugReport()
        {
            // Only a report still being created blocks a new one; one uploading in the background does not.
            if (_activeSubmission != null && IsBeingCreated(_activeSubmission.State))
            {
                Debug.LogWarning(UnityBetaHubLog.Prefix + "Submit ignored: the previous report is still being created.", this);
                return;
            }

            var options = new BugReportOptions
            {
                IncludeScreenshots = IncludeScreenshotToggle.isOn,
                IncludeLogs = IncludePlayerLogToggle.isOn,
                IncludeRecording = IncludeVideoToggle.isOn,
            };

            SetSubmitButton(false, SubmittingLabel);

            _activeSubmission = _reporter.Submit(DescriptionField.text, StepsField.text, options);
            _activeSubmission.StateChanged += OnSubmissionStateChanged;
            _activeSubmission.ReporterEmailFailed += OnReporterEmailFailed;

            // A configuration problem yields an already failed handle; StateChanged will not fire again.
            if (_activeSubmission.State == ReportSubmissionState.Failed)
            {
                OnSubmissionStateChanged(_activeSubmission);
            }
        }

        private void OnSubmissionStateChanged(ReportSubmission submission)
        {
            switch (submission.State)
            {
                case ReportSubmissionState.UploadingMedia:
                    SetSubmitButton(false, UploadingLabel);
                    break;

                case ReportSubmissionState.AwaitingPublish:
                    SetSubmitButton(true, SubmitLabel);
                    BugReportPanel.SetActive(false);
                    ReportSubmittedUI.Show(submission, _reporter.EffectiveEmailAddress);
                    break;

                case ReportSubmissionState.Publishing:
                    // Reached directly when the user is authenticated and the report auto-publishes.
                    SetSubmitButton(true, SubmitLabel);
                    BugReportPanel.SetActive(false);
                    break;

                case ReportSubmissionState.Published:
                    if (_reporter.IsUserAuthenticated)
                    {
                        MessagePanelUI.ShowMessagePanel("Thank You", "Your bug report has been submitted successfully. Thank you for helping us improve the game!");
                    }
                    Detach(submission);
                    break;

                case ReportSubmissionState.Failed:
                    SetSubmitButton(true, SubmitLabel);
                    MessagePanelUI.ShowMessagePanel("Error", submission.Error);
                    Detach(submission);
                    break;
            }
        }

        private void OnReporterEmailFailed(string error)
        {
            MessagePanelUI.ShowMessagePanel("Error", "Could not save your email address: " + error);
        }

        private static bool IsBeingCreated(ReportSubmissionState state)
        {
            return state == ReportSubmissionState.Preparing || state == ReportSubmissionState.CreatingDraft || state == ReportSubmissionState.UploadingMedia;
        }

        private void Detach(ReportSubmission submission)
        {
            submission.StateChanged -= OnSubmissionStateChanged;
            submission.ReporterEmailFailed -= OnReporterEmailFailed;
        }

        private void SetSubmitButton(bool interactable, string label)
        {
            SubmitButton.interactable = interactable;
            if (_submitButtonLabel != null)
            {
                _submitButtonLabel.text = label;
            }
        }
    }
}
