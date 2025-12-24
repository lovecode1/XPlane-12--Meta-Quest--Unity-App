using UnityEngine;

public class QuadRender : MonoBehaviour
{
    // Scene object names and sizing clamps used to find the quad and keep it comfortable to view.
    private const string QuadObjectName = "Cokpit_Quad";
    private const float DistanceMetersPerDegree = 0.01f;
    private const float MinDistanceMeters = 0.1f;
    private const float MaxDistanceMeters = 2.5f;
    private const float MinHalfHeightMeters = 0.2f;
    private const float MaxHalfHeightMeters = 2.0f;
    private const float MinHalfWidthMeters = 0.2f;
    private const float MaxHalfWidthMeters = 3.0f;
    private const float MinFovDegrees = 1f;
    private const float FovLockEpsilon = 0.01f;
    private const float VerticalCenterOffsetRatio = 0.18f;

    // Shader property IDs to avoid repeated string lookups.
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int CursorTexId = Shader.PropertyToID("_CursorTex");
    private static readonly int CursorEnabledId = Shader.PropertyToID("_CursorEnabled");
    private static readonly int CursorUVId = Shader.PropertyToID("_CursorUV");
    private static readonly int CursorSizeId = Shader.PropertyToID("_CursorSize");
    private static readonly int CursorAlphaCutoffId = Shader.PropertyToID("_CursorAlphaCutoff");
    private const float CursorAlphaDefault = 0.08f;
    private const float StretchTextureToFullyCoverFov = 1.0f;

    // Cached references and state for quad rendering and overlay data.
    private OVRCameraRig cameraRig;
    private Transform quadTransform;
    private Renderer quadRenderer;
    private Transform centerEyeAnchor;
    private MaterialPropertyBlock propertyBlock;
    private Texture lastTexture;
    private Texture initialQuadTexture;
    private bool quadLookupFailed;
    private bool isActive;
    private bool quadReady;
    private float cachedTextureAspect = 1f;
    private bool lastCursorStateActive;
    private Texture lastCursorTexture;
    private Vector2 lastCursorUV = new Vector2(0.5f, 0.5f);
    private Vector2 lastCursorSize = new Vector2(0.05f, 0.05f);
    private bool lastCursorEnabled;
    private uint lastCursorStateVersion;
    private float lastCursorAlphaCutoff;
    private Texture2D overlayTexture;
    private bool overlayTextureOwned;
    private Texture2D cursorTexture;
    private Vector2 cursorUV = new Vector2(0.5f, 0.5f);
    private Vector2 cursorSize = new Vector2(0.05f, 0.05f);
    private bool cursorEnabled;
    private uint cursorStateVersion;
    private float cursorAlphaCutoff = CursorAlphaDefault;
    private bool overlayFlipX;
    private float vFov = 96.09f * StretchTextureToFullyCoverFov;
    private float hFov = 126.35f * StretchTextureToFullyCoverFov;


    // Enables quad rendering, wiring up dependencies if they were supplied externally.
    public void Activate(OVRCameraRig rig)
    {
        if (rig != null)
        {
            cameraRig = rig;
        }

        if (isActive)
        {
            return;
        }

        isActive = true;
        enabled = true;

        quadReady = TryInitializeTransforms();
        if (quadReady)
        {
            ApplyTexture(force: true);
            UpdateQuadPose();
    }
}

    // Disables quad rendering when the quad is not needed.
    public void Deactivate()
    {
        if (!isActive)
        {
            return;
        }

        isActive = false;
        quadReady = false;
        enabled = false;
    }

    // Unity callback to reset state when the component is disabled unexpectedly.
    private void OnDisable()
    {
        if (!isActive)
        {
            quadReady = false;
        }
    }

    // Core update loop keeps the quad posed in front of the user and fed with textures.
    private void LateUpdate()
    {
        if (!isActive)
        {
            return;
        }

        bool initialized = TryInitializeTransforms();
        if (!initialized)
        {
            if (quadReady)
            {
                quadReady = false;
            }
            return;
        }

        if (!quadReady)
        {
            quadReady = true;
            ApplyTexture(force: true);
        }

        if (!TryResolveAnchor())
        {
            return;
        }

        UpdateQuadPose();
        ApplyTexture(force: false);
    }

