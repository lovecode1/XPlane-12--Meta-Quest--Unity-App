using System.Collections.Concurrent;
using System;
using System.Globalization;
using UnityEngine;

public static class ControllerHandler
{
    private static VRServer registeredVrServer;
    private static float yPositionOffset = 0f;
    private static OVRCameraRig cameraRig;
    private static LiveXPlane mainClass;
    private const bool SUPPORT_MOVMENT_ACCELERATION = true;
    private const float MIN_SEC_TO_ACCELERATE = 0.4f;
    private const float MIN_SEC_TO_EXTRA_ACCELERATE = 3f;
    private const float ACCELERATION_RATE_PER_SECOND = 5.0f;
    private const float EXTRA_ACCELERATION_RATE_PER_SECOND = 15.0f;
    private const float cockpitRadiusStep = 1f;
    private const float cockpitRightToggleInterval = 4f;
    private static float lastBButtonPressTime = 0f;
    private static float lastYButtonPressTime = 0f;
    private static float lastXButtonPressTime = 0f;
    private static bool useXPlaneCameraOffset;

    private static GameObject cockpitRightObject;
    private static float nextCockpitRightToggleTime;
    private static float leftThumbstickHoldTime;
    private static float rightThumbstickHorizontalHoldTime;
    private static float rightThumbstickVerticalHoldTime;
    private static volatile bool isLeftGrabButtonPressed;
    private static QuadRender screenshotQuadRender;

    /// <summary>
    /// Handle all OVR controller inputs (movement, rotation, vertical, A and B buttons) in one call
    /// </summary>
    /// <param name="cameraRig">The OVR camera rig</param>
    /// <param name="moveSpeed">Movement speed multiplier</param>
    /// <param name="rotationSensitivity">Rotation sensitivity multiplier</param>
    /// <param name="verticalMoveSpeed">Vertical movement speed multiplier</param>
    public static void HandleAllOVRInput(float moveSpeed, float rotationSensitivity, float verticalMoveSpeed)
    {
        // Controller buttons
        HandleRightControllerBButton();
        HandleRightControllerAButton();
        HandleLeftControllerYButton();
        HandleLeftControllerXButton();

        HandleLeftControllerTriggerCapture();
        HandleBasePoseCaptureInput();

        // Movements
        HandleOVRMovement(moveSpeed);
        // For now lets disable rotations
        // HandleOVRRotation(rotationSensitivity);
        HandleOVRVerticalMovement(verticalMoveSpeed);

        UpdateLeftGrabButtonState();
    }


    // Get function for yPositionOffset
    public static float GetYPositionOffset()
    {
        return yPositionOffset;
        /*if (cameraRig == null)
        {
            MyLogs.Log("[ERROR] Camera rig is not set. Returning default yPositionOffset of 0.");
            return 0f;
        }
        //return yPositionOffset;
        return cameraRig.transform.position.y;*/
    }

    public static void SetCameraRig(OVRCameraRig rig)
    {
        cameraRig = rig;
    }

    public static void SetMainClass(LiveXPlane main)
    {
        mainClass = main;
    }

    public static void RegisterVrServer(VRServer server)
    {
        registeredVrServer = server;
    }

    public static void UnregisterVrServer(VRServer server)
    {
        if (registeredVrServer == server)
        {
            registeredVrServer = null;
        }
    }

    public static void ConfigureCameraOffsetMode(bool offsetXPlaneCamera)
    {
        useXPlaneCameraOffset = offsetXPlaneCamera;
        XPlaneUnityConv.SetControllerOffsetEnabled(offsetXPlaneCamera);
        if (offsetXPlaneCamera)
        {
            XPlaneUnityConv.ResetControllerCameraOffset();
        }
    }

    public static void InitializeCockpitAlignment()
    {
        if (cameraRig == null || cameraRig.centerEyeAnchor == null)
        {
            MyLogs.Log("InitializeCockpitAlignment: cameraRig or centerEyeAnchor is null");
            return;
        }
        // Nothing else to align now that the cockpit sphere has been removed.
    }

    /// <summary>
    /// Handle OVR controller movement input for natural walking
    /// </summary>
    /// <param name="cameraRig">The OVR camera rig</param>
    /// <param name="moveSpeed">Movement speed multiplier</param>
    public static void HandleOVRMovement(float moveSpeed)
    {
        if (cameraRig == null || cameraRig.centerEyeAnchor == null)
        {
            MyLogs.Log("HandleOVRMovement: cameraRig or centerEyeAnchor is null");
            return;
        }

        // Get left controller thumbstick input for natural walking
        Vector2 leftThumbstick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);
        bool movementActive = Mathf.Abs(leftThumbstick.x) > 0.1f || Mathf.Abs(leftThumbstick.y) > 0.1f;
        float accelerationFactor = CalculateThumbstickAcceleration(movementActive, ref leftThumbstickHoldTime);

