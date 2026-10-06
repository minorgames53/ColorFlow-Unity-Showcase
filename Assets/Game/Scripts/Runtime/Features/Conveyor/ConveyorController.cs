using System;
using System.Collections.Generic;
using DG.Tweening;
using Gameplay.Levels;
using Gameplay.SourceBoxes;
using Gameplay.TargetBoxes;
using Sirenix.OdinInspector;
using UnityEngine;
#if UNITY_EDITOR
using Gameplay.MarbleDebug;
#endif

namespace Gameplay.Conveyor
{
    public sealed class ConveyorController : MonoBehaviour, IGameplaySpeedTarget
    {
        #region Fields

        private const int SlotCount = 30;

        [Header("References")]
        [SerializeField] private Transform slotRoot;
        [SerializeField] private Transform conveyorMarbleContainer;
        [SerializeField] private MarbleCapacityController capacityController;
        [SerializeField] private LevelSessionController levelSessionController;

        [Header("Warning Glow")]
        [Tooltip("Enables both blocked-occupancy and manual fail warning pulses.")]
        [SerializeField, OnValueChanged(nameof(OnWarningGlowEnabledChanged))]
        private bool warningGlowEnabled = false;
        [SerializeField] private SpriteRenderer conveyorGlow;
        [Tooltip("Fraction of conveyor capacity occupied by marbles that cannot currently match an accepting active target.")]
        [SerializeField, Range(0f, 1f)] private float warningThreshold = 0.80f;
        [SerializeField, Range(0, 255)] private int minAlpha = 0;
        [SerializeField, Range(0, 255)] private int maxAlpha = 255;
        [SerializeField, Min(1)] private int blinkCount = 3;
        [Tooltip("Duration in seconds of one min-to-max-to-min pulse.")]
        [SerializeField, Min(0.01f)] private float blinkDuration = 0.6f;

        [Header("Movement")]
        [SerializeField, Min(0f)] private float baseSpeed = 1f;
        [Tooltip("Absolute conveyor path speed limit (slots per second), applied after all speed multipliers, including end-of-level speed-up.")]
        [SerializeField, Min(0.01f)] private float maxSpeed = 15f;

        [Header("Catch-Up")]
        [SerializeField] private bool enableCatchUp = true;
        [SerializeField, Min(0.01f)] private float catchUpMaxSpeed = 8f;
        [SerializeField, Min(0.01f)] private float catchUpSpeedMultiplier = 20f;
        [SerializeField, Min(0f)] private float catchUpSnapTolerance = 0.005f;
        [SerializeField, Min(0f)] private float fallSpeedUp = 8f;
        [SerializeField, Min(0f)] private float fallSpeedUpDuration = 0.25f;
        [SerializeField, Min(0.1f)] private float minimumMarbleGapInSlots = 1f;
        [SerializeField] private Vector3 conveyorMarbleScale = Vector3.one;
        [SerializeField] private bool catchUpFromPreviousSlot = true;

        [Header("Debug")]
        [SerializeField] private bool showRuntimeGizmos = true;
        [SerializeField] private List<Marble> testMarbles = new List<Marble>();

        private readonly Vector3[] baseSlotLocalPositions = new Vector3[SlotCount];
        private readonly Vector3[] previousSlotWorldPositions = new Vector3[SlotCount];
        private readonly ConveyorSlot[] slots = new ConveyorSlot[SlotCount];
        private readonly List<EntryReservation> entryReservations = new List<EntryReservation>(SlotCount);
        private readonly List<ConveyorMarbleMotion> marbleMotions = new List<ConveyorMarbleMotion>(SlotCount);
        private readonly List<ConveyorMarbleMotion> sortedMarbleMotions = new List<ConveyorMarbleMotion>(SlotCount);
        private bool slotsCached;
        private bool previousSlotPositionsCached;
        private bool slotValidationErrorLogged;
        private float globalPathOffset;
        private int nextEntryReservationId = 1;
        private int nextMarbleEntryOrder = 1;
        private float gameplaySpeedMultiplier = 1f;
        private bool ufoMovementFrozen;
        private Sequence warningGlowSequence;
        private bool warningGlowTriggered;
        private TargetLaneController warningTargetSource;

        #endregion

        #region Runtime Types

        private sealed class EntryReservation
        {
            public int Id;
            public ConveyorSlot Slot;
        }

        private sealed class ConveyorMarbleMotion
        {
            public ConveyorSlot Slot;
            public Marble Marble;
            public float PathPosition;
            public float PreviousPathPosition;
            public Vector3 PreviousPosition;
            public float CurrentSpeed;
            public float LockedZ;
            public float FallSpeedUpRemaining;
            public int EntryOrder;
            public bool IsLeader;
        }

        #endregion

        #region Properties

        public bool HasCapacity => AvailableCapacity > 0;
        public bool IsFull => AvailableCapacity <= 0;
        public int AvailableCapacity => CountSlots(ConveyorSlotState.Empty);
        public int MarbleCount => CountSlots(ConveyorSlotState.Occupied);
        public event Action ContentsChanged;
        public Vector3 EntryPosition => TryGetSlot(0, out ConveyorSlot slot) && slot.MarbleAnchor != null
            ? slot.MarbleAnchor.position
            : transform.position;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            ResetWarningGlow();
            CacheLevelSessionController();
            CacheSlots(true);
            ApplySlotPositions();
            CachePreviousSlotWorldPositions();
        }

        private void OnValidate()
        {
            baseSpeed = Mathf.Max(0f, baseSpeed);
            maxSpeed = Mathf.Max(0.01f, maxSpeed);
            catchUpMaxSpeed = Mathf.Max(0.01f, catchUpMaxSpeed);
            catchUpSpeedMultiplier = Mathf.Max(0.01f, catchUpSpeedMultiplier);
            catchUpSnapTolerance = Mathf.Max(0f, catchUpSnapTolerance);
            fallSpeedUp = Mathf.Max(0f, fallSpeedUp);
            fallSpeedUpDuration = Mathf.Max(0f, fallSpeedUpDuration);
            minimumMarbleGapInSlots = Mathf.Max(0.1f, minimumMarbleGapInSlots);
            warningThreshold = Mathf.Clamp01(warningThreshold);
            minAlpha = Mathf.Clamp(minAlpha, 0, 255);
            maxAlpha = Mathf.Clamp(maxAlpha, minAlpha, 255);
            blinkCount = Mathf.Max(1, blinkCount);
            blinkDuration = Mathf.Max(0.01f, blinkDuration);
        }

