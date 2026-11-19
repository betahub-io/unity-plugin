using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BetaHub.Editor
{
    /// <summary>
    /// Inspector for the widget with a preview section: shows any screen with sample data in the
    /// Game view, in edit mode, so themes and strings can be checked without a play session.
    /// </summary>
    [CustomEditor(typeof(BugReportWidget))]
    public class BugReportWidgetEditor : UnityEditor.Editor
    {
        private const string PreviewHeader = "Preview (Game view, no Play mode needed)";
        private const string ClearLabel = "Clear preview";
        private const string AssignedThemeLabel = "Assigned (Theme field)";
        private static readonly int[] AttachmentCounts = { 3, 6, 9 };
        private static readonly string[] AttachmentCountLabels = { "3", "6", "9" };

        private WidgetPreviewOptions _options = new WidgetPreviewOptions { State = WidgetPreviewState.None, AttachmentCount = 3, FocusedAttachment = -1 };
        private int _themeIndex;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(PreviewHeader, EditorStyles.boldLabel);
            var widget = (BugReportWidget)target;

            EditorGUI.BeginChangeCheck();
            _options.State = (WidgetPreviewState)EditorGUILayout.EnumPopup("State", _options.State);
            int countIndex = System.Array.IndexOf(AttachmentCounts, _options.AttachmentCount);
            countIndex = EditorGUILayout.Popup("Attachments", countIndex < 0 ? 0 : countIndex, AttachmentCountLabels);
            _options.AttachmentCount = AttachmentCounts[countIndex];
            _options.SignedIn = EditorGUILayout.Toggle("Signed in", _options.SignedIn);
            _options.Terms = EditorGUILayout.Toggle("Terms row", _options.Terms);
            _options.FocusedAttachment = EditorGUILayout.IntSlider("Focused attachment", _options.FocusedAttachment, -1, _options.AttachmentCount - 1);
            _options.Typing = EditorGUILayout.Toggle("Typing", _options.Typing);
            _options.Glyphs = (GlyphSet)EditorGUILayout.EnumPopup("Glyphs", _options.Glyphs);
            var themeNames = System.Array.ConvertAll(BugReportThemeLibrary.BuiltIns, s => s.Name);
            var themeLabels = new string[themeNames.Length + 1];
            themeLabels[0] = AssignedThemeLabel;
            System.Array.Copy(themeNames, 0, themeLabels, 1, themeNames.Length);
            _themeIndex = EditorGUILayout.Popup("Theme", _themeIndex, themeLabels);
            if (EditorGUI.EndChangeCheck())
            {
                _options.Theme = _themeIndex == 0 ? null : LoadBuiltInTheme(widget, themeNames[_themeIndex - 1]);
                Apply(widget);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (WidgetPreviewState state in System.Enum.GetValues(typeof(WidgetPreviewState)))
                {
                    if (state == WidgetPreviewState.None) continue;
                    if (GUILayout.Button(state.ToString(), EditorStyles.miniButton))
                    {
                        _options.State = state;
                        Apply(widget);
                    }
                }
            }
            if (GUILayout.Button(ClearLabel))
            {
                _options.State = WidgetPreviewState.None;
                Apply(widget);
            }
            EditorGUILayout.HelpBox("Preview uses sample data. Theme, strings and layout are live; submission is not. " +
                "The preview's Theme and Terms row only change the preview, never the settings above.", MessageType.None);
        }

        private void Apply(BugReportWidget widget)
        {
            widget.ApplyPreview(_options);
            EditorApplication.QueuePlayerLoopUpdate();
        }

        // The built-ins sit next to the shipped UXML, wherever the package lives.
        private static StyleSheet LoadBuiltInTheme(BugReportWidget widget, string name)
        {
            var document = widget.GetComponent<UIDocument>();
            if (document == null || document.visualTreeAsset == null) return null;
            string uxmlPath = AssetDatabase.GetAssetPath(document.visualTreeAsset);
            string path = Path.GetDirectoryName(uxmlPath).Replace('\\', '/') + "/Themes/" + ThemeUssWriter.FileNameFor(name);
            return AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
        }
    }
}
