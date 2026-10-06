using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Purchasing;

namespace Game.Shared.Store
{
    public sealed class UnityIapPurchaseService : IStorePurchaseService
    {
        private readonly Dictionary<string, Product> products =
            new Dictionary<string, Product>(StringComparer.Ordinal);
        private readonly List<ProductDefinition> productDefinitions =
            new List<ProductDefinition>();

        private StoreController storeController;
        private bool initializationStarted;
        private bool initializationCompleted;
        private bool initialConnectionAwaitCompleted;
        private bool callbacksBound;
        private bool storeConnected;
        private bool storeRefreshInProgress;
        private bool storeRefreshQueued;
        private bool initialPurchaseFetchInProgress;
        private bool restoreInProgress;
        private bool disposed;

        public bool IsReady { get; private set; }

        public event Action<bool, string> InitializationCompleted;
        public event Action ProductsChanged;
        public event Action<IReadOnlyList<StorePurchase>> InitialEntitlementsFetched;
        public event Action<bool, string> InitialEntitlementSyncCompleted;
        public event Action<StorePurchase> PurchasePending;
        public event Action<string> PurchaseDeferred;
        public event Action<IReadOnlyList<string>> DeferredPurchasesFetched;
        public event Action<IReadOnlyList<StorePurchase>> PendingPurchasesFetched;
        public event Action<StorePurchase> PurchaseConfirmed;
        public event Action<StorePurchase, string> PurchaseConfirmationFailed;
        public event Action<StorePurchaseFailure> PurchaseFailed;
        public event Action<IReadOnlyList<StorePurchase>> PurchasesRestored;
        public event Action<bool, string> RestoreCompleted;
        public event Action<string> StoreDisconnected;

        public async void Initialize(IReadOnlyList<StoreProductDefinition> catalogProducts)
        {
            if (disposed || initializationStarted)
            {
                return;
            }

            initializationStarted = true;

            try
            {
                storeController = UnityIAPServices.StoreController();
                BindCallbacks();
                storeController.ProcessPendingOrdersOnPurchasesFetched(true);
                for (int i = 0; i < catalogProducts.Count; i++)
                {
                    StoreProductDefinition definition = catalogProducts[i];
                    productDefinitions.Add(new ProductDefinition(
                        definition.ProductId,
                        definition.ProductType == StoreProductType.NonConsumable
                            ? ProductType.NonConsumable
                            : ProductType.Consumable));
                }

                await storeController.Connect();

                if (disposed)
                {
                    return;
                }

                initialConnectionAwaitCompleted = true;
                BeginStoreRefresh();
            }
            catch (Exception exception)
            {
                CompleteInitialization(false, exception.Message);
            }
        }

        public bool Purchase(string productId)
        {
            if (!IsReady || storeController == null ||
                !products.TryGetValue(productId, out Product product) ||
                !product.availableToPurchase)
            {
                return false;
            }

            try
            {
                storeController.PurchaseProduct(product);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[Store] Purchase start threw for '{productId}': {exception.Message}");
                return false;
            }
        }

        public bool ConfirmPurchase(StorePurchase purchase)
        {
            if (purchase?.NativeOrder is not PendingOrder pendingOrder || storeController == null)
            {
                return false;
            }

            try
            {
                storeController.ConfirmPurchase(pendingOrder);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[Store] Purchase confirmation threw for '{purchase.ProductId}': {exception.Message}");
                return false;
            }
        }

        public void RestorePurchases()
        {
            if (!IsReady || storeController == null)
            {
                RestoreCompleted?.Invoke(false, "Store is not ready.");
                return;
            }

            if (restoreInProgress)
            {
                RestoreCompleted?.Invoke(false, "A restore operation is already in progress.");
                return;
            }

            restoreInProgress = true;

#if UNITY_IOS || UNITY_STANDALONE_OSX || UNITY_TVOS
            try
            {
                storeController.RestoreTransactions(HandleNativeRestoreCompleted);
            }
            catch (Exception exception)
            {
                CompleteRestore(false, exception.Message);
            }
#else
            FetchPurchasesForRestore();
#endif
        }

        public bool TryGetProduct(string productId, out StoreProductInfo productInfo)
        {
            if (!string.IsNullOrEmpty(productId) &&
                products.TryGetValue(productId, out Product product))
            {
                productInfo = new StoreProductInfo(
                    productId,
                    product.availableToPurchase,
                    product.metadata?.localizedPriceString ?? string.Empty,
                    product.metadata?.localizedTitle ?? string.Empty);
                return true;
            }

            productInfo = default;
            return false;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            IsReady = false;
            UnbindCallbacks();
            storeController = null;
            products.Clear();
            productDefinitions.Clear();
        }

