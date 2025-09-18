using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;
using System; // Added for Guid

namespace BetaHub
{
    /// <summary>
    /// VideoEncoder handles video recording and encoding using FFmpeg.
    /// 
    /// Bug Fix (File Access Conflicts):
    /// - Uses unique instance directories to prevent conflicts between multiple VideoEncoder instances
    /// - Implements retry logic with exponential backoff for file deletion operations
    /// - Gracefully handles IOException when files are still in use by FFmpeg processes
    /// - Properly cleans up instance directories when empty
    /// 
    /// This fixes the issue where scene reloads would cause IOException spam due to
    /// multiple VideoEncoder instances trying to access the same segment files.
    /// </summary>
    public class VideoEncoder
    {
        private IProcessWrapper ffmpegProcess;
        private string outputDir;
        private string outputPathPattern;
        private int width;
        private int height;
        private int frameRate;
        private int segmentLength = 10; // Segment length in seconds
        private int maxSegments = 6; // Number of segments to keep (covering 60 seconds)
        private CircularBuffer<string> errorBuffer = new CircularBuffer<string>(256); // Circular buffer for stderr

        private string _functionalEncoder = null;

        private byte[] lastFrame;
        private float frameInterval;

        private bool debugMode;
        
        // Unique instance identifier to prevent conflicts between multiple instances
        private readonly string instanceId;
        
        // Control vertical mirroring of the video
        private readonly bool mirrorVertically;

        // if set to true, the encoding thread will pause adding new frames
        public bool IsPaused { get; set; }

        public bool Disposed { get { return disposed; } }
        private volatile bool disposed;

        // if true, the inner thread should stop
        private volatile bool _stopRequest = false;
        private volatile bool _stopRequestHandled = false;

        public VideoEncoder(int width, int height, int frameRate, int recordingDurationSeconds, string baseOutputDir = "Recording", bool mirrorVertically = false)
        {
            this.width = width;
            this.height = height;
            this.frameRate = frameRate;
            this.mirrorVertically = mirrorVertically;
            
            // Create unique instance identifier and output directory
            this.instanceId = Guid.NewGuid().ToString("N").Substring(0, 8); // Use first 8 characters of GUID
            this.outputDir = Path.Combine(baseOutputDir, instanceId);
            this.outputPathPattern = Path.Combine(outputDir, "segment_%03d.mp4");

            #if BETAHUB_DEBUG
            UnityEngine.Debug.Log($"Video output directory: {outputDir} (instance: {instanceId})");
            #endif

            Directory.CreateDirectory(outputDir);

            maxSegments = recordingDurationSeconds / segmentLength;
            if (recordingDurationSeconds % segmentLength != 0)
            {
                maxSegments++;
            }

            frameInterval = 1.0f / frameRate;
        }

        public void Dispose()
        {
            disposed = true;
            
            if (ffmpegProcess != null && ffmpegProcess.IsRunning())
            {
                SendStopRequestAndWait();
                
                // Give the process a moment to fully release file handles
                System.Threading.Thread.Sleep(100);
            }

            // Clean up segments with retry logic for better file access handling
            RemoveAllSegmentsWithRetry();
            
            // Clean up the unique instance directory if it's empty
            CleanupInstanceDirectory();
        }

