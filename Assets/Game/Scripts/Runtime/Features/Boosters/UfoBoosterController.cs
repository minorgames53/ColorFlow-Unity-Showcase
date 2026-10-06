using System.Collections.Generic;
using DG.Tweening;
using Game.Shared.Audio;
using Gameplay.Conveyor;
using Gameplay.Levels;
using Gameplay.SourceBoxes;
using Gameplay.TargetBoxes;
using UnityEngine;
#if UNITY_EDITOR
using Gameplay.MarbleDebug;
#endif

namespace Gameplay.Boosters
{
    public enum UfoBoosterRuntimeState
    {
        Idle = 0,
        Entering = 1,
        Ready = 2,
        Collecting = 3,
        Holding = 4,
        PreparingFire = 5,
        ReadyForFire = 6,
        Firing = 7,
        WaitingForLane = 8,
        Completing = 9,
        BlockedNoTarget = 10,
        Exiting = 11
    }

    [DisallowMultipleComponent]
    public sealed class UfoBoosterController : MonoBehaviour
    {
        [Header("Runtime Services")]
        [SerializeField] private BoosterController boosterController;
        [SerializeField] private ConveyorController conveyorController;
        [SerializeField] private TargetLaneController targetLaneController;
        [SerializeField] private LevelSessionController levelSessionController;

        [Header("UFO Hierarchy")]
        [SerializeField] private Transform ufoRoot;
        [SerializeField] private Transform view;
        [SerializeField] private SpriteRenderer glassRenderer;
        [SerializeField] private SpriteRenderer bodyRenderer;
        [SerializeField] private GameObject maskObject;
        [SerializeField] private Transform maskTransferTarget;
        [SerializeField] private SpriteRenderer bodyTopRenderer;
        [SerializeField] private Transform shooterAnchor;
        [SerializeField] private SpriteRenderer shooterBottomRenderer;
        [SerializeField] private SpriteRenderer shooterTopRenderer;
        [SerializeField] private Transform marbleStartPosition;
        [SerializeField] private Transform marbleCollectPosition;
        [SerializeField] private Transform marbleWaitingCenter;

        [Header("Entry")]
        [SerializeField] private float entryStartY = -8f;
        [SerializeField, Min(0f)] private float entryStartScaleMultiplier = 0.5f;
        [SerializeField, Min(0f)] private float entryDuration = 0.4f;
        [SerializeField] private Ease entryMoveEase = Ease.OutQuad;
        [SerializeField] private Ease entryScaleEase = Ease.OutBack;

        [Header("Collect")]
        [SerializeField, Min(0f)] private float collectRandomDelayMin;
        [SerializeField, Min(0f)] private float collectRandomDelayMax = 0.1f;
        [SerializeField, Min(0f)] private float collectToEntryDuration = 0.28f;
        [SerializeField, Min(0f)] private float collectCurveHorizontalStrength = 0.65f;
        [SerializeField] private float collectCurveVerticalOffset = -1f;
        [SerializeField] private Ease collectToEntryEase = Ease.InQuad;
        [SerializeField, Min(0f)] private float collectToWaitingDuration = 0.16f;
        [SerializeField] private Ease collectToWaitingEase = Ease.OutQuad;
        [SerializeField, Min(0f)] private float collectScaleMultiplier = 0.8f;
        [SerializeField, Min(0f)] private float waitingRadius = 0.5f;
        [SerializeField, Min(0f)] private float chamberHoldDuration = 0.5f;
        [SerializeField] private float ufoMarbleWorldZ = -5f;

        [Header("Collect Glow")]
        [SerializeField] private GameObject ufoGlowPrefab;
        [SerializeField, Min(0f)] private float collectGlowFadeDuration = 0.12f;
        [SerializeField] private Ease collectGlowFadeEase = Ease.OutQuad;
        [SerializeField] private int collectGlowSortingOffset = -1;

        [Header("Fire Preparation")]
        [SerializeField, Min(0f)] private float maskTransferDuration = 0.22f;
        [SerializeField] private Ease maskTransferEase = Ease.InQuad;
        [SerializeField, Min(0f)] private float maskTransferScaleMultiplier = 0.75f;
        [SerializeField, Min(0f)] private float maskHideDelay = 0.03f;
        [SerializeField, Min(0f)] private float shooterRotateDuration = 0.25f;
        [SerializeField] private Ease shooterRotateEase = Ease.OutQuad;
        [SerializeField] private float shooterAngleOffset;

        [Header("Fire")]
        [SerializeField, Min(0.01f)] private float fireSpeedMultiplier = 2f;
        [SerializeField, Min(0f)] private float shotDuration = 0.2f;
        [SerializeField] private Ease shotEase = Ease.InQuad;
        [SerializeField] private float shotCurveStrength = 0.2f;
        [SerializeField, Min(0f)] private float shotLaunchInterval = 0.3f;
        [SerializeField, Min(0f)] private float shotMarbleStartScale = 5.3f;
        [SerializeField, Min(0f)] private float shotMarbleScaleDuration = 0.2f;
        [SerializeField] private Ease shotMarbleScaleEase = Ease.InQuad;
        [SerializeField, Min(0f)] private float shotArrivalPunch = 0.08f;
        [SerializeField, InspectorName("UFO Shot Scale Multiplier"), Min(1f)]
        private float ufoShotScaleMultiplier = 1.08f;
        [SerializeField, InspectorName("UFO Shot Scale Up Duration"), Min(0f)]
        private float ufoShotScaleUpDuration = 0.06f;
        [SerializeField, InspectorName("UFO Shot Scale Down Duration"), Min(0f)]
        private float ufoShotScaleDownDuration = 0.08f;
        [SerializeField, InspectorName("UFO Shot Scale Up Ease")]
        private Ease ufoShotScaleUpEase = Ease.OutQuad;
        [SerializeField, InspectorName("UFO Shot Scale Down Ease")]
        private Ease ufoShotScaleDownEase = Ease.InQuad;
        [SerializeField, Min(0f)] private float completionDelay = 0.15f;

        [Header("Exit")]
        [SerializeField] private float exitTargetY = -8f;
        [SerializeField, Min(0f)] private float exitScaleMultiplier = 0.5f;
        [SerializeField, Min(0f)] private float exitDuration = 0.3f;
        [SerializeField] private Ease exitEase = Ease.InQuad;

        [Header("Sorting Infrastructure")]
        [SerializeField] private int collectMarbleOrderInLayer = 10;
        [SerializeField] private int bodyCollectOrderInLayer = 11;
        [SerializeField] private int fireMarbleOrderInLayer = 13;
        [SerializeField] private int bodyFireOrderInLayer = 14;

        [Header("Target Recovery")]
        [SerializeField, Min(0.02f)] private float noTargetRetryInterval = 0.1f;
        [SerializeField, Min(0.5f)] private float noTargetRecoveryTimeout = 5f;

        private Vector3 originalRootPosition;
        private Vector3 originalViewLocalScale;
        private Vector3 ufoShotBaseLocalScale;
        private Vector3 originalShooterLocalScale;
        private Quaternion originalShooterLocalRotation;
        private int originalBodySortingOrder;
        private Sequence presentationSequence;
        private Sequence ufoShotScaleSequence;
        private int presentationLifecycleVersion;
        private int activeBoosterLifecycleVersion;
        private bool initialized;
        private bool ownsActiveUfo;
        private TargetBox currentAimTarget;
        private Transform ufoMarbleRoot;
        private bool shooterPreparationInProgress;
        private bool launchCadencePending;
        private bool waitingForTargetAvailability;
        private int inFlightShotCount;
        private int nextReservationSequence;
        private float nextAllowedLaunchTime;
        private float targetWaitStartedAt = -1f;
        private float nextTargetRetryAt;
        private bool targetWaitWarningLogged;
        private bool abortRecoveryPending;
        private float nextAbortRecoveryRetryAt;
        private bool abortRecoveryWarningLogged;
        private ConveyorEntryZone conveyorEntryZone;
        private bool gameplayMarbleMovementFrozen;
        private bool ufoShotBaseScaleCached;
        private readonly List<Marble> conveyorSnapshot = new List<Marble>();
        private readonly List<CapturedMarbleRecord> capturedMarbles = new List<CapturedMarbleRecord>();
        private readonly List<FrozenMarblePhysicsRecord> frozenMarblePhysics =
            new List<FrozenMarblePhysicsRecord>();
        private readonly List<SourceBoxReleaseAnimator> frozenReleaseAnimators =
            new List<SourceBoxReleaseAnimator>();

        private enum UfoMarbleState
        {
            Capturing = 0,
            Chamber = 1,
            MovingToMask = 2,
            AtShooter = 3,
            ReadyToFire = 4,
            Firing = 5,
            Delivered = 6
        }

        private sealed class CapturedRendererState
        {
            public SpriteRenderer Renderer;
            public int SortingLayerId;
            public int SortingOrder;
            public SpriteMaskInteraction MaskInteraction;
        }

        private sealed class CapturedMarbleRecord
        {
            public Marble Marble;
            public int OriginalBaseSortingOrder;
            public Transform OriginalParent;
            public int OriginalSiblingIndex;
            public Vector3 OriginalLocalPosition;
            public Quaternion OriginalLocalRotation;
            public Vector3 OriginalLocalScale;
            public Vector3 WaitingLocalPosition;
            public Vector3 CollectStartLocalScale;
            public UfoMarbleState State;
            public CapturedRendererState[] Renderers;
            public TargetBox AimTarget;
            public float CollectStartDelay;
            public TargetLaneController.UfoTransferReservation Reservation;
            public Sequence ShotSequence;
            public Sequence GlowFadeSequence;
            public GameObject CollectGlow;
            public SpriteRenderer[] CollectGlowRenderers;
            public int ReservationSequence;
        }

