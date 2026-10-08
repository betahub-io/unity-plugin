using System;
using System.Collections;
using System.Collections.Generic;

namespace BetaHub
{
    public enum ReportSubmissionState
    {
        /// <summary>Collecting data; nothing sent yet.</summary>
        Preparing,
        CreatingDraft,
        /// <summary>Draft exists; waiting for media before the UI may continue (WaitForUpload mode only).</summary>
        UploadingMedia,
        /// <summary>Draft exists; waiting for a Publish() call. Media may still upload in the background.</summary>
        AwaitingPublish,
        /// <summary>Publish requested; publishes as soon as media upload finishes.</summary>
        Publishing,
        Published,
        Failed,
    }

    public enum AttachmentUploadStatus
    {
        Queued,
        Uploading,
        Sent,
        /// <summary>This item did not arrive. The rest of the report is unaffected.</summary>
        Failed,
    }

    public readonly struct AttachmentUploadState
    {
        public readonly AttachmentUploadStatus Status;
        /// <summary>0 to 1. Sent and Failed items are 1: nothing is left to wait for.</summary>
        public readonly float Progress;

        public AttachmentUploadState(AttachmentUploadStatus status, float progress)
        {
            Status = status;
            Progress = progress;
        }

        public bool IsFinished => Status == AttachmentUploadStatus.Sent || Status == AttachmentUploadStatus.Failed;
    }

    /// <summary>
    /// Handle for one bug report submission. Drives <see cref="IIssueClient"/> through the draft,
    /// media upload, optional reporter email and publish steps, and exposes state to any UI.
    /// Callers never touch coroutines; the runner executes them.
    /// </summary>
    public sealed class ReportSubmission
    {
        private readonly IIssueClient _client;
        private readonly ICoroutineRunner _runner;
        private readonly IBetaHubLog _log;

        private bool _started;
        private bool _publishRequested;
        private bool _publishDispatched;
        private bool _emailMyReport;
        private bool _emailInFlight;
        private readonly Dictionary<string, AttachmentUploadState> _uploads = new Dictionary<string, AttachmentUploadState>();
        private readonly List<string> _inlineIds = new List<string>();

        public ReportSubmissionState State { get; private set; } = ReportSubmissionState.Preparing;
        public string Error { get; private set; }
        public bool IsMediaUploadComplete { get; private set; }
        public string IssueId => _client?.IssueId;
        public string IssueUrl => _client?.IssueUrl;

        public bool IsFinished => State == ReportSubmissionState.Published || State == ReportSubmissionState.Failed;
        /// <summary>True while a network upload may be running. Recording is paused during this time.</summary>
        public bool IsUploadInProgress => _started && !IsMediaUploadComplete && State != ReportSubmissionState.Failed;

        public event Action<ReportSubmission> StateChanged;
        /// <summary>Fired once when the draft issue exists on the server.</summary>
        public event Action<ReportSubmission> DraftCreated;
        public event Action<ReportSubmission> MediaUploadCompleted;
        /// <summary>The reporter email could not be saved. The report itself is still publishable.</summary>
        public event Action<string> ReporterEmailFailed;
        /// <summary>One attachment's upload state changed; read it with <see cref="TryGetUploadState"/>.</summary>
        public event Action<ReportSubmission, string> AttachmentUploadChanged;

        /// <summary>Upload state of an included attachment. False for ids that are not part of this report.</summary>
        public bool TryGetUploadState(string attachmentId, out AttachmentUploadState state)
        {
            if (attachmentId == null)
            {
                state = default;
                return false;
            }
            return _uploads.TryGetValue(attachmentId, out state);
        }

        /// <summary>Mean progress of all included attachments, 0 to 1. With none, 1 once media upload finished.</summary>
        public float UploadProgress
        {
            get
            {
                if (_uploads.Count == 0) return IsMediaUploadComplete ? 1f : 0f;
                float sum = 0f;
                foreach (var state in _uploads.Values) sum += state.Progress;
                return sum / _uploads.Count;
            }
        }

