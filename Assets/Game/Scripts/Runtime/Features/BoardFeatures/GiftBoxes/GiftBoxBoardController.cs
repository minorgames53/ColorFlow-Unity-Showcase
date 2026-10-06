using System;
using System.Collections.Generic;
using Gameplay.Levels;
using Gameplay.SourceBoxes;
using UnityEngine;

namespace Gameplay.BoardFeatures.GiftBoxes
{
    /// <summary>Owns Gift Box runtime objects and their deterministic resolution queue.</summary>
    public sealed class GiftBoxBoardController
    {
        private readonly List<GiftBoxController> activeGiftBoxes = new List<GiftBoxController>();
        private readonly List<Resolution> resolutionQueue = new List<Resolution>();
        private readonly Dictionary<Vector2Int, List<GiftBoxController>> pendingGiftBoxesBySourceCell = new Dictionary<Vector2Int, List<GiftBoxController>>();
        private SourceBoxBoardController boardController;
        private LevelDefinition level;
        private bool resolving;

        public bool HasActiveGiftBoxes => activeGiftBoxes.Count > 0;
        public bool IsResolving => resolving;

        private sealed class Resolution
        {
            public GiftBoxController GiftBox;
            public List<SourceBox> SpawnedSourceBoxes;
        }

        public bool Build(LevelDefinition levelDefinition, SourceBoxBoardController sourceBoxBoardController)
        {
            Clear();
            level = levelDefinition;
            boardController = sourceBoxBoardController;

            if (level == null || boardController == null)
            {
                return false;
            }

            for (int cellIndex = 0; cellIndex < level.CellCount; cellIndex++)
            {
                LevelCellData cell = level.Cells[cellIndex];
                if (cell == null || cell.CellType != LevelCellType.GiftBox)
                {
                    continue;
                }

                if (!boardController.TryCreateGiftBox(cellIndex, out GiftBoxController giftBox))
                {
                    Clear();
                    return false;
                }

                activeGiftBoxes.Add(giftBox);
            }

            activeGiftBoxes.Sort(CompareGiftBoxesByCellIndex);
            return true;
        }

        public void NotifySourceBoxActivated(Vector2Int sourceCoordinate)
        {
            if (resolving || level == null || boardController == null)
            {
                return;
            }

            List<GiftBoxController> adjacentGiftBoxes = CollectAdjacentGiftBoxes(sourceCoordinate);
            if (adjacentGiftBoxes.Count == 0)
            {
                return;
            }

            pendingGiftBoxesBySourceCell[sourceCoordinate] = adjacentGiftBoxes;
            resolving = true;
            boardController.SetSourceBoxInteractionsBlocked(true);
        }

        public bool NotifySourceBoxReleased(Vector2Int sourceCoordinate)
        {
            if (!pendingGiftBoxesBySourceCell.TryGetValue(sourceCoordinate, out List<GiftBoxController> pendingGiftBoxes))
            {
                return false;
            }

            pendingGiftBoxesBySourceCell.Remove(sourceCoordinate);
            for (int i = 0; i < pendingGiftBoxes.Count; i++)
            {
                GiftBoxController giftBox = pendingGiftBoxes[i];
                if (giftBox == null || !TryPrepareResolution(giftBox, out List<SourceBox> spawnedSourceBoxes))
                {
                    continue;
                }

                resolutionQueue.Add(new Resolution
                {
                    GiftBox = giftBox,
                    SpawnedSourceBoxes = spawnedSourceBoxes
                });
            }

            bool hasPreparedResolution = resolutionQueue.Count > 0;
            PlayNextResolution();
            return hasPreparedResolution;
        }

        public bool IsGiftBoxCell(Vector2Int coordinate)
        {
            for (int i = 0; i < activeGiftBoxes.Count; i++)
            {
                GiftBoxController giftBox = activeGiftBoxes[i];
                if (giftBox != null && giftBox.Coordinate == coordinate)
                {
                    return true;
                }
            }

            return false;
        }

        public void Clear()
        {
            resolving = false;
            resolutionQueue.Clear();
            pendingGiftBoxesBySourceCell.Clear();

            for (int i = 0; i < activeGiftBoxes.Count; i++)
            {
                GiftBoxController giftBox = activeGiftBoxes[i];
                if (giftBox != null)
                {
                    boardController?.DestroyRuntimeBoardObject(giftBox.gameObject);
                }
            }

            activeGiftBoxes.Clear();
            boardController?.SetSourceBoxInteractionsBlocked(false);
            boardController = null;
            level = null;
        }

