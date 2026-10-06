using System.Collections.Generic;
using Gameplay.BoardTiles;
using Gameplay.BoardFeatures;
using Gameplay.BoardFeatures.ConnectedBoxes;
using Gameplay.BoardFeatures.Crates;
using Gameplay.BoardFeatures.GiftBoxes;
using Gameplay.BoardFeatures.Panels;
using Gameplay.BoardFeatures.ArrowBoxes;
using Gameplay.BoardFeatures.MultiplierGates;
using Gameplay.Conveyor;
using Gameplay.Levels;
using Gameplay.Spawners;
using UnityEngine;
#if UNITY_EDITOR
using Gameplay.MarbleDebug;
#endif

namespace Gameplay.SourceBoxes
{
    public sealed class SourceBoxBoardController : MonoBehaviour
    {
        private const int InitialConveyorMarbleVisualSortingOrder = 2;
        private const int InitialConveyorMarbleOutlineSortingOrder = 0;
        private const float MultiplierGateDuplicateSeparationSafetyMultiplier = 1.05f;

        private static readonly Vector2Int[] OrthogonalNeighborOffsets =
        {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

        [SerializeField] private BoardFeatureCatalog boardFeatureCatalog;
        [SerializeField] private MarbleColorCatalog colorCatalog;
        [SerializeField] private Transform releasedMarbleContainer;
        [SerializeField] private MarbleCapacityController capacityController;
        [SerializeField] private SourceBoxPlaceholderGridController placeholderGridController;
        [SerializeField] private BoardTileController boardTileController;

        private readonly List<SourceBox> spawnedSourceBoxes = new List<SourceBox>();
        private readonly List<SourceBox> recoveredSourceBoxes = new List<SourceBox>();
        private readonly List<SourceBoxSpawner> spawnedSpawners = new List<SourceBoxSpawner>();
        private readonly Dictionary<Vector2Int, SourceBox> activeSourceBoxes = new Dictionary<Vector2Int, SourceBox>();
        private readonly HashSet<Vector2Int> activeGameplayCells = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> initialEntrySourceCells = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> vacatedGameplayCells = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> progressionSuppressedReleaseCells = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> pendingSpawnerReplacementCells = new HashSet<Vector2Int>();
        private readonly HashSet<SourceBox> progressionGrantedSourceBoxes = new HashSet<SourceBox>();
        private readonly HashSet<SourceBox> giftSourceBoxesAwaitingAvailability = new HashSet<SourceBox>();
        private readonly HashSet<SourceBox> sealedSourceBoxesAwaitingAvailability = new HashSet<SourceBox>();
        private readonly List<SourceBoxSpawner> activeSpawners = new List<SourceBoxSpawner>();
        private readonly List<string> validationErrors = new List<string>();
        private readonly ConnectedBoxBoardController connectedBoxBoardController = new ConnectedBoxBoardController();
        private readonly MultiplierGateBoardController multiplierGateBoardController = new MultiplierGateBoardController();
        private readonly GiftBoxBoardController giftBoxBoardController = new GiftBoxBoardController();
        private readonly CrateBoardController crateBoardController = new CrateBoardController();
        private readonly PanelBoardController panelBoardController = new PanelBoardController();
        private readonly ArrowBoxBoardController arrowBoxBoardController = new ArrowBoxBoardController();
        private LevelDefinition currentLevel;
        private SourceBox[] sourceBoxesByCell;

        public LevelDefinition CurrentLevel => currentLevel;
        public IReadOnlyList<SourceBox> SpawnedSourceBoxes => spawnedSourceBoxes;
        public IReadOnlyList<SourceBoxSpawner> SpawnedSpawners => spawnedSpawners;
        public BoardFeatureCatalog FeatureCatalog => boardFeatureCatalog;
        public MarbleColorCatalog ColorCatalog => colorCatalog;
        public Transform ReleasedMarbleContainer => releasedMarbleContainer;
        public MarbleCapacityController CapacityController => capacityController;
        public SourceBoxPlaceholderGridController PlaceholderGridController => placeholderGridController;
        public event System.Action<SourceBox> SourceBoxSpawned;
        public bool HasBuiltBoard => currentLevel != null;
        public bool HasActiveGiftBoxes => giftBoxBoardController.HasActiveGiftBoxes;
        public bool HasSealedCrateSources => crateBoardController.HasSealedCells;
        public bool HasSealedSources => crateBoardController.HasSealedCells || panelBoardController.HasSealedCells;
        public bool IsGiftBoxResolving => giftBoxBoardController.IsResolving;
        public bool HasPendingSpawnerSourceBoxes
        {
            get
            {
                for (int i = 0; i < activeSpawners.Count; i++)
                {
                    SourceBoxSpawner spawner = activeSpawners[i];
                    if (spawner != null && spawner.RemainingCount > 0)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private void OnEnable()
        {
            SubscribeToCapacityChanges();
        }

        private void OnDisable()
        {
            UnsubscribeFromCapacityChanges();
        }

        public void BuildBoard(LevelDefinition levelDefinition)
        {
            if (!ValidateBuildInput(levelDefinition))
            {
                return;
            }

            ClearBoard();

            currentLevel = levelDefinition;
            sourceBoxesByCell = new SourceBox[levelDefinition.CellCount];
            arrowBoxBoardController.Prepare(levelDefinition);
            crateBoardController.Prepare(levelDefinition);
            panelBoardController.Prepare(levelDefinition);
            multiplierGateBoardController.Prepare(levelDefinition);
            InitializeGameplayCellTopology(levelDefinition);

            if (!placeholderGridController.BuildGrid(levelDefinition.RowCount, levelDefinition.ColumnCount))
            {
                ClearBoard();
                return;
            }

            placeholderGridController.ApplyLevelCellStates(levelDefinition);
            if (!boardTileController.Build(levelDefinition, multiplierGateBoardController))
            {
                ClearBoard();
                return;
            }

            if (!multiplierGateBoardController.BuildViews(this, boardFeatureCatalog, placeholderGridController))
            {
                ClearBoard();
                return;
            }

            BuildSourceBoxes(levelDefinition);
            if (!arrowBoxBoardController.BuildViews(levelDefinition, this, boardFeatureCatalog, colorCatalog))
            {
                ClearBoard();
                return;
            }

            if (!crateBoardController.BuildViews(this, boardFeatureCatalog, placeholderGridController))
            {
                ClearBoard();
                return;
            }

            if (!panelBoardController.BuildViews(this, boardFeatureCatalog, placeholderGridController))
            {
                ClearBoard();
                return;
            }

            if (!connectedBoxBoardController.Build(levelDefinition, this, boardFeatureCatalog, colorCatalog, placeholderGridController.GeneratedGridRoot))
            {
                ClearBoard();
                return;
            }

            BuildSpawners(levelDefinition);
            if (!giftBoxBoardController.Build(levelDefinition, this))
            {
                ClearBoard();
                return;
            }

            RecomputeSourceBoxAvailability(false);
        }

        public bool CanBuild(LevelDefinition levelDefinition)
        {
            return ValidateBuildInput(levelDefinition);
        }

        public bool HasReleasableSourceBox()
        {
            for (int i = 0; i < spawnedSourceBoxes.Count; i++)
            {
                SourceBox sourceBox = spawnedSourceBoxes[i];
                if (sourceBox != null && sourceBox.CanReleaseByUser)
                {
                    return true;
                }
            }

            for (int i = 0; i < recoveredSourceBoxes.Count; i++)
            {
                SourceBox sourceBox = recoveredSourceBoxes[i];
                if (sourceBox != null && sourceBox.CanReleaseByUser)
                {
                    return true;
                }
            }

            return false;
        }

        public bool HasUserInteractableSourceBox()
        {
            for (int i = 0; i < spawnedSourceBoxes.Count; i++)
            {
                SourceBox sourceBox = spawnedSourceBoxes[i];
                if (sourceBox != null && sourceBox.CanAttemptReleaseByUser)
                {
                    return true;
                }
            }

            for (int i = 0; i < recoveredSourceBoxes.Count; i++)
            {
                SourceBox sourceBox = recoveredSourceBoxes[i];
                if (sourceBox != null && sourceBox.CanAttemptReleaseByUser)
                {
                    return true;
                }
            }

            return false;
        }

        public void RegisterRecoveredSourceBox(SourceBox sourceBox)
        {
            if (sourceBox == null || !sourceBox.IsRecoveredSourceBox ||
                recoveredSourceBoxes.Contains(sourceBox))
            {
                return;
            }

            recoveredSourceBoxes.Add(sourceBox);
            sourceBox.RefreshInputState();
        }

        public void UnregisterRecoveredSourceBox(SourceBox sourceBox)
        {
            recoveredSourceBoxes.Remove(sourceBox);
        }

        public bool TryGetSourceBoxByCellIndex(int cellIndex, out SourceBox sourceBox)
        {
            sourceBox = null;
            if (sourceBoxesByCell == null || cellIndex < 0 || cellIndex >= sourceBoxesByCell.Length)
            {
                return false;
            }

            sourceBox = sourceBoxesByCell[cellIndex];
            return sourceBox != null;
        }

        public SourceBoxReleaseResult TryReleaseSourceBoxWithResult(SourceBox sourceBox)
        {
            return TryReleaseSourceBoxWithResult(sourceBox, false);
        }

        public SourceBoxReleaseResult TryReleaseSourceBoxWithHandBoosterResult(SourceBox sourceBox)
        {
            return TryReleaseSourceBoxWithResult(sourceBox, true);
        }

        private SourceBoxReleaseResult TryReleaseSourceBoxWithResult(SourceBox sourceBox, bool bypassAvailability)
        {
            if (sourceBox == null || !IsRuntimeSourceBox(sourceBox))
            {
                return SourceBoxReleaseResult.InvalidState;
            }

            return connectedBoxBoardController.IsConnected(sourceBox)
                ? connectedBoxBoardController.TryRelease(sourceBox, capacityController, bypassAvailability)
                : bypassAvailability
                    ? sourceBox.TryReleaseWithHandBooster()
                    : sourceBox.TryReleaseMarblesWithResult();
        }

        public bool IsRuntimeSourceBox(SourceBox sourceBox)
        {
            if (sourceBox == null || currentLevel == null || sourceBoxesByCell == null ||
                sourceBox.CellIndex < 0 || sourceBox.CellIndex >= sourceBoxesByCell.Length)
            {
                return false;
            }

            return sourceBoxesByCell[sourceBox.CellIndex] == sourceBox &&
                   sourceBox.CurrentState != SourceBoxState.Released;
        }

        public bool IsValidHandBoosterTarget(SourceBox sourceBox)
        {
            return IsRuntimeSourceBox(sourceBox) &&
                   !giftSourceBoxesAwaitingAvailability.Contains(sourceBox) &&
                   !sealedSourceBoxesAwaitingAvailability.Contains(sourceBox) &&
                   !HasMultiplierAffectedHandTransactionSource(sourceBox) &&
                   sourceBox.CurrentState == SourceBoxState.Locked &&
                   sourceBox.CanBeginHandDirectTransfer;
        }

        private bool HasMultiplierAffectedHandTransactionSource(SourceBox sourceBox)
        {
            if (IsMultiplierAffectedSourceBox(sourceBox))
            {
                return true;
            }

            return connectedBoxBoardController.TryGetPartner(sourceBox, out SourceBox partner) &&
                   IsMultiplierAffectedSourceBox(partner);
        }

        private bool IsMultiplierAffectedSourceBox(SourceBox sourceBox)
        {
            return sourceBox != null && GetMultiplierForSourceCell(sourceBox.CellIndex) > 1;
        }

        public bool TryGetHandDirectTransferSources(SourceBox selectedSourceBox, List<SourceBox> result)
        {
            if (result == null)
            {
                return false;
            }

            result.Clear();
            if (!IsValidHandBoosterTarget(selectedSourceBox))
            {
                return false;
            }

            result.Add(selectedSourceBox);
            if (!connectedBoxBoardController.TryGetPartner(selectedSourceBox, out SourceBox partner))
            {
                return true;
            }

            if (!IsRuntimeSourceBox(partner) || partner.CurrentState == SourceBoxState.Released ||
                partner.MarbleCount <= 0 || partner == selectedSourceBox ||
                IsMultiplierAffectedSourceBox(partner))
            {
                result.Clear();
                return false;
            }

            result.Add(partner);
            return true;
        }

        public void NotifyHandDirectTransferCommitted(IReadOnlyList<SourceBox> transferredSources)
        {
            if (transferredSources == null)
            {
                return;
            }

            for (int i = 0; i < transferredSources.Count; i++)
            {
                SourceBox sourceBox = transferredSources[i];
                if (sourceBox != null && connectedBoxBoardController.IsConnected(sourceBox))
                {
                    connectedBoxBoardController.ConsumeAfterExternalRelease(sourceBox);
                    return;
                }
            }
        }

        public bool IsFeatureSourceBox(SourceBox sourceBox)
        {
            if (!IsRuntimeSourceBox(sourceBox))
            {
                return false;
            }

            if (sourceBox.IsMysterySourceBox || connectedBoxBoardController.IsConnected(sourceBox))
            {
                return true;
            }

            int cellIndex = sourceBox.CellIndex;
            if (currentLevel == null || cellIndex < 0 || cellIndex >= currentLevel.Cells.Count)
            {
                return false;
            }

            LevelCellData cell = currentLevel.Cells[cellIndex];
            return cell != null && (cell.HasArrow || cell.HasKey || cell.HasConnectedBoxPair || cell.IsMysterySourceBox);
        }

        public void SetHandBoosterTargetingActive(bool active)
        {
            for (int i = 0; i < spawnedSourceBoxes.Count; i++)
            {
                SourceBox sourceBox = spawnedSourceBoxes[i];
                if (sourceBox != null)
                {
                    bool allowOverride = active &&
                                         IsRuntimeSourceBox(sourceBox) &&
                                         !giftSourceBoxesAwaitingAvailability.Contains(sourceBox) &&
                                         !sealedSourceBoxesAwaitingAvailability.Contains(sourceBox) &&
                                         !HasMultiplierAffectedHandTransactionSource(sourceBox);
                    sourceBox.SetBoosterTargetingInputOverride(allowOverride);
                }
            }
        }

        private void InitializeGameplayCellTopology(LevelDefinition levelDefinition)
        {
            activeGameplayCells.Clear();
            initialEntrySourceCells.Clear();
            vacatedGameplayCells.Clear();
            progressionSuppressedReleaseCells.Clear();
            progressionGrantedSourceBoxes.Clear();

            for (int column = 0; column < levelDefinition.ColumnCount; column++)
            {
                int initialEntrySourceRow = int.MaxValue;
                for (int row = 0; row < levelDefinition.RowCount; row++)
                {
                    int cellIndex = row * levelDefinition.ColumnCount + column;
                    LevelCellData cell = levelDefinition.Cells[cellIndex];
                    if (cell == null || cell.CellType != LevelCellType.SourceBox)
                    {
                        continue;
                    }

                    Vector2Int coordinate = new Vector2Int(row, column);
                    initialEntrySourceRow = Mathf.Min(initialEntrySourceRow, row);
                    if (IsCellSealed(coordinate))
                    {
                        continue;
                    }

                    activeGameplayCells.Add(coordinate);
                }

                if (initialEntrySourceRow != int.MaxValue)
                {
                    Vector2Int entryCoordinate = new Vector2Int(initialEntrySourceRow, column);
                    if (!IsCellSealed(entryCoordinate) &&
                        !multiplierGateBoardController.BlocksInitialEntry(entryCoordinate) &&
                        !HasInitialEntryObstacleBelow(levelDefinition, entryCoordinate))
                    {
                        initialEntrySourceCells.Add(entryCoordinate);
                    }
                }
            }
        }

        private void BuildSourceBoxes(LevelDefinition levelDefinition)
        {
            int rowCount = levelDefinition.RowCount;
            int columnCount = levelDefinition.ColumnCount;

            for (int row = 0; row < rowCount; row++)
            {
                for (int column = 0; column < columnCount; column++)
                {
                    int index = row * columnCount + column;
                    LevelCellData cell = levelDefinition.Cells[index];

                    if (cell.CellType != LevelCellType.SourceBox)
                    {
                        continue;
                    }

                    if (IsCellSealed(new Vector2Int(row, column)))
                    {
                        continue;
                    }

                    SpawnSourceBoxAtCell(
                        row,
                        column,
                        cell.ColorId,
                        cell.MarbleCount,
                        cell.IsMysterySourceBox,
                        $"SourceBox_{row}_{column}");
                }
            }
        }

        private void BuildSpawners(LevelDefinition levelDefinition)
        {
            int rowCount = levelDefinition.RowCount;
            int columnCount = levelDefinition.ColumnCount;

            for (int row = 0; row < rowCount; row++)
            {
                for (int column = 0; column < columnCount; column++)
                {
                    int index = row * columnCount + column;
                    LevelCellData cell = levelDefinition.Cells[index];

                    if (cell.CellType != LevelCellType.Spawner)
                    {
                        continue;
                    }

                    placeholderGridController.SetSpriteVisible(index, false);
                    BuildSpawnerAtCell(row, column, cell);
                }
            }
        }

        public void ClearBoard()
        {
            boardTileController?.Clear();
            arrowBoxBoardController.Clear();
            multiplierGateBoardController.Clear();
            panelBoardController.Clear();
            crateBoardController.Clear();
            giftBoxBoardController.Clear();
            connectedBoxBoardController.Clear();

            for (int i = 0; i < spawnedSourceBoxes.Count; i++)
            {
                SourceBox sourceBox = spawnedSourceBoxes[i];
                if (sourceBox == null)
                {
                    continue;
                }

                sourceBox.ReleaseStarted -= OnSourceBoxReleaseStarted;
                DestroyGameObject(sourceBox.gameObject);
            }

            for (int i = 0; i < spawnedSpawners.Count; i++)
            {
                SourceBoxSpawner spawner = spawnedSpawners[i];
                if (spawner == null)
                {
                    continue;
                }

                DestroyGameObject(spawner.gameObject);
            }

            for (int i = 0; i < recoveredSourceBoxes.Count; i++)
            {
                if (recoveredSourceBoxes[i] != null)
                {
                    DestroyGameObject(recoveredSourceBoxes[i].gameObject);
                }
            }

            spawnedSourceBoxes.Clear();
            recoveredSourceBoxes.Clear();
            spawnedSpawners.Clear();
            activeSourceBoxes.Clear();
            activeGameplayCells.Clear();
            initialEntrySourceCells.Clear();
            vacatedGameplayCells.Clear();
            progressionSuppressedReleaseCells.Clear();
            pendingSpawnerReplacementCells.Clear();
            progressionGrantedSourceBoxes.Clear();
            giftSourceBoxesAwaitingAvailability.Clear();
            sealedSourceBoxesAwaitingAvailability.Clear();
            activeSpawners.Clear();
            sourceBoxesByCell = null;
            currentLevel = null;
            if (placeholderGridController != null)
            {
                placeholderGridController.ClearRuntimeGrid();
            }

            ClearReleasedMarbles();
        }

        private void OnSourceBoxReleaseStarted(SourceBox sourceBox)
        {
            if (sourceBox == null || !TryGetCoordinateFromCellIndex(sourceBox.CellIndex, "activation callback", out Vector2Int coordinate))
            {
                return;
            }

            ReservePendingSpawnerReplacementIfNeeded(coordinate);
            if (sourceBox.SuppressesProgressionForCurrentRelease)
            {
                progressionSuppressedReleaseCells.Add(coordinate);
                vacatedGameplayCells.Remove(coordinate);
            }
            else
            {
                progressionSuppressedReleaseCells.Remove(coordinate);
            }

            if (!sourceBox.SuppressesProgressionForCurrentRelease &&
                !HasPendingSpawnerReplacement(coordinate))
            {
                MarkGameplayCellVacated(coordinate);
            }

            giftBoxBoardController.NotifySourceBoxActivated(coordinate);
            crateBoardController.NotifySourceBoxRemoved(coordinate, sourceBox, this);
            arrowBoxBoardController.NotifySourceBoxReleaseStarted(sourceBox.CellIndex);
            if (!sourceBox.SuppressesProgressionForCurrentRelease)
            {
                RecomputeSourceBoxAvailability();
            }
        }

        private void OnSourceBoxReleased(int cellIndex)
        {
            if (TryGetCoordinateFromCellIndex(cellIndex, "release callback", out Vector2Int coordinate))
            {
                HandleSourceBoxReleased(coordinate);
            }
        }

        private bool TryGetCoordinateFromCellIndex(int cellIndex, string callbackName, out Vector2Int coordinate)
        {
            coordinate = default;
            if (currentLevel == null || sourceBoxesByCell == null)
            {
                Debug.LogError($"{nameof(SourceBoxBoardController)} on '{name}' received {callbackName} for cell {cellIndex}, but no board is currently built.", this);
                return false;
            }

            if (cellIndex < 0 || cellIndex >= sourceBoxesByCell.Length)
            {
                Debug.LogError($"{nameof(SourceBoxBoardController)} on '{name}' received invalid {callbackName} cell index {cellIndex}.", this);
                return false;
            }

            int row = cellIndex / currentLevel.ColumnCount;
            int column = cellIndex % currentLevel.ColumnCount;
            coordinate = new Vector2Int(row, column);
            return true;
        }

        public void HandleSourceBoxReleased(Vector2Int coordinate)
        {
            if (currentLevel == null || sourceBoxesByCell == null)
            {
                Debug.LogError($"{nameof(SourceBoxBoardController)} on '{name}' received release callback for coordinate {coordinate}, but no board is currently built.", this);
                return;
            }

            if (!IsInsideBoard(coordinate.x, coordinate.y))
            {
                Debug.LogError($"{nameof(SourceBoxBoardController)} on '{name}' received invalid release callback coordinate {coordinate}.", this);
                return;
            }

            int releasedCellIndex = GetCellIndex(coordinate.x, coordinate.y);
            bool suppressProgression = progressionSuppressedReleaseCells.Remove(coordinate);
            SourceBox releasedSourceBox = sourceBoxesByCell[releasedCellIndex];
            progressionGrantedSourceBoxes.Remove(releasedSourceBox);
            sourceBoxesByCell[releasedCellIndex] = null;
            activeSourceBoxes.Remove(coordinate);
            bool replacementSpawned = TrySpawnFromSpawnerTargeting(coordinate);
            pendingSpawnerReplacementCells.Remove(coordinate);
            if (replacementSpawned)
            {
                vacatedGameplayCells.Remove(coordinate);
            }
            else if (!suppressProgression)
            {
                MarkGameplayCellVacated(coordinate);
            }
            else
            {
                vacatedGameplayCells.Remove(coordinate);
            }

            bool giftResolutionPrepared = giftBoxBoardController.NotifySourceBoxReleased(coordinate);
            if (suppressProgression)
            {
                RefreshOrphanedNeighborsAfterHandRemoval(coordinate);
            }
            else if (!giftResolutionPrepared && !giftBoxBoardController.IsResolving)
            {
                RecomputeSourceBoxAvailability();
            }
        }

        public bool TryCreateGiftBox(int cellIndex, out GiftBoxController giftBox)
        {
            giftBox = null;
            if (currentLevel == null || boardFeatureCatalog == null || boardFeatureCatalog.GiftBoxPrefab == null ||
                cellIndex < 0 || cellIndex >= currentLevel.CellCount)
            {
                return false;
            }

            int row = cellIndex / currentLevel.ColumnCount;
            int column = cellIndex % currentLevel.ColumnCount;
            GiftBoxController prefab = boardFeatureCatalog.GiftBoxPrefab;
            giftBox = Instantiate(prefab, placeholderGridController.GeneratedGridRoot);
            giftBox.name = $"GiftBox_{row}_{column}";
            giftBox.transform.localPosition = placeholderGridController.CalculateCellLocalPosition(currentLevel.RowCount, currentLevel.ColumnCount, row, column, giftBox.transform.localPosition.z);
            giftBox.transform.localRotation = Quaternion.identity;
            giftBox.transform.localScale = Vector3.one;

            LevelCellData giftCell = currentLevel.Cells[cellIndex];
            int giftContentCount = giftCell?.GiftBoxContents.Count ?? 0;
            if (!giftBox.Initialize(cellIndex, new Vector2Int(row, column), giftContentCount))
            {
                DestroyGameObject(giftBox.gameObject);
                giftBox = null;
                return false;
            }

            placeholderGridController.SetSpriteVisible(cellIndex, false);
            return true;
        }

        public bool TryFindGiftBoxSpawnCells(
            Vector2Int giftBoxCoordinate,
            int requestedCount,
            GiftBoxBoardController giftBoxController,
            out List<Vector2Int> targetCells)
        {
            targetCells = new List<Vector2Int>(requestedCount);
            if (currentLevel == null || sourceBoxesByCell == null || requestedCount < 1)
            {
                return false;
            }

            List<Vector2Int> candidates = new List<Vector2Int>();
            for (int row = 0; row < currentLevel.RowCount; row++)
            {
                for (int column = 0; column < currentLevel.ColumnCount; column++)
                {
                    Vector2Int candidate = new Vector2Int(row, column);
                    if (candidate == giftBoxCoordinate || !CanUseCellForGiftBoxSpawn(candidate, giftBoxController))
                    {
                        continue;
                    }

                    candidates.Add(candidate);
                }
            }

            candidates.Sort((left, right) =>
            {
                int leftDistance = Mathf.Abs(left.x - giftBoxCoordinate.x) + Mathf.Abs(left.y - giftBoxCoordinate.y);
                int rightDistance = Mathf.Abs(right.x - giftBoxCoordinate.x) + Mathf.Abs(right.y - giftBoxCoordinate.y);
                int distanceComparison = leftDistance.CompareTo(rightDistance);
                return distanceComparison != 0 ? distanceComparison : GetCellIndex(left.x, left.y).CompareTo(GetCellIndex(right.x, right.y));
            });

            if (candidates.Count < requestedCount)
            {
                targetCells.Clear();
                return false;
            }

            targetCells.AddRange(candidates.GetRange(0, requestedCount));
            return true;
        }

        public bool TrySpawnGiftBoxSourceBox(Vector2Int coordinate, GiftBoxSourceBoxData content, out SourceBox sourceBox)
        {
            sourceBox = null;
            if (content == null || !CanUseCellForGiftBoxSpawn(coordinate, giftBoxBoardController))
            {
                return false;
            }

            sourceBox = SpawnSourceBoxAtCell(
                coordinate.x,
                coordinate.y,
                content.ColorId,
                content.MarbleCount,
                false,
                $"SourceBox_Gift_{coordinate.x}_{coordinate.y}");

            if (sourceBox != null)
            {
                sourceBox.gameObject.SetActive(false);
                giftSourceBoxesAwaitingAvailability.Add(sourceBox);
            }

            return sourceBox != null;
        }

        public void RemoveRuntimeSourceBox(SourceBox sourceBox)
        {
            if (sourceBox == null)
            {
                return;
            }

            if (TryGetCoordinateFromCellIndex(sourceBox.CellIndex, "Gift Box rollback", out Vector2Int coordinate))
            {
                giftSourceBoxesAwaitingAvailability.Remove(sourceBox);
                RemoveSourceBoxAt(coordinate, sourceBox);
            }
        }

        public void RecomputeAfterGiftBoxRollback()
        {
            RecomputeSourceBoxAvailability();
            RefreshSourceBoxInputStates();
        }

        public void SetSourceBoxInteractionsBlocked(bool blocked)
        {
            for (int i = 0; i < spawnedSourceBoxes.Count; i++)
            {
                spawnedSourceBoxes[i]?.SetInteractionBlocked(blocked);
            }
        }

        public void RefreshSourceBoxInputStatesAfterBoardChange()
        {
            RefreshSourceBoxInputStates();
        }

        public void FinalizeGiftSourceBoxAvailability(IReadOnlyList<SourceBox> giftSourceBoxes)
        {
            if (giftSourceBoxes != null)
            {
                for (int i = 0; i < giftSourceBoxes.Count; i++)
                {
                    SourceBox giftSourceBox = giftSourceBoxes[i];
                    giftSourceBoxesAwaitingAvailability.Remove(giftSourceBox);
                    if (giftSourceBox != null && TryGetCoordinateFromCellIndex(giftSourceBox.CellIndex, "Gift Box finalize", out Vector2Int coordinate))
                    {
                        vacatedGameplayCells.Remove(coordinate);
                    }
                }
            }

            RecomputeSourceBoxAvailability();
            RefreshSourceBoxInputStates();
        }

        public void NotifyRuntimeOccupancyChanged()
        {
            RecomputeSourceBoxAvailability();
        }

        public void NotifyTargetBoxFirstFilled(Gameplay.TargetBoxes.TargetBox targetBox)
        {
            panelBoardController.NotifyTargetBoxFirstFilled(targetBox, this);
        }

        public bool CanBeginCrateSourceReveal(IReadOnlyList<Vector2Int> coordinates, out string error)
        {
            return CanBeginSealedSourceReveal(coordinates, 4, crateBoardController.IsCellSealed, "Crate", out error);
        }

        public bool CanBeginPanelSourceReveal(IReadOnlyList<Vector2Int> coordinates, out string error)
        {
            return CanBeginSealedSourceReveal(coordinates, 9, panelBoardController.IsCellSealed, "Panel", out error);
        }

        private bool CanBeginSealedSourceReveal(
            IReadOnlyList<Vector2Int> coordinates,
            int requiredCellCount,
            System.Func<Vector2Int, bool> isSealedByOwner,
            string featureName,
            out string error)
        {
            error = string.Empty;
            if (currentLevel == null || sourceBoxesByCell == null)
            {
                error = "no board is currently built";
                return false;
            }

            if (coordinates == null || coordinates.Count != requiredCellCount)
            {
                error = $"the {featureName} footprint must contain exactly {requiredCellCount} cells";
                return false;
            }

            HashSet<Vector2Int> uniqueCoordinates = new HashSet<Vector2Int>();
            for (int i = 0; i < coordinates.Count; i++)
            {
                Vector2Int coordinate = coordinates[i];
                if (!uniqueCoordinates.Add(coordinate) || !IsInsideBoard(coordinate.x, coordinate.y))
                {
                    error = $"cell {coordinate} is duplicate or outside the board";
                    return false;
                }

                int cellIndex = GetCellIndex(coordinate.x, coordinate.y);
                LevelCellData cell = currentLevel.Cells[cellIndex];
                if (cell == null || cell.CellType != LevelCellType.SourceBox)
                {
                    error = $"cell {coordinate} is not a SourceBox";
                    return false;
                }

                if (!isSealedByOwner(coordinate))
                {
                    error = $"cell {coordinate} is not sealed by an active {featureName}";
                    return false;
                }

                if (sourceBoxesByCell[cellIndex] != null || activeSourceBoxes.ContainsKey(coordinate))
                {
                    error = $"cell {coordinate} already has a runtime SourceBox";
                    return false;
                }
            }

            return true;
        }

        public bool TryBeginCrateSourceReveal(IReadOnlyList<Vector2Int> coordinates, out List<SourceBox> revealedSourceBoxes)
        {
            return TryBeginSealedSourceReveal(coordinates, 4, "Crate", out revealedSourceBoxes);
        }

        public bool TryBeginPanelSourceReveal(IReadOnlyList<Vector2Int> coordinates, out List<SourceBox> revealedSourceBoxes)
        {
            return TryBeginSealedSourceReveal(coordinates, 9, "Panel", out revealedSourceBoxes);
        }

        private bool TryBeginSealedSourceReveal(
            IReadOnlyList<Vector2Int> coordinates,
            int requiredCellCount,
            string featureName,
            out List<SourceBox> revealedSourceBoxes)
        {
            revealedSourceBoxes = new List<SourceBox>(requiredCellCount);
            if (currentLevel == null || sourceBoxesByCell == null || coordinates == null || coordinates.Count != requiredCellCount)
            {
                return false;
            }

            for (int i = 0; i < coordinates.Count; i++)
            {
                Vector2Int coordinate = coordinates[i];
                if (!IsInsideBoard(coordinate.x, coordinate.y))
                {
                    RollbackSealedSourceReveal(coordinates, revealedSourceBoxes);
                    return false;
                }

                int cellIndex = GetCellIndex(coordinate.x, coordinate.y);
                LevelCellData cell = currentLevel.Cells[cellIndex];
                if (cell == null || cell.CellType != LevelCellType.SourceBox || sourceBoxesByCell[cellIndex] != null)
                {
                    RollbackSealedSourceReveal(coordinates, revealedSourceBoxes);
                    return false;
                }

                activeGameplayCells.Add(coordinate);
                placeholderGridController.SetSpriteVisible(cellIndex, true);
                SourceBox sourceBox = SpawnSourceBoxAtCell(
                    coordinate.x,
                    coordinate.y,
                    cell.ColorId,
                    cell.MarbleCount,
                    cell.IsMysterySourceBox,
                    $"SourceBox_{featureName}_{coordinate.x}_{coordinate.y}");

                if (sourceBox == null)
                {
                    RollbackSealedSourceReveal(coordinates, revealedSourceBoxes);
                    return false;
                }

                sourceBox.transform.localScale = Vector3.zero;
                sourceBox.SetInteractionBlocked(true);
                sealedSourceBoxesAwaitingAvailability.Add(sourceBox);
                revealedSourceBoxes.Add(sourceBox);
            }

            AddLowestActiveEntriesForColumns(coordinates);
            return true;
        }

        public void FinalizeCrateSourceReveal(IReadOnlyList<SourceBox> revealedSourceBoxes)
        {
            FinalizeSealedSourceReveal(revealedSourceBoxes);
        }

        public void FinalizePanelSourceReveal(IReadOnlyList<SourceBox> revealedSourceBoxes)
        {
            FinalizeSealedSourceReveal(revealedSourceBoxes);
        }

        private void FinalizeSealedSourceReveal(IReadOnlyList<SourceBox> revealedSourceBoxes)
        {
            if (currentLevel == null || revealedSourceBoxes == null)
            {
                return;
            }

            bool keepInteractionBlocked = giftBoxBoardController.IsResolving;
            for (int i = 0; i < revealedSourceBoxes.Count; i++)
            {
                SourceBox sourceBox = revealedSourceBoxes[i];
                sealedSourceBoxesAwaitingAvailability.Remove(sourceBox);
                if (sourceBox == null)
                {
                    continue;
                }

                sourceBox.transform.localScale = Vector3.one;
                sourceBox.SetInteractionBlocked(keepInteractionBlocked);
            }

            RecomputeSourceBoxAvailability();
            RefreshSourceBoxInputStates();
        }

        public void DestroyRuntimeBoardObject(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            GiftBoxController giftBox = target.GetComponent<GiftBoxController>();
            if (giftBox != null && currentLevel != null && giftBox.CellIndex >= 0 && giftBox.CellIndex < currentLevel.CellCount)
            {
                placeholderGridController.SetSpriteVisible(giftBox.CellIndex, true);
            }

            DestroyGameObject(target);
        }

        private bool CanUseCellForGiftBoxSpawn(Vector2Int coordinate, GiftBoxBoardController giftBoxController)
        {
            if (IsCellSealed(coordinate) || multiplierGateBoardController.IsMultiplierAffectedCell(coordinate) ||
                !IsVacatedGameplayEmptyCell(coordinate) || sourceBoxesByCell[GetCellIndex(coordinate.x, coordinate.y)] != null ||
                (giftBoxController != null && giftBoxController.IsGiftBoxCell(coordinate)))
            {
                return false;
            }

            for (int i = 0; i < activeSpawners.Count; i++)
            {
                SourceBoxSpawner spawner = activeSpawners[i];
                if (spawner != null && (spawner.GridCoordinate == coordinate || (spawner.TargetCoordinate == coordinate && spawner.RemainingCount > 0)))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsCellSealed(Vector2Int coordinate)
        {
            return crateBoardController.IsCellSealed(coordinate) || panelBoardController.IsCellSealed(coordinate) ||
                   multiplierGateBoardController.IsCellReserved(coordinate);
        }

        private void AddLowestActiveEntriesForColumns(IReadOnlyList<Vector2Int> revealedCoordinates)
        {
            HashSet<int> affectedColumns = new HashSet<int>();
            for (int i = 0; i < revealedCoordinates.Count; i++)
            {
                affectedColumns.Add(revealedCoordinates[i].y);
            }

            foreach (int column in affectedColumns)
            {
                int lowestActiveRow = int.MaxValue;
                foreach (Vector2Int activeCoordinate in activeGameplayCells)
                {
                    if (activeCoordinate.y == column)
                    {
                        lowestActiveRow = Mathf.Min(lowestActiveRow, activeCoordinate.x);
                    }
                }

                if (lowestActiveRow != int.MaxValue)
                {
                    Vector2Int entryCoordinate = new Vector2Int(lowestActiveRow, column);
                    if (!multiplierGateBoardController.BlocksInitialEntry(entryCoordinate) &&
                        !HasInitialEntryObstacleBelow(currentLevel, entryCoordinate))
                    {
                        initialEntrySourceCells.Add(entryCoordinate);
                    }
                }
            }
        }

        public bool IsVacatedGameplayEmptyCell(Vector2Int coordinate)
        {
            if (currentLevel == null || sourceBoxesByCell == null || !IsInsideBoard(coordinate.x, coordinate.y))
            {
                return false;
            }

            if (!activeGameplayCells.Contains(coordinate) || !vacatedGameplayCells.Contains(coordinate))
            {
                return false;
            }

            if (IsCellSealed(coordinate))
            {
                return false;
            }

            int cellIndex = GetCellIndex(coordinate.x, coordinate.y);
            LevelCellData cell = currentLevel.Cells[cellIndex];
            if (cell == null || BoardTileController.IsTerrainCell(cell))
            {
                return false;
            }

            SourceBox sourceBox = sourceBoxesByCell[cellIndex];
            if (sourceBox != null &&
                sourceBox.CurrentState != SourceBoxState.Released &&
                !giftSourceBoxesAwaitingAvailability.Contains(sourceBox))
            {
                return false;
            }

            if (giftBoxBoardController.IsGiftBoxCell(coordinate) || HasPendingSpawnerReplacement(coordinate))
            {
                return false;
            }

            for (int i = 0; i < activeSpawners.Count; i++)
            {
                SourceBoxSpawner spawner = activeSpawners[i];
                if (spawner != null && spawner.GridCoordinate == coordinate)
                {
                    return false;
                }
            }

            return true;
        }

        public void RecomputeSourceBoxAvailability(bool animateNewlyAvailable = true)
        {
            if (currentLevel == null || sourceBoxesByCell == null)
            {
                return;
            }

            for (int row = 0; row < currentLevel.RowCount; row++)
            {
                for (int column = 0; column < currentLevel.ColumnCount; column++)
                {
                    int cellIndex = GetCellIndex(row, column);
                    SourceBox sourceBox = sourceBoxesByCell[cellIndex];
                    if (sourceBox == null || sourceBox.CurrentState == SourceBoxState.Released ||
                        giftSourceBoxesAwaitingAvailability.Contains(sourceBox) ||
                        sealedSourceBoxesAwaitingAvailability.Contains(sourceBox))
                    {
                        continue;
                    }

                    bool shouldBeAvailable = CalculateSourceBoxAvailability(sourceBox);
                    if (connectedBoxBoardController.TryGetPartner(sourceBox, out SourceBox partner))
                    {
                        shouldBeAvailable |= CalculateSourceBoxAvailability(partner);
                    }

                    sourceBox.SetRuntimeAvailability(shouldBeAvailable, animateNewlyAvailable);
                }
            }
        }

        private bool CalculateSourceBoxAvailability(SourceBox sourceBox)
        {
            if (sourceBox == null || sourceBox.CurrentState == SourceBoxState.Released ||
                giftSourceBoxesAwaitingAvailability.Contains(sourceBox) ||
                sealedSourceBoxesAwaitingAvailability.Contains(sourceBox))
            {
                return false;
            }

            int cellIndex = sourceBox.CellIndex;
            if (cellIndex < 0 || cellIndex >= sourceBoxesByCell.Length || sourceBoxesByCell[cellIndex] != sourceBox)
            {
                return false;
            }

            Vector2Int coordinate = new Vector2Int(
                cellIndex / currentLevel.ColumnCount,
                cellIndex % currentLevel.ColumnCount);
            bool shouldBeAvailable = progressionGrantedSourceBoxes.Contains(sourceBox) ||
                                     initialEntrySourceCells.Contains(coordinate) ||
                                     HasVacatedGameplayNeighbor(coordinate);
            return arrowBoxBoardController.ConstrainAvailability(cellIndex, shouldBeAvailable);
        }

        private bool HasVacatedGameplayNeighbor(Vector2Int coordinate)
        {
            return IsVacatedGameplayEmptyCell(new Vector2Int(coordinate.x + 1, coordinate.y)) ||
                   IsVacatedGameplayEmptyCell(new Vector2Int(coordinate.x - 1, coordinate.y)) ||
                   IsVacatedGameplayEmptyCell(new Vector2Int(coordinate.x, coordinate.y + 1)) ||
                   IsVacatedGameplayEmptyCell(new Vector2Int(coordinate.x, coordinate.y - 1));
        }

        private void RefreshOrphanedNeighborsAfterHandRemoval(Vector2Int removedCoordinate)
        {
            HashSet<SourceBox> visitedSourceBoxes = new HashSet<SourceBox>();
            for (int directIndex = 0; directIndex < OrthogonalNeighborOffsets.Length; directIndex++)
            {
                Vector2Int directCoordinate = removedCoordinate + OrthogonalNeighborOffsets[directIndex];
                if (!TryGetActiveHandProgressionSource(directCoordinate, out SourceBox directNeighbor) ||
                    visitedSourceBoxes.Contains(directNeighbor))
                {
                    continue;
                }

                TryOpenStrandedSourceComponent(
                    directCoordinate,
                    removedCoordinate,
                    visitedSourceBoxes);
            }
        }

        private bool TryGetActiveHandProgressionSource(Vector2Int coordinate, out SourceBox sourceBox)
        {
            return activeSourceBoxes.TryGetValue(coordinate, out sourceBox) &&
                   sourceBox != null &&
                   sourceBox.CurrentState != SourceBoxState.Released;
        }

        private void TryOpenStrandedSourceComponent(
            Vector2Int startCoordinate,
            Vector2Int removedCoordinate,
            HashSet<SourceBox> visitedSourceBoxes)
        {
            Queue<Vector2Int> pending = new Queue<Vector2Int>();
            List<SourceBox> component = new List<SourceBox>();
            Dictionary<SourceBox, Vector2Int> componentCoordinates = new Dictionary<SourceBox, Vector2Int>();
            pending.Enqueue(startCoordinate);

            while (pending.Count > 0)
            {
                Vector2Int currentCoordinate = pending.Dequeue();
                if (!TryGetActiveHandProgressionSource(currentCoordinate, out SourceBox current) ||
                    !visitedSourceBoxes.Add(current))
                {
                    continue;
                }

                component.Add(current);
                componentCoordinates.Add(current, currentCoordinate);
                for (int directionIndex = 0; directionIndex < OrthogonalNeighborOffsets.Length; directionIndex++)
                {
                    Vector2Int neighborCoordinate = currentCoordinate + OrthogonalNeighborOffsets[directionIndex];
                    if (!TryGetActiveHandProgressionSource(neighborCoordinate, out SourceBox neighbor) ||
                        visitedSourceBoxes.Contains(neighbor))
                    {
                        continue;
                    }

                    pending.Enqueue(neighborCoordinate);
                }
            }

            for (int i = 0; i < component.Count; i++)
            {
                if (component[i] == null || component[i].CurrentState != SourceBoxState.Locked)
                {
                    return;
                }
            }

            SourceBox closestEligibleSource = null;
            int closestDistance = int.MaxValue;
            int closestCellIndex = int.MaxValue;
            for (int i = 0; i < component.Count; i++)
            {
                SourceBox sourceBox = component[i];
                if (sourceBox == null || sourceBox.CurrentState != SourceBoxState.Locked ||
                    giftSourceBoxesAwaitingAvailability.Contains(sourceBox) ||
                    sealedSourceBoxesAwaitingAvailability.Contains(sourceBox) ||
                    !arrowBoxBoardController.ConstrainAvailability(sourceBox.CellIndex, true))
                {
                    continue;
                }

                Vector2Int coordinate = componentCoordinates[sourceBox];
                int distance = Mathf.Abs(coordinate.x - removedCoordinate.x) +
                               Mathf.Abs(coordinate.y - removedCoordinate.y);
                if (distance < closestDistance ||
                    distance == closestDistance && sourceBox.CellIndex < closestCellIndex)
                {
                    closestEligibleSource = sourceBox;
                    closestDistance = distance;
                    closestCellIndex = sourceBox.CellIndex;
                }
            }

            if (closestEligibleSource != null)
            {
                OpenSourceBoxThroughNormalAvailability(closestEligibleSource);
            }
        }

        private void OpenSourceBoxThroughNormalAvailability(SourceBox sourceBox)
        {
            progressionGrantedSourceBoxes.Add(sourceBox);
            sourceBox.SetRuntimeAvailability(true);
            if (!connectedBoxBoardController.TryGetPartner(sourceBox, out SourceBox partner) ||
                !IsRuntimeSourceBox(partner) ||
                giftSourceBoxesAwaitingAvailability.Contains(partner) ||
                sealedSourceBoxesAwaitingAvailability.Contains(partner))
            {
                return;
            }

            progressionGrantedSourceBoxes.Add(partner);
            partner.SetRuntimeAvailability(true);
        }

        private bool HasInitialEntryObstacleBelow(
            LevelDefinition levelDefinition,
            Vector2Int sourceCoordinate)
        {
            int rowBelow = sourceCoordinate.x - 1;
            Vector2Int coordinateBelow = new Vector2Int(rowBelow, sourceCoordinate.y);
            return rowBelow >= 0 &&
                   levelDefinition != null &&
                   !multiplierGateBoardController.IsCellReserved(coordinateBelow) &&
                   levelDefinition.TryGetCell(rowBelow, sourceCoordinate.y, out LevelCellData cellBelow) &&
                   // A spawner occupies the cell below; it is not an open board entry.
                   (BoardTileController.IsTerrainCell(cellBelow) || cellBelow?.CellType == LevelCellType.Spawner);
        }

        private bool ValidateBuildInput(LevelDefinition levelDefinition)
        {
            if (levelDefinition == null)
            {
                Debug.LogError($"{nameof(SourceBoxBoardController)} on '{name}' cannot build because LevelDefinition is null.", this);
                return false;
            }

            if (boardFeatureCatalog == null)
            {
                Debug.LogError($"{nameof(SourceBoxBoardController)} on '{name}' is missing BoardFeatureCatalog reference.", this);
                return false;
            }

            if (!boardFeatureCatalog.Validate(this))
            {
                return false;
            }

            if (colorCatalog == null)
            {
                Debug.LogError($"{nameof(SourceBoxBoardController)} on '{name}' is missing MarbleColorCatalog reference.", this);
                return false;
            }

            if (releasedMarbleContainer == null)
            {
                Debug.LogError($"{nameof(SourceBoxBoardController)} on '{name}' is missing ReleasedMarbleContainer reference.", this);
                return false;
            }

            if (capacityController == null)
            {
                Debug.LogError($"{nameof(SourceBoxBoardController)} on '{name}' is missing MarbleCapacityController reference.", this);
                return false;
            }

            if (placeholderGridController == null)
            {
                Debug.LogError($"{nameof(SourceBoxBoardController)} on '{name}' is missing SourceBoxPlaceholderGridController reference.", this);
                return false;
            }

            if (boardTileController == null)
            {
                Debug.LogError($"{nameof(SourceBoxBoardController)} on '{name}' is missing BoardTileController reference.", this);
                return false;
            }

            if (!boardTileController.CanBuild(levelDefinition))
            {
                return false;
            }

            validationErrors.Clear();
            bool levelIsValid = LevelDefinitionValidator.ValidateForRuntimeBuild(levelDefinition, validationErrors);
            if (levelIsValid)
            {
                levelIsValid = ValidateCatalogEntries(levelDefinition, validationErrors);
            }

            if (levelIsValid)
            {
                levelIsValid = ValidateSpawnerBuildTargets(levelDefinition, validationErrors);
            }

            if (levelIsValid)
            {
                return true;
            }

            for (int i = 0; i < validationErrors.Count; i++)
            {
                Debug.LogError($"{nameof(SourceBoxBoardController)} on '{name}' cannot build level: {validationErrors[i]}", this);
            }

            return false;
        }

        private bool ValidateCatalogEntries(LevelDefinition levelDefinition, List<string> errors)
        {
            bool isValid = true;
            IReadOnlyList<LevelCellData> cells = levelDefinition.Cells;
            bool usesConnectedBoxes = false;
            bool usesArrowBoxes = false;

            if (levelDefinition.Crates.Count > 0)
            {
                if (boardFeatureCatalog.CratePrefab == null)
                {
                    errors.Add($"Level '{levelDefinition.name}' contains Crates but BoardFeatureCatalog has no Crate prefab.");
                    isValid = false;
                }
                else if (!boardFeatureCatalog.CratePrefab.HasRequiredReferences)
                {
                    errors.Add($"Level '{levelDefinition.name}' contains Crates but the Crate prefab is missing View, Part 1, Part 2, Part 3, or CrateDebrisFX ParticleSystem references.");
                    isValid = false;
                }
            }

            if (levelDefinition.Panels.Count > 0)
            {
                if (boardFeatureCatalog.PanelPrefab == null)
                {
                    errors.Add($"Level '{levelDefinition.name}' contains Panels but BoardFeatureCatalog has no Panel prefab.");
                    isValid = false;
                }
                else if (!boardFeatureCatalog.PanelPrefab.HasRequiredReferences)
                {
                    errors.Add($"Level '{levelDefinition.name}' contains Panels but the Panel prefab is missing Visual or Text/TMP_Text references.");
                    isValid = false;
                }
            }

            if (levelDefinition.Gates.Count > 0)
            {
                if (boardFeatureCatalog.MultiplierGatePrefab == null)
                {
                    errors.Add($"Level '{levelDefinition.name}' contains Multiplier Gates but BoardFeatureCatalog has no Multiplier Gate prefab.");
                    isValid = false;
                }
                else if (!boardFeatureCatalog.MultiplierGatePrefab.HasRequiredReferences)
                {
                    errors.Add($"Level '{levelDefinition.name}' contains Multiplier Gates but the prefab is missing View, trigger Collider2D, or MultiplierGateTrigger references, or its collider is not a trigger.");
                    isValid = false;
                }
            }

            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i] != null && cells[i].HasConnectedBoxPair)
                {
                    usesConnectedBoxes = true;
                    break;
                }
            }

            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i] != null && cells[i].HasArrow)
                {
                    usesArrowBoxes = true;
                    break;
                }
            }

            if (usesArrowBoxes)
            {
                if (boardFeatureCatalog.ArrowBoxPrefab == null)
                {
                    errors.Add($"Level '{levelDefinition.name}' contains Arrow Boxes but BoardFeatureCatalog has no Arrow Box prefab.");
                    isValid = false;
                }
                else if (!boardFeatureCatalog.ArrowBoxPrefab.HasRequiredReferences)
                {
                    errors.Add($"Level '{levelDefinition.name}' contains Arrow Boxes but the Arrow prefab is missing its arrow_stroke SpriteRenderer reference.");
                    isValid = false;
                }
            }

            if (usesConnectedBoxes)
            {
                if (boardFeatureCatalog.ConnectionPrefab == null)
                {
                    errors.Add($"Level '{levelDefinition.name}' contains Connected Boxes but BoardFeatureCatalog has no Connection prefab.");
                    isValid = false;
                }
                else if (!boardFeatureCatalog.ConnectionPrefab.HasRequiredReferences)
                {
                    errors.Add($"Level '{levelDefinition.name}' contains Connected Boxes but Connection prefab is missing Part 1/Sprite or Part 2/Sprite reference.");
                    isValid = false;
                }

                if (boardFeatureCatalog.ConnectedBoxClosedStrokePrefab == null)
                {
                    errors.Add($"Level '{levelDefinition.name}' contains Connected Boxes but BoardFeatureCatalog has no connected_box_closeview_stroke prefab.");
                    isValid = false;
                }

                if (boardFeatureCatalog.ConnectedBoxOpenStrokePrefab == null)
                {
                    errors.Add($"Level '{levelDefinition.name}' contains Connected Boxes but BoardFeatureCatalog has no connected_box_openview_stroke prefab.");
                    isValid = false;
                }
            }

            for (int i = 0; i < cells.Count; i++)
            {
                LevelCellData cell = cells[i];
                if (cell == null || cell.CellType != LevelCellType.SourceBox)
                {
                    if (cell != null && cell.CellType == LevelCellType.GiftBox)
                    {
                        if (boardFeatureCatalog.GiftBoxPrefab == null)
                        {
                            errors.Add($"Level '{levelDefinition.name}' contains a Gift Box but BoardFeatureCatalog has no Gift Box prefab.");
                            isValid = false;
                            continue;
                        }

                        if (!boardFeatureCatalog.GiftBoxPrefab.HasRequiredReferences)
                        {
                            errors.Add($"Level '{levelDefinition.name}' contains a Gift Box but the Gift Box prefab is missing Box Top Part, Box Bottom Part, SpawnPoint, or View/Number TMP_Text reference.");
                            isValid = false;
                            continue;
                        }

                        IReadOnlyList<GiftBoxSourceBoxData> contents = cell.GiftBoxContents;
                        for (int contentIndex = 0; contentIndex < contents.Count; contentIndex++)
                        {
                            GiftBoxSourceBoxData content = contents[contentIndex];
                            if (content == null || !colorCatalog.TryGetEntry(content.ColorId, out MarbleColorCatalog.Entry giftColorEntry))
                            {
                                int row = i / levelDefinition.ColumnCount;
                                int column = i % levelDefinition.ColumnCount;
                                errors.Add($"Level '{levelDefinition.name}' Gift Box cell {i} at row {row}, column {column}, content {contentIndex + 1} has no valid color catalog entry.");
                                isValid = false;
                                continue;
                            }

                            if (!ValidateSourceBoxSprites(levelDefinition, content.ColorId, giftColorEntry, true, $"Gift Box cell {i} content {contentIndex + 1}", errors))
                            {
                                isValid = false;
                            }
                        }

                        continue;
                    }

                    if (cell == null || cell.CellType != LevelCellType.Spawner)
                    {
                        continue;
                    }

                    IReadOnlyList<SpawnerSourceBoxData> sequence = cell.SpawnSequence;
                    for (int sequenceIndex = 0; sequenceIndex < sequence.Count; sequenceIndex++)
                    {
                        SpawnerSourceBoxData entry = sequence[sequenceIndex];
                        if (entry == null)
                        {
                            continue;
                        }

                        if (!colorCatalog.TryGetEntry(entry.ColorId, out MarbleColorCatalog.Entry colorEntry))
                        {
                            int row = i / levelDefinition.ColumnCount;
                            int column = i % levelDefinition.ColumnCount;
                            errors.Add($"Level '{levelDefinition.name}' Spawner cell {i} at row {row}, column {column}, sequence {sequenceIndex + 1} has no valid color catalog entry for '{entry.ColorId}'.");
                            isValid = false;
                            continue;
                        }

                        if (!ValidateSourceBoxSprites(levelDefinition, entry.ColorId, colorEntry, true, $"Spawner cell {i} sequence {sequenceIndex + 1}", errors))
                        {
                            isValid = false;
                        }
                    }

                    continue;
                }

                if (!colorCatalog.TryGetEntry(cell.ColorId, out MarbleColorCatalog.Entry sourceBoxColorEntry))
                {
                    int row = i / levelDefinition.ColumnCount;
                    int column = i % levelDefinition.ColumnCount;
                    errors.Add($"Level '{levelDefinition.name}' SourceBox cell {i} at row {row}, column {column} has no valid color catalog entry for '{cell.ColorId}'.");
                    isValid = false;
                    continue;
                }

                if (cell.HasConnectedBoxPair && !sourceBoxColorEntry.HasConnectedBoxConnectionTint)
                {
                    int row = i / levelDefinition.ColumnCount;
                    int column = i % levelDefinition.ColumnCount;
                    errors.Add($"Level '{levelDefinition.name}' Connected Box cell {i} at row {row}, column {column} color '{cell.ColorId}' is missing a visible connection tint in MarbleColorCatalog.");
                    isValid = false;
                }

                if (cell.HasArrow && !sourceBoxColorEntry.HasArrowStrokeTint)
                {
                    int row = i / levelDefinition.ColumnCount;
                    int column = i % levelDefinition.ColumnCount;
                    errors.Add($"Level '{levelDefinition.name}' Arrow Box cell {i} at row {row}, column {column} color '{cell.ColorId}' is missing Arrow Stroke Tint in MarbleColorCatalog.");
                    isValid = false;
                }

                bool requiresLockedSprite = !cell.IsMysterySourceBox;
                if (!ValidateSourceBoxSprites(levelDefinition, cell.ColorId, sourceBoxColorEntry, requiresLockedSprite, $"SourceBox cell {i}", errors))
                {
                    isValid = false;
                }

                if (cell.IsMysterySourceBox && boardFeatureCatalog.MysterySourceBoxSprite == null)
                {
                    int row = i / levelDefinition.ColumnCount;
                    int column = i % levelDefinition.ColumnCount;
                    errors.Add($"Level '{levelDefinition.name}' Mystery SourceBox cell {i} at row {row}, column {column} is missing Mystery SourceBox sprite in BoardFeatureCatalog.");
                    isValid = false;
                }
            }

            return isValid;
        }

