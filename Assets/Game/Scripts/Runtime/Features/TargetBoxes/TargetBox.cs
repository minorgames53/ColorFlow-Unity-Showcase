using System;
#if UNITY_EDITOR
using System.Collections.Generic;
#endif
using Gameplay.SourceBoxes;
using UnityEngine;
#if UNITY_EDITOR
using Gameplay.MarbleDebug;
#endif

namespace Gameplay.TargetBoxes
{
    [SelectionBase]
    public sealed class TargetBox : MonoBehaviour
    {
        private const int SlotCount = 3;

        [SerializeField] private GameObject activeViewRoot;
        [SerializeField] private SpriteRenderer activeViewRenderer;
        [SerializeField] private GameObject passiveViewRoot;
        [SerializeField] private SpriteRenderer passiveViewRenderer;
        [SerializeField] private Transform[] marbleSlots = new Transform[SlotCount];
        [SerializeField] private TargetBoxRevealView revealView;
        [SerializeField] private TargetBoxFillAnimationView fillAnimationView;

        private Sprite activeSprite;
        private Sprite passiveSprite;
        private Sprite mysteryPassiveSprite;
        private int reservedSlotCount;
        private int arrivedMarbleCount;
        private bool isCompleting;
        private bool runtimeLocked;
        private bool activationRequestedWhileLocked;
        private Action pendingActivationCallbacks;

        public MarbleColorId ColorId { get; private set; }
        public bool IsMystery { get; private set; }
        public int ConnectedTargetGroupId { get; private set; }
        public bool HasConnectedTargetGroup => ConnectedTargetGroupId > 0;
        public bool IsActive { get; private set; }
        public int ReservedSlotCount => reservedSlotCount;
        public int ArrivedMarbleCount => arrivedMarbleCount;
        public int AvailableReservationCount => Mathf.Max(0, SlotCount - reservedSlotCount);
        public bool IsFullyReserved => reservedSlotCount >= SlotCount;
        public bool IsComplete => arrivedMarbleCount >= SlotCount;
        public bool IsCompleting => isCompleting;
        public bool IsRuntimeLocked => runtimeLocked;
        public Vector3 AimPosition => transform.position;

        public void Initialize(
            MarbleColorId colorId,
            bool isMystery,
            bool isActive,
            Sprite activeSprite,
            Sprite passiveSprite,
            Sprite mysteryPassiveSprite,
            int connectedTargetGroupId = 0,
            bool initiallyLocked = false)
        {
            if (revealView == null)
            {
                revealView = GetComponent<TargetBoxRevealView>();
            }

            if (fillAnimationView == null)
            {
                fillAnimationView = GetComponent<TargetBoxFillAnimationView>();
            }

            if (!ValidateReferences())
            {
                return;
            }

            if (!MarbleColorCatalog.IsGameplayColor(colorId))
            {
                Debug.LogError($"{nameof(TargetBox)} on '{name}' cannot initialize with color '{colorId}'.", this);
                return;
            }

            if (activeSprite == null)
            {
                Debug.LogError($"{nameof(TargetBox)} on '{name}' is missing active sprite for color '{colorId}'.", this);
                return;
            }

            if (passiveSprite == null)
            {
                Debug.LogError($"{nameof(TargetBox)} on '{name}' is missing passive sprite for color '{colorId}'.", this);
                return;
            }

            if (isMystery && (!isActive || initiallyLocked) && mysteryPassiveSprite == null)
            {
                Debug.LogError($"{nameof(TargetBox)} on '{name}' is missing mystery passive sprite.", this);
                return;
            }

            ColorId = colorId;
            IsMystery = isMystery;
            ConnectedTargetGroupId = connectedTargetGroupId;
            this.activeSprite = activeSprite;
            this.passiveSprite = passiveSprite;
            this.mysteryPassiveSprite = mysteryPassiveSprite;
            reservedSlotCount = 0;
            arrivedMarbleCount = 0;
            isCompleting = false;
            runtimeLocked = initiallyLocked;
            activationRequestedWhileLocked = false;
            pendingActivationCallbacks = null;

            activeViewRenderer.sprite = activeSprite;
            passiveViewRenderer.sprite = isMystery && !isActive ? mysteryPassiveSprite : passiveSprite;

            if (isActive)
            {
                SetActiveViewImmediate();
            }
            else
            {
                SetPassiveView();
            }
        }

        public bool CanReserve(MarbleColorId colorId)
        {
            return IsActive && !runtimeLocked && ColorId == colorId && reservedSlotCount < SlotCount && !isCompleting;
        }

