using UnityEngine;

namespace BetaHub
{
    /// <summary>
    /// Developer-facing diagnostics channel. Core classes log through this so tests can assert on
    /// hints without touching the Unity console.
    /// </summary>
    public interface IBetaHubLog
    {
        void Info(string message);
        void Warning(string message);
        void Error(string message);
    }

    public sealed class UnityBetaHubLog : IBetaHubLog
    {
        public const string Prefix = "BetaHub: ";

        private readonly Object _context;

        public UnityBetaHubLog(Object context = null)
        {
            _context = context;
        }

        public void Info(string message) => Debug.Log(Prefix + message, _context);
        public void Warning(string message) => Debug.LogWarning(Prefix + message, _context);
        public void Error(string message) => Debug.LogError(Prefix + message, _context);
    }
}