        public void StartEncoding()
        {
            string ffmpegPath = GetFfmpegPath();
            if (ffmpegPath == null)
            {
                return;
            }
            
            RemoveAllSegments();
            
            string encoder = FindPreferredEncoder();
            string presetName = GetPresetName(encoder);
            string presetString = "";

            if (!string.IsNullOrEmpty(presetName))
            {
                presetString = $"-preset {presetName}";
            }

            UnityEngine.Debug.Log($"Using encoder: {encoder}");

            List<string> arguments = new List<string>
            {
                "-y",
                "-f", "rawvideo",
                "-pix_fmt", "rgb24",
                "-s", $"{width}x{height}",
                "-r", frameRate.ToString(),
                "-i", "-",
                "-c:v", encoder,
                "-pix_fmt", "yuv420p"
            };

            if (!string.IsNullOrEmpty(presetString))
            {
                arguments.Add("-preset");
                arguments.Add(presetName);
            }
            
            // Add vertical mirroring filter if enabled
            if (mirrorVertically)
            {
                arguments.Add("-vf");
                arguments.Add("vflip");
            }

            arguments.AddRange(new[]
            {
                "-f", "segment",
                "-segment_time", segmentLength.ToString(),
                "-reset_timestamps", "1",
                outputPathPattern
            });

            #if ENABLE_IL2CPP && ENABLE_BETAHUB_FFMPEG
            ffmpegProcess = new NativeProcessWrapper();
            #if BETAHUB_DEBUG
            UnityEngine.Debug.Log("Using native process wrapper");
            #endif

            #elif !ENABLE_IL2CPP
            ffmpegProcess = new DotNetProcessWrapper();
            #if BETAHUB_DEBUG
            UnityEngine.Debug.Log("Using dotnet process wrapper");
            #endif
            #endif

            #if !ENABLE_IL2CPP || ENABLE_BETAHUB_FFMPEG
            if (ffmpegProcess != null && ffmpegProcess.Start(ffmpegPath, arguments.ToArray()))
            {
                // Start a background task to feed frames at a constant rate
                Task.Run(() => FeedFrames());
            }
            else
            {
                UnityEngine.Debug.LogError("Failed to start FFmpeg process");
            }
            #endif
        }

        public void AddFrame(byte[] frameData)
        {
            lastFrame = frameData;
        }

        private void FeedFrames()
        {
            try
            {
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();
                float nextFrameTime = 0f;
                float nextCleanupTime = segmentLength; // Schedule cleanup after the first segment duration

                while (ffmpegProcess != null && ffmpegProcess.IsRunning() && !_stopRequest)
                {
                    float elapsedSeconds = (float)stopwatch.Elapsed.TotalSeconds;

                    if (elapsedSeconds >= nextFrameTime)
                    {
                        nextFrameTime += frameInterval;

                        try
                        {
                            if (lastFrame != null && !IsPaused)
                            {
                                int result = ffmpegProcess.WriteStdin(lastFrame);
                                if (result < 0)
                                {
                                    UnityEngine.Debug.LogError("Error writing frame data to FFmpeg");
                                }
                            }
                        }
                        catch (System.Exception e)
                        {
                            UnityEngine.Debug.LogError($"Error adding frame data: {e.Message}");
                            UnityEngine.Debug.LogError(string.Join("\n", ffmpegProcess.ReadStderr())); // Log collected stderr messages
                        }
                    }

                    // Check if it's time to clean up old segments
                    if (elapsedSeconds >= nextCleanupTime)
                    {
                        CleanupSegments();
                        nextCleanupTime += segmentLength; // Schedule the next cleanup
                    }

                    // Sleep for a short time to avoid busy-waiting
                    System.Threading.Thread.Sleep(1);
                }

                _stopRequest = false; // reset the flag

                if (ffmpegProcess != null)
                {
                    ffmpegProcess.Close();
                    _stopRequestHandled = true; // let the main thread know that the stop request has been handled

                    // If ffmpeg process has exited with exit code other than 0, log the stderr messages
                    if (ffmpegProcess.ExitCode != 0 && !disposed)
                    {
                        UnityEngine.Debug.LogError("Error during encoding.");
                        UnityEngine.Debug.LogError(string.Join("\n", ffmpegProcess.ReadStderr())); // Log collected stderr messages
                    }
                }
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError("Error during encoding.");
                UnityEngine.Debug.LogException(e);
            }
        }

        public string StopEncoding()
        {
            SendStopRequestAndWait();

            string mergedFilePath = MergeSegments();

            // Clean up old segments
            CleanupSegments();

            return mergedFilePath;
        }

        private void SendStopRequestAndWait()
        {
            if (ffmpegProcess != null && ffmpegProcess.IsRunning())
            {
                _stopRequestHandled = false;
                _stopRequest = true; // this will ask the thread to close the standard input and exit

                // wait until stop request
                int timeout = 2000; // 2 seconds

                while (!_stopRequestHandled && timeout > 0)
                {
                    System.Threading.Thread.Sleep(10);
                    timeout -= 10;
                }

                if (timeout <= 0)
                {
                    UnityEngine.Debug.LogError("Timeout while waiting for the encoding thread to stop.");
                }
            }
        }

