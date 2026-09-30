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
    // Renders the UXML widget through a runtime panel into a texture and writes a PNG,
    // so the UI Toolkit result can be compared against the HTML preview pixel for pixel.
    public class WidgetPreviewCapture
    {
        private const int Width = 1280;
        private const int Height = 720;
        private const string UxmlPath = "Assets/Runtime/UI/BugReportWidget.uxml";
        private const string ThemePath = "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss";
        private const string ThemeSource = "@import url(\"unity-theme://default\");";
        private const string OutputEnvVar = "BH_PREVIEW_OUT";
        private const int WarmupFrames = 6;
        private static readonly Color StageBackground = new Color(0.043f, 0.051f, 0.078f, 1f);
        private const string RepaintMethodName = "RepaintOffscreenPanels";

        [UnityTest]
        public IEnumerator CapturesComposeScreen()
        {
            string outPath = System.Environment.GetEnvironmentVariable(OutputEnvVar);
            if (string.IsNullOrEmpty(outPath))
            {
                outPath = Path.Combine(Application.dataPath, "..", "widget-unity.png");
            }

            if (!File.Exists(ThemePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ThemePath));
                File.WriteAllText(ThemePath, ThemeSource);
                AssetDatabase.ImportAsset(ThemePath);
            }
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            Assert.IsNotNull(theme, "Runtime theme could not be loaded from " + ThemePath);

            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            Assert.IsNotNull(tree, "UXML not found at " + UxmlPath);

            var target = new RenderTexture(Width, Height, 24);
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = theme;
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.targetTexture = target;
            settings.clearColor = true;
            // The clear value is consumed in the project's working color space; USS colors are sRGB.
            settings.colorClearValue = QualitySettings.activeColorSpace == ColorSpace.Linear ? StageBackground.linear : StageBackground;

            var host = new GameObject("BetaHubWidgetPreview");
            var document = host.AddComponent<UIDocument>();
            document.panelSettings = settings;
            document.visualTreeAsset = tree;

            // Offscreen panels repaint as part of frame rendering, which -batchmode skips.
            // In batchmode we poke UI Toolkit's own repaint entry point through reflection instead.
            // WaitForEndOfFrame never resolves in batchmode, hence the guard.
            for (int i = 0; i < WarmupFrames; i++)
            {
                yield return null;
                if (Application.isBatchMode)
                {
                    ForceOffscreenRepaint();
                }
                else
                {
                    yield return new WaitForEndOfFrame();
                }
                Debug.Log($"frame {i}: corner={SamplePixel(target, 5, 5)} center={SamplePixel(target, Width / 2, Height / 2)}");
            }

            DumpTextFieldLayout(document.rootVisualElement);

            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            texture.Apply();
            RenderTexture.active = null;

            File.WriteAllBytes(outPath, texture.EncodeToPNG());
            Debug.Log("Widget preview written to " + outPath);

            Object.Destroy(host);
            Object.Destroy(settings);
            target.Release();
            Assert.IsTrue(File.Exists(outPath));
        }

        // Logs the resolved box of every element inside the description field, to explain wrap-width
        // differences between Unity and the HTML preview.
        private static void DumpTextFieldLayout(VisualElement root)
        {
            var field = root.Q<TextField>("bh-description");
            if (field == null) { Debug.LogWarning("bh-description not found"); return; }
            foreach (var el in field.Query<VisualElement>().Build())
            {
                var rs = el.resolvedStyle;
                Debug.Log($"layout {el.GetType().Name} name={el.name} classes=[{string.Join(" ", el.GetClasses())}] " +
                          $"w={rs.width:F1} h={rs.height:F1} pad={rs.paddingLeft:F1}/{rs.paddingRight:F1} margin={rs.marginLeft:F1}/{rs.marginRight:F1} " +
                          $"border={rs.borderLeftWidth:F1}/{rs.borderRightWidth:F1} font={rs.fontSize:F1} ws={rs.whiteSpace}");
            }
        }

        private static Color32 SamplePixel(RenderTexture rt, int x, int y)
        {
            var one = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            one.ReadPixels(new Rect(x, y, 1, 1), 0, 0);
            one.Apply();
            RenderTexture.active = previous;
            Color32 c = one.GetPixel(0, 0);
            Object.Destroy(one);
            return c;
        }

        private static void ForceOffscreenRepaint()
        {
            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            var utility = typeof(UIDocument).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");
            if (utility == null)
            {
                Debug.LogWarning("UIElementsRuntimeUtility not found; cannot force repaint in batchmode.");
                return;
            }
            var method = utility.GetMethod(RepaintMethodName, flags);
            if (method == null)
            {
                var names = new System.Text.StringBuilder();
                foreach (var m in utility.GetMethods(flags)) names.Append(m.Name).Append(' ');
                Debug.LogWarning(RepaintMethodName + " not found. Available: " + names);
                return;
            }
            method.Invoke(null, method.GetParameters().Length == 0 ? null : new object[method.GetParameters().Length]);
        }
    }
}
#endif
