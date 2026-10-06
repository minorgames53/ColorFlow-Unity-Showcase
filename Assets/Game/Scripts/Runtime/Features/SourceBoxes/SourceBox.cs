using System;
using System.Collections.Generic;
using DG.Tweening;
using Game.Shared.Audio;
using Gameplay.Conveyor;
using Gameplay.Levels;
using UnityEngine;
#if UNITY_EDITOR
using Gameplay.MarbleDebug;
#endif
using UnityEngine.Rendering;

namespace Gameplay.SourceBoxes
{
    [SelectionBase]
    public sealed class SourceBox : MonoBehaviour
    {
        [SerializeField] private GameObject holderVisual;
        [SerializeField] private SpriteRenderer holderSpriteRenderer;
        [SerializeField] private GameObject lockedVisual;
        [SerializeField] private SpriteRenderer lockedSpriteRenderer;
        [SerializeField] private GameObject marbleRoot;
        [SerializeField] private Collider2D inputCollider;
        [SerializeField] private Transform[] marbleSlots = new Transform[LevelCellData.MaxMarbleCount];
        [SerializeField] private SourceBoxReleaseAnimator releaseAnimator;
        [SerializeField] private SourceBoxUnlockAnimator unlockAnimator;
        [SerializeField] private GameObject outlineObject;

        [Header("Invalid Move Feedback")]
        [SerializeField] private Transform invalidMoveFeedbackTarget;
        [SerializeField] private Vector3 invalidMoveNormalScale = Vector3.one;
        [SerializeField] private Vector3 invalidMoveScale = new Vector3(1.08f, 1.08f, 1f);
        [SerializeField, Min(0f)] private float invalidMoveScaleUpDuration = 0.08f;
        [SerializeField, Min(0f)] private float invalidMoveScaleDownDuration = 0.1f;
        [SerializeField] private Ease invalidMoveScaleUpEase = Ease.OutBack;
        [SerializeField] private Ease invalidMoveScaleDownEase = Ease.OutQuad;

        private readonly List<Marble> marbles = new List<Marble>(LevelCellData.MaxMarbleCount);
        private MarbleColorCatalog.Entry colorEntry;
        private Transform releasedMarbleContainer;
        private MarbleCapacityController capacityController;
        private Action<int> releasedCallback;
        private Sprite mysteryLockedSprite;
        private MarbleColorId colorId = MarbleColorId.None;
        private SourceBoxState currentState = SourceBoxState.Locked;
        private int cellIndex = -1;
        private bool releaseCallbackInvoked;
        private bool unlockAnimationInProgress;
        private bool isMysterySourceBox;
        private bool interactionBlocked;
        private bool lockedVisualSortingGroupResolved;
        private Sequence invalidMoveFeedbackSequence;
        private SortingGroup lockedVisualSortingGroup;
        private int releaseOutputMultiplier = 1;
        private bool boosterTargetingInputOverride;
        private bool handOpenViewPreview;
        private bool handMysteryRevealInProgress;
        private bool handDirectTransferCompleted;
        private bool handDirectSourceAnimationStarted;
        private bool handDirectSourceAnimationCompleted;
        private bool handDirectPresentationHeld;
        private bool suppressesProgressionForCurrentRelease;
        private bool isRecoveredSourceBox;

        public MarbleColorId ColorId => colorId;
        public SourceBoxState CurrentState => currentState;
        public int CellIndex => cellIndex;
        public int MarbleCount => marbles.Count;
        public IReadOnlyList<Marble> RuntimeMarbles => marbles;
        public bool IsMysterySourceBox => isMysterySourceBox;
        public int RequiredReleaseCapacity => marbles.Count * releaseOutputMultiplier;
        public Transform LockedRoot => lockedVisual != null ? lockedVisual.transform : null;
        public Transform PresentationRoot => invalidMoveFeedbackTarget != null ? invalidMoveFeedbackTarget : transform;
        public bool IsPresentedOpen => currentState == SourceBoxState.Available || handOpenViewPreview;
        public bool SuppressesProgressionForCurrentRelease => suppressesProgressionForCurrentRelease;
        public bool IsRecoveredSourceBox => isRecoveredSourceBox;
        public event Action<SourceBox> ReleaseStarted;
        public event Action<SourceBox> StateChanged;
        public event Action<SourceBox> PresentationChanged;
        public bool CanReleaseByUser =>
            currentState == SourceBoxState.Available &&
            !unlockAnimationInProgress &&
            !interactionBlocked &&
            marbles.Count > 0 &&
            capacityController != null &&
            capacityController.CanReserve(RequiredReleaseCapacity);
        public bool CanAttemptReleaseByUser =>
            currentState == SourceBoxState.Available &&
            !unlockAnimationInProgress &&
            !interactionBlocked &&
            marbles.Count > 0;
        public bool CanReleaseByHandBooster => CanBeginHandDirectTransfer;
        public bool CanBeginHandDirectTransfer =>
            currentState == SourceBoxState.Locked &&
            !unlockAnimationInProgress &&
            !handMysteryRevealInProgress &&
            !interactionBlocked &&
            marbles.Count > 0 &&
            releasedMarbleContainer != null &&
            releaseAnimator != null;
        public bool CanBeginForcedHandDirectTransfer =>
            currentState != SourceBoxState.Released &&
            !handMysteryRevealInProgress &&
            !interactionBlocked &&
            marbles.Count > 0 &&
            releasedMarbleContainer != null &&
            releaseAnimator != null;

