using UnityEditor;
using UnityEngine;

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
        private const string SignedInLabel = "Preview as signed-in user";
        private const string ClearLabel = "Clear preview";

        private WidgetPreviewState _state = WidgetPreviewState.None;
        private bool _signedIn;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(PreviewHeader, EditorStyles.boldLabel);
            var widget = (BugReportWidget)target;

            EditorGUI.BeginChangeCheck();
            _signedIn = EditorGUILayout.Toggle(SignedInLabel, _signedIn);
            var picked = (WidgetPreviewState)EditorGUILayout.EnumPopup("State", _state);
            if (EditorGUI.EndChangeCheck())
            {
                _state = picked;
                widget.ApplyPreview(_state, _signedIn);
                EditorApplication.QueuePlayerLoopUpdate();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (WidgetPreviewState state in System.Enum.GetValues(typeof(WidgetPreviewState)))
                {
                    if (state == WidgetPreviewState.None) continue;
                    if (GUILayout.Button(state.ToString()))
                    {
                        _state = state;
                        widget.ApplyPreview(_state, _signedIn);
                        EditorApplication.QueuePlayerLoopUpdate();
                    }
                }
            }
            if (GUILayout.Button(ClearLabel))
            {
                _state = WidgetPreviewState.None;
                widget.ApplyPreview(_state, _signedIn);
                EditorApplication.QueuePlayerLoopUpdate();
            }
            EditorGUILayout.HelpBox("Preview uses sample data. Theme, strings and layout are live; submission is not.", MessageType.None);
        }
    }
}
