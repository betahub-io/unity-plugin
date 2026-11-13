using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
#endif

namespace BetaHub
{
    /// <summary>States the editor preview and the render tests can show without a real submission.</summary>
    public enum WidgetPreviewState
    {
        None,
        Compose,
        Preview,
        Sending,
        Error,
        Unavailable,
        Uploading,
        Sent,
        Toast,
    }

    /// <summary>Sample data for <see cref="BugReportWidget.ApplyPreview(WidgetPreviewOptions)"/>.</summary>
    public struct WidgetPreviewOptions
    {
        public WidgetPreviewState State;
        public bool SignedIn;
        /// <summary>1 to 9 sample attachments.</summary>
        public int AttachmentCount;
        public bool Terms;
        /// <summary>Compose only: which attachment has focus, or -1 for the description.</summary>
        public int FocusedAttachment;
        /// <summary>Compose only: the description is being typed in.</summary>
        public bool Typing;
        /// <summary>Whose buttons the prompt bar shows.</summary>
        public GlyphSet Glyphs;
        /// <summary>A bug report or a suggestion.</summary>
        public ReportType Type;
        /// <summary>A stylesheet to preview instead of the assigned one; null shows the assigned one. The Theme field is left alone.</summary>
        public StyleSheet Theme;
    }

    /// <summary>
    /// The shipped bug report form: a full-screen UI Toolkit view over <see cref="BetaHubReporter"/>.
    /// What happened on the left, what gets sent on the right, actions in a prompt bar. Collects input
    /// and shows the submission state, nothing else. Restyle through a theme stylesheet or the USS.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [RequireComponent(typeof(BetaHubReporter))]
    [AddComponentMenu("BetaHub/Bug Report Widget")]
    public class BugReportWidget : MonoBehaviour
    {
        // enough for the tail lines, even with stack traces; the whole file is sent, not read here
        private const long MaxLogBytesForPreview = 64L * 1024;
        private const int ToastDoneMilliseconds = 4000;
        private const int SpinnerIntervalMs = 16;
        private const float SpinnerDegreesPerTick = 6f;
        private const float PaneAspect = 9f / 16f;
        private const int MaxSampleAttachments = 9;
        // BetaHub's default for a project's shortest suggestion
        private const int DefaultSuggestionMinLength = 80;
        private const string TimeFormat = "HH:mm:ss";
        private const string Separator = " · ";
        private const string SampleIssueId = "4821";
        private const string SampleSignedInName = "Kasia_P";
        private const string SampleDescription = "Dragging the sword onto the shield in the inventory freezes the game for about two seconds, then the sword is gone.";
        private const string SampleError = "No connection.";
        private const string SampleConfigurationError = "Project ID is not set. Set it on BetaHubReporter.";
        private const int SampleTextureWidth = 640;
        private const int SampleTextureHeight = 360;
        private static readonly string[] WarningMarkers = { "Warn", "warning" };
        private static readonly string[] ErrorMarkers = { "Exception", "Error", "error" };

        // element names, shared with BugReportWidget.uxml
        private const string LayerName = "bh-layer";
        private const string OverlayName = "bh-overlay";
        private const string KickerName = "bh-kicker";
        private const string TitleName = "bh-title";
        private const string TabsName = "bh-tabs";
        private const string TypeName = "bh-type";
        private const string TypeBugName = "bh-type-bug";
        private const string TypeSuggestionName = "bh-type-suggestion";
        private const string TermsCheckName = "bh-terms-check";
        private const string SubtitleName = "bh-subtitle";
        private const string ScreenName = "bh-screen";
        private const string FormName = "bh-form";
        private const string DescriptionFieldName = "bh-fld-description";
        private const string StepsFieldName = "bh-fld-steps";
        private const string EmailFieldName = "bh-fld-email";
        private const string TermsName = "bh-terms";
        private const string SentName = "bh-sent";
        private const string LinkName = "bh-link";
        private const string PaneName = "bh-pane";
        private const string CapLeftName = "bh-cap-left";
        private const string CapRightName = "bh-cap-right";
        private const string AttachmentsName = "bh-attachments";
        private const string GroupLabelName = "bh-group-label";
        private const string GroupCountName = "bh-group-count";
        private const string RowsName = "bh-rows";
        private const string SentWithName = "bh-sent-with";
        private const string SentWithLabelName = "bh-sent-with-label";
        private const string SentWithCountName = "bh-sent-with-count";
        private const string SentListName = "bh-sent-list";
        private const string SentNoteName = "bh-sent-note";
        private const string PreviewName = "bh-preview";
        private const string PreviewKickerName = "bh-preview-kicker";
        private const string PreviewTabsName = "bh-preview-tabs";
        private const string PreviewSubtitleName = "bh-preview-subtitle";
        private const string PreviewCountName = "bh-preview-count";
        private const string PreviewListName = "bh-preview-list";
        private const string PreviewPaneName = "bh-preview-pane";
        private const string PromptsName = "bh-prompts";
        private const string CapActionClass = "bh-cap-right--action";
        private const string PromptsLeftName = "bh-prompts-left";
        private const string PromptsRightName = "bh-prompts-right";
        private const string ToastName = "bh-toast";
        private const string RingName = "bh-ring";
        private const string ToastTitleName = "bh-toast-title";
        private const string ToastSubName = "bh-toast-sub";

        // class names, shared with BugReportWidget.uss
        private const string HiddenClass = "bh-hidden";
        private const string LockedClass = "bh-locked";
        private const string PointerClass = "bh-pointer";
        private const string FieldFocusedClass = "bh-fld--focused";
        private const string FieldEditingClass = "bh-fld--editing";
        private const string TagLimitClass = "bh-tag--limit";
        private const string RowClass = "bh-row";
        private const string RowFocusedClass = "bh-row--focused";
        private const string RowsTwoClass = "bh-rows--two";
        private const string RowNameClass = "bh-row-name";
        private const string NameClass = "bh-oname";
        private const string NameOffClass = "bh-oname--off";
        private const string CheckClass = "bh-check";
        private const string CheckOnClass = "bh-check--on";
        private const string CheckMarkClass = "bh-check-mark";
        private const string CheckMark = "✓";
        private const string SwitchOptionOnClass = "bh-switch-opt--on";
        private const string TabsShoulderClass = "bh-tabs-shoulder";
        private const string TabsShoulderLeftClass = "bh-tabs-shoulder--left";
        private const string TabClass = "bh-tab";
        private const string TabOnClass = "bh-tab--on";
        private const string TabLineClass = "bh-tab-line";
        private const string TabKeyClass = "bh-tab-key";
        private const string TabTextClass = "bh-tab-text";
        private const string TabMarkClass = "bh-tab-mark";
        private const string TabBadgeClass = "bh-tab-badge";
        private const string MetaClass = "bh-ometa";
        private const string ValueClass = "bh-oval";
        private const string ValueMutedClass = "bh-oval--muted";
        private const string ValueOkClass = "bh-oval--ok";
        private const string ValueErrorClass = "bh-oval--error";
        private const string MiniClass = "bh-mini";
        private const string MiniFillClass = "bh-mini-fill";
        private const string TitleErrorClass = "bh-title--error";
        private const string TitleWarnClass = "bh-title--warn";
        private const string SubErrorClass = "bh-sub--error";
        private const string SubWarnClass = "bh-sub--warn";
        private const string PaneImageClass = "bh-pane-image";
        private const string PaneEmptyClass = "bh-pane-empty";
        private const string PaneFramesClass = "bh-pane-frames";
        private const string FrameRowClass = "bh-frame-row";
        private const string FrameClass = "bh-frame";
        private const int FramesPerRow = 3;
        private const string FrameLastClass = "bh-frame--last";
        private const string FrameEmptyClass = "bh-frame--empty";
        private const string PaneLogClass = "bh-pane-log";
        private const string LogWarnClass = "bh-log--warn";
        private const string LogErrorClass = "bh-log--err";
        private const string PaneKvClass = "bh-pane-kv";
        private const string KvClass = "bh-kv";
        private const string KvKeyClass = "bh-kv-key";
        private const string KvValueClass = "bh-kv-value";
        private const string PaneNoteClass = "bh-pane-note";
        private const string PromptClass = "bh-p";
        private const string PromptPrimaryClass = "bh-p--primary";
        private const string PromptDisabledClass = "bh-p--disabled";
        private const string PromptHintClass = "bh-p--hint";
        private const string PromptLabelClass = "bh-p-label";
        private const string GlyphClass = "bh-glyph";
        private const string KeyClass = "bh-key";
        private const string KeyLastClass = "bh-key--last";
        private const string DpadClass = "bh-dpad";
        private const string LogScrollClass = "bh-log-scroll";
        private const string PadClass = "bh-pad";
        private const string PadClassPrefix = "bh-pad--";
        // indexed by GlyphSet and PadButton: bh-pad--xbox, bh-pad--xbox-south
        private static readonly string[] GlyphSetClassNames = { "keyboard", "xbox", "ps", "switch" };
        private static readonly string[] PadButtonClassNames = { "south", "east", "west", "north" };
        // a nudge of the mouse is not a switch from the controller
        private const float MouseMoveThreshold = 6f;
        // PgUp/PgDn move this share of the visible log, so a line of context stays on screen.
        private const float LogPageFraction = 0.9f;
        // right stick: panel px per second at full tilt, ignored below the dead zone
        private const float StickScrollSpeed = 1500f;
        private const float StickScrollDeadZone = 0.2f;
        // half a panel pixel: below what a layout can tell apart
        private const float LayoutEpsilon = 0.5f;
        private const string SpinClass = "bh-spin";
        private const string ArcClass = "bh-arc";
        private const string NoteErrorClass = "bh-note--error";
        private const string ToastOkClass = "bh-toast--ok";
        private const string ToastErrorClass = "bh-toast--error";

    #if ENABLE_INPUT_SYSTEM
        [Tooltip("Opens the form. Default F12.")]
        public InputAction ShortcutAction = new InputAction("BugReportShortcut", binding: "<Keyboard>/f12");
    #else
        [Tooltip("Opens the form. Default F12. None disables the shortcut.")]
        public KeyCode ShortcutKey = KeyCode.F12;
    #endif

        [Tooltip("Theme stylesheet: one of Runtime/UI/Themes or your own generated from a theme.")]
        public StyleSheet Theme;

        [Tooltip("Show a terms row that must be accepted before sending.")]
        public bool RequireTermsAcceptance;

        [Tooltip("Opened from the terms row. Leave empty to show the row without a link.")]
        public string TermsUrl = "";

        [Tooltip("Shortest suggestion BetaHub accepts: your project's setting, 80 characters unless you changed it. Send stays off below it.")]
        [Min(0)]
        public int SuggestionMinLength = DefaultSuggestionMinLength;

        public BugReportWidgetStrings Strings = new BugReportWidgetStrings();

        public UnityEvent OnBugReportWindowShown;
        public UnityEvent OnBugReportWindowHidden;

        private sealed class RowView
        {
            public VisualElement Root;
            public VisualElement Check;
            public Label Name;
            public Label Meta;
            public VisualElement Value;
            public Label ValueLabel;
            public VisualElement Mini;
            public VisualElement MiniFill;
        }

        private sealed class FieldView
        {
            public VisualElement Root;
            public TextField Input;
            public Label Prompt;
        }

        // The header's two tabs; built in code because their key glyphs follow the device.
        private sealed class TabsView
        {
            public VisualElement Root, LeftShoulder, RightShoulder, Report, ReportKey, Attachments, AttachmentsKey;
            public Label ReportText, Count;
            public GlyphSet? KeysFor;
        }

        private UIDocument _document;
        private BetaHubReporter _reporter;
        private bool _bound;

        private VisualElement _keyTarget;
        private VisualElement _layer, _overlay, _screenRoot, _form, _type, _terms, _termsCheck, _sent, _link, _pane, _capRight, _attachmentsGroup, _rowsContainer;
        private VisualElement _sentWith, _preview, _previewList, _previewPane, _promptsLeft, _promptsRight, _toast;
        private Label _kicker, _title, _subtitle, _capLeft, _groupLabel, _groupCount, _sentWithLabel, _sentWithCount, _sentList, _sentNote;
        private Label _previewKicker, _previewSubtitle, _previewCount, _toastTitle, _toastSub, _typeLabel, _typeBug, _typeSuggestion, _termsLabel, _linkLabel;
        private VisualElement _linkValue;
        private TabsView _tabs, _previewTabs;
        private FieldView _description, _steps, _email;
        private ArcElement _ring;
        private readonly List<RowView> _rows = new List<RowView>();
        private readonly List<RowView> _previewRows = new List<RowView>();
        private readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();

