using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.IO;
using System.Collections;
using UnityEngine.Events;
using System.Collections.Generic;
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

    public class BugReportUI : MonoBehaviour
    {
        private static BugReportUI instance;
        
        public GameObject bugReportPanel;
        public TMP_InputField descriptionField;
        public TMP_InputField stepsField;
        
        public Toggle IncludeVideoToggle;
        public Toggle IncludeScreenshotToggle;
        public Toggle IncludePlayerLogToggle;

        public Button submitButton;
        public Button closeButton;

        public GameObject messagePanel;

        public MessagePanelUI messagePanelUI;

        [Tooltip("Upload in background : The media will be uploaded in the background without blocking the process" +
            " \n Wait for upload : The process will wait until the media has finished uploading before continuing")]
        public MediaUploadType mediaUploadType;

        public ReportSubmittedUI reportSubmittedUI;

        public string submitEndpoint = "https://app.betahub.io";

        public string projectID;

        public string authToken;

        // If set, this email address will be used as the default email address of the reporter.
        // This is a hidden field since it's purpose is to be pre-filled programmatically by the developer if the user is somehow already logged in with a specific email address.
        [HideInInspector]
        public string defaultEmailAddress;

    #if ENABLE_INPUT_SYSTEM
        public InputAction shortcutAction = new InputAction("BugReportShortcut", binding: "<Keyboard>/f12");
    #else
        public KeyCode shortcutKey = KeyCode.F12;
    #endif

        public bool includePlayerLog = true;
        public bool includeVideo = true;

        public UnityEvent OnBugReportWindowShown;
        public UnityEvent OnBugReportWindowHidden;

        private List<Issue.ScreenshotFileReference> _screenshots = new List<Issue.ScreenshotFileReference>();
        private List<Issue.LogFileReference> _logFiles = new List<Issue.LogFileReference>();

        private static Logger logger;
        private bool _cursorStateChanged;
        private CursorLockMode _previousCursorLockMode;

        private GameRecorder _gameRecorder;

        // we keep track of the issues to not record the video when any of the issues are being uploaded
        private List<Issue> _issues = new List<Issue>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitializeLogger()
        {
            logger = new Logger();
        }
        
        void Awake()
        {
            if (instance == null)
            {
                instance = this;
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
                shortcutAction.Enable();
                shortcutAction.performed += OnShortcutActionPerformed;
            }
    #endif
        }

        void OnDisable()
        {
    #if ENABLE_INPUT_SYSTEM
            if (IsNewInputSystemEnabled())
            {
                shortcutAction.performed -= OnShortcutActionPerformed;
                shortcutAction.Disable();
            }
    #endif
        }
        
        void Start()
        {
            _gameRecorder = GetComponent<GameRecorder>();
            
            bugReportPanel.SetActive(false);
            submitButton.onClick.AddListener(SubmitBugReport);
            closeButton.onClick.AddListener(() =>
            {
                bugReportPanel.SetActive(false);
            });

            if (string.IsNullOrEmpty(projectID))
            {
                Debug.LogError("Project ID is not set. I won't be able to submit bug reports.");
            }

            if (string.IsNullOrEmpty(authToken))
            {
                Debug.LogError("Auth token is not set. I won't be able to submit bug reports.");
            }

            // auth token must start with tkn-
            if (!authToken.StartsWith("tkn-"))
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

            if (bugReportPanel.activeSelf && !_cursorStateChanged)
            {
                ModifyCursorState();
            }
            else if (!bugReportPanel.activeSelf && !messagePanelUI.gameObject.activeSelf && _cursorStateChanged)
            {
                RestoreCursorState();
            }
            
    #if !ENABLE_INPUT_SYSTEM
            if (shortcutKey != KeyCode.None && Input.GetKeyDown(shortcutKey))
            {
                StartCoroutine(CaptureScreenshotAndShowUI());
            }
    #endif
        }

        private bool SholdBeRecordingVideo()
        {
            // if true, the report is being uploaded, some processes should be paused
            return includeVideo && !bugReportPanel.activeSelf && !_issues.Exists(issue => !issue.IsMediaUploadComplete);
        }

        private void ModifyCursorState()
        {
            if (!Cursor.visible || Cursor.lockState != CursorLockMode.None)
            {
                _cursorStateChanged = true;
                _previousCursorLockMode = Cursor.lockState;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                OnBugReportWindowShown?.Invoke();
            }
        }

        private void RestoreCursorState()
        {
            if (_cursorStateChanged)
            {
                Cursor.lockState = _previousCursorLockMode;
                Cursor.visible = false;
                _cursorStateChanged = false;
                OnBugReportWindowHidden?.Invoke();
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
            descriptionField.text = "";
            stepsField.text = "";
            IncludeVideoToggle.isOn = true;
            IncludeScreenshotToggle.isOn = true;
            IncludePlayerLogToggle.isOn = true;
            
            bugReportPanel.SetActive(true);
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
            string description = descriptionField.text;
            string steps = stepsField.text;

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
                if (includePlayerLog && !string.IsNullOrEmpty(logger.LogPath) && File.Exists(logger.LogPath))
                {
                    // skip if size over 200MB
                    if (new FileInfo(logger.LogPath).Length < 200 * 1024 * 1024)
                    {
                        logFiles.Add(new Issue.LogFileReference { path = logger.LogPath, removeAfterUpload = false });
                    }
                }
            }
            
            if (includeVideo && IncludeVideoToggle.isOn)
            {
                gameRecorder = _gameRecorder;
            }
            
            // Create Issue instance and post it
            Issue issue = new Issue(submitEndpoint, projectID, authToken, messagePanelUI, reportSubmittedUI, gameRecorder);
            _issues.Add(issue);


            Action<ErrorMessage> onIssueError = (ErrorMessage errorMessage) =>
            {
                try {
                    // error, get ready to try again
                    submitButton.interactable = true;
                    submitButton.GetComponentInChildren<TMP_Text>().text = "Submit";

                    if (errorMessage.exception != null)
                    {
                        Debug.LogError("Error submitting bug report: " + errorMessage.exception);
                        messagePanelUI.ShowMessagePanel("Error", "Error submitting bug report. Please try again later.");
                    }
                    else if (!string.IsNullOrEmpty(errorMessage.error))
                    {
                        messagePanelUI.ShowMessagePanel("Error", errorMessage.error);
                    }
                    else
                    {
                        messagePanelUI.ShowMessagePanel("Error", "Unknown error submitting bug report. Please try again later.");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError("Error displaying error message: " + e);
                }
            };


            CoroutineUtils.StartThrowingCoroutine(this,
            issue.PostIssue(description, steps, screenshots, logFiles, false,
                (issueId) => // successful post
                {
                    submitButton.interactable = true;
                    submitButton.GetComponentInChildren<TMP_Text>().text = "Submit";

                    // Clear lists after successful upload
                    _screenshots.Clear();
                    _logFiles.Clear();

                    // show the report submitted UI
                    reportSubmittedUI.Show(issue, defaultEmailAddress);

                    // hide bug report panel
                    bugReportPanel.SetActive(false);
                },
                mediaUploadType,
                (error) =>
                {
                    onIssueError(new ErrorMessage { error = error });
                }
            ),
            (ex) => // done
            {
                if (ex != null)
                {
                    onIssueError(new ErrorMessage { exception = ex });
                }
            });

            submitButton.interactable = false;
            submitButton.GetComponentInChildren<TMP_Text>().text = "Submitting...";
        }

        class ErrorMessage {
            public string error;
            public Exception exception;
        }

        class IssueResponse {
            public string id;
        }
    }
}