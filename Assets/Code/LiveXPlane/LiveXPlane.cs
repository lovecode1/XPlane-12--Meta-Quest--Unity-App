using System.Collections;
using UnityEngine;

public class LiveXPlane : MonoBehaviour
{
    private OVRCameraRig cameraRig;
    private float moveSpeed = 0.05f;
    private float rotationSensitivity = 1f;
    private float verticalMoveSpeed = 0.05f;
    private const bool USE_QUAD_FOR_PROJECTING_X_PLANE = true;
    [SerializeField] private QuadRender quadRenderer;
    private const bool VR_AS_A_SERVER = true;
    private const bool REQUEST_POSE_IMAGE = true;
    private const bool SHOW_MOUSE_CURSOR = true;
    private const bool CONTROLLER_OFFSET_X_PLANE_CAMERA = true;
    private const bool UI_SHOW_POS_ORI_FOR_XPLANE = false;
    private const bool UI_SHOW_MOUSE_COORDS = false;
    private VRServer vrServer;
    [SerializeField] private GameObject poseUiDebugObject;
    private Mouse mouseController;
    private LiveXPlaneUI uiManager;
    private Coroutine initialBasePoseRoutine;
    private FpsMonitor fpsMonitor;

    void Start()
    {
        MyLogs.Initialize();
        Analytics.Log("X_PLANE_VR_APP_STARTED");
        MyLogs.Log("LiveXPlane: Start called.", true, true);
        ControllerHandler.ConfigureCameraOffsetMode(CONTROLLER_OFFSET_X_PLANE_CAMERA);
        // Find the camera rig if not assigned in inspector
        if (cameraRig == null)
        {
            cameraRig = FindFirstObjectByType<OVRCameraRig>();
        }

        // Set the camera rig in ControllerHandler_LiveGenesis
        if (cameraRig != null)
        {
            MyLogs.Log("LiveXPlane: OVRCameraRig found and assigned.");
            ControllerHandler.SetCameraRig(cameraRig);
            ControllerHandler.InitializeCockpitAlignment();
        }
        else
        {
            MyLogs.Log("[ERROR] LiveXPlane: Could not find OVRCameraRig in scene!");
        }

        VRNotificationUI.ShowNotification("Starting...");

        // Register this main class so ControllerHandler_LiveGenesis can call recentering
        ControllerHandler.SetMainClass(this);

        InitializeQuadProjectionSurface();
        InitializeMouseCursorSupport();
        InitializeUserInterface();
        InitializeFpsMonitor();

        if (VR_AS_A_SERVER)
        {
            StartVRServer();
            ScheduleInitialBasePoseCapture();
        }
        else if (REQUEST_POSE_IMAGE)
        {
            StartPoseImageRequests();
        }
    }

    public GameObject GetPoseUiGameObject()
    {
        if (uiManager != null)
        {
            return uiManager.GetPoseUiGameObject() ?? poseUiDebugObject;
        }

        return poseUiDebugObject;
    }

    void Update()
    {
        ControllerHandler.HandleAllOVRInput(moveSpeed, rotationSensitivity, verticalMoveSpeed);

        if (uiManager != null)
        {
            uiManager.SetCameraRig(cameraRig);
            uiManager.Tick();
        }
    }

    private void InitializeQuadProjectionSurface()
    {
        if (!USE_QUAD_FOR_PROJECTING_X_PLANE)
        {
            if (quadRenderer != null)
            {
                quadRenderer.Deactivate();
            }
            return;
        }

        if (quadRenderer == null)
        {
            quadRenderer = GetComponent<QuadRender>();
            if (quadRenderer == null)
            {
                quadRenderer = gameObject.AddComponent<QuadRender>();
            }
        }

        quadRenderer.Activate(cameraRig);
    }

    private void InitializeMouseCursorSupport()
    {
        if (!SHOW_MOUSE_CURSOR)
        {
            return;
        }

        if (quadRenderer == null)
        {
            MyLogs.Log("[WARN] LiveXPlane: Cannot initialize mouse cursor without a QuadRender component.");
            return;
        }

        mouseController = GetComponent<Mouse>();
        if (mouseController == null)
        {
            mouseController = gameObject.AddComponent<Mouse>();
        }

        mouseController.Initialize(quadRenderer);
    }