    // Attempts to resolve the quad transform in the scene, caching the results.
    private bool TryInitializeTransforms()
    {
        if (quadTransform == null)
        {
            quadTransform = FindSceneTransform(QuadObjectName);
            if (quadTransform == null)
            {
                if (!quadLookupFailed)
                {
                MyLogs.Log("[WARN] QuadRender: Could not find Cokpit_Quad in the scene.");
                    quadLookupFailed = true;
                }
                return false;
            }

            quadLookupFailed = false;
            quadRenderer = quadTransform.GetComponentInChildren<Renderer>(true);
            if (quadRenderer == null)
            {
                MyLogs.Log("[WARN] QuadRender: Cokpit_Quad is missing a Renderer component.");
                return false;
            }

            quadTransform.gameObject.SetActive(true);
            quadRenderer.enabled = true;
            CacheInitialQuadTexture();
        }
        else if (quadRenderer == null)
        {
            quadRenderer = quadTransform.GetComponentInChildren<Renderer>(true);
            if (quadRenderer != null)
            {
                CacheInitialQuadTexture();
            }
        }

        return quadTransform != null && quadRenderer != null;
    }

    // Looks up a transform by name even if it is inactive in the hierarchy.
    private static Transform FindSceneTransform(string targetName)
    {
        GameObject activeObject = GameObject.Find(targetName);
        if (activeObject != null)
        {
            return activeObject.transform;
        }

        Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
        foreach (Transform transform in transforms)
        {
            if (transform == null || !transform.gameObject.scene.IsValid())
            {
                continue;
            }

            if (string.Equals(transform.name, targetName))
            {
                return transform;
            }
        }

        return null;
    }

    private void CacheInitialQuadTexture()
    {
        if (initialQuadTexture != null || quadRenderer == null)
        {
            return;
        }

        Material material = quadRenderer.sharedMaterial;
        if (material == null)
        {
            return;
        }

        if (material.HasProperty(MainTexId))
        {
            initialQuadTexture = material.GetTexture(MainTexId);
        }

        if (initialQuadTexture == null && material.HasProperty(BaseMapId))
        {
            initialQuadTexture = material.GetTexture(BaseMapId);
        }

        if (initialQuadTexture != null)
        {
            UpdateTextureAspect(initialQuadTexture);
        }
    }

    // Resolves the active headset anchor so the quad can follow the player.
    private bool TryResolveAnchor()
    {
        if (cameraRig == null)
        {
            cameraRig = FindFirstObjectByType<OVRCameraRig>();
        }

        if (cameraRig != null && cameraRig.centerEyeAnchor != null)
        {
            centerEyeAnchor = cameraRig.centerEyeAnchor;
            return true;
        }

        if (centerEyeAnchor != null)
        {
            return true;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            centerEyeAnchor = mainCamera.transform;
            return true;
        }

        return false;
    }

    // Positions, rotates, and scales the quad so it matches the current FOV and texture aspect.
    private void UpdateQuadPose()
    {
        if (quadTransform == null || centerEyeAnchor == null)
        {
            return;
        }

        float verticalFov = Mathf.Clamp(GetVerticalFov(), 1f, 170f);
        float horizontalFov = Mathf.Clamp(GetHorizontalFov(), 1f, 170f);
        float distance = ComputeDistanceFromFov(horizontalFov, verticalFov); // Keep quad far enough that the full image remains visible.

        Vector3 anchorForward = centerEyeAnchor.forward;
        if (anchorForward.sqrMagnitude < 1e-6f)
        {
            anchorForward = Vector3.forward;
        }
        else
        {
            anchorForward.Normalize();
        }

        Vector3 anchorUp = centerEyeAnchor.up;
        if (anchorUp.sqrMagnitude < 1e-6f)
        {
            anchorUp = Vector3.up;
        }
        else
        {
            anchorUp.Normalize();
        }

        Vector3 anchorPosition = centerEyeAnchor.position;
        Vector3 quadPosition = anchorPosition + anchorForward * distance; // Place the quad directly in front of the viewer.
        quadTransform.position = quadPosition;

        Vector3 quadForward = quadPosition - anchorPosition;
        if (quadForward.sqrMagnitude < 1e-6f)
        {
            quadForward = anchorForward;
        }
        quadTransform.rotation = Quaternion.LookRotation(quadForward.normalized, anchorUp); // Face the visible side toward the user.

        Vector2 halfExtents = ComputeQuadHalfExtents(distance, horizontalFov, verticalFov); // Compute size that matches FOV + texture aspect.
        quadTransform.localScale = new Vector3(halfExtents.x * 2f, halfExtents.y * 2f, 1f); // Apply final quad width/height.

        float verticalOffset = Mathf.Clamp(halfExtents.y * VerticalCenterOffsetRatio, 0f, MaxHalfHeightMeters);
        if (verticalOffset > 1e-4f)
        {
            quadTransform.position -= anchorUp * verticalOffset;
        }
    }

