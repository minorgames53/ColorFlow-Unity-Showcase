using System;
using System.Collections;
using System.Collections.Generic;
using Game.Shared.Ads.Core;
using Game.Shared.Analytics;
using Game.Shared.Analytics.Core;
using Game.Shared.Audio;
using Game.Shared.Config;
using Game.Shared.Lives;
using Game.Shared.Save;
using Game.Menu;
using Gameplay.Analytics;
using UnityEngine;

namespace Game.Shared.Store
{
    [DisallowMultipleComponent]
    public sealed class StoreManager : MonoBehaviour
    {
        private const float InitialRetryDelaySeconds = 5f;
        private const float MaxRetryDelaySeconds = 60f;

        private SaveManager saveManager;
        private LivesService livesService;
        private IStorePurchaseService purchaseService;
        private PurchaseProcessor purchaseProcessor;

        private string activeProductId = string.Empty;
        private StoreRewardContext activeRewardContext;
        private LevelIapAnalyticsContext activeIapAnalyticsContext;
        private string activeTransactionId = string.Empty;
        private readonly Dictionary<string, StorePurchase> confirmationsPending =
            new Dictionary<string, StorePurchase>(StringComparer.Ordinal);
        private readonly HashSet<string> confirmationsToPublish =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> userInitiatedConfirmations =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> confirmationsRetryEligible =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> gameplayFulfillmentsPending =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, StorePurchase> fulfillmentPurchasesPending =
            new Dictionary<string, StorePurchase>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> persistedUnconfirmedTransactions =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> deferredProducts =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, StoreRewardContext> deferredRewardContexts =
            new Dictionary<string, StoreRewardContext>(StringComparer.Ordinal);
        private readonly Dictionary<string, LevelIapAnalyticsContext>
            deferredIapAnalyticsContexts =
            new Dictionary<string, LevelIapAnalyticsContext>(StringComparer.Ordinal);
        private bool isPurchasing;
        private bool initialized;
        private bool startupEntitlementApplySucceeded = true;
        private string startupEntitlementApplyError = string.Empty;
        private bool restoreEntitlementApplySucceeded = true;
        private string restoreEntitlementApplyError = string.Empty;
        private bool retryingPendingStoreWork;
        private bool consumingFailOfferRecoveryContinue;
        private bool restoreInProgress;
        private bool lastRestoreFoundRestorablePurchase;
        private Coroutine pendingStoreRetryCoroutine;
        private float pendingStoreRetryDelaySeconds = InitialRetryDelaySeconds;

        private readonly struct LevelIapAnalyticsContext
        {
            public LevelIapAnalyticsContext(int displayedLevelNumber, int internalLevelNumber)
            {
                DisplayedLevelNumber = displayedLevelNumber;
                InternalLevelNumber = internalLevelNumber;
            }

            public int DisplayedLevelNumber { get; }
            public int InternalLevelNumber { get; }
            public bool IsValid => DisplayedLevelNumber > 0 && InternalLevelNumber > 0;
        }

        public static StoreManager Instance { get; private set; }

        public StoreInitializationState InitializationState { get; private set; } =
            StoreInitializationState.NotInitialized;
        public StoreEntitlementSyncState EntitlementSyncState { get; private set; } =
            StoreEntitlementSyncState.NotStarted;
        public StorePurchaseLifecycleState PurchaseState { get; private set; } =
            StorePurchaseLifecycleState.Idle;
        public bool IsReady =>
            InitializationState == StoreInitializationState.Ready &&
            purchaseService != null && purchaseService.IsReady;
        public bool IsPurchasing => isPurchasing;
        public bool IsRestoringPurchases => restoreInProgress;
        public bool LastRestoreFoundRestorablePurchase =>
            lastRestoreFoundRestorablePurchase;
        public string ActiveProductId => activeProductId;
        public int PendingFailOfferRecoveryContinues =>
            saveManager?.PendingFailOfferContinueCredits ?? 0;
        public bool CanPurchaseStarterPack => CanPurchase(StoreProductIds.StarterPack);

        public event Action StoreReady;
        public event Action<StoreEntitlementSyncState> EntitlementSyncStateChanged;
        public event Action<string> PurchaseStarted;
        public event Action<string> PurchaseDeferred;
        // Raised only for a user-initiated transaction after it is confirmed by
        // the store. UI uses this to end its purchase presentation before the
        // shared success event publishes rewards.
        public event Action<string> UserPurchaseConfirmed;
        public event Action<string> PurchaseSucceeded;
        public event Action<StorePurchaseFailure> PurchaseFailed;
        public event Action<string> PurchaseCancelled;
        public event Action<string> ProductStateChanged;
        public event Action<bool, string> RestoreCompleted;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            UnbindPurchaseService();
            purchaseService?.Dispose();
            purchaseService = null;

            if (saveManager != null)
            {
                saveManager.StoreStateChanged -= HandleSavedStoreStateChanged;
            }

            Instance = null;
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (!pauseStatus)
            {
                foreach (string transactionId in confirmationsPending.Keys)
                {
                    confirmationsRetryEligible.Add(transactionId);
                }

                RetryPendingStoreWork();
            }
        }

        public void Initialize(
            SaveManager runtimeSaveManager,
            LivesService runtimeLivesService,
            AdsService runtimeAdsService)
        {
            if (initialized || InitializationState == StoreInitializationState.Initializing)
            {
                return;
            }

            saveManager = runtimeSaveManager ??
                throw new ArgumentNullException(nameof(runtimeSaveManager));
            livesService = runtimeLivesService ??
                throw new ArgumentNullException(nameof(runtimeLivesService));
            if (runtimeAdsService == null)
            {
                Debug.LogWarning(
                    "[Store] AdsService is unavailable. No Ads entitlement will still be saved, " +
                    "but runtime interstitial suppression cannot be verified.",
                    this);
            }

            StoreRewardProcessor rewardProcessor =
                new StoreRewardProcessor(saveManager, livesService);
            purchaseProcessor = new PurchaseProcessor(saveManager, rewardProcessor);

            if (!FeatureConfig.ExternalServicesEnabled)
            {
                // Keep local rewards and saved entitlements available without opening a store connection.
                saveManager.StoreStateChanged += HandleSavedStoreStateChanged;
                initialized = true;
                InitializationState = StoreInitializationState.Failed;
                return;
            }

            purchaseService = new UnityIapPurchaseService();
            HydratePersistedUnconfirmedTransactions();

            saveManager.StoreStateChanged += HandleSavedStoreStateChanged;
            BindPurchaseService();

            initialized = true;
            InitializationState = StoreInitializationState.Initializing;
            SetEntitlementSyncState(StoreEntitlementSyncState.Syncing);
            purchaseService.Initialize(StoreCatalog.Products);
        }

