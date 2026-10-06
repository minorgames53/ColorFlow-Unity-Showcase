using System;
using Game.Shared.Ads.Core;
using Game.Shared.Store;
using Game.Shared.UI;
using Game.Shared.UI.Panels;
using TMPro;
using UnityEngine;

namespace Game.Menu
{
    [DisallowMultipleComponent]
    public sealed class NoAdsPanelController : MonoBehaviour
    {
        private const string PricePlaceholder = "\u2014";

        [Header("Panel")]
        [SerializeField] private PanelManager panelManager;
        [SerializeField] private UIPanel panel;

        [Header("Actions")]
        [SerializeField] private TweenButton closeButton;
        [SerializeField] private TweenButton buyButton;
        [SerializeField] private TMP_Text priceText;

        private AdsService adsService;
        private StoreManager storeManager;

        public bool IsOpen => panel != null && panel.gameObject.activeInHierarchy;

        private void OnEnable()
        {
            BindButtons();
            BindServices();
            RefreshPresentation();
        }

        private void OnDisable()
        {
            UnbindButtons();
            UnbindServices();
        }

        public bool Open()
        {
            return Open(null);
        }

        public bool Open(Action onOpened)
        {
            if (panelManager != null && panelManager.TryDeferOpen(panel, () => Open(onOpened))) return true;

            BindServices();
            if (adsService == null || !adsService.CanShowNoAdsOffer ||
                panelManager == null || panel == null)
            {
                return false;
            }

            RefreshPresentation();
            panelManager.OpenRoot(panel);
            onOpened?.Invoke();
            return true;
        }

        public void Close()
        {
            if (panelManager != null && panel != null && IsOpen)
            {
                panelManager.TryClose(panel);
            }
        }

        private void BindButtons()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Close);
                closeButton.onClick.AddListener(Close);
            }

            if (buyButton != null)
            {
                buyButton.onClick.RemoveListener(HandleBuyClicked);
                buyButton.onClick.AddListener(HandleBuyClicked);
            }
        }

        private void UnbindButtons()
        {
            closeButton?.onClick.RemoveListener(Close);
            buyButton?.onClick.RemoveListener(HandleBuyClicked);
        }

        private void BindServices()
        {
            AdsService availableAdsService = AdsService.Instance;
            StoreManager availableStoreManager = StoreManager.Instance;
            if (adsService == availableAdsService && storeManager == availableStoreManager)
            {
                return;
            }

            UnbindServices();
            adsService = availableAdsService;
            storeManager = availableStoreManager;

            if (adsService != null)
            {
                adsService.AdPresentationStateChanged += HandlePresentationStateChanged;
            }

            if (storeManager != null)
            {
                storeManager.StoreReady += HandleStoreStateChanged;
                storeManager.ProductStateChanged += HandleProductStateChanged;
                storeManager.PurchaseSucceeded += HandleProductStateChanged;
                storeManager.RestoreCompleted += HandleRestoreCompleted;
            }
        }

        private void UnbindServices()
        {
            if (adsService != null)
            {
                adsService.AdPresentationStateChanged -= HandlePresentationStateChanged;
                adsService = null;
            }

            if (storeManager != null)
            {
                storeManager.StoreReady -= HandleStoreStateChanged;
                storeManager.ProductStateChanged -= HandleProductStateChanged;
                storeManager.PurchaseSucceeded -= HandleProductStateChanged;
                storeManager.RestoreCompleted -= HandleRestoreCompleted;
                storeManager = null;
            }
        }

        private void HandleBuyClicked()
        {
            BindServices();
            if (storeManager != null && adsService != null &&
                adsService.CanShowNoAdsOffer &&
                storeManager.CanPurchase(StoreProductIds.NoAds))
            {
                storeManager.Purchase(StoreProductIds.NoAds);
            }

            RefreshPresentation();
        }

        private void HandlePresentationStateChanged()
        {
            RefreshPresentation();
        }

        private void HandleStoreStateChanged()
        {
            RefreshPresentation();
        }

        private void HandleProductStateChanged(string productId)
        {
            if (productId == StoreProductIds.NoAds)
            {
                RefreshPresentation();
            }
        }

        private void HandleRestoreCompleted(bool _, string __)
        {
            RefreshPresentation();
        }

        private void RefreshPresentation()
        {
            if (priceText != null)
            {
                string localizedPrice = storeManager?.GetLocalizedPrice(StoreProductIds.NoAds);
                priceText.text = string.IsNullOrWhiteSpace(localizedPrice)
                    ? PricePlaceholder
                    : localizedPrice;
            }

            bool offerAvailable = adsService != null && adsService.CanShowNoAdsOffer;
            if (buyButton != null)
            {
                buyButton.interactable = offerAvailable && storeManager != null &&
                                         storeManager.CanPurchase(StoreProductIds.NoAds);
            }

            if (!offerAvailable && IsOpen)
            {
                Close();
            }
        }
    }
}
