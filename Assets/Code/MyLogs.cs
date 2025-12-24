using UnityEngine;
using System.IO;

public static class MyLogs
{
    private static readonly object InitLock = new object();
    private static readonly object FileLock = new object();
    private static string persistentRoot;
    private static bool hasPersistentPath;

    public static void Initialize()
    {
        if (hasPersistentPath)
        {
            return;
        }

        lock (InitLock)
        {
            if (hasPersistentPath)
            {
                return;
            }

            persistentRoot = Application.persistentDataPath;
            hasPersistentPath = true;
        }
    }

    public static void Log(string message, bool saveToFile = true, bool overwriteFile = false)
    {
        Debug.Log("##>>>> " + message);
        if (!saveToFile)
        {
            return;
        }

        if (TryResolveFilePath("debug_log.txt", out string filePath))
        {
            WriteContent(filePath, $"[{System.DateTime.Now:HH:mm:ss}] {message}\n", !overwriteFile);
        }
    }
    
    public static void WriteTextToFile(string fileName, string content, bool append = true)
    {
        if (!TryResolveFilePath(fileName, out string filePath))
        {
            return;
        }

        WriteContent(filePath, content, append);
    }

    private static bool TryResolveFilePath(string fileName, out string filePath)
    {
        filePath = null;
        if (string.IsNullOrEmpty(fileName) || !hasPersistentPath)
        {
            return false;
        }

        filePath = Path.Combine(persistentRoot, fileName);
        return true;
    }

    private static void WriteContent(string filePath, string content, bool append)
    {
        try
        {
            lock (FileLock)
            {
                if (append)
                {
                    File.AppendAllText(filePath, content);
                }
                else
                {
                    File.WriteAllText(filePath, content);
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("Failed to write to file: " + e.Message);
        }
    }
}
