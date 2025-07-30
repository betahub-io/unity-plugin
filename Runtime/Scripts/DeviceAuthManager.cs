using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace BetaHub
{
    public enum DeviceAuthState
    {
        SignedOut,
        SigningIn,
        SignedIn
    }

    [System.Serializable]
    public class DeviceAuthCreateRequest
    {
        public string request_id;
        public string entity_kind;
        public string entity_name;
    }

    [System.Serializable]
    public class DeviceAuthCreateResponse
    {
        public string status;
        public string error;
    }

    [System.Serializable]
    public class DeviceAuthPollResponse
    {
        public string status;
        public string token;
        public string user_name;
        public string error;
    }

    public class DeviceAuthManager : MonoBehaviour
    {
        [Header("Authentication Settings")]
        [SerializeField] private string betahubEndpoint = "https://app.betahub.io";
        [SerializeField] private string projectId;
        [SerializeField] private string entityKind = "game";
        [SerializeField] private string entityName = "Unity Game";
        
        [Header("Polling Settings")]
        [SerializeField] private float pollInterval = 3f;
        [SerializeField] private float authTimeout = 300f; // 5 minutes
        
        public DeviceAuthState CurrentState { get; private set; } = DeviceAuthState.SignedOut;
        public string UserDisplayName { get; private set; }
        public string JwtToken { get; private set; }
        
        public event Action<DeviceAuthState> OnAuthStateChanged;
        public event Action<string> OnAuthError;
        
        private string currentRequestId;
        private Coroutine pollCoroutine;
        private float authStartTime;
        
        private const string JWT_TOKEN_KEY = "BetaHub_JWT_Token";
        private const string USER_NAME_KEY = "BetaHub_User_Name";
        private const string JWT_EXPIRY_KEY = "BetaHub_JWT_Expiry";

        void Start()
        {
            LoadStoredAuth();
        }

        void OnDestroy()
        {
            if (pollCoroutine != null)
            {
                StopCoroutine(pollCoroutine);
            }
        }

        public void StartDeviceAuth()
        {
            if (CurrentState == DeviceAuthState.SigningIn)
            {
                CancelAuth();
                return;
            }

            if (CurrentState == DeviceAuthState.SignedIn)
            {
                SignOut();
                return;
            }

            StartCoroutine(InitiateDeviceAuth());
        }

        public void CancelAuth()
        {
            if (CurrentState != DeviceAuthState.SigningIn) return;

            if (pollCoroutine != null)
            {
                StopCoroutine(pollCoroutine);
                pollCoroutine = null;
            }

            currentRequestId = null;
            SetAuthState(DeviceAuthState.SignedOut);
        }

        public void SignOut()
        {
            if (CurrentState == DeviceAuthState.SigningIn)
            {
                CancelAuth();
            }

            ClearStoredAuth();
            JwtToken = null;
            UserDisplayName = null;
            SetAuthState(DeviceAuthState.SignedOut);
        }

        private IEnumerator InitiateDeviceAuth()
        {
            SetAuthState(DeviceAuthState.SigningIn);
            authStartTime = Time.time;

            // Generate UUID for this authentication request
            currentRequestId = System.Guid.NewGuid().ToString();

            // Step 1: Create authentication request
            yield return StartCoroutine(CreateAuthRequest());
            
            // If creation failed, currentRequestId will be null
            if (string.IsNullOrEmpty(currentRequestId))
            {
                yield break;
            }

            // Step 2: Open authorization URL in browser
            string authUrl = $"{betahubEndpoint}/device_auth/{currentRequestId}/authorize";
            Application.OpenURL(authUrl);
            
            // Step 3: Start polling for authentication result
            pollCoroutine = StartCoroutine(PollForAuth());
        }

        private IEnumerator CreateAuthRequest()
        {
            if (string.IsNullOrEmpty(currentRequestId))
            {
                HandleAuthError("Invalid request ID");
                yield break;
            }

            string url = $"{betahubEndpoint}/device_auth/create";
            
            DeviceAuthCreateRequest request = new DeviceAuthCreateRequest
            {
                request_id = currentRequestId,
                entity_kind = entityKind,
                entity_name = entityName
            };

            string jsonPayload = JsonUtility.ToJson(request);
            
            using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
                www.uploadHandler = new UploadHandlerRaw(bodyRaw);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("Accept", "application/json");
                
                if (!string.IsNullOrEmpty(projectId))
                {
                    www.SetRequestHeader("BetaHub-Project-ID", projectId);
                }

                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    if (www.responseCode == 422)
                    {
                        try
                        {
                            DeviceAuthCreateResponse response = JsonUtility.FromJson<DeviceAuthCreateResponse>(www.downloadHandler.text);
                            HandleAuthError($"Validation error: {response.error ?? "Invalid request data"}");
                        }
                        catch
                        {
                            HandleAuthError("Validation error: Invalid request data");
                        }
                    }
                    else
                    {
                        HandleAuthError($"Failed to create auth request: {www.error}");
                    }
                    
                    currentRequestId = null;
                    yield break;
                }

                try
                {
                    DeviceAuthCreateResponse response = JsonUtility.FromJson<DeviceAuthCreateResponse>(www.downloadHandler.text);
                    if (response.status != "created")
                    {
                        HandleAuthError($"Unexpected response status: {response.status}");
                        currentRequestId = null;
                    }
                }
                catch (Exception e)
                {
                    HandleAuthError($"Failed to parse create response: {e.Message}");
                    currentRequestId = null;
                }
            }
        }

        private IEnumerator PollForAuth()
        {
            while (CurrentState == DeviceAuthState.SigningIn && currentRequestId != null)
            {
                if (Time.time - authStartTime > authTimeout)
                {
                    HandleAuthError("Authentication timed out");
                    yield break;
                }

                yield return new WaitForSeconds(pollInterval);
                yield return StartCoroutine(CheckAuthStatus());
            }
        }

        private IEnumerator CheckAuthStatus()
        {
            if (string.IsNullOrEmpty(currentRequestId)) yield break;

            string url = $"{betahubEndpoint}/device_auth/{currentRequestId}/poll";
            
            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                www.SetRequestHeader("BetaHub-Project-ID", projectId);
                www.SetRequestHeader("Accept", "application/json");
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    if (www.responseCode == 404)
                    {
                        yield break;
                    }
                    
                    HandleAuthError($"Polling failed: {www.error}");
                    yield break;
                }

                try
                {
                    DeviceAuthPollResponse response = JsonUtility.FromJson<DeviceAuthPollResponse>(www.downloadHandler.text);
                    
                    switch (response.status)
                    {
                        case "approved":
                            HandleAuthSuccess(response.token, response.user_name);
                            break;
                        case "expired":
                            HandleAuthError("Authentication request expired");
                            break;
                        case "not_found":
                            break;
                        default:
                            if (!string.IsNullOrEmpty(response.error))
                            {
                                HandleAuthError(response.error);
                            }
                            break;
                    }
                }
                catch (Exception e)
                {
                    HandleAuthError($"Failed to parse poll response: {e.Message}");
                }
            }
        }

        private void HandleAuthSuccess(string jwtToken, string userName)
        {
            JwtToken = jwtToken;
            UserDisplayName = userName;
            
            StoreAuth(jwtToken, userName);
            
            if (pollCoroutine != null)
            {
                StopCoroutine(pollCoroutine);
                pollCoroutine = null;
            }
            
            currentRequestId = null;
            SetAuthState(DeviceAuthState.SignedIn);
        }

        private void HandleAuthError(string error)
        {
            Debug.LogError($"Device auth error: {error}");
            OnAuthError?.Invoke(error);
            CancelAuth();
        }

        private void SetAuthState(DeviceAuthState newState)
        {
            if (CurrentState != newState)
            {
                CurrentState = newState;
                OnAuthStateChanged?.Invoke(newState);
            }
        }

        private void StoreAuth(string jwtToken, string userName)
        {
            PlayerPrefs.SetString(JWT_TOKEN_KEY, jwtToken);
            PlayerPrefs.SetString(USER_NAME_KEY, userName);
            PlayerPrefs.SetString(JWT_EXPIRY_KEY, DateTime.Now.AddHours(24).ToBinary().ToString());
            PlayerPrefs.Save();
        }

        private void LoadStoredAuth()
        {
            if (!PlayerPrefs.HasKey(JWT_TOKEN_KEY)) return;

            string expiryString = PlayerPrefs.GetString(JWT_EXPIRY_KEY, "");
            if (!string.IsNullOrEmpty(expiryString))
            {
                try
                {
                    DateTime expiry = DateTime.FromBinary(Convert.ToInt64(expiryString));
                    if (DateTime.Now > expiry)
                    {
                        ClearStoredAuth();
                        return;
                    }
                }
                catch
                {
                    ClearStoredAuth();
                    return;
                }
            }

            JwtToken = PlayerPrefs.GetString(JWT_TOKEN_KEY);
            UserDisplayName = PlayerPrefs.GetString(USER_NAME_KEY);
            
            if (!string.IsNullOrEmpty(JwtToken))
            {
                SetAuthState(DeviceAuthState.SignedIn);
            }
        }

        private void ClearStoredAuth()
        {
            PlayerPrefs.DeleteKey(JWT_TOKEN_KEY);
            PlayerPrefs.DeleteKey(USER_NAME_KEY);
            PlayerPrefs.DeleteKey(JWT_EXPIRY_KEY);
            PlayerPrefs.Save();
        }

        public void SetProjectId(string newProjectId)
        {
            projectId = newProjectId;
        }

        public void SetEndpoint(string newEndpoint)
        {
            betahubEndpoint = newEndpoint;
        }

        public void SetEntityInfo(string kind, string name)
        {
            entityKind = kind;
            entityName = name;
        }

        public bool IsAuthenticated()
        {
            return CurrentState == DeviceAuthState.SignedIn && !string.IsNullOrEmpty(JwtToken);
        }

        public string GetAuthHeaderValue()
        {
            if (!IsAuthenticated()) return null;
            return $"Bearer {JwtToken}";
        }
    }
}