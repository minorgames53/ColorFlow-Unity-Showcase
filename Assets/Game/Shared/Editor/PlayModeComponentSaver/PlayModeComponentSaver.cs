#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PlayModeComponentSaverTool
{
    internal enum SavedComponentStatus
    {
        Pending,
        Applied,
        Failed
    }

    [Serializable]
    internal sealed class SavedObjectReference
    {
        public string propertyPath;
        public bool isNull;
        public bool preserveExistingValue;

        public string globalObjectId;
        public string assetGuid;
        public long assetLocalId;
        public string scenePath;
        public string hierarchyIndexPath;
        public string objectKind;
        public string componentTypeName;
        public int componentTypeIndex;
    }

    [Serializable]
    internal sealed class SavedComponentSnapshot
    {
        public string id;
        public string sessionId;
        public string targetKey;

        public string targetGlobalObjectId;
        public string scenePath;
        public string sceneName;
        public string hierarchyIndexPath;
        public string hierarchyNamePath;
        public string componentTypeName;
        public string componentDisplayName;
        public int componentTypeIndex;

        [TextArea]
        public string json;
        public List<SavedObjectReference> objectReferences = new List<SavedObjectReference>();

        public string savedUtc;
        public string appliedUtc;
        public SavedComponentStatus status;
        public string message;
    }

    [Serializable]
    internal sealed class PrePlayObjectEntry
    {
        public string catalogKey;
        public string globalObjectId;
        public string scenePath;
        public string sceneName;
        public string hierarchyIndexPath;
        public string hierarchyNamePath;
        public string objectKind;
        public string componentTypeName;
        public string componentDisplayName;
        public int componentTypeIndex;
    }

    [Serializable]
    internal sealed class RuntimeObjectBinding
    {
        public int instanceId;
        public string catalogKey;
    }

    [FilePath("Library/PlayModeComponentSaver.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class PlayModeComponentSaveStore : ScriptableSingleton<PlayModeComponentSaveStore>
    {
        [SerializeField] private string activeSessionId;
        [SerializeField] private List<PrePlayObjectEntry> prePlayCatalog = new List<PrePlayObjectEntry>();
        [SerializeField] private List<RuntimeObjectBinding> runtimeBindings = new List<RuntimeObjectBinding>();
        [SerializeField] private List<SavedComponentSnapshot> snapshots = new List<SavedComponentSnapshot>();

        internal string ActiveSessionId
        {
            get { return activeSessionId; }
            set { activeSessionId = value; }
        }

        internal List<PrePlayObjectEntry> PrePlayCatalog
        {
            get { return prePlayCatalog; }
        }

        internal List<RuntimeObjectBinding> RuntimeBindings
        {
            get { return runtimeBindings; }
        }

        internal List<SavedComponentSnapshot> Snapshots
        {
            get { return snapshots; }
        }

        internal void Persist()
        {
            Save(true);
            PlayModeComponentSaver.NotifyChanged();
        }
    }

    [InitializeOnLoad]
    internal static class PlayModeComponentSaver
    {
        private const string NullGlobalObjectId =
            "GlobalObjectId_V1-0-00000000000000000000000000000000-0-0";

        private const int MaximumHistoryCount = 200;

        private static readonly Dictionary<string, PrePlayObjectEntry> CatalogByKey =
            new Dictionary<string, PrePlayObjectEntry>();

        private static readonly Dictionary<int, string> RuntimeCatalogKeyByInstanceId =
            new Dictionary<int, string>();

        internal static event Action Changed;

        static PlayModeComponentSaver()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            RebuildCachesFromStore();
        }

        internal static void NotifyChanged()
        {
            if (Changed != null)
                Changed();
        }

        [MenuItem("CONTEXT/Component/Save Play Mode Changes", false, 500)]
        private static void SavePlayModeChanges(MenuCommand command)
        {
            Component component = command.context as Component;
            if (component == null)
                return;

            SaveComponent(component);
        }

        [MenuItem("CONTEXT/Component/Save Play Mode Changes", true)]
        private static bool ValidateSavePlayModeChanges(MenuCommand command)
        {
            return EditorApplication.isPlaying && command.context is Component;
        }

        internal static IReadOnlyList<SavedComponentSnapshot> GetSnapshots()
        {
            return PlayModeComponentSaveStore.instance.Snapshots;
        }

        internal static int GetPendingCount()
        {
            return PlayModeComponentSaveStore.instance.Snapshots.Count(
                snapshot => snapshot.status == SavedComponentStatus.Pending);
        }

        internal static void ApplyAllPendingNow()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog(
                    "Play Mode Component Saver",
                    "Pending kayıtlar yalnızca Edit Mode'da uygulanabilir.",
                    "Tamam");
                return;
            }

            ApplySnapshots(snapshot => snapshot.status == SavedComponentStatus.Pending, false);
        }

        internal static void RetrySnapshot(string snapshotId)
        {
            if (EditorApplication.isPlaying)
                return;

            SavedComponentSnapshot snapshot = PlayModeComponentSaveStore.instance.Snapshots
                .FirstOrDefault(item => item.id == snapshotId);

            if (snapshot == null)
                return;

            snapshot.status = SavedComponentStatus.Pending;
            snapshot.message = string.Empty;
            PlayModeComponentSaveStore.instance.Persist();

            ApplySnapshots(item => item.id == snapshotId, false);
        }

        internal static void RemoveSnapshot(string snapshotId)
        {
            PlayModeComponentSaveStore store = PlayModeComponentSaveStore.instance;
            store.Snapshots.RemoveAll(snapshot => snapshot.id == snapshotId);
            store.Persist();
        }

        internal static void ClearHistory(bool includePending)
        {
            PlayModeComponentSaveStore store = PlayModeComponentSaveStore.instance;

            if (includePending)
            {
                store.Snapshots.Clear();
            }
            else
            {
                store.Snapshots.RemoveAll(snapshot => snapshot.status != SavedComponentStatus.Pending);
            }

            store.Persist();
        }

        internal static Object ResolveSnapshotForSelection(SavedComponentSnapshot snapshot)
        {
            if (snapshot == null)
                return null;

            if (EditorApplication.isPlaying)
            {
                RuntimeObjectBinding binding = PlayModeComponentSaveStore.instance.RuntimeBindings
                    .FirstOrDefault(item => item.catalogKey == BuildCatalogKey(snapshot));

                if (binding != null)
                    return EditorUtility.InstanceIDToObject(binding.instanceId);
            }

            return ResolveObject(
                snapshot.targetGlobalObjectId,
                snapshot.scenePath,
                snapshot.hierarchyIndexPath,
                "Component",
                snapshot.componentTypeName,
                snapshot.componentTypeIndex);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    PrepareForPlayMode();
                    break;

                case PlayModeStateChange.EnteredPlayMode:
                    BuildRuntimeBindings();
                    break;

                case PlayModeStateChange.EnteredEditMode:
                    EditorApplication.delayCall += ApplyCurrentSessionAfterPlayMode;
                    break;
            }
        }

        private static void PrepareForPlayMode()
        {
            PlayModeComponentSaveStore store = PlayModeComponentSaveStore.instance;

            foreach (SavedComponentSnapshot snapshot in store.Snapshots)
            {
                if (snapshot.status != SavedComponentStatus.Pending)
                    continue;

                snapshot.status = SavedComponentStatus.Failed;
                snapshot.message =
                    "Yeni bir Play Mode oturumu başladı. Önceki bekleyen kayıt otomatik uygulanamadı; Viewer'dan Retry kullanabilirsin.";
            }

            store.ActiveSessionId = Guid.NewGuid().ToString("N");
            store.PrePlayCatalog.Clear();
            store.RuntimeBindings.Clear();

            BuildPrePlayCatalog(store.PrePlayCatalog);
            TrimHistory(store.Snapshots);
            store.Persist();
            RebuildCachesFromStore();
        }

        private static void BuildPrePlayCatalog(List<PrePlayObjectEntry> destination)
        {
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene))
                    continue;

                if (string.IsNullOrEmpty(scene.path))
                {
                    Debug.LogWarning(
                        "[Play Mode Component Saver] Kaydedilmemiş sahne atlandı: " + scene.name +
                        ". Tool'u kullanmadan önce sahneyi kaydet.");
                    continue;
                }

                GameObject[] roots = scene.GetRootGameObjects();
                foreach (GameObject root in roots)
                    AddGameObjectAndChildrenToCatalog(root, destination);
            }
        }

        private static void AddGameObjectAndChildrenToCatalog(
            GameObject gameObject,
            List<PrePlayObjectEntry> destination)
        {
            string scenePath = gameObject.scene.path;
            string sceneName = gameObject.scene.name;
            string indexPath = GetHierarchyIndexPath(gameObject.transform);
            string namePath = GetHierarchyNamePath(gameObject.transform);

            destination.Add(new PrePlayObjectEntry
            {
                catalogKey = BuildGameObjectCatalogKey(scenePath, indexPath),
                globalObjectId = GetGlobalObjectIdString(gameObject),
                scenePath = scenePath,
                sceneName = sceneName,
                hierarchyIndexPath = indexPath,
                hierarchyNamePath = namePath,
                objectKind = "GameObject",
                componentDisplayName = "GameObject"
            });

            Component[] components = gameObject.GetComponents<Component>();
            Dictionary<Type, int> typeCounts = new Dictionary<Type, int>();

            foreach (Component component in components)
            {
                if (component == null)
                    continue;

                Type type = component.GetType();
                int typeIndex;
                typeCounts.TryGetValue(type, out typeIndex);
                typeCounts[type] = typeIndex + 1;

                destination.Add(new PrePlayObjectEntry
                {
                    catalogKey = BuildComponentCatalogKey(
                        scenePath,
                        indexPath,
                        type.AssemblyQualifiedName,
                        typeIndex),
                    globalObjectId = GetGlobalObjectIdString(component),
                    scenePath = scenePath,
                    sceneName = sceneName,
                    hierarchyIndexPath = indexPath,
                    hierarchyNamePath = namePath,
                    objectKind = "Component",
                    componentTypeName = type.AssemblyQualifiedName,
                    componentDisplayName = type.Name,
                    componentTypeIndex = typeIndex
                });
            }

            Transform transform = gameObject.transform;
            for (int i = 0; i < transform.childCount; i++)
                AddGameObjectAndChildrenToCatalog(transform.GetChild(i).gameObject, destination);
        }

        private static void BuildRuntimeBindings()
        {
            PlayModeComponentSaveStore store = PlayModeComponentSaveStore.instance;
            store.RuntimeBindings.Clear();
            RebuildCatalogCache(store.PrePlayCatalog);

            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.path))
                    continue;

                GameObject[] roots = scene.GetRootGameObjects();
                foreach (GameObject root in roots)
                    AddRuntimeBindingsForGameObject(root, store.RuntimeBindings);
            }

            store.Persist();
            RebuildRuntimeBindingCache(store.RuntimeBindings);
        }

        private static void AddRuntimeBindingsForGameObject(
            GameObject gameObject,
            List<RuntimeObjectBinding> destination)
        {
            string scenePath = gameObject.scene.path;
            string indexPath = GetHierarchyIndexPath(gameObject.transform);

            TryAddRuntimeBinding(
                gameObject.GetInstanceID(),
                BuildGameObjectCatalogKey(scenePath, indexPath),
                destination);

            Component[] components = gameObject.GetComponents<Component>();
            Dictionary<Type, int> typeCounts = new Dictionary<Type, int>();

            foreach (Component component in components)
            {
                if (component == null)
                    continue;

                Type type = component.GetType();
                int typeIndex;
                typeCounts.TryGetValue(type, out typeIndex);
                typeCounts[type] = typeIndex + 1;

                TryAddRuntimeBinding(
                    component.GetInstanceID(),
                    BuildComponentCatalogKey(
                        scenePath,
                        indexPath,
                        type.AssemblyQualifiedName,
                        typeIndex),
                    destination);
            }

            Transform transform = gameObject.transform;
            for (int i = 0; i < transform.childCount; i++)
                AddRuntimeBindingsForGameObject(transform.GetChild(i).gameObject, destination);
        }

        private static void TryAddRuntimeBinding(
            int instanceId,
            string catalogKey,
            List<RuntimeObjectBinding> destination)
        {
            if (!CatalogByKey.ContainsKey(catalogKey))
                return;

            destination.Add(new RuntimeObjectBinding
            {
                instanceId = instanceId,
                catalogKey = catalogKey
            });
        }

        private static void SaveComponent(Component component)
        {
            if (!EditorApplication.isPlaying)
                return;

            PrePlayObjectEntry targetEntry;
            if (!TryGetPrePlayEntry(component, out targetEntry))
            {
                EditorUtility.DisplayDialog(
                    "Play Mode Component Saver",
                    "Bu component Play Mode başlamadan önce eşleştirilemedi.\n\n" +
                    "Muhtemel nedenler:\n" +
                    "• Obje/component runtime'da oluşturuldu.\n" +
                    "• Sahne Play Mode öncesinde kaydedilmedi.\n" +
                    "• Awake/OnEnable sırasında hierarchy veya component sırası değişti.",
                    "Tamam");
                return;
            }

            string json;
            List<SavedObjectReference> objectReferences;

            try
            {
                json = EditorJsonUtility.ToJson(component, false);
                objectReferences = CaptureObjectReferences(component);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Play Mode Component Saver",
                    "Component verisi serialize edilemedi. Console'daki hataya bak.",
                    "Tamam");
                return;
            }

            PlayModeComponentSaveStore store = PlayModeComponentSaveStore.instance;
            string sessionId = string.IsNullOrEmpty(store.ActiveSessionId)
                ? Guid.NewGuid().ToString("N")
                : store.ActiveSessionId;

            store.ActiveSessionId = sessionId;

            string targetKey = !string.IsNullOrEmpty(targetEntry.globalObjectId)
                ? targetEntry.globalObjectId
                : targetEntry.catalogKey;

            SavedComponentSnapshot snapshot = store.Snapshots.FirstOrDefault(item =>
                item.sessionId == sessionId &&
                item.targetKey == targetKey &&
                item.status == SavedComponentStatus.Pending);

            if (snapshot == null)
            {
                snapshot = new SavedComponentSnapshot
                {
                    id = Guid.NewGuid().ToString("N")
                };
                store.Snapshots.Add(snapshot);
            }

            snapshot.sessionId = sessionId;
            snapshot.targetKey = targetKey;
            snapshot.targetGlobalObjectId = targetEntry.globalObjectId;
            snapshot.scenePath = targetEntry.scenePath;
            snapshot.sceneName = targetEntry.sceneName;
            snapshot.hierarchyIndexPath = targetEntry.hierarchyIndexPath;
            snapshot.hierarchyNamePath = targetEntry.hierarchyNamePath;
            snapshot.componentTypeName = targetEntry.componentTypeName;
            snapshot.componentDisplayName = targetEntry.componentDisplayName;
            snapshot.componentTypeIndex = targetEntry.componentTypeIndex;
            snapshot.json = json;
            snapshot.objectReferences = objectReferences;
            snapshot.savedUtc = DateTime.UtcNow.ToString("O");
            snapshot.appliedUtc = string.Empty;
            snapshot.status = SavedComponentStatus.Pending;
            snapshot.message = string.Empty;

            TrimHistory(store.Snapshots);
            store.Persist();

            Debug.Log(
                "[Play Mode Component Saver] Kaydedildi: " +
                snapshot.hierarchyNamePath + " / " + snapshot.componentDisplayName,
                component);
        }

        private static List<SavedObjectReference> CaptureObjectReferences(Component component)
        {
            List<SavedObjectReference> result = new List<SavedObjectReference>();
            SerializedObject serializedObject = new SerializedObject(component);
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;

            while (iterator.Next(enterChildren))
            {
                enterChildren = false;

                if (iterator.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                SavedObjectReference savedReference = new SavedObjectReference
                {
                    propertyPath = iterator.propertyPath
                };

                Object referencedObject = iterator.objectReferenceValue;
                if (referencedObject == null)
                {
                    savedReference.isNull = true;
                    result.Add(savedReference);
                    continue;
                }

                PrePlayObjectEntry sceneEntry;
                if (TryGetPrePlayEntry(referencedObject, out sceneEntry))
                {
                    CopyEntryToReference(sceneEntry, savedReference);
                    result.Add(savedReference);
                    continue;
                }

                if (EditorUtility.IsPersistent(referencedObject))
                {
                    savedReference.globalObjectId = GetGlobalObjectIdString(referencedObject);

                    string assetGuid;
                    long assetLocalId;
                    if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                            referencedObject,
                            out assetGuid,
                            out assetLocalId))
                    {
                        savedReference.assetGuid = assetGuid;
                        savedReference.assetLocalId = assetLocalId;
                    }

                    if (string.IsNullOrEmpty(savedReference.globalObjectId) &&
                        string.IsNullOrEmpty(savedReference.assetGuid))
                    {
                        savedReference.preserveExistingValue = true;
                    }

                    result.Add(savedReference);
                    continue;
                }

                // Runtime-only veya eşleştirilemeyen bir referans Edit Mode'a taşınamaz.
                // JSON uygulandıktan sonra Edit Mode'daki mevcut değer korunur.
                savedReference.preserveExistingValue = true;
                result.Add(savedReference);
            }

            return result;
        }

        private static void CopyEntryToReference(
            PrePlayObjectEntry entry,
            SavedObjectReference destination)
        {
            destination.globalObjectId = entry.globalObjectId;
            destination.scenePath = entry.scenePath;
            destination.hierarchyIndexPath = entry.hierarchyIndexPath;
            destination.objectKind = entry.objectKind;
            destination.componentTypeName = entry.componentTypeName;
            destination.componentTypeIndex = entry.componentTypeIndex;
        }

        private static void ApplyCurrentSessionAfterPlayMode()
        {
            EditorApplication.delayCall -= ApplyCurrentSessionAfterPlayMode;

            PlayModeComponentSaveStore store = PlayModeComponentSaveStore.instance;
            string sessionId = store.ActiveSessionId;

            ApplySnapshots(
                snapshot =>
                    snapshot.status == SavedComponentStatus.Pending &&
                    snapshot.sessionId == sessionId,
                true);
        }

        private static void ApplySnapshots(
            Func<SavedComponentSnapshot, bool> predicate,
            bool finishPlaySession)
        {
            if (EditorApplication.isPlaying)
                return;

            PlayModeComponentSaveStore store = PlayModeComponentSaveStore.instance;
            List<SavedComponentSnapshot> snapshots = store.Snapshots
                .Where(predicate)
                .OrderBy(snapshot => snapshot.savedUtc)
                .ToList();

            if (snapshots.Count == 0)
            {
                if (finishPlaySession)
                    FinishPlaySession(store);
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Apply Play Mode Component Changes");

            int appliedCount = 0;
            int failedCount = 0;

            foreach (SavedComponentSnapshot snapshot in snapshots)
            {
                string message;
                if (TryApplySnapshot(snapshot, out message))
                {
                    snapshot.status = SavedComponentStatus.Applied;
                    snapshot.appliedUtc = DateTime.UtcNow.ToString("O");
                    snapshot.message = message;
                    appliedCount++;
                }
                else
                {
                    snapshot.status = SavedComponentStatus.Failed;
                    snapshot.message = message;
                    failedCount++;
                }
            }

            Undo.CollapseUndoOperations(undoGroup);

            if (finishPlaySession)
                FinishPlaySession(store);
            else
                store.Persist();

            Debug.Log(
                "[Play Mode Component Saver] Uygulama tamamlandı. Başarılı: " +
                appliedCount + ", başarısız: " + failedCount + ".");
        }

        private static bool TryApplySnapshot(
            SavedComponentSnapshot snapshot,
            out string message)
        {
            message = string.Empty;

            Component target = ResolveObject(
                snapshot.targetGlobalObjectId,
                snapshot.scenePath,
                snapshot.hierarchyIndexPath,
                "Component",
                snapshot.componentTypeName,
                snapshot.componentTypeIndex) as Component;

            if (target == null)
            {
                message =
                    "Hedef bulunamadı. Sahnenin açık olduğundan, objenin silinmediğinden ve component sırasının değişmediğinden emin ol.";
                return false;
            }

            string backupJson;
            Dictionary<string, Object> previousObjectReferences =
                CaptureCurrentObjectReferenceValues(target, snapshot.objectReferences);

            try
            {
                backupJson = EditorJsonUtility.ToJson(target, false);
            }
            catch (Exception exception)
            {
                message = "Mevcut component yedeği alınamadı: " + exception.Message;
                return false;
            }

            try
            {
                Undo.RecordObject(target, "Apply Play Mode Changes: " + target.GetType().Name);
                EditorJsonUtility.FromJsonOverwrite(snapshot.json, target);

                List<string> warnings = RestoreObjectReferences(
                    target,
                    snapshot.objectReferences,
                    previousObjectReferences);

                EditorUtility.SetDirty(target);

                if (PrefabUtility.IsPartOfPrefabInstance(target))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(target);

                Scene scene = target.gameObject.scene;
                if (scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.MarkSceneDirty(scene);

                if (warnings.Count > 0)
                    message = string.Join("\n", warnings.ToArray());

                return true;
            }
            catch (Exception exception)
            {
                try
                {
                    EditorJsonUtility.FromJsonOverwrite(backupJson, target);
                    RestoreObjectReferences(target, snapshot.objectReferences, previousObjectReferences, true);
                }
                catch
                {
                    // Asıl hatayı koru.
                }

                message = "Uygulama hatası: " + exception.Message;
                Debug.LogException(exception);
                return false;
            }
        }

        private static Dictionary<string, Object> CaptureCurrentObjectReferenceValues(
            Component target,
            List<SavedObjectReference> references)
        {
            Dictionary<string, Object> values = new Dictionary<string, Object>();
            if (references == null || references.Count == 0)
                return values;

            SerializedObject serializedObject = new SerializedObject(target);

            foreach (SavedObjectReference savedReference in references)
            {
                SerializedProperty property = serializedObject.FindProperty(savedReference.propertyPath);
                if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                values[savedReference.propertyPath] = property.objectReferenceValue;
            }

            return values;
        }

        private static List<string> RestoreObjectReferences(
            Component target,
            List<SavedObjectReference> references,
            Dictionary<string, Object> previousValues,
            bool forcePreviousValues = false)
        {
            List<string> warnings = new List<string>();
            if (references == null || references.Count == 0)
                return warnings;

            SerializedObject serializedObject = new SerializedObject(target);
            serializedObject.Update();

            foreach (SavedObjectReference savedReference in references)
            {
                SerializedProperty property = serializedObject.FindProperty(savedReference.propertyPath);
                if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                Object previousValue = null;
                previousValues.TryGetValue(savedReference.propertyPath, out previousValue);

                if (forcePreviousValues || savedReference.preserveExistingValue)
                {
                    property.objectReferenceValue = previousValue;
                    continue;
                }

                if (savedReference.isNull)
                {
                    property.objectReferenceValue = null;
                    continue;
                }

                Object resolved = ResolveSavedReference(savedReference);

                if (resolved == null)
                {
                    property.objectReferenceValue = previousValue;
                    warnings.Add(
                        "Referans çözülemedi; mevcut Edit Mode değeri korundu: " +
                        savedReference.propertyPath);
                }
                else
                {
                    property.objectReferenceValue = resolved;
                }
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            return warnings;
        }


        private static Object ResolveSavedReference(SavedObjectReference savedReference)
        {
            Object resolved = ResolveObject(
                savedReference.globalObjectId,
                savedReference.scenePath,
                savedReference.hierarchyIndexPath,
                savedReference.objectKind,
                savedReference.componentTypeName,
                savedReference.componentTypeIndex);

            if (resolved != null || string.IsNullOrEmpty(savedReference.assetGuid))
                return resolved;

            string assetPath = AssetDatabase.GUIDToAssetPath(savedReference.assetGuid);
            if (string.IsNullOrEmpty(assetPath))
                return null;

            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            foreach (Object asset in assets)
            {
                string guid;
                long localId;
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out guid, out localId))
                    continue;

                if (guid == savedReference.assetGuid && localId == savedReference.assetLocalId)
                    return asset;
            }

            return null;
        }

        private static Object ResolveObject(
            string globalObjectId,
            string scenePath,
            string hierarchyIndexPath,
            string objectKind,
            string componentTypeName,
            int componentTypeIndex)
        {
            Object resolvedByGlobalId = ResolveGlobalObjectId(globalObjectId);
            if (resolvedByGlobalId != null)
                return resolvedByGlobalId;

            if (string.IsNullOrEmpty(scenePath) || string.IsNullOrEmpty(hierarchyIndexPath))
                return null;

            Scene scene = SceneManager.GetSceneByPath(scenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                return null;

            GameObject gameObject = ResolveGameObjectByIndexPath(scene, hierarchyIndexPath);
            if (gameObject == null)
                return null;

            if (objectKind == "GameObject")
                return gameObject;

            Type componentType = Type.GetType(componentTypeName);
            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
                return null;

            Component[] components = gameObject.GetComponents(componentType);
            if (componentTypeIndex < 0 || componentTypeIndex >= components.Length)
                return null;

            return components[componentTypeIndex];
        }

        private static Object ResolveGlobalObjectId(string globalObjectId)
        {
            if (string.IsNullOrEmpty(globalObjectId) || globalObjectId == NullGlobalObjectId)
                return null;

            GlobalObjectId parsed;
            if (!GlobalObjectId.TryParse(globalObjectId, out parsed))
                return null;

            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed);
        }

        private static GameObject ResolveGameObjectByIndexPath(
            Scene scene,
            string hierarchyIndexPath)
        {
            string[] parts = hierarchyIndexPath.Split('/');
            if (parts.Length == 0)
                return null;

            int rootIndex;
            if (!int.TryParse(parts[0], out rootIndex))
                return null;

            GameObject[] roots = scene.GetRootGameObjects();
            if (rootIndex < 0 || rootIndex >= roots.Length)
                return null;

            Transform current = roots[rootIndex].transform;

            for (int i = 1; i < parts.Length; i++)
            {
                int childIndex;
                if (!int.TryParse(parts[i], out childIndex))
                    return null;

                if (childIndex < 0 || childIndex >= current.childCount)
                    return null;

                current = current.GetChild(childIndex);
            }

            return current.gameObject;
        }

        private static bool TryGetPrePlayEntry(Object runtimeObject, out PrePlayObjectEntry entry)
        {
            entry = null;
            if (runtimeObject == null)
                return false;

            string catalogKey;
            if (!RuntimeCatalogKeyByInstanceId.TryGetValue(runtimeObject.GetInstanceID(), out catalogKey))
            {
                // Script recompilation happened while playing: rebuild the cache from the persisted map.
                RebuildRuntimeBindingCache(PlayModeComponentSaveStore.instance.RuntimeBindings);
                RuntimeCatalogKeyByInstanceId.TryGetValue(runtimeObject.GetInstanceID(), out catalogKey);
            }

            if (string.IsNullOrEmpty(catalogKey))
                return false;

            if (!CatalogByKey.TryGetValue(catalogKey, out entry))
            {
                RebuildCatalogCache(PlayModeComponentSaveStore.instance.PrePlayCatalog);
                CatalogByKey.TryGetValue(catalogKey, out entry);
            }

            return entry != null;
        }

        private static string GetGlobalObjectIdString(Object target)
        {
            if (target == null)
                return string.Empty;

            try
            {
                string value = GlobalObjectId.GetGlobalObjectIdSlow(target).ToString();
                return value == NullGlobalObjectId ? string.Empty : value;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetHierarchyIndexPath(Transform transform)
        {
            List<int> indices = new List<int>();
            Transform current = transform;

            while (current != null)
            {
                indices.Add(current.GetSiblingIndex());
                current = current.parent;
            }

            indices.Reverse();
            return string.Join("/", indices.Select(index => index.ToString()).ToArray());
        }

        private static string GetHierarchyNamePath(Transform transform)
        {
            List<string> names = new List<string>();
            Transform current = transform;

            while (current != null)
            {
                names.Add(current.name);
                current = current.parent;
            }

            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        private static string BuildGameObjectCatalogKey(string scenePath, string hierarchyIndexPath)
        {
            return scenePath + "|" + hierarchyIndexPath + "|GameObject";
        }

        private static string BuildComponentCatalogKey(
            string scenePath,
            string hierarchyIndexPath,
            string componentTypeName,
            int componentTypeIndex)
        {
            return scenePath + "|" + hierarchyIndexPath + "|Component|" +
                   componentTypeName + "|" + componentTypeIndex;
        }

        private static string BuildCatalogKey(SavedComponentSnapshot snapshot)
        {
            return BuildComponentCatalogKey(
                snapshot.scenePath,
                snapshot.hierarchyIndexPath,
                snapshot.componentTypeName,
                snapshot.componentTypeIndex);
        }

        private static void FinishPlaySession(PlayModeComponentSaveStore store)
        {
            store.ActiveSessionId = string.Empty;
            store.PrePlayCatalog.Clear();
            store.RuntimeBindings.Clear();
            TrimHistory(store.Snapshots);
            store.Persist();
            RebuildCachesFromStore();
        }

        private static void TrimHistory(List<SavedComponentSnapshot> snapshots)
        {
            List<SavedComponentSnapshot> removable = snapshots
                .Where(snapshot => snapshot.status != SavedComponentStatus.Pending)
                .OrderByDescending(snapshot => snapshot.savedUtc)
                .Skip(MaximumHistoryCount)
                .ToList();

            foreach (SavedComponentSnapshot snapshot in removable)
                snapshots.Remove(snapshot);
        }

        private static void RebuildCachesFromStore()
        {
            PlayModeComponentSaveStore store = PlayModeComponentSaveStore.instance;
            RebuildCatalogCache(store.PrePlayCatalog);
            RebuildRuntimeBindingCache(store.RuntimeBindings);
        }

        private static void RebuildCatalogCache(IEnumerable<PrePlayObjectEntry> entries)
        {
            CatalogByKey.Clear();

            foreach (PrePlayObjectEntry entry in entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.catalogKey))
                    continue;

                CatalogByKey[entry.catalogKey] = entry;
            }
        }

        private static void RebuildRuntimeBindingCache(IEnumerable<RuntimeObjectBinding> bindings)
        {
            RuntimeCatalogKeyByInstanceId.Clear();

            foreach (RuntimeObjectBinding binding in bindings)
            {
                if (binding == null || string.IsNullOrEmpty(binding.catalogKey))
                    continue;

                RuntimeCatalogKeyByInstanceId[binding.instanceId] = binding.catalogKey;
            }
        }
    }
}
#endif
