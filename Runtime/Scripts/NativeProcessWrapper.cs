using System;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace BetaHub
{
    /// <summary>
    /// Implementation of IProcessWrapper using native library
    /// </summary>
    #if ENABLE_IL2CPP && ENABLE_BETAHUB_FFMPEG
    public class NativeProcessWrapper : IProcessWrapper
    {
        private IntPtr _processPtr;
        private CircularBuffer<string> _errorBuffer = new CircularBuffer<string>(256);
        private bool _isRunning = false;
        private int _exitCode = -1;

        // Import the native functions
        [DllImport("libbetahub_process_wrapper")]
        private static extern IntPtr process_start_with_args(
            string program, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] 
            string[] args, 
            int args_len);

        [DllImport("libbetahub_process_wrapper")]
        private static extern int process_write_stdin(IntPtr proc, byte[] data, int len);

        [DllImport("libbetahub_process_wrapper")]
        private static extern int process_read_stderr(IntPtr proc, byte[] buf, int len);

        [DllImport("libbetahub_process_wrapper")]
        private static extern int process_is_running(IntPtr proc);

        [DllImport("libbetahub_process_wrapper")]
        private static extern int process_wait(IntPtr proc);

        [DllImport("libbetahub_process_wrapper")]
        private static extern void process_close(IntPtr proc);

        public int ExitCode => _exitCode;

        public bool Start(string programPath, string[] arguments)
        {
            _processPtr = process_start_with_args(programPath, arguments, arguments.Length);
            _isRunning = _processPtr != IntPtr.Zero;
            
            if (_isRunning)
            {
                // Start a task to periodically read stderr
                Task.Run(() => StderrReadingLoop());
            }
            
            return _isRunning;
        }

        private void StderrReadingLoop()
        {
            byte[] buffer = new byte[4096];
            
            while (_isRunning && process_is_running(_processPtr) != 0)
            {
                int bytesRead = process_read_stderr(_processPtr, buffer, buffer.Length);
                if (bytesRead > 0)
                {
                    string errorText = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    string[] lines = errorText.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        _errorBuffer.Add(line);
                    }
                }
                
                System.Threading.Thread.Sleep(100); // Don't busy-wait
            }
        }

        public int WriteStdin(byte[] data)
        {
            if (_processPtr == IntPtr.Zero)
                return -1;
                
            return process_write_stdin(_processPtr, data, data.Length);
        }

        public string[] ReadStderr()
        {
            return _errorBuffer.ToArray();
        }

        public bool IsRunning()
        {
            if (_processPtr == IntPtr.Zero)
                return false;
                
            return process_is_running(_processPtr) != 0;
        }

        public void WaitForExit()
        {
            if (_processPtr == IntPtr.Zero)
                return;
                
            _exitCode = process_wait(_processPtr);
            _isRunning = false;
        }

        public void Close()
        {
            if (_processPtr == IntPtr.Zero)
                return;
                
            process_close(_processPtr);
            _exitCode = process_wait(_processPtr);
            _isRunning = false;
            _processPtr = IntPtr.Zero;
        }
    }
    #endif
} 