        private AttachmentSet _attachments = new AttachmentSet(null);
        // the rows on screen: every attachment for a bug, only the screenshot for a suggestion
        private readonly List<AttachmentDescriptor> _visible = new List<AttachmentDescriptor>();
        private ReportType _reportType = ReportType.Bug;
        private List<WidgetField> _fields = BugReportWidgetModel.FieldsFor(ReportType.Bug, false);
        private WidgetFocus _focus = new WidgetFocus(1, 0, 1);
        private WidgetScreen _screen = WidgetScreen.Compose;
        private SentFocus _sentFocus;
        private bool _linkCopied;
        private FieldView _typing;
        private int _previewIndex;
        private bool _termsAccepted;
        private ReportSubmission _active;
        // a report still uploading from an earlier open; it only drives the toast
        private ReportSubmission _background;
        private string _submissionError;
        private string _pendingEmail;
        private string _emailNote;
        private bool _emailNoteIsError;
        private DateTime _captureTime;
        private string _paneKey, _previewPaneKey;
        // the full-screen preview's log, while it shows one, and whether it has nothing older or newer to show
        private ScrollView _previewLog;
        private bool _logAtTop, _logAtEnd = true;

        // sample mode: the editor preview and the render tests
        private bool _sample;
        private bool _sampleSignedIn;
        private bool _sampleUnconfigured;
        private bool _sampleTerms;
        private Texture2D _sampleTexture;
        // the theme in the tree: Theme, or the editor preview's pick
        private StyleSheet _appliedSheet;

        private bool _wasVisible;
        private bool _cursorStateChanged;
        private CursorLockMode _previousCursorLockMode;

        private GlyphSet _glyphs = GlyphSet.Keyboard;
        private bool _textEntryOpen;
    #if ENABLE_INPUT_SYSTEM
        private static readonly PadButton[] FaceButtons = { PadButton.South, PadButton.East, PadButton.West, PadButton.North };
        private readonly bool[] _faceHeld = new bool[FaceButtons.Length];
        // left and right shoulder: the header's tabs
        private readonly bool[] _shoulderHeld = new bool[2];
        private readonly NavRepeat _padRepeat = new NavRepeat();
        private Gamepad _lastPad;
        private GlyphSet _lastPadGlyphs;
    #endif
    #if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        private const string OwnEventSystemName = "BetaHub EventSystem";
        private GameObject _ownEventSystem;
        private static bool _eventSystemNoted;
    #endif

        public bool IsVisible => _overlay != null && !_overlay.ClassListContains(HiddenClass);

        /// <summary>The glyphs the prompt bar shows: the device the player used last.</summary>
        public GlyphSet Glyphs => _glyphs;

        /// <summary>
        /// The platform keyboard for controller players. Defaults to a component on this GameObject that
        /// implements <see cref="ITextEntryProvider"/>.
        /// </summary>
        public ITextEntryProvider TextEntry { get; set; }

        // ------------------------------------------------------------------ lifecycle

        void OnEnable()
        {
            // UIDocument rebuilds its tree when re-enabled; bind again on next use.
            _bound = false;
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
            StopListeningForKeys();
    #if ENABLE_INPUT_SYSTEM
            ShortcutAction.performed -= OnShortcutPerformed;
            ShortcutAction.Disable();
    #endif
            // Update stops with the object, so close the window here: the report waiting for an email goes
            // out, recording resumes and the cursor comes back. Re-enabling rebuilds a hidden tree.
            if (_bound)
            {
                SetOverlayVisible(false);
                PublishIfWaiting(_active);
            }
            if (_wasVisible)
            {
                _wasVisible = false;
                NotifyWindowHidden();
            }
        }

        void Start()
        {
            if (!EnsureBound()) return;
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
                NotifyWindowHidden();
            }
            _wasVisible = visible;

    #if !ENABLE_INPUT_SYSTEM
            if (ShortcutKey != KeyCode.None && Input.GetKeyDown(ShortcutKey))
            {
                Show();
            }
    #else
            if (visible) PollGamepad();
    #endif
        }

        void OnDestroy()
        {
            StopListeningForKeys();
            Detach(_active);
            Detach(_background);
            ReleaseTextures();
            if (_sampleTexture != null) DestroyImmediate(_sampleTexture);
    #if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            ReleaseEventSystem();
    #endif
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Captures the current frame, collects the attachments, then opens the form.</summary>
        public void Show()
        {
            if (!EnsureBound() || IsVisible) return;
            StartCoroutine(CaptureAndShow());
        }

        /// <summary>Closes the form. A report still waiting for the email step, or for its media first, is published without an email.</summary>
        public void Hide()
        {
            if (!_bound) return;
            StopTyping();
            SetOverlayVisible(false);
            PublishIfWaiting(_active);
            RefreshToast();
        }

        /// <summary>Swaps the theme stylesheet at runtime, e.g. for a settings menu or the editor preview.</summary>
        public void SetTheme(StyleSheet theme)
        {
            if (theme == null) throw new ArgumentNullException(nameof(theme));
            Theme = theme;
            if (_bound) ApplyStyleSheet(theme);
        }

        private void ApplyStyleSheet(StyleSheet sheet)
        {
            if (_appliedSheet == sheet) return;
            if (_appliedSheet != null) _layer.styleSheets.Remove(_appliedSheet);
            _layer.styleSheets.Add(sheet);
            _appliedSheet = sheet;
        }

        /// <summary>Editor preview: one state with sample data and three sample attachments.</summary>
        public void ApplyPreview(WidgetPreviewState state, bool signedIn)
        {
            ApplyPreview(new WidgetPreviewOptions { State = state, SignedIn = signedIn, AttachmentCount = 3, FocusedAttachment = -1 });
        }

        /// <summary>Editor preview and render tests: any state with sample data, no submission.</summary>
        public void ApplyPreview(WidgetPreviewOptions options)
        {
            if (!EnsureBound()) return;
            HideToast();
            CloseTextEntry();
            if (options.State == WidgetPreviewState.None)
            {
                _sample = false;
                ApplyStyleSheet(Theme);
                SetOverlayVisible(false);
                return;
            }

            _sample = true;
            _glyphs = options.Glyphs;
            _sampleSignedIn = options.SignedIn;
            _sampleUnconfigured = options.State == WidgetPreviewState.Unavailable;
            _sampleTerms = options.Terms;
            ApplyStyleSheet(options.Theme != null ? options.Theme : Theme);
            _active = null;
            _typing = null;
            _termsAccepted = false;
            _pendingEmail = null;
            _emailNote = null;
            _sentFocus = SentFocus.Email;
            _linkCopied = false;
            _reportType = options.Type;
            _captureTime = DateTime.Now;
            _submissionError = options.State == WidgetPreviewState.Error ? SampleError : null;
            _description.Input.SetValueWithoutNotify(SampleDescription);
            _steps.Input.SetValueWithoutNotify("");
            _email.Input.SetValueWithoutNotify("");
            SetAttachments(new AttachmentSet(SampleAttachments(Mathf.Clamp(options.AttachmentCount, 1, MaxSampleAttachments))));
            _focus.FocusField(FieldIndex(WidgetField.Description));
            if (options.FocusedAttachment >= 0 && options.FocusedAttachment < _visible.Count)
            {
                _focus.FocusAttachment(options.FocusedAttachment);
            }

            switch (options.State)
            {
                case WidgetPreviewState.Preview:
                    _screen = WidgetScreen.Preview;
                    _previewIndex = Math.Min(2, _visible.Count - 1);
                    ResetLogEdges();
                    break;
                case WidgetPreviewState.Sending: _screen = WidgetScreen.Sending; break;
                case WidgetPreviewState.Uploading: _screen = WidgetScreen.Uploading; break;
                case WidgetPreviewState.Sent: _screen = WidgetScreen.Sent; break;
                default: _screen = WidgetScreen.Compose; break;
            }
            if (options.Typing && _screen == WidgetScreen.Compose && options.FocusedAttachment < 0) _typing = _description;

            if (options.State == WidgetPreviewState.Toast)
            {
                SetOverlayVisible(false);
                ShowToast(Strings.ToastUploading, string.Format(Strings.ToastUploadingSub, 45), null, 0.45f, autoHide: false);
                return;
            }
            SetOverlayVisible(true);
            Render();
        }

        // ------------------------------------------------------------------ binding

        private bool EnsureBound()
        {
            if (_bound) return true;
            _document = GetComponent<UIDocument>();
            _reporter = GetComponent<BetaHubReporter>();
            var root = _document != null ? _document.rootVisualElement : null;
            if (root == null) return false;

            _layer = root.Q<VisualElement>(LayerName);
            _overlay = root.Q<VisualElement>(OverlayName);
            _kicker = root.Q<Label>(KickerName);
            _title = root.Q<Label>(TitleName);
            var tabsHost = root.Q<VisualElement>(TabsName);
            _subtitle = root.Q<Label>(SubtitleName);
            _screenRoot = root.Q<VisualElement>(ScreenName);
            _form = root.Q<VisualElement>(FormName);
            _type = root.Q<VisualElement>(TypeName);
            _typeBug = root.Q<Label>(TypeBugName);
            _typeSuggestion = root.Q<Label>(TypeSuggestionName);
            _termsCheck = root.Q<VisualElement>(TermsCheckName);
            _description = BindField(root, DescriptionFieldName);
            _steps = BindField(root, StepsFieldName);
            _email = BindField(root, EmailFieldName);
            _terms = root.Q<VisualElement>(TermsName);
            _sent = root.Q<VisualElement>(SentName);
            _link = root.Q<VisualElement>(LinkName);
            _pane = root.Q<VisualElement>(PaneName);
            _capLeft = root.Q<Label>(CapLeftName);
            _capRight = root.Q<VisualElement>(CapRightName);
            _attachmentsGroup = root.Q<VisualElement>(AttachmentsName);
            _groupLabel = root.Q<Label>(GroupLabelName);
            _groupCount = root.Q<Label>(GroupCountName);
            _rowsContainer = root.Q<VisualElement>(RowsName);
            _sentWith = root.Q<VisualElement>(SentWithName);
            _sentWithLabel = root.Q<Label>(SentWithLabelName);
            _sentWithCount = root.Q<Label>(SentWithCountName);
            _sentList = root.Q<Label>(SentListName);
            _sentNote = root.Q<Label>(SentNoteName);
            _preview = root.Q<VisualElement>(PreviewName);
            _previewKicker = root.Q<Label>(PreviewKickerName);
            var previewTabsHost = root.Q<VisualElement>(PreviewTabsName);
            _previewSubtitle = root.Q<Label>(PreviewSubtitleName);
            _previewCount = root.Q<Label>(PreviewCountName);
            _previewList = root.Q<VisualElement>(PreviewListName);
            _previewPane = root.Q<VisualElement>(PreviewPaneName);
            _promptsLeft = root.Q<VisualElement>(PromptsLeftName);
            _promptsRight = root.Q<VisualElement>(PromptsRightName);
            _toast = root.Q<VisualElement>(ToastName);
            _toastTitle = root.Q<Label>(ToastTitleName);
            _toastSub = root.Q<Label>(ToastSubName);
            var ringHost = root.Q<VisualElement>(RingName);

            if (_layer == null || _overlay == null || _description == null || _steps == null || _email == null || _rowsContainer == null
                || _pane == null || _preview == null || _promptsLeft == null || _toast == null || ringHost == null || _terms == null || _link == null
                || tabsHost == null || previewTabsHost == null || _type == null || _typeBug == null || _typeSuggestion == null || _termsCheck == null)
            {
                Debug.LogError(UnityBetaHubLog.Prefix + "BugReportWidget.uxml is missing expected elements. Assign the shipped UXML to the UIDocument, or keep the bh-* element names when customizing.", this);
                return false;
            }
            if (Theme == null)
            {
                Debug.LogError(UnityBetaHubLog.Prefix + "BugReportWidget has no Theme stylesheet. Assign one from Runtime/UI/Themes, e.g. BugReportTheme-Dark.uss.", this);
                return false;
            }
            _appliedSheet = null;
            ApplyStyleSheet(Theme);

            _typeLabel = _type.Q<Label>(className: NameClass);
            _termsLabel = _terms.Q<Label>(className: NameClass);
            _linkLabel = _link.Q<Label>(className: NameClass);
            _tabs = BuildTabs(tabsHost);
            _previewTabs = BuildTabs(previewTabsHost);
            _linkValue = _link.Q<VisualElement>(className: ValueClass);
            _ring = new ArcElement();
            _ring.AddToClassList(ArcClass);
            ringHost.Add(_ring);

            _pane.RegisterCallback<GeometryChangedEvent>(e => KeepAspect(_pane, e.newRect.width));
            ListenForKeys(root.panel != null ? root.panel.visualTree : root);

            // CanSend refuses longer text, so the fields stop there instead of greying out Send unexplained.
            _description.Input.maxLength = BugReportWidgetModel.DescriptionLimit;
            _steps.Input.maxLength = BugReportWidgetModel.StepsLimit;
            _email.Input.maxLength = BugReportWidgetModel.EmailLimit;
            // Long text grows the field up to its max-height, then scrolls inside it.
            _description.Input.SetVerticalScrollerVisibility(ScrollerVisibility.Auto);
            _steps.Input.SetVerticalScrollerVisibility(ScrollerVisibility.Auto);

            foreach (var field in new[] { _description, _steps, _email })
            {
                var view = field;
                // UI Toolkit selects all text on focus, so coming back to a field and typing replaced it.
                view.Input.selectAllOnFocus = false;
                view.Input.selectAllOnMouseUp = false;
                view.Root.RegisterCallback<ClickEvent>(_ => OnFieldClicked(view));
                view.Input.RegisterCallback<FocusInEvent>(_ => OnFieldFocusIn(view));
                view.Input.RegisterCallback<FocusOutEvent>(_ => OnFieldFocusOut(view));
                view.Input.RegisterValueChangedCallback(_ => Render());
            }
            // A click selects; only the checkbox and the switch's values change anything.
            _type.RegisterCallback<ClickEvent>(_ => SelectField(WidgetField.Type));
            _typeBug.RegisterCallback<ClickEvent>(e => { OnTypeClicked(ReportType.Bug); e.StopPropagation(); });
            _typeSuggestion.RegisterCallback<ClickEvent>(e => { OnTypeClicked(ReportType.Suggestion); e.StopPropagation(); });
            _terms.RegisterCallback<ClickEvent>(_ => SelectField(WidgetField.Terms));
            _termsCheck.RegisterCallback<ClickEvent>(e => { OnTermsCheckClicked(); e.StopPropagation(); });
            _link.RegisterCallback<ClickEvent>(_ => OnLinkClicked());
            _overlay.RegisterCallback<PointerDownEvent>(OnPointerUsed, TrickleDown.TrickleDown);
            // A press on the prompt bar would take focus from the field being typed; that rebuilds the bar
            // under the pointer and the click never arrives. The prompt acts on its own, so keep the focus.
            root.Q<VisualElement>(PromptsName).RegisterCallback<PointerDownEvent>(e => e.PreventDefault());
            // "Full screen" under the pane is a prompt too, rebuilt with every render.
            _capRight.RegisterCallback<PointerDownEvent>(e => e.PreventDefault());
            _capRight.RegisterCallback<ClickEvent>(_ => { if (PaneShowsAttachment) Act(PromptAction.Alt); });
            _overlay.RegisterCallback<PointerMoveEvent>(OnPointerUsed, TrickleDown.TrickleDown);
            if (TextEntry == null) TextEntry = GetComponent<ITextEntryProvider>();

            _bound = true;
            return true;
        }

        // Keyboard events go to the focused element, or to the panel's own root when nothing has
        // focus, so the panel root is the one place that sees them all. Handlers do nothing while hidden.
        private void ListenForKeys(VisualElement target)
        {
            StopListeningForKeys();
            _keyTarget = target;
            _keyTarget.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            _keyTarget.RegisterCallback<NavigationMoveEvent>(SwallowNavigation, TrickleDown.TrickleDown);
            _keyTarget.RegisterCallback<NavigationSubmitEvent>(SwallowNavigation, TrickleDown.TrickleDown);
            _keyTarget.RegisterCallback<NavigationCancelEvent>(SwallowNavigation, TrickleDown.TrickleDown);
        }

        private void StopListeningForKeys()
        {
            if (_keyTarget == null) return;
            _keyTarget.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            _keyTarget.UnregisterCallback<NavigationMoveEvent>(SwallowNavigation, TrickleDown.TrickleDown);
            _keyTarget.UnregisterCallback<NavigationSubmitEvent>(SwallowNavigation, TrickleDown.TrickleDown);
            _keyTarget.UnregisterCallback<NavigationCancelEvent>(SwallowNavigation, TrickleDown.TrickleDown);
            _keyTarget = null;
        }

        private static FieldView BindField(VisualElement root, string name)
        {
            var element = root.Q<VisualElement>(name);
            if (element == null) return null;
            var input = element.Q<TextField>();
            var prompt = element.Q<Label>(className: "bh-prompt");
            if (input == null || prompt == null) return null;
            return new FieldView { Root = element, Input = input, Prompt = prompt };
        }

        // ------------------------------------------------------------------ open

        private IEnumerator CaptureAndShow()
        {
            yield return _reporter.CaptureScreenshot();

            _sample = false;
            ApplyStyleSheet(Theme);
            _captureTime = DateTime.Now;
            _description.Input.SetValueWithoutNotify("");
            _steps.Input.SetValueWithoutNotify("");
            _email.Input.SetValueWithoutNotify("");
            _termsAccepted = false;
            _submissionError = null;
            _pendingEmail = null;
            _emailNote = null;
            _sentFocus = SentFocus.Email;
            _linkCopied = false;
            _reportType = ReportType.Bug;
            PublishIfWaiting(_active);
            if (_active != null && _active.IsUploadInProgress)
            {
                Detach(_background);
                _background = _active;
            }
            else
            {
                Detach(_active);
            }
            _active = null;
            _screen = WidgetScreen.Compose;
            ReleaseTextures();
            SetAttachments(_reporter.CollectAttachments());

            HideToast();
            SetOverlayVisible(true);
            Render();
            StartTyping(_description);
        }

        private void SetAttachments(AttachmentSet attachments)
        {
            _attachments = attachments;
            ShowRowsForType();
        }

        // The rows and the left column follow the report type: a suggestion takes only the screenshot and has no steps.
        private void ShowRowsForType()
        {
            _visible.Clear();
            foreach (var item in _attachments.Items)
            {
                if (_reportType == ReportType.Bug || BugReportSession.TravelsWithSuggestion(item.Id)) _visible.Add(item);
            }
            BuildRows(_rowsContainer, _rows, _visible, compact: true);
            BuildRows(_previewList, _previewRows, _visible, compact: false);
            for (int i = 0; i < _rows.Count; i++)
            {
                int index = i;
                _rows[i].Root.RegisterCallback<ClickEvent>(_ => OnRowClicked(index));
                _rows[i].Check.RegisterCallback<ClickEvent>(e => { OnRowCheckClicked(index); e.StopPropagation(); });
                _previewRows[i].Root.RegisterCallback<ClickEvent>(_ => OnPreviewRowClicked(index));
                _previewRows[i].Check.RegisterCallback<ClickEvent>(e => { OnPreviewRowCheckClicked(index); e.StopPropagation(); });
            }
            int columns = BugReportWidgetModel.ColumnCountFor(_visible.Count);
            _rowsContainer.EnableInClassList(RowsTwoClass, columns == 2);
            _fields = BugReportWidgetModel.FieldsFor(_reportType, TermsRequired);
            _focus = new WidgetFocus(_fields.Count, _visible.Count, columns);
            _previewIndex = 0;
            _paneKey = _previewPaneKey = null;
        }

        // the editor preview shows a terms row without turning the setting on
        private bool TermsRequired => _sample ? _sampleTerms : RequireTermsAcceptance;

        private int FieldIndex(WidgetField field)
        {
            int index = _fields.IndexOf(field);
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(field), field, "Not on the form for a " + _reportType + ".");
            return index;
        }