        public bool TryGetInputBounds(out Bounds bounds)
        {
            bounds = default;
            if (inputCollider == null)
            {
                return false;
            }

            bounds = inputCollider.bounds;
            return true;
        }

        private void Reset()
        {
            Transform root = transform.Find("Root");
            Transform searchRoot = root != null ? root : transform;

            Transform holderVisualTransform = searchRoot.Find("HolderVisual");
            holderVisual = holderVisualTransform != null ? holderVisualTransform.gameObject : null;
            holderSpriteRenderer = holderVisualTransform != null ? holderVisualTransform.GetComponent<SpriteRenderer>() : null;

            Transform lockedVisualTransform = searchRoot.Find("LockedVisual");
            lockedVisual = lockedVisualTransform != null ? lockedVisualTransform.gameObject : null;
            lockedSpriteRenderer = lockedVisualTransform != null ? lockedVisualTransform.GetComponent<SpriteRenderer>() : null;

            Transform marbleRootTransform = searchRoot.Find("MarbleRoot");
            marbleRoot = marbleRootTransform != null ? marbleRootTransform.gameObject : null;
            inputCollider = searchRoot.Find("InputHitArea")?.GetComponent<Collider2D>();
            releaseAnimator = GetComponent<SourceBoxReleaseAnimator>();
            unlockAnimator = GetComponent<SourceBoxUnlockAnimator>();
            outlineObject = searchRoot.Find("Outline")?.gameObject;
            invalidMoveFeedbackTarget = root != null ? root : transform;

            if (marbleRoot == null)
            {
                return;
            }

            marbleSlots = new Transform[LevelCellData.MaxMarbleCount];
            int slotCount = Mathf.Min(LevelCellData.MaxMarbleCount, marbleRoot.transform.childCount);
            for (int i = 0; i < slotCount; i++)
            {
                marbleSlots[i] = marbleRoot.transform.GetChild(i);
            }
        }

        public void Initialize(
            MarbleColorId newColorId,
            int marbleCount,
            int newCellIndex,
            bool newIsMysterySourceBox,
            Action<int> newReleasedCallback,
            MarbleColorCatalog.Entry colorEntry,
            Sprite newMysteryLockedSprite,
            Marble marblePrefab,
            Transform newReleasedMarbleContainer,
            MarbleCapacityController newCapacityController)
        {
            releaseAnimator?.ResetVisuals();
            KillInvalidMoveFeedback();
            ClearRuntimeMarbles();

            releasedCallback = newReleasedCallback;
            cellIndex = newCellIndex;
            releaseCallbackInvoked = false;
            unlockAnimationInProgress = false;
            interactionBlocked = false;
            boosterTargetingInputOverride = false;
            handOpenViewPreview = false;
            handMysteryRevealInProgress = false;
            handDirectTransferCompleted = false;
            handDirectSourceAnimationStarted = false;
            handDirectSourceAnimationCompleted = false;
            handDirectPresentationHeld = false;
            suppressesProgressionForCurrentRelease = false;
            isRecoveredSourceBox = false;
            isMysterySourceBox = false;
            colorId = MarbleColorId.None;
            this.colorEntry = null;
            mysteryLockedSprite = null;
            capacityController = null;
            currentState = SourceBoxState.Locked;
            releaseOutputMultiplier = 1;

            if (!ValidateInitializeInput(newColorId, marbleCount, newIsMysterySourceBox, colorEntry, newMysteryLockedSprite, marblePrefab, newReleasedMarbleContainer, newCapacityController))
            {
                ApplyStateVisuals();
                unlockAnimator?.ResetVisuals(false);
                return;
            }

            colorId = newColorId;
            isMysterySourceBox = newIsMysterySourceBox;
            this.colorEntry = colorEntry;
            mysteryLockedSprite = newMysteryLockedSprite;
            releasedMarbleContainer = newReleasedMarbleContainer;
            capacityController = newCapacityController;
            holderSpriteRenderer.sprite = colorEntry.SourceBoxSprite;
            if (isMysterySourceBox)
            {
                lockedSpriteRenderer.sprite = mysteryLockedSprite;
            }
            else
            {
                lockedSpriteRenderer.sprite = colorEntry.LockedSourceBoxSprite;
            }

            for (int i = 0; i < marbleCount; i++)
            {
                Marble marble = Instantiate(marblePrefab, marbleSlots[i]);
                marble.name = $"Marble_{i:00}";

                Transform marbleTransform = marble.transform;
                marbleTransform.localPosition = Vector3.zero;
                marbleTransform.localRotation = Quaternion.identity;
                marbleTransform.localScale = Vector3.one;

                marble.Initialize(newColorId, colorEntry.MarbleSprite, newCellIndex);
                marbles.Add(marble);
#if UNITY_EDITOR
                MarbleDebugTracker.SourceAdded(marble, this, i);
#endif
            }

            releaseAnimator?.ResetVisuals();
            ApplyStateVisuals();
            unlockAnimator?.ResetVisuals(false);
        }

