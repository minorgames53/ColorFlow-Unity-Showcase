#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public sealed class FontAssetReplacerWindow : EditorWindow
{
    private const string UndoName = "Replace Font Assets";
    private static readonly BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;

    private enum SceneScope
    {
        ActiveScene,
        OpenScenes,
        AllProjectScenes
    }

    [Serializable]
    private class FontAssetMap
    {
        public Object from;
        public Object to;
    }

    private class ReplaceResult
    {
        public int scenesScanned;
        public int scenesChanged;
        public int scenesSaved;
        public int componentsScanned;
        public int componentsChanged;
        public int missingScripts;
        public int referencesMatched;
        public bool aborted;
    }

    [SerializeField] private SceneScope sceneScope = SceneScope.ActiveScene;
    [SerializeField] private bool saveScenesAfterApply = true;
    [SerializeField] private bool replaceTmpSharedMaterial = false;
    [SerializeField] private bool logDetailsToConsole = false;
    [SerializeField] private List<FontAssetMap> maps = new List<FontAssetMap>();

    private Vector2 scroll;

    [MenuItem("Tools/Font Asset Replacer")]
    public static void ShowWindow()
    {
        FontAssetReplacerWindow window = GetWindow<FontAssetReplacerWindow>("Font Replacer");
        window.minSize = new Vector2(600, 380);
        window.Show();
    }

    private void OnEnable()
    {
        if (maps == null)
            maps = new List<FontAssetMap>();

        if (maps.Count == 0)
            maps.Add(new FontAssetMap());
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Scene Font Asset Replacer", EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "X asset → Y asset eşleştirmeleri ekle. Tool sahnedeki component'lerin serialized object reference alanlarını tarar. TMP_FontAsset, Font, Material veya custom asset referanslarında çalışabilir.",
            MessageType.Info);

        sceneScope = (SceneScope)EditorGUILayout.EnumPopup("Kapsam", sceneScope);

        using (new EditorGUI.DisabledScope(sceneScope == SceneScope.AllProjectScenes))
        {
            saveScenesAfterApply = EditorGUILayout.ToggleLeft(
                "Değişiklik sonrası açık/aktif sahneleri kaydet",
                saveScenesAfterApply);
        }

        if (sceneScope == SceneScope.AllProjectScenes)
        {
            EditorGUILayout.HelpBox(
                "All Project Scenes modunda değişen sahneler otomatik kaydedilir. İşlemden önce açık sahnelerdeki kaydedilmemiş değişiklikler için Unity onay sorar.",
                MessageType.Warning);
        }

        replaceTmpSharedMaterial = EditorGUILayout.ToggleLeft(
            "TMP font değişince shared material'i yeni font'un default material'iyle değiştir",
            replaceTmpSharedMaterial);

        logDetailsToConsole = EditorGUILayout.ToggleLeft(
            "Detayları Console'a yaz",
            logDetailsToConsole);

        EditorGUILayout.Space(8);
        DrawMappings();

        EditorGUILayout.Space(8);

        bool hasValidMap = GetValidMap().Count > 0;

        using (new EditorGUI.DisabledScope(!hasValidMap || EditorApplication.isPlayingOrWillChangePlaymode))
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Önizleme / Say"))
                Run(apply: false);

            if (GUILayout.Button("Değiştir"))
                Run(apply: true);

            EditorGUILayout.EndHorizontal();
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorGUILayout.HelpBox(
                "Play Mode sırasında çalıştırma kapalı. Edit Mode'da kullan.",
                MessageType.Warning);
        }
    }

    private void DrawMappings()
    {
        EditorGUILayout.LabelField("Eşleştirmeler", EditorStyles.boldLabel);

        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(120));

        int removeIndex = -1;

        for (int i = 0; i < maps.Count; i++)
        {
            if (maps[i] == null)
                maps[i] = new FontAssetMap();

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField((i + 1).ToString(), GUILayout.Width(22));

            maps[i].from = EditorGUILayout.ObjectField(
                maps[i].from,
                typeof(Object),
                false,
                GUILayout.MinWidth(180));

            GUILayout.Label("→", GUILayout.Width(18));

            maps[i].to = EditorGUILayout.ObjectField(
                maps[i].to,
                typeof(Object),
                false,
                GUILayout.MinWidth(180));

            if (GUILayout.Button("X", GUILayout.Width(28)))
                removeIndex = i;

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        if (removeIndex >= 0)
            maps.RemoveAt(removeIndex);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("+ Eşleştirme Ekle"))
            maps.Add(new FontAssetMap());

        if (GUILayout.Button("Boş Satırları Temizle"))
            maps.RemoveAll(m => m == null || (m.from == null && m.to == null));

        EditorGUILayout.EndHorizontal();

        Object duplicateFrom = maps
            .Where(m => m != null && m.from != null)
            .GroupBy(m => m.from)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .FirstOrDefault();

        if (duplicateFrom != null)
        {
            EditorGUILayout.HelpBox(
                $"Aynı kaynak asset birden fazla kez girilmiş: {duplicateFrom.name}. Son değer kullanılacaktır.",
                MessageType.Warning);
        }
    }

    private Dictionary<Object, Object> GetValidMap()
    {
        Dictionary<Object, Object> result = new Dictionary<Object, Object>();

        foreach (FontAssetMap map in maps)
        {
            if (map == null)
                continue;

            if (map.from == null || map.to == null)
                continue;

            if (map.from == map.to)
                continue;

            result[map.from] = map.to;
        }

        return result;
    }

    private void Run(bool apply)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog(
                "Font Replacer",
                "Bu tool'u Play Mode sırasında çalıştırma. Edit Mode'a geçip tekrar dene.",
                "OK");
            return;
        }

        Dictionary<Object, Object> validMap = GetValidMap();

        if (validMap.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Font Replacer",
                "Geçerli en az bir eşleştirme ekle: From ve To dolu olmalı, aynı asset olmamalı.",
                "OK");
            return;
        }

        if (apply)
        {
            string message = sceneScope == SceneScope.AllProjectScenes
                ? "Projede Assets altındaki tüm sahneler taranacak ve değişen sahneler kaydedilecek. Devam edilsin mi?"
                : "Seçili kapsamdaki sahnelerde eşleşen referanslar değiştirilecek. Devam edilsin mi?";

            if (!EditorUtility.DisplayDialog("Font Replacer", message, "Değiştir", "İptal"))
                return;
        }

        ReplaceResult result = new ReplaceResult();

        try
        {
            switch (sceneScope)
            {
                case SceneScope.ActiveScene:
                    ProcessActiveScene(validMap, apply, result);
                    break;

                case SceneScope.OpenScenes:
                    ProcessOpenScenes(validMap, apply, result);
                    break;

                case SceneScope.AllProjectScenes:
                    ProcessAllProjectScenes(validMap, apply, result);
                    break;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        string title = apply ? "Değiştirme tamamlandı" : "Önizleme tamamlandı";
        string verb = apply ? "değiştirildi" : "bulundu";

        string summary =
            $"{result.scenesScanned} sahne tarandı.\n" +
            $"{result.componentsScanned} component tarandı.\n" +
            $"{result.referencesMatched} referans {verb}.\n" +
            $"{result.componentsChanged} component değişti.\n" +
            $"{result.scenesChanged} sahne değişti.\n" +
            $"{result.scenesSaved} sahne kaydedildi.\n" +
            $"{result.missingScripts} missing script atlandı.";

        if (result.aborted)
            summary += "\n\nİşlem kullanıcı tarafından iptal edildi veya açık sahneler kaydedilmediği için durduruldu.";

        EditorUtility.DisplayDialog(title, summary, "OK");
    }

    private void ProcessActiveScene(Dictionary<Object, Object> map, bool apply, ReplaceResult result)
    {
        Scene scene = SceneManager.GetActiveScene();

        int hits = ProcessLoadedScene(scene, map, apply, result);
        SaveSceneIfNeeded(scene, hits, apply, forceSave: false, result);
    }

    private void ProcessOpenScenes(Dictionary<Object, Object> map, bool apply, ReplaceResult result)
    {
        List<Scene> scenes = new List<Scene>();

        for (int i = 0; i < SceneManager.sceneCount; i++)
            scenes.Add(SceneManager.GetSceneAt(i));

        for (int i = 0; i < scenes.Count; i++)
        {
            Scene scene = scenes[i];

            if (EditorUtility.DisplayCancelableProgressBar(
                    "Font Asset Replacer",
                    $"Sahne taranıyor: {scene.path}",
                    scenes.Count == 0 ? 1f : (float)i / scenes.Count))
            {
                result.aborted = true;
                return;
            }

            int hits = ProcessLoadedScene(scene, map, apply, result);
            SaveSceneIfNeeded(scene, hits, apply, forceSave: false, result);
        }
    }

    private void ProcessAllProjectScenes(Dictionary<Object, Object> map, bool apply, ReplaceResult result)
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            result.aborted = true;
            return;
        }

        SceneSetup[] originalSetup = EditorSceneManager.GetSceneManagerSetup();

        string[] scenePaths = AssetDatabase
            .FindAssets("t:Scene", new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path)
            .ToArray();

        try
        {
            for (int i = 0; i < scenePaths.Length; i++)
            {
                string scenePath = scenePaths[i];

                if (EditorUtility.DisplayCancelableProgressBar(
                        "Font Asset Replacer",
                        $"Sahne açılıyor: {scenePath}",
                        scenePaths.Length == 0 ? 1f : (float)i / scenePaths.Length))
                {
                    result.aborted = true;
                    return;
                }

                try
                {
                    Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                    int hits = ProcessLoadedScene(scene, map, apply, result);

                    // All Project Scenes modunda değişiklikler kaydedilmezse bir sonraki sahneye geçerken kaybolabilir.
                    SaveSceneIfNeeded(scene, hits, apply, forceSave: true, result);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FontAssetReplacer] Sahne işlenemedi: {scenePath}\n{ex.Message}");
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();

            if (originalSetup != null && originalSetup.Length > 0)
                EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
        }
    }

    private void SaveSceneIfNeeded(Scene scene, int hits, bool apply, bool forceSave, ReplaceResult result)
    {
        if (!apply || hits <= 0 || !scene.IsValid())
            return;

        EditorSceneManager.MarkSceneDirty(scene);
        result.scenesChanged++;

        bool shouldSave = forceSave || saveScenesAfterApply;

        if (shouldSave && EditorSceneManager.SaveScene(scene))
            result.scenesSaved++;
    }

    private int ProcessLoadedScene(
        Scene scene,
        Dictionary<Object, Object> map,
        bool apply,
        ReplaceResult result)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return 0;

        result.scenesScanned++;

        int sceneHits = 0;
        GameObject[] roots = scene.GetRootGameObjects();

        foreach (GameObject root in roots)
        {
            Component[] components = root.GetComponentsInChildren<Component>(true);

            foreach (Component component in components)
            {
                if (component == null)
                {
                    result.missingScripts++;
                    continue;
                }

                sceneHits += ProcessComponent(scene, component, map, apply, result);
            }
        }

        return sceneHits;
    }

    private int ProcessComponent(
        Scene scene,
        Component component,
        Dictionary<Object, Object> map,
        bool apply,
        ReplaceResult result)
    {
        result.componentsScanned++;

        int hits = 0;
        bool genericChanged = false;
        bool undoRecorded = false;

        Object tmpOldFont = null;
        Object tmpNewFont = null;

        try
        {
            SerializedObject serializedObject = new SerializedObject(component);
            SerializedProperty property = serializedObject.GetIterator();

            bool enterChildren = true;

            while (property.Next(enterChildren))
            {
                enterChildren = false;

                if (property.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                Object current = property.objectReferenceValue;

                if (current == null)
                    continue;

                if (!map.TryGetValue(current, out Object replacement) || replacement == null)
                    continue;

                hits++;
                result.referencesMatched++;

                bool isTmpMainFont =
                    IsTmpMainFontProperty(component, property.propertyPath) &&
                    CanAssignTmpFont(component, replacement);

                if (isTmpMainFont)
                {
                    tmpOldFont = current;
                    tmpNewFont = replacement;

                    if (!apply && logDetailsToConsole)
                        LogReplacement(scene, component, "TMP_Text.font", current, replacement, apply);

                    continue;
                }

                if (!apply)
                {
                    if (logDetailsToConsole)
                        LogReplacement(scene, component, property.propertyPath, current, replacement, apply);

                    continue;
                }

                if (!undoRecorded)
                {
                    Undo.RecordObject(component, UndoName);
                    undoRecorded = true;
                }

                property.objectReferenceValue = replacement;
                genericChanged = true;

                if (logDetailsToConsole)
                    LogReplacement(scene, component, property.propertyPath, current, replacement, apply);
            }

            if (apply && genericChanged)
                serializedObject.ApplyModifiedProperties();

            bool tmpChanged = false;

            if (apply && tmpNewFont != null)
            {
                if (!undoRecorded)
                {
                    Undo.RecordObject(component, UndoName);
                    undoRecorded = true;
                }

                tmpChanged = TryAssignTmpFont(component, tmpNewFont);

                if (tmpChanged && logDetailsToConsole)
                    LogReplacement(scene, component, "TMP_Text.font", tmpOldFont, tmpNewFont, apply);
            }

            if (apply && (genericChanged || tmpChanged))
            {
                result.componentsChanged++;

                EditorUtility.SetDirty(component);

                if (PrefabUtility.IsPartOfPrefabInstance(component))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning(
                $"[FontAssetReplacer] Component işlenemedi: {GetComponentPath(component)}\n{ex.Message}",
                component);
        }

        return hits;
    }

    private static bool IsTmpMainFontProperty(Component component, string propertyPath)
    {
        return propertyPath == "m_fontAsset" &&
               InheritsFromFullName(component.GetType(), "TMPro.TMP_Text");
    }

    private static bool CanAssignTmpFont(Component component, Object fontAsset)
    {
        if (component == null || fontAsset == null)
            return false;

        PropertyInfo fontProperty = component.GetType().GetProperty("font", PublicInstance);

        return fontProperty != null &&
               fontProperty.CanWrite &&
               fontProperty.PropertyType.IsInstanceOfType(fontAsset);
    }

    private bool TryAssignTmpFont(Component component, Object newFontAsset)
    {
        try
        {
            Type componentType = component.GetType();

            PropertyInfo fontProperty = componentType.GetProperty("font", PublicInstance);

            if (fontProperty == null ||
                !fontProperty.CanWrite ||
                !fontProperty.PropertyType.IsInstanceOfType(newFontAsset))
            {
                return false;
            }

            fontProperty.SetValue(component, newFontAsset, null);

            if (replaceTmpSharedMaterial)
            {
                PropertyInfo fontMaterialProperty = newFontAsset.GetType().GetProperty("material", PublicInstance);
                Material defaultMaterial = fontMaterialProperty != null
                    ? fontMaterialProperty.GetValue(newFontAsset, null) as Material
                    : null;

                PropertyInfo sharedMaterialProperty = componentType.GetProperty("fontSharedMaterial", PublicInstance);

                if (defaultMaterial != null &&
                    sharedMaterialProperty != null &&
                    sharedMaterialProperty.CanWrite)
                {
                    sharedMaterialProperty.SetValue(component, defaultMaterial, null);
                }
            }

            PropertyInfo havePropertiesChanged = componentType.GetProperty("havePropertiesChanged", PublicInstance);

            if (havePropertiesChanged != null &&
                havePropertiesChanged.CanWrite &&
                havePropertiesChanged.PropertyType == typeof(bool))
            {
                havePropertiesChanged.SetValue(component, true, null);
            }

            InvokeNoArg(component, "SetVerticesDirty");
            InvokeNoArg(component, "SetMaterialDirty");
            InvokeNoArg(component, "SetLayoutDirty");

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning(
                $"[FontAssetReplacer] TMP font property atanamadı: {GetComponentPath(component)}\n{ex.Message}",
                component);

            return false;
        }
    }

    private static void InvokeNoArg(Component component, string methodName)
    {
        MethodInfo method = component
            .GetType()
            .GetMethod(methodName, PublicInstance, null, Type.EmptyTypes, null);

        if (method != null)
            method.Invoke(component, null);
    }

    private static bool InheritsFromFullName(Type type, string fullName)
    {
        while (type != null)
        {
            if (type.FullName == fullName)
                return true;

            type = type.BaseType;
        }

        return false;
    }

    private void LogReplacement(
        Scene scene,
        Component component,
        string propertyPath,
        Object from,
        Object to,
        bool apply)
    {
        string action = apply ? "Changed" : "Found";

        Debug.Log(
            $"[FontAssetReplacer] {action}: {scene.path} :: {GetComponentPath(component)} :: {propertyPath} :: {GetObjectLabel(from)} → {GetObjectLabel(to)}",
            component);
    }

    private static string GetComponentPath(Component component)
    {
        if (component == null)
            return "<null component>";

        Stack<string> names = new Stack<string>();
        Transform current = component.transform;

        while (current != null)
        {
            names.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", names.ToArray()) + $" ({component.GetType().Name})";
    }

    private static string GetObjectLabel(Object obj)
    {
        if (obj == null)
            return "<null>";

        string path = AssetDatabase.GetAssetPath(obj);

        if (string.IsNullOrEmpty(path))
            return obj.name;

        return $"{obj.name} [{path}]";
    }
}
#endif