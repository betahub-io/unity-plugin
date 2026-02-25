using UnityEngine;

namespace BetaHub
{
    /// <summary>
    /// Example usage of LoggerPerformanceTest component.
    /// 
    /// SETUP INSTRUCTIONS:
    /// 1. Add LoggerPerformanceTest component to any GameObject in the scene
    /// 2. Configure test parameters in the Inspector:
    ///    - Log Count: Number of logs to generate (10-10000)
    ///    - Message Length: Length of each log message (10-1000 chars)
    ///    - Delay Between Logs: Time between logs in sustained mode (0-1s)
    ///    - Burst Mode: Generate all logs at once vs. spread over time
    ///    - Test types: Enable/disable Debug.Log, Warning, Error tests
    /// 3. Configure performance monitoring:
    ///    - Monitor Frame Rate: Track FPS during test
    ///    - Monitor Duration: How long to monitor performance
    /// 4. Set controls:
    ///    - Run Test Key: Key to press to start test (default: T)
    ///    - Run On Start: Auto-start test when scene loads
    /// 
    /// USAGE:
    /// - Press T key (or configured key) to run test
    /// - Use OnGUI sliders for runtime adjustment
    /// - Right-click component → "Run Performance Test" in context menu
    /// - Check Console for detailed performance results
    /// - Check BetaHub log file at reported path for written logs
    /// 
    /// MEASURING PERFORMANCE:
    /// The test will output:
    /// - Total time taken
    /// - Average time per log
    /// - Logs per second
    /// - Frame rate statistics (avg/min/max)
    /// - Path to BetaHub log file
    /// 
    /// RECOMMENDED TEST SCENARIOS:
    /// 1. Burst test with 1000 logs, 100 chars each
    /// 2. Sustained test with 5000 logs, 500 chars each, 0.001s delay
    /// 3. Large message test with 100 logs, 1000 chars each
    /// 4. High frequency test with 10000 logs, 50 chars each
    /// </summary>
    public class LoggerPerformanceTestUsage : MonoBehaviour
    {
        [Header("Quick Test Presets")]
        [SerializeField] private LoggerPerformanceTest performanceTest;
        
        private void Start()
        {
            if (performanceTest == null)
            {
                performanceTest = GetComponent<LoggerPerformanceTest>();
                if (performanceTest == null)
                {
                    performanceTest = gameObject.AddComponent<LoggerPerformanceTest>();
                }
            }
            
            Debug.Log("LoggerPerformanceTest setup complete. Press T to run test or use the OnGUI controls.");
        }
        
        [ContextMenu("Quick Burst Test")]
        public void QuickBurstTest()
        {
            if (performanceTest != null)
            {
                performanceTest.RunTest();
            }
        }
    }
}