        private static bool ValidateSourceBoxSprites(
            LevelDefinition levelDefinition,
            MarbleColorId colorId,
            MarbleColorCatalog.Entry entry,
            bool requireLockedSourceBoxSprite,
            string usage,
            List<string> errors)
        {
            bool isValid = true;

            if (entry.SourceBoxSprite == null)
            {
                errors.Add($"Level '{levelDefinition.name}' {usage} color '{colorId}' is missing SourceBox sprite in MarbleColorCatalog.");
                isValid = false;
            }

            if (requireLockedSourceBoxSprite && entry.LockedSourceBoxSprite == null)
            {
                errors.Add($"Level '{levelDefinition.name}' {usage} color '{colorId}' is missing locked SourceBox sprite in MarbleColorCatalog.");
                isValid = false;
            }

            if (entry.MarbleSprite == null)
            {
                errors.Add($"Level '{levelDefinition.name}' {usage} color '{colorId}' is missing Marble sprite in MarbleColorCatalog.");
                isValid = false;
            }

            return isValid;
        }

        private bool ValidateSpawnerBuildTargets(LevelDefinition levelDefinition, List<string> errors)
        {
            bool isValid = true;
            IReadOnlyList<LevelCellData> cells = levelDefinition.Cells;

            for (int i = 0; i < cells.Count; i++)
            {
                LevelCellData cell = cells[i];
                if (cell == null || cell.CellType != LevelCellType.Spawner)
                {
                    continue;
                }

                int row = i / levelDefinition.ColumnCount;
                int column = i % levelDefinition.ColumnCount;
                if (cell.SpawnDirection == SpawnerDirection.Up)
                {
                    errors.Add($"Level '{levelDefinition.name}' Spawner cell {i} at row {row}, column {column} uses unsupported Up direction.");
                    isValid = false;
                }
            }

            return isValid;
        }

