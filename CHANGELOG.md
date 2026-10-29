# Changelog

## Unreleased

### Added
- New default UI: `BugReportWidget` prefab built with UI Toolkit (`Runtime/UI/BugReportWidget.uxml` + `.uss`). One panel, six states, attachments shown as cards with a real screenshot thumbnail, background upload toast, inline optional email after sending, errors in the header instead of banners, keyboard shortcuts and gamepad focus. Reskin by overriding USS variables; light theme included; all strings editable; inspector preview of every screen without Play mode.
- `Tools~/UxmlPreview/uxml2html.rb`: renders the UXML/USS to HTML for design review in a browser. Measured against Unity: identical layout, 4% of widget pixels differ (glyph anti-aliasing).
- `Tests/Preview`: PlayMode tests that render the widget to a PNG headless and smoke-test the prefab through every preview state.
- `Tools~/EditTests`: dotnet project that runs the plain-C# EditMode tests in seconds without Unity.
- Attachment list: `BetaHubReporter.CollectAttachments()` returns one row per screenshot, recording, log, network and location data, added file and provider item; `Submit(description, steps, AttachmentSet)` sends only the included rows. A left-out Location row sends no geolocation request.
- `BetaHubReporter.AddFile()` attaches any file (saves, dumps) and `RegisterAttachmentProvider()` adds rows computed at open time; Data rows are sent as a text file.
- Per-attachment upload progress: `ReportSubmission.AttachmentUploadChanged`, `TryGetUploadState()` and `UploadProgress`, fed by `UnityWebRequest.uploadProgress`. A failed upload marks its row as Failed.
- `BetaHubReporter.CaptureScreenshot()` replaces the previous capture instead of piling up a screenshot per opened form.
- Widget redesign: a full-screen form sized for TVs and handhelds. What happened on the left, what gets sent on the right as settings-style rows (two columns from six), a preview pane that follows focus (screenshot, clip frames, log tail, data), a full-screen preview per attachment, and a prompt bar with key caps. Errors replace the title; upload progress shows per row and in an in-game toast.
- Themes: six built-in theme stylesheets generated from `ThemeRecipe` (`Runtime/UI/Themes`), `BugReportWidget.Theme` and `SetTheme()`. Replaces `UseLightTheme`.
- `GameRecorder.GetPreviewFrames()`: a small frame every 10 s of the recording for the clip preview (not on WebGL).
- Fonts: Barlow, Barlow Semi Condensed and JetBrains Mono (SIL OFL) for the widget.
- Controllers: with the Input System package the widget takes a gamepad (d-pad or stick with repeat, face buttons per the prompt bar) and draws the last-used device's buttons: key caps, Xbox, PlayStation or Switch (A confirms). `ITextEntryProvider` plugs in a platform keyboard for controller players, such as Steam Deck's floating keyboard or a console's text entry.
- With only the new Input System active and no EventSystem in the scene, the widget adds one (`InputSystemUIInputModule`) while the form is open; without it UI Toolkit ignored mouse clicks.

### Changed
- Split `BugReportUI` into a headless `BetaHubReporter` component (configuration, attachments, custom fields, recording policy, submission) and a pure view. Custom UIs call `BetaHubReporter.Submit()` and follow the returned `ReportSubmission` handle instead of driving coroutines. `Issue` is now a thin HTTP client; publish/email sequencing lives in `ReportSubmission`.
- Breaking: `ProjectID`, `AuthToken`, `SubmitEndpoint`, release, upload mode, include flags, `DefaultEmailAddress`, provider references and the attachment/custom field API moved from `BugReportUI` to `BetaHubReporter`. `GeolocationProvider` and `LatencyProvider` auto-attach to `BetaHubReporter`. `ReportSubmittedUI.Show` takes a `ReportSubmission`.

