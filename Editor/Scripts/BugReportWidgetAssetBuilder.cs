using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BetaHub.Editor
{
    /// <summary>
    /// Builds the shipped widget assets through Unity's own APIs instead of hand-written YAML:
    /// the runtime theme file, one USS per built-in theme, a PanelSettings asset and the
    /// BugReportWidget prefab. Menu: BetaHub / Build Widget Assets. Also callable with -executeMethod.
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
        private const string ThemesFolder = "Themes";
        private const string DefaultThemeName = "Dark";
        // The widget is sized for a 1080p TV and follows the screen height, so text keeps its size on any aspect.
        private const int ReferenceWidth = 1920;
        private const int ReferenceHeight = 1080;
        private const float MatchHeight = 1f;
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
            var widgetTheme = WriteBuiltInThemes(uiDir);
            var panelSettings = EnsurePanelSettings(uiDir + "/" + PanelSettingsFileName, theme);
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            string prefabPath = BuildPrefab(prefabDir + "/" + PrefabFileName, panelSettings, tree, widgetTheme);

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

        /// <summary>Writes Runtime/UI/Themes/BugReportTheme-*.uss from the recipe; returns the default one.</summary>
        public static StyleSheet WriteBuiltInThemes(string uiDir)
        {
            string folder = uiDir + "/" + ThemesFolder;
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(uiDir, ThemesFolder);

            StyleSheet defaultTheme = null;
            foreach (var spec in BugReportThemeLibrary.BuiltIns)
            {
                string path = folder + "/" + ThemeUssWriter.FileNameFor(spec.Name);
                string uss = ThemeUssWriter.Write(spec);
                if (!File.Exists(path) || File.ReadAllText(path) != uss)
                {
                    File.WriteAllText(path, uss);
                    AssetDatabase.ImportAsset(path);
                }
                if (spec.Name == DefaultThemeName) defaultTheme = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            }
            if (defaultTheme == null) Debug.LogError(UnityBetaHubLog.Prefix + "Default theme " + DefaultThemeName + " could not be imported.");
            return defaultTheme;
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
            settings.match = MatchHeight;
            settings.sortingOrder = SortingOrder;
            EditorUtility.SetDirty(settings);
            return settings;
        }

        private static string BuildPrefab(string path, PanelSettings panelSettings, VisualTreeAsset tree, StyleSheet widgetTheme)
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
            var widget = go.GetComponent<BugReportWidget>() ?? go.AddComponent<BugReportWidget>();
            if (widget.Theme == null) widget.Theme = widgetTheme;
            // The shipped prefab carries the default copy; serialized values from older versions would
            // otherwise survive field renames. Studios edit strings on their own instance or variant.
            widget.Strings = new BugReportWidgetStrings();

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
