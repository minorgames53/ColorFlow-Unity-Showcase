using System;
using System.Collections.Generic;
using Gameplay.SourceBoxes;
using UnityEngine;

namespace Gameplay.Levels
{
    public static class LevelDefinitionValidator
    {
        public static bool Validate(LevelDefinition level, List<string> errors)
        {
            return ValidateInternal(level, errors, true);
        }

        public static bool ValidateForRuntimeBuild(LevelDefinition level, List<string> errors)
        {
            return ValidateInternal(level, errors, false);
        }

#if UNITY_EDITOR
        public static void GetDebugInventory(LevelDefinition level, Dictionary<MarbleColorId, int> supply,
            Dictionary<MarbleColorId, int> requirements, List<string> errors)
        {
            ValidateInternal(level, errors, false, supply, requirements);
            if (level != null && level.InitialConveyorMarbles != null)
                foreach (var color in level.InitialConveyorMarbles)
                {
                    supply.TryGetValue(color, out int count);
                    supply[color] = count + 1;
                }
            foreach (var pair in requirements)
            {
                supply.TryGetValue(pair.Key, out int count);
                if (count != pair.Value)
                    errors.Add($"INITIAL DATA IMBALANCE: {pair.Key} authored supply (including seeds/deferred/gates) {count}, target requirement {pair.Value}.");
            }
        }
#endif

