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
- **Video recording**: Record a video of the bug happening in-game. The video is automatically recorded and attached to the bug report.
- **Log collection**: Collect logs from the game and attach them to the bug report. By default, Unity logs are collected, but you can also add custom logs.
- **Screenshot of the game**: A screenshot of the game is automatically attached to the bug report when the user submits a bug.
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

## Support

Join our [Discord server](https://discord.gg/g2wpRtG) for support, feedback, and feature requests.

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

