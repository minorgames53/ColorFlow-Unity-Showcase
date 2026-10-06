using System;
using System.Collections.Generic;
using Gameplay.Levels;
using Gameplay.SourceBoxes;
using Newtonsoft.Json.Linq;

namespace Gameplay.Editor.LevelManagement
{
    /// <summary>
    /// Converts the Level Designer authoring contract into the single DTO consumed by
    /// the existing Unity LevelDefinition import pipeline.
    /// </summary>
    internal static class GameLevelDesignerJsonAdapter
    {
        private const int DesignerGiftBoxMarbleCount = 9;

        public static GameLevelJsonDto ParseAndNormalize(JObject levelObject)
        {
            if (levelObject == null)
            {
                return null;
            }

            if (!IsDesignerFormat(levelObject))
            {
                GameLevelJsonDto level = levelObject.ToObject<GameLevelJsonDto>();
                NormalizeDifficultyAlias(level);
                return level;
            }

            DesignerLevelJsonDto external = levelObject.ToObject<DesignerLevelJsonDto>();
            return Normalize(external);
        }

        private static bool IsDesignerFormat(JObject levelObject)
        {
            if (levelObject["cells"] is JArray cells)
            {
                for (int i = 0; i < cells.Count; i++)
                {
                    if (cells[i] is JObject cell &&
                        (cell.Property("connectedTo", StringComparison.Ordinal) != null ||
                         cell.Property("giftContents", StringComparison.Ordinal) != null))
                    {
                        return true;
                    }
                }
            }

            if (levelObject["targetBoxLanes"] is JArray lanes)
            {
                for (int laneIndex = 0; laneIndex < lanes.Count; laneIndex++)
                {
                    if (!(lanes[laneIndex] is JObject lane) || !(lane["boxes"] is JArray boxes))
                    {
                        continue;
                    }

                    for (int boxIndex = 0; boxIndex < boxes.Count; boxIndex++)
                    {
                        if (boxes[boxIndex] is JObject box &&
                            box.Property("linkId", StringComparison.Ordinal) != null)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static GameLevelJsonDto Normalize(DesignerLevelJsonDto external)
        {
            if (external == null)
            {
                return null;
            }

            GameLevelJsonDto result = new GameLevelJsonDto
            {
                version = GameLevelJsonConstants.CurrentVersion,
                levelId = external.levelId,
                levelNumber = external.levelNumber,
                difficulty = ResolveDifficulty(external.difficulty, external.levelType),
                rowCount = external.rowCount,
                columnCount = external.columnCount,
                isExternalAuthoringFormat = true,
                externalSchemaVersion = external.version,
                cells = new List<GameLevelCellJsonDto>(),
                crates = CopyCrates(external.crates),
                panels = CopyPanels(external.panels),
                gates = CopyGates(external.gates),
                targetBoxLanes = new List<GameTargetLaneJsonDto>(),
                normalizationErrors = new List<string>()
            };

            IReadOnlyList<DesignerLevelCellJsonDto> externalCells = external.cells;
            if (externalCells == null)
            {
                externalCells = Array.Empty<DesignerLevelCellJsonDto>();
            }
            for (int cellIndex = 0; cellIndex < externalCells.Count; cellIndex++)
            {
                result.cells.Add(NormalizeCell(externalCells[cellIndex], cellIndex, result.normalizationErrors));
            }

            NormalizeConnectedSourceBoxes(
                externalCells,
                result.cells,
                external.rowCount,
                external.columnCount,
                result.normalizationErrors);
            NormalizeTargetLanes(external.targetBoxLanes, result.targetBoxLanes, result.normalizationErrors);
            return result;
        }

        private static void NormalizeDifficultyAlias(GameLevelJsonDto level)
        {
            if (level != null)
            {
                level.difficulty = ResolveDifficulty(level.difficulty, level.levelType);
            }
        }

        private static string ResolveDifficulty(string difficulty, string levelType)
        {
            return string.IsNullOrWhiteSpace(difficulty) ? levelType : difficulty;
        }

        private static GameLevelCellJsonDto NormalizeCell(
            DesignerLevelCellJsonDto external,
            int cellIndex,
            List<string> errors)
        {
            if (external == null)
            {
                errors.Add($"cells[{cellIndex}] is null.");
                return null;
            }

            LevelCellType cellType = MapCellType(external.cellType, cellIndex, errors);
            GameLevelCellJsonDto result = new GameLevelCellJsonDto
            {
                cellType = cellType,
                colorId = (MarbleColorId)external.colorId,
                marbleCount = external.marbleCount,
                isMysterySourceBox = external.isMysterySourceBox,
                connectedBoxPairId = external.connectedBoxPairId ?? -1,
                arrowDir = external.arrowDir ?? -1,
                hasKey = external.hasKey,
                spawnDirection = (SpawnerDirection)external.spawnDirection,
                spawnSequence = external.spawnSequence ?? new List<GameSpawnerSourceBoxJsonDto>(),
                giftBoxContents = new List<GameGiftBoxSourceBoxJsonDto>()
            };

            result.spawnCount = external.spawnCount ?? result.spawnSequence.Count;
            if (cellType != LevelCellType.GiftBox)
            {
                result.giftBoxContents = external.giftBoxContents ?? new List<GameGiftBoxSourceBoxJsonDto>();
                return result;
            }

            if (external.giftContents != null)
            {
                if (external.giftContents.Count == 0)
                {
                    errors.Add($"cells[{cellIndex}].giftContents must contain at least one color ID.");
                }

                for (int contentIndex = 0; contentIndex < external.giftContents.Count; contentIndex++)
                {
                    result.giftBoxContents.Add(new GameGiftBoxSourceBoxJsonDto
                    {
                        colorId = (MarbleColorId)external.giftContents[contentIndex],
                        marbleCount = DesignerGiftBoxMarbleCount
                    });
                }
            }
            else if (external.giftBoxContents != null)
            {
                result.giftBoxContents.AddRange(external.giftBoxContents);
            }
            else
            {
                errors.Add($"cells[{cellIndex}] is an external Gift Box but has no giftContents field.");
            }

            return result;
        }

        private static void NormalizeConnectedSourceBoxes(
            IReadOnlyList<DesignerLevelCellJsonDto> externalCells,
            IReadOnlyList<GameLevelCellJsonDto> cells,
            int rowCount,
            int columnCount,
            List<string> errors)
        {
            for (int cellIndex = 0; cellIndex < externalCells.Count; cellIndex++)
            {
                DesignerLevelCellJsonDto external = externalCells[cellIndex];
                GameLevelCellJsonDto cell = cellIndex < cells.Count ? cells[cellIndex] : null;
                if (external == null || cell == null || !external.connectedTo.HasValue)
                {
                    continue;
                }

                int partnerIndex = external.connectedTo.Value;
                if (partnerIndex < -1)
                {
                    errors.Add($"cells[{cellIndex}].connectedTo must be -1 or a valid partner cell index.");
                    cell.connectedBoxPairId = -1;
                    continue;
                }

                if (partnerIndex == -1)
                {
                    cell.connectedBoxPairId = -1;
                    continue;
                }

                if (cell.cellType != LevelCellType.SourceBox)
                {
                    errors.Add($"cells[{cellIndex}].connectedTo is only valid for SourceBox cells.");
                    cell.connectedBoxPairId = -1;
                    continue;
                }

                if (partnerIndex == cellIndex)
                {
                    errors.Add($"cells[{cellIndex}].connectedTo cannot reference itself.");
                    cell.connectedBoxPairId = -1;
                    continue;
                }

                long configuredCellCount = (long)rowCount * columnCount;
                if (partnerIndex < 0 ||
                    partnerIndex >= externalCells.Count ||
                    (configuredCellCount > 0 && partnerIndex >= configuredCellCount))
                {
                    errors.Add($"cells[{cellIndex}].connectedTo references out-of-bounds cell {partnerIndex}.");
                    cell.connectedBoxPairId = -1;
                    continue;
                }

                DesignerLevelCellJsonDto partnerExternal = externalCells[partnerIndex];
                GameLevelCellJsonDto partnerCell = partnerIndex < cells.Count ? cells[partnerIndex] : null;
                if (partnerExternal == null || partnerCell == null || partnerCell.cellType != LevelCellType.SourceBox)
                {
                    errors.Add($"cells[{cellIndex}].connectedTo references cell {partnerIndex}, which is not a SourceBox.");
                    cell.connectedBoxPairId = -1;
                    continue;
                }

                if (!partnerExternal.connectedTo.HasValue || partnerExternal.connectedTo.Value != cellIndex)
                {
                    string partnerValue = partnerExternal.connectedTo.HasValue
                        ? partnerExternal.connectedTo.Value.ToString()
                        : "missing";
                    errors.Add($"cells[{cellIndex}] -> {partnerIndex} is not symmetric; cells[{partnerIndex}].connectedTo is {partnerValue}.");
                    cell.connectedBoxPairId = -1;
                    continue;
                }

                int safeColumnCount = Math.Max(1, columnCount);
                int row = cellIndex / safeColumnCount;
                int column = cellIndex % safeColumnCount;
                int partnerRow = partnerIndex / safeColumnCount;
                int partnerColumn = partnerIndex % safeColumnCount;
                int distance = Math.Abs(row - partnerRow) + Math.Abs(column - partnerColumn);
                if (distance != 1)
                {
                    errors.Add($"cells[{cellIndex}] and cells[{partnerIndex}] must be horizontal or vertical neighbors for connectedTo.");
                    cell.connectedBoxPairId = -1;
                    continue;
                }

                cell.connectedBoxPairId = Math.Min(cellIndex, partnerIndex);
            }
        }

        private static void NormalizeTargetLanes(
            IReadOnlyList<DesignerTargetLaneJsonDto> externalLanes,
            List<GameTargetLaneJsonDto> targetLanes,
            List<string> errors)
        {
            Dictionary<int, int> memberCounts = new Dictionary<int, int>();
            Dictionary<int, HashSet<int>> lanesByExternalGroup = new Dictionary<int, HashSet<int>>();
            IReadOnlyList<DesignerTargetLaneJsonDto> lanes = externalLanes;
            if (lanes == null)
            {
                lanes = Array.Empty<DesignerTargetLaneJsonDto>();
            }

            for (int laneIndex = 0; laneIndex < lanes.Count; laneIndex++)
            {
                DesignerTargetLaneJsonDto externalLane = lanes[laneIndex];
                GameTargetLaneJsonDto lane = new GameTargetLaneJsonDto();
                targetLanes.Add(lane);
                if (externalLane?.boxes == null)
                {
                    continue;
                }

                for (int boxIndex = 0; boxIndex < externalLane.boxes.Count; boxIndex++)
                {
                    DesignerTargetBoxJsonDto externalBox = externalLane.boxes[boxIndex];
                    if (externalBox == null)
                    {
                        lane.boxes.Add(null);
                        errors.Add($"targetBoxLanes[{laneIndex}].boxes[{boxIndex}] is null.");
                        continue;
                    }


                    int internalGroupId = externalBox.connectedTargetGroupId ?? 0;
                    if (!externalBox.linkId.HasValue && internalGroupId < 0)
                    {
                        errors.Add($"targetBoxLanes[{laneIndex}].boxes[{boxIndex}].connectedTargetGroupId must be 0 or greater.");
                        internalGroupId = 0;
                    }

                    if (externalBox.linkId.HasValue)
                    {
                        int linkId = externalBox.linkId.Value;
                        if (linkId < -1)
                        {
                            errors.Add($"targetBoxLanes[{laneIndex}].boxes[{boxIndex}].linkId must be -1 or greater.");
                            internalGroupId = 0;
                        }
                        else if (linkId == -1)
                        {
                            internalGroupId = 0;
                        }
                        else
                        {
                            if (linkId == int.MaxValue)
                            {
                                errors.Add($"targetBoxLanes[{laneIndex}].boxes[{boxIndex}].linkId is too large to normalize.");
                                internalGroupId = 0;
                            }
                            else
                            {
                                internalGroupId = linkId + 1;
                                memberCounts.TryGetValue(linkId, out int memberCount);
                                memberCounts[linkId] = memberCount + 1;
                                if (!lanesByExternalGroup.TryGetValue(linkId, out HashSet<int> groupLanes))
                                {
                                    groupLanes = new HashSet<int>();
                                    lanesByExternalGroup.Add(linkId, groupLanes);
                                }

                                if (!groupLanes.Add(laneIndex))
                                {
                                    errors.Add($"External Connected Target group linkId {linkId} has more than one member in targetBoxLanes[{laneIndex}].");
                                }
                            }
                        }
                    }

                    lane.boxes.Add(new GameTargetBoxJsonDto
                    {
                        colorId = (MarbleColorId)externalBox.colorId,
                        isMystery = externalBox.isMystery,
                        isLocked = externalBox.isLocked ?? false,
                        connectedTargetGroupId = internalGroupId
                    });
                }
            }

            foreach (KeyValuePair<int, int> group in memberCounts)
            {
                if (group.Value < 2 || group.Value > LevelDefinition.TargetBoxLaneCount)
                {
                    errors.Add($"External Connected Target group linkId {group.Key} has {group.Value} members. Expected 2-{LevelDefinition.TargetBoxLaneCount} members in distinct lanes.");
                }
            }
        }

        private static LevelCellType MapCellType(int externalCellType, int cellIndex, List<string> errors)
        {
            switch (externalCellType)
            {
                case 0: return LevelCellType.Empty;
                case 1: return LevelCellType.SourceBox;
                case 2: return LevelCellType.Spawner;
                case 3: return LevelCellType.Blocked;
                case 4: return LevelCellType.GiftBox;
                default:
                    errors.Add($"cells[{cellIndex}].cellType has unsupported external value {externalCellType}.");
                    return LevelCellType.Empty;
            }
        }

        private static List<GameCrateJsonDto> CopyCrates(IReadOnlyList<GameCrateJsonDto> crates)
        {
            List<GameCrateJsonDto> result = new List<GameCrateJsonDto>();
            if (crates == null)
            {
                return result;
            }

            for (int i = 0; i < crates.Count; i++)
            {
                GameCrateJsonDto crate = crates[i];
                result.Add(crate == null ? null : new GameCrateJsonDto { row = crate.row, col = crate.col });
            }

            return result;
        }

        private static List<GamePanelJsonDto> CopyPanels(IReadOnlyList<GamePanelJsonDto> panels)
        {
            List<GamePanelJsonDto> result = new List<GamePanelJsonDto>();
            if (panels == null)
            {
                return result;
            }

            for (int i = 0; i < panels.Count; i++)
            {
                GamePanelJsonDto panel = panels[i];
                result.Add(panel == null ? null : new GamePanelJsonDto
                {
                    row = panel.row,
                    col = panel.col,
                    number = panel.number
                });
            }

            return result;
        }

        private static List<GameMultiplierGateJsonDto> CopyGates(IReadOnlyList<GameMultiplierGateJsonDto> gates)
        {
            List<GameMultiplierGateJsonDto> result = new List<GameMultiplierGateJsonDto>();
            if (gates == null)
            {
                return result;
            }

            for (int i = 0; i < gates.Count; i++)
            {
                GameMultiplierGateJsonDto gate = gates[i];
                result.Add(gate == null ? null : new GameMultiplierGateJsonDto
                {
                    row = gate.row,
                    col = gate.col,
                    mult = gate.mult
                });
            }

            return result;
        }

        [Serializable]
        private sealed class DesignerLevelJsonDto
        {
            public int? version;
            public string levelId;
            public int levelNumber;
            public string difficulty;
            public string levelType;
            public int rowCount;
            public int columnCount;
            public List<DesignerLevelCellJsonDto> cells;
            public List<GameCrateJsonDto> crates;
            public List<GamePanelJsonDto> panels;
            public List<GameMultiplierGateJsonDto> gates;
            public List<DesignerTargetLaneJsonDto> targetBoxLanes;
        }

        [Serializable]
        private sealed class DesignerLevelCellJsonDto
        {
            public int cellType;
            public int colorId;
            public int marbleCount;
            public bool isMysterySourceBox;
            public int? connectedTo;
            public int? connectedBoxPairId;
            public int? arrowDir;
            public bool hasKey;
            public int spawnDirection;
            public int? spawnCount;
            public List<GameSpawnerSourceBoxJsonDto> spawnSequence;
            public List<int> giftContents;
            public List<GameGiftBoxSourceBoxJsonDto> giftBoxContents;
        }

        [Serializable]
        private sealed class DesignerTargetLaneJsonDto
        {
            public List<DesignerTargetBoxJsonDto> boxes;
        }

        [Serializable]
        private sealed class DesignerTargetBoxJsonDto
        {
            public int colorId;
            public bool isMystery;
            public int? linkId;
            public bool? isLocked;
            public int? connectedTargetGroupId;
        }
    }
}