        private SourceBox SpawnSourceBoxAtCell(
            int row,
            int column,
            MarbleColorId colorId,
            int marbleCount,
            bool isMysterySourceBox,
            string objectName)
        {
            if (!colorCatalog.TryGetEntry(colorId, out MarbleColorCatalog.Entry colorEntry))
            {
                return null;
            }

            SourceBox prefab = boardFeatureCatalog.SourceBoxPrefab;

            if (prefab == null)
            {
                Debug.LogError($"{nameof(SourceBoxBoardController)} on '{name}' cannot spawn SourceBox at row {row}, column {column} because prefab is missing.", this);
                return null;
            }

            int index = GetCellIndex(row, column);
            Vector2Int coordinate = new Vector2Int(row, column);
            if (sourceBoxesByCell[index] != null || activeSourceBoxes.ContainsKey(coordinate))
            {
                Debug.LogError($"{nameof(SourceBoxBoardController)} on '{name}' cannot spawn SourceBox at occupied row {row}, column {column}.", this);
                return null;
            }

            if (!placeholderGridController.TryGetSourceBoxAnchor(index, out Transform sourceBoxAnchor))
            {
                Debug.LogError($"{nameof(SourceBoxBoardController)} on '{name}' cannot spawn SourceBox at row {row}, column {column} because the placeholder SourceBoxAnchor is missing.", this);
                return null;
            }

            SourceBox sourceBox = Instantiate(prefab, sourceBoxAnchor);
            sourceBox.name = objectName;
            sourceBox.transform.localPosition = Vector3.zero;
            sourceBox.transform.localRotation = Quaternion.identity;
            sourceBox.transform.localScale = Vector3.one;

            sourceBox.Initialize(
                colorId,
                marbleCount,
                index,
                isMysterySourceBox,
                OnSourceBoxReleased,
                colorEntry,
                isMysterySourceBox ? boardFeatureCatalog.MysterySourceBoxSprite : null,
                boardFeatureCatalog.MarblePrefab,
                releasedMarbleContainer,
                capacityController);

            sourceBox.SetReleaseOutputMultiplier(multiplierGateBoardController.GetMultiplierForSourceCell(index, currentLevel));

            sourceBox.ReleaseStarted -= OnSourceBoxReleaseStarted;
            sourceBox.ReleaseStarted += OnSourceBoxReleaseStarted;

            spawnedSourceBoxes.Add(sourceBox);
            activeSourceBoxes.Add(coordinate, sourceBox);
            sourceBoxesByCell[index] = sourceBox;
            SourceBoxSpawned?.Invoke(sourceBox);
            return sourceBox;
        }

