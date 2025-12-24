using System;
using UnityEngine;

internal sealed class AsyncAndroidDecodeStrategy : IFrameDecodeStrategy
{
    private readonly int maxQueueDepth;
    private FrameDecodeContext context;
    private ImageDecodeWorker worker;
    private readonly SimpleJpegDecodeStrategy fallbackStrategy;
    private bool workerUnavailableLogged;

    public AsyncAndroidDecodeStrategy(int maxPendingImageQueue)
    {
        maxQueueDepth = Mathf.Max(1, maxPendingImageQueue);
        fallbackStrategy = new SimpleJpegDecodeStrategy(maxQueueDepth);
    }

    public void Initialize(FrameDecodeContext ctx)
    {
        context = ctx ?? throw new ArgumentNullException(nameof(ctx));
        fallbackStrategy.Initialize(ctx);
#if UNITY_ANDROID && !UNITY_EDITOR
        worker = ImageDecodeWorker.TryCreateAndroidWorker(OnDecodedFrameReady, context.Profiler, maxQueueDepth);
        if (worker == null)
        {
            if (!workerUnavailableLogged)
            {
                MyLogs.Log("[WARN] AsyncAndroidDecodeStrategy: Async decoder unavailable; falling back to simple decoding.");
                workerUnavailableLogged = true;
            }
        }
        else
        {
            worker.Start();
        }
#else
        worker = null;
        if (!workerUnavailableLogged)
        {
            MyLogs.Log("[WARN] AsyncAndroidDecodeStrategy: Async decoding not supported on this platform; using simple decoder.");
            workerUnavailableLogged = true;
        }
#endif
    }

    public bool TryHandleUpload(byte[] payload)
    {
        if (payload == null || payload.Length == 0)
        {
            return false;
        }

        if (worker == null)
        {
            return fallbackStrategy.TryHandleUpload(payload);
        }

        var copy = new byte[payload.Length];
        Buffer.BlockCopy(payload, 0, copy, 0, payload.Length);
        bool accepted = worker.TryEnqueue(copy);
        if (!accepted)
        {
            context.Profiler.RecordFrameDropped();
        }
        return accepted;
    }

    private void OnDecodedFrameReady(DecodedImage decoded)
    {
        context.EnqueueMainThread(() => context.ApplyDecodedImage(decoded));
    }

    public void Dispose()
    {
        worker?.Dispose();
        worker = null;
        fallbackStrategy.Dispose();
    }
}
