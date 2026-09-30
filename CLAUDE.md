# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a Unity plugin for BetaHub bug reporting that enables in-game bug submissions with video recording, screenshot capture, and log collection. The plugin is distributed as a Unity Package Manager (UPM) package.

## Architecture

### Core Components

- **GameRecorder** (`Runtime/Scripts/GameRecorder.cs`): Handles video recording using FFmpeg with optimized GPU-based capture
- **BugReportUI** (`Runtime/Scripts/BugReportUI.cs`): Main UI component for bug report submission form
- **Issue** (`Runtime/Scripts/Issue.cs`): Data model representing bug reports and their upload state
- **VideoEncoder** (`Runtime/Scripts/VideoEncoder.cs`): FFmpeg wrapper for video encoding with segmented recording
- **Process Wrappers**: Platform-specific process handling for FFmpeg
  - **IProcessWrapper** (`Runtime/Scripts/IProcessWrapper.cs`): Interface for process abstraction
  - **DotNetProcessWrapper** (`Runtime/Scripts/DotNetProcessWrapper.cs`): .NET/Mono implementation
  - **NativeProcessWrapper** (`Runtime/Scripts/NativeProcessWrapper.cs`): IL2CPP implementation using native library

### Assembly Definitions

- **Runtime**: `io.betahub.bugreporter.asmdef` - Main plugin runtime assembly
- **Editor**: `io.betahub.bugreporter.editor.asmdef` - Editor-only features (FFmpeg downloader)

### Platform Support

The plugin supports Windows, macOS, and Linux with special handling for IL2CPP builds:
- **Mono/.NET**: Uses standard .NET Process class
- **IL2CPP**: Requires `ENABLE_BETAHUB_FFMPEG` scripting symbol and native process wrapper libraries in `Plugins/` directories

### Native Libraries

Platform-specific native libraries for IL2CPP FFmpeg support:
- Windows: `Plugins/x86_64/betahub_process_wrapper.dll`
- macOS: `Plugins/macOS/libbetahub_process_wrapper.dylib`
- Linux: `Plugins/Linux/x86_64/libbetahub_process_wrapper.so`

## Development

### Unity Package Structure

This project follows Unity Package Manager conventions:
- `package.json`: Package metadata and dependencies
- `Runtime/`: Runtime scripts and assets
- `Editor/`: Editor-only scripts
- `Samples~/`: Sample scenes and scripts
- Assembly definition files (`.asmdef`) organize code into separate assemblies

### Testing

The plugin includes a working demo scene in `Samples~/ExampleScene/` with:
- Sample scene setup
- Example integration with `RotateCube.cs` script
- Pre-configured demo project settings for immediate testing

### FFmpeg Integration

The plugin automatically downloads FFmpeg binaries through the editor script `FfmpegDownloader.cs`. Video recording is implemented with:
- Segmented recording (10-second segments, 60-second rolling window)
- GPU-optimized capture using RenderTextures
- Cross-platform process wrapper abstraction
- Error handling and retry logic for file operations

## Configuration

### Required Settings for IL2CPP

When building with IL2CPP scripting backend:
1. Define `ENABLE_BETAHUB_FFMPEG` in Player Settings > Scripting Define Symbols
2. Ensure native process wrapper libraries are included in build

### Demo Project

The plugin comes pre-configured with demo project credentials (`DEMO_PROJECT_ID = "pr-5287510306"`) for immediate testing. Reports submitted to the demo project are only visible to the submitter via email links.

## Fast Test Loop (seconds)

`Tools~/EditTests/` is a dotnet SDK project that compiles `Runtime/Scripts` + `Tests/Editor` against Unity's DLLs and runs the plain-C# NUnit tests without launching Unity. Use it as the inner red/green loop:

