using ConsoleGame.RayTracing;
using NullEngine.Video;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleGame.Renderer
{
    public class Texture
    {
        private int[] pixels;
        private bool disposed;
        public int width;
        public int height;

        public Texture(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("filePath must be a valid path.", nameof(filePath));
            using (Mat src = Cv2.ImRead(filePath, ImreadModes.Color))
            {
                if (src.Empty())
                    throw new ArgumentException($"Failed to load image: {filePath}");
                using (Mat rgba = new Mat())
                {
                    Cv2.CvtColor(src, rgba, ColorConversionCodes.BGR2RGBA);
                    if (!rgba.IsContinuous())
                    {
                        using (Mat clone = rgba.Clone())
                        {
                            InitializeFromRgbaMat(clone);
                        }
                    }
                    else
                    {
                        InitializeFromRgbaMat(rgba);
                    }
                }
            }
        }

        private void InitializeFromRgbaMat(Mat rgba)
        {
            width = rgba.Cols;
            height = rgba.Rows;
            int byteCount = width * height * 4;
            pixels = new int[width * height];
            byte[] tmp = new byte[byteCount];
            Marshal.Copy(rgba.Data, tmp, 0, byteCount);
            Buffer.BlockCopy(tmp, 0, pixels, 0, byteCount);
        }

        public RGBA32 GetPixel(int x, int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
                throw new ArgumentOutOfRangeException("x/y out of bounds.");
            int idx = y * width + x;
            return new RGBA32(pixels[idx]);
        }

        public void SetPixel(int x, int y, RGBA32 color)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
                throw new ArgumentOutOfRangeException("x/y out of bounds.");
            int idx = y * width + x;
            pixels[idx] = color.ToInt();
        }

        public Vec3 SampleBilinear(float u, float v)
        {
            if (pixels == null || width <= 0 || height <= 0)
                return new Vec3(1.0, 1.0, 1.0);
            u = u - MathF.Floor(u);
            v = v - MathF.Floor(v);
            float fx = u * (width - 1);
            float fy = v * (height - 1);
            int x0 = (int)MathF.Floor(fx);
            int y0 = (int)MathF.Floor(fy);
            int x1 = (x0 + 1) % width;
            int y1 = (y0 + 1) % height;
            float tx = fx - x0;
            float ty = fy - y0;
            RGBA32 c00 = new RGBA32(pixels[y0 * width + x0]);
            RGBA32 c10 = new RGBA32(pixels[y0 * width + x1]);
            RGBA32 c01 = new RGBA32(pixels[y1 * width + x0]);
            RGBA32 c11 = new RGBA32(pixels[y1 * width + x1]);
            Vec3 a = Lerp(c00.toVec3(), c10.toVec3(), tx);
            Vec3 b = Lerp(c01.toVec3(), c11.toVec3(), tx);
            Vec3 c = Lerp(a, b, ty);
            return c.Saturate();
        }

        private static Vec3 Lerp(Vec3 a, Vec3 b, float t)
        {
            return a * (1.0f - t) + b * t;
        }
    }
}