        // The left-column item with focus, or null on an attachment.
        private WidgetField? FocusedField => _focus.OnAttachment ? (WidgetField?)null : _fields[_focus.Index];

        private FieldView ViewFor(WidgetField? field) => field == WidgetField.Description ? _description : field == WidgetField.Steps ? _steps : null;

        private static void BuildRows(VisualElement container, List<RowView> views, IReadOnlyList<AttachmentDescriptor> items, bool compact)
        {
            container.Clear();
            views.Clear();
            foreach (var item in items)
            {
                var row = new RowView { Root = new VisualElement() };
                row.Root.AddToClassList(RowClass);
                var name = new VisualElement();
                name.AddToClassList(RowNameClass);
                row.Check = NewCheck();
                name.Add(row.Check);
                row.Name = new Label(item.Name);
                row.Name.AddToClassList(NameClass);
                name.Add(row.Name);
                if (compact && !string.IsNullOrEmpty(item.Meta))
                {
                    row.Meta = new Label(item.Meta);
                    row.Meta.AddToClassList(MetaClass);
                    name.Add(row.Meta);
                }
                row.Value = new VisualElement();
                row.Value.AddToClassList(ValueClass);
                row.Mini = new VisualElement();
                row.Mini.AddToClassList(MiniClass);
                row.MiniFill = new VisualElement();
                row.MiniFill.AddToClassList(MiniFillClass);
                row.Mini.Add(row.MiniFill);
                row.ValueLabel = new Label();
                row.Value.Add(row.Mini);
                row.Value.Add(row.ValueLabel);
                row.Root.Add(name);
                row.Root.Add(row.Value);
                container.Add(row.Root);
                views.Add(row);
            }
        }

        private static VisualElement NewCheck()
        {
            var check = new VisualElement();
            check.AddToClassList(CheckClass);
            var mark = new Label(CheckMark);
            mark.AddToClassList(CheckMarkClass);
            check.Add(mark);
            return check;
        }

        private TabsView BuildTabs(VisualElement host)
        {
            host.Clear();
            var view = new TabsView { Root = host };
            view.LeftShoulder = new VisualElement();
            view.LeftShoulder.AddToClassList(TabsShoulderClass);
            view.LeftShoulder.AddToClassList(TabsShoulderLeftClass);
            host.Add(view.LeftShoulder);
            view.Report = NewTab(host, out view.ReportKey, out view.ReportText);
            view.Attachments = NewTab(host, out view.AttachmentsKey, out var attachmentsText);
            attachmentsText.text = Caps(Strings.AttachmentsTab);
            var badge = new VisualElement();
            badge.AddToClassList(TabBadgeClass);
            view.Count = new Label();
            badge.Add(view.Count);
            attachmentsText.parent.Add(badge);
            view.RightShoulder = new VisualElement();
            view.RightShoulder.AddToClassList(TabsShoulderClass);
            host.Add(view.RightShoulder);

            // a press would take focus from the field being typed in; the tab acts on its own
            view.Report.RegisterCallback<PointerDownEvent>(e => e.PreventDefault());
            view.Attachments.RegisterCallback<PointerDownEvent>(e => e.PreventDefault());
            view.Report.RegisterCallback<ClickEvent>(_ => OnTabClicked(WidgetTab.Report));
            view.Attachments.RegisterCallback<ClickEvent>(_ => OnTabClicked(WidgetTab.Attachments));
            return view;
        }

        private static VisualElement NewTab(VisualElement host, out VisualElement key, out Label text)
        {
            var tab = new VisualElement();
            tab.AddToClassList(TabClass);
            var line = new VisualElement();
            line.AddToClassList(TabLineClass);
            key = new VisualElement();
            key.AddToClassList(TabKeyClass);
            text = new Label();
            text.AddToClassList(TabTextClass);
            line.Add(key);
            line.Add(text);
            var mark = new VisualElement();
            mark.AddToClassList(TabMarkClass);
            tab.Add(line);
            tab.Add(mark);
            host.Add(tab);
            return tab;
        }

        // ------------------------------------------------------------------ render

        private bool Configured => _sample ? !_sampleUnconfigured : _reporter.IsConfigured;
        private bool SignedIn => _sample ? _sampleSignedIn : _reporter.IsUserAuthenticated;
        private string SignedInName => _sample ? (_sampleSignedIn ? SampleSignedInName : null) : _reporter.UserDisplayName;
        private string IssueId => _sample ? SampleIssueId : _active?.IssueId;
        private bool Interactive => (_screen == WidgetScreen.Compose && Configured) || _screen == WidgetScreen.Sent || _screen == WidgetScreen.Preview;
        // a suggestion takes no email: it publishes on its own
        private bool CanEmail => !SignedIn && _reportType == ReportType.Bug && string.IsNullOrEmpty(_pendingEmail)
            && (_sample || (_active != null && _active.State == ReportSubmissionState.AwaitingPublish));

        // a suggestion shorter than the project's minimum is refused by BetaHub
        private int MinLength => _reportType == ReportType.Suggestion ? SuggestionMinLength : 0;

        private bool CanSendNow()
        {
            return _screen == WidgetScreen.Compose
                && BugReportWidgetModel.CanSend(_description.Input.value, TermsRequired, _termsAccepted, Configured, MinLength);
        }

        private WidgetFocusKind FocusKind
        {
            get
            {
                switch (FocusedField)
                {
                    case null: return WidgetFocusKind.Attachment;
                    case WidgetField.Type: return WidgetFocusKind.Type;
                    case WidgetField.Terms: return WidgetFocusKind.Terms;
                    default: return WidgetFocusKind.Field;
                }
            }
        }