        private static bool ValidateInternal(
            LevelDefinition level,
            List<string> errors,
            bool validateMarbleCountBalance
#if UNITY_EDITOR
            , Dictionary<MarbleColorId, int> debugSupply = null,
            Dictionary<MarbleColorId, int> debugRequirements = null
#endif
            )
        {
            if (errors == null)
            {
                return false;
            }

            if (level == null)
            {
                errors.Add("LevelDefinition reference is null.");
                return false;
            }

            bool isValid = true;
            int rowCount = level.RowCount;
            int columnCount = level.ColumnCount;
            int safeColumnCount = Math.Max(1, columnCount);
            IReadOnlyList<LevelCellData> cells = level.Cells;
            IReadOnlyList<TargetBoxLaneData> targetBoxLanes = level.TargetBoxLanes;
            Dictionary<MarbleColorId, int> sourceMarbleCounts = CreateColorCountMap();
            Dictionary<MarbleColorId, int> targetBoxCounts = CreateColorCountMap();
            HashSet<int> spawnerTargetIndices = new HashSet<int>();

            if (level.LevelNumber < 1)
            {
                errors.Add($"Level '{level.name}' has invalid level number {level.LevelNumber}. Expected minimum 1.");
                isValid = false;
            }

            if (rowCount < 1)
            {
                errors.Add($"Level '{level.name}' has invalid row count {rowCount}. Expected minimum 1.");
                isValid = false;
            }

            if (columnCount < 1)
            {
                errors.Add($"Level '{level.name}' has invalid column count {columnCount}. Expected minimum 1.");
                isValid = false;
            }

            if (cells == null)
            {
                errors.Add($"Level '{level.name}' cell list is null.");
                return false;
            }

            int expectedCellCount = rowCount > 0 && columnCount > 0 ? rowCount * columnCount : 0;
            if (cells.Count != expectedCellCount)
            {
                errors.Add($"Level '{level.name}' has {cells.Count} cells but expected {expectedCellCount} for {rowCount}x{columnCount}.");
                isValid = false;
            }

            int sourceBoxCount = 0;
            int keySourceBoxCount = 0;
            int inspectedCellCount = Math.Min(cells.Count, Math.Max(0, expectedCellCount));

            for (int i = 0; i < inspectedCellCount; i++)
            {
                LevelCellData cell = cells[i];
                int row = i / safeColumnCount;
                int column = i % safeColumnCount;

                if (cell == null)
                {
                    errors.Add($"Level '{level.name}' cell {i} at row {row}, column {column} is null.");
                    isValid = false;
                    continue;
                }

                if (cell.IsMysterySourceBox && cell.CellType != LevelCellType.SourceBox)
                {
                    errors.Add($"Level '{level.name}' cell {i} at row {row}, column {column} is marked as mystery but is not a SourceBox.");
                    isValid = false;
                }

                if (cell.ConnectedBoxPairId < -1)
                {
                    errors.Add($"Level '{level.name}' cell {i} at row {row}, column {column} has invalid Connected Box pair id {cell.ConnectedBoxPairId}. Expected -1 or greater.");
                    isValid = false;
                }

                int arrowDirectionValue = (int)cell.ArrowDirection;
                if (arrowDirectionValue < (int)ArrowDirection.None || arrowDirectionValue > (int)ArrowDirection.Left)
                {
                    errors.Add($"Level '{level.name}' cell {i} at row {row}, column {column} has invalid Arrow direction {arrowDirectionValue}. Expected -1, 0, 1, 2, or 3.");
                    isValid = false;
                }

                if (cell.HasArrow && cell.CellType != LevelCellType.SourceBox)
                {
                    errors.Add($"Level '{level.name}' cell {i} at row {row}, column {column} has Arrow direction {arrowDirectionValue} but is not a SourceBox.");
                    isValid = false;
                }

                if (cell.HasKey && cell.CellType != LevelCellType.SourceBox)
                {
                    errors.Add($"Level '{level.name}' cell {i} at row {row}, column {column} has Key data but is not a SourceBox.");
                    isValid = false;
                }

                if (cell.CellType != LevelCellType.Empty && cell.CellType != LevelCellType.SourceBox &&
                    cell.CellType != LevelCellType.Spawner && cell.CellType != LevelCellType.GiftBox &&
                    cell.CellType != LevelCellType.Blocked)
                {
                    errors.Add($"Level '{level.name}' cell {i} at row {row}, column {column} has invalid cell type '{cell.CellType}'.");
                    isValid = false;
                    continue;
                }

                if (cell.CellType == LevelCellType.Empty)
                {
                    continue;
                }

                if (cell.CellType == LevelCellType.Blocked)
                {
                    ValidateBlockedCell(level, cell, i, row, column, errors, ref isValid);
                    continue;
                }

                if (cell.CellType == LevelCellType.Spawner)
                {
                    ValidateSpawnerCell(level, cell, i, row, column, rowCount, columnCount, cells, spawnerTargetIndices, sourceMarbleCounts, errors, ref isValid);
                    continue;
                }

                if (cell.CellType == LevelCellType.GiftBox)
                {
                    ValidateGiftBoxCell(level, cell, i, row, column, rowCount, columnCount, cells, sourceMarbleCounts, errors, ref isValid);
                    continue;
                }

                sourceBoxCount++;
                if (cell.HasKey)
                {
                    keySourceBoxCount++;
                }

                if (cell.HasArrow)
                {
                    if (cell.HasConnectedBoxPair)
                    {
                        errors.Add($"Level '{level.name}' Arrow Box cell {i} at row {row}, column {column} cannot also be a Connected Box.");
                        isValid = false;
                    }

                    if (cell.IsMysterySourceBox)
                    {
                        errors.Add($"Level '{level.name}' Arrow Box cell {i} at row {row}, column {column} cannot also be a Mystery SourceBox.");
                        isValid = false;
                    }

                    if (ArrowDirectionUtility.TryGetDelta(cell.ArrowDirection, out UnityEngine.Vector2Int arrowDelta))
                    {
                        int targetRow = row + arrowDelta.x;
                        int targetColumn = column + arrowDelta.y;
                        if (targetRow < 0 || targetRow >= rowCount || targetColumn < 0 || targetColumn >= columnCount)
                        {
                            errors.Add($"Level '{level.name}' Arrow Box cell {i} at row {row}, column {column} points outside the board to row {targetRow}, column {targetColumn}.");
                            isValid = false;
                        }
                        else
                        {
                            int targetIndex = targetRow * columnCount + targetColumn;
                            if (cells[targetIndex]?.CellType != LevelCellType.SourceBox)
                            {
                                errors.Add($"Level '{level.name}' Arrow Box cell {i} at row {row}, column {column} must point to a direct-neighbor SourceBox, but target row {targetRow}, column {targetColumn} is not a SourceBox.");
                                isValid = false;
                            }
                        }
                    }
                }

                if (cell.ColorId == MarbleColorId.None)
                {
                    errors.Add($"Level '{level.name}' SourceBox cell {i} at row {row}, column {column} has color '{MarbleColorId.None}'.");
                    isValid = false;
                }
                else if (!MarbleColorCatalog.IsGameplayColor(cell.ColorId))
                {
                    errors.Add($"Level '{level.name}' SourceBox cell {i} at row {row}, column {column} has invalid color '{cell.ColorId}'. Expected Blue-Grey.");
                    isValid = false;
                }

                if (cell.MarbleCount < LevelCellData.MinMarbleCount || cell.MarbleCount > LevelCellData.MaxMarbleCount)
                {
                    errors.Add($"Level '{level.name}' SourceBox cell {i} at row {row}, column {column} has marble count {cell.MarbleCount}. Expected {LevelCellData.MinMarbleCount}-{LevelCellData.MaxMarbleCount}.");
                    isValid = false;
                }
                else if (!sourceMarbleCounts.ContainsKey(cell.ColorId))
                {
                    errors.Add($"Level '{level.name}' SourceBox cell {i} at row {row}, column {column} has invalid color '{cell.ColorId}'.");
                    isValid = false;
                }
                else
                {
                    sourceMarbleCounts[cell.ColorId] += cell.MarbleCount * GetMultiplierForCell(level, row, column);
                }

            }

            if (sourceBoxCount == 0)
            {
                errors.Add($"Level '{level.name}' must contain at least one SourceBox cell.");
                isValid = false;
            }

            if (isValid && cells.Count == expectedCellCount)
            {
                ValidateConnectedBoxPairs(level, cells, rowCount, columnCount, errors, ref isValid);
            }

            if (cells.Count == expectedCellCount)
            {
                ValidateCrates(level, cells, rowCount, columnCount, errors, ref isValid);
            }

            ValidateTargetBoxes(level, targetBoxLanes, targetBoxCounts, errors, ref isValid);
            int lockedTargetBoxCount = CountLockedTargetBoxes(targetBoxLanes);
            if (keySourceBoxCount != lockedTargetBoxCount)
            {
                errors.Add($"Level '{level.name}' has {keySourceBoxCount} Key SourceBoxes but {lockedTargetBoxCount} Locked TargetBoxes. These counts must be equal.");
                isValid = false;
            }
            if (cells.Count == expectedCellCount)
            {
                ValidatePanels(level, cells, rowCount, columnCount, CountTargetBoxes(targetBoxLanes), errors, ref isValid);
                ValidateMultiplierGates(level, cells, rowCount, columnCount, errors, ref isValid);
            }
            if (validateMarbleCountBalance)
            {
                ValidateColorBalance(level, sourceMarbleCounts, targetBoxCounts, errors, ref isValid);
            }

#if UNITY_EDITOR
            if (debugSupply != null)
                foreach (var pair in sourceMarbleCounts) debugSupply[pair.Key] = pair.Value;
            if (debugRequirements != null)
                foreach (var pair in targetBoxCounts)
                    debugRequirements[pair.Key] = pair.Value * LevelDefinition.TargetBoxCapacity;
#endif

            return isValid;
        }

