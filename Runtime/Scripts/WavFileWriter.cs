using System.IO;
using UnityEngine;

namespace BetaHub
{
    public static class WavFileWriter
    {
        public static void Write(string path, float[] samples, int sampleRate, int channels)
        {
            const int bitsPerSample = 16;
            int byteRate = sampleRate * channels * bitsPerSample / 8;
            int blockAlign = channels * bitsPerSample / 8;
            int dataSize = samples.Length * (bitsPerSample / 8);

            using (var stream = new FileStream(path, FileMode.Create))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataSize);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

                writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)channels);
                writer.Write(sampleRate);
                writer.Write(byteRate);
                writer.Write((short)blockAlign);
                writer.Write((short)bitsPerSample);

                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                writer.Write(dataSize);

                for (int i = 0; i < samples.Length; i++)
                {
                    float clamped = Mathf.Clamp(samples[i], -1f, 1f);
                    writer.Write((short)(clamped * 32767f));
                }
            }
        }
    }
}
