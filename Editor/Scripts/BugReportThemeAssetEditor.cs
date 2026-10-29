using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BetaHub.Editor
{
    /// <summary>
    /// Inspector for a theme asset: writes its stylesheet next to it on every change and shows the
    /// colors the recipe derived, with the contrast each one clears.
    /// </summary>
    [CustomEditor(typeof(BugReportThemeAsset))]
    public class BugReportThemeAssetEditor : UnityEditor.Editor
    {
        private const string StylesheetLabel = "Stylesheet";
        private const string DerivedHeader = "Derived colors";
        private const string AssignHint = "Assign the stylesheet to the BugReportWidget's Theme field. It is regenerated whenever this asset changes; do not edit it.";
        private const float SwatchHeight = 18f;
        private const float SwatchWidth = 44f;

        // what this inspector last wrote; Undo changes the asset without a GUI change, so compare content
        private string _written;

        private void OnEnable()
        {
            // A renamed asset or a pulled change gets its stylesheet on first view.
            if (target != null && IsSaved((BugReportThemeAsset)target)) _written = File.ReadAllText(WriteStylesheet((BugReportThemeAsset)target));
        }

        public override void OnInspectorGUI()
        {
            var asset = (BugReportThemeAsset)target;
            DrawDefaultInspector();
            // still being named in the Project window
            if (!IsSaved(asset)) return;
            string path = BugReportThemeAsset.StylesheetPathFor(AssetDatabase.GetAssetPath(asset));
            string uss = ThemeUssWriter.Write(asset.ToSpec());
            if (uss != _written || !File.Exists(path))
            {
                WriteStylesheet(asset);
                _written = uss;
            }
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(StylesheetLabel, AssetDatabase.LoadAssetAtPath<StyleSheet>(path), typeof(StyleSheet), false);
            }
            EditorGUILayout.HelpBox(AssignHint, MessageType.Info);

            var spec = asset.ToSpec();
            var t = ThemeRecipe.Derive(spec.Accent, spec.NeutralTint, spec.Mode, spec.Radius);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(DerivedHeader, EditorStyles.boldLabel);
            Swatch("Background", t.Ground, null);
            Swatch("Surface", t.Surface, null);
            Swatch("Field", t.Field, null);
            Swatch("Text", t.Text, t.Surface);
            Swatch("Secondary text", t.Muted, t.Surface);
            Swatch("Accent", t.Accent, t.Ground);
            Swatch("On accent", t.OnAccent, t.Accent);
            Swatch("OK", t.Ok, t.Surface);
            Swatch("Warning", t.Warn, t.Surface);
            Swatch("Error", t.Error, t.Surface);
        }

        private static bool IsSaved(BugReportThemeAsset asset) => !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(asset));

        private static void Swatch(string label, string hex, string againstHex)
        {
            var rect = EditorGUILayout.GetControlRect(false, SwatchHeight);
            var labelRect = new Rect(rect.x, rect.y, EditorGUIUtility.labelWidth, rect.height);
            var colorRect = new Rect(labelRect.xMax, rect.y + 1f, SwatchWidth, rect.height - 2f);
            var valueRect = new Rect(colorRect.xMax + 8f, rect.y, rect.xMax - colorRect.xMax - 8f, rect.height);
            EditorGUI.LabelField(labelRect, label);
            var rgb = ColorMath.FromHex(hex);
            EditorGUI.DrawRect(colorRect, new Color(rgb.R, rgb.G, rgb.B));
            string value = hex;
            if (againstHex != null)
            {
                float ratio = ColorMath.ContrastRatio(rgb, ColorMath.FromHex(againstHex));
                value += string.Format(System.Globalization.CultureInfo.InvariantCulture, "   {0:0.0}:1", ratio);
            }
            EditorGUI.LabelField(valueRect, value, EditorStyles.miniLabel);
        }

        /// <summary>Writes the asset's stylesheet next to it when it differs. Returns the stylesheet path.</summary>
        public static string WriteStylesheet(BugReportThemeAsset asset)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            string assetPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(assetPath)) throw new ArgumentException("The theme is not saved as an asset yet.", nameof(asset));
            string path = BugReportThemeAsset.StylesheetPathFor(assetPath);
            string uss = ThemeUssWriter.Write(asset.ToSpec());
            if (!File.Exists(path) || File.ReadAllText(path) != uss)
            {
                File.WriteAllText(path, uss);
                AssetDatabase.ImportAsset(path);
            }
            return path;
        }

        [MenuItem("BetaHub/Regenerate Theme Stylesheets")]
        public static void RegenerateAll()
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(BugReportThemeAsset));
            foreach (string guid in guids)
            {
                WriteStylesheet(AssetDatabase.LoadAssetAtPath<BugReportThemeAsset>(AssetDatabase.GUIDToAssetPath(guid)));
            }
            Debug.Log(UnityBetaHubLog.Prefix + "Theme stylesheets up to date: " + guids.Length + " theme asset(s).");
        }
    }
}
