using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

internal interface IImageDecoder : IDisposable
{
    bool TryDecode(byte[] encodedBytes, out DecodedImage decodedImage);
}

internal sealed class ImageDecodeWorker : IDisposable
{
    private readonly ConcurrentQueue<byte[]> jobQueue = new ConcurrentQueue<byte[]>();
    private readonly AutoResetEvent signal = new AutoResetEvent(false);
    private readonly Action<DecodedImage> deliverDecodedFrame;
    private readonly ProfilingHelper profiler;
    private readonly Func<IImageDecoder> decoderFactory;
    private readonly int maxQueueDepth;
    private Thread workerThread;
    private volatile bool running;
    private int pendingJobs;

    private ImageDecodeWorker(
        Func<IImageDecoder> decoderFactory,
        Action<DecodedImage> deliverDecoded,
        ProfilingHelper profiler,
        int maxQueueDepth)
    {
        this.decoderFactory = decoderFactory;
        deliverDecodedFrame = deliverDecoded ?? throw new ArgumentNullException(nameof(deliverDecoded));
        this.profiler = profiler ?? throw new ArgumentNullException(nameof(profiler));
        this.maxQueueDepth = Mathf.Max(1, maxQueueDepth);
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    public static ImageDecodeWorker TryCreateAndroidWorker(
        Action<DecodedImage> deliverDecoded,
        ProfilingHelper profiler,
        int maxQueueDepth)
    {
        if (!AndroidBitmapDecoder.IsSupported)
        {
            return null;
        }

        return new ImageDecodeWorker(() => new AndroidBitmapDecoder(), deliverDecoded, profiler, maxQueueDepth);
    }
#endif

    public void Start()
    {
        if (running)
        {
            return;
        }

        running = true;
        workerThread = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "VRServerImageDecoder"
        };
        workerThread.Start();
    }

    public bool TryEnqueue(byte[] data)
    {
        if (!running || data == null || data.Length == 0)
        {
            return false;
        }

        jobQueue.Enqueue(data);
        int newCount = Interlocked.Increment(ref pendingJobs);
        profiler.ObservePendingDepth(newCount);
        if (newCount > maxQueueDepth)
        {
            if (jobQueue.TryDequeue(out _))
            {
                Interlocked.Decrement(ref pendingJobs);
                profiler.RecordFrameDropped();
            }
        }

        signal.Set();
        return true;
    }

    public void Dispose()
    {
        running = false;
        signal.Set();
        try
        {
            workerThread?.Join(200);
        }
        catch (Exception)
        {
            // Suppress thread shutdown exceptions.
        }
        workerThread = null;
        signal.Dispose();
        while (jobQueue.TryDequeue(out _))
        {
            // release queued payloads
        }
    }

    private void WorkerLoop()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        bool attached = false;
        try
        {
            AndroidJNI.AttachCurrentThread();
            attached = true;
#else
        try
        {
#endif
            using (IImageDecoder decoder = decoderFactory?.Invoke())
            {
                while (running)
                {
                    DrainJobs(decoder);
                    signal.WaitOne(5);
                }
                DrainJobs(decoder);
            }
        }
        finally
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (attached)
            {
                AndroidJNI.DetachCurrentThread();
            }
#endif
        }
    }

    private void DrainJobs(IImageDecoder decoder)
    {
        while (running && jobQueue.TryDequeue(out var payload))
        {
            try
            {
                if (decoder == null)
                {
                    profiler.RecordDecodeFailure();
                    continue;
                }

                profiler.RecordFrameDequeued();
                long decodeStamp = profiler.Stamp();
                bool success = decoder.TryDecode(payload, out DecodedImage decoded) && decoded.IsValid;
                profiler.RecordImageDecode(decodeStamp);

                if (success)
                {
                    deliverDecodedFrame?.Invoke(decoded);
                }
                else
                {
                    profiler.RecordDecodeFailure();
                }
            }
            finally
            {
                Interlocked.Decrement(ref pendingJobs);
            }
        }
    }
}

#if UNITY_ANDROID && !UNITY_EDITOR
internal sealed class AndroidBitmapDecoder : IImageDecoder
{
    private readonly AndroidJavaClass bitmapFactory = new AndroidJavaClass("android.graphics.BitmapFactory");
    private readonly AndroidJavaClass bitmapConfigClass = new AndroidJavaClass("android.graphics.Bitmap$Config");
    private readonly AndroidJavaObject argb8888Config;

    public AndroidBitmapDecoder()
    {
        argb8888Config = bitmapConfigClass.CallStatic<AndroidJavaObject>("valueOf", "ARGB_8888");
    }

    public static bool IsSupported => Application.platform == RuntimePlatform.Android;

    public bool TryDecode(byte[] encodedBytes, out DecodedImage decodedImage)
    {
        decodedImage = default;
        if (encodedBytes == null || encodedBytes.Length == 0)
        {
            return false;
        }

        AndroidJavaObject bitmap = null;
        try
        {
            bitmap = bitmapFactory.CallStatic<AndroidJavaObject>("decodeByteArray", encodedBytes, 0, encodedBytes.Length);
            if (bitmap == null)
            {
                return false;
            }

            int width = bitmap.Call<int>("getWidth");
            int height = bitmap.Call<int>("getHeight");
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            int pixelCount = width * height;
            int[] argbPixels = new int[pixelCount];
            bitmap.Call("getPixels", argbPixels, 0, width, 0, 0, width, height);

            var rgba = new byte[pixelCount * 4];
            for (int y = 0; y < height; y++)
            {
                int srcRow = y * width;
                int dstRow = (height - 1 - y) * width; // Flip vertically to match Unity
                for (int x = 0; x < width; x++)
                {
                    int color = argbPixels[srcRow + x];
                    byte a = (byte)((color >> 24) & 0xFF);
                    if (a == 0)
                    {
                        a = 255;
                    }
                    byte r = (byte)((color >> 16) & 0xFF);
                    byte g = (byte)((color >> 8) & 0xFF);
                    byte b = (byte)(color & 0xFF);

                    int dst = (dstRow + x) * 4;
                    rgba[dst] = r;
                    rgba[dst + 1] = g;
                    rgba[dst + 2] = b;
                    rgba[dst + 3] = a;
                }
            }

            decodedImage = new DecodedImage(rgba, width, height, encodedBytes.Length);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            bitmap?.Call("recycle");
            bitmap?.Dispose();
        }
    }

    public void Dispose()
    {
        argb8888Config?.Dispose();
        bitmapFactory?.Dispose();
        bitmapConfigClass?.Dispose();
    }
}
#endif
