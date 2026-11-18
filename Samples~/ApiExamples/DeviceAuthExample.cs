using BetaHub;
using UnityEngine;

/// <summary>
/// Lets players sign in to BetaHub through their browser, so their reports carry their BetaHub
/// account instead of arriving anonymously, and the form names them ("Reporting as ...").
///
/// Setup: put the DeviceAuthCanvas prefab (Runtime/Prefabs) in the scene next to the BugReportWidget,
/// add this component anywhere and assign both fields. The canvas has its own Sign In button; call
/// <see cref="SignIn"/> and <see cref="SignOut"/> from your menus instead if you prefer.
/// </summary>
public class DeviceAuthExample : MonoBehaviour
{
    [Tooltip("The BetaHubReporter on the BugReportWidget.")]
    [SerializeField] private BetaHubReporter reporter;

    [Tooltip("The DeviceAuthManager on the DeviceAuthCanvas.")]
    [SerializeField] private DeviceAuthManager authManager;

    [Tooltip("Shown to the player on BetaHub's sign-in page.")]
    [SerializeField] private string gameName = "My Game";

    void Start()
    {
        if (reporter == null || authManager == null)
        {
            Debug.LogError("DeviceAuthExample: assign the BetaHubReporter and the DeviceAuthManager.");
            enabled = false;
            return;
        }

        // the sign-in is for the same project and server the reports go to
        authManager.SetEndpoint(reporter.SubmitEndpoint);
        authManager.SetProjectId(reporter.ProjectID);
        authManager.SetEntityInfo("game", gameName);
        reporter.SetDeviceAuthManager(authManager);
    }

    /// <summary>Opens BetaHub's sign-in page in the browser.</summary>
    public void SignIn() => authManager.StartDeviceAuth();

    public void SignOut() => authManager.SignOut();

#if UNITY_EDITOR
    void OnValidate()
    {
        if (reporter == null) reporter = FindObjectOfType<BetaHubReporter>();
        if (authManager == null) authManager = FindObjectOfType<DeviceAuthManager>();
    }
#endif
}
