using System;
using DG.Tweening;
using Game.Shared.Ads.Core;
using Game.Shared.Save;
using Game.Shared.UI;
using Game.Shared.UI.Panels;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Game.Shared.Store.UI
{
    [DisallowMultipleComponent]
    public sealed class ShopContentController : MonoBehaviour
    {
        [Header("Gold")]
        [SerializeField] private TMP_Text goldText;
        [SerializeField] private CoinFlyAnimator coinFlyAnimator;
        [SerializeField] private RectTransform coinFlyTarget;
        [SerializeField] private GoldDisplayPresenter coinFlyDisplayPresenter;

        [Header("Cards")]
        [SerializeField] private ShopProductCardController[] productCards =
            Array.Empty<ShopProductCardController>();
        [SerializeField] private ShopProductCardController noAdsCard;

        [Header("Feedback")]
        [SerializeField] private PanelManager panelManager;
        [SerializeField] private GameObject purchaseLoading;
        [FormerlySerializedAs("purchaseLoadingVisual")]
        [SerializeField] private RectTransform purchaseLoadingAnimation;
        [SerializeField] private GameObject purchaseBlocker;
        [SerializeField] private UIPanel purchaseCancelPanel;
        [SerializeField, Min(0.01f)] private float loadingRotationDuration = 1f;
        [SerializeField] private string stringTable = "General";
        [SerializeField] private string purchaseFailedKey = "store_purchase_failed";
        [SerializeField] private string purchaseDeferredKey = "store_purchase_deferred";
        [SerializeField] private string purchaseCancelledKey;

        private StoreManager storeManager;
        private SaveManager saveManager;
        private AdsService adsService;
        private bool goldSubscribed;
        private Tween loadingRotationTween;
        private string loadingProductId = string.Empty;
        private Vector3 loadingVisualBaseEulerAngles;
        private bool hasLoadingVisualBaseEulerAngles;

        private void OnEnable()
        {
            productCards ??= Array.Empty<ShopProductCardController>();
            TryBindSaveManager();
            TryBindStoreManager();
            TryBindAdsService();
            RefreshPurchaseLoading();
            RefreshAll();
        }

        private void OnDisable()
        {
            StopPurchaseLoading();
            coinFlyAnimator?.CancelActiveAnimation();
            UnbindSaveManager();
            UnbindStoreManager();
            UnbindAdsService();
            SetCardStoreManager(null);
        }

        private void OnValidate()
        {
            productCards ??= Array.Empty<ShopProductCardController>();
            ValidateCardBindings();
        }

        public void RefreshAll()
        {
            TryBindSaveManager();
            RefreshGoldDisplay();
            RefreshNoAdsCardVisibility();

            for (int i = 0; i < productCards.Length; i++)
            {
                productCards[i]?.Refresh();
            }

        }

        private void TryBindAdsService()
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
                adsService.AdPresentationStateChanged += HandleAdPresentationStateChanged;
            }
        }

        private void UnbindAdsService()
        {
            if (adsService != null)
            {
                adsService.AdPresentationStateChanged -= HandleAdPresentationStateChanged;
                adsService = null;
            }
        }

        private void HandleAdPresentationStateChanged()
        {
            RefreshAll();
        }

        private void RefreshNoAdsCardVisibility()
        {
            if (noAdsCard == null)
            {
                return;
            }

            bool visible = adsService != null && adsService.CanShowNoAdsOffer;
            if (noAdsCard.gameObject.activeSelf != visible)
            {
                noAdsCard.gameObject.SetActive(visible);
            }
        }

        private void TryBindSaveManager()
        {
            SaveManager available = SaveManager.Instance;
            if (saveManager != null && saveManager == available && goldSubscribed)
            {
                return;
            }

            UnbindSaveManager();
            if (available == null || !available.IsInitialized)
            {
                return;
            }

            saveManager = available;
            saveManager.GoldChanged += HandleGoldChanged;
            goldSubscribed = true;
            RefreshGoldDisplay();
        }

        private void UnbindSaveManager()
        {
            if (saveManager != null && goldSubscribed)
            {
                saveManager.GoldChanged -= HandleGoldChanged;
            }

            goldSubscribed = false;
            saveManager = null;
        }

        private void HandleGoldChanged(int gold)
        {
            SetGoldText(gold);
        }

        private void RefreshGoldDisplay()
        {
            if (saveManager != null && saveManager.IsInitialized)
            {
                SetGoldText(saveManager.Gold);
            }
        }

        private void SetGoldText(int gold)
        {
            if (coinFlyDisplayPresenter != null)
            {
                coinFlyDisplayPresenter.SetAuthoritativeAmount(gold);
                return;
            }

            if (goldText != null)
            {
                goldText.SetText("{0}", Mathf.Max(0, gold));
            }
        }

        private void TryBindStoreManager()
        {
            StoreManager available = StoreManager.Instance;
            if (storeManager == available)
            {
                return;
            }

            UnbindStoreManager();
            storeManager = available;
            SetCardStoreManager(storeManager);

            if (storeManager == null)
            {
                return;
            }

            storeManager.StoreReady += HandleStoreStateChanged;
            storeManager.PurchaseStarted += HandlePurchaseStarted;
            storeManager.PurchaseDeferred += HandlePurchaseDeferred;
            storeManager.UserPurchaseConfirmed += HandleUserPurchaseConfirmed;
            storeManager.PurchaseSucceeded += HandlePurchaseSucceeded;
            storeManager.PurchaseFailed += HandlePurchaseFailed;
            storeManager.PurchaseCancelled += HandlePurchaseCancelled;
            storeManager.ProductStateChanged += HandleProductStateChanged;
            storeManager.RestoreCompleted += HandleRestoreCompleted;
            RefreshAll();
        }

        private void UnbindStoreManager()
        {
            if (storeManager == null)
            {
                return;
            }

            storeManager.StoreReady -= HandleStoreStateChanged;
            storeManager.PurchaseStarted -= HandlePurchaseStarted;
            storeManager.PurchaseDeferred -= HandlePurchaseDeferred;
            storeManager.UserPurchaseConfirmed -= HandleUserPurchaseConfirmed;
            storeManager.PurchaseSucceeded -= HandlePurchaseSucceeded;
            storeManager.PurchaseFailed -= HandlePurchaseFailed;
            storeManager.PurchaseCancelled -= HandlePurchaseCancelled;
            storeManager.ProductStateChanged -= HandleProductStateChanged;
            storeManager.RestoreCompleted -= HandleRestoreCompleted;
            storeManager = null;
        }

        private void SetCardStoreManager(StoreManager manager)
        {
            for (int i = 0; i < productCards.Length; i++)
            {
                productCards[i]?.SetStoreManager(manager);
            }
        }

        private void HandleStoreStateChanged()
        {
            RefreshAll();
        }

        private void HandleProductStateChanged(string _)
        {
            RefreshAll();
        }

        private void HandlePurchaseStarted(string productId)
        {
            if (HasProductCard(productId))
            {
                loadingProductId = productId;
                StartPurchaseLoading();
            }

            RefreshAll();
        }

        private void HandleUserPurchaseConfirmed(string productId)
        {
            StopPurchaseLoadingFor(productId);
        }

        private void HandlePurchaseSucceeded(string productId)
        {
            RefreshAll();

            int coinReward = GetAnimatedCoinReward(productId);
            if (coinReward <= 0 || coinFlyAnimator == null)
            {
                return;
            }

            coinFlyAnimator.Play(
                coinReward,
                GetCoinFlySource(productId),
                coinFlyTarget,
                coinFlyDisplayPresenter,
                playRewardSound: false);
        }

        private void HandlePurchaseDeferred(string productId)
        {
            StopPurchaseLoadingFor(productId);
            RefreshAll();
            ShowLocalizedMessage(purchaseDeferredKey);
        }

        private void HandlePurchaseFailed(StorePurchaseFailure failure)
        {
            StopPurchaseLoadingFor(failure.ProductId);
            RefreshAll();
            OpenPurchaseCancel();
        }

        private void HandlePurchaseCancelled(string productId)
        {
            StopPurchaseLoadingFor(productId);
            RefreshAll();
            OpenPurchaseCancel();
        }

        private void HandleRestoreCompleted(bool _, string __)
        {
            RefreshAll();
        }

        private void ShowLocalizedMessage(string localizationKey)
        {
            if (panelManager == null || string.IsNullOrWhiteSpace(localizationKey))
            {
                return;
            }

            string message = LocalizationSettings.StringDatabase.GetLocalizedString(
                stringTable,
                localizationKey);
            if (!string.IsNullOrWhiteSpace(message))
            {
                panelManager.ShowTemporaryMessage(message);
            }
        }

        private void ValidateCardBindings()
        {
            for (int i = 0; i < productCards.Length; i++)
            {
                ShopProductCardController current = productCards[i];
                if (current == null || string.IsNullOrEmpty(current.ProductId))
                {
                    continue;
                }

                for (int j = i + 1; j < productCards.Length; j++)
                {
                    ShopProductCardController other = productCards[j];
                    if (other != null && current.ProductId == other.ProductId)
                    {
                        Debug.LogWarning(
                            $"Duplicate Shop card product id '{current.ProductId}' on {name}.",
                            this);
                    }
                }
            }
        }

        private RectTransform GetCoinFlySource(string productId)
        {
            for (int i = 0; i < productCards.Length; i++)
            {
                ShopProductCardController card = productCards[i];
                if (card != null && card.ProductId == productId)
                {
                    return card.CoinFlySource;
                }
            }

            return null;
        }

        private bool HasProductCard(string productId)
        {
            for (int i = 0; i < productCards.Length; i++)
            {
                ShopProductCardController card = productCards[i];
                if (card != null && card.ProductId == productId)
                {
                    return true;
                }
            }

            return false;
        }

        private void RefreshPurchaseLoading()
        {
            if (storeManager != null && storeManager.IsPurchasing &&
                HasProductCard(storeManager.ActiveProductId))
            {
                loadingProductId = storeManager.ActiveProductId;
                StartPurchaseLoading();
                return;
            }

            StopPurchaseLoading();
        }

        private void StartPurchaseLoading()
        {
            SetPurchaseBlockerVisible(true);

            // The shared blocker stays behind Panels. The serialized full-screen
            // loading graphic owns pointer blocking while the native purchase is pending.
            if (purchaseLoading != null && purchaseLoading.TryGetComponent(out Graphic loadingGraphic))
            {
                loadingGraphic.raycastTarget = true;
            }

            if (purchaseLoading != null && !purchaseLoading.activeSelf)
            {
                purchaseLoading.SetActive(true);
            }

            purchaseLoading?.transform.SetAsLastSibling();

            if (purchaseLoadingAnimation == null)
            {
                return;
            }

            if (!purchaseLoadingAnimation.gameObject.activeSelf)
            {
                purchaseLoadingAnimation.gameObject.SetActive(true);
            }

            if (!hasLoadingVisualBaseEulerAngles)
            {
                loadingVisualBaseEulerAngles = purchaseLoadingAnimation.localEulerAngles;
                hasLoadingVisualBaseEulerAngles = true;
            }

            loadingRotationTween?.Kill(false);
            purchaseLoadingAnimation.localEulerAngles = loadingVisualBaseEulerAngles;
            loadingRotationTween = purchaseLoadingAnimation
                .DORotate(new Vector3(0f, 0f, -360f), loadingRotationDuration,
                    RotateMode.FastBeyond360)
                .SetRelative(true)
                .SetEase(Ease.Linear)
                .SetLoops(-1)
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
        }

        private void StopPurchaseLoadingFor(string productId)
        {
            if (!string.IsNullOrEmpty(loadingProductId) &&
                loadingProductId != productId)
            {
                return;
            }

            StopPurchaseLoading();
        }

        private void StopPurchaseLoading()
        {
            loadingRotationTween?.Kill(false);
            loadingRotationTween = null;

            if (purchaseLoadingAnimation != null && hasLoadingVisualBaseEulerAngles)
            {
                purchaseLoadingAnimation.localEulerAngles = loadingVisualBaseEulerAngles;
            }

            if (purchaseLoading != null && purchaseLoading.activeSelf)
            {
                purchaseLoading.SetActive(false);
            }

            SetPurchaseBlockerVisible(false);
            loadingProductId = string.Empty;
        }

        private void SetPurchaseBlockerVisible(bool visible)
        {
            if (panelManager != null &&
                (purchaseBlocker == null || panelManager.BackgroundBlocker == purchaseBlocker))
            {
                panelManager.SetExternalInputBlockerVisible(this, visible);
                return;
            }

            if (purchaseBlocker == null)
            {
                return;
            }

            if (purchaseBlocker.activeSelf != visible)
            {
                purchaseBlocker.SetActive(visible);
            }

            if (visible)
            {
                purchaseBlocker.transform.SetAsLastSibling();
                purchaseLoading?.transform.SetAsLastSibling();
            }
        }

        private void OpenPurchaseCancel()
        {
            if (panelManager == null || purchaseCancelPanel == null ||
                panelManager.ContainsPanel(purchaseCancelPanel))
            {
                return;
            }

            if (panelManager.HasOpenPanel)
            {
                purchaseCancelPanel.SetAllowBackgroundDismiss(false);
                panelManager.PushOverlay(purchaseCancelPanel);
            }
            else
            {
                purchaseCancelPanel.SetAllowBackgroundDismiss(false);
                panelManager.OpenRoot(purchaseCancelPanel);
            }
        }

        private static int GetAnimatedCoinReward(string productId)
        {
            if (!StoreCatalog.TryGet(productId, out StoreProductDefinition product) ||
                product.Rewards == null)
            {
                return 0;
            }

            // Starter Pack also contains boosters. Present only its coin portion;
            // reward granting remains entirely owned by the existing store pipeline.
            if (productId == StoreProductIds.StarterPack)
            {
                long coinAmount = 0;
                for (int i = 0; i < product.Rewards.Count; i++)
                {
                    StoreReward item = product.Rewards[i];
                    if (item.Type == StoreRewardType.Coin)
                        coinAmount += Mathf.Max(0, item.Amount);
                }
                return (int)Math.Min(int.MaxValue, coinAmount);
            }

            if (product.Rewards.Count != 1) return 0;

            StoreReward reward = product.Rewards[0];
            return reward.Type == StoreRewardType.Coin
                ? Mathf.Max(0, reward.Amount)
                : 0;
        }
    }
}
