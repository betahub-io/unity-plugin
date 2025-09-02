using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace BetaHub
{
    [System.Serializable]
    public struct CustomFieldRequirement
    {
        public string name;
        public string type;
        public string description;
        
        public CustomFieldRequirement(string name, string type, string description)
        {
            this.name = name;
            this.type = type;
            this.description = description;
        }
    }

    [System.Serializable]
    public class CustomIssueField
    {
        public string name;
        public string field_type;
        public bool tester_settable;
    }

    [System.Serializable]
    public class CustomFieldsResponse
    {
        public CustomIssueField[] custom_issue_fields;
    }

    public class CustomFieldValidator : MonoBehaviour
    {
        [Tooltip("Timeout for custom field validation requests in seconds")]
        public float TimeoutSeconds = 5.0f;

        public void ValidateCustomFields(string betahubEndpoint, string projectId, string authToken, List<CustomFieldRequirement> requiredFields)
        {
            if (requiredFields == null || requiredFields.Count == 0)
                return;

            StartCoroutine(ValidateCustomFieldsCoroutine(betahubEndpoint, projectId, authToken, requiredFields));
        }

        private IEnumerator ValidateCustomFieldsCoroutine(string betahubEndpoint, string projectId, string authToken, List<CustomFieldRequirement> requiredFields)
        {
            if (string.IsNullOrEmpty(betahubEndpoint) || string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(authToken))
            {
                Debug.LogWarning("CustomFieldValidator: Cannot validate fields - missing endpoint, project ID, or auth token");
                yield break;
            }

            string endpoint = betahubEndpoint;
            if (!endpoint.EndsWith("/"))
                endpoint += "/";

            string url = $"{endpoint}projects/{projectId}/custom_issue_fields.json";

            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                www.timeout = Mathf.RoundToInt(TimeoutSeconds);
                www.SetRequestHeader("Authorization", "FormUser " + authToken);
                www.SetRequestHeader("BetaHub-Project-ID", projectId);
                www.SetRequestHeader("Accept", "application/json");

                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"CustomFieldValidator: Failed to fetch custom fields - {www.error} (Code: {www.responseCode})");
                    yield break;
                }

                try
                {
                    string responseText = www.downloadHandler.text;
                    CustomFieldsResponse response = JsonUtility.FromJson<CustomFieldsResponse>(responseText);

                    if (response?.custom_issue_fields == null)
                    {
                        Debug.LogError("CustomFieldValidator: Invalid response format from custom fields API");
                        yield break;
                    }

                    ValidateFields(response.custom_issue_fields, requiredFields, endpoint, projectId);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"CustomFieldValidator: Error parsing custom fields response - {ex.Message}");
                }
            }
        }

        private void ValidateFields(CustomIssueField[] existingFields, List<CustomFieldRequirement> requiredFields, string betahubEndpoint, string projectId)
        {
            Dictionary<string, CustomIssueField> existingFieldsDict = new Dictionary<string, CustomIssueField>();
            
            foreach (var field in existingFields)
            {
                if (!string.IsNullOrEmpty(field.name))
                {
                    existingFieldsDict[field.name] = field;
                }
            }

            string baseUrl = betahubEndpoint.TrimEnd('/') + $"/projects/{projectId}/custom_issue_fields";
            bool hasErrors = false;

            foreach (var requirement in requiredFields)
            {
                string requiredFieldName = requirement.name;
                string requiredFieldType = requirement.type;
                string fieldDescription = requirement.description;
                string fieldWithDescription = string.IsNullOrEmpty(fieldDescription) ? 
                    $"'{requiredFieldName}'" : 
                    $"'{requiredFieldName}' ({fieldDescription})";
                
                if (!existingFieldsDict.ContainsKey(requiredFieldName))
                {
                    Debug.LogError($"CustomFieldValidator: Custom field {fieldWithDescription} is missing. Please create it as type '{requiredFieldType}' with tester_settable. Visit: {baseUrl}");
                    hasErrors = true;
                }
                else
                {
                    CustomIssueField existingField = existingFieldsDict[requiredFieldName];
                    bool typeMatches = string.Equals(existingField.field_type, requiredFieldType, StringComparison.OrdinalIgnoreCase);
                    
                    if (!typeMatches)
                    {
                        Debug.LogError($"CustomFieldValidator: Custom field {fieldWithDescription} exists but is type '{existingField.field_type}' (expected: '{requiredFieldType}'). Please update it. Visit: {baseUrl}");
                        hasErrors = true;
                    }
                    else if (!existingField.tester_settable)
                    {
                        Debug.LogError($"CustomFieldValidator: Custom field {fieldWithDescription} exists but is not tester_settable. Please update it. Visit: {baseUrl}");
                        hasErrors = true;
                    }
                    else
                    {
#if BETAHUB_DEBUG
                        Debug.Log($"CustomFieldValidator: Custom field {fieldWithDescription} is correctly configured (type: {existingField.field_type}, tester_settable: true)");
#endif
                    }
                }
            }

            if (!hasErrors)
            {
#if BETAHUB_DEBUG
                Debug.Log("CustomFieldValidator: All required custom fields are properly configured");
#endif
            }
        }
    }
}