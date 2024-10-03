using UnityEngine;

namespace BetaHub
{
    public class DrawBox : ITextureFilter
    {
        // DrawBox settings
        private int xPosition;
        private int yPosition;
        private int boxWidth;
        private int boxHeight;
        private Color boxColor;

        // Constructor to initialize DrawBox filter
        public DrawBox(int x, int y, int width, int height, Color color)
        {
            if (width <= 0 || height <= 0)
            {
                throw new System.ArgumentException("Width and height must be positive values.");
            }

            xPosition = x;
            yPosition = y;
            boxWidth = width;
            boxHeight = height;
            boxColor = color;
        }

        public DrawBox(Vector2Int position, Vector2Int size, Color color)
            : this(position.x, position.y, size.x, size.y, color) { }

        // Apply the filter on the texture
        public void ApplyFilter(TexturePainter texture)
        {
            if (texture == null)
            {
                throw new System.ArgumentNullException(nameof(texture), "TexturePainter cannot be null.");
            }

            // Draw the rectangle on the texture
            texture.DrawRectangle(xPosition, yPosition, boxWidth, boxHeight, boxColor);
        }

    }
}