        public int GetMultiplierForSourceCell(int cellIndex)
        {
            return multiplierGateBoardController.GetMultiplierForSourceCell(cellIndex, currentLevel);
        }

#if UNITY_EDITOR
        public void DebugCollectInitialBuildIssues(List<string> issues)
        {
            if (currentLevel == null)
            {
                issues.Add("BUILD SHORTFALL: Source board did not finish building.");
                return;
            }
            for (int i = 0; i < currentLevel.CellCount; i++)
            {
                LevelCellData cell = currentLevel.Cells[i];
                if (cell == null || cell.CellType != LevelCellType.SourceBox ||
                    IsCellSealed(new Vector2Int(i / currentLevel.ColumnCount, i % currentLevel.ColumnCount))) continue;
                int actual = sourceBoxesByCell != null && i < sourceBoxesByCell.Length && sourceBoxesByCell[i] != null
                    ? sourceBoxesByCell[i].MarbleCount : 0;
                if (actual != cell.MarbleCount)
                    issues.Add($"BUILD SHORTFALL: Source R{i / currentLevel.ColumnCount + 1} C{i % currentLevel.ColumnCount + 1} / {cell.ColorId}: expected {cell.MarbleCount}, created {actual}.");
            }
        }
#endif

        public bool TryCreateFieldMarble(MarbleColorId colorId, out Marble marble)
        {
            marble = null;
            if (boardFeatureCatalog?.MarblePrefab == null || releasedMarbleContainer == null ||
                colorCatalog == null || !colorCatalog.TryGetEntry(colorId, out MarbleColorCatalog.Entry colorEntry))
            {
                return false;
            }

            marble = Instantiate(boardFeatureCatalog.MarblePrefab, releasedMarbleContainer);
            marble.name = $"InitialConveyorMarble_{colorId}";
            marble.Initialize(colorId, colorEntry.MarbleSprite);
#if UNITY_EDITOR
            MarbleDebugTracker.SetOrigin(marble, "Initial conveyor seed");
#endif
            marble.SetRendererSortingOrders(
                InitialConveyorMarbleVisualSortingOrder,
                InitialConveyorMarbleOutlineSortingOrder);
            return true;
        }

