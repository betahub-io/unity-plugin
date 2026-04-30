#if BETAHUB_WWISE
using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BetaHub
{
    public class WwiseAudioCapture : IAudioCaptureBackend
    {
        private string _captureFilePath;
        private string _captureRelativePath;
        private Type _akSoundEngineType;
        private MethodInfo _startCaptureMethod;
        private MethodInfo _stopCaptureMethod;
        private int _akSuccessValue = -1;

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

            _captureFilePath = Path.Combine(outputDirectory, "wwise_capture.wav");

            // Wwise Stream Manager prepends its own base path (typically Application.persistentDataPath).
            // We must pass a path relative to that base, not an absolute path.
            string basePath = Application.persistentDataPath;
            if (_captureFilePath.StartsWith(basePath))
            {
                _captureRelativePath = _captureFilePath.Substring(basePath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            else
            {
                _captureRelativePath = "wwise_capture.wav";
                _captureFilePath = Path.Combine(basePath, _captureRelativePath);
            }

            var result = _startCaptureMethod.Invoke(null, new object[] { _captureRelativePath });
            int resultInt = (int)result;

            if (resultInt == _akSuccessValue)
            {
                IsCapturing = true;
            }
            else
            {
                Debug.LogError($"BetaHub: Failed to start Wwise audio capture (result: {result})");
                _captureFilePath = null;
            }
        }

        public string StopCapture()
        {
            if (!IsCapturing) return null;

            IsCapturing = false;

            if (_stopCaptureMethod != null)
            {
                _stopCaptureMethod.Invoke(null, null);
            }

            if (_captureFilePath != null && File.Exists(_captureFilePath))
            {
                return _captureFilePath;
            }

            return null;
        }
    }
}
#endif
