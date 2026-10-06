using System;
using System.Collections.Generic;
using DG.Tweening;
using Game.Shared.Audio;
using Game.Shared.Haptics;
using Gameplay.Levels;
using Gameplay.SourceBoxes;
using UnityEngine;
#if UNITY_EDITOR
using Gameplay.MarbleDebug;
#endif

namespace Gameplay.Conveyor
{
    [RequireComponent(typeof(Collider2D))]
    public sealed class ConveyorEntryZone : MonoBehaviour, IGameplaySpeedTarget
    {
        [Header("References")]
        [SerializeField] private ConveyorController conveyorController;
        [SerializeField] private Collider2D entryTrigger;
        [SerializeField] private LevelSessionController levelSessionController;

        [Header("Detection")]
        [SerializeField] private LayerMask marbleLayerMask = Physics2D.DefaultRaycastLayers;
        [SerializeField, Min(0.01f)] private float marbleRayDistance = 6f;

        [Header("Entry Pull")]
        [SerializeField, Min(0.01f)] private float entryPullDuration = 0.18f;
        [SerializeField, Min(0f)] private float entryCatchUpStrength = 0.02f;
        [SerializeField, Min(0.01f)] private float minimumEntryPullDuration = 0.08f;
        [SerializeField, Min(0.01f)] private float maximumEntryPullDuration = 0.24f;
        [SerializeField] private Ease entryPullEase = Ease.OutQuad;
        [SerializeField, Min(0f)] private float entryArcHeight = 0.1f;
        [SerializeField] private Vector3 conveyorMarbleScale = Vector3.one;

        private readonly List<Marble> pendingMarbles = new List<Marble>();
        private readonly List<Marble> cleanupDeferredMarbles = new List<Marble>();
        private readonly List<ConveyorSlot> slotsInEntryZone = new List<ConveyorSlot>();
        private readonly List<EntryTransfer> activeTransfers = new List<EntryTransfer>();
        private float gameplaySpeedMultiplier = 1f;
        private bool ufoMovementFrozen;
        private bool cleanupEntryAcceptanceBlocked;

