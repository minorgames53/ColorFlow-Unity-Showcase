using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Gameplay.Levels;
using UnityEditor;
using UnityEngine;

namespace Game.Shared.Editor.LevelManagement
{
    public sealed class SharedLevelManagementWindow : EditorWindow
    {
        private const string WindowTitle = "Level Management";
        private const string MenuPath = "Tools/Level Management";
        private const string PrefPrefix = "SharedLevelManagement.";

        private readonly LevelImportSettings settings = new LevelImportSettings();
        private readonly LevelImportValidationResult validation = new LevelImportValidationResult();
        private readonly LevelImportReport report = new LevelImportReport();
        private readonly List<LevelImportSource> sources = new List<LevelImportSource>();
        private readonly List<SharedLevelImportData> parsedLevels = new List<SharedLevelImportData>();
        private readonly List<SharedLevelSummary> levelSummaries = new List<SharedLevelSummary>();

        private ISharedLevelImportAdapter adapter;
        private Vector2 scrollPosition;
        private Vector2 levelsScrollPosition;
        private Vector2 validationScrollPosition;
        private Vector2 reportScrollPosition;
        private string searchText = string.Empty;
        private LevelListFilter listFilter;
        private string statusMessage;

        private enum LevelListFilter
        {
            All,
            Valid,
            Invalid,
            IncludedInLoop,
            ExcludedFromLoop
        }

        [MenuItem(MenuPath)]
        public static void Open()
        {
            GetWindow<SharedLevelManagementWindow>(WindowTitle);
        }

        private void OnEnable()
        {
            adapter = SharedLevelImportAdapterRegistry.CreateFirst();
            LoadPrefs();
            RefreshLevels();
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            SavePrefs();
        }

