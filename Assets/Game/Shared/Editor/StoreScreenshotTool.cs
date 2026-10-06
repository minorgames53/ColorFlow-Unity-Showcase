using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class StoreScreenshotTool
{
    [MenuItem("Tools/Capture Store Screenshot _F12")]
    private static void CaptureScreenshot()
    {
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        string outputFolder = Path.Combine(projectRoot, "StoreScreenshots");

        Directory.CreateDirectory(outputFolder);

        string fileName =
            $"ColorFlow_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";

        string outputPath = Path.Combine(outputFolder, fileName);

        // Game View 1080x1920 ise sonuç 2160x3840 olur.
        ScreenCapture.CaptureScreenshot(outputPath, 2);

        Debug.Log($"Store screenshot kaydediliyor: {outputPath}");
    }
}