        public bool TryCreateMultiplierGateDuplicate(Marble original, int gateRuntimeId, float horizontalDirection)
        {
            if (original == null || boardFeatureCatalog?.MarblePrefab == null || releasedMarbleContainer == null ||
                !colorCatalog.TryGetEntry(original.ColorId, out MarbleColorCatalog.Entry colorEntry))
            {
                return false;
            }

            Marble duplicate = Instantiate(boardFeatureCatalog.MarblePrefab, releasedMarbleContainer);
            duplicate.name = $"{original.name}_x2";
            duplicate.transform.rotation = original.transform.rotation;
            duplicate.transform.localScale = original.transform.localScale;
            duplicate.Initialize(original.ColorId, colorEntry.MarbleSprite, original.SourceCellIndex);
#if UNITY_EDITOR
            MarbleDebugTracker.MultiplierCreated(duplicate, original, gateRuntimeId);
#endif
            duplicate.CopyMultiplierGateStateFrom(original);
            duplicate.MarkMultiplierGateProcessed(gateRuntimeId);

            Rigidbody2D originalRigidbody = original.Rigidbody;
            Vector2 originalPhysicsPosition = originalRigidbody != null
                ? originalRigidbody.position
                : (Vector2)original.transform.position;
            float requiredCenterSeparation =
                (original.ColliderDiameter + duplicate.ColliderDiameter) * 0.5f *
                MultiplierGateDuplicateSeparationSafetyMultiplier;
            float direction = horizontalDirection >= 0f ? 1f : -1f;
            Vector3 duplicatePosition = new Vector3(
                originalPhysicsPosition.x + direction * requiredCenterSeparation,
                originalPhysicsPosition.y,
                original.transform.position.z);
            duplicate.transform.position = duplicatePosition;
            duplicate.Release();

            if (duplicate.Rigidbody != null && originalRigidbody != null)
            {
                duplicate.Rigidbody.linearVelocity = originalRigidbody.linearVelocity;
                duplicate.Rigidbody.angularVelocity = originalRigidbody.angularVelocity;
            }

            return true;
        }

