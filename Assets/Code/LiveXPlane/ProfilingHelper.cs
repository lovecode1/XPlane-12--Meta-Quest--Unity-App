using System.Globalization;
using System.Text;
using System.Threading;
using UnityEngine;
using System.Diagnostics;

internal sealed class ProfilingHelper
{
    private readonly bool profilingEnabled;
    private readonly float logIntervalSeconds;
    private readonly ProfilingBucket uploadEnqueueBucket = new ProfilingBucket("Upload enqueue");
    private readonly ProfilingBucket drainBucket = new ProfilingBucket("Drain loop");
    private readonly ProfilingBucket decodeBucket = new ProfilingBucket("Image decode");
    private readonly ProfilingBucket overlayBucket = new ProfilingBucket("Overlay apply");

    private int framesQueued;
    private int framesDequeued;
    private int framesApplied;
    private int framesDropped;
    private int decodeFailures;
    private int maxPendingDepth;
    private float nextLogTime;
    private bool logTimerInitialized;

    public ProfilingHelper(bool profilingEnabled, float logIntervalSeconds)
    {
        this.profilingEnabled = profilingEnabled;
        this.logIntervalSeconds = Mathf.Max(1f, logIntervalSeconds);
    }

    public long Stamp()
    {
        return profilingEnabled ? Stopwatch.GetTimestamp() : 0;
    }

    public void RecordUploadEnqueue(long stamp)
    {
        Record(uploadEnqueueBucket, stamp);
    }

    public void RecordDrainLoop(long stamp)
    {
        Record(drainBucket, stamp);
    }

    public void RecordImageDecode(long stamp)
    {
        Record(decodeBucket, stamp);
    }

    public void RecordOverlayApply(long stamp)
    {
        Record(overlayBucket, stamp);
    }

    public void RecordFrameQueued()
    {
        if (!profilingEnabled)
        {
            return;
        }

        Interlocked.Increment(ref framesQueued);
    }

    public void RecordFrameDequeued()
    {
        if (!profilingEnabled)
        {
            return;
        }

        Interlocked.Increment(ref framesDequeued);
    }

    public void RecordFrameApplied()
    {
        if (!profilingEnabled)
        {
            return;
        }

        Interlocked.Increment(ref framesApplied);
    }

    public void RecordFrameDropped()
    {
        if (!profilingEnabled)
        {
            return;
        }

        Interlocked.Increment(ref framesDropped);
    }

    public void RecordDecodeFailure()
    {
        if (!profilingEnabled)
        {
            return;
        }

        Interlocked.Increment(ref decodeFailures);
    }

    public void ObservePendingDepth(int depth)
    {
        if (!profilingEnabled)
        {
            return;
        }

        int snapshot;
        do
        {
            snapshot = Volatile.Read(ref maxPendingDepth);
            if (depth <= snapshot)
            {
                return;
            }
        } while (Interlocked.CompareExchange(ref maxPendingDepth, depth, snapshot) != snapshot);
    }

    public void MaybeLog()
    {
        if (!profilingEnabled)
        {
            return;
        }

        float now = Time.realtimeSinceStartup;
        if (!logTimerInitialized)
        {
            nextLogTime = now + logIntervalSeconds;
            logTimerInitialized = true;
        }

        if (now < nextLogTime)
        {
            return;
        }

        nextLogTime = now + logIntervalSeconds;

        if (!HasAnySamples())
        {
            return;
        }

        int queued = Interlocked.Exchange(ref framesQueued, 0);
        int dequeued = Interlocked.Exchange(ref framesDequeued, 0);
        int applied = Interlocked.Exchange(ref framesApplied, 0);
        int dropped = Interlocked.Exchange(ref framesDropped, 0);
        int failedDecodes = Interlocked.Exchange(ref decodeFailures, 0);
        int peakDepth = Interlocked.Exchange(ref maxPendingDepth, 0);

        var builder = new StringBuilder();
        builder.Append("VRServer profiling (last ");
        builder.Append(logIntervalSeconds.ToString("F0", CultureInfo.InvariantCulture));
        builder.Append("s) queued=");
        builder.Append(queued);
        builder.Append(", dequeued=");
        builder.Append(dequeued);
        builder.Append(", applied=");
        builder.Append(applied);
        builder.Append(", dropped=");
        builder.Append(dropped);
        builder.Append(", decodeFailures=");
        builder.Append(failedDecodes);
        builder.Append(", maxQueueDepth=");
        builder.Append(peakDepth);
        builder.Append(" | ");
        builder.Append(uploadEnqueueBucket.BuildSummary());
        builder.Append(" | ");
        builder.Append(drainBucket.BuildSummary());
        builder.Append(" | ");
        builder.Append(decodeBucket.BuildSummary());
        builder.Append(" | ");
        builder.Append(overlayBucket.BuildSummary());

        MyLogs.Log(builder.ToString());
        ResetBuckets();
    }

    private void Record(ProfilingBucket bucket, long startStamp)
    {
        if (!profilingEnabled || startStamp == 0)
        {
            return;
        }

        long elapsed = Stopwatch.GetTimestamp() - startStamp;
        bucket.AddSample(elapsed);
    }

    private bool HasAnySamples()
    {
        return uploadEnqueueBucket.HasSamples ||
               drainBucket.HasSamples ||
               decodeBucket.HasSamples ||
               overlayBucket.HasSamples ||
               Volatile.Read(ref framesQueued) > 0 ||
               Volatile.Read(ref framesDequeued) > 0 ||
               Volatile.Read(ref framesApplied) > 0 ||
               Volatile.Read(ref framesDropped) > 0 ||
               Volatile.Read(ref decodeFailures) > 0;
    }

    private void ResetBuckets()
    {
        uploadEnqueueBucket.Reset();
        drainBucket.Reset();
        decodeBucket.Reset();
        overlayBucket.Reset();
    }

    private sealed class ProfilingBucket
    {
        private readonly object sync = new object();
        private long totalTicks;
        private long maxTicks;
        private int sampleCount;

        public ProfilingBucket(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public bool HasSamples
        {
            get
            {
                lock (sync)
                {
                    return sampleCount > 0;
                }
            }
        }

        public void AddSample(long ticks)
        {
            lock (sync)
            {
                sampleCount++;
                totalTicks += ticks;
                if (ticks > maxTicks)
                {
                    maxTicks = ticks;
                }
            }
        }

        public string BuildSummary()
        {
            lock (sync)
            {
                if (sampleCount == 0)
                {
                    return $"{Name}: no samples";
                }

                double avgMs = (double)totalTicks / sampleCount * 1000.0 / Stopwatch.Frequency;
                double maxMs = (double)maxTicks * 1000.0 / Stopwatch.Frequency;
                return $"{Name}: avg {avgMs:F2}ms (max {maxMs:F2}ms, n={sampleCount})";
            }
        }

        public void Reset()
        {
            lock (sync)
            {
                totalTicks = 0;
                maxTicks = 0;
                sampleCount = 0;
            }
        }
    }
}
