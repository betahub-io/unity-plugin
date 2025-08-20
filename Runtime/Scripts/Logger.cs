﻿using UnityEngine;
using System;
using System.IO;

namespace BetaHub
{
    public class Logger
    {
        private string logFileName;
        private string _logPath;

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
            if (File.Exists(_logPath))
            {
                File.Delete(_logPath);
            }
        }

        private void UnityLogHandler(string condition, string stackTrace, LogType type)
        {
            string log = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " [" + type + "] " + condition + "\n" + stackTrace;
            WriteToLog(log);
        }

        private void WriteToLog(string log)
        {
            try
            {
                using (var fileStream = new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                {
                    using (var writer = new StreamWriter(fileStream))
                    {
                        writer.WriteLine(log);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("Error writing to log file: " + e.Message);
            }
        }
    }
}