        private int IncludedCount()
        {
            int included = 0;
            foreach (var item in _visible)
            {
                if (_attachments.IsIncluded(item.Id)) included++;
            }
            return included;
        }

        private void Render()
        {
            if (!_bound) return;
            bool locked = _screen == WidgetScreen.Sending || _screen == WidgetScreen.Uploading || (_screen == WidgetScreen.Compose && !Configured);
            _overlay.EnableInClassList(LockedClass, locked);
            // hover marks are for mouse players; a resting cursor must not mark a row while a controller drives
            _overlay.EnableInClassList(PointerClass, _glyphs == GlyphSet.Keyboard);
            // a report on its way can't change, so the lock is more than a look
            _description.Input.focusable = !locked;
            _steps.Input.focusable = !locked;

            RenderHeader();
            bool sent = _screen == WidgetScreen.Sent;
            _form.EnableInClassList(HiddenClass, sent);
            _sent.EnableInClassList(HiddenClass, !sent);
            _attachmentsGroup.EnableInClassList(HiddenClass, sent);
            _sentWith.EnableInClassList(HiddenClass, !sent);

            if (sent) RenderSent();
            else RenderForm();

            RenderPane();
            RenderPreview();
            RenderPrompts();
        }

        private void RenderHeader()
        {
            BugReportWidgetModel.Header header;
            if (_screen == WidgetScreen.Uploading) header = BugReportWidgetModel.UploadingHeader(Strings);
            else if (_screen == WidgetScreen.Sent) header = BugReportWidgetModel.SentHeader(IssueId, SignedIn, _reportType, Strings);
            else header = BugReportWidgetModel.ComposeHeader(Configured, Debug.isDebugBuild || Application.isEditor,
                _sample ? SampleConfigurationError : _reporter.ConfigurationError, _submissionError, SignedInName, _reportType, Strings);

            _kicker.text = Caps(Strings.Kicker);
            _title.text = Caps(header.Title);
            _title.EnableInClassList(HiddenClass, header.Tabs);
            _tabs.Root.EnableInClassList(HiddenClass, !header.Tabs);
            RenderTabs(_tabs, WidgetTab.Report);
            RenderTabs(_previewTabs, WidgetTab.Attachments);
            _subtitle.text = header.Subtitle ?? "";
            _title.EnableInClassList(TitleErrorClass, header.Kind == BugReportWidgetModel.HeaderKind.Error);
            _title.EnableInClassList(TitleWarnClass, header.Kind == BugReportWidgetModel.HeaderKind.Warning);
            _subtitle.EnableInClassList(SubErrorClass, header.Kind == BugReportWidgetModel.HeaderKind.Error);
            _subtitle.EnableInClassList(SubWarnClass, header.Kind == BugReportWidgetModel.HeaderKind.Warning);
        }

        private void RenderTabs(TabsView tabs, WidgetTab open)
        {
            tabs.ReportText.text = Caps(BugReportWidgetModel.ReportTitle(_reportType, Strings));
            tabs.Count.text = IncludedCount().ToString();
            tabs.Report.EnableInClassList(TabOnClass, open == WidgetTab.Report);
            tabs.Attachments.EnableInClassList(TabOnClass, open == WidgetTab.Attachments);
            if (tabs.KeysFor == _glyphs) return;
            tabs.KeysFor = _glyphs;
            // a keyboard's keys sit in the tabs; a pad's shoulder buttons either side of them
            bool keyboard = _glyphs == GlyphSet.Keyboard;
            var reportHost = keyboard ? tabs.ReportKey : tabs.LeftShoulder;
            var attachmentsHost = keyboard ? tabs.AttachmentsKey : tabs.RightShoulder;
            foreach (var host in new[] { tabs.LeftShoulder, tabs.ReportKey, tabs.AttachmentsKey, tabs.RightShoulder })
            {
                host.Clear();
                host.EnableInClassList(HiddenClass, host != reportHost && host != attachmentsHost);
            }
            reportHost.Add(Glyph(PromptAction.TabReport));
            attachmentsHost.Add(Glyph(PromptAction.TabAttachments));
        }

        private void RenderForm()
        {
            bool focusVisible = _screen == WidgetScreen.Compose && Configured;
            WidgetField? focused = focusVisible ? FocusedField : null;

            _type.EnableInClassList(RowFocusedClass, focused == WidgetField.Type);
            _typeLabel.text = Strings.ReportTypeLabel;
            _typeBug.text = Caps(Strings.TypeBug);
            _typeSuggestion.text = Caps(Strings.TypeSuggestion);
            _typeBug.EnableInClassList(SwitchOptionOnClass, _reportType == ReportType.Bug);
            _typeSuggestion.EnableInClassList(SwitchOptionOnClass, _reportType == ReportType.Suggestion);

            string description = _description.Input.value;
            RenderField(_description, Strings.DescriptionLabel, BugReportWidgetModel.DescriptionTag(description, MinLength, Strings),
                BugReportWidgetModel.IsAtLimit(description.Length, BugReportWidgetModel.DescriptionLimit), Strings.DescriptionPrompt, focused == WidgetField.Description);
            _steps.Root.EnableInClassList(HiddenClass, _reportType == ReportType.Suggestion);
            int steps = _steps.Input.value.Length;
            RenderField(_steps, Strings.StepsLabel, BugReportWidgetModel.FieldTag(Strings.Optional, steps, BugReportWidgetModel.StepsLimit, Strings),
                BugReportWidgetModel.IsAtLimit(steps, BugReportWidgetModel.StepsLimit), Strings.StepsPrompt, focused == WidgetField.Steps);

            _terms.EnableInClassList(HiddenClass, !TermsRequired);
            _terms.EnableInClassList(RowFocusedClass, focused == WidgetField.Terms);
            _termsLabel.text = Strings.TermsLabel;
            _termsLabel.EnableInClassList(NameOffClass, !_termsAccepted);
            _termsCheck.EnableInClassList(CheckOnClass, _termsAccepted);

            for (int i = 0; i < _rows.Count; i++)
            {
                var item = _visible[i];
                bool on = _attachments.IsIncluded(item.Id);
                RowStatus status;
                if (_screen == WidgetScreen.Uploading && _active != null && _active.TryGetUploadState(item.Id, out var state))
                {
                    status = BugReportWidgetModel.UploadRowStatus(on, state, Strings);
                }
                else if (_screen == WidgetScreen.Uploading && _sample)
                {
                    status = BugReportWidgetModel.UploadRowStatus(on, SampleUploadState(i), Strings);
                }
                else
                {
                    status = BugReportWidgetModel.ComposeRowStatus(on, Strings);
                }
                var row = _rows[i];
                row.Root.EnableInClassList(RowFocusedClass, focusVisible && _focus.OnAttachment && _focus.Index == i);
                RenderIncluded(row, on);
                SetValue(row.Value, Caps(status.Text), status.Tone, status.Progress);
            }
            _groupLabel.text = Caps(Strings.AttachmentsGroup);
            _groupCount.text = Caps(string.Format(Strings.AttachmentsCount, IncludedCount(), _visible.Count));
            _attachmentsGroup.EnableInClassList(HiddenClass, _visible.Count == 0);
        }

        private static void RenderIncluded(RowView row, bool included)
        {
            row.Check.EnableInClassList(CheckOnClass, included);
            row.Name.EnableInClassList(NameOffClass, !included);
        }

        private void RenderField(FieldView field, string label, string tag, bool atLimit, string prompt, bool focused)
        {
            var labels = field.Root.Q<VisualElement>(className: "bh-fld-label").Query<Label>().ToList();
            labels[0].text = Caps(label);
            labels[1].text = tag;
            labels[1].EnableInClassList(TagLimitClass, atLimit);
            bool editing = _typing == field;
            field.Root.EnableInClassList(FieldFocusedClass, focused || editing);
            field.Root.EnableInClassList(FieldEditingClass, editing);
            field.Prompt.text = prompt;
            field.Prompt.EnableInClassList(HiddenClass, editing || !string.IsNullOrEmpty(field.Input.value));
        }

        private void RenderSent()
        {
            bool canEmail = CanEmail;
            bool showEmail = !SignedIn && (canEmail || !string.IsNullOrEmpty(_pendingEmail));
            _email.Root.EnableInClassList(HiddenClass, !showEmail);
            int email = _email.Input.value.Length;
            RenderField(_email, Strings.EmailLabel, BugReportWidgetModel.FieldTag(Strings.Optional, email, BugReportWidgetModel.EmailLimit, Strings),
                BugReportWidgetModel.IsAtLimit(email, BugReportWidgetModel.EmailLimit), Strings.EmailPrompt, canEmail && _sentFocus == SentFocus.Email);
            _email.Input.SetEnabled(canEmail);
            _link.EnableInClassList(RowFocusedClass, canEmail && _typing == null && _sentFocus == SentFocus.Link);

            string note = _emailNote ?? (SignedIn ? Strings.SignedInNote
                : !string.IsNullOrEmpty(_pendingEmail) ? string.Format(Strings.EmailSaved, _pendingEmail)
                : Strings.EmailNote);
            _sentNote.text = note;
            _sentNote.EnableInClassList(NoteErrorClass, _emailNoteIsError && _emailNote != null);
            // its notes are about an email when it's fixed, which a suggestion doesn't get
            _sentNote.EnableInClassList(HiddenClass, _reportType == ReportType.Suggestion && _emailNote == null);

            _linkLabel.text = Strings.ReportLink;
            SetValue(_linkValue, Caps(_linkCopied ? Strings.LinkCopied : Strings.Copy), _linkCopied ? RowTone.Ok : RowTone.Accent, -1f);

            var names = new List<string>();
            foreach (var item in _visible)
            {
                if (_attachments.IsIncluded(item.Id)) names.Add(item.Name);
            }
            _sentWithLabel.text = Caps(Strings.SentWith);
            _sentWithCount.text = names.Count.ToString();
            _sentList.text = names.Count > 0 ? string.Join(Separator, names) : Strings.TextOnly;
        }

        private static void SetValue(VisualElement value, string text, RowTone tone, float progress)
        {
            var label = value.Q<Label>();
            label.text = text;
            value.EnableInClassList(ValueMutedClass, tone == RowTone.Muted);
            value.EnableInClassList(ValueOkClass, tone == RowTone.Ok);
            value.EnableInClassList(ValueErrorClass, tone == RowTone.Error);
            var mini = value.Q<VisualElement>(className: MiniClass);
            if (mini == null) return;
            mini.EnableInClassList(HiddenClass, progress < 0f);
            if (progress >= 0f)
            {
                mini.Q<VisualElement>(className: MiniFillClass).style.width = Length.Percent(progress * 100f);
            }
        }

        // ------------------------------------------------------------------ pane: follows focus

        // The pane shows the focused attachment, with "Full screen" under it; otherwise the screenshot.
        private bool PaneShowsAttachment => _screen == WidgetScreen.Compose && Configured && _focus.OnAttachment;

        private void RenderPane()
        {
            AttachmentDescriptor item = null;
            if (PaneShowsAttachment) item = _visible[_focus.Index];

            string key = item == null ? BuiltInAttachments.Screenshot + ":frame" : item.Id;
            if (key != _paneKey)
            {
                _paneKey = key;
                FillPane(_pane, item, fullScreen: false);
            }

            _capLeft.text = item == null ? Caps(Strings.ScreenshotCaption)
                : Caps(string.IsNullOrEmpty(item.Meta) ? item.Name : item.Name + Separator + item.Meta);
            _capRight.Clear();
            _capRight.EnableInClassList(CapActionClass, item != null);
            if (item != null)
            {
                _capRight.Add(Glyph(PromptAction.Alt));
                _capRight.Add(new Label(Strings.AttachmentsTab));
            }
            else
            {
                _capRight.Add(new Label(_captureTime.ToString(TimeFormat)));
            }
        }

        private void RenderPreview()
        {
            bool visible = _screen == WidgetScreen.Preview && _visible.Count > 0;
            _preview.EnableInClassList(HiddenClass, !visible);
            _screenRoot.EnableInClassList(HiddenClass, visible);
            if (!visible) return;

            _previewIndex = Mathf.Clamp(_previewIndex, 0, _visible.Count - 1);
            var item = _visible[_previewIndex];
            _previewKicker.text = Caps(Strings.Kicker);
            // the tabs stand where the title was, so the subtitle names the attachment
            _previewSubtitle.text = item.Name + Separator + Describe(item);
            _previewCount.text = Caps(string.Format(Strings.PreviewCount, _previewIndex + 1, _visible.Count));
            for (int i = 0; i < _previewRows.Count; i++)
            {
                bool on = _attachments.IsIncluded(_visible[i].Id);
                var status = BugReportWidgetModel.ComposeRowStatus(on, Strings);
                _previewRows[i].Root.EnableInClassList(RowFocusedClass, i == _previewIndex);
                RenderIncluded(_previewRows[i], on);
                SetValue(_previewRows[i].Value, Caps(status.Text), status.Tone, -1f);
            }
            if (item.Id != _previewPaneKey)
            {
                _previewPaneKey = item.Id;
                FillPane(_previewPane, item, fullScreen: true);
            }
        }