        private void Update()
        {
            if (levelSessionController == null)
            {
                CacheLevelSessionController();
            }

            if (levelSessionController == null || !levelSessionController.IsSimulationRunning || ufoMovementFrozen)
            {
                return;
            }

            if (!EnsureSlotsCached(true))
            {
                return;
            }

            CachePreviousSlotWorldPositions();
            CachePreviousMarbleMotionPositions();

            float deltaTime = Time.deltaTime;
            globalPathOffset = NormalizePathPosition(globalPathOffset + ClampMarbleSpeed(baseSpeed * gameplaySpeedMultiplier) * deltaTime);
            RemoveNullSlotMarbles();
            ApplySlotPositions();
            UpdateContinuousCatchUp(deltaTime);
        }

        private void OnDisable()
        {
            Clear();
        }

        #endregion

        #region Warning Glow

        private void OnWarningGlowEnabledChanged()
        {
            if (Application.isPlaying) ResetWarningGlow();
        }

        // Supplied by the existing target controller's conveyor reference; no scene wiring or lookup.
        internal void SetWarningTargetSource(TargetLaneController source)
        {
            warningTargetSource = source;
        }

        internal void ClearWarningTargetSource(TargetLaneController source)
        {
            if (warningTargetSource != source) return;
            warningTargetSource = null;
            ResetWarningGlow();
        }

        private void RefreshWarningGlow()
        {
            if (!warningGlowEnabled || conveyorGlow == null || warningTargetSource == null || !EnsureSlotsCached(false))
            {
                ResetWarningGlow();
                return;
            }

            // Only committed conveyor marbles count, not incoming slot reservations.
            int blockedMarbles = Mathf.Max(0, MarbleCount - warningTargetSource.CountMatchableConveyorMarbles());
            float blockedOccupancy = blockedMarbles / (float)SlotCount;
            if (blockedMarbles == 0 || blockedOccupancy < warningThreshold)
            {
                ResetWarningGlow();
                return;
            }

            if (warningGlowTriggered)
            {
                return;
            }

            PlayWarningGlow();
        }

        // Also used once when a logical fail enters recovery, before the Clean Up offer.
        // A manual warning restarts the same pulse even if the occupancy warning already ran.
        internal void PlayWarningGlow()
        {
            if (!warningGlowEnabled)
            {
                ResetWarningGlow();
                return;
            }

            if (!isActiveAndEnabled || conveyorGlow == null) return;
            warningGlowSequence?.Kill(false);
            warningGlowSequence = null;
            warningGlowTriggered = true;
            float lowAlpha = minAlpha / 255f;
            float highAlpha = maxAlpha / 255f;
            float halfPulseDuration = blinkDuration * 0.5f;
            SetWarningGlowAlpha(lowAlpha);

            warningGlowSequence = DOTween.Sequence();
            warningGlowSequence.Append(DOTween.To(() => conveyorGlow.color.a, SetWarningGlowAlpha,
                highAlpha, halfPulseDuration).SetEase(Ease.InOutSine));
            warningGlowSequence.Append(DOTween.To(() => conveyorGlow.color.a, SetWarningGlowAlpha,
                lowAlpha, halfPulseDuration).SetEase(Ease.InOutSine));
            warningGlowSequence.SetLoops(blinkCount, LoopType.Restart);
            warningGlowSequence.SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            warningGlowSequence.OnComplete(() =>
            {
                warningGlowSequence = null;
                SetWarningGlowAlpha(0f);
            });
        }

        private void ResetWarningGlow()
        {
            warningGlowSequence?.Kill(false);
            warningGlowSequence = null;
            warningGlowTriggered = false;
            SetWarningGlowAlpha(0f);
        }

        private void SetWarningGlowAlpha(float alpha)
        {
            if (conveyorGlow == null)
            {
                return;
            }

            Color color = conveyorGlow.color;
            color.a = alpha;
            conveyorGlow.color = color;
        }

        #endregion

        private void CacheLevelSessionController()
        {
            if (levelSessionController == null)
            {
                levelSessionController = FindFirstObjectByType<LevelSessionController>(FindObjectsInactive.Include);
            }
        }

        #region Public API

        internal int GetInitialMarbleSlotIndex(int entryIndex, int marbleCount)
        {
            // Catch-up treats the oldest entry as the leader, so seed the front first.
            return GetForwardStep() > 0 ? marbleCount - 1 - entryIndex : entryIndex;
        }

        public bool TryAddMarble(Marble marble, float pathPosition)
        {
            return TryAddMarble(marble, pathPosition, 0f);
        }

        public bool TryAddMarble(Marble marble, float pathPosition, float clearanceInSlots)
        {
            if (marble == null || !EnsureSlotsCached(true))
            {
                return false;
            }

            int slotIndex = WrapIndex(Mathf.RoundToInt(pathPosition));
            if (!TryGetSlot(slotIndex, out ConveyorSlot slot) || !slot.TryReserveForEntry())
            {
                return false;
            }

            AssignMarbleToSlot(slot, marble, conveyorMarbleScale);
            return true;
        }

        public bool TryStartEntry(Marble marble, float entryDuration, Ease entryEase)
        {
            return TryAddMarble(marble, 0f);
        }

        public bool HasEntryClearance(float clearanceInSlots)
        {
            return HasCapacity;
        }

        public bool TryReserveEntry(float clearanceInSlots)
        {
            return TryReserveEntrySlot(0, clearanceInSlots, out _, out _);
        }

        public bool TryReserveEntrySlot(Transform slotTransform, float clearanceInSlots, out int reservationId, out Vector3 targetPosition)
        {
            reservationId = -1;
            targetPosition = default;

            if (!TryGetSlot(slotTransform, out ConveyorSlot slot))
            {
                return false;
            }

            return TryReserveEntrySlot(slot, out reservationId, out targetPosition);
        }

        public bool TryReserveEntrySlot(int slotIndex, float clearanceInSlots, out int reservationId, out Vector3 targetPosition)
        {
            reservationId = -1;
            targetPosition = default;

            if (!TryGetSlot(slotIndex, out ConveyorSlot slot))
            {
                return false;
            }

            return TryReserveEntrySlot(slot, out reservationId, out targetPosition);
        }

