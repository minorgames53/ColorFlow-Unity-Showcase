using DG.Tweening;
using Game.Shared.Ads.Core;
using Game.Shared.Navigation;
using Game.Shared.UI;
using UnityEngine;

namespace Game.Menu
{
    [DisallowMultipleComponent]
    public sealed class MenuNoAdsOfferController : MonoBehaviour
    {
        [Header("Menu Ready")]
        [SerializeField] private SceneReadyNotifier sceneReadyNotifier;

        [Header("Offer")]
        [SerializeField] private GameObject offerRoot;
        [SerializeField] private TweenButton offerButton;
        [SerializeField] private NoAdsPanelController noAdsPanelController;

        [Header("Presentation Handoff")]
        [SerializeField] private CoinFlyAnimator coinFlyAnimator;
        [SerializeField] private UIInputLockService uiInputLockService;

        private AdsService adsService;
        private bool menuReady;
        private bool offerOpenPending;
        private bool markIntroWhenOpened;
        private bool ownsOfferOpenInputLock;
        private bool milestoneOfferPending;
        private Tween coinFlyStartWindowTween;

        private void OnEnable()
        {
            milestoneOfferPending =
                CoinFlyPresentationHandoff.TryConsumeNoAdsOfferRequest();
            SetOfferOpenInputLocked(milestoneOfferPending);
            BindReadyNotifier();
            BindOfferButton();
            BindAdsService();
            RefreshPresentation();
        }

        private void OnDisable()
        {
            CancelPendingOfferOpen();
            UnbindReadyNotifier();
            UnbindOfferButton();
            UnbindAdsService();
        }

        private void BindReadyNotifier()
        {
            if (sceneReadyNotifier != null)
            {
                sceneReadyNotifier.Ready -= HandleMenuReady;
                sceneReadyNotifier.Ready += HandleMenuReady;
            }
        }

        private void UnbindReadyNotifier()
        {
            if (sceneReadyNotifier != null)
            {
                sceneReadyNotifier.Ready -= HandleMenuReady;
            }
        }

        private void BindOfferButton()
        {
            if (offerButton != null)
            {
                offerButton.onClick.RemoveListener(HandleOfferClicked);
                offerButton.onClick.AddListener(HandleOfferClicked);
            }
        }

        private void UnbindOfferButton()
        {
            offerButton?.onClick.RemoveListener(HandleOfferClicked);
        }

        private void BindAdsService()
        {
            AdsService available = AdsService.Instance;
            if (adsService == available)
            {
                return;
            }

            UnbindAdsService();
            adsService = available;
            if (adsService != null)
            {
                adsService.AdPresentationStateChanged += HandlePresentationStateChanged;
            }
        }

        private void UnbindAdsService()
        {
            if (adsService != null)
            {
                adsService.AdPresentationStateChanged -= HandlePresentationStateChanged;
                adsService = null;
            }
        }

        private void HandleMenuReady()
        {
            menuReady = true;
            RefreshPresentation();
        }

        private void HandlePresentationStateChanged()
        {
            RefreshPresentation();
        }

        private void HandleOfferClicked()
        {
            BindAdsService();
            RequestOfferOpen(false);
        }

        private void RefreshPresentation()
        {
            BindAdsService();
            bool visible = adsService != null && adsService.CanShowNoAdsOffer;
            if (offerRoot != null && offerRoot.activeSelf != visible)
            {
                offerRoot.SetActive(visible);
            }

            if (milestoneOfferPending)
            {
                if (menuReady)
                {
                    RequestMilestoneOfferOpen();
                }

                return;
            }

            if (!visible)
            {
                CancelPendingOfferOpen();
                noAdsPanelController?.Close();
                return;
            }

            if (menuReady && !adsService.NoAdsIntroShown)
            {
                RequestOfferOpen(true);
            }
        }

        private void RequestOfferOpen(bool markIntro)
        {
            if (adsService == null || !adsService.CanShowNoAdsOffer ||
                noAdsPanelController == null || noAdsPanelController.IsOpen)
            {
                return;
            }

            markIntroWhenOpened |= markIntro;
            if (offerOpenPending)
            {
                return;
            }

            offerOpenPending = true;
            SetOfferOpenInputLocked(true);
            SchedulePendingOfferOpen();
        }

        private void RequestMilestoneOfferOpen()
        {
            markIntroWhenOpened = true;
            if (offerOpenPending)
            {
                return;
            }

            offerOpenPending = true;
            SchedulePendingOfferOpen();
        }

        private void SchedulePendingOfferOpen()
        {
            coinFlyStartWindowTween?.Kill(false);
            coinFlyStartWindowTween = DOVirtual.DelayedCall(0f, TryOpenPendingOffer)
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
        }

        private void TryOpenPendingOffer()
        {
            coinFlyStartWindowTween = null;
            if (CoinFlyPresentationHandoff.HasPendingPresentation ||
                coinFlyAnimator != null && coinFlyAnimator.IsPlaying)
            {
                SchedulePendingOfferOpen();
                return;
            }

            OpenPendingOffer();
        }

        private void OpenPendingOffer()
        {
            coinFlyStartWindowTween = null;
            try
            {
                BindAdsService();
                bool shouldMarkIntro = markIntroWhenOpened;
                if (offerOpenPending && adsService != null &&
                    adsService.CanShowNoAdsOffer && noAdsPanelController != null)
                {
                    noAdsPanelController.Open(() =>
                    {
                        if (shouldMarkIntro) adsService?.TryMarkNoAdsIntroShown();
                    });
                }

                offerOpenPending = false;
                markIntroWhenOpened = false;

            }
            finally
            {
                milestoneOfferPending = false;
                SetOfferOpenInputLocked(false);
            }
        }

        private void CancelPendingOfferOpen()
        {
            coinFlyStartWindowTween?.Kill(false);
            coinFlyStartWindowTween = null;
            offerOpenPending = false;
            markIntroWhenOpened = false;
            milestoneOfferPending = false;
            SetOfferOpenInputLocked(false);
        }

        private void SetOfferOpenInputLocked(bool locked)
        {
            if (ownsOfferOpenInputLock == locked)
            {
                return;
            }

            ownsOfferOpenInputLock = locked;
            if (uiInputLockService == null)
            {
                return;
            }

            if (locked)
            {
                uiInputLockService.LockUIInput();
            }
            else
            {
                uiInputLockService.UnlockUIInput();
            }
        }
    }
}
