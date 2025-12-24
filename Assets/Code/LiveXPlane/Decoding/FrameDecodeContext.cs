using System;
using UnityEngine;

internal sealed class FrameDecodeContext
{
    private readonly Action<Action> enqueueOnMainThread;
    private readonly ProfilingHelper profilingHelper;
    private QuadRender quadRender;
    private Mouse mouseController;

    public FrameDecodeContext(ProfilingHelper profiler, Action<Action> enqueueAction)
    {
        profilingHelper = profiler ?? throw new ArgumentNullException(nameof(profiler));
        enqueueOnMainThread = enqueueAction ?? throw new ArgumentNullException(nameof(enqueueAction));
    }

    public ProfilingHelper Profiler => profilingHelper;

    public void UpdateTargets(QuadRender quad, Mouse mouse)
    {
        quadRender = quad;
        mouseController = mouse;
    }

    public void EnqueueMainThread(Action action)
    {
        if (action == null)
        {
            return;
        }

        enqueueOnMainThread(action);
    }

    public void ApplyTexture(Texture2D texture, int sourceBytes)
    {
        if (texture == null)
        {
            return;
        }

        if (quadRender == null && (mouseController == null || !mouseController.IsReady))
        {
            UnityEngine.Object.Destroy(texture);
            return;
        }

        bool overlayComplete = false;
        long overlayStamp = profilingHelper.Stamp();
        try
        {
            if (mouseController != null && mouseController.IsReady)
            {
                mouseController.ApplyOverlayTexture(texture);
            }
            else
            {
                quadRender?.UpdateOverlayTexture(texture, true);
            }

            overlayComplete = true;
        }
        finally
        {
            profilingHelper.RecordOverlayApply(overlayStamp);
        }

        if (overlayComplete)
        {
            profilingHelper.RecordFrameApplied();
        }

        PerformanceStats.IncrementScreensReceived();
        PerformanceStats.AddReceivedImageBytes(sourceBytes);
    }

    public void ApplyDecodedImage(in DecodedImage decoded)
    {
        if (!decoded.IsValid)
        {
            profilingHelper.RecordDecodeFailure();
            return;
        }

        Texture2D texture = null;
        try
        {
            texture = new Texture2D(decoded.Width, decoded.Height, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.LoadRawTextureData(decoded.Pixels);
            texture.Apply(false, true);

            ApplyTexture(texture, decoded.SourceBytes);
            texture = null;
        }
        catch (Exception ex)
        {
            MyLogs.Log($"[WARN] FrameDecodeContext: Failed to apply decoded image - {ex.Message}");
        }
        finally
        {
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
            }
        }
    }
}