        private static void ValidateTargetBoxes(
            LevelDefinition level,
            IReadOnlyList<TargetBoxLaneData> targetBoxLanes,
            Dictionary<MarbleColorId, int> targetBoxCounts,
            List<string> errors,
            ref bool isValid)
        {
            Dictionary<int, int> connectedMemberCounts = new Dictionary<int, int>();
            Dictionary<int, HashSet<int>> connectedGroupLanes = new Dictionary<int, HashSet<int>>();

            if (targetBoxLanes == null)
            {
                errors.Add($"Level '{level.name}' target box lane list is null.");
                isValid = false;
                return;
            }

            if (targetBoxLanes.Count != LevelDefinition.TargetBoxLaneCount)
            {
                errors.Add($"Level '{level.name}' has {targetBoxLanes.Count} target box lanes but expected {LevelDefinition.TargetBoxLaneCount}.");
                isValid = false;
            }

            int totalTargetBoxes = 0;
            int inspectedLaneCount = Math.Min(targetBoxLanes.Count, LevelDefinition.TargetBoxLaneCount);
            for (int laneIndex = 0; laneIndex < inspectedLaneCount; laneIndex++)
            {
                TargetBoxLaneData lane = targetBoxLanes[laneIndex];
                if (lane == null)
                {
                    errors.Add($"Level '{level.name}' target box lane {laneIndex + 1} is null.");
                    isValid = false;
                    continue;
                }

                IReadOnlyList<TargetBoxData> boxes = lane.Boxes;
                if (boxes == null)
                {
                    errors.Add($"Level '{level.name}' target box lane {laneIndex + 1} box list is null.");
                    isValid = false;
                    continue;
                }

                totalTargetBoxes += boxes.Count;
                for (int boxIndex = 0; boxIndex < boxes.Count; boxIndex++)
                {
                    TargetBoxData box = boxes[boxIndex];
                    if (box == null)
                    {
                        errors.Add($"Level '{level.name}' target box lane {laneIndex + 1}, box {boxIndex + 1} is null.");
                        isValid = false;
                        continue;
                    }

                    int groupId = box.ConnectedTargetGroupId;
                    if (groupId > 0)
                    {
                        connectedMemberCounts.TryGetValue(groupId, out int memberCount);
                        connectedMemberCounts[groupId] = memberCount + 1;

                        if (!connectedGroupLanes.TryGetValue(groupId, out HashSet<int> groupLanes))
                        {
                            groupLanes = new HashSet<int>();
                            connectedGroupLanes.Add(groupId, groupLanes);
                        }

                        if (!groupLanes.Add(laneIndex))
                        {
                            errors.Add($"Level '{level.name}' Connected Target group {groupId} has more than one TargetBox in lane {laneIndex + 1}.");
                            isValid = false;
                        }
                    }

                    MarbleColorId colorId = box.ColorId;
                    if (colorId == MarbleColorId.None)
                    {
                        errors.Add($"Level '{level.name}' target box lane {laneIndex + 1}, box {boxIndex + 1} has color '{MarbleColorId.None}'.");
                        isValid = false;
                        continue;
                    }

                    if (!MarbleColorCatalog.IsGameplayColor(colorId) || !targetBoxCounts.ContainsKey(colorId))
                    {
                        errors.Add($"Level '{level.name}' target box lane {laneIndex + 1}, box {boxIndex + 1} has invalid color '{colorId}'.");
                        isValid = false;
                        continue;
                    }

                    targetBoxCounts[colorId]++;
                }
            }

            foreach (KeyValuePair<int, int> pair in connectedMemberCounts)
            {
                if (pair.Value >= 2 && pair.Value <= LevelDefinition.TargetBoxLaneCount)
                {
                    continue;
                }

                errors.Add($"Level '{level.name}' Connected Target group {pair.Key} has {pair.Value} members. Expected 2-{LevelDefinition.TargetBoxLaneCount} members in distinct lanes.");
                isValid = false;
            }

            if (totalTargetBoxes == 0)
            {
                errors.Add($"Level '{level.name}' must contain at least one Target Box.");
                isValid = false;
            }
        }

        private static void ValidateBlockedCell(
            LevelDefinition level,
            LevelCellData cell,
            int cellIndex,
            int row,
            int column,
            List<string> errors,
            ref bool isValid)
        {
            if (cell.ColorId != MarbleColorId.None || cell.IsMysterySourceBox || cell.HasConnectedBoxPair || cell.HasArrow || cell.HasKey ||
                cell.SpawnSequence.Count > 0 || cell.GiftBoxContents.Count > 0)
            {
                errors.Add($"Level '{level.name}' Blocked cell {cellIndex} at row {row}, column {column} contains SourceBox, Connected Box, Spawner, or Gift Box feature data.");
                isValid = false;
            }
        }

