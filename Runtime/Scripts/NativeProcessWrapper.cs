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
        private Task _stderrTask;

        // Import the native functions with platform-specific library names
        #if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [DllImport("betahub_process_wrapper")]
        #else
        [DllImport("libbetahub_process_wrapper")]
        #endif
        private static extern IntPtr process_start_with_args(
            string program, 
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] 
            string[] args, 
            int args_len);

        #if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [DllImport("betahub_process_wrapper")]
        #else
        [DllImport("libbetahub_process_wrapper")]
        #endif
        private static extern int process_write_stdin(IntPtr proc, byte[] data, int len);

        #if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [DllImport("betahub_process_wrapper")]
        #else
        [DllImport("libbetahub_process_wrapper")]
        #endif
        private static extern int process_read_stderr(IntPtr proc, byte[] buf, int len);

        #if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [DllImport("betahub_process_wrapper")]
        #else
        [DllImport("libbetahub_process_wrapper")]
        #endif
        private static extern int process_is_running(IntPtr proc);

        #if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [DllImport("betahub_process_wrapper")]
        #else
        [DllImport("libbetahub_process_wrapper")]
        #endif
        private static extern int process_wait(IntPtr proc);

        #if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [DllImport("betahub_process_wrapper")]
        #else
        [DllImport("libbetahub_process_wrapper")]
        #endif
        private static extern void process_close(IntPtr proc);

        public int ExitCode => _exitCode;

        public bool Start(string programPath, string[] arguments)
        {
            _processPtr = process_start_with_args(programPath, arguments, arguments.Length);
            _isRunning = _processPtr != IntPtr.Zero;
            
            if (_isRunning)
            {
                // Start a task to periodically read stderr
                _stderrTask = Task.Run(() => StderrReadingLoop());
            }

            return _isRunning;
        }

        private void StderrReadingLoop()
        {
            byte[] buffer = new byte[4096];

            while (_isRunning && process_is_running(_processPtr) != 0)
            {
                DrainStderrOnce(buffer);
                System.Threading.Thread.Sleep(100); // Don't busy-wait
            }

            // Final drain after the process has exited (or a stop was requested).
            // The native side keeps any remaining stderr buffered until it is read,
            // but the loop above stops the instant the process is gone. Without this,
            // fast-exiting calls - notably the `ffmpeg -i` duration probe, which
            // finishes in a few milliseconds - lose their output entirely, which is
            // what breaks audio/video sync in IL2CPP builds. Keep draining until we
            // see several consecutive empty reads (covering the brief flush window
            // after EOF), with a hard cap so this can never spin forever.
            int consecutiveEmpty = 0;
            int guard = 0;
            while (consecutiveEmpty < 5 && guard < 300)
            {
                guard++;
                if (DrainStderrOnce(buffer) > 0)
                {
                    consecutiveEmpty = 0;
                }
                else
                {
                    consecutiveEmpty++;
                    System.Threading.Thread.Sleep(10);
                }
            }
        }

        // Reads whatever stderr is currently buffered on the native side and appends
        // it to the error buffer. Returns the number of bytes read. All calls happen
        // on the single stderr-reader thread, so the error buffer is never written
        // from two threads at once.
        private int DrainStderrOnce(byte[] buffer)
        {
            if (_processPtr == IntPtr.Zero)
                return 0;

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
            return bytesRead;
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

            // Wait for the reader thread to finish its final post-exit drain so a
            // subsequent ReadStderr() (e.g. duration probing) sees the complete
            // output. Bounded so a misbehaving reader can never hang the caller.
            try { _stderrTask?.Wait(3000); } catch (System.Exception) { }
        }

        public void Close()
        {
            if (_processPtr == IntPtr.Zero)
                return;

            process_close(_processPtr);
            _exitCode = process_wait(_processPtr);
            _isRunning = false;

            // Let the reader thread complete its final drain before we drop our
            // reference, so error output captured during encoding isn't lost.
            try { _stderrTask?.Wait(3000); } catch (System.Exception) { }

            _processPtr = IntPtr.Zero;
        }
    }
    #endif
} 