        private sealed class FrozenMarblePhysicsRecord
        {
            public Marble Marble;
            public Rigidbody2D Rigidbody;
            public bool WasSimulated;
            public bool WasSleeping;
            public RigidbodyType2D BodyType;
            public RigidbodyConstraints2D Constraints;
            public float GravityScale;
            public Vector2 LinearVelocity;
            public float AngularVelocity;
            public bool CapturedByUfo;
        }

        public UfoBoosterRuntimeState RuntimeState { get; private set; } = UfoBoosterRuntimeState.Idle;
        public int CollectMarbleOrderInLayer => collectMarbleOrderInLayer;
        public int FireMarbleOrderInLayer => fireMarbleOrderInLayer;

        private void Awake()
        {
            CacheMissingRuntimeServices();
            InitializePresentation();
        }

        private void OnEnable()
        {
            CacheMissingRuntimeServices();
            InitializePresentation();
            Subscribe();
            RegisterActivationGuard();
            if (abortRecoveryPending)
            {
                TryCompletePendingAbortRecovery();
                if (abortRecoveryPending)
                {
                    return;
                }
            }

            TryStartActiveUfo();
        }

        private void OnDisable()
        {
            UnregisterActivationGuard();
            Unsubscribe();

            if (boosterController != null && boosterController.ActiveBooster == BoosterType.Ufo)
            {
                boosterController.NotifyCancelled(BoosterType.Ufo, boosterController.ActiveLifecycleVersion);
            }

            ownsActiveUfo = false;
            ResetPresentationImmediately();
        }

        private void Update()
        {
            if (abortRecoveryPending)
            {
                if (Time.unscaledTime >= nextAbortRecoveryRetryAt)
                {
                    nextAbortRecoveryRetryAt = Time.unscaledTime + noTargetRetryInterval;
                    TryCompletePendingAbortRecovery();
                }

                return;
            }

            if (levelSessionController == null || !levelSessionController.IsPlaying ||
                !waitingForTargetAvailability || shooterPreparationInProgress || launchCadencePending ||
                !IsCurrentUfoLifecycle(activeBoosterLifecycleVersion))
            {
                return;
            }

            float currentTime = Time.unscaledTime;
            if (targetWaitStartedAt >= 0f &&
                currentTime - targetWaitStartedAt >= noTargetRecoveryTimeout)
            {
                RecoverBlockedUfoTransaction(activeBoosterLifecycleVersion);
                return;
            }

            if (!targetWaitWarningLogged && targetWaitStartedAt >= 0f &&
                currentTime - targetWaitStartedAt >= Mathf.Max(0.5f, noTargetRetryInterval))
            {
                targetWaitWarningLogged = true;
                bool hasTransientTargetActivity =
                    targetLaneController != null && targetLaneController.HasUfoTargetTransitionActivity;
                Debug.LogWarning(
                    $"{nameof(UfoBoosterController)} on '{name}' is still waiting for a UFO target. " +
                    $"Transient target activity: {hasTransientTargetActivity}, in-flight shots: {inFlightShotCount}. " +
                    $"Recovery timeout: {noTargetRecoveryTimeout:0.###}s.",
                    this);
            }

            if (currentTime < nextTargetRetryAt)
            {
                return;
            }

            nextTargetRetryAt = currentTime + noTargetRetryInterval;
            TryStartNextFirePreparation(activeBoosterLifecycleVersion);
        }

        private void OnValidate()
        {
            entryStartScaleMultiplier = Mathf.Max(0f, entryStartScaleMultiplier);
            entryDuration = Mathf.Max(0f, entryDuration);
            exitScaleMultiplier = Mathf.Max(0f, exitScaleMultiplier);
            exitDuration = Mathf.Max(0f, exitDuration);
            collectRandomDelayMin = Mathf.Max(0f, collectRandomDelayMin);
            collectRandomDelayMax = Mathf.Max(collectRandomDelayMin, collectRandomDelayMax);
            collectToEntryDuration = Mathf.Max(0f, collectToEntryDuration);
            collectCurveHorizontalStrength = Mathf.Max(0f, collectCurveHorizontalStrength);
            collectToWaitingDuration = Mathf.Max(0f, collectToWaitingDuration);
            collectScaleMultiplier = Mathf.Max(0f, collectScaleMultiplier);
            waitingRadius = Mathf.Max(0f, waitingRadius);
            chamberHoldDuration = Mathf.Max(0f, chamberHoldDuration);
            collectGlowFadeDuration = Mathf.Max(0f, collectGlowFadeDuration);
            maskTransferDuration = Mathf.Max(0f, maskTransferDuration);
            maskTransferScaleMultiplier = Mathf.Max(0f, maskTransferScaleMultiplier);
            maskHideDelay = Mathf.Max(0f, maskHideDelay);
            shooterRotateDuration = Mathf.Max(0f, shooterRotateDuration);
            fireSpeedMultiplier = Mathf.Max(0.01f, fireSpeedMultiplier);
            shotDuration = Mathf.Max(0f, shotDuration);
            shotLaunchInterval = Mathf.Max(0f, shotLaunchInterval);
            shotMarbleStartScale = Mathf.Max(0f, shotMarbleStartScale);
            shotMarbleScaleDuration = Mathf.Max(0f, shotMarbleScaleDuration);
            shotArrivalPunch = Mathf.Max(0f, shotArrivalPunch);
            ufoShotScaleMultiplier = Mathf.Max(1f, ufoShotScaleMultiplier);
            ufoShotScaleUpDuration = Mathf.Max(0f, ufoShotScaleUpDuration);
            ufoShotScaleDownDuration = Mathf.Max(0f, ufoShotScaleDownDuration);
            completionDelay = Mathf.Max(0f, completionDelay);
            noTargetRetryInterval = Mathf.Max(0.02f, noTargetRetryInterval);
            noTargetRecoveryTimeout = Mathf.Max(0.5f, noTargetRecoveryTimeout);
        }

        public bool CanActivateUfo()
        {
            CacheMissingRuntimeServices();
            return enabled && gameObject.activeInHierarchy && !abortRecoveryPending &&
                   RuntimeState == UfoBoosterRuntimeState.Idle &&
                   conveyorController != null && targetLaneController != null &&
                   (conveyorController.MarbleCount > 0 || HasActiveConveyorEntryTransfer()) &&
                   HasCapturableMarbleForUfo() &&
                   ValidatePresentationReferences(false);
        }

        private bool HasCapturableMarbleForUfo()
        {
            PopulateConveyorSnapshot();
            Dictionary<MarbleColorId, int> capacityByColor = GetAvailableUfoCaptureCapacity();
            bool hasCapturableMarble = false;
            for (int i = 0; i < conveyorSnapshot.Count; i++)
            {
                Marble marble = conveyorSnapshot[i];
                if (marble != null && marble.CanBeOwnedByUfo &&
                    capacityByColor.TryGetValue(marble.ColorId, out int availableCapacity) &&
                    availableCapacity > 0)
                {
                    hasCapturableMarble = true;
                    break;
                }
            }

            conveyorSnapshot.Clear();
            return hasCapturableMarble;
        }

        private void PopulateConveyorSnapshot()
        {
            conveyorSnapshot.Clear();
            conveyorController?.GetOccupiedMarbles(conveyorSnapshot);
            conveyorEntryZone?.AppendActiveTransferMarblesForUfo(conveyorSnapshot);
        }

        private Dictionary<MarbleColorId, int> GetAvailableUfoCaptureCapacity()
        {
            Dictionary<MarbleColorId, int> capacityByColor =
                new Dictionary<MarbleColorId, int>();
            targetLaneController?.GetAvailableUfoCapacityByColor(capacityByColor);
            return capacityByColor;
        }

        private bool HasActiveConveyorEntryTransfer()
        {
            return conveyorEntryZone != null && conveyorEntryZone.ActiveTransferMarbleCount > 0;
        }

        public void ApplyCollectSorting()
        {
            if (bodyRenderer != null)
            {
                ApplyRendererSorting(
                    bodyRenderer,
                    bodyRenderer.sortingLayerID,
                    bodyCollectOrderInLayer);
            }
        }

        public void ApplyFireSorting()
        {
            if (bodyRenderer != null)
            {
                ApplyRendererSorting(
                    bodyRenderer,
                    bodyRenderer.sortingLayerID,
                    bodyFireOrderInLayer);
            }
        }

        public void RestoreSorting()
        {
            if (bodyRenderer != null && initialized)
            {
                ApplyRendererSorting(
                    bodyRenderer,
                    bodyRenderer.sortingLayerID,
                    originalBodySortingOrder);
            }
        }

        private bool CanCancelUfo()
        {
            return RuntimeState == UfoBoosterRuntimeState.Idle ||
                   RuntimeState == UfoBoosterRuntimeState.Entering;
        }

        private void HandleBoosterActivated(BoosterType type)
        {
            if (type == BoosterType.Ufo)
            {
                TryStartActiveUfo();
            }
        }

        private void HandleBoosterStateChanged(BoosterType type, BoosterState state)
        {
            if (!ownsActiveUfo || type == BoosterType.Ufo && state == BoosterState.Running)
            {
                return;
            }

            ownsActiveUfo = false;
            if (RuntimeState == UfoBoosterRuntimeState.Entering ||
                RuntimeState == UfoBoosterRuntimeState.Ready)
            {
                BeginExit();
                return;
            }

            ResetPresentationImmediately();
        }