        public bool TryReserveEntrySlot(ConveyorSlot slot, out int reservationId, out Vector3 targetPosition)
        {
            reservationId = -1;
            targetPosition = default;

            if (slot == null || slot.MarbleAnchor == null || !slot.TryReserveForEntry())
            {
                return false;
            }

            EntryReservation reservation = new EntryReservation
            {
                Id = nextEntryReservationId++,
                Slot = slot
            };

            entryReservations.Add(reservation);
            reservationId = reservation.Id;
            targetPosition = slot.MarbleAnchor.position;
            return true;
        }

        public bool TryReserveSlotForEntry(ConveyorSlot slot)
        {
            return slot != null && slot.TryReserveForEntry();
        }

        public bool TryCommitReservedEntry(Marble marble)
        {
            if (entryReservations.Count == 0)
            {
                return false;
            }

            return TryCommitReservedEntry(marble, entryReservations[0].Id);
        }

        public bool TryCommitReservedEntry(Marble marble, int reservationId)
        {
            int reservationIndex = FindEntryReservationIndex(reservationId);
            if (reservationIndex < 0)
            {
                return false;
            }

            ConveyorSlot slot = entryReservations[reservationIndex].Slot;
            entryReservations.RemoveAt(reservationIndex);
            return TryAssignReservedSlot(slot, marble, conveyorMarbleScale);
        }

        public bool TryAssignReservedSlot(ConveyorSlot slot, Marble marble, Vector3 conveyorMarbleScale)
        {
            if (slot == null || !slot.IsReserved || marble == null)
            {
                return false;
            }

            AssignMarbleToSlot(slot, marble, conveyorMarbleScale, true);
            return true;
        }

        public void CancelEntryReservation()
        {
            for (int i = 0; i < entryReservations.Count; i++)
            {
                entryReservations[i].Slot?.CancelReservation();
            }

            entryReservations.Clear();
        }

        public void CancelEntryReservation(int reservationId)
        {
            int reservationIndex = FindEntryReservationIndex(reservationId);
            if (reservationIndex < 0)
            {
                return;
            }

            entryReservations[reservationIndex].Slot?.CancelReservation();
            entryReservations.RemoveAt(reservationIndex);
        }

        public bool TryGetEntryReservationPosition(int reservationId, out Vector3 position)
        {
            position = default;
            int reservationIndex = FindEntryReservationIndex(reservationId);
            if (reservationIndex < 0)
            {
                return false;
            }

            ConveyorSlot slot = entryReservations[reservationIndex].Slot;
            if (slot == null || slot.MarbleAnchor == null)
            {
                return false;
            }

            position = slot.MarbleAnchor.position;
            return true;
        }

        public bool RemoveMarble(Marble marble)
        {
            ConveyorSlot slot = FindSlotForMarble(marble);
            if (slot == null || !slot.IsOccupied)
            {
                return false;
            }

            slot.ReleaseMarble();
            RemoveMarbleMotion(marble);
#if UNITY_EDITOR
            MarbleDebugTracker.SetLocation(marble, MarbleDebugLocation.InTransit, null,
                $"Detached conveyor slot {slot.SlotIndex}", "Conveyor ownership removed");
#endif
            capacityController?.NotifyMarbleConsumed();
            RefreshWarningGlow();
            ContentsChanged?.Invoke();
            return true;
        }

        public void NotifyExternalFieldMarbleRemoved()
        {
            capacityController?.NotifyMarbleConsumed();
            ContentsChanged?.Invoke();
        }

        public bool TryConsumeReservedEntryForUfo(ConveyorSlot slot)
        {
            if (slot == null || !slot.IsReserved)
            {
                return false;
            }

            slot.CancelReservation();
            capacityController?.NotifyMarbleConsumed();
            return true;
        }

