using System;
using System.Collections.Generic;

namespace BetaHub
{
    /// <summary>
    /// Plain C# core of bug reporting: pending attachments, custom fields, and the rules that turn
    /// them into a <see cref="BugReportDraft"/>. No Unity lifecycle, no UI.
    /// </summary>
    public sealed class BugReportSession
    {
        public const long MaxPlayerLogBytes = 200L * 1024 * 1024;

        private readonly IBetaHubLog _log;
        private readonly List<Issue.ScreenshotFileReference> _screenshots = new List<Issue.ScreenshotFileReference>();
        private readonly List<Issue.LogFileReference> _logFiles = new List<Issue.LogFileReference>();
        private readonly List<Issue.VideoFileReference> _videos = new List<Issue.VideoFileReference>();
        private readonly Dictionary<string, string> _customFields = new Dictionary<string, string>();
        private readonly Dictionary<string, Func<string>> _customFieldProviders = new Dictionary<string, Func<string>>();

        public BugReportSession(IBetaHubLog log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>Recording runs only while video is enabled, no report window is open and nothing is uploading.</summary>
        public static bool ShouldRecord(bool videoEnabled, bool reportWindowVisible, bool uploadInProgress)
        {
            return videoEnabled && !reportWindowVisible && !uploadInProgress;
        }

        // --- attachments ---

        public void AddScreenshot(string path, bool removeAfterUpload)
        {
            _screenshots.Add(new Issue.ScreenshotFileReference { path = path, removeAfterUpload = removeAfterUpload });
        }

        public void AddLogFile(string path, bool removeAfterUpload)
        {
            _logFiles.Add(new Issue.LogFileReference { path = path, removeAfterUpload = removeAfterUpload });
        }

        /// <summary>Developer-added videos are always uploaded, regardless of the recording option.</summary>
        public void AddVideo(string path, bool removeAfterUpload)
        {
            _videos.Add(new Issue.VideoFileReference { path = path, removeAfterUpload = removeAfterUpload });
        }

        public void ClearAttachments()
        {
            _screenshots.Clear();
            _logFiles.Clear();
            _videos.Clear();
        }

        // --- custom fields ---

        public void SetCustomField(string name, string value)
        {
            if (string.IsNullOrEmpty(name))
            {
                _log.Warning("Cannot set a custom field with an empty name.");
                return;
            }
            if (value == null)
            {
                _log.Warning($"Cannot set custom field '{name}' to null. Use RemoveCustomField() to remove it.");
                return;
            }
            _customFields[name] = value;
        }

        public void RemoveCustomField(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            _customFields.Remove(name);
        }

        /// <summary>Called at submit time. A provider's value overrides a static field of the same name.</summary>
        public void RegisterCustomFieldProvider(string name, Func<string> valueGetter)
        {
            if (string.IsNullOrEmpty(name))
            {
                _log.Warning("Cannot register a custom field provider with an empty name.");
                return;
            }
            if (valueGetter == null)
            {
                _log.Warning($"Cannot register custom field provider '{name}' with a null getter.");
                return;
            }
            _customFieldProviders[name] = valueGetter;
        }

        public void UnregisterCustomFieldProvider(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            _customFieldProviders.Remove(name);
        }

        public Dictionary<string, string> CollectCustomFields()
        {
            var result = new Dictionary<string, string>(_customFields);
            foreach (var kvp in _customFieldProviders)
            {
                try
                {
                    string value = kvp.Value();
                    if (value != null)
                    {
                        result[kvp.Key] = value;
                    }
                }
                catch (Exception ex)
                {
                    _log.Warning($"Custom field provider '{kvp.Key}' threw and was skipped: {ex.Message}");
                }
            }
            return result;
        }

        // --- draft ---

        /// <param name="recorderAvailable">Whether a GameRecorder exists; without one no recording is attached.</param>
        /// <param name="playerLog">The plugin's own log file, or null when unavailable.</param>
        /// <param name="extraFields">Fields collected by built-in providers; they win over user fields.</param>
        public BugReportDraft BuildReport(string description, string steps, BugReportOptions options,
            bool recorderAvailable, PlayerLogSource playerLog, IDictionary<string, string> extraFields = null)
        {
            var draft = new BugReportDraft
            {
                Description = description,
                Steps = steps,
                IncludeRecording = options.IncludeRecording && recorderAvailable,
                Videos = new List<Issue.VideoFileReference>(_videos),
                CustomFields = CollectCustomFields(),
            };

            if (options.IncludeScreenshots)
            {
                draft.Screenshots.AddRange(_screenshots);
            }

            if (options.IncludeLogs)
            {
                draft.LogFiles.AddRange(_logFiles);
                if (playerLog != null)
                {
                    if (playerLog.SizeBytes < MaxPlayerLogBytes)
                    {
                        draft.LogFiles.Add(playerLog.Reference);
                    }
                    else
                    {
                        _log.Warning($"Player log is {playerLog.SizeBytes / (1024 * 1024)} MB, above the {MaxPlayerLogBytes / (1024 * 1024)} MB limit, and was not attached.");
                    }
                }
            }

            if (extraFields != null)
            {
                foreach (var kvp in extraFields)
                {
                    draft.CustomFields[kvp.Key] = kvp.Value;
                }
            }

            return draft;
        }
    }
}