        private string MergeSegments()
        {
            string ffmpegPath = GetFfmpegPath();
            if (ffmpegPath == null)
            {
                return null;
            }
            
            var directoryInfo = new DirectoryInfo(outputDir);
            var files = directoryInfo.GetFiles("segment_*.mp4")
                                     .OrderBy(f => f.Name)
                                     .TakeLast(maxSegments)
                                     .ToArray();

            string mergedFilePath = Path.Join(outputDir, $"Gameplay_{System.DateTime.Now:yyyyMMdd_HHmmss}.mp4");

            // Use FFmpeg to concatenate the segments into one file
            string concatFilePath = Path.Combine(outputDir, "concat.txt");
            File.WriteAllLines(concatFilePath, files.Select(f => $"file '{f.FullName.Replace("'", @"'\''")}'")); // Properly escape single quotes

            #if ENABLE_IL2CPP && ENABLE_BETAHUB_FFMPEG
            var mergeProcess = new NativeProcessWrapper();
            #elif !ENABLE_IL2CPP
            var mergeProcess = new DotNetProcessWrapper();
            #else
            return null;
            #endif

            #if !ENABLE_IL2CPP || ENABLE_BETAHUB_FFMPEG
            string[] mergeArguments = new string[]
            {
                "-f", "concat",
                "-safe", "0",
                "-i", concatFilePath,
                "-c", "copy",
                mergedFilePath
            };
            
            mergeProcess.Start(ffmpegPath, mergeArguments);
            mergeProcess.WaitForExit();

            if (mergeProcess.ExitCode != 0)
            {
                UnityEngine.Debug.LogError("Error during merging segments.");
                UnityEngine.Debug.LogError(string.Join("\n", mergeProcess.ReadStderr())); // Log collected stderr messages
            }

            File.Delete(concatFilePath); // Clean up the concat file
            #endif

            return mergedFilePath;
        }

        private void RemoveAllSegments()
        {
            var directoryInfo = new DirectoryInfo(outputDir);
            foreach (var file in directoryInfo.GetFiles("segment_*.mp4"))
            {
                file.Delete();
            }
        }

        // cleanups only the old segments, keeping the ones with the latest segment numbers
        private void CleanupSegments()
        {
            if (!Directory.Exists(outputDir))
                return;
                
            var directoryInfo = new DirectoryInfo(outputDir);
        
            var filesToDelete = directoryInfo.GetFiles("segment_*.mp4")
                                             .OrderBy(f => f.Name)
                                             .Take(directoryInfo.GetFiles("segment_*.mp4").Length - maxSegments)
                                             .ToArray();
            
            foreach (var file in filesToDelete)
            {
                // Retry logic for file deletion to handle file access conflicts
                int retryCount = 0;
                const int maxRetries = 3;
                const int retryDelayMs = 25;
                
                while (retryCount < maxRetries)
                {
                    try
                    {
                        file.Delete();
                        break; // Success, exit retry loop
                    }
                    catch (IOException ex) when (ex.Message.Contains("being used by another process"))
                    {
                        retryCount++;
                        if (retryCount >= maxRetries)
                        {
                            #if BETAHUB_DEBUG
                            UnityEngine.Debug.LogWarning($"Could not delete old segment file {file.Name} after {maxRetries} attempts. File may still be in use.");
                            #endif
                        }
                        else
                        {
                            System.Threading.Thread.Sleep(retryDelayMs * retryCount);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        UnityEngine.Debug.LogError($"Unexpected error deleting old segment file {file.Name}: {ex.Message}");
                        break; // Don't retry for unexpected errors
                    }
                }
            }
        }

        private string FindPreferredEncoder()
        {
            string[] encoders = FindAvailableEncoders();
            if (!string.IsNullOrEmpty(_functionalEncoder) && encoders.Contains(_functionalEncoder))
            {
                return _functionalEncoder;
            }

            // execute ffmpeg -f lavfi -i nullsrc=d=1 -c:v h264_nvenc -t 1 -f null - for each encoder on the list,
            // exit code 0 menas the encoder is available

            string ffmpegPath = GetFfmpegPath();
            if (ffmpegPath == null)
            {
                return encoders[encoders.Length - 1];
            }

            foreach (var encoder in encoders)
            {
                UnityEngine.Debug.Log($"Checking encoder: {encoder}");
                
                #if ENABLE_IL2CPP && ENABLE_BETAHUB_FFMPEG
                var process = new NativeProcessWrapper();
                #elif !ENABLE_IL2CPP
                var process = new DotNetProcessWrapper();
                #else
                return encoders[encoders.Length - 1];
                #endif

                #if !ENABLE_IL2CPP || ENABLE_BETAHUB_FFMPEG
                string[] encoderTestArgs = new string[]
                {
                    "-f", "lavfi",
                    "-i", "nullsrc=d=1",
                    "-c:v", encoder,
                    "-t", "1",
                    "-f", "null",
                    "-"
                };
                
                process.Start(ffmpegPath, encoderTestArgs);
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    UnityEngine.Debug.Log($"Encoder {encoder} is available.");
                    _functionalEncoder = encoder;

                    return encoder;
                }
                else
                {
                    UnityEngine.Debug.Log($"Encoder {encoder} is not available.");
                }
                #endif
            }

            return encoders[encoders.Length - 1];
        }

