using System.Globalization;
using System.IO;
using UnityEngine;

public static class DebugScreen
{
    private const string ScreenshotFolderName = "Screenshot";
    private const string ScreenshotFileName = "Screenshot.png";
    private const string ScreenshotMouseFileName = "Screenshot_Mouse.txt";

    public static void TrySaveScreenshotAndMouseData(QuadRender quadRender)
    {
        MyLogs.Log($"TrySaveScreenshotAndMouseData: Invoked.");
        if (quadRender == null)
        {
            MyLogs.Log("DebugScreen: QuadRender unavailable; cannot save screenshot.");
            VRNotificationUI.ShowNotification("Screenshot unavailable");
            return;
        }

        Texture2D overlayTexture = quadRender.GetOverlayTexture();
        if (overlayTexture == null)
        {
            MyLogs.Log("DebugScreen: Overlay texture missing; cannot save screenshot.");
            VRNotificationUI.ShowNotification("No screenshot data yet");
            return;
        }

        Texture2D screenshotTexture = CreateReadableCopy(overlayTexture);
        if (screenshotTexture == null)
        {
            MyLogs.Log("DebugScreen: Unable to copy overlay texture; cannot save screenshot.");
            VRNotificationUI.ShowNotification("Screenshot unavailable");
            return;
        }

        ApplyCursorOverlay(screenshotTexture, quadRender);

        byte[] pngData = null;
        try
        {
            pngData = screenshotTexture.EncodeToPNG();
        }
        catch (System.Exception ex)
        {
            MyLogs.Log($"DebugScreen: Failed to encode screenshot - {ex.Message}");
        }
        finally
        {
            Object.Destroy(screenshotTexture);
        }

        if (pngData == null || pngData.Length == 0)
        {
            VRNotificationUI.ShowNotification("Screenshot encoding failed");
            return;
        }

        string folderPath = Path.Combine(Application.persistentDataPath, ScreenshotFolderName);
        try
        {
            Directory.CreateDirectory(folderPath);
        }
        catch (System.Exception ex)
        {
            MyLogs.Log($"DebugScreen: Failed to create screenshot folder - {ex.Message}");
            VRNotificationUI.ShowNotification("Cannot access Screenshot folder");
            return;
        }

        string screenshotPath = Path.Combine(folderPath, ScreenshotFileName);
        try
        {
            File.WriteAllBytes(screenshotPath, pngData);
        }
        catch (System.Exception ex)
        {
            MyLogs.Log($"DebugScreen: Failed to write screenshot - {ex.Message}");
            VRNotificationUI.ShowNotification("Screenshot save failed");
            return;
        }

        SaveMouseCoordinates(folderPath);
        VRNotificationUI.ShowNotification("Screenshot saved");
        MyLogs.Log($"Screenshot saved to {screenshotPath}");
    }

    private static void SaveMouseCoordinates(string folderPath)
    {
        string mouseFilePath = Path.Combine(folderPath, ScreenshotMouseFileName);
        string content;

        if (VRServer.TryGetLastMouseCoordinates(out Vector2 coords))
        {
            content = string.Format(
                CultureInfo.InvariantCulture,
                "X={0:F4}\nY={1:F4}",
                coords.x,
                coords.y);
        }
        else
        {
            content = "Mouse coordinates unavailable";
        }

        try
        {
            File.WriteAllText(mouseFilePath, content);
        }
        catch (System.Exception ex)
        {
            MyLogs.Log($"DebugScreen: Failed to save mouse coordinates - {ex.Message}");
        }
    }

    private static void ApplyCursorOverlay(Texture2D screenshotTexture, QuadRender quadRender)
    {
        if (screenshotTexture == null || quadRender == null)
        {
            return;
        }

        if (!quadRender.TryGetCursorState(out Texture cursorTex, out Vector2 cursorUv, out Vector2 cursorSize, out bool cursorEnabled, out uint _))
        {
            return;
        }

        if (!cursorEnabled)
        {
            return;
        }

        Texture2D cursorTexture = cursorTex as Texture2D;
        if (cursorTexture == null)
        {
            MyLogs.Log("DebugScreen: Cursor texture is not a Texture2D; skipping cursor overlay.");
            return;
        }

        Texture2D readableCursor = CreateReadableCopy(cursorTexture);
        if (readableCursor == null)
        {
            MyLogs.Log("DebugScreen: Unable to access cursor texture for screenshot overlay.");
            return;
        }

        try
        {
            float alphaCutoff = quadRender.GetCursorAlphaCutoff();
            Vector2 shaderSize = new Vector2(
                Mathf.Max(0.0001f, cursorSize.x),
                Mathf.Max(0.0001f, cursorSize.y));
            float cursorBottomLeftY = Mathf.Clamp(cursorUv.y, 0f, Mathf.Max(0f, 1f - shaderSize.y));
            Vector2 shaderUv = new Vector2(
                quadRender.IsOverlayFlippedX() ? Mathf.Clamp01(1f - cursorUv.x - shaderSize.x) : cursorUv.x,
                cursorBottomLeftY);

            BlendCursorTexture(screenshotTexture, readableCursor, shaderUv, shaderSize, alphaCutoff);
        }
        finally
        {
            Object.Destroy(readableCursor);
        }
    }

