using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;

namespace BetaHub
{
    public class Logger : IDisposable
    {
        private string logFileName;
        private string _logPath;
        private FileStream fileStream;
        private StreamWriter writer;
        private List<string> logBuffer;
        private readonly object lockObject = new object();
        private DateTime lastFlushTime;
        private bool disposed = false;

        private const int BUFFER_SIZE = 50;
        private static readonly TimeSpan FLUSH_INTERVAL = TimeSpan.FromSeconds(2);

        public string LogPath => _logPath;

        public Logger()
        {
    #if UNITY_EDITOR
            logFileName = "BH_Editor.log";
    #else
            logFileName = "BH_Player.log";
    #endif

            Application.logMessageReceivedThreaded += UnityLogHandler;
            _logPath = Path.Combine(Application.persistentDataPath, logFileName);
            
            logBuffer = new List<string>(BUFFER_SIZE);
            lastFlushTime = DateTime.UtcNow;
            
            InitializeFileStream();
        }

        private void InitializeFileStream()
        {
            try
            {
                if (File.Exists(_logPath))
                {
                    File.Delete(_logPath);
                }
                
                fileStream = new FileStream(_logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                writer = new StreamWriter(fileStream) { AutoFlush = false };
            }
            catch (Exception e)
            {
                Debug.LogError("Error initializing log file stream: " + e.Message);
            }
        }

        private void UnityLogHandler(string condition, string stackTrace, LogType type)
        {
            if (disposed) return;
            
            string log = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " [" + type + "] " + condition + "\n" + stackTrace;
            WriteToLog(log);
        }

        private void WriteToLog(string log)
        {
            if (disposed || writer == null) return;
            
            lock (lockObject)
            {
                try
                {
                    logBuffer.Add(log);
                    
                    bool shouldFlush = logBuffer.Count >= BUFFER_SIZE || 
                                     (DateTime.UtcNow - lastFlushTime) >= FLUSH_INTERVAL;
                    
                    if (shouldFlush)
                    {
                        FlushBuffer();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError("Error buffering log: " + e.Message);
                }
            }
        }

        private void FlushBuffer()
        {
            if (disposed || logBuffer.Count == 0 || writer == null) return;
            
            try
            {
                if (fileStream?.CanWrite != true) return;
                
                foreach (string log in logBuffer)
                {
                    writer.WriteLine(log);
                }
                
                writer.Flush();
                fileStream.Flush();
                logBuffer.Clear();
                lastFlushTime = DateTime.UtcNow;
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception e)
            {
                Debug.LogError("Error flushing log buffer: " + e.Message);
            }
        }

        public void ForceFlush()
        {
            lock (lockObject)
            {
                FlushBuffer();
            }
        }

        public void PauseLogging()
        {
            lock (lockObject)
            {
                FlushBuffer();
                writer?.Dispose();
                fileStream?.Dispose();
                writer = null;
                fileStream = null;
            }
        }

        public void ResumeLogging()
        {
            lock (lockObject)
            {
                if (writer == null && fileStream == null)
                {
                    try
                    {
                        // Append to existing file instead of recreating it
                        fileStream = new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                        writer = new StreamWriter(fileStream) { AutoFlush = false };
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("Error resuming log file stream: " + e.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Safely reads the entire log file by temporarily closing the file stream.
        /// This prevents file sharing violations when other parts of the application need to read the log.
        /// </summary>
        /// <returns>The complete log file content as a byte array, or null if an error occurs</returns>
        public byte[] ReadLogFileBytes()
        {
            if (disposed || string.IsNullOrEmpty(_logPath))
            {
                Debug.LogWarning("Cannot read log file: Logger is disposed or log path is not set");
                return null;
            }

            lock (lockObject)
            {
                try
                {
                    // First, flush any buffered data
                    FlushBuffer();

                    // Temporarily close the file streams
                    writer?.Dispose();
                    fileStream?.Dispose();
                    writer = null;
                    fileStream = null;

                    // Now we can safely read the file since we've closed our handle
                    byte[] fileData = null;
                    if (File.Exists(_logPath))
                    {
                        fileData = File.ReadAllBytes(_logPath);
                    }

                    // Reopen the file streams to continue logging
                    InitializeFileStream();

                    return fileData;
                }
                catch (Exception e)
                {
                    Debug.LogError("Error reading log file: " + e.Message);

                    // Ensure we reopen the file streams even if reading failed
                    try
                    {
                        InitializeFileStream();
                    }
                    catch (Exception reopenEx)
                    {
                        Debug.LogError("Error reopening log file after failed read: " + reopenEx.Message);
                    }

                    return null;
                }
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            
            Application.logMessageReceivedThreaded -= UnityLogHandler;
            
            lock (lockObject)
            {
                FlushBuffer();
                
                writer?.Dispose();
                fileStream?.Dispose();
                
                writer = null;
                fileStream = null;
                logBuffer?.Clear();
                
                disposed = true;
            }
        }

        ~Logger()
        {
            try
            {
                Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception)
            {
            }
        }
    }
}