        private void OnGUI()
        {
            if (adapter == null)
            {
                adapter = SharedLevelImportAdapterRegistry.CreateFirst();
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            EditorGUILayout.LabelField(WindowTitle, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Adapter", adapter == null ? "No adapter registered" : adapter.AdapterName);

            DrawImportSection();
            DrawLevelsSection();
            DrawValidationSection();
            DrawReportSection();

            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.HelpBox(statusMessage, MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawImportSection()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Import", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            settings.ImportMode = (LevelImportMode)EditorGUILayout.EnumPopup("Import Mode", settings.ImportMode);
            DrawDestinationFolder();
            settings.CatalogAsset = EditorGUILayout.ObjectField("Level Catalog", settings.CatalogAsset, typeof(UnityEngine.Object), false);
            settings.CreateMissing = EditorGUILayout.Toggle("Create Missing", settings.CreateMissing);
            settings.OverwriteExisting = EditorGUILayout.Toggle("Overwrite Existing", settings.OverwriteExisting);
            settings.IncludeSubfolders = EditorGUILayout.Toggle("Include Subfolders", settings.IncludeSubfolders);
            settings.DryRun = EditorGUILayout.Toggle("Dry Run", settings.DryRun);
            if (EditorGUI.EndChangeCheck())
            {
                settings.Normalize();
                SavePrefs();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Source"))
            {
                AddSourceForCurrentMode();
            }

            if (GUILayout.Button("Clear Sources"))
            {
                sources.Clear();
                parsedLevels.Clear();
                validation.Clear();
                report.Clear();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField($"Sources: {sources.Count}");
            for (int i = 0; i < sources.Count; i++)
            {
                EditorGUILayout.LabelField(sources[i].SourceName);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Validate / Preview"))
            {
                ValidateSources();
            }

            using (new EditorGUI.DisabledScope(settings.DryRun || adapter == null || sources.Count == 0))
            {
                if (GUILayout.Button("Import"))
                {
                    ImportValidatedLevels();
                }
            }

            EditorGUILayout.EndHorizontal();

            if (settings.DryRun)
            {
                EditorGUILayout.HelpBox("Dry Run is enabled. Validation and matching run, but Import is disabled until Dry Run is off.", MessageType.Info);
            }
            else if (sources.Count == 0)
            {
                EditorGUILayout.HelpBox("Add at least one JSON source before importing.", MessageType.Info);
            }
        }

        private void DrawDestinationFolder()
        {
            EditorGUILayout.BeginHorizontal();
            settings.DestinationFolder = EditorGUILayout.TextField("Destination Folder", settings.DestinationFolder);
            if (GUILayout.Button("Pick", GUILayout.Width(64)))
            {
                string absolute = EditorUtility.OpenFolderPanel("Destination Folder", Application.dataPath, string.Empty);
                if (!string.IsNullOrEmpty(absolute))
                {
                    string projectRoot = Directory.GetParent(Application.dataPath)?.FullName.Replace('\\', '/');
                    string normalized = absolute.Replace('\\', '/');
                    if (!string.IsNullOrEmpty(projectRoot) && normalized.StartsWith(projectRoot, StringComparison.Ordinal))
                    {
                        settings.DestinationFolder = normalized.Substring(projectRoot.Length + 1);
                    }
                    else
                    {
                        statusMessage = "Destination must be inside this Unity project.";
                    }
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawLevelsSection()
        {
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Levels", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            searchText = EditorGUILayout.TextField("Search", searchText);
            listFilter = (LevelListFilter)EditorGUILayout.EnumPopup(listFilter, GUILayout.Width(150));
            if (GUILayout.Button("Refresh", GUILayout.Width(80)))
            {
                RefreshLevels();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Order", GUILayout.Width(45));
            GUILayout.Label("Number", GUILayout.Width(60));
            GUILayout.Label("Level ID", GUILayout.Width(90));
            GUILayout.Label("Asset", GUILayout.MinWidth(130));
            GUILayout.Label("Difficulty", GUILayout.Width(80));
            GUILayout.Label("Loop", GUILayout.Width(45));
            GUILayout.Label("Status", GUILayout.Width(80));
            GUILayout.Label("Actions", GUILayout.Width(165));
            EditorGUILayout.EndHorizontal();

            levelsScrollPosition = EditorGUILayout.BeginScrollView(levelsScrollPosition, GUILayout.MinHeight(180), GUILayout.MaxHeight(320));
            int order = 0;
            for (int i = 0; i < levelSummaries.Count; i++)
            {
                SharedLevelSummary level = levelSummaries[i];
                if (!PassesFilter(level))
                {
                    continue;
                }

                order++;
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(order.ToString(), GUILayout.Width(45));
                GUILayout.Label(level.LevelNumber.ToString(), GUILayout.Width(60));
                GUILayout.Label(string.IsNullOrEmpty(level.LevelId) ? "-" : level.LevelId, GUILayout.Width(90));
                GUILayout.Label(level.AssetName, GUILayout.MinWidth(130));
                GUILayout.Label(level.Difficulty, GUILayout.Width(80));
                GUILayout.Label(level.IncludeInLoop ? "Yes" : "No", GUILayout.Width(45));
                GUILayout.Label(level.IsValid ? "Valid" : "Invalid", GUILayout.Width(80));
                if (GUILayout.Button("Select", GUILayout.Width(55))) adapter?.SelectLevelAsset(level);
                if (GUILayout.Button("Delete", GUILayout.Width(55))) DeleteSingle(level);
                using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || !level.IsValid || !(level.Asset is LevelDefinition)))
                {
                    if (GUILayout.Button("Play", GUILayout.Width(45)))
                    {
                        PlayLevel(level);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        private void PlayLevel(SharedLevelSummary level)
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("Level Management can only play a level while Unity is in Play Mode.");
                return;
            }

            if (!level.IsValid || !(level.Asset is LevelDefinition levelDefinition))
            {
                Debug.LogWarning("Level Management cannot play an invalid or missing LevelDefinition asset.");
                return;
            }

            if (!LevelSessionController.TryPlayLevelForEditor(levelDefinition))
            {
                statusMessage = $"Could not load '{level.AssetName}'. See Console for details.";
                Repaint();
                return;
            }

            statusMessage = $"Loaded '{level.AssetName}' (Level {levelDefinition.LevelNumber}) in the current Game scene.";
            Repaint();
        }

        private void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            Repaint();
        }

        private void DrawValidationSection()
        {
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);
            validationScrollPosition = EditorGUILayout.BeginScrollView(validationScrollPosition, GUILayout.MinHeight(110), GUILayout.MaxHeight(220));
            if (validation.Messages.Count == 0)
            {
                EditorGUILayout.LabelField("No validation messages.");
            }

            for (int i = 0; i < validation.Messages.Count; i++)
            {
                LevelImportValidationMessage message = validation.Messages[i];
                EditorGUILayout.LabelField($"[{message.Severity}] {message.SourceName} / {message.LevelKey}: {message.Message}", EditorStyles.wordWrappedLabel);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawReportSection()
        {
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Import Report", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Created {report.CreatedCount}, Updated {report.UpdatedCount}, Unchanged {report.UnchangedCount}, Skipped {report.SkippedCount}, Warnings {report.WarningCount}, Errors {report.ErrorCount}");
            reportScrollPosition = EditorGUILayout.BeginScrollView(reportScrollPosition, GUILayout.MinHeight(140), GUILayout.MaxHeight(260));
            if (report.Entries.Count == 0)
            {
                EditorGUILayout.LabelField("No import report yet.");
            }

            for (int i = 0; i < report.Entries.Count; i++)
            {
                LevelImportReportEntry entry = report.Entries[i];
                EditorGUILayout.LabelField($"[{entry.Group}] {entry.SourceName} / {entry.LevelKey}: {entry.Result} - {entry.Message}", EditorStyles.wordWrappedLabel);
            }

            EditorGUILayout.EndScrollView();
        }

        private void AddSourceForCurrentMode()
        {
            switch (settings.ImportMode)
            {
                case LevelImportMode.JsonFiles:
                    AddJsonFiles();
                    break;
                case LevelImportMode.Folder:
                    AddFolderJsonFiles();
                    break;
                case LevelImportMode.ClipboardJson:
                    AddClipboardJson();
                    break;
            }
        }

        private void AddJsonFiles()
        {
            int selectedCount = AddSelectedTextAssets();
            if (selectedCount > 0)
            {
                statusMessage = $"Added {selectedCount} selected JSON asset(s).";
                return;
            }

            AddJsonFilesFromPicker();
        }

        private void AddJsonFilesFromPicker()
        {
            string[] paths = OpenJsonFilesPanel("Level JSON Files", Application.dataPath);
            if (paths.Length > 0)
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    AddFileSource(paths[i]);
                }

                statusMessage = $"Added {paths.Length} JSON file(s).";
            }
            else
            {
                statusMessage = "Select one or more JSON files, or select JSON TextAssets in Project view before clicking Add Source.";
            }
        }

        private int AddSelectedTextAssets()
        {
            UnityEngine.Object[] selectedObjects = Selection.objects;
            int added = 0;
            for (int i = 0; i < selectedObjects.Length; i++)
            {
                string assetPath = AssetDatabase.GetAssetPath(selectedObjects[i]);
                if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string fullPath = Path.GetFullPath(assetPath);
                AddFileSource(fullPath);
                added++;
            }

            return added;
        }

        private void AddFolderJsonFiles()
        {
            string folder = EditorUtility.OpenFolderPanel("JSON Folder", Application.dataPath, string.Empty);
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            SearchOption option = settings.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            string[] files = Directory.GetFiles(folder, "*.json", option);
            Array.Sort(files, StringComparer.Ordinal);
            for (int i = 0; i < files.Length; i++)
            {
                AddFileSource(files[i]);
            }

            statusMessage = $"Added {files.Length} JSON file(s) from folder.";
        }

        private void AddClipboardJson()
        {
            string json = EditorGUIUtility.systemCopyBuffer;
            if (string.IsNullOrWhiteSpace(json))
            {
                statusMessage = "Clipboard does not contain JSON text.";
                return;
            }

            sources.Add(new LevelImportSource
            {
                SourceName = "Clipboard",
                Json = json
            });
        }

        private void AddFileSource(string fullPath)
        {
            try
            {
                sources.Add(new LevelImportSource
                {
                    SourceName = Path.GetFileName(fullPath),
                    Json = File.ReadAllText(fullPath)
                });
            }
            catch (Exception exception)
            {
                validation.Add(LevelImportMessageSeverity.Error, "-", fullPath, $"Could not read JSON file: {exception.Message}");
            }
        }

        private void ValidateSources()
        {
            report.Clear();
            validation.Clear();
            parsedLevels.Clear();
            settings.Normalize();

            if (adapter == null)
            {
                validation.Add(LevelImportMessageSeverity.Error, "-", "-", "No level import adapter is registered.");
                return;
            }

            LevelImportRequest request = new LevelImportRequest
            {
                Settings = settings,
                Sources = new List<LevelImportSource>(sources)
            };

            IReadOnlyList<SharedLevelImportData> parsed = adapter.ParseInput(request, validation);
            if (parsed != null)
            {
                parsedLevels.AddRange(parsed);
            }

            ValidateDuplicateImportKeys();
            adapter.ValidateLevels(parsedLevels, settings, validation);
            BuildPreviewReport();
            statusMessage = $"Validated {parsedLevels.Count} level(s). Errors: {validation.HasErrors}.";
        }

        private void ValidateDuplicateImportKeys()
        {
            Dictionary<string, SharedLevelImportData> byId = new Dictionary<string, SharedLevelImportData>(StringComparer.Ordinal);
            Dictionary<int, SharedLevelImportData> byNumber = new Dictionary<int, SharedLevelImportData>();
            for (int i = 0; i < parsedLevels.Count; i++)
            {
                SharedLevelImportData level = parsedLevels[i];
                if (string.IsNullOrWhiteSpace(level.LevelId) && level.LevelNumber < 1)
                {
                    validation.Add(LevelImportMessageSeverity.Error, level.StableKey, level.SourceName, "Level must have a valid levelId or levelNumber.");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(level.LevelId))
                {
                    if (byId.TryGetValue(level.LevelId, out SharedLevelImportData duplicateId))
                    {
                        validation.Add(LevelImportMessageSeverity.Error, level.LevelId, level.SourceName, $"Duplicate levelId also appears in {duplicateId.SourceName}.");
                    }
                    else
                    {
                        byId.Add(level.LevelId, level);
                    }
                }

                if (level.LevelNumber > 0)
                {
                    if (byNumber.TryGetValue(level.LevelNumber, out SharedLevelImportData duplicateNumber))
                    {
                        validation.Add(LevelImportMessageSeverity.Error, level.LevelNumber.ToString(), level.SourceName, $"Duplicate levelNumber also appears in {duplicateNumber.SourceName}.");
                    }
                    else
                    {
                        byNumber.Add(level.LevelNumber, level);
                    }
                }
            }
        }

        private void BuildPreviewReport()
        {
            report.Clear();
            report.AddValidation(validation);
            IReadOnlyList<SharedLevelSummary> existingLevels = adapter.GetExistingLevelSummaries(settings);
            Dictionary<string, SharedLevelSummary> existingByKey = BuildExistingMap(existingLevels);

            for (int i = 0; i < parsedLevels.Count; i++)
            {
                SharedLevelImportData level = parsedLevels[i];
                bool hasLevelError = HasErrorForImportLevel(level);
                if (hasLevelError)
                {
                    report.Add(LevelImportReportGroup.Skipped, level.StableKey, level.SourceName, "Skipped", "Validation errors block import.");
                    continue;
                }

                bool exists = TryFindExistingLevel(level, existingByKey, out SharedLevelSummary _);
                if (exists && !settings.OverwriteExisting)
                {
                    report.Add(LevelImportReportGroup.Skipped, level.StableKey, level.SourceName, "Skipped", "Existing level found and Overwrite Existing is disabled.");
                }
                else if (!exists && !settings.CreateMissing)
                {
                    report.Add(LevelImportReportGroup.Skipped, level.StableKey, level.SourceName, "Skipped", "No existing level found and Create Missing is disabled.");
                }
                else
                {
                    report.Add(exists ? LevelImportReportGroup.Updated : LevelImportReportGroup.Created, level.StableKey, level.SourceName, settings.DryRun ? "Dry Run" : "Ready", exists ? "Will update existing asset." : "Will create new asset.");
                }
            }
        }

        private void ImportValidatedLevels()
        {
            RestoreProjectWorkingDirectory();
            ValidateSources();
            if (validation.HasErrors)
            {
                statusMessage = "Import cancelled because validation has errors.";
                return;
            }

            IReadOnlyList<SharedLevelSummary> existingLevels = adapter.GetExistingLevelSummaries(settings);
            Dictionary<string, SharedLevelSummary> existingByKey = BuildExistingMap(existingLevels);
            report.Clear();
            report.AddValidation(validation);

            bool startedAssetEditing = false;
            try
            {
                AssetDatabase.StartAssetEditing();
                startedAssetEditing = true;

                for (int i = 0; i < parsedLevels.Count; i++)
                {
                    SharedLevelImportData level = parsedLevels[i];
                    TryFindExistingLevel(level, existingByKey, out SharedLevelSummary existing);
                    if (existing != null && !settings.OverwriteExisting)
                    {
                        report.Add(LevelImportReportGroup.Skipped, level.StableKey, level.SourceName, "Skipped", "Existing level found and Overwrite Existing is disabled.");
                        continue;
                    }

                    if (existing == null && !settings.CreateMissing)
                    {
                        report.Add(LevelImportReportGroup.Skipped, level.StableKey, level.SourceName, "Skipped", "No existing level found and Create Missing is disabled.");
                        continue;
                    }

                    SharedLevelImportResult result = adapter.CreateOrUpdateLevel(level, existing, settings);
                    LevelImportReportGroup group = ToReportGroup(result.Kind);
                    report.Add(group, level.StableKey, level.SourceName, result.Kind.ToString(), result.Message);
                }
            }
            catch (Exception exception)
            {
                report.Add(LevelImportReportGroup.Error, "-", "-", "Critical Error", exception.Message);
                statusMessage = $"Import stopped: {exception.Message}";
                return;
            }
            finally
            {
                if (startedAssetEditing)
                {
                    AssetDatabase.StopAssetEditing();
                }
            }

            adapter.RebuildCatalog(settings, report);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            RefreshLevels();
            Debug.Log($"Level Management import finished. Created {report.CreatedCount}, Updated {report.UpdatedCount}, Unchanged {report.UnchangedCount}, Skipped {report.SkippedCount}, Warnings {report.WarningCount}, Errors {report.ErrorCount}.");
            statusMessage = "Import finished.";
        }

        private static LevelImportReportGroup ToReportGroup(SharedLevelImportResultKind kind)
        {
            switch (kind)
            {
                case SharedLevelImportResultKind.Created:
                    return LevelImportReportGroup.Created;
                case SharedLevelImportResultKind.Updated:
                    return LevelImportReportGroup.Updated;
                case SharedLevelImportResultKind.Unchanged:
                    return LevelImportReportGroup.Unchanged;
                default:
                    return LevelImportReportGroup.Skipped;
            }
        }

        private Dictionary<string, SharedLevelSummary> BuildExistingMap(IReadOnlyList<SharedLevelSummary> existingLevels)
        {
            Dictionary<string, SharedLevelSummary> map = new Dictionary<string, SharedLevelSummary>(StringComparer.Ordinal);
            if (existingLevels == null)
            {
                return map;
            }

            for (int i = 0; i < existingLevels.Count; i++)
            {
                SharedLevelSummary level = existingLevels[i];
                if (level == null || string.IsNullOrEmpty(level.StableKey) || level.StableKey == "-")
                {
                    continue;
                }

                if (!map.ContainsKey(level.StableKey))
                {
                    map.Add(level.StableKey, level);
                }
            }

            return map;
        }

        private static bool TryFindExistingLevel(SharedLevelImportData importLevel, Dictionary<string, SharedLevelSummary> existingByKey, out SharedLevelSummary existing)
        {
            existing = null;
            if (importLevel == null || existingByKey == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(importLevel.LevelId) && existingByKey.TryGetValue(importLevel.LevelId, out existing))
            {
                return true;
            }

            return importLevel.LevelNumber > 0 && existingByKey.TryGetValue(importLevel.LevelNumber.ToString(), out existing);
        }

        private bool HasErrorForLevel(string levelKey, string sourceName)
        {
            for (int i = 0; i < validation.Messages.Count; i++)
            {
                LevelImportValidationMessage message = validation.Messages[i];
                if (message.Severity == LevelImportMessageSeverity.Error &&
                    message.LevelKey == levelKey &&
                    message.SourceName == sourceName)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasErrorForImportLevel(SharedLevelImportData level)
        {
            if (level == null)
            {
                return false;
            }

            for (int i = 0; i < validation.Messages.Count; i++)
            {
                LevelImportValidationMessage message = validation.Messages[i];
                if (message.Severity != LevelImportMessageSeverity.Error || message.SourceName != level.SourceName)
                {
                    continue;
                }

                if (message.LevelKey == level.StableKey ||
                    (!string.IsNullOrWhiteSpace(level.LevelId) && message.LevelKey == level.LevelId) ||
                    (level.LevelNumber > 0 && message.LevelKey == level.LevelNumber.ToString()))
                {
                    return true;
                }
            }

            return false;
        }

        private void RefreshLevels()
        {
            levelSummaries.Clear();
            if (adapter == null)
            {
                return;
            }

            IReadOnlyList<SharedLevelSummary> summaries = adapter.GetExistingLevelSummaries(settings);
            if (summaries != null)
            {
                levelSummaries.AddRange(summaries);
            }
        }

        private bool PassesFilter(SharedLevelSummary level)
        {
            if (level == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                string needle = searchText.Trim();
                bool match = level.LevelNumber.ToString().IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                             (!string.IsNullOrEmpty(level.LevelId) && level.LevelId.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!match)
                {
                    return false;
                }
            }

            switch (listFilter)
            {
                case LevelListFilter.Valid:
                    return level.IsValid;
                case LevelListFilter.Invalid:
                    return !level.IsValid;
                case LevelListFilter.IncludedInLoop:
                    return level.IncludeInLoop;
                case LevelListFilter.ExcludedFromLoop:
                    return !level.IncludeInLoop;
                default:
                    return true;
            }
        }

        private void ValidateSingleLevel(SharedLevelSummary level)
        {
            validation.Clear();
            if (level == null)
            {
                return;
            }

            validation.Add(level.IsValid ? LevelImportMessageSeverity.Info : LevelImportMessageSeverity.Error, level.StableKey, level.AssetPath, level.IsValid ? "Level is valid." : level.ValidationMessage);
        }

        private void ReimportSingle(SharedLevelSummary level)
        {
            if (level == null)
            {
                return;
            }

            ValidateSources();
            SharedLevelImportData importLevel = null;
            for (int i = 0; i < parsedLevels.Count; i++)
            {
                if (parsedLevels[i].StableKey == level.StableKey)
                {
                    importLevel = parsedLevels[i];
                    break;
                }
            }

            if (importLevel == null)
            {
                statusMessage = $"Current sources do not contain level {level.StableKey}.";
                return;
            }

            if (HasErrorForLevel(importLevel.StableKey, importLevel.SourceName))
            {
                statusMessage = $"Level {level.StableKey} has validation errors and was not reimported.";
                return;
            }

            if (settings.DryRun)
            {
                statusMessage = $"Dry Run is enabled. Level {level.StableKey} matched and would update {level.AssetName}.";
                return;
            }

            try
            {
                report.Clear();
                SharedLevelImportResult result = adapter.CreateOrUpdateLevel(importLevel, level, settings);
                report.Add(ToReportGroup(result.Kind), importLevel.StableKey, importLevel.SourceName, result.Kind.ToString(), result.Message);
                adapter.RebuildCatalog(settings, report);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                RefreshLevels();
                statusMessage = $"Reimported level {level.StableKey}.";
            }
            catch (Exception exception)
            {
                report.Add(LevelImportReportGroup.Error, level.StableKey, level.AssetPath, "Reimport Failed", exception.Message);
                statusMessage = $"Reimport failed: {exception.Message}";
            }
        }

        private void DeleteSingle(SharedLevelSummary level)
        {
            if (level == null || adapter == null)
            {
                return;
            }

            if (adapter.DeleteLevelAsset(level))
            {
                RefreshLevels();
                statusMessage = $"Deleted {level.AssetName}.";
            }
        }

        private static string[] OpenJsonFilesPanel(string title, string initialDirectory)
        {
            RestoreProjectWorkingDirectory();
#if UNITY_EDITOR_WIN
            try
            {
                return OpenWindowsMultiSelectJsonPanel(title, initialDirectory);
            }
            finally
            {
                RestoreProjectWorkingDirectory();
            }
#else
            try
            {
                string path = EditorUtility.OpenFilePanelWithFilters(title, initialDirectory, new[] { "JSON", "json" });
                return string.IsNullOrEmpty(path) ? Array.Empty<string>() : new[] { path };
            }
            finally
            {
                RestoreProjectWorkingDirectory();
            }
#endif
        }

        private static void RestoreProjectWorkingDirectory()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (!string.IsNullOrEmpty(projectRoot))
            {
                Directory.SetCurrentDirectory(projectRoot);
            }
        }

#if UNITY_EDITOR_WIN
        private const int OpenFileNameBufferCharCount = 65535;
        private const int OfnAllowMultiSelect = 0x00000200;
        private const int OfnExplorer = 0x00080000;
        private const int OfnFileMustExist = 0x00001000;
        private const int OfnHideReadOnly = 0x00000004;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string lpstrFilter;
            public IntPtr lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public IntPtr lpstrFile;
            public int nMaxFile;
            public IntPtr lpstrFileTitle;
            public int nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetOpenFileName(ref OpenFileName openFileName);

        private static string[] OpenWindowsMultiSelectJsonPanel(string title, string initialDirectory)
        {
            IntPtr buffer = Marshal.AllocHGlobal(OpenFileNameBufferCharCount * sizeof(char));
            try
            {
                for (int i = 0; i < OpenFileNameBufferCharCount; i++)
                {
                    Marshal.WriteInt16(buffer, i * sizeof(char), 0);
                }

                OpenFileName openFileName = new OpenFileName
                {
                    lStructSize = Marshal.SizeOf(typeof(OpenFileName)),
                    lpstrFilter = "JSON Files\0*.json\0All Files\0*.*\0\0",
                    lpstrFile = buffer,
                    nMaxFile = OpenFileNameBufferCharCount,
                    lpstrInitialDir = initialDirectory,
                    lpstrTitle = title,
                    Flags = OfnExplorer | OfnAllowMultiSelect | OfnFileMustExist | OfnHideReadOnly,
                    lpstrDefExt = "json"
                };

                if (!GetOpenFileName(ref openFileName))
                {
                    return Array.Empty<string>();
                }

                string raw = Marshal.PtrToStringUni(buffer, OpenFileNameBufferCharCount);
                if (string.IsNullOrEmpty(raw))
                {
                    return Array.Empty<string>();
                }

                string[] parts = raw.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    return Array.Empty<string>();
                }

                if (parts.Length == 1)
                {
                    return new[] { parts[0] };
                }

                string directory = parts[0];
                List<string> paths = new List<string>(parts.Length - 1);
                for (int i = 1; i < parts.Length; i++)
                {
                    paths.Add(Path.Combine(directory, parts[i]));
                }

                return paths.ToArray();
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
#endif

        private void LoadPrefs()
        {
            settings.DestinationFolder = EditorPrefs.GetString(PrefPrefix + "DestinationFolder", settings.DestinationFolder);
            settings.ImportMode = (LevelImportMode)EditorPrefs.GetInt(PrefPrefix + "ImportMode", (int)settings.ImportMode);
            settings.IncludeSubfolders = EditorPrefs.GetBool(PrefPrefix + "IncludeSubfolders", settings.IncludeSubfolders);
            settings.CreateMissing = EditorPrefs.GetBool(PrefPrefix + "CreateMissing", settings.CreateMissing);
            settings.OverwriteExisting = EditorPrefs.GetBool(PrefPrefix + "OverwriteExisting", settings.OverwriteExisting);
            string catalogPath = EditorPrefs.GetString(PrefPrefix + "CatalogPath", string.Empty);
            if (!string.IsNullOrEmpty(catalogPath))
            {
                settings.CatalogAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(catalogPath);
            }

            settings.Normalize();
        }

        private void SavePrefs()
        {
            settings.Normalize();
            EditorPrefs.SetString(PrefPrefix + "DestinationFolder", settings.DestinationFolder);
            EditorPrefs.SetInt(PrefPrefix + "ImportMode", (int)settings.ImportMode);
            EditorPrefs.SetBool(PrefPrefix + "IncludeSubfolders", settings.IncludeSubfolders);
            EditorPrefs.SetBool(PrefPrefix + "CreateMissing", settings.CreateMissing);
            EditorPrefs.SetBool(PrefPrefix + "OverwriteExisting", settings.OverwriteExisting);
            EditorPrefs.SetString(PrefPrefix + "CatalogPath", settings.CatalogAsset == null ? string.Empty : AssetDatabase.GetAssetPath(settings.CatalogAsset));
        }
    }
}
