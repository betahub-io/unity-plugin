using System.Collections.Generic;

namespace BetaHub
{
    public class TextureFilterManager
    {
        // Dictionary to store filters with a unique string ID
        private Dictionary<string, ITextureFilter> filters = new Dictionary<string, ITextureFilter>();

        // Method to add a filter to the dictionary with a unique string ID
        public void AddFilter(string id, ITextureFilter filter)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new System.ArgumentException("Filter ID cannot be null or empty.", nameof(id));
            }
            if (filter == null)
            {
                throw new System.ArgumentNullException(nameof(filter), "Filter cannot be null.");
            }

            filters[id] = filter;
        }

        // Method to remove a filter based on its string ID
        public void RemoveFilter(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new System.ArgumentException("Filter ID cannot be null or empty.", nameof(id));
            }

            if (filters.ContainsKey(id))
            {
                filters.Remove(id);
            }
        }

        // Method to get the filter by string ID
        public ITextureFilter GetFilter(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new System.ArgumentException("Filter ID cannot be null or empty.", nameof(id));
            }

            filters.TryGetValue(id, out ITextureFilter filter);
            return filter;
        }

        // Method to apply all filters on the provided texture
        public void ApplyFilters(TexturePainter texture)
        {
            if (texture == null)
            {
                throw new System.ArgumentNullException(nameof(texture), "TexturePainter cannot be null.");
            }

            foreach (var filter in filters.Values)
            {
                filter.ApplyFilter(texture);
            }
        }
    }
}