        public bool CanPurchase(string productId)
        {
            if (!IsReady || isPurchasing || restoreInProgress ||
                IsProductResolutionPending(productId) ||
                !StoreCatalog.TryGet(productId, out StoreProductDefinition product) ||
                !purchaseService.TryGetProduct(productId, out StoreProductInfo storeProduct) ||
                !storeProduct.AvailableToPurchase)
            {
                return false;
            }

            if (product.LocallyOneTime && saveManager.StarterPackPurchased)
            {
                return false;
            }

            if (productId == StoreProductIds.NoAds &&
                (saveManager.HasNoAds ||
                 EntitlementSyncState != StoreEntitlementSyncState.Succeeded))
            {
                return false;
            }

            return productId != StoreProductIds.FailOffer ||
                   StoreGameplayActionRegistry.TryCaptureFailedSession(out _);
        }

        public bool Purchase(string productId)
        {
            if (!StoreCatalog.TryGet(productId, out StoreProductDefinition product))
            {
                RejectPurchase(
                    productId,
                    StorePurchaseFailureReason.ProductUnavailable,
                    "Product is not present in the store catalog.");
                return false;
            }

            if (!IsReady)
            {
                RejectPurchase(
                    productId,
                    StorePurchaseFailureReason.StoreUnavailable,
                    "Store is not ready.");
                return false;
            }

            if (isPurchasing)
            {
                RejectPurchase(
                    productId,
                    StorePurchaseFailureReason.PurchaseAlreadyInProgress,
                    "Another purchase is already in progress.");
                return false;
            }

            if (restoreInProgress)
            {
                RejectPurchase(
                    productId,
                    StorePurchaseFailureReason.PurchaseAlreadyInProgress,
                    "An entitlement restore is already in progress.");
                return false;
            }

            if (IsProductResolutionPending(productId))
            {
                RejectPurchase(
                    productId,
                    StorePurchaseFailureReason.PurchaseAlreadyInProgress,
                    "This product still has a deferred or unresolved fulfillment.");
                return false;
            }

            if (product.LocallyOneTime && saveManager.StarterPackPurchased)
            {
                RejectPurchase(
                    productId,
                    StorePurchaseFailureReason.NotEligible,
                    "Starter Pack has already been purchased on this local save.");
                return false;
            }

            if (productId == StoreProductIds.NoAds && saveManager.HasNoAds)
            {
                RejectPurchase(
                    productId,
                    StorePurchaseFailureReason.NotEligible,
                    "No Ads is already owned.");
                return false;
            }

            if (productId == StoreProductIds.NoAds &&
                EntitlementSyncState != StoreEntitlementSyncState.Succeeded)
            {
                RejectPurchase(
                    productId,
                    StorePurchaseFailureReason.StoreUnavailable,
                    "No Ads ownership has not been reconciled safely yet.");
                return false;
            }

            StoreRewardContext context = default;
            if (productId == StoreProductIds.FailOffer)
            {
                if (!StoreGameplayActionRegistry.TryCaptureFailedSession(
                        out string sessionToken))
                {
                    RejectPurchase(
                        productId,
                        StorePurchaseFailureReason.InvalidFailSession,
                        "Fail Offer requires an active failed-level session.");
                    return false;
                }

                context = new StoreRewardContext(true, sessionToken);
            }

            if (!purchaseService.TryGetProduct(productId, out StoreProductInfo storeProduct) ||
                !storeProduct.AvailableToPurchase)
            {
                RejectPurchase(
                    productId,
                    StorePurchaseFailureReason.ProductUnavailable,
                    "Product metadata is unavailable or the product cannot be purchased.");
                return false;
            }

            isPurchasing = true;
            activeProductId = productId;
            activeTransactionId = string.Empty;
            activeRewardContext = context;
            activeIapAnalyticsContext = CaptureLevelIapAnalyticsContext(
                out object iapAnalyticsContextOwner);
            PersistPendingIapAnalyticsIntent(productId, activeIapAnalyticsContext);
            SynchronizePurchaseStartAnalyticsContext(
                activeIapAnalyticsContext,
                iapAnalyticsContextOwner);
            PurchaseState = StorePurchaseLifecycleState.Purchasing;
            PurchaseStarted?.Invoke(productId);
            ProductStateChanged?.Invoke(productId);

            if (purchaseService.Purchase(productId))
            {
                return true;
            }

            if (isPurchasing && activeProductId == productId)
            {
                ClearPendingIapAnalyticsIntent(productId);
                ClearActivePurchase();
                RejectPurchase(
                    productId,
                    StorePurchaseFailureReason.StoreError,
                    "The store rejected the purchase start request.");
            }

            return false;
        }

        public void RestorePurchases()
        {
            if (!IsReady)
            {
                RestoreCompleted?.Invoke(false, "Store is not ready.");
                return;
            }

            if (isPurchasing || restoreInProgress)
            {
                RestoreCompleted?.Invoke(
                    false,
                    "A purchase or restore operation is already in progress.");
                return;
            }

            restoreEntitlementApplySucceeded = true;
            restoreEntitlementApplyError = string.Empty;
            lastRestoreFoundRestorablePurchase = false;
            restoreInProgress = true;
            purchaseService.RestorePurchases();
        }

        public bool IsProductDeferred(string productId)
        {
            return !string.IsNullOrEmpty(productId) && deferredProducts.Contains(productId);
        }

        public bool IsProductAwaitingResolution(string productId)
        {
            return IsProductResolutionPending(productId);
        }

        public bool IsFailOfferSessionLocked(string sessionToken)
        {
            return isPurchasing && activeProductId == StoreProductIds.FailOffer &&
                   activeRewardContext.HasFailedSession &&
                   string.Equals(
                       activeRewardContext.FailedSessionToken,
                       sessionToken,
                       StringComparison.Ordinal);
        }

        public bool RetryPendingConfirmations()
        {
            if (purchaseService == null || !purchaseService.IsReady ||
                confirmationsRetryEligible.Count == 0)
            {
                return false;
            }

            bool startedAny = false;
            List<string> transactionIds =
                new List<string>(confirmationsRetryEligible);
            for (int i = 0; i < transactionIds.Count; i++)
            {
                string transactionId = transactionIds[i];
                if (!confirmationsPending.TryGetValue(
                        transactionId,
                        out StorePurchase purchase))
                {
                    confirmationsRetryEligible.Remove(transactionId);
                    continue;
                }

                confirmationsRetryEligible.Remove(transactionId);
                if (purchaseService.ConfirmPurchase(purchase))
                {
                    startedAny = true;
                }
                else
                {
                    confirmationsRetryEligible.Add(transactionId);
                }
            }

            RefreshPurchaseState();
            return startedAny;
        }

        public bool RetryPendingFulfillments()
        {
            if (purchaseService == null || !purchaseService.IsReady ||
                fulfillmentPurchasesPending.Count == 0)
            {
                return false;
            }

            List<StorePurchase> purchases =
                new List<StorePurchase>(fulfillmentPurchasesPending.Values);
            for (int i = 0; i < purchases.Count; i++)
            {
                HandlePurchasePending(purchases[i]);
            }

            return true;
        }

