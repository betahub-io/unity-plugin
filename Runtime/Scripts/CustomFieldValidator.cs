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
            string fieldsUrl = betahubEndpoint.TrimEnd('/') + $"/projects/{projectId}/custom_issue_fields";
            foreach (var problem in FieldProblems(existingFields, requiredFields, fieldsUrl))
            {
                Debug.LogWarning(problem);
            }
        }

        /// <summary>
        /// What is wrong with the project's custom fields for these values, one message per field. A missing field
        /// is fine: BetaHub creates it (text, for developers) with the first report that carries it, and values
        /// arrive as issue[custom], which fills a field whether or not testers can set it. A field of another
        /// type may refuse the value.
        /// </summary>
        public static List<string> FieldProblems(CustomIssueField[] existingFields, IEnumerable<CustomFieldRequirement> requiredFields, string fieldsUrl)
        {
            var existing = new Dictionary<string, CustomIssueField>();
            foreach (var field in existingFields)
            {
                if (!string.IsNullOrEmpty(field.name)) existing[field.name] = field;
            }

            var problems = new List<string>();
            foreach (var requirement in requiredFields)
            {
                string label = string.IsNullOrEmpty(requirement.description) ? $"'{requirement.name}'" : $"'{requirement.name}' ({requirement.description})";
                if (existing.TryGetValue(requirement.name, out var field)
                    && !string.Equals(field.field_type, requirement.type, StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add($"BetaHub: custom field {label} is type '{field.field_type}', but the plugin sends {requirement.type}; its values may be refused. Make it a {requirement.type} field: {fieldsUrl}");
                }
            }
            return problems;
        }
    }
}