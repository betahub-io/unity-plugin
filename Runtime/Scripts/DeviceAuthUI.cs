using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

namespace BetaHub
{
    public class DeviceAuthUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Button authButton;
        [SerializeField] private TMP_Text authButtonText;
        [SerializeField] private TMP_Text userInfoText;
        [SerializeField] private GameObject statusPanel;
        [SerializeField] private TMP_Text statusText;
        
        [Header("Dependencies")]
        [SerializeField] private DeviceAuthManager authManager;
        [SerializeField] private MessagePanelUI messagePanelUI;
        
        [Header("Button Text Settings")]
        [SerializeField] private string signInText = "Sign In";
        [SerializeField] private string cancelText = "Cancel";
        [SerializeField] private string signOutText = "Sign Out";
        
        [Header("Status Messages")]
        [SerializeField] private string browserOpenMessage = "Browser opened. Please complete sign-in in your web browser and return to the game.";
        [SerializeField] private string waitingMessage = "Waiting for authentication...";

        void Start()
        {
            SetupUI();
            
            if (authManager != null)
            {
                authManager.OnAuthStateChanged += OnAuthStateChanged;
                authManager.OnAuthError += OnAuthError;
                
                OnAuthStateChanged(authManager.CurrentState);
            }
            else
            {
                Debug.LogError("DeviceAuthUI: AuthManager reference is missing!");
            }
        }

        void OnDestroy()
        {
            if (authManager != null)
            {
                authManager.OnAuthStateChanged -= OnAuthStateChanged;
                authManager.OnAuthError -= OnAuthError;
            }
        }

        private void SetupUI()
        {
            if (authButton != null)
            {
                authButton.onClick.AddListener(OnAuthButtonClicked);
            }
            else
            {
                Debug.LogError("DeviceAuthUI: Auth button reference is missing!");
            }

            if (statusPanel != null)
            {
                statusPanel.SetActive(false);
            }
        }

        private void OnAuthButtonClicked()
        {
            if (authManager == null) return;

            switch (authManager.CurrentState)
            {
                case DeviceAuthState.SignedOut:
                    authManager.StartDeviceAuth();
                    break;
                
                case DeviceAuthState.SigningIn:
                    authManager.CancelAuth();
                    break;
                
                case DeviceAuthState.SignedIn:
                    authManager.SignOut();
                    break;
            }
        }

        private void OnAuthStateChanged(DeviceAuthState newState)
        {
            UpdateUI(newState);
        }

        private void OnAuthError(string error)
        {
            if (messagePanelUI != null)
            {
                messagePanelUI.ShowMessagePanel("Authentication Error", error);
            }
            else
            {
                Debug.LogError($"Auth Error: {error}");
            }
        }

        private void UpdateUI(DeviceAuthState state)
        {
            switch (state)
            {
                case DeviceAuthState.SignedOut:
                    UpdateButtonText(signInText);
                    UpdateUserInfoText("");
                    HideStatusPanel();
                    break;
                
                case DeviceAuthState.SigningIn:
                    UpdateButtonText(cancelText);
                    UpdateUserInfoText("");
                    ShowStatusPanel(browserOpenMessage);
                    break;
                
                case DeviceAuthState.SignedIn:
                    string userDisplayName = authManager.UserDisplayName ?? "Unknown User";
                    UpdateButtonText(signOutText);
                    UpdateUserInfoText($"Signed in as {userDisplayName}");
                    HideStatusPanel();
                    break;
            }
        }

        private void UpdateButtonText(string text)
        {
            if (authButtonText != null)
            {
                authButtonText.text = text;
            }
        }

        private void UpdateUserInfoText(string text)
        {
            if (userInfoText != null)
            {
                userInfoText.text = text;
                userInfoText.gameObject.SetActive(!string.IsNullOrEmpty(text));
            }
        }

        private void ShowStatusPanel(string message)
        {
            if (statusPanel != null)
            {
                statusPanel.SetActive(true);
            }
            
            if (statusText != null)
            {
                statusText.text = message;
            }
        }

        private void HideStatusPanel()
        {
            if (statusPanel != null)
            {
                statusPanel.SetActive(false);
            }
        }


        public void SetAuthManager(DeviceAuthManager manager)
        {
            if (authManager != null)
            {
                authManager.OnAuthStateChanged -= OnAuthStateChanged;
                authManager.OnAuthError -= OnAuthError;
            }

            authManager = manager;

            if (authManager != null)
            {
                authManager.OnAuthStateChanged += OnAuthStateChanged;
                authManager.OnAuthError += OnAuthError;
                OnAuthStateChanged(authManager.CurrentState);
            }
        }

        public void SetMessagePanelUI(MessagePanelUI messagePanelUI)
        {
            this.messagePanelUI = messagePanelUI;
        }

        #if UNITY_EDITOR
        void OnValidate()
        {
            if (authButton != null && authButtonText == null)
            {
                authButtonText = authButton.GetComponentInChildren<TMP_Text>();
            }
            
            if (userInfoText == null)
            {
                userInfoText = GetComponentInChildren<TMP_Text>();
            }
        }
        #endif
    }
}