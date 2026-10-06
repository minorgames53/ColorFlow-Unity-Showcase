using System.Collections.Generic;
using DG.Tweening;
using Gameplay.Conveyor;
using Gameplay.Levels;
using Gameplay.SourceBoxes;
using UnityEngine;

namespace Gameplay.BoardFeatures.ConnectedBoxes
{
    /// <summary>Owns level-defined Connected Box pairs, their board views, and paired release coordination.</summary>
    public sealed class ConnectedBoxBoardController
    {
        private const float PartnerReleaseDelay = 0.1f;

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
            public Tween PartnerReleaseDelayTween;
            public MarbleCapacityController PendingCapacityController;
            public int PendingReservationCount;
            public bool PartnerReleaseStarted;
        }

        public bool Build(LevelDefinition level, SourceBoxBoardController boardController, BoardFeatureCatalog featureCatalog, MarbleColorCatalog colorCatalog, Transform boardRoot)
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
                if (!connection.Initialize(first.PresentationRoot, second.PresentationRoot, firstColor.ConnectedBoxConnectionTint, secondColor.ConnectedBoxConnectionTint))
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
                first.PresentationChanged += OnSourceBoxStateChanged;
                second.PresentationChanged += OnSourceBoxStateChanged;
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

        public bool TryGetPartner(SourceBox sourceBox, out SourceBox partner)
        {
            partner = null;
            if (sourceBox == null || !pairsBySourceBox.TryGetValue(sourceBox, out RuntimePair pair))
            {
                return false;
            }

            partner = pair.First == sourceBox ? pair.Second : pair.First;
            return partner != null;
        }

        public void ConsumeAfterExternalRelease(SourceBox sourceBox)
        {
            if (sourceBox != null && pairsBySourceBox.TryGetValue(sourceBox, out RuntimePair pair))
            {
                ConsumePair(pair);
            }
        }

        public SourceBoxReleaseResult TryRelease(SourceBox tappedSourceBox, MarbleCapacityController capacityController)
        {
            return TryRelease(tappedSourceBox, capacityController, false);
        }

        public SourceBoxReleaseResult TryRelease(
            SourceBox tappedSourceBox,
            MarbleCapacityController capacityController,
            bool bypassTappedAvailability)
        {
            if (tappedSourceBox == null || !pairsBySourceBox.TryGetValue(tappedSourceBox, out RuntimePair pair))
            {
                if (tappedSourceBox == null)
                {
                    return SourceBoxReleaseResult.InvalidState;
                }

                return bypassTappedAvailability
                    ? tappedSourceBox.TryReleaseWithHandBooster()
                    : tappedSourceBox.TryReleaseMarblesWithResult();
            }

            if (bypassTappedAvailability)
            {
                if (!tappedSourceBox.CanReleaseByHandBooster)
                {
                    return SourceBoxReleaseResult.InvalidState;
                }
            }
            else if (!tappedSourceBox.CanAttemptReleaseByUser)
            {
                return tappedSourceBox.TryReleaseMarblesWithResult();
            }

            SourceBox partner = pair.First == tappedSourceBox ? pair.Second : pair.First;
            if (partner == null || !tappedSourceBox.CanReleaseAsConnectedPairMember() || !partner.CanReleaseAsConnectedPairMember())
            {
                return SourceBoxReleaseResult.InvalidState;
            }

            int totalMarbleCount = tappedSourceBox.RequiredReleaseCapacity + partner.RequiredReleaseCapacity;
            if (capacityController == null || !capacityController.TryReserve(totalMarbleCount))
            {
                return SourceBoxReleaseResult.BoardFull;
            }

            SourceBoxReleaseResult tappedResult = bypassTappedAvailability
                ? tappedSourceBox.TryReleaseWithReservedCapacityAsHandBooster()
                : tappedSourceBox.TryReleaseWithReservedCapacity();
            if (tappedResult != SourceBoxReleaseResult.Success)
            {
                capacityController.CancelReservation(totalMarbleCount);
                return tappedResult;
            }

            partner.SetInteractionBlocked(true);
            pair.PendingCapacityController = capacityController;
            pair.PendingReservationCount = partner.RequiredReleaseCapacity;
            pair.PartnerReleaseStarted = false;
            pair.PartnerReleaseDelayTween = DOVirtual
                .DelayedCall(PartnerReleaseDelay, () => StartPartnerRelease(pair, partner))
                .SetLink(partner.gameObject, LinkBehaviour.KillOnDestroy)
                .OnKill(() => OnPartnerReleaseDelayKilled(pair));
            return SourceBoxReleaseResult.Success;
        }

        private void StartPartnerRelease(RuntimePair pair, SourceBox partner)
        {
            if (pair == null || pair.PartnerReleaseStarted)
            {
                return;
            }

            pair.PartnerReleaseStarted = true;
            pair.PartnerReleaseDelayTween = null;

            SourceBoxReleaseResult partnerResult = partner != null
                ? partner.TryReleaseWithReservedCapacity()
                : SourceBoxReleaseResult.InvalidState;

            if (partnerResult != SourceBoxReleaseResult.Success)
            {
                if (partner != null)
                {
                    partner.SetInteractionBlocked(false);
                }

                CancelPendingPartnerReservation(pair);
                Debug.LogError($"{nameof(ConnectedBoxBoardController)} could not start the partner release after the paired capacity was reserved.", partner);
                ConsumePair(pair);
                return;
            }

            pair.PendingReservationCount = 0;
            pair.PendingCapacityController = null;
            ConsumePair(pair);
        }

        private static void OnPartnerReleaseDelayKilled(RuntimePair pair)
        {
            if (pair == null)
            {
                return;
            }

            pair.PartnerReleaseDelayTween = null;
            if (pair.PartnerReleaseStarted || pair.PendingReservationCount <= 0)
            {
                return;
            }

            CancelPendingPartnerReservation(pair);

            SourceBox pendingPartner = pair.First != null && pair.First.CurrentState != SourceBoxState.Released
                ? pair.First
                : pair.Second;
            if (pendingPartner != null && pendingPartner.CurrentState != SourceBoxState.Released)
            {
                pendingPartner.SetInteractionBlocked(false);
            }
        }

        private static void CancelPendingPartnerReservation(RuntimePair pair)
        {
            if (pair == null || pair.PendingReservationCount <= 0)
            {
                return;
            }

            pair.PendingCapacityController?.CancelReservation(pair.PendingReservationCount);
            pair.PendingReservationCount = 0;
            pair.PendingCapacityController = null;
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

            Transform parent = sourceBox.PresentationRoot != null ? sourceBox.PresentationRoot : sourceBox.transform;
            GameObject stroke = Object.Instantiate(prefab, parent);
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
                closedStroke.SetActive(!isReleased && !sourceBox.IsPresentedOpen);
            }

            if (openStroke != null)
            {
                openStroke.SetActive(!isReleased && sourceBox.IsPresentedOpen);
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

            if (pair.PartnerReleaseDelayTween != null && pair.PartnerReleaseDelayTween.IsActive())
            {
                Tween delayTween = pair.PartnerReleaseDelayTween;
                pair.PartnerReleaseDelayTween = null;
                delayTween.Kill(false);
            }

            OnPartnerReleaseDelayKilled(pair);

            if (pair.First != null)
            {
                pair.First.StateChanged -= OnSourceBoxStateChanged;
                pair.First.PresentationChanged -= OnSourceBoxStateChanged;
            }

            if (pair.Second != null)
            {
                pair.Second.StateChanged -= OnSourceBoxStateChanged;
                pair.Second.PresentationChanged -= OnSourceBoxStateChanged;
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