        private void BindCallbacks()
        {
            if (storeController == null || callbacksBound)
            {
                return;
            }

            callbacksBound = true;
            storeController.OnStoreConnected += HandleStoreConnected;
            storeController.OnProductsFetched += HandleProductsFetched;
            storeController.OnProductsFetchFailed += HandleProductsFetchFailed;
            storeController.OnPurchasePending += HandlePurchasePending;
            storeController.OnPurchaseDeferred += HandlePurchaseDeferred;
            storeController.OnPurchaseFailed += HandlePurchaseFailed;
            storeController.OnPurchaseConfirmed += HandlePurchaseConfirmed;
            storeController.OnPurchasesFetched += HandlePurchasesFetched;
            storeController.OnPurchasesFetchFailed += HandlePurchasesFetchFailed;
            storeController.OnStoreDisconnected += HandleStoreDisconnected;
        }

        private void UnbindCallbacks()
        {
            if (storeController == null || !callbacksBound)
            {
                return;
            }

            callbacksBound = false;
            storeController.OnStoreConnected -= HandleStoreConnected;
            storeController.OnProductsFetched -= HandleProductsFetched;
            storeController.OnProductsFetchFailed -= HandleProductsFetchFailed;
            storeController.OnPurchasePending -= HandlePurchasePending;
            storeController.OnPurchaseDeferred -= HandlePurchaseDeferred;
            storeController.OnPurchaseFailed -= HandlePurchaseFailed;
            storeController.OnPurchaseConfirmed -= HandlePurchaseConfirmed;
            storeController.OnPurchasesFetched -= HandlePurchasesFetched;
            storeController.OnPurchasesFetchFailed -= HandlePurchasesFetchFailed;
            storeController.OnStoreDisconnected -= HandleStoreDisconnected;
        }

        private void HandleStoreConnected()
        {
            storeConnected = true;
            if (!initialConnectionAwaitCompleted)
            {
                return;
            }

            if (storeRefreshInProgress)
            {
                storeRefreshQueued = true;
                return;
            }

            BeginStoreRefresh();
        }

        private void BeginStoreRefresh()
        {
            if (disposed || storeController == null || storeRefreshInProgress)
            {
                return;
            }

            storeRefreshQueued = false;
            storeRefreshInProgress = true;
            initializationCompleted = false;
            initialPurchaseFetchInProgress = false;
            IsReady = false;

            try
            {
                storeController.FetchProducts(productDefinitions);
            }
            catch (Exception exception)
            {
                CompleteInitialization(false, exception.Message);
            }
        }

        private void HandleProductsFetched(List<Product> fetchedProducts)
        {
            products.Clear();
            for (int i = 0; i < fetchedProducts.Count; i++)
            {
                Product product = fetchedProducts[i];
                if (product?.definition?.id != null)
                {
                    products[product.definition.id] = product;
                }
            }

            ProductsChanged?.Invoke();

            initialPurchaseFetchInProgress = true;
            try
            {
                storeController.FetchPurchases();
            }
            catch (Exception exception)
            {
                CompleteInitialEntitlementSync(false, exception.Message);
            }
        }

        private void HandleProductsFetchFailed(ProductFetchFailed failure)
        {
            CompleteInitialization(false, failure?.FailureReason.ToString() ?? "Product fetch failed.");
        }

        private void HandlePurchasePending(PendingOrder order)
        {
            if (TryConvertOrder(order, out StorePurchase purchase))
            {
                PurchasePending?.Invoke(purchase);
                return;
            }

            PurchaseFailed?.Invoke(new StorePurchaseFailure(
                GetProductId(order),
                StorePurchaseFailureReason.InvalidTransaction,
                "The store returned a purchase without a valid product or transaction ID."));
        }

        private void HandlePurchaseDeferred(DeferredOrder order)
        {
            PurchaseDeferred?.Invoke(GetProductId(order));
        }

        private void HandlePurchaseFailed(FailedOrder order)
        {
            StorePurchaseFailureReason reason =
                order.FailureReason == PurchaseFailureReason.UserCancelled ||
                order.FailureReason == PurchaseFailureReason.OrderCancelled
                    ? StorePurchaseFailureReason.UserCancelled
                    : StorePurchaseFailureReason.StoreError;

            PurchaseFailed?.Invoke(new StorePurchaseFailure(
                GetProductId(order),
                reason,
                $"{order.FailureReason}: {order.Details}"));
        }

        private void HandlePurchaseConfirmed(Order order)
        {
            if (order is FailedOrder failedOrder)
            {
                TryConvertOrder(failedOrder, out StorePurchase failedPurchase);
                PurchaseConfirmationFailed?.Invoke(
                    failedPurchase,
                    $"{failedOrder.FailureReason}: {failedOrder.Details}");
                return;
            }

            if (TryConvertOrder(order, out StorePurchase purchase))
            {
                PurchaseConfirmed?.Invoke(purchase);
                return;
            }

            PurchaseConfirmationFailed?.Invoke(
                null,
                "The store returned a confirmation callback without a valid transaction ID.");
        }

