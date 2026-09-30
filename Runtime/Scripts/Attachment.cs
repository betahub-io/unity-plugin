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
    }

    /// <summary>
    /// A studio, or a built-in like geolocation, contributes attachments through this. Called at the
    /// moment the form opens, so returned meta can reflect current state.
    /// </summary>
    public interface IAttachmentProvider
    {
        IEnumerable<AttachmentDescriptor> GetAttachments();
    }

    /// <summary>
    /// Ordered attachments plus per-item include flags. Pure C#: the view reads it, the reporter
    /// consults it when building the report. Order is preserved as given.
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
                if (d == null || string.IsNullOrEmpty(d.Id)) continue;
                _items.Add(d);
                _included[d.Id] = d.IncludedByDefault;
            }
        }

        public IReadOnlyList<AttachmentDescriptor> Items => _items;

        public bool IsIncluded(string id) => _included.TryGetValue(id, out var on) && on;

        public void SetIncluded(string id, bool included)
        {
            if (_included.ContainsKey(id)) _included[id] = included;
        }

        public void Toggle(string id)
        {
            if (_included.TryGetValue(id, out var on)) _included[id] = !on;
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
