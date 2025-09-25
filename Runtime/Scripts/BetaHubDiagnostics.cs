using System;
using UnityEngine;

namespace BetaHub
{
    /// <summary>
    /// Interface for contextual diagnostics providing error handling and logging capabilities.
    /// </summary>
    public interface IContextualDiagnostics
    {
        /// <summary>
        /// Logs an informational message for an operation.
        /// </summary>
        void LogInfo(string operation, string message);

        /// <summary>
        /// Logs a successful operation completion.
        /// </summary>
        void LogSuccess(string operation, string message);

        /// <summary>
        /// Logs progress information for a long-running operation.
        /// </summary>
        void LogProgress(string operation, string message);

        /// <summary>
        /// Logs an error with a custom message and exception details.
        /// </summary>
        void LogError(string operation, string message, Exception ex = null);
    }

    /// <summary>
    /// Main entry point for BetaHub diagnostics.
    /// </summary>
    public static class BetaHubDiagnostics
    {
        /// <summary>
        /// Creates a contextual diagnostics service for a specific component or operation context.
        /// </summary>
        public static IContextualDiagnostics ForContext(string contextName)
        {
            return new ContextualDiagnostics(contextName);
        }
    }

    /// <summary>
    /// Implementation of contextual diagnostics that provides consistent logging.
    /// </summary>
    internal class ContextualDiagnostics : IContextualDiagnostics
    {
        private readonly string _contextName;

        public ContextualDiagnostics(string contextName)
        {
            _contextName = contextName ?? throw new ArgumentNullException(nameof(contextName));
        }

        public void LogInfo(string operation, string message)
        {
            Debug.Log($"[INFO] {_contextName}.{operation}: {message}");
        }

        public void LogSuccess(string operation, string message)
        {
            Debug.Log($"[SUCCESS] {_contextName}.{operation}: {message}");
        }

        public void LogProgress(string operation, string message)
        {
            Debug.Log($"[PROGRESS] {_contextName}.{operation}: {message}");
        }

        public void LogError(string operation, string message, Exception ex = null)
        {
            string errorMessage = ex != null 
                ? $"[ERROR] {_contextName}.{operation}: {message} - {ex.Message}\n{ex.StackTrace}"
                : $"[ERROR] {_contextName}.{operation}: {message}";
            Debug.LogError(errorMessage);
        }
    }
}