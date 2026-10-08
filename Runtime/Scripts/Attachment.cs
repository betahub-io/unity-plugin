using System.Collections.Generic;

namespace BetaHub
{
    public enum AttachmentKind
    {
        Image,
        Clip,
        Log,
        Data,
        File,
    }

    /// <summary>Ids of the rows the reporter produces itself. Provider ids must not reuse them.</summary>
    public static class BuiltInAttachments
    {
        public const string Screenshot = "screenshot";
        public const string Recording = "recording";
        public const string PlayerLog = "player-log";
        public const string Latency = "latency";
        public const string Location = "location";

        public static bool IsBuiltIn(string id)
        {
            return id == Screenshot || id == Recording || id == PlayerLog || id == Latency || id == Location;
        }
    }

    /// <summary>
    /// One thing that can travel with a report. The reporter assembles a list of these at open time
    /// from its own capture (screenshot, clip, log) and from anything providers register. The view
    /// renders one checklist row per descriptor and drives a preview from <see cref="Kind"/>.
    /// </summary>
    public sealed class AttachmentDescriptor
    {
        public string Id;
        public string Name;
        public string Meta;
        public AttachmentKind Kind;
        public bool IncludedByDefault = true;

        /// <summary>The file sent for Image, Clip, Log and File rows.</summary>
        public string FilePath;
        /// <summary>Delete <see cref="FilePath"/> once the upload was attempted. For temporary files only.</summary>
        public bool RemoveAfterUpload;
        /// <summary>Data rows: shown in the preview and sent as a text file, one "key: value" per line.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Fields;
    }

    /// <summary>
    /// A studio contributes attachments through this. Called each time the form opens, so returned
    /// items can reflect current state. File rows need <see cref="AttachmentDescriptor.FilePath"/>,
    /// Data rows need <see cref="AttachmentDescriptor.Fields"/>; anything else is skipped with a warning.
    /// </summary>
    public interface IAttachmentProvider
    {
        IEnumerable<AttachmentDescriptor> GetAttachments();
    }

    /// <summary>
    /// Ordered attachments plus per-item include flags. Pure C#: the view reads it, the reporter
    /// consults it when building the report. Order is preserved as given; a repeated id keeps the first.
    /// </summary>
    public sealed class AttachmentSet
    {
        private readonly List<AttachmentDescriptor> _items = new List<AttachmentDescriptor>();
        private readonly Dictionary<string, bool> _included = new Dictionary<string, bool>();

        public AttachmentSet(IEnumerable<AttachmentDescriptor> descriptors)
        {
            if (descriptors == null) return;
            foreach (var d in descriptors)
            {
                if (d == null || string.IsNullOrEmpty(d.Id) || _included.ContainsKey(d.Id)) continue;
                _items.Add(d);
                _included[d.Id] = d.IncludedByDefault;
            }
        }

        public IReadOnlyList<AttachmentDescriptor> Items => _items;

        public bool IsIncluded(string id) => id != null && _included.TryGetValue(id, out var on) && on;

        public void SetIncluded(string id, bool included)
        {
            if (id != null && _included.ContainsKey(id)) _included[id] = included;
        }

        public void Toggle(string id)
        {
            if (id != null && _included.TryGetValue(id, out var on)) _included[id] = !on;
        }

        public IEnumerable<string> IncludedIds
        {
            get
            {
                foreach (var item in _items)
                {
                    if (_included[item.Id]) yield return item.Id;
                }
            }
        }
    }
}
