# BetaHub Bug Reporter for Unity

In-game bug reports and suggestions for [BetaHub](https://betahub.io). The player presses F12 (or a button you choose) and says what happened. The report reaches your BetaHub project with a screenshot, the last 60 seconds of play as video with sound, and the game log. The form works with keyboard, mouse and controllers.

## Try the demo

1. In Unity 2022.3 or later, open **Window > Package Manager**, click **+ > Add package from git URL** and enter `https://github.com/betahub-io/unity-plugin.git`.
2. When Unity offers to download FFmpeg, click **Download**. Video in reports needs it.
3. In the Package Manager, select **BetaHub Bug Reporter**, open **Samples** and import **Demo**. Open the `BugReportDemo` scene it adds and press Play.
4. Collect the coins. One of them is broken: press **F12** (or **Start** on a controller), describe the bug and send it. The last screen gives you a link to your report.

The demo reports go to BetaHub's public demo project. Each report there is visible only through its link, and the project has a shared quota, so test with your own project once you have one.

## Add it to your game

1. **GameObject > BetaHub > Bug Report Widget** adds the form to the open scene. It stays loaded across scenes, so add it to your first scene.
2. On its **BetaHub Reporter** component, replace the demo project's **Project ID** and **Auth Token** with your own. Both are in your BetaHub project under **Settings → Integrations → Auth Tokens**.
3. Press Play and F12.

Controller players have no F12: open the form from your pause menu with `bugReportWidget.Show()`.

For IL2CPP builds, add `ENABLE_BETAHUB_FFMPEG` to **Player Settings > Scripting Define Symbols**, or those builds record no video.

## Documentation

The [Unity guide in the BetaHub docs](https://betahub.io/docs/integrations/game-engines/#unity-plugin) covers configuration, controllers and Steam Deck, themes, attachments and custom fields, suggestions, player sign-in, building your own form, audio, IL2CPP, and upgrading from 1.x.

## Upgrading from 1.x

2.0 replaces the uGUI form (`BugReportingFormCanvas`) with the widget above.

1. Before you update, copy the Project ID and Auth Token from the `BugReportingFormCanvas` in your scenes.
2. After updating, delete the missing prefab, add the widget (**GameObject > BetaHub > Bug Report Widget**) and enter the two values on its BetaHub Reporter.
3. Code that used `BugReportUI` now talks to `BetaHubReporter` (settings, attachments, custom fields) and `BugReportWidget` (`Show()`, `Hide()` and the window events).

[CHANGELOG.md](CHANGELOG.md) lists every change.

## Requirements

- Unity 2022.3 or later.
- Video: Windows, macOS and Linux through FFmpeg, and WebGL through the browser.
- Controllers: the Input System package. Without it the form takes keyboard and mouse.

## Support

Ask on our [Discord server](https://discord.gg/g2wpRtG) for help, feedback and feature requests.
