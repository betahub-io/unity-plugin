using UnityEngine;
using BetaHub;

/// <summary>
/// Example script demonstrating how to attach custom field values to bug reports.
/// Custom fields must be pre-configured in your BetaHub project settings
/// (Project Settings > Custom Fields) and marked as tester_settable.
/// </summary>
public class CustomFieldsExample : MonoBehaviour
{
    [SerializeField] private BugReportUI bugReportUI;

    [Header("Example Custom Field Values")]
    [SerializeField] private string gameId = "my-game-123";

    void Start()
    {
        if (bugReportUI == null)
        {
            Debug.LogError("CustomFieldsExample: Missing BugReportUI reference!");
            return;
        }

        // Static custom field: value is set once and included in every bug report.
        bugReportUI.SetCustomField("game_id", gameId);

        // Dynamic custom field provider: the function is called at submission time,
        // so it always returns the current value.
        bugReportUI.RegisterCustomFieldProvider("current_level", () => GetCurrentLevelName());
        bugReportUI.RegisterCustomFieldProvider("play_time", () => GetPlayTime());
    }

    private string GetCurrentLevelName()
    {
        // Replace with your actual level tracking logic
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        return scene.name;
    }

    private string GetPlayTime()
    {
        // Example: total play time since session start
        return $"{Mathf.FloorToInt(Time.realtimeSinceStartup / 60)}m {Mathf.FloorToInt(Time.realtimeSinceStartup % 60)}s";
    }

    #if UNITY_EDITOR
    void OnValidate()
    {
        if (bugReportUI == null)
            bugReportUI = FindObjectOfType<BugReportUI>();
    }
    #endif
}
