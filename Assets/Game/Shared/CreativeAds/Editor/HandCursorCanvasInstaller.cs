using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class HandCursorCanvasInstaller
{
    private const string MenuPath =
        "Tools/Creative Ads/El Cursor Canvas Ekle";

    private const string PrefabPath =
        "Assets/Game/Shared/CreativeAds/Prefabs/HandCursorCanvas.prefab";

    private const string InstanceName =
        "HandCursorCanvas";

    [MenuItem(MenuPath, false, 100)]
    private static void AddHandCursorCanvas()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog(
                "El Cursor Canvas",
                "Bu işlem yalnızca Edit Mode sırasında kullanılabilir.",
                "Tamam"
            );

            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();

        if (!activeScene.IsValid() || !activeScene.isLoaded)
        {
            EditorUtility.DisplayDialog(
                "El Cursor Canvas",
                "Aktif ve açık bir sahne bulunamadı.",
                "Tamam"
            );

            return;
        }

        GameObject existingInstance =
            FindExistingInstance(activeScene);

        if (existingInstance != null)
        {
            Selection.activeGameObject = existingInstance;
            EditorGUIUtility.PingObject(existingInstance);

            Debug.Log(
                "HandCursorCanvas zaten sahnede bulunuyor.",
                existingInstance
            );

            return;
        }

        GameObject prefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        if (prefab == null)
        {
            EditorUtility.DisplayDialog(
                "Prefab Bulunamadı",
                $"Prefab belirtilen konumda bulunamadı:\n\n{PrefabPath}",
                "Tamam"
            );

            Debug.LogError(
                $"HandCursorCanvas prefabı bulunamadı: {PrefabPath}"
            );

            return;
        }

        GameObject instance =
            PrefabUtility.InstantiatePrefab(
                prefab,
                activeScene
            ) as GameObject;

        if (instance == null)
        {
            EditorUtility.DisplayDialog(
                "Oluşturma Hatası",
                "Prefab sahneye eklenemedi.",
                "Tamam"
            );

            return;
        }

        instance.name = InstanceName;

        Undo.RegisterCreatedObjectUndo(
            instance,
            "El Cursor Canvas Ekle"
        );

        EditorSceneManager.MarkSceneDirty(activeScene);

        Selection.activeGameObject = instance;
        EditorGUIUtility.PingObject(instance);

        Debug.Log(
            "HandCursorCanvas aktif sahneye eklendi.",
            instance
        );
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateAddHandCursorCanvas()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    private static GameObject FindExistingInstance(Scene scene)
    {
        GameObject[] rootObjects = scene.GetRootGameObjects();

        foreach (GameObject rootObject in rootObjects)
        {
            if (rootObject.name == InstanceName)
            {
                return rootObject;
            }
        }

        return null;
    }
}