        public ReportSubmission(IIssueClient client, ICoroutineRunner runner, IBetaHubLog log)
        {
            _client = client;
            _runner = runner;
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>A submission that is already failed, e.g. because of missing configuration.</summary>
        public static ReportSubmission CreateFailed(string error, IBetaHubLog log)
        {
            var submission = new ReportSubmission(null, null, log);
            submission.Fail(error);
            return submission;
        }

        /// <param name="autoPublish">Publish as soon as media upload finishes, without waiting for Publish().</param>
        public void Start(BugReportDraft draft, ReleaseTarget release, MediaUploadType uploadType, bool autoPublish)
        {
            if (_started || State != ReportSubmissionState.Preparing)
            {
                _log.Warning($"Submission cannot be started in state {State}. Create a new submission instead.");
                return;
            }
            if (draft == null) throw new ArgumentNullException(nameof(draft));

            _started = true;
            _publishRequested = autoPublish;
            foreach (var upload in draft.Uploads)
            {
                _uploads[upload.AttachmentId] = new AttachmentUploadState(AttachmentUploadStatus.Queued, 0f);
            }
            foreach (var id in draft.InlineAttachmentIds)
            {
                _uploads[id] = new AttachmentUploadState(AttachmentUploadStatus.Queued, 0f);
                _inlineIds.Add(id);
            }
            _runner.Run(Guarded(RunFlow(draft, release, uploadType)));
        }

        /// <summary>Attach the reporter's email. Only valid once the draft exists and before the report is finished.</summary>
        public void SetReporterEmail(string email)
        {
            if (string.IsNullOrEmpty(IssueId) || IsFinished)
            {
                _log.Warning($"Reporter email cannot be set in state {State}. Call it after the draft is created and before the report is published.");
                return;
            }
            if (string.IsNullOrEmpty(email))
            {
                _log.Warning("Reporter email is empty and was ignored.");
                return;
            }

            _emailInFlight = true;
            _runner.Run(Guarded(EmailFlow(email)));
        }

        /// <summary>Request publication. Waits for media upload if it is still running.</summary>
        public void Publish(bool emailMyReport)
        {
            if (State != ReportSubmissionState.AwaitingPublish && State != ReportSubmissionState.UploadingMedia)
            {
                _log.Warning($"Publish() ignored in state {State}. It is only valid while the report awaits publishing.");
                return;
            }

            _publishRequested = true;
            _emailMyReport = emailMyReport;
            SetState(ReportSubmissionState.Publishing);

            if (IsMediaUploadComplete)
            {
                DispatchPublish();
            }
        }

        /// <summary>Marks the submission as failed. Used by the host when an unexpected exception occurs.</summary>
        public void Fail(string error)
        {
            if (State == ReportSubmissionState.Failed) return;
            Error = string.IsNullOrEmpty(error) ? "Unknown error" : error;
            _log.Error("Bug report submission failed: " + Error);
            SetState(ReportSubmissionState.Failed);
        }

        private IEnumerator RunFlow(BugReportDraft draft, ReleaseTarget release, MediaUploadType uploadType)
        {
            SetState(ReportSubmissionState.CreatingDraft);

            string draftError = null;
            yield return _client.CreateDraft(draft, release, e => draftError = e);
            if (draftError != null)
            {
                Fail(draftError);
                yield break;
            }

            foreach (var id in _inlineIds)
            {
                SetUploadState(id, AttachmentUploadStatus.Sent, 1f);
            }
            DraftCreated?.Invoke(this);
            SetState(uploadType == MediaUploadType.WaitForUpload
                ? ReportSubmissionState.UploadingMedia
                : StateAfterDraft());

            yield return _client.UploadMedia(draft, OnUploadProgress);
            if (State == ReportSubmissionState.Failed) yield break;

            FailUnreportedUploads(draft);
            IsMediaUploadComplete = true;
            MediaUploadCompleted?.Invoke(this);

            if (State == ReportSubmissionState.UploadingMedia)
            {
                SetState(StateAfterDraft());
            }

            if (_publishRequested)
            {
                DispatchPublish();
            }
        }

        private void OnUploadProgress(string attachmentId, AttachmentUploadStatus status, float progress)
        {
            if (attachmentId == null || !_uploads.ContainsKey(attachmentId))
            {
                _log.Warning($"Upload progress for unknown attachment '{attachmentId}' ignored.");
                return;
            }
            SetUploadState(attachmentId, status, progress);
        }

        // A client that returns without reporting an item would leave its row spinning forever.
        private void FailUnreportedUploads(BugReportDraft draft)
        {
            foreach (var upload in draft.Uploads)
            {
                if (_uploads[upload.AttachmentId].IsFinished) continue;
                _log.Warning($"Upload of attachment '{upload.AttachmentId}' never reported a result; marked as failed.");
                SetUploadState(upload.AttachmentId, AttachmentUploadStatus.Failed, 1f);
            }
        }

        private void SetUploadState(string attachmentId, AttachmentUploadStatus status, float progress)
        {
            var state = new AttachmentUploadState(status, status == AttachmentUploadStatus.Queued ? 0f
                : status == AttachmentUploadStatus.Uploading ? Math.Max(0f, Math.Min(1f, progress)) : 1f);
            if (_uploads[attachmentId].Equals(state)) return;
            _uploads[attachmentId] = state;
            AttachmentUploadChanged?.Invoke(this, attachmentId);
        }

        private ReportSubmissionState StateAfterDraft()
        {
            return _publishRequested ? ReportSubmissionState.Publishing : ReportSubmissionState.AwaitingPublish;
        }

        private void DispatchPublish()
        {
            if (_publishDispatched) return;
            _publishDispatched = true;
            _runner.Run(Guarded(PublishFlow()));
        }

        private IEnumerator PublishFlow()
        {
            while (_emailInFlight)
            {
                yield return null;
            }
            if (State == ReportSubmissionState.Failed) yield break;

            string publishError = null;
            yield return _client.Publish(_emailMyReport, e => publishError = e);
            if (publishError != null)
            {
                Fail(publishError);
                yield break;
            }

            SetState(ReportSubmissionState.Published);
        }

        private IEnumerator EmailFlow(string email)
        {
            try
            {
                string emailError = null;
                yield return _client.SetReporterEmail(email, e => emailError = e);
                if (emailError != null)
                {
                    _log.Error("Could not save reporter email: " + emailError);
                    ReporterEmailFailed?.Invoke(emailError);
                }
            }
            finally
            {
                _emailInFlight = false;
            }
        }

        // Drives nested IEnumerators itself so an exception anywhere in a flow becomes a Failed
        // state instead of silently killing the coroutine. Non-enumerator yields (null, Unity
        // async operations) pass through to the runner.
        private IEnumerator Guarded(IEnumerator routine)
        {
            var stack = new System.Collections.Generic.Stack<IEnumerator>();
            stack.Push(routine);
            while (stack.Count > 0)
            {
                object current;
                try
                {
                    var top = stack.Peek();
                    if (!top.MoveNext())
                    {
                        stack.Pop();
                        continue;
                    }
                    current = top.Current;
                }
                catch (Exception ex)
                {
                    _emailInFlight = false;
                    Fail(ex.Message);
                    yield break;
                }

                if (current is IEnumerator nested)
                {
                    stack.Push(nested);
                    continue;
                }
                yield return current;
            }
        }

        private void SetState(ReportSubmissionState state)
        {
            if (State == state) return;
            State = state;
            StateChanged?.Invoke(this);
        }
    }
}