        // It returns the preferred encoder based on the available encoders in order of preference
        private static string[] FindAvailableEncoders()
        {
            List<string> encoders = new List<string>();
            
            // Run ffmpeg to get the list of available encoders
            #if ENABLE_IL2CPP && ENABLE_BETAHUB_FFMPEG
            var process = new NativeProcessWrapper();
            #elif !ENABLE_IL2CPP
            var process = new DotNetProcessWrapper();
            #else
            // If IL2CPP is enabled but BETAHUB_FFMPEG is not, return default encoders
            encoders.Add("libx264");
            return encoders.ToArray();
            #endif

            string ffmpegPath = GetFfmpegPath();
            if (ffmpegPath == null)
            {
                encoders.Add("libx264");
                return encoders.ToArray();
            }

            #if !ENABLE_IL2CPP || ENABLE_BETAHUB_FFMPEG
            string output = "";
            if (process.Start(ffmpegPath, new string[] { "-encoders" }))
            {
                process.WaitForExit();
                output = string.Join("\n", process.ReadStderr());
            }

            // Check for specific hardware encoders in the output
            if (output.Contains("h264_nvenc"))
            {
                encoders.Add("h264_nvenc");
            }

            if (output.Contains("h264_amf"))
            {
                encoders.Add("h264_amf");
            }

            if (output.Contains("h264_videotoolbox"))
            {
                encoders.Add("h264_videotoolbox");
            }
            
            if (output.Contains("h264_vaapi"))
            {
                encoders.Add("h264_vaapi");
            }
            #endif

            // Always add libx264 as a fallback
            encoders.Add("libx264");

            return encoders.ToArray();
        }

        // It returns the fastest preset for the given encoder
        private string GetPresetName(string encoder)
        {
            if (encoder == "h264_nvenc")
            {
                return "p1";
            }
            else if (encoder == "h264_videotoolbox")
            {
                return "ultrafast";
            }
            else if (encoder == "h264_vaapi")
            {
                return ""; // TODO: check what the fastest preset is
            }
            else if (encoder == "libx264")
            {
                return "ultrafast";
            }
            else
            {
                return "";
            }
        }

        public static bool IsFFmpegAvailable()
        {
            return GetFfmpegPath() != null;
        }