    private void InitializeUserInterface()
    {
        if (uiManager == null)
        {
            uiManager = GetComponent<LiveXPlaneUI>();
            if (uiManager == null)
            {
                uiManager = gameObject.AddComponent<LiveXPlaneUI>();
            }
        }

        uiManager.Configure(UI_SHOW_POS_ORI_FOR_XPLANE, UI_SHOW_MOUSE_COORDS);
        uiManager.SetCameraRig(cameraRig);
        uiManager.SetPoseDebugObject(poseUiDebugObject);
    }

    private void InitializeFpsMonitor()
    {
        if (fpsMonitor == null)
        {
            fpsMonitor = GetComponent<FpsMonitor>();
            if (fpsMonitor == null)
            {
                fpsMonitor = gameObject.AddComponent<FpsMonitor>();
            }
        }
    }

    public void RotateOverlayLongitude(float degrees)
    {
        quadRenderer?.RotateOverlayLongitude(degrees);
    }

    private void StartVRServer()
    {
        if (cameraRig == null)
        {
            cameraRig = FindFirstObjectByType<OVRCameraRig>();
            if (cameraRig == null)
            {
                MyLogs.Log("[WARN] LiveXPlane: Cannot start VR server without an OVRCameraRig.");
                return;
            }
        }

        if (vrServer == null)
        {
            vrServer = GetComponent<VRServer>();
            if (vrServer == null)
            {
                vrServer = gameObject.AddComponent<VRServer>();
            }
        }

        if (quadRenderer == null)
        {
            quadRenderer = GetComponent<QuadRender>();
            if (quadRenderer == null)
            {
                quadRenderer = gameObject.AddComponent<QuadRender>();
            }
        }

        vrServer.Initialize(quadRenderer, cameraRig);
        if (SHOW_MOUSE_CURSOR && mouseController != null)
        {
            vrServer.SetMouseController(mouseController);
        }
    }

    private void ScheduleInitialBasePoseCapture()
    {
        if (!VR_AS_A_SERVER || vrServer == null)
        {
            return;
        }

        if (initialBasePoseRoutine != null)
        {
            StopCoroutine(initialBasePoseRoutine);
        }

        initialBasePoseRoutine = StartCoroutine(CaptureInitialBasePoseAfterSettle());
    }

    private IEnumerator CaptureInitialBasePoseAfterSettle()
    {
        const float anchorWaitTimeoutSeconds = 5f;
        const float settleDelaySeconds = 0.5f;
        float elapsed = 0f;
        Transform anchor = null;

        while (elapsed < anchorWaitTimeoutSeconds)
        {
            if (cameraRig != null)
            {
                anchor = cameraRig.centerEyeAnchor;
            }

            if (anchor != null)
            {
                break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (anchor == null)
        {
            MyLogs.Log("[WARN] LiveXPlane: Failed to capture initial base pose; eye anchor unavailable.");
            yield break;
        }

        yield return new WaitForSeconds(settleDelaySeconds);

        if (vrServer == null || cameraRig == null || cameraRig.centerEyeAnchor == null)
        {
            yield break;
        }

        Transform settledAnchor = cameraRig.centerEyeAnchor;
        vrServer.SetBasePose(settledAnchor.position, settledAnchor.rotation, showNotification: false);
        MyLogs.Log("LiveXPlane: Initial base pose sent to VRServer.");
    }

    private void StartPoseImageRequests()
    {
        if (cameraRig == null)
        {
            cameraRig = FindFirstObjectByType<OVRCameraRig>();
            if (cameraRig == null)
            {
                MyLogs.Log("[WARN] LiveXPlane: Cannot request pose images without an OVRCameraRig.");
                return;
            }
        }

        InitializeQuadProjectionSurface();
    }

    private void OnDisable()
    {
        if (initialBasePoseRoutine != null)
        {
            StopCoroutine(initialBasePoseRoutine);
            initialBasePoseRoutine = null;
        }
    }
}
