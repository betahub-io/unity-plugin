using UnityEngine;
using BetaHub;

/// <summary>
/// Example script demonstrating how to integrate device authentication with bug reporting.
/// This shows how to set up the components and link them together.
/// </summary>
public class DeviceAuthExample : MonoBehaviour
{
    [Header("Authentication Setup")]
    [SerializeField] private DeviceAuthManager authManager;
    [SerializeField] private DeviceAuthUI authUI;
    [SerializeField] private BugReportUI bugReportUI;
    
    [Header("Configuration")]
    [SerializeField] private string projectId = "pr-5287510306"; // Demo project
    [SerializeField] private string entityName = "Unity Game Demo";

    void Start()
    {
        SetupDeviceAuth();
    }

    void Update()
    {
        // Example: Toggle auth UI with T key
        if (Input.GetKeyDown(KeyCode.T))
        {
            ToggleAuthUI();
        }
        
        // Example: Show current auth status with Y key
        if (Input.GetKeyDown(KeyCode.Y))
        {
            ShowAuthStatus();
        }
    }

    private void SetupDeviceAuth()
    {
        if (authManager == null || authUI == null || bugReportUI == null)
        {
            Debug.LogError("DeviceAuthExample: Missing component references! Please assign all required components.");
            return;
        }

        // Configure auth manager
        authManager.SetProjectId(projectId);
        authManager.SetEntityInfo("game", entityName);

        // Link auth UI to manager
        authUI.SetAuthManager(authManager);
        authUI.SetMessagePanelUI(bugReportUI.MessagePanelUI);

        // Link bug report UI to auth manager
        bugReportUI.SetDeviceAuthManager(authManager);

        Debug.Log("Device authentication setup complete. Press T to test authentication, Y to show status.");
    }

    private void ToggleAuthUI()
    {
        if (authManager == null) return;

        // This simulates clicking the auth button
        authManager.StartDeviceAuth();
    }

    private void ShowAuthStatus()
    {
        if (authManager == null) return;

        string status = $"Auth State: {authManager.CurrentState}";
        if (authManager.IsAuthenticated())
        {
            status += $"\nUser: {authManager.UserDisplayName}";
            status += $"\nJWT Available: {!string.IsNullOrEmpty(authManager.JwtToken)}";
        }

        Debug.Log(status);

        // Also show in UI if available
        if (bugReportUI != null && bugReportUI.MessagePanelUI != null)
        {
            bugReportUI.MessagePanelUI.ShowMessagePanel("Authentication Status", status);
        }
    }

    #if UNITY_EDITOR
    void OnValidate()
    {
        // Auto-find components if not assigned
        if (authManager == null)
            authManager = FindObjectOfType<DeviceAuthManager>();
        
        if (authUI == null)
            authUI = FindObjectOfType<DeviceAuthUI>();
            
        if (bugReportUI == null)
            bugReportUI = FindObjectOfType<BugReportUI>();
    }
    #endif
}