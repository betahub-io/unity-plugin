namespace BetaHub
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using UnityEngine;
    using UnityEngine.Networking;

    // Represents an issue (persistent or not on BetaHub)
    public class Issue
    {
        // set if the issue is persistent on BetaHub
        public string Id { get; private set; }

        public string Url { get; private set; }

        // returns true if the media upload is complete
        public bool IsMediaUploadComplete { get { return _mediaUploadComplete; } }

        private string _betahubEndpoint;

        private string _projectId;

        // token to create an issue, it's a JWT token
        private string _createIssueAuthToken;

        // token to update an issue, starts with tkn-
        private string _updateIssueAuthToken;

        // State flags for publishing logic
        private bool _mediaUploadComplete = false;
        private bool _publishRequested = false;
        private bool _isPublished = false;

        private GameRecorder _gameRecorder;

        private MessagePanelUI _messagePanelUI;

        private ReportSubmittedUI _reportSubmittedUI;

        private bool _emailMyReport = false;
        
        // Reference structs moved to be nested inside Issue class
        public struct LogFileReference
        {
            public string path;
            public bool removeAfterUpload;
            public Logger logger; // Optional: if set, use logger.ReadLogFileBytes() instead of path
        }

        public struct ScreenshotFileReference
        {
            public string path;
            public bool removeAfterUpload;
        }

        public Issue(string betahubEndpoint, string projectId,
                    string authToken,
                    MessagePanelUI messagePanelUI, ReportSubmittedUI reportSubmittedUI, GameRecorder gameRecorder)
        {
            _betahubEndpoint = betahubEndpoint;
            if (!betahubEndpoint.EndsWith("/"))
            {
                _betahubEndpoint += "/";
            }

            _projectId = projectId;
            _createIssueAuthToken = authToken;
            _messagePanelUI = messagePanelUI;
            _reportSubmittedUI = reportSubmittedUI;

            if (gameRecorder != null)
            {
                _gameRecorder = gameRecorder;
            }

            if (!authToken.StartsWith("tkn-"))
            {
                throw new Exception("Auth token must start with tkn-");
            }

            if (string.IsNullOrEmpty(projectId))
            {
                throw new Exception("Project ID is required");
            }

            if (messagePanelUI == null)
            {
                throw new Exception("Message panel UI is required");
            }

            if (reportSubmittedUI == null)
            {
                throw new Exception("Report submitted UI is required");
            }
        }

        // Posts an issue to BetaHub.
        // The posting flow looks as follows:
        // 1. A draft issue is created with a title and description. This sets the Issue.Id and _updateIssueAuthToken.
        // 2. Media files are uploaded to the issue in the background.
        // 3. The user can optionally post their email address (not implemented here).
        // 4. After media upload finishes, the issue is published if requested (either via autoPublish or a prior call to Publish).
        // Parameters:
        // - description: description of the issue
        // - steps: (optional) steps to reproduce the issue
        // - screenshots: (optional) screenshots of the issue
        // - logFiles: (optional) log files of the issue
        // - autoPublish: (optional) if true, the issue will be requested to be published automatically after media upload.
        public IEnumerator PostIssue(string description, string steps = null,
                                     List<ScreenshotFileReference> screenshots = null, List<LogFileReference> logFiles = null,
                                     int releaseId = 0, string releaseLabel = "",
                                     bool autoPublish = false,
                                     Action<string> onAllMediaUploaded = null, MediaUploadType mediaUploadType = MediaUploadType.UploadInBackground, Action<string> onError = null,
                                     Dictionary<string, string> customFields = null)
        {
            if (Id != null)
            {
                throw new Exception("Issue instance cannot be reused for posting.");
            }

            if (releaseId > 0 && !string.IsNullOrEmpty(releaseLabel))
            {
                throw new Exception("Cannot set both release ID and release label");
            }

            // Initialize state for this posting attempt
            _publishRequested = autoPublish;
            _mediaUploadComplete = false;
            _isPublished = false;

            string issueIdLocal = null; // Use local variables for draft result callback
            string issueUrlLocal = null;
            string updateIssueAuthTokenLocal = null;
            string error = null;

            yield return PostIssueDraft(description, steps, releaseId, releaseLabel, customFields, (draftResult) =>
            {
                issueIdLocal = draftResult.IssueId;
                updateIssueAuthTokenLocal = draftResult.UpdateIssueAuthToken;
                error = draftResult.Error;
                issueUrlLocal = draftResult.Url;
            });

            if (error != null)
            {
                Debug.LogError("Error posting issue: " + error);
                onError?.Invoke(error);
                yield break;
            }

            // Draft created successfully, store ID and token in member variables
            this.Id = issueIdLocal;
            this.Url = issueUrlLocal;
            this._updateIssueAuthToken = updateIssueAuthTokenLocal;

            if (mediaUploadType == MediaUploadType.UploadInBackground)
            {
                onAllMediaUploaded?.Invoke(this.Id); // Notify caller with the persistent ID
            }

            yield return PostAllMedia(screenshots, logFiles, _gameRecorder);

            if (mediaUploadType == MediaUploadType.WaitForUpload)
            {
                onAllMediaUploaded?.Invoke(this.Id); // Notify caller with the persistent ID
            }

            // Mark media as complete and attempt to publish if requested
            yield return MarkMediaCompleteAndTryPublish();
        }

        public IEnumerator SubmitEmail(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                throw new Exception("Email is required");
            }

            // issue must be persistent
            if (string.IsNullOrEmpty(Id))
            {
                throw new Exception("Issue must be persistent before submitting email");
            }

            WWWForm form = new WWWForm();
            form.AddField("email", email);
            using (UnityWebRequest www = UnityWebRequest.Post(GetSubmitEmailUrl(), form))
            {
                www.SetRequestHeader("Authorization", "Bearer " + _updateIssueAuthToken);
                www.SetRequestHeader("BetaHub-Project-ID", _projectId);
                www.SetRequestHeader("Accept", "application/json");
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("Error submitting email: " + www.error);
                }
            }
        }


        // Requests the issue to be published.
        // If media upload is already complete, it publishes immediately.
        // Otherwise, it sets a flag ensuring publication occurs once media upload finishes.
        public IEnumerator Publish(bool emailMyReport)
        {
            _emailMyReport = emailMyReport;
            
            // Request publishing and attempt to publish if conditions are met
            yield return RequestPublishAndTryPublish();
        }

        // Helper method called after media upload completes
        private IEnumerator MarkMediaCompleteAndTryPublish()
        {
            lock (this)
            {
                _mediaUploadComplete = true;
            }
            yield return CheckAndPublishIfReady();
        }

        // Helper method called by the public Publish() method
        private IEnumerator RequestPublishAndTryPublish()
        {
            lock (this)
            {
                _publishRequested = true;
            }
            yield return CheckAndPublishIfReady();
        }

        // Centralized check to publish the issue if all conditions are met
        private IEnumerator CheckAndPublishIfReady()
        {
            bool shouldPublish = false;
            lock (this)
            {
                // Check if publish is requested, media is done, not already published, and we have the necessary ID/token
                if (_publishRequested && _mediaUploadComplete && !_isPublished && !string.IsNullOrEmpty(Id) && !string.IsNullOrEmpty(_updateIssueAuthToken))
                {
                    shouldPublish = true;
                    _isPublished = true; // Prevent duplicate publish attempts
                }
            }

            if (shouldPublish)
            {
                yield return PublishNow();
            }
            // else: Conditions not met yet, publishing will be checked again later if needed.
        }

        // posts a draft issue to BetaHub.
        // returns the issue id and the update issue auth token via callback
        private IEnumerator PostIssueDraft(string description, string steps, int releaseId, string releaseLabel, Dictionary<string, string> customFields, Action<DraftResult> onResult)
        {
            WWWForm form = new WWWForm();
            form.AddField("issue[description]", description);
            form.AddField("issue[unformatted_steps_to_reproduce]", steps);
            form.AddField("draft", "true"); // this enables the draft flow

            if (releaseId > 0)
            {
                form.AddField("issue[release_id]", releaseId);
            }
            else if (!string.IsNullOrEmpty(releaseLabel))
            {
                form.AddField("issue[release_label]", releaseLabel);
            }

            if (customFields != null && customFields.Count > 0)
            {
                foreach (var kvp in customFields)
                {
                    form.AddField($"issue[custom_fields][{kvp.Key}]", kvp.Value);
                }
            }

            string url = GetPostIssueUrl();
            
            using (UnityWebRequest www = UnityWebRequest.Post(url, form))
            {
                www.SetRequestHeader("Authorization", "FormUser " + _createIssueAuthToken);
                www.SetRequestHeader("BetaHub-Project-ID", _projectId);
                www.SetRequestHeader("Accept", "application/json");
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    string errorMessage = www.downloadHandler.text;
                    // try parsing as json, the format should be {"error":"...","status":"..."}
                    try {
                        ErrorMessage errorMessageObject = JsonUtility.FromJson<ErrorMessage>(errorMessage);
                        if (errorMessageObject != null)
                        {
                            errorMessage = errorMessageObject.error;
                        } else {
                            errorMessage = "Unknown error";
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("Error parsing error message: " + e);
                    }

                    Debug.LogError("Response code: " + www.responseCode);
                    onResult?.Invoke(new DraftResult { IssueId = null, UpdateIssueAuthToken = null, Url = null, Error = errorMessage });
                    yield break;
                }

                string response = www.downloadHandler.text;
                IssueResponse issueResponse = JsonUtility.FromJson<IssueResponse>(response);
                onResult?.Invoke(new DraftResult { IssueId = issueResponse.id, UpdateIssueAuthToken = issueResponse.token, Url = issueResponse.url, Error = null });
            }
        }

        private IEnumerator PostAllMedia(List<ScreenshotFileReference> screenshots, List<LogFileReference> logFiles, GameRecorder gameRecorder)
        {
            if (screenshots != null)
            {
                Debug.Log("Posting " + screenshots.Count + " screenshots");
                
                foreach (var screenshot in screenshots)
                {
                    yield return PostScreenshot(screenshot);
                }
            }
            else
            {
                Debug.Log("No screenshots to post");
            }
            
            if (logFiles != null)
            {
                Debug.Log("Posting " + logFiles.Count + " log files");
                foreach (var logFile in logFiles)
                {
                    yield return PostLogFile(logFile);
                }
            }
            else
            {
                Debug.Log("No log files to post");
            }

            if (gameRecorder != null)
            {
                Debug.Log("Posting video");
                yield return PostVideo(gameRecorder);
            }
            else
            {
                Debug.Log("No video to post");
            }
        }

        private string GetPostIssueUrl()
        {
            return _betahubEndpoint + "projects/" + _projectId + "/issues.json";
        }

        private string GetSubmitEmailUrl()
        {
            return _betahubEndpoint + "projects/" + _projectId + "/issues/g-" + Id + "/set_reporter_email";
        }
        
        private IEnumerator PostScreenshot(ScreenshotFileReference screenshot)
        {
            if (File.Exists(screenshot.path))
            {
                yield return UploadFile("screenshots", "screenshot[image]", screenshot.path, "image/png");

                if (screenshot.removeAfterUpload)
                {
                    File.Delete(screenshot.path);
                }
            }
        }
        
        private IEnumerator PostLogFile(LogFileReference logFile)
        {
            if (logFile.logger != null)
            {
                // Use logger's safe read method to avoid sharing violations
                Debug.Log("Reading BetaHub log file safely using Logger instance");
                byte[] fileData = logFile.logger.ReadLogFileBytes();

                if (fileData != null)
                {
                    string fileName = Path.GetFileName(logFile.logger.LogPath) ?? "BH_Player.log";
                    yield return UploadStringAsFile("log_files", "log_file[file]", fileData, fileName, "text/plain");
                }
                else
                {
                    Debug.LogError("Failed to read log file data from Logger instance");
                }
            }
            else if (File.Exists(logFile.path))
            {
                // Original file path logic
                yield return UploadFile("log_files", "log_file[file]", logFile.path, "text/plain");

                if (logFile.removeAfterUpload)
                {
                    File.Delete(logFile.path);
                }
            }
        }
        
        private IEnumerator PostVideo(GameRecorder gameRecorder)
        {
            if (gameRecorder != null)
            {
                string videoPath = gameRecorder.StopRecordingAndSaveLastMinute();
                if (!string.IsNullOrEmpty(videoPath) && File.Exists(videoPath))
                {
                    yield return UploadFile("video_clips", "video_clip[video]", videoPath, "video/mp4");

                    // Delete the video file after uploading
                    File.Delete(videoPath);
                }
            }
        }
        
        private IEnumerator UploadFile(string endpoint, string fieldName, string filePath, string contentType)
        {
            bool isLogFile = Path.GetExtension(filePath).Equals(".log", StringComparison.OrdinalIgnoreCase);
            if (isLogFile)
            {
                BugReportUI.PauseLogger();
                yield return new WaitForSeconds(0.1f);
            }
            
            byte[] fileData;
            try
            {
                fileData = File.ReadAllBytes(filePath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error reading file {filePath}: {ex.Message}");
                if (isLogFile)
                {
                    BugReportUI.ResumeLogger();
                }
                yield break;
            }
            
            if (isLogFile)
            {
                BugReportUI.ResumeLogger();
            }
            
            WWWForm form = new WWWForm();
            form.AddBinaryData(fieldName, fileData, Path.GetFileName(filePath), contentType);

            string url = $"{_betahubEndpoint}projects/{_projectId}/issues/g-{Id}/{endpoint}";
            using (UnityWebRequest www = UnityWebRequest.Post(url, form))
            {
                www.SetRequestHeader("Authorization", "Bearer " + _updateIssueAuthToken);
                www.SetRequestHeader("BetaHub-Project-ID", _projectId);
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

        private IEnumerator UploadStringAsFile(string endpoint, string fieldName, byte[] fileData, string fileName, string contentType)
        {
            if (fileData == null)
            {
                Debug.LogError($"Cannot upload {fileName}: file data is null");
                yield break;
            }

            WWWForm form = new WWWForm();
            form.AddBinaryData(fieldName, fileData, fileName, contentType);

            string url = $"{_betahubEndpoint}projects/{_projectId}/issues/g-{Id}/{endpoint}";
            using (UnityWebRequest www = UnityWebRequest.Post(url, form))
            {
                www.SetRequestHeader("Authorization", "Bearer " + _updateIssueAuthToken);
                www.SetRequestHeader("BetaHub-Project-ID", _projectId);
                www.SetRequestHeader("Accept", "application/json");

                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"Error uploading {fileName}: {www.error}");
                }
                else
                {
                    Debug.Log($"{fileName} uploaded successfully!");
                }
            }
        }

        private IEnumerator PublishNow()
        {
            // Ensure we have valid parameters before proceeding
            if (string.IsNullOrEmpty(Id) || string.IsNullOrEmpty(_updateIssueAuthToken))
            {
                Debug.LogError("Cannot publish issue: Missing Issue ID or Update Token.");
                yield break;
            }

            WWWForm form = new WWWForm();
            form.AddField("email_my_report", _emailMyReport.ToString());

            string url = $"{_betahubEndpoint}projects/{_projectId}/issues/g-{Id}/publish";
            using (UnityWebRequest www = UnityWebRequest.Post(url, form)) // POST request with empty body
            {
                www.SetRequestHeader("Authorization", "Bearer " + _updateIssueAuthToken);
                www.SetRequestHeader("BetaHub-Project-ID", _projectId);
                www.SetRequestHeader("Accept", "application/json");

                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("Error publishing issue: " + www.error);
                }
                else
                {
                    Debug.Log("Issue published successfully!");

                    if (_projectId == BugReportUI.DEMO_PROJECT_ID)
                    {
                        Debug.Log("Demo project published issue: " + Url);
                    }
                }
            }
        }
        
        
        private class ErrorMessage 
        {
            public string error;
            public string status;
        }

        private class IssueResponse 
        {
            public string id;
            public string token;
            public string url;
        }

        private struct DraftResult {
            public string IssueId;
            public string UpdateIssueAuthToken;
            public string Error;
            public string Url;
        }
    }
}