using System.Collections.Generic;
using UnityEngine;
using BetaHub;

public class CustomFieldValidatorTest : MonoBehaviour
{
    [Header("Test Settings")]
    public string testEndpoint = "http://localhost:3000";
    public string testProjectId = "pr-6790810205";
    public string testAuthToken = "tkn-71b57f1210c761a76bf49b5e164b4fd77e536747600357b96f4fbb06ab4d4d6a";

    void Start()
    {
        // Test the CustomFieldValidator directly
        var validator = gameObject.AddComponent<CustomFieldValidator>();
        
        Debug.Log("Note: In production, use [RequireComponent(typeof(CustomFieldValidator))] on your class " +
                  "instead of manually adding the component. This ensures Unity automatically adds it.");
        
        // Test with required fields that should exist
        var requiredFields = new List<CustomFieldRequirement> 
        { 
            new CustomFieldRequirement("test", "text", "test field for validation"),
            new CustomFieldRequirement("latency", "text", "network latency measurements to help identify network-related performance issues"), 
            new CustomFieldRequirement("country", "text", "reporter's country code for geographic bug distribution analysis"),
            new CustomFieldRequirement("asn", "text", "ISP/company name from reporter's IP address for network provider insights")
        };
        
        var fieldNames = new List<string>();
        foreach(var field in requiredFields)
        {
            fieldNames.Add($"{field.name}({field.type}) - {field.description}");
        }
        Debug.Log("Testing CustomFieldValidator with fields: " + string.Join(", ", fieldNames.ToArray()));
        
        validator.ValidateCustomFields(testEndpoint, testProjectId, testAuthToken, requiredFields);
    }
}