using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.IO;
using System.Collections;
using UnityEngine.Networking;
using UnityEngine.Events;
using System.Collections.Generic;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BetaHub
{
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

        public string submitEndpoint = "https://app.betahub.io";

        public string projectID;

#if ENABLE_INPUT_SYSTEM
        public InputAction shortcutAction = new InputAction("BugReportShortcut", binding: "<Keyboard>/f12");
#else
        public KeyCode shortcutKey = KeyCode.F12;
#endif

        public bool includePlayerLog = true;
        public bool includeVideo = true;

        public UnityEvent OnBugReportWindowShown;
        public UnityEvent OnBugReportWindowHidden;

        private List<ScreenshotFileReference> _screenshots = new List<ScreenshotFileReference>();
        private List<LogFileReference> _logFiles = new List<LogFileReference>();

        private static Logger logger;
        private bool _cursorStateChanged;
        private CursorLockMode _previousCursorLockMode;

        private GameRecorder _gameRecorder;

        // if true, the report is being uploaded, some processes should be paused
        private bool _uploadingReport = false;

        private DefaultSettings _defaultSettings;

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


            messagePanel.SetActive(false);
            SaveDefaultValue();
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
                    Debug.Log("Updated IncludeVideoToggle label to: " + textComponent.text);
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
            return includeVideo && !bugReportPanel.activeSelf && !_uploadingReport;
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

            bugReportPanel.SetActive(true);
        }

        // Sets screenshot path to be uploaded. Useful on manual invocation of bug report UI.
        public void AddScreenshot(string path, bool removeAfterUpload)
        {
            _screenshots.Add(new ScreenshotFileReference { path = path, removeAfterUpload = removeAfterUpload });
        }

        public void AddLogFile(string path, bool removeAfterUpload)
        {
            _logFiles.Add(new LogFileReference { path = path, removeAfterUpload = removeAfterUpload });
        }

        void SubmitBugReport()
        {
            string description = descriptionField.text;
            string steps = stepsField.text;

            _uploadingReport = true;
            CoroutineUtils.StartThrowingCoroutine(this, PostIssue(description, steps), (ex) =>
            {
                _uploadingReport = false;
                bugReportPanel.SetActive(false);
                submitButton.interactable = true;
                submitButton.GetComponentInChildren<TMP_Text>().text = "Submit";

                if (ex != null)
                {
                    Debug.LogError("Error submitting bug report: " + ex);
                    messagePanelUI.ShowMessagePanel("Error", "Error submitting bug report. Please try again later.");
                }
            });

            submitButton.interactable = false;
            submitButton.GetComponentInChildren<TMP_Text>().text = "Submitting...";
        }

        IEnumerator PostIssue(string description, string steps)
        {
            WWWForm form = new WWWForm();
            form.AddField("issue[description]", description);
            form.AddField("issue[unformatted_steps_to_reproduce]", steps);

            string url = submitEndpoint;
            if (!url.EndsWith("/"))
            {
                url += "/";
            }

            url += "projects/" + projectID + "/issues.json";

            using (UnityWebRequest www = UnityWebRequest.Post(url, form))
            {
                www.SetRequestHeader("Authorization", "FormUser " + "anonymous");
                www.SetRequestHeader("BetaHub-Project-ID", projectID);
                www.SetRequestHeader("Accept", "application/json");

                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError(www.error);
                    Debug.LogError("Response code: " + www.responseCode);
                    if (www.responseCode == 422)
                    {
                        Debug.LogError(www.downloadHandler.text);
                        ErrorMessage message = JsonUtility.FromJson<ErrorMessage>(www.downloadHandler.text);
                        messagePanelUI.ShowMessagePanel("Error", message.error);
                    }
                }
                else
                {
                    Debug.Log("Bug report submitted successfully!");
                    string response = www.downloadHandler.text;
                    IssueResponse issueResponse = JsonUtility.FromJson<IssueResponse>(response);
                    string issueId = issueResponse.id;
                    messagePanelUI.ShowMessagePanel("Success", "Bug report submitted successfully!", () =>
                    {
                        bugReportPanel.SetActive(false);
                    });

                    ResetToDefaultValue();
                    yield return UploadAdditionalFiles(issueId);
                }
            }
        }

        IEnumerator UploadAdditionalFiles(string issueId)
        {
            // Upload screenshots
            if (IncludeScreenshotToggle.isOn)
            {
                foreach (var screenshot in _screenshots)
                {
                    if (File.Exists(screenshot.path))
                    {
                        yield return StartCoroutine(UploadFile(issueId, "screenshots", "screenshot[image]", screenshot.path, "image/png"));

                        if (screenshot.removeAfterUpload)
                        {
                            File.Delete(screenshot.path);
                        }
                    }
                }

                _screenshots.Clear();
            }

            if (IncludePlayerLogToggle.isOn)
            {
                // Upload logger log files
                if (includePlayerLog && !string.IsNullOrEmpty(logger.LogPath) && File.Exists(logger.LogPath))
                {
                    // skip if size over 200MB
                    if (new FileInfo(logger.LogPath).Length < 200 * 1024 * 1024)
                    {
                        yield return StartCoroutine(UploadFile(issueId, "log_files", "log_file[file]", logger.LogPath, "text/plain"));
                    }
                }

                // Upload custom log files
                foreach (var logFile in _logFiles)
                {
                    if (File.Exists(logFile.path))
                    {
                        yield return StartCoroutine(UploadFile(issueId, "log_files", "log_file[file]", logFile.path, "text/plain"));

                        if (logFile.removeAfterUpload)
                        {
                            File.Delete(logFile.path);
                        }
                    }
                }

                _logFiles.Clear();
            }

            // // Upload performance samples (if any)
            // string samplesFile = Application.persistentDataPath + "/samples.csv";
            // GetComponent<BH_PerformanceSampler>().SaveSamplesToFile(samplesFile);
            // if (File.Exists(samplesFile))
            // {
            //     yield return StartCoroutine(UploadFile(issueId, "log_files", "log_file[file]", samplesFile, "text/csv"));
            //     File.Delete(samplesFile);
            // }

            // Upload video file
            if (includeVideo && IncludeVideoToggle.isOn)
            {
                GameRecorder gameRecorder = GetComponent<GameRecorder>();
                if (gameRecorder != null)
                {
                    string videoPath = gameRecorder.StopRecordingAndSaveLastMinute();
                    if (!string.IsNullOrEmpty(videoPath) && File.Exists(videoPath))
                    {
                        yield return StartCoroutine(UploadFile(issueId, "video_clips", "video_clip[video]", videoPath, "video/mp4"));

                        // Delete the video file after uploading
                        File.Delete(videoPath);
                    }
                }
            }

            // Final debug message
            Debug.Log("All files uploaded successfully!");
        }

        IEnumerator UploadFile(string issueId, string endpoint, string fieldName, string filePath, string contentType)
        {
            WWWForm form = new WWWForm();
            byte[] fileData = File.ReadAllBytes(filePath);
            form.AddBinaryData(fieldName, fileData, Path.GetFileName(filePath), contentType);

            string url = $"{submitEndpoint}/projects/{projectID}/issues/g-{issueId}/{endpoint}";
            using (UnityWebRequest www = UnityWebRequest.Post(url, form))
            {
                www.SetRequestHeader("Authorization", "FormUser " + "anonymous");
                www.SetRequestHeader("BetaHub-Project-ID", projectID);
                www.SetRequestHeader("Accept", "application/json");

                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"Error uploading {Path.GetFileName(filePath)}: {www.error}");
                }
                else
                {
                    Debug.Log($"{Path.GetFileName(filePath)} uploaded successfully!");
                }
            }
        }

        // Method to save the current values as defaults
        void SaveDefaultValue()
        {
            _defaultSettings = new DefaultSettings(
                descriptionField.text,
                stepsField.text,
                IncludeVideoToggle.isOn,
                IncludeScreenshotToggle.isOn,
                IncludePlayerLogToggle.isOn
            );
        }

        // Method to reset fields to their default values
        void ResetToDefaultValue()
        {
            descriptionField.text = _defaultSettings.description;
            stepsField.text = _defaultSettings.steps;
            IncludeVideoToggle.isOn = _defaultSettings.includeVideo;
            IncludeScreenshotToggle.isOn = _defaultSettings.includeScreenshot;
            IncludePlayerLogToggle.isOn = _defaultSettings.includePlayerLog;
        }

        class ErrorMessage
        {
            public string error;
            public string status;
        }

        class IssueResponse
        {
            public string id;
        }

        struct LogFileReference
        {
            public string path;
            public bool removeAfterUpload;
        }

        struct ScreenshotFileReference
        {
            public string path;
            public bool removeAfterUpload;
        }
    }
}