        public bool InitializeRecovered(
            MarbleColorId newColorId,
            MarbleColorCatalog.Entry recoveredColorEntry,
            Transform newReleasedMarbleContainer,
            MarbleCapacityController newCapacityController,
            Action<SourceBox> released)
        {
            releaseAnimator?.ResetVisuals();
            KillInvalidMoveFeedback();
            ClearRuntimeMarbles();

            if (!ValidateReferences() ||
                !MarbleColorCatalog.IsGameplayColor(newColorId) ||
                recoveredColorEntry == null ||
                recoveredColorEntry.SourceBoxSprite == null ||
                newReleasedMarbleContainer == null ||
                newCapacityController == null)
            {
                Debug.LogError(
                    $"{nameof(SourceBox)} on '{name}' cannot initialize as a recovered SourceBox because its recovery references are incomplete.",
                    this);
                return false;
            }

            releasedCallback = _ => released?.Invoke(this);
            cellIndex = -1;
            releaseCallbackInvoked = false;
            unlockAnimationInProgress = false;
            interactionBlocked = false;
            boosterTargetingInputOverride = false;
            handOpenViewPreview = false;
            handMysteryRevealInProgress = false;
            handDirectTransferCompleted = false;
            handDirectSourceAnimationStarted = false;
            handDirectSourceAnimationCompleted = false;
            handDirectPresentationHeld = false;
            suppressesProgressionForCurrentRelease = false;
            isRecoveredSourceBox = true;
            isMysterySourceBox = false;
            colorId = newColorId;
            colorEntry = recoveredColorEntry;
            mysteryLockedSprite = null;
            releasedMarbleContainer = newReleasedMarbleContainer;
            capacityController = newCapacityController;
            currentState = SourceBoxState.Available;
            releaseOutputMultiplier = 1;

            holderSpriteRenderer.sprite = recoveredColorEntry.SourceBoxSprite;
            if (lockedSpriteRenderer != null)
            {
                lockedSpriteRenderer.sprite = recoveredColorEntry.LockedSourceBoxSprite;
            }

            releaseAnimator?.ResetVisuals();
            unlockAnimator?.ResetVisuals(true);
            ApplyStateVisuals();
            return true;
        }

        public bool AddRecoveredMarble(Marble marble, int slotIndex)
        {
            if (!isRecoveredSourceBox || currentState != SourceBoxState.Available ||
                marble == null || marble.ColorId != colorId || marbles.Contains(marble) ||
                marbles.Count >= LevelCellData.MaxMarbleCount ||
                slotIndex < 0 || slotIndex >= marbleSlots.Length ||
                marbleSlots[slotIndex] == null)
            {
                return false;
            }

            Transform slot = marbleSlots[slotIndex];
            marble.transform.SetParent(slot, false);
            marble.transform.localPosition = Vector3.zero;
            marble.transform.localRotation = Quaternion.identity;
            marble.transform.localScale = Vector3.one;
            marble.PrepareForReleaseAnimation();
            marble.SetVisualSortingOrder(1);
            marbles.Add(marble);
#if UNITY_EDITOR
            MarbleDebugTracker.SourceAdded(marble, this, slotIndex);
#endif
            ApplyInputState();
            return true;
        }

        public Vector3 GetRecoveredMarbleTargetWorldPosition(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= marbleSlots.Length || marbleSlots[slotIndex] == null)
            {
                return marbleRoot != null ? marbleRoot.transform.position : transform.position;
            }

            return marbleSlots[slotIndex].position;
        }

        public bool TryReleaseMarbles()
        {
            return TryReleaseMarblesWithResult() == SourceBoxReleaseResult.Success;
        }

        public void SetReleaseOutputMultiplier(int multiplier)
        {
            releaseOutputMultiplier = Mathf.Max(1, multiplier);
            RefreshInputState();
        }

        public void CollectReleaseOrderedMarbles(List<Marble> result)
        {
            releaseAnimator.CollectReleaseOrderedMarbles(marbles, result);
        }

        public SourceBoxReleaseResult TryReleaseMarblesWithResult()
        {
            suppressesProgressionForCurrentRelease = false;
            return TryReleaseMarblesWithResult(false, true);
        }

        public bool CanReleaseAsConnectedPairMember()
        {
            return CanPrepareRelease(false);
        }