        private string Describe(AttachmentDescriptor item)
        {
            if (item.Id == BuiltInAttachments.Screenshot)
            {
                var texture = ScreenshotTexture();
                return texture != null ? string.Format(Strings.PreviewShot, texture.width, texture.height) : Strings.PreviewShotNoSize;
            }
            if (item.Id == BuiltInAttachments.Recording) return Strings.PreviewClip;
            if (item.Id == BuiltInAttachments.Latency) return Strings.LatencyNote;
            if (item.Id == BuiltInAttachments.Location) return Strings.LocationNote;
            switch (item.Kind)
            {
                case AttachmentKind.Clip: return Strings.PreviewVideo;
                case AttachmentKind.Log: return Strings.PreviewLog;
                default: return Strings.FileAddedBy;
            }
        }

        private void FillPane(VisualElement pane, AttachmentDescriptor item, bool fullScreen)
        {
            pane.Clear();
            if (fullScreen) _previewLog = null;
            if (item == null || item.Id == BuiltInAttachments.Screenshot)
            {
                AddImage(pane, ScreenshotTexture(), Strings.NoScreenshot);
                return;
            }
            if (item.Id == BuiltInAttachments.Recording)
            {
                AddFrames(pane);
                return;
            }
            if (item.Id == BuiltInAttachments.Latency)
            {
                AddKeyValues(pane, Strings.LatencyNote, _sample ? SampleLatency() : _reporter.LatencyPreview());
                return;
            }
            if (item.Id == BuiltInAttachments.Location)
            {
                AddKeyValues(pane, Strings.LocationNote, null);
                return;
            }
            switch (item.Kind)
            {
                case AttachmentKind.Image:
                    AddImage(pane, _sample ? _sampleTexture : LoadTexture(item.FilePath), Strings.NoScreenshot);
                    break;
                case AttachmentKind.Log:
                    if (fullScreen) AddScrollingLog(pane, item);
                    else AddLog(pane, item);
                    break;
                case AttachmentKind.Data:
                    AddKeyValues(pane, null, item.Fields);
                    break;
                default:
                    AddKeyValues(pane, Strings.FileAddedBy, new[]
                    {
                        new KeyValuePair<string, string>("file", Path.GetFileName(item.FilePath ?? item.Name)),
                        new KeyValuePair<string, string>("size", string.IsNullOrEmpty(item.Meta) ? "?" : item.Meta),
                    });
                    break;
            }
        }

        private static void AddImage(VisualElement pane, Texture2D texture, string emptyText)
        {
            if (texture == null)
            {
                var empty = new Label(emptyText);
                empty.AddToClassList(PaneEmptyClass);
                pane.Add(empty);
                return;
            }
            var image = new VisualElement();
            image.AddToClassList(PaneImageClass);
            image.style.backgroundImage = new StyleBackground(texture);
            pane.Add(image);
        }

        private void AddFrames(VisualElement pane)
        {
            var frames = new List<(Texture texture, string label)>();
            if (_sample)
            {
                for (int i = 0; i < GameRecorder.PreviewFrameCount; i++)
                {
                    frames.Add((_sampleTexture, BugReportWidgetModel.FormatDuration(i * (int)GameRecorder.PreviewFrameInterval)));
                }
            }
            else if (_reporter.Recorder != null)
            {
                var recorded = _reporter.Recorder.GetPreviewFrames();
                float oldest = recorded.Count > 0 ? recorded[0].SecondsBeforeNewest : 0f;
                foreach (var frame in recorded)
                {
                    frames.Add((frame.Texture, BugReportWidgetModel.FormatDuration((int)(oldest - frame.SecondsBeforeNewest))));
                }
            }

            if (frames.Count == 0)
            {
                var empty = new Label(Strings.ClipNoFrames);
                empty.AddToClassList(PaneEmptyClass);
                pane.Add(empty);
                return;
            }

            var grid = new VisualElement();
            grid.AddToClassList(PaneFramesClass);
            VisualElement row = null;
            for (int i = 0; i < frames.Count; i++)
            {
                if (i % FramesPerRow == 0)
                {
                    row = new VisualElement();
                    row.AddToClassList(FrameRowClass);
                    grid.Add(row);
                }
                var cell = new VisualElement();
                cell.AddToClassList(FrameClass);
                if (i == frames.Count - 1) cell.AddToClassList(FrameLastClass);
                if (frames[i].texture is Texture2D t2d) cell.style.backgroundImage = new StyleBackground(t2d);
                else if (frames[i].texture is RenderTexture rt) cell.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
                cell.Add(new Label(frames[i].label));
                row.Add(cell);
            }
            // Fewer frames than a full row (under a minute of play): pad so they stay on the grid.
            while (row.childCount < FramesPerRow)
            {
                var spacer = new VisualElement();
                spacer.AddToClassList(FrameClass);
                spacer.AddToClassList(FrameEmptyClass);
                row.Add(spacer);
            }
            pane.Add(grid);
        }

        // Full screen: every line read for the preview, newest at the bottom and in view first.
        private void AddScrollingLog(VisualElement pane, AttachmentDescriptor item)
        {
            string text = _sample ? SampleLog : ReadLogForPreview(item);
            // the scrollbar shows only when there is more than fits
            var scroll = new ScrollView(ScrollViewMode.Vertical)
            {
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Auto,
            };
            scroll.AddToClassList(LogScrollClass);
            foreach (var line in BugReportWidgetModel.LogTail(text, int.MaxValue).Split('\n'))
            {
                scroll.Add(LogLine(line));
            }
            // Stays at the end through layout passes (wrapped lines keep growing it) until the player scrolls up.
            bool atEnd = true;
            scroll.verticalScroller.valueChanged += value =>
            {
                atEnd = value >= scroll.verticalScroller.highValue - LayoutEpsilon;
                UpdateLogEdges();
            };
            EventCallback<GeometryChangedEvent> keepAtEnd = _ =>
            {
                if (atEnd) scroll.verticalScroller.value = scroll.verticalScroller.highValue;
                UpdateLogEdges();
            };
            scroll.contentContainer.RegisterCallback(keepAtEnd);
            scroll.contentViewport.RegisterCallback(keepAtEnd);
            pane.Add(scroll);
            _previewLog = scroll;
        }

        private Label LogLine(string line)
        {
            var label = new Label(line);
            if (ContainsAny(line, ErrorMarkers)) label.AddToClassList(LogErrorClass);
            else if (ContainsAny(line, WarningMarkers)) label.AddToClassList(LogWarnClass);
            return label;
        }

        private void ScrollPreviewLog(float pages)
        {
            if (_previewLog == null) return;
            _previewLog.verticalScroller.value += pages * LogPageFraction * _previewLog.contentViewport.layout.height;
        }

        // Older lines and Newer lines grey out at their ends; a log opens at its newest line.
        private void ResetLogEdges()
        {
            _logAtTop = false;
            _logAtEnd = true;
        }

        private void UpdateLogEdges()
        {
            if (_previewLog == null) return;
            var scroller = _previewLog.verticalScroller;
            bool top = scroller.value <= scroller.lowValue + LayoutEpsilon;
            bool end = scroller.value >= scroller.highValue - LayoutEpsilon;
            if (top == _logAtTop && end == _logAtEnd) return;
            _logAtTop = top;
            _logAtEnd = end;
            if (IsVisible && _screen == WidgetScreen.Preview) RenderPrompts();
        }

        private void AddLog(VisualElement pane, AttachmentDescriptor item)
        {
            string text = _sample ? SampleLog : ReadLogForPreview(item);
            var box = new VisualElement();
            box.AddToClassList(PaneLogClass);
            foreach (var line in BugReportWidgetModel.LogTail(text, BugReportWidgetModel.LogTailLines).Split('\n'))
            {
                box.Add(LogLine(line));
            }
            pane.Add(box);
        }

        private static void AddKeyValues(VisualElement pane, string note, IReadOnlyList<KeyValuePair<string, string>> fields)
        {
            var box = new VisualElement();
            box.AddToClassList(PaneKvClass);
            if (!string.IsNullOrEmpty(note))
            {
                var label = new Label(note);
                label.AddToClassList(PaneNoteClass);
                box.Add(label);
            }
            if (fields != null)
            {
                foreach (var kvp in fields)
                {
                    var row = new VisualElement();
                    row.AddToClassList(KvClass);
                    var key = new Label(kvp.Key);
                    key.AddToClassList(KvKeyClass);
                    var value = new Label(kvp.Value);
                    value.AddToClassList(KvValueClass);
                    row.Add(key);
                    row.Add(value);
                    box.Add(row);
                }
            }
            pane.Add(box);
        }

        private static void KeepAspect(VisualElement pane, float width)
        {
            float height = Mathf.Round(width * PaneAspect);
            if (Mathf.Abs(pane.resolvedStyle.height - height) > 0.5f) pane.style.height = height;
        }

        // ------------------------------------------------------------------ prompts

        private void RenderPrompts()
        {
            var context = new PromptContext
            {
                Screen = _screen,
                Focus = FocusKind,
                Typing = _typing != null,
                Gamepad = _glyphs != GlyphSet.Keyboard,
                FocusedIncluded = _screen == WidgetScreen.Preview
                    ? _visible.Count > 0 && _attachments.IsIncluded(_visible[Mathf.Clamp(_previewIndex, 0, _visible.Count - 1)].Id)
                    : _focus.OnAttachment && _attachments.IsIncluded(_visible[_focus.Index].Id),
                TermsAccepted = _termsAccepted,
                CanSend = CanSendNow(),
                HasError = _submissionError != null,
                Configured = Configured,
                CanEmail = CanEmail,
                SentFocus = _sentFocus,
                HasAttachments = _visible.Count > 0,
                PreviewIsLog = _screen == WidgetScreen.Preview && _previewLog != null,
                LogAtTop = _logAtTop,
                LogAtEnd = _logAtEnd,
            };
            var bar = BugReportWidgetModel.Prompts(context, Strings);
            FillPrompts(_promptsLeft, bar.Left);
            FillPrompts(_promptsRight, bar.Right);
        }

        private void FillPrompts(VisualElement side, List<Prompt> prompts)
        {
            side.Clear();
            foreach (var prompt in prompts)
            {
                var item = new VisualElement();
                item.AddToClassList(PromptClass);
                item.EnableInClassList(PromptPrimaryClass, prompt.Primary);
                item.EnableInClassList(PromptDisabledClass, prompt.Disabled);
                if (prompt.Busy)
                {
                    var spinner = new ArcElement { Sweep = 0.75f };
                    spinner.AddToClassList(SpinClass);
                    spinner.AddToClassList(ArcClass);
                    spinner.schedule.Execute(() => spinner.Spin(SpinnerDegreesPerTick)).Every(SpinnerIntervalMs);
                    item.Add(spinner);
                }
                else
                {
                    item.Add(Glyph(prompt.Action));
                    var action = prompt.Action;
                    // Move only explains the arrows
                    item.EnableInClassList(PromptHintClass, action == PromptAction.Move);
                    if (!prompt.Disabled && action != PromptAction.Move) item.RegisterCallback<ClickEvent>(e => { Act(action); e.StopPropagation(); });
                }
                var label = new Label(prompt.Label);
                label.AddToClassList(PromptLabelClass);
                item.Add(label);
                side.Add(item);
            }
        }

        private VisualElement Glyph(PromptAction action)
        {
            var glyph = new VisualElement();
            glyph.AddToClassList(GlyphClass);
            foreach (var part in PromptGlyphs.For(_glyphs, action))
            {
                var cap = new Label(part.Text);
                switch (part.Kind)
                {
                    case GlyphPartKind.Key:
                        cap.AddToClassList(KeyClass);
                        break;
                    case GlyphPartKind.Dpad:
                        cap.AddToClassList(DpadClass);
                        break;
                    case GlyphPartKind.Face:
                        string set = PadClassPrefix + GlyphSetClassNames[(int)_glyphs];
                        cap.AddToClassList(PadClass);
                        cap.AddToClassList(set);
                        cap.AddToClassList(set + "-" + PadButtonClassNames[(int)part.Button]);
                        break;
                }
                glyph.Add(cap);
            }
            // UI Toolkit has no :last-child
            if (glyph.childCount > 0) glyph[glyph.childCount - 1].AddToClassList(KeyLastClass);
            return glyph;
        }

        // ------------------------------------------------------------------ input

