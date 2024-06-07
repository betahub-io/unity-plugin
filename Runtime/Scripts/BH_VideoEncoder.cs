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

    private volatile bool disposed;

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
            ffmpegProcess.Kill();
        }

        RemoveAllSegments();
    }

    public void StartEncoding()
    {
        RemoveAllSegments();
        
        string encoder = GetHardwareEncoder();
        UnityEngine.Debug.Log($"Using encoder: {encoder}");

        ffmpegProcess = new Process();
        ffmpegProcess.StartInfo.FileName = "/opt/homebrew/bin/ffmpeg";
        ffmpegProcess.StartInfo.Arguments = $"-y -f rawvideo -pix_fmt rgb24 -s {width}x{height} -r {frameRate} -i - " +
                                            $"-vf vflip -c:v {encoder} -pix_fmt yuv420p -preset ultrafast -f segment -segment_time {segmentLength} " +
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

            while (ffmpegProcess != null && !ffmpegProcess.HasExited)
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
        if (ffmpegProcess != null && !ffmpegProcess.HasExited)
        {
            ffmpegProcess.StandardInput.Close();
            ffmpegProcess.WaitForExit();
        }

        string mergedFilePath = MergeSegments();

        // Clean up old segments
        CleanupSegments();

        return mergedFilePath;
    }

    private string MergeSegments()
    {
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
        mergeProcess.StartInfo.FileName = "/opt/homebrew/bin/ffmpeg";
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

    private string GetHardwareEncoder()
    {
        // Run ffmpeg to get the list of available encoders
        var process = new Process();
        process.StartInfo.FileName = "/opt/homebrew/bin/ffmpeg";
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

    // private FindFfmpegPath()
    // {
    //     string[] paths = new string[] {
    //         "/usr/local/bin/ffmpeg",
    //         "/usr/bin/ffmpeg",
    //         "/opt/homebrew/bin/ffmpeg"
    //     };

    //     foreach (var path in paths)
    //     {
    //         if (File.Exists(path))
    //         {
    //             return path;
    //         }
    //     }

    //     return null;
    // }
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