        public int ActiveTransferMarbleCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < activeTransfers.Count; i++)
                {
                    if (IsEligibleActiveTransfer(activeTransfers[i]))
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public bool HasActiveTransferMarbleWithColor(MarbleColorId colorId)
        {
            if (!MarbleColorCatalog.IsGameplayColor(colorId))
            {
                return false;
            }

            for (int i = 0; i < activeTransfers.Count; i++)
            {
                EntryTransfer transfer = activeTransfers[i];
                if (IsEligibleActiveTransfer(transfer) && transfer.Marble.ColorId == colorId)
                {
                    return true;
                }
            }

            return false;
        }

        public event Action TransferStateChanged;

        private sealed class EntryTransfer
        {
            public ConveyorSlot Slot;
            public Marble Marble;
            public Tween Tween;
            public Vector3 StartPosition;
            public float LockedZ;
            public bool ResumeAfterUfoFreeze;
        }

        private void Reset()
        {
            entryTrigger = GetComponent<Collider2D>();
            EnsureTrigger();
        }

        private void Awake()
        {
            CacheLevelSessionController();
        }

        private void OnValidate()
        {
            marbleRayDistance = Mathf.Max(0.01f, marbleRayDistance);
            entryPullDuration = Mathf.Max(0.01f, entryPullDuration);
            entryCatchUpStrength = Mathf.Max(0f, entryCatchUpStrength);
            minimumEntryPullDuration = Mathf.Max(0.01f, minimumEntryPullDuration);
            maximumEntryPullDuration = Mathf.Max(minimumEntryPullDuration, maximumEntryPullDuration);
            EnsureTrigger();
        }

        private void Update()
        {
            if (levelSessionController == null)
            {
                CacheLevelSessionController();
            }

            if (levelSessionController == null ||
                (levelSessionController.GameplayState != GameplaySessionState.Playing &&
                 levelSessionController.GameplayState != GameplaySessionState.Recovering) ||
                ufoMovementFrozen)
            {
                return;
            }

            RemoveInvalidPendingMarbles();
            RemoveInvalidTransfers();
            TryStartEntriesForEmptySlots();
        }

        private void OnDisable()
        {
            ClearQueue();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (levelSessionController == null ||
                (levelSessionController.GameplayState != GameplaySessionState.Playing &&
                 levelSessionController.GameplayState != GameplaySessionState.Recovering))
            {
                return;
            }

            Marble marble = GetValidMarble(other);
            if (marble != null)
            {
                if (cleanupEntryAcceptanceBlocked)
                {
                    if (!cleanupDeferredMarbles.Contains(marble))
                    {
                        cleanupDeferredMarbles.Add(marble);
#if UNITY_EDITOR
                        MarbleDebugTracker.SetLocation(marble, MarbleDebugLocation.DropZone, this,
                            $"Entry #{GetInstanceID()} / deferred queue", "Recovery-blocked entry accepted");
#endif
                    }

                    return;
                }

                if (AddPendingMarble(marble) && !ufoMovementFrozen)
                {
                    // Acquire an available conveyor slot while the marble is known to be
                    // inside the trigger. At low render rates, physics can otherwise send
                    // Enter and Exit before Update gets a chance to process the queue.
                    TryStartEntriesForEmptySlots();
                }
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            Marble marble = GetValidMarble(other);
            cleanupDeferredMarbles.Remove(marble);
            if (marble != null && !IsTransferMarble(marble))
            {
                pendingMarbles.Remove(marble);
#if UNITY_EDITOR
                MarbleDebugTracker.ReleasedToField(marble);
#endif
                return;
            }

        }

        public void ClearQueue()
        {
            for (int i = 0; i < activeTransfers.Count; i++)
            {
                EntryTransfer transfer = activeTransfers[i];
                transfer.Tween?.Kill(false);
                transfer.Slot?.CancelReservation();
            }

            activeTransfers.Clear();
            pendingMarbles.Clear();
            cleanupDeferredMarbles.Clear();
            slotsInEntryZone.Clear();
            cleanupEntryAcceptanceBlocked = false;
            TransferStateChanged?.Invoke();
        }

        public void SetCleanupEntryAcceptanceBlocked(bool isBlocked)
        {
            if (cleanupEntryAcceptanceBlocked == isBlocked)
            {
                return;
            }

            cleanupEntryAcceptanceBlocked = isBlocked;
            if (isBlocked)
            {
                cleanupDeferredMarbles.Clear();
                return;
            }

            for (int i = 0; i < cleanupDeferredMarbles.Count; i++)
            {
                AddPendingMarble(cleanupDeferredMarbles[i]);
            }

            cleanupDeferredMarbles.Clear();
            if (levelSessionController == null ||
                (levelSessionController.GameplayState != GameplaySessionState.Playing &&
                 levelSessionController.GameplayState != GameplaySessionState.Recovering) ||
                ufoMovementFrozen)
            {
                return;
            }

            RemoveInvalidPendingMarbles();
            RemoveInvalidTransfers();
            TryStartEntriesForEmptySlots();
        }

        public void ResumeAfterRecovery()
        {
            if (levelSessionController == null || !levelSessionController.IsPlaying || ufoMovementFrozen)
            {
                return;
            }

            RemoveInvalidPendingMarbles();
            RemoveInvalidTransfers();
            TryStartEntriesForEmptySlots();
        }

        public void SetGameplaySpeedMultiplier(float multiplier)
        {
            gameplaySpeedMultiplier = Mathf.Max(0.01f, multiplier);
        }

        public void SetUfoMovementFrozen(bool isFrozen)
        {
            if (ufoMovementFrozen == isFrozen)
            {
                return;
            }

            ufoMovementFrozen = isFrozen;
#if UNITY_EDITOR
            MarbleDebugTracker.SuspendTransfers(this, isFrozen);
#endif
            for (int i = 0; i < activeTransfers.Count; i++)
            {
                EntryTransfer transfer = activeTransfers[i];
                if (transfer?.Tween == null || !transfer.Tween.IsActive())
                {
                    continue;
                }

                if (isFrozen)
                {
                    transfer.ResumeAfterUfoFreeze = transfer.Tween.IsPlaying();
                    transfer.Tween.Pause();
                }
                else if (transfer.ResumeAfterUfoFreeze)
                {
                    transfer.ResumeAfterUfoFreeze = false;
                    transfer.Tween.Play();
                }
            }
        }

        public int AppendActiveTransferMarblesForUfo(List<Marble> results)
        {
            return AppendEligibleActiveTransferMarbles(results);
        }

        public int AppendMarblesForRecovery(List<Marble> results)
        {
            if (results == null)
            {
                return 0;
            }

            int initialCount = results.Count;
            for (int i = 0; i < pendingMarbles.Count; i++)
            {
                Marble marble = pendingMarbles[i];
                if (marble != null && marble.gameObject.activeInHierarchy &&
                    marble.CanBeOwnedByUfo && !results.Contains(marble))
                {
                    results.Add(marble);
                }
            }

            AppendEligibleActiveTransferMarbles(results);
            return results.Count - initialCount;
        }

        private int AppendEligibleActiveTransferMarbles(List<Marble> results)
        {
            if (results == null)
            {
                return 0;
            }

            int addedCount = 0;
            for (int i = 0; i < activeTransfers.Count; i++)
            {
                EntryTransfer transfer = activeTransfers[i];
                Marble marble = transfer?.Marble;
                if (!IsEligibleActiveTransfer(transfer) || results.Contains(marble))
                {
                    continue;
                }

                results.Add(marble);
                addedCount++;
            }

            return addedCount;
        }

        private static bool IsEligibleActiveTransfer(EntryTransfer transfer)
        {
            Marble marble = transfer?.Marble;
            return transfer?.Slot != null &&
                   transfer.Slot.State == ConveyorSlotState.ReservedForEntry &&
                   marble != null && marble.gameObject.activeInHierarchy &&
                   marble.CanBeOwnedByUfo;
        }

        public bool TryDetachActiveTransferForUfo(Marble marble)
        {
            return TryDetachActiveTransfer(marble);
        }

        public bool TryDetachMarbleForRecovery(Marble marble)
        {
            if (TryDetachActiveTransfer(marble))
            {
                return true;
            }

            if (marble == null)
            {
                return false;
            }

            bool wasPending = pendingMarbles.Remove(marble);
            bool wasDeferred = cleanupDeferredMarbles.Remove(marble);
            if (!wasPending && !wasDeferred &&
                (!marble.gameObject.activeInHierarchy || !marble.CanBeOwnedByUfo ||
                 (conveyorController != null && conveyorController.IsMarbleOnConveyor(marble))))
            {
                return false;
            }

            conveyorController?.NotifyExternalFieldMarbleRemoved();
#if UNITY_EDITOR
            MarbleDebugTracker.SetLocation(marble, MarbleDebugLocation.InTransit, null,
                "Entry / field detached", "Recovery entry detach committed");
#endif
            TransferStateChanged?.Invoke();
            return true;
        }

        private bool TryDetachActiveTransfer(Marble marble)
        {
            if (marble == null || conveyorController == null)
            {
                return false;
            }

            for (int i = 0; i < activeTransfers.Count; i++)
            {
                EntryTransfer transfer = activeTransfers[i];
                if (transfer?.Marble != marble || transfer.Slot == null ||
                    !conveyorController.TryConsumeReservedEntryForUfo(transfer.Slot))
                {
                    continue;
                }

                transfer.Tween?.Kill(false);
                transfer.Tween = null;
                activeTransfers.RemoveAt(i);
#if UNITY_EDITOR
                MarbleDebugTracker.SetLocation(marble, MarbleDebugLocation.InTransit, null,
                    "Entry transfer detached", "Entry transfer detached for UFO / recovery");
#endif
                TransferStateChanged?.Invoke();
                return true;
            }

            return false;
        }

        private void CacheLevelSessionController()
        {
            if (levelSessionController == null)
            {
                levelSessionController = FindFirstObjectByType<LevelSessionController>(FindObjectsInactive.Include);
            }
        }

        private void TryStartEntriesForEmptySlots()
        {
            if (conveyorController == null || pendingMarbles.Count == 0)
            {
                return;
            }

            SortPendingMarblesByLowestY();
            conveyorController.GetEmptySlotsInside(entryTrigger, slotsInEntryZone);
            if (slotsInEntryZone.Count == 0)
            {
                return;
            }

            SortSlotsByEntryOrder();
            for (int i = 0; i < slotsInEntryZone.Count; i++)
            {
                ConveyorSlot slot = slotsInEntryZone[i];
                if (slot == null || !slot.IsEmpty || slot.MarbleAnchor == null)
                {
                    continue;
                }

                Marble marble = FindMarbleAboveSlot(slot);
                if (marble == null)
                {
                    continue;
                }

                if (!conveyorController.TryReserveSlotForEntry(slot))
                {
                    continue;
                }

                pendingMarbles.Remove(marble);
                StartEntryTransfer(slot, marble);
            }
        }

        private void StartEntryTransfer(ConveyorSlot slot, Marble marble)
        {
            marble.PrepareForConveyorControl();

            Transform marbleTransform = marble.transform;
            marbleTransform.localScale = conveyorMarbleScale;
            EntryTransfer transfer = new EntryTransfer
            {
                Slot = slot,
                Marble = marble,
                StartPosition = marbleTransform.position,
                LockedZ = marbleTransform.position.z
            };

            activeTransfers.Add(transfer);
#if UNITY_EDITOR
            MarbleDebugTracker.SetLocation(marble, MarbleDebugLocation.InTransit, this,
                $"Entry #{GetInstanceID()} -> Conveyor slot {slot.SlotIndex}", "Entry transfer registered");
#endif
            TransferStateChanged?.Invoke();
            PlayEntryTween(transfer);
            if (ufoMovementFrozen && transfer.Tween != null)
            {
                transfer.ResumeAfterUfoFreeze = true;
                transfer.Tween.Pause();
            }
        }

        private void PlayEntryTween(EntryTransfer transfer)
        {
            Transform marbleTransform = transfer.Marble.transform;
            float distance = Vector3.Distance(transfer.StartPosition, transfer.Slot.MarbleAnchor.position);
            float duration = Mathf.Clamp(
                entryPullDuration - distance * entryCatchUpStrength,
                minimumEntryPullDuration,
                maximumEntryPullDuration);
            duration = GetEffectiveDuration(duration);

            float progress = 0f;
            transfer.Tween = DOTween
                .To(
                    () => progress,
                    value =>
                    {
                        progress = value;
                        if (marbleTransform != null && transfer.Slot != null && transfer.Slot.MarbleAnchor != null)
                        {
                            marbleTransform.position = EvaluateEntryPosition(transfer, value);
                        }
                    },
                    1f,
                    duration)
                .SetEase(entryPullEase)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy)
                .OnComplete(() => CompleteEntryTransfer(transfer));
        }

        private float GetEffectiveDuration(float duration)
        {
            return Mathf.Max(0f, duration) / gameplaySpeedMultiplier;
        }

        private Vector3 EvaluateEntryPosition(EntryTransfer transfer, float progress)
        {
            Vector3 targetPosition = transfer.Slot.MarbleAnchor.position;
            targetPosition.z = transfer.LockedZ;

            Vector3 position = Vector3.LerpUnclamped(transfer.StartPosition, targetPosition, progress);
            if (entryArcHeight > 0f)
            {
                position.y += Mathf.Sin(progress * Mathf.PI) * entryArcHeight;
            }

            return position;
        }

        private void CompleteEntryTransfer(EntryTransfer transfer)
        {
            if (transfer == null)
            {
                return;
            }

            activeTransfers.Remove(transfer);
            TransferStateChanged?.Invoke();

            if (transfer.Slot == null ||
                transfer.Marble == null ||
                conveyorController == null ||
                !conveyorController.TryAssignReservedSlot(transfer.Slot, transfer.Marble, conveyorMarbleScale))
            {
                transfer.Slot?.CancelReservation();
                if (transfer.Marble != null)
                {
                    AddPendingMarble(transfer.Marble);
                }

                return;
            }

            AudioManager.Instance?.PlaySfx(AudioKey.MarbleDropToConveyor);
            HapticManager.Instance?.Play(HapticType.Rigid, true);
        }

        private Marble FindMarbleAboveSlot(ConveyorSlot slot)
        {
            Vector2 origin = slot.MarbleAnchor.position;
            RaycastHit2D[] hits = Physics2D.RaycastAll(origin, Vector2.up, marbleRayDistance, marbleLayerMask);
            if (hits == null || hits.Length == 0)
            {
                return null;
            }

            Marble bestMarble = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D hitCollider = hits[i].collider;
                Marble marble = GetValidMarble(hitCollider);
                if (marble == null ||
                    !pendingMarbles.Contains(marble) ||
                    IsTransferMarble(marble) ||
                    conveyorController.IsMarbleOnConveyor(marble) ||
                    HasLowerPendingMarble(marble))
                {
                    continue;
                }

                if (hits[i].distance >= bestDistance)
                {
                    continue;
                }

                bestMarble = marble;
                bestDistance = hits[i].distance;
            }

            return bestMarble;
        }

