using System;
using System.IO;
using UnityEngine;

namespace BetaHub
{
    public class UnityAudioCapture : MonoBehaviour, IAudioCaptureBackend
    {
        private CircularAudioBuffer _buffer;
        private string _outputDirectory;
        private int _durationSeconds;

        public bool IsCapturing { get; private set; }

        public void Initialize(int durationSeconds)
        {
            _durationSeconds = durationSeconds;
        }

        public void StartCapture(string outputDirectory)
        {
            _outputDirectory = outputDirectory;
            int sampleRate = AudioSettings.outputSampleRate;
            int channels = GetChannelCount(AudioSettings.speakerMode);

            _buffer = new CircularAudioBuffer(sampleRate, channels, _durationSeconds);
            IsCapturing = true;
        }

        public string StopCapture()
        {
            IsCapturing = false;
            if (_buffer == null) return null;

            float[] samples = _buffer.ReadAll();
            _buffer = null;
            if (samples.Length == 0) return null;

            string wavPath = Path.Combine(_outputDirectory, $"audio_{DateTime.Now:yyyyMMdd_HHmmss}.wav");
            WavFileWriter.Write(wavPath, samples, AudioSettings.outputSampleRate,
                GetChannelCount(AudioSettings.speakerMode));

            return wavPath;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (IsCapturing && _buffer != null)
            {
                _buffer.Write(data, data.Length);
            }
        }

        private static int GetChannelCount(AudioSpeakerMode mode)
        {
            switch (mode)
            {
                case AudioSpeakerMode.Mono: return 1;
                case AudioSpeakerMode.Stereo: return 2;
                case AudioSpeakerMode.Quad: return 4;
                case AudioSpeakerMode.Surround: return 5;
                case AudioSpeakerMode.Mode5point1: return 6;
                case AudioSpeakerMode.Mode7point1: return 8;
                default: return 2;
            }
        }
    }
}