        private void SwallowNavigation(EventBase evt)
        {
            if (!IsVisible) return;
            // Focus is ours: UI Toolkit's own navigation would jump into text fields.
            evt.StopPropagation();
            evt.PreventDefault();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (!IsVisible) return;
            // 1 and 2 switch the tabs when nothing is typed; on AZERTY those keys read & and é
            WidgetTab? tab = _typing == null ? TabKey(evt) : null;
            if (evt.keyCode == KeyCode.None && tab == null) return;
            // a floating platform keyboard types through key events; the player still holds the controller
            if (!_textEntryOpen) UseGlyphs(GlyphSet.Keyboard);
            bool send = (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) && (evt.ctrlKey || evt.commandKey);
            // Tab and Shift+Tab go through every item, typing or not
            PromptAction step = evt.shiftKey ? PromptAction.Prev : PromptAction.Next;
            bool handled = true;

            if (send) Act(PromptAction.Send);
            else if (tab != null) Act(tab == WidgetTab.Report ? PromptAction.TabReport : PromptAction.TabAttachments);
            else if (_typing != null)
            {
                if (evt.keyCode == KeyCode.Escape) Act(PromptAction.Back);
                else if (evt.keyCode == KeyCode.Tab) Act(step);
                else if (evt.keyCode == KeyCode.Return && _typing == _email) Act(PromptAction.Send);
                // up from the first character or down from the last leaves the field, as Tab does
                else if (evt.keyCode == KeyCode.UpArrow && !evt.shiftKey && _typing != _email && CaretAt(_typing.Input, start: true)) Act(PromptAction.Prev);
                else if (evt.keyCode == KeyCode.DownArrow && !evt.shiftKey && _typing != _email && CaretAt(_typing.Input, start: false)) Act(PromptAction.Next);
                else handled = false;
            }
            else if (_screen == WidgetScreen.Preview)
            {
                if (evt.keyCode == KeyCode.UpArrow) MovePreview(-1);
                else if (evt.keyCode == KeyCode.DownArrow) MovePreview(1);
                else if (evt.keyCode == KeyCode.Tab) Act(step);
                else if (evt.keyCode == KeyCode.PageUp) Act(PromptAction.ScrollUp);
                else if (evt.keyCode == KeyCode.PageDown) Act(PromptAction.ScrollDown);
                else if (evt.keyCode == KeyCode.P) Act(PromptAction.Alt);
                else handled = HandleCommonKeys(evt.keyCode);
            }
            else
            {
                if (evt.keyCode == KeyCode.UpArrow) MoveFocus(FocusMove.Up);
                else if (evt.keyCode == KeyCode.DownArrow) MoveFocus(FocusMove.Down);
                else if (evt.keyCode == KeyCode.LeftArrow) MoveFocus(FocusMove.Left);
                else if (evt.keyCode == KeyCode.RightArrow) MoveFocus(FocusMove.Right);
                else if (evt.keyCode == KeyCode.Tab) Act(step);
                else if (evt.keyCode == KeyCode.P) Act(PromptAction.Alt);
                else handled = HandleCommonKeys(evt.keyCode);
            }

            if (handled)
            {
                evt.StopImmediatePropagation();
                evt.PreventDefault();
            }
        }