        public bool TryConsumeFailOfferRecoveryContinue(string failedSessionToken)
        {
            if (consumingFailOfferRecoveryContinue || saveManager == null ||
                saveManager.PendingFailOfferContinueCredits <= 0 ||
                !StoreGameplayActionRegistry.CanContinueFailedSession(
                    failedSessionToken))
            {
                return false;
            }

            consumingFailOfferRecoveryContinue = true;
            try
            {
                string saveSnapshot = saveManager.CaptureRuntimeSnapshot();
                bool wasDirty = saveManager.IsDirty;
                if (!saveManager.TryReserveFailOfferRecoveryContinueCredit() ||
                    !saveManager.TrySave())
                {
                    saveManager.RestoreRuntimeSnapshot(saveSnapshot, wasDirty);
                    Debug.LogWarning(
                        "[Store] Recovery Continue could not be reserved durably; the fail session was left unchanged.");
                    return false;
                }

                if (!StoreGameplayActionRegistry.TryContinueFailedSession(
                        failedSessionToken))
                {
                    saveManager.ReleaseReservedFailOfferRecoveryContinueCredit();
                    if (!saveManager.TrySave())
                    {
                        Debug.LogWarning(
                            "[Store] Recovery Continue reservation was released in memory, but the release save remains dirty for retry.");
                    }

                    ProductStateChanged?.Invoke(StoreProductIds.FailOffer);
                    return false;
                }

                saveManager.CompleteReservedFailOfferRecoveryContinueCredit();
                if (!saveManager.TrySave())
                {
                    // Do not restore the credit after gameplay continuation. The consumed runtime
                    // state stays dirty and SaveManager retries it on pause/quit. If the process
                    // dies first, the durable reservation is recovered to pending during next load.
                    Debug.LogWarning(
                        "[Store] Recovery Continue was applied; its completion save remains dirty for retry.");
                }

                ProductStateChanged?.Invoke(StoreProductIds.FailOffer);
                return true;
            }
            finally
            {
                consumingFailOfferRecoveryContinue = false;
            }
        }

        public bool TryGetProduct(string productId, out StoreProductInfo product)
        {
            if (purchaseService != null &&
                purchaseService.TryGetProduct(productId, out product))
            {
                return true;
            }

            if (StoreCatalog.TryGet(productId, out StoreProductDefinition definition))
            {
                product = new StoreProductInfo(
                    productId,
                    false,
                    string.Empty,
                    definition.ConfigName);
                return true;
            }

            product = default;
            return false;
        }

        public string GetLocalizedPrice(string productId)
        {
            return TryGetProduct(productId, out StoreProductInfo product)
                ? product.LocalizedPrice
                : string.Empty;
        }

        public string GetLocalizedTitle(string productId)
        {
            return TryGetProduct(productId, out StoreProductInfo product)
                ? product.LocalizedTitle
                : string.Empty;
        }

        public StoreProductUiState GetProductUiState(string productId)
        {
            if (!StoreCatalog.TryGet(productId, out StoreProductDefinition product))
            {
                return StoreProductUiState.Unavailable;
            }

            if (IsProductDeferred(productId))
            {
                return StoreProductUiState.Deferred;
            }

            if (IsProductResolutionPending(productId))
            {
                return StoreProductUiState.Resolving;
            }

            if (isPurchasing &&
                string.Equals(activeProductId, productId, StringComparison.Ordinal))
            {
                return StoreProductUiState.Purchasing;
            }

            if (product.LocallyOneTime && saveManager != null &&
                saveManager.StarterPackPurchased)
            {
                return StoreProductUiState.Owned;
            }

            if (productId == StoreProductIds.NoAds && saveManager != null &&
                saveManager.HasNoAds)
            {
                return StoreProductUiState.Owned;
            }

            if (!IsReady)
            {
                return InitializationState == StoreInitializationState.Failed
                    ? StoreProductUiState.Unavailable
                    : StoreProductUiState.Loading;
            }

            if (productId == StoreProductIds.NoAds &&
                EntitlementSyncState != StoreEntitlementSyncState.Succeeded)
            {
                return StoreProductUiState.Loading;
            }

            return purchaseService.TryGetProduct(productId, out StoreProductInfo info) &&
                   info.AvailableToPurchase
                ? StoreProductUiState.Available
                : StoreProductUiState.Unavailable;
        }

        private void BindPurchaseService()
        {
            purchaseService.InitializationCompleted += HandleInitializationCompleted;
            purchaseService.ProductsChanged += HandleProductsChanged;
            purchaseService.InitialEntitlementsFetched += HandleInitialEntitlementsFetched;
            purchaseService.InitialEntitlementSyncCompleted +=
                HandleInitialEntitlementSyncCompleted;
            purchaseService.PurchasePending += HandlePurchasePending;
            purchaseService.PurchaseDeferred += HandlePurchaseDeferred;
            purchaseService.DeferredPurchasesFetched += HandleDeferredPurchasesFetched;
            purchaseService.PendingPurchasesFetched += HandlePendingPurchasesFetched;
            purchaseService.PurchaseConfirmed += HandlePurchaseConfirmed;
            purchaseService.PurchaseConfirmationFailed +=
                HandlePurchaseConfirmationFailed;
            purchaseService.PurchaseFailed += HandlePurchaseFailed;
            purchaseService.PurchasesRestored += HandlePurchasesRestored;
            purchaseService.RestoreCompleted += HandleRestoreCompleted;
            purchaseService.StoreDisconnected += HandleStoreDisconnected;
        }

        private void UnbindPurchaseService()
        {
            if (purchaseService == null)
            {
                return;
            }

            purchaseService.InitializationCompleted -= HandleInitializationCompleted;
            purchaseService.ProductsChanged -= HandleProductsChanged;
            purchaseService.InitialEntitlementsFetched -= HandleInitialEntitlementsFetched;
            purchaseService.InitialEntitlementSyncCompleted -=
                HandleInitialEntitlementSyncCompleted;
            purchaseService.PurchasePending -= HandlePurchasePending;
            purchaseService.PurchaseDeferred -= HandlePurchaseDeferred;
            purchaseService.DeferredPurchasesFetched -= HandleDeferredPurchasesFetched;
            purchaseService.PendingPurchasesFetched -= HandlePendingPurchasesFetched;
            purchaseService.PurchaseConfirmed -= HandlePurchaseConfirmed;
            purchaseService.PurchaseConfirmationFailed -=
                HandlePurchaseConfirmationFailed;
            purchaseService.PurchaseFailed -= HandlePurchaseFailed;
            purchaseService.PurchasesRestored -= HandlePurchasesRestored;
            purchaseService.RestoreCompleted -= HandleRestoreCompleted;
            purchaseService.StoreDisconnected -= HandleStoreDisconnected;
        }

        private void HandleInitializationCompleted(bool success, string error)
        {
            InitializationState = success
                ? StoreInitializationState.Ready
                : StoreInitializationState.Failed;

            if (success)
            {
                if (EntitlementSyncState == StoreEntitlementSyncState.Syncing)
                {
                    SetEntitlementSyncState(StoreEntitlementSyncState.Failed);
                    startupEntitlementApplyError =
                        "Store initialization completed without an entitlement sync result.";
                }

                RetryPendingStoreWork();
                FlushConfirmedIapAnalyticsTransactions();
                StoreReady?.Invoke();
                return;
            }

            SetEntitlementSyncState(StoreEntitlementSyncState.Failed);
            Debug.LogWarning($"[Store] Initialization failed. IAP is unavailable: {error}");
        }

