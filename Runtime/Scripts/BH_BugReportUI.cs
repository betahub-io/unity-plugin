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

public class BH_BugReportUI : MonoBehaviour
{
    private static BH_BugReportUI instance;
    
    public GameObject bugReportPanel;
    public TMP_InputField descriptionField;
    public TMP_InputField stepsField;
    public Button submitButton;

    public GameObject messagePanel;

    public BH_MessagePanelUI messagePanelUI;

    public string submitEndpoint = "https://app.betahub.io";

    public string projectID;

#if ENABLE_INPUT_SYSTEM
    public InputAction shortcutAction = new InputAction("BugReportShortcut", binding: "<Keyboard>/f12");
#else
    public KeyCode shortcutKey = KeyCode.F12;
#endif

    public bool includePlayerLog = true;

    public bool includeVideo = true;

    private bool _startedRecordingInitially = false;

    public UnityEvent OnBugReportWindowShown;
    public UnityEvent OnBugReportWindowHidden;

    private List<ScreenshotFileReference> _screenshots = new List<ScreenshotFileReference>();
    private List<LogFileReference> _logFiles = new List<LogFileReference>();

    private static BH_Logger logger;
    private bool _cursorStateChanged;
    private CursorLockMode _previousCursorLockMode;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void InitializeLogger()
    {
        logger = new BH_Logger();
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
        bugReportPanel.SetActive(false);
        submitButton.onClick.AddListener(SubmitBugReport);

        if (string.IsNullOrEmpty(projectID))
        {
            Debug.LogError("Project ID is not set. I won't be able to submit bug reports.");
        }

        var gameRecorder = GetComponent<BH_GameRecorder>();
        if (gameRecorder == null)
        {
            Debug.LogWarning("BH_GameRecorder component is not attached to the same GameObject as BH_BugReportUI. Video won't be recorded.");
        }

        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        if (includeVideo && !_startedRecordingInitially) {
            var gameRecorder = GetComponent<BH_GameRecorder>();
            if (gameRecorder != null && !gameRecorder.IsRecording) {
                gameRecorder.StartRecording();
                _startedRecordingInitially = true;
            }
        }
        
#if !ENABLE_INPUT_SYSTEM
        if (shortcutKey != KeyCode.None && Input.GetKeyDown(shortcutKey))
        {
            StartCoroutine(CaptureScreenshotAndShowUI());
        }
#endif
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
        ModifyCursorState();
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

    void SubmitBugReport()
    {
        string description = descriptionField.text;
        string steps = stepsField.text;

        StartCoroutine(PostIssue(description, steps));

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

            submitButton.interactable = true;
            submitButton.GetComponentInChildren<TMP_Text>().text = "Submit";

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
                    RestoreCursorState();
                });

                StartCoroutine(UploadAdditionalFiles(issueId));
            }
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

    IEnumerator UploadAdditionalFiles(string issueId)
    {
        // Upload screenshots

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

        // // Upload performance samples (if any)
        // string samplesFile = Application.persistentDataPath + "/samples.csv";
        // GetComponent<BH_PerformanceSampler>().SaveSamplesToFile(samplesFile);
        // if (File.Exists(samplesFile))
        // {
        //     yield return StartCoroutine(UploadFile(issueId, "log_files", "log_file[file]", samplesFile, "text/csv"));
        //     File.Delete(samplesFile);
        // }

        // Upload video file
        if (includeVideo)
        {
            BH_GameRecorder gameRecorder = GetComponent<BH_GameRecorder>();
            if (gameRecorder != null)
            {
                string videoPath = gameRecorder.StopRecordingAndSaveLastMinute();
                if (!string.IsNullOrEmpty(videoPath) && File.Exists(videoPath))
                {
                    yield return StartCoroutine(UploadFile(issueId, "video_clips", "video_clip[video]", videoPath, "video/mp4"));

                    // Delete the video file after uploading
                    File.Delete(videoPath);
                }
                gameRecorder.StartRecording(); // Restart recording
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

    class ErrorMessage {
        public string error;
        public string status;
    }

    class IssueResponse {
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
