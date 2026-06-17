#if BETAHUB_WWISE
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BetaHub
{
    public class WwiseAudioCapture : IAudioCaptureBackend
    {
        private string _outputDirectory;
        private string _captureFilePath;
        private string _captureRelativePath;
        private Type _akSoundEngineType;
        private MethodInfo _startCaptureMethod;
        private MethodInfo _stopCaptureMethod;
        private int _akSuccessValue = -1;
        private int _segmentIndex;
        private readonly List<string> _segmentPaths = new List<string>();
        private bool _wwiseIsRecording;
#if BETAHUB_DEBUG
        private System.Diagnostics.Stopwatch _captureStopwatch;
        private int _resumeCount;
#endif

        public bool IsCapturing { get; private set; }

        public bool Initialize()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                _akSoundEngineType = assembly.GetType("AkSoundEngine");
                if (_akSoundEngineType != null) break;
            }

            if (_akSoundEngineType == null)
            {
                Debug.LogError("BetaHub: AkSoundEngine type not found. Is Wwise installed in the project?");
                return false;
            }

            var flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            _startCaptureMethod = _akSoundEngineType.GetMethod("StartOutputCapture", flags, null, new[] { typeof(string) }, null);
            _stopCaptureMethod = _akSoundEngineType.GetMethod("StopOutputCapture", flags, null, Type.EmptyTypes, null);

            if (_startCaptureMethod == null || _stopCaptureMethod == null)
            {
                Debug.LogError("BetaHub: Wwise StartOutputCapture/StopOutputCapture methods not found.");
                return false;
            }

            var akResultType = _startCaptureMethod.ReturnType;
            var successField = akResultType.GetField("AK_Success");
            if (successField != null)
            {
                _akSuccessValue = (int)successField.GetValue(null);
            }
            else
            {
                _akSuccessValue = 1;
            }

            return true;
        }

        public void StartCapture(string outputDirectory)
        {
            if (_startCaptureMethod == null)
            {
                Debug.LogError("BetaHub: Wwise audio capture not initialized.");
                return;
            }

            _outputDirectory = outputDirectory;
            _segmentIndex = 0;
            _segmentPaths.Clear();

            if (StartWwiseCapture())
            {
                IsCapturing = true;
#if BETAHUB_DEBUG
                _resumeCount = 0;
                _captureStopwatch = System.Diagnostics.Stopwatch.StartNew();
                Debug.Log("BetaHub Wwise diag: StartCapture");
#endif
            }
        }

        public void PauseCapture()
        {
            if (!IsCapturing || !_wwiseIsRecording) return;

            StopWwiseCapture();
        }

        public void ResumeCapture()
        {
            if (!IsCapturing || _wwiseIsRecording) return;

#if BETAHUB_DEBUG
            _resumeCount++;
#endif
            StartWwiseCapture();
        }

        public string StopCapture()
        {
            if (!IsCapturing) return null;

            IsCapturing = false;

            if (_wwiseIsRecording)
            {
                StopWwiseCapture();
            }

            string result;
            if (_segmentPaths.Count == 0)
            {
                result = null;
            }
            else if (_segmentPaths.Count == 1)
            {
                result = _segmentPaths[0];
            }
            else
            {
                string mergedPath = Path.Combine(_outputDirectory, "wwise_capture_merged.wav");
                if (ConcatenateWavFiles(_segmentPaths, mergedPath))
                {
                    foreach (var seg in _segmentPaths)
                    {
                        try { File.Delete(seg); } catch (Exception) { }
                    }
                    result = mergedPath;
                }
                else
                {
                    result = _segmentPaths[_segmentPaths.Count - 1];
                }
            }

#if BETAHUB_DEBUG
            double wallClock = _captureStopwatch != null ? _captureStopwatch.Elapsed.TotalSeconds : -1;
            LogCaptureDiagnostics(result, wallClock, _segmentPaths.Count);
#endif
            return result;
        }

#if BETAHUB_DEBUG

        // Diagnostic: compares how long capture was ACTIVE (wall-clock) against how
        // much audio Wwise actually produced (WAV length). A ratio well below 1.0 means
        // the engine under-rendered while capturing (OutputCapture is RenderAudio-cadence
        // bound, not real-time). Helps pin down the build-only short-audio bug.
        private void LogCaptureDiagnostics(string path, double wallClockSeconds, int segmentCount)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    Debug.Log($"BetaHub Wwise diag: StopCapture - NO audio file. wall-clock={wallClockSeconds:F2}s, segments={segmentCount}, resumes={_resumeCount}");
                    return;
                }

                int sampleRate = 0;
                short channels = 0;
                short bits = 0;
                long dataBytes = 0;

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (var reader = new BinaryReader(stream))
                {
                    reader.ReadBytes(4);  // "RIFF"
                    reader.ReadInt32();   // file size
                    reader.ReadBytes(4);  // "WAVE"
                    while (stream.Position <= stream.Length - 8)
                    {
                        string chunkId = new string(reader.ReadChars(4));
                        int chunkSize = reader.ReadInt32();
                        if (chunkId == "fmt ")
                        {
                            reader.ReadInt16();            // audio format
                            channels = reader.ReadInt16();
                            sampleRate = reader.ReadInt32();
                            reader.ReadInt32();            // byte rate
                            reader.ReadInt16();            // block align
                            bits = reader.ReadInt16();
                            if (chunkSize > 16) reader.ReadBytes(chunkSize - 16);
                        }
                        else if (chunkId == "data")
                        {
                            dataBytes = chunkSize;
                            break;
                        }
                        else
                        {
                            stream.Position += chunkSize;
                        }
                    }
                }

                double byteRate = sampleRate * channels * (bits / 8.0);
                double wavSeconds = byteRate > 0 ? dataBytes / byteRate : 0;
                double ratio = wallClockSeconds > 0 ? wavSeconds / wallClockSeconds : 0;
                Debug.Log($"BetaHub Wwise diag: StopCapture - wall-clock={wallClockSeconds:F2}s, wav={wavSeconds:F2}s, ratio={ratio:F3}, sampleRate={sampleRate}, channels={channels}, bits={bits}, segments={segmentCount}, resumes={_resumeCount}, file={Path.GetFileName(path)}");
            }
            catch (Exception e)
            {
                Debug.Log($"BetaHub Wwise diag failed: {e.Message}");
            }
        }
