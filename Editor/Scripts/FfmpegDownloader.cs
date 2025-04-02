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
        static string GetFfmpegFilename()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                return "ffmpeg.exe";
            }
            else if (Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.LinuxEditor)
            {
                return "ffmpeg";
            }
            else
            {
                throw new System.PlatformNotSupportedException("Unsupported platform");
            }
        }

        static void DownloadAndExtractFfmpeg(string path)
        {
            bool withErrors = false;
            
            if (File.Exists(path))
            {
                // show error dialog
                EditorUtility.DisplayDialog("FFmpeg already exists", "FFmpeg is already installed at " + path, "OK");
                return;
            }
            
            string url = GetFfmpegDownloadUrl();
            string tempPath = Path.Combine(Path.GetTempPath(), Path.GetFileName(url));

            using (WebClient client = new WebClient())
            {
                client.DownloadFile(url, tempPath);
                Debug.Log("FFmpeg downloaded to " + tempPath);
            }

            string destinationDirectory = Path.Combine(Application.streamingAssetsPath, "BetaHub");

            ExtractZip(tempPath, destinationDirectory);
            File.Delete(tempPath);

            // Add execute permission to the ffmpeg binary
            if (Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.LinuxEditor)
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo("chmod", "+x \"" + path + "\"");
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                var process = System.Diagnostics.Process.Start(startInfo);
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    Debug.LogError("Failed to add execute permission to ffmpeg binary. Please add execute permission manually.");
                    withErrors = true;
                }
            }

            Debug.Log("FFmpeg installed at " + path);

            if (!withErrors) {
                UnityEditor.EditorUtility.DisplayDialog("FFmpeg Installed", "FFmpeg has been installed at " + path, "OK");
            } else {
                UnityEditor.EditorUtility.DisplayDialog("FFmpeg Installed with Errors", "FFmpeg has been installed at " + path + " but there were errors. Please check the console for more information.", "OK");
            }

            // refresh the assets
            AssetDatabase.Refresh();
        }

        static string GetFfmpegDownloadUrl()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                return "https://betahub.io/packages/ffmpeg/current/windows_x86_64/ffmpeg.zip";
            }
            else if (Application.platform == RuntimePlatform.OSXEditor)
            {
                return "https://betahub.io/packages/ffmpeg/current/macos_intel/ffmpeg.zip";

            }
            else if (Application.platform == RuntimePlatform.LinuxEditor)
            {
                return "https://betahub.io/packages/ffmpeg/current/linux_x86_64/ffmpeg.zip";
            }
            else
            {
                throw new System.PlatformNotSupportedException("Unsupported platform");
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
            string streamingAssetsPath = Application.streamingAssetsPath;
            string ffmpegPath = Path.Combine(streamingAssetsPath, "BetaHub", GetFfmpegFilename());
            DownloadAndExtractFfmpeg(ffmpegPath);
        }
    }
}