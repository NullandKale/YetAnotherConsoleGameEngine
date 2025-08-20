using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using OpenCvSharp; // Only used here for metadata extraction

namespace NullEngine.Video
{
    public static class WindowsJob
    {
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern nint CreateJobObject(nint lpJobAttributes, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(nint hJob, JOBOBJECTINFOCLASS infoClass,
            nint lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(nint hJob, nint hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool TerminateProcess(nint hProcess, uint uExitCode);

        private enum JOBOBJECTINFOCLASS
        {
            BasicLimitInformation = 2,
            ExtendedLimitInformation = 9,
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public nuint MinimumWorkingSetSize;
            public nuint MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public nint Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public nuint ProcessMemoryLimit;
            public nuint JobMemoryLimit;
            public nuint PeakProcessMemoryUsed;
            public nuint PeakJobMemoryUsed;
        }

        private static readonly nint jobHandle;

        static WindowsJob()
        {
            jobHandle = CreateJobObject(nint.Zero, null);

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

            int length = Marshal.SizeOf(info);
            nint infoPtr = Marshal.AllocHGlobal(length);
            Marshal.StructureToPtr(info, infoPtr, false);

            SetInformationJobObject(jobHandle, JOBOBJECTINFOCLASS.ExtendedLimitInformation,
                infoPtr, (uint)length);

            Marshal.FreeHGlobal(infoPtr);
        }

        public static void AddProcess(Process process)
        {
            if (process != null && !process.HasExited)
            {
                AssignProcessToJobObject(jobHandle, process.Handle);
            }
        }
    }
}


namespace NullEngine.Video
{
    public class AsyncFfmpegVideoReader : IFrameReader, IDisposable
    {
        private Thread frameReadThread;
        private bool isRunning;
        private bool isPaused;
        private volatile bool hasLooped;

        private readonly bool singleFrameAdvance;
        private AutoResetEvent frameAdvanceEvent;
        private AutoResetEvent frameReadyEvent;

        private readonly object bufferLock = new object();
        private readonly IntPtr[] frameBuffers = new IntPtr[2];
        private int currentBufferIndex = 0;

        private Process ffmpegProcess;
        private Stream ffmpegStdOut;
        private byte[] readBuffer;

        public string VideoFile { get; }
        public int Width { get; }
        public int Height { get; }
        public double Fps { get; }

        private readonly int bytesPerPixel;
        private readonly int frameBytes;

        private double frameIntervalMs;
        private Stopwatch timer;
        private double nextFrameTime;

        public bool HasLooped => hasLooped;

        public AsyncFfmpegVideoReader(
            string videoFile,
            bool singleFrameAdvance = false,
            bool useRGBA = false,
            bool playAudio = true)
        {
            VideoFile = videoFile;

            // Use OpenCV only to extract metadata.
            using (var tmpCap = new VideoCapture(videoFile, VideoCaptureAPIs.FFMPEG))
            {
                if (!tmpCap.IsOpened())
                    throw new ArgumentException($"Could not open video file: {videoFile}");
                Width = tmpCap.FrameWidth;
                Height = tmpCap.FrameHeight;
                Fps = tmpCap.Fps;
            }

            this.singleFrameAdvance = singleFrameAdvance;
            // Determine pixel format and bytes per pixel.
            // Note: "bgra" is used if useRGBA is true; otherwise, "bgr24".
            string ffmpegPixFmt = useRGBA ? "bgra" : "bgr24";
            bytesPerPixel = useRGBA ? 4 : 3;
            frameBytes = Width * Height * bytesPerPixel;

            // Allocate two unmanaged buffers for double-buffering.
            frameBuffers[0] = Marshal.AllocHGlobal(frameBytes);
            frameBuffers[1] = Marshal.AllocHGlobal(frameBytes);

            if (singleFrameAdvance)
            {
                frameAdvanceEvent = new AutoResetEvent(false);
                frameReadyEvent = new AutoResetEvent(false);
            }

            ffmpegProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg.exe",
                    Arguments = $"-hwaccel cuda -i \"{videoFile}\" -f rawvideo -pix_fmt {ffmpegPixFmt} pipe:1",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            ffmpegProcess.ErrorDataReceived += (sender, e) => { /* Optional logging */ };
            ffmpegProcess.Start();
            WindowsJob.AddProcess(ffmpegProcess);
            ffmpegProcess.BeginErrorReadLine();
            ffmpegStdOut = ffmpegProcess.StandardOutput.BaseStream;

            readBuffer = new byte[frameBytes];

            isRunning = true;
            frameReadThread = new Thread(FrameReadLoop) { IsBackground = true };
            frameReadThread.Start();

            if (!singleFrameAdvance)
            {
                frameIntervalMs = 1000.0 / Fps;
                timer = Stopwatch.StartNew();
                nextFrameTime = 0;
            }
        }

        private void FrameReadLoop()
        {
            if (singleFrameAdvance)
            {
                while (isRunning)
                {
                    frameAdvanceEvent.WaitOne();
                    if (!isRunning) break;
                    if (!ReadOneFrame(out bool looped))
                    {
                        LoopOrBreak();
                    }
                    else if (looped)
                    {
                        hasLooped = true;
                    }
                    frameReadyEvent.Set();
                }
            }
            else
            {
                if (timer == null)
                    timer = Stopwatch.StartNew();
                double nextFrameTimestamp = timer.Elapsed.TotalMilliseconds;
                double frameDuration = 1000.0 / Fps;
                while (isRunning)
                {
                    if (isPaused)
                    {
                        Thread.Sleep(10);
                        continue;
                    }
                    double currentMs = timer.Elapsed.TotalMilliseconds;
                    if (currentMs < nextFrameTimestamp)
                    {
                        double remaining = nextFrameTimestamp - currentMs;
                        if (remaining > 2.0)
                        {
                            Thread.Sleep((int)(remaining - 1));
                        }
                        else
                        {
                            Thread.SpinWait(50);
                        }
                        continue;
                    }
                    if (!ReadOneFrame(out bool looped))
                    {
                        LoopOrBreak();
                    }
                    else if (looped)
                    {
                        hasLooped = true;
                    }
                    nextFrameTimestamp += frameDuration;
                }
            }
        }

        private bool ReadOneFrame(out bool looped)
        {
            looped = false;
            int totalRead = 0;
            while (totalRead < frameBytes)
            {
                int n = ffmpegStdOut.Read(readBuffer, totalRead, frameBytes - totalRead);
                if (n <= 0)
                {
                    return false;
                }
                totalRead += n;
            }
            int nextBufferIndex = 1 - currentBufferIndex;
            // Copy the frame data from the managed buffer to the unmanaged buffer.
            Marshal.Copy(readBuffer, 0, frameBuffers[nextBufferIndex], frameBytes);
            lock (bufferLock)
            {
                currentBufferIndex = nextBufferIndex;
            }
            return totalRead == frameBytes;
        }

        private void LoopOrBreak()
        {
            try
            {
                ffmpegProcess?.Kill();
            }
            catch
            {
            }

            ffmpegProcess?.Dispose();
            hasLooped = true;
            var info = ffmpegProcess?.StartInfo;
            if (info == null) return;
            ffmpegProcess = new Process { StartInfo = info };
            ffmpegProcess.ErrorDataReceived += (sender, e) => { };
            ffmpegProcess.Start();
            WindowsJob.AddProcess(ffmpegProcess);
            ffmpegProcess.BeginErrorReadLine();
            ffmpegStdOut = ffmpegProcess.StandardOutput.BaseStream;
        }

        public void PopFrame()
        {
            if (!singleFrameAdvance)
                return;
            frameAdvanceEvent.Set();
            frameReadyEvent.WaitOne();
        }

        // Returns a pointer to the current frame's data.
        public nint GetCurrentFramePtr()
        {
            lock (bufferLock)
            {
                return frameBuffers[currentBufferIndex];
            }
        }

        public void Play()
        {
            if (!singleFrameAdvance)
                isPaused = false;
        }

        public void Pause()
        {
            if (!singleFrameAdvance)
                isPaused = true;
        }

        /// <summary>
        /// Stops audio playback by invoking the audio player's ForceKill.
        /// </summary>
        public void Stop()
        {
            if (!singleFrameAdvance)
            {
                Pause();
            }
        }

        public void Dispose()
        {
            isRunning = false;
            if (singleFrameAdvance)
            {
                frameAdvanceEvent.Set();
            }
            if (frameReadThread != null && frameReadThread.IsAlive)
            {
                frameReadThread.Join(1000);
                if (frameReadThread.IsAlive)
                {
#pragma warning disable SYSLIB0003
                    frameReadThread.Abort();
#pragma warning restore SYSLIB0003
                }
            }

            try
            {
                ffmpegStdOut?.Close();
                if (ffmpegProcess != null && !ffmpegProcess.HasExited)
                {
                    ffmpegProcess.Kill();
                    ffmpegProcess.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Dispose(): Exception on ffmpeg shutdown: " + ex);
            }
            ffmpegProcess?.Dispose();

            // Free unmanaged frame buffers.
            for (int i = 0; i < frameBuffers.Length; i++)
            {
                if (frameBuffers[i] != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(frameBuffers[i]);
                    frameBuffers[i] = IntPtr.Zero;
                }
            }
            frameAdvanceEvent?.Dispose();
            frameReadyEvent?.Dispose();
        }
    }
}