        public SourceBoxReleaseResult TryReleaseWithReservedCapacity()
        {
            suppressesProgressionForCurrentRelease = false;
            return TryReleaseMarblesWithResult(true, false);
        }

        public SourceBoxReleaseResult TryReleaseWithHandBooster()
        {
            if (!CanPrepareHandBoosterRelease())
            {
                PlayInvalidMoveFeedback();
                return SourceBoxReleaseResult.InvalidState;
            }

            suppressesProgressionForCurrentRelease = true;
            SourceBoxReleaseResult result = TryReleaseMarblesWithResult(false, false);
            if (result != SourceBoxReleaseResult.Success)
            {
                suppressesProgressionForCurrentRelease = false;
            }

            return result;
        }

        public SourceBoxReleaseResult TryReleaseWithReservedCapacityAsHandBooster()
        {
            if (!CanPrepareHandBoosterRelease())
            {
                return SourceBoxReleaseResult.InvalidState;
            }

            suppressesProgressionForCurrentRelease = true;
            SourceBoxReleaseResult result = TryReleaseMarblesWithResult(true, false);
            if (result != SourceBoxReleaseResult.Success)
            {
                suppressesProgressionForCurrentRelease = false;
            }

            return result;
        }

        public bool TryBeginHandDirectTransfer(bool bypassAvailability, out Marble[] transferMarbles)
        {
            transferMarbles = null;
            if (bypassAvailability ? !CanBeginForcedHandDirectTransfer : !CanBeginHandDirectTransfer)
            {
                return false;
            }

            transferMarbles = marbles.ToArray();
            marbles.Clear();
#if UNITY_EDITOR
            foreach (var marble in transferMarbles)
                MarbleDebugTracker.SetLocation(marble, MarbleDebugLocation.InTransit, this,
                    "Hand committed / awaiting target reveal", "Hand source detached");
#endif
            handOpenViewPreview = false;
            handMysteryRevealInProgress = false;
            currentState = SourceBoxState.Released;
            unlockAnimationInProgress = false;
            releaseCallbackInvoked = false;
            handDirectTransferCompleted = false;
            handDirectSourceAnimationStarted = false;
            handDirectSourceAnimationCompleted = false;
            handDirectPresentationHeld = true;
            suppressesProgressionForCurrentRelease = true;
            unlockAnimator?.ResetVisuals(false);
            StateChanged?.Invoke(this);
            PrepareHandDirectReleaseVisuals();
            ReleaseStarted?.Invoke(this);
            return true;
        }

        public Sequence CreateHandMarbleReleaseSequence(Marble marble)
        {
            if (marble == null || releasedMarbleContainer == null || releaseAnimator == null)
            {
                return null;
            }

            return releaseAnimator.CreateMarbleReleaseSequence(
                marble,
                () => marble.transform.SetParent(releasedMarbleContainer, true));
        }

        public void CompleteHandDirectTransfer()
        {
            if (currentState == SourceBoxState.Released)
            {
                handDirectTransferCompleted = true;
                BeginHandDirectSourceAnimation();
            }
        }

        private void BeginHandDirectSourceAnimation()
        {
            if (handDirectSourceAnimationStarted)
            {
                return;
            }

            handDirectSourceAnimationStarted = true;
            handDirectPresentationHeld = false;
            if (releaseAnimator == null || !releaseAnimator.PlaySourceOnly(HandleHandDirectSourceAnimationCompleted))
            {
                HandleHandDirectSourceAnimationCompleted();
            }
        }

        private void HandleHandDirectSourceAnimationCompleted()
        {
            handDirectSourceAnimationCompleted = true;
            ApplyStateVisuals();
            TryFinalizeHandDirectRelease();
        }

        private void TryFinalizeHandDirectRelease()
        {
            if (handDirectTransferCompleted && handDirectSourceAnimationCompleted)
            {
                InvokeReleasedCallback();
            }
        }

        private void PrepareHandDirectReleaseVisuals()
        {
            handOpenViewPreview = false;
            unlockAnimator?.ResetVisuals(true);
            if (holderVisual != null)
            {
                holderVisual.SetActive(true);
            }

            if (lockedVisual != null)
            {
                lockedVisual.SetActive(false);
            }

            if (marbleRoot != null)
            {
                marbleRoot.SetActive(true);
            }

            SetOutlineActive(false);
            SetInputEnabled(false);
        }

        public bool PlayHandMysteryRevealPreview(Action onCompleted)
        {
            if (!isMysterySourceBox || currentState != SourceBoxState.Locked ||
                handMysteryRevealInProgress || interactionBlocked || marbles.Count == 0)
            {
                return false;
            }

            handMysteryRevealInProgress = true;
            ApplyInputState();
            AudioManager.Instance?.PlaySfx(AudioKey.SourceBoxUnlock);

            if (unlockAnimator == null)
            {
                handMysteryRevealInProgress = false;
                onCompleted?.Invoke();
                return true;
            }

            unlockAnimator.Play(() =>
            {
                handMysteryRevealInProgress = false;
                ApplyInputState();
                onCompleted?.Invoke();
            });
            return true;
        }

