using System;
using System.Diagnostics;
using System.Threading;
using OpenCvSharp;

namespace NullEngine.Video
{
    public interface IFrameReader : IDisposable
    {
        /// <summary>
        /// The width of the frame in pixels.
        /// </summary>
        int Width { get; }

        /// <summary>
        /// The height of the frame in pixels.
        /// </summary>
        int Height { get; }

        /// <summary>
        /// The frames per second.
        /// </summary>
        double Fps { get; }

        /// <summary>
        /// Indicates if the reader has looped back to the beginning.
        /// </summary>
        bool HasLooped { get; }

        /// <summary>
        /// In single-frame mode, advances to the next frame. In automatic mode, this may be a no-op.
        /// </summary>
        void PopFrame();

        /// <summary>
        /// Returns a pointer to the current frame’s data.
        /// </summary>
        IntPtr GetCurrentFramePtr();

        void Stop();
    }

    public class AsyncCameraReader : IFrameReader
    {
        private VideoCapture capture;
        private Thread frameReadThread;
        private bool isRunning;
        private double frameIntervalMs;

        // Double buffer components.
        private Mat[] frameMats = new Mat[2];
        private int currentBufferIndex = 0;
        private object bufferLock = new object();

        // AutoResetEvents for single-frame advancement.
        private AutoResetEvent frameAdvanceEvent;
        private AutoResetEvent frameReadyEvent;

        // The camera index (e.g., 0 for default camera).
        public int CameraIndex { get; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public double Fps { get; }

        // When true, the reader only advances when PopFrame is called.
        private bool singleFrameAdvance;
        // When true, output frames are converted to RGBA.
        private bool useRGBA;

        // For a live camera, EndOfVideo is generally false.
        public bool EndOfVideo { get; private set; } = false;

        public bool HasLooped => false;

        // If > 0, we will force the aspect ratio and resize the image.
        private float forcedAspect;

        /// <summary>
        /// Original constructor (no forced aspect).
        /// </summary>
        public AsyncCameraReader(int cameraIndex, bool singleFrameAdvance = false, bool useRGBA = false)
        {
            this.singleFrameAdvance = singleFrameAdvance;
            this.useRGBA = useRGBA;
            CameraIndex = cameraIndex;

            // Open the camera device.
            capture = new VideoCapture(cameraIndex);
            if (!capture.IsOpened())
                throw new ArgumentException($"Could not open camera with index: {cameraIndex}");

            Width = capture.FrameWidth;
            Height = capture.FrameHeight;
            // Use the reported FPS, or assume 30 if unavailable.
            Fps = capture.Fps > 0 ? capture.Fps : 30;
            frameIntervalMs = 1000.0 / Fps;

            // Allocate double buffers with the desired Mat type.
            MatType matType = useRGBA ? MatType.CV_8UC4 : MatType.CV_8UC3;
            frameMats[0] = new Mat(Height, Width, matType);
            frameMats[1] = new Mat(Height, Width, matType);

            if (singleFrameAdvance)
            {
                frameAdvanceEvent = new AutoResetEvent(false);
                frameReadyEvent = new AutoResetEvent(false);
            }

            isRunning = true;
            frameReadThread = new Thread(FrameReadLoop);
            frameReadThread.Start();
        }

        /// <summary>
        /// New constructor that optionally forces the aspect ratio of the output.
        /// When forcedAspect > 0, we scale the frame to match that aspect ratio.
        /// </summary>
        public AsyncCameraReader(int cameraIndex, float forcedAspect, bool singleFrameAdvance = false, bool useRGBA = false)
        {
            this.singleFrameAdvance = singleFrameAdvance;
            this.useRGBA = useRGBA;
            this.forcedAspect = forcedAspect;
            CameraIndex = cameraIndex;

            // Open the camera device.
            capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.DSHOW);
            if (!capture.IsOpened())
                throw new ArgumentException($"Could not open camera with index: {cameraIndex}");

            // Use the raw camera dimensions first.
            int rawWidth = capture.FrameWidth;
            int rawHeight = capture.FrameHeight;

            // Use the reported FPS, or assume 30 if unavailable.
            Fps = capture.Fps > 0 ? capture.Fps : 30;
            frameIntervalMs = 1000.0 / Fps;

            // If forcedAspect <= 0, behave exactly like the original constructor.
            if (forcedAspect <= 0.0f)
            {
                Width = rawWidth;
                Height = rawHeight;
            }
            else
            {
                // Force the new output width/height to keep the requested aspect:
                // aspect = width / height  =>  height = width / aspect
                // We'll keep the same camera width and force the height for simplicity.
                Width = rawWidth;
                Height = (int)(rawWidth / forcedAspect);
            }

            // Allocate double buffers with the desired Mat type at the forced resolution.
            MatType matType = useRGBA ? MatType.CV_8UC4 : MatType.CV_8UC3;
            frameMats[0] = new Mat(Height, Width, matType);
            frameMats[1] = new Mat(Height, Width, matType);

            if (singleFrameAdvance)
            {
                frameAdvanceEvent = new AutoResetEvent(false);
                frameReadyEvent = new AutoResetEvent(false);
            }

            isRunning = true;
            frameReadThread = new Thread(FrameReadLoop);
            frameReadThread.Start();
        }

