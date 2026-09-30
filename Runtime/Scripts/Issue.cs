namespace BetaHub
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using UnityEngine;
    using UnityEngine.Networking;

    // HTTP client for one BetaHub issue: draft creation, media upload, reporter email and publish.
    // Workflow state (what to publish when) lives in ReportSubmission; this class only talks to the server.
    public class Issue : IIssueClient
    {
        private const string UnknownErrorMessage = "Unknown error";

        // set if the issue is persistent on BetaHub
        public string Id { get; private set; }

        public string Url { get; private set; }

        string IIssueClient.IssueId => Id;
        string IIssueClient.IssueUrl => Url;

        private string _betahubEndpoint;

        private string _projectId;

        // token to create an issue, it's a JWT token
        private string _createIssueAuthToken;

        // token to update an issue, starts with tkn-
        private string _updateIssueAuthToken;

        private GameRecorder _gameRecorder;

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

        public struct VideoFileReference
        {
            public string path;
            public bool removeAfterUpload;
        }

        /// <param name="gameRecorder">When set, the recorder's last minute is stopped, saved and uploaded with the media.</param>
        public Issue(string betahubEndpoint, string projectId, string authToken, GameRecorder gameRecorder = null)
        {
            if (string.IsNullOrEmpty(betahubEndpoint))
            {
                throw new ArgumentException("BetaHub endpoint is required", nameof(betahubEndpoint));
            }
            if (string.IsNullOrEmpty(projectId))
            {
                throw new ArgumentException("Project ID is required", nameof(projectId));
            }
            if (string.IsNullOrEmpty(authToken))
            {
                throw new ArgumentException("Auth token is required", nameof(authToken));
            }

            _betahubEndpoint = betahubEndpoint.EndsWith("/") ? betahubEndpoint : betahubEndpoint + "/";
            _projectId = projectId;
            _createIssueAuthToken = authToken;
            _gameRecorder = gameRecorder;
        }

        // Creates the draft issue. On success Id, Url and the update token are set.
        public IEnumerator CreateDraft(BugReportDraft draft, ReleaseTarget release, Action<string> onError)
        {
            if (Id != null)
            {
                throw new InvalidOperationException("Issue instance cannot be reused for posting.");
            }
            if (release.HasId && release.HasLabel)
            {
                throw new ArgumentException("Cannot set both release ID and release label");
            }

            DraftResult result = default;
            yield return PostIssueDraft(draft.Description, draft.Steps, release.Id, release.Label, draft.CustomFields, r => result = r);

            if (result.Error != null)
            {
                onError?.Invoke(result.Error);
                yield break;
            }

            Id = result.IssueId;
            Url = result.Url;
            _updateIssueAuthToken = result.UpdateIssueAuthToken;
        }

        // Uploads screenshots, logs, the recording (if any) and extra videos. Individual upload
        // failures are logged and reported to the server as client events; they do not abort the report.
        public IEnumerator UploadMedia(BugReportDraft draft)
        {
            RequirePersisted("upload media");
            yield return PostAllMedia(draft.Screenshots, draft.LogFiles, draft.IncludeRecording ? _gameRecorder : null, draft.Videos);
        }

        public IEnumerator SetReporterEmail(string email, Action<string> onError)
        {
            if (string.IsNullOrEmpty(email))
            {
                throw new ArgumentException("Email is required", nameof(email));
            }
            RequirePersisted("set reporter email");

            WWWForm form = new WWWForm();
            form.AddField("email", email);

            using (UnityWebRequest www = UnityWebRequest.Post(GetSubmitEmailUrl(), form))
            {
                SetUpdateHeaders(www);
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("Error submitting email: " + www.error + "\nResponse body: " + www.downloadHandler?.text);
                    onError?.Invoke(ExtractErrorMessage(www));
                }
            }
        }

        public IEnumerator Publish(bool emailMyReport, Action<string> onError)
        {
            RequirePersisted("publish");

            WWWForm form = new WWWForm();
            form.AddField("email_my_report", emailMyReport.ToString());

            string url = $"{_betahubEndpoint}projects/{_projectId}/issues/g-{Id}/publish";
            using (UnityWebRequest www = UnityWebRequest.Post(url, form))
            {
                SetUpdateHeaders(www);
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("Error publishing issue: " + www.error + "\nResponse body: " + www.downloadHandler?.text);
                    onError?.Invoke(ExtractErrorMessage(www));
                    yield break;
                }

                Debug.Log("Issue published successfully!");
                if (_projectId == BetaHubReporter.DEMO_PROJECT_ID)
                {
                    Debug.Log("Demo project published issue: " + Url);
                }
            }
        }

        private void RequirePersisted(string action)
        {
            if (string.IsNullOrEmpty(Id) || string.IsNullOrEmpty(_updateIssueAuthToken))
            {
                throw new InvalidOperationException($"Cannot {action}: the draft issue has not been created yet.");
            }
        }

        private void SetUpdateHeaders(UnityWebRequest www)
        {
            www.SetRequestHeader("Authorization", "Bearer " + _updateIssueAuthToken);
            www.SetRequestHeader("BetaHub-Project-ID", _projectId);
            www.SetRequestHeader("Accept", "application/json");
        }

        // Server errors arrive as {"error":"...","status":"..."}; fall back to the transport error.
        private static string ExtractErrorMessage(UnityWebRequest www)
        {
            string body = www.downloadHandler?.text;
            if (!string.IsNullOrEmpty(body))
            {
                try
                {
                    ErrorMessage parsed = JsonUtility.FromJson<ErrorMessage>(body);
                    if (parsed != null && !string.IsNullOrEmpty(parsed.error))
                    {
                        return parsed.error;
                    }
                }
                catch (Exception)
                {
                    // not JSON, fall through
                }
            }
            return string.IsNullOrEmpty(www.error) ? UnknownErrorMessage : www.error;
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
                    string errorMessage = ExtractErrorMessage(www);
                    Debug.LogError("Response code: " + www.responseCode);
                    onResult?.Invoke(new DraftResult { IssueId = null, UpdateIssueAuthToken = null, Url = null, Error = errorMessage });
                    yield break;
                }

                string response = www.downloadHandler.text;
                IssueResponse issueResponse = JsonUtility.FromJson<IssueResponse>(response);
                onResult?.Invoke(new DraftResult { IssueId = issueResponse.id, UpdateIssueAuthToken = issueResponse.token, Url = issueResponse.url, Error = null });
            }
        }

        private IEnumerator PostAllMedia(List<ScreenshotFileReference> screenshots, List<LogFileReference> logFiles, GameRecorder gameRecorder, List<VideoFileReference> videos = null)
        {
            PostClientEvent("info", "upload_started", "Media upload started");

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
#if UNITY_WEBGL && !UNITY_EDITOR
                yield return PostVideoSegments(gameRecorder);
#else
                yield return PostVideo(gameRecorder);
#endif
            }
            else
            {
                Debug.Log("No video to post");
            }

            if (videos != null && videos.Count > 0)
            {
                Debug.Log("Posting " + videos.Count + " additional video files");
                foreach (var video in videos)
                {
                    yield return PostVideoFile(video);
                }
            }

            PostClientEvent("success", "upload_completed", "Media upload completed");
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
        