        private static string GetFfmpegPath()
        {
            string path = null;
            string legacyPath = null;
            
            // Define platform-specific paths
            string platformFolder = null;
            string executableName = null;

    #if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            platformFolder = "Windows";
            executableName = "ffmpeg.exe";
            legacyPath = Path.Combine(Application.streamingAssetsPath, "BetaHub", "ffmpeg.exe");
    #elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            platformFolder = "MacOS";
            executableName = "ffmpeg";
            legacyPath = Path.Combine(Application.streamingAssetsPath, "BetaHub", "ffmpeg");
    #elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
            platformFolder = "Linux";
            executableName = "ffmpeg";
            legacyPath = Path.Combine(Application.streamingAssetsPath, "BetaHub", "ffmpeg");
    #else
            UnityEngine.Debug.LogError("Unsupported platform for FFmpeg");
            return null;
    #endif

            // Try the new platform-specific path first
            path = Path.Combine(Application.streamingAssetsPath, "BetaHub", platformFolder, executableName);
            
            // If the new path doesn't exist, try the legacy path
            if (!File.Exists(path) && File.Exists(legacyPath))
            {
    #if BETAHUB_DEBUG
                UnityEngine.Debug.Log($"Using legacy FFmpeg path: {legacyPath}");
    #endif
                return legacyPath;
            }
            
            if (!File.Exists(path))
            {
                UnityEngine.Debug.LogWarning("FFmpeg binary not found, BetaHub video recording will not work. " +
                    "You can download it by clicking on the Windows/BetaHub/Download FFmpeg menu item. " +
                    "If that doesn't work, you can download it manually from https://ffmpeg.org/download.html " +
                    "and place the executable in the StreamingAssets/BetaHub/" + platformFolder + " directory. " +
                    "If you don't want to see this warning, set the Include Video property to false.");

                return null;
            }

            return path;
        }

        private void RemoveAllSegmentsWithRetry()
        {
            if (!Directory.Exists(outputDir))
                return;
                
            var directoryInfo = new DirectoryInfo(outputDir);
            var files = directoryInfo.GetFiles("segment_*.mp4");
            
            foreach (var file in files)
            {
                // Retry logic for file deletion to handle file access conflicts
                int retryCount = 0;
                const int maxRetries = 5;
                const int retryDelayMs = 50;
                
                while (retryCount < maxRetries)
                {
                    try
                    {
                        file.Delete();
                        break; // Success, exit retry loop
                    }
                    catch (IOException ex) when (ex.Message.Contains("being used by another process"))
                    {
                        retryCount++;
                        if (retryCount >= maxRetries)
                        {
                            UnityEngine.Debug.LogWarning($"Could not delete segment file {file.Name} after {maxRetries} attempts. File may still be in use. Error: {ex.Message}");
                        }
                        else
                        {
                            #if BETAHUB_DEBUG
                            UnityEngine.Debug.Log($"Retrying deletion of {file.Name} (attempt {retryCount}/{maxRetries})");
                            #endif
                            System.Threading.Thread.Sleep(retryDelayMs * retryCount); // Exponential backoff
                        }
                    }
                    catch (System.Exception ex)
                    {
                        UnityEngine.Debug.LogError($"Unexpected error deleting segment file {file.Name}: {ex.Message}");
                        break; // Don't retry for unexpected errors
                    }
                }
            }
        }

        private void CleanupInstanceDirectory()
        {
            try
            {
                if (Directory.Exists(outputDir))
                {
                    // Check if directory is empty (no files left)
                    var remainingFiles = Directory.GetFiles(outputDir);
                    var remainingDirs = Directory.GetDirectories(outputDir);
                    
                    if (remainingFiles.Length == 0 && remainingDirs.Length == 0)
                    {
                        Directory.Delete(outputDir);
                        #if BETAHUB_DEBUG
                        UnityEngine.Debug.Log($"Cleaned up empty instance directory: {outputDir}");
                        #endif
                    }
                    else
                    {
                        #if BETAHUB_DEBUG
                        UnityEngine.Debug.Log($"Instance directory not empty, keeping: {outputDir} (files: {remainingFiles.Length}, dirs: {remainingDirs.Length})");
                        #endif
                    }
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning($"Could not clean up instance directory {outputDir}: {ex.Message}");
            }
        }
    }

    public class CircularBuffer<T>
    {
        private readonly T[] buffer;
        private int nextIndex;

        public CircularBuffer(int capacity)
        {
            buffer = new T[capacity];
            nextIndex = 0;
        }

        public void Add(T item)
        {
            buffer[nextIndex] = item;
            nextIndex = (nextIndex + 1) % buffer.Length;
        }

        public T[] ToArray()
        {
            var result = new T[buffer.Length];
            int j = 0;
            for (int i = nextIndex; i < buffer.Length; i++)
            {
                result[j++] = buffer[i];
            }
            for (int i = 0; i < nextIndex; i++)
            {
                result[j++] = buffer[i];
            }
            return result;
        }
    }
}