        private void SortPendingMarblesByLowestY()
        {
            pendingMarbles.Sort((left, right) =>
            {
                if (left == null || right == null)
                {
                    return left == null ? 1 : -1;
                }

                return left.transform.position.y.CompareTo(right.transform.position.y);
            });
        }

        private bool HasLowerPendingMarble(Marble candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            float candidateY = candidate.transform.position.y;
            for (int i = 0; i < pendingMarbles.Count; i++)
            {
                Marble pendingMarble = pendingMarbles[i];
                if (pendingMarble == null ||
                    pendingMarble == candidate ||
                    IsTransferMarble(pendingMarble) ||
                    conveyorController.IsMarbleOnConveyor(pendingMarble))
                {
                    continue;
                }

                if (pendingMarble.transform.position.y < candidateY)
                {
                    return true;
                }
            }

            return false;
        }

        private void SortSlotsByEntryOrder()
        {
            slotsInEntryZone.Sort((left, right) =>
            {
                if (left == null || right == null)
                {
                    return left == null ? 1 : -1;
                }

                return left.transform.position.y.CompareTo(right.transform.position.y);
            });
        }

        private bool AddPendingMarble(Marble marble)
        {
            if (marble == null ||
                pendingMarbles.Contains(marble) ||
                IsTransferMarble(marble) ||
                conveyorController != null && conveyorController.IsMarbleOnConveyor(marble))
            {
                return false;
            }

            pendingMarbles.Add(marble);
#if UNITY_EDITOR
            MarbleDebugTracker.SetLocation(marble, MarbleDebugLocation.DropZone, this,
                $"Entry #{GetInstanceID()} / Queue {pendingMarbles.Count - 1}", "Entry queue accepted / transfer rollback");
#endif
            return true;
        }

#if UNITY_EDITOR
        public void DebugCollectMarbles(List<MarbleDebugObservation> result)
        {
            for (int i = 0; i < pendingMarbles.Count; i++)
                MarbleDebugTracker.Observe(result, pendingMarbles[i], MarbleDebugLocation.DropZone, this,
                    $"Entry #{GetInstanceID()} / Queue {i}");
            for (int i = 0; i < cleanupDeferredMarbles.Count; i++)
                if (!pendingMarbles.Contains(cleanupDeferredMarbles[i]))
                    MarbleDebugTracker.Observe(result, cleanupDeferredMarbles[i], MarbleDebugLocation.DropZone, this,
                        $"Entry #{GetInstanceID()} / Deferred {i}");
            foreach (var transfer in activeTransfers)
                if (transfer != null)
                    MarbleDebugTracker.Observe(result, transfer.Marble, MarbleDebugLocation.InTransit, this,
                        $"Entry #{GetInstanceID()} -> Conveyor slot {transfer.Slot?.SlotIndex}");
        }
#endif