        private static void ValidateSpawnerCell(
            LevelDefinition level,
            LevelCellData cell,
            int cellIndex,
            int row,
            int column,
            int rowCount,
            int columnCount,
            IReadOnlyList<LevelCellData> cells,
            HashSet<int> spawnerTargetIndices,
            Dictionary<MarbleColorId, int> sourceMarbleCounts,
            List<string> errors,
            ref bool isValid)
        {
            if (!Enum.IsDefined(typeof(SpawnerDirection), cell.SpawnDirection))
            {
                errors.Add($"Level '{level.name}' Spawner cell {cellIndex} at row {row}, column {column} has invalid spawn direction '{cell.SpawnDirection}'.");
                isValid = false;
            }
            else if (!IsSupportedSpawnerDirection(cell.SpawnDirection))
            {
                errors.Add($"Level '{level.name}' Spawner cell {cellIndex} at row {row}, column {column} uses unsupported spawn direction '{cell.SpawnDirection}'. Expected Down, Left, or Right.");
                isValid = false;
            }
            else
            {
                ValidateSpawnerFacingCell(level, cell, cellIndex, row, column, rowCount, columnCount, cells, spawnerTargetIndices, errors, ref isValid);
            }

            IReadOnlyList<SpawnerSourceBoxData> sequence = cell.SpawnSequence;
            if (sequence == null)
            {
                errors.Add($"Level '{level.name}' Spawner cell {cellIndex} at row {row}, column {column} has a null spawn sequence.");
                isValid = false;
                return;
            }

            if (sequence.Count < LevelCellData.MinMarbleCount || sequence.Count > LevelCellData.MaxMarbleCount)
            {
                errors.Add($"Level '{level.name}' Spawner cell {cellIndex} at row {row}, column {column} has {sequence.Count} entries. Expected {LevelCellData.MinMarbleCount}-{LevelCellData.MaxMarbleCount}.");
                isValid = false;
            }

            for (int sequenceIndex = 0; sequenceIndex < sequence.Count; sequenceIndex++)
            {
                SpawnerSourceBoxData entry = sequence[sequenceIndex];
                if (entry == null)
                {
                    errors.Add($"Level '{level.name}' Spawner cell {cellIndex} at row {row}, column {column}, sequence {sequenceIndex + 1} is null.");
                    isValid = false;
                    continue;
                }

                if (entry.ColorId == MarbleColorId.None)
                {
                    errors.Add($"Level '{level.name}' Spawner cell {cellIndex} at row {row}, column {column}, sequence {sequenceIndex + 1} has color '{MarbleColorId.None}'.");
                    isValid = false;
                }
                else if (!MarbleColorCatalog.IsGameplayColor(entry.ColorId) || !sourceMarbleCounts.ContainsKey(entry.ColorId))
                {
                    errors.Add($"Level '{level.name}' Spawner cell {cellIndex} at row {row}, column {column}, sequence {sequenceIndex + 1} has invalid color '{entry.ColorId}'.");
                    isValid = false;
                }

                if (sourceMarbleCounts.ContainsKey(entry.ColorId))
                {
                    GetSpawnerTargetCoordinate(row, column, cell.SpawnDirection, out int targetRow, out int targetColumn);
                    sourceMarbleCounts[entry.ColorId] += LevelCellData.MaxMarbleCount *
                                                        GetMultiplierForCell(level, targetRow, targetColumn);
                }
            }
        }

        private static void ValidateGiftBoxCell(
            LevelDefinition level,
            LevelCellData cell,
            int cellIndex,
            int row,
            int column,
            int rowCount,
            int columnCount,
            IReadOnlyList<LevelCellData> cells,
            Dictionary<MarbleColorId, int> sourceMarbleCounts,
            List<string> errors,
            ref bool isValid)
        {
            IReadOnlyList<GiftBoxSourceBoxData> contents = cell.GiftBoxContents;
            int contentCount = contents?.Count ?? 0;
            if (contentCount == 0)
            {
                errors.Add($"Level '{level.name}' Gift Box cell {cellIndex} at row {row}, column {column} must contain at least one SourceBox.");
                isValid = false;
                return;
            }

            bool hasSourceBoxNeighbor = false;
            int futureGameplayCellCount = 0;
            for (int inspectedIndex = 0; inspectedIndex < cells.Count; inspectedIndex++)
            {
                if (inspectedIndex == cellIndex || cells[inspectedIndex] == null)
                {
                    continue;
                }

                int inspectedRow = inspectedIndex / columnCount;
                int inspectedColumn = inspectedIndex % columnCount;
                if (cells[inspectedIndex].CellType == LevelCellType.SourceBox &&
                    GetMultiplierForCell(level, inspectedRow, inspectedColumn) == 1)
                {
                    futureGameplayCellCount++;
                }
            }

            CheckGiftBoxNeighbor(row - 1, column, rowCount, columnCount, cells, ref hasSourceBoxNeighbor);
            CheckGiftBoxNeighbor(row + 1, column, rowCount, columnCount, cells, ref hasSourceBoxNeighbor);
            CheckGiftBoxNeighbor(row, column - 1, rowCount, columnCount, cells, ref hasSourceBoxNeighbor);
            CheckGiftBoxNeighbor(row, column + 1, rowCount, columnCount, cells, ref hasSourceBoxNeighbor);

            if (!hasSourceBoxNeighbor)
            {
                errors.Add($"Level '{level.name}' Gift Box cell {cellIndex} at row {row}, column {column} has no adjacent normal SourceBox that can activate it.");
                isValid = false;
            }

            if (futureGameplayCellCount < contents.Count)
            {
                errors.Add($"Level '{level.name}' Gift Box cell {cellIndex} at row {row}, column {column} can never have {contents.Count} vacated gameplay cells for its contents.");
                isValid = false;
            }

            for (int contentIndex = 0; contentIndex < contents.Count; contentIndex++)
            {
                GiftBoxSourceBoxData content = contents[contentIndex];
                if (content == null)
                {
                    errors.Add($"Level '{level.name}' Gift Box cell {cellIndex} content {contentIndex + 1} is null.");
                    isValid = false;
                    continue;
                }

                if (!MarbleColorCatalog.IsGameplayColor(content.ColorId) || !sourceMarbleCounts.ContainsKey(content.ColorId))
                {
                    errors.Add($"Level '{level.name}' Gift Box cell {cellIndex} content {contentIndex + 1} has invalid color '{content.ColorId}'.");
                    isValid = false;
                }
                if (content.MarbleCount < LevelCellData.MinMarbleCount || content.MarbleCount > LevelCellData.MaxMarbleCount)
                {
                    errors.Add($"Level '{level.name}' Gift Box cell {cellIndex} content {contentIndex + 1} has marble count {content.MarbleCount}. Expected {LevelCellData.MinMarbleCount}-{LevelCellData.MaxMarbleCount}.");
                    isValid = false;
                }
                else if (MarbleColorCatalog.IsGameplayColor(content.ColorId) && sourceMarbleCounts.ContainsKey(content.ColorId))
                {
                    sourceMarbleCounts[content.ColorId] += content.MarbleCount;
                }
            }
        }

