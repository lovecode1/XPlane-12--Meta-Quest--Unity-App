using System.Threading;

public static class PerformanceStats
{
    private static int receivedScreens;
    private static int renderedScreens;
    private static long receivedImageBytes;

    public static void IncrementScreensReceived()
    {
        Interlocked.Increment(ref receivedScreens);
    }

    public static void IncrementScreensRendered()
    {
        Interlocked.Increment(ref renderedScreens);
    }

    public static void AddReceivedImageBytes(int byteCount)
    {
        if (byteCount <= 0)
        {
            return;
        }

        Interlocked.Add(ref receivedImageBytes, byteCount);
    }

    public static (int received, int rendered, long imageBytes) ConsumeScreenCounts()
    {
        int received = Interlocked.Exchange(ref receivedScreens, 0);
        int rendered = Interlocked.Exchange(ref renderedScreens, 0);
        long imageBytes = Interlocked.Exchange(ref receivedImageBytes, 0);
        return (received, rendered, imageBytes);
    }
}