        private void RemoveInvalidPendingMarbles()
        {
            for (int i = pendingMarbles.Count - 1; i >= 0; i--)
            {
                Marble pendingMarble = pendingMarbles[i];
                if (pendingMarble != null &&
                    pendingMarble.gameObject.activeInHierarchy &&
                    !IsTransferMarble(pendingMarble) &&
                    (conveyorController == null || !conveyorController.IsMarbleOnConveyor(pendingMarble)))
                {
                    continue;
                }

                pendingMarbles.RemoveAt(i);
            }
        }

        private void RemoveInvalidTransfers()
        {
            for (int i = activeTransfers.Count - 1; i >= 0; i--)
            {
                EntryTransfer transfer = activeTransfers[i];
                if (transfer != null &&
                    transfer.Marble != null &&
                    transfer.Marble.gameObject.activeInHierarchy &&
                    transfer.Slot != null)
                {
                    continue;
                }

                transfer?.Tween?.Kill(false);
                transfer?.Slot?.CancelReservation();
                activeTransfers.RemoveAt(i);
            }
        }

        private bool IsTransferMarble(Marble marble)
        {
            for (int i = 0; i < activeTransfers.Count; i++)
            {
                if (activeTransfers[i].Marble == marble)
                {
                    return true;
                }
            }

            return false;
        }

        private Marble GetValidMarble(Collider2D other)
        {
            if (other == null)
            {
                return null;
            }

            Marble marble = other.GetComponentInParent<Marble>();
            if (marble == null)
            {
                return null;
            }

            int layerMask = 1 << marble.gameObject.layer;
            if ((marbleLayerMask.value & layerMask) == 0)
            {
                return null;
            }

            return marble;
        }
        private void EnsureTrigger()
        {
            if (entryTrigger == null)
            {
                entryTrigger = GetComponent<Collider2D>();
            }

            if (entryTrigger != null)
            {
                entryTrigger.isTrigger = true;
            }
        }
    }
}
