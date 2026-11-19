namespace BetaHub
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using UnityEngine;
    using UnityEngine.Networking;

    /// <summary>
    /// HTTP client for one BetaHub suggestion (a feature request): a draft that carries its screenshot,
    /// then publish. Nothing uploads after the draft, and a suggestion takes no reporter email.
    /// Build the draft with <see cref="BugReportSession.BuildSuggestion"/>.
    /// </summary>
    public class FeatureRequest : IIssueClient
    {
        public const string NoEmailError = "Suggestions do not take a reporter email.";
        public const string UnidentifiedReplyError = "BetaHub's reply did not identify the new suggestion.";
        private const string DescriptionField = "feature_request[description]";
        private const string FilesField = "feature_request[files][]";
        private const string DraftField = "draft";
        private const string PngContentType = "image/png";

        /// <summary>The suggestion's id in its project, from the end of <see cref="Url"/>.</summary>
        public string Id { get; private set; }
        public string Url { get; private set; }

        string IIssueClient.IssueId => Id;
        string IIssueClient.IssueUrl => Url;

        private readonly string _endpoint;
        private readonly string _projectId;
        private readonly string _createAuthToken;
        private string _updateAuthToken;
        // screenshots that went out with the draft
        private readonly HashSet<string> _carried = new HashSet<string>();

        public FeatureRequest(string betahubEndpoint, string projectId, string authToken)
        {
            if (string.IsNullOrEmpty(betahubEndpoint)) throw new ArgumentException("BetaHub endpoint is required", nameof(betahubEndpoint));
            if (string.IsNullOrEmpty(projectId)) throw new ArgumentException("Project ID is required", nameof(projectId));
            if (string.IsNullOrEmpty(authToken)) throw new ArgumentException("Auth token is required", nameof(authToken));

            _endpoint = betahubEndpoint.EndsWith("/") ? betahubEndpoint : betahubEndpoint + "/";
            _projectId = projectId;
            _createAuthToken = authToken;
        }

        /// <summary>
        /// The last path segment of a suggestion's URL: the id its publish endpoint takes. Null when the URL has none.
        /// </summary>
        public static string ScopedIdFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            int slash = url.LastIndexOf('/');
            string id = slash < 0 ? null : url.Substring(slash + 1);
            return string.IsNullOrEmpty(id) ? null : id;
        }

        /// <summary>Creates the hidden draft with the description and the screenshot. Release targets do not apply.</summary>
        // Checks run on the call, not on the first step of the routine.
        public IEnumerator CreateDraft(BugReportDraft draft, ReleaseTarget release, Action<string> onError)
        {
            if (Id != null) throw new InvalidOperationException("FeatureRequest instance cannot be reused for posting.");
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            foreach (var upload in draft.Uploads)
            {
                if (upload.Kind != UploadKind.Screenshot)
                {
                    throw new ArgumentException($"A suggestion carries only its screenshot, not '{upload.AttachmentId}'. Build it with BugReportSession.BuildSuggestion.", nameof(draft));
                }
            }
            return PostDraft(draft, onError);
        }

        private IEnumerator PostDraft(BugReportDraft draft, Action<string> onError)
        {
            var form = new WWWForm();
            form.AddField(DescriptionField, draft.Description ?? "");
            form.AddField(DraftField, "true");
            foreach (var upload in draft.Uploads)
            {
                byte[] bytes = ReadScreenshot(upload.Path);
                if (bytes == null) continue;
                form.AddBinaryData(FilesField, bytes, Path.GetFileName(upload.Path), PngContentType);
                _carried.Add(upload.AttachmentId);
            }

            using (UnityWebRequest www = UnityWebRequest.Post($"{_endpoint}projects/{_projectId}/feature_requests.json", form))
            {
                www.SetRequestHeader("Authorization", "FormUser " + _createAuthToken);
                www.SetRequestHeader("BetaHub-Project-ID", _projectId);
                www.SetRequestHeader("Accept", "application/json");
                yield return www.SendWebRequest();

                // the screenshot is a temporary file whether or not the draft went through
                foreach (var upload in draft.Uploads)
                {
                    if (upload.RemoveAfterUpload) TryDelete(upload.Path);
                }

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError(UnityBetaHubLog.Prefix + "Error creating the suggestion: " + www.error + "\nResponse body: " + www.downloadHandler?.text);
                    onError?.Invoke(Issue.ExtractErrorMessage(www));
                    yield break;
                }

                var reply = ParseReply(www.downloadHandler.text);
                string id = ScopedIdFromUrl(reply?.url);
                if (id == null || string.IsNullOrEmpty(reply.token))
                {
                    Debug.LogError(UnityBetaHubLog.Prefix + "Unexpected suggestion response: " + www.downloadHandler.text);
                    onError?.Invoke(UnidentifiedReplyError);
                    yield break;
                }
                Id = id;
                Url = reply.url;
                _updateAuthToken = reply.token;
            }
        }

        /// <summary>The screenshot went out with the draft; this only reports how that went.</summary>
        public IEnumerator UploadMedia(BugReportDraft draft, Action<string, AttachmentUploadStatus, float> onProgress)
        {
            foreach (var upload in draft.Uploads)
            {
                onProgress?.Invoke(upload.AttachmentId, _carried.Contains(upload.AttachmentId) ? AttachmentUploadStatus.Sent : AttachmentUploadStatus.Failed, 1f);
            }
            yield break;
        }

        public IEnumerator SetReporterEmail(string email, Action<string> onError)
        {
            onError?.Invoke(NoEmailError);
            yield break;
        }

        /// <summary>Makes the draft visible. BetaHub sends no email for suggestions, so the flag is not sent.</summary>
        public IEnumerator Publish(bool emailMyReport, Action<string> onError)
        {
            if (string.IsNullOrEmpty(Id) || string.IsNullOrEmpty(_updateAuthToken))
            {
                throw new InvalidOperationException("Cannot publish: the suggestion draft has not been created yet.");
            }
            return PostPublish(onError);
        }

        private IEnumerator PostPublish(Action<string> onError)
        {
            using (var www = new UnityWebRequest($"{_endpoint}projects/{_projectId}/feature_requests/{Id}/publish", UnityWebRequest.kHttpVerbPOST))
            {
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Authorization", "Bearer " + _updateAuthToken);
                www.SetRequestHeader("BetaHub-Project-ID", _projectId);
                www.SetRequestHeader("Accept", "application/json");
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError(UnityBetaHubLog.Prefix + "Error publishing the suggestion: " + www.error + "\nResponse body: " + www.downloadHandler?.text);
                    onError?.Invoke(Issue.ExtractErrorMessage(www));
                }
            }
        }

        private static byte[] ReadScreenshot(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) return File.ReadAllBytes(path);
                Debug.LogError(UnityBetaHubLog.Prefix + "Suggestion screenshot not found at " + path + "; sending the text alone.");
            }
            catch (Exception ex)
            {
                Debug.LogError(UnityBetaHubLog.Prefix + "Could not read the suggestion screenshot " + path + ": " + ex.Message);
            }
            return null;
        }

        private static Reply ParseReply(string body)
        {
            try
            {
                return string.IsNullOrEmpty(body) ? null : JsonUtility.FromJson<Reply>(body);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(UnityBetaHubLog.Prefix + $"Could not delete temporary file {path}: {ex.Message}");
            }
        }

        [Serializable]
        private class Reply
        {
            public string url;
            public string token;
        }
    }
}