        private static void CheckGiftBoxNeighbor(
            int row,
            int column,
            int rowCount,
            int columnCount,
            IReadOnlyList<LevelCellData> cells,
            ref bool hasSourceBoxNeighbor)
        {
            if (row < 0 || row >= rowCount || column < 0 || column >= columnCount)
            {
                return;
            }

            int index = row * columnCount + column;
            hasSourceBoxNeighbor |= cells[index] != null && cells[index].CellType == LevelCellType.SourceBox;
        }

        private static void ValidateSpawnerFacingCell(
            LevelDefinition level,
            LevelCellData cell,
            int cellIndex,
            int row,
            int column,
            int rowCount,
            int columnCount,
            IReadOnlyList<LevelCellData> cells,
            HashSet<int> spawnerTargetIndices,
            List<string> errors,
            ref bool isValid)
        {
            int targetRow = row;
            int targetColumn = column;

            switch (cell.SpawnDirection)
            {
                case SpawnerDirection.Up:
                    targetRow++;
                    break;
                case SpawnerDirection.Right:
                    targetColumn++;
                    break;
                case SpawnerDirection.Down:
                    targetRow--;
                    break;
                case SpawnerDirection.Left:
                    targetColumn--;
                    break;
            }

            if (targetRow < 0 || targetRow >= rowCount || targetColumn < 0 || targetColumn >= columnCount)
            {
                errors.Add($"Level '{level.name}' Spawner cell {cellIndex} at row {row}, column {column} points outside the board.");
                isValid = false;
                return;
            }

            int targetIndex = targetRow * columnCount + targetColumn;
            if (targetIndex < 0 || targetIndex >= cells.Count || cells[targetIndex] == null || cells[targetIndex].CellType != LevelCellType.SourceBox)
            {
                errors.Add($"Level '{level.name}' Spawner cell {cellIndex} at row {row}, column {column} must face a SourceBox at row {targetRow}, column {targetColumn}.");
                isValid = false;
                return;
            }

            if (!spawnerTargetIndices.Add(targetIndex))
            {
                errors.Add($"Level '{level.name}' has more than one Spawner targeting row {targetRow}, column {targetColumn}.");
                isValid = false;
            }
        }

        private static bool IsSupportedSpawnerDirection(SpawnerDirection direction)
        {
            return direction == SpawnerDirection.Down ||
                   direction == SpawnerDirection.Left ||
                   direction == SpawnerDirection.Right;
        }

        private static void ValidateColorBalance(
            LevelDefinition level,
            Dictionary<MarbleColorId, int> sourceMarbleCounts,
            Dictionary<MarbleColorId, int> targetBoxCounts,
            List<string> errors,
            ref bool isValid)
        {
            foreach (KeyValuePair<MarbleColorId, int> sourceCount in sourceMarbleCounts)
            {
                MarbleColorId colorId = sourceCount.Key;
                int sourceMarbleCount = sourceCount.Value;
                int targetRequirement = targetBoxCounts[colorId] * LevelDefinition.TargetBoxCapacity;

                if (sourceMarbleCount != targetRequirement)
                {
                    errors.Add($"{colorId} marble count mismatch in level '{level.name}'. Source: {sourceMarbleCount}, Target requirement: {targetRequirement}.");
                    isValid = false;
                }
            }
        }