        public void SetHandOpenViewPreview(bool active)
        {
            bool shouldPreview = active && currentState == SourceBoxState.Locked && !isMysterySourceBox;
            if (shouldPreview && handOpenViewPreview && !handMysteryRevealInProgress)
            {
                return;
            }

            handOpenViewPreview = shouldPreview;
            if (!active)
            {
                handMysteryRevealInProgress = false;
                unlockAnimator?.ResetVisuals(currentState == SourceBoxState.Available);
                ApplyStateVisuals();
                PresentationChanged?.Invoke(this);
                return;
            }

            ApplyInputState();
            SetOutlineActive(true);
            PresentationChanged?.Invoke(this);

            if (unlockAnimator == null)
            {
                ApplyStateVisuals();
                return;
            }

            unlockAnimator.Play(() =>
            {
                if (!handOpenViewPreview || currentState != SourceBoxState.Locked)
                {
                    return;
                }

                ApplyStateVisuals();
                PresentationChanged?.Invoke(this);
            });
        }

        private SourceBoxReleaseResult TryReleaseMarblesWithResult(bool usesExistingCapacityReservation, bool requiresUserInteraction)
        {
            if (!CanPrepareRelease(requiresUserInteraction))
            {
                PlayInvalidMoveFeedback();
                return currentState == SourceBoxState.Available
                    ? SourceBoxReleaseResult.NotInteractable
                    : SourceBoxReleaseResult.InvalidState;
            }

            Marble[] releaseMarbles = marbles.ToArray();
            int releaseCount = RequiredReleaseCapacity;
            if (!usesExistingCapacityReservation && (capacityController == null || !capacityController.TryReserve(releaseCount)))
            {
                PlayInvalidMoveFeedback();
                RefreshInputState();
                return capacityController == null
                    ? SourceBoxReleaseResult.InvalidState
                    : SourceBoxReleaseResult.BoardFull;
            }

            SourceBoxState previousState = currentState;
            currentState = SourceBoxState.Released;
            StateChanged?.Invoke(this);
            releaseCallbackInvoked = false;
            SetOutlineActive(false);
            if (inputCollider != null)
            {
                inputCollider.isTrigger = true;
                inputCollider.enabled = false;
            }

            if (!releaseAnimator.Play(releaseMarbles, releasedMarbleContainer, OnReleaseAnimationCompleted))
            {
                currentState = previousState;
                if (!usesExistingCapacityReservation)
                {
                    capacityController.CancelReservation(releaseCount);
                }

                ApplyStateVisuals();
                StateChanged?.Invoke(this);
                PlayInvalidMoveFeedback();
                return SourceBoxReleaseResult.InvalidState;
            }

            capacityController.CommitReservation(releaseCount);
#if UNITY_EDITOR
            foreach (var marble in releaseMarbles)
                if (!marble.IsReleased)
                    MarbleDebugTracker.SetLocation(marble, MarbleDebugLocation.InTransit, this,
                        "Source release animation", "Source release committed");
#endif
            ReleaseStarted?.Invoke(this);
            return SourceBoxReleaseResult.Success;
        }

        private bool CanPrepareRelease(bool requiresUserInteraction)
        {
            bool hasValidState = requiresUserInteraction
                ? currentState == SourceBoxState.Available && !unlockAnimationInProgress && !interactionBlocked
                : currentState != SourceBoxState.Released;
            if (!hasValidState || marbles.Count == 0 || releasedMarbleContainer == null || releaseAnimator == null || capacityController == null)
            {
                return false;
            }

            return releaseAnimator.CanPlay(marbles, releasedMarbleContainer);
        }

        private bool CanPrepareHandBoosterRelease()
        {
            if (currentState != SourceBoxState.Locked || unlockAnimationInProgress || interactionBlocked ||
                marbles.Count == 0 || releasedMarbleContainer == null || releaseAnimator == null || capacityController == null)
            {
                return false;
            }

            return releaseAnimator.CanPlay(marbles, releasedMarbleContainer);
        }

        public void RefreshInputState()
        {
            ApplyInputState();
        }

        public void RefreshHolderVisual()
        {
            if (holderVisual == null || currentState != SourceBoxState.Available)
            {
                return;
            }

            holderVisual.SetActive(false);
            holderVisual.SetActive(true);
        }

        public void RefreshVisuals()
        {
            ApplyStateVisuals();
            ApplyMarbleVisuals();
            RefreshSpriteRenderers();
            RefreshHolderVisual();
        }

        public void SetInteractionBlocked(bool blocked)
        {
            interactionBlocked = blocked;
            ApplyInputState();
        }

        public void SetBoosterTargetingInputOverride(bool active)
        {
            boosterTargetingInputOverride = active;
            ApplyInputState();
        }

