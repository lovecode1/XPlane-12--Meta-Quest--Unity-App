using UnityEngine;

/// <summary>
/// Logs the average frame rate over fixed intervals so we can monitor performance on device.
/// </summary>
public class FpsMonitor : MonoBehaviour
{
    private const float LogIntervalSeconds = 60f;

    private float accumulatedTime;
    private int accumulatedFrames;

    private void Update()
    {
        accumulatedTime += Time.unscaledDeltaTime;
        accumulatedFrames++;

        if (accumulatedTime < LogIntervalSeconds)
        {
            return;
        }

        float avgFps = accumulatedFrames / accumulatedTime;
        var (received, rendered, imageBytes) = PerformanceStats.ConsumeScreenCounts();
        float renderPercent = received > 0 ? (rendered / (float)received) * 100f : 0f;
        float bytesPerSecond = (float)imageBytes / LogIntervalSeconds;
        float megabytesPerSecond = bytesPerSecond / (1024f * 1024f);

        MyLogs.Log($"FPS Monitor: Avg FPS {avgFps:F2} | Screens recv={received} rendered={rendered} ({renderPercent:F0}%) | Data {megabytesPerSecond:F2} MB/s");

        accumulatedTime = 0f;
        accumulatedFrames = 0;
    }
}
