#if UNITY_EDITOR_WIN

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using UnityEditor;
using UnityEngine;

public sealed class PlayerPrefsEditorWindow : EditorWindow
{
    private enum PrefType
    {
        Int,
        Float,
        String
    }

    [Serializable]
    private sealed class TypeOverride
    {
        public string key;
        public PrefType type;
    }

    [Serializable]
    private sealed class ToolState
    {
        public List<string> hiddenKeys = new();
        public List<TypeOverride> typeOverrides = new();
    }

    private sealed class PrefEntry
    {
        public string Key;
        public PrefType Type;
        public string Value;
    }

    // Registry daha sık kontrol edilir. Pending PlayerPrefs değişiklikleri ise
    // gameplay sırasında gereksiz disk yazımını azaltmak için daha seyrek flush edilir.
    private const double RegistryCheckIntervalSeconds = 0.25d;
    private const double PendingChangesFlushIntervalSeconds = 1.00d;

    private readonly List<PrefEntry> entries = new();

    private ToolState toolState = new();
    private Dictionary<string, string> registrySnapshot =
        new(StringComparer.Ordinal);

    private Vector2 scrollPosition;
    private bool showHidden;

    private string newKey = string.Empty;
    private string newValue = string.Empty;
    private PrefType newType = PrefType.Int;

    private double nextRegistryCheckTime;
    private double nextPendingChangesFlushTime;

    private string StateKey =>
        $"PlayerPrefsEditor.{Application.companyName}.{Application.productName}";

    [MenuItem("Tools/Game/PlayerPrefs Editor")]
    private static void Open()
    {
        GetWindow<PlayerPrefsEditorWindow>("PlayerPrefs");
    }

    private void OnEnable()
    {
        LoadToolState();
        Refresh();

        EditorApplication.update -= OnEditorUpdate;
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
    }

    private void OnEditorUpdate()
    {
        double currentTime = EditorApplication.timeSinceStartup;

        if (currentTime < nextRegistryCheckTime)
        {
            return;
        }

        nextRegistryCheckTime =
            currentTime + RegistryCheckIntervalSeconds;

        // PlayerPrefs API ile eklenmiş fakat henüz diske yazılmamış yeni key'ler,
        // registry taraması ile bulunamaz. Pencere açıkken seyrek şekilde flush
        // ederek ekleme, silme ve güncellemelerin otomatik görünmesini sağlıyoruz.
        if (currentTime >= nextPendingChangesFlushTime)
        {
            nextPendingChangesFlushTime =
                currentTime + PendingChangesFlushIntervalSeconds;

            PlayerPrefs.Save();
        }

        Dictionary<string, string> currentSnapshot =
            CreateRegistrySnapshot();

        if (RegistrySnapshotsEqual(
                registrySnapshot,
                currentSnapshot))
        {
            return;
        }

        Refresh(currentSnapshot);
    }

    private void OnGUI()
    {
        DrawToolbar();
        DrawEntries();
        DrawAddRow();
    }

