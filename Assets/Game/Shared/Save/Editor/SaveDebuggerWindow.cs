using System;
using System.IO;
using Game.Shared.Bootstrap;
using Game.Shared.Store;
using Gameplay.Levels;
using UnityEditor;
using UnityEngine;

namespace Game.Shared.Save
{
    public sealed class SaveDebuggerWindow : EditorWindow
    {
        private JsonFileSaveStorage storage;
        private GameSaveData workingData;

        private Vector2 mainScrollPosition;
        private Vector2 rawJsonScrollPosition;
        private bool showLevelProgress;
        private bool showRawJson = true;

        private string rawJson = string.Empty;
        private string statusMessage = string.Empty;
        private MessageType statusType = MessageType.Info;
        private bool writeBlockedForUnsupportedVersion;
        private int quickGoldAmount = 100;

        [MenuItem("Tools/Save Debugger")]
        private static void OpenWindow()
        {
            GetWindow<SaveDebuggerWindow>("Save Debugger");
        }

        private void OnEnable()
        {
            storage = new JsonFileSaveStorage();
            ReloadWorkingCopy(false);
        }

        private void OnGUI()
        {
            EnsureWorkingData();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("SAVE DEBUGGER", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Save Path", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(
                storage.SavePath,
                EditorStyles.textField,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));

            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.HelpBox(statusMessage, statusType);
            }

