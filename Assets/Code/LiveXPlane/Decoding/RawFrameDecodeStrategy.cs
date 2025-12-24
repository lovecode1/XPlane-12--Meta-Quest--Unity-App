using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

internal sealed class RawFrameDecodeStrategy : IFrameDecodeStrategy
{
    private const int HeaderSizeBytes = 8; // width + height (int32 each)

    private readonly int maxPending;
    private readonly ConcurrentQueue<byte[]> pendingImages = new ConcurrentQueue<byte[]>();
    private FrameDecodeContext context;
    private int pendingCount;
    private int drainScheduled;

    public RawFrameDecodeStrategy(int maxPendingImageQueue)
    {
        maxPending = Mathf.Max(1, maxPendingImageQueue);
    }

    public void Initialize(FrameDecodeContext ctx)
    {
        context = ctx ?? throw new ArgumentNullException(nameof(ctx));
    }

    public bool TryHandleUpload(byte[] payload)
    {
        if (context == null || payload == null || payload.Length < HeaderSizeBytes)
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
                    ProcessRawPayload(payload);
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

    private void ProcessRawPayload(byte[] payload)
    {
        context.Profiler.RecordFrameDequeued();

        if (!TryExtractPixels(payload, out int width, out int height, out byte[] pixelData))
        {
            context.Profiler.RecordDecodeFailure();
            return;
        }

        Texture2D texture = null;
        long decodeStamp = context.Profiler.Stamp();
        try
        {
            texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.LoadRawTextureData(pixelData);
            texture.Apply(false, true);
        }
        catch (Exception ex)
        {
            MyLogs.Log($"[WARN] RawFrameDecodeStrategy: Failed to load raw texture - {ex.Message}");
            context.Profiler.RecordDecodeFailure();
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
            }
            return;
        }
        finally
        {
            context.Profiler.RecordImageDecode(decodeStamp);
        }

        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        context.ApplyTexture(texture, payload.Length);
    }

    private static bool TryExtractPixels(byte[] payload, out int width, out int height, out byte[] pixelData)
    {
        width = 0;
        height = 0;
        pixelData = null;

        if (payload.Length < HeaderSizeBytes)
        {
            MyLogs.Log("[WARN] RawFrameDecodeStrategy: Payload too small for header.");
            return false;
        }

        width = BitConverter.ToInt32(payload, 0);
        height = BitConverter.ToInt32(payload, 4);
        if (width <= 0 || height <= 0)
        {
            MyLogs.Log("[WARN] RawFrameDecodeStrategy: Invalid dimensions in header.");
            return false;
        }

        long expectedPixels = (long)width * height * 4;
        if (expectedPixels <= 0 || expectedPixels > int.MaxValue)
        {
            MyLogs.Log("[WARN] RawFrameDecodeStrategy: Pixel count overflow.");
            return false;
        }

        int pixelBytes = (int)expectedPixels;
        if (payload.Length - HeaderSizeBytes < pixelBytes)
        {
            MyLogs.Log("[WARN] RawFrameDecodeStrategy: Payload smaller than expected raw data.");
            return false;
        }

        pixelData = new byte[pixelBytes];
        Buffer.BlockCopy(payload, HeaderSizeBytes, pixelData, 0, pixelBytes);
        return true;
    }

    public void Dispose()
    {
        while (pendingImages.TryDequeue(out _)) { }
        Interlocked.Exchange(ref pendingCount, 0);
        Interlocked.Exchange(ref drainScheduled, 0);
    }
}
