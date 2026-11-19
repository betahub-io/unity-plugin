// Maintainers only: it writes the Demo sample's scene. The development workspace defines BETAHUB_PLUGIN_DEVELOPMENT
// and links the package's Samples~ in as Assets/Samples.
#if BETAHUB_PLUGIN_DEVELOPMENT
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BetaHub.Editor
{
    /// <summary>
    /// Builds Samples~/Demo/BugReportDemo.unity: a camera, the demo game and the widget prefab. Only
    /// built-in components and the package's own assets, so the scene opens in any project.
    /// Menu: BetaHub / Build Demo Scene. Also callable with -executeMethod.
    /// </summary>
    public static class DemoSceneBuilder
    {
        public const string ScenePath = "Assets/Samples/Demo/BugReportDemo.unity";
        private const string WidgetPrefabGuid = "9b3e989a3a90248d1943e213519d0869";
        // the sample's script compiles into the game's assembly
        private const string GameTypeName = "DemoGame, Assembly-CSharp";
        private const string WidgetField = "widget";
        private const float CameraSize = 5f;
        private const float CameraDistance = 10f;
        private static readonly Color Background = new Color32(20, 24, 42, 255);

        [MenuItem("BetaHub/Build Demo Scene")]
        public static void Build()
        {
            var gameType = Type.GetType(GameTypeName);
            if (gameType == null)
            {
                Debug.LogError(UnityBetaHubLog.Prefix + "DemoGame is not compiled. Is Samples~ linked in as Assets/Samples?");
                return;
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(WidgetPrefabGuid));
            if (prefab == null)
            {
                Debug.LogError(UnityBetaHubLog.Prefix + "BugReportWidget.prefab not found.");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // sprites need no sky or ambient light; a skybox would be one more asset to resolve
            RenderSettings.skybox = null;

            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
            cameraObject.transform.position = new Vector3(0f, 0f, -CameraDistance);
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = CameraSize;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Background;

            var widget = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);

            var game = new GameObject("Demo Game");
            var component = game.AddComponent(gameType);
            var serialized = new SerializedObject(component);
            serialized.FindProperty(WidgetField).objectReferenceValue = widget.GetComponent<BugReportWidget>();
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log(UnityBetaHubLog.Prefix + "Demo scene built: " + ScenePath);
        }
    }
}
#endif
