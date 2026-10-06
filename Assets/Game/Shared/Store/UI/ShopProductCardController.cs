using TMPro;
using UnityEngine;
using Game.Shared.UI;

namespace Game.Shared.Store.UI
{
    [DisallowMultipleComponent]
    public sealed class ShopProductCardController : MonoBehaviour
    {
        private const string PricePlaceholder = "\u2014";

        [Header("Product")]
        [SerializeField] private string productId;

        [Header("Binding")]
        [SerializeField] private TweenButton purchaseButton;
        [SerializeField] private TMP_Text priceText;
        [SerializeField] private RectTransform coinFlySource;

        [Header("Optional States")]
        [SerializeField] private bool hideWhenOwned;
        [SerializeField] private GameObject purchasedState;
        [SerializeField] private GameObject pendingState;
        [SerializeField] private GameObject loadingState;
        [SerializeField] private GameObject deferredState;
        [SerializeField] private GameObject resolvingState;
        [SerializeField] private GameObject unavailableState;

        private StoreManager storeManager;

        public string ProductId => productId;
        public RectTransform CoinFlySource => coinFlySource;
        public StoreProductUiState CurrentState { get; private set; } =
            StoreProductUiState.Loading;

        private void OnEnable()
        {
            RegisterButton();
            Refresh();
        }

        private void OnDisable()
        {
            UnregisterButton();
        }

        private void OnValidate()
        {
            productId = productId?.Trim();
            if (!string.IsNullOrEmpty(productId) && !IsMainShopProduct(productId))
            {
                Debug.LogWarning(
                    $"{nameof(ShopProductCardController)} on '{name}' has unsupported main Shop product id '{productId}'.",
                    this);
            }
        }

        internal void SetStoreManager(StoreManager manager)
        {
            storeManager = manager;
            RegisterButton();
            Refresh();
        }

        public void Refresh()
        {
            CurrentState = storeManager != null
                ? storeManager.GetProductUiState(productId)
                : StoreProductUiState.Loading;

            bool isOwned = CurrentState == StoreProductUiState.Owned;
            bool isLoading = CurrentState == StoreProductUiState.Loading;
            bool isPurchasing = CurrentState == StoreProductUiState.Purchasing;
            bool isDeferred = CurrentState == StoreProductUiState.Deferred;
            bool isResolving = CurrentState == StoreProductUiState.Resolving;
            bool isUnavailable = CurrentState == StoreProductUiState.Unavailable;

            if (hideWhenOwned && storeManager != null)
            {
                bool visible = !isOwned;
                if (gameObject.activeSelf != visible)
                {
                    gameObject.SetActive(visible);
                }

                if (!visible)
                {
                    return;
                }
            }

            SetActive(purchasedState, isOwned);
            SetActive(pendingState, isLoading || isPurchasing || isDeferred || isResolving);
            SetActive(loadingState, isLoading);
            SetActive(deferredState, isDeferred);
            SetActive(resolvingState, isResolving);
            SetActive(unavailableState, isUnavailable);

            if (priceText != null)
            {
                string localizedPrice = storeManager?.GetLocalizedPrice(productId);
                priceText.text = string.IsNullOrWhiteSpace(localizedPrice)
                    ? PricePlaceholder
                    : localizedPrice;
            }

            if (purchaseButton != null)
            {
                purchaseButton.interactable =
                    CurrentState == StoreProductUiState.Available &&
                    storeManager != null &&
                    storeManager.CanPurchase(productId);
            }
        }

        private void RegisterButton()
        {
            if (purchaseButton == null)
            {
                return;
            }

            purchaseButton.onClick.RemoveListener(HandlePurchaseClicked);
            purchaseButton.onClick.AddListener(HandlePurchaseClicked);
        }

        private void UnregisterButton()
        {
            if (purchaseButton != null)
            {
                purchaseButton.onClick.RemoveListener(HandlePurchaseClicked);
            }
        }

        private void HandlePurchaseClicked()
        {
            if (storeManager == null || !storeManager.CanPurchase(productId))
            {
                Refresh();
                return;
            }

            storeManager.Purchase(productId);
            Refresh();
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }

        private static bool IsMainShopProduct(string id)
        {
            return id == StoreProductIds.StarterPack ||
                   id == StoreProductIds.NoAds ||
                   id == StoreProductIds.Coins1000 ||
                   id == StoreProductIds.Coins5000 ||
                   id == StoreProductIds.Coins10000 ||
                   id == StoreProductIds.Coins25000 ||
                   id == StoreProductIds.Coins50000 ||
                   id == StoreProductIds.Coins100000;
        }
    }
}
