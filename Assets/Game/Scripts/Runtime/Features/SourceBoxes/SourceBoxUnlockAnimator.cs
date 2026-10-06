using System;
using DG.Tweening;
using UnityEngine;

namespace Gameplay.SourceBoxes
{
    public sealed class SourceBoxUnlockAnimator : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform activeVisual;
        [SerializeField] private Transform lockedVisual;
        [SerializeField] private GameObject marbleRoot;

        [Header("Locked Tween")]
        [SerializeField] private Vector3 lockedStartScale = Vector3.one;
        [SerializeField] private Vector3 lockedFinalScale = Vector3.zero;
        [SerializeField, Min(0f)] private float lockedDelay;
        [SerializeField, Min(0.01f)] private float lockedDuration = 0.28f;
        [SerializeField] private Ease lockedEase = Ease.InBack;

        [Header("Active Tween")]
        [SerializeField] private Vector3 activeStartScale = Vector3.zero;
        [SerializeField] private Vector3 activeFinalScale = Vector3.one;
        [SerializeField, Min(0f)] private float activeDelay = 0.2f;
        [SerializeField, Min(0.01f)] private float activeDuration = 0.3f;
        [SerializeField] private Ease activeEase = Ease.OutBack;

        private Sequence activeSequence;
        private Transform marbleRootOriginalParent;
        private int marbleRootOriginalSiblingIndex;
        private Vector3 marbleRootOriginalLocalPosition;
        private Quaternion marbleRootOriginalLocalRotation;
        private Vector3 marbleRootOriginalLocalScale;
        private bool isMarbleRootReparented;

        private void Reset()
        {
            Transform root = transform.Find("Root");
            Transform searchRoot = root != null ? root : transform;

            activeVisual = searchRoot.Find("HolderVisual");
            lockedVisual = searchRoot.Find("LockedVisual");
            Transform marbleRootTransform = searchRoot.Find("MarbleRoot");
            marbleRoot = marbleRootTransform != null ? marbleRootTransform.gameObject : null;
        }

        private void OnDisable()
        {
            KillActiveSequence();
        }

        private void OnValidate()
        {
            lockedDelay = Mathf.Max(0f, lockedDelay);
            lockedDuration = Mathf.Max(0.01f, lockedDuration);
            activeDelay = Mathf.Max(0f, activeDelay);
            activeDuration = Mathf.Max(0.01f, activeDuration);
        }

        public void Play(Action onCompleted)
        {
            KillActiveSequence();
            PrepareActiveVisualForUnlock();

            if (activeVisual == null || lockedVisual == null)
            {
                onCompleted?.Invoke();
                return;
            }

            SetLockedVisualState(true);
            SetMarbleRootState(false);

            lockedVisual.localScale = lockedStartScale;
            AttachMarbleRootToActiveVisual();
            activeSequence = DOTween.Sequence();
            activeSequence.SetLink(gameObject, LinkBehaviour.KillOnDestroy);

            activeSequence.Insert(
                lockedDelay,
                lockedVisual
                    .DOScale(lockedFinalScale, lockedDuration)
                    .SetEase(lockedEase));

            activeSequence.InsertCallback(activeDelay, () => SetMarbleRootState(true));
            activeSequence.Insert(
                activeDelay,
                activeVisual
                    .DOScale(activeFinalScale, activeDuration)
                    .SetEase(activeEase));

            activeSequence.OnComplete(() =>
            {
                activeSequence = null;
                RestoreMarbleRootParent();
                SetLockedVisualState(false);
                activeVisual.localScale = activeFinalScale;
                onCompleted?.Invoke();
            });
        }

        private void PrepareActiveVisualForUnlock()
        {
            if (activeVisual == null)
            {
                return;
            }

            SetActiveVisualState(true);
            activeVisual.localScale = activeStartScale;
        }

        public void ResetVisuals(bool isAvailable)
        {
            KillActiveSequence();

            if (activeVisual != null)
            {
                activeVisual.DOKill(false);
                activeVisual.localScale = isAvailable ? activeFinalScale : activeStartScale;
                SetActiveVisualState(isAvailable);
            }

            if (lockedVisual != null)
            {
                lockedVisual.DOKill(false);
                lockedVisual.localScale = isAvailable ? lockedFinalScale : lockedStartScale;
                SetLockedVisualState(!isAvailable);
            }

            SetMarbleRootState(isAvailable);
        }

        private void KillActiveSequence()
        {
            if (activeSequence == null)
            {
                RestoreMarbleRootParent();
                return;
            }

            activeSequence.Kill(false);
            activeSequence = null;
            RestoreMarbleRootParent();
        }

        private void AttachMarbleRootToActiveVisual()
        {
            if (marbleRoot == null || activeVisual == null || isMarbleRootReparented)
            {
                return;
            }

            Transform marbleRootTransform = marbleRoot.transform;
            marbleRootOriginalParent = marbleRootTransform.parent;
            marbleRootOriginalSiblingIndex = marbleRootTransform.GetSiblingIndex();
            marbleRootOriginalLocalPosition = marbleRootTransform.localPosition;
            marbleRootOriginalLocalRotation = marbleRootTransform.localRotation;
            marbleRootOriginalLocalScale = marbleRootTransform.localScale;

            marbleRootTransform.SetParent(activeVisual, false);
            isMarbleRootReparented = true;
        }

        private void RestoreMarbleRootParent()
        {
            if (!isMarbleRootReparented)
            {
                return;
            }

            isMarbleRootReparented = false;

            if (marbleRoot == null)
            {
                ClearStoredMarbleRootParent();
                return;
            }

            Transform marbleRootTransform = marbleRoot.transform;
            marbleRootTransform.SetParent(marbleRootOriginalParent, false);
            marbleRootTransform.localPosition = marbleRootOriginalLocalPosition;
            marbleRootTransform.localRotation = marbleRootOriginalLocalRotation;
            marbleRootTransform.localScale = marbleRootOriginalLocalScale;

            if (marbleRootOriginalParent != null)
            {
                int siblingIndex = Mathf.Clamp(
                    marbleRootOriginalSiblingIndex,
                    0,
                    marbleRootOriginalParent.childCount - 1);
                marbleRootTransform.SetSiblingIndex(siblingIndex);
            }

            ClearStoredMarbleRootParent();
        }

        private void ClearStoredMarbleRootParent()
        {
            marbleRootOriginalParent = null;
            marbleRootOriginalSiblingIndex = 0;
            marbleRootOriginalLocalPosition = Vector3.zero;
            marbleRootOriginalLocalRotation = Quaternion.identity;
            marbleRootOriginalLocalScale = Vector3.one;
        }

        private void SetActiveVisualState(bool isActive)
        {
            if (activeVisual != null)
            {
                activeVisual.gameObject.SetActive(isActive);
            }
        }

        private void SetLockedVisualState(bool isActive)
        {
            if (lockedVisual != null)
            {
                lockedVisual.gameObject.SetActive(isActive);
            }
        }

        private void SetMarbleRootState(bool isActive)
        {
            if (marbleRoot != null)
            {
                marbleRoot.SetActive(isActive);
            }
        }
    }
}
