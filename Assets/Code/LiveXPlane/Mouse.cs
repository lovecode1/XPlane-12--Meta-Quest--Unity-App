using UnityEngine;

public class Mouse : MonoBehaviour
{
    [SerializeField] private string cursorResourcePath = "Plane_mat/icons8-cursor-50";
    [SerializeField, Range(0.005f, 0.25f)] private float cursorWidthNormalized = 0.05f;
    private QuadRender quadRender;
    private Texture2D cursorTexture;
    private Vector2 cursorSize = new Vector2(0.05f, 0.05f);
    private Vector2 lastCursorUV = new Vector2(0.5f, 0.5f);
    private bool cursorVisible;
    private int overlayWidth;
    private int overlayHeight;
    private bool initialized;

    public bool IsReady => initialized && quadRender != null && cursorTexture != null;

    public void Initialize(QuadRender quad)
    {
        quadRender = quad;
        cursorTexture = Resources.Load<Texture2D>(cursorResourcePath);
        if (cursorTexture == null)
        {
            MyLogs.Log($"[WARN] Mouse: Failed to load cursor texture at Resources/{cursorResourcePath}; cursor disabled.");
            initialized = false;
            return;
        }
        cursorTexture.wrapMode = TextureWrapMode.Clamp;
        cursorTexture.filterMode = FilterMode.Point;

        if (quadRender == null)
        {
            MyLogs.Log("[WARN] Mouse: Missing QuadRender reference; cursor disabled.");
            initialized = false;
            return;
        }

        initialized = true;

        float aspect = cursorTexture.width > 0
            ? (float)cursorTexture.height / cursorTexture.width
            : 1f;
        cursorSize = new Vector2(
            Mathf.Clamp(cursorWidthNormalized, 0.001f, 1f),
            Mathf.Clamp(cursorWidthNormalized * aspect, 0.001f, 1f));

        Texture2D overlaySnapshot = quadRender.GetOverlayTexture();
        if (overlaySnapshot != null)
        {
            overlayWidth = overlaySnapshot.width;
            overlayHeight = overlaySnapshot.height;
        }

        quadRender.UpdateCursor(cursorTexture, lastCursorUV, cursorSize, false);
    }

    public void ApplyOverlayTexture(Texture2D sourceTexture)
    {
        if (sourceTexture == null)
        {
            return;
        }

        overlayWidth = sourceTexture.width;
        overlayHeight = sourceTexture.height;

        if (quadRender != null)
        {
            quadRender.UpdateOverlayTexture(sourceTexture, true);
            if (cursorVisible && IsReady)
            {
                quadRender.UpdateCursor(cursorTexture, lastCursorUV, cursorSize, true);
            }
        }
        else
        {
            Destroy(sourceTexture);
        }
    }

    public void UpdateCursorPosition(float x, float y)
    {
        if (!IsReady)
        {
            return;
        }

        lastCursorUV = NormalizeCoordinates(x, y);
        cursorVisible = true;
        quadRender.UpdateCursor(cursorTexture, lastCursorUV, cursorSize, true);
    }

    public void HideCursor()
    {
        cursorVisible = false;
        if (IsReady && quadRender != null)
        {
            quadRender.UpdateCursor(cursorTexture, lastCursorUV, cursorSize, false);
        }
    }

    private Vector2 NormalizeCoordinates(float x, float y)
    {
        float normalizedX = x;
        float normalizedY = y;

        if (overlayWidth > 0 && Mathf.Abs(x) > 1f)
        {
            normalizedX = x / Mathf.Max(1f, overlayWidth);
        }

        if (overlayHeight > 0 && Mathf.Abs(y) > 1f)
        {
            normalizedY = y / Mathf.Max(1f, overlayHeight);
        }

        normalizedX = Mathf.Clamp01(normalizedX);
        normalizedY = Mathf.Clamp01(normalizedY);

        return new Vector2(normalizedX, normalizedY);
    }
}
