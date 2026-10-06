using System;
using System.Collections.Generic;
using Game.Shared.Editor.LevelManagement;
using Gameplay.Levels;
using Gameplay.SourceBoxes;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Gameplay.Editor.LevelManagement
{
    [InitializeOnLoad]
    public sealed class GameLevelImportAdapter : ISharedLevelImportAdapter
    {
        private const string LevelNumberPropertyName = "levelNumber";
        private const string DifficultyPropertyName = "difficulty";
        private const string RowCountPropertyName = "rowCount";
        private const string ColumnCountPropertyName = "columnCount";
        private const string CellsPropertyName = "cells";
        private const string CratesPropertyName = "crates";
        private const string PanelsPropertyName = "panels";
        private const string GatesPropertyName = "gates";
        private const string RowPropertyName = "row";
        private const string ColPropertyName = "col";
        private const string NumberPropertyName = "number";
        private const string MultiplierPropertyName = "mult";
        private const string CellTypePropertyName = "cellType";
        private const string ColorIdPropertyName = "colorId";
        private const string MarbleCountPropertyName = "marbleCount";
        private const string IsMysterySourceBoxPropertyName = "isMysterySourceBox";
        private const string ConnectedBoxPairIdPropertyName = "connectedBoxPairId";
        private const string ArrowDirectionPropertyName = "arrowDirection";
        private const string HasKeyPropertyName = "hasKey";
        private const string SpawnDirectionPropertyName = "spawnDirection";
        private const string SpawnSequencePropertyName = "spawnSequence";
        private const string GiftBoxContentsPropertyName = "giftBoxContents";
        private const string TargetBoxLanesPropertyName = "targetBoxLanes";
        private const string TargetBoxesPropertyName = "boxes";
        private const string LegacyBoxColorsPropertyName = "boxColors";
        private const string IsMysteryPropertyName = "isMystery";
        private const string ConnectedTargetGroupIdPropertyName = "connectedTargetGroupId";
        private const string IsLockedPropertyName = "isLocked";
        private const string CatalogLevelsPropertyName = "levels";

        static GameLevelImportAdapter()
        {
            SharedLevelImportAdapterRegistry.Register(() => new GameLevelImportAdapter());
        }

        public string AdapterName => "Marble Sort Level Adapter";

        public IReadOnlyList<SharedLevelImportData> ParseInput(LevelImportRequest request, LevelImportValidationResult validation)
        {
            List<SharedLevelImportData> levels = new List<SharedLevelImportData>();
            if (request?.Sources == null)
            {
                validation.Add(LevelImportMessageSeverity.Error, "-", "-", "Import request has no sources.");
                return levels;
            }

            for (int i = 0; i < request.Sources.Count; i++)
            {
                LevelImportSource source = request.Sources[i];
                if (string.IsNullOrWhiteSpace(source.Json))
                {
                    validation.Add(LevelImportMessageSeverity.Error, "-", source.SourceName, "JSON is empty.");
                    continue;
                }

                ParseSource(source, validation, levels);
            }

            return levels;
        }

        public void ValidateLevels(IReadOnlyList<SharedLevelImportData> levels, LevelImportSettings settings, LevelImportValidationResult validation)
        {
            if (levels == null)
            {
                return;
            }

            for (int i = 0; i < levels.Count; i++)
            {
                SharedLevelImportData level = levels[i];
                GameLevelJsonDto dto = level.Payload as GameLevelJsonDto;
                if (dto == null)
                {
                    validation.Add(LevelImportMessageSeverity.Error, level.StableKey, level.SourceName, "Parsed payload is not a game level DTO.");
                    continue;
                }

                List<string> dtoErrors = new List<string>();
                ValidateJsonDto(dto, dtoErrors);
                for (int errorIndex = 0; errorIndex < dtoErrors.Count; errorIndex++)
                {
                    validation.Add(LevelImportMessageSeverity.Error, level.StableKey, level.SourceName, dtoErrors[errorIndex]);
                }

                if (dtoErrors.Count > 0)
                {
                    continue;
                }

                LevelDefinition tempLevel = ScriptableObject.CreateInstance<LevelDefinition>();
                tempLevel.name = $"ImportPreview_Level_{dto.levelNumber:000}";
                try
                {
                    ApplyDtoToLevel(tempLevel, dto);
                    List<string> runtimeErrors = new List<string>();
                    if (!LevelDefinitionValidator.Validate(tempLevel, runtimeErrors))
                    {
                        for (int errorIndex = 0; errorIndex < runtimeErrors.Count; errorIndex++)
                        {
                            validation.Add(LevelImportMessageSeverity.Error, level.StableKey, level.SourceName, runtimeErrors[errorIndex]);
                        }
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(tempLevel);
                }
            }
        }

        public IReadOnlyList<SharedLevelSummary> GetExistingLevelSummaries(LevelImportSettings settings)
        {
            List<SharedLevelSummary> summaries = new List<SharedLevelSummary>();
            string[] guids = AssetDatabase.FindAssets("t:LevelDefinition");
            HashSet<int> duplicateNumbers = FindDuplicateLevelNumbers(guids);

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                LevelDefinition level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(path);
                if (level == null)
                {
                    continue;
                }

                List<string> errors = new List<string>();
                bool isValid = LevelDefinitionValidator.Validate(level, errors) && !duplicateNumbers.Contains(level.LevelNumber);
                if (duplicateNumbers.Contains(level.LevelNumber))
                {
                    errors.Add($"Duplicate level number {level.LevelNumber} exists in project.");
                }

                summaries.Add(new SharedLevelSummary
                {
                    LevelId = string.Empty,
                    LevelNumber = level.LevelNumber,
                    AssetName = level.name,
                    AssetPath = path,
                    Difficulty = level.Difficulty == LevelDifficulty.VeryHard ? "Very Hard" : level.Difficulty.ToString(),
                    IncludeInLoop = level.IncludeInLoop,
                    IsValid = isValid,
                    ValidationMessage = errors.Count == 0 ? string.Empty : string.Join("; ", errors),
                    Asset = level
                });
            }

            summaries.Sort(CompareSummaries);
            return summaries;
        }

        public SharedLevelImportResult CreateOrUpdateLevel(SharedLevelImportData level, SharedLevelSummary existingLevel, LevelImportSettings settings)
        {
            GameLevelJsonDto dto = level.Payload as GameLevelJsonDto;
            if (dto == null)
            {
                return new SharedLevelImportResult { Kind = SharedLevelImportResultKind.Skipped, Message = "Payload is not a game level DTO." };
            }

            EnsureFolder(settings.DestinationFolder);
            LevelDefinition target = existingLevel?.Asset as LevelDefinition;
            string beforeJson = target == null ? null : EditorJsonUtility.ToJson(target);

            if (target == null)
            {
                target = ScriptableObject.CreateInstance<LevelDefinition>();
                target.name = $"Level_{dto.levelNumber:000}";
                ApplyDtoToLevel(target, dto);
                string path = AssetDatabase.GenerateUniqueAssetPath($"{settings.DestinationFolder}/Level_{dto.levelNumber:000}.asset");
                AssetDatabase.CreateAsset(target, path);
                EditorUtility.SetDirty(target);
                return new SharedLevelImportResult
                {
                    Kind = SharedLevelImportResultKind.Created,
                    Asset = target,
                    AssetPath = path,
                    Message = $"Created {path}."
                };
            }

            Undo.RecordObject(target, "Bulk Import Level");
            ApplyDtoToLevel(target, dto);
            string afterJson = EditorJsonUtility.ToJson(target);
            if (string.Equals(beforeJson, afterJson, StringComparison.Ordinal))
            {
                return new SharedLevelImportResult
                {
                    Kind = SharedLevelImportResultKind.Unchanged,
                    Asset = target,
                    AssetPath = existingLevel.AssetPath,
                    Message = "Existing asset already matches imported data."
                };
            }

            EditorUtility.SetDirty(target);
            return new SharedLevelImportResult
            {
                Kind = SharedLevelImportResultKind.Updated,
                Asset = target,
                AssetPath = existingLevel.AssetPath,
                Message = $"Updated {existingLevel.AssetPath}."
            };
        }

        public void RebuildCatalog(LevelImportSettings settings, LevelImportReport report)
        {
            LevelCatalog catalog = settings.CatalogAsset as LevelCatalog;
            if (catalog == null)
            {
                report.Add(LevelImportReportGroup.Warning, "-", "-", "Catalog", "No LevelCatalog assigned. Import assets were saved but catalog was not rebuilt.");
                return;
            }

            List<LevelDefinition> orderedLevels = LoadLevelsInFolder(settings.DestinationFolder);
            Undo.RecordObject(catalog, "Rebuild Level Catalog");
            SerializedObject serializedCatalog = new SerializedObject(catalog);
            serializedCatalog.Update();
            SerializedProperty levelsProperty = serializedCatalog.FindProperty(CatalogLevelsPropertyName);
            if (levelsProperty == null)
            {
                report.Add(LevelImportReportGroup.Error, "-", AssetDatabase.GetAssetPath(catalog), "Catalog", "LevelCatalog serialized levels property could not be found.");
                return;
            }

            levelsProperty.arraySize = orderedLevels.Count;
            for (int i = 0; i < orderedLevels.Count; i++)
            {
                levelsProperty.GetArrayElementAtIndex(i).objectReferenceValue = orderedLevels[i];
            }

            serializedCatalog.ApplyModifiedProperties();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            report.Add(LevelImportReportGroup.Updated, "-", AssetDatabase.GetAssetPath(catalog), "Catalog", $"Rebuilt catalog with {orderedLevels.Count} level(s).");
        }

        public void PingLevelAsset(SharedLevelSummary level)
        {
            if (level?.Asset != null)
            {
                EditorGUIUtility.PingObject(level.Asset);
            }
        }

        public void SelectLevelAsset(SharedLevelSummary level)
        {
            if (level?.Asset != null)
            {
                Selection.activeObject = level.Asset;
                EditorGUIUtility.PingObject(level.Asset);
            }
        }

        public bool DeleteLevelAsset(SharedLevelSummary level)
        {
            if (level == null || string.IsNullOrEmpty(level.AssetPath))
            {
                return false;
            }

            return AssetDatabase.DeleteAsset(level.AssetPath);
        }

        private void ParseSource(LevelImportSource source, LevelImportValidationResult validation, List<SharedLevelImportData> output)
        {
            try
            {
                JToken root = JToken.Parse(source.Json);
                if (!(root is JObject rootObject))
                {
                    validation.Add(LevelImportMessageSeverity.Error, "-", source.SourceName, "JSON root must be an object.");
                    return;
                }

                if (rootObject["levels"] is JArray levels)
                {
                    if (levels.Count == 0)
                    {
                        validation.Add(LevelImportMessageSeverity.Error, "-", source.SourceName, "Batch JSON contains no levels.");
                        return;
                    }

                    for (int levelIndex = 0; levelIndex < levels.Count; levelIndex++)
                    {
                        if (!(levels[levelIndex] is JObject levelObject))
                        {
                            validation.Add(LevelImportMessageSeverity.Error, "-", source.SourceName, $"levels[{levelIndex}] must be an object.");
                            continue;
                        }

                        AddParsedLevel(
                            GameLevelDesignerJsonAdapter.ParseAndNormalize(levelObject),
                            source.SourceName,
                            validation,
                            output);
                    }

                    return;
                }

                AddParsedLevel(
                    GameLevelDesignerJsonAdapter.ParseAndNormalize(rootObject),
                    source.SourceName,
                    validation,
                    output);
            }
            catch (Exception exception)
            {
                validation.Add(LevelImportMessageSeverity.Error, "-", source.SourceName, $"Level JSON parse failed: {exception.Message}");
            }
        }

        private static void AddParsedLevel(
            GameLevelJsonDto dto,
            string sourceName,
            LevelImportValidationResult validation,
            List<SharedLevelImportData> output)
        {
            AddDifficultyWarningIfNeeded(dto, sourceName, validation);

            output.Add(new SharedLevelImportData
            {
                LevelId = dto?.levelId,
                LevelNumber = dto?.levelNumber ?? 0,
                SourceName = sourceName,
                Payload = dto
            });
        }

        private static void AddDifficultyWarningIfNeeded(
            GameLevelJsonDto dto,
            string sourceName,
            LevelImportValidationResult validation)
        {
            if (dto == null)
            {
                return;
            }

            LevelDifficultyParser.Parse(dto.difficulty, out bool isUnknown);
            if (!isUnknown)
            {
                return;
            }

            string levelKey = !string.IsNullOrWhiteSpace(dto.levelId)
                ? dto.levelId
                : dto.levelNumber > 0
                    ? dto.levelNumber.ToString()
                    : "-";
            string levelLabel = dto.levelNumber > 0
                ? $"Level {dto.levelNumber}"
                : !string.IsNullOrWhiteSpace(dto.levelId)
                    ? $"Level '{dto.levelId}'"
                    : "unknown level";

            validation.Add(
                LevelImportMessageSeverity.Warning,
                levelKey,
                sourceName,
                $"Unknown difficulty '{dto.difficulty.Trim()}' for {levelLabel}. Falling back to Normal.");
        }

        private static void ValidateJsonDto(GameLevelJsonDto dto, List<string> errors)
        {
            if (dto == null)
            {
                errors.Add("JSON root is empty or does not match the level schema.");
                return;
            }

            if (dto.normalizationErrors != null)
            {
                errors.AddRange(dto.normalizationErrors);
            }

            if (!dto.isExternalAuthoringFormat &&
                (dto.version < GameLevelJsonConstants.MinimumSupportedVersion || dto.version > GameLevelJsonConstants.CurrentVersion))
            {
                errors.Add($"Unsupported JSON version {dto.version}. Expected {GameLevelJsonConstants.MinimumSupportedVersion}-{GameLevelJsonConstants.CurrentVersion}.");
            }

            if (string.IsNullOrWhiteSpace(dto.levelId) && dto.levelNumber < 1)
            {
                errors.Add("levelId or levelNumber must be valid.");
            }

            if (dto.rowCount < 1 || dto.columnCount < 1)
            {
                errors.Add("rowCount and columnCount must be at least 1.");
            }

            int expectedCellCount = Mathf.Max(0, dto.rowCount * dto.columnCount);
            if (dto.cells == null || dto.cells.Count != expectedCellCount)
            {
                errors.Add($"cells must contain {expectedCellCount} entries.");
            }
            else
            {
                for (int i = 0; i < dto.cells.Count; i++)
                {
                    ValidateCellJsonDto(dto.cells[i], i, dto.version, errors);
                }
            }

            if (dto.targetBoxLanes == null || dto.targetBoxLanes.Count != LevelDefinition.TargetBoxLaneCount)
            {
                errors.Add($"targetBoxLanes must contain {LevelDefinition.TargetBoxLaneCount} lanes.");
            }
            else
            {
                for (int laneIndex = 0; laneIndex < dto.targetBoxLanes.Count; laneIndex++)
                {
                    ValidateTargetLaneJsonDto(dto.targetBoxLanes[laneIndex], laneIndex, errors);
                }

                if (dto.version >= GameLevelJsonConstants.ConnectedTargetsIntroducedVersion)
                {
                    ValidateConnectedTargetGroups(dto.targetBoxLanes, errors);
                }
            }

            ValidateCrateJsonDtos(dto, errors);
            ValidatePanelJsonDtos(dto, errors);
            ValidateGateJsonDtos(dto, errors);
        }

        private static void ValidateCellJsonDto(GameLevelCellJsonDto cell, int cellIndex, int version, List<string> errors)
        {
            if (cell == null)
            {
                errors.Add($"cells[{cellIndex}] is null.");
                return;
            }

            if (cell.cellType != LevelCellType.Empty && cell.cellType != LevelCellType.SourceBox &&
                cell.cellType != LevelCellType.Spawner && cell.cellType != LevelCellType.GiftBox &&
                cell.cellType != LevelCellType.Blocked)
            {
                errors.Add($"cells[{cellIndex}].cellType is invalid.");
                return;
            }

            if (cell.cellType == LevelCellType.Blocked && version < GameLevelJsonConstants.RuntimeAvailabilityIntroducedVersion)
            {
                errors.Add($"cells[{cellIndex}].cellType Blocked requires JSON version {GameLevelJsonConstants.RuntimeAvailabilityIntroducedVersion} or newer.");
            }

            if (cell.cellType == LevelCellType.SourceBox)
            {
                if (!MarbleColorCatalog.IsGameplayColor(cell.colorId))
                {
                    errors.Add($"cells[{cellIndex}].colorId must be a valid non-None color for SourceBox.");
                }

                if (cell.marbleCount < LevelCellData.MinMarbleCount || cell.marbleCount > LevelCellData.MaxMarbleCount)
                {
                    errors.Add($"cells[{cellIndex}].marbleCount must be {LevelCellData.MinMarbleCount}-{LevelCellData.MaxMarbleCount}.");
                }

                if (cell.connectedBoxPairId < -1)
                {
                    errors.Add($"cells[{cellIndex}].connectedBoxPairId must be -1 or greater.");
                }

                if (cell.connectedBoxPairId >= 0 && cell.isMysterySourceBox)
                {
                    errors.Add($"cells[{cellIndex}] cannot be both Connected Box and mystery.");
                }

                int arrowDir = version >= GameLevelJsonConstants.ArrowBoxesIntroducedVersion ? cell.arrowDir : -1;
                if (arrowDir < -1 || arrowDir > 3)
                {
                    errors.Add($"cells[{cellIndex}].arrowDir must be -1, 0, 1, 2, or 3.");
                }

                if (arrowDir >= 0 && cell.connectedBoxPairId >= 0)
                {
                    errors.Add($"cells[{cellIndex}] cannot be both Arrow Box and Connected Box.");
                }

                if (arrowDir >= 0 && cell.isMysterySourceBox)
                {
                    errors.Add($"cells[{cellIndex}] cannot be both Arrow Box and mystery.");
                }
            }

            if (cell.cellType != LevelCellType.SourceBox && cell.connectedBoxPairId != -1)
            {
                errors.Add($"cells[{cellIndex}].connectedBoxPairId is only valid for SourceBox cells.");
            }

            if (cell.cellType != LevelCellType.SourceBox &&
                version >= GameLevelJsonConstants.ArrowBoxesIntroducedVersion && cell.arrowDir != -1)
            {
                errors.Add($"cells[{cellIndex}].arrowDir is only valid for SourceBox cells.");
            }

            if (cell.cellType != LevelCellType.SourceBox &&
                version >= GameLevelJsonConstants.KeyLockedTargetsIntroducedVersion && cell.hasKey)
            {
                errors.Add($"cells[{cellIndex}].hasKey is only valid for SourceBox cells.");
            }

            if (cell.cellType == LevelCellType.GiftBox)
            {
                int contentCount = cell.giftBoxContents?.Count ?? 0;
                if (contentCount == 0)
                {
                    errors.Add($"cells[{cellIndex}].giftBoxContents must contain at least one entry.");
                    return;
                }

                for (int i = 0; i < cell.giftBoxContents.Count; i++)
                {
                    GameGiftBoxSourceBoxJsonDto entry = cell.giftBoxContents[i];
                    if (entry == null)
                    {
                        errors.Add($"cells[{cellIndex}].giftBoxContents[{i}] is null.");
                        continue;
                    }

                    if (!MarbleColorCatalog.IsGameplayColor(entry.colorId))
                    {
                        errors.Add($"cells[{cellIndex}].giftBoxContents[{i}].colorId must be a valid non-None color.");
                    }

                    if (entry.marbleCount < LevelCellData.MinMarbleCount || entry.marbleCount > LevelCellData.MaxMarbleCount)
                    {
                        errors.Add($"cells[{cellIndex}].giftBoxContents[{i}].marbleCount must be {LevelCellData.MinMarbleCount}-{LevelCellData.MaxMarbleCount}.");
                    }
                }

                return;
            }

            if (cell.cellType != LevelCellType.Spawner)
            {
                return;
            }

            if (!Enum.IsDefined(typeof(SpawnerDirection), cell.spawnDirection) || !IsSupportedSpawnerDirection(cell.spawnDirection))
            {
                errors.Add($"cells[{cellIndex}].spawnDirection must be Down, Right, or Left.");
            }

            if (cell.spawnSequence == null || cell.spawnSequence.Count < LevelCellData.MinMarbleCount || cell.spawnSequence.Count > LevelCellData.MaxMarbleCount)
            {
                errors.Add($"cells[{cellIndex}].spawnSequence must contain {LevelCellData.MinMarbleCount}-{LevelCellData.MaxMarbleCount} entries.");
                return;
            }

            if (version >= GameLevelJsonConstants.SpawnCountIntroducedVersion && cell.spawnCount != cell.spawnSequence.Count)
            {
                errors.Add($"cells[{cellIndex}].spawnCount must match spawnSequence length.");
            }

            if (version < GameLevelJsonConstants.SpawnCountIntroducedVersion && cell.spawnCount == 0 && cell.spawnSequence.Count > 0)
            {
                cell.spawnCount = cell.spawnSequence.Count;
            }

            for (int i = 0; i < cell.spawnSequence.Count; i++)
            {
                GameSpawnerSourceBoxJsonDto entry = cell.spawnSequence[i];
                if (entry == null)
                {
                    errors.Add($"cells[{cellIndex}].spawnSequence[{i}] is null.");
                    continue;
                }

                if (!MarbleColorCatalog.IsGameplayColor(entry.colorId))
                {
                    errors.Add($"cells[{cellIndex}].spawnSequence[{i}].colorId must be a valid non-None color.");
                }
            }
        }

        private static void ValidateTargetLaneJsonDto(GameTargetLaneJsonDto lane, int laneIndex, List<string> errors)
        {
            if (lane == null || lane.boxes == null)
            {
                errors.Add($"targetBoxLanes[{laneIndex}].boxes is null.");
                return;
            }

            for (int i = 0; i < lane.boxes.Count; i++)
            {
                GameTargetBoxJsonDto box = lane.boxes[i];
                if (box == null)
                {
                    errors.Add($"targetBoxLanes[{laneIndex}].boxes[{i}] is null.");
                    continue;
                }

                if (!MarbleColorCatalog.IsGameplayColor(box.colorId))
                {
                    errors.Add($"targetBoxLanes[{laneIndex}].boxes[{i}].colorId must be a valid non-None color.");
                }

                if (box.connectedTargetGroupId < 0)
                {
                    errors.Add($"targetBoxLanes[{laneIndex}].boxes[{i}].connectedTargetGroupId must be 0 or greater.");
                }
            }
        }

        private static void ValidateConnectedTargetGroups(IReadOnlyList<GameTargetLaneJsonDto> lanes, List<string> errors)
        {
            Dictionary<int, int> memberCounts = new Dictionary<int, int>();
            Dictionary<int, HashSet<int>> lanesByGroup = new Dictionary<int, HashSet<int>>();

            for (int laneIndex = 0; laneIndex < lanes.Count; laneIndex++)
            {
                GameTargetLaneJsonDto lane = lanes[laneIndex];
                if (lane?.boxes == null)
                {
                    continue;
                }

                for (int boxIndex = 0; boxIndex < lane.boxes.Count; boxIndex++)
                {
                    GameTargetBoxJsonDto box = lane.boxes[boxIndex];
                    int groupId = box?.connectedTargetGroupId ?? 0;
                    if (groupId <= 0)
                    {
                        continue;
                    }

                    memberCounts.TryGetValue(groupId, out int count);
                    memberCounts[groupId] = count + 1;

                    if (!lanesByGroup.TryGetValue(groupId, out HashSet<int> groupLanes))
                    {
                        groupLanes = new HashSet<int>();
                        lanesByGroup.Add(groupId, groupLanes);
                    }

                    if (!groupLanes.Add(laneIndex))
                    {
                        errors.Add($"Connected Target group {groupId} has more than one member in targetBoxLanes[{laneIndex}].");
                    }
                }
            }

            foreach (KeyValuePair<int, int> pair in memberCounts)
            {
                if (pair.Value < 2 || pair.Value > LevelDefinition.TargetBoxLaneCount)
                {
                    errors.Add($"Connected Target group {pair.Key} has {pair.Value} members. Expected 2-{LevelDefinition.TargetBoxLaneCount} members in distinct lanes.");
                }
            }
        }

        private static void ValidateCrateJsonDtos(GameLevelJsonDto dto, List<string> errors)
        {
            int crateCount = dto.crates?.Count ?? 0;
            if (crateCount > 0 && dto.version < GameLevelJsonConstants.CratesIntroducedVersion)
            {
                errors.Add($"crates requires JSON version {GameLevelJsonConstants.CratesIntroducedVersion} or newer.");
                return;
            }

            for (int crateIndex = 0; crateIndex < crateCount; crateIndex++)
            {
                GameCrateJsonDto crate = dto.crates[crateIndex];
                if (crate == null)
                {
                    errors.Add($"crates[{crateIndex}] is null.");
                    continue;
                }

                if (crate.row < 0 || crate.row >= dto.rowCount - 1 || crate.col < 0 || crate.col >= dto.columnCount - 1)
                {
                    errors.Add($"crates[{crateIndex}] at row {crate.row}, col {crate.col} has a 2x2 footprint outside the board.");
                }
            }
        }

        private static void ValidatePanelJsonDtos(GameLevelJsonDto dto, List<string> errors)
        {
            int panelCount = dto.panels?.Count ?? 0;
            if (panelCount > 0 && dto.version < GameLevelJsonConstants.PanelsIntroducedVersion)
            {
                errors.Add($"panels requires JSON version {GameLevelJsonConstants.PanelsIntroducedVersion} or newer.");
                return;
            }

            for (int panelIndex = 0; panelIndex < panelCount; panelIndex++)
            {
                GamePanelJsonDto panel = dto.panels[panelIndex];
                if (panel == null)
                {
                    errors.Add($"panels[{panelIndex}] is null.");
                    continue;
                }

                if (panel.row < 0 || panel.row >= dto.rowCount - 2 || panel.col < 0 || panel.col >= dto.columnCount - 2)
                {
                    errors.Add($"panels[{panelIndex}] at row {panel.row}, col {panel.col} has a 3x3 footprint outside the board.");
                }

                if (panel.number < 1)
                {
                    errors.Add($"panels[{panelIndex}].number must be at least 1.");
                }
            }
        }

        private static void ValidateGateJsonDtos(GameLevelJsonDto dto, List<string> errors)
        {
            int gateCount = dto.gates?.Count ?? 0;
            if (gateCount > 0 && dto.version < GameLevelJsonConstants.MultiplierGatesIntroducedVersion)
            {
                errors.Add($"gates requires JSON version {GameLevelJsonConstants.MultiplierGatesIntroducedVersion} or newer.");
                return;
            }

            for (int gateIndex = 0; gateIndex < gateCount; gateIndex++)
            {
                GameMultiplierGateJsonDto gate = dto.gates[gateIndex];
                if (gate == null)
                {
                    errors.Add($"gates[{gateIndex}] is null.");
                    continue;
                }

                if (gate.row < 0 || gate.row >= dto.rowCount || gate.col < 0 || gate.col >= dto.columnCount - 1)
                {
                    errors.Add($"gates[{gateIndex}] at row {gate.row}, col {gate.col} has a horizontal 2-cell footprint outside the board.");
                }

                if (gate.mult != 2)
                {
                    errors.Add($"gates[{gateIndex}].mult must be 2.");
                }
            }
        }

        private static void ApplyDtoToLevel(LevelDefinition level, GameLevelJsonDto dto)
        {
            SerializedObject serializedLevel = new SerializedObject(level);
            serializedLevel.Update();
            SerializedProperty levelNumberProperty = serializedLevel.FindProperty(LevelNumberPropertyName);
            SerializedProperty difficultyProperty = serializedLevel.FindProperty(DifficultyPropertyName);
            SerializedProperty rowCountProperty = serializedLevel.FindProperty(RowCountPropertyName);
            SerializedProperty columnCountProperty = serializedLevel.FindProperty(ColumnCountPropertyName);
            SerializedProperty cellsProperty = serializedLevel.FindProperty(CellsPropertyName);
            SerializedProperty cratesProperty = serializedLevel.FindProperty(CratesPropertyName);
            SerializedProperty panelsProperty = serializedLevel.FindProperty(PanelsPropertyName);
            SerializedProperty gatesProperty = serializedLevel.FindProperty(GatesPropertyName);
            SerializedProperty targetBoxLanesProperty = serializedLevel.FindProperty(TargetBoxLanesPropertyName);

            if (levelNumberProperty == null || difficultyProperty == null || rowCountProperty == null || columnCountProperty == null || cellsProperty == null || cratesProperty == null || panelsProperty == null || gatesProperty == null || targetBoxLanesProperty == null)
            {
                throw new InvalidOperationException("Required LevelDefinition serialized properties are missing.");
            }

            levelNumberProperty.intValue = Mathf.Max(1, dto.levelNumber);
            difficultyProperty.enumValueIndex = (int)LevelDifficultyParser.Parse(
                dto.difficulty,
                out _);
            rowCountProperty.intValue = Mathf.Max(1, dto.rowCount);
            columnCountProperty.intValue = Mathf.Max(1, dto.columnCount);

            cellsProperty.arraySize = dto.cells.Count;
            for (int i = 0; i < dto.cells.Count; i++)
            {
                ApplyCellDto(cellsProperty.GetArrayElementAtIndex(i), dto.cells[i], dto.version);
            }

            int crateCount = dto.version >= GameLevelJsonConstants.CratesIntroducedVersion
                ? dto.crates?.Count ?? 0
                : 0;
            cratesProperty.arraySize = crateCount;
            for (int crateIndex = 0; crateIndex < crateCount; crateIndex++)
            {
                GameCrateJsonDto crate = dto.crates[crateIndex];
                SerializedProperty crateProperty = cratesProperty.GetArrayElementAtIndex(crateIndex);
                SetInt(crateProperty, RowPropertyName, crate.row);
                SetInt(crateProperty, ColPropertyName, crate.col);
            }

            int panelCount = dto.version >= GameLevelJsonConstants.PanelsIntroducedVersion
                ? dto.panels?.Count ?? 0
                : 0;
            panelsProperty.arraySize = panelCount;
            for (int panelIndex = 0; panelIndex < panelCount; panelIndex++)
            {
                GamePanelJsonDto panel = dto.panels[panelIndex];
                SerializedProperty panelProperty = panelsProperty.GetArrayElementAtIndex(panelIndex);
                SetInt(panelProperty, RowPropertyName, panel.row);
                SetInt(panelProperty, ColPropertyName, panel.col);
                SetInt(panelProperty, NumberPropertyName, panel.number);
            }

            int gateCount = dto.version >= GameLevelJsonConstants.MultiplierGatesIntroducedVersion
                ? dto.gates?.Count ?? 0
                : 0;
            gatesProperty.arraySize = gateCount;
            for (int gateIndex = 0; gateIndex < gateCount; gateIndex++)
            {
                GameMultiplierGateJsonDto gate = dto.gates[gateIndex];
                SerializedProperty gateProperty = gatesProperty.GetArrayElementAtIndex(gateIndex);
                SetInt(gateProperty, RowPropertyName, gate.row);
                SetInt(gateProperty, ColPropertyName, gate.col);
                SetInt(gateProperty, MultiplierPropertyName, gate.mult);
            }

            targetBoxLanesProperty.arraySize = LevelDefinition.TargetBoxLaneCount;
            for (int laneIndex = 0; laneIndex < LevelDefinition.TargetBoxLaneCount; laneIndex++)
            {
                ApplyTargetLaneDto(targetBoxLanesProperty.GetArrayElementAtIndex(laneIndex), dto.targetBoxLanes[laneIndex], dto.version);
            }

            serializedLevel.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ApplyCellDto(SerializedProperty cellProperty, GameLevelCellJsonDto dto, int version)
        {
            if (dto.cellType == LevelCellType.Empty || dto.cellType == LevelCellType.Blocked)
            {
                SetCellType(cellProperty, dto.cellType);
                SetColorId(cellProperty, MarbleColorId.None);
                SetMarbleCount(cellProperty, 0);
                SetMysterySourceBox(cellProperty, false);
                SetConnectedBoxPairId(cellProperty, -1);
                SetArrowDirection(cellProperty, ArrowDirection.None);
                SetHasKey(cellProperty, false);
                SetSpawnDirection(cellProperty, SpawnerDirection.Up);
                ClearSpawnerSequence(cellProperty);
                ClearGiftBoxContents(cellProperty);
                return;
            }

            if (dto.cellType == LevelCellType.SourceBox)
            {
                SetCellType(cellProperty, LevelCellType.SourceBox);
                SetColorId(cellProperty, dto.colorId);
                SetMarbleCount(cellProperty, dto.marbleCount);
                SetMysterySourceBox(cellProperty, dto.isMysterySourceBox);
                SetConnectedBoxPairId(cellProperty, version >= GameLevelJsonConstants.ConnectedBoxesIntroducedVersion ? dto.connectedBoxPairId : -1);
                SetArrowDirection(cellProperty, version >= GameLevelJsonConstants.ArrowBoxesIntroducedVersion
                    ? (ArrowDirection)dto.arrowDir
                    : ArrowDirection.None);
                SetHasKey(cellProperty, version >= GameLevelJsonConstants.KeyLockedTargetsIntroducedVersion && dto.hasKey);
                SetSpawnDirection(cellProperty, SpawnerDirection.Up);
                ClearSpawnerSequence(cellProperty);
                ClearGiftBoxContents(cellProperty);
                return;
            }

            if (dto.cellType == LevelCellType.GiftBox)
            {
                SetCellType(cellProperty, LevelCellType.GiftBox);
                SetColorId(cellProperty, MarbleColorId.None);
                SetMarbleCount(cellProperty, 0);
                SetMysterySourceBox(cellProperty, false);
                SetConnectedBoxPairId(cellProperty, -1);
                SetArrowDirection(cellProperty, ArrowDirection.None);
                SetHasKey(cellProperty, false);
                SetSpawnDirection(cellProperty, SpawnerDirection.Up);
                ClearSpawnerSequence(cellProperty);
                SerializedProperty contents = cellProperty.FindPropertyRelative(GiftBoxContentsPropertyName);
                if (contents != null)
                {
                    contents.arraySize = dto.giftBoxContents.Count;
                    for (int i = 0; i < dto.giftBoxContents.Count; i++)
                    {
                        SetGiftBoxContentEntry(contents.GetArrayElementAtIndex(i), dto.giftBoxContents[i]);
                    }
                }

                return;
            }

            SetCellType(cellProperty, LevelCellType.Spawner);
            SetColorId(cellProperty, MarbleColorId.None);
            SetMarbleCount(cellProperty, 0);
            SetMysterySourceBox(cellProperty, false);
            SetConnectedBoxPairId(cellProperty, -1);
            SetArrowDirection(cellProperty, ArrowDirection.None);
            SetHasKey(cellProperty, false);
            ClearGiftBoxContents(cellProperty);
            SetSpawnDirection(cellProperty, dto.spawnDirection);
            SerializedProperty sequence = cellProperty.FindPropertyRelative(SpawnSequencePropertyName);
            if (sequence == null)
            {
                return;
            }

            sequence.arraySize = dto.spawnSequence.Count;
            for (int i = 0; i < dto.spawnSequence.Count; i++)
            {
                SetSpawnerSequenceEntry(sequence.GetArrayElementAtIndex(i), dto.spawnSequence[i].colorId);
            }
        }

        private static void ApplyTargetLaneDto(SerializedProperty laneProperty, GameTargetLaneJsonDto dto, int version)
        {
            SerializedProperty boxes = laneProperty.FindPropertyRelative(TargetBoxesPropertyName);
            SerializedProperty legacyBoxColors = laneProperty.FindPropertyRelative(LegacyBoxColorsPropertyName);
            legacyBoxColors?.ClearArray();
            if (boxes == null)
            {
                return;
            }

            boxes.arraySize = dto.boxes.Count;
            for (int i = 0; i < dto.boxes.Count; i++)
            {
                int connectedTargetGroupId = version >= GameLevelJsonConstants.ConnectedTargetsIntroducedVersion
                    ? dto.boxes[i].connectedTargetGroupId
                    : 0;
                bool isLocked = version >= GameLevelJsonConstants.KeyLockedTargetsIntroducedVersion && dto.boxes[i].isLocked;
                SetTargetBoxEntry(
                    boxes.GetArrayElementAtIndex(i),
                    dto.boxes[i].colorId,
                    dto.boxes[i].isMystery,
                    connectedTargetGroupId,
                    isLocked);
            }
        }

        private static void SetCellType(SerializedProperty cellProperty, LevelCellType value) => SetInt(cellProperty, CellTypePropertyName, (int)value);
        private static void SetColorId(SerializedProperty property, MarbleColorId value) => SetInt(property, ColorIdPropertyName, (int)value);
        private static void SetMarbleCount(SerializedProperty property, int value) => SetInt(property, MarbleCountPropertyName, value);
        private static void SetConnectedBoxPairId(SerializedProperty property, int value) => SetInt(property, ConnectedBoxPairIdPropertyName, value);
        private static void SetArrowDirection(SerializedProperty property, ArrowDirection value) => SetInt(property, ArrowDirectionPropertyName, (int)value);
        private static void SetHasKey(SerializedProperty cellProperty, bool value)
        {
            SerializedProperty property = cellProperty.FindPropertyRelative(HasKeyPropertyName);
            if (property != null)
            {
                property.boolValue = value;
            }
        }
        private static void SetSpawnDirection(SerializedProperty property, SpawnerDirection value) => SetInt(property, SpawnDirectionPropertyName, (int)value);

        private static void SetMysterySourceBox(SerializedProperty cellProperty, bool value)
        {
            SerializedProperty property = cellProperty.FindPropertyRelative(IsMysterySourceBoxPropertyName);
            if (property != null)
            {
                property.boolValue = value;
            }
        }

        private static void SetInt(SerializedProperty parentProperty, string propertyName, int value)
        {
            SerializedProperty property = parentProperty.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.intValue = value;
            }
        }

        private static void ClearSpawnerSequence(SerializedProperty cellProperty)
        {
            SerializedProperty sequence = cellProperty.FindPropertyRelative(SpawnSequencePropertyName);
            sequence?.ClearArray();
        }

        private static void ClearGiftBoxContents(SerializedProperty cellProperty)
        {
            SerializedProperty contents = cellProperty.FindPropertyRelative(GiftBoxContentsPropertyName);
            contents?.ClearArray();
        }

        private static void SetSpawnerSequenceEntry(SerializedProperty entryProperty, MarbleColorId colorId)
        {
            SetInt(entryProperty, ColorIdPropertyName, (int)(colorId == MarbleColorId.None ? MarbleColorId.Blue : colorId));
        }

        private static void SetGiftBoxContentEntry(SerializedProperty entryProperty, GameGiftBoxSourceBoxJsonDto entry)
        {
            if (entry == null)
            {
                return;
            }

            SetInt(entryProperty, ColorIdPropertyName, (int)entry.colorId);
            SetInt(entryProperty, MarbleCountPropertyName, entry.marbleCount);
        }

        private static void SetTargetBoxEntry(
            SerializedProperty boxProperty,
            MarbleColorId colorId,
            bool isMystery,
            int connectedTargetGroupId,
            bool isLocked)
        {
            SetInt(boxProperty, ColorIdPropertyName, (int)(colorId == MarbleColorId.None ? MarbleColorId.Blue : colorId));
            SetInt(boxProperty, ConnectedTargetGroupIdPropertyName, connectedTargetGroupId);
            SerializedProperty isMysteryProperty = boxProperty.FindPropertyRelative(IsMysteryPropertyName);
            if (isMysteryProperty != null)
            {
                isMysteryProperty.boolValue = isMystery;
            }

            SerializedProperty isLockedProperty = boxProperty.FindPropertyRelative(IsLockedPropertyName);
            if (isLockedProperty != null)
            {
                isLockedProperty.boolValue = isLocked;
            }
        }

        private static HashSet<int> FindDuplicateLevelNumbers(string[] guids)
        {
            Dictionary<int, string> firstByNumber = new Dictionary<int, string>();
            HashSet<int> duplicates = new HashSet<int>();
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                LevelDefinition level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(path);
                if (level == null)
                {
                    continue;
                }

                if (firstByNumber.ContainsKey(level.LevelNumber))
                {
                    duplicates.Add(level.LevelNumber);
                }
                else
                {
                    firstByNumber.Add(level.LevelNumber, path);
                }
            }

            return duplicates;
        }

        private static List<LevelDefinition> LoadLevelsInFolder(string folder)
        {
            List<LevelDefinition> levels = new List<LevelDefinition>();
            string[] guids = AssetDatabase.FindAssets("t:LevelDefinition", new[] { folder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                LevelDefinition level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(path);
                if (level != null && !levels.Contains(level))
                {
                    levels.Add(level);
                }
            }

            levels.Sort((left, right) =>
            {
                int numberCompare = left.LevelNumber.CompareTo(right.LevelNumber);
                return numberCompare != 0
                    ? numberCompare
                    : string.Compare(left.name, right.name, StringComparison.Ordinal);
            });
            return levels;
        }

        private static int CompareSummaries(SharedLevelSummary left, SharedLevelSummary right)
        {
            int numberCompare = left.LevelNumber.CompareTo(right.LevelNumber);
            return numberCompare != 0
                ? numberCompare
                : string.Compare(left.AssetPath, right.AssetPath, StringComparison.Ordinal);
        }

        private static bool IsSupportedSpawnerDirection(SpawnerDirection direction)
        {
            return direction == SpawnerDirection.Down ||
                   direction == SpawnerDirection.Right ||
                   direction == SpawnerDirection.Left;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