    private static void BlendCursorTexture(Texture2D targetTexture, Texture2D cursorTexture, Vector2 cursorUv, Vector2 cursorSize, float cursorAlphaCutoff)
    {
        if (targetTexture == null || cursorTexture == null)
        {
            return;
        }

        int targetWidth = targetTexture.width;
        int targetHeight = targetTexture.height;
        if (targetWidth <= 0 || targetHeight <= 0)
        {
            return;
        }

        if (cursorSize.x <= 0.00001f || cursorSize.y <= 0.00001f)
        {
            return;
        }

        Color32[] targetPixels = targetTexture.GetPixels32();
        Color32[] cursorPixels = cursorTexture.GetPixels32();
        int cursorWidth = cursorTexture.width;
        int cursorHeight = cursorTexture.height;
        if (cursorWidth <= 0 || cursorHeight <= 0)
        {
            return;
        }

        float cutoff = Mathf.Clamp01(cursorAlphaCutoff);
        float cutoffDivisor = Mathf.Max(0.0001f, 1f - cutoff);

        for (int y = 0; y < targetHeight; y++)
        {
            float uvRawY = targetHeight <= 1 ? 0f : (float)y / (targetHeight - 1);
            float cursorDomainY = (uvRawY - cursorUv.y) / cursorSize.y;
            if (cursorDomainY < 0f || cursorDomainY > 1f)
            {
                continue;
            }

            int srcY = Mathf.Clamp(Mathf.RoundToInt(cursorDomainY * (cursorHeight - 1)), 0, cursorHeight - 1);

            for (int x = 0; x < targetWidth; x++)
            {
                float uvRawX = targetWidth <= 1 ? 0f : (float)x / (targetWidth - 1);
                float cursorDomainX = (uvRawX - cursorUv.x) / cursorSize.x;
                if (cursorDomainX < 0f || cursorDomainX > 1f)
                {
                    continue;
                }

                int srcX = Mathf.Clamp(Mathf.RoundToInt(cursorDomainX * (cursorWidth - 1)), 0, cursorWidth - 1);

                Color32 cursorColor = cursorPixels[srcY * cursorWidth + srcX];
                if (cursorColor.a == 0)
                {
                    continue;
                }

                float alpha = cursorColor.a / 255f;
                alpha = Mathf.Clamp01((alpha - cutoff) / cutoffDivisor);
                if (alpha <= 0.0001f)
                {
                    continue;
                }

                int destIndex = y * targetWidth + x;
                Color32 baseColor = targetPixels[destIndex];

                float invAlpha = 1f - alpha;
                float baseAlpha = baseColor.a / 255f;
                float outAlpha = alpha + baseAlpha * invAlpha;

                float cursorR = cursorColor.r / 255f;
                float cursorG = cursorColor.g / 255f;
                float cursorB = cursorColor.b / 255f;

                float baseR = baseColor.r / 255f;
                float baseG = baseColor.g / 255f;
                float baseB = baseColor.b / 255f;

                float outR = cursorR * alpha + baseR * invAlpha;
                float outG = cursorG * alpha + baseG * invAlpha;
                float outB = cursorB * alpha + baseB * invAlpha;

                targetPixels[destIndex] = new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt(outR * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(outG * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(outB * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(outAlpha * 255f), 0, 255));
            }
        }

        targetTexture.SetPixels32(targetPixels);
        targetTexture.Apply();
    }

    private static Texture2D CreateReadableCopy(Texture2D sourceTexture)
    {
        if (sourceTexture == null)
        {
            return null;
        }

        try
        {
            Color32[] pixels = sourceTexture.GetPixels32();
            var copy = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.RGBA32, false);
            copy.SetPixels32(pixels);
            copy.Apply();
            return copy;
        }
        catch (System.Exception)
        {
            // Fall back to RenderTexture-based copy when the texture is not readable.
        }

        Texture2D fallback = CreateReadableCopyViaRenderTexture(sourceTexture);
        if (fallback == null)
        {
            MyLogs.Log($"DebugScreen: Failed to copy texture - {sourceTexture.name} is not readable and RenderTexture copy failed.");
        }
        return fallback;
    }

    private static Texture2D CreateReadableCopyViaRenderTexture(Texture2D sourceTexture)
    {
        if (sourceTexture == null)
        {
            return null;
        }

        RenderTexture previous = RenderTexture.active;
        RenderTexture temp = null;
        try
        {
            temp = RenderTexture.GetTemporary(sourceTexture.width, sourceTexture.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(sourceTexture, temp);
            RenderTexture.active = temp;

            var readable = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, sourceTexture.width, sourceTexture.height), 0, 0);
            readable.Apply();
            return readable;
        }
        catch (System.Exception ex)
        {
            MyLogs.Log($"DebugScreen: RenderTexture copy failed - {ex.Message}");
            return null;
        }
        finally
        {
            RenderTexture.active = previous;
            if (temp != null)
            {
                RenderTexture.ReleaseTemporary(temp);
            }
        }
    }
}
