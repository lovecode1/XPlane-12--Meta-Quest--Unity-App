using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

internal sealed class SimpleJpegDecodeStrategy : IFrameDecodeStrategy
{
    private readonly int maxPending;
    private readonly ConcurrentQueue<byte[]> pendingImages = new ConcurrentQueue<byte[]>();
    private FrameDecodeContext context;
    private int pendingCount;
    private int drainScheduled;

    public SimpleJpegDecodeStrategy(int maxPendingImageQueue)
    {
        maxPending = Mathf.Max(1, maxPendingImageQueue);
    }

    public void Initialize(FrameDecodeContext ctx)
    {
        context = ctx ?? throw new ArgumentNullException(nameof(ctx));
    }

    public bool TryHandleUpload(byte[] payload)
    {
        if (context == null || payload == null || payload.Length == 0)
        {
            return false;
        }

        var copy = new byte[payload.Length];
        Buffer.BlockCopy(payload, 0, copy, 0, payload.Length);

        pendingImages.Enqueue(copy);
        int newCount = Interlocked.Increment(ref pendingCount);
        context.Profiler.ObservePendingDepth(newCount);

        if (newCount > maxPending)
        {
            if (pendingImages.TryDequeue(out _))
            {
                Interlocked.Decrement(ref pendingCount);
                context.Profiler.RecordFrameDropped();
            }
        }

        if (Interlocked.CompareExchange(ref drainScheduled, 1, 0) == 0)
        {
            context.EnqueueMainThread(DrainPendingOnMainThread);
        }

        return true;
    }

    private void DrainPendingOnMainThread()
    {
        try
        {
            while (pendingImages.TryDequeue(out var payload))
            {
                try
                {
                    ProcessPayload(payload);
                }
                finally
                {
                    Interlocked.Decrement(ref pendingCount);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref drainScheduled, 0);
            if (!pendingImages.IsEmpty && Interlocked.CompareExchange(ref drainScheduled, 1, 0) == 0)
            {
                context.EnqueueMainThread(DrainPendingOnMainThread);
            }
        }
    }

    private void ProcessPayload(byte[] payload)
    {
        context.Profiler.RecordFrameDequeued();
        Texture2D texture = null;
        long decodeStamp = context.Profiler.Stamp();
        try
        {
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(payload))
            {
                MyLogs.Log("[WARN] SimpleJpegDecodeStrategy: Failed to decode uploaded image.");
                context.Profiler.RecordDecodeFailure();
                UnityEngine.Object.Destroy(texture);
                return;
            }
        }
        finally
        {
            context.Profiler.RecordImageDecode(decodeStamp);
        }

        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        context.ApplyTexture(texture, payload.Length);
        texture = null;
    }

    public void Dispose()
    {
        while (pendingImages.TryDequeue(out _)) { }
        Interlocked.Exchange(ref pendingCount, 0);
        Interlocked.Exchange(ref drainScheduled, 0);
    }
}
