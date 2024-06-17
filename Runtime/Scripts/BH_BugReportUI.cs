using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.IO;
using System.Collections;
using UnityEngine.Networking;
using UnityEngine.Events;

public class BH_BugReportUI : MonoBehaviour
{
    public GameObject bugReportPanel;
    public TMP_InputField descriptionField;
    public TMP_InputField stepsField;
    public Button submitButton;

    public GameObject messagePanel;

    public BH_MessagePanelUI messagePanelUI;

    public string submitEndpoint = "https://app.betahub.io";

    public string projectID;

    public KeyCode shortcutKey = KeyCode.F12;

    public UnityEvent OnBugReportWindowShown;
    public UnityEvent OnBugReportWindowHidden;

    private string screenshotPath;
    private static BH_Logger logger;
    private bool _cursorStateChanged;
    private CursorLockMode _previousCursorLockMode;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void InitializeLogger()
    {
        logger = new BH_Logger();
    }
    void Start()
    {
        bugReportPanel.SetActive(false);
        submitButton.onClick.AddListener(SubmitBugReport);

        if (string.IsNullOrEmpty(projectID))
        {
            Debug.LogError("Project ID is not set. I won't be able to submit bug reports.");
        }
    }

    void Update()
    {
        if (shortcutKey != KeyCode.None && Input.GetKeyDown(shortcutKey)) // Shortcut key to open bug report UI
        {
            // Capture screenshot
            screenshotPath = Application.persistentDataPath + "/screenshot.png";
            ScreenCapture.CaptureScreenshot(screenshotPath);
            
            bugReportPanel.SetActive(true);

            ModifyCursorState();
        }
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
        // Upload screenshot
        if (File.Exists(screenshotPath))
        {
            yield return StartCoroutine(UploadFile(issueId, "screenshots", "screenshot[image]", screenshotPath, "image/png"));
        }

        // Upload log files
        if (!string.IsNullOrEmpty(logger.LogPath) && File.Exists(logger.LogPath))
        {
            // skip if size over 200MB
            if (new FileInfo(logger.LogPath).Length < 200 * 1024 * 1024)
            {
                yield return StartCoroutine(UploadFile(issueId, "log_files", "log_file[file]", logger.LogPath, "text/plain"));
            }
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
        BH_GameRecorder gameRecorder = GetComponent<BH_GameRecorder>();
        string videoPath = gameRecorder.StopRecordingAndSaveLastMinute();
        if (!string.IsNullOrEmpty(videoPath) && File.Exists(videoPath))
        {
            yield return StartCoroutine(UploadFile(issueId, "video_clips", "video_clip[video]", videoPath, "video/mp4"));

            // Delete the video file after uploading
            File.Delete(videoPath);
        }

        gameRecorder.StartRecording(); // Restart recording

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
}
