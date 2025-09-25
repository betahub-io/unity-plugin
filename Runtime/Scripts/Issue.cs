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

        // Contextual diagnostics service for consistent error handling and logging
        private IContextualDiagnostics _diagnostics;

        private void InitializeDiagnostics()
        {
            string issueContext = string.IsNullOrEmpty(Id) ? "Draft" : Id;
            _diagnostics = BetaHubDiagnostics.ForContext($"Issue({issueContext})");
            if (_diagnostics == null)
            {
                Debug.LogWarning("Failed to initialize diagnostics");
            }
        }

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
                throw new ArgumentException("Auth token must start with tkn-", nameof(authToken));
            }

            if (string.IsNullOrEmpty(projectId))
            {
                throw new ArgumentException("Project ID is required", nameof(projectId));
            }

            if (messagePanelUI == null)
            {
                throw new ArgumentNullException(nameof(messagePanelUI), "Message panel UI is required");
            }

            if (reportSubmittedUI == null)
            {
                throw new ArgumentNullException(nameof(reportSubmittedUI), "Report submitted UI is required");
            }

            // Initialize diagnostics for this issue instance
            InitializeDiagnostics();
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
                                     Action<string> onAllMediaUploaded = null, MediaUploadType mediaUploadType = MediaUploadType.UploadInBackground,
                                     Dictionary<string, string> customFields = null)
        {
            yield return PostIssueInternal(description, steps, screenshots, logFiles, releaseId, releaseLabel, autoPublish, onAllMediaUploaded, mediaUploadType, customFields);
        }

        private IEnumerator PostIssueInternal(string description, string steps,
                                             List<ScreenshotFileReference> screenshots, List<LogFileReference> logFiles,
                                             int releaseId, string releaseLabel,
                                             bool autoPublish,
                                             Action<string> onAllMediaUploaded, MediaUploadType mediaUploadType,
                                             Dictionary<string, string> customFields)
        {
            if (Id != null)
            {
                throw new InvalidOperationException("Issue instance cannot be reused for posting.");
            }

            if (releaseId > 0 && !string.IsNullOrEmpty(releaseLabel))
            {
                throw new ArgumentException("Cannot set both release ID and release label");
            }

            // Initialize state for this posting attempt
            _publishRequested = autoPublish;
            _mediaUploadComplete = false;
            _isPublished = false;

            yield return PostIssueDraft(description, steps, releaseId, releaseLabel, customFields);

            // Draft created successfully, store ID and token in member variables
            this.Id = _lastDraftResult.IssueId;
            this.Url = _lastDraftResult.Url;
            this._updateIssueAuthToken = _lastDraftResult.UpdateIssueAuthToken;

            // Update diagnostics with the new issue ID
            InitializeDiagnostics();

            _diagnostics?.LogSuccess("PostIssueDraft", $"Created draft issue with ID {this.Id}");

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

            _diagnostics?.LogSuccess("PostIssue", "Issue posting flow completed successfully");
        }

        public IEnumerator SubmitEmail(string email)
        {
            yield return SubmitEmailInternal(email);
        }

        private IEnumerator SubmitEmailInternal(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                throw new ArgumentException("Email is required", nameof(email));
            }

            // issue must be persistent
            if (string.IsNullOrEmpty(Id))
            {
                throw new InvalidOperationException("Issue must be persistent before submitting email");
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
                    throw new InvalidOperationException($"Error submitting email (HTTP {www.responseCode}): {www.error}");
                }
            }

            _diagnostics?.LogSuccess("SubmitEmail", $"Email {email} submitted successfully");
        }


        // Requests the issue to be published.
        // If media upload is already complete, it publishes immediately.
        // Otherwise, it sets a flag ensuring publication occurs once media upload finishes.
        public IEnumerator Publish(bool emailMyReport)
        {
            yield return PublishInternal(emailMyReport);
        }

        private IEnumerator PublishInternal(bool emailMyReport)
        {
            _emailMyReport = emailMyReport;

            // Request publishing and attempt to publish if conditions are met
            yield return RequestPublishAndTryPublish();

            _diagnostics?.LogSuccess("Publish", "Publish request completed successfully");
        }

        // Helper method called after media upload completes
        private IEnumerator MarkMediaCompleteAndTryPublish()
        {
            bool lockTaken = false;
            try
            {
                System.Threading.Monitor.TryEnter(this, 1000, ref lockTaken); // 1 second timeout
                if (lockTaken)
                {
                    _mediaUploadComplete = true;
                }
                else
                {
                    throw new TimeoutException("Failed to acquire lock for MarkMediaCompleteAndTryPublish within timeout");
                }
            }
            finally
            {
                if (lockTaken)
                {
                    System.Threading.Monitor.Exit(this);
                }
            }
            yield return CheckAndPublishIfReady();
        }

        // Helper method called by the public Publish() method
        private IEnumerator RequestPublishAndTryPublish()
        {
            bool lockTaken = false;
            try
            {
                System.Threading.Monitor.TryEnter(this, 1000, ref lockTaken); // 1 second timeout
                if (lockTaken)
                {
                    _publishRequested = true;
                }
                else
                {
                    throw new TimeoutException("Failed to acquire lock for RequestPublishAndTryPublish within timeout");
                }
            }
            finally
            {
                if (lockTaken)
                {
                    System.Threading.Monitor.Exit(this);
                }
            }
            yield return CheckAndPublishIfReady();
        }

        // Centralized check to publish the issue if all conditions are met
        private IEnumerator CheckAndPublishIfReady()
        {
            bool shouldPublish = false;
            bool lockTaken = false;
            try
            {
                System.Threading.Monitor.TryEnter(this, 1000, ref lockTaken); // 1 second timeout
                if (lockTaken)
                {
                    // Check if publish is requested, media is done, not already published, and we have the necessary ID/token
                    if (_publishRequested && _mediaUploadComplete && !_isPublished && !string.IsNullOrEmpty(Id) && !string.IsNullOrEmpty(_updateIssueAuthToken))
                    {
                        shouldPublish = true;
                        _isPublished = true; // Prevent duplicate publish attempts
                    }
                }
                else
                {
                    throw new TimeoutException("Failed to acquire lock for CheckAndPublishIfReady within timeout");
                }
            }
            finally
            {
                if (lockTaken)
                {
                    System.Threading.Monitor.Exit(this);
                }
            }

            if (shouldPublish)
            {
                yield return PublishNow();
                _diagnostics?.LogSuccess("PublishNow", "Issue published successfully");
            }
            // else: Conditions not met yet, publishing will be checked again later if needed.
        }

        // posts a draft issue to BetaHub.
        // stores the draft result in _lastDraftResult field
        // throws exceptions on failure
        private DraftResult _lastDraftResult;
        private IEnumerator PostIssueDraft(string description, string steps, int releaseId, string releaseLabel, Dictionary<string, string> customFields)
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
                    try
                    {
                        ErrorMessage errorMessageObject = JsonUtility.FromJson<ErrorMessage>(errorMessage);
                        if (errorMessageObject != null)
                        {
                            errorMessage = errorMessageObject.error;
                        }
                        else
                        {
                            errorMessage = "Unknown error";
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("Error parsing error message: " + e);
                    }

                    throw new InvalidOperationException($"Failed to create issue draft (HTTP {www.responseCode}): {errorMessage}");
                }

                string response = www.downloadHandler.text;
                IssueResponse issueResponse = JsonUtility.FromJson<IssueResponse>(response);
                _lastDraftResult = new DraftResult { IssueId = issueResponse.id, UpdateIssueAuthToken = issueResponse.token, Url = issueResponse.url };
            }
        }

        private IEnumerator PostAllMedia(List<ScreenshotFileReference> screenshots, List<LogFileReference> logFiles, GameRecorder gameRecorder)
        {
            int totalFiles = (screenshots?.Count ?? 0) + (logFiles?.Count ?? 0) + (gameRecorder != null ? 1 : 0);
            int uploadedFiles = 0;

            _diagnostics?.LogInfo("PostAllMedia", $"Starting upload of {totalFiles} media files");

            if (screenshots != null && screenshots.Count > 0)
            {
                _diagnostics?.LogProgress("PostAllMedia", $"Uploading {screenshots.Count} screenshots");

                for (int i = 0; i < screenshots.Count; i++)
                {
                    var screenshot = screenshots[i];
                    yield return TryPostScreenshot(screenshot, ++uploadedFiles, totalFiles);
                }
            }

            if (logFiles != null && logFiles.Count > 0)
            {
                _diagnostics?.LogProgress("PostAllMedia", $"Uploading {logFiles.Count} log files");

                for (int i = 0; i < logFiles.Count; i++)
                {
                    var logFile = logFiles[i];
                    yield return TryPostLogFile(logFile, ++uploadedFiles, totalFiles);
                }
            }

            if (gameRecorder != null)
            {
                _diagnostics?.LogProgress("PostAllMedia", "Uploading video");
                yield return TryPostVideo(gameRecorder, ++uploadedFiles, totalFiles);
            }

            _diagnostics?.LogSuccess("PostAllMedia", $"All {totalFiles} media files uploaded successfully");
        }


        private string GetPostIssueUrl()
        {
            return _betahubEndpoint + "projects/" + _projectId + "/issues.json";
        }

        private string GetSubmitEmailUrl()
        {
            return _betahubEndpoint + "projects/" + _projectId + "/issues/g-" + Id + "/set_reporter_email";
        }

        // Wrapper methods to handle try-catch with yield returns
        private IEnumerator TryPostScreenshot(ScreenshotFileReference screenshot, int currentFile, int totalFiles)
        {
            IEnumerator coroutine = PostScreenshot(screenshot, currentFile, totalFiles);
            bool hasMore = true;
            
            while (hasMore)
            {
                try
                {
                    hasMore = coroutine.MoveNext();
                    if (!hasMore)
                        break;
                }
                catch (Exception ex)
                {
                    _diagnostics?.LogError("PostAllMedia", $"Failed to upload screenshot", ex);
                    yield break; // Exit the coroutine on error
                }
                
                yield return coroutine.Current;
            }
        }

        private IEnumerator TryPostLogFile(LogFileReference logFile, int currentFile, int totalFiles)
        {
            IEnumerator coroutine = PostLogFile(logFile, currentFile, totalFiles);
            bool hasMore = true;
            
            while (hasMore)
            {
                try
                {
                    hasMore = coroutine.MoveNext();
                    if (!hasMore)
                        break;
                }
                catch (Exception ex)
                {
                    _diagnostics?.LogError("PostAllMedia", $"Failed to upload log file", ex);
                    yield break; // Exit the coroutine on error
                }
                
                yield return coroutine.Current;
            }
        }

        private IEnumerator TryPostVideo(GameRecorder gameRecorder, int currentFile, int totalFiles)
        {
            IEnumerator coroutine = PostVideo(gameRecorder, currentFile, totalFiles);
            bool hasMore = true;
            
            while (hasMore)
            {
                try
                {
                    hasMore = coroutine.MoveNext();
                    if (!hasMore)
                        break;
                }
                catch (Exception ex)
                {
                    _diagnostics?.LogError("PostAllMedia", $"Failed to upload video", ex);
                    yield break; // Exit the coroutine on error
                }
                
                yield return coroutine.Current;
            }
        }

        private IEnumerator PostScreenshot(ScreenshotFileReference screenshot, int currentFile, int totalFiles)
        {
            if (!File.Exists(screenshot.path))
            {
                throw new FileNotFoundException($"Screenshot file not found: {screenshot.path}");
            }

            yield return UploadFile("screenshots", "screenshot[image]", screenshot.path, "image/png");

            if (screenshot.removeAfterUpload)
            {
                File.Delete(screenshot.path);
            }

            _diagnostics?.LogProgress("PostScreenshot", $"Screenshot {Path.GetFileName(screenshot.path)} uploaded ({currentFile}/{totalFiles})");
        }

        private IEnumerator PostLogFile(LogFileReference logFile, int currentFile, int totalFiles)
        {
            string fileName;

            if (logFile.logger != null)
            {
                byte[] fileData = logFile.logger.ReadLogFileBytes();

                if (fileData == null)
                {
                    throw new InvalidOperationException("Failed to read log file data from Logger instance");
                }

                fileName = Path.GetFileName(logFile.logger.LogPath) ?? "BH_Player.log";
                yield return UploadStringAsFile("log_files", "log_file[file]", fileData, fileName, "text/plain");
            }
            else if (File.Exists(logFile.path))
            {
                // Original file path logic
                fileName = Path.GetFileName(logFile.path);
                yield return UploadFile("log_files", "log_file[file]", logFile.path, "text/plain");

                if (logFile.removeAfterUpload)
                {
                    File.Delete(logFile.path);
                }
            }
            else
            {
                throw new FileNotFoundException($"Log file not found: {logFile.path}");
            }

            _diagnostics?.LogProgress("PostLogFile", $"Log file {fileName} uploaded ({currentFile}/{totalFiles})");
        }

        private IEnumerator PostVideo(GameRecorder gameRecorder, int currentFile, int totalFiles)
        {
            string videoPath = gameRecorder.StopRecordingAndSaveLastMinute();
            if (string.IsNullOrEmpty(videoPath))
            {
                throw new InvalidOperationException("GameRecorder failed to produce video file");
            }

            if (!File.Exists(videoPath))
            {
                throw new FileNotFoundException($"Video file not found: {videoPath}");
            }

            yield return UploadFile("video_clips", "video_clip[video]", videoPath, "video/mp4");

            // Delete the video file after uploading
            File.Delete(videoPath);

            _diagnostics?.LogProgress("PostVideo", $"Video uploaded ({currentFile}/{totalFiles})");
        }

        private IEnumerator UploadFile(string endpoint, string fieldName, string filePath, string contentType)
        {
            bool isLogFile = Path.GetExtension(filePath).Equals(".log", StringComparison.OrdinalIgnoreCase);
            byte[] fileData;

            if (isLogFile)
            {
                if (BugReportUI.Logger != null && filePath == BugReportUI.Logger.LogPath)
                {
                    fileData = BugReportUI.Logger.ReadLogFileBytes();
                    if (fileData == null)
                    {
                        throw new InvalidOperationException("Failed to read log file data from Logger instance");
                    }
                }
                else
                {
                    BugReportUI.PauseLogger();

                    try
                    {
                        fileData = File.ReadAllBytes(filePath);
                    }
                    catch (Exception ex)
                    {
                        BugReportUI.ResumeLogger();
                        throw new InvalidOperationException($"Error reading file {filePath}: {ex.Message}", ex);
                    }

                    BugReportUI.ResumeLogger();
                }
            }
            else
            {
                // For non-log files, read normally
                try
                {
                    fileData = File.ReadAllBytes(filePath);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Error reading file {filePath}: {ex.Message}", ex);
                }
            }

            yield return UploadStringAsFile(endpoint, fieldName, fileData, Path.GetFileName(filePath), contentType);
        }

        private IEnumerator UploadStringAsFile(string endpoint, string fieldName, byte[] fileData, string fileName, string contentType)
        {
            if (fileData == null)
            {
                throw new ArgumentNullException(nameof(fileData), $"Cannot upload {fileName}: file data is null");
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
                    throw new InvalidOperationException($"Error uploading {fileName} (HTTP {www.responseCode}): {www.error}");
                }
                // Success logging handled by calling method
            }
        }

        private IEnumerator PublishNow()
        {
            // Ensure we have valid parameters before proceeding
            if (string.IsNullOrEmpty(Id) || string.IsNullOrEmpty(_updateIssueAuthToken))
            {
                throw new InvalidOperationException("Cannot publish issue: Missing Issue ID or Update Token.");
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
                    throw new InvalidOperationException($"Error publishing issue (HTTP {www.responseCode}): {www.error}");
                }
                // Success logging handled by calling method
                if (_projectId == BugReportUI.DEMO_PROJECT_ID)
                {
                    _diagnostics?.LogInfo("PublishNow", "Demo project published issue: " + Url);
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

        private struct DraftResult
        {
            public string IssueId;
            public string UpdateIssueAuthToken;
            public string Url;
        }
    }
}