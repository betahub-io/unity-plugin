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

## Development Notes

- Do not generate meta files, let Unity do this
- Use `ruby validate_prefab.rb <prefab_path>` to validate Unity prefabs for duplicate IDs and broken references