        private void HandlePurchasesFetched(Orders orders)
        {
            List<StorePurchase> restoredPurchases = new List<StorePurchase>();
            List<StorePurchase> pendingPurchases = new List<StorePurchase>();
            List<string> deferredProductIds = new List<string>();
            if (orders?.ConfirmedOrders != null)
            {
                for (int i = 0; i < orders.ConfirmedOrders.Count; i++)
                {
                    if (TryConvertOrder(orders.ConfirmedOrders[i], out StorePurchase purchase))
                    {
                        restoredPurchases.Add(purchase);
                    }
                }
            }

            if (orders?.PendingOrders != null)
            {
                for (int i = 0; i < orders.PendingOrders.Count; i++)
                {
                    if (TryConvertOrder(
                            orders.PendingOrders[i],
                            out StorePurchase pendingPurchase))
                    {
                        pendingPurchases.Add(pendingPurchase);
                    }
                }
            }

            if (orders?.DeferredOrders != null)
            {
                HashSet<string> uniqueDeferredProducts =
                    new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < orders.DeferredOrders.Count; i++)
                {
                    string productId = GetProductId(orders.DeferredOrders[i]);
                    if (productId.Length > 0 && uniqueDeferredProducts.Add(productId))
                    {
                        deferredProductIds.Add(productId);
                        PurchaseDeferred?.Invoke(productId);
                    }
                }
            }

            DeferredPurchasesFetched?.Invoke(deferredProductIds);
            PendingPurchasesFetched?.Invoke(pendingPurchases);

            if (initialPurchaseFetchInProgress)
            {
                InitialEntitlementsFetched?.Invoke(restoredPurchases);
                CompleteInitialEntitlementSync(true, string.Empty);
                return;
            }

            if (restoreInProgress)
            {
                PurchasesRestored?.Invoke(restoredPurchases);
                CompleteRestore(true, string.Empty);
            }
        }

        private void HandlePurchasesFetchFailed(PurchasesFetchFailureDescription failure)
        {
            if (initialPurchaseFetchInProgress)
            {
                CompleteInitialEntitlementSync(
                    false,
                    failure?.ToString() ?? "Initial entitlement fetch failed.");
                return;
            }

            if (restoreInProgress)
            {
                CompleteRestore(false, failure?.ToString() ?? "Purchase restore failed.");
            }
        }

        private void HandleStoreDisconnected(StoreConnectionFailureDescription failure)
        {
            storeConnected = false;
            storeRefreshQueued = true;
            IsReady = false;
            string message = failure?.ToString() ?? "Store disconnected.";
            if (restoreInProgress)
            {
                CompleteRestore(false, message);
            }
            StoreDisconnected?.Invoke(message);
        }

        private void HandleNativeRestoreCompleted(bool success, string error)
        {
            if (!success)
            {
                CompleteRestore(false, error ?? "Native restore failed.");
                return;
            }

            FetchPurchasesForRestore();
        }

        private void FetchPurchasesForRestore()
        {
            try
            {
                storeController.FetchPurchases();
            }
            catch (Exception exception)
            {
                CompleteRestore(false, exception.Message);
            }
        }

        private void CompleteInitialization(bool success, string error)
        {
            if (initializationCompleted)
            {
                return;
            }

            storeRefreshInProgress = false;
            if (storeRefreshQueued)
            {
                if (storeConnected)
                {
                    BeginStoreRefresh();
                }

                return;
            }

            initializationCompleted = true;
            IsReady = success && storeConnected;
            InitializationCompleted?.Invoke(success, error ?? string.Empty);
        }

        private void CompleteInitialEntitlementSync(bool success, string error)
        {
            if (!initialPurchaseFetchInProgress)
            {
                return;
            }

            initialPurchaseFetchInProgress = false;
            InitialEntitlementSyncCompleted?.Invoke(success, error ?? string.Empty);

            // Product metadata remains usable even when the entitlement lookup failed. StoreManager
            // exposes that distinction through its entitlement sync state.
            CompleteInitialization(true, string.Empty);
        }

        private void CompleteRestore(bool success, string error)
        {
            restoreInProgress = false;
            RestoreCompleted?.Invoke(success, error ?? string.Empty);
        }

        private static bool TryConvertOrder(Order order, out StorePurchase purchase)
        {
            Product product = GetProduct(order);
            string productId = product?.definition?.id ?? string.Empty;
            string transactionId = order?.Info?.TransactionID ?? string.Empty;
            if (productId.Length == 0 || transactionId.Length == 0)
            {
                purchase = null;
                return false;
            }

            purchase = new StorePurchase(
                productId,
                transactionId,
                order.Info.Receipt,
                (double)(product.metadata?.localizedPrice ?? 0m),
                product.metadata?.isoCurrencyCode ?? string.Empty,
                order);
            return true;
        }

        private static string GetProductId(Order order)
        {
            return GetProduct(order)?.definition?.id ?? string.Empty;
        }

        private static Product GetProduct(Order order)
        {
            IReadOnlyList<CartItem> items = order?.CartOrdered?.Items();
            return items != null && items.Count > 0
                ? items[0]?.Product
                : null;
        }
    }
}