            mainScrollPosition = EditorGUILayout.BeginScrollView(mainScrollPosition);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("Schema Version", workingData.version);
            }

            DrawQuickActionsSection();
            DrawLevelSection();
            DrawLivesSection();
            DrawGoldSection();
            DrawBoostersSection();
            DrawStoreSection();
            DrawSettingsSection();
            DrawRawJsonSection();

            EditorGUILayout.EndScrollView();

            DrawActions();
        }

        private void DrawQuickActionsSection()
        {
            DrawSectionHeader("QUICK ACTIONS");

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Quick Actions are available in Play Mode.",
                    MessageType.Info);
            }

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                EditorGUILayout.LabelField("LEVEL RESULT", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Win", "Immediately trigger the normal win result and presentation.")))
                    {
                        TriggerLevelResult(true);
                    }

                    if (GUILayout.Button(new GUIContent("Lose", "Immediately finalize a loss, skipping the Clean Up offer.")))
                    {
                        TriggerLevelResult(false);
                    }
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("LIVES", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button("Spend 1 Life"))
                {
                    SpendOneLife();
                }

                if (GUILayout.Button("Add 1 Life"))
                {
                    AddOneLife();
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("GOLD", EditorStyles.boldLabel);
                quickGoldAmount = EditorGUILayout.IntField("Amount", quickGoldAmount);

                using (new EditorGUI.DisabledScope(quickGoldAmount <= 0))
                {
                    if (GUILayout.Button("Add Gold"))
                    {
                        AddQuickGold();
                    }
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("STORE", EditorStyles.boldLabel);

                if (GUILayout.Button("Grant Starter Pack Rewards"))
                {
                    GrantStoreRewards(StoreProductIds.StarterPack, false);
                }

                if (GUILayout.Button("Grant Fail Offer Rewards"))
                {
                    GrantStoreRewards(StoreProductIds.FailOffer, true);
                }

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Set No Ads"))
                {
                    SetNoAds(true);
                }

                if (GUILayout.Button("Clear No Ads"))
                {
                    SetNoAds(false);
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Set Starter Pack Purchased"))
                {
                    SetStarterPackPurchased(true);
                }

                if (GUILayout.Button("Clear Starter Pack Purchased"))
                {
                    SetStarterPackPurchased(false);
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Start Infinite Lives 60m"))
                {
                    StartInfiniteLives();
                }

                if (GUILayout.Button("Clear Infinite Lives"))
                {
                    ClearInfiniteLives();
                }

                EditorGUILayout.EndHorizontal();
                if (GUILayout.Button("Print Store State"))
                {
                    PrintStoreState();
                }
            }
        }

        private void TriggerLevelResult(bool win)
        {
            LevelResultFlowController resultFlow = Application.isPlaying
                ? UnityEngine.Object.FindFirstObjectByType<LevelResultFlowController>()
                : null;
            if (resultFlow == null || !resultFlow.TryTriggerImmediateResultForEditor(win))
            {
                SetQuickActionUnavailable("Start an active gameplay level in Play Mode before triggering a result.");
                return;
            }

            RefreshAfterQuickAction(
                win ? "Win triggered through the normal level result flow."
                    : "Lose finalized through the normal level result flow (Clean Up skipped).",
                MessageType.Info);
        }

        private void GrantStoreRewards(string productId, bool includeGameplayActions)
        {
            StoreManager storeManager = StoreManager.Instance;
            if (storeManager == null)
            {
                SetQuickActionUnavailable("StoreManager is not initialized in Play Mode.");
                return;
            }

            bool success = storeManager.DebugGrantProductRewards(
                productId,
                includeGameplayActions);
            RefreshAfterQuickAction(
                success
                    ? $"Granted '{productId}' through StoreRewardProcessor."
                    : $"Could not fully grant '{productId}'. See Console for details.",
                success ? MessageType.Info : MessageType.Warning);
        }

        private void SetNoAds(bool enabled)
        {
            MutateRuntimeStore(
                manager => manager.SetHasNoAds(enabled),
                enabled ? "No Ads enabled." : "No Ads cleared.");
        }

        private void SetStarterPackPurchased(bool purchased)
        {
            MutateRuntimeStore(
                manager => manager.SetStarterPackPurchased(purchased),
                purchased
                    ? "Starter Pack marked purchased."
                    : "Starter Pack purchased flag cleared.");
        }

        private void StartInfiniteLives()
        {
            if (!TryGetRuntimeLivesService(out Lives.LivesService livesService))
            {
                return;
            }

            livesService.AddInfiniteLives(TimeSpan.FromMinutes(60));
            RefreshAfterQuickAction("Started 60 minutes of Infinite Lives.", MessageType.Info);
        }

        private void ClearInfiniteLives()
        {
            MutateRuntimeStore(
                manager => manager.SetInfiniteLivesEndUtc(0),
                "Infinite Lives cleared.");
        }

        private void PrintStoreState()
        {
            StoreManager storeManager = StoreManager.Instance;
            if (storeManager == null)
            {
                SetQuickActionUnavailable("StoreManager is not initialized in Play Mode.");
                return;
            }

            storeManager.DebugPrintState();
            RefreshAfterQuickAction("Store state printed to the Console.", MessageType.Info);
        }

        private void MutateRuntimeStore(Action<SaveManager> mutation, string successMessage)
        {
            SaveManager runtimeManager = GetRuntimeManager();
            if (runtimeManager == null || !runtimeManager.IsInitialized)
            {
                SetQuickActionUnavailable("SaveManager is not initialized in Play Mode.");
                return;
            }

            mutation(runtimeManager);
            bool saved = runtimeManager.TrySave();
            RefreshAfterQuickAction(
                saved ? successMessage : "Store state changed in memory but save failed.",
                saved ? MessageType.Info : MessageType.Error);
        }

        private void SpendOneLife()
        {
            if (!TryGetRuntimeLivesService(out Lives.LivesService livesService))
            {
                return;
            }

            bool spent = livesService.TrySpendLife();
            RefreshAfterQuickAction(
                spent
                    ? "Spent 1 life through LivesService."
                    : "No life was spent because the runtime balance is empty.",
                spent ? MessageType.Info : MessageType.Warning);
        }

        private void AddOneLife()
        {
            if (!TryGetRuntimeLivesService(out Lives.LivesService livesService))
            {
                return;
            }

            int previousLives = livesService.CurrentLives;
            livesService.AddLives(1);
            bool changed = livesService.CurrentLives != previousLives;
            RefreshAfterQuickAction(
                changed
                    ? "Added 1 life through LivesService."
                    : "Lives are already full.",
                changed ? MessageType.Info : MessageType.Warning);
        }

        private void AddQuickGold()
        {
            SaveManager runtimeManager = GetRuntimeManager();
            if (runtimeManager == null || !runtimeManager.IsInitialized)
            {
                SetQuickActionUnavailable("SaveManager is not initialized in Play Mode.");
                return;
            }

            if (runtimeManager.IsWriteBlocked)
            {
                SetQuickActionUnavailable(
                    "Gold could not be added because runtime save writes are blocked.");
                return;
            }

            runtimeManager.AddGold(quickGoldAmount);
            runtimeManager.Save();
            RefreshAfterQuickAction(
                $"Added {quickGoldAmount} gold through SaveManager.",
                MessageType.Info);
        }

        private bool TryGetRuntimeLivesService(out Lives.LivesService livesService)
        {
            livesService = Application.isPlaying
                ? SharedSystemsBootstrap.Instance?.LivesService
                : null;

            if (livesService != null && livesService.IsInitialized)
            {
                return true;
            }

            SetQuickActionUnavailable("LivesService is not initialized in Play Mode.");
            return false;
        }

        private void RefreshAfterQuickAction(string message, MessageType messageType)
        {
            ReloadWorkingCopy(false);
            statusMessage = message;
            statusType = messageType;
            Repaint();
        }

        private void SetQuickActionUnavailable(string message)
        {
            statusMessage = message;
            statusType = MessageType.Error;
            Repaint();
        }

        private void DrawLevelSection()
        {
            DrawSectionHeader("LEVEL");

            workingData.level.currentLevel =
                EditorGUILayout.IntField("Current Level", workingData.level.currentLevel);
            workingData.level.pendingNextLevel =
                EditorGUILayout.IntField("Pending", workingData.level.pendingNextLevel);
            workingData.level.hasPendingNextLevel =
                EditorGUILayout.Toggle("Has Pending", workingData.level.hasPendingNextLevel);
            workingData.level.lastLoopContentLevelNumber =
                EditorGUILayout.IntField(
                    "Last Loop Content Level",
                    workingData.level.lastLoopContentLevelNumber);

            showLevelProgress = EditorGUILayout.Foldout(
                showLevelProgress,
                $"Level Progress ({workingData.level.levels.Count})",
                true);

            if (!showLevelProgress)
            {
                return;
            }

            EditorGUI.indentLevel++;
            for (int i = 0; i < workingData.level.levels.Count; i++)
            {
                LevelProgressSaveData progress = workingData.level.levels[i];
                if (progress == null)
                {
                    progress = new LevelProgressSaveData();
                    workingData.level.levels[i] = progress;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                progress.levelNumber = EditorGUILayout.IntField("Level", progress.levelNumber);
                if (GUILayout.Button("Remove", GUILayout.Width(64f)))
                {
                    workingData.level.levels.RemoveAt(i);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }

                EditorGUILayout.EndHorizontal();
                progress.completed = EditorGUILayout.Toggle("Completed", progress.completed);
                EditorGUILayout.LabelField("Custom Values", progress.values.Count.ToString());
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("Add Level Progress"))
            {
                workingData.level.levels.Add(new LevelProgressSaveData
                {
                    levelNumber = GetNextLevelProgressNumber()
                });
            }

            EditorGUI.indentLevel--;
        }

        private void DrawLivesSection()
        {
            DrawSectionHeader("LIVES");

            workingData.lives.current = EditorGUILayout.IntField("Current", workingData.lives.current);
            workingData.lives.max = EditorGUILayout.IntField("Max", workingData.lives.max);
            workingData.lives.nextRefillUtc =
                EditorGUILayout.LongField("Next Refill UTC", workingData.lives.nextRefillUtc);
        }

        private void DrawGoldSection()
        {
            DrawSectionHeader("GOLD");
            workingData.gold.amount = EditorGUILayout.IntField("Amount", workingData.gold.amount);
        }

        private void DrawBoostersSection()
        {
            DrawSectionHeader("BOOSTERS");

            for (int i = 0; i < workingData.boosters.Count; i++)
            {
                BoosterSaveData booster = workingData.boosters[i];
                if (booster == null)
                {
                    booster = new BoosterSaveData();
                    workingData.boosters[i] = booster;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                booster.id = EditorGUILayout.TextField("ID", booster.id ?? string.Empty);
                if (GUILayout.Button("Remove", GUILayout.Width(64f)))
                {
                    workingData.boosters.RemoveAt(i);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }

                EditorGUILayout.EndHorizontal();
                booster.amount = EditorGUILayout.IntField("Amount", booster.amount);
                booster.unlocked = EditorGUILayout.Toggle("Unlocked", booster.unlocked);
                EditorGUILayout.LabelField("Custom Values", booster.values.Count.ToString());
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("Add Booster"))
            {
                workingData.boosters.Add(new BoosterSaveData());
            }
        }

        private void DrawStoreSection()
        {
            DrawSectionHeader("STORE");

            workingData.store.hasNoAds =
                EditorGUILayout.Toggle("Has No Ads", workingData.store.hasNoAds);
            workingData.store.starterPackPurchased = EditorGUILayout.Toggle(
                "Starter Pack Purchased",
                workingData.store.starterPackPurchased);
            workingData.store.infiniteLivesEndUtc = EditorGUILayout.LongField(
                "Infinite Lives End UTC",
                workingData.store.infiniteLivesEndUtc);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField(
                    "Processed IAP Transactions",
                    workingData.store.processedIapTransactions.Count);
            }
        }

        private void DrawSettingsSection()
        {
            DrawSectionHeader("SETTINGS");

            for (int i = 0; i < workingData.settings.Count; i++)
            {
                SettingSaveData setting = workingData.settings[i];
                if (setting == null)
                {
                    setting = new SettingSaveData();
                    workingData.settings[i] = setting;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                setting.key = EditorGUILayout.TextField("Key", setting.key ?? string.Empty);
                if (GUILayout.Button("Remove", GUILayout.Width(64f)))
                {
                    workingData.settings.RemoveAt(i);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }

                EditorGUILayout.EndHorizontal();

                SaveValueType previousType = setting.type;
                setting.type = (SaveValueType)EditorGUILayout.EnumPopup("Type", setting.type);
                if (setting.type != previousType)
                {
                    ClearValues(setting);
                }

                DrawActiveSettingValue(setting);
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("Add Setting"))
            {
                workingData.settings.Add(new SettingSaveData());
            }
        }

        private void DrawRawJsonSection()
        {
            DrawSectionHeader(string.Empty);
            showRawJson = EditorGUILayout.Foldout(showRawJson, "Raw JSON (read-only)", true);
            if (!showRawJson)
            {
                return;
            }

            rawJsonScrollPosition = EditorGUILayout.BeginScrollView(
                rawJsonScrollPosition,
                GUILayout.MinHeight(180f),
                GUILayout.MaxHeight(320f));
            EditorGUILayout.SelectableLabel(
                rawJson ?? string.Empty,
                EditorStyles.textArea,
                GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void DrawActions()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Reload"))
            {
                ReloadWorkingCopy(true);
            }

            if (GUILayout.Button("Save"))
            {
                SaveWorkingCopy();
            }

            if (GUILayout.Button("Reset Save"))
            {
                ResetSaveWithConfirmation();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Open Save Folder"))
            {
                OpenSaveFolder();
            }

            using (new EditorGUI.DisabledScope(!File.Exists(storage.SavePath)))
            {
                if (GUILayout.Button("Open JSON"))
                {
                    EditorUtility.OpenWithDefaultApp(storage.SavePath);
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }

        private void ReloadWorkingCopy(bool synchronizeRuntime)
        {
            storage = new JsonFileSaveStorage();
            writeBlockedForUnsupportedVersion = false;

            SaveManager runtimeManager = GetRuntimeManager();
            if (runtimeManager != null && runtimeManager.IsInitialized)
            {
                if (synchronizeRuntime)
                {
                    runtimeManager.Reload();
                }

                if (TryClone(runtimeManager.Data, out workingData, out string cloneError))
                {
                    writeBlockedForUnsupportedVersion = runtimeManager.IsWriteBlocked;
                    LoadRawJsonFromDisk();
                    statusMessage = writeBlockedForUnsupportedVersion
                        ? "The active SaveManager rejected an unsupported or inaccessible save. Save is blocked; use Reset Save only if replacement is intentional."
                        : synchronizeRuntime
                            ? "Reloaded save.json and synchronized the active SaveManager."
                            : "Loaded a working copy from the active SaveManager.";
                    statusType = writeBlockedForUnsupportedVersion
                        ? MessageType.Error
                        : MessageType.Info;
                    Repaint();
                    return;
                }

                statusMessage = $"Could not clone active runtime save data: {cloneError}";
                statusType = MessageType.Error;
            }

            LoadWorkingCopyFromDisk();
            Repaint();
        }

        private void LoadWorkingCopyFromDisk()
        {
            rawJson = string.Empty;
            writeBlockedForUnsupportedVersion = false;

            try
            {
                bool primaryExists = storage.Exists();
                if (primaryExists)
                {
                    rawJson = storage.Load();
                    if (TryParse(
                            rawJson,
                            out workingData,
                            out bool unsupportedVersion,
                            out string error))
                    {
                        statusMessage = "Loaded save.json from disk.";
                        statusType = MessageType.Info;
                        return;
                    }

                    writeBlockedForUnsupportedVersion |= unsupportedVersion;
                    statusMessage = $"Primary save could not be read: {error}";
                    statusType = MessageType.Error;

                    if (unsupportedVersion)
                    {
                        statusMessage +=
                            " The primary file was left unchanged and backup recovery was skipped.";
                        workingData = GameSaveDataFactory.CreateDefault();
                        return;
                    }
                }

                if (primaryExists && storage.BackupExists())
                {
                    string backupJson = storage.LoadBackup();
                    if (TryParse(
                            backupJson,
                            out workingData,
                            out bool unsupportedVersion,
                            out string backupError))
                    {
                        writeBlockedForUnsupportedVersion = false;
                        rawJson = backupJson;
                        statusMessage = "Primary save is unavailable; showing the readable backup copy.";
                        statusType = MessageType.Warning;
                        return;
                    }

                    writeBlockedForUnsupportedVersion |= unsupportedVersion;
                    statusMessage = $"Primary and backup saves could not be read: {backupError}";
                    statusType = MessageType.Error;
                }
                else if (!primaryExists)
                {
                    statusMessage = "No save.json exists yet. Showing unsaved default data.";
                    statusType = MessageType.Info;
                }
            }
            catch (Exception exception)
            {
                statusMessage = $"Save load failed: {exception.Message}";
                statusType = MessageType.Error;
            }

            workingData = GameSaveDataFactory.CreateDefault();
        }

        private void SaveWorkingCopy()
        {
            EnsureWorkingData();

            if (writeBlockedForUnsupportedVersion)
            {
                statusMessage =
                    "Save is blocked to protect an unsupported schema. Use Reset Save if overwriting it is intentional.";
                statusType = MessageType.Error;
                return;
            }

            if (!SaveJsonSerializer.IsSchemaVersionSupported(
                    workingData.version,
                    out string schemaError))
            {
                writeBlockedForUnsupportedVersion = true;
                statusMessage =
                    $"Save was not written: {schemaError} Use Reset Save only if replacing the " +
                    "existing save is intentional.";
                statusType = MessageType.Error;
                return;
            }

            GameSaveDataNormalizer.Normalize(workingData);

            try
            {
                string json = SaveJsonSerializer.ToJson(workingData, true);
                storage.Save(json);

                SaveManager runtimeManager = GetRuntimeManager();
                if (runtimeManager != null)
                {
                    if (!runtimeManager.IsInitialized)
                    {
                        runtimeManager.Initialize();
                    }
                    else
                    {
                        runtimeManager.Reload();
                    }
                }

                ReloadWorkingCopy(false);
                statusMessage = runtimeManager != null
                    ? "Saved save.json and synchronized the active SaveManager."
                    : "Saved save.json.";
                statusType = MessageType.Info;
            }
            catch (Exception exception)
            {
                statusMessage = $"Save write failed: {exception.Message}";
                statusType = MessageType.Error;
                Debug.LogError(statusMessage);
            }
        }

        private void ResetSaveWithConfirmation()
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "Reset Save",
                "save.json and its backup will be replaced with default save data. Continue?",
                "Reset",
                "Cancel");

            if (!confirmed)
            {
                return;
            }

            try
            {
                storage.Delete();
                GameSaveData defaults = GameSaveDataFactory.CreateDefault();
                storage.Save(SaveJsonSerializer.ToJson(defaults, true));

                SaveManager runtimeManager = GetRuntimeManager();
                if (runtimeManager != null)
                {
                    if (runtimeManager.IsInitialized)
                    {
                        runtimeManager.Reload();
                    }
                    else
                    {
                        runtimeManager.Initialize();
                    }
                }

                ReloadWorkingCopy(false);
                statusMessage = runtimeManager != null
                    ? "Reset save.json and synchronized the active SaveManager."
                    : "Reset save.json to defaults.";
                statusType = MessageType.Info;
            }
            catch (Exception exception)
            {
                statusMessage = $"Save reset failed: {exception.Message}";
                statusType = MessageType.Error;
                Debug.LogError(statusMessage);
            }
        }

        private void LoadRawJsonFromDisk()
        {
            try
            {
                rawJson = storage.Exists() ? storage.Load() : string.Empty;
            }
            catch (Exception exception)
            {
                rawJson = string.Empty;
                statusMessage = $"Raw JSON could not be read: {exception.Message}";
                statusType = MessageType.Warning;
            }
        }

        private void OpenSaveFolder()
        {
            try
            {
                Directory.CreateDirectory(storage.DirectoryPath);
                EditorUtility.RevealInFinder(storage.DirectoryPath);
            }
            catch (Exception exception)
            {
                statusMessage = $"Save folder could not be opened: {exception.Message}";
                statusType = MessageType.Error;
            }
        }

        private void EnsureWorkingData()
        {
            workingData ??= GameSaveDataFactory.CreateDefault();
        }

        private int GetNextLevelProgressNumber()
        {
            int nextLevelNumber = 1;
            for (int i = 0; i < workingData.level.levels.Count; i++)
            {
                LevelProgressSaveData progress = workingData.level.levels[i];
                if (progress != null && progress.levelNumber >= nextLevelNumber)
                {
                    nextLevelNumber = progress.levelNumber + 1;
                }
            }

            return nextLevelNumber;
        }

        private static bool TryClone(
            GameSaveData source,
            out GameSaveData clone,
            out string error)
        {
            clone = null;
            error = string.Empty;

            if (source == null)
            {
                error = "Runtime data is null.";
                return false;
            }

            string json = SaveJsonSerializer.ToJson(source, true);
            return TryParse(json, out clone, out _, out error);
        }

        private static bool TryParse(
            string json,
            out GameSaveData parsedData,
            out bool unsupportedVersion,
            out string error)
        {
            return SaveJsonSerializer.TryFromJson(
                json,
                out parsedData,
                out _,
                out unsupportedVersion,
                out error);
        }

        private static SaveManager GetRuntimeManager()
        {
            return Application.isPlaying ? SaveManager.Instance : null;
        }

        private static void DrawActiveSettingValue(SettingSaveData setting)
        {
            switch (setting.type)
            {
                case SaveValueType.Bool:
                    setting.boolValue = EditorGUILayout.Toggle("Value", setting.boolValue);
                    break;

                case SaveValueType.Int:
                    setting.intValue = EditorGUILayout.IntField("Value", setting.intValue);
                    break;

                case SaveValueType.Float:
                    setting.floatValue = EditorGUILayout.FloatField("Value", setting.floatValue);
                    break;

                case SaveValueType.String:
                    setting.stringValue = EditorGUILayout.TextField(
                        "Value",
                        setting.stringValue ?? string.Empty);
                    break;
            }
        }

        private static void ClearValues(SaveValueData value)
        {
            value.boolValue = false;
            value.intValue = 0;
            value.floatValue = 0f;
            value.stringValue = string.Empty;
        }

        private static void DrawSectionHeader(string title)
        {
            EditorGUILayout.Space();
            if (!string.IsNullOrEmpty(title))
            {
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            }
        }
    }
}
