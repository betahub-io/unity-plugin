using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Net;
using System.IO.Compression;

namespace BetaHub
{
    public class FfmpegDownloader
    {
        private static readonly Dictionary<string, string> PlatformInfo = new Dictionary<string, string>
        {
            { "Windows", "ffmpeg.exe" },
            { "MacOS", "ffmpeg" },
            { "Linux", "ffmpeg" }
        };

        private static readonly Dictionary<string, string> DownloadUrls = new Dictionary<string, string>
        {
            { "Windows", "https://betahub.io/packages/ffmpeg/current/windows_x86_64/ffmpeg.zip" },
            { "MacOS", "https://betahub.io/packages/ffmpeg/current/macos_intel/ffmpeg.zip" },
            { "Linux", "https://betahub.io/packages/ffmpeg/current/linux_x86_64/ffmpeg.zip" }
        };

        static string GetFfmpegFilename(string platform)
        {
            if (PlatformInfo.TryGetValue(platform, out string filename))
            {
                return filename;
            }

            throw new System.PlatformNotSupportedException($"Unsupported platform: {platform}");
        }

        static void DownloadAndExtractFfmpeg(string platform, out bool withErrors)
        {
            withErrors = false;

            string streamingAssetsPath = Application.streamingAssetsPath;
            string platformDir = Path.Combine(streamingAssetsPath, "BetaHub", platform);
            string ffmpegPath = Path.Combine(platformDir, GetFfmpegFilename(platform));
            
            if (File.Exists(ffmpegPath))
            {
                Debug.Log($"FFmpeg for {platform} already exists at {ffmpegPath}");
                return;
            }
            
            Directory.CreateDirectory(platformDir);
            
            if (!DownloadUrls.TryGetValue(platform, out string url))
            {
                Debug.LogError($"No download URL defined for platform: {platform}");
                return;
            }
            
            string tempPath = Path.Combine(Path.GetTempPath(), $"ffmpeg_{platform}.zip");

            try
            {
                using (WebClient client = new WebClient())
                {
                    client.DownloadFile(url, tempPath);
                    Debug.Log($"FFmpeg for {platform} downloaded to {tempPath}");
                }

                ExtractZip(tempPath, platformDir);

                // Add execute permission to the ffmpeg binary on Unix platforms
                if (platform == "MacOS" || platform == "Linux")
                {
                    if (Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.LinuxEditor)
                    {
                        var startInfo = new System.Diagnostics.ProcessStartInfo("chmod", $"+x \"{ffmpegPath}\"");
                        startInfo.UseShellExecute = false;
                        startInfo.CreateNoWindow = true;
                        var process = System.Diagnostics.Process.Start(startInfo);
                        process.WaitForExit();

                        if (process.ExitCode != 0)
                        {
                            Debug.LogError($"Failed to add execute permission to {platform} ffmpeg binary. Please add execute permission manually.");
                            withErrors = true;
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"Cannot set execute permissions for {platform} FFmpeg binary on current platform. If you plan to build for this platform, you may need to set permissions manually.");
                    }
                }

                Debug.Log($"FFmpeg for {platform} installed at {ffmpegPath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error downloading or extracting FFmpeg for {platform}: {e.Message}");
                withErrors = true;
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        static void ExtractZip(string sourceFile, string destinationDirectory)
        {
            ZipFile.ExtractToDirectory(sourceFile, destinationDirectory);
            // remove the __MACOSX directory if it exists
            string macosxDir = Path.Combine(destinationDirectory, "__MACOSX");
            if (Directory.Exists(macosxDir))
            {
                Directory.Delete(macosxDir, true);
            }
        }

        [MenuItem("Window/BetaHub/Download FFmpeg", false, 10000)]
        static void DownloadFfmpegMenu()
        {
            bool anyErrors = false;

            // Warn if legacy ffmpeg binary exists
            string legacyDir = Path.Combine(Application.streamingAssetsPath, "BetaHub");
            string legacyWin = Path.Combine(legacyDir, "ffmpeg.exe");
            string legacyUnix = Path.Combine(legacyDir, "ffmpeg");
            if (File.Exists(legacyWin) || File.Exists(legacyUnix))
            {
                Debug.LogWarning("[BetaHub] Legacy FFmpeg binary detected in StreamingAssets/BetaHub. Please remove 'ffmpeg.exe' or 'ffmpeg' from that directory to avoid confusion. Binaries should now be placed in platform-specific subfolders.");
            }

            // Download FFmpeg for all platforms
            foreach (string platform in PlatformInfo.Keys)
            {
                try
                {
                    bool withErrors = false;
                    DownloadAndExtractFfmpeg(platform, out withErrors);

                    anyErrors = anyErrors || withErrors;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Error downloading FFmpeg for {platform}: {e.Message}");
                    anyErrors = true;
                }
            }

            // Refresh the assets
            AssetDatabase.Refresh();

            // Show a message to the user
            if (!anyErrors)
            {
                EditorUtility.DisplayDialog("FFmpeg Installed", 
                    "FFmpeg has been installed for all supported platforms.", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("FFmpeg Installed with Errors", 
                    "FFmpeg has been installed but there were errors. Please check the console for more information.", "OK");
            }
        }
    }
}