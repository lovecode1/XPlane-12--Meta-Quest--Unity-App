using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

internal sealed class AstcFrameDecodeStrategy : IFrameDecodeStrategy
{
    private readonly int maxPending;
    private readonly ConcurrentQueue<byte[]> pendingImages = new ConcurrentQueue<byte[]>();
    private FrameDecodeContext context;
    private int pendingCount;
    private int drainScheduled;
    private bool formatSupportLogged;
    private bool formatSupported;

    public AstcFrameDecodeStrategy(int maxPendingImageQueue)
    {
        maxPending = Mathf.Max(1, maxPendingImageQueue);
    }

    public void Initialize(FrameDecodeContext ctx)
    {
        context = ctx ?? throw new ArgumentNullException(nameof(ctx));
        formatSupported = SystemInfo.SupportsTextureFormat(TextureFormat.ASTC_4x4);
        if (!formatSupported && !formatSupportLogged)
        {
            MyLogs.Log("[WARN] AstcFrameDecodeStrategy: Device does not support ASTC_4x4 textures.");
            formatSupportLogged = true;
        }
    }

    public bool TryHandleUpload(byte[] payload)
    {
        if (context == null || payload == null || payload.Length < 8) // need header + minimal data
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
                    ProcessAstcPayload(payload);
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

    private void ProcessAstcPayload(byte[] payload)
    {
        if (!formatSupported)
        {
            return;
        }

        context.Profiler.RecordFrameDequeued();

        if (!TryParseHeader(payload, out int width, out int height, out int payloadOffset, out int blockWidth, out int blockHeight))
        {
            MyLogs.Log("[WARN] AstcFrameDecodeStrategy: Invalid ASTC payload header.");
            context.Profiler.RecordDecodeFailure();
            return;
        }

        int expectedBytes = CalculateAstcByteLength(width, height, blockWidth, blockHeight);
        if (payload.Length - payloadOffset < expectedBytes)
        {
            MyLogs.Log("[WARN] AstcFrameDecodeStrategy: Payload smaller than expected ASTC data.");
            context.Profiler.RecordDecodeFailure();
            return;
        }

        var textureData = new byte[expectedBytes];
        Buffer.BlockCopy(payload, payloadOffset, textureData, 0, expectedBytes);

        Texture2D texture = null;
        long decodeStamp = context.Profiler.Stamp();
        try
        {
            texture = new Texture2D(width, height, TextureFormat.ASTC_4x4, false);
            texture.LoadRawTextureData(textureData);
            texture.Apply(false, true);
        }
        catch (Exception ex)
        {
            MyLogs.Log($"[WARN] AstcFrameDecodeStrategy: Failed to load ASTC texture - {ex.Message}");
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

        context.ApplyTexture(texture, expectedBytes);
    }

    private static bool TryParseHeader(byte[] payload, out int width, out int height, out int offset, out int blockWidth, out int blockHeight)
    {
        width = 0;
        height = 0;
        offset = 0;
        blockWidth = 4;
        blockHeight = 4;

        if (payload.Length >= 16 &&
            payload[0] == 0x13 &&
            payload[1] == 0xAB &&
            payload[2] == 0xA1 &&
            payload[3] == 0x5C)
        {
            blockWidth = Mathf.Max(4, payload[4]);
            blockHeight = Mathf.Max(4, payload[5]);
            int dimX = payload[7] | (payload[8] << 8) | (payload[9] << 16);
            int dimY = payload[10] | (payload[11] << 8) | (payload[12] << 16);

            width = Mathf.Max(1, dimX);
            height = Mathf.Max(1, dimY);
            offset = 16;
            return true;
        }

        if (payload.Length >= 8)
        {
            width = BitConverter.ToInt32(payload, 0);
            height = BitConverter.ToInt32(payload, 4);
            offset = 8;
            return width > 0 && height > 0;
        }

        return false;
    }

    private static int CalculateAstcByteLength(int width, int height, int blockWidth, int blockHeight)
    {
        int clampedBlockWidth = Mathf.Clamp(blockWidth, 4, 12);
        int clampedBlockHeight = Mathf.Clamp(blockHeight, 4, 12);
        int blocksX = Mathf.CeilToInt(width / (float)clampedBlockWidth);
        int blocksY = Mathf.CeilToInt(height / (float)clampedBlockHeight);
        return Mathf.Max(1, blocksX * blocksY) * 16;
    }

    public void Dispose()
    {
        while (pendingImages.TryDequeue(out _)) { }
        Interlocked.Exchange(ref pendingCount, 0);
        Interlocked.Exchange(ref drainScheduled, 0);
    }
}
