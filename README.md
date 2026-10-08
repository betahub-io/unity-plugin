# BetaHub Bug Reporter Plugin

An easy in-game bug reporting plugin for Unity with video recording and log collection.

https://github.com/betahub-io/unity-plugin/assets/113201/b01f372c-c3ac-4f49-8b38-e892db873adf

## QuickStart Demo

This plugin is ready for immediate testing. The included configuration (project ID and token) points to a public **demo project on BetaHub**:

👉 [View the Demo Project](https://app.betahub.io/projects/pr-5287510306)

To test the plugin:

1. Run the demo scene included in the package.
2. Submit a bug report using the in-game form.
3. **Be sure to enter your email address** in the submission form.

You'll receive a link to your report via email, allowing you to see how bug submissions appear in the BetaHub dashboard.

Note: Submissions to the demo project are only visible to the person who submitted them via the email link. However, there is a quota on how many reports can be submitted to this shared demo project. Once the limit is reached, you may encounter errors when trying to submit new reports.

For continued use and more extensive testing, we strongly recommend creating your own BetaHub project. **Free BetaHub accounts do not enforce hard limits** on the number of bug reports, making them suitable for active development and testing.

## Features

- **In-game bug submission form**: Easily submit bugs with a form that only asks for a description and steps to reproduce. Titles, priority, and tags are handled by BetaHub's AI algorithms.
- **Device authentication**: Optional OAuth-like authentication flow that allows users to sign in via web browser. Authenticated users can submit bug reports without providing email addresses.
- **Video recording**: Record a video of the bug happening in-game. The video is automatically recorded and attached to the bug report.
- **Audio recording**: Record in-game audio alongside video. Supports Unity's built-in audio system and Wwise. Audio is automatically synced with video and pauses/resumes with the bug report UI.
- **Log collection**: Collect logs from the game and attach them to the bug report. By default, Unity logs are collected, but you can also add custom logs.
- **Screenshot of the game**: A screenshot of the game is automatically attached to the bug report when the user submits a bug.
- **Geolocation and latency data**: Optional components for collecting user's country information and network latency measurements to help with debugging location-specific and network-related issues.
- **Working Example Scene**: The plugin comes with a working example scene that demonstrates how to use the plugin, serving as a good starting point for your implementation.
- **Customizable**: Customize the bug submission form to ask for more information or to change the look and feel of the form.

## Requirements

- Unity 2021.3 or later (for Mac builds we recommend at least 2022.3 due to Metal handling bugs in 2021)
- BetaHub account (sign up at [betahub.io](https://www.betahub.io))
- Windows, macOS, or Linux
- Internet connection
- A living, breathing game project
- A bug or two to report
- A human being to play the game

## Installation

The installation and setup documentation is available [here](https://www.betahub.io/docs/integration-guides/).

For detailed information about optional components like geolocation and latency collection, see the included [Documentation.html](Documentation.html) file.

## Device Authentication (Optional)

The plugin supports an optional device authentication flow that provides a seamless user experience for bug reporting.

### How It Works

1. **User Authentication**: Users click "Sign In" which opens a browser window for BetaHub authentication
2. **Secure Token Storage**: JWT authentication tokens are securely stored locally and persist across game sessions (24-hour expiry)
3. **Streamlined Bug Reports**: Authenticated users can submit bug reports without entering email addresses
4. **Automatic User Association**: Bug reports are automatically linked to the authenticated user's BetaHub account

### Setup

1. Add the `DeviceAuthCanvas` prefab to your scene alongside the `BugReportWidget` prefab
2. Configure both prefabs with the same Project ID
3. Link the `DeviceAuthManager` component to your `BetaHubReporter` component (on the `BugReportWidget` object)
4. Optionally customize entity information (game name, type) in the `DeviceAuthManager`

### Integration Example

```csharp
// Example: Programmatically trigger authentication
DeviceAuthManager authManager = FindObjectOfType<DeviceAuthManager>();
authManager.StartDeviceAuth(); // Opens browser for user authentication

// Check authentication status
if (authManager.IsAuthenticated())
{
    Debug.Log($"Signed in as: {authManager.UserDisplayName}");
}
```

The plugin includes a complete example scene (`DeviceAuthExample.cs`) demonstrating the integration between device authentication and bug reporting.

## Support

Join our [Discord server](https://discord.gg/g2wpRtG) for support, feedback, and feature requests.

## Architecture: Reporter and UI

The `BugReportWidget` prefab carries these components on one GameObject:

- `BetaHubReporter`: headless core. Configuration (Project ID, Auth Token, release, upload mode), attachments, custom fields, recording policy and submission. Your code talks to this one.
- `BugReportWidget` + `UIDocument`: the shipped form, built with UI Toolkit (`Runtime/UI/BugReportWidget.uxml` and `.uss`). A full-screen view over the reporter: what happened on the left, what gets sent on the right, actions in a prompt bar. Sized for a 1080p TV (30 px field text, 64 px tall controls, a safe area), scaled to the screen height.
- `GameRecorder`, `CustomFieldValidator`: recording and custom field checks.

The older uGUI form (`BugReportingFormCanvas` prefab with `BugReportUI`) still works and still talks to the same reporter, but new features land in the UI Toolkit widget.

### Reskinning the widget

A theme is one small stylesheet of `--bh-*` variables; the layout stylesheet only reads them. Six built-ins ship in `Runtime/UI/Themes` (Dark, Light, Neutral, Ocean, Forest, Violet), each derived from one accent color with contrast checked. Assign one to the widget's **Theme** field, or call `SetTheme()` at runtime. For your own look, write a stylesheet with the same variables, or generate one from an accent with `ThemeUssWriter.Write(new ThemeSpec(...))`. Strings are editable on the component. Select the prefab and use the inspector's Preview section to see any screen, attachment count and theme in the Game view without entering Play mode.

Configuration mistakes are reported once in the Unity console at startup, prefixed with `BetaHub:`, together with the fix.

## Attachments

When the form opens, the reporter builds the list of what can travel with the report: the screenshot, the last minute of play, the game log, network and location data when those providers are on, files added in code, and rows from your providers. Rows start included unless a provider sets `IncludedByDefault = false`; the player can leave any of them out, and only included rows are sent.

```csharp
BetaHubReporter reporter = FindObjectOfType<BetaHubReporter>();

// Files for the next report, one row each.
reporter.AddScreenshot("/path/to/screenshot.png", removeAfterUpload: true);
reporter.AddLogFile("/path/to/custom.log", removeAfterUpload: true);
reporter.AddVideo("/path/to/video.mp4", removeAfterUpload: true);
reporter.AddFile("/path/to/slot2.sav", "Save file");   // any file, up to 50 MB

// Rows computed each time the form opens.
class HardwareRows : IAttachmentProvider
{
    public IEnumerable<AttachmentDescriptor> GetAttachments()
    {
        yield return new AttachmentDescriptor
        {
            Id = "hardware", Name = "Hardware", Meta = "GPU, RAM", Kind = AttachmentKind.Data,
            Fields = new[] { new KeyValuePair<string, string>("gpu", SystemInfo.graphicsDeviceName) },
        };
    }
}
reporter.RegisterAttachmentProvider(new HardwareRows());
```

- `removeAfterUpload`: delete the file from disk once its upload was attempted. Use it for temporary files only.
- File rows need `FilePath`; Data rows need `Fields` and are sent as a text file. Invalid rows, repeated ids and built-in ids (`BuiltInAttachments`) are skipped with a console warning.
- A left-out Location row sends no geolocation request.
- Added files and the screenshot are cleared once a report's draft is created on the server.
- The backend allows a maximum of 3 video clips per issue (including the automatically recorded video).

## Custom Fields

```csharp
reporter.SetCustomField("game_id", "my-game-123");
reporter.RegisterCustomFieldProvider("current_level", () => SceneManager.GetActiveScene().name);
```

Providers are evaluated at submit time and override a static field of the same name. Fields must exist in your project and be marked as tester settable.

## Building Your Own UI

Submit directly and follow the handle. No coroutines needed on your side:

```csharp
BetaHubReporter reporter = FindObjectOfType<BetaHubReporter>();

// Capture before your form is visible, then tell the reporter it is open:
// recording pauses and latency sampling starts.
yield return reporter.CaptureScreenshot();
reporter.NotifyReportWindowShown();

// One row per attachment; bind them to toggles and let the player leave any out.
AttachmentSet attachments = reporter.CollectAttachments();
attachments.SetIncluded(BuiltInAttachments.Location, false);

ReportSubmission submission = reporter.Submit(description, steps, attachments);
submission.AttachmentUploadChanged += (s, id) =>
{
    s.TryGetUploadState(id, out AttachmentUploadState state);
    Debug.Log($"{id}: {state.Status} {state.Progress:P0}, overall {s.UploadProgress:P0}");
};
submission.StateChanged += s =>
{
    switch (s.State)
    {
        case ReportSubmissionState.AwaitingPublish:
            // Draft exists. Optionally collect an email, then publish.
            s.SetReporterEmail("player@example.com");
            s.Publish(emailMyReport: true);
            break;
        case ReportSubmissionState.Published:
            Debug.Log("Reported: " + s.IssueUrl);
            break;
        case ReportSubmissionState.Failed:
            Debug.LogError(s.Error);
            break;
    }
};

reporter.NotifyReportWindowHidden();
```

Each included row reports `Queued`, `Uploading` (with progress), `Sent` or `Failed`; a failed row does not stop the report. The legacy `Submit(description, steps, BugReportOptions)` overload still works: it collects the list and applies the three toggles.

States: `Preparing`, `CreatingDraft`, `UploadingMedia` (WaitForUpload mode only), `AwaitingPublish`, `Publishing`, `Published`, `Failed`. Signed-in users (device authentication) skip `AwaitingPublish`; the report publishes automatically. A misconfigured reporter returns a handle already in `Failed` with the reason in `Error`.

## Audio Recording

Audio recording is enabled by default using Unity's AudioListener. The plugin supports two audio capture backends:

- **Unity AudioListener** (default): Captures all audio output from Unity's audio system. No additional setup required.
- **Wwise**: Captures audio from the Wwise sound engine. Requires the `BETAHUB_WWISE` scripting define symbol in Player Settings > Scripting Define Symbols.

You can change the audio capture mode in the **GameRecorder** component's inspector under "Audio Recording". Set it to `None` to disable audio recording entirely.

Audio recording automatically pauses when the bug report form is visible and resumes when it is dismissed, ensuring audio stays in sync with video.

## ⚠️ IL2CPP Support and FFmpeg Video Recording

**If you are building your Unity project with IL2CPP as the scripting backend, special handling is required for video recording to work:**

- The plugin uses a native process wrapper library to launch and communicate with FFmpeg when running under IL2CPP. This is necessary because the standard .NET `Process` class is not available in IL2CPP builds.
- **You must define the scripting symbol `ENABLE_BETAHUB_FFMPEG` in your project settings** to enable native FFmpeg support for IL2CPP builds.
- The native library (`libbetahub_process_wrapper`) must be present in your build's Plugins directory for your platform (e.g., `Plugins/macOS/`, `Plugins/Linux/`).
- If you do not define `ENABLE_BETAHUB_FFMPEG`, video recording will be disabled in IL2CPP builds and a warning will be logged at runtime.
- If you are using Mono or .NET scripting backend, the plugin will use the standard .NET `Process` class and does not require the native library or the scripting symbol.

**Summary:**
- For IL2CPP: define `ENABLE_BETAHUB_FFMPEG` and ensure the native library is present.
- For Mono/.NET: no special action is needed.

## ⚠️ Performance: Disabling Logger

**If you need to disable BetaHub's logging system for performance reasons or production builds:**

- Define the scripting symbol `DISABLE_BETAHUB_LOGGER` in Player Settings > Scripting Define Symbols
- This prevents the logger from capturing Unity logs, which can cause:
  - File I/O operations every time Unity logs a message
  - CPU overhead from continuous disk writes
  - File access violations when multiple Unity instances run simultaneously
- Bug reports will still work, but won't include captured Unity logs
- A warning will be displayed when BetaHubReporter starts with logging disabled

**When to use:**
- Production builds where logging overhead impacts performance
- Multi-instance Unity scenarios (automated testing, CI/CD)
- Projects with high-frequency logging that causes file access issues

