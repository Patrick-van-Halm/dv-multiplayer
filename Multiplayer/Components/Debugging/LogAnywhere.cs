using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Multiplayer.Components.Debugging;

internal sealed class LogAnywhere : MonoBehaviour
{
    private string filename;

    public static void StartCapture(string modDirectory)
    {
        if (string.IsNullOrWhiteSpace(modDirectory) ||
            FindObjectOfType<LogAnywhere>() != null)
        {
            return;
        }

        GameObject gameObject = new("[Multiplayer Log Capture]");
        gameObject.SetActive(false);
        LogAnywhere capture = gameObject.AddComponent<LogAnywhere>();
        capture.filename = Path.Combine(
            modDirectory,
            $"log_{DateTime.Now.ToString(
                "yyyyMMdd_HHmmss_fff",
                CultureInfo.InvariantCulture)}.txt");

        try
        {
            Directory.CreateDirectory(modDirectory);
            File.AppendAllText(
                capture.filename,
                $"Log started {DateTime.Now:O}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never prevent the mod from loading.
        }

        DontDestroyOnLoad(gameObject);
        gameObject.SetActive(true);
    }

    private void OnEnable()
    {
        Application.logMessageReceived += Log;
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= Log;
    }

    public void Log(
        string logString,
        string stackTrace,
        LogType type)
    {
        if (string.IsNullOrEmpty(filename))
        {
            return;
        }

        try
        {
            File.AppendAllText(
                filename,
                logString + Environment.NewLine);
        }
        catch
        {
            // Logging must never throw back into Unity's log pipeline.
        }
    }
}