#endif

        private bool StartWwiseCapture()
        {
            string fileName = $"wwise_capture_{_segmentIndex}.wav";
            _segmentIndex++;

            _captureFilePath = Path.Combine(_outputDirectory, fileName);

            // Wwise Stream Manager prepends its own base path (typically Application.persistentDataPath).
            // We must pass a path relative to that base, not an absolute path.
            string basePath = Application.persistentDataPath;
            if (_captureFilePath.StartsWith(basePath))
            {
                _captureRelativePath = _captureFilePath.Substring(basePath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            else
            {
                _captureRelativePath = fileName;
                _captureFilePath = Path.Combine(basePath, _captureRelativePath);
            }

            var result = _startCaptureMethod.Invoke(null, new object[] { _captureRelativePath });
            int resultInt = (int)result;

            if (resultInt == _akSuccessValue)
            {
                _wwiseIsRecording = true;
                return true;
            }

            Debug.LogError($"BetaHub: Failed to start Wwise audio capture (result: {result})");
            _captureFilePath = null;
            return false;
        }

        private void StopWwiseCapture()
        {
            if (_stopCaptureMethod != null)
            {
                _stopCaptureMethod.Invoke(null, null);
            }

            _wwiseIsRecording = false;

            if (_captureFilePath != null && File.Exists(_captureFilePath))
            {
                _segmentPaths.Add(_captureFilePath);
            }

            _captureFilePath = null;
        }

        private static bool ConcatenateWavFiles(List<string> inputPaths, string outputPath)
        {
            try
            {
                int sampleRate = 0;
                short channels = 0;
                short bitsPerSample = 0;

                var allPcmData = new List<byte[]>();
                int totalDataSize = 0;

                foreach (var path in inputPaths)
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                    using (var reader = new BinaryReader(stream))
                    {
                        if (stream.Length < 44) continue;

                        reader.ReadBytes(4); // "RIFF"
                        reader.ReadInt32();   // file size
                        reader.ReadBytes(4); // "WAVE"
                        reader.ReadBytes(4); // "fmt "
                        int fmtSize = reader.ReadInt32();
                        reader.ReadInt16();   // audio format
                        short ch = reader.ReadInt16();
                        int sr = reader.ReadInt32();
                        reader.ReadInt32();   // byte rate
                        reader.ReadInt16();   // block align
                        short bps = reader.ReadInt16();

                        if (fmtSize > 16)
                            reader.ReadBytes(fmtSize - 16);

                        if (sampleRate == 0)
                        {
                            sampleRate = sr;
                            channels = ch;
                            bitsPerSample = bps;
                        }

                        // Find "data" chunk
                        while (stream.Position < stream.Length - 8)
                        {
                            string chunkId = new string(reader.ReadChars(4));
                            int chunkSize = reader.ReadInt32();

                            if (chunkId == "data")
                            {
                                int bytesToRead = (int)Math.Min(chunkSize, stream.Length - stream.Position);
                                byte[] pcmData = reader.ReadBytes(bytesToRead);
                                allPcmData.Add(pcmData);
                                totalDataSize += pcmData.Length;
                                break;
                            }

                            stream.Position += chunkSize;
                        }
                    }
                }

                if (totalDataSize == 0 || sampleRate == 0) return false;

                int byteRate = sampleRate * channels * bitsPerSample / 8;
                short blockAlign = (short)(channels * bitsPerSample / 8);

                using (var stream = new FileStream(outputPath, FileMode.Create))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                    writer.Write(36 + totalDataSize);
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                    writer.Write(16);
                    writer.Write((short)1);
                    writer.Write(channels);
                    writer.Write(sampleRate);
                    writer.Write(byteRate);
                    writer.Write(blockAlign);
                    writer.Write(bitsPerSample);
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                    writer.Write(totalDataSize);

                    foreach (var pcm in allPcmData)
                    {
                        writer.Write(pcm);
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"BetaHub: Failed to concatenate WAV files: {e.Message}");
                return false;
            }
        }
    }
}
#endif
