// File: PixelThreadPool.cs
using System;
using System.Threading;

namespace ConsoleGame.Threads
{
    /// <summary>
    /// PixelThreadPool: runs exactly N persistent threads.
    /// For each For2D(width,height,body) call, the producer posts exactly one job per thread containing the threadId and delegate.
    /// Each worker processes its share of pixels via a bijective, randomized mapping that evenly partitions work across threads.
    /// Intended for kernels where cache locality is not critical.
    /// </summary>
    public sealed class PixelThreadPool : IDisposable
    {
        public delegate void PixelBody(int x, int y, int threadId);

        private sealed class Job
        {
            public PixelBody Body;
            public int Width;
            public int Height;
            public int N;
            public int A;
            public int B;
            public int ThreadCount;
            public int ThreadId;
            public int Epoch;
        }

        private readonly Thread[] threads;
        private readonly string threadNamePrefix;
        private readonly Job[] jobs;

        private volatile int jobEpoch;
        private volatile int jobsRemaining;
        private readonly ManualResetEventSlim jobDone;

        private volatile bool stop;

        public int ThreadCount { get; }

        public PixelThreadPool(int threadCount = 0, string namePrefix = "PTP")
        {
            if (threadCount <= 0) threadCount = Math.Max(1, Environment.ProcessorCount);
            ThreadCount = threadCount;
            threadNamePrefix = namePrefix ?? "PTP";
            threads = new Thread[ThreadCount];
            jobs = new Job[ThreadCount];
            jobDone = new ManualResetEventSlim(false);
            stop = false;

            for (int i = 0; i < ThreadCount; i++)
            {
                int workerId = i;
                threads[i] = new Thread(() => WorkerLoop(workerId));
                threads[i].IsBackground = true;
                threads[i].Name = threadNamePrefix + "-" + workerId;
                threads[i].Start();
            }
        }

        /// <summary>
        /// Executes body(x,y,threadId) for all pixels in [0,width) x [0,height) using exactly ThreadCount worker threads.
        /// The producer thread does not participate in computation; it posts exactly one job object per worker and waits.
        /// Work is evenly and randomly distributed via a per-job bijective mapping over the pixel index space.
        /// </summary>
        public void For2D(int width, int height, PixelBody body)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (width <= 0 || height <= 0) return;

            long nLong = (long)width * (long)height;
            if (nLong > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(width), "width*height must fit in Int32.");
            int N = (int)nLong;

            int epoch = unchecked(Volatile.Read(ref jobEpoch) + 1);

            int seed = unchecked(Environment.TickCount ^ (width * 73856093) ^ (height * 19349663) ^ (epoch * 83492791));
            SplitMix32 sm = new SplitMix32((uint)seed);
            int a = FindCoprimeMultiplier(N, ref sm);
            int b = PositiveMod((int)sm.Next(), N);

            Volatile.Write(ref jobsRemaining, ThreadCount);
            jobDone.Reset();

            for (int t = 0; t < ThreadCount; t++)
            {
                Job j = new Job();
                j.Body = body;
                j.Width = width;
                j.Height = height;
                j.N = N;
                j.A = a;
                j.B = b;
                j.ThreadCount = ThreadCount;
                j.ThreadId = t;
                j.Epoch = epoch;
                Volatile.Write(ref jobs[t], j);
            }

            Interlocked.Exchange(ref jobEpoch, epoch);

            jobDone.Wait();
        }

        private void WorkerLoop(int workerId)
        {
            int seenEpoch = Volatile.Read(ref jobEpoch);
            while (!Volatile.Read(ref stop))
            {
                int curEpoch = Volatile.Read(ref jobEpoch);
                if (curEpoch == seenEpoch)
                {
                    Thread.Sleep(1);
                    continue;
                }

                seenEpoch = curEpoch;

                Job job = Volatile.Read(ref jobs[workerId]);
                if (job == null || job.Epoch != curEpoch)
                {
                    continue;
                }

                ExecuteJob(job);
            }
        }

        private void ExecuteJob(Job job)
        {
            try
            {
                int width = job.Width;
                int N = job.N;
                int a = job.A;
                int b = job.B;
                int step = job.ThreadCount;
                int start = job.ThreadId;

                for (int k = start; k < N; k += step)
                {
                    int idx = PermuteLCG(k, a, b, N);
                    int y = idx / width;
                    int x = idx - y * width;
                    try
                    {
                        job.Body(x, y, job.ThreadId);
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                if (Interlocked.Decrement(ref jobsRemaining) == 0)
                {
                    jobDone.Set();
                }
            }
        }

        private static int PermuteLCG(int i, int a, int b, int n)
        {
            int r = (int)((((long)a * (long)i) + (long)b) % (long)n);
            if (r < 0) r += n;
            return r;
        }

        private static int FindCoprimeMultiplier(int n, ref SplitMix32 sm)
        {
            if (n <= 2) return 1;
            for (int tries = 0; tries < 64; tries++)
            {
                int candidate = PositiveMod((int)sm.Next() | 1, n);
                if (candidate == 0) candidate = 1;
                if (Gcd(candidate, n) == 1) return candidate;
            }
            for (int a = 1; a < n; a++)
            {
                if (Gcd(a, n) == 1) return a;
            }
            return 1;
        }

        private static int Gcd(int a, int b)
        {
            a = Math.Abs(a);
            b = Math.Abs(b);
            if (a == 0) return b;
            if (b == 0) return a;
            while (b != 0)
            {
                int t = a % b;
                a = b;
                b = t;
            }
            return a;
        }

        private static int PositiveMod(int x, int m)
        {
            int r = x % m;
            if (r < 0) r += m;
            return r;
        }

        private struct SplitMix32
        {
            private uint state;
            public SplitMix32(uint seed)
            {
                state = seed;
            }
            public uint Next()
            {
                uint z = unchecked(state += 0x9E3779B9u);
                z = unchecked((z ^ (z >> 16)) * 0x85EBCA6Bu);
                z = unchecked((z ^ (z >> 13)) * 0xC2B2AE35u);
                z = unchecked(z ^ (z >> 16));
                return z;
            }
        }

        public void Dispose()
        {
            if (!stop)
            {
                stop = true;
                Interlocked.Increment(ref jobEpoch);
                jobDone.Set();
                for (int i = 0; i < threads.Length; i++)
                {
                    try
                    {
                        threads[i]?.Join();
                    }
                    catch
                    {
                    }
                }
            }
            jobDone.Dispose();
        }
    }
}