        private bool HasPendingSpawnerReplacement(Vector2Int releasedCoordinate)
        {
            return pendingSpawnerReplacementCells.Contains(releasedCoordinate);
        }

        private void MarkGameplayCellVacated(Vector2Int coordinate)
        {
            if (activeGameplayCells.Contains(coordinate) && !HasPendingSpawnerReplacement(coordinate))
            {
                vacatedGameplayCells.Add(coordinate);
            }
        }

        private void ReservePendingSpawnerReplacementIfNeeded(Vector2Int releasedCoordinate)
        {
            for (int i = 0; i < activeSpawners.Count; i++)
            {
                SourceBoxSpawner spawner = activeSpawners[i];
                if (spawner != null && spawner.TargetCoordinate == releasedCoordinate && spawner.RemainingCount > 0)
                {
                    pendingSpawnerReplacementCells.Add(releasedCoordinate);
                    return;
                }
            }
        }

        private void BuildSpawnerAtCell(int row, int column, LevelCellData cell)
        {
            if (cell.SpawnDirection == SpawnerDirection.Up)
            {
                Debug.LogError($"{nameof(SourceBoxBoardController)} on '{name}' cannot build Spawner at row {row}, column {column} because Up direction is unsupported.", this);
                return;
            }

            Vector2Int coordinate = new Vector2Int(row, column);
            Vector2Int targetCoordinate = SourceBoxSpawner.CalculateTargetCoordinate(coordinate, cell.SpawnDirection);
            bool hasRuntimeTarget = IsInsideBoard(targetCoordinate.x, targetCoordinate.y) &&
                                    sourceBoxesByCell[GetCellIndex(targetCoordinate.x, targetCoordinate.y)] != null;
            bool hasSealedFeatureTarget = IsInsideBoard(targetCoordinate.x, targetCoordinate.y) &&
                                          IsCellSealed(targetCoordinate) &&
                                          currentLevel.Cells[GetCellIndex(targetCoordinate.x, targetCoordinate.y)]?.CellType == LevelCellType.SourceBox;
            if (!hasRuntimeTarget && !hasSealedFeatureTarget)
            {
                Debug.LogError($"{nameof(SourceBoxBoardController)} on '{name}' cannot build Spawner at row {row}, column {column} because target coordinate {targetCoordinate} is not an existing SourceBox.", this);
                return;
            }

            SourceBoxSpawner spawner = Instantiate(boardFeatureCatalog.SpawnerPrefab, placeholderGridController.GeneratedGridRoot);
            spawner.name = $"Spawner_{row}_{column}";
            spawner.transform.localPosition = placeholderGridController.CalculateCellLocalPosition(currentLevel.RowCount, currentLevel.ColumnCount, row, column, spawner.transform.localPosition.z);
            spawner.transform.localRotation = Quaternion.identity;
            spawner.transform.localScale = Vector3.one;

            if (!spawner.Initialize(
                coordinate,
                cell.SpawnDirection,
                cell.SpawnSequence,
                boardFeatureCatalog.DownSprite,
                boardFeatureCatalog.HorizontalSprite))
            {
                DestroyGameObject(spawner.gameObject);
                return;
            }

            spawnedSpawners.Add(spawner);
            activeSpawners.Add(spawner);
        }

        private bool TrySpawnFromSpawnerTargeting(Vector2Int releasedCoordinate)
        {
            for (int i = 0; i < activeSpawners.Count; i++)
            {
                SourceBoxSpawner spawner = activeSpawners[i];
                if (spawner == null || spawner.TargetCoordinate != releasedCoordinate)
                {
                    continue;
                }

                if (!spawner.TryConsumeNextColor(out MarbleColorId colorId))
                {
                    return false;
                }

                SourceBox sourceBox = SpawnSourceBoxAtCell(
                    releasedCoordinate.x,
                    releasedCoordinate.y,
                    colorId,
                    LevelCellData.MaxMarbleCount,
                    false,
                    $"SourceBox_Spawned_{releasedCoordinate.x}_{releasedCoordinate.y}_{spawner.CurrentSequenceIndex:00}");

                if (sourceBox != null)
                {
                    if (spawner.TryPlaySpawn(sourceBox, () =>
                        {
                            RecomputeSourceBoxAvailability();
                            RefreshSourceBoxInputStates();
                        }))
                    {
                        return true;
                    }

                    RemoveSourceBoxAt(releasedCoordinate, sourceBox);
                    Debug.LogError($"{nameof(SourceBoxBoardController)} on '{name}' spawned SourceBox at {releasedCoordinate}, but Spawner '{spawner.name}' could not play the spawn animation. The spawned SourceBox was removed to avoid a broken visual state.", this);
                }

                return false;
            }

            return false;
        }

        private void RemoveSourceBoxAt(Vector2Int coordinate, SourceBox sourceBox)
        {
            if (currentLevel != null && IsInsideBoard(coordinate.x, coordinate.y))
            {
                int index = GetCellIndex(coordinate.x, coordinate.y);
                if (sourceBoxesByCell != null && index >= 0 && index < sourceBoxesByCell.Length && sourceBoxesByCell[index] == sourceBox)
                {
                    sourceBoxesByCell[index] = null;
                }
            }

            activeSourceBoxes.Remove(coordinate);
            spawnedSourceBoxes.Remove(sourceBox);
            progressionGrantedSourceBoxes.Remove(sourceBox);

            if (sourceBox != null)
            {
                sourceBox.ReleaseStarted -= OnSourceBoxReleaseStarted;
                DestroyGameObject(sourceBox.gameObject);
            }
        }

        private void RollbackSealedSourceReveal(IReadOnlyList<Vector2Int> coordinates, List<SourceBox> revealedSourceBoxes)
        {
            for (int i = 0; i < revealedSourceBoxes.Count; i++)
            {
                SourceBox sourceBox = revealedSourceBoxes[i];
                sealedSourceBoxesAwaitingAvailability.Remove(sourceBox);
                if (sourceBox != null && TryGetCoordinateFromCellIndex(sourceBox.CellIndex, "sealed SourceBox reveal rollback", out Vector2Int coordinate))
                {
                    RemoveSourceBoxAt(coordinate, sourceBox);
                }
            }

            for (int i = 0; i < coordinates.Count; i++)
            {
                Vector2Int coordinate = coordinates[i];
                activeGameplayCells.Remove(coordinate);
                vacatedGameplayCells.Remove(coordinate);
                if (IsInsideBoard(coordinate.x, coordinate.y))
                {
                    placeholderGridController.SetSpriteVisible(GetCellIndex(coordinate.x, coordinate.y), false);
                }
            }

            revealedSourceBoxes.Clear();
        }

        private bool IsInsideBoard(int row, int column)
        {
            return currentLevel != null &&
                   row >= 0 &&
                   row < currentLevel.RowCount &&
                   column >= 0 &&
                   column < currentLevel.ColumnCount;
        }

        private int GetCellIndex(int row, int column)
        {
            return row * currentLevel.ColumnCount + column;
        }

        private void ClearReleasedMarbles()
        {
            if (releasedMarbleContainer == null)
            {
                return;
            }

            Marble[] releasedMarbles = releasedMarbleContainer.GetComponentsInChildren<Marble>(true);
            for (int i = 0; i < releasedMarbles.Length; i++)
            {
                Marble marble = releasedMarbles[i];
                if (marble == null)
                {
                    continue;
                }

                DestroyGameObject(marble.gameObject);
            }
        }

        private void RefreshSourceBoxInputStates()
        {
            for (int i = 0; i < spawnedSourceBoxes.Count; i++)
            {
                SourceBox sourceBox = spawnedSourceBoxes[i];
                if (sourceBox != null)
                {
                    sourceBox.RefreshInputState();
                }
            }

            for (int i = 0; i < recoveredSourceBoxes.Count; i++)
            {
                SourceBox sourceBox = recoveredSourceBoxes[i];
                if (sourceBox != null)
                {
                    sourceBox.RefreshInputState();
                }
            }
        }

        private void SubscribeToCapacityChanges()
        {
            if (capacityController != null)
            {
                capacityController.CountsChanged -= RefreshSourceBoxInputStates;
                capacityController.CountsChanged += RefreshSourceBoxInputStates;
            }
        }

        private void UnsubscribeFromCapacityChanges()
        {
            if (capacityController != null)
            {
                capacityController.CountsChanged -= RefreshSourceBoxInputStates;
            }
        }

        private static void DestroyGameObject(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
