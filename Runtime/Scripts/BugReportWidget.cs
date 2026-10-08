using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BetaHub
{
    /// <summary>Player-facing copy. Edit in the inspector or swap the whole object for another language.</summary>
    [Serializable]
    public class BugReportWidgetStrings
    {
        public string Title = "Report a bug";
        public string ErrorTitle = "Couldn't send";
        public string UnavailableTitle = "Bug reporting unavailable";
        public string DescriptionPlaceholder = "What went wrong, and what did you expect instead?";
        public string StepsPlaceholder = "1. Open the inventory\n2. Drag the sword onto the shield\n3. Game freezes";
        public string ScreenshotMeta = "taken when you pressed {0}";
        public string ClipTitle = "Last {0} s of play";
        public string ClipMeta = "video + audio · {0}";
        public string LogMeta = "{0} · {1}";
        public string SendReport = "Send report";
        public string Sending = "Sending…";
        public string Cancel = "Cancel";
        public string UploadingTitle = "Almost there";
        public string UploadingText = "Your report is saved. Uploading attachments…";
        public string SentTitle = "Report sent";
        public string SentAnonymous = "saved anonymously";
        public string SentSignedIn = "under your BetaHub reports";
        public string EmailPlaceholder = "you@example.com";
        public string EmailMeALink = "Email me a link";
        public string EmailSaved = "We'll email {0} when there's news.";
        public string EmailInvalid = "That doesn't look like an email address.";
        public string EmailFailed = "Couldn't save your email. Try again or just go back.";
        public string SentNote = "We'll also tell you when it's fixed. Nothing else, no account.";
        public string SentSignedInNote = "We'll notify you on BetaHub when it's fixed.";
        public string BackToGame = "Back to game";
        public string CopyLink = "Copy link";
        public string LinkCopied = "Link copied";
        public string ToastUploading = "Uploading your report";
        public string ToastUploadingSub = "the game continues";
        public string ToastDone = "Report sent";
        public string ToastDoneSub = "thank you";
        public string TermsText = "I accept the terms of bug reporting";
        public string HintClose = "Esc  close";
        public string HintSend = "Ctrl+Enter  send";
    }

    /// <summary>States the editor preview can show without a real submission.</summary>
    public enum WidgetPreviewState
    {
        None,
        Compose,
        Sending,
        Error,
        Uploading,
        Sent,
        Unavailable,
    }

    /// <summary>
    /// UI Toolkit bug report form. A view over <see cref="BetaHubReporter"/>: collects input, shows the
    /// submission state, and nothing else. Reskin through the USS variables; replace freely.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [RequireComponent(typeof(BetaHubReporter))]
    [AddComponentMenu("BetaHub/Bug Report Widget")]
    public class BugReportWidget : MonoBehaviour
    {
        private const long MaxLogBytesForPreview = 2L * 1024 * 1024;
        private const int ToastDoneMilliseconds = 4000;
        private const string SampleIssueId = "4821";
        private const string SampleSignedInName = "Kasia_P";

        // element names, shared with BugReportWidget.uxml
        private const string OverlayName = "bh-overlay";
        private const string WidgetName = "bh-widget";
        private const string TitleName = "bh-title";
        private const string SubtitleName = "bh-subtitle";
        private const string CloseName = "bh-close";
        private const string ComposeName = "bh-compose";
        private const string UploadingName = "bh-uploading";
        private const string SentName = "bh-sent";
        private const string CountName = "bh-count";
        private const string DescriptionName = "bh-description";
        private const string DescriptionPlaceholderName = "bh-description-placeholder";
        private const string StepsName = "bh-steps";
        private const string StepsPlaceholderName = "bh-steps-placeholder";
        private const string CardScreenshotName = "bh-card-screenshot";
        private const string CardClipName = "bh-card-clip";
        private const string CardLogName = "bh-card-log";
        private const string ShotImageName = "bh-shot-image";
        private const string ShotMetaName = "bh-shot-meta";
        private const string ClipTitleName = "bh-clip-title";
        private const string ClipMetaName = "bh-clip-meta";
        private const string LogTextName = "bh-log-text";
        private const string LogMetaName = "bh-log-meta";
        private const string TermsName = "bh-terms";
        private const string TermsToggleName = "bh-terms-toggle";
        private const string TermsTextName = "bh-terms-text";
        private const string UploadingTextName = "bh-uploading-text";
        private const string EmailRowName = "bh-email-row";
        private const string EmailName = "bh-email";
        private const string EmailPlaceholderName = "bh-email-placeholder";
        private const string EmailSendName = "bh-email-send";
        private const string EmailErrorName = "bh-email-error";
        private const string SentNoteName = "bh-sent-note";
        private const string SentSignedInNoteName = "bh-sent-signedin-note";
        private const string HintCloseName = "bh-hint-close";
        private const string HintSendName = "bh-hint-send";
        private const string StatusName = "bh-status";
        private const string CancelName = "bh-cancel";
        private const string SendName = "bh-send";
        private const string CopyLinkName = "bh-copy-link";
        private const string BackName = "bh-back";
        private const string ToastName = "bh-toast";
        private const string ToastTextName = "bh-toast-text";
        private const string ToastSubName = "bh-toast-sub";

        // class names, shared with BugReportWidget.uss
        private const string HiddenClass = "bh-hidden";
        private const string CardOnClass = "bh-card--on";
        private const string CardOffClass = "bh-card--off";
        private const string ThemeDarkClass = "bh-theme-dark";
        private const string ThemeLightClass = "bh-theme-light";
        private const string WidgetErrorClass = "bh-widget--error";
        private const string WidgetWarningClass = "bh-widget--warning";
        private const string TitleErrorClass = "bh-title--error";
        private const string TitleWarningClass = "bh-title--warning";
        private const string SubtitleErrorClass = "bh-subtitle--error";
        private const string SubtitleWarningClass = "bh-subtitle--warning";
        private const string CountOverClass = "bh-count--over";
        private const string ToastDoneClass = "bh-toast--done";
        private const string ToastFailedClass = "bh-toast--failed";

    #if ENABLE_INPUT_SYSTEM
        [Tooltip("Opens the form. Default F12.")]
        public InputAction ShortcutAction = new InputAction("BugReportShortcut", binding: "<Keyboard>/f12");
    #else
        [Tooltip("Opens the form. Default F12. None disables the shortcut.")]
        public KeyCode ShortcutKey = KeyCode.F12;
    #endif

        [Tooltip("Use the light token set instead of the dark one.")]
        public bool UseLightTheme;

        [Tooltip("Show a terms checkbox that must be ticked before sending.")]
        public bool RequireTermsAcceptance;

        [Tooltip("Opened when the player clicks the terms text. Leave empty for plain text.")]
        public string TermsUrl = "";

        public BugReportWidgetStrings Strings = new BugReportWidgetStrings();

        public UnityEvent OnBugReportWindowShown;
        public UnityEvent OnBugReportWindowHidden;

        private UIDocument _document;
        private BetaHubReporter _reporter;
        private bool _bound;

        private VisualElement _overlay, _widget, _compose, _uploading, _sent, _toast;
        private VisualElement _cardShot, _cardClip, _cardLog, _shotImage, _terms, _emailRow;
        private Label _title, _subtitle, _count, _descriptionPlaceholder, _stepsPlaceholder, _shotMeta, _clipTitle, _clipMeta, _logText, _logMeta;
        private Label _termsText, _uploadingText, _emailPlaceholder, _emailError, _sentNote, _sentSignedInNote, _hintClose, _hintSend, _status, _toastText, _toastSub;
        private TextField _description, _steps, _email;
        private Button _close, _cancel, _send, _copyLink, _back, _emailSend;
        private Toggle _termsToggle;

        private bool _includeScreenshot = true, _includeClip = true, _includeLog = true;
        private ReportSubmission _active;
        private string _submissionError;
        private Texture2D _shotTexture;
        private bool _wasVisible;
        private bool _cursorStateChanged;
        private CursorLockMode _previousCursorLockMode;
        private WidgetScreen _screen = WidgetScreen.Compose;

        public bool IsVisible => _overlay != null && !_overlay.ClassListContains(HiddenClass);

        // ------------------------------------------------------------------ lifecycle

        void OnEnable()
        {
    #if ENABLE_INPUT_SYSTEM
            if (Application.isPlaying && InputSystem.settings != null)
            {
                ShortcutAction.Enable();
                ShortcutAction.performed += OnShortcutPerformed;
            }
    #endif
        }

        void OnDisable()
        {
    #if ENABLE_INPUT_SYSTEM
            ShortcutAction.performed -= OnShortcutPerformed;
            ShortcutAction.Disable();
    #endif
        }

        void Start()
        {
            if (!EnsureBound()) return;
            ApplyStrings();
            SetOverlayVisible(false);
            HideToast();
        }

        void Update()
        {
            if (!_bound) return;

            bool visible = IsVisible;
            if (visible && !_wasVisible)
            {
                OnBugReportWindowShown?.Invoke();
                _reporter.NotifyReportWindowShown();
                ModifyCursorState();
            }
            else if (!visible && _wasVisible)
            {
                OnBugReportWindowHidden?.Invoke();
                _reporter.NotifyReportWindowHidden();
                RestoreCursorState();
            }
            _wasVisible = visible;

    #if !ENABLE_INPUT_SYSTEM
            if (ShortcutKey != KeyCode.None && Input.GetKeyDown(ShortcutKey))
            {
                Show();
            }
    #endif
        }

        void OnDestroy()
        {
            Detach();
            if (_shotTexture != null) Destroy(_shotTexture);
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Captures a screenshot of the current frame, then opens the form.</summary>
        public void Show()
        {
            if (!EnsureBound() || IsVisible) return;
            StartCoroutine(CaptureAndShow());
        }

        /// <summary>Closes the form. A report awaiting publish is published without an email.</summary>
        public void Hide()
        {
            if (!_bound) return;
            if (_active != null && _active.State == ReportSubmissionState.AwaitingPublish)
            {
                _active.Publish(false);
            }
            SetOverlayVisible(false);
            _description?.Blur();
        }

        /// <summary>Editor preview: fills the widget with sample data and shows one state without a submission.</summary>
        public void ApplyPreview(WidgetPreviewState state, bool signedIn)
        {
            if (!EnsureBound()) return;
            ApplyStrings();
            HideToast();
            if (state == WidgetPreviewState.None)
            {
                SetOverlayVisible(false);
                return;
            }

            _description.SetValueWithoutNotify("Dragging the sword onto the shield in the inventory freezes the game for ~2 s, then the sword is gone.");
            _steps.SetValueWithoutNotify("");
            _includeScreenshot = _includeClip = _includeLog = true;
            ApplyCardState();
            _cardClip.RemoveFromClassList(HiddenClass);
            _cardLog.RemoveFromClassList(HiddenClass);
            _shotImage.style.backgroundImage = StyleKeyword.Null;
            _clipTitle.text = string.Format(Strings.ClipTitle, 60);
            _clipMeta.text = string.Format(Strings.ClipMeta, BugReportWidgetModel.FormatDuration(60));
            _logMeta.text = string.Format(Strings.LogMeta, "BH_Player.log", "1.2 MB");
            _submissionError = null;
            string signedInAs = signedIn ? SampleSignedInName : null;

            switch (state)
            {
                case WidgetPreviewState.Compose:
                    SetScreen(WidgetScreen.Compose);
                    ApplyComposeHeader(true, null, signedInAs);
                    break;
                case WidgetPreviewState.Sending:
                    SetScreen(WidgetScreen.Sending);
                    ApplyComposeHeader(true, null, signedInAs);
                    break;
                case WidgetPreviewState.Error:
                    SetScreen(WidgetScreen.Compose);
                    ApplyComposeHeader(true, "No connection. Text kept.", signedInAs);
                    break;
                case WidgetPreviewState.Uploading:
                    SetScreen(WidgetScreen.Uploading);
                    break;
                case WidgetPreviewState.Sent:
                    SetScreen(WidgetScreen.Sent);
                    ApplySentScreen(SampleIssueId, signedIn, awaitingEmail: !signedIn, emailSavedTo: null);
                    break;
                case WidgetPreviewState.Unavailable:
                    SetScreen(WidgetScreen.Compose);
                    ApplyComposeHeader(false, null, null, "Project ID is not set. Copy it from your BetaHub project settings into the BetaHubReporter component.");
                    break;
            }
            RefreshComposeControls();
            SetOverlayVisible(true);
        }

        // ------------------------------------------------------------------ binding

        private bool EnsureBound()
        {
            if (_bound) return true;
            _document = GetComponent<UIDocument>();
            _reporter = GetComponent<BetaHubReporter>();
            var root = _document != null ? _document.rootVisualElement : null;
            if (root == null)
            {
                return false;
            }

            _overlay = root.Q<VisualElement>(OverlayName);
            _widget = root.Q<VisualElement>(WidgetName);
            _title = root.Q<Label>(TitleName);
            _subtitle = root.Q<Label>(SubtitleName);
            _close = root.Q<Button>(CloseName);
            _compose = root.Q<VisualElement>(ComposeName);
            _uploading = root.Q<VisualElement>(UploadingName);
            _sent = root.Q<VisualElement>(SentName);
            _count = root.Q<Label>(CountName);
            _description = root.Q<TextField>(DescriptionName);
            _descriptionPlaceholder = root.Q<Label>(DescriptionPlaceholderName);
            _steps = root.Q<TextField>(StepsName);
            _stepsPlaceholder = root.Q<Label>(StepsPlaceholderName);
            _cardShot = root.Q<VisualElement>(CardScreenshotName);
            _cardClip = root.Q<VisualElement>(CardClipName);
            _cardLog = root.Q<VisualElement>(CardLogName);
            _shotImage = root.Q<VisualElement>(ShotImageName);
            _shotMeta = root.Q<Label>(ShotMetaName);
            _clipTitle = root.Q<Label>(ClipTitleName);
            _clipMeta = root.Q<Label>(ClipMetaName);
            _logText = root.Q<Label>(LogTextName);
            _logMeta = root.Q<Label>(LogMetaName);
            _terms = root.Q<VisualElement>(TermsName);
            _termsToggle = root.Q<Toggle>(TermsToggleName);
            _termsText = root.Q<Label>(TermsTextName);
            _uploadingText = root.Q<Label>(UploadingTextName);
            _emailRow = root.Q<VisualElement>(EmailRowName);
            _email = root.Q<TextField>(EmailName);
            _emailPlaceholder = root.Q<Label>(EmailPlaceholderName);
            _emailSend = root.Q<Button>(EmailSendName);
            _emailError = root.Q<Label>(EmailErrorName);
            _sentNote = root.Q<Label>(SentNoteName);
            _sentSignedInNote = root.Q<Label>(SentSignedInNoteName);
            _hintClose = root.Q<Label>(HintCloseName);
            _hintSend = root.Q<Label>(HintSendName);
            _status = root.Q<Label>(StatusName);
            _cancel = root.Q<Button>(CancelName);
            _send = root.Q<Button>(SendName);
            _copyLink = root.Q<Button>(CopyLinkName);
            _back = root.Q<Button>(BackName);
            _toast = root.Q<VisualElement>(ToastName);
            _toastText = root.Q<Label>(ToastTextName);
            _toastSub = root.Q<Label>(ToastSubName);

            if (_overlay == null || _widget == null || _description == null || _send == null)
            {
                Debug.LogError(UnityBetaHubLog.Prefix + "BugReportWidget.uxml is missing expected elements. Assign the shipped UXML to the UIDocument, or keep the element names when customizing.", this);
                return false;
            }

            _close.clicked += Hide;
            _cancel.clicked += Hide;
            _send.clicked += SubmitBugReport;
            _back.clicked += Hide;
            _copyLink.clicked += CopyLink;
            _emailSend.clicked += SendEmail;
            _description.RegisterValueChangedCallback(_ => RefreshComposeControls());
            _steps.RegisterValueChangedCallback(_ => RefreshComposeControls());
            _email.RegisterValueChangedCallback(_ => RefreshEmailControls());
            _termsToggle.RegisterValueChangedCallback(_ => RefreshComposeControls());
            _termsText.RegisterCallback<ClickEvent>(_ => OpenTerms());
            RegisterCardToggle(_cardShot, () => _includeScreenshot = !_includeScreenshot);
            RegisterCardToggle(_cardClip, () => _includeClip = !_includeClip);
            RegisterCardToggle(_cardLog, () => _includeLog = !_includeLog);
            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            ApplyTheme();
            _bound = true;
            return true;
        }

        private void RegisterCardToggle(VisualElement card, Action toggle)
        {
            card.RegisterCallback<ClickEvent>(_ => { toggle(); ApplyCardState(); });
            card.RegisterCallback<NavigationSubmitEvent>(_ => { toggle(); ApplyCardState(); });
        }

        private void ApplyTheme()
        {
            foreach (var themed in new[] { _widget, _toast })
            {
                if (themed == null) continue;
                themed.EnableInClassList(ThemeDarkClass, !UseLightTheme);
                themed.EnableInClassList(ThemeLightClass, UseLightTheme);
            }
        }

        private void ApplyStrings()
        {
            _descriptionPlaceholder.text = Strings.DescriptionPlaceholder;
            _stepsPlaceholder.text = Strings.StepsPlaceholder;
            _cancel.text = Strings.Cancel;
            _uploadingText.text = Strings.UploadingText;
            _emailPlaceholder.text = Strings.EmailPlaceholder;
            _emailSend.text = Strings.EmailMeALink;
            _sentNote.text = Strings.SentNote;
            _sentSignedInNote.text = Strings.SentSignedInNote;
            _back.text = Strings.BackToGame;
            _copyLink.text = Strings.CopyLink;
            _termsText.text = Strings.TermsText;
            _hintClose.text = Strings.HintClose;
            _hintSend.text = Strings.HintSend;
            _shotMeta.text = string.Format(Strings.ScreenshotMeta, ShortcutLabel());
            _terms.EnableInClassList(HiddenClass, !RequireTermsAcceptance);
        }

        private string ShortcutLabel()
        {
    #if ENABLE_INPUT_SYSTEM
            string binding = ShortcutAction.bindings.Count > 0 ? ShortcutAction.GetBindingDisplayString(0) : "";
            return string.IsNullOrEmpty(binding) ? "the shortcut" : binding;
    #else
            return ShortcutKey == KeyCode.None ? "the button" : ShortcutKey.ToString();
    #endif
        }

        // ------------------------------------------------------------------ show / compose

        private IEnumerator CaptureAndShow()
        {
            yield return _reporter.CaptureScreenshot();

            _description.SetValueWithoutNotify("");
            _steps.SetValueWithoutNotify("");
            _termsToggle.SetValueWithoutNotify(false);
            _includeScreenshot = _includeClip = _includeLog = true;
            _submissionError = null;
            _active = null;

            LoadScreenshotPreview(_reporter.ScreenshotPath);
            ApplyAttachmentCards();
            ApplyCardState();
            SetScreen(WidgetScreen.Compose);
            ApplyComposeHeader(_reporter.IsConfigured, null, SignedInName(), _reporter.ConfigurationError);
            RefreshComposeControls();
            HideToast();
            SetOverlayVisible(true);
            _description.Focus();
        }

        private void LoadScreenshotPreview(string path)
        {
            _shotImage.style.backgroundImage = StyleKeyword.Null;
            try
            {
                if (!File.Exists(path)) return;
                if (_shotTexture == null) _shotTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (_shotTexture.LoadImage(File.ReadAllBytes(path)))
                {
                    _shotImage.style.backgroundImage = new StyleBackground(_shotTexture);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(UnityBetaHubLog.Prefix + "Screenshot preview unavailable: " + ex.Message, this);
            }
        }

        private void ApplyAttachmentCards()
        {
            bool hasClip = _reporter.HasRecorder && _reporter.IncludeVideo;
            _cardClip.EnableInClassList(HiddenClass, !hasClip);
            if (hasClip)
            {
                int seconds = _reporter.Recorder.RecordingDuration;
                _clipTitle.text = string.Format(Strings.ClipTitle, seconds);
                _clipMeta.text = string.Format(Strings.ClipMeta, BugReportWidgetModel.FormatDuration(seconds));
            }

            var logger = BetaHubReporter.Logger;
            bool hasLog = _reporter.IncludePlayerLog && logger != null && !string.IsNullOrEmpty(logger.LogPath) && File.Exists(logger.LogPath);
            _cardLog.EnableInClassList(HiddenClass, !hasLog);
            if (hasLog)
            {
                long size = new FileInfo(logger.LogPath).Length;
                _logMeta.text = string.Format(Strings.LogMeta, Path.GetFileName(logger.LogPath), BugReportWidgetModel.FormatFileSize(size));
                string tail = "";
                if (size <= MaxLogBytesForPreview)
                {
                    byte[] bytes = logger.ReadLogFileBytes();
                    if (bytes != null) tail = BugReportWidgetModel.LogTail(System.Text.Encoding.UTF8.GetString(bytes), BugReportWidgetModel.LogTailLines);
                }
                _logText.text = tail;
            }
            _cardLog.EnableInClassList("bh-card--last", true);
        }

        private void ApplyCardState()
        {
            SetCard(_cardShot, _includeScreenshot);
            SetCard(_cardClip, _includeClip);
            SetCard(_cardLog, _includeLog);
        }

        private static void SetCard(VisualElement card, bool on)
        {
            card.EnableInClassList(CardOnClass, on);
            card.EnableInClassList(CardOffClass, !on);
        }

        private void RefreshComposeControls()
        {
            string description = _description.value ?? "";
            _descriptionPlaceholder.EnableInClassList(HiddenClass, description.Length > 0);
            _stepsPlaceholder.EnableInClassList(HiddenClass, !string.IsNullOrEmpty(_steps.value));
            _count.text = BugReportWidgetModel.FormatCounter(description.Length, BugReportWidgetModel.DescriptionLimit);
            _count.EnableInClassList(CountOverClass, description.Length > BugReportWidgetModel.DescriptionLimit);

            bool configured = _reporter == null || _reporter.IsConfigured;
            bool canSend = BugReportWidgetModel.CanSend(description, RequireTermsAcceptance, _termsToggle.value, configured)
                           && description.Length <= BugReportWidgetModel.DescriptionLimit
                           && _screen == WidgetScreen.Compose;
            _send.SetEnabled(canSend);
        }

        private void RefreshEmailControls()
        {
            _emailPlaceholder.EnableInClassList(HiddenClass, !string.IsNullOrEmpty(_email.value));
            _emailError.AddToClassList(HiddenClass);
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (!IsVisible) return;
            if (evt.keyCode == KeyCode.Escape)
            {
                Hide();
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Return && (evt.ctrlKey || evt.commandKey) && _screen == WidgetScreen.Compose && _send.enabledSelf)
            {
                SubmitBugReport();
                evt.StopPropagation();
            }
        }

        private void OpenTerms()
        {
            if (!string.IsNullOrEmpty(TermsUrl)) Application.OpenURL(TermsUrl);
        }

        // ------------------------------------------------------------------ submission

        private void SubmitBugReport()
        {
            if (_active != null && !_active.IsFinished) return;

            var options = new BugReportOptions
            {
                IncludeScreenshots = _includeScreenshot,
                IncludeLogs = _includeLog,
                IncludeRecording = _includeClip,
            };
            _submissionError = null;
            SetScreen(WidgetScreen.Sending);
            ApplyComposeHeader(true, null, SignedInName());

            _active = _reporter.Submit(_description.value, _steps.value, options);
            _active.StateChanged += OnSubmissionStateChanged;
            _active.ReporterEmailFailed += OnReporterEmailFailed;
            if (_active.State == ReportSubmissionState.Failed)
            {
                OnSubmissionStateChanged(_active); // already failed: StateChanged will not fire again
            }
        }

        private void OnSubmissionStateChanged(ReportSubmission submission)
        {
            var screen = BugReportWidgetModel.ScreenFor(submission.State);
            switch (submission.State)
            {
                case ReportSubmissionState.Failed:
                    _submissionError = submission.Error;
                    SetScreen(WidgetScreen.Compose);
                    ApplyComposeHeader(true, _submissionError, SignedInName());
                    RefreshComposeControls();
                    if (!IsVisible) ShowToast(Strings.ToastDone, submission.Error, ToastFailedClass, autoHide: true);
                    Detach();
                    break;

                case ReportSubmissionState.UploadingMedia:
                    SetScreen(screen);
                    break;

                case ReportSubmissionState.AwaitingPublish:
                    SetScreen(screen);
                    ApplySentScreen(submission.IssueId, _reporter.IsUserAuthenticated, awaitingEmail: !_reporter.IsUserAuthenticated, emailSavedTo: null);
                    break;

                case ReportSubmissionState.Publishing:
                    SetScreen(screen);
                    ApplySentScreen(submission.IssueId, _reporter.IsUserAuthenticated, awaitingEmail: false, emailSavedTo: _pendingEmail);
                    break;

                case ReportSubmissionState.Published:
                    SetScreen(screen);
                    ApplySentScreen(submission.IssueId, _reporter.IsUserAuthenticated, awaitingEmail: false, emailSavedTo: _pendingEmail);
                    if (!IsVisible) ShowToast(Strings.ToastDone, Strings.ToastDoneSub, ToastDoneClass, autoHide: true);
                    Detach();
                    break;

                default:
                    SetScreen(screen);
                    break;
            }

            if (!IsVisible && submission.IsUploadInProgress)
            {
                ShowToast(Strings.ToastUploading, Strings.ToastUploadingSub, null, autoHide: false);
            }
        }

        private string _pendingEmail;

        private void SendEmail()
        {
            if (_active == null || _active.State != ReportSubmissionState.AwaitingPublish) return;
            string email = (_email.value ?? "").Trim();
            if (email.Length < 3 || !email.Contains("@"))
            {
                _emailError.text = Strings.EmailInvalid;
                _emailError.RemoveFromClassList(HiddenClass);
                return;
            }
            _emailError.AddToClassList(HiddenClass);
            _emailSend.SetEnabled(false);
            _pendingEmail = email;
            _active.SetReporterEmail(email);
            _active.Publish(true);
        }

        private void OnReporterEmailFailed(string error)
        {
            _pendingEmail = null;
            _emailError.text = Strings.EmailFailed;
            _emailError.RemoveFromClassList(HiddenClass);
            _emailSend.SetEnabled(true);
        }

        private void CopyLink()
        {
            if (_active == null || string.IsNullOrEmpty(_active.IssueUrl)) return;
            GUIUtility.systemCopyBuffer = _active.IssueUrl;
            SetStatus(Strings.LinkCopied);
        }

        private void Detach()
        {
            if (_active == null) return;
            _active.StateChanged -= OnSubmissionStateChanged;
            _active.ReporterEmailFailed -= OnReporterEmailFailed;
        }

        private string SignedInName()
        {
            return _reporter != null && _reporter.IsUserAuthenticated ? _reporter.UserDisplayName : null;
        }

        // ------------------------------------------------------------------ screens

        private void SetScreen(WidgetScreen screen)
        {
            _screen = screen;
            bool compose = screen == WidgetScreen.Compose || screen == WidgetScreen.Sending;
            _compose.EnableInClassList(HiddenClass, !compose);
            _uploading.EnableInClassList(HiddenClass, screen != WidgetScreen.Uploading);
            _sent.EnableInClassList(HiddenClass, screen != WidgetScreen.Sent);

            _cancel.EnableInClassList(HiddenClass, !compose);
            _send.EnableInClassList(HiddenClass, !compose);
            _copyLink.EnableInClassList(HiddenClass, screen != WidgetScreen.Sent);
            _back.EnableInClassList(HiddenClass, compose);
            _hintSend.EnableInClassList(HiddenClass, !compose);
            _close.EnableInClassList(HiddenClass, screen == WidgetScreen.Sending);

            bool sending = screen == WidgetScreen.Sending;
            _send.text = sending ? Strings.Sending : Strings.SendReport;
            _send.SetEnabled(!sending && _send.enabledSelf);
            _cancel.SetEnabled(!sending);
            _description.SetEnabled(!sending);
            _steps.SetEnabled(!sending);
            _cardShot.SetEnabled(!sending);
            _cardClip.SetEnabled(!sending);
            _cardLog.SetEnabled(!sending);
            SetStatus(null);

            if (screen == WidgetScreen.Uploading)
            {
                SetHeader(Strings.UploadingTitle, Strings.UploadingText, BugReportWidgetModel.HeaderKind.Normal);
                _uploadingText.text = Strings.UploadingText;
            }
        }

        private void ApplyComposeHeader(bool configured, string submissionError, string signedInAs, string configurationError = null)
        {
            var header = BugReportWidgetModel.ComposeHeader(configured, Debug.isDebugBuild || Application.isEditor, configurationError, submissionError, signedInAs);
            string title = header.Kind == BugReportWidgetModel.HeaderKind.Error ? Strings.ErrorTitle
                         : header.Kind == BugReportWidgetModel.HeaderKind.Warning ? Strings.UnavailableTitle
                         : Strings.Title;
            SetHeader(title, header.Subtitle, header.Kind);
        }

        private void SetHeader(string title, string subtitle, BugReportWidgetModel.HeaderKind kind)
        {
            _title.text = title;
            _subtitle.text = subtitle ?? "";
            _subtitle.EnableInClassList(HiddenClass, string.IsNullOrEmpty(subtitle));
            _title.EnableInClassList(TitleErrorClass, kind == BugReportWidgetModel.HeaderKind.Error);
            _title.EnableInClassList(TitleWarningClass, kind == BugReportWidgetModel.HeaderKind.Warning);
            _subtitle.EnableInClassList(SubtitleErrorClass, kind == BugReportWidgetModel.HeaderKind.Error);
            _subtitle.EnableInClassList(SubtitleWarningClass, kind == BugReportWidgetModel.HeaderKind.Warning);
            _widget.EnableInClassList(WidgetErrorClass, kind == BugReportWidgetModel.HeaderKind.Error);
            _widget.EnableInClassList(WidgetWarningClass, kind == BugReportWidgetModel.HeaderKind.Warning);
        }

        private void ApplySentScreen(string issueId, bool signedIn, bool awaitingEmail, string emailSavedTo)
        {
            string where = signedIn ? Strings.SentSignedIn : Strings.SentAnonymous;
            SetHeader(Strings.SentTitle, string.IsNullOrEmpty(issueId) ? where : $"#{issueId} · {where}", BugReportWidgetModel.HeaderKind.Normal);
            _emailRow.EnableInClassList(HiddenClass, signedIn || (!awaitingEmail && string.IsNullOrEmpty(emailSavedTo)));
            _sentSignedInNote.EnableInClassList(HiddenClass, !signedIn);
            _emailSend.SetEnabled(awaitingEmail);
            _email.SetEnabled(awaitingEmail);
            _emailError.AddToClassList(HiddenClass);
            if (!string.IsNullOrEmpty(emailSavedTo))
            {
                _sentNote.text = string.Format(Strings.EmailSaved, emailSavedTo);
            }
            else
            {
                _sentNote.text = Strings.SentNote;
                _email.SetValueWithoutNotify(_reporter != null ? _reporter.EffectiveEmailAddress ?? "" : "");
                RefreshEmailControls();
            }
            _copyLink.SetEnabled(_active != null && !string.IsNullOrEmpty(_active.IssueUrl));
        }

        private void SetStatus(string text)
        {
            _status.text = text ?? "";
            _status.EnableInClassList(HiddenClass, string.IsNullOrEmpty(text));
            _hintClose.EnableInClassList(HiddenClass, !string.IsNullOrEmpty(text));
        }

        private void SetOverlayVisible(bool visible)
        {
            _overlay.EnableInClassList(HiddenClass, !visible);
        }

        // ------------------------------------------------------------------ toast

        private void ShowToast(string text, string sub, string stateClass, bool autoHide)
        {
            _toastText.text = text;
            _toastSub.text = sub ?? "";
            _toast.EnableInClassList(ToastDoneClass, stateClass == ToastDoneClass);
            _toast.EnableInClassList(ToastFailedClass, stateClass == ToastFailedClass);
            _toast.RemoveFromClassList(HiddenClass);
            if (autoHide)
            {
                _toast.schedule.Execute(HideToast).StartingIn(ToastDoneMilliseconds);
            }
        }

        private void HideToast()
        {
            _toast.AddToClassList(HiddenClass);
        }

        // ------------------------------------------------------------------ cursor

        private void ModifyCursorState()
        {
            if (!UnityEngine.Cursor.visible || UnityEngine.Cursor.lockState != CursorLockMode.None)
            {
                _cursorStateChanged = true;
                _previousCursorLockMode = UnityEngine.Cursor.lockState;
                UnityEngine.Cursor.lockState = CursorLockMode.None;
                UnityEngine.Cursor.visible = true;
            }
        }

        private void RestoreCursorState()
        {
            if (_cursorStateChanged)
            {
                UnityEngine.Cursor.lockState = _previousCursorLockMode;
                UnityEngine.Cursor.visible = false;
                _cursorStateChanged = false;
            }
        }

    #if ENABLE_INPUT_SYSTEM
        private void OnShortcutPerformed(InputAction.CallbackContext context)
        {
            Show();
        }
    #endif
    }
}
