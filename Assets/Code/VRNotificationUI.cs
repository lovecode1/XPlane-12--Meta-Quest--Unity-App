using System.Collections;
using TMPro;
using UnityEngine;

public class VRNotificationUI : MonoBehaviour
{
    private static VRNotificationUI instance;

    [SerializeField] private Canvas notificationCanvas;
    [SerializeField] private GameObject notificationPanel;
    [SerializeField] private TMP_Text notificationText;
    [SerializeField] private float displayDuration = 1f;
    [SerializeField] private float fadeInDuration = 0.1f;
    [SerializeField] private float fadeOutDuration = 0.1f;

    private CanvasGroup canvasGroup;
    private Coroutine currentNotificationCoroutine;
    private Camera playerCamera;

    public static VRNotificationUI Instance
    {
        get
        {
            if (instance == null)
            {
                instance = LocateExistingInstance();
                if (instance != null && !instance.gameObject.activeSelf)
                {
                    instance.gameObject.SetActive(true);
                }

                if (instance == null)
                {
                    MyLogs.Log("[WARN] VRNotificationUI: Instance not found in scene.");
                }
            }
            return instance;
        }
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        EnsureReferences();
        HideNotificationImmediate();
    }

    void Start()
    {
        FindPlayerCamera();
    }

    private void EnsureReferences()
    {
        if (notificationCanvas == null)
        {
            notificationCanvas = GetComponentInChildren<Canvas>(true);
        }

        if (notificationPanel == null && notificationCanvas != null)
        {
            notificationPanel = notificationCanvas.transform.parent != null
                ? notificationCanvas.transform.parent.gameObject
                : notificationCanvas.gameObject;
        }

        if (notificationText == null && notificationPanel != null)
        {
            notificationText = notificationPanel.GetComponentInChildren<TMP_Text>(true);
        }

        if (notificationPanel != null)
        {
            canvasGroup = notificationPanel.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = notificationPanel.AddComponent<CanvasGroup>();
            }
        }
    }

    private void FindPlayerCamera()
    {
        OVRCameraRig cameraRig = FindObjectOfType<OVRCameraRig>();
        if (cameraRig != null && cameraRig.centerEyeAnchor != null)
        {
            playerCamera = cameraRig.centerEyeAnchor.GetComponent<Camera>();
        }
        
        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }
    }

    public static void ShowNotification(string message)
    {
        if (Instance != null)
        {
            Instance.DisplayNotification(message);
        }
    }

    private void DisplayNotification(string message)
    {
        if (currentNotificationCoroutine != null)
        {
            StopCoroutine(currentNotificationCoroutine);
        }
        
        if (!isActiveAndEnabled)
        {
            gameObject.SetActive(true);
        }

        currentNotificationCoroutine = StartCoroutine(ShowNotificationCoroutine(message));
    }

    private IEnumerator ShowNotificationCoroutine(string message)
    {
        EnsureReferences();
        if (notificationPanel == null || notificationText == null)
        {
            MyLogs.Log("[WARN] VRNotificationUI: Missing UI references; cannot display notification.");
            yield break;
        }

        notificationText.text = message;
        notificationPanel.SetActive(true);
        gameObject.SetActive(true);
        
        yield return new WaitForSeconds(displayDuration);
        
        HideNotificationImmediate();
    }

    private static VRNotificationUI LocateExistingInstance()
    {
        VRNotificationUI activeInstance = FindObjectOfType<VRNotificationUI>();
        if (activeInstance != null)
        {
            return activeInstance;
        }

        VRNotificationUI[] allInstances = Resources.FindObjectsOfTypeAll<VRNotificationUI>();
        if (allInstances != null && allInstances.Length > 0)
        {
            return allInstances[0];
        }

        return null;
    }

    private void HideNotificationImmediate()
    {
        if (notificationPanel != null)
        {
            notificationPanel.SetActive(false);
        }

        gameObject.SetActive(false);
    }
}