        public bool TryReserveSlot(MarbleColorId colorId, out Transform slot)
        {
            slot = null;

            if (!CanReserve(colorId) || marbleSlots == null || reservedSlotCount >= marbleSlots.Length)
            {
                return false;
            }

            Transform reservedSlot = marbleSlots[reservedSlotCount];
            if (reservedSlot == null)
            {
                Debug.LogError($"{nameof(TargetBox)} on '{name}' cannot reserve slot {reservedSlotCount} because the slot reference is missing.", this);
                return false;
            }

            slot = reservedSlot;
            reservedSlotCount++;
            return true;
        }

        public bool CanReserveForHand(MarbleColorId colorId)
        {
            return !HasConnectedTargetGroup && CanReserveIgnoringActiveView(colorId);
        }

        public bool TryReserveSlotForHand(MarbleColorId colorId, out Transform slot)
        {
            return TryReserveSlotIgnoringActiveView(
                CanReserveForHand(colorId),
                "Hand",
                out slot);
        }

        public bool CanReserveForUfo(MarbleColorId colorId)
        {
            return CanReserveIgnoringActiveView(colorId);
        }

        public bool TryReserveSlotForUfo(MarbleColorId colorId, out Transform slot)
        {
            return TryReserveSlotIgnoringActiveView(
                CanReserveForUfo(colorId),
                "UFO",
                out slot);
        }

        private bool CanReserveIgnoringActiveView(MarbleColorId colorId)
        {
            return !runtimeLocked &&
                   ColorId == colorId &&
                   reservedSlotCount < SlotCount &&
                   !isCompleting;
        }

        private bool TryReserveSlotIgnoringActiveView(
            bool isEligible,
            string transferName,
            out Transform slot)
        {
            slot = null;
            if (!isEligible || marbleSlots == null || reservedSlotCount >= marbleSlots.Length)
            {
                return false;
            }

            Transform reservedSlot = marbleSlots[reservedSlotCount];
            if (reservedSlot == null)
            {
                Debug.LogError($"{nameof(TargetBox)} on '{name}' cannot reserve {transferName} slot {reservedSlotCount} because the slot reference is missing.", this);
                return false;
            }

            slot = reservedSlot;
            reservedSlotCount++;
            return true;
        }

        public void RevealForHandTransfer(Action onRevealed)
        {
            SetActiveSprite();
            if (revealView != null)
            {
                revealView.Reveal(onRevealed);
                return;
            }

            SetLegacyActiveRoots();
            onRevealed?.Invoke();
        }

        public void ConfirmMarbleArrival()
        {
            arrivedMarbleCount = Mathf.Min(arrivedMarbleCount + 1, SlotCount);
        }

        public void CancelLastReservation()
        {
            if (reservedSlotCount > arrivedMarbleCount)
            {
                reservedSlotCount--;
            }
        }

        public void SetActiveView(Action onActivated = null)
        {
            if (runtimeLocked)
            {
                activationRequestedWhileLocked = true;
                pendingActivationCallbacks += onActivated;
                SetPassiveView();
                return;
            }

            IsActive = false;
            SetActiveSprite();

            if (revealView == null)
            {
                SetActiveViewImmediate();
                onActivated?.Invoke();
                return;
            }

            revealView.Reveal(() =>
            {
                IsActive = true;
                onActivated?.Invoke();
            });
        }

        internal void SetActiveViewImmediate()
        {
            if (runtimeLocked)
            {
                activationRequestedWhileLocked = true;
                SetPassiveView();
                return;
            }

            IsActive = true;
            SetActiveSprite();

            if (revealView != null)
            {
                revealView.SetRevealedImmediate(true);
                return;
            }

            SetLegacyActiveRoots();
        }

        public void SetPassiveView()
        {
            IsActive = false;
            if (passiveViewRenderer != null)
            {
                passiveViewRenderer.sprite = IsMystery ? mysteryPassiveSprite : passiveSprite;
            }

            if (revealView != null)
            {
                revealView.SetRevealedImmediate(false, !HasConnectedTargetGroup);
                return;
            }

            SetLegacyPassiveRoots();
        }

        internal void SetShuffleClosedPresentation()
        {
            if (passiveViewRenderer != null)
            {
                passiveViewRenderer.sprite = IsMystery && !IsActive
                    ? mysteryPassiveSprite
                    : passiveSprite;
            }

            if (revealView != null)
            {
                revealView.SetRevealedImmediate(false, !HasConnectedTargetGroup);
                return;
            }

            SetLegacyPassiveRoots();
        }

