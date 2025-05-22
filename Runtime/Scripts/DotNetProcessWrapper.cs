using System.Diagnostics;
using System.IO;

namespace BetaHub
{
    /// <summary>
    /// Implementation of IProcessWrapper using .NET Process class
    /// </summary>
    #if !ENABLE_IL2CPP
    public class DotNetProcessWrapper : IProcessWrapper
    {
        private Process _process;
        private CircularBuffer<string> _errorBuffer = new CircularBuffer<string>(256);

        public int ExitCode => _process?.ExitCode ?? -1;

        public bool Start(string programPath, string[] arguments)
        {
            // wrap every argument in double quotes
            string[] wrappedArguments = System.Array.ConvertAll(arguments, arg => $"\"{arg.Replace("\"", "\"\"")}\"");

            _process = new Process();
            _process.StartInfo.FileName = programPath;
            _process.StartInfo.Arguments = string.Join(" ", wrappedArguments);
            _process.StartInfo.UseShellExecute = false;
            _process.StartInfo.RedirectStandardInput = true;
            _process.StartInfo.RedirectStandardError = true;
            _process.StartInfo.CreateNoWindow = true;
            _process.ErrorDataReceived += (sender, e) => { if (e.Data != null) _errorBuffer.Add(e.Data); };
            
            bool result = _process.Start();
            if (result)
            {
                _process.BeginErrorReadLine();
            }
            return result;
        }

        public int WriteStdin(byte[] data)
        {
            if (_process == null || _process.HasExited)
                return -1;

            try
            {
                _process.StandardInput.BaseStream.Write(data, 0, data.Length);
                return data.Length;
            }
            catch (IOException)
            {
                return -1;
            }
        }

        public string[] ReadStderr()
        {
            return _errorBuffer.ToArray();
        }

        public bool IsRunning()
        {
            return _process != null && !_process.HasExited;
        }

        public void WaitForExit()
        {
            _process?.WaitForExit();
        }

        public void Close()
        {
            if (_process != null)
            {
                if (!_process.HasExited)
                {
                    try
                    {
                        _process.StandardInput.Close();
                        _process.WaitForExit(1000); // Wait up to 1 second

                        if (!_process.HasExited)
                        {
                            _process.Kill();
                        }
                    }
                    catch (System.Exception e)
                    {
                        UnityEngine.Debug.LogError($"Error closing process: {e.Message}");
                    }
                }
            }
        }
    }
    #endif
} 