        private void HandleProductsChanged()
        {
            IReadOnlyList<StoreProductDefinition> products = StoreCatalog.Products;
            for (int i = 0; i < products.Count; i++)
            {
                ProductStateChanged?.Invoke(products[i].ProductId);
            }

            if (purchaseService.IsReady)
            {
                RetryPendingStoreWork();
            }
        }

        private void HandleInitialEntitlementsFetched(
            IReadOnlyList<StorePurchase> purchases)
        {
            startupEntitlementApplySucceeded = true;
            startupEntitlementApplyError = string.Empty;

            RecoverPersistedConfirmedIapAnalytics(purchases);

            if (ContainsProduct(purchases, StoreProductIds.NoAds) &&
                !purchaseProcessor.ApplyRestoredNoAdsEntitlement())
            {
                startupEntitlementApplySucceeded = false;
                startupEntitlementApplyError =
                    "No Ads ownership was found, but it could not be persisted locally.";
            }

            ProductStateChanged?.Invoke(StoreProductIds.NoAds);
        }

        private void HandleInitialEntitlementSyncCompleted(bool success, string error)
        {
            bool reconciliationSucceeded = success && startupEntitlementApplySucceeded;
            SetEntitlementSyncState(
                reconciliationSucceeded
                    ? StoreEntitlementSyncState.Succeeded
                    : StoreEntitlementSyncState.Failed);

            if (!reconciliationSucceeded)
            {
                string message = !startupEntitlementApplySucceeded
                    ? startupEntitlementApplyError
                    : error;
                Debug.LogWarning(
                    $"[Store] Initial entitlement reconciliation failed. Products remain usable: {message}");
            }
        }

        private void HandlePurchasePending(StorePurchase purchase)
        {
            if (!StoreCatalog.TryGet(purchase.ProductId, out StoreProductDefinition product))
            {
                HandlePurchaseFailed(new StorePurchaseFailure(
                    purchase.ProductId,
                    StorePurchaseFailureReason.ProductUnavailable,
                    "Pending transaction references an unknown product."));
                return;
            }

            if (!isPurchasing && deferredRewardContexts.TryGetValue(
                    purchase.ProductId,
                    out StoreRewardContext resumedDeferredContext))
            {
                isPurchasing = true;
                activeProductId = purchase.ProductId;
                activeRewardContext = resumedDeferredContext;
                deferredIapAnalyticsContexts.TryGetValue(
                    purchase.ProductId,
                    out activeIapAnalyticsContext);
            }

            bool belongsToActivePurchase =
                isPurchasing && activeProductId == purchase.ProductId;
            if (belongsToActivePurchase)
            {
                userInitiatedConfirmations.Add(purchase.TransactionId);
            }

            StoreRewardContext context = belongsToActivePurchase
                ? activeRewardContext
                : deferredRewardContexts.TryGetValue(
                    purchase.ProductId,
                    out StoreRewardContext deferredContext)
                    ? deferredContext
                    : default;

            LevelIapAnalyticsContext iapAnalyticsContext = belongsToActivePurchase
                ? activeIapAnalyticsContext
                : deferredIapAnalyticsContexts.TryGetValue(
                    purchase.ProductId,
                    out LevelIapAnalyticsContext deferredIapAnalyticsContext)
                    ? deferredIapAnalyticsContext
                    : default;
            if (!saveManager.BindIapAnalyticsTransaction(
                    purchase.TransactionId,
                    purchase.ProductId,
                    purchase.LocalizedPriceValue,
                    purchase.Currency,
                    iapAnalyticsContext.DisplayedLevelNumber,
                    iapAnalyticsContext.InternalLevelNumber,
                    purchase.ProductId == StoreProductIds.FailOffer))
            {
                Debug.LogWarning(
                    $"[Store] IAP analytics context could not be bound to transaction '{purchase.TransactionId}'.");
            }
            else if (saveManager.IsDirty && !saveManager.TrySave())
            {
                Debug.LogWarning(
                    $"[Store] IAP analytics context for transaction '{purchase.TransactionId}' remains dirty for retry.");
            }

            TenjinPurchaseValidation.Capture(purchase, saveManager);
            deferredProducts.Remove(purchase.ProductId);
            deferredRewardContexts.Remove(purchase.ProductId);
            deferredIapAnalyticsContexts.Remove(purchase.ProductId);
            if (belongsToActivePurchase)
            {
                activeTransactionId = purchase.TransactionId;
                confirmationsToPublish.Add(purchase.TransactionId);
            }
            persistedUnconfirmedTransactions[purchase.TransactionId] =
                purchase.ProductId;

            PurchaseProcessResult result =
                purchaseProcessor.Process(purchase, product, context);
            if (result.Status == PurchaseProcessStatus.Duplicate)
            {
                userInitiatedConfirmations.Remove(purchase.TransactionId);
            }

            if (result.Status == PurchaseProcessStatus.Failed)
            {
                fulfillmentPurchasesPending[purchase.TransactionId] = purchase;
                if (belongsToActivePurchase)
                {
                    ClearActivePurchase();
                }
                else
                {
                    RefreshPurchaseState();
                }

                Debug.LogWarning(
                    $"[Store] Paid order fulfillment remains pending for transaction '{purchase.TransactionId}': {result.Error}");
                SchedulePendingStoreRetry();
                ProductStateChanged?.Invoke(purchase.ProductId);
                return;
            }

            if (result.Status == PurchaseProcessStatus.GameplayFulfillmentPending)
            {
                fulfillmentPurchasesPending[purchase.TransactionId] = purchase;
                gameplayFulfillmentsPending[purchase.TransactionId] = purchase.ProductId;
                if (belongsToActivePurchase)
                {
                    ClearActivePurchase();
                }
                else
                {
                    RefreshPurchaseState();
                }

                Debug.LogWarning(
                    $"[Store] Gameplay fulfillment remains pending for transaction '{purchase.TransactionId}': {result.Error}");
                SchedulePendingStoreRetry();
                ProductStateChanged?.Invoke(purchase.ProductId);
                return;
            }

            fulfillmentPurchasesPending.Remove(purchase.TransactionId);
            gameplayFulfillmentsPending.Remove(purchase.TransactionId);
            confirmationsPending[purchase.TransactionId] = purchase;
            confirmationsRetryEligible.Remove(purchase.TransactionId);
            if (result.Status == PurchaseProcessStatus.Succeeded ||
                belongsToActivePurchase)
            {
                confirmationsToPublish.Add(purchase.TransactionId);
            }

            if (belongsToActivePurchase)
            {
                ClearActivePurchase();
            }

            if (!purchaseService.ConfirmPurchase(purchase))
            {
                confirmationsRetryEligible.Add(purchase.TransactionId);
                RefreshPurchaseState();
                Debug.LogWarning(
                    $"[Store] Reward is persisted for transaction '{purchase.TransactionId}', but confirmation could not be started. The transaction remains awaiting confirmation.");
                SchedulePendingStoreRetry();
                ProductStateChanged?.Invoke(purchase.ProductId);
                return;
            }

            RefreshPurchaseState();
            ProductStateChanged?.Invoke(purchase.ProductId);
        }

