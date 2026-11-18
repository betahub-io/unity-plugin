using UnityEditor;
using UnityEngine;

namespace BetaHub
{
    /// <summary>
    /// Offers FFmpeg once the package is in a project that lacks it: without it reports carry no video,
    /// and the only other sign is a warning in Play mode. Never in batch mode, so builds and CI don't wait.
    /// </summary>
    [InitializeOnLoad]
    internal static class FfmpegFirstRunPrompt
    {
        // per project: the editor's preferences are shared by every project
        private static readonly string DeclinedKey = "BetaHub.FfmpegPrompt.Declined." + Application.dataPath;
        // until the editor restarts: Not now asks again next time
        private const string AskedKey = "BetaHub.FfmpegPrompt.Asked";

        static FfmpegFirstRunPrompt()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += AskIfMissing;
        }

        private static void AskIfMissing()
        {
            if (SessionState.GetBool(AskedKey, false) || EditorPrefs.GetBool(DeclinedKey, false)) return;
            if (FfmpegDownloader.IsInstalledForThisEditor()) return;
            SessionState.SetBool(AskedKey, true);

            int choice = EditorUtility.DisplayDialogComplex("BetaHub: download FFmpeg?",
                "Bug reports carry the last 60 seconds of play as a video. The plugin records it with FFmpeg, which this project doesn't have yet.\n\n" +
                "Download it now into Assets/StreamingAssets/BetaHub, for Windows, macOS and Linux? You can also do it later from " +
                "Window > BetaHub > Download FFmpeg.",
                "Download", "Not now", "Don't ask again");
            if (choice == 0) FfmpegDownloader.DownloadForAllPlatforms();
            else if (choice == 2) EditorPrefs.SetBool(DeclinedKey, true);
        }
    }
}