```bash
cd Tools~/EditTests && dotnet test --filter "FullyQualifiedName~BugReportSessionTests|FullyQualifiedName~ReportSubmissionTests|FullyQualifiedName~ReporterConfigurationTests|FullyQualifiedName~BugReportWidgetModelTests|FullyQualifiedName~AttachmentSetTests|FullyQualifiedName~ColorMathTests|FullyQualifiedName~ThemeRecipeTests"
```

Add new pure-C# suites to the filter. Tests that touch the Unity runtime (e.g. `VideoEncoderCleanupTests`) only run in Unity. The NUnit adapter in that project silently drops `[TestCase]` methods with enum arguments; use a table inside a plain `[Test]` instead. Before finishing, confirm with the real Unity EditMode run:

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics \
  -projectPath "/Users/piotrkorzuszek/Projects/betahub/betahub-unity-plugin-workspace-2022/BetaHub Unity Plugin" \
  -runTests -testPlatform EditMode -testResults /tmp/editmode_results.xml \
  -logFile /tmp/unity_tests.log; echo "EXIT CODE: $?"
```

If any Unity batchmode run exits 0 after only a few dozen log lines, check for `No valid Unity Editor license found`: the Hub entitlement expired and the user must sign in to Unity Hub.

## UI Toolkit Widget

The shipped UI is `Runtime/Prefabs/BugReportWidget.prefab`: `UIDocument` + `BugReportWidget` view over `BetaHubReporter`, markup in `Runtime/UI/BugReportWidget.uxml` and `.uss`. The prefab, `Runtime/UI/BugReportPanelSettings.asset` and `Runtime/UI/BetaHubRuntimeTheme.tss` are generated, never hand-edited: menu **BetaHub > Build Widget Assets** or

```bash
Unity -batchmode -nographics -quit -projectPath "<workspace>" -executeMethod BetaHub.Editor.BugReportWidgetAssetBuilder.Build -logFile /tmp/unity_build_assets.log
```

Design review without Unity: `ruby Tools~/UxmlPreview/uxml2html.rb Runtime/UI/BugReportWidget.uxml /tmp/preview.html` (see its README for the mapping and known deviations). Rendering and smoke tests live in `Tests/Preview/` (PlayMode; run headless with `-batchmode`, `-testPlatform PlayMode`, `-testFilter "BetaHub.Tests.WidgetPreviewCapture;BetaHub.Tests.WidgetPrefabSmokeTests"`; set `BH_PREVIEW_OUT` to choose the PNG path). Element names (`bh-*`) and class names are shared between the UXML, the USS and `BugReportWidget.cs`; keep them in sync.

## Build Verification

There is a test Unity project at `/Users/piotrkorzuszek/Projects/betahub/betahub-unity-plugin-workspace-2022/BetaHub Unity Plugin` that consumes this plugin. It has a `BuildScript.cs` in its `Assets/` folder (not in this repo — it lives in the workspace project) that can run WebGL builds from the command line. Use this as the primary feedback loop to verify code compiles:

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -quit \
  -projectPath "/Users/piotrkorzuszek/Projects/betahub/betahub-unity-plugin-workspace-2022/BetaHub Unity Plugin" \
  -buildTarget WebGL \
  -executeMethod BuildScript.BuildWebGL \
  -logFile /tmp/unity_webgl_build.log 2>&1; echo "EXIT CODE: $?"
```

To check for compile errors: `grep -i "error CS" /tmp/unity_webgl_build.log | sort -u`

For non-WebGL compilation check (no full build, just opens the project and compiles scripts):
```bash
/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -quit \
  -projectPath "/Users/piotrkorzuszek/Projects/betahub/betahub-unity-plugin-workspace-2022/BetaHub Unity Plugin" \
  -buildTarget Android \
  -logFile /tmp/unity_android_compile.log 2>&1; echo "EXIT CODE: $?"
```

## Development Notes

- Do not generate meta files, let Unity do this
- Use `ruby validate_prefab.rb <prefab_path>` to validate Unity prefabs for duplicate IDs and broken references