        private void HandlePurchaseDeferred(string productId)
        {
            if (isPurchasing && (string.IsNullOrEmpty(productId) ||
                activeProductId == productId))
            {
                string deferredProductId = string.IsNullOrEmpty(productId)
                    ? activeProductId
                    : productId;
                if (!string.IsNullOrEmpty(deferredProductId))
                {
                    deferredProducts.Add(deferredProductId);
                    deferredRewardContexts[deferredProductId] = activeRewardContext;
                    if (activeIapAnalyticsContext.IsValid)
                    {
                        deferredIapAnalyticsContexts[deferredProductId] =
                            activeIapAnalyticsContext;
                    }
                }

                ClearActivePurchase();
                PurchaseDeferred?.Invoke(deferredProductId);
                ProductStateChanged?.Invoke(deferredProductId);
                return;
            }

            if (!string.IsNullOrEmpty(productId))
            {
                deferredProducts.Add(productId);
                RefreshPurchaseState();
                PurchaseDeferred?.Invoke(productId);
                ProductStateChanged?.Invoke(productId);
            }
        }

        private void HandleDeferredPurchasesFetched(
            IReadOnlyList<string> productIds)
        {
            HashSet<string> authoritativeProducts =
                new HashSet<string>(StringComparer.Ordinal);
            if (productIds != null)
            {
                for (int i = 0; i < productIds.Count; i++)
                {
                    if (!string.IsNullOrEmpty(productIds[i]))
                    {
                        authoritativeProducts.Add(productIds[i]);
                    }
                }
            }

            List<string> previouslyDeferred = new List<string>(deferredProducts);
            for (int i = 0; i < previouslyDeferred.Count; i++)
            {
                string productId = previouslyDeferred[i];
                if (!authoritativeProducts.Contains(productId))
                {
                    deferredProducts.Remove(productId);
                    deferredRewardContexts.Remove(productId);
                    deferredIapAnalyticsContexts.Remove(productId);
                    ClearPendingIapAnalyticsIntent(productId);
                    ProductStateChanged?.Invoke(productId);
                }
            }

            foreach (string productId in authoritativeProducts)
            {
                if (deferredProducts.Add(productId))
                {
                    ProductStateChanged?.Invoke(productId);
                }
            }

            RefreshPurchaseState();
        }

        private void HandlePendingPurchasesFetched(
            IReadOnlyList<StorePurchase> purchases)
        {
            HashSet<string> pendingTransactionIds =
                new HashSet<string>(StringComparer.Ordinal);
            if (purchases != null)
            {
                for (int i = 0; i < purchases.Count; i++)
                {
                    if (purchases[i] != null &&
                        !string.IsNullOrEmpty(purchases[i].TransactionId))
                    {
                        pendingTransactionIds.Add(purchases[i].TransactionId);
                    }
                }
            }

            List<string> knownTransactionIds =
                new List<string>(persistedUnconfirmedTransactions.Keys);
            for (int i = 0; i < knownTransactionIds.Count; i++)
            {
                string transactionId = knownTransactionIds[i];
                if (pendingTransactionIds.Contains(transactionId))
                {
                    continue;
                }

                string productId = persistedUnconfirmedTransactions[transactionId];
                persistedUnconfirmedTransactions.Remove(transactionId);
                confirmationsPending.Remove(transactionId);
                confirmationsRetryEligible.Remove(transactionId);
                confirmationsToPublish.Remove(transactionId);
                userInitiatedConfirmations.Remove(transactionId);
                ProductStateChanged?.Invoke(productId);
            }

            if (saveManager.ReconcileUnconfirmedIapTransactions(
                    pendingTransactionIds) && !saveManager.TrySave())
            {
                Debug.LogWarning(
                    "[Store] Authoritative pending-order reconciliation remains dirty for retry.");
            }

            RefreshPurchaseState();
        }

        private void HandlePurchaseConfirmed(StorePurchase purchase)
        {
            StorePurchase retainedAnalyticsPurchase = confirmationsPending.TryGetValue(
                purchase.TransactionId,
                out StorePurchase retainedPurchase)
                ? retainedPurchase
                : purchase;
            StorePurchase analyticsPurchase =
                HasCompleteIapPriceMetadata(retainedAnalyticsPurchase)
                    ? retainedAnalyticsPurchase
                    : purchase;
            LevelIapAnalyticsContext confirmationContext =
                isPurchasing && activeProductId == purchase.ProductId
                    ? activeIapAnalyticsContext
                    : default;
            saveManager.BindIapAnalyticsTransaction(
                purchase.TransactionId,
                purchase.ProductId,
                analyticsPurchase.LocalizedPriceValue,
                analyticsPurchase.Currency,
                confirmationContext.DisplayedLevelNumber,
                confirmationContext.InternalLevelNumber,
                purchase.ProductId == StoreProductIds.FailOffer);
            saveManager.MarkIapAnalyticsTransactionConfirmed(
                purchase.TransactionId,
                purchase.ProductId,
                analyticsPurchase.LocalizedPriceValue,
                analyticsPurchase.Currency);
            bool shouldPublishSuccess =
                confirmationsToPublish.Remove(purchase.TransactionId);
            bool wasUserInitiated =
                userInitiatedConfirmations.Remove(purchase.TransactionId);
            confirmationsPending.Remove(purchase.TransactionId);
            confirmationsRetryEligible.Remove(purchase.TransactionId);
            fulfillmentPurchasesPending.Remove(purchase.TransactionId);
            gameplayFulfillmentsPending.Remove(purchase.TransactionId);
            persistedUnconfirmedTransactions.Remove(purchase.TransactionId);
            saveManager.ClearIapTransactionUnconfirmed(purchase.TransactionId);
            if (saveManager.IsDirty && !saveManager.TrySave())
            {
                Debug.LogWarning(
                    $"[Store] Transaction '{purchase.TransactionId}' is confirmed, but local confirmation cleanup remains dirty for retry.");
            }
            bool belongsToActivePurchase =
                isPurchasing && activeProductId == purchase.ProductId &&
                (activeTransactionId.Length == 0 ||
                 activeTransactionId == purchase.TransactionId);
            if (belongsToActivePurchase)
            {
                ClearActivePurchase();
                shouldPublishSuccess = true;
            }
            else
            {
                RefreshPurchaseState();
            }

            ProductStateChanged?.Invoke(purchase.ProductId);
            FlushConfirmedIapAnalyticsTransactions();
            if (!shouldPublishSuccess)
            {
                return;
            }

            PublishPurchaseSuccess(purchase.ProductId, wasUserInitiated);
        }

