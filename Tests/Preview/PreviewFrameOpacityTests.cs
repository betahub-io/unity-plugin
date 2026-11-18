using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BetaHub.Tests
{
    // Unity's default camera clears with an alpha near 0. The clip's preview frames are copies of the
    // capture; with that alpha kept, the pane behind showed through the game's background.
    public class PreviewFrameOpacityTests
    {
        private const int Width = 64;
        private const int Height = 36;

        [UnityTest]
        public IEnumerator PreviewFrame_DropsTheGamesAlpha_KeepsItsColour()
        {
            var capture = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32);
            var frame = new RenderTexture(Width, Height, 0, GameRecorder.PreviewFrameFormat());
            var read = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = capture;
                GL.Clear(true, true, new Color(0.19f, 0.30f, 0.47f, 0.02f));
                Graphics.Blit(capture, frame);
                RenderTexture.active = frame;
                read.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                read.Apply();

                Color pixel = read.GetPixel(Width / 2, Height / 2);
                Assert.AreEqual(1f, pixel.a, 0.001f, "opaque");
                Assert.IsTrue(pixel.b > pixel.g && pixel.g > pixel.r, $"still the game's blue: {pixel}");
            }
            finally
            {
                RenderTexture.active = previous;
                capture.Release();
                frame.Release();
                Object.Destroy(capture);
                Object.Destroy(frame);
                Object.Destroy(read);
            }
            yield return null;
        }
    }
}
