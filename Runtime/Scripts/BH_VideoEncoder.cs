using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;

public class BH_VideoEncoder
{
    private Process ffmpegProcess;
    private string outputDir;
    private string outputPathPattern;
    private int width;
    private int height;
    private int frameRate;
    private int segmentLength = 10; // Segment length in seconds
    private int maxSegments = 6; // Number of segments to keep (covering 60 seconds)
    private CircularBuffer<string> errorBuffer = new CircularBuffer<string>(256); // Circular buffer for stderr

    private byte[] lastFrame;
    private float frameInterval;

    private bool debugMode;

    public bool Disposed { get { return disposed; } }
    private volatile bool disposed;

    // if true, the inner thread should stop
    private volatile bool _stopRequest = false;
    private volatile bool _stopRequestHandled = false;

    public BH_VideoEncoder(int width, int height, int frameRate, int recordingDurationSeconds, string outputDir = "Recording")
    {
        this.width = width;
        this.height = height;
        this.frameRate = frameRate;
        this.outputDir = outputDir;
        this.outputPathPattern = Path.Combine(outputDir, "segment_%03d.mp4");

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
        
        if (ffmpegProcess != null && !ffmpegProcess.HasExited)
        {
            SendStopRequestAndWait();
        }

        RemoveAllSegments();
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

        ffmpegProcess = new Process();
        ffmpegProcess.StartInfo.FileName = ffmpegPath;
        ffmpegProcess.StartInfo.Arguments = $"-y -f rawvideo -pix_fmt rgb24 -s {width}x{height} -r {frameRate} -i - " +
                                            $"-vf vflip -c:v {encoder} -pix_fmt yuv420p {presetString} -f segment -segment_time {segmentLength} " +
                                            $"-reset_timestamps 1 \"{outputPathPattern}\"";
        ffmpegProcess.StartInfo.UseShellExecute = false;
        ffmpegProcess.StartInfo.RedirectStandardInput = true;
        ffmpegProcess.StartInfo.RedirectStandardError = true;
        ffmpegProcess.ErrorDataReceived += (sender, e) => { if (e.Data != null) errorBuffer.Add(e.Data); };
        ffmpegProcess.Start();

        ffmpegProcess.BeginErrorReadLine(); // Start reading the standard error asynchronously

        // Start a background task to feed frames at a constant rate
        Task.Run(() => FeedFrames());
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

            while (ffmpegProcess != null && !ffmpegProcess.HasExited && !_stopRequest)
            {
                float elapsedSeconds = (float)stopwatch.Elapsed.TotalSeconds;

                if (elapsedSeconds >= nextFrameTime)
                {
                    nextFrameTime += frameInterval;

                    try
                    {
                        if (lastFrame != null)
                        {
                            // TODO: This sometimes can cause NullReferenceException when the process is closed, fix it
                            // synchronization may be needed
                            ffmpegProcess.StandardInput.BaseStream.Write(lastFrame, 0, lastFrame.Length);
                        }
                    }
                    catch (IOException e)
                    {
                        UnityEngine.Debug.LogError($"Error adding frame data: {e.Message}");
                        UnityEngine.Debug.LogError(string.Join("\n", errorBuffer.ToArray())); // Log collected stderr messages
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

            ffmpegProcess.StandardInput.Close();
            ffmpegProcess.WaitForExit();

            _stopRequestHandled = true; // let the main thread know that the stop request has been handled

            // If ffmpeg process has exited with exit code other than 0, log the stderr messages
            if (ffmpegProcess.ExitCode != 0 && !disposed)
            {
                UnityEngine.Debug.LogError("Error during encoding.");
                UnityEngine.Debug.LogError(string.Join("\n", errorBuffer.ToArray())); // Log collected stderr messages
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

    private string SendStopRequestAndWait()
    {
        if (ffmpegProcess != null && !ffmpegProcess.HasExited)
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

        return MergeSegments();
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

        // string mergedFilePath = Path.Combine(Application.persistentDataPath, $"Gameplay_{System.DateTime.Now:yyyyMMdd_HHmmss}.mp4");
        string mergedFilePath = Path.Join(outputDir, $"Gameplay_{System.DateTime.Now:yyyyMMdd_HHmmss}.mp4");

        // Use FFmpeg to concatenate the segments into one file
        string concatFilePath = Path.Combine(outputDir, "concat.txt");
        File.WriteAllLines(concatFilePath, files.Select(f => $"file '{f.FullName.Replace("'", @"'\''")}'")); // Properly escape single quotes

        var mergeProcess = new Process();
        mergeProcess.StartInfo.FileName = ffmpegPath;
        mergeProcess.StartInfo.Arguments = $"-f concat -safe 0 -i \"{concatFilePath}\" -c copy \"{mergedFilePath}\"";
        mergeProcess.StartInfo.UseShellExecute = false;
        mergeProcess.StartInfo.RedirectStandardError = true;
        mergeProcess.ErrorDataReceived += (sender, e) => { if (e.Data != null) errorBuffer.Add(e.Data); };
        mergeProcess.Start();
        mergeProcess.BeginErrorReadLine();
        mergeProcess.WaitForExit();

        if (mergeProcess.ExitCode != 0)
        {
            UnityEngine.Debug.LogError("Error during merging segments.");
            UnityEngine.Debug.LogError(string.Join("\n", errorBuffer.ToArray())); // Log collected stderr messages
        }

        File.Delete(concatFilePath); // Clean up the concat file

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
        var directoryInfo = new DirectoryInfo(outputDir);
    
        var filesToDelete = directoryInfo.GetFiles("segment_*.mp4")
                                         .OrderBy(f => f.Name)
                                         .Take(directoryInfo.GetFiles("segment_*.mp4").Length - maxSegments)
                                         .ToArray();
        
        foreach (var file in filesToDelete)
        {
            file.Delete();
        }
    }

    private static string FindPreferredEncoder()
    {
        // Run ffmpeg to get the list of available encoders
        var process = new Process();

        string ffmpegPath = GetFfmpegPath();
        if (ffmpegPath == null)
        {
            return "libx264";
        }

        process.StartInfo.FileName = ffmpegPath;
        process.StartInfo.Arguments = "-encoders";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.Start();

        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        // Check for specific hardware encoders in the output
        if (output.Contains("h264_nvenc"))
        {
            return "h264_nvenc";
        }
        else if (output.Contains("h264_videotoolbox"))
        {
            return "h264_videotoolbox";
        }
        else if (output.Contains("h264_vaapi"))
        {
            return "h264_vaapi";
        }
        else
        {
            // Default to software encoding if no hardware encoder is found
            return "libx264";
        }
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

    private static string GetFfmpegPath()
    {
        // ffmpeg should be installed in streaming assets directory of the current platform, e.g:
        // - Windows: <ProjectRoot>/Assets/StreamingAssets/BetaHub/ffmpeg.exe
        // - macOS: <ProjectRoot>/Assets/StreamingAssets/BetaHub/ffmpeg
        // - Linux: <ProjectRoot>/Assets/StreamingAssets/BetaHub/ffmpeg

        string path = null;
    
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        path = Path.Combine(Application.streamingAssetsPath, "BetaHub", "ffmpeg.exe");
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        path = Path.Combine(Application.streamingAssetsPath, "BetaHub", "ffmpeg");
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
        path = Path.Combine(Application.streamingAssetsPath, "BetaHub", "ffmpeg");
#endif

        if (!File.Exists(path))
        {
            UnityEngine.Debug.LogError("FFmpeg binary not found, BetaHub video recording will not work. " +
                "You can download it by clicking on the Windows/BetaHub/Download FFmpeg menu item. " +
                "If that doesn't work, you can download it manually from https://ffmpeg.org/download.html " +
                "and place the executable directly in the StreamingAssets/BetaHub directory.");

            return null;
        }

        return path;
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