        private void TryStartActiveUfo()
        {
            if (ownsActiveUfo || boosterController == null ||
                boosterController.ActiveBooster != BoosterType.Ufo ||
                boosterController.State != BoosterState.Running)
            {
                return;
            }

            activeBoosterLifecycleVersion = boosterController.ActiveLifecycleVersion;
            if (!CanActivateUfo())
            {
                boosterController.NotifyCancelled(BoosterType.Ufo, activeBoosterLifecycleVersion);
                return;
            }

            ownsActiveUfo = true;
            targetLaneController?.BeginUfoTargetPreviewTransaction();
            FreezeGameplayMarbleMovement();
            BeginEntry(activeBoosterLifecycleVersion);
        }

        private void BeginEntry(int expectedBoosterLifecycleVersion)
        {
            presentationLifecycleVersion++;
            int expectedPresentationVersion = presentationLifecycleVersion;
            KillPresentationSequence();
            KillUfoShotScaleSequence(true);
            ufoShotBaseScaleCached = false;
            RestoreAuthoredPresentationState();

            RuntimeState = UfoBoosterRuntimeState.Entering;
            view.gameObject.SetActive(true);
            maskObject.SetActive(false);
            AudioManager.Instance?.PlaySfx(AudioKey.UfoIn);

            Vector3 startPosition = originalRootPosition;
            startPosition.x = 0f;
            startPosition.y = entryStartY;
            ufoRoot.position = startPosition;
            view.localScale = originalViewLocalScale * entryStartScaleMultiplier;

            Vector3 targetPosition = startPosition;
            targetPosition.x = 0f;
            targetPosition.y = 0f;

            Sequence sequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            sequence.Join(ufoRoot.DOMove(targetPosition, entryDuration).SetEase(entryMoveEase));
            sequence.Join(view.DOScale(originalViewLocalScale, entryDuration).SetEase(entryScaleEase));
            sequence.OnComplete(() =>
            {
                if (!ownsActiveUfo || presentationLifecycleVersion != expectedPresentationVersion ||
                    boosterController == null || boosterController.ActiveBooster != BoosterType.Ufo ||
                    boosterController.State != BoosterState.Running ||
                    boosterController.ActiveLifecycleVersion != expectedBoosterLifecycleVersion)
                {
                    return;
                }

                presentationSequence = null;
                ufoShotBaseLocalScale = view.localScale;
                ufoShotBaseScaleCached = true;
                BeginCollect(expectedBoosterLifecycleVersion);
            });

            presentationSequence = sequence;
        }

        private void BeginCollect(int expectedBoosterLifecycleVersion)
        {
            if (!IsCurrentUfoLifecycle(expectedBoosterLifecycleVersion) || conveyorController == null)
            {
                return;
            }

            PopulateConveyorSnapshot();
            if (conveyorSnapshot.Count == 0)
            {
                BeginUfoCompletion(expectedBoosterLifecycleVersion);
                return;
            }

            Dictionary<MarbleColorId, int> capacityByColor = GetAvailableUfoCaptureCapacity();

            CreateUfoMarbleRoot();
            if (ufoMarbleRoot == null)
            {
                boosterController.NotifyCancelled(BoosterType.Ufo, expectedBoosterLifecycleVersion);
                return;
            }

            ApplyCollectSorting();
            capturedMarbles.Clear();
            for (int i = 0; i < conveyorSnapshot.Count; i++)
            {
                Marble marble = conveyorSnapshot[i];
                if (marble == null ||
                    !capacityByColor.TryGetValue(marble.ColorId, out int availableCapacity) ||
                    availableCapacity <= 0 || !TryCaptureConveyorMarble(marble))
                {
                    continue;
                }

                capacityByColor[marble.ColorId] = availableCapacity - 1;
            }

            conveyorSnapshot.Clear();
            if (capturedMarbles.Count == 0)
            {
                BeginUfoCompletion(expectedBoosterLifecycleVersion);
                return;
            }

            AssignWaitingPositions();
            RuntimeState = UfoBoosterRuntimeState.Collecting;
            AudioManager.Instance?.PlaySfx(AudioKey.UfoSuckIn);
            presentationLifecycleVersion++;
            int expectedPresentationVersion = presentationLifecycleVersion;

            Sequence sequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            for (int i = 0; i < capturedMarbles.Count; i++)
            {
                CapturedMarbleRecord record = capturedMarbles[i];
                record.CollectStartDelay = Random.Range(
                    collectRandomDelayMin,
                    collectRandomDelayMax);
                Vector3 waitingWorldPosition = ufoMarbleRoot.TransformPoint(record.WaitingLocalPosition);
                Sequence marbleSequence = CreateMarbleCollectSequence(
                    record,
                    waitingWorldPosition);
                sequence.Insert(
                    record.CollectStartDelay,
                    marbleSequence);
                sequence.Insert(
                    record.CollectStartDelay,
                    record.Marble.transform
                        .DOScale(
                            record.CollectStartLocalScale * collectScaleMultiplier,
                            collectToEntryDuration + collectToWaitingDuration)
                        .SetEase(collectToWaitingEase));
                sequence.InsertCallback(
                    record.CollectStartDelay + collectToEntryDuration + collectToWaitingDuration,
                    () => CompleteMarbleCapture(record, expectedPresentationVersion));
            }

            sequence.OnComplete(() => BeginChamberHold(
                expectedBoosterLifecycleVersion,
                expectedPresentationVersion));
            presentationSequence = sequence;
        }

        private Sequence CreateMarbleCollectSequence(
            CapturedMarbleRecord record,
            Vector3 waitingWorldPosition)
        {
            Transform marbleTransform = record.Marble.transform;
            Vector3 curveStart = marbleTransform.position;
            Vector3 curveEnd = marbleCollectPosition.position;
            curveStart.z = ufoMarbleWorldZ;
            curveEnd.z = ufoMarbleWorldZ;
            waitingWorldPosition.z = ufoMarbleWorldZ;
            Vector3 midpoint = Vector3.Lerp(curveStart, curveEnd, 0.5f);
            Vector3 curveControl = midpoint;
            curveControl.x = Mathf.LerpUnclamped(
                midpoint.x,
                curveEnd.x,
                collectCurveHorizontalStrength);
            curveControl.y += collectCurveVerticalOffset;

            float curveProgress = 0f;
            Tween curveTween = DOTween.To(
                    () => curveProgress,
                    value =>
                    {
                        curveProgress = value;
                        if (marbleTransform != null)
                        {
                            marbleTransform.position = EvaluateQuadraticBezier(
                                curveStart,
                                curveControl,
                                curveEnd,
                                value);
                        }
                    },
                    1f,
                    collectToEntryDuration)
                .SetEase(collectToEntryEase);

            return DOTween.Sequence()
                .Append(curveTween)
                .Append(
                    marbleTransform
                        .DOMove(waitingWorldPosition, collectToWaitingDuration)
                        .SetEase(collectToWaitingEase));
        }

        private static Vector3 EvaluateQuadraticBezier(
            Vector3 start,
            Vector3 control,
            Vector3 end,
            float progress)
        {
            float clampedProgress = Mathf.Clamp01(progress);
            float inverse = 1f - clampedProgress;
            return inverse * inverse * start +
                   2f * inverse * clampedProgress * control +
                   clampedProgress * clampedProgress * end;
        }

        private bool TryCaptureConveyorMarble(Marble marble)
        {
            if (marble == null || !marble.CanBeOwnedByUfo || !marble.TryBeginUfoTransfer())
            {
                return false;
            }

            CapturedMarbleRecord record = CreateCapturedMarbleRecord(marble);
            bool ownershipRemoved = conveyorController.RemoveMarble(marble) ||
                                    conveyorEntryZone != null &&
                                    conveyorEntryZone.TryDetachActiveTransferForUfo(marble);
            if (!ownershipRemoved)
            {
                marble.CancelUfoTransfer();
                Debug.LogWarning(
                    $"{nameof(UfoBoosterController)} on '{name}' skipped marble '{marble.name}' because its conveyor or entry-transfer ownership changed before the UFO snapshot could be committed.",
                    marble);
                return false;
            }

            marble.PrepareForUfoControl();
            MarkFrozenMarbleCaptured(marble);
            marble.transform.SetParent(ufoMarbleRoot, true);
            Vector3 capturedPosition = marble.transform.position;
            capturedPosition.z = ufoMarbleWorldZ;
            marble.transform.position = capturedPosition;
            record.CollectStartLocalScale = marble.transform.localScale;
            ApplyMarbleSorting(record, collectMarbleOrderInLayer);
            CreateCollectGlow(record);
            capturedMarbles.Add(record);
#if UNITY_EDITOR
            MarbleDebugTracker.SetLocation(marble, MarbleDebugLocation.Ufo, this,
                $"UFO #{GetInstanceID()} / Capturing", "UFO capture committed");
#endif
            levelSessionController?.MarkPlayerMoveCommitted();
            return true;
        }

#if UNITY_EDITOR
        public void DebugCollectMarbles(List<MarbleDebugObservation> result)
        {
            foreach (var record in capturedMarbles)
                // A pending target reservation owns the marble; capturedMarbles also retains
                // presentation/rollback data until arrival and is not a second owner then.
                if (record?.Marble != null && record.State != UfoMarbleState.Delivered &&
                    !(record.Reservation != null && record.Reservation.IsPending))
                    MarbleDebugTracker.Observe(result, record.Marble, MarbleDebugLocation.Ufo, this,
                        $"UFO #{GetInstanceID()} / {record.State}");
        }
#endif

        private CapturedMarbleRecord CreateCapturedMarbleRecord(Marble marble)
        {
            Transform marbleTransform = marble.transform;
            SpriteRenderer[] renderers = marble.GetComponentsInChildren<SpriteRenderer>(true);
            CapturedRendererState[] rendererStates = new CapturedRendererState[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer renderer = renderers[i];
                rendererStates[i] = new CapturedRendererState
                {
                    Renderer = renderer,
                    SortingLayerId = renderer.sortingLayerID,
                    SortingOrder = renderer.sortingOrder,
                    MaskInteraction = renderer.maskInteraction
                };
            }

            return new CapturedMarbleRecord
            {
                Marble = marble,
                OriginalBaseSortingOrder = marble.VisualSortingOrder,
                OriginalParent = marbleTransform.parent,
                OriginalSiblingIndex = marbleTransform.GetSiblingIndex(),
                OriginalLocalPosition = marbleTransform.localPosition,
                OriginalLocalRotation = marbleTransform.localRotation,
                OriginalLocalScale = marbleTransform.localScale,
                State = UfoMarbleState.Capturing,
                Renderers = rendererStates
            };
        }

        private void AssignWaitingPositions()
        {
            for (int i = 0; i < capturedMarbles.Count; i++)
            {
                Vector2 randomOffset = Random.insideUnitCircle * waitingRadius;
                Vector3 targetWorldPosition = marbleWaitingCenter.position +
                                              new Vector3(randomOffset.x, randomOffset.y, 0f);
                targetWorldPosition.z = ufoMarbleWorldZ;
                capturedMarbles[i].WaitingLocalPosition =
                    ufoMarbleRoot.InverseTransformPoint(targetWorldPosition);
            }
        }

        private void CompleteMarbleCapture(CapturedMarbleRecord record, int expectedPresentationVersion)
        {
            if (presentationLifecycleVersion != expectedPresentationVersion ||
                record?.Marble == null || ufoMarbleRoot == null)
            {
                return;
            }

            Transform marbleTransform = record.Marble.transform;
            marbleTransform.localPosition = record.WaitingLocalPosition;
            record.State = UfoMarbleState.Chamber;
            FadeOutCollectGlow(record);
        }

        private void CreateCollectGlow(CapturedMarbleRecord record)
        {
            if (record?.Marble == null || ufoGlowPrefab == null)
            {
                return;
            }

            CleanupCollectGlow(record);
            GameObject glow = Instantiate(ufoGlowPrefab, record.Marble.transform, false);
            glow.name = $"UFO Glow ({record.Marble.name})";
            glow.transform.SetAsFirstSibling();
            record.CollectGlow = glow;
            record.CollectGlowRenderers = glow.GetComponentsInChildren<SpriteRenderer>(true);
            int glowSortingOrder = collectMarbleOrderInLayer + collectGlowSortingOffset;
            for (int i = 0; i < record.CollectGlowRenderers.Length; i++)
            {
                SpriteRenderer renderer = record.CollectGlowRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.sortingLayerID = bodyRenderer.sortingLayerID;
                renderer.sortingOrder = glowSortingOrder;
            }
        }

        private void FadeOutCollectGlow(CapturedMarbleRecord record)
        {
            if (record?.CollectGlow == null)
            {
                return;
            }

            record.GlowFadeSequence?.Kill(false);
            if (collectGlowFadeDuration <= 0f || record.CollectGlowRenderers == null ||
                record.CollectGlowRenderers.Length == 0)
            {
                CleanupCollectGlow(record);
                return;
            }

            Sequence sequence = DOTween.Sequence()
                .SetLink(record.CollectGlow, LinkBehaviour.KillOnDestroy);
            for (int i = 0; i < record.CollectGlowRenderers.Length; i++)
            {
                SpriteRenderer renderer = record.CollectGlowRenderers[i];
                if (renderer != null)
                {
                    sequence.Join(renderer
                        .DOFade(0f, collectGlowFadeDuration)
                        .SetEase(collectGlowFadeEase));
                }
            }

            sequence.OnComplete(() => CleanupCollectGlow(record, false));
            record.GlowFadeSequence = sequence;
        }

        private static void CleanupCollectGlow(CapturedMarbleRecord record, bool killTween = true)
        {
            if (record == null)
            {
                return;
            }

            if (killTween)
            {
                record.GlowFadeSequence?.Kill(false);
            }

            record.GlowFadeSequence = null;
            if (record.CollectGlow != null)
            {
                Destroy(record.CollectGlow);
            }

            record.CollectGlow = null;
            record.CollectGlowRenderers = null;
        }

        private void BeginChamberHold(
            int expectedBoosterLifecycleVersion,
            int expectedPresentationVersion)
        {
            if (!IsCurrentUfoLifecycle(expectedBoosterLifecycleVersion) ||
                presentationLifecycleVersion != expectedPresentationVersion)
            {
                return;
            }

            presentationSequence = null;
            RuntimeState = UfoBoosterRuntimeState.Holding;
            presentationLifecycleVersion++;
            int holdPresentationVersion = presentationLifecycleVersion;
            presentationSequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy)
                .AppendInterval(chamberHoldDuration)
                .OnComplete(() =>
                {
                    if (!IsCurrentUfoLifecycle(expectedBoosterLifecycleVersion) ||
                        presentationLifecycleVersion != holdPresentationVersion)
                    {
                        return;
                    }

                    presentationSequence = null;
                    TryStartNextFirePreparation(expectedBoosterLifecycleVersion);
                });
        }

        private void TryStartNextFirePreparation(int expectedBoosterLifecycleVersion)
        {
            if (!IsCurrentUfoLifecycle(expectedBoosterLifecycleVersion) ||
                shooterPreparationInProgress || launchCadencePending)
            {
                return;
            }

            if (!HasChamberMarbles())
            {
                ClearTargetWaitState();
                TryCompleteFirePipeline(expectedBoosterLifecycleVersion);
                return;
            }

            if (!TryFindAndReserveNextFireCandidate(
                    expectedBoosterLifecycleVersion,
                    out CapturedMarbleRecord record,
                    out TargetBox aimTarget))
            {
                EnterTargetWaitState();
                return;
            }

            ClearTargetWaitState();
            shooterPreparationInProgress = true;
            RuntimeState = UfoBoosterRuntimeState.PreparingFire;
            maskObject.SetActive(true);
            ApplyFireSorting();
            record.AimTarget = aimTarget;
            record.State = UfoMarbleState.MovingToMask;
            ApplyMarbleSorting(record, fireMarbleOrderInLayer);
            ApplyMaskTransferInteraction(record);
            StartMaskTransferAndAim(record, aimTarget, expectedBoosterLifecycleVersion);
        }

        private bool TryFindAndReserveNextFireCandidate(
            int expectedBoosterLifecycleVersion,
            out CapturedMarbleRecord record,
            out TargetBox aimTarget)
        {
            record = null;
            aimTarget = null;
            if (targetLaneController == null)
            {
                return false;
            }

            for (int i = 0; i < capturedMarbles.Count; i++)
            {
                CapturedMarbleRecord candidate = capturedMarbles[i];
                if (candidate?.Marble == null || candidate.State != UfoMarbleState.Chamber ||
                    !targetLaneController.TryGetUfoAimTarget(
                        candidate.Marble.ColorId,
                        out TargetBox candidateTarget))
                {
                    continue;
                }

                CapturedMarbleRecord reservedRecord = candidate;
                if (!targetLaneController.TryReserveUfoTransfer(
                        candidate.Marble,
                        candidateTarget,
                        shotArrivalPunch,
                        fireSpeedMultiplier,
                        () => HandleUfoArrivalCommitted(
                            reservedRecord,
                            expectedBoosterLifecycleVersion),
                        out TargetLaneController.UfoTransferReservation reservation))
                {
                    continue;
                }

                candidate.Reservation = reservation;
                candidate.ReservationSequence = nextReservationSequence++;
                record = candidate;
                aimTarget = candidateTarget;
                return true;
            }

            return false;
        }

        private void EnterTargetWaitState()
        {
            float currentTime = Time.unscaledTime;
            if (targetWaitStartedAt < 0f)
            {
                targetWaitStartedAt = currentTime;
                nextTargetRetryAt = currentTime + noTargetRetryInterval;
            }

            waitingForTargetAvailability = true;
            RuntimeState = UfoBoosterRuntimeState.WaitingForLane;
        }

        private void ClearTargetWaitState()
        {
            waitingForTargetAvailability = false;
            targetWaitStartedAt = -1f;
            nextTargetRetryAt = 0f;
            targetWaitWarningLogged = false;
        }

        private void RecoverBlockedUfoTransaction(int expectedBoosterLifecycleVersion)
        {
            if (!IsCurrentUfoLifecycle(expectedBoosterLifecycleVersion))
            {
                return;
            }

            RuntimeState = UfoBoosterRuntimeState.BlockedNoTarget;
            waitingForTargetAvailability = false;
            KillPresentationSequence();
            shooterPreparationInProgress = false;
            launchCadencePending = false;

            int restoredCount = 0;
            int deliveredCount = 0;
            for (int i = capturedMarbles.Count - 1; i >= 0; i--)
            {
                CapturedMarbleRecord record = capturedMarbles[i];
                record?.ShotSequence?.Kill(false);
                CleanupCollectGlow(record);
                if (record == null || record.Marble == null)
                {
                    capturedMarbles.RemoveAt(i);
                    continue;
                }

                if (record.Reservation != null && record.Reservation.IsPending)
                {
                    CancelReservationForRecord(record);
                }

                if (!record.Marble.IsOwnedByUfo && record.Marble.IsTransferringToTarget)
                {
                    RestoreMarbleSorting(record);
                    capturedMarbles.RemoveAt(i);
                    deliveredCount++;
                    continue;
                }

                RestoreMarbleMaskInteraction(record);
                RestoreMarbleSorting(record);
                record.Marble.CancelUfoTransfer();
                if (conveyorController != null &&
                    conveyorController.TryRestoreMarbleFromUfo(record.Marble))
                {
                    capturedMarbles.RemoveAt(i);
                    restoredCount++;
                    continue;
                }

                record.Marble.TryBeginUfoTransfer();
                record.Marble.PrepareForUfoControl();
                if (ufoMarbleRoot != null)
                {
                    record.Marble.transform.SetParent(ufoMarbleRoot, true);
                    record.Marble.transform.localPosition = record.WaitingLocalPosition;
                }

                record.State = UfoMarbleState.Chamber;
                ApplyMarbleSorting(record, fireMarbleOrderInLayer);
            }

            inFlightShotCount = 0;
            if (capturedMarbles.Count > 0)
            {
                Debug.LogError(
                    $"{nameof(UfoBoosterController)} on '{name}' timed out waiting for a TargetBox. " +
                    $"Restored {restoredCount} marble(s), recognized {deliveredCount} committed marble(s), " +
                    $"but {capturedMarbles.Count} marble(s) could not be restored to the conveyor. Retrying safely.",
                    this);
                ClearTargetWaitState();
                EnterTargetWaitState();
                return;
            }

            Debug.LogError(
                $"{nameof(UfoBoosterController)} on '{name}' timed out waiting for a TargetBox. " +
                $"The transaction recovered without discarding marbles: {restoredCount} returned to the conveyor, " +
                $"{deliveredCount} already committed to TargetBoxes.",
                this);
            ClearTargetWaitState();
            BeginUfoCompletion(expectedBoosterLifecycleVersion);
        }

        private void StartMaskTransferAndAim(
            CapturedMarbleRecord record,
            TargetBox aimTarget,
            int expectedBoosterLifecycleVersion)
        {
            presentationLifecycleVersion++;
            int expectedPresentationVersion = presentationLifecycleVersion;
            Transform marbleTransform = record.Marble.transform;
            float effectiveMaskTransferDuration = GetEffectiveFireDuration(maskTransferDuration);
            float effectiveMaskHideDelay = GetEffectiveFireDuration(maskHideDelay);
            float effectiveShooterRotateDuration = GetEffectiveFireDuration(shooterRotateDuration);
            float teleportTime = effectiveMaskTransferDuration + effectiveMaskHideDelay;
            bool needsRotation = currentAimTarget != aimTarget;
            Vector3 maskTargetPosition = maskTransferTarget.position;
            maskTargetPosition.z = ufoMarbleWorldZ;

            Sequence sequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            sequence.Insert(
                0f,
                marbleTransform
                    .DOMove(maskTargetPosition, effectiveMaskTransferDuration)
                    .SetEase(maskTransferEase));
            sequence.Insert(
                0f,
                marbleTransform
                    .DOScale(
                        marbleTransform.localScale * maskTransferScaleMultiplier,
                        effectiveMaskTransferDuration)
                    .SetEase(maskTransferEase));

            if (needsRotation)
            {
                Vector3 targetLocalEulerAngles = CalculateShooterTargetLocalEuler(aimTarget);
                sequence.Insert(
                    0f,
                    shooterAnchor
                        .DOLocalRotate(
                            targetLocalEulerAngles,
                            effectiveShooterRotateDuration,
                            RotateMode.Fast)
                        .SetEase(shooterRotateEase));
            }

            sequence.InsertCallback(
                teleportTime,
                () => TeleportMarbleToShooter(
                    record,
                    expectedBoosterLifecycleVersion,
                    expectedPresentationVersion));
            sequence.OnComplete(
                () => CompleteFirePreparation(
                    record,
                    aimTarget,
                    expectedBoosterLifecycleVersion,
                    expectedPresentationVersion));
            presentationSequence = sequence;
        }

        private Vector3 CalculateShooterTargetLocalEuler(TargetBox aimTarget)
        {
            Vector3 authoredEuler = originalShooterLocalRotation.eulerAngles;
            Vector2 worldDirection = (Vector2)(aimTarget.AimPosition - shooterAnchor.position);
            if (worldDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                return authoredEuler;
            }

            Vector3 normalizedWorldDirection = new Vector3(
                worldDirection.x,
                worldDirection.y,
                0f).normalized;
            Vector3 localDirection3 = shooterAnchor.parent != null
                ? shooterAnchor.parent.InverseTransformDirection(normalizedWorldDirection)
                : normalizedWorldDirection;
            Vector2 localDirection = new Vector2(localDirection3.x, localDirection3.y).normalized;
            float targetLocalZ = Vector2.SignedAngle(Vector2.down, localDirection) + shooterAngleOffset;

            authoredEuler.z = targetLocalZ;
            return authoredEuler;
        }

        private void TeleportMarbleToShooter(
            CapturedMarbleRecord record,
            int expectedBoosterLifecycleVersion,
            int expectedPresentationVersion)
        {
            if (!IsCurrentFirePreparation(
                    record,
                    expectedBoosterLifecycleVersion,
                    expectedPresentationVersion))
            {
                return;
            }

            Transform marbleTransform = record.Marble.transform;
            marbleTransform.SetParent(shooterAnchor, true);
            Vector3 shooterPosition = marbleStartPosition.position;
            shooterPosition.z = ufoMarbleWorldZ;
            marbleTransform.position = shooterPosition;
            record.State = UfoMarbleState.AtShooter;
        }

        private void CompleteFirePreparation(
            CapturedMarbleRecord record,
            TargetBox aimTarget,
            int expectedBoosterLifecycleVersion,
            int expectedPresentationVersion)
        {
            if (!IsCurrentFirePreparation(
                    record,
                    expectedBoosterLifecycleVersion,
                    expectedPresentationVersion) ||
                record.State != UfoMarbleState.AtShooter)
            {
                return;
            }

            currentAimTarget = aimTarget;
            record.State = UfoMarbleState.ReadyToFire;
            presentationSequence = null;
            RuntimeState = UfoBoosterRuntimeState.ReadyForFire;
            BeginReservedShotWhenCadenceAllows(record, expectedBoosterLifecycleVersion);
        }

        private void BeginReservedShotWhenCadenceAllows(
            CapturedMarbleRecord record,
            int expectedBoosterLifecycleVersion)
        {
            float remainingDelay = Mathf.Max(0f, nextAllowedLaunchTime - Time.time);
            if (remainingDelay <= 0f)
            {
                StartReservedShot(record, expectedBoosterLifecycleVersion);
                return;
            }

            launchCadencePending = true;
            presentationLifecycleVersion++;
            int expectedPresentationVersion = presentationLifecycleVersion;
            presentationSequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy)
                .AppendInterval(remainingDelay)
                .OnComplete(() =>
                {
                    if (!IsCurrentUfoLifecycle(expectedBoosterLifecycleVersion) ||
                        presentationLifecycleVersion != expectedPresentationVersion)
                    {
                        return;
                    }

                    if (record?.Reservation == null || !record.Reservation.IsPending)
                    {
                        presentationSequence = null;
                        launchCadencePending = false;
                        shooterPreparationInProgress = false;
                        CancelReservationForRecord(record);
                        ReturnReadyMarbleToChamber(record);
                        TryStartNextFirePreparation(expectedBoosterLifecycleVersion);
                        return;
                    }

                    presentationSequence = null;
                    launchCadencePending = false;
                    StartReservedShot(record, expectedBoosterLifecycleVersion);
                });
        }

        private void StartReservedShot(
            CapturedMarbleRecord record,
            int expectedBoosterLifecycleVersion)
        {
            TargetLaneController.UfoTransferReservation reservation = record?.Reservation;
            if (record?.Marble == null || reservation?.Slot == null || !reservation.IsPending ||
                !IsCurrentUfoLifecycle(expectedBoosterLifecycleVersion))
            {
                CancelReservationForRecord(record);
                shooterPreparationInProgress = false;
                ReturnReadyMarbleToChamber(record);
                launchCadencePending = false;
                TryStartNextFirePreparation(expectedBoosterLifecycleVersion);
                return;
            }

            SetMarbleMaskInteraction(record, SpriteMaskInteraction.None);
            RuntimeState = UfoBoosterRuntimeState.Firing;
            record.State = UfoMarbleState.Firing;
            shooterPreparationInProgress = false;
            launchCadencePending = false;
            nextAllowedLaunchTime = Time.time + GetEffectiveFireDuration(shotLaunchInterval);
            inFlightShotCount++;
            Transform marbleTransform = record.Marble.transform;
            marbleTransform.SetParent(ufoMarbleRoot, true);
            Vector3 startPosition = marbleTransform.position;
            Vector3 initialEndPosition = reservation.Slot.position;
            startPosition.z = ufoMarbleWorldZ;
            initialEndPosition.z = ufoMarbleWorldZ;
            marbleTransform.position = startPosition;
            Vector3 direction = initialEndPosition - startPosition;
            Vector3 perpendicular = direction.sqrMagnitude > Mathf.Epsilon
                ? new Vector3(-direction.y, direction.x, 0f).normalized
                : Vector3.up;
            float effectiveShotDuration = GetEffectiveFireDuration(shotDuration);
            float effectiveScaleDuration = GetEffectiveFireDuration(shotMarbleScaleDuration);
            marbleTransform.localScale = Vector3.one * shotMarbleStartScale;
            record.Marble.SetOutlineActive(true);
            Vector3 targetMarbleScale = reservation.TargetMarbleScale;
            float progress = 0f;
            AudioManager.Instance?.PlaySfx(AudioKey.UfoShoot);
            PlayUfoShotScalePulse();

            Sequence shotSequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            shotSequence.Join(DOTween.To(
                    () => progress,
                    value =>
                    {
                        progress = value;
                        if (marbleTransform != null && reservation?.Slot != null)
                        {
                            Vector3 liveEndPosition = reservation.Slot.position;
                            liveEndPosition.z = ufoMarbleWorldZ;
                            Vector3 controlPosition =
                                Vector3.Lerp(startPosition, liveEndPosition, 0.5f) +
                                perpendicular * shotCurveStrength;
                            marbleTransform.position = EvaluateQuadraticBezier(
                                startPosition,
                                controlPosition,
                                liveEndPosition,
                                value);
                        }
                    },
                    1f,
                    effectiveShotDuration)
                .SetEase(shotEase));
            shotSequence.Join(marbleTransform
                .DOScale(targetMarbleScale, effectiveScaleDuration)
                .SetEase(shotMarbleScaleEase));
            shotSequence.OnComplete(() =>
            {
                if (!IsCurrentUfoLifecycle(expectedBoosterLifecycleVersion))
                {
                    return;
                }

                if (record.Reservation != reservation || !reservation.IsPending)
                {
                    record.ShotSequence = null;
                    inFlightShotCount = Mathf.Max(0, inFlightShotCount - 1);
                    CancelReservationForRecord(record);
                    ReturnReadyMarbleToChamber(record);
                    EnterTargetWaitState();
                    return;
                }

                record.ShotSequence = null;
                RestoreMarbleSorting(record);
                if (!targetLaneController.CommitUfoTransferArrival(reservation))
                {
                    inFlightShotCount = Mathf.Max(0, inFlightShotCount - 1);
                    CancelReservationForRecord(record);
                    ReturnReadyMarbleToChamber(record);
                    Debug.LogError(
                        $"{nameof(UfoBoosterController)} on '{name}' could not commit the reserved UFO arrival. The marble was returned to the chamber and the transaction stopped without leaking the reservation.",
                        this);
                    EnterTargetWaitState();
                }
            });
            record.ShotSequence = shotSequence;
            TryStartNextFirePreparation(expectedBoosterLifecycleVersion);
        }

        private void HandleUfoArrivalCommitted(
            CapturedMarbleRecord record,
            int expectedBoosterLifecycleVersion)
        {
            if (!IsCurrentUfoLifecycle(expectedBoosterLifecycleVersion) ||
                record == null || !capturedMarbles.Contains(record))
            {
                return;
            }

            record.Reservation = null;
            record.ShotSequence = null;
            CleanupCollectGlow(record);
            inFlightShotCount = Mathf.Max(0, inFlightShotCount - 1);
            record.State = UfoMarbleState.Delivered;
            capturedMarbles.Remove(record);
            if (!shooterPreparationInProgress && !launchCadencePending)
            {
                TryStartNextFirePreparation(expectedBoosterLifecycleVersion);
            }
        }

        private void ReturnReadyMarbleToChamber(CapturedMarbleRecord record)
        {
            if (record?.Marble == null || ufoMarbleRoot == null)
            {
                return;
            }

            Transform marbleTransform = record.Marble.transform;
            marbleTransform.SetParent(ufoMarbleRoot, true);
            marbleTransform.localPosition = record.WaitingLocalPosition;
            marbleTransform.localRotation = Quaternion.identity;
            marbleTransform.localScale = record.CollectStartLocalScale * collectScaleMultiplier;
            record.Marble.SetOutlineActive(true);
            RestoreMarbleMaskInteraction(record);
            ApplyMarbleSorting(record, collectMarbleOrderInLayer);
            record.AimTarget = null;
            record.State = UfoMarbleState.Chamber;
        }

        private bool HasChamberMarbles()
        {
            for (int i = 0; i < capturedMarbles.Count; i++)
            {
                if (capturedMarbles[i]?.Marble != null &&
                    capturedMarbles[i].State == UfoMarbleState.Chamber)
                {
                    return true;
                }
            }

            return false;
        }

        private void TryCompleteFirePipeline(int expectedBoosterLifecycleVersion)
        {
            if (shooterPreparationInProgress || launchCadencePending || inFlightShotCount > 0 ||
                HasChamberMarbles())
            {
                RuntimeState = UfoBoosterRuntimeState.Firing;
                return;
            }

            BeginUfoCompletion(expectedBoosterLifecycleVersion);
        }

        private bool IsCurrentFirePreparation(
            CapturedMarbleRecord record,
            int expectedBoosterLifecycleVersion,
            int expectedPresentationVersion)
        {
            return record?.Marble != null &&
                   IsCurrentUfoLifecycle(expectedBoosterLifecycleVersion) &&
                   presentationLifecycleVersion == expectedPresentationVersion &&
                   shooterPreparationInProgress &&
                   record.Reservation != null && record.Reservation.IsPending &&
                   RuntimeState == UfoBoosterRuntimeState.PreparingFire;
        }

        private static void ApplyMaskTransferInteraction(CapturedMarbleRecord record)
        {
            SetMarbleMaskInteraction(record, SpriteMaskInteraction.VisibleOutsideMask);
        }

        private static void SetMarbleMaskInteraction(
            CapturedMarbleRecord record,
            SpriteMaskInteraction maskInteraction)
        {
            if (record?.Renderers == null)
            {
                return;
            }

            for (int i = 0; i < record.Renderers.Length; i++)
            {
                SpriteRenderer renderer = record.Renderers[i]?.Renderer;
                if (renderer != null)
                {
                    renderer.maskInteraction = maskInteraction;
                }
            }
        }

        private static void RestoreMarbleMaskInteraction(CapturedMarbleRecord record)
        {
            if (record?.Renderers == null)
            {
                return;
            }

            for (int i = 0; i < record.Renderers.Length; i++)
            {
                CapturedRendererState state = record.Renderers[i];
                if (state?.Renderer != null)
                {
                    state.Renderer.maskInteraction = state.MaskInteraction;
                }
            }
        }

        private bool IsCurrentUfoLifecycle(int expectedBoosterLifecycleVersion)
        {
            return ownsActiveUfo && boosterController != null &&
                   boosterController.ActiveBooster == BoosterType.Ufo &&
                   boosterController.State == BoosterState.Running &&
                   boosterController.ActiveLifecycleVersion == expectedBoosterLifecycleVersion;
        }

        private void CreateUfoMarbleRoot()
        {
            DestroyUfoMarbleRoot();
            GameObject rootObject = new GameObject("UfoMarbleRoot");
            ufoMarbleRoot = rootObject.transform;
            ufoMarbleRoot.SetParent(view, false);
        }

        private void ApplyMarbleSorting(CapturedMarbleRecord record, int sortingOrder)
        {
            if (record?.Renderers == null)
            {
                return;
            }

            for (int i = 0; i < record.Renderers.Length; i++)
            {
                CapturedRendererState state = record.Renderers[i];
                if (state?.Renderer != null)
                {
                    int targetSortingOrder = sortingOrder +
                                             state.SortingOrder -
                                             record.OriginalBaseSortingOrder;
                    ApplyRendererSorting(
                        state.Renderer,
                        bodyRenderer.sortingLayerID,
                        targetSortingOrder);
                }
            }
        }

        private static void RestoreMarbleSorting(CapturedMarbleRecord record)
        {
            if (record?.Renderers == null)
            {
                return;
            }

            for (int i = 0; i < record.Renderers.Length; i++)
            {
                CapturedRendererState state = record.Renderers[i];
                if (state?.Renderer == null)
                {
                    continue;
                }

                state.Renderer.maskInteraction = state.MaskInteraction;
                ApplyRendererSorting(
                    state.Renderer,
                    state.SortingLayerId,
                    state.SortingOrder);
            }
        }

        private static void ApplyRendererSorting(
            SpriteRenderer renderer,
            int sortingLayerId,
            int sortingOrder)
        {
            if (renderer == null ||
                (renderer.sortingLayerID == sortingLayerId &&
                 renderer.sortingOrder == sortingOrder))
            {
                return;
            }

            renderer.sortingLayerID = sortingLayerId;
            renderer.sortingOrder = sortingOrder;
            RefreshSpriteRenderer(renderer);
        }

        private static void RefreshSpriteRenderer(SpriteRenderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            Sprite sprite = renderer.sprite;
            renderer.sprite = null;
            renderer.sprite = sprite;
        }

        private void BeginUfoCompletion(int expectedBoosterLifecycleVersion)
        {
            if (!IsCurrentUfoLifecycle(expectedBoosterLifecycleVersion))
            {
                return;
            }

            RuntimeState = UfoBoosterRuntimeState.Completing;
            ClearTargetWaitState();
            shooterPreparationInProgress = false;
            launchCadencePending = false;
            presentationLifecycleVersion++;
            int expectedPresentationVersion = presentationLifecycleVersion;
            Sequence sequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy)
                .AppendInterval(GetEffectiveFireDuration(completionDelay))
                .AppendCallback(() =>
                {
                    if (maskObject != null)
                    {
                        maskObject.SetActive(false);
                    }

                    currentAimTarget = null;
                    RestoreSorting();
                    DestroyUfoMarbleRoot();
                })
                .Append(shooterAnchor
                    .DOLocalRotate(
                        originalShooterLocalRotation.eulerAngles,
                        GetEffectiveFireDuration(shooterRotateDuration),
                        RotateMode.Fast)
                    .SetEase(shooterRotateEase))
                .OnComplete(() =>
                {
                    if (!IsCurrentUfoLifecycle(expectedBoosterLifecycleVersion) ||
                        presentationLifecycleVersion != expectedPresentationVersion)
                    {
                        return;
                    }

                    presentationSequence = null;
                    BeginExit(true, expectedBoosterLifecycleVersion);
                });
            presentationSequence = sequence;
        }

        private void BeginExit(
            bool completeBooster = false,
            int expectedBoosterLifecycleVersion = 0)
        {
            presentationLifecycleVersion++;
            int expectedPresentationVersion = presentationLifecycleVersion;
            KillPresentationSequence();
            KillUfoShotScaleSequence(true);

            if (!initialized || ufoRoot == null || view == null || !view.gameObject.activeSelf)
            {
                if (completeBooster)
                {
                    ownsActiveUfo = false;
                }

                ResetPresentationImmediately();
                if (completeBooster)
                {
                    boosterController?.NotifyCompleted(
                        BoosterType.Ufo,
                        expectedBoosterLifecycleVersion);
                }

                return;
            }

            RuntimeState = UfoBoosterRuntimeState.Exiting;
            maskObject.SetActive(false);
            if (completeBooster)
            {
                AudioManager.Instance?.PlaySfx(AudioKey.UfoOut);
            }

            Vector3 exitPosition = ufoRoot.position;
            exitPosition.y = exitTargetY;
            Vector3 exitScale = originalViewLocalScale * exitScaleMultiplier;

            Sequence sequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            sequence.Join(ufoRoot.DOMove(exitPosition, exitDuration).SetEase(exitEase));
            sequence.Join(view.DOScale(exitScale, exitDuration).SetEase(exitEase));
            sequence.OnComplete(() =>
            {
                if (presentationLifecycleVersion != expectedPresentationVersion)
                {
                    return;
                }

                presentationSequence = null;
                if (completeBooster &&
                    !IsCurrentUfoLifecycle(expectedBoosterLifecycleVersion))
                {
                    return;
                }

                if (completeBooster)
                {
                    ownsActiveUfo = false;
                }

                ResetPresentationImmediately();
                if (completeBooster)
                {
                    boosterController?.NotifyCompleted(
                        BoosterType.Ufo,
                        expectedBoosterLifecycleVersion);
                }
            });

            presentationSequence = sequence;
        }

        private void InitializePresentation()
        {
            if (initialized || !ValidatePresentationReferences(true))
            {
                return;
            }

            originalRootPosition = ufoRoot.position;
            originalViewLocalScale = view.localScale;
            originalShooterLocalScale = shooterAnchor.localScale;
            originalShooterLocalRotation = shooterAnchor.localRotation;
            originalBodySortingOrder = bodyRenderer.sortingOrder;
            initialized = true;
            ResetPresentationImmediately();
        }

        private void RestoreAuthoredPresentationState()
        {
            if (!initialized)
            {
                return;
            }

            ufoRoot.position = originalRootPosition;
            view.localScale = originalViewLocalScale;
            shooterAnchor.localScale = originalShooterLocalScale;
            shooterAnchor.localRotation = originalShooterLocalRotation;
            maskObject.SetActive(false);
            currentAimTarget = null;
            RestoreSorting();
        }

        private void ResetPresentationImmediately()
        {
            presentationLifecycleVersion++;
            KillPresentationSequence();
            KillUfoShotScaleSequence(true);
            ClearTargetWaitState();
            shooterPreparationInProgress = false;
            launchCadencePending = false;
            inFlightShotCount = 0;
            nextReservationSequence = 0;
            nextAllowedLaunchTime = 0f;

            bool discardCapturedMarbles = ShouldDiscardCapturedMarbles();
            if (!CleanupCapturedMarbles(discardCapturedMarbles))
            {
                abortRecoveryPending = true;
                nextAbortRecoveryRetryAt = Time.unscaledTime + noTargetRetryInterval;
                RuntimeState = UfoBoosterRuntimeState.BlockedNoTarget;
                targetLaneController?.EndUfoTargetPreviewTransaction();
                if (!abortRecoveryWarningLogged)
                {
                    abortRecoveryWarningLogged = true;
                    Debug.LogError(
                        $"{nameof(UfoBoosterController)} on '{name}' could not immediately restore all captured " +
                        "marbles after an interrupted UFO transaction. The remaining marbles were preserved and " +
                        "will be retried instead of being discarded.",
                        this);
                }

                return;
            }

            abortRecoveryPending = false;
            abortRecoveryWarningLogged = false;
            nextAbortRecoveryRetryAt = 0f;
            RuntimeState = UfoBoosterRuntimeState.Idle;

            if (!initialized)
            {
                targetLaneController?.EndUfoTargetPreviewTransaction();
                RestoreGameplayMarbleMovement();
                return;
            }

            targetLaneController?.EndUfoTargetPreviewTransaction();
            RestoreAuthoredPresentationState();
            view.gameObject.SetActive(false);
            RestoreGameplayMarbleMovement();
        }

        private void TryCompletePendingAbortRecovery()
        {
            if (!abortRecoveryPending)
            {
                return;
            }

            if (!CleanupCapturedMarbles(ShouldDiscardCapturedMarbles()))
            {
                return;
            }

            abortRecoveryPending = false;
            abortRecoveryWarningLogged = false;
            nextAbortRecoveryRetryAt = 0f;
            RuntimeState = UfoBoosterRuntimeState.Idle;
            targetLaneController?.EndUfoTargetPreviewTransaction();
            if (initialized)
            {
                RestoreAuthoredPresentationState();
                view.gameObject.SetActive(false);
            }

            RestoreGameplayMarbleMovement();
        }

        private bool ShouldDiscardCapturedMarbles()
        {
            return !Application.isPlaying ||
                   levelSessionController != null &&
                   levelSessionController.GameplayState == GameplaySessionState.Exiting;
        }

        private void KillPresentationSequence()
        {
            presentationSequence?.Kill(false);
            presentationSequence = null;
        }

        private void PlayUfoShotScalePulse()
        {
            if (!ufoShotBaseScaleCached || view == null ||
                RuntimeState == UfoBoosterRuntimeState.Exiting ||
                !view.gameObject.activeInHierarchy)
            {
                return;
            }

            KillUfoShotScaleSequence(true);
            Vector3 pulseScale = ufoShotBaseLocalScale * ufoShotScaleMultiplier;
            Sequence sequence = DOTween.Sequence()
                .SetLink(view.gameObject, LinkBehaviour.KillOnDestroy)
                .Append(view
                    .DOScale(pulseScale, ufoShotScaleUpDuration)
                    .SetEase(ufoShotScaleUpEase))
                .Append(view
                    .DOScale(ufoShotBaseLocalScale, ufoShotScaleDownDuration)
                    .SetEase(ufoShotScaleDownEase));
            sequence.OnComplete(() =>
            {
                if (ufoShotScaleSequence != sequence)
                {
                    return;
                }

                view.localScale = ufoShotBaseLocalScale;
                ufoShotScaleSequence = null;
            });
            ufoShotScaleSequence = sequence;
        }

        private void KillUfoShotScaleSequence(bool restoreBaseScale)
        {
            ufoShotScaleSequence?.Kill(false);
            ufoShotScaleSequence = null;
            if (restoreBaseScale && ufoShotBaseScaleCached && view != null)
            {
                view.localScale = ufoShotBaseLocalScale;
            }
        }

        private float GetEffectiveFireDuration(float baseDuration)
        {
            return Mathf.Max(0f, baseDuration) / Mathf.Max(0.01f, fireSpeedMultiplier);
        }

        private void FreezeGameplayMarbleMovement()
        {
            if (gameplayMarbleMovementFrozen)
            {
                return;
            }

            gameplayMarbleMovementFrozen = true;
            conveyorController?.SetUfoMovementFrozen(true);
            conveyorEntryZone?.SetUfoMovementFrozen(true);
            targetLaneController?.SetUfoGameplayMovementFrozen(true);

            frozenReleaseAnimators.Clear();
            SourceBoxReleaseAnimator[] releaseAnimators =
                FindObjectsByType<SourceBoxReleaseAnimator>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);
            for (int i = 0; i < releaseAnimators.Length; i++)
            {
                SourceBoxReleaseAnimator releaseAnimator = releaseAnimators[i];
                if (releaseAnimator == null)
                {
                    continue;
                }

                releaseAnimator.SetUfoMovementFrozen(true);
                frozenReleaseAnimators.Add(releaseAnimator);
            }

            frozenMarblePhysics.Clear();
            Marble[] gameplayMarbles = FindObjectsByType<Marble>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (int i = 0; i < gameplayMarbles.Length; i++)
            {
                Marble marble = gameplayMarbles[i];
                Rigidbody2D body = marble != null ? marble.Rigidbody : null;
                if (body == null || !marble.IsReleased || !marble.CanBeOwnedByUfo)
                {
                    continue;
                }

                FrozenMarblePhysicsRecord record = new FrozenMarblePhysicsRecord
                {
                    Marble = marble,
                    Rigidbody = body,
                    WasSimulated = body.simulated,
                    WasSleeping = body.IsSleeping(),
                    BodyType = body.bodyType,
                    Constraints = body.constraints,
                    GravityScale = body.gravityScale,
                    LinearVelocity = body.linearVelocity,
                    AngularVelocity = body.angularVelocity
                };

                frozenMarblePhysics.Add(record);
                body.simulated = false;
            }
        }

        private void MarkFrozenMarbleCaptured(Marble marble)
        {
            if (marble == null)
            {
                return;
            }

            for (int i = 0; i < frozenMarblePhysics.Count; i++)
            {
                FrozenMarblePhysicsRecord record = frozenMarblePhysics[i];
                if (record?.Marble == marble)
                {
                    record.CapturedByUfo = true;
                    return;
                }
            }
        }

        private void RestoreGameplayMarbleMovement()
        {
            if (!gameplayMarbleMovementFrozen)
            {
                return;
            }

            for (int i = 0; i < frozenMarblePhysics.Count; i++)
            {
                FrozenMarblePhysicsRecord record = frozenMarblePhysics[i];
                Marble marble = record?.Marble;
                Rigidbody2D body = record?.Rigidbody;
                if (record == null || record.CapturedByUfo || marble == null || body == null ||
                    !marble.gameObject.activeInHierarchy || marble.IsOwnedByUfo ||
                    marble.IsTransferringToTarget)
                {
                    continue;
                }

                body.simulated = false;
                body.bodyType = record.BodyType;
                body.constraints = record.Constraints;
                body.gravityScale = record.GravityScale;
                body.simulated = record.WasSimulated;
                if (!record.WasSimulated)
                {
                    continue;
                }

                body.linearVelocity = record.LinearVelocity;
                body.angularVelocity = record.AngularVelocity;
                if (record.WasSleeping)
                {
                    body.Sleep();
                }
                else
                {
                    body.WakeUp();
                }
            }

            frozenMarblePhysics.Clear();
            for (int i = 0; i < frozenReleaseAnimators.Count; i++)
            {
                frozenReleaseAnimators[i]?.SetUfoMovementFrozen(false);
            }

            frozenReleaseAnimators.Clear();
            conveyorEntryZone?.SetUfoMovementFrozen(false);
            conveyorController?.SetUfoMovementFrozen(false);
            targetLaneController?.SetUfoGameplayMovementFrozen(false);
            gameplayMarbleMovementFrozen = false;
        }

        private bool CleanupCapturedMarbles(bool discardCapturedMarbles)
        {
            List<CapturedMarbleRecord> reservedRecords = new List<CapturedMarbleRecord>();
            for (int i = 0; i < capturedMarbles.Count; i++)
            {
                CapturedMarbleRecord record = capturedMarbles[i];
                record?.ShotSequence?.Kill(false);
                CleanupCollectGlow(record);
                if (record?.Reservation != null && record.Reservation.IsPending)
                {
                    reservedRecords.Add(record);
                }
            }

            reservedRecords.Sort((left, right) =>
                right.ReservationSequence.CompareTo(left.ReservationSequence));
            for (int i = 0; i < reservedRecords.Count; i++)
            {
                CancelReservationForRecord(reservedRecords[i]);
            }

            for (int i = capturedMarbles.Count - 1; i >= 0; i--)
            {
                CapturedMarbleRecord record = capturedMarbles[i];
                RestoreMarbleSorting(record);
                if (record?.Marble == null)
                {
                    capturedMarbles.RemoveAt(i);
                    continue;
                }

                RestoreMarbleMaskInteraction(record);
                record.Marble.CancelUfoTransfer();
                if (!discardCapturedMarbles && conveyorController != null &&
                    conveyorController.TryRestoreMarbleFromUfo(record.Marble))
                {
                    capturedMarbles.RemoveAt(i);
                    continue;
                }

                if (!discardCapturedMarbles)
                {
                    record.Marble.TryBeginUfoTransfer();
                    record.Marble.PrepareForUfoControl();
                    if (ufoMarbleRoot != null)
                    {
                        record.Marble.transform.SetParent(ufoMarbleRoot, true);
                        record.Marble.transform.localPosition = record.WaitingLocalPosition;
                    }

                    record.State = UfoMarbleState.Chamber;
                    ApplyMarbleSorting(record, fireMarbleOrderInLayer);
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(record.Marble.gameObject);
                }
                else
                {
                    DestroyImmediate(record.Marble.gameObject);
                }

                capturedMarbles.RemoveAt(i);
            }

            conveyorSnapshot.Clear();
            inFlightShotCount = 0;
            shooterPreparationInProgress = false;
            launchCadencePending = false;
            ClearTargetWaitState();
            nextAllowedLaunchTime = 0f;
            if (capturedMarbles.Count > 0)
            {
                return false;
            }

            DestroyUfoMarbleRoot();
            return true;
        }

        private void CancelReservationForRecord(CapturedMarbleRecord record)
        {
            if (record?.Reservation == null)
            {
                return;
            }

            if (record.Reservation.IsPending)
            {
                targetLaneController?.CancelUfoTransferReservation(record.Reservation);
            }

            record.Reservation = null;
        }

        private void DestroyUfoMarbleRoot()
        {
            if (ufoMarbleRoot == null)
            {
                return;
            }

            if (capturedMarbles.Count > 0)
            {
                Debug.LogError(
                    $"{nameof(UfoBoosterController)} on '{name}' kept its marble root because " +
                    $"{capturedMarbles.Count} captured marble(s) are still owned by the UFO.",
                    this);
                return;
            }

            GameObject rootObject = ufoMarbleRoot.gameObject;
            ufoMarbleRoot = null;
            if (Application.isPlaying)
            {
                Destroy(rootObject);
            }
            else
            {
                DestroyImmediate(rootObject);
            }
        }

        private bool ValidatePresentationReferences(bool logErrors)
        {
            bool valid = ufoRoot != null && view != null && glassRenderer != null &&
                          bodyRenderer != null && maskObject != null && maskTransferTarget != null &&
                          bodyTopRenderer != null &&
                          shooterAnchor != null && shooterBottomRenderer != null &&
                          shooterTopRenderer != null && marbleStartPosition != null &&
                          marbleCollectPosition != null && marbleWaitingCenter != null &&
                          ufoGlowPrefab != null;
            if (!valid && logErrors)
            {
                Debug.LogError($"{nameof(UfoBoosterController)} on '{name}' is missing one or more UFO hierarchy references.", this);
            }

            return valid;
        }

        private void RegisterActivationGuard()
        {
            boosterController?.RegisterActivationGuard(BoosterType.Ufo, CanActivateUfo);
            boosterController?.RegisterCancellationGuard(BoosterType.Ufo, CanCancelUfo);
        }

        private void UnregisterActivationGuard()
        {
            boosterController?.UnregisterActivationGuard(BoosterType.Ufo, CanActivateUfo);
            boosterController?.UnregisterCancellationGuard(BoosterType.Ufo, CanCancelUfo);
        }

        private void Subscribe()
        {
            if (boosterController == null)
            {
                return;
            }

            boosterController.BoosterActivated -= HandleBoosterActivated;
            boosterController.BoosterActivated += HandleBoosterActivated;
            boosterController.BoosterStateChanged -= HandleBoosterStateChanged;
            boosterController.BoosterStateChanged += HandleBoosterStateChanged;

            if (conveyorController != null)
            {
                conveyorController.ContentsChanged -= HandleAvailabilityChanged;
                conveyorController.ContentsChanged += HandleAvailabilityChanged;
            }

            if (conveyorEntryZone != null)
            {
                conveyorEntryZone.TransferStateChanged -= HandleAvailabilityChanged;
                conveyorEntryZone.TransferStateChanged += HandleAvailabilityChanged;
            }

            if (targetLaneController != null)
            {
                targetLaneController.TargetTransferStateChanged -= HandleAvailabilityChanged;
                targetLaneController.TargetTransferStateChanged += HandleAvailabilityChanged;
            }
        }

        private void Unsubscribe()
        {
            if (boosterController == null)
            {
                return;
            }

            boosterController.BoosterActivated -= HandleBoosterActivated;
            boosterController.BoosterStateChanged -= HandleBoosterStateChanged;

            if (conveyorController != null)
            {
                conveyorController.ContentsChanged -= HandleAvailabilityChanged;
            }

            if (conveyorEntryZone != null)
            {
                conveyorEntryZone.TransferStateChanged -= HandleAvailabilityChanged;
            }

            if (targetLaneController != null)
            {
                targetLaneController.TargetTransferStateChanged -= HandleAvailabilityChanged;
            }
        }

        private void HandleAvailabilityChanged()
        {
            boosterController?.NotifyAvailabilityChanged();
        }

        private void CacheMissingRuntimeServices()
        {
            if (boosterController == null)
            {
                boosterController = FindFirstObjectByType<BoosterController>(FindObjectsInactive.Include);
            }

            if (conveyorController == null)
            {
                conveyorController = FindFirstObjectByType<ConveyorController>(FindObjectsInactive.Include);
            }

            if (conveyorEntryZone == null)
            {
                conveyorEntryZone = FindFirstObjectByType<ConveyorEntryZone>(FindObjectsInactive.Include);
            }

            if (targetLaneController == null)
            {
                targetLaneController = FindFirstObjectByType<TargetLaneController>(FindObjectsInactive.Include);
            }

            if (levelSessionController == null)
            {
                levelSessionController = FindFirstObjectByType<LevelSessionController>(FindObjectsInactive.Include);
            }
        }
    }
}
