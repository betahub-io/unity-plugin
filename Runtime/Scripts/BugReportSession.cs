using System;
using System.Collections.Generic;
using System.Text;

namespace BetaHub
{
    /// <summary>What the host can offer at the moment the form opens.</summary>
    public sealed class AttachmentSources
    {
        public const long UnknownSize = -1;

        /// <summary>Length of the recorder's rolling window in seconds; 0 when no recording can be attached.</summary>
        public int RecordingSeconds;
        /// <summary>The plugin's own log file, or null when unavailable.</summary>
        public PlayerLogSource PlayerLog;
        public bool LocationCountry;
        public bool LocationNetwork;
        public bool Latency;
        /// <summary>Size of a file in bytes, or <see cref="UnknownSize"/>. The host passes a real file check.</summary>
        public Func<string, long> FileSize = _ => UnknownSize;
    }

    /// <summary>
    /// Plain C# core of bug reporting: pending attachments, custom fields, and the rules that turn
    /// them into an <see cref="AttachmentSet"/> and then a <see cref="BugReportDraft"/>. No Unity lifecycle, no UI.
    /// </summary>
    public sealed class BugReportSession
    {
        public const long MaxPlayerLogBytes = 200L * 1024 * 1024;

        private const string AddedIdPrefix = "added-";
        private const string DataFileExtension = ".txt";

        private sealed class AddedFile
        {
            public string Id;
            public AttachmentKind Kind;
            public string Path;
            public string Name;
            public bool RemoveAfterUpload;
        }

        private readonly IBetaHubLog _log;
        private readonly List<AddedFile> _added = new List<AddedFile>();
        private readonly List<IAttachmentProvider> _providers = new List<IAttachmentProvider>();
        private readonly Dictionary<string, string> _customFields = new Dictionary<string, string>();
        private readonly Dictionary<string, Func<string>> _customFieldProviders = new Dictionary<string, Func<string>>();
        private string _frameScreenshot;
        private int _addedCount;

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

        /// <summary>The frame captured when the form opened. Replaces the previous one; null removes it.</summary>
        public void SetFrameScreenshot(string path)
        {
            _frameScreenshot = string.IsNullOrEmpty(path) ? null : path;
        }

        public void AddScreenshot(string path, bool removeAfterUpload) => Add(AttachmentKind.Image, path, null, removeAfterUpload);
        public void AddLogFile(string path, bool removeAfterUpload) => Add(AttachmentKind.Log, path, null, removeAfterUpload);
        public void AddVideo(string path, bool removeAfterUpload) => Add(AttachmentKind.Clip, path, null, removeAfterUpload);

        /// <summary>Any file, uploaded as is. <paramref name="name"/> defaults to the file name.</summary>
        public void AddFile(string path, string name, bool removeAfterUpload) => Add(AttachmentKind.File, path, name, removeAfterUpload);

        /// <summary>Forgets added files and the frame screenshot. Called once a report's draft exists.</summary>
        public void ClearAttachments()
        {
            _added.Clear();
            _frameScreenshot = null;
        }

        public void RegisterAttachmentProvider(IAttachmentProvider provider)
        {
            if (provider == null)
            {
                _log.Warning("Cannot register a null attachment provider.");
                return;
            }
            if (!_providers.Contains(provider)) _providers.Add(provider);
        }

        public void UnregisterAttachmentProvider(IAttachmentProvider provider)
        {
            _providers.Remove(provider);
        }

        private void Add(AttachmentKind kind, string path, string name, bool removeAfterUpload)
        {
            if (string.IsNullOrEmpty(path))
            {
                _log.Warning($"Cannot attach a {kind} file with an empty path.");
                return;
            }
            _addedCount++;
            _added.Add(new AddedFile
            {
                Id = AddedIdPrefix + _addedCount,
                Kind = kind,
                Path = path,
                Name = string.IsNullOrEmpty(name) ? System.IO.Path.GetFileName(path) : name,
                RemoveAfterUpload = removeAfterUpload,
            });
        }

