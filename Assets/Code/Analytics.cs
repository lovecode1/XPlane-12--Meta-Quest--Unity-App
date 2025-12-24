using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

public static class Analytics
{
    public static string EXCEPTION = "EXCEPTION";
    public static string ERROR = "ERROR";
    
    public static void Log(string eventName)
    {
        CoroutineRunner.Instance.StartCoroutine(SendAnalytics(eventName, null));
    }

    public static void ErrorLog(string errorMessage)
    {
        CoroutineRunner.Instance.StartCoroutine(SendAnalytics(ERROR, new Dictionary<string, string>
        {
            { "error", errorMessage }
        }));

        MyLogs.Log($"Error: Analytics: {errorMessage}");
    }
    
    public static void Log(string eventName, Dictionary<string, string> properties)
    {
        CoroutineRunner.Instance.StartCoroutine(SendAnalytics(eventName, properties));

        MyLogs.Log($"eventName: {eventName}");
    }
    
}

public class CoroutineRunner : MonoBehaviour
{
    private static CoroutineRunner _instance;
    
    public static CoroutineRunner Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("CoroutineRunner");
                _instance = go.AddComponent<CoroutineRunner>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }
}