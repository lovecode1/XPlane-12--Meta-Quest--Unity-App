using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LiveXPlaneUI : MonoBehaviour
{
    private const float PoseUiHorizontalOffset = 0f;
    private const float PoseUiVerticalOffset = 0.12f;
    private const float PoseUiForwardOffset = 0.32f;
    private const float PoseUiScale = 0.0009f;
    private const string PoseUiObjectName = "XPlanePoseUI";
    private const string PoseUiLayerName = "Default";
    private const int PoseUiSortingOrder = 500;

    private const string MouseDebugRootObjectName = "Debug_UI";
    private const string MouseDebugTextObjectName = "UI_Text";

    [SerializeField] private bool showPoseUi;
    [SerializeField] private bool showMouseCoords = true;
    [SerializeField] private GameObject poseUiDebugObject;

    private OVRCameraRig cameraRig;

    // Pose UI state
    private Transform poseUiRoot;
    private Text poseBodyText;
    private bool poseUiInitialized;
    private static Transform sharedPoseUiRoot;
    private static Text sharedPoseBodyText;

    // Mouse debug text state
    private Transform mouseDebugRoot;
    private TMP_Text mouseDebugText;
    private bool mouseDebugObjectsMissingLogged;

    public void Configure(bool showPose, bool showMouse)
    {
        showPoseUi = showPose;
        showMouseCoords = showMouse;

        if (showMouseCoords)
        {
            if (EnsureMouseDebugObjects(logWarning: true))
            {
                if (!mouseDebugRoot.gameObject.activeSelf)
                {
                    mouseDebugRoot.gameObject.SetActive(true);
                }

                mouseDebugText.text =
                    "Mouse Coordinates\n" +
                    "  Waiting for X-Plane mouse data...";
            }
        }
        else
        {
            HideMouseDebugRoot();
        }
    }

    public void SetCameraRig(OVRCameraRig rig)
    {
        cameraRig = rig;
    }

    public void SetPoseDebugObject(GameObject debugObject)
    {
        poseUiDebugObject = debugObject;
    }

    public void Tick()
    {
        if (showPoseUi)
        {
            TryInitializePoseUI();
            UpdatePoseUITransform();
            UpdatePoseUIContent();
            SyncPoseUICamera();

            if (poseUiRoot != null && !poseUiRoot.gameObject.activeSelf)
            {
                poseUiRoot.gameObject.SetActive(true);
            }
        }
        else if (poseUiRoot != null && poseUiRoot.gameObject.activeSelf)
        {
            poseUiRoot.gameObject.SetActive(false);
        }

        if (showMouseCoords)
        {
            UpdateMouseCoordinatesUi();
        }
        else
        {
            HideMouseDebugRoot();
        }
    }

    public GameObject GetPoseUiGameObject()
    {
        if (poseUiRoot != null)
        {
            return poseUiRoot.gameObject;
        }

        return poseUiDebugObject;
    }

    private void TryInitializePoseUI()
    {
        if (poseUiInitialized && poseUiRoot != null)
        {
            return;
        }

        if (poseUiInitialized && poseUiRoot == null)
        {
            poseUiInitialized = false;
            poseUiDebugObject = null;
        }

        Transform anchorTransform = ResolvePlayerAnchor();
        if (anchorTransform == null)
        {
            return;
        }


        if (sharedPoseUiRoot == null || !sharedPoseUiRoot)
        {
            sharedPoseUiRoot = FindExistingPoseUiInScene();
            if (sharedPoseUiRoot != null)
            {
                sharedPoseBodyText = null;
            }
        }

        if (sharedPoseUiRoot == null)
        {
            sharedPoseUiRoot = CreatePoseUiRoot(anchorTransform, out sharedPoseBodyText);
            if (sharedPoseUiRoot == null)
            {
                return;
            }
        }
        else
        {
            ConfigurePoseUiRoot(sharedPoseUiRoot, anchorTransform);
            sharedPoseBodyText = EnsurePoseUiLayout(sharedPoseUiRoot, sharedPoseBodyText);
        }

        if (sharedPoseUiRoot == null)
        {
            return;
        }

        poseUiRoot = sharedPoseUiRoot;
        poseBodyText = sharedPoseBodyText;
        poseUiDebugObject = poseUiRoot.gameObject;
        poseUiInitialized = poseUiRoot != null && poseBodyText != null;

        if (poseUiInitialized)
        {
            UpdatePoseUITransform();
        }
    }

    private Transform CreatePoseUiRoot(Transform anchorTransform, out Text bodyText)
    {
        bodyText = null;
        if (anchorTransform == null)
        {
            return null;
        }

        Transform root = new GameObject(PoseUiObjectName).transform;
        ConfigurePoseUiRoot(root, anchorTransform);
        bodyText = EnsurePoseUiLayout(root, null);
        return bodyText != null ? root : null;
    }

    private void ConfigurePoseUiRoot(Transform root, Transform anchorTransform)
    {
        if (root == null || anchorTransform == null)
        {
            return;
        }

        root.name = PoseUiObjectName;
        root.SetParent(anchorTransform, true);
        root.gameObject.SetActive(true);
        root.localScale = new Vector3(-PoseUiScale, PoseUiScale, PoseUiScale);

        Canvas canvas = root.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = root.gameObject.AddComponent<Canvas>();
        }
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = PoseUiSortingOrder;
        canvas.worldCamera = ResolvePlayerCamera();

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = root.gameObject.AddComponent<CanvasScaler>();
        }
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;

        if (root.GetComponent<GraphicRaycaster>() == null)
        {
            root.gameObject.AddComponent<GraphicRaycaster>();
        }

        int poseLayer = ResolvePoseUiLayer(anchorTransform.gameObject.layer);
        SetLayerRecursively(root, poseLayer);
    }

    private Text EnsurePoseUiLayout(Transform root, Text cachedBodyText)
    {
        if (root == null)
        {
            return null;
        }

        if (cachedBodyText != null)
        {
            return cachedBodyText;
        }

        Text bodyText = FindPoseUiBodyText(root);
        if (bodyText != null)
        {
            return bodyText;
        }

        ClearPoseUiChildren(root);

        GameObject panelObject = new GameObject("Panel");
        panelObject.transform.SetParent(root, false);
        RectTransform panelRect = panelObject.AddComponent<RectTransform>();
        panelRect.sizeDelta = new Vector2(180f, 195f);
        panelRect.anchoredPosition = new Vector2(0f, 8f);
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);

        Image panelImage = panelObject.AddComponent<Image>();
        panelImage.color = new Color(0.08f, 0.1f, 0.12f, 0.85f);

        VerticalLayoutGroup layoutGroup = panelObject.AddComponent<VerticalLayoutGroup>();
        layoutGroup.padding = new RectOffset(12, 12, 8, 12);
        layoutGroup.spacing = 8f;
        layoutGroup.childAlignment = TextAnchor.UpperLeft;
        layoutGroup.childForceExpandHeight = false;
        layoutGroup.childForceExpandWidth = false;

        Font builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (builtinFont == null)
        {
            builtinFont = Font.CreateDynamicFontFromOSFont("Arial", 18);
        }

        GameObject titleObject = new GameObject("Title");
        titleObject.transform.SetParent(panelObject.transform, false);
        Text titleText = titleObject.AddComponent<Text>();
        titleText.font = builtinFont;
        titleText.fontSize = 20;
        titleText.alignment = TextAnchor.UpperLeft;
        titleText.fontStyle = FontStyle.Bold;
        titleText.color = Color.white;
        titleText.text = "X-Plane Pose";

        GameObject bodyObject = new GameObject("Body");
        bodyObject.transform.SetParent(panelObject.transform, false);
        bodyText = bodyObject.AddComponent<Text>();
        bodyText.font = builtinFont;
        bodyText.fontSize = 18;
        bodyText.alignment = TextAnchor.UpperLeft;
        bodyText.color = Color.white;
        bodyText.text = "Waiting for X-Plane pose...";

        return bodyText;
    }

    private static Text FindPoseUiBodyText(Transform root)
    {
        Text[] texts = root.GetComponentsInChildren<Text>(true);
        foreach (Text text in texts)
        {
            if (text != null && text.gameObject.name == "Body")
            {
                return text;
            }
        }

        return null;
    }

    private static void ClearPoseUiChildren(Transform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            DestroyImmediateOrDeferred(root.GetChild(i).gameObject);
        }
    }

    private Transform FindExistingPoseUiInScene()
    {
        GameObject existing = GameObject.Find(PoseUiObjectName);
        if (existing != null)
        {
            return existing.transform;
        }

        Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
        foreach (Transform transform in transforms)
        {
            if (transform == null)
            {
                continue;
            }

            if (transform.gameObject.scene.IsValid() && string.Equals(transform.name, PoseUiObjectName))
            {
                return transform;
            }
        }

        return null;
    }

    private void UpdatePoseUITransform()
    {
        if (!poseUiInitialized || poseUiRoot == null)
        {
            return;
        }

        Transform anchor = ResolvePlayerAnchor();
        if (anchor == null)
        {
            return;
        }


        if (poseUiRoot.parent != anchor)
        {
            poseUiRoot.SetParent(anchor, true);
            poseUiDebugObject = poseUiRoot.gameObject;
        }

        Vector3 anchorForward = anchor.forward.sqrMagnitude < 0.0001f ? Vector3.forward : anchor.forward.normalized;
        Vector3 anchorRight = anchor.right.sqrMagnitude < 0.0001f ? Vector3.right : anchor.right.normalized;
        Vector3 anchorUp = anchor.up.sqrMagnitude < 0.0001f ? Vector3.up : anchor.up.normalized;

        Vector3 targetPosition =
            anchor.position +
            anchorForward * PoseUiForwardOffset +
            anchorUp * PoseUiVerticalOffset -
            anchorRight * PoseUiHorizontalOffset;

        poseUiRoot.position = targetPosition;

        Vector3 lookDirection = anchor.position - targetPosition;
        if (lookDirection.sqrMagnitude < 0.0001f)
        {
            lookDirection = anchorForward;
        }

        poseUiRoot.rotation = Quaternion.LookRotation(lookDirection, anchorUp);

        Transform parent = poseUiRoot.parent;
        if (parent != null)
        {
            float parentScale = Mathf.Max(parent.lossyScale.x, Mathf.Max(parent.lossyScale.y, parent.lossyScale.z));
            if (!Mathf.Approximately(parentScale, 0f))
            {
                float adjustedScale = PoseUiScale / parentScale;
                poseUiRoot.localScale = new Vector3(-adjustedScale, adjustedScale, adjustedScale);
            }
        }
        else
        {
            poseUiRoot.localScale = new Vector3(-PoseUiScale, PoseUiScale, PoseUiScale);
        }
    }

    private void UpdatePoseUIContent()
    {
        if (!poseUiInitialized || poseBodyText == null)
        {
            return;
        }

        if (VRServer.TryGetLastSentCamera(out Vector3 position, out Vector3 angles))
        {
            poseBodyText.text =
                $"Position (m)\n" +
                $"  X: {position.x:F2}\n" +
                $"  Y: {position.y:F2}\n" +
                $"  Z: {position.z:F2}\n\n" +
                $"Orientation (deg)\n" +
                $"  Pitch: {angles.x:F1}\n" +
                $"  Yaw:   {angles.y:F1}\n" +
                $"  Roll:  {angles.z:F1}";
        }
        else
        {
            poseBodyText.text = "Waiting for X-Plane pose...";
        }
    }

    private void SyncPoseUICamera()
    {
        if (!poseUiInitialized || poseUiRoot == null)
        {
            return;
        }

        Camera playerCamera = ResolvePlayerCamera();
        if (playerCamera == null)
        {
            return;
        }

        Canvas canvas = poseUiRoot.GetComponent<Canvas>();
        if (canvas != null && canvas.worldCamera != playerCamera)
        {
            canvas.worldCamera = playerCamera;
        }
    }

    private void UpdateMouseCoordinatesUi()
    {
        if (!EnsureMouseDebugObjects(logWarning: false))
        {
            return;
        }

        if (!mouseDebugRoot.gameObject.activeSelf)
        {
            mouseDebugRoot.gameObject.SetActive(true);
        }

        if (VRServer.TryGetLastMouseCoordinates(out Vector2 coords))
        {
            mouseDebugText.text =
                $"Mouse Coordinates\n" +
                $"  X: {coords.x:N0}\n" +
                $"  Y: {coords.y:N0}";
        }
        else
        {
            mouseDebugText.text =
                "Mouse Coordinates\n" +
                "  Waiting for X-Plane mouse data...";
        }
    }

    private bool EnsureMouseDebugObjects(bool logWarning)
    {
        if (mouseDebugRoot != null && mouseDebugText != null)
        {
            return true;
        }

        mouseDebugRoot = null;
        mouseDebugText = null;

        GameObject rootObject = GameObject.Find(MouseDebugRootObjectName);
        if (rootObject == null)
        {
            if (logWarning && !mouseDebugObjectsMissingLogged)
            {
                MyLogs.Log($"[WARN] LiveXPlaneUI: Could not find '{MouseDebugRootObjectName}' object for mouse debugging.");
                mouseDebugObjectsMissingLogged = true;
            }
            return false;
        }

        mouseDebugRoot = rootObject.transform;

        TMP_Text text = null;
        Transform textTransform = mouseDebugRoot.Find(MouseDebugTextObjectName);
        if (textTransform != null)
        {
            text = textTransform.GetComponent<TMP_Text>();
        }

        if (text == null)
        {
            text = mouseDebugRoot.GetComponentInChildren<TMP_Text>(true);
        }

        if (text == null)
        {
            if (logWarning && !mouseDebugObjectsMissingLogged)
            {
                MyLogs.Log($"[WARN] LiveXPlaneUI: Could not find '{MouseDebugTextObjectName}' TMP text under '{MouseDebugRootObjectName}'.");
                mouseDebugObjectsMissingLogged = true;
            }

            mouseDebugRoot = null;
            return false;
        }

        mouseDebugText = text;
        mouseDebugObjectsMissingLogged = false;
        return true;
    }

    private void HideMouseDebugRoot()
    {
        if (mouseDebugRoot == null)
        {
            EnsureMouseDebugObjects(logWarning: false);
        }

        if (mouseDebugRoot != null && mouseDebugRoot.gameObject.activeSelf)
        {
            mouseDebugRoot.gameObject.SetActive(false);
        }
    }

    private Transform ResolvePlayerAnchor()
    {
        if (cameraRig == null)
        {
            cameraRig = FindFirstObjectByType<OVRCameraRig>();
        }

        if (cameraRig != null && cameraRig.centerEyeAnchor != null)
        {
            return cameraRig.centerEyeAnchor;
        }

        Camera mainCamera = Camera.main;
        return mainCamera != null ? mainCamera.transform : null;
    }

    private Camera ResolvePlayerCamera()
    {
        Transform anchor = ResolvePlayerAnchor();
        return anchor != null ? anchor.GetComponent<Camera>() : Camera.main;
    }

    private static void DestroyImmediateOrDeferred(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private static void SetLayerRecursively(Transform target, int layer)
    {
        if (target == null)
        {
            return;
        }

        target.gameObject.layer = layer;
        for (int i = 0; i < target.childCount; i++)
        {
            SetLayerRecursively(target.GetChild(i), layer);
        }
    }

    private static int ResolvePoseUiLayer(int fallbackLayer)
    {
        int desiredLayer = LayerMask.NameToLayer(PoseUiLayerName);
        return desiredLayer >= 0 ? desiredLayer : fallbackLayer;
    }

    private void OnDestroy()
    {
        if (poseUiRoot != null && sharedPoseUiRoot == poseUiRoot)
        {
            DestroyImmediateOrDeferred(poseUiRoot.gameObject);
            sharedPoseUiRoot = null;
            sharedPoseBodyText = null;
        }

        poseUiRoot = null;
        poseBodyText = null;
        poseUiInitialized = false;

        mouseDebugRoot = null;
        mouseDebugText = null;
    }
}