        private void FrameReadLoop()
        {
            if (singleFrameAdvance)
            {
                // Single-frame mode: wait for a signal to advance.
                while (isRunning)
                {
                    frameAdvanceEvent.WaitOne();
                    if (!isRunning)
                        break;

                    int nextBufferIndex = 1 - currentBufferIndex;
                    Mat targetMat = frameMats[nextBufferIndex];

                    using (Mat temp = new Mat())
                    {
                        bool frameRead = capture.Read(temp);
                        if (!frameRead)
                        {
                            // In case of read error, simply continue.
                            continue;
                        }

                        // If forcedAspect > 0, we resize to the forced resolution.
                        if (forcedAspect > 0.0f)
                        {
                            Cv2.Resize(temp, temp, new Size(Width, Height));
                        }

                        if (useRGBA)
                        {
                            Cv2.CvtColor(temp, targetMat, ColorConversionCodes.RGB2BGRA);
                        }
                        else
                        {
                            temp.CopyTo(targetMat);
                        }

                        lock (bufferLock)
                        {
                            currentBufferIndex = nextBufferIndex;
                        }
                    }
                    // Signal that the new frame is ready.
                    frameReadyEvent.Set();
                }
            }
            else
            {
                // Automatic mode: advance frames at a fixed interval.
                var timer = Stopwatch.StartNew();
                long nextFrameTime = 0;

                while (isRunning)
                {
                    long currentTime = timer.ElapsedMilliseconds;
                    if (currentTime >= nextFrameTime)
                    {
                        int nextBufferIndex = 1 - currentBufferIndex;
                        Mat targetMat = frameMats[nextBufferIndex];

                        using (Mat temp = new Mat())
                        {
                            bool frameRead = capture.Read(temp);
                            if (!frameRead)
                            {
                                // If reading fails, try again.
                                continue;
                            }

                            // Resize if forced aspect is enabled.
                            if (forcedAspect > 0.0f)
                            {
                                Cv2.Resize(temp, temp, new Size(Width, Height));
                            }

                            if (useRGBA)
                            {
                                Cv2.CvtColor(temp, targetMat, ColorConversionCodes.BGR2RGBA);
                            }
                            else
                            {
                                temp.CopyTo(targetMat);
                            }

                            lock (bufferLock)
                            {
                                currentBufferIndex = nextBufferIndex;
                            }
                            nextFrameTime = currentTime + (long)frameIntervalMs;
                        }
                    }

                    long sleepTime = nextFrameTime - timer.ElapsedMilliseconds;
                    if (sleepTime > 0)
                    {
                        Thread.Sleep((int)Math.Max(1, sleepTime));
                    }
                }
            }
        }

        /// <summary>
        /// In single-frame mode, signals the reader to advance to the next frame and blocks until the frame is loaded.
        /// In automatic mode, this method is a no-op.
        /// </summary>
        public void PopFrame()
        {
            if (singleFrameAdvance)
            {
                frameAdvanceEvent.Set();
                // Block until the frame is loaded.
                frameReadyEvent.WaitOne();
            }
        }

        /// <summary>
        /// Returns a pointer to the data of the current frame.
        /// </summary>
        public nint GetCurrentFramePtr()
        {
            lock (bufferLock)
            {
                return frameMats[currentBufferIndex].Data;
            }
        }

        public void Dispose()
        {
            isRunning = false;
            if (singleFrameAdvance)
            {
                // Signal to unblock the waiting thread.
                frameAdvanceEvent.Set();
            }

            if (frameReadThread != null && frameReadThread.IsAlive)
            {
                frameReadThread.Join(1000);
                if (frameReadThread.IsAlive)
                    frameReadThread.Abort();
            }

            capture?.Dispose();
            frameMats[0]?.Dispose();
            frameMats[1]?.Dispose();
            frameAdvanceEvent?.Dispose();
            frameReadyEvent?.Dispose();
        }

        public void Stop()
        {
            // do nothing for cameras
        }
    }
}
