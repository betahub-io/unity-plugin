using System.Collections;
using UnityEngine;
using BetaHub;

public class LatencyProviderTest : MonoBehaviour
{
    private LatencyProvider latencyProvider;

    void Start()
    {
        latencyProvider = GetComponent<LatencyProvider>();
        if (latencyProvider == null)
        {
            latencyProvider = gameObject.AddComponent<LatencyProvider>();
        }
        
        // Add multiple test hosts
        latencyProvider.IcmpTargetHosts.Clear();
        latencyProvider.IcmpTargetHosts.Add("ping.betahub.io");
        latencyProvider.IcmpTargetHosts.Add("8.8.8.8");
        latencyProvider.IcmpTargetHosts.Add("1.1.1.1");
        
        // Test the new features
        latencyProvider.MinPingRequests = 5;
        latencyProvider.DelayBetweenRequests = 0.5f;
        latencyProvider.PingInterval = 10f; // 10 second intervals for continuous mode
        
        // Test both modes
        StartCoroutine(TestBothModes());
    }
    
    IEnumerator TestBothModes()
    {
        // Test OnBugReport Mode (default)
        Debug.Log("=== Testing OnBugReport Mode ===");
        latencyProvider.collectionMode = PingCollectionMode.OnBugReport;
        latencyProvider.StartLatencyTest();
        
        // Wait for test to complete
        while (!latencyProvider.HasLatencyData)
        {
            yield return new WaitForSeconds(0.5f);
        }
        
        var onBugReportData = latencyProvider.GetCachedLatencyData();
        if (onBugReportData != null)
        {
            Debug.Log("OnBugReport Mode Results:");
            Debug.Log(onBugReportData.GetFormattedLatency());
            Debug.Log("OnBugReport JSON:");
            Debug.Log(onBugReportData.ToJson());
        }
        else
        {
            Debug.LogWarning("OnBugReport Mode: No latency data available");
        }
        
        yield return new WaitForSeconds(3f);
        
        // Test Continuous Mode
        Debug.Log("=== Testing Continuous Mode ===");
        latencyProvider.ClearCache();
        latencyProvider.collectionMode = PingCollectionMode.Continuous;
        
        // Trigger continuous collection by restarting the component
        latencyProvider.enabled = false;
        yield return null;
        latencyProvider.enabled = true;
        
        // Wait for some background pings to accumulate
        Debug.Log("Waiting for continuous pings to accumulate...");
        yield return new WaitForSeconds(25f);
        
        // Test force missing pings
        Debug.Log("Testing force missing pings...");
        latencyProvider.StartLatencyTest(); // This triggers ForceMissingPings in continuous mode
        
        yield return new WaitForSeconds(5f);
        
        var continuousData = latencyProvider.GetCurrentLatencyData();
        if (continuousData != null)
        {
            Debug.Log("Continuous Mode Results:");
            Debug.Log(continuousData.GetFormattedLatency());
            Debug.Log("Continuous JSON:");
            Debug.Log(continuousData.ToJson());
        }
        else
        {
            Debug.LogWarning("Continuous Mode: No latency data available");
        }
        
        // Continue logging continuous results periodically
        InvokeRepeating(nameof(LogContinuousResults), 15f, 15f);
    }
    
    private void LogContinuousResults()
    {
        if (latencyProvider.collectionMode == PingCollectionMode.Continuous)
        {
            var data = latencyProvider.GetCurrentLatencyData();
            if (data != null && data.HasSuccessfulResults())
            {
                Debug.Log($"Continuous Update: {data.GetFormattedLatency()}");
            }
            else
            {
                Debug.Log("Continuous Update: No successful results yet");
            }
        }
    }
    
    void Update()
    {
        // Test controls for manual testing
        if (Input.GetKeyDown(KeyCode.T))
        {
            Debug.Log("=== Manual Test Triggered ===");
            latencyProvider.StartLatencyTest();
        }
        
        if (Input.GetKeyDown(KeyCode.C))
        {
            Debug.Log("=== Cache Cleared ===");
            latencyProvider.ClearCache();
        }
    }
    
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 400, 150));
        
        GUILayout.Label("LatencyProvider Test Controls:");
        GUILayout.Label("T - Trigger manual latency test");
        GUILayout.Label("C - Clear cache");
        
        GUILayout.Space(10);
        
        if (latencyProvider != null)
        {
            GUILayout.Label($"Mode: {latencyProvider.collectionMode}");
            GUILayout.Label($"Min Ping Requests: {latencyProvider.MinPingRequests}");
            GUILayout.Label($"Ping Interval: {latencyProvider.PingInterval}s");
            GUILayout.Label($"Has Cached Data: {latencyProvider.HasLatencyData}");
            
            var currentData = latencyProvider.GetCurrentLatencyData();
            if (currentData != null && currentData.HasSuccessfulResults())
            {
                GUILayout.Label("Current Results:");
                string[] lines = currentData.GetFormattedLatency().Split('\n');
                foreach (string line in lines)
                {
                    if (!string.IsNullOrEmpty(line))
                        GUILayout.Label($"  {line}");
                }
            }
        }
        
        GUILayout.EndArea();
    }
}