using UnityEngine;

public static class XPlaneUnityConv
{
    private static bool controllerOffsetEnabled;
    private static Vector3 controllerCameraOffset = Vector3.zero;

    public static void SetControllerOffsetEnabled(bool enabled)
    {
        controllerOffsetEnabled = enabled;
        if (!enabled)
        {
            controllerCameraOffset = Vector3.zero;
        }
    }

    public static void AddControllerCameraOffset(Vector3 delta)
    {
        if (!controllerOffsetEnabled || delta == Vector3.zero)
        {
            return;
        }

        controllerCameraOffset += delta;
    }

    public static void RotateControllerCameraOffset(float rotationDegrees)
    {
        if (!controllerOffsetEnabled || Mathf.Approximately(rotationDegrees, 0f))
        {
            return;
        }

        Quaternion rotation = Quaternion.AngleAxis(rotationDegrees, Vector3.up);
        controllerCameraOffset = rotation * controllerCameraOffset;
    }

    public static void ResetControllerCameraOffset()
    {
        if (!controllerOffsetEnabled)
        {
            return;
        }

        controllerCameraOffset = Vector3.zero;
    }

    // Converts position offsets from Unity world space into aircraft-local X-Plane offsets.
    public static Vector3 ConvertPositionToXPlane(
        Vector3 unityWorldOffset,
        Quaternion baseWorldRotation,
        bool hasBasePose)
    {
        Vector3 adjustedOffset = unityWorldOffset;
        if (controllerOffsetEnabled && controllerCameraOffset != Vector3.zero)
        {
            adjustedOffset += controllerCameraOffset;
        }

        Quaternion referenceRotation = hasBasePose ? baseWorldRotation : Quaternion.identity;
        Quaternion worldToAircraft = Quaternion.Inverse(referenceRotation);
        Vector3 aircraftOffset = worldToAircraft * adjustedOffset;

        // Unity +Z points forward; X-Plane aircraft +Z points toward the tail (aft).
        return ToXPlaneVector(aircraftOffset);
    }

    // Returns [pitch, heading(yaw), roll] in degrees to match CameraControl expectations.
    public static Vector3 ConvertRotationToXPlaneAngles(Quaternion rotation)
    {
        Vector3 xpRight = ToXPlaneVector(rotation * Vector3.right);
        Vector3 xpUp = ToXPlaneVector(rotation * Vector3.up);
        Vector3 xpTail = ToXPlaneVector(-(rotation * Vector3.forward));

        float r00 = xpRight.x;
        float r01 = xpUp.x;
        float r02 = xpTail.x;

        float r10 = xpRight.y;
        float r11 = xpUp.y;
        float r12 = xpTail.y;

        float r20 = xpRight.z;
        float r21 = xpUp.z;
        float r22 = xpTail.z;

        float sinPitch = Mathf.Clamp(-r12, -1f, 1f);
        float pitchRad = Mathf.Asin(sinPitch);
        float cosPitch = Mathf.Cos(pitchRad);

        float rollRad;
        float headingRad;

        if (cosPitch > 1e-6f)
        {
            rollRad = Mathf.Atan2(-r10, r11);
            headingRad = Mathf.Atan2(-r02, r22);
        }
        else
        {
            // Gimbal lock when pitch approaches +/-90 degrees: roll and heading couple.
            rollRad = 0f;
            headingRad = Mathf.Atan2(-r20, r00);
        }

        float pitchDeg = NormalizeAngleDegrees(Mathf.Rad2Deg * pitchRad);
        float headingDeg = NormalizeAngleDegrees(Mathf.Rad2Deg * headingRad);
        float rollDeg = NormalizeAngleDegrees(Mathf.Rad2Deg * rollRad);

        return new Vector3(pitchDeg, headingDeg, rollDeg);
    }

    private static float NormalizeAngleDegrees(float degrees)
    {
        // Wrap degrees so that angles equivalent modulo 360 fall within [-180, 180).
        return Mathf.Repeat(degrees + 180f, 360f) - 180f;
    }

    private static Vector3 ToXPlaneVector(Vector3 unityVector)
    {
        // Remap Unity (right, up, forward) components into X-Plane's aircraft frame (right, up, tail).
        return new Vector3(unityVector.x, unityVector.y, -unityVector.z);
    }
}