        internal void PlayShuffleOpenPresentation(Action onCompleted)
        {
            if (runtimeLocked)
            {
                onCompleted?.Invoke();
                return;
            }

            SetActiveSprite();
            if (revealView != null)
            {
                revealView.Reveal(onCompleted);
                return;
            }

            SetLegacyActiveRoots();
            onCompleted?.Invoke();
        }

        public bool UnlockRuntimeLock()
        {
            if (!runtimeLocked)
            {
                return false;
            }

            runtimeLocked = false;
            if (!activationRequestedWhileLocked)
            {
                return true;
            }

            activationRequestedWhileLocked = false;
            Action callbacks = pendingActivationCallbacks;
            pendingActivationCallbacks = null;
            SetActiveView(callbacks);
            return true;
        }

        public bool TryBeginCompletion()
        {
            if (isCompleting)
            {
                return false;
            }

            isCompleting = true;
#if UNITY_EDITOR
            MarbleDebugTracker.TargetCompleted(this);
#endif
            return true;
        }

#if UNITY_EDITOR
        public void DebugCollectArrivedMarbles(List<MarbleDebugObservation> result, System.Func<Marble, bool> isTransferring, string currentPosition)
        {
            // TargetBox stores an arrival count and slot children, not a marble list.
            // Arrival animation parents to the slot before commit, so exclude active transfers.
            if (marbleSlots == null) return;
            for (int i = 0; i < marbleSlots.Length; i++)
            {
                if (marbleSlots[i] == null) continue;
                foreach (var marble in marbleSlots[i].GetComponentsInChildren<Marble>(true))
                    if (!isTransferring(marble))
                        MarbleDebugTracker.Observe(result, marble, MarbleDebugLocation.TargetBox, this,
                            $"{MarbleDebugTracker.TargetDetail(this, i)} / {currentPosition}");
            }
        }
#endif

        public bool PlayFillAnimation(Action onCompleted)
        {
            if (fillAnimationView == null)
            {
                return false;
            }

            return fillAnimationView.Play(onCompleted);
        }

        public bool TryCompleteReveal()
        {
            return revealView != null && revealView.TryCompleteReveal();
        }

        private bool ValidateReferences()
        {
            bool isValid = true;

            if (activeViewRoot == null)
            {
                Debug.LogError($"{nameof(TargetBox)} on '{name}' is missing ActiveViewRoot reference.", this);
                isValid = false;
            }

            if (activeViewRenderer == null)
            {
                Debug.LogError($"{nameof(TargetBox)} on '{name}' is missing ActiveView SpriteRenderer reference.", this);
                isValid = false;
            }

            if (passiveViewRoot == null)
            {
                Debug.LogError($"{nameof(TargetBox)} on '{name}' is missing PassiveViewRoot reference.", this);
                isValid = false;
            }

            if (passiveViewRenderer == null)
            {
                Debug.LogError($"{nameof(TargetBox)} on '{name}' is missing PassiveView SpriteRenderer reference.", this);
                isValid = false;
            }

            if (marbleSlots == null || marbleSlots.Length != SlotCount)
            {
                int slotCount = marbleSlots?.Length ?? 0;
                Debug.LogError($"{nameof(TargetBox)} on '{name}' must have exactly {SlotCount} marble slots, but has {slotCount}.", this);
                isValid = false;
            }
            else
            {
                for (int i = 0; i < marbleSlots.Length; i++)
                {
                    if (marbleSlots[i] != null)
                    {
                        continue;
                    }

                    Debug.LogError($"{nameof(TargetBox)} on '{name}' is missing marble slot reference at index {i}.", this);
                    isValid = false;
                }
            }

            return isValid;
        }

        private void SetActiveSprite()
        {
            if (activeViewRenderer != null)
            {
                activeViewRenderer.sprite = activeSprite;
            }
        }

        private void SetLegacyActiveRoots()
        {
            if (activeViewRoot != null)
            {
                activeViewRoot.SetActive(true);
            }

            if (passiveViewRoot != null)
            {
                passiveViewRoot.SetActive(false);
            }
        }

        private void SetLegacyPassiveRoots()
        {
            if (activeViewRoot != null)
            {
                activeViewRoot.SetActive(false);
            }

            if (passiveViewRoot != null)
            {
                passiveViewRoot.SetActive(true);
            }
        }
    }
}