        private static void ValidateConnectedBoxPairs(
            LevelDefinition level,
            IReadOnlyList<LevelCellData> cells,
            int rowCount,
            int columnCount,
            List<string> errors,
            ref bool isValid)
        {
            Dictionary<int, List<int>> cellIndicesByPairId = new Dictionary<int, List<int>>();

            for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
            {
                LevelCellData cell = cells[cellIndex];
                if (cell == null || !cell.HasConnectedBoxPair)
                {
                    continue;
                }

                int row = cellIndex / columnCount;
                int column = cellIndex % columnCount;
                if (cell.CellType != LevelCellType.SourceBox)
                {
                    errors.Add($"Level '{level.name}' cell {cellIndex} at row {row}, column {column} has Connected Box pair id {cell.ConnectedBoxPairId} but is not a SourceBox.");
                    isValid = false;
                    continue;
                }

                if (cell.IsMysterySourceBox)
                {
                    errors.Add($"Level '{level.name}' Connected Box pair id {cell.ConnectedBoxPairId} includes mystery SourceBox cell {cellIndex} at row {row}, column {column}. Mystery Connected Boxes are not supported.");
                    isValid = false;
                }

                if (!cellIndicesByPairId.TryGetValue(cell.ConnectedBoxPairId, out List<int> pairCells))
                {
                    pairCells = new List<int>(2);
                    cellIndicesByPairId.Add(cell.ConnectedBoxPairId, pairCells);
                }

                pairCells.Add(cellIndex);
            }

            foreach (KeyValuePair<int, List<int>> pair in cellIndicesByPairId)
            {
                List<int> pairCells = pair.Value;
                if (pairCells.Count != 2)
                {
                    errors.Add($"Level '{level.name}' Connected Box pair id {pair.Key} must contain exactly 2 SourceBoxes, but contains {pairCells.Count}.");
                    isValid = false;
                    continue;
                }

                int firstIndex = pairCells[0];
                int secondIndex = pairCells[1];
                int firstRow = firstIndex / columnCount;
                int firstColumn = firstIndex % columnCount;
                int secondRow = secondIndex / columnCount;
                int secondColumn = secondIndex % columnCount;
                int manhattanDistance = Math.Abs(firstRow - secondRow) + Math.Abs(firstColumn - secondColumn);
                if (manhattanDistance != 1)
                {
                    errors.Add($"Level '{level.name}' Connected Box pair id {pair.Key} cells {firstIndex} and {secondIndex} must be orthogonally adjacent. Diagonal and non-adjacent pairs are not supported.");
                    isValid = false;
                }
            }
        }

        private static void ValidateCrates(
            LevelDefinition level,
            IReadOnlyList<LevelCellData> cells,
            int rowCount,
            int columnCount,
            List<string> errors,
            ref bool isValid)
        {
            IReadOnlyList<CrateData> crates = level.Crates;
            if (crates == null || crates.Count == 0)
            {
                return;
            }

            HashSet<int> coveredCellIndices = new HashSet<int>();
            for (int crateIndex = 0; crateIndex < crates.Count; crateIndex++)
            {
                CrateData crate = crates[crateIndex];
                if (crate == null)
                {
                    errors.Add($"Level '{level.name}' Crate {crateIndex} is null.");
                    isValid = false;
                    continue;
                }

                int row = crate.Row;
                int column = crate.Col;
                if (row < 0 || row >= rowCount - 1 || column < 0 || column >= columnCount - 1)
                {
                    errors.Add($"Level '{level.name}' Crate {crateIndex} at row {row}, column {column} has a 2x2 footprint outside the {rowCount}x{columnCount} board.");
                    isValid = false;
                    continue;
                }

                int[] footprintIndices =
                {
                    row * columnCount + column,
                    row * columnCount + column + 1,
                    (row + 1) * columnCount + column,
                    (row + 1) * columnCount + column + 1
                };

                for (int footprintIndex = 0; footprintIndex < footprintIndices.Length; footprintIndex++)
                {
                    int cellIndex = footprintIndices[footprintIndex];
                    int cellRow = cellIndex / columnCount;
                    int cellColumn = cellIndex % columnCount;
                    if (!coveredCellIndices.Add(cellIndex))
                    {
                        errors.Add($"Level '{level.name}' Crate {crateIndex} at row {row}, column {column} overlaps another Crate at covered cell row {cellRow}, column {cellColumn}.");
                        isValid = false;
                    }

                    LevelCellData cell = cells[cellIndex];
                    if (cell == null || cell.CellType != LevelCellType.SourceBox)
                    {
                        errors.Add($"Level '{level.name}' Crate {crateIndex} at row {row}, column {column} must cover a SourceBox at row {cellRow}, column {cellColumn}.");
                        isValid = false;
                        continue;
                    }

                    if (cell.IsMysterySourceBox || cell.HasConnectedBoxPair || cell.HasArrow ||
                        cell.SpawnSequence.Count > 0 || cell.GiftBoxContents.Count > 0)
                    {
                        errors.Add($"Level '{level.name}' Crate {crateIndex} at row {row}, column {column} covers special SourceBox data at row {cellRow}, column {cellColumn}. Crates currently support plain SourceBoxes only.");
                        isValid = false;
                    }
                }

                int perimeterSourceCount = CountCratePerimeterSourceBoxes(cells, rowCount, columnCount, row, column);
                if (perimeterSourceCount < 3)
                {
                    errors.Add($"Level '{level.name}' Crate {crateIndex} at row {row}, column {column} has only {perimeterSourceCount} SourceBox cell(s) on its orthogonal perimeter. At least 3 are required.");
                    isValid = false;
                }
            }
        }

        private static int CountCratePerimeterSourceBoxes(
            IReadOnlyList<LevelCellData> cells,
            int rowCount,
            int columnCount,
            int row,
            int column)
        {
            int[,] coordinates =
            {
                { row - 1, column },
                { row - 1, column + 1 },
                { row + 2, column },
                { row + 2, column + 1 },
                { row, column - 1 },
                { row + 1, column - 1 },
                { row, column + 2 },
                { row + 1, column + 2 }
            };

            int sourceCount = 0;
            for (int i = 0; i < coordinates.GetLength(0); i++)
            {
                int candidateRow = coordinates[i, 0];
                int candidateColumn = coordinates[i, 1];
                if (candidateRow < 0 || candidateRow >= rowCount || candidateColumn < 0 || candidateColumn >= columnCount)
                {
                    continue;
                }

                int cellIndex = candidateRow * columnCount + candidateColumn;
                if (cells[cellIndex]?.CellType == LevelCellType.SourceBox)
                {
                    sourceCount++;
                }
            }

            return sourceCount;
        }

