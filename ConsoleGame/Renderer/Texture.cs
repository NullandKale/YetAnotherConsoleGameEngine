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
        private NullEngine.Video.IFrameReader dynamicReader;
        private bool isDynamic;
        private int dynamicBytesPerPixel; // 3=BGR, 4=BGRA
        private bool flipU;
        private bool flipV;
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

        // Live camera/video-backed texture
        public Texture(NullEngine.Video.IFrameReader reader, bool useRGBA = false, bool flipU = false, bool flipV = false)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            dynamicReader = reader;
            isDynamic = true;
            dynamicBytesPerPixel = useRGBA ? 4 : 3; // we sample as BGR or BGRA
            width = reader.Width;
            height = reader.Height;
            this.flipU = flipU;
            this.flipV = flipV;
        }

        // Convenience: build from a camera index
        public static Texture FromCamera(int cameraIndex, bool requestRGBA = false, bool singleFrameAdvance = false, float forcedAspect = 0.0f)
        {
            NullEngine.Video.IFrameReader reader = forcedAspect > 0.0f
                ? new NullEngine.Video.AsyncCameraReader(cameraIndex, forcedAspect, singleFrameAdvance, requestRGBA)
                : new NullEngine.Video.AsyncCameraReader(cameraIndex, singleFrameAdvance, requestRGBA);
            return new Texture(reader, requestRGBA, flipU: false, flipV: false);
        }

        // Convenience: build from a video file using ffmpeg reader
        public static Texture FromVideo(string videoPath, bool requestRGBA = false, bool singleFrameAdvance = false, bool playAudio = false)
        {
            var reader = new NullEngine.Video.AsyncFfmpegVideoReader(videoPath, singleFrameAdvance: singleFrameAdvance, useRGBA: requestRGBA, playAudio: playAudio);
            // Videos often need a vertical flip to match UV conventions
            return new Texture(reader, requestRGBA, flipU: false, flipV: true);
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
            if (width <= 0 || height <= 0)
                return new Vec3(1.0, 1.0, 1.0);

            if (isDynamic && dynamicReader != null)
            {
                // Sample from live frame pointer (BGR/BGRA order)
                IntPtr basePtr = dynamicReader.GetCurrentFramePtr();
                float uu = flipU ? (1.0f - u) : u;
                float vv = flipV ? (1.0f - v) : v;
                float dFx = Frac(uu) * (width - 1);
                float dFy = Frac(vv) * (height - 1);
                int dX0 = (int)MathF.Floor(dFx);
                int dY0 = (int)MathF.Floor(dFy);
                int dX1 = (dX0 + 1) >= width ? (width - 1) : (dX0 + 1);
                int dY1 = (dY0 + 1) >= height ? (height - 1) : (dY0 + 1);
                float dTx = dFx - dX0;
                float dTy = dFy - dY0;

                LoadPixel(basePtr, width, dynamicBytesPerPixel, dX0, dY0, out float r00, out float g00, out float b00);
                LoadPixel(basePtr, width, dynamicBytesPerPixel, dX1, dY0, out float r10, out float g10, out float b10);
                LoadPixel(basePtr, width, dynamicBytesPerPixel, dX0, dY1, out float r01, out float g01, out float b01);
                LoadPixel(basePtr, width, dynamicBytesPerPixel, dX1, dY1, out float r11, out float g11, out float b11);

                float r0 = r00 * (1 - dTx) + r10 * dTx;
                float g0 = g00 * (1 - dTx) + g10 * dTx;
                float b0 = b00 * (1 - dTx) + b10 * dTx;
                float r1 = r01 * (1 - dTx) + r11 * dTx;
                float g1 = g01 * (1 - dTx) + g11 * dTx;
                float b1 = b01 * (1 - dTx) + b11 * dTx;

                return new Vec3(r0 * (1 - dTy) + r1 * dTy, g0 * (1 - dTy) + g1 * dTy, b0 * (1 - dTy) + b1 * dTy).Saturate();
            }

            if (pixels == null)
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

        private static float Frac(float x) => x - MathF.Floor(x);

        private static void LoadPixel(IntPtr basePtr, int w, int bpp, int x, int y, out float r, out float g, out float b)
        {
            int offset = (y * w + x) * bpp;
            byte bb = Marshal.ReadByte(basePtr, offset + 0);
            byte gg = Marshal.ReadByte(basePtr, offset + 1);
            byte rr = Marshal.ReadByte(basePtr, offset + 2);
            r = rr / 255.0f;
            g = gg / 255.0f;
            b = bb / 255.0f;
        }
    }
}