    private void DrawToolbar()
    {
        int hiddenCount = entries.Count(entry => IsHidden(entry.Key));
        int visibleCount = entries.Count - hiddenCount;

        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        GUILayout.Label(
            $"PlayerPrefs: {visibleCount} visible / {entries.Count} total",
            EditorStyles.boldLabel);

        GUILayout.FlexibleSpace();

        GUILayout.Label(
            "Auto refresh",
            EditorStyles.miniLabel);

        showHidden = GUILayout.Toggle(
            showHidden,
            "Show Hidden",
            EditorStyles.toolbarButton,
            GUILayout.Width(90));

        if (GUILayout.Button(
                "Hide unity.*",
                EditorStyles.toolbarButton,
                GUILayout.Width(90)))
        {
            HideUnityInternalKeys();
        }

        if (GUILayout.Button(
                "Delete All",
                EditorStyles.toolbarButton,
                GUILayout.Width(75)))
        {
            DeleteAll();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawEntries()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        for (int i = 0; i < entries.Count; i++)
        {
            PrefEntry entry = entries[i];
            bool isHidden = IsHidden(entry.Key);

            if (isHidden && !showHidden)
            {
                continue;
            }

            bool listChanged = DrawEntryRow(entry, isHidden);

            if (listChanged)
            {
                break;
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private bool DrawEntryRow(PrefEntry entry, bool isHidden)
    {
        EditorGUILayout.BeginHorizontal();

        GUI.enabled = !isHidden;

        GUILayout.Label(
            entry.Key,
            GUILayout.MinWidth(220),
            GUILayout.MaxWidth(320));

        PrefType selectedType = (PrefType)EditorGUILayout.EnumPopup(
            entry.Type,
            GUILayout.Width(60));

        if (selectedType != entry.Type)
        {
            entry.Type = selectedType;
            SetTypeOverride(entry.Key, selectedType);
            entry.Value = ReadValue(entry.Key, selectedType);
        }

        EditorGUI.BeginChangeCheck();

        string editedValue = EditorGUILayout.DelayedTextField(
            entry.Value,
            GUILayout.MinWidth(100));

        if (EditorGUI.EndChangeCheck())
        {
            entry.Value = editedValue;
            SaveEntry(entry);
        }

        GUI.enabled = true;

        string hideButtonLabel = isHidden ? "↩" : "–";

        if (GUILayout.Button(hideButtonLabel, GUILayout.Width(24)))
        {
            SetHidden(entry.Key, !isHidden);
        }

        if (GUILayout.Button("X", GUILayout.Width(24)))
        {
            DeleteEntry(entry.Key);

            EditorGUILayout.EndHorizontal();

            // Liste Refresh() ile değişti. Çizimi bu frame için durdur.
            return true;
        }

        EditorGUILayout.EndHorizontal();
        return false;
    }

    private void DrawAddRow()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Add New Pref", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();

        newKey = EditorGUILayout.TextField(
            newKey,
            GUILayout.MinWidth(220),
            GUILayout.MaxWidth(320));

        newType = (PrefType)EditorGUILayout.EnumPopup(
            newType,
            GUILayout.Width(60));

        newValue = EditorGUILayout.TextField(
            newValue,
            GUILayout.MinWidth(100));

        if (GUILayout.Button("Add", GUILayout.Width(55)))
        {
            AddEntry();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void AddEntry()
    {
        string trimmedKey = newKey.Trim();

        if (string.IsNullOrEmpty(trimmedKey))
        {
            EditorUtility.DisplayDialog(
                "Invalid Key",
                "Key boş bırakılamaz.",
                "OK");

            return;
        }

        PrefEntry newEntry = new()
        {
            Key = trimmedKey,
            Type = newType,
            Value = newValue
        };

        if (!SaveEntry(newEntry))
        {
            return;
        }

        SetTypeOverride(trimmedKey, newType);

        newKey = string.Empty;
        newValue = string.Empty;

        Refresh();
    }

    private bool SaveEntry(PrefEntry entry)
    {
        try
        {
            switch (entry.Type)
            {
                case PrefType.Int:
                    PlayerPrefs.SetInt(
                        entry.Key,
                        int.Parse(entry.Value));
                    break;

                case PrefType.Float:
                    PlayerPrefs.SetFloat(
                        entry.Key,
                        float.Parse(
                            entry.Value,
                            CultureInfo.InvariantCulture));
                    break;

                case PrefType.String:
                    PlayerPrefs.SetString(
                        entry.Key,
                        entry.Value);
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            PlayerPrefs.Save();
            SetTypeOverride(entry.Key, entry.Type);

            return true;
        }
        catch (Exception exception)
        {
            EditorUtility.DisplayDialog(
                "Invalid Value",
                $"'{entry.Key}' kaydedilemedi.\n\n{exception.Message}",
                "OK");

            entry.Value = ReadValue(entry.Key, entry.Type);

            return false;
        }
    }

    private void DeleteEntry(string key)
    {
        PlayerPrefs.DeleteKey(key);
        PlayerPrefs.Save();

        toolState.hiddenKeys.Remove(key);
        toolState.typeOverrides.RemoveAll(item => item.key == key);

        SaveToolState();
        Refresh();
    }

    private void DeleteAll()
    {
        bool confirmed = EditorUtility.DisplayDialog(
            "Delete All PlayerPrefs",
            "Bütün PlayerPrefs kayıtları silinecek. Emin misin?",
            "Delete All",
            "Cancel");

        if (!confirmed)
        {
            return;
        }

        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();

        toolState = new ToolState();

        SaveToolState();
        Refresh();
    }

    private void HideUnityInternalKeys()
    {
        foreach (PrefEntry entry in entries)
        {
            if (entry.Key.StartsWith(
                    "unity.",
                    StringComparison.OrdinalIgnoreCase))
            {
                SetHidden(entry.Key, true, saveImmediately: false);
            }
        }

        SaveToolState();
        Repaint();
    }

    private void Refresh()
    {
        Refresh(CreateRegistrySnapshot());
    }

    private void Refresh(
        Dictionary<string, string> currentSnapshot)
    {
        entries.Clear();

        foreach (string rawKey in currentSnapshot.Keys)
        {
            string cleanKey = RemoveUnityHash(rawKey);

            if (entries.Any(entry => entry.Key == cleanKey))
            {
                continue;
            }

            PrefType type = GetStoredType(cleanKey);

            entries.Add(new PrefEntry
            {
                Key = cleanKey,
                Type = type,
                Value = ReadValue(cleanKey, type)
            });
        }

        entries.Sort((left, right) =>
            string.Compare(
                left.Key,
                right.Key,
                StringComparison.OrdinalIgnoreCase));

        registrySnapshot = currentSnapshot;

        Repaint();
    }

    private static Dictionary<string, string> CreateRegistrySnapshot()
    {
        Dictionary<string, string> snapshot =
            new(StringComparer.Ordinal);

        try
        {
            using RegistryKey registryKey =
                Registry.CurrentUser.OpenSubKey(GetRegistryPath());

            if (registryKey == null)
            {
                return snapshot;
            }

            foreach (string rawKey in registryKey.GetValueNames())
            {
                object value = registryKey.GetValue(
                    rawKey,
                    null,
                    RegistryValueOptions.DoNotExpandEnvironmentNames);

                RegistryValueKind kind =
                    registryKey.GetValueKind(rawKey);

                snapshot[rawKey] =
                    $"{kind}:{SerializeRegistryValue(value)}";
            }
        }
        catch
        {
            // Registry aynı anda değişirse bir sonraki otomatik kontrolde tekrar okunur.
        }

        return snapshot;
    }

    private static string SerializeRegistryValue(object value)
    {
        return value switch
        {
            null => "<null>",
            byte[] bytes => Convert.ToBase64String(bytes),
            string[] strings => string.Join("\0", strings),
            _ => Convert.ToString(
                     value,
                     CultureInfo.InvariantCulture)
                 ?? string.Empty
        };
    }

    private static bool RegistrySnapshotsEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (KeyValuePair<string, string> pair in left)
        {
            if (!right.TryGetValue(pair.Key, out string value))
            {
                return false;
            }

            if (!string.Equals(
                    pair.Value,
                    value,
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string ReadValue(string key, PrefType type)
    {
        if (!PlayerPrefs.HasKey(key))
        {
            return string.Empty;
        }

        return type switch
        {
            PrefType.Int =>
                PlayerPrefs.GetInt(key).ToString(),

            PrefType.Float =>
                PlayerPrefs.GetFloat(key).ToString(
                    CultureInfo.InvariantCulture),

            PrefType.String =>
                PlayerPrefs.GetString(key),

            _ => string.Empty
        };
    }

    private bool IsHidden(string key)
    {
        return toolState.hiddenKeys.Contains(key);
    }

    private void SetHidden(
        string key,
        bool hidden,
        bool saveImmediately = true)
    {
        if (hidden)
        {
            if (!toolState.hiddenKeys.Contains(key))
            {
                toolState.hiddenKeys.Add(key);
            }
        }
        else
        {
            toolState.hiddenKeys.Remove(key);
        }

        if (saveImmediately)
        {
            SaveToolState();
        }
    }

    private PrefType GetStoredType(string key)
    {
        TypeOverride storedType =
            toolState.typeOverrides.Find(item => item.key == key);

        return storedType?.type ?? PrefType.Int;
    }

    private void SetTypeOverride(string key, PrefType type)
    {
        TypeOverride existing =
            toolState.typeOverrides.Find(item => item.key == key);

        if (existing == null)
        {
            toolState.typeOverrides.Add(new TypeOverride
            {
                key = key,
                type = type
            });
        }
        else
        {
            existing.type = type;
        }

        SaveToolState();
    }

    private void LoadToolState()
    {
        string json = EditorPrefs.GetString(
            StateKey,
            string.Empty);

        if (string.IsNullOrWhiteSpace(json))
        {
            toolState = new ToolState();
            return;
        }

        toolState = JsonUtility.FromJson<ToolState>(json)
                    ?? new ToolState();
    }

    private void SaveToolState()
    {
        EditorPrefs.SetString(
            StateKey,
            JsonUtility.ToJson(toolState));
    }

    private static string GetRegistryPath()
    {
        return
            $@"Software\Unity\UnityEditor\{Application.companyName}\{Application.productName}";
    }

    private static string RemoveUnityHash(string key)
    {
        return Regex.Replace(
            key,
            @"_h\d+$",
            string.Empty);
    }
}

#endif