        public bool TryRestoreMarbleFromUfo(Marble marble)
        {
            if (marble == null || !EnsureSlotsCached(true) || capacityController == null ||
                !capacityController.TryReserve(1))
            {
                return false;
            }

            ConveyorSlot destination = null;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null && slots[i].IsEmpty)
                {
                    destination = slots[i];
                    break;
                }
            }

            if (destination == null || !destination.TryReserveForEntry())
            {
                capacityController.CancelReservation(1);
                return false;
            }

            AssignMarbleToSlot(destination, marble, conveyorMarbleScale, true);
            capacityController.CommitReservation(1);
            return true;
        }

        public int GetOccupiedMarbles(List<Marble> results)
        {
            if (results == null)
            {
                return 0;
            }

            results.Clear();
            if (!EnsureSlotsCached(true))
            {
                return 0;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                Marble marble = GetCapturableSlotMarble(slots[i]);
                if (marble != null && marble.CanBeOwnedByUfo)
                {
                    results.Add(marble);
                }
            }

            return results.Count;
        }

        public bool IsMarbleOnConveyor(Marble marble)
        {
            ConveyorSlot slot = FindSlotForMarble(marble);
            return (slot != null && (slot.IsOccupied || slot.IsBlocked || slot.IsReserved)) ||
                   FindMarbleMotion(marble) != null;
        }

        public bool HasCapturableMarbleWithColor(MarbleColorId colorId)
        {
            if (!MarbleColorCatalog.IsGameplayColor(colorId) || !EnsureSlotsCached(true))
            {
                return false;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                Marble marble = GetCapturableSlotMarble(slots[i]);
                if (marble != null && marble.CanBeOwnedByUfo && marble.ColorId == colorId)
                {
                    return true;
                }
            }

            return false;
        }

        public int GetMarblesInCaptureArea(
            Vector2 origin,
            Vector2 direction,
            float length,
            float width,
            LayerMask layerMask,
            Marble[] results,
            float[] resultDistances)
        {
            if (results == null || resultDistances == null || results.Length == 0 || resultDistances.Length == 0)
            {
                return 0;
            }

            if (direction.sqrMagnitude <= 0f || length <= 0f || width <= 0f || !EnsureSlotsCached(true))
            {
                return 0;
            }

            Vector2 forward = direction.normalized;
            Vector2 side = new Vector2(-forward.y, forward.x);
            float halfWidth = width * 0.5f;
            int resultLimit = Mathf.Min(results.Length, resultDistances.Length);
            int resultCount = 0;

            for (int i = 0; i < slots.Length; i++)
            {
                ConveyorSlot slot = slots[i];
                Marble marble = GetCapturableSlotMarble(slot);
                if (marble == null || marble.IsTransferringToTarget)
                {
                    continue;
                }

                int marbleLayerMask = 1 << marble.gameObject.layer;
                if ((layerMask.value & marbleLayerMask) == 0)
                {
                    continue;
                }

                ConveyorMarbleMotion motion = FindMarbleMotion(marble);
                Vector2 currentPosition = motion != null
                    ? marble.transform.position
                    : slot.MarbleAnchor != null
                        ? slot.MarbleAnchor.position
                        : marble.transform.position;
                Vector2 previousPosition = motion != null
                    ? motion.PreviousPosition
                    : previousSlotPositionsCached
                        ? previousSlotWorldPositions[i]
                        : currentPosition;
                float radius = Mathf.Max(0.01f, marble.ColliderDiameter * 0.5f);
                if (!TryGetSweptCaptureDistance(
                        origin,
                        forward,
                        side,
                        length,
                        halfWidth,
                        radius,
                        previousPosition,
                        currentPosition,
                        out float forwardDistance))
                {
                    continue;
                }

                if (resultCount >= resultLimit)
                {
                    continue;
                }

                results[resultCount] = marble;
                resultDistances[resultCount] = forwardDistance;
                resultCount++;
            }

            SortMarbleCaptureResults(results, resultDistances, resultCount);
            return resultCount;
        }

        public bool TryGetSlotIndex(Transform slotTransform, out int slotIndex)
        {
            slotIndex = -1;
            if (!TryGetSlot(slotTransform, out ConveyorSlot slot))
            {
                return false;
            }

            slotIndex = GetRuntimeSlotIndex(slot);
            return slotIndex >= 0;
        }

        public bool TryGetSlot(Transform slotTransform, out ConveyorSlot slot)
        {
            slot = null;
            if (slotTransform == null || !EnsureSlotsCached(true))
            {
                return false;
            }

            ConveyorSlot candidate = slotTransform.GetComponent<ConveyorSlot>();
            if (candidate == null)
            {
                candidate = slotTransform.GetComponentInParent<ConveyorSlot>();
            }

            if (candidate == null)
            {
                return false;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != candidate)
                {
                    continue;
                }

                slot = candidate;
                return true;
            }

            return false;
        }

        public void GetEmptySlotsInside(Collider2D area, List<ConveyorSlot> results)
        {
            if (results == null)
            {
                return;
            }

            results.Clear();
            if (area == null || !EnsureSlotsCached(true))
            {
                return;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                ConveyorSlot slot = slots[i];
                if (slot == null || !slot.IsEmpty)
                {
                    continue;
                }

                Vector3 testPosition = slot.MarbleAnchor != null ? slot.MarbleAnchor.position : slot.transform.position;
                if (area.OverlapPoint(testPosition))
                {
                    results.Add(slot);
                }
            }
        }

        public void Clear()
        {
            ResetWarningGlow();
            CancelEntryReservation();
            marbleMotions.Clear();
            nextEntryReservationId = 1;
            nextMarbleEntryOrder = 1;

            if (EnsureSlotsCached(false))
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    ConveyorSlot slot = slots[i];
                    if (slot == null)
                    {
                        continue;
                    }

                    Marble marble = slot.CurrentMarble;
                    if (marble != null)
                    {
                        DestroyGameObject(marble.gameObject);
                    }

                    slot.Clear();
                }
            }

            DestroyContainerMarbles();
            globalPathOffset = 0f;
            ApplySlotPositions();
            ContentsChanged?.Invoke();
        }

        public void SetGameplaySpeedMultiplier(float multiplier)
        {
            gameplaySpeedMultiplier = Mathf.Max(0.01f, multiplier);
        }

        public void SetUfoMovementFrozen(bool isFrozen)
        {
            ufoMovementFrozen = isFrozen;
        }

        [Button("Add Test Marbles")]
        public void AddTestMarbles()
        {
            Clear();

            int count = Mathf.Min(9, testMarbles.Count);
            for (int i = 0; i < count; i++)
            {
                Marble marble = testMarbles[i];
                if (marble == null)
                {
                    continue;
                }

                TryAddMarble(marble, 8f - i);
            }
        }

        #endregion

        #region Marble Motion

        private void AssignMarbleToSlot(
            ConveyorSlot slot,
            Marble marble,
            Vector3 conveyorMarbleScale,
            bool tryStartFallSpeedUp = false)
        {
            marble.PrepareForConveyorControl();
            marble.transform.SetParent(slot.MarbleAnchor, true);
            marble.transform.localPosition = Vector3.zero;
            marble.transform.localRotation = Quaternion.identity;
            marble.transform.localScale = conveyorMarbleScale;
            slot.AssignMarble(marble);
            ConveyorMarbleMotion motion = RegisterOrUpdateMarbleMotion(slot, marble);
#if UNITY_EDITOR
            MarbleDebugTracker.SetLocation(marble, MarbleDebugLocation.Conveyor, this,
                $"Conveyor #{GetInstanceID()} / Slot {slot.SlotIndex} / Entry {motion?.EntryOrder}", "Conveyor slot assignment committed");
#endif

            if (tryStartFallSpeedUp)
            {
                TryStartFallSpeedUp(motion);
            }

            RefreshWarningGlow();
            ContentsChanged?.Invoke();
        }

        private ConveyorMarbleMotion RegisterOrUpdateMarbleMotion(ConveyorSlot slot, Marble marble)
        {
            if (slot == null || marble == null)
            {
                return null;
            }

            ConveyorMarbleMotion motion = FindMarbleMotion(marble);
            int slotIndex = GetRuntimeSlotIndex(slot);
            float slotPathPosition = slotIndex >= 0
                ? NormalizePathPosition(slotIndex + globalPathOffset)
                : NormalizePathPosition(globalPathOffset);

            if (motion == null)
            {
                motion = new ConveyorMarbleMotion
                {
                    Marble = marble,
                    PathPosition = slotPathPosition,
                    PreviousPathPosition = slotPathPosition,
                    PreviousPosition = marble.transform.position,
                    CurrentSpeed = ClampMarbleSpeed(baseSpeed * gameplaySpeedMultiplier),
                    LockedZ = marble.transform.position.z,
                    EntryOrder = nextMarbleEntryOrder++
                };

                marbleMotions.Add(motion);
            }

            motion.Slot = slot;
            motion.PathPosition = slotPathPosition;
            motion.PreviousPathPosition = slotPathPosition;
            motion.PreviousPosition = marble.transform.position;
            motion.LockedZ = marble.transform.position.z;
            motion.CurrentSpeed = ClampMarbleSpeed(Mathf.Max(motion.CurrentSpeed, baseSpeed * gameplaySpeedMultiplier));

            Transform marbleTransform = marble.transform;
            marbleTransform.SetParent(conveyorMarbleContainer != null ? conveyorMarbleContainer : transform, true);
            marbleTransform.localRotation = Quaternion.identity;
            marbleTransform.localScale = conveyorMarbleScale;
            UpdateMotionTransform(motion);
            return motion;
        }

        private void CachePreviousMarbleMotionPositions()
        {
            for (int i = 0; i < marbleMotions.Count; i++)
            {
                ConveyorMarbleMotion motion = marbleMotions[i];
                if (motion == null || motion.Marble == null)
                {
                    continue;
                }

                motion.PreviousPathPosition = motion.PathPosition;
                motion.PreviousPosition = motion.Marble.transform.position;
            }
        }

        private void UpdateContinuousCatchUp(float deltaTime)
        {
            SyncMarbleMotionRecords();

            if (marbleMotions.Count == 0)
            {
                return;
            }

            if (!enableCatchUp || marbleMotions.Count == 1)
            {
                float baseTargetSpeed = ClampMarbleSpeed(baseSpeed * gameplaySpeedMultiplier);
                for (int i = 0; i < marbleMotions.Count; i++)
                {
                    ConveyorMarbleMotion motion = marbleMotions[i];
                    motion.CurrentSpeed = baseTargetSpeed;
                    AdvanceMotion(motion, baseTargetSpeed * deltaTime, deltaTime);
                }

                return;
            }

            sortedMarbleMotions.Clear();
            sortedMarbleMotions.AddRange(marbleMotions);
            sortedMarbleMotions.Sort(CompareMotionForwardCoordinate);

            int count = sortedMarbleMotions.Count;
            float baseTarget = ClampMarbleSpeed(baseSpeed * gameplaySpeedMultiplier);
            float desiredGap = Mathf.Max(0.1f, minimumMarbleGapInSlots);
            int leaderIndex = FindOldestEntryMotionIndex();

            for (int offset = 0; offset < count; offset++)
            {
                int motionIndex = WrapMotionIndex(leaderIndex - offset, count);
                ConveyorMarbleMotion motion = sortedMarbleMotions[motionIndex];
                bool isLeader = offset == 0;
                motion.IsLeader = isLeader;

                if (isLeader)
                {
                    motion.FallSpeedUpRemaining = 0f;
                    motion.CurrentSpeed = baseTarget;
                    AdvanceMotion(motion, baseTarget * deltaTime, deltaTime);
                    continue;
                }

                ConveyorMarbleMotion ahead = sortedMarbleMotions[WrapMotionIndex(motionIndex + 1, count)];
                float gap = GetForwardGap(motion, ahead);
                if (Mathf.Abs(gap - desiredGap) <= catchUpSnapTolerance)
                {
                    motion.CurrentSpeed = baseTarget;
                    SnapMotionBehindAhead(motion, ahead, desiredGap, deltaTime);
                    TickFallSpeedUp(motion, deltaTime);
                    continue;
                }

                float targetSpeed = gap > desiredGap
                    ? catchUpMaxSpeed * gameplaySpeedMultiplier
                    : baseTarget;

                if (gap > desiredGap && motion.FallSpeedUpRemaining > 0f && fallSpeedUp > 0f)
                {
                    targetSpeed = Mathf.Max(targetSpeed, fallSpeedUp * gameplaySpeedMultiplier);
                }

                targetSpeed = ClampMarbleSpeed(Mathf.Max(baseTarget, targetSpeed));
                motion.CurrentSpeed = SmoothCatchUpSpeed(motion.CurrentSpeed, targetSpeed, deltaTime);
                AdvanceMotion(motion, motion.CurrentSpeed * deltaTime, deltaTime, ahead, desiredGap);
                TickFallSpeedUp(motion, deltaTime);
            }
        }

        private void SyncMarbleMotionRecords()
        {
            for (int i = marbleMotions.Count - 1; i >= 0; i--)
            {
                ConveyorMarbleMotion motion = marbleMotions[i];
                if (motion == null ||
                    motion.Marble == null ||
                    !motion.Marble.gameObject.activeInHierarchy)
                {
                    marbleMotions.RemoveAt(i);
                }
            }

            if (!EnsureSlotsCached(false))
            {
                return;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                ConveyorSlot slot = slots[i];
                if (slot == null || !slot.IsOccupied || slot.CurrentMarble == null)
                {
                    continue;
                }

                if (FindMarbleMotion(slot.CurrentMarble) != null)
                {
                    continue;
                }

                RegisterOrUpdateMarbleMotion(slot, slot.CurrentMarble);
            }
        }

        private int FindOldestEntryMotionIndex()
        {
            int bestIndex = 0;
            int bestEntryOrder = int.MaxValue;

            for (int i = 0; i < sortedMarbleMotions.Count; i++)
            {
                ConveyorMarbleMotion motion = sortedMarbleMotions[i];
                motion.IsLeader = false;
                if (motion.EntryOrder >= bestEntryOrder)
                {
                    continue;
                }

                bestEntryOrder = motion.EntryOrder;
                bestIndex = i;
            }

            return bestIndex;
        }

        private float ClampMarbleSpeed(float speed)
        {
            return Mathf.Clamp(speed, 0f, Mathf.Max(0.01f, maxSpeed));
        }

        private float SmoothCatchUpSpeed(float currentSpeed, float targetSpeed, float deltaTime)
        {
            float response = Mathf.Max(0.01f, catchUpSpeedMultiplier) * gameplaySpeedMultiplier;
            float t = Mathf.Clamp01(deltaTime * response);
            return ClampMarbleSpeed(Mathf.Lerp(currentSpeed, targetSpeed, t));
        }

        private static void TickFallSpeedUp(ConveyorMarbleMotion motion, float deltaTime)
        {
            if (motion.FallSpeedUpRemaining > 0f)
            {
                motion.FallSpeedUpRemaining = Mathf.Max(0f, motion.FallSpeedUpRemaining - deltaTime);
            }
        }

        private float GetForwardGap(ConveyorMarbleMotion from, ConveyorMarbleMotion to)
        {
            float fromForwardCoordinate = GetForwardCoordinate(from.PathPosition);
            float toForwardCoordinate = GetForwardCoordinate(to.PathPosition);
            float gap = toForwardCoordinate - fromForwardCoordinate;
            if (gap < 0f)
            {
                gap += SlotCount;
            }

            return gap;
        }

        private void SnapMotionBehindAhead(
            ConveyorMarbleMotion motion,
            ConveyorMarbleMotion ahead,
            float desiredGap,
            float deltaTime)
        {
            float forwardCoordinate = GetForwardCoordinate(motion.PathPosition);
            float aheadForwardCoordinate = GetForwardCoordinate(ahead.PathPosition);
            if (aheadForwardCoordinate <= forwardCoordinate) aheadForwardCoordinate += SlotCount;
            float targetForwardCoordinate = aheadForwardCoordinate - desiredGap;
            float maxMovement = ClampMarbleSpeed(maxSpeed) * Mathf.Max(0f, deltaTime);
            targetForwardCoordinate = forwardCoordinate + Mathf.Clamp(
                targetForwardCoordinate - forwardCoordinate, -maxMovement, maxMovement);
            motion.PathPosition = GetPathPositionFromForwardCoordinate(targetForwardCoordinate);
            UpdateMotionTransform(motion);
            UpdateMotionSlotOwnership(motion);
        }

        private void AdvanceMotion(
            ConveyorMarbleMotion motion,
            float forwardMovement,
            float deltaTime,
            ConveyorMarbleMotion ahead = null,
            float desiredGap = 1f)
        {
            if (motion == null || motion.Marble == null)
            {
                return;
            }

            float forwardCoordinate = GetForwardCoordinate(motion.PathPosition);
            float nextForwardCoordinate = forwardCoordinate + Mathf.Max(0f, forwardMovement);
            if (ahead != null)
            {
                float aheadForwardCoordinate = GetForwardCoordinate(ahead.PathPosition);
                if (aheadForwardCoordinate <= forwardCoordinate)
                {
                    aheadForwardCoordinate += SlotCount;
                }

                float maximumForwardCoordinate = aheadForwardCoordinate - desiredGap;
                nextForwardCoordinate = Mathf.Min(nextForwardCoordinate, maximumForwardCoordinate);
            }

            // Keep spacing corrections and runtime Inspector changes within the same hard limit.
            float maxMovement = ClampMarbleSpeed(maxSpeed) * Mathf.Max(0f, deltaTime);
            nextForwardCoordinate = forwardCoordinate + Mathf.Clamp(
                nextForwardCoordinate - forwardCoordinate, -maxMovement, maxMovement);
            motion.PathPosition = GetPathPositionFromForwardCoordinate(nextForwardCoordinate);
            UpdateMotionTransform(motion);
            UpdateMotionSlotOwnership(motion);
        }

        private void UpdateMotionTransform(ConveyorMarbleMotion motion)
        {
            if (motion == null || motion.Marble == null)
            {
                return;
            }

            Vector3 position = EvaluatePathWorldPosition(motion.PathPosition);
            position.z = motion.LockedZ;
            Transform marbleTransform = motion.Marble.transform;
            marbleTransform.position = position;
            marbleTransform.localRotation = Quaternion.identity;
            marbleTransform.localScale = conveyorMarbleScale;
        }

        private void UpdateMotionSlotOwnership(ConveyorMarbleMotion motion)
        {
            if (motion == null || motion.Marble == null || !EnsureSlotsCached(false))
            {
                return;
            }

            int nearestSlotIndex = WrapIndex(Mathf.RoundToInt(NormalizePathPosition(motion.PathPosition - globalPathOffset)));
            ConveyorSlot nearestSlot = slots[nearestSlotIndex];
            if (nearestSlot == null || nearestSlot == motion.Slot)
            {
                return;
            }

            if (!nearestSlot.IsEmpty)
            {
                return;
            }

            if (motion.Slot != null && motion.Slot.CurrentMarble == motion.Marble)
            {
                motion.Slot.ReleaseMarble();
            }

            nearestSlot.AssignMarble(motion.Marble);
            motion.Slot = nearestSlot;
#if UNITY_EDITOR
            MarbleDebugTracker.SetLocation(motion.Marble, MarbleDebugLocation.Conveyor, this,
                $"Conveyor #{GetInstanceID()} / Slot {nearestSlot.SlotIndex} / Entry {motion.EntryOrder}", "Conveyor slot ownership changed");
#endif
        }

        private bool RemoveMarbleMotion(Marble marble)
        {
            int index = FindMarbleMotionIndex(marble);
            if (index < 0)
            {
                return false;
            }

            marbleMotions.RemoveAt(index);
            return true;
        }

        private ConveyorMarbleMotion FindMarbleMotion(Marble marble)
        {
            int index = FindMarbleMotionIndex(marble);
            return index >= 0 ? marbleMotions[index] : null;
        }

        private int FindMarbleMotionIndex(Marble marble)
        {
            if (marble == null)
            {
                return -1;
            }

            for (int i = 0; i < marbleMotions.Count; i++)
            {
                if (marbleMotions[i] != null && marbleMotions[i].Marble == marble)
                {
                    return i;
                }
            }

            return -1;
        }

        private int CompareMotionForwardCoordinate(ConveyorMarbleMotion left, ConveyorMarbleMotion right)
        {
            return GetForwardCoordinate(left.PathPosition).CompareTo(GetForwardCoordinate(right.PathPosition));
        }

        private float GetForwardCoordinate(float pathPosition)
        {
            return NormalizePathPosition(pathPosition * GetForwardStep());
        }

        private float GetPathPositionFromForwardCoordinate(float forwardCoordinate)
        {
            return NormalizePathPosition(forwardCoordinate * GetForwardStep());
        }

        private static int WrapMotionIndex(int index, int count)
        {
            index %= count;
            return index < 0 ? index + count : index;
        }

        private int GetForwardStep()
        {
            return catchUpFromPreviousSlot ? 1 : -1;
        }

        private void TryStartFallSpeedUp(ConveyorMarbleMotion motion)
        {
            if (!enableCatchUp ||
                fallSpeedUp <= 0f ||
                fallSpeedUpDuration <= 0f ||
                motion == null ||
                motion.Slot == null ||
                !EnsureSlotsCached(true))
            {
                return;
            }

            if (!HasOlderMarbleOnConveyor(motion))
            {
                return;
            }

            motion.FallSpeedUpRemaining = GetEffectiveDuration(fallSpeedUpDuration);
            motion.CurrentSpeed = ClampMarbleSpeed(Mathf.Max(motion.CurrentSpeed, fallSpeedUp * gameplaySpeedMultiplier));
        }

        private bool HasOlderMarbleOnConveyor(ConveyorMarbleMotion motion)
        {
            if (motion == null)
            {
                return false;
            }

            for (int i = 0; i < marbleMotions.Count; i++)
            {
                ConveyorMarbleMotion candidate = marbleMotions[i];
                if (candidate != null &&
                    candidate != motion &&
                    candidate.Marble != null &&
                    candidate.EntryOrder < motion.EntryOrder)
                {
                    return true;
                }
            }

            return false;
        }

        private float GetEffectiveDuration(float duration)
        {
            return Mathf.Max(0f, duration) / gameplaySpeedMultiplier;
        }

        private Vector3 EvaluatePathWorldPosition(float pathPosition)
        {
            return slotRoot != null
                ? slotRoot.TransformPoint(EvaluateLocalPathPosition(pathPosition))
                : transform.position;
        }

        #endregion

        #region Helpers

        private static Marble GetCapturableSlotMarble(ConveyorSlot slot)
        {
            if (slot == null)
            {
                return null;
            }

            if (slot.IsOccupied)
            {
                return slot.CurrentMarble;
            }

            return null;
        }

        private bool TryGetSlot(int slotIndex, out ConveyorSlot slot)
        {
            slot = null;
            if (!EnsureSlotsCached(true) || slotIndex < 0 || slotIndex >= SlotCount)
            {
                return false;
            }

            slot = slots[slotIndex];
            return slot != null;
        }

        private bool EnsureSlotsCached(bool logErrors)
        {
            if (slotsCached && IsSlotCacheValid())
            {
                return true;
            }

            return CacheSlots(logErrors);
        }

        private bool IsSlotCacheValid()
        {
            if (slotRoot == null || slotRoot.childCount != SlotCount)
            {
                return false;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                ConveyorSlot slot = slots[i];
                if (slot == null || slot.transform != slotRoot.GetChild(i))
                {
                    return false;
                }
            }

            return true;
        }

        private bool CacheSlots(bool logErrors)
        {
            slotsCached = false;

            if (slotRoot == null)
            {
                LogSlotValidationError($"{nameof(ConveyorController)} on '{name}' cannot cache slots because Slot Root is missing.", logErrors);
                return false;
            }

            if (slotRoot.childCount != SlotCount)
            {
                LogSlotValidationError($"{nameof(ConveyorController)} on '{name}' requires exactly {SlotCount} slot children under Slot Root, but found {slotRoot.childCount}.", logErrors);
                return false;
            }

            for (int i = 0; i < SlotCount; i++)
            {
                Transform child = slotRoot.GetChild(i);
                ConveyorSlot slot = child.GetComponent<ConveyorSlot>();
                if (slot == null)
                {
                    LogSlotValidationError($"{nameof(ConveyorController)} on '{name}' requires ConveyorSlot on '{child.name}'. Regenerate conveyor slots.", logErrors);
                    return false;
                }

                if (slot.MarbleAnchor == null)
                {
                    LogSlotValidationError($"{nameof(ConveyorController)} on '{name}' requires MarbleAnchor on '{child.name}'. Regenerate conveyor slots or assign the anchor.", logErrors);
                    return false;
                }

                slots[i] = slot;
                baseSlotLocalPositions[i] = child.localPosition;
            }

            slotsCached = true;
            slotValidationErrorLogged = false;
            previousSlotPositionsCached = false;
            return true;
        }

        private void ApplySlotPositions()
        {
            if (!slotsCached)
            {
                return;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                ConveyorSlot slot = slots[i];
                if (slot == null)
                {
                    continue;
                }

                slot.transform.localPosition = EvaluateLocalPathPosition(i + globalPathOffset);
            }
        }

        private void CachePreviousSlotWorldPositions()
        {
            if (!slotsCached)
            {
                previousSlotPositionsCached = false;
                return;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                ConveyorSlot slot = slots[i];
                if (slot == null)
                {
                    previousSlotWorldPositions[i] = transform.position;
                    continue;
                }

                previousSlotWorldPositions[i] = slot.MarbleAnchor != null
                    ? slot.MarbleAnchor.position
                    : slot.transform.position;
            }

            previousSlotPositionsCached = true;
        }

        private Vector3 EvaluateLocalPathPosition(float pathPosition)
        {
            float normalizedPathPosition = NormalizePathPosition(pathPosition);
            int currentIndex = Mathf.FloorToInt(normalizedPathPosition);
            float t = normalizedPathPosition - currentIndex;

            Vector3 p0 = baseSlotLocalPositions[WrapIndex(currentIndex - 1)];
            Vector3 p1 = baseSlotLocalPositions[WrapIndex(currentIndex)];
            Vector3 p2 = baseSlotLocalPositions[WrapIndex(currentIndex + 1)];
            Vector3 p3 = baseSlotLocalPositions[WrapIndex(currentIndex + 2)];

            return EvaluateCatmullRom(p0, p1, p2, p3, t);
        }

        private static Vector3 EvaluateCatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        private int CountSlots(ConveyorSlotState state)
        {
            if (!EnsureSlotsCached(false))
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null && slots[i].State == state)
                {
                    count++;
                }
            }

            return count;
        }

        private ConveyorSlot FindSlotForMarble(Marble marble)
        {
            if (marble == null || !EnsureSlotsCached(true))
            {
                return null;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                ConveyorSlot slot = slots[i];
                if (slot != null && slot.CurrentMarble == marble)
                {
                    return slot;
                }
            }

            return null;
        }

        private int GetRuntimeSlotIndex(ConveyorSlot slot)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == slot)
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindEntryReservationIndex(int reservationId)
        {
            for (int i = 0; i < entryReservations.Count; i++)
            {
                if (entryReservations[i].Id == reservationId)
                {
                    return i;
                }
            }

            return -1;
        }

