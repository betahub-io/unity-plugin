using System;

namespace BetaHub
{
    public class CircularAudioBuffer
    {
        private readonly float[] _buffer;
        private int _writePosition;
        private long _totalSamplesWritten;
        private readonly object _lock = new object();

        public int SampleRate { get; }
        public int Channels { get; }

        public CircularAudioBuffer(int sampleRate, int channels, int durationSeconds)
        {
            SampleRate = sampleRate;
            Channels = channels;
            _buffer = new float[sampleRate * channels * durationSeconds];
        }

        public void Write(float[] data, int count)
        {
            lock (_lock)
            {
                for (int i = 0; i < count; i++)
                {
                    _buffer[_writePosition] = data[i];
                    _writePosition = (_writePosition + 1) % _buffer.Length;
                }
                _totalSamplesWritten += count;
            }
        }

        public float[] ReadAll()
        {
            lock (_lock)
            {
                int validSamples = (int)Math.Min(_totalSamplesWritten, _buffer.Length);
                float[] result = new float[validSamples];

                if (_totalSamplesWritten <= _buffer.Length)
                {
                    Array.Copy(_buffer, 0, result, 0, validSamples);
                }
                else
                {
                    int firstPart = _buffer.Length - _writePosition;
                    Array.Copy(_buffer, _writePosition, result, 0, firstPart);
                    Array.Copy(_buffer, 0, result, firstPart, _writePosition);
                }

                return result;
            }
        }
    }
}