        private List<GiftBoxController> CollectAdjacentGiftBoxes(Vector2Int sourceCoordinate)
        {
            List<GiftBoxController> result = new List<GiftBoxController>();
            for (int i = 0; i < activeGiftBoxes.Count; i++)
            {
                GiftBoxController giftBox = activeGiftBoxes[i];
                if (giftBox == null)
                {
                    continue;
                }

                int distance = Mathf.Abs(giftBox.Coordinate.x - sourceCoordinate.x) + Mathf.Abs(giftBox.Coordinate.y - sourceCoordinate.y);
                if (distance == 1)
                {
                    result.Add(giftBox);
                }
            }

            result.Sort(CompareGiftBoxesByCellIndex);
            return result;
        }

        private bool TryPrepareResolution(GiftBoxController giftBox, out List<SourceBox> spawnedSourceBoxes)
        {
            spawnedSourceBoxes = null;
            LevelCellData giftCell = level.Cells[giftBox.CellIndex];
            IReadOnlyList<GiftBoxSourceBoxData> contents = giftCell.GiftBoxContents;
            int requestedCount = contents?.Count ?? 0;
            if (requestedCount == 0)
            {
                Debug.LogWarning($"{nameof(GiftBoxBoardController)} on '{boardController.name}' cannot resolve Gift Box at cell {giftBox.CellIndex}: at least one content entry is required.", boardController);
                return false;
            }

            if (!boardController.TryFindGiftBoxSpawnCells(giftBox.Coordinate, requestedCount, this, out List<Vector2Int> targetCells))
            {
                Debug.LogWarning($"{nameof(GiftBoxBoardController)} on '{boardController.name}' could not resolve Gift Box at cell {giftBox.CellIndex}: {requestedCount} usable empty cells are required. The Gift Box was left untouched.", boardController);
                return false;
            }

            List<SourceBox> prepared = new List<SourceBox>(contents.Count);
            for (int i = 0; i < contents.Count; i++)
            {
                if (!boardController.TrySpawnGiftBoxSourceBox(targetCells[i], contents[i], out SourceBox sourceBox))
                {
                    for (int spawnedIndex = 0; spawnedIndex < prepared.Count; spawnedIndex++)
                    {
                        boardController.RemoveRuntimeSourceBox(prepared[spawnedIndex]);
                    }

                    boardController.RecomputeAfterGiftBoxRollback();

                    Debug.LogWarning($"{nameof(GiftBoxBoardController)} on '{boardController.name}' could not reserve all Gift Box spawn cells at cell {giftBox.CellIndex}. The Gift Box was left untouched.", boardController);
                    return false;
                }

                prepared.Add(sourceBox);
            }

            spawnedSourceBoxes = prepared;
            return true;
        }

        private void PlayNextResolution()
        {
            if (resolutionQueue.Count == 0)
            {
                resolving = false;
                boardController?.SetSourceBoxInteractionsBlocked(false);
                boardController?.RefreshSourceBoxInputStatesAfterBoardChange();
                return;
            }

            Resolution resolution = resolutionQueue[0];
            resolutionQueue.RemoveAt(0);
            if (resolution.GiftBox == null)
            {
                RollBackPreparedResolution(resolution);
                PlayNextResolution();
                return;
            }

            if (!resolution.GiftBox.PlayResolve(
                    resolution.SpawnedSourceBoxes,
                    () => boardController.FinalizeGiftSourceBoxAvailability(resolution.SpawnedSourceBoxes),
                    () => OnResolutionAnimationCompleted(resolution.GiftBox)))
            {
                Debug.LogWarning($"{nameof(GiftBoxBoardController)} on '{boardController.name}' could not play Gift Box animation at cell {resolution.GiftBox.CellIndex}. The Gift Box was left untouched.", boardController);
                RollBackPreparedResolution(resolution);
                PlayNextResolution();
            }
        }

        private void OnResolutionAnimationCompleted(GiftBoxController giftBox)
        {
            if (giftBox != null)
            {
                activeGiftBoxes.Remove(giftBox);
                boardController?.DestroyRuntimeBoardObject(giftBox.gameObject);
                boardController?.NotifyRuntimeOccupancyChanged();
            }

            PlayNextResolution();
        }

        private void RollBackPreparedResolution(Resolution resolution)
        {
            if (resolution?.SpawnedSourceBoxes == null)
            {
                return;
            }

            for (int i = 0; i < resolution.SpawnedSourceBoxes.Count; i++)
            {
                boardController?.RemoveRuntimeSourceBox(resolution.SpawnedSourceBoxes[i]);
            }

            boardController?.RecomputeAfterGiftBoxRollback();
        }

        private static int CompareGiftBoxesByCellIndex(GiftBoxController left, GiftBoxController right)
        {
            int leftIndex = left != null ? left.CellIndex : int.MaxValue;
            int rightIndex = right != null ? right.CellIndex : int.MaxValue;
            return leftIndex.CompareTo(rightIndex);
        }
    }
}
