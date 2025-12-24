using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class VRServer : MonoBehaviour
{
    private const string CameraEndpoint = "/get_camera";
    private const string UploadImageEndpoint = "/upload_image";
    private const string TestEndpoint = "/test_get";
    private const string FovEndpoint = "/send_fov";
    private const int MaxHeaderBytes = 64 * 1024;
    private const int MaxImageSizeBytes = 8 * 1024 * 1024;
    private const int MaxPendingImageQueue = 3;
    private const bool PROFILE_LOAD = true;
    private const float ProfilingLogIntervalSeconds = 60f;
    private enum DecodingType
    {
        Simple,
        AsyncAndroid,
        AstcFormat,
        RawImage
    }
    private const DecodingType DECODING_TYPE = DecodingType.Simple;

    [SerializeField] private int port = 4598;

    private QuadRender quadRender;
    private Mouse mouseController;
    private OVRCameraRig cameraRig;
    private TcpListener tcpListener;
    private Thread listenerThread;
    private bool isRunning;
    private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
    private readonly object poseLock = new object();
    private Vector3 latestPosition;
    private Quaternion latestRotation = Quaternion.identity;
    private bool poseAvailable;
    private static readonly object lastSentLock = new object();
    private static Vector3 lastSentPosition = Vector3.zero;
    private static Vector3 lastSentAngles = Vector3.zero;
    private static bool hasLastSentPayload;
    private static readonly object lastMouseLock = new object();
    private static Vector2 lastSentMouse;
    private static bool hasLastMousePayload;
    private bool hasBasePose;
    private Vector3 basePosition;
    private Quaternion baseRotation = Quaternion.identity;
    private volatile bool hasReceivedFov;
    private readonly ProfilingHelper profilingHelper = new ProfilingHelper(PROFILE_LOAD, ProfilingLogIntervalSeconds);
    private FrameDecodeContext frameDecodeContext;
    private readonly Dictionary<DecodingType, IFrameDecodeStrategy> decoderCache = new Dictionary<DecodingType, IFrameDecodeStrategy>();

    public void Initialize(QuadRender quad, OVRCameraRig rig)
    {
        quadRender = quad;
        cameraRig = rig;

        quadRender?.SetOverlayFlip(false);
        ControllerHandler.RegisterVrServer(this);
        UpdateDecoderTargets();

        StopServer();
        lock (poseLock)
        {
            hasBasePose = false;
            basePosition = Vector3.zero;
            baseRotation = Quaternion.identity;
        }
        RefreshCameraPose();
        StartServer();
    }

    public void SetMouseController(Mouse controller)
    {
        mouseController = controller;
        UpdateDecoderTargets();
    }

    public void SetBasePose(Vector3 position, Quaternion rotation, bool showNotification = true)
    {
        lock (poseLock)
        {
            basePosition = position;
            baseRotation = rotation;
            hasBasePose = true;
        }

        if (showNotification)
        {
            VRNotificationUI.ShowNotification("Server base pose captured.");
        }
    }

    private void Update()
    {
        while (mainThreadActions.TryDequeue(out var action))
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                MyLogs.Log($"[WARN] VRServer: Exception while executing queued action - {ex.Message}");
            }
        }

        profilingHelper.MaybeLog();
    }

    private void CaptureBasePose()
    {
        Transform eyeAnchor = ResolveEyeAnchor();
        if (eyeAnchor == null)
        {
            MyLogs.Log("[WARN] VRServer: Cannot capture base pose; eye anchor unavailable.");
            return;
        }

        SetBasePose(eyeAnchor.position, eyeAnchor.rotation);
    }

    private void LateUpdate()
    {
        RefreshCameraPose();
    }

    private void OnDisable()
    {
        ControllerHandler.UnregisterVrServer(this);
        StopServer();
    }

    private void OnDestroy()
    {
        ControllerHandler.UnregisterVrServer(this);
        StopServer();
    }

    private void StartServer()
    {
        if (isRunning)
        {
            return;
        }

        try
        {
            tcpListener = new TcpListener(IPAddress.Any, port);
            tcpListener.Start();
        }
        catch (Exception ex)
        {
            MyLogs.Log($"[ERROR] VRServer: Failed to start TCP listener on port {port} - {ex.Message}");
            MyLogs.Log($"VRServer: Failed to start on port {port} - {ex.Message}", true);
            StopServer();
            return;
        }

        isRunning = true;

        listenerThread = new Thread(ListenLoop)
        {
            IsBackground = true,
            Name = "VRServerListener"
        };
        listenerThread.Start();

        MyLogs.Log($"VRServer: Listening for requests on port {port}");
        MyLogs.Log($"VRServer: Server started on port {port}");
        EnsureContext();
    }

    private void StopServer()
    {
        if (!isRunning)
        {
            return;
        }

        isRunning = false;

        try
        {
            tcpListener?.Stop();
        }
        catch (Exception ex)
        {
            MyLogs.Log($"[WARN] VRServer: Error stopping TCP listener - {ex.Message}");
        }

        tcpListener = null;
        DisposeDecoders();

        if (listenerThread != null && listenerThread.IsAlive)
        {
            if (!listenerThread.Join(500))
            {
                listenerThread.Interrupt();
            }
        }

        listenerThread = null;
    }

    private void EnsureContext()
    {
        if (frameDecodeContext == null)
        {
            frameDecodeContext = new FrameDecodeContext(profilingHelper, action => mainThreadActions.Enqueue(action));
        }

        frameDecodeContext.UpdateTargets(quadRender, mouseController);
    }

    private void DisposeDecoders()
    {
        foreach (var strategy in decoderCache.Values)
        {
            strategy?.Dispose();
        }
        decoderCache.Clear();
    }

    private void UpdateDecoderTargets()
    {
        frameDecodeContext?.UpdateTargets(quadRender, mouseController);
    }

    private IFrameDecodeStrategy GetDecoder(DecodingType type)
    {
        EnsureContext();

        if (decoderCache.TryGetValue(type, out var strategy))
        {
            return strategy;
        }

        strategy = CreateDecoder(type);
        if (strategy != null)
        {
            strategy.Initialize(frameDecodeContext);
            decoderCache[type] = strategy;
        }
        return strategy;
    }

    private static IFrameDecodeStrategy CreateDecoder(DecodingType type)
    {
        switch (type)
        {
            case DecodingType.AsyncAndroid:
                return new AsyncAndroidDecodeStrategy(MaxPendingImageQueue);
            case DecodingType.AstcFormat:
                return new AstcFrameDecodeStrategy(MaxPendingImageQueue);
            case DecodingType.RawImage:
                return new RawFrameDecodeStrategy(MaxPendingImageQueue);
            default:
                return new SimpleJpegDecodeStrategy(MaxPendingImageQueue);
        }
    }

    private DecodingType ResolveDecodingType(Dictionary<string, string> headers)
    {
        if (headers != null && headers.TryGetValue("Content-Type", out string contentType))
        {
            string lowered = contentType?.Trim().ToLowerInvariant();
            if (lowered == "image/astc" || lowered == "application/astc")
            {
                return DecodingType.AstcFormat;
            }
            if (lowered == "image/jpeg" || lowered == "image/jpg")
            {
                return DecodingType.Simple;
            }
            if (lowered == "application/octet-stream")
            {
                return DecodingType.RawImage;
            }
        }

        return DECODING_TYPE;
    }

    private void ListenLoop()
    {
        while (isRunning)
        {
            TcpClient client = null;

            try
            {
                client = tcpListener?.AcceptTcpClient();
            }
            catch (SocketException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (InvalidOperationException)
            {
                break;
            }
            catch (Exception ex)
            {
                MyLogs.Log($"[WARN] VRServer: Listener exception - {ex.Message}");
                continue;
            }

            if (client == null)
            {
                continue;
            }

            ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
        }
    }

    private void HandleClient(TcpClient client)
    {
        using (client)
        {
            NetworkStream stream = null;

            try
            {
                stream = client.GetStream();
                stream.ReadTimeout = 5000;
                stream.WriteTimeout = 5000;

                if (!TryReadRequest(stream, out string method, out string path, out Dictionary<string, string> headers, out byte[] body))
                {
                    RespondWithText(stream, "Bad Request", HttpStatusCode.BadRequest);
                    return;
                }

                string normalizedPath = NormalizePath(path);
                string verb = method?.ToUpperInvariant() ?? string.Empty;

                switch (normalizedPath)
                {
                    case CameraEndpoint:
                        HandleGetCamera(stream, body);
                        break;
                    case UploadImageEndpoint:
                        HandleUploadImage(stream, verb, headers, body);
                        break;
                    case FovEndpoint:
                        HandleSendFov(stream, verb, body);
                        break;
                    case TestEndpoint:
                        HandleTestGet(stream, verb);
                        break;
                    default:
                        RespondWithText(stream, "Not Found", HttpStatusCode.NotFound);
                        break;
                }
            }
            catch (Exception ex)
            {
                MyLogs.Log($"[WARN] VRServer: Failed to handle client request - {ex.Message}");
            }
            finally
            {
                try
                {
                    stream?.Close();
                }
                catch (Exception)
                {
                    // Suppress cleanup exceptions.
                }
            }
        }
    }

    private static string NormalizePath(string rawPath)
    {
        if (string.IsNullOrEmpty(rawPath))
        {
            return string.Empty;
        }

        int queryIndex = rawPath.IndexOf('?');
        string pathWithoutQuery = queryIndex >= 0 ? rawPath.Substring(0, queryIndex) : rawPath;
        return pathWithoutQuery.ToLowerInvariant();
    }

    private bool TryReadRequest(NetworkStream stream, out string method, out string path, out Dictionary<string, string> headers, out byte[] body)
    {
        method = null;
        path = null;
        headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        body = Array.Empty<byte>();

        var headerBytes = new List<byte>();
        int matched = 0;

        while (true)
        {
            int value;
            try
            {
                value = stream.ReadByte();
            }
            catch (IOException)
            {
                return false;
            }

            if (value == -1)
            {
                return false;
            }

            headerBytes.Add((byte)value);

            if (matched == 0 && value == '\r') matched = 1;
            else if (matched == 1 && value == '\n') matched = 2;
            else if (matched == 2 && value == '\r') matched = 3;
            else if (matched == 3 && value == '\n') break;
            else matched = value == '\r' ? 1 : 0;

            if (headerBytes.Count > MaxHeaderBytes)
            {
                return false;
            }
        }

        string headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
        string[] lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);

        if (lines.Length == 0 || string.IsNullOrEmpty(lines[0]))
        {
            return false;
        }

        string[] requestParts = lines[0].Split(' ');
        if (requestParts.Length < 2)
        {
            return false;
        }

        method = requestParts[0];
        path = requestParts[1];

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrEmpty(line))
            {
                break;
            }

            int colonIndex = line.IndexOf(':');
            if (colonIndex <= 0)
            {
                continue;
            }

            string headerName = line.Substring(0, colonIndex).Trim();
            string headerValue = line.Substring(colonIndex + 1).Trim();
            headers[headerName] = headerValue;
        }

        int contentLength = 0;
        if (headers.TryGetValue("Content-Length", out string contentLengthValue))
        {
            if (!int.TryParse(contentLengthValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out contentLength) || contentLength < 0)
            {
                return false;
            }
        }

        if (contentLength > 0)
        {
            body = new byte[contentLength];
            int totalRead = 0;

            while (totalRead < contentLength)
            {
                int read = stream.Read(body, totalRead, contentLength - totalRead);
                if (read <= 0)
                {
                    return false;
                }

                totalRead += read;
            }
        }

        return true;
    }

    private void HandleGetCamera(NetworkStream stream, byte[] body)
    {
        float? pendingMouseX = null;
        float? pendingMouseY = null;
        if (body != null && body.Length > 0 && mouseController != null)
        {
            if (TryParseMousePayload(body, out float mouseX, out float mouseY))
            {
                pendingMouseX = mouseX;
                pendingMouseY = mouseY;
            }
        }

        if (!hasReceivedFov)
        {
            RespondWithJson(stream, "{\"request_fov\":1}");
            return;
        }

        if (pendingMouseX.HasValue && pendingMouseY.HasValue)
        {
            float queuedX = pendingMouseX.Value;
            float queuedY = pendingMouseY.Value;
            ControllerHandler.QueueMouseCursorUpdate(mainThreadActions, mouseController, queuedX, queuedY);
            RecordMouseCoordinates(queuedX, queuedY);
        }

        bool goCloserActive = ControllerHandler.IsLeftGrabButtonPressed();

        Vector3 position;
        Quaternion rotation;
        bool hasPose;
        bool hasBase;
        Vector3 basePos;
        Quaternion baseRot;

        lock (poseLock)
        {
            position = latestPosition;
            hasPose = poseAvailable;
            rotation = latestRotation;
            hasBase = hasBasePose;
            basePos = basePosition;
            baseRot = baseRotation;
        }

        if (!hasPose)
        {
            Vector3 cachedPosition;
            Vector3 cachedAngles;
            bool hasCachedPayload;

            lock (lastSentLock)
            {
                cachedPosition = lastSentPosition;
                cachedAngles = lastSentAngles;
                hasCachedPayload = hasLastSentPayload;
            }

            if (hasCachedPayload)
            {
                string cachedJson = BuildPoseJson(cachedPosition, cachedAngles, goCloserActive);
                RespondWithJson(stream, cachedJson);
            }
            else
            {
                RespondWithText(stream, "Pose unavailable", HttpStatusCode.ServiceUnavailable);
            }
            return;
        }

        Vector3 relativePosition = position;
        Quaternion relativeRotation = rotation;

        if (hasBase)
        {
            Vector3 delta = position - basePos;
            relativePosition = delta;
            relativeRotation = rotation * Quaternion.Inverse(baseRot);
        }

        Vector3 xpPosition = XPlaneUnityConv.ConvertPositionToXPlane(
            relativePosition,
            baseRot,
            hasBase);
        Vector3 xpAngles = XPlaneUnityConv.ConvertRotationToXPlaneAngles(relativeRotation);

        lock (lastSentLock)
        {
            lastSentPosition = xpPosition;
            lastSentAngles = xpAngles;
            hasLastSentPayload = true;
        }

        string json = BuildPoseJson(xpPosition, xpAngles, goCloserActive);

        RespondWithJson(stream, json);
    }

    private void HandleUploadImage(NetworkStream stream, string method, Dictionary<string, string> headers, byte[] body)
    {
        if (method != "POST" && method != "PUT")
        {
            RespondWithText(stream, "Method Not Allowed", HttpStatusCode.MethodNotAllowed);
            return;
        }

        if (body == null || body.Length == 0)
        {
            RespondWithText(stream, "Missing image payload", HttpStatusCode.BadRequest);
            return;
        }

        if (body.Length > MaxImageSizeBytes)
        {
            RespondWithText(stream, "Payload Too Large", HttpStatusCode.RequestEntityTooLarge);
            return;
        }

        DecodingType decodingType = ResolveDecodingType(headers);
        var decoder = GetDecoder(decodingType);

        profilingHelper.RecordFrameQueued();
        long enqueueStamp = profilingHelper.Stamp();

        bool accepted = decoder != null && decoder.TryHandleUpload(body);
        profilingHelper.RecordUploadEnqueue(enqueueStamp);

        if (!accepted)
        {
            profilingHelper.RecordFrameDropped();
            MyLogs.Log("[WARN] VRServer: Decoder rejected uploaded image.");
        }

        RespondWithText(stream, "Image received", HttpStatusCode.OK);
    }

    private void HandleSendFov(NetworkStream stream, string method, byte[] body)
    {
        if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
        {
            RespondWithText(stream, "Method Not Allowed", HttpStatusCode.MethodNotAllowed);
            return;
        }

        if (body == null || body.Length == 0)
        {
            RespondWithText(stream, "Missing payload", HttpStatusCode.BadRequest);
            return;
        }

        if (!TryParseFovPayload(body, out float verticalFov, out float horizontalFov))
        {
            RespondWithText(stream, "Invalid JSON payload", HttpStatusCode.BadRequest);
            return;
        }

        if (verticalFov <= 0f || horizontalFov <= 0f)
        {
            RespondWithText(stream, "Invalid FOV values", HttpStatusCode.BadRequest);
            return;
        }

        mainThreadActions.Enqueue(() =>
        {
            quadRender?.UpdateFov(horizontalFov, verticalFov);
            MyLogs.Log($"VRServer: Received FOV update H={horizontalFov:F2} V={verticalFov:F2}");
        });
        hasReceivedFov = true;
        RespondWithText(stream, "FOV updated", HttpStatusCode.OK);
    }

    private void HandleTestGet(NetworkStream stream, string method)
    {
        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            RespondWithText(stream, "Method Not Allowed", HttpStatusCode.MethodNotAllowed);
            return;
        }

        RespondWithText(stream, "OK!", HttpStatusCode.OK);
    }

    [Serializable]
    private struct MousePayload
    {
        public float mouse_x;
        public float mouse_y;
    }

    [Serializable]
    private struct FovPayload
    {
        public float vertical_fov;
        public float horizontal_fov;
    }

    private static bool TryParseMousePayload(byte[] body, out float mouseX, out float mouseY)
    {
        mouseX = 0f;
        mouseY = 0f;

        if (body == null || body.Length == 0)
        {
            return false;
        }

        string json;
        try
        {
            json = Encoding.UTF8.GetString(body).Trim();
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrEmpty(json))
        {
            return false;
        }

        if (!json.Contains("\"mouse_x\"") || !json.Contains("\"mouse_y\""))
        {
            return false;
        }

        try
        {
            MousePayload payload = JsonUtility.FromJson<MousePayload>(json);
            mouseX = payload.mouse_x;
            mouseY = payload.mouse_y;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseFovPayload(byte[] body, out float verticalFov, out float horizontalFov)
    {
        verticalFov = 0f;
        horizontalFov = 0f;

        if (body == null || body.Length == 0)
        {
            return false;
        }

        string json;
        try
        {
            json = Encoding.UTF8.GetString(body).Trim();
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrEmpty(json) ||
            (!json.Contains("\"vertical_fov\"") && !json.Contains("\"horizontal_fov\"")))
        {
            return false;
        }

        try
        {
            FovPayload payload = JsonUtility.FromJson<FovPayload>(json);
            verticalFov = payload.vertical_fov;
            horizontalFov = payload.horizontal_fov;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void RefreshCameraPose()
    {
        Transform eyeAnchor = ResolveEyeAnchor();
        if (eyeAnchor == null)
        {
            lock (poseLock)
            {
                poseAvailable = false;
            }
            return;
        }

        Vector3 position = eyeAnchor.position;
        Quaternion rotation = eyeAnchor.rotation;

        lock (poseLock)
        {
            latestPosition = position;
            latestRotation = rotation;
            poseAvailable = true;
        }
    }

    private Transform ResolveEyeAnchor()
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

    private static string BuildPoseJson(Vector3 position, Vector3 angles, bool goCloser)
    {
        // angles are in pitch, heading(yaw), roll order to match X-Plane expectations.
        return string.Format(
            CultureInfo.InvariantCulture,
            "{{\"position\":[{0:F6},{1:F6},{2:F6}],\"orientation_deg\":[{3:F6},{4:F6},{5:F6}],\"go_closer\":{6}}}",
            position.x, position.y, position.z,
            angles.x, angles.y, angles.z,
            goCloser ? 1 : 0);
    }

    private static void RespondWithJson(NetworkStream stream, string json)
    {
        Respond(stream, json, "application/json", HttpStatusCode.OK);
    }

    public static bool TryGetLastSentCamera(out Vector3 position, out Vector3 angles)
    {
        lock (lastSentLock)
        {
            position = lastSentPosition;
            angles = lastSentAngles;
            return hasLastSentPayload;
        }
    }

    public static bool TryGetLastMouseCoordinates(out Vector2 coords)
    {
        lock (lastMouseLock)
        {
            coords = lastSentMouse;
            return hasLastMousePayload;
        }
    }

    private static void RespondWithText(NetworkStream stream, string message, HttpStatusCode statusCode)
    {
        Respond(stream, message, "text/plain", statusCode);
    }

    private static void Respond(NetworkStream stream, string body, string contentType, HttpStatusCode statusCode)
    {
        if (stream == null)
        {
            return;
        }

        byte[] payload = body != null ? Encoding.UTF8.GetBytes(body) : Array.Empty<byte>();
        string statusText = $"{(int)statusCode} {statusCode}";
        string header = $"HTTP/1.1 {statusText}\r\nContent-Type: {contentType}\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";

        byte[] headerBytes = Encoding.ASCII.GetBytes(header);

        try
        {
            stream.Write(headerBytes, 0, headerBytes.Length);
            if (payload.Length > 0)
            {
                stream.Write(payload, 0, payload.Length);
            }
            stream.Flush();
        }
        catch (Exception)
        {
            // Ignore write exceptions; client may have disconnected.
        }
    }

    private static void RecordMouseCoordinates(float x, float y)
    {
        lock (lastMouseLock)
        {
            lastSentMouse = new Vector2(x, y);
            hasLastMousePayload = true;
        }
    }

}