    // Pushes the latest screenshot onto the quad material via a property block.
    private void ApplyTexture(bool force)
    {
        if (quadRenderer == null)
        {
            return;
        }

        Texture overlay = overlayTexture;
        Texture resolvedTexture = overlay != null ? overlay : initialQuadTexture;

        Texture cursorTex = cursorTexture;
        Vector2 cursorUv = cursorUV;
        Vector2 cursorSz = cursorSize;
        bool cursorIsEnabled = cursorEnabled;
        uint cursorVersion = cursorStateVersion;
        float cursorCutoff = cursorAlphaCutoff;

        bool cursorAvailable = cursorTex != null && cursorIsEnabled;

        bool cursorChanged =
            cursorAvailable != lastCursorStateActive ||
            (cursorAvailable &&
             (cursorTex != lastCursorTexture ||
              cursorIsEnabled != lastCursorEnabled ||
              Mathf.Abs(cursorCutoff - lastCursorAlphaCutoff) > 1e-4f ||
              (cursorUv - lastCursorUV).sqrMagnitude > 1e-8f ||
              (cursorSz - lastCursorSize).sqrMagnitude > 1e-8f ||
              cursorVersion != lastCursorStateVersion));

        if (!force && overlay == lastTexture && !cursorChanged)
        {
            return;
        }

        bool isNewOverlay = overlay != null && overlay != lastTexture;

        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        quadRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.Clear();

        Texture mainTexture = resolvedTexture != null ? resolvedTexture : Texture2D.blackTexture;
        propertyBlock.SetTexture(MainTexId, mainTexture);
        propertyBlock.SetTexture(BaseMapId, mainTexture);

        if (cursorAvailable)
        {
            Vector2 shaderSize = new Vector2(
                Mathf.Max(0.0001f, cursorSz.x),
                Mathf.Max(0.0001f, cursorSz.y));
            // cursorUv.y - shaderSize.y: Since UV origin is bottom-left, so subtract the cursor height to shift the 
            // screen-space Y (top-down) point down to its bottom-left corner in texture space.
            float cursorBottomLeftY = Mathf.Clamp(cursorUv.y - shaderSize.y, 0f, Mathf.Max(0f, 1f - shaderSize.y));
            Vector2 shaderUV = new Vector2(
                overlayFlipX ? Mathf.Clamp01(1f - cursorUv.x - shaderSize.x) : cursorUv.x,
                cursorBottomLeftY);

            propertyBlock.SetTexture(CursorTexId, cursorTex);
            propertyBlock.SetFloat(CursorEnabledId, cursorIsEnabled ? 1f : 0f);
            propertyBlock.SetVector(CursorUVId, new Vector4(shaderUV.x, shaderUV.y, 0f, 0f));
            propertyBlock.SetVector(CursorSizeId, new Vector4(shaderSize.x, shaderSize.y, 0f, 0f));
            propertyBlock.SetFloat(CursorAlphaCutoffId, cursorCutoff);
        }
        else
        {
            propertyBlock.SetTexture(CursorTexId, Texture2D.blackTexture);
            propertyBlock.SetFloat(CursorEnabledId, 0f);
            propertyBlock.SetVector(CursorUVId, Vector4.zero);
            propertyBlock.SetVector(CursorSizeId, Vector4.zero);
            propertyBlock.SetFloat(CursorAlphaCutoffId, cursorCutoff);
        }

        quadRenderer.SetPropertyBlock(propertyBlock);
        if (isNewOverlay)
        {
            PerformanceStats.IncrementScreensRendered();
        }
        UpdateTextureAspect(overlay);
        lastTexture = overlay;
        lastCursorStateActive = cursorAvailable;
        lastCursorStateVersion = cursorAvailable ? cursorVersion : 0;
        lastCursorTexture = cursorAvailable ? cursorTex : null;
        lastCursorEnabled = cursorAvailable && cursorIsEnabled;
        lastCursorUV = cursorAvailable ? cursorUv : new Vector2(0.5f, 0.5f);
        lastCursorSize = cursorAvailable ? cursorSz : new Vector2(0.05f, 0.05f);
        lastCursorAlphaCutoff = cursorCutoff;
    }

    // Reads the horizontal FOV reported by the patch texture, falling back to a sane default.
    private float GetHorizontalFov()
    {
        return Mathf.Max(1f, hFov);
    }

    // Reads the vertical FOV reported by the patch texture, falling back when needed.
    private float GetVerticalFov()
    {
        return Mathf.Max(1f, vFov);
    }

