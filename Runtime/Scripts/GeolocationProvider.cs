using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace BetaHub
{
    [System.Serializable]
    public class GeolocationData
    {
        public string country;
        public string asn;
        
        public string ToJson()
        {
            return JsonUtility.ToJson(this);
        }
    }
    
    public class GeolocationProvider : MonoBehaviour
    {
        [Tooltip("Enable geolocation data collection for bug reports")]
        public bool EnableGeolocation = true;
        
        [Tooltip("Enable ISP/ASN data collection for bug reports")]
        public bool EnableAsnCollection = true;
        
        public List<CustomFieldRequirement> RequiredCustomFields
        {
            get
            {
                var fields = new List<CustomFieldRequirement>();
                if (EnableGeolocation) fields.Add(new CustomFieldRequirement("country", "text", "reporter's country code for geographic bug distribution analysis"));
                if (EnableAsnCollection) fields.Add(new CustomFieldRequirement("asn", "text", "ISP/company name from reporter's IP address for network provider insights"));
                return fields;
            }
        }
        
        [Tooltip("Geolocation API endpoint URL")]
        public string GeolocationEndpoint = "https://ping.betahub.io/ping.txt";
        
        [Tooltip("Timeout for geolocation requests in seconds")]
        public float TimeoutSeconds = 5.0f;
        
        private GeolocationData _cachedLocationData;
        private bool _hasCachedData = false;
        private bool _isRequestInProgress = false;
        
        public bool HasLocationData => _hasCachedData && _cachedLocationData != null;
        
        public GeolocationData GetCachedLocationData()
        {
            return _hasCachedData ? _cachedLocationData : null;
        }
        
        public IEnumerator GetLocationDataAsync(Action<GeolocationData> onSuccess, Action<string> onError = null)
        {
            if (!EnableGeolocation && !EnableAsnCollection)
            {
                onError?.Invoke("Both geolocation and ASN collection are disabled");
                yield break;
            }
            
            if (_hasCachedData)
            {
                onSuccess?.Invoke(_cachedLocationData);
                yield break;
            }
            
            if (_isRequestInProgress)
            {
                while (_isRequestInProgress)
                {
                    yield return null;
                }
                
                if (_hasCachedData)
                {
                    onSuccess?.Invoke(_cachedLocationData);
                }
                else
                {
                    onError?.Invoke("Failed to get location data from concurrent request");
                }
                yield break;
            }
            
            _isRequestInProgress = true;
            
            using (UnityWebRequest www = UnityWebRequest.Get(GeolocationEndpoint))
            {
                www.timeout = Mathf.RoundToInt(TimeoutSeconds);
                www.SetRequestHeader("Cache-Control", "no-cache");
                
                yield return www.SendWebRequest();
                
                _isRequestInProgress = false;
                
                if (www.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var locationData = ExtractLocationFromHeaders(www);
                        bool hasCountry = !string.IsNullOrEmpty(locationData?.country);
                        bool hasAsn = !string.IsNullOrEmpty(locationData?.asn);
                        
                        if (locationData != null && (hasCountry || hasAsn))
                        {
                            _cachedLocationData = locationData;
                            _hasCachedData = true;
                            onSuccess?.Invoke(_cachedLocationData);
                        }
                        else
                        {
                            string error = "No location or ASN data found in response";
                            Debug.LogWarning($"GeolocationProvider: {error}");
                            onError?.Invoke(error);
                        }
                    }
                    catch (Exception ex)
                    {
                        string error = $"Failed to parse location data: {ex.Message}";
                        Debug.LogError($"GeolocationProvider: {error}");
                        onError?.Invoke(error);
                    }
                }
                else
                {
                    string error = $"Network error: {www.error} (Code: {www.responseCode})";
                    Debug.LogError($"GeolocationProvider: {error}");
                    onError?.Invoke(error);
                }
            }
        }
        
        private GeolocationData ExtractLocationFromHeaders(UnityWebRequest www)
        {
            var locationData = new GeolocationData();
            
            var responseHeaders = www.GetResponseHeaders();
            if (responseHeaders != null)
            {
                foreach (var header in responseHeaders)
                {
                    string key = header.Key.ToLowerInvariant();
                    string value = header.Value;
                    
                    switch (key)
                    {
                        case "cf-ipcountry":
                        case "cloudflare-country":
                        case "x-country":
                        case "x-country-code":
                        case "x-viewer-country":
                            if (!string.IsNullOrEmpty(value))
                                locationData.country = value.ToUpperInvariant();
                            break;
                        case "x-viewer-asn":
                            if (!string.IsNullOrEmpty(value))
                                locationData.asn = value;
                            break;
                    }
                }
            }
            
            if (string.IsNullOrEmpty(locationData.country) && string.IsNullOrEmpty(locationData.asn))
            {
                Debug.LogWarning("GeolocationProvider: No country or ASN information found in response headers. Available headers: " + 
                    (responseHeaders != null ? string.Join(", ", responseHeaders.Keys) : "none"));
            }
            
            return locationData;
        }
        
        public void ClearCache()
        {
            _cachedLocationData = null;
            _hasCachedData = false;
        }
        
        void Start()
        {
            // Try to auto-attach to BugReportUI on the same GameObject
            var bugReportUI = GetComponent<BugReportUI>();
            if (bugReportUI != null)
            {
                bugReportUI.SetGeolocationProvider(this);
                #if BETAHUB_DEBUG
                Debug.Log("GeolocationProvider: Successfully attached to BugReportUI on the same GameObject.");
                #endif
            }
            else
            {
                Debug.LogWarning("GeolocationProvider: No BugReportUI found on the same GameObject. " +
                    "Geolocation data will not be included in bug reports. " +
                    "Please attach BugReportUI to the same GameObject or manually call SetGeolocationProvider().");
            }
        }
        
#if UNITY_EDITOR
        void OnValidate()
        {
            // Provide editor-time validation feedback
            var bugReportUI = GetComponent<BugReportUI>();
            if (bugReportUI == null)
            {
                Debug.LogWarning("GeolocationProvider: For automatic setup, attach BugReportUI to the same GameObject. " +
                    "Otherwise, you'll need to manually call SetGeolocationProvider().", this);
            }
        }
#endif
    }
}