#if UNITY_EDITOR
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace BetaHub.Tests
{
    // Renders the shipped widget prefab in every state into PNGs, headless, so the UI Toolkit result
    // can be compared with the design of record. Output folder: BH_PREVIEW_OUT, else <project>/WidgetPreview.
    public class WidgetPreviewCapture
    {
        private const string PrefabPath = "Assets/Runtime/Prefabs/BugReportWidget.prefab";
        private const string ThemeFolder = "Assets/Runtime/UI/Themes/";
        private const string OutputEnvVar = "BH_PREVIEW_OUT";
        private const string DefaultOutputFolder = "WidgetPreview";
        private const int Width = 1920;
        private const int Height = 1080;
        private const int DeckWidth = 1280;
        private const int DeckHeight = 800;
        private const int SettleFrames = 4;
        private const string RepaintMethodName = "RepaintOffscreenPanels";
        // a dim game frame stand-in behind the scrim
        private static readonly Color StageBackground = new Color(0.10f, 0.11f, 0.15f, 1f);

        private struct Shot
        {
            public string Name;
            public WidgetPreviewOptions Options;
            public string Theme;
        }

        private static Shot S(string name, WidgetPreviewState state, int attachments = 3, int focus = -1, bool signedIn = false, bool terms = false, string theme = "Dark")
        {
            return new Shot
            {
                Name = name,
                Theme = theme,
                Options = new WidgetPreviewOptions { State = state, AttachmentCount = attachments, FocusedAttachment = focus, SignedIn = signedIn, Terms = terms },
            };
        }

        private static readonly Shot[] Shots =
        {
            S("compose-3", WidgetPreviewState.Compose),
            S("compose-6-terms", WidgetPreviewState.Compose, attachments: 6, terms: true),
            S("compose-9-log-focused", WidgetPreviewState.Compose, attachments: 9, focus: 2),
            S("compose-9-data-focused", WidgetPreviewState.Compose, attachments: 9, focus: 5),
            S("compose-3-clip-focused", WidgetPreviewState.Compose, focus: 1),
            S("preview", WidgetPreviewState.Preview, attachments: 9),
            S("sending", WidgetPreviewState.Sending),
            S("error", WidgetPreviewState.Error),
            S("unavailable", WidgetPreviewState.Unavailable),
            S("uploading", WidgetPreviewState.Uploading, attachments: 6),
            S("sent-anonymous", WidgetPreviewState.Sent),
            S("sent-signed-in", WidgetPreviewState.Sent, signedIn: true),
            S("toast", WidgetPreviewState.Toast),
            S("compose-light", WidgetPreviewState.Compose, theme: "Light"),
            S("compose-ocean", WidgetPreviewState.Compose, attachments: 6, focus: 0, theme: "Ocean"),
        };

        [UnityTest]
        public IEnumerator CapturesEveryState()
        {
            string folder = OutputFolder();
            yield return CaptureAll(folder, Width, Height, Shots);
        }

        [UnityTest]
        public IEnumerator CapturesSteamDeckSize()
        {
            string folder = OutputFolder();
            yield return CaptureAll(folder, DeckWidth, DeckHeight, new[]
            {
                S("deck-compose-9", WidgetPreviewState.Compose, attachments: 9, focus: 2),
                S("deck-sent", WidgetPreviewState.Sent),
            });
        }

        private static string OutputFolder()
        {
            string folder = System.Environment.GetEnvironmentVariable(OutputEnvVar);
            if (string.IsNullOrEmpty(folder)) folder = Path.Combine(Application.dataPath, "..", DefaultOutputFolder);
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static IEnumerator CaptureAll(string folder, int width, int height, Shot[] shots)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, "Prefab missing at " + PrefabPath + ". Run BetaHub > Build Widget Assets.");
            var sourceSettings = prefab.GetComponent<UIDocument>().panelSettings;

            var target = new RenderTexture(width, height, 24);
            var settings = Object.Instantiate(sourceSettings);
            settings.targetTexture = target;
            settings.clearColor = true;
            // The clear value is consumed in the project's working color space; USS colors are sRGB.
            settings.colorClearValue = QualitySettings.activeColorSpace == ColorSpace.Linear ? StageBackground.linear : StageBackground;

            // Instantiate inactive so the document builds its tree on the capture panel, not the shared one.
            var parent = new GameObject("BetaHubWidgetCapture");
            parent.SetActive(false);
            var instance = Object.Instantiate(prefab, parent.transform);
            instance.GetComponent<UIDocument>().panelSettings = settings;
            parent.SetActive(true);
            var widget = instance.GetComponent<BugReportWidget>();

            try
            {
                yield return Settle();
                foreach (var shot in shots)
                {
                    var theme = AssetDatabase.LoadAssetAtPath<StyleSheet>(ThemeFolder + ThemeUssWriter.FileNameFor(shot.Theme));
                    Assert.IsNotNull(theme, "Theme missing: " + shot.Theme);
                    widget.SetTheme(theme);
                    widget.ApplyPreview(shot.Options);
                    yield return Settle();

                    string path = Path.Combine(folder, "widget-" + shot.Name + ".png");
                    File.WriteAllBytes(path, ReadBack(target, width, height));
                    Debug.Log("Widget preview written to " + path);
                    Assert.IsTrue(File.Exists(path));
                }
            }
            finally
            {
                Object.Destroy(parent);
                Object.Destroy(settings);
                target.Release();
            }
        }

        // Offscreen panels repaint as part of frame rendering, which -batchmode skips. In batchmode we
        // poke UI Toolkit's own repaint entry point through reflection; WaitForEndOfFrame never resolves there.
        private static IEnumerator Settle()
        {
            for (int i = 0; i < SettleFrames; i++)
            {
                yield return null;
                if (Application.isBatchMode) ForceOffscreenRepaint();
                else yield return new WaitForEndOfFrame();
            }
        }

        private static byte[] ReadBack(RenderTexture target, int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            byte[] png = texture.EncodeToPNG();
            Object.Destroy(texture);
            return png;
        }

        private static void ForceOffscreenRepaint()
        {
            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            var utility = typeof(UIDocument).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");
            var method = utility?.GetMethod(RepaintMethodName, flags);
            if (method == null)
            {
                Debug.LogWarning(RepaintMethodName + " not found; batchmode captures may be blank.");
                return;
            }
            method.Invoke(null, method.GetParameters().Length == 0 ? null : new object[method.GetParameters().Length]);
        }
    }
}
#endif
