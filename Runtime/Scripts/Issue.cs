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
        private const string ScreenshotsEndpoint = "screenshots";
        private const string LogFilesEndpoint = "log_files";
        private const string VideoClipsEndpoint = "video_clips";
        private const string BinaryFilesEndpoint = "binary_files";
        private const string BinaryFileField = "binary_file[file]";
        private const string OctetStream = "application/octet-stream";

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

        /// <param name="gameRecorder">Stopped, saved and uploaded when the draft includes the recording.</param>
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

        // Uploads draft.Uploads in order and reports each one through onProgress. A failed item is
        // logged and reported to the server as a client event; it does not stop the others.
        public IEnumerator UploadMedia(BugReportDraft draft, Action<string, AttachmentUploadStatus, float> onProgress)
        {
            RequirePersisted("upload media");
            PostClientEvent("info", "upload_started", "Media upload started");

            foreach (var upload in draft.Uploads)
            {
                string id = upload.AttachmentId;
                onProgress?.Invoke(id, AttachmentUploadStatus.Uploading, 0f);
                bool sent = false;
                yield return PostUpload(upload, p => onProgress?.Invoke(id, AttachmentUploadStatus.Uploading, p), ok => sent = ok);
                onProgress?.Invoke(id, sent ? AttachmentUploadStatus.Sent : AttachmentUploadStatus.Failed, 1f);
            }

            PostClientEvent("success", "upload_completed", "Media upload completed");
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

        private IEnumerator PostUpload(ReportUpload upload, Action<float> onProgress, Action<bool> onDone)
        {
            switch (upload.Kind)
            {
                case UploadKind.Screenshot:
                    yield return PostFile(upload, ScreenshotsEndpoint, "screenshot[image]", "image/png", null, onProgress, onDone);
                    break;
                case UploadKind.Log:
                    yield return PostFile(upload, LogFilesEndpoint, "log_file[file]", "text/plain", null, onProgress, onDone);
                    break;
                case UploadKind.Video:
                    yield return PostFile(upload, VideoClipsEndpoint, "video_clip[video]", "video/mp4", null, onProgress, onDone);
                    break;
                case UploadKind.Recording:
                    yield return PostRecording(onProgress, onDone);
                    break;
                case UploadKind.File:
                    var nameField = new Dictionary<string, string> { { "binary_file[name]", upload.Name ?? "" } };
                    if (upload.Content != null)
                    {
                        yield return UploadBytes(BinaryFilesEndpoint, BinaryFileField, upload.Content, upload.FileName, OctetStream, nameField, onProgress, onDone);
                    }
                    else
                    {
                        yield return PostFile(upload, BinaryFilesEndpoint, BinaryFileField, OctetStream, nameField, onProgress, onDone);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(upload), upload.Kind, "Unknown upload kind.");
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
        
        private IEnumerator PostFile(ReportUpload upload, string endpoint, string fieldName, string contentType,
            Dictionary<string, string> extraFields, Action<float> onProgress, Action<bool> onDone)
        {
            if (string.IsNullOrEmpty(upload.Path) || !File.Exists(upload.Path))
            {
                Debug.LogError($"Cannot upload attachment '{upload.AttachmentId}': file not found at {upload.Path}");
                PostClientEvent("error", "upload_failed", $"File not found: {Path.GetFileName(upload.Path)}");
                onDone(false);
                yield break;
            }

            yield return UploadFile(endpoint, fieldName, upload.Path, contentType, extraFields, onProgress, onDone);

            if (upload.RemoveAfterUpload)
            {
                TryDelete(upload.Path);
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private IEnumerator PostRecording(Action<float> onProgress, Action<bool> onDone)
        {
            if (_gameRecorder == null)
            {
                Debug.LogError("Cannot upload the recording: no GameRecorder was given to this issue");
                onDone(false);
                yield break;
            }

            // Stop recording — this sets IsRecording=false and triggers async finalization in JS
            _gameRecorder.StopRecordingAndSaveLastMinute();

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
                onDone(false);
                yield break;
            }

            Debug.Log($"Uploading {segments.Count} video segments");
            string mergeGroup = System.Guid.NewGuid().ToString();
            bool allSent = true;
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
                int done = i;
                bool sent = false;
                yield return UploadBytes("video_segments", "video_segment[video]", segment.Data, fileName, segment.ContentType, extraFields,
                    p => onProgress((done + p) / segments.Count), ok => sent = ok);
                allSent &= sent;
            }
            onDone(allSent);
        }
#else
        private IEnumerator PostRecording(Action<float> onProgress, Action<bool> onDone)
        {
            if (_gameRecorder == null)
            {
                Debug.LogError("Cannot upload the recording: no GameRecorder was given to this issue");
                onDone(false);
                yield break;
            }

            string videoPath = _gameRecorder.StopRecordingAndSaveLastMinute();
            if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
            {
                PostClientEvent("warning", "recording_error", "Video recording produced no output file");
                onDone(false);
                yield break;
            }

            yield return UploadFile(VideoClipsEndpoint, "video_clip[video]", videoPath, "video/mp4", null, onProgress, onDone);

            // the saved clip is a temporary file
            TryDelete(videoPath);
        }
#endif

        private IEnumerator UploadFile(string endpoint, string fieldName, string filePath, string contentType,
            Dictionary<string, string> extraFields, Action<float> onProgress, Action<bool> onDone)
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
                        onDone(false);
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
                        onDone(false);
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
                    onDone(false);
                    yield break;
                }
            }

            yield return UploadBytes(endpoint, fieldName, fileData, Path.GetFileName(filePath), contentType, extraFields, onProgress, onDone);
        }

        private IEnumerator UploadBytes(string endpoint, string fieldName, byte[] fileData, string fileName, string contentType,
            Dictionary<string, string> extraFields, Action<float> onProgress, Action<bool> onDone)
        {
            if (fileData == null)
            {
                Debug.LogError($"Cannot upload {fileName}: file data is null");
                onDone(false);
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

                // Poll instead of yielding the operation so the progress reaches the UI every frame.
                var operation = www.SendWebRequest();
                while (!operation.isDone)
                {
                    onProgress(www.uploadProgress);
                    yield return null;
                }

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"Error uploading {fileName}: {www.error}\nResponse body: {www.downloadHandler?.text}");
                    PostClientEvent("error", "upload_failed", $"Failed to upload {fileName}: {www.error}");
                    onDone(false);
                }
                else
                {
                    Debug.Log($"{fileName} uploaded successfully!");
                    onDone(true);
                }
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Could not delete temporary file {path}: {ex.Message}");
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