    // Determines how far the quad should sit so the entire image remains inside the view frustum.
    private float ComputeDistanceFromFov(float horizontalFov, float verticalFov)
    {
        // Prioritize the vertical FOV so the quad always fills the top/bottom of the viewer's
        // frustum, even when the incoming imagery does not contain the entire horizontal FOV.
        float drivingFov = Mathf.Clamp(verticalFov, 1f, 170f);
        if (drivingFov <= 1.001f)
        {
            drivingFov = Mathf.Clamp(horizontalFov, 1f, 170f);
        }

        float targetHalfHeight = Mathf.Clamp(Mathf.Tan(Mathf.Deg2Rad * drivingFov * 0.5f) * 0.5f, MinHalfHeightMeters, MaxHalfHeightMeters);
        return Mathf.Clamp(targetHalfHeight / Mathf.Tan(Mathf.Deg2Rad * drivingFov * 0.5f), MinDistanceMeters, MaxDistanceMeters);
    }

    // Balances the quad width/height between the incoming texture aspect and the captured FOV.
    private Vector2 ComputeQuadHalfExtents(float distance, float horizontalFov, float verticalFov)
    {
        // We only want to make sure we do not exceed viewer vertical frustum, the horizontal view can exceed
        // the reported horizontal FOV if the texture aspect demands it.
        float verticalHalfRadians = Mathf.Deg2Rad * Mathf.Clamp(verticalFov, 1f, 170f) * 0.5f; // Clamp to avoid NaNs.
        float targetHalfHeight = Mathf.Clamp(distance * Mathf.Tan(verticalHalfRadians), MinHalfHeightMeters, MaxHalfHeightMeters);

        float textureAspect = Mathf.Clamp(GetCurrentTextureAspect(), 0.01f, 100f); // Keep ratios sane even if texture metadata is missing.
        // Let the width follow the texture aspect even if it exceeds the reported horizontal FOV.
        float targetHalfWidth = Mathf.Clamp(targetHalfHeight * textureAspect, MinHalfWidthMeters, MaxHalfWidthMeters);

        return new Vector2(targetHalfWidth, targetHalfHeight);
    }

    // Returns the cached texture aspect so pose math can stay responsive between uploads.
    private float GetCurrentTextureAspect()
    {
        return cachedTextureAspect;
    }

    // Updates the aspect cache from the texture dimensions once a new frame arrives.
    private void UpdateTextureAspect(Texture overlay)
    {
        if (overlay == null)
        {
            return;
        }

        int width = overlay.width;
        int height = overlay.height;
        if (width > 0 && height > 0)
        {
            cachedTextureAspect = Mathf.Clamp((float)width / height, 0.01f, 100f);
        }
    }

    public void SetOverlayFlip(bool flip)
    {
        overlayFlipX = flip;
    }

    public bool IsOverlayFlippedX()
    {
        return overlayFlipX;
    }

    public void UpdateOverlayTexture(Texture2D newTexture, bool takeOwnership = true)
    {
        if (newTexture == null)
        {
            MyLogs.Log("[WARN] QuadRender: Provided texture is null; overlay not updated.");
            return;
        }

        if (overlayTextureOwned && overlayTexture != null)
        {
            Destroy(overlayTexture);
        }

        overlayTexture = newTexture;
        overlayTextureOwned = takeOwnership;
    }

    public Texture2D GetOverlayTexture()
    {
        return overlayTexture;
    }

    public void UpdateCursor(Texture2D texture, Vector2 normalizedUV, Vector2 normalizedSize, bool enabled)
    {
        cursorTexture = texture;
        cursorUV = new Vector2(
            Mathf.Clamp01(normalizedUV.x),
            Mathf.Clamp01(normalizedUV.y));
        cursorSize = new Vector2(
            Mathf.Max(0.0001f, normalizedSize.x),
            Mathf.Max(0.0001f, normalizedSize.y));
        cursorEnabled = enabled && cursorTexture != null;
        cursorStateVersion++;
    }

    public bool TryGetCursorState(out Texture texture, out Vector2 uv, out Vector2 size, out bool enabled, out uint version)
    {
        texture = cursorTexture;
        uv = cursorUV;
        size = cursorSize;
        enabled = cursorEnabled && cursorTexture != null;
        version = cursorStateVersion;
        return texture != null;
    }

    public float GetCursorAlphaCutoff()
    {
        return Mathf.Clamp(cursorAlphaCutoff, 0f, 0.5f);
    }

    public void UpdateFov(float horizontalDegrees, float verticalDegrees)
    {
        if (horizontalDegrees > 0.01f)
        {
            hFov = Mathf.Clamp(horizontalDegrees, MinFovDegrees, 360f);
        }

        if (verticalDegrees > 0.01f)
        {
            vFov = Mathf.Clamp(verticalDegrees, MinFovDegrees, 360f);
        }
    }

    public void RotateOverlayLongitude(float _)
    {
        // No-op retained for backwards compatibility with previous controls.
    }

    private void OnDestroy()
    {
        if (overlayTextureOwned && overlayTexture != null)
        {
            Destroy(overlayTexture);
            overlayTexture = null;
            overlayTextureOwned = false;
        }
    }
}