### Improved
- Startup configuration checks log actionable `BetaHub:`-prefixed errors and hints; a misconfigured reporter returns an already failed submission instead of throwing.
- Reporter email failures are surfaced to the UI instead of being logged only.
- Publish failures now put the submission in a Failed state with the server message.

### Fixed
- `GameRecorder.RenderCursor` read the legacy `Input.mousePosition`, which throws when only the new Input System is active; it reads `Mouse.current` there.

## 1.8.3

### Fixed
- Fix audio/video desync in IL2CPP builds: the native process wrapper killed FFmpeg on stop before it could finalize the in-progress recording segment, dropping the most recent video segment (~one segment length) so audio ended up out of sync. The wrapper now lets FFmpeg exit cleanly (finalizing its last segment) before force-killing. Requires the updated native libraries.
- IL2CPP: Fix the FFmpeg media-duration probe returning empty — the native process wrapper now drains buffered stderr after the process exits, so video duration is measured correctly instead of falling back to an estimate.
- VideoEncoder: When the duration probe is unavailable, the fallback estimate now accounts for the partial final segment instead of over-counting it.

## 1.8.2

### Fixed
- Fix .wav audio files and other temp files (concat.txt, temp_concat.mp4) persisting in the BH_Recording folder between sessions.
- Fix orphaned audio file leak when the game exits or scene unloads while recording is active.
- Clean up stale recording directories from previous sessions on startup.

## 1.8.1

### Fixed
- VideoEncoder: Fix audio-video muxing failure on non-English OS locales (e.g. French) caused by locale-sensitive decimal separator in FFmpeg arguments.

## 1.8.0

### Added
- Audio recording: Bug reports now include audio alongside video. Supports Unity's built-in audio (via AudioListener) and Wwise (requires `BETAHUB_WWISE` scripting define). Audio capture is enabled by default using Unity AudioListener.
- Audio pause/resume: Audio recording automatically pauses when the bug report UI is shown and resumes when dismissed, keeping audio in sync with video.

### Improved
- HTTP error logging now includes the server response body for easier debugging.

## 1.7.0

### Added
- Client event telemetry: The plugin now reports upload lifecycle events (started, completed, failed, recording errors) to the backend for improved diagnostics.

## 1.6.0

### Added
- AddVideo API: Developers can now programmatically attach custom video files to bug reports via `BugReportUI.AddVideo()`. Developer-added videos are always uploaded regardless of the Include Video toggle state.

## 1.5.1

### Fixed
- WebGL Video Recording: Fix crash on Safari/WebKit browsers caused by unsupported MediaRecorder mimeType. Added proper fallback chain (webm/vp9 -> webm/vp8 -> webm -> mp4).

## 1.4.3

### Fixed
- LatencyProvider: ICMP ping is not available on WebGL; the component now automatically falls back to HTTP mode with a warning.

## 1.4.2

### Fixed
- VideoRecording: If ffmpeg is not available, the video recording will not be initialized.

## 1.4.1

### Fixed
- Logger: Fixed disposing of the logger error.

## 1.4.0

### Added
- Added support for geolocation and latency data collection.

### Fixed
- Logger: Fixed disposing of the logger when the application is closed.
- VideoEncoder: Fix compilation warnings.

## 1.3.2

### Chnages
- Logger: (Optimization) Keep log the file open for the duration of the application.

## 1.3.1

### Fixed
- Logs: Fixed sharing violation error when writing to the log file.

## 1.3.0

### Added
- Can now provide an email to receive a link to the report and updates on the report status.

### Changed
- Using the new draf submission flow.

## 1.2.0

### Added
- Three toggles to allow the user to choose which data to include in the bug report (screenshot, video, and log file).

## 1.1.0

### Improvements
- Bug Reporter Canvas Prefab: Changed scaling mode to `ScaleWithScreenSize` to make it more responsive on different screen sizes.
- Video Recording: Pausing the recording when the bug submission form is visible, resuming when it's not.
- UI: Added close button to the bug submission form.

## 1.0.0

Initial Relase