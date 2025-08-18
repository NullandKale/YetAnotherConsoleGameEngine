// File: FixedThreadFor.cs
using System;
using System.Threading;

namespace ConsoleGame.Threads
{
    /// <summary>
    /// Fixed thread team with a lockless Parallel.For-style API.
    /// Create once, reuse every frame. Each worker sleeps ~1ms while waiting for work.
    /// </summary>
    public sealed class FixedThreadFor : IDisposable
    {
        private readonly Thread[] threads;
        private readonly string threadNamePrefix;

        // Job publication fields (written by producer thread in For(...), read by workers)
        private Action<int> jobBody;
        private volatile int jobStart;
        private volatile int jobEnd;
        private volatile int jobNext;
        private volatile int jobRemaining;

        // Coordination
        private volatile int jobEpoch;                  // increment per job to wake workers
        private readonly ManualResetEventSlim jobDone;  // signaled when jobRemaining hits 0
        private volatile bool stop;

        public int ThreadCount { get; }

        public FixedThreadFor(int threadCount = 0, string namePrefix = "FTF")
        {
            if (threadCount <= 0) threadCount = Math.Max(1, Environment.ProcessorCount);
            ThreadCount = threadCount;
            threadNamePrefix = namePrefix ?? "FTF";
            threads = new Thread[ThreadCount];
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
        /// Parallel.For-style API: executes body(i) for i in [fromInclusive, toExclusive).
        /// Blocks until all iterations complete.
        /// </summary>
        public void For(int fromInclusive, int toExclusive, Action<int> body)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (toExclusive <= fromInclusive) return;

            // Publish job data (writes before epoch increment must be visible to workers)
            Volatile.Write(ref jobBody, body);
            Volatile.Write(ref jobStart, fromInclusive);
            Volatile.Write(ref jobEnd, toExclusive);
            Volatile.Write(ref jobNext, fromInclusive);
            Volatile.Write(ref jobRemaining, toExclusive - fromInclusive);

            jobDone.Reset();

            // Bump epoch to wake workers; this is the release barrier for the published fields.
            Interlocked.Increment(ref jobEpoch);

            // Producer participates too (work-first helps latency).
            DrainWorkLocally();

            // Wait for all workers to finish.
            jobDone.Wait();
        }

        private void WorkerLoop(int workerId)
        {
            int seenEpoch = Volatile.Read(ref jobEpoch);
            while (!Volatile.Read(ref stop))
            {
                // Wait for a new job (simple low-power wait with ~1ms sleep)
                int curEpoch = Volatile.Read(ref jobEpoch);
                if (curEpoch == seenEpoch)
                {
                    Thread.Sleep(1);
                    continue;
                }

                seenEpoch = curEpoch;

                // Drain work for this epoch
                while (true)
                {
                    int i = Interlocked.Increment(ref jobNext) - 1;
                    int end = Volatile.Read(ref jobEnd);
                    if (i >= end) break;

                    Action<int> body = Volatile.Read(ref jobBody);
                    try
                    {
                        body(i);
                    }
                    catch
                    {
                        // Swallow per-iteration exceptions to avoid deadlocking the job;
                        // you can surface/log as needed for your engine.
                    }

                    if (Interlocked.Decrement(ref jobRemaining) == 0)
                    {
                        jobDone.Set();
                    }
                }
            }
        }

        // Allow the producer thread to help complete work before waiting.
        private void DrainWorkLocally()
        {
            while (true)
            {
                int i = Interlocked.Increment(ref jobNext) - 1;
                int end = Volatile.Read(ref jobEnd);
                if (i >= end) break;

                Action<int> body = Volatile.Read(ref jobBody);
                body(i);

                if (Interlocked.Decrement(ref jobRemaining) == 0)
                {
                    jobDone.Set();
                    break;
                }
            }
        }

        public void Dispose()
        {
            if (!stop)
            {
                stop = true;
                // Nudge sleepers by bumping epoch and signaling completion.
                Interlocked.Increment(ref jobEpoch);
                jobDone.Set();
                for (int i = 0; i < threads.Length; i++)
                {
                    try { threads[i]?.Join(); } catch { }
                }
            }
            jobDone.Dispose();
        }
    }
}