        public bool TryGetLockedVisualSortingGroup(out SortingGroup sortingGroup)
        {
            if (!lockedVisualSortingGroupResolved)
            {
                lockedVisualSortingGroup = lockedVisual != null ? lockedVisual.GetComponent<SortingGroup>() : null;
                lockedVisualSortingGroupResolved = true;
            }

            sortingGroup = lockedVisualSortingGroup;
            return sortingGroup != null;
        }

        public void SetRuntimeAvailability(bool available, bool animateUnlock = true)
        {
            if (currentState == SourceBoxState.Released)
            {
                return;
            }

            if (available)
            {
                if (currentState == SourceBoxState.Available)
                {
                    return;
                }

                if (animateUnlock)
                {
                    Unlock();
                    return;
                }

                currentState = SourceBoxState.Available;
                unlockAnimationInProgress = false;
                unlockAnimator?.ResetVisuals(true);
                ApplyStateVisuals();
                StateChanged?.Invoke(this);
                return;
            }

            if (currentState != SourceBoxState.Available)
            {
                return;
            }

            unlockAnimationInProgress = false;
            currentState = SourceBoxState.Locked;
            StateChanged?.Invoke(this);
            unlockAnimator?.ResetVisuals(false);
            ApplyStateVisuals();
            SetInputEnabled(false);
        }

        private void OnDisable()
        {
            releaseAnimator?.ResetVisuals();
            unlockAnimator?.ResetVisuals(currentState == SourceBoxState.Available);
            KillInvalidMoveFeedback();
            unlockAnimationInProgress = false;
            interactionBlocked = false;
            handOpenViewPreview = false;
            handMysteryRevealInProgress = false;
            handDirectPresentationHeld = false;
        }

        public void Unlock()
        {
            if (currentState != SourceBoxState.Locked)
            {
                return;
            }

            currentState = SourceBoxState.Available;
            StateChanged?.Invoke(this);
            unlockAnimationInProgress = true;
            SetOutlineActive(true);
            SetInputEnabled(false);
            AudioManager.Instance?.PlaySfx(AudioKey.SourceBoxUnlock);

            if (unlockAnimator == null)
            {
                unlockAnimationInProgress = false;
                ApplyStateVisuals();
                return;
            }

            unlockAnimator.Play(OnUnlockAnimationCompleted);
        }

        private void OnUnlockAnimationCompleted()
        {
            if (currentState != SourceBoxState.Available)
            {
                unlockAnimationInProgress = false;
                return;
            }

            unlockAnimationInProgress = false;
            ApplyStateVisuals();
        }

        private void OnReleaseAnimationCompleted()
        {
            ApplyStateVisuals();
            InvokeReleasedCallback();
        }

        private void InvokeReleasedCallback()
        {
            if (releaseCallbackInvoked)
            {
                return;
            }

            releaseCallbackInvoked = true;
            releasedCallback?.Invoke(cellIndex);
        }

        private bool ValidateInitializeInput(
            MarbleColorId newColorId,
            int marbleCount,
            bool newIsMysterySourceBox,
            MarbleColorCatalog.Entry colorEntry,
            Sprite newMysteryLockedSprite,
            Marble marblePrefab,
            Transform newReleasedMarbleContainer,
            MarbleCapacityController newCapacityController)
        {
            if (!ValidateReferences())
            {
                return false;
            }

            if (!MarbleColorCatalog.IsGameplayColor(newColorId))
            {
                Debug.LogError($"{nameof(SourceBox)} on '{name}' at cell index {cellIndex} cannot initialize with color '{newColorId}'.", this);
                return false;
            }

            if (colorEntry == null)
            {
                Debug.LogError($"{nameof(SourceBox)} on '{name}' at cell index {cellIndex} cannot initialize because the color catalog entry is missing.", this);
                return false;
            }

            if (colorEntry.SourceBoxSprite == null)
            {
                Debug.LogError($"{nameof(SourceBox)} on '{name}' at cell index {cellIndex} cannot initialize because SourceBox sprite for '{newColorId}' is missing.", this);
                return false;
            }

            if (newIsMysterySourceBox)
            {
                if (newMysteryLockedSprite == null)
                {
                    Debug.LogError($"{nameof(SourceBox)} on '{name}' at cell index {cellIndex} cannot initialize because Mystery SourceBox sprite is missing.", this);
                    return false;
                }
            }
            else if (colorEntry.LockedSourceBoxSprite == null)
            {
                Debug.LogError($"{nameof(SourceBox)} on '{name}' at cell index {cellIndex} cannot initialize because locked SourceBox sprite for '{newColorId}' is missing.", this);
                return false;
            }

            if (colorEntry.MarbleSprite == null)
            {
                Debug.LogError($"{nameof(SourceBox)} on '{name}' at cell index {cellIndex} cannot initialize because Marble sprite for '{newColorId}' is missing.", this);
                return false;
            }

            if (marblePrefab == null)
            {
                Debug.LogError($"{nameof(SourceBox)} on '{name}' at cell index {cellIndex} cannot initialize because Marble prefab reference is missing.", this);
                return false;
            }

            if (newReleasedMarbleContainer == null)
            {
                Debug.LogError($"{nameof(SourceBox)} on '{name}' at cell index {cellIndex} cannot initialize because ReleasedMarbleContainer reference is missing.", this);
                return false;
            }

            if (newCapacityController == null)
            {
                Debug.LogError($"{nameof(SourceBox)} on '{name}' at cell index {cellIndex} cannot initialize because MarbleCapacityController reference is missing.", this);
                return false;
            }

            if (marbleCount < LevelCellData.MinMarbleCount || marbleCount > LevelCellData.MaxMarbleCount)
            {
                Debug.LogError($"{nameof(SourceBox)} on '{name}' at cell index {cellIndex} received invalid marble count {marbleCount}. Expected {LevelCellData.MinMarbleCount}-{LevelCellData.MaxMarbleCount}.", this);
                return false;
            }

            for (int i = 0; i < marbleCount; i++)
            {
                if (marbleSlots[i] == null)
                {
                    Debug.LogError($"{nameof(SourceBox)} on '{name}' at cell index {cellIndex} is missing marble slot reference at index {i}.", this);
                    return false;
                }
            }

            return true;
        }