        private static void ValidatePanels(
            LevelDefinition level,
            IReadOnlyList<LevelCellData> cells,
            int rowCount,
            int columnCount,
            int targetBoxCount,
            List<string> errors,
            ref bool isValid)
        {
            IReadOnlyList<PanelData> panels = level.Panels;
            if (panels == null || panels.Count == 0)
            {
                return;
            }

            HashSet<int> crateFootprint = new HashSet<int>();
            IReadOnlyList<CrateData> crates = level.Crates;
            for (int crateIndex = 0; crateIndex < crates.Count; crateIndex++)
            {
                CrateData crate = crates[crateIndex];
                if (crate == null || crate.Row < 0 || crate.Row >= rowCount - 1 || crate.Col < 0 || crate.Col >= columnCount - 1)
                {
                    continue;
                }

                for (int rowOffset = 0; rowOffset < 2; rowOffset++)
                {
                    for (int columnOffset = 0; columnOffset < 2; columnOffset++)
                    {
                        crateFootprint.Add((crate.Row + rowOffset) * columnCount + crate.Col + columnOffset);
                    }
                }
            }

            HashSet<int> panelFootprint = new HashSet<int>();
            for (int panelIndex = 0; panelIndex < panels.Count; panelIndex++)
            {
                PanelData panel = panels[panelIndex];
                if (panel == null)
                {
                    errors.Add($"Level '{level.name}' Panel {panelIndex} is null.");
                    isValid = false;
                    continue;
                }

                int row = panel.Row;
                int column = panel.Col;
                if (row < 0 || row >= rowCount - 2 || column < 0 || column >= columnCount - 2)
                {
                    errors.Add($"Level '{level.name}' Panel {panelIndex} at row {row}, column {column} has a 3x3 footprint outside the {rowCount}x{columnCount} board.");
                    isValid = false;
                    continue;
                }

                if (panel.Number < 1)
                {
                    errors.Add($"Level '{level.name}' Panel {panelIndex} at row {row}, column {column} has number {panel.Number}. Expected at least 1.");
                    isValid = false;
                }
                else if (panel.Number > targetBoxCount)
                {
                    errors.Add($"Level '{level.name}' Panel {panelIndex} at row {row}, column {column} has number {panel.Number}, but the level contains only {targetBoxCount} TargetBoxes.");
                    isValid = false;
                }

                for (int rowOffset = 0; rowOffset < 3; rowOffset++)
                {
                    for (int columnOffset = 0; columnOffset < 3; columnOffset++)
                    {
                        int cellRow = row + rowOffset;
                        int cellColumn = column + columnOffset;
                        int cellIndex = cellRow * columnCount + cellColumn;
                        if (!panelFootprint.Add(cellIndex))
                        {
                            errors.Add($"Level '{level.name}' Panel {panelIndex} at row {row}, column {column} overlaps another Panel at covered cell row {cellRow}, column {cellColumn}.");
                            isValid = false;
                        }

                        if (crateFootprint.Contains(cellIndex))
                        {
                            errors.Add($"Level '{level.name}' Panel {panelIndex} at row {row}, column {column} overlaps a Crate at covered cell row {cellRow}, column {cellColumn}.");
                            isValid = false;
                        }

                        LevelCellData cell = cells[cellIndex];
                        if (cell == null || cell.CellType != LevelCellType.SourceBox)
                        {
                            errors.Add($"Level '{level.name}' Panel {panelIndex} at row {row}, column {column} must cover a SourceBox at row {cellRow}, column {cellColumn}.");
                            isValid = false;
                            continue;
                        }

                        if (cell.IsMysterySourceBox || cell.HasConnectedBoxPair || cell.HasArrow ||
                            cell.SpawnSequence.Count > 0 || cell.GiftBoxContents.Count > 0)
                        {
                            errors.Add($"Level '{level.name}' Panel {panelIndex} at row {row}, column {column} covers special SourceBox data at row {cellRow}, column {cellColumn}. Panels currently support plain SourceBoxes only.");
                            isValid = false;
                        }
                    }
                }
            }
        }

