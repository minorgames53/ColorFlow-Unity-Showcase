using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class BuildSettingsExporter
{
    private const string PlayerSettingsPath =
        "ProjectSettings/ProjectSettings.asset";

    // Player Settings dosyasını doğrudan clipboard'a kopyalar.
    [MenuItem("Tools/Build/Player Settings'i Panoya Kopyala")]
    private static void CopyPlayerSettingsToClipboard()
    {
        AssetDatabase.SaveAssets();

        string projectRoot = GetProjectRoot();
        string fullPath = Path.Combine(projectRoot, PlayerSettingsPath);

        if (!File.Exists(fullPath))
        {
            EditorUtility.DisplayDialog(
                "Player Settings bulunamadı",
                $"Dosya bulunamadı:\n{fullPath}",
                "Tamam"
            );

            return;
        }

        string contents = File.ReadAllText(fullPath);

        var output = new StringBuilder();

        output.AppendLine("UNITY PLAYER SETTINGS");
        output.AppendLine("=====================");
        output.AppendLine($"Unity Version: {Application.unityVersion}");
        output.AppendLine(
            $"Active Build Target: {EditorUserBuildSettings.activeBuildTarget}"
        );
        output.AppendLine($"Export Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        output.AppendLine();
        output.AppendLine(contents);

        EditorGUIUtility.systemCopyBuffer = output.ToString();

        Debug.Log(
            $"Player Settings panoya kopyalandı. Karakter sayısı: {output.Length}"
        );

        EditorUtility.DisplayDialog(
            "Tamamlandı",
            "Player Settings panoya kopyalandı.\n\n" +
            "Artık GPT konuşmasına yapıştırabilirsin.",
            "Tamam"
        );
    }

    // Player Settings dışında build'i etkileyebilecek diğer önemli
    // ProjectSettings ve Packages dosyalarını da dışarı aktarır.
    [MenuItem("Tools/Build/GPT İçin Tüm Build Ayarlarını Dışa Aktar")]
    private static void ExportAllBuildSettings()
    {
        AssetDatabase.SaveAssets();

        string projectRoot = GetProjectRoot();

        string selectedFolder = EditorUtility.SaveFolderPanel(
            "Build ayarlarının kaydedileceği klasörü seç",
            projectRoot,
            string.Empty
        );

        if (string.IsNullOrWhiteSpace(selectedFolder))
            return;

        string exportFolder = Path.Combine(
            selectedFolder,
            $"UnityBuildSettings_{DateTime.Now:yyyyMMdd_HHmmss}"
        );

        Directory.CreateDirectory(exportFolder);

        string[] filesToExport =
        {
            // Unity ve paket bilgileri
            "ProjectSettings/ProjectVersion.txt",
            "Packages/manifest.json",
            "Packages/packages-lock.json",

            // Ana Player Settings
            "ProjectSettings/ProjectSettings.asset",

            // Build sahneleri ve build ayarları
            "ProjectSettings/EditorBuildSettings.asset",

            // Rendering ve kalite
            "ProjectSettings/GraphicsSettings.asset",
            "ProjectSettings/QualitySettings.asset",

            // Physics
            "ProjectSettings/DynamicsManager.asset",
            "ProjectSettings/Physics2DSettings.asset",

            // Diğer önemli proje ayarları
            "ProjectSettings/AudioManager.asset",
            "ProjectSettings/InputManager.asset",
            "ProjectSettings/TagManager.asset",
            "ProjectSettings/TimeManager.asset",
            "ProjectSettings/NavMeshAreas.asset",
            "ProjectSettings/MemorySettings.asset"
        };

        int copiedFileCount = 0;
        var copiedFiles = new StringBuilder();

        foreach (string relativePath in filesToExport)
        {
            string sourcePath = Path.Combine(projectRoot, relativePath);

            if (!File.Exists(sourcePath))
                continue;

            string destinationPath = Path.Combine(exportFolder, relativePath);
            string destinationDirectory =
                Path.GetDirectoryName(destinationPath);

            if (!string.IsNullOrEmpty(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            File.Copy(sourcePath, destinationPath, true);

            copiedFileCount++;
            copiedFiles.AppendLine($"- {relativePath}");
        }

        string readmePath = Path.Combine(
            exportFolder,
            "README_FOR_GPT.txt"
        );

        File.WriteAllText(
            readmePath,
            CreateGptReadme(copiedFiles.ToString()),
            Encoding.UTF8
        );

        EditorUtility.RevealInFinder(exportFolder);

        EditorUtility.DisplayDialog(
            "Dışa aktarma tamamlandı",
            $"{copiedFileCount} ayar dosyası dışa aktarıldı.\n\n" +
            exportFolder,
            "Tamam"
        );

        Debug.Log($"Build ayarları dışa aktarıldı: {exportFolder}");
    }

    private static string GetProjectRoot()
    {
        return Directory.GetParent(Application.dataPath)?.FullName
               ?? throw new DirectoryNotFoundException(
                   "Unity proje klasörü bulunamadı."
               );
    }

    private static string CreateGptReadme(string copiedFiles)
    {
        var result = new StringBuilder();

        result.AppendLine("UNITY BUILD SETTINGS EXPORT");
        result.AppendLine("===========================");
        result.AppendLine();
        result.AppendLine($"Unity Version: {Application.unityVersion}");
        result.AppendLine(
            $"Active Build Target: {EditorUserBuildSettings.activeBuildTarget}"
        );
        result.AppendLine(
            $"Active Build Target Group: " +
            $"{BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget)}"
        );
        result.AppendLine($"Export Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        result.AppendLine();

        result.AppendLine("GPT İÇİN AÇIKLAMA");
        result.AppendLine("-----------------");
        result.AppendLine(
            "Bu klasör Unity projesinin Player Settings, build scenes, " +
            "graphics, quality, physics ve package ayarlarını içerir."
        );
        result.AppendLine();
        result.AppendLine(
            "Lütfen bu ayarları incele, muhtemel build sorunlarını belirt " +
            "ve önerdiğin değişiklikleri ayar adı + mevcut değer + " +
            "önerilen değer şeklinde listele."
        );
        result.AppendLine();

        result.AppendLine("KOPYALANAN DOSYALAR");
        result.AppendLine("-------------------");
        result.AppendLine(copiedFiles);

        return result.ToString();
    }
}