        private void PublishPurchaseSuccess(string productId, bool wasUserInitiated)
        {
            if (wasUserInitiated)
            {
                // Transaction confirmation owns the purchase SFX. Product and reward
                // presentation callbacks must not decide which purchase sound to play.
                AudioManager.Instance?.PlaySfx(AudioKey.Purchase);
                UserPurchaseConfirmed?.Invoke(productId);
            }

            PurchaseSucceeded?.Invoke(productId);
        }

        private void HandlePurchaseConfirmationFailed(
            StorePurchase purchase,
            string error)
        {
            if (purchase != null &&
                !confirmationsPending.ContainsKey(purchase.TransactionId))
            {
                Debug.LogWarning(
                    $"[Store] Confirmation failure for transaction '{purchase.TransactionId}' has no retained PendingOrder. A store redelivery is required before it can be retried.");
            }
            else if (purchase != null)
            {
                confirmationsRetryEligible.Add(purchase.TransactionId);
            }
            else
            {
                foreach (string transactionId in confirmationsPending.Keys)
                {
                    confirmationsRetryEligible.Add(transactionId);
                }
            }

            RefreshPurchaseState();
            string productId = purchase?.ProductId ?? activeProductId;
            Debug.LogWarning(
                $"[Store] Confirmation remains pending for '{productId}'. Reward and transaction marker are retained: {error}");
            SchedulePendingStoreRetry();
            ProductStateChanged?.Invoke(productId);
        }

        private void HandlePurchaseFailed(StorePurchaseFailure failure)
        {
            if (IsProductResolutionPending(failure.ProductId) &&
                !deferredProducts.Contains(failure.ProductId))
            {
                Debug.LogWarning(
                    $"[Store] Ignored a normal purchase failure for '{failure.ProductId}' because a paid fulfillment is already awaiting recovery: {failure.Message}");
                ProductStateChanged?.Invoke(failure.ProductId);
                return;
            }

            deferredProducts.Remove(failure.ProductId);
            deferredRewardContexts.Remove(failure.ProductId);
            deferredIapAnalyticsContexts.Remove(failure.ProductId);
            string failedProductId = string.IsNullOrEmpty(failure.ProductId)
                ? activeProductId
                : failure.ProductId;
            ClearPendingIapAnalyticsIntent(failedProductId);
            if (!isPurchasing || string.IsNullOrEmpty(failure.ProductId) ||
                activeProductId == failure.ProductId)
            {
                if (!string.IsNullOrEmpty(activeTransactionId))
                {
                    userInitiatedConfirmations.Remove(activeTransactionId);
                }

                ClearActivePurchase();
            }
            ProductStateChanged?.Invoke(failure.ProductId);

            if (failure.IsCancellation)
            {
                PurchaseCancelled?.Invoke(failure.ProductId);
                return;
            }

            Debug.LogWarning(
                $"[Store] Purchase failed for '{failure.ProductId}': {failure.Reason} - {failure.Message}");
            PurchaseFailed?.Invoke(failure);
        }

        private void HandlePurchasesRestored(IReadOnlyList<StorePurchase> purchases)
        {
            RecoverPersistedConfirmedIapAnalytics(purchases);

            for (int i = 0; i < purchases.Count; i++)
            {
                if (purchases[i].ProductId == StoreProductIds.NoAds)
                {
                    lastRestoreFoundRestorablePurchase = true;
                    if (!purchaseProcessor.ApplyRestoredNoAdsEntitlement())
                    {
                        restoreEntitlementApplySucceeded = false;
                        restoreEntitlementApplyError =
                            "No Ads ownership was restored by the store, but it could not be persisted locally.";
                    }

                    break;
                }
            }

            ProductStateChanged?.Invoke(StoreProductIds.NoAds);
        }

        private void HandleRestoreCompleted(bool success, string error)
        {
            restoreInProgress = false;
            bool finalSuccess = success && restoreEntitlementApplySucceeded;
            string finalError = !restoreEntitlementApplySucceeded
                ? restoreEntitlementApplyError
                : error;
            if (!finalSuccess)
            {
                Debug.LogWarning($"[Store] Restore failed: {finalError}");
            }

            if (finalSuccess)
            {
                SetEntitlementSyncState(StoreEntitlementSyncState.Succeeded);
            }
            else if (success && !restoreEntitlementApplySucceeded)
            {
                SetEntitlementSyncState(StoreEntitlementSyncState.Failed);
            }

            RestoreCompleted?.Invoke(finalSuccess, finalError);
            ProductStateChanged?.Invoke(StoreProductIds.NoAds);
            if (success)
            {
                RetryPendingStoreWork();
            }
        }

        private void HandleStoreDisconnected(string error)
        {
            InitializationState = StoreInitializationState.Failed;
            if (PurchaseState != StorePurchaseLifecycleState.AwaitingConfirmation &&
                PurchaseState != StorePurchaseLifecycleState.GameplayFulfillmentPending)
            {
                ClearActivePurchase();
            }
            Debug.LogWarning($"[Store] Store disconnected: {error}");
        }

        private void SetEntitlementSyncState(StoreEntitlementSyncState state)
        {
            if (EntitlementSyncState == state)
            {
                return;
            }

            EntitlementSyncState = state;
            EntitlementSyncStateChanged?.Invoke(state);
        }

        private void HandleSavedStoreStateChanged()
        {
            ProductStateChanged?.Invoke(StoreProductIds.NoAds);
            ProductStateChanged?.Invoke(StoreProductIds.StarterPack);
            ProductStateChanged?.Invoke(StoreProductIds.FailOffer);
        }

        private void ClearActivePurchase()
        {
            string previousProductId = activeProductId;
            isPurchasing = false;
            activeProductId = string.Empty;
            activeTransactionId = string.Empty;
            activeRewardContext = default;
            activeIapAnalyticsContext = default;
            RefreshPurchaseState();

            if (previousProductId.Length > 0)
            {
                ProductStateChanged?.Invoke(previousProductId);
            }
        }

        private void RefreshPurchaseState()
        {
            if (isPurchasing)
            {
                if (activeTransactionId.Length > 0 &&
                    gameplayFulfillmentsPending.ContainsKey(activeTransactionId))
                {
                    PurchaseState =
                        StorePurchaseLifecycleState.GameplayFulfillmentPending;
                }
                else if (activeTransactionId.Length > 0 &&
                         confirmationsPending.ContainsKey(activeTransactionId))
                {
                    PurchaseState = StorePurchaseLifecycleState.AwaitingConfirmation;
                }
                else
                {
                    PurchaseState = StorePurchaseLifecycleState.Purchasing;
                }

                return;
            }

            if (gameplayFulfillmentsPending.Count > 0)
            {
                PurchaseState = StorePurchaseLifecycleState.GameplayFulfillmentPending;
            }
            else if (fulfillmentPurchasesPending.Count > 0)
            {
                PurchaseState = StorePurchaseLifecycleState.FulfillmentPending;
            }
            else if (confirmationsPending.Count > 0)
            {
                PurchaseState = StorePurchaseLifecycleState.AwaitingConfirmation;
            }
            else if (persistedUnconfirmedTransactions.Count > 0)
            {
                PurchaseState = StorePurchaseLifecycleState.AwaitingConfirmation;
            }
            else if (deferredProducts.Count > 0)
            {
                PurchaseState = StorePurchaseLifecycleState.Deferred;
            }
            else
            {
                PurchaseState = StorePurchaseLifecycleState.Idle;
            }
        }