#if UNITY_WEBGL && !UNITY_EDITOR
        private IEnumerator PostVideoSegments(GameRecorder gameRecorder)
        {
            if (gameRecorder == null) yield break;

            // Stop recording — this sets IsRecording=false and triggers async finalization in JS
            gameRecorder.StopRecordingAndSaveLastMinute();

            // Wait for JS-side async finalization to complete (encoder flush, muxer finalize)
            // BetaHubRecorder_IsRecording() returns 0 once the JS side is fully done
            float timeout = 5f;
            float elapsed = 0f;
            while (WebGLRecorderBridge.BetaHubRecorder_IsRecording() != 0 && elapsed < timeout)
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }

            if (elapsed >= timeout)
            {
                Debug.LogWarning("Timed out waiting for WebGL recording to finalize");
                PostClientEvent("warning", "recording_error", "Timed out waiting for WebGL recording to finalize");
            }

            var segments = WebGLRecorderBridge.CollectSegments();
            if (segments == null || segments.Count == 0)
            {
                Debug.Log("No video segments to upload");
                PostClientEvent("warning", "recording_error", "No video segments available after recording");
                yield break;
            }

            Debug.Log($"Uploading {segments.Count} video segments");
            string mergeGroup = System.Guid.NewGuid().ToString();
            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                string fileName = $"recording-segment-{i}.{segment.FileExtension}";
                var extraFields = new Dictionary<string, string>
                {
                    { "video_segment[position]", i.ToString() },
                    { "video_segment[merge_group]", mergeGroup },
                    { "video_segment[merge_total]", segments.Count.ToString() },
                };
                yield return UploadStringAsFile("video_segments", "video_segment[video]", segment.Data, fileName, segment.ContentType, extraFields);
            }
        }