        private static WidgetTab? TabKey(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.Alpha1:
                case KeyCode.Keypad1:
                case KeyCode.Ampersand:
                    return WidgetTab.Report;
                case KeyCode.Alpha2:
                case KeyCode.Keypad2:
                    return WidgetTab.Attachments;
            }
            switch (evt.character)
            {
                case '1':
                case '&':
                    return WidgetTab.Report;
                case '2':
                case 'é':
                    return WidgetTab.Attachments;
                default:
                    return null;
            }
        }

        // The caret sits before the first character or after the last, with nothing selected.
        private static bool CaretAt(TextField input, bool start)
        {
            if (input.cursorIndex != input.selectIndex) return false;
            return start ? input.cursorIndex == 0 : input.cursorIndex >= input.value.Length;
        }

        private bool HandleCommonKeys(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                case KeyCode.Space:
                    Act(PromptAction.Confirm);
                    return true;
                case KeyCode.Escape:
                    Act(PromptAction.Back);
                    return true;
                default:
                    return false;
            }
        }

        private void OnPointerUsed(IPointerEvent evt)
        {
            if (!IsVisible || evt.pointerType != UnityEngine.UIElements.PointerType.mouse) return;
            if (evt is PointerMoveEvent && evt.deltaPosition.sqrMagnitude < MouseMoveThreshold * MouseMoveThreshold) return;
            UseGlyphs(GlyphSet.Keyboard);
        }

        private void OnPointerUsed(PointerDownEvent evt) => OnPointerUsed((IPointerEvent)evt);
        private void OnPointerUsed(PointerMoveEvent evt) => OnPointerUsed((IPointerEvent)evt);

        private void UseGlyphs(GlyphSet glyphs)
        {
            if (_glyphs == glyphs) return;
            _glyphs = glyphs;
            if (IsVisible) Render();
        }

    #if ENABLE_INPUT_SYSTEM
        private void PollGamepad()
        {
            var pad = Gamepad.current;
            if (pad == null) return;
            if (pad != _lastPad)
            {
                _lastPad = pad;
                _lastPadGlyphs = GlyphSetOf(pad);
            }

            for (int i = 0; i < FaceButtons.Length; i++)
            {
                bool down = FaceControl(pad, FaceButtons[i]).isPressed;
                bool pressed = down && !_faceHeld[i];
                _faceHeld[i] = down;
                // the platform keyboard owns the controller while it is up
                if (!pressed || _textEntryOpen) continue;
                UseGlyphs(_lastPadGlyphs);
                OnPadButton(PadLayout.ActionFor(_lastPadGlyphs, FaceButtons[i]));
                if (!IsVisible) return;
            }

            for (int i = 0; i < _shoulderHeld.Length; i++)
            {
                bool down = (i == 0 ? pad.leftShoulder : pad.rightShoulder).isPressed;
                bool pressed = down && !_shoulderHeld[i];
                _shoulderHeld[i] = down;
                if (!pressed || _textEntryOpen) continue;
                UseGlyphs(_lastPadGlyphs);
                OnPadButton(i == 0 ? PromptAction.TabReport : PromptAction.TabAttachments);
            }

            float stick = pad.rightStick.ReadValue().y;
            if (_screen == WidgetScreen.Preview && _previewLog != null && Mathf.Abs(stick) > StickScrollDeadZone)
            {
                UseGlyphs(_lastPadGlyphs);
                // tilting up reads back towards the start
                _previewLog.verticalScroller.value -= stick * StickScrollSpeed * Time.unscaledDeltaTime;
            }

            var direction = PadDirection(pad);
            if (direction != null && !_textEntryOpen) UseGlyphs(_lastPadGlyphs);
            var move = _padRepeat.Step(_textEntryOpen ? null : direction, Time.unscaledTime);
            if (move != null) OnPadMove(move.Value);
        }

        // Buttons and sticks already held when the form opens (the menu press that opened it) wait for a release.
        private void ResetPadInput()
        {
            var pad = Gamepad.current;
            for (int i = 0; i < FaceButtons.Length; i++)
            {
                _faceHeld[i] = pad != null && FaceControl(pad, FaceButtons[i]).isPressed;
            }
            _shoulderHeld[0] = pad != null && pad.leftShoulder.isPressed;
            _shoulderHeld[1] = pad != null && pad.rightShoulder.isPressed;
            _padRepeat.Suppress(pad != null ? PadDirection(pad) : null);
        }

        private static FocusMove? PadDirection(Gamepad pad)
        {
            var dpad = pad.dpad.ReadValue();
            var stick = pad.leftStick.ReadValue();
            return PadNavigation.DirectionOf(dpad.x, dpad.y) ?? PadNavigation.DirectionOf(stick.x, stick.y);
        }

        private static UnityEngine.InputSystem.Controls.ButtonControl FaceControl(Gamepad pad, PadButton button)
        {
            switch (button)
            {
                case PadButton.South: return pad.buttonSouth;
                case PadButton.East: return pad.buttonEast;
                case PadButton.West: return pad.buttonWest;
                case PadButton.North: return pad.buttonNorth;
                default: throw new ArgumentOutOfRangeException(nameof(button), button, null);
            }
        }

        private static GlyphSet GlyphSetOf(InputDevice device)
        {
            var layouts = new List<string>();
            for (string layout = device.layout; !string.IsNullOrEmpty(layout); layout = InputSystem.GetNameOfBaseLayout(layout))
            {
                layouts.Add(layout);
            }
            return GlyphSets.ForLayouts(layouts);
        }
    #endif

        private void OnPadButton(PromptAction action)
        {
            // while typing a controller only finishes or sends; the field keeps the rest
            if (_typing != null && action != PromptAction.Back && action != PromptAction.Send) return;
            Act(action);
        }

        private void OnPadMove(FocusMove move)
        {
            if (_typing != null) return;
            if (_screen == WidgetScreen.Preview)
            {
                if (move == FocusMove.Up) MovePreview(-1);
                else if (move == FocusMove.Down) MovePreview(1);
            }
            else
            {
                MoveFocus(move);
            }
        }

        private void MoveFocus(FocusMove move)
        {
            if (_screen == WidgetScreen.Sent)
            {
                // the arrows move between the email and the report link
                if (CanEmail && _typing == null && (move == FocusMove.Up || move == FocusMove.Down))
                {
                    _sentFocus = move == FocusMove.Up ? SentFocus.Email : SentFocus.Link;
                    Render();
                }
                return;
            }
            if (_screen != WidgetScreen.Compose || !Configured || _typing != null) return;
            // left and right on the type row choose the type, as on a game's settings screen
            if (FocusedField == WidgetField.Type && (move == FocusMove.Left || move == FocusMove.Right))
            {
                SetReportType(move == FocusMove.Left ? ReportType.Bug : ReportType.Suggestion);
                return;
            }
            if (_focus.Move(move)) Render();
        }

        // Tab, Shift+Tab, or an arrow out of a field's first or last character: the next or previous item.
        // On a keyboard, landing on a text field types there, as in a browser.
        private void StepFocus(int delta)
        {
            if (_typing != null && _typing != _email) _focus.FocusField(FieldIndex(FieldOf(_typing)));
            _focus.Step(delta);
            var field = ViewFor(FocusedField);
            if (field != null && _glyphs == GlyphSet.Keyboard)
            {
                StartTyping(field);
                return;
            }
            StopTyping();
            Render();
        }

        private WidgetField FieldOf(FieldView field)
        {
            if (field == _description) return WidgetField.Description;
            if (field == _steps) return WidgetField.Steps;
            throw new ArgumentException("Not a field of the report form.", nameof(field));
        }

        private void MovePreview(int delta)
        {
            int count = _visible.Count;
            if (count == 0) return;
            int index = (_previewIndex + delta + count) % count;
            // one attachment: the same log stays where it was scrolled
            if (index != _previewIndex) ResetLogEdges();
            _previewIndex = index;
            Render();
        }

        private void OpenPreview(int index)
        {
            if (_visible.Count == 0) return;
            StopTyping();
            _previewIndex = Mathf.Clamp(index, 0, _visible.Count - 1);
            _previewPaneKey = null;
            ResetLogEdges();
            _screen = WidgetScreen.Preview;
            Render();
        }

        // Back, P (the key that opened it) and the report tab close it, on the attachment it showed.
        private void ClosePreview()
        {
            _screen = WidgetScreen.Compose;
            if (_previewIndex < _visible.Count) _focus.FocusAttachment(_previewIndex);
            Render();
            FocusOverlay();
        }

        private void SetReportType(ReportType type)
        {
            if (_reportType == type) return;
            StopTyping();
            _reportType = type;
            ShowRowsForType();
            _focus.FocusField(FieldIndex(WidgetField.Type));
            Render();
        }

        /// <summary>One verb from the prompt bar, a key, or a click on a prompt.</summary>
        private void Act(PromptAction action)
        {
            switch (_screen)
            {
                case WidgetScreen.Preview:
                    if (action == PromptAction.Back || action == PromptAction.Alt || action == PromptAction.TabReport) ClosePreview();
                    else if (action == PromptAction.Confirm) ToggleAttachment(_previewIndex);
                    else if (action == PromptAction.Navigate || action == PromptAction.Next) MovePreview(1);
                    else if (action == PromptAction.Prev) MovePreview(-1);
                    else if (action == PromptAction.ScrollUp) ScrollPreviewLog(-1f);
                    else if (action == PromptAction.ScrollDown) ScrollPreviewLog(1f);
                    break;

                case WidgetScreen.Compose:
                    ActCompose(action);
                    break;

                case WidgetScreen.Uploading:
                    if (action == PromptAction.Back) Hide();
                    break;

                case WidgetScreen.Sent:
                    ActSent(action);
                    break;
            }
        }

        private void ActCompose(PromptAction action)
        {
            if (!Configured)
            {
                if (action == PromptAction.Back) Hide();
                return;
            }
            switch (action)
            {
                case PromptAction.Send:
                    if (CanSendNow()) SubmitBugReport();
                    break;
                case PromptAction.Back:
                    if (_typing != null) StopTyping();
                    else Hide();
                    break;
                case PromptAction.Next:
                case PromptAction.Prev:
                    StepFocus(action == PromptAction.Next ? 1 : -1);
                    break;
                case PromptAction.ChangeType:
                    SetReportType(_reportType == ReportType.Bug ? ReportType.Suggestion : ReportType.Bug);
                    break;
                case PromptAction.TabAttachments:
                    OpenPreview(_focus.OnAttachment ? _focus.Index : _focus.LastAttachment);
                    break;
                case PromptAction.Confirm:
                    switch (FocusKind)
                    {
                        case WidgetFocusKind.Attachment: ToggleAttachment(_focus.Index); break;
                        case WidgetFocusKind.Type: Act(PromptAction.ChangeType); break;
                        case WidgetFocusKind.Terms: _termsAccepted = !_termsAccepted; Render(); break;
                        default: StartTyping(ViewFor(FocusedField)); break;
                    }
                    break;
                case PromptAction.Alt:
                    if (FocusKind == WidgetFocusKind.Attachment) OpenPreview(_focus.Index);
                    else if (FocusKind == WidgetFocusKind.Terms) { if (!string.IsNullOrEmpty(TermsUrl)) Application.OpenURL(TermsUrl); }
                    else OpenPreview(_focus.LastAttachment);
                    break;
            }
        }

        private void ActSent(PromptAction action)
        {
            switch (action)
            {
                case PromptAction.Back:
                    if (_typing != null) StopTyping();
                    else Hide();
                    break;
                case PromptAction.Confirm:
                    if (CanEmail && _sentFocus == SentFocus.Link) CopyLink();
                    else if (CanEmail) StartTyping(_email);
                    break;
                case PromptAction.Next:
                case PromptAction.Prev:
                    if (!CanEmail) break;
                    StopTyping();
                    _sentFocus = _sentFocus == SentFocus.Email ? SentFocus.Link : SentFocus.Email;
                    Render();
                    break;
                case PromptAction.Alt:
                    CopyLink();
                    break;
                case PromptAction.Send:
                    if (CanEmail) SendEmail();
                    break;
            }
        }

        private void ToggleAttachment(int index)
        {
            if (index < 0 || index >= _visible.Count) return;
            if (_screen != WidgetScreen.Compose && _screen != WidgetScreen.Preview) return;
            _attachments.Toggle(_visible[index].Id);
            Render();
        }

        // A click on a row selects it; its checkbox includes or leaves it out.
        private void OnRowClicked(int index)
        {
            if (_screen != WidgetScreen.Compose || !Configured) return;
            StopTyping();
            _focus.FocusAttachment(index);
            Render();
            FocusOverlay();
        }

        private void OnRowCheckClicked(int index)
        {
            if (_screen != WidgetScreen.Compose || !Configured) return;
            StopTyping();
            _focus.FocusAttachment(index);
            ToggleAttachment(index);
            FocusOverlay();
        }

        private void OnPreviewRowClicked(int index)
        {
            if (_screen != WidgetScreen.Preview || index == _previewIndex) return;
            MovePreview(index - _previewIndex);
        }

        private void OnPreviewRowCheckClicked(int index)
        {
            if (_screen != WidgetScreen.Preview) return;
            OnPreviewRowClicked(index);
            ToggleAttachment(index);
        }

        private void OnTabClicked(WidgetTab tab)
        {
            if (tab == WidgetTab.Attachments && _screen == WidgetScreen.Compose && Configured) Act(PromptAction.TabAttachments);
            else if (tab == WidgetTab.Report && _screen == WidgetScreen.Preview) Act(PromptAction.TabReport);
        }

        private void SelectField(WidgetField field)
        {
            if (_screen != WidgetScreen.Compose || !Configured) return;
            StopTyping();
            _focus.FocusField(FieldIndex(field));
            Render();
            FocusOverlay();
        }

        private void OnTypeClicked(ReportType type)
        {
            if (_screen != WidgetScreen.Compose || !Configured) return;
            SelectField(WidgetField.Type);
            SetReportType(type);
        }

        private void OnTermsCheckClicked()
        {
            if (_screen != WidgetScreen.Compose || !Configured) return;
            SelectField(WidgetField.Terms);
            _termsAccepted = !_termsAccepted;
            Render();
        }

        private void OnLinkClicked()
        {
            if (_screen != WidgetScreen.Sent) return;
            StopTyping();
            _sentFocus = SentFocus.Link;
            CopyLink();
            Render();
        }

        private void OnFieldClicked(FieldView field)
        {
            if (!Interactive || _screen == WidgetScreen.Preview) return;
            if (field == _email && !CanEmail) return;
            StartTyping(field);
        }

        private void OnFieldFocusIn(FieldView field)
        {
            if (_typing == field) return;
            _typing = field;
            NoteFocus(field);
            Render();
        }

        // Typing in a field puts focus there: the row for a form field, the email on the sent screen.
        private void NoteFocus(FieldView field)
        {
            if (field == _email) _sentFocus = SentFocus.Email;
            else _focus.FocusField(FieldIndex(FieldOf(field)));
        }

        private void OnFieldFocusOut(FieldView field)
        {
            ScrollToStart(field);
            if (_typing != field) return;
            _typing = null;
            Render();
        }

        private void StartTyping(FieldView field)
        {
            if (!IsVisible) return;
            _typing = field;
            NoteFocus(field);
            Render();
            // A field that was display:none until this frame cannot take focus before the next style pass.
            field.Input.schedule.Execute(() =>
            {
                if (_typing != field) return;
                // A focused TextField hands a second Focus() to its own frame, where typed keys are dropped;
                // a mouse click has already focused the field by now.
                if (!HoldsFocus(field.Input)) field.Input.Focus();
                OpenTextEntry(field);
            });
        }

        // A field left in the middle of long text shows its first lines again.
        private static void ScrollToStart(FieldView field)
        {
            // single-line fields have no scroll view
            var scroll = field.Input.Q<ScrollView>();
            if (scroll != null) scroll.scrollOffset = Vector2.zero;
        }

        private static bool HoldsFocus(VisualElement element)
        {
            var focused = element.focusController?.focusedElement as VisualElement;
            return focused != null && (focused == element || element.Contains(focused));
        }

        private void StopTyping()
        {
            CloseTextEntry();
            if (_typing == null) return;
            var field = _typing;
            _typing = null;
            field.Input.Blur();
            Render();
            FocusOverlay();
        }

        // Controller players get the platform keyboard; keyboard players already have one.
        private void OpenTextEntry(FieldView field)
        {
            if (_glyphs == GlyphSet.Keyboard || TextEntry == null || _textEntryOpen) return;
            var request = new TextEntryRequest
            {
                Label = field == _description ? Strings.DescriptionLabel : field == _steps ? Strings.StepsLabel : Strings.EmailLabel,
                Text = field.Input.value,
                Multiline = field.Input.multiline,
                Email = field == _email,
                MaxLength = field == _description ? BugReportWidgetModel.DescriptionLimit
                    : field == _steps ? BugReportWidgetModel.StepsLimit : BugReportWidgetModel.EmailLimit,
                FieldRect = ScreenRect(field.Input),
            };
            _textEntryOpen = TextEntry.Open(request, text => OnTextEntryDone(field, text));
        }

        private void OnTextEntryDone(FieldView field, string text)
        {
            if (!_textEntryOpen) return;
            _textEntryOpen = false;
            // a modal keyboard's text counts even if the field lost focus meanwhile
            if (text != null) field.Input.value = text;
            if (_typing == field) StopTyping();
        }

        private void CloseTextEntry()
        {
            if (!_textEntryOpen) return;
            _textEntryOpen = false;
            TextEntry.Close();
        }

        // Panel coordinates to screen pixels, top-left origin.
        private static Rect ScreenRect(VisualElement element)
        {
            var bound = element.worldBound;
            var root = element.panel.visualTree.layout;
            float x = Screen.width / root.width, y = Screen.height / root.height;
            return new Rect(bound.x * x, bound.y * y, bound.width * x, bound.height * y);
        }

        private void FocusOverlay()
        {
            _overlay.schedule.Execute(() => { if (IsVisible && _typing == null) _overlay.Focus(); });
        }

        // ------------------------------------------------------------------ submission

        private void SubmitBugReport()
        {
            if (_active != null && !_active.IsFinished) return;
            _submissionError = null;
            StopTyping();
            _screen = WidgetScreen.Sending;
            Render();

            _sentFocus = SentFocus.Email;
            _linkCopied = false;
            _active = _reportType == ReportType.Suggestion
                ? _reporter.SubmitSuggestion(_description.Input.value, _attachments)
                : _reporter.Submit(_description.Input.value, _steps.Input.value, _attachments);
            _active.StateChanged += OnSubmissionStateChanged;
            _active.ReporterEmailFailed += OnReporterEmailFailed;
            _active.AttachmentUploadChanged += OnAttachmentUploadChanged;
            if (_active.State == ReportSubmissionState.Failed)
            {
                OnSubmissionStateChanged(_active); // already failed: StateChanged will not fire again
            }
        }

        private void OnSubmissionStateChanged(ReportSubmission submission)
        {
            if (submission == _background)
            {
                if (submission.IsFinished) FinishInBackground(submission);
                return;
            }
            if (submission != _active) return;
            switch (submission.State)
            {
                case ReportSubmissionState.Failed:
                    _submissionError = submission.Error;
                    _screen = WidgetScreen.Compose;
                    if (!IsVisible) ShowToast(Strings.ToastFailed, submission.Error, ToastErrorClass, 1f, autoHide: true);
                    Detach(submission);
                    break;
                case ReportSubmissionState.Published:
                    _screen = WidgetScreen.Sent;
                    if (!IsVisible) ShowToast(Strings.ToastDone, Strings.ToastDoneSub, ToastOkClass, 1f, autoHide: true);
                    Detach(submission);
                    break;
                default:
                    _screen = BugReportWidgetModel.ScreenFor(submission.State);
                    if (!IsVisible)
                    {
                        PublishIfWaiting(submission);
                        RefreshToast();
                    }
                    break;
            }
            Render();
        }

        private void PublishIfWaiting(ReportSubmission submission)
        {
            if (submission != null && BugReportWidgetModel.ShouldPublishWithoutEmail(submission.State, IsVisible))
            {
                submission.Publish(false);
            }
        }

        private void NotifyWindowHidden()
        {
            OnBugReportWindowHidden?.Invoke();
            if (_reporter != null) _reporter.NotifyReportWindowHidden();
            RestoreCursorState();
        }

        private void FinishInBackground(ReportSubmission submission)
        {
            Detach(submission);
            _background = null;
            if (IsVisible) return;
            if (submission.State == ReportSubmissionState.Published) ShowToast(Strings.ToastDone, Strings.ToastDoneSub, ToastOkClass, 1f, autoHide: true);
            else ShowToast(Strings.ToastFailed, submission.Error, ToastErrorClass, 1f, autoHide: true);
        }

        private void OnAttachmentUploadChanged(ReportSubmission submission, string attachmentId)
        {
            if (submission != _active && submission != _background) return;
            if (submission == _active && IsVisible && _screen == WidgetScreen.Uploading) RenderForm();
            if (!IsVisible) RefreshToast();
        }

        private void SendEmail()
        {
            string email = (_email.Input.value ?? "").Trim();
            if (email.Length < 3 || !email.Contains("@"))
            {
                _emailNote = Strings.EmailInvalid;
                _emailNoteIsError = true;
                Render();
                return;
            }
            _emailNote = null;
            _pendingEmail = email;
            StopTyping();
            if (!_sample)
            {
                _active.SetReporterEmail(email);
                _active.Publish(true);
            }
            Render();
        }

        private void OnReporterEmailFailed(string error)
        {
            _pendingEmail = null;
            _emailNote = Strings.EmailFailed;
            _emailNoteIsError = true;
            Render();
        }

        private void CopyLink()
        {
            string url = _active?.IssueUrl;
            if (string.IsNullOrEmpty(url)) return;
            GUIUtility.systemCopyBuffer = url;
            _linkCopied = true;
            SetValue(_linkValue, Caps(Strings.LinkCopied), RowTone.Ok, -1f);
        }

        private void Detach(ReportSubmission submission)
        {
            if (submission == null) return;
            submission.StateChanged -= OnSubmissionStateChanged;
            submission.ReporterEmailFailed -= OnReporterEmailFailed;
            submission.AttachmentUploadChanged -= OnAttachmentUploadChanged;
        }

        // ------------------------------------------------------------------ toast

        // The toast follows whichever report is uploading while the form is closed.
        private void RefreshToast()
        {
            var uploading = _active != null && _active.IsUploadInProgress ? _active
                          : _background != null && _background.IsUploadInProgress ? _background
                          : null;
            if (uploading == null || IsVisible) return;
            float progress = uploading.UploadProgress;
            ShowToast(Strings.ToastUploading, string.Format(Strings.ToastUploadingSub, BugReportWidgetModel.Percent(progress)), null, progress, autoHide: false);
        }

        private void ShowToast(string title, string sub, string toneClass, float progress, bool autoHide)
        {
            _toastTitle.text = Caps(title);
            _toastSub.text = sub ?? "";
            _toast.EnableInClassList(ToastOkClass, toneClass == ToastOkClass);
            _toast.EnableInClassList(ToastErrorClass, toneClass == ToastErrorClass);
            _ring.Sweep = Mathf.Clamp01(progress);
            _toast.RemoveFromClassList(HiddenClass);
            if (autoHide) _toast.schedule.Execute(HideToast).StartingIn(ToastDoneMilliseconds);
        }

        private void HideToast()
        {
            _toast.AddToClassList(HiddenClass);
        }

        // ------------------------------------------------------------------ helpers

        private void SetOverlayVisible(bool visible)
        {
            _overlay.EnableInClassList(HiddenClass, !visible);
    #if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            if (visible) EnsureEventSystem();
            else ReleaseEventSystem();
    #endif
            if (!visible) return;
            FocusOverlay();
    #if ENABLE_INPUT_SYSTEM
            ResetPadInput();
    #endif
        }

    #if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        // UI Toolkit reads the legacy Input Manager unless an EventSystem feeds it. With only the new
        // Input System active and no EventSystem, clicks (and keys in a build) never reach the form.
        private void EnsureEventSystem()
        {
            if (!Application.isPlaying || _ownEventSystem != null || EventSystem.current != null) return;
            _ownEventSystem = new GameObject(OwnEventSystemName, typeof(EventSystem), typeof(InputSystemUIInputModule));
            _ownEventSystem.transform.SetParent(transform, false);
            if (_eventSystemNoted) return;
            _eventSystemNoted = true;
            Debug.Log(UnityBetaHubLog.Prefix + "Added an EventSystem with InputSystemUIInputModule while the bug report form is open: UI Toolkit needs one when only the new Input System is active. Add your own EventSystem to the scene to skip this.", this);
        }

        private void ReleaseEventSystem()
        {
            if (_ownEventSystem == null) return;
            Destroy(_ownEventSystem);
            _ownEventSystem = null;
        }
    #endif

        private static string Caps(string text) => text?.ToUpperInvariant() ?? "";

        private static bool ContainsAny(string text, string[] markers)
        {
            foreach (var marker in markers)
            {
                if (text.IndexOf(marker, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        private Texture2D ScreenshotTexture() => _sample ? _sampleTexture : LoadTexture(_reporter.ScreenshotPath);

        // Cached for one open of the form; files can change between opens.
        private Texture2D LoadTexture(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (_textures.TryGetValue(path, out var cached)) return cached;
            try
            {
                if (!File.Exists(path)) return null;
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(File.ReadAllBytes(path)))
                {
                    Destroy(texture);
                    return null;
                }
                _textures[path] = texture;
                return texture;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(UnityBetaHubLog.Prefix + "Preview of " + path + " unavailable: " + ex.Message, this);
                return null;
            }
        }

        private void ReleaseTextures()
        {
            foreach (var texture in _textures.Values)
            {
                if (texture != null) Destroy(texture);
            }
            _textures.Clear();
        }

        private string ReadLogForPreview(AttachmentDescriptor item)
        {
            try
            {
                // the logger keeps recent lines in memory; the tail below reads the file it shares
                if (item.Id == BuiltInAttachments.PlayerLog) BetaHubReporter.Logger?.ForceFlush();
                if (string.IsNullOrEmpty(item.FilePath) || !File.Exists(item.FilePath)) return "";
                using (var stream = new FileStream(item.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long start = Math.Max(0, stream.Length - MaxLogBytesForPreview);
                    stream.Seek(start, SeekOrigin.Begin);
                    var bytes = new byte[stream.Length - start];
                    int read = stream.Read(bytes, 0, bytes.Length);
                    string text = Encoding.UTF8.GetString(bytes, 0, read);
                    if (start == 0) return text;
                    // the read began mid-file: its first line (and maybe a split character) is a fragment
                    int firstBreak = text.IndexOf('\n');
                    return firstBreak < 0 ? "" : text.Substring(firstBreak + 1);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(UnityBetaHubLog.Prefix + "Log preview unavailable: " + ex.Message, this);
                return "";
            }
        }

        // ------------------------------------------------------------------ sample data

        // longer than the full-screen preview, so its renders show the log scrolled to the end
        private const string SampleLog =
            "[14:00:02] Game started, build 0.9.4 (Steam)\n[14:00:02] Graphics: Vulkan, 1920x1080, VSync on\n" +
            "[14:00:03] Audio: 48 kHz, 2 channels\n[14:00:04] Input: keyboard and mouse, gamepad connected\n" +
            "[14:00:05] Loading save slot 2\n[14:00:06] Save loaded: 41 h 12 min played\n[14:00:08] Scene loaded: Town_Hub\n" +
            "[14:00:09] Quest log restored: 14 active, 63 done\n[14:00:21] Shop opened: Blacksmith\n[14:00:34] Bought Shield_01\n" +
            "[14:00:35] Shop closed\n[14:01:02] Warn Texture streaming budget exceeded (1.1 GB)\n[14:01:10] Teleport: Town_Hub -> Dungeon_03\n" +
            "[14:01:11] Unloading Town_Hub\n[14:01:14] Navmesh built for Dungeon_03 in 380 ms\n[14:01:20] Enemy group spawned: Skeleton x4\n" +
            "[14:01:42] Combat started\n[14:01:58] Warn Animation event missing on Skeleton_Attack_02\n[14:02:03] Combat ended: 4 kills\n" +
            "[14:02:04] Loot dropped: Sword_02, 31 gold\n[14:02:06] Autosave written in 120 ms\n" +
            "[14:02:11] Scene loaded: Dungeon_03\n[14:02:12] Player spawned at (12.4, 0.0, -3.1)\n[14:02:13] Inventory opened\n" +
            "[14:02:14] Warn Missing ref: Sword_LOD1\n[14:02:15] Inventory.Drop(sword)\n[14:02:15] Warn Drop target Shield_01 has no slot\n" +
            "[14:02:15] NullReferenceException at Inventory.cs:212\n[14:02:15]   at InventoryUI.OnDrop (InventoryUI.cs:88)\n" +
            "[14:02:17] Frame time spike: 2,140 ms\n[14:02:19] BetaHub: report opened";

        private IEnumerable<AttachmentDescriptor> SampleAttachments(int count)
        {
            EnsureSampleTexture();
            var all = new[]
            {
                new AttachmentDescriptor { Id = BuiltInAttachments.Screenshot, Name = "Screenshot", Meta = "this frame", Kind = AttachmentKind.Image },
                new AttachmentDescriptor { Id = BuiltInAttachments.Recording, Name = "Last 60 s of play", Meta = "video", Kind = AttachmentKind.Clip },
                new AttachmentDescriptor { Id = BuiltInAttachments.PlayerLog, Name = "Game log", Meta = "1.2 MB", Kind = AttachmentKind.Log },
                new AttachmentDescriptor { Id = BuiltInAttachments.Latency, Name = "Network", Meta = "ping times", Kind = AttachmentKind.Data },
                new AttachmentDescriptor { Id = BuiltInAttachments.Location, Name = "Location", Meta = "country, ISP", Kind = AttachmentKind.Data },
                new AttachmentDescriptor { Id = "hardware", Name = "Hardware", Meta = "GPU, CPU, RAM", Kind = AttachmentKind.Data, Fields = new[]
                {
                    new KeyValuePair<string, string>("gpu", "Steam Deck APU"), new KeyValuePair<string, string>("cpu", "AMD Custom APU 0405 · 8 threads"),
                    new KeyValuePair<string, string>("ram", "16 GB"), new KeyValuePair<string, string>("os", "SteamOS 3.6"),
                } },
                new AttachmentDescriptor { Id = "save", Name = "Save file", Meta = "84 KB", Kind = AttachmentKind.File, FilePath = "slot2.sav" },
                new AttachmentDescriptor { Id = "performance", Name = "Performance", Meta = "frame times", Kind = AttachmentKind.Data, Fields = new[]
                {
                    new KeyValuePair<string, string>("average", "16.8 ms"), new KeyValuePair<string, string>("95th percentile", "31.2 ms"),
                    new KeyValuePair<string, string>("worst", "2,140 ms at 14:02:17"),
                } },
                new AttachmentDescriptor { Id = "inventory", Name = "Inventory dump", Meta = "6 KB", Kind = AttachmentKind.File, FilePath = "inventory.json" },
            };
            for (int i = 0; i < count; i++) yield return all[i];
        }

        private static IReadOnlyList<KeyValuePair<string, string>> SampleLatency()
        {
            return new[]
            {
                new KeyValuePair<string, string>("ping.betahub.io", "38 ms · 0% loss"),
                new KeyValuePair<string, string>("eu-west.example.com", "41 ms · 0% loss"),
            };
        }

        private static AttachmentUploadState SampleUploadState(int index)
        {
            if (index == 0) return new AttachmentUploadState(AttachmentUploadStatus.Sent, 1f);
            if (index == 1) return new AttachmentUploadState(AttachmentUploadStatus.Uploading, 0.45f);
            return new AttachmentUploadState(AttachmentUploadStatus.Queued, 0f);
        }

        // A dim dusk gradient standing in for a game frame.
        private void EnsureSampleTexture()
        {
            if (_sampleTexture != null) return;
            _sampleTexture = new Texture2D(SampleTextureWidth, SampleTextureHeight, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var top = new Color(0.05f, 0.06f, 0.09f);
            var horizon = new Color(0.16f, 0.13f, 0.11f);
            var floor = new Color(0.05f, 0.035f, 0.03f);
            var pixels = new Color[SampleTextureWidth * SampleTextureHeight];
            for (int y = 0; y < SampleTextureHeight; y++)
            {
                float v = y / (float)(SampleTextureHeight - 1);
                var row = v > 0.38f ? Color.Lerp(horizon, top, (v - 0.38f) / 0.62f) : Color.Lerp(floor, horizon, v / 0.38f);
                for (int x = 0; x < SampleTextureWidth; x++)
                {
                    float glow = Mathf.Max(0f, 1f - Vector2.Distance(new Vector2(x, y), new Vector2(230, 250)) / 180f);
                    pixels[y * SampleTextureWidth + x] = row + new Color(0.45f, 0.25f, 0.08f) * glow * glow;
                }
            }
            _sampleTexture.SetPixels(pixels);
            _sampleTexture.Apply();
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
            UseGlyphs(context.control.device is Gamepad pad ? GlyphSetOf(pad) : GlyphSet.Keyboard);
            Show();
        }
    #endif
    }

    /// <summary>
    /// An arc drawn with the vector API: the toast's progress ring and the sending spinner. Colors come
    /// from USS: <c>color</c> for the arc, <c>border-color</c> for the track.
    /// </summary>
    public class ArcElement : VisualElement
    {
        private const float StrokeWidth = 5f;
        private const float FullCircle = 360f;
        private const float StartAngle = -90f;
        private float _sweep;
        private float _rotation;

        /// <summary>0 to 1 of the circle drawn in the accent color.</summary>
        public float Sweep
        {
            get => _sweep;
            set { _sweep = Mathf.Clamp01(value); MarkDirtyRepaint(); }
        }

        public ArcElement()
        {
            pickingMode = PickingMode.Ignore;
            style.flexGrow = 1;
            generateVisualContent += Draw;
        }

        public void Spin(float degrees)
        {
            _rotation = (_rotation + degrees) % FullCircle;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            float radius = Mathf.Min(rect.width, rect.height) / 2f - StrokeWidth / 2f;
            if (radius <= 0f) return;
            var center = rect.center;
            var painter = context.painter2D;
            painter.lineWidth = StrokeWidth;

            painter.strokeColor = resolvedStyle.borderTopColor;
            painter.BeginPath();
            painter.Arc(center, radius, 0f, FullCircle);
            painter.Stroke();

            if (_sweep <= 0f) return;
            float start = StartAngle + _rotation;
            painter.strokeColor = resolvedStyle.color;
            painter.lineCap = LineCap.Round;
            painter.BeginPath();
            painter.Arc(center, radius, start, start + _sweep * FullCircle);
            painter.Stroke();
        }
    }
}