        /// <summary>
        /// Everything that can travel with a report right now, in display order: the reporter's own
        /// capture, files added in code, built-in data, then provider items. Call when the form opens.
        /// </summary>
        public AttachmentSet CollectAttachments(AttachmentSources sources)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));

            var items = new List<AttachmentDescriptor>();
            if (_frameScreenshot != null)
            {
                items.Add(new AttachmentDescriptor
                {
                    Id = BuiltInAttachments.Screenshot, Name = "Screenshot", Meta = "this frame",
                    Kind = AttachmentKind.Image, FilePath = _frameScreenshot, RemoveAfterUpload = true,
                });
            }

            if (sources.RecordingSeconds > 0)
            {
                items.Add(new AttachmentDescriptor
                {
                    Id = BuiltInAttachments.Recording, Name = $"Last {sources.RecordingSeconds} s of play", Meta = "video",
                    Kind = AttachmentKind.Clip,
                });
            }

            var playerLog = sources.PlayerLog;
            if (playerLog != null)
            {
                if (playerLog.SizeBytes < MaxPlayerLogBytes)
                {
                    items.Add(new AttachmentDescriptor
                    {
                        Id = BuiltInAttachments.PlayerLog, Name = "Game log", Meta = FormatSize(playerLog.SizeBytes),
                        Kind = AttachmentKind.Log, FilePath = playerLog.Path,
                    });
                }
                else
                {
                    _log.Warning($"Player log is {playerLog.SizeBytes / (1024 * 1024)} MB, above the {MaxPlayerLogBytes / (1024 * 1024)} MB limit, and will not be attached.");
                }
            }

            foreach (var file in _added)
            {
                items.Add(new AttachmentDescriptor
                {
                    Id = file.Id, Name = file.Name, Meta = FormatSize(sources.FileSize(file.Path)),
                    Kind = file.Kind, FilePath = file.Path, RemoveAfterUpload = file.RemoveAfterUpload,
                });
            }

            if (sources.Latency)
            {
                items.Add(new AttachmentDescriptor { Id = BuiltInAttachments.Latency, Name = "Network", Meta = "ping times", Kind = AttachmentKind.Data });
            }

            if (sources.LocationCountry || sources.LocationNetwork)
            {
                items.Add(new AttachmentDescriptor
                {
                    Id = BuiltInAttachments.Location, Name = "Location", Kind = AttachmentKind.Data,
                    Meta = sources.LocationCountry && sources.LocationNetwork ? "country, ISP" : sources.LocationCountry ? "country" : "ISP",
                });
            }

            AddProviderItems(items);
            return new AttachmentSet(items);
        }

        private void AddProviderItems(List<AttachmentDescriptor> items)
        {
            var used = new HashSet<string>();
            foreach (var item in items) used.Add(item.Id);

            foreach (var provider in _providers)
            {
                try
                {
                    var provided = provider.GetAttachments();
                    if (provided == null) continue;
                    foreach (var descriptor in provided)
                    {
                        string problem = ProblemWith(descriptor, used);
                        if (problem != null)
                        {
                            _log.Warning($"Attachment from {provider.GetType().Name} skipped: {problem}");
                            continue;
                        }
                        used.Add(descriptor.Id);
                        items.Add(string.IsNullOrEmpty(descriptor.Name) ? Renamed(descriptor, descriptor.Id) : descriptor);
                    }
                }
                catch (Exception ex)
                {
                    _log.Warning($"Attachment provider {provider.GetType().Name} threw and was skipped: {ex.Message}");
                }
            }
        }

        private static string ProblemWith(AttachmentDescriptor descriptor, HashSet<string> used)
        {
            if (descriptor == null) return "the descriptor is null.";
            if (string.IsNullOrEmpty(descriptor.Id)) return "the Id is empty.";
            if (BuiltInAttachments.IsBuiltIn(descriptor.Id)) return $"'{descriptor.Id}' is a built-in id.";
            if (used.Contains(descriptor.Id)) return $"the id '{descriptor.Id}' is already used.";
            if (descriptor.Kind == AttachmentKind.Data)
            {
                return descriptor.Fields == null || descriptor.Fields.Count == 0 ? $"Data item '{descriptor.Id}' has no Fields." : null;
            }
            return string.IsNullOrEmpty(descriptor.FilePath) ? $"{descriptor.Kind} item '{descriptor.Id}' has no FilePath." : null;
        }

        private static AttachmentDescriptor Renamed(AttachmentDescriptor d, string name)
        {
            return new AttachmentDescriptor
            {
                Id = d.Id, Name = name, Meta = d.Meta, Kind = d.Kind, IncludedByDefault = d.IncludedByDefault,
                FilePath = d.FilePath, RemoveAfterUpload = d.RemoveAfterUpload, Fields = d.Fields,
            };
        }

        private static string FormatSize(long bytes)
        {
            return bytes < 0 ? string.Empty : BugReportWidgetModel.FormatFileSize(bytes);
        }

        /// <summary>Maps the legacy form's three toggles onto a set. Only ever leaves items out.</summary>
        public static void ApplyOptions(AttachmentSet attachments, BugReportOptions options)
        {
            if (attachments == null) throw new ArgumentNullException(nameof(attachments));
            foreach (var item in attachments.Items)
            {
                bool keep = item.Id == BuiltInAttachments.Recording ? options.IncludeRecording
                          : item.Kind == AttachmentKind.Image ? options.IncludeScreenshots
                          : item.Kind == AttachmentKind.Log ? options.IncludeLogs
                          : true;
                if (!keep) attachments.SetIncluded(item.Id, false);
            }
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

        /// <summary>Turns the included items of <paramref name="attachments"/> into uploads, in list order.</summary>
        /// <param name="extraFields">Fields collected by built-in providers; they win over user fields.</param>
        public BugReportDraft BuildReport(string description, string steps, AttachmentSet attachments,
            IDictionary<string, string> extraFields = null)
        {
            if (attachments == null) throw new ArgumentNullException(nameof(attachments));

            var draft = new BugReportDraft
            {
                Description = description,
                Steps = steps,
                CustomFields = CollectCustomFields(),
            };

            foreach (var item in attachments.Items)
            {
                if (!attachments.IsIncluded(item.Id)) continue;
                switch (item.Id)
                {
                    case BuiltInAttachments.Recording:
                        draft.Uploads.Add(new ReportUpload { AttachmentId = item.Id, Kind = UploadKind.Recording });
                        break;
                    case BuiltInAttachments.Latency:
                    case BuiltInAttachments.Location:
                        draft.InlineAttachmentIds.Add(item.Id);
                        break;
                    default:
                        draft.Uploads.Add(UploadFor(item));
                        break;
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

        private static ReportUpload UploadFor(AttachmentDescriptor item)
        {
            var upload = new ReportUpload { AttachmentId = item.Id, Path = item.FilePath, RemoveAfterUpload = item.RemoveAfterUpload };
            switch (item.Kind)
            {
                case AttachmentKind.Image: upload.Kind = UploadKind.Screenshot; break;
                case AttachmentKind.Log: upload.Kind = UploadKind.Log; break;
                case AttachmentKind.Clip: upload.Kind = UploadKind.Video; break;
                case AttachmentKind.File:
                    upload.Kind = UploadKind.File;
                    upload.Name = item.Name;
                    break;
                case AttachmentKind.Data:
                    upload.Kind = UploadKind.File;
                    upload.Name = item.Name;
                    upload.FileName = item.Id + DataFileExtension;
                    upload.Content = Encoding.UTF8.GetBytes(FormatFields(item.Fields));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(item), item.Kind, "Unknown attachment kind.");
            }
            return upload;
        }

        private static string FormatFields(IReadOnlyList<KeyValuePair<string, string>> fields)
        {
            var text = new StringBuilder();
            foreach (var kvp in fields)
            {
                text.Append(kvp.Key).Append(": ").Append(kvp.Value).Append('\n');
            }
            return text.ToString();
        }
    }
}
