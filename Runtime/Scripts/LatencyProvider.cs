using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace BetaHub
{
    public enum PingMethod
    {
        ICMP,  // Unity's built-in Ping class (default)
        HTTP   // UnityWebRequest method
    }
    
    public enum PingCollectionMode
    {
        OnBugReport,   // Collect pings only when bug report window is displayed (default)
        Continuous     // Continuously collect pings in the background
    }
    
    [System.Serializable]
    public class HostLatencyData
    {
        public float minLatency;
        public float maxLatency;
        public float avgLatency;
        public int totalRequests;
        public int successfulRequests;
        public int lostRequests;
        
        public string GetFormattedLatency()
        {
            if (successfulRequests == 0)
                return "latency unavailable";
                
            string result = $"min/avg/max = {minLatency:F0}ms/{avgLatency:F0}ms/{maxLatency:F0}ms ({successfulRequests}/{totalRequests} requests)";
            
            if (lostRequests > 0)
            {
                float lossPercentage = (float)lostRequests / totalRequests * 100f;
                result += $", Lost: {lostRequests}/{totalRequests} ({lossPercentage:F1}%)";
            }
            
            return result;
        }
    }
    
    [System.Serializable]
    public class LatencyData
    {
        public Dictionary<string, HostLatencyData> hostResults = new Dictionary<string, HostLatencyData>();
        
        public bool HasSuccessfulResults()
        {
            return hostResults.Values.Any(hostData => hostData.successfulRequests > 0);
        }
        
        public string GetFormattedLatency()
        {
            if (hostResults.Count == 0)
                return "latency unavailable";
            
            var lines = new List<string>();
            foreach (var kvp in hostResults)
            {
                string hostName = kvp.Key;
                HostLatencyData hostData = kvp.Value;
                lines.Add($"{hostName}: {hostData.GetFormattedLatency()}");
            }
            
            return string.Join("\n", lines);
        }
        
        public string ToJson()
        {
            return $"{{\"latency\":\"{GetFormattedLatency().Replace("\"", "\\\"")}\"}}";
        }
    }
    
    public class LatencyProvider : MonoBehaviour
    {
        private const int MAX_STORED_PINGS = 256;
        
        [Tooltip("Enable latency measurement for bug reports")]
        public bool EnableLatency = true;
        
        public List<CustomFieldRequirement> RequiredCustomFields => new List<CustomFieldRequirement> { 
            new CustomFieldRequirement("latency", "text", "network latency measurements to help identify network-related performance issues")
        };
        
        [Tooltip("Ping collection mode")]
        public PingCollectionMode collectionMode = PingCollectionMode.OnBugReport;
        
        [Tooltip("Method to use for latency measurement")]
        public PingMethod pingMethod = PingMethod.ICMP;
        
        [Tooltip("Target hosts for ICMP ping (IP or domain)")]
        public List<string> IcmpTargetHosts = new List<string> { "ping.betahub.io" };
        
        [Tooltip("HTTP endpoint for fallback/HTTP mode")]
        public string HttpEndpoint = "https://ping.betahub.io/ping.txt";
        
        [Tooltip("Minimum number of ping requests to perform (was MaxPingRequests)")]
        [Range(1, 20)]
        public int MinPingRequests = 10;
        
        [Tooltip("Timeout for each ping request in seconds")]
        public float TimeoutSeconds = 5.0f;
        
        [Tooltip("Delay between ping requests in seconds")]
        public float DelayBetweenRequests = 0.1f;
        
        [Tooltip("Interval between background pings in continuous mode (seconds)")]
        public float PingInterval = 15.0f;
        
        private class CircularPingBuffer
        {
            private float[] buffer = new float[MAX_STORED_PINGS];
            private bool[] validFlags = new bool[MAX_STORED_PINGS];
            private int writeIndex = 0;
            private int count = 0;
            
            public void Add(float latency)
            {
                buffer[writeIndex] = latency;
                validFlags[writeIndex] = true;
                writeIndex = (writeIndex + 1) % MAX_STORED_PINGS;
                
                if (count < MAX_STORED_PINGS)
                    count++;
            }
            
            public float[] GetValidPings()
            {
                if (count == 0)
                    return new float[0];
                
                float[] result = new float[count];
                int resultIndex = 0;
                
                if (count < MAX_STORED_PINGS)
                {
                    for (int i = 0; i < writeIndex; i++)
                    {
                        if (validFlags[i])
                            result[resultIndex++] = buffer[i];
                    }
                }
                else
                {
                    for (int i = 0; i < MAX_STORED_PINGS; i++)
                    {
                        int actualIndex = (writeIndex + i) % MAX_STORED_PINGS;
                        if (validFlags[actualIndex])
                            result[resultIndex++] = buffer[actualIndex];
                    }
                }
                
                if (resultIndex != count)
                {
                    float[] trimmed = new float[resultIndex];
                    System.Array.Copy(result, trimmed, resultIndex);
                    return trimmed;
                }
                
                return result;
            }
            
            public int GetCount()
            {
                return count;
            }
            
            public void Clear()
            {
                count = 0;
                writeIndex = 0;
                System.Array.Clear(validFlags, 0, validFlags.Length);
            }
        }
        
        private LatencyData _cachedLatencyData;
        private bool _hasCachedData = false;
        private bool _isTestingInProgress = false;
        private Coroutine _currentLatencyTest;
        private Coroutine _backgroundPingCoroutine;
        
        // For on-demand collection
        private Dictionary<string, List<float>> _hostLatencyResults = new Dictionary<string, List<float>>();
        private Dictionary<string, int> _hostConsecutiveFailures = new Dictionary<string, int>();
        private bool _hasUsedFallback = false;
        
        // For continuous collection
        private Dictionary<string, CircularPingBuffer> _hostPingBuffers = new Dictionary<string, CircularPingBuffer>();
        private Dictionary<string, int> _hostTotalAttempts = new Dictionary<string, int>();
        private Dictionary<string, int> _hostLostPings = new Dictionary<string, int>();
        
        public bool HasLatencyData => _hasCachedData && _cachedLatencyData != null;
        
        public LatencyData GetCachedLatencyData()
        {
            return _hasCachedData ? _cachedLatencyData : null;
        }
        
        public void StartLatencyTest()
        {
            if (!EnableLatency)
            {
                Debug.LogWarning("LatencyProvider: Latency testing is disabled.");
                return;
            }
            
            if (collectionMode == PingCollectionMode.Continuous)
            {
                // In continuous mode, check if we need to force additional pings
                StartCoroutine(ForceMissingPings());
                return;
            }
            
            if (_isTestingInProgress)
            {
                Debug.Log("LatencyProvider: Latency test already in progress.");
                return;
            }
            
            // Stop any existing test and start a new one
            if (_currentLatencyTest != null)
            {
                StopCoroutine(_currentLatencyTest);
            }
            
            _currentLatencyTest = StartCoroutine(PerformLatencyTest());
        }
        
        public LatencyData GetCurrentLatencyData()
        {
            if (collectionMode == PingCollectionMode.Continuous)
            {
                // For continuous mode, always return fresh data from buffers
                return CalculateLatencyDataFromBuffers();
            }
            
            if (_isTestingInProgress && _hostLatencyResults.Count > 0)
            {
                // Return current results even if test is not complete
                return CalculateLatencyData(_hostLatencyResults, MinPingRequests);
            }
            
            return GetCachedLatencyData();
        }
        
        public void StopLatencyTest()
        {
            if (_currentLatencyTest != null)
            {
                StopCoroutine(_currentLatencyTest);
                _currentLatencyTest = null;
            }
            
            if (_isTestingInProgress && _hostLatencyResults.Count > 0)
            {
                // Finalize results with whatever we have
                _cachedLatencyData = CalculateLatencyData(_hostLatencyResults, MinPingRequests);
                _hasCachedData = true;
                int totalResults = _hostLatencyResults.Values.Sum(list => list.Count);
                Debug.Log($"LatencyProvider: Test stopped early with {totalResults} total results - {_cachedLatencyData.GetFormattedLatency()}");
            }
            
            _isTestingInProgress = false;
        }
        
        private IEnumerator PerformICMPPing(string host, int requestNumber)
        {
            Ping ping = new Ping(host);
            float timeoutTime = Time.realtimeSinceStartup + TimeoutSeconds;
            
            // Wait for ping completion with timeout
            while (!ping.isDone && Time.realtimeSinceStartup < timeoutTime)
            {
                yield return null;
            }
            
            if (ping.isDone && ping.time >= 0)
            {
                if (!_hostLatencyResults.ContainsKey(host))
                    _hostLatencyResults[host] = new List<float>();
                
                _hostLatencyResults[host].Add(ping.time);
                _hostConsecutiveFailures[host] = 0; // Reset failure counter on success
                Debug.Log($"LatencyProvider: ICMP Ping to {host} {requestNumber}/{MinPingRequests} - {ping.time}ms");
            }
            else
            {
                if (!_hostConsecutiveFailures.ContainsKey(host))
                    _hostConsecutiveFailures[host] = 0;
                    
                _hostConsecutiveFailures[host]++;
                Debug.LogWarning($"LatencyProvider: ICMP Ping to {host} {requestNumber}/{MinPingRequests} failed or timed out (failures: {_hostConsecutiveFailures[host]})");
            }
            
            ping.DestroyPing();
        }
        
        private IEnumerator PerformHttpPing(int requestNumber)
        {
            string host = HttpEndpoint;
            float startTime = Time.realtimeSinceStartup;
            
            using (UnityWebRequest www = UnityWebRequest.Get(HttpEndpoint))
            {
                www.timeout = Mathf.RoundToInt(TimeoutSeconds);
                
                yield return www.SendWebRequest();
                
                float latency = (Time.realtimeSinceStartup - startTime) * 1000f; // Convert to milliseconds
                
                if (www.result == UnityWebRequest.Result.Success)
                {
                    if (!_hostLatencyResults.ContainsKey(host))
                        _hostLatencyResults[host] = new List<float>();
                    
                    _hostLatencyResults[host].Add(latency);
                    _hostConsecutiveFailures[host] = 0; // Reset failure counter on success
                    Debug.Log($"LatencyProvider: HTTP Ping to {host} {requestNumber}/{MinPingRequests} - {latency:F0}ms");
                }
                else
                {
                    if (!_hostConsecutiveFailures.ContainsKey(host))
                        _hostConsecutiveFailures[host] = 0;
                        
                    _hostConsecutiveFailures[host]++;
                    Debug.LogWarning($"LatencyProvider: HTTP Ping to {host} {requestNumber}/{MinPingRequests} failed - {www.error} (failures: {_hostConsecutiveFailures[host]})");
                }
            }
        }

        private IEnumerator PerformLatencyTest()
        {
            _isTestingInProgress = true;
            _hostLatencyResults.Clear();
            _hostConsecutiveFailures.Clear();
            _hasUsedFallback = false;
            
            PingMethod currentMethod = pingMethod;
            string methodName = currentMethod == PingMethod.ICMP ? "ICMP" : "HTTP";
            
            if (currentMethod == PingMethod.ICMP && (IcmpTargetHosts == null || IcmpTargetHosts.Count == 0))
            {
                Debug.LogWarning("LatencyProvider: No ICMP target hosts configured, falling back to HTTP");
                currentMethod = PingMethod.HTTP;
                _hasUsedFallback = true;
            }
            
            Debug.Log($"LatencyProvider: Starting {methodName} latency test with up to {MinPingRequests} requests...");
            
            for (int i = 0; i < MinPingRequests && _isTestingInProgress; i++)
            {
                List<Coroutine> pingCoroutines = new List<Coroutine>();
                
                // Check for fallback if using ICMP and all hosts have too many failures
                if (currentMethod == PingMethod.ICMP && !_hasUsedFallback)
                {
                    bool shouldFallback = true;
                    foreach (string host in IcmpTargetHosts)
                    {
                        if (!_hostConsecutiveFailures.ContainsKey(host) || _hostConsecutiveFailures[host] < 3)
                        {
                            shouldFallback = false;
                            break;
                        }
                    }
                    
                    if (shouldFallback)
                    {
                        _hasUsedFallback = true;
                        currentMethod = PingMethod.HTTP;
                        Debug.LogWarning($"LatencyProvider: All ICMP hosts failing consistently, falling back to HTTP for remaining requests");
                    }
                }
                
                // Use the appropriate ping method
                if (currentMethod == PingMethod.ICMP)
                {
                    // Ping all ICMP hosts in parallel
                    foreach (string host in IcmpTargetHosts)
                    {
                        if (!string.IsNullOrEmpty(host))
                        {
                            pingCoroutines.Add(StartCoroutine(PerformICMPPing(host, i + 1)));
                        }
                    }
                }
                else
                {
                    // Ping HTTP endpoint
                    pingCoroutines.Add(StartCoroutine(PerformHttpPing(i + 1)));
                }
                
                // Wait for all pings to complete
                foreach (var coroutine in pingCoroutines)
                {
                    yield return coroutine;
                }
                
                // Add delay between requests (except for the last one)
                if (i < MinPingRequests - 1 && _isTestingInProgress && DelayBetweenRequests > 0)
                {
                    yield return new WaitForSeconds(DelayBetweenRequests);
                }
            }
            
            // Finalize results
            if (_hostLatencyResults.Count > 0 && _hostLatencyResults.Values.Any(list => list.Count > 0))
            {
                _cachedLatencyData = CalculateLatencyData(_hostLatencyResults, MinPingRequests);
                _hasCachedData = true;
                Debug.Log($"LatencyProvider: Test completed - {_cachedLatencyData.GetFormattedLatency()}");
            }
            else
            {
                _cachedLatencyData = new LatencyData();
                if (currentMethod == PingMethod.ICMP && IcmpTargetHosts != null)
                {
                    foreach (string host in IcmpTargetHosts)
                    {
                        if (!string.IsNullOrEmpty(host))
                        {
                            _cachedLatencyData.hostResults[host] = new HostLatencyData
                            {
                                totalRequests = MinPingRequests,
                                successfulRequests = 0
                            };
                        }
                    }
                }
                else
                {
                    _cachedLatencyData.hostResults[HttpEndpoint] = new HostLatencyData
                    {
                        totalRequests = MinPingRequests,
                        successfulRequests = 0
                    };
                }
                _hasCachedData = true;
                Debug.LogWarning("LatencyProvider: All ping requests failed.");
            }
            
            _isTestingInProgress = false;
            _currentLatencyTest = null;
        }
        
        private LatencyData CalculateLatencyData(Dictionary<string, List<float>> hostResults, int totalRequests)
        {
            var data = new LatencyData();
            
            foreach (var kvp in hostResults)
            {
                string host = kvp.Key;
                List<float> results = kvp.Value;
                
                var hostData = new HostLatencyData
                {
                    totalRequests = totalRequests,
                    successfulRequests = results.Count
                };
                
                if (results.Count > 0)
                {
                    hostData.minLatency = results.Min();
                    hostData.maxLatency = results.Max();
                    hostData.avgLatency = results.Average();
                }
                
                data.hostResults[host] = hostData;
            }
            
            return data;
        }
        
        public void ClearCache()
        {
            _cachedLatencyData = null;
            _hasCachedData = false;
            _hostLatencyResults.Clear();
            _hostConsecutiveFailures.Clear();
            _hasUsedFallback = false;
            
            // Clear continuous collection data
            foreach (var buffer in _hostPingBuffers.Values)
            {
                buffer.Clear();
            }
            _hostTotalAttempts.Clear();
            _hostLostPings.Clear();
        }
        
        void Start()
        {
            // Try to auto-attach to BugReportUI on the same GameObject
            var bugReportUI = GetComponent<BugReportUI>();
            if (bugReportUI != null)
            {
                bugReportUI.SetLatencyProvider(this);
                #if BETAHUB_DEBUG
                Debug.Log("LatencyProvider: Successfully attached to BugReportUI on the same GameObject.");
                #endif
            }
            else
            {
                Debug.LogWarning("LatencyProvider: No BugReportUI found on the same GameObject. " +
                    "Latency data will not be included in bug reports. " +
                    "Please attach BugReportUI to the same GameObject or manually call SetLatencyProvider().");
            }
            
            // Start continuous ping collection if enabled
            if (EnableLatency && collectionMode == PingCollectionMode.Continuous)
            {
                InitializeContinuousCollection();
                _backgroundPingCoroutine = StartCoroutine(BackgroundPingCollection());
                Debug.Log("LatencyProvider: Started continuous ping collection mode.");
            }
        }
        
        void OnDestroy()
        {
            StopLatencyTest();
            
            if (_backgroundPingCoroutine != null)
            {
                StopCoroutine(_backgroundPingCoroutine);
                _backgroundPingCoroutine = null;
            }
        }
        
        private void InitializeContinuousCollection()
        {
            _hostPingBuffers.Clear();
            _hostTotalAttempts.Clear();
            _hostLostPings.Clear();
            
            // Initialize buffers for all target hosts
            if (pingMethod == PingMethod.ICMP && IcmpTargetHosts != null)
            {
                foreach (string host in IcmpTargetHosts)
                {
                    if (!string.IsNullOrEmpty(host))
                    {
                        _hostPingBuffers[host] = new CircularPingBuffer();
                        _hostTotalAttempts[host] = 0;
                        _hostLostPings[host] = 0;
                    }
                }
            }
            else
            {
                string host = HttpEndpoint;
                _hostPingBuffers[host] = new CircularPingBuffer();
                _hostTotalAttempts[host] = 0;
                _hostLostPings[host] = 0;
            }
        }
        
        private IEnumerator BackgroundPingCollection()
        {
            while (EnableLatency && collectionMode == PingCollectionMode.Continuous)
            {
                yield return StartCoroutine(PerformBackgroundPing());
                
                if (PingInterval > 0)
                    yield return new WaitForSeconds(PingInterval);
                else
                    yield return new WaitForSeconds(15.0f); // Fallback interval
            }
        }
        
        private IEnumerator PerformBackgroundPing()
        {
            List<Coroutine> pingCoroutines = new List<Coroutine>();
            
            if (pingMethod == PingMethod.ICMP && IcmpTargetHosts != null && IcmpTargetHosts.Count > 0)
            {
                foreach (string host in IcmpTargetHosts)
                {
                    if (!string.IsNullOrEmpty(host))
                    {
                        pingCoroutines.Add(StartCoroutine(PerformBackgroundICMPPing(host)));
                    }
                }
            }
            else
            {
                pingCoroutines.Add(StartCoroutine(PerformBackgroundHttpPing()));
            }
            
            foreach (var coroutine in pingCoroutines)
            {
                yield return coroutine;
            }
        }
        
        private IEnumerator PerformBackgroundICMPPing(string host)
        {
            if (!_hostPingBuffers.ContainsKey(host))
                _hostPingBuffers[host] = new CircularPingBuffer();
            
            if (!_hostTotalAttempts.ContainsKey(host))
                _hostTotalAttempts[host] = 0;
            
            if (!_hostLostPings.ContainsKey(host))
                _hostLostPings[host] = 0;
            
            _hostTotalAttempts[host]++;
            
            Ping ping = new Ping(host);
            float timeoutTime = Time.realtimeSinceStartup + TimeoutSeconds;
            
            while (!ping.isDone && Time.realtimeSinceStartup < timeoutTime)
            {
                yield return null;
            }
            
            if (ping.isDone && ping.time >= 0)
            {
#if BETAHUB_DEBUG
                Debug.Log($"LatencyProvider: ICMP Ping to {host} - {ping.time}ms");
#endif

                _hostPingBuffers[host].Add(ping.time);
            }
            else
            {
#if BETAHUB_DEBUG
                Debug.Log($"LatencyProvider: ICMP Ping to {host} failed - {ping.time}ms");
#endif

                _hostLostPings[host]++;
            }
            
            ping.DestroyPing();
        }
        
        private IEnumerator PerformBackgroundHttpPing()
        {
            string host = HttpEndpoint;
            
            if (!_hostPingBuffers.ContainsKey(host))
                _hostPingBuffers[host] = new CircularPingBuffer();
            
            if (!_hostTotalAttempts.ContainsKey(host))
                _hostTotalAttempts[host] = 0;
            
            if (!_hostLostPings.ContainsKey(host))
                _hostLostPings[host] = 0;
            
            _hostTotalAttempts[host]++;
            
            float startTime = Time.realtimeSinceStartup;
            
            using (UnityWebRequest www = UnityWebRequest.Get(HttpEndpoint))
            {
                www.timeout = Mathf.RoundToInt(TimeoutSeconds);
                
                yield return www.SendWebRequest();
                
                float latency = (Time.realtimeSinceStartup - startTime) * 1000f;
                
                if (www.result == UnityWebRequest.Result.Success)
                {
                    _hostPingBuffers[host].Add(latency);
                }
                else
                {
                    _hostLostPings[host]++;
                }
            }
        }
        
        private IEnumerator ForceMissingPings()
        {
            if (collectionMode != PingCollectionMode.Continuous)
                yield break;
            
            List<string> targetHosts = new List<string>();
            
            if (pingMethod == PingMethod.ICMP && IcmpTargetHosts != null)
            {
                foreach (string host in IcmpTargetHosts)
                {
                    if (!string.IsNullOrEmpty(host))
                    {
                        targetHosts.Add(host);
                    }
                }
            }
            else
            {
                targetHosts.Add(HttpEndpoint);
            }
            
            foreach (string host in targetHosts)
            {
                if (!_hostPingBuffers.ContainsKey(host))
                    continue;
                
                int currentPings = _hostPingBuffers[host].GetCount();
                int missingPings = MinPingRequests - currentPings;
                
                if (missingPings > 0)
                {
                    Debug.Log($"LatencyProvider: Forcing {missingPings} additional pings for {host}");
                    
                    for (int i = 0; i < missingPings; i++)
                    {
                        if (pingMethod == PingMethod.ICMP)
                        {
                            yield return StartCoroutine(PerformBackgroundICMPPing(host));
                        }
                        else
                        {
                            yield return StartCoroutine(PerformBackgroundHttpPing());
                        }
                        
                        if (DelayBetweenRequests > 0 && i < missingPings - 1)
                        {
                            yield return new WaitForSeconds(DelayBetweenRequests);
                        }
                    }
                }
            }
            
            // Update cached data with new results
            _cachedLatencyData = CalculateLatencyDataFromBuffers();
            _hasCachedData = true;
            
            Debug.Log($"LatencyProvider: Force collection completed - {_cachedLatencyData.GetFormattedLatency()}");
        }
        
        private LatencyData CalculateLatencyDataFromBuffers()
        {
            var data = new LatencyData();
            
            foreach (var kvp in _hostPingBuffers)
            {
                string host = kvp.Key;
                CircularPingBuffer buffer = kvp.Value;
                float[] pings = buffer.GetValidPings();
                
                int totalAttempts = _hostTotalAttempts.ContainsKey(host) ? _hostTotalAttempts[host] : 0;
                int lostPings = _hostLostPings.ContainsKey(host) ? _hostLostPings[host] : 0;
                
                var hostData = new HostLatencyData
                {
                    totalRequests = totalAttempts,
                    successfulRequests = pings.Length,
                    lostRequests = lostPings
                };
                
                if (pings.Length > 0)
                {
                    hostData.minLatency = pings.Min();
                    hostData.maxLatency = pings.Max();
                    hostData.avgLatency = pings.Average();
                }
                
                data.hostResults[host] = hostData;
            }
            
            return data;
        }
        
#if UNITY_EDITOR
        void OnValidate()
        {
            // Provide editor-time validation feedback
            var bugReportUI = GetComponent<BugReportUI>();
            if (bugReportUI == null)
            {
                Debug.LogWarning("LatencyProvider: For automatic setup, attach BugReportUI to the same GameObject. " +
                    "Otherwise, you'll need to manually call SetLatencyProvider().", this);
            }
        }
#endif
    }
}