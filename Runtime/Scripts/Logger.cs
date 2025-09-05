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