#if UNITY_EDITOR
        public void DebugCollectMarbles(List<MarbleDebugObservation> result)
        {
            // Motions are the moving conveyor owners; incoming/outgoing slot reservations
            // can refer to the same marble and are not independent owners.
            foreach (var motion in marbleMotions)
            {
                bool slotMatches = motion.Slot != null && motion.Slot.CurrentMarble == motion.Marble;
                MarbleDebugTracker.Observe(result, motion.Marble,
                    slotMatches ? MarbleDebugLocation.Conveyor : MarbleDebugLocation.Unknown, this,
                    $"Conveyor #{GetInstanceID()} / Slot {motion.Slot?.SlotIndex} / Entry {motion.EntryOrder}" +
                    (slotMatches ? "" : " / MOTION-SLOT MISMATCH"));
            }
            foreach (var slot in slots)
                if (slot != null && slot.CurrentMarble != null &&
                    !marbleMotions.Exists(motion => motion.Marble == slot.CurrentMarble && motion.Slot == slot))
                    MarbleDebugTracker.Observe(result, slot.CurrentMarble, MarbleDebugLocation.Unknown, slot,
                        $"ORPHAN / DUPLICATE conveyor slot {slot.SlotIndex} (no matching motion)");
        }
#endif

        private void RemoveNullSlotMarbles()
        {
            bool contentsChanged = false;
            for (int i = 0; i < slots.Length; i++)
            {
                ConveyorSlot slot = slots[i];
                if (slot == null || !slot.IsOccupied || slot.CurrentMarble != null)
                {
                    continue;
                }

                slot.Clear();
                contentsChanged = true;
            }

            // Reevaluate only if the existing null-owner cleanup actually changed a slot.
            if (contentsChanged) RefreshWarningGlow();
        }

        private void DestroyContainerMarbles()
        {
            if (conveyorMarbleContainer == null)
            {
                return;
            }

            for (int i = conveyorMarbleContainer.childCount - 1; i >= 0; i--)
            {
                Transform child = conveyorMarbleContainer.GetChild(i);
                if (child.GetComponent<Marble>() == null)
                {
                    continue;
                }

                DestroyGameObject(child.gameObject);
            }
        }

        private static float NormalizePathPosition(float pathPosition)
        {
            pathPosition %= SlotCount;
            if (pathPosition < 0f)
            {
                pathPosition += SlotCount;
            }

            return pathPosition;
        }

        private static int WrapIndex(int index)
        {
            index %= SlotCount;
            return index < 0 ? index + SlotCount : index;
        }

        private static void SortMarbleCaptureResults(Marble[] results, float[] distances, int count)
        {
            for (int i = 1; i < count; i++)
            {
                Marble marble = results[i];
                float distance = distances[i];
                int j = i - 1;
                while (j >= 0 && distances[j] > distance)
                {
                    results[j + 1] = results[j];
                    distances[j + 1] = distances[j];
                    j--;
                }

                results[j + 1] = marble;
                distances[j + 1] = distance;
            }
        }

        private static bool TryGetSweptCaptureDistance(
            Vector2 origin,
            Vector2 forward,
            Vector2 side,
            float length,
            float halfWidth,
            float radius,
            Vector2 previousPosition,
            Vector2 currentPosition,
            out float forwardDistance)
        {
            Vector2 previousLocal = ToCaptureLocal(previousPosition, origin, forward, side);
            Vector2 currentLocal = ToCaptureLocal(currentPosition, origin, forward, side);

            float minX = -radius;
            float maxX = length + radius;
            float minY = -halfWidth - radius;
            float maxY = halfWidth + radius;

            if (IsInsideCaptureBounds(currentLocal, minX, maxX, minY, maxY))
            {
                forwardDistance = Mathf.Clamp(currentLocal.x, 0f, length);
                return true;
            }

            if (IsInsideCaptureBounds(previousLocal, minX, maxX, minY, maxY))
            {
                forwardDistance = Mathf.Clamp(previousLocal.x, 0f, length);
                return true;
            }

            if (TryIntersectSegmentBounds(previousLocal, currentLocal, minX, maxX, minY, maxY, out float t))
            {
                forwardDistance = Mathf.Clamp(Mathf.Lerp(previousLocal.x, currentLocal.x, t), 0f, length);
                return true;
            }

            forwardDistance = 0f;
            return false;
        }

        private static Vector2 ToCaptureLocal(Vector2 position, Vector2 origin, Vector2 forward, Vector2 side)
        {
            Vector2 offset = position - origin;
            return new Vector2(Vector2.Dot(offset, forward), Vector2.Dot(offset, side));
        }

        private static bool IsInsideCaptureBounds(Vector2 localPosition, float minX, float maxX, float minY, float maxY)
        {
            return localPosition.x >= minX &&
                   localPosition.x <= maxX &&
                   localPosition.y >= minY &&
                   localPosition.y <= maxY;
        }

        private static bool TryIntersectSegmentBounds(
            Vector2 start,
            Vector2 end,
            float minX,
            float maxX,
            float minY,
            float maxY,
            out float entryT)
        {
            Vector2 delta = end - start;
            float tMin = 0f;
            float tMax = 1f;

            if (!ClipSegmentAxis(start.x, delta.x, minX, maxX, ref tMin, ref tMax) ||
                !ClipSegmentAxis(start.y, delta.y, minY, maxY, ref tMin, ref tMax))
            {
                entryT = 0f;
                return false;
            }

            entryT = tMin;
            return true;
        }

        private static bool ClipSegmentAxis(
            float start,
            float delta,
            float min,
            float max,
            ref float tMin,
            ref float tMax)
        {
            if (Mathf.Approximately(delta, 0f))
            {
                return start >= min && start <= max;
            }

            float inverseDelta = 1f / delta;
            float t1 = (min - start) * inverseDelta;
            float t2 = (max - start) * inverseDelta;
            if (t1 > t2)
            {
                float temp = t1;
                t1 = t2;
                t2 = temp;
            }

            tMin = Mathf.Max(tMin, t1);
            tMax = Mathf.Min(tMax, t2);
            return tMin <= tMax;
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

        #endregion

        #region Gizmos

        private void OnDrawGizmos()
        {
            if (!showRuntimeGizmos || !EnsureSlotsCached(false))
            {
                return;
            }

            Gizmos.color = Color.magenta;
            const int Samples = SlotCount * 4;
            Vector3 previous = slotRoot.TransformPoint(EvaluateLocalPathPosition(globalPathOffset));
            for (int i = 1; i <= Samples; i++)
            {
                Vector3 next = slotRoot.TransformPoint(EvaluateLocalPathPosition(globalPathOffset + i / 4f));
                Gizmos.DrawLine(previous, next);
                previous = next;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                ConveyorSlot slot = slots[i];
                if (slot == null)
                {
                    continue;
                }

                Gizmos.color = slot.IsOccupied ? Color.yellow : slot.IsReserved ? Color.red : slot.IsBlocked ? Color.cyan : Color.white;
                Gizmos.DrawWireSphere(slot.transform.position, 0.08f);
            }
        }

        private void LogSlotValidationError(string message, bool shouldLog)
        {
            if (!shouldLog || slotValidationErrorLogged)
            {
                return;
            }

            slotValidationErrorLogged = true;
            Debug.LogError(message, this);
        }

        #endregion
    }
}
