using UnityEditor;
using UnityEngine;

namespace BetaHub.Editor
{
    /// <summary>GameObject > BetaHub > Bug Report Widget: puts the shipped widget prefab in the open scene.</summary>
    internal static class AddWidgetMenu
    {
        // BugReportWidget.prefab, wherever the package sits
        private const string WidgetPrefabGuid = "9b3e989a3a90248d1943e213519d0869";

        [MenuItem("GameObject/BetaHub/Bug Report Widget", false, 10)]
        private static void AddWidget()
        {
            var existing = Object.FindObjectOfType<BetaHubReporter>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing.gameObject);
                Debug.LogWarning(UnityBetaHubLog.Prefix + "This scene already has a BetaHubReporter on '" + existing.name + "'. One per game is enough.");
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(WidgetPrefabGuid));
            if (prefab == null)
            {
                Debug.LogError(UnityBetaHubLog.Prefix + "BugReportWidget.prefab is missing from the package. Reinstall the BetaHub Bug Reporter package.");
                return;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(instance, "Add Bug Report Widget");
            Selection.activeGameObject = instance;
        }
    }
}
