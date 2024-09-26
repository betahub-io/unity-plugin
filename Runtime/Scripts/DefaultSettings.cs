#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BetaHub
{
    [System.Serializable]
    public class DefaultSettings
    {
        public string description;
        public string steps;
        public bool includeVideo;
        public bool includeScreenshot;
        public bool includePlayerLog;

        // Constructor to initialize default values
        public DefaultSettings(string description, string steps, bool includeVideo, bool includeScreenshot, bool includePlayerLog)
        {
            this.description = description;
            this.steps = steps;
            this.includeVideo = includeVideo;
            this.includeScreenshot = includeScreenshot;
            this.includePlayerLog = includePlayerLog;
        }
    }
}