        private bool IsProductResolutionPending(string productId)
        {
            if (string.IsNullOrEmpty(productId) || deferredProducts.Contains(productId))
            {
                return !string.IsNullOrEmpty(productId);
            }

            foreach (StorePurchase purchase in confirmationsPending.Values)
            {
                if (purchase != null && purchase.ProductId == productId)
                {
                    return true;
                }
            }

            foreach (StorePurchase purchase in fulfillmentPurchasesPending.Values)
            {
                if (purchase != null && purchase.ProductId == productId)
                {
                    return true;
                }
            }

            if (persistedUnconfirmedTransactions.ContainsValue(productId))
            {
                return true;
            }

            return gameplayFulfillmentsPending.ContainsValue(productId);
        }

        private static LevelIapAnalyticsContext CaptureLevelIapAnalyticsContext(
            out object contextOwner)
        {
            contextOwner = null;
            LevelAnalyticsTracker tracker = LevelAnalyticsTracker.Instance;
            if (tracker != null)
            {
                if (!tracker.TryGetLevelContext(
                        out int displayedLevelNumber,
                        out int internalLevelNumber))
                {
                    return default;
                }

                contextOwner = tracker;
                return new LevelIapAnalyticsContext(
                    displayedLevelNumber,
                    internalLevelNumber);
            }

            MenuLevelProgressController menuLevelProgressController =
                FindFirstObjectByType<MenuLevelProgressController>();
            if (menuLevelProgressController == null ||
                !menuLevelProgressController.TryGetCurrentLevelContext(
                    out int menuDisplayedLevelNumber,
                    out int menuInternalLevelNumber))
            {
                return default;
            }

            contextOwner = menuLevelProgressController;
            return new LevelIapAnalyticsContext(
                menuDisplayedLevelNumber,
                menuInternalLevelNumber);
        }

        private void PersistPendingIapAnalyticsIntent(
            string productId,
            LevelIapAnalyticsContext context)
        {
            bool changed = context.IsValid
                ? saveManager.SetPendingIapAnalyticsIntent(
                    productId,
                    context.DisplayedLevelNumber,
                    context.InternalLevelNumber,
                    productId == StoreProductIds.FailOffer)
                : saveManager.ClearPendingIapAnalyticsIntent(productId);
            if (changed && !saveManager.TrySave())
            {
                Debug.LogWarning(
                    $"[Store] Pending IAP analytics context for '{productId}' remains dirty for retry.");
            }
        }

        private static void SynchronizePurchaseStartAnalyticsContext(
            LevelIapAnalyticsContext context,
            object contextOwner)
        {
            if (!context.IsValid)
            {
                return;
            }

            AnalyticsBootstrap analyticsBootstrap = AnalyticsBootstrap.Instance;
            if (contextOwner != null)
            {
                analyticsBootstrap?.SetDefaultLevelContext(
                    context.DisplayedLevelNumber,
                    context.InternalLevelNumber,
                    contextOwner,
                    "iap_purchase_start");
                return;
            }

            analyticsBootstrap?.RefreshDefaultLevelContext(
                context.DisplayedLevelNumber,
                context.InternalLevelNumber,
                "iap_purchase_start_without_owner");
        }

        private void ClearPendingIapAnalyticsIntent(string productId)
        {
            if (saveManager != null &&
                saveManager.ClearPendingIapAnalyticsIntent(productId) &&
                !saveManager.TrySave())
            {
                Debug.LogWarning(
                    $"[Store] Terminal IAP analytics context cleanup for '{productId}' remains dirty for retry.");
            }
        }

        private void FlushConfirmedIapAnalyticsTransactions()
        {
            AnalyticsBootstrap analyticsBootstrap = AnalyticsBootstrap.Instance;
            if (saveManager == null || analyticsBootstrap?.Service == null)
            {
                SchedulePendingStoreRetry();
                return;
            }

            bool retryRequired = false;
            List<StoreIapAnalyticsTransactionSaveData> transactions =
                new List<StoreIapAnalyticsTransactionSaveData>(
                    saveManager.GetIapAnalyticsTransactions());
            for (int i = 0; i < transactions.Count; i++)
            {
                StoreIapAnalyticsTransactionSaveData entry = transactions[i];
                if (entry != null && entry.confirmed && entry.tenjinPurchasePending)
                {
                    bool disabled = analyticsBootstrap.IsTenjinPurchaseDisabled;
                    if (disabled || analyticsBootstrap.CanTrackTenjinPurchase)
                    {
                        if (saveManager.ReserveTenjinPurchaseDispatch(entry.transactionId))
                        {
                            if (!disabled) analyticsBootstrap.TrackTenjinPurchase(entry);
                            saveManager.ClearTenjinReceipt(entry.transactionId);
                        }
                        else retryRequired = true;
                    }
                    else retryRequired = true;
                }
                if (entry == null || !entry.confirmed ||
                    string.IsNullOrWhiteSpace(entry.transactionId) ||
                    entry.levelDisplayedNumber <= 0 || entry.levelNumber <= 0)
                {
                    continue;
                }

                if (!entry.levelIapPurchaseAccepted &&
                    !string.IsNullOrWhiteSpace(entry.productId) &&
                    !string.IsNullOrWhiteSpace(entry.currency))
                {
                    AnalyticsProviderTrackResult result =
                        analyticsBootstrap.TrackWithoutProviderQueue(
                            AnalyticsEventFactory.CreateLevelIapPurchase(
                                entry.levelDisplayedNumber,
                                entry.levelNumber,
                                entry.productId,
                                entry.localizedPriceValue,
                                entry.currency));
                    if (ShouldConsumePersistedAnalyticsEvent(result))
                    {
                        TryAcceptPersistedIapAnalyticsEvent(
                            entry.transactionId,
                            false);
                    }
                    else
                    {
                        retryRequired |= ShouldRetryPersistedAnalyticsEvent(result);
                    }
                }

                if (entry.isFailOffer && !entry.failOfferPurchasedAccepted)
                {
                    AnalyticsProviderTrackResult result =
                        analyticsBootstrap.TrackWithoutProviderQueue(
                            AnalyticsEventFactory.CreateFailOfferPurchased(
                                entry.levelDisplayedNumber,
                                entry.levelNumber));
                    if (ShouldConsumePersistedAnalyticsEvent(result))
                    {
                        TryAcceptPersistedIapAnalyticsEvent(
                            entry.transactionId,
                            true);
                    }
                    else
                    {
                        retryRequired |= ShouldRetryPersistedAnalyticsEvent(result);
                    }
                }
            }

            if (retryRequired)
            {
                SchedulePendingStoreRetry();
            }
        }

