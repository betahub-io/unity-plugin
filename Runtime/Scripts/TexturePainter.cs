using UnityEngine;

namespace BetaHub
{
    /// <summary>
    /// Wrapper for byte buffer that provides pixel access without needing to know color component offsets
    /// </summary>
    public class ByteBufferWrapper
    {
        private byte[] buffer;
        private int width;
        private int height;
        private int bytesPerPixel;
        
        public ByteBufferWrapper(byte[] buffer, int width, int height, int bytesPerPixel = 4)
        {
            this.buffer = buffer;
            this.width = width;
            this.height = height;
            this.bytesPerPixel = bytesPerPixel;
        }
        
        public void SetPixel(int x, int y, Color color)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return;
            
            int index = (y * width + x) * bytesPerPixel;
            if (index + bytesPerPixel - 1 >= buffer.Length) return;
            
            // RGBA format
            buffer[index] = (byte)(color.r * 255);     // R
            buffer[index + 1] = (byte)(color.g * 255); // G
            buffer[index + 2] = (byte)(color.b * 255); // B
            if (bytesPerPixel == 4)
            {
                buffer[index + 3] = (byte)(color.a * 255); // A
            }
        }
        
        public Color GetPixel(int x, int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return Color.clear;
            
            int index = (y * width + x) * bytesPerPixel;
            if (index + bytesPerPixel - 1 >= buffer.Length) return Color.clear;
            
            float r = buffer[index] / 255f;
            float g = buffer[index + 1] / 255f;
            float b = buffer[index + 2] / 255f;
            float a = bytesPerPixel == 4 ? buffer[index + 3] / 255f : 1f;
            
            return new Color(r, g, b, a);
        }
        
        public int Width => width;
        public int Height => height;
    }

    /// <summary>
    /// TexturePainter provides drawing operations on byte buffers.
    /// 
    /// Y Coordinate System:
    /// - When flipY is false: Y=0 is at the top of the image, Y increases downward (standard screen coordinates)
    /// - When flipY is true: Y=0 is at the bottom of the image, Y increases upward (mathematical/OpenGL coordinates)
    /// </summary>
    public class TexturePainter
    {
        private ByteBufferWrapper bufferWrapper;
        private bool flipY;
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

        public TexturePainter(ByteBufferWrapper bufferWrapper, bool flipY = false)
        {
            this.bufferWrapper = bufferWrapper;
            this.flipY = flipY;
        }

        /// <summary>
        /// Transforms Y coordinate based on flipY setting.
        /// If flipY is true, flips Y coordinate so that Y=0 is at the bottom.
        /// </summary>
        private int TransformY(int y)
        {
            return flipY ? (bufferWrapper.Height - 1 - y) : y;
        }

        /// <summary>
        /// Sets a pixel with coordinate transformation applied.
        /// </summary>
        private void SetPixel(int x, int y, Color color)
        {
            bufferWrapper.SetPixel(x, TransformY(y), color);
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
                    SetPixel(x + i, y + j, barColor);
                }
            }
        }

        private void DrawRectangle(int x, int y, int width, int height, Color color)
        {
            // Draw top and bottom borders
            for (int i = 0; i < width; i++)
            {
                SetPixel(x + i, y, color);
                SetPixel(x + i, y + height - 1, color);
            }

            // Draw left and right borders
            for (int i = 0; i < height; i++)
            {
                SetPixel(x, y + i, color);
                SetPixel(x + width - 1, y + i, color);
            }
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
                                    SetPixel(x + (dx * scale) + sx + i * (digitWidth * scale + spacing), y + ((digitHeight - 1 - dy) * scale) + sy, color);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}