        private bool ValidateReferences()
        {
            if (holderVisual == null)
            {
                Debug.LogError($"{nameof(SourceBox)} on '{name}' is missing HolderVisual GameObject reference.", this);
                return false;
            }

            if (holderSpriteRenderer == null)
            {
                Debug.LogError($"{nameof(SourceBox)} on '{name}' is missing HolderVisual SpriteRenderer reference.", this);
                return false;
            }

            if (lockedVisual == null)
            {
                Debug.LogError($"{nameof(SourceBox)} on '{name}' is missing LockedVisual GameObject reference.", this);
                return false;
            }

            if (lockedSpriteRenderer == null)
            {
                Debug.LogError($"{nameof(SourceBox)} on '{name}' is missing LockedVisual SpriteRenderer reference.", this);
                return false;
            }

            if (inputCollider == null)
            {
                Debug.LogError($"{nameof(SourceBox)} on '{name}' is missing InputHitArea Collider2D reference.", this);
                return false;
            }

            if (marbleRoot == null)
            {
                Debug.LogError($"{nameof(SourceBox)} on '{name}' is missing MarbleRoot Transform reference.", this);
                return false;
            }

            if (marbleSlots == null || marbleSlots.Length < LevelCellData.MaxMarbleCount)
            {
                Debug.LogError($"{nameof(SourceBox)} on '{name}' needs {LevelCellData.MaxMarbleCount} marble slot references.", this);
                return false;
            }

            return true;
        }

        private void ApplyStateVisuals()
        {
            if (handDirectPresentationHeld)
            {
                PrepareHandDirectReleaseVisuals();
                return;
            }

            bool isAvailable = currentState == SourceBoxState.Available;
            bool isLocked = currentState == SourceBoxState.Locked;
            bool isReleased = currentState == SourceBoxState.Released;
            bool displayAsAvailable = isAvailable || (isLocked && handOpenViewPreview && !isMysterySourceBox);

            if (holderVisual != null)
            {
                holderVisual.SetActive(displayAsAvailable && !isReleased);
            }

            if (lockedVisual != null)
            {
                lockedVisual.SetActive(isLocked && !handOpenViewPreview);
            }

            if (inputCollider != null)
            {
                ApplyInputState();
            }

            if (colorEntry != null)
            {
                if (holderSpriteRenderer != null)
                {
                    holderSpriteRenderer.sprite = colorEntry.SourceBoxSprite;
                }

                if (lockedSpriteRenderer != null)
                {
                    lockedSpriteRenderer.sprite = isMysterySourceBox
                        ? mysteryLockedSprite
                        : colorEntry.LockedSourceBoxSprite;
                }
            }

            if (marbleRoot != null)
            {
                marbleRoot.SetActive(displayAsAvailable && !isReleased);
            }

            SetOutlineActive(displayAsAvailable && !isReleased);
        }

        private void ApplyMarbleVisuals()
        {
            if (colorEntry == null || colorEntry.MarbleSprite == null)
            {
                return;
            }

            for (int i = 0; i < marbles.Count; i++)
            {
                Marble marble = marbles[i];
                if (marble != null)
                {
                    marble.ApplyVisual(colorEntry.MarbleSprite);
                }
            }
        }

        private void RefreshSpriteRenderers()
        {
            RefreshSpriteRenderer(holderSpriteRenderer);
            RefreshSpriteRenderer(lockedSpriteRenderer);

            if (marbleRoot == null)
            {
                return;
            }

            SpriteRenderer[] spriteRenderers = marbleRoot.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                RefreshSpriteRenderer(spriteRenderers[i]);
            }
        }

        private static void RefreshSpriteRenderer(SpriteRenderer spriteRenderer)
        {
            if (spriteRenderer == null)
            {
                return;
            }

            bool wasEnabled = spriteRenderer.enabled;
            spriteRenderer.enabled = false;
            spriteRenderer.enabled = wasEnabled;
        }

        private void SetOutlineActive(bool isActive)
        {
            if (outlineObject != null)
            {
                outlineObject.SetActive(isActive);
            }
        }

        private void SetInputEnabled(bool isEnabled)
        {
            if (inputCollider == null)
            {
                return;
            }

            inputCollider.isTrigger = true;
            inputCollider.enabled = isEnabled;
        }

        private void ApplyInputState()
        {
            bool canReceiveInput =
                currentState == SourceBoxState.Available &&
                !interactionBlocked &&
                !unlockAnimationInProgress &&
                marbles.Count > 0;

            if (!canReceiveInput && boosterTargetingInputOverride)
            {
                canReceiveInput = currentState == SourceBoxState.Locked &&
                                  !interactionBlocked &&
                                  !unlockAnimationInProgress &&
                                  !handMysteryRevealInProgress &&
                                  marbles.Count > 0;
            }

            SetInputEnabled(canReceiveInput);
        }

        private void PlayInvalidMoveFeedback()
        {
            AudioManager.Instance?.PlaySfx(AudioKey.WrongMove);

            Transform feedbackTarget = invalidMoveFeedbackTarget != null ? invalidMoveFeedbackTarget : transform;
            invalidMoveFeedbackSequence?.Kill(false);
            feedbackTarget.localScale = invalidMoveNormalScale;

            invalidMoveFeedbackSequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            invalidMoveFeedbackSequence.Append(feedbackTarget
                .DOScale(invalidMoveScale, invalidMoveScaleUpDuration)
                .SetEase(invalidMoveScaleUpEase));
            invalidMoveFeedbackSequence.Append(feedbackTarget
                .DOScale(invalidMoveNormalScale, invalidMoveScaleDownDuration)
                .SetEase(invalidMoveScaleDownEase));
            invalidMoveFeedbackSequence.OnKill(() => invalidMoveFeedbackSequence = null);
        }

        private void KillInvalidMoveFeedback()
        {
            if (invalidMoveFeedbackSequence != null)
            {
                invalidMoveFeedbackSequence.Kill(false);
                invalidMoveFeedbackSequence = null;
            }

            Transform feedbackTarget = invalidMoveFeedbackTarget != null ? invalidMoveFeedbackTarget : transform;
            feedbackTarget.localScale = invalidMoveNormalScale;
        }

#if UNITY_EDITOR
        public void DebugCollectMarbles(List<MarbleDebugObservation> result)
        {
            for (int i = 0; i < marbles.Count; i++)
            {
                Marble marble = marbles[i];
                if (currentState != SourceBoxState.Released)
                    MarbleDebugTracker.Observe(result, marble, MarbleDebugLocation.SourceBox, this, MarbleDebugTracker.SourceDetail(this, i));
                else if (marble != null && !marble.IsReleased && !marble.IsTransferringToTarget &&
                         !marble.IsOwnedByUfo && marble.transform.parent == releasedMarbleContainer)
                    MarbleDebugTracker.Observe(result, marble, MarbleDebugLocation.InTransit, this, "Source release animation");
            }
        }
#endif

        private void ClearRuntimeMarbles()
        {
            HashSet<Marble> destroyedMarbles = new HashSet<Marble>();

            for (int i = 0; i < marbles.Count; i++)
            {
                DestroyMarbleOnce(marbles[i], destroyedMarbles);
            }

            marbles.Clear();

            if (marbleRoot != null)
            {
                Marble[] rootMarbles = marbleRoot.GetComponentsInChildren<Marble>(true);
                for (int i = 0; i < rootMarbles.Length; i++)
                {
                    DestroyMarbleOnce(rootMarbles[i], destroyedMarbles);
                }
            }

            if (marbleSlots == null)
            {
                return;
            }

            for (int i = 0; i < marbleSlots.Length; i++)
            {
                Transform slot = marbleSlots[i];
                if (slot == null)
                {
                    continue;
                }

                Marble[] slotMarbles = slot.GetComponentsInChildren<Marble>(true);
                for (int j = 0; j < slotMarbles.Length; j++)
                {
                    DestroyMarbleOnce(slotMarbles[j], destroyedMarbles);
                }
            }
        }

        private static void DestroyMarbleOnce(Marble marble, HashSet<Marble> destroyedMarbles)
        {
            if (marble == null || destroyedMarbles.Contains(marble))
            {
                return;
            }

            destroyedMarbles.Add(marble);
            DestroyMarble(marble);
        }

        private static void DestroyMarble(Marble marble)
        {
            if (marble == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(marble.gameObject);
            }
            else
            {
                DestroyImmediate(marble.gameObject);
            }
        }
    }
}
