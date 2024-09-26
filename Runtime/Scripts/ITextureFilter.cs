namespace BetaHub
{
    public interface ITextureFilter
    {
        // Now it applies the filter on the given texture
        public void ApplyFilter(TexturePainter texture);
    }
}