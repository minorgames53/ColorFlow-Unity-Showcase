#if false // Kept only so Unity can retain the original asset GUID after moving the compiled script to the parent folder.
using System.Collections.Generic;
using Gameplay.Conveyor;
using Gameplay.Levels;
using Gameplay.SourceBoxes;
using UnityEngine;

namespace Gameplay.BoardFeatures.ConnectedBoxes
{
    /// <summary>Owns level-defined Connected Box pairs, their board views, and paired release coordination.</summary>
    public sealed class ConnectedBoxBoardController
    {
        private readonly List<RuntimePair> activePairs = new List<RuntimePair>();
        private readonly Dictionary<SourceBox, RuntimePair> pairsBySourceBox = new Dictionary<SourceBox, RuntimePair>();

        private sealed class RuntimePair
        {
            public SourceBox First;
            public SourceBox Second;
            public ConnectionController Connection;
            public GameObject FirstClosedStroke;
            public GameObject FirstOpenStroke;
            public GameObject SecondClosedStroke;
            public GameObject SecondOpenStroke;
        }

        public bool Build(
            LevelDefinition level,
            SourceBoxBoardController boardController,
            BoardFeatureCatalog featureCatalog,
            MarbleColorCatalog colorCatalog,
            Transform boardRoot)
        {
            Clear();
            if (level == null || boardController == null || featureCatalog == null || colorCatalog == null || boardRoot == null)
            {
                return false;
            }

            Dictionary<int, List<int>> pairCellIndices = CollectPairCellIndices(level);
            if (pairCellIndices.Count == 0)
            {
                return true;
            }

            if (featureCatalog.ConnectionPrefab == null)
            {
                return false;
            }

            foreach (KeyValuePair<int, List<int>> pairEntry in pairCellIndices)
            {
                List<int> cellIndices = pairEntry.Value;
                if (cellIndices.Count != 2 ||
                    !boardController.TryGetSourceBoxByCellIndex(cellIndices[0], out SourceBox first) ||
                    !boardController.TryGetSourceBoxByCellIndex(cellIndices[1], out SourceBox second) ||
                    !colorCatalog.TryGetEntry(first.ColorId, out MarbleColorCatalog.Entry firstColor) ||
                    !colorCatalog.TryGetEntry(second.ColorId, out MarbleColorCatalog.Entry secondColor))
                {
                    Clear();
                    return false;
                }

                ConnectionController connection = Object.Instantiate(featureCatalog.ConnectionPrefab, boardRoot);
                connection.name = $"Connection_{pairEntry.Key}";
                if (!connection.Initialize(first.transform.position, second.transform.position, firstColor.ConnectedBoxConnectionTint, secondColor.ConnectedBoxConnectionTint))
                {
                    Object.Destroy(connection.gameObject);
                    Clear();
                    return false;
                }

                RuntimePair pair = new RuntimePair
                {
                    First = first,
                    Second = second,
                    Connection = connection,
                    FirstClosedStroke = CreateStroke(featureCatalog.ConnectedBoxClosedStrokePrefab, first),
                    FirstOpenStroke = CreateStroke(featureCatalog.ConnectedBoxOpenStrokePrefab, first),
                    SecondClosedStroke = CreateStroke(featureCatalog.ConnectedBoxClosedStrokePrefab, second),
                    SecondOpenStroke = CreateStroke(featureCatalog.ConnectedBoxOpenStrokePrefab, second)
                };

                if (pair.FirstClosedStroke == null || pair.FirstOpenStroke == null || pair.SecondClosedStroke == null || pair.SecondOpenStroke == null)
                {
                    DestroyPairObjects(pair);
                    Clear();
                    return false;
                }

                activePairs.Add(pair);
                pairsBySourceBox.Add(first, pair);
                pairsBySourceBox.Add(second, pair);
                first.StateChanged += OnSourceBoxStateChanged;
                second.StateChanged += OnSourceBoxStateChanged;
                RefreshPairStrokes(pair);
            }

            return true;
        }

        public void Clear()
        {
            for (int i = 0; i < activePairs.Count; i++)
            {
                DisposePair(activePairs[i]);
            }

            activePairs.Clear();
            pairsBySourceBox.Clear();
        }

        public bool IsConnected(SourceBox sourceBox)
        {
            return sourceBox != null && pairsBySourceBox.ContainsKey(sourceBox);
        }

        public SourceBoxReleaseResult TryRelease(SourceBox tappedSourceBox, MarbleCapacityController capacityController)
        {
            if (tappedSourceBox == null || !pairsBySourceBox.TryGetValue(tappedSourceBox, out RuntimePair pair))
            {
                return tappedSourceBox == null ? SourceBoxReleaseResult.InvalidState : tappedSourceBox.TryReleaseMarblesWithResult();
            }

            if (!tappedSourceBox.CanAttemptReleaseByUser)
            {
                return tappedSourceBox.TryReleaseMarblesWithResult();
            }

            SourceBox partner = pair.First == tappedSourceBox ? pair.Second : pair.First;
            if (partner == null || !tappedSourceBox.CanReleaseAsConnectedPairMember() || !partner.CanReleaseAsConnectedPairMember())
            {
                return SourceBoxReleaseResult.InvalidState;
            }

            int totalMarbleCount = tappedSourceBox.MarbleCount + partner.MarbleCount;
            if (capacityController == null || !capacityController.TryReserve(totalMarbleCount))
            {
                return SourceBoxReleaseResult.BoardFull;
            }

            SourceBoxReleaseResult tappedResult = tappedSourceBox.TryReleaseWithReservedCapacity();
            if (tappedResult != SourceBoxReleaseResult.Success)
            {
                capacityController.CancelReservation(totalMarbleCount);
                return tappedResult;
            }

            SourceBoxReleaseResult partnerResult = partner.TryReleaseWithReservedCapacity();
            if (partnerResult != SourceBoxReleaseResult.Success)
            {
                Debug.LogError($"{nameof(ConnectedBoxBoardController)} could not start both releases after capacity was reserved. Connected Box pair was left in an invalid partial state.", partner);
                return partnerResult;
            }

            ConsumePair(pair);
            return SourceBoxReleaseResult.Success;
        }

        private static Dictionary<int, List<int>> CollectPairCellIndices(LevelDefinition level)
        {
            Dictionary<int, List<int>> result = new Dictionary<int, List<int>>();
            IReadOnlyList<LevelCellData> cells = level.Cells;
            for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
            {
                LevelCellData cell = cells[cellIndex];
                if (cell == null || !cell.HasConnectedBoxPair)
                {
                    continue;
                }

                if (!result.TryGetValue(cell.ConnectedBoxPairId, out List<int> pairCells))
                {
                    pairCells = new List<int>(2);
                    result.Add(cell.ConnectedBoxPairId, pairCells);
                }

                pairCells.Add(cellIndex);
            }

            return result;
        }

        private static GameObject CreateStroke(GameObject prefab, SourceBox sourceBox)
        {
            if (prefab == null || sourceBox == null)
            {
                return null;
            }

            GameObject stroke = Object.Instantiate(prefab, sourceBox.transform);
            stroke.transform.localPosition = Vector3.zero;
            stroke.transform.localRotation = Quaternion.identity;
            stroke.transform.localScale = Vector3.one;
            return stroke;
        }

        private void OnSourceBoxStateChanged(SourceBox sourceBox)
        {
            if (sourceBox != null && pairsBySourceBox.TryGetValue(sourceBox, out RuntimePair pair))
            {
                RefreshPairStrokes(pair);
            }
        }

        private static void RefreshPairStrokes(RuntimePair pair)
        {
            SetStrokeState(pair.First, pair.FirstClosedStroke, pair.FirstOpenStroke);
            SetStrokeState(pair.Second, pair.SecondClosedStroke, pair.SecondOpenStroke);
        }

        private static void SetStrokeState(SourceBox sourceBox, GameObject closedStroke, GameObject openStroke)
        {
            bool isReleased = sourceBox == null || sourceBox.CurrentState == SourceBoxState.Released;
            if (closedStroke != null)
            {
                closedStroke.SetActive(!isReleased && sourceBox.CurrentState == SourceBoxState.Locked);
            }

            if (openStroke != null)
            {
                openStroke.SetActive(!isReleased && sourceBox.CurrentState == SourceBoxState.Available);
            }
        }

        private void ConsumePair(RuntimePair pair)
        {
            activePairs.Remove(pair);
            pairsBySourceBox.Remove(pair.First);
            pairsBySourceBox.Remove(pair.Second);
            DisposePair(pair);
        }

        private void DisposePair(RuntimePair pair)
        {
            if (pair == null)
            {
                return;
            }

            if (pair.First != null)
            {
                pair.First.StateChanged -= OnSourceBoxStateChanged;
            }

            if (pair.Second != null)
            {
                pair.Second.StateChanged -= OnSourceBoxStateChanged;
            }

            DestroyPairObjects(pair);
        }

        private static void DestroyPairObjects(RuntimePair pair)
        {
            if (pair.Connection != null)
            {
                Object.Destroy(pair.Connection.gameObject);
            }

            DestroyObject(pair.FirstClosedStroke);
            DestroyObject(pair.FirstOpenStroke);
            DestroyObject(pair.SecondClosedStroke);
            DestroyObject(pair.SecondOpenStroke);
        }

        private static void DestroyObject(GameObject target)
        {
            if (target != null)
            {
                Object.Destroy(target);
            }
        }
    }
}
#endif
