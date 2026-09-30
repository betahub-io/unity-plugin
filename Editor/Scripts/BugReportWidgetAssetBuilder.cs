using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BetaHub.Editor
{
    /// <summary>
    /// Builds the shipped widget assets through Unity's own APIs instead of hand-written YAML:
    /// the runtime theme file, a PanelSettings asset and the BugReportWidget prefab.
    /// Menu: BetaHub / Build Widget Assets. Also callable with -executeMethod.
    /// </summary>
    public static class BugReportWidgetAssetBuilder
    {
        private const string MenuPath = "BetaHub/Build Widget Assets";
        private const string UxmlFileName = "BugReportWidget";
        private const string ThemeFileName = "BetaHubRuntimeTheme.tss";
        private const string ThemeSource = "@import url(\"unity-theme://default\");\n";
        private const string PanelSettingsFileName = "BugReportPanelSettings.asset";
        private const string PrefabFileName = "BugReportWidget.prefab";
        private const string PrefabObjectName = "BugReportWidget";
        private const int ReferenceWidth = 1280;
        private const int ReferenceHeight = 720;
        private const float MatchWidthOrHeight = 0.5f;
        private const float SortingOrder = 1000f;
        private const string DemoProjectId = BetaHubReporter.DEMO_PROJECT_ID;
        private const string DemoAuthToken = "tkn-15e6fbc1470613d5cfd2199edbde52157379b8c6dcd365441eabf8fea62d76a7";

        [MenuItem(MenuPath)]
        public static void Build()
        {
            string uxmlPath = FindUxmlPath();
            if (uxmlPath == null)
            {
                Debug.LogError(UnityBetaHubLog.Prefix + "BugReportWidget.uxml not found; cannot build widget assets.");
                return;
            }

            string uiDir = Path.GetDirectoryName(uxmlPath).Replace('\\', '/');
            string runtimeDir = Path.GetDirectoryName(uiDir).Replace('\\', '/');
            string prefabDir = runtimeDir + "/Prefabs";

            var theme = EnsureTheme(uiDir + "/" + ThemeFileName);
            var panelSettings = EnsurePanelSettings(uiDir + "/" + PanelSettingsFileName, theme);
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            string prefabPath = BuildPrefab(prefabDir + "/" + PrefabFileName, panelSettings, tree);

            AssetDatabase.SaveAssets();
            Debug.Log(UnityBetaHubLog.Prefix + "Widget assets built: " + prefabPath);
        }

        private static string FindUxmlPath()
        {
            foreach (string guid in AssetDatabase.FindAssets(UxmlFileName + " t:VisualTreeAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == UxmlFileName) return path;
            }
            return null;
        }

        private static ThemeStyleSheet EnsureTheme(string path)
        {
            if (!File.Exists(path))
            {
                File.WriteAllText(path, ThemeSource);
                AssetDatabase.ImportAsset(path);
            }
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(path);
            if (theme == null) Debug.LogError(UnityBetaHubLog.Prefix + "Theme could not be imported at " + path);
            return theme;
        }

        private static PanelSettings EnsurePanelSettings(string path, ThemeStyleSheet theme)
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, path);
            }
            settings.themeStyleSheet = theme;
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(ReferenceWidth, ReferenceHeight);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = MatchWidthOrHeight;
            settings.sortingOrder = SortingOrder;
            EditorUtility.SetDirty(settings);
            return settings;
        }

        private static string BuildPrefab(string path, PanelSettings panelSettings, VisualTreeAsset tree)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var go = existing != null ? PrefabUtility.LoadPrefabContents(path) : new GameObject(PrefabObjectName);

            var document = go.GetComponent<UIDocument>() ?? go.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = tree;

            var reporter = go.GetComponent<BetaHubReporter>() ?? go.AddComponent<BetaHubReporter>();
            if (string.IsNullOrEmpty(reporter.ProjectID))
            {
                reporter.ProjectID = DemoProjectId;
                reporter.AuthToken = DemoAuthToken;
            }
            if (go.GetComponent<CustomFieldValidator>() == null) go.AddComponent<CustomFieldValidator>();
            if (go.GetComponent<GameRecorder>() == null) go.AddComponent<GameRecorder>();
            if (go.GetComponent<BugReportWidget>() == null) go.AddComponent<BugReportWidget>();

            if (existing != null)
            {
                PrefabUtility.SaveAsPrefabAsset(go, path);
                PrefabUtility.UnloadPrefabContents(go);
            }
            else
            {
                PrefabUtility.SaveAsPrefabAsset(go, path);
                Object.DestroyImmediate(go);
            }
            return path;
        }
    }
}