        // Calculate movement directions based on head orientation (centerEyeAnchor)
        Vector3 headForward = cameraRig.centerEyeAnchor.forward;
        Vector3 headRight = cameraRig.centerEyeAnchor.right;

        // Keep movement on horizontal plane for natural walking
        headForward.y = 0;
        headRight.y = 0;
        headForward.Normalize();
        headRight.Normalize();

        // Natural movement: forward/back with Y axis, strafe left/right with X axis
        Vector3 moveDirection = (headForward * leftThumbstick.y) + (headRight * leftThumbstick.x);
        Vector3 movement = moveDirection * moveSpeed * accelerationFactor * Time.deltaTime;

        // Apply smooth movement to camera rig transform
        cameraRig.transform.position += movement;
        AddMovementToCockpitOffset(movement);

        //if (movement != Vector3.zero)
        //    MyLogs.Log($"OVR Movement - Left Thumbstick: {leftThumbstick}, Movement Applied: {movement}");
    }

    /// <summary>
    /// Handle OVR controller rotation input for smooth turning
    /// </summary>
    /// <param name="cameraRig">The OVR camera rig</param>
    /// <param name="rotationSensitivity">Rotation sensitivity multiplier</param>
    public static void HandleOVRRotation(float rotationSensitivity)
    {
        if (cameraRig == null || cameraRig.centerEyeAnchor == null) return;

        // Get right controller thumbstick input for smooth rotation
        Vector2 rightThumbstick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);

        // Only use left/right input for natural turning, ignore up/down
        float horizontalInput = rightThumbstick.x;
        bool rotationActive = Mathf.Abs(horizontalInput) > 0.1f;
        float accelerationFactor = CalculateThumbstickAcceleration(rotationActive, ref rightThumbstickHorizontalHoldTime);

        // Apply smooth rotation based on thumbstick input
        if (rotationActive)
        {
            float rotationAmount = horizontalInput * rotationSensitivity * accelerationFactor * Time.deltaTime;

            // Rotate camera rig around the centerEyeAnchor position with world up axis
            // This ensures rotation happens around the head position, not the rig origin
            Vector3 pivotPoint = cameraRig.centerEyeAnchor.position;
            cameraRig.transform.RotateAround(pivotPoint, Vector3.up, rotationAmount);
            RotateCockpitOffset(rotationAmount);
        }
    }

    /// <summary>
    /// Handle OVR controller vertical movement input
    /// </summary>
    /// <param name="cameraRig">The OVR camera rig</param>
    /// <param name="verticalMoveSpeed">Vertical movement speed multiplier</param>
    public static void HandleOVRVerticalMovement(float verticalMoveSpeed)
    {
        if (cameraRig == null) return;
        
        // Get right controller thumbstick input for vertical movement
        Vector2 rightThumbstick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
        
        // Use Y-axis of right thumbstick for vertical movement
        float verticalInput = rightThumbstick.y;
        bool verticalActive = Mathf.Abs(verticalInput) > 0.1f;
        float accelerationFactor = CalculateThumbstickAcceleration(verticalActive, ref rightThumbstickVerticalHoldTime);
        
        // Apply vertical movement: forward = up, backward = down
        if (verticalActive)
        {
            Vector3 verticalMovement = Vector3.up * verticalInput * verticalMoveSpeed * accelerationFactor * Time.deltaTime;
            yPositionOffset += verticalMovement.y;
            cameraRig.transform.position += verticalMovement;
            AddMovementToCockpitOffset(verticalMovement);
        }
    }

    /// <summary>
    /// Handle A button press on right controller
    /// </summary>
    /// <returns>True if A button was pressed this frame</returns>
    public static bool HandleRightControllerAButton()
    {
        bool aButtonPressed = OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch);
        
        if (aButtonPressed)
        {
            MyLogs.Log("Right controller A button pressed");
            if (mainClass != null)
            {
                mainClass.RotateOverlayLongitude(10f);
            }
        }
        
        return aButtonPressed;
    }

    /// <summary>
    /// Handle B button press on right controller
    /// </summary>
    /// <returns>True if B button was pressed and action was executed</returns>
    public static bool HandleRightControllerBButton()
    {
        bool bButtonPressed = OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch);

        if (bButtonPressed)
        {
            MyLogs.Log("Right controller B button pressed");
            float timeSinceLastPress = Time.time - lastBButtonPressTime;
            if (timeSinceLastPress >= 0.1f)
            {
                lastBButtonPressTime = Time.time;
                if (mainClass != null)
                {
                    mainClass.RotateOverlayLongitude(-10f);
                }
            }
        }
        return bButtonPressed;
    }


    /// <summary>
    /// Get left controller thumbstick input
    /// </summary>
    /// <returns>Vector2 representing left thumbstick input</returns>
    public static Vector2 GetLeftThumbstickInput()
    {
        return OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);
    }

    /// <summary>
    /// Get right controller thumbstick input
    /// </summary>
    /// <returns>Vector2 representing right thumbstick input</returns>
    public static Vector2 GetRightThumbstickInput()
    {
        return OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
    }

    /// <summary>
    /// Get button input from specified controller
    /// </summary>
    /// <param name="button">The OVR button to check</param>
    /// <param name="controller">The controller to check (optional, defaults to LTouch)</param>
    /// <returns>True if button is pressed</returns>
    public static bool GetButtonInput(OVRInput.Button button, OVRInput.Controller controller = OVRInput.Controller.LTouch)
    {
        return OVRInput.Get(button, controller);
    }

    /// <summary>
    /// Get button down input from specified controller
    /// </summary>
    /// <param name="button">The OVR button to check</param>
    /// <param name="controller">The controller to check (optional, defaults to LTouch)</param>
    /// <returns>True if button was just pressed this frame</returns>
    public static bool GetButtonDown(OVRInput.Button button, OVRInput.Controller controller = OVRInput.Controller.LTouch)
    {
        return OVRInput.GetDown(button, controller);
    }

    /// <summary>
    /// Handle Y button press on left controller to toggle the right cockpit visibility
    /// </summary>
    /// <returns>True if Y button was pressed this frame</returns>
    public static bool HandleLeftControllerYButton()
    {
        bool yButtonPressed = OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.LTouch);

        if (yButtonPressed)
        {
            float timeSinceLastPress = Time.time - lastYButtonPressTime;
            if (timeSinceLastPress < 0.1f)
            {
                return true;
            }

            lastYButtonPressTime = Time.time;
            MyLogs.Log("Left controller Y button pressed");
            if (useXPlaneCameraOffset)
            {
                XPlaneUnityConv.ResetControllerCameraOffset();
            }
            else
            {
                // Offset maintenance no longer required now that the cockpit sphere was removed.
            }
        }

        return yButtonPressed;
    }


    public static bool HandleLeftControllerXButton()
    {
        bool xButtonPressed = OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.LTouch);

        if (xButtonPressed)
        {
            float timeSinceLastPress = Time.time - lastXButtonPressTime;
            if (timeSinceLastPress < 0.1f)
            {
                return true;
            }

            lastXButtonPressTime = Time.time;
            MyLogs.Log("Left controller X button pressed");
            Vector3 headsetPosition = Vector3.zero;
            float roll = 0f;
            float pitch = 0f;
            float yaw = 0f;

            if (cameraRig != null && cameraRig.centerEyeAnchor != null)
            {
                Transform anchor = cameraRig.centerEyeAnchor;
                headsetPosition = anchor.position;
                Vector3 euler = anchor.rotation.eulerAngles;
                pitch = NormalizeAngleDegrees(euler.x);
                yaw = NormalizeAngleDegrees(euler.y);
                roll = NormalizeAngleDegrees(euler.z);
            }
            else
            {
                MyLogs.Log("Left controller X button: camera rig or eye anchor unavailable.");
            }

            MyLogs.Log(string.Format(
                CultureInfo.InvariantCulture,
                "Headset pose position=({0:F3},{1:F3},{2:F3}) roll={3:F2} pitch={4:F2} yaw={5:F2}",
                headsetPosition.x, headsetPosition.y, headsetPosition.z,
                roll, pitch, yaw));

            if (VRServer.TryGetLastSentCamera(out Vector3 lastSentPos, out Vector3 lastSentAngles))
            {
                MyLogs.Log(string.Format(
                    CultureInfo.InvariantCulture,
                    "Last get_camera payload position=({0:F3},{1:F3},{2:F3}) orientation(pitch,heading,roll)=({3:F2},{4:F2},{5:F2})",
                    lastSentPos.x, lastSentPos.y, lastSentPos.z,
                    lastSentAngles.x, lastSentAngles.y, lastSentAngles.z));
            }
            else
            {
                MyLogs.Log("Last get_camera payload unavailable.");
            }
        }

        return xButtonPressed;
    }

    /// <summary>
    /// Get button up input from specified controller
    /// </summary>
    /// <param name="button">The OVR button to check</param>
    /// <param name="controller">The controller to check (optional, defaults to LTouch)</param>
    /// <returns>True if button was just released this frame</returns>
    public static bool GetButtonUp(OVRInput.Button button, OVRInput.Controller controller = OVRInput.Controller.LTouch)
    {
        return OVRInput.GetUp(button, controller);
    }

    /// <summary>
    /// Get trigger input from specified controller
    /// </summary>
    /// <param name="trigger">The trigger to check</param>
    /// <param name="controller">The controller to check (optional, defaults to LTouch)</param>
    /// <returns>Float value representing trigger pressure (0-1)</returns>
    public static float GetTriggerInput(OVRInput.Axis1D trigger, OVRInput.Controller controller = OVRInput.Controller.LTouch)
    {
        return OVRInput.Get(trigger, controller);
    }

    /// <summary>
    /// Check if any controller is connected
    /// </summary>
    /// <returns>True if at least one controller is connected</returns>
    public static bool IsAnyControllerConnected()
    {
        return OVRInput.IsControllerConnected(OVRInput.Controller.LTouch) || 
               OVRInput.IsControllerConnected(OVRInput.Controller.RTouch);
    }

    /// <summary>
    /// Check if specific controller is connected
    /// </summary>
    /// <param name="controller">The controller to check</param>
    /// <returns>True if specified controller is connected</returns>
    public static bool IsControllerConnected(OVRInput.Controller controller)
    {
        return OVRInput.IsControllerConnected(controller);
    }

    public static bool IsLeftGrabButtonPressed()
    {
        return isLeftGrabButtonPressed;
    }

    public static void QueueMouseCursorUpdate(ConcurrentQueue<Action> actionQueue, Mouse mouseController, float x, float y)
    {
        if (actionQueue == null)
        {
            return;
        }

        actionQueue.Enqueue(() =>
        {
            if (mouseController != null && mouseController.IsReady)
            {
                mouseController.UpdateCursorPosition(x, y);
            }
        });
    }

    /// <summary>
    /// Haptic feedback for controller
    /// </summary>
    /// <param name="frequency">Vibration frequency</param>
    /// <param name="amplitude">Vibration amplitude</param>
    /// <param name="controller">Target controller</param>
    public static void SetControllerVibration(float frequency, float amplitude, OVRInput.Controller controller = OVRInput.Controller.LTouch)
    {
        OVRInput.SetControllerVibration(frequency, amplitude, controller);
    }

    /// <summary>
    /// Custom movement handler with deadzone and smoothing
    /// </summary>
    /// <param name="cameraRig">The OVR camera rig</param>
    /// <param name="moveSpeed">Movement speed multiplier</param>
    /// <param name="deadzone">Input deadzone threshold</param>
    /// <param name="smoothing">Movement smoothing factor</param>
    public static void HandleSmoothMovement(float moveSpeed, float deadzone = 0.1f, float smoothing = 1f)
    {
        if (cameraRig == null || cameraRig.centerEyeAnchor == null) return;

        Vector2 leftThumbstick = GetLeftThumbstickInput();
        
        // Apply deadzone
        if (leftThumbstick.magnitude < deadzone)
        {
            leftThumbstick = Vector2.zero;
        }
        
        // Calculate movement with smoothing
        Vector3 headForward = cameraRig.centerEyeAnchor.forward;
        Vector3 headRight = cameraRig.centerEyeAnchor.right;
        
        headForward.y = 0;
        headRight.y = 0;
        headForward.Normalize();
        headRight.Normalize();
        
        Vector3 moveDirection = (headForward * leftThumbstick.y) + (headRight * leftThumbstick.x);
        Vector3 movement = moveDirection * moveSpeed * smoothing * Time.deltaTime;
        
        cameraRig.transform.position += movement;
    }

    private static float CalculateThumbstickAcceleration(bool isActive, ref float holdTime)
    {
        if (!SUPPORT_MOVMENT_ACCELERATION)
        {
            holdTime = 0f;
            return 1f;
        }

        if (isActive)
        {
            holdTime += Time.deltaTime;
        }
        else
        {
            holdTime = 0f;
            return 1f;
        }

        if (holdTime <= MIN_SEC_TO_ACCELERATE)
        {
            return 1f;
        }

        float acceleratedDuration = holdTime - MIN_SEC_TO_ACCELERATE;
        float rate = holdTime > MIN_SEC_TO_EXTRA_ACCELERATE
            ? EXTRA_ACCELERATION_RATE_PER_SECOND
            : ACCELERATION_RATE_PER_SECOND;
        return 1f + acceleratedDuration * rate;
    }

    private static float NormalizeAngleDegrees(float degrees)
    {
        return Mathf.Repeat(degrees + 180f, 360f) - 180f;
    }

    private static void UpdateLeftGrabButtonState()
    {
        bool buttonPressed = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch);

        if (!buttonPressed)
        {
            float analogValue = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.LTouch);
            buttonPressed = analogValue > 0.1f;
        }

        isLeftGrabButtonPressed = buttonPressed;
    }

    private static void AddMovementToCockpitOffset(Vector3 delta)
    {
        if (!useXPlaneCameraOffset || delta == Vector3.zero)
        {
            return;
        }

        XPlaneUnityConv.AddControllerCameraOffset(delta);
    }

    private static void RotateCockpitOffset(float rotationDegrees)
    {
        if (!useXPlaneCameraOffset || Mathf.Approximately(rotationDegrees, 0f))
        {
            return;
        }

        XPlaneUnityConv.RotateControllerCameraOffset(rotationDegrees);
    }

    private static bool EnsureCockpitRightObject()
    {
        if (cockpitRightObject != null)
        {
            return true;
        }

        cockpitRightObject = FindGameObjectEvenIfInactive("Cokpit_Right");

        if (cockpitRightObject == null)
        {
            MyLogs.Log("EnsureCockpitRightObject: Cokpit_Right object not found");
            return false;
        }

        if (nextCockpitRightToggleTime == 0f)
        {
            nextCockpitRightToggleTime = Time.time + cockpitRightToggleInterval;
        }

        return true;
    }

    private static GameObject FindGameObjectEvenIfInactive(string name)
    {
        GameObject found = GameObject.Find(name);
        if (found != null)
        {
            return found;
        }

        GameObject[] allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>(true);
        foreach (GameObject obj in allObjects)
        {
            if (obj.name == name)
            {
                return obj;
            }
        }

        return null;
    }

    private static bool ToggleCockpitRightVisibility(bool showNotification, string trigger)
    {
        if (!EnsureCockpitRightObject())
        {
            if (showNotification)
            {
                VRNotificationUI.ShowNotification("Cokpit_Right not found");
            }
            return false;
        }

        bool newState = !cockpitRightObject.activeSelf;
        cockpitRightObject.SetActive(newState);

        string stateText = newState ? "visible" : "hidden";
        if (showNotification)
        {
            VRNotificationUI.ShowNotification($"Cokpit_Right {stateText}");
        }
        MyLogs.Log($"Cokpit_Right toggled {stateText} via {trigger}");
        return true;
    }

    private static void HandleLeftControllerTriggerCapture()
    {
        if (!OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch))
        {
            return;
        }

        if (!EnsureScreenshotQuadRenderer())
        {
            DebugScreen.TrySaveScreenshotAndMouseData(null);
            return;
        }

        DebugScreen.TrySaveScreenshotAndMouseData(screenshotQuadRender);
    }

    private static void HandleBasePoseCaptureInput()
    {
        if (registeredVrServer == null)
        {
            return;
        }

        if (!OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch))
        {
            return;
        }

        if (cameraRig == null || cameraRig.centerEyeAnchor == null)
        {
            MyLogs.Log("[WARN] ControllerHandler: Cannot capture base pose; camera rig or eye anchor is null.");
            return;
        }

        Transform anchor = cameraRig.centerEyeAnchor;
        registeredVrServer.SetBasePose(anchor.position, anchor.rotation);
    }

    private static bool EnsureScreenshotQuadRenderer()
    {
        if (screenshotQuadRender == null)
        {
#if UNITY_2023_1_OR_NEWER
            screenshotQuadRender = UnityEngine.Object.FindFirstObjectByType<QuadRender>();
#else
            screenshotQuadRender = UnityEngine.Object.FindObjectOfType<QuadRender>();
#endif
        }

        if (screenshotQuadRender == null)
        {
            MyLogs.Log("ControllerHandler: QuadRender not found; cannot save screenshot.");
            return false;
        }

        return true;
    }
}
