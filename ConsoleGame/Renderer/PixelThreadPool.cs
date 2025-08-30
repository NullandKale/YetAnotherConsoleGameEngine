// File: PixelThreadPool.cs
using System;
using System.Threading;
using System.Collections.Concurrent;

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
            public int ThreadId;
            public CountdownEvent Done;
            public bool Stop;
        }

        private readonly Thread[] threads;
        private readonly string threadNamePrefix;
        private readonly BlockingCollection<Job>[] queues;

        private volatile bool stop;

        public int ThreadCount { get; }

        public PixelThreadPool(int threadCount = 0, string namePrefix = "PTP")
        {
            if (threadCount <= 0) threadCount = Math.Max(1, Environment.ProcessorCount);
            ThreadCount = threadCount;
            threadNamePrefix = namePrefix ?? "PTP";
            threads = new Thread[ThreadCount];
            queues = new BlockingCollection<Job>[ThreadCount];
            stop = false;

            for (int i = 0; i < ThreadCount; i++)
            {
                queues[i] = new BlockingCollection<Job>(new ConcurrentQueue<Job>());
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

            int seed = unchecked(Environment.TickCount ^ (width * 73856093) ^ (height * 19349663));
            SplitMix32 sm = new SplitMix32((uint)seed);
            int a = FindCoprimeMultiplier(N, ref sm);
            int b = PositiveMod((int)sm.Next(), N);

            using (var done = new CountdownEvent(ThreadCount))
            {
                for (int t = 0; t < ThreadCount; t++)
                {
                    Job j = new Job();
                    j.Body = body;
                    j.Width = width;
                    j.Height = height;
                    j.N = N;
                    j.A = a;
                    j.B = b;
                    j.ThreadId = t;
                    j.Done = done;
                    j.Stop = false;
                    queues[t].Add(j);
                }

                done.Wait();
            }
        }

        private void WorkerLoop(int workerId)
        {
            while (true)
            {
                Job job;
                try
                {
                    job = queues[workerId].Take();
                }
                catch (InvalidOperationException)
                {
                    break;
                }

                if (job == null)
                {
                    continue;
                }

                if (job.Stop)
                {
                    break;
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
                int step = ThreadCount;
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
                try
                {
                    job.Done.Signal();
                }
                catch
                {
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
                for (int i = 0; i < threads.Length; i++)
                {
                    try
                    {
                        queues[i].Add(new Job { Stop = true });
                    }
                    catch
                    {
                    }
                }
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
            for (int i = 0; i < queues.Length; i++)
            {
                try
                {
                    queues[i]?.Dispose();
                }
                catch
                {
                }
            }
        }
    }
}