        private static int CountTargetBoxes(IReadOnlyList<TargetBoxLaneData> lanes)
        {
            if (lanes == null)
            {
                return 0;
            }

            int count = 0;
            for (int laneIndex = 0; laneIndex < lanes.Count; laneIndex++)
            {
                IReadOnlyList<TargetBoxData> boxes = lanes[laneIndex]?.Boxes;
                if (boxes == null)
                {
                    continue;
                }

                for (int boxIndex = 0; boxIndex < boxes.Count; boxIndex++)
                {
                    if (boxes[boxIndex] != null)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static int CountLockedTargetBoxes(IReadOnlyList<TargetBoxLaneData> lanes)
        {
            if (lanes == null)
            {
                return 0;
            }

            int count = 0;
            for (int laneIndex = 0; laneIndex < lanes.Count; laneIndex++)
            {
                IReadOnlyList<TargetBoxData> boxes = lanes[laneIndex]?.Boxes;
                if (boxes == null)
                {
                    continue;
                }

                for (int boxIndex = 0; boxIndex < boxes.Count; boxIndex++)
                {
                    if (boxes[boxIndex]?.IsLocked == true)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static void ValidateMultiplierGates(
            LevelDefinition level,
            IReadOnlyList<LevelCellData> cells,
            int rowCount,
            int columnCount,
            List<string> errors,
            ref bool isValid)
        {
            IReadOnlyList<MultiplierGateData> gates = level.Gates;
            HashSet<Vector2Int> claimedFootprint = new HashSet<Vector2Int>();
            HashSet<int> claimedColumns = new HashSet<int>();
            HashSet<Vector2Int> crateFootprint = CollectCrateFootprint(level.Crates);
            HashSet<Vector2Int> panelFootprint = CollectPanelFootprint(level.Panels);

            for (int gateIndex = 0; gateIndex < gates.Count; gateIndex++)
            {
                MultiplierGateData gate = gates[gateIndex];
                if (gate == null)
                {
                    errors.Add($"Level '{level.name}' Multiplier Gate {gateIndex} is null.");
                    isValid = false;
                    continue;
                }

                if (gate.Multiplier != 2)
                {
                    errors.Add($"Level '{level.name}' Multiplier Gate {gateIndex} has multiplier {gate.Multiplier}. Only x2 is supported.");
                    isValid = false;
                }

                if (gate.Row < 0 || gate.Row >= rowCount || gate.Col < 0 || gate.Col >= columnCount - 1)
                {
                    errors.Add($"Level '{level.name}' Multiplier Gate {gateIndex} at row {gate.Row}, column {gate.Col} has a horizontal 2-cell footprint outside the board.");
                    isValid = false;
                    continue;
                }

                for (int offset = 0; offset < 2; offset++)
                {
                    Vector2Int coordinate = new Vector2Int(gate.Row, gate.Col + offset);
                    int cellIndex = coordinate.x * columnCount + coordinate.y;
                    if (cells[cellIndex]?.CellType != LevelCellType.Empty)
                    {
                        errors.Add($"Level '{level.name}' Multiplier Gate {gateIndex} footprint cell row {coordinate.x}, column {coordinate.y} must be Empty.");
                        isValid = false;
                    }

                    if (!claimedFootprint.Add(coordinate))
                    {
                        errors.Add($"Level '{level.name}' Multiplier Gate {gateIndex} overlaps another Gate at row {coordinate.x}, column {coordinate.y}.");
                        isValid = false;
                    }

                    if (crateFootprint.Contains(coordinate) || panelFootprint.Contains(coordinate))
                    {
                        errors.Add($"Level '{level.name}' Multiplier Gate {gateIndex} overlaps a sealed board feature at row {coordinate.x}, column {coordinate.y}.");
                        isValid = false;
                    }
                }

                bool leftColumnAvailable = claimedColumns.Add(gate.Col);
                bool rightColumnAvailable = claimedColumns.Add(gate.Col + 1);
                if (!leftColumnAvailable || !rightColumnAvailable)
                {
                    errors.Add($"Level '{level.name}' Multiplier Gate {gateIndex} shares a multiplier column with another Gate.");
                    isValid = false;
                }
            }
        }

        private static int GetMultiplierForCell(LevelDefinition level, int row, int column)
        {
            if (level?.Gates == null)
            {
                return 1;
            }

            for (int i = 0; i < level.Gates.Count; i++)
            {
                MultiplierGateData gate = level.Gates[i];
                if (gate != null && row > gate.Row && (column == gate.Col || column == gate.Col + 1))
                {
                    return gate.Multiplier == 2 ? 2 : 1;
                }
            }

            return 1;
        }

        private static void GetSpawnerTargetCoordinate(
            int row,
            int column,
            SpawnerDirection direction,
            out int targetRow,
            out int targetColumn)
        {
            targetRow = row;
            targetColumn = column;
            switch (direction)
            {
                case SpawnerDirection.Up: targetRow++; break;
                case SpawnerDirection.Right: targetColumn++; break;
                case SpawnerDirection.Down: targetRow--; break;
                case SpawnerDirection.Left: targetColumn--; break;
            }
        }

        private static HashSet<Vector2Int> CollectCrateFootprint(IReadOnlyList<CrateData> crates)
        {
            HashSet<Vector2Int> result = new HashSet<Vector2Int>();
            if (crates == null) return result;
            for (int i = 0; i < crates.Count; i++)
            {
                CrateData crate = crates[i];
                if (crate == null) continue;
                for (int rowOffset = 0; rowOffset < 2; rowOffset++)
                for (int colOffset = 0; colOffset < 2; colOffset++)
                    result.Add(new Vector2Int(crate.Row + rowOffset, crate.Col + colOffset));
            }
            return result;
        }

        private static HashSet<Vector2Int> CollectPanelFootprint(IReadOnlyList<PanelData> panels)
        {
            HashSet<Vector2Int> result = new HashSet<Vector2Int>();
            if (panels == null) return result;
            for (int i = 0; i < panels.Count; i++)
            {
                PanelData panel = panels[i];
                if (panel == null) continue;
                for (int rowOffset = 0; rowOffset < 3; rowOffset++)
                for (int colOffset = 0; colOffset < 3; colOffset++)
                    result.Add(new Vector2Int(panel.Row + rowOffset, panel.Col + colOffset));
            }
            return result;
        }

        private static Dictionary<MarbleColorId, int> CreateColorCountMap()
        {
            Dictionary<MarbleColorId, int> counts = new Dictionary<MarbleColorId, int>();
            for (int colorValue = (int)MarbleColorId.Blue; colorValue <= (int)MarbleColorId.Grey; colorValue++)
            {
                counts.Add((MarbleColorId)colorValue, 0);
            }

            return counts;
        }
    }
}