        private static bool ShouldConsumePersistedAnalyticsEvent(
            AnalyticsProviderTrackResult result)
        {
            return result != null &&
                   (result.Status == AnalyticsProviderTrackStatus.ForwardedToSdk ||
                    result.Status == AnalyticsProviderTrackStatus.SkippedCollectionDisabled);
        }

        private static bool ShouldRetryPersistedAnalyticsEvent(
            AnalyticsProviderTrackResult result)
        {
            return result != null &&
                   result.Status == AnalyticsProviderTrackStatus.SkippedProviderNotReady;
        }

        private static bool HasCompleteIapPriceMetadata(StorePurchase purchase)
        {
            return purchase != null &&
                   !double.IsNaN(purchase.LocalizedPriceValue) &&
                   !double.IsInfinity(purchase.LocalizedPriceValue) &&
                   purchase.LocalizedPriceValue >= 0d &&
                   !string.IsNullOrWhiteSpace(purchase.Currency);
        }

        private void RecoverPersistedConfirmedIapAnalytics(
            IReadOnlyList<StorePurchase> purchases)
        {
            if (purchases == null || saveManager == null)
            {
                return;
            }

            for (int i = 0; i < purchases.Count; i++)
            {
                StorePurchase purchase = purchases[i];
                if (purchase == null || !saveManager.HasIapAnalyticsTransaction(
                        purchase.TransactionId,
                        purchase.ProductId))
                {
                    continue;
                }

                saveManager.BindIapAnalyticsTransaction(
                    purchase.TransactionId,
                    purchase.ProductId,
                    purchase.LocalizedPriceValue,
                    purchase.Currency,
                    0,
                    0,
                    purchase.ProductId == StoreProductIds.FailOffer);
                saveManager.MarkIapAnalyticsTransactionConfirmed(
                    purchase.TransactionId,
                    purchase.ProductId,
                    purchase.LocalizedPriceValue,
                    purchase.Currency);
            }

            if (saveManager.IsDirty && !saveManager.TrySave())
            {
                Debug.LogWarning(
                    "[Store] Recovered confirmed IAP analytics state remains dirty for retry.");
            }

            FlushConfirmedIapAnalyticsTransactions();
        }

        private bool TryAcceptPersistedIapAnalyticsEvent(
            string transactionId,
            bool failOfferPurchasedEvent)
        {
            if (!saveManager.MarkIapAnalyticsEventAccepted(
                    transactionId,
                    failOfferPurchasedEvent))
            {
                return false;
            }

            if (saveManager.TrySave())
            {
                return true;
            }

            Debug.LogWarning(
                $"[Store] Analytics acceptance for transaction '{transactionId}' is retained in memory but remains dirty for persistence retry.");
            return true;
        }

        private void HydratePersistedUnconfirmedTransactions()
        {
            persistedUnconfirmedTransactions.Clear();
            IReadOnlyList<StoreUnconfirmedTransactionSaveData> entries =
                saveManager.GetUnconfirmedIapTransactions();
            for (int i = 0; i < entries.Count; i++)
            {
                StoreUnconfirmedTransactionSaveData entry = entries[i];
                if (entry != null && !string.IsNullOrEmpty(entry.transactionId) &&
                    !string.IsNullOrEmpty(entry.productId))
                {
                    persistedUnconfirmedTransactions[entry.transactionId] =
                        entry.productId;
                }
            }

            RefreshPurchaseState();
        }

        private void RetryPendingStoreWork()
        {
            if (retryingPendingStoreWork || purchaseService == null ||
                !purchaseService.IsReady)
            {
                return;
            }

            retryingPendingStoreWork = true;
            try
            {
                RetryPendingConfirmations();
                RetryPendingFulfillments();
                FlushConfirmedIapAnalyticsTransactions();
            }
            finally
            {
                retryingPendingStoreWork = false;
            }

            if (fulfillmentPurchasesPending.Count > 0 ||
                confirmationsRetryEligible.Count > 0)
            {
                SchedulePendingStoreRetry();
            }
            else
            {
                pendingStoreRetryDelaySeconds = InitialRetryDelaySeconds;
            }
        }

        private void SchedulePendingStoreRetry()
        {
            if (!isActiveAndEnabled || purchaseService == null ||
                !purchaseService.IsReady || pendingStoreRetryCoroutine != null)
            {
                return;
            }

            pendingStoreRetryCoroutine = StartCoroutine(RetryPendingStoreWorkAfterDelay());
        }

        private IEnumerator RetryPendingStoreWorkAfterDelay()
        {
            float delaySeconds = pendingStoreRetryDelaySeconds;
            yield return new WaitForSecondsRealtime(delaySeconds);
            pendingStoreRetryCoroutine = null;
            pendingStoreRetryDelaySeconds = Mathf.Min(
                MaxRetryDelaySeconds,
                Math.Max(InitialRetryDelaySeconds, delaySeconds * 2f));
            RetryPendingStoreWork();
        }

        private static bool ContainsProduct(
            IReadOnlyList<StorePurchase> purchases,
            string productId)
        {
            if (purchases == null)
            {
                return false;
            }

            for (int i = 0; i < purchases.Count; i++)
            {
                if (purchases[i]?.ProductId == productId)
                {
                    return true;
                }
            }

            return false;
        }

        private void RejectPurchase(
            string productId,
            StorePurchaseFailureReason reason,
            string message)
        {
            StorePurchaseFailure failure =
                new StorePurchaseFailure(productId, reason, message);
            Debug.LogWarning(
                $"[Store] Purchase rejected for '{productId}': {reason} - {message}");
            PurchaseFailed?.Invoke(failure);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public bool DebugGrantProductRewards(
            string productId,
            bool includeGameplayActions = true)
        {
            if (purchaseProcessor == null ||
                !StoreCatalog.TryGet(productId, out StoreProductDefinition product))
            {
                return false;
            }

            StoreRewardContext context = default;
            if (includeGameplayActions && productId == StoreProductIds.FailOffer &&
                StoreGameplayActionRegistry.TryCaptureFailedSession(
                    out string sessionToken))
            {
                context = new StoreRewardContext(true, sessionToken);
            }

            bool result = purchaseProcessor.DebugGrant(
                product,
                context,
                includeGameplayActions);
            ProductStateChanged?.Invoke(productId);
            return result;
        }

        public void DebugPrintState()
        {
            Debug.Log(
                $"[Store] State={InitializationState}, Entitlements={EntitlementSyncState}, " +
                $"Purchase={PurchaseState}, Ready={IsReady}, Purchasing={IsPurchasing}, " +
                $"Restoring={IsRestoringPurchases}, " +
                $"NoAds={saveManager?.HasNoAds}, " +
                $"FailOfferRecoveryContinues={PendingFailOfferRecoveryContinues}, " +
                $"StarterPackPurchased={saveManager?.StarterPackPurchased}, " +
                $"InfiniteLivesEndUtc={saveManager?.InfiniteLivesEndUtcUnixSeconds}, " +
                $"ProcessedTransactions={saveManager?.ProcessedIapTransactionCount}");
        }
#endif
    }
}