#else
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
                else
                {
                    PostClientEvent("warning", "recording_error", "Video recording produced no output file");
                }
            }
        }
#endif

        private IEnumerator PostVideoFile(VideoFileReference video)
        {
            if (File.Exists(video.path))
            {
                yield return UploadFile("video_clips", "video_clip[video]", video.path, "video/mp4");

                if (video.removeAfterUpload)
                {
                    File.Delete(video.path);
                }
            }
        }
        
        private IEnumerator UploadFile(string endpoint, string fieldName, string filePath, string contentType)
        {
            bool isLogFile = Path.GetExtension(filePath).Equals(".log", StringComparison.OrdinalIgnoreCase);
            byte[] fileData;
            
            if (isLogFile)
            {
                if (BetaHubReporter.Logger != null && filePath == BetaHubReporter.Logger.LogPath)
                {
                    Debug.Log("Reading log file safely using Logger instance");
                    fileData = BetaHubReporter.Logger.ReadLogFileBytes();
                    if (fileData == null)
                    {
                        Debug.LogError("Failed to read log file data from Logger instance");
                        PostClientEvent("error", "upload_failed", "Failed to read log file from Logger instance");
                        yield break;
                    }
                }
                else
                {
                    BetaHubReporter.PauseLogger();
                    
                    try
                    {
                        fileData = File.ReadAllBytes(filePath);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Error reading file {filePath}: {ex.Message}");
                        PostClientEvent("error", "upload_failed", $"Failed to read log file {Path.GetFileName(filePath)}: {ex.Message}");
                        BetaHubReporter.ResumeLogger();
                        yield break;
                    }
                    
                    BetaHubReporter.ResumeLogger();
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
                    Debug.LogError($"Error reading file {filePath}: {ex.Message}");
                    PostClientEvent("error", "upload_failed", $"Failed to read file {Path.GetFileName(filePath)}: {ex.Message}");
                    yield break;
                }
            }
            
            yield return UploadStringAsFile(endpoint, fieldName, fileData, Path.GetFileName(filePath), contentType);
        }

        private IEnumerator UploadStringAsFile(string endpoint, string fieldName, byte[] fileData, string fileName, string contentType, Dictionary<string, string> extraFields = null)
        {
            if (fileData == null)
            {
                Debug.LogError($"Cannot upload {fileName}: file data is null");
                yield break;
            }

            WWWForm form = new WWWForm();
            form.AddBinaryData(fieldName, fileData, fileName, contentType);

            if (extraFields != null)
            {
                foreach (var kvp in extraFields)
                {
                    form.AddField(kvp.Key, kvp.Value);
                }
            }

            string url = $"{_betahubEndpoint}projects/{_projectId}/issues/g-{Id}/{endpoint}";
            using (UnityWebRequest www = UnityWebRequest.Post(url, form))
            {
                www.SetRequestHeader("Authorization", "Bearer " + _updateIssueAuthToken);
                www.SetRequestHeader("BetaHub-Project-ID", _projectId);
                www.SetRequestHeader("Accept", "application/json");

                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"Error uploading {fileName}: {www.error}\nResponse body: {www.downloadHandler?.text}");
                    PostClientEvent("error", "upload_failed", $"Failed to upload {fileName}: {www.error}");
                }
                else
                {
                    Debug.Log($"{fileName} uploaded successfully!");
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

        private void PostClientEvent(string level, string eventType, string message)
        {
            if (string.IsNullOrEmpty(Id)) return;

            WWWForm form = new WWWForm();
            form.AddField("client_event[level]", level);
            form.AddField("client_event[event_type]", eventType);
            form.AddField("client_event[message]", message);

            string url = $"{_betahubEndpoint}projects/{_projectId}/issues/g-{Id}/client_events";
            UnityWebRequest www = UnityWebRequest.Post(url, form);
            www.SetRequestHeader("Authorization", "FormUser " + _createIssueAuthToken);
            www.SetRequestHeader("BetaHub-Project-ID", _projectId);
            www.SetRequestHeader("Accept", "application/json");

            var op = www.SendWebRequest();
            op.completed += _ => { www.Dispose(); };
        }
    }
}
