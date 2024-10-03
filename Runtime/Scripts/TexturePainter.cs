using System.Linq;
using UnityEngine;

namespace BetaHub
{
    public class TexturePainter
    {
        private Texture2D texture;
        private static readonly byte[][] digits = new byte[][]
        {
            new byte[] // 0
            {
                1, 1, 1,
                1, 0, 1,
                1, 0, 1,
                1, 0, 1,
                1, 1, 1
            },
            new byte[] // 1
            {
                0, 1, 0,
                1, 1, 0,
                0, 1, 0,
                0, 1, 0,
                1, 1, 1
            },
            new byte[] // 2
            {
                1, 1, 1,
                0, 0, 1,
                1, 1, 1,
                1, 0, 0,
                1, 1, 1
            },
            new byte[] // 3
            {
                1, 1, 1,
                0, 0, 1,
                1, 1, 1,
                0, 0, 1,
                1, 1, 1
            },
            new byte[] // 4
            {
                1, 0, 1,
                1, 0, 1,
                1, 1, 1,
                0, 0, 1,
                0, 0, 1
            },
            new byte[] // 5
            {
                1, 1, 1,
                1, 0, 0,
                1, 1, 1,
                0, 0, 1,
                1, 1, 1
            },
            new byte[] // 6
            {
                1, 1, 1,
                1, 0, 0,
                1, 1, 1,
                1, 0, 1,
                1, 1, 1
            },
            new byte[] // 7
            {
                1, 1, 1,
                0, 0, 1,
                0, 0, 1,
                0, 0, 1,
                0, 0, 1
            },
            new byte[] // 8
            {
                1, 1, 1,
                1, 0, 1,
                1, 1, 1,
                1, 0, 1,
                1, 1, 1
            },
            new byte[] // 9
            {
                1, 1, 1,
                1, 0, 1,
                1, 1, 1,
                0, 0, 1,
                1, 1, 1
            }
        };

        public TexturePainter(Texture2D texture)
        {
            this.texture = texture;
        }

        public void DrawVerticalProgressBar(int x, int y, int width, int height, float progress, Color barColor, Color borderColor)
        {
            // Draw the border
            DrawRectangle(x, y, width, height, borderColor);

            // Calculate the height of the progress bar
            int progressHeight = Mathf.RoundToInt(height * progress);

            // Draw the progress bar
            for (int i = 1; i < width - 1; i++)
            {
                for (int j = 1; j < progressHeight - 1; j++)
                {
                    texture.SetPixel(x + i, y + j, barColor);
                }
            }

            // Apply the changes to the texture
            texture.Apply();
        }

        public void DrawRectangle(int x, int y, int width, int height, Color color)
        {
            Color[] colors = Enumerable.Repeat(color, width * height).ToArray();
            texture.SetPixels(x, y, width, height, colors);
        }

        public void DrawNumber(int x, int y, int number, Color color, int scale = 1)
        {
            string numberString = number.ToString();
            int digitWidth = 3;
            int digitHeight = 5;
            int spacing = 1 * scale;

            for (int i = 0; i < numberString.Length; i++)
            {
                int digit = int.Parse(numberString[i].ToString());
                byte[] bitmap = digits[digit];

                for (int dy = 0; dy < digitHeight; dy++)
                {
                    for (int dx = 0; dx < digitWidth; dx++)
                    {
                        if (bitmap[dy * digitWidth + dx] == 1)
                        {
                            for (int sy = 0; sy < scale; sy++)
                            {
                                for (int sx = 0; sx < scale; sx++)
                                {
                                    texture.SetPixel(x + (dx * scale) + sx + i * (digitWidth * scale + spacing), y + ((digitHeight - 1 - dy) * scale) + sy, color);
                                }
                            }
                        }
                    }
                }
            }

            // Apply the changes to the texture
            texture.Apply();
        }
    }
}