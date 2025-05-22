using System;

namespace BetaHub
{
    /// <summary>
    /// Interface for process wrappers to abstract process handling based on platform requirements
    /// </summary>
    public interface IProcessWrapper
    {
        bool Start(string programPath, string[] arguments);
        int WriteStdin(byte[] data);
        string[] ReadStderr();
        bool IsRunning();
        void WaitForExit();
        int ExitCode { get; }
        void Close();
    }
} 