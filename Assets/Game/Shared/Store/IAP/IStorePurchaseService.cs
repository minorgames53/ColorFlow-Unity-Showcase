using System;
using System.Collections.Generic;

namespace Game.Shared.Store
{
    public interface IStorePurchaseService : IDisposable
    {
        bool IsReady { get; }

        event Action<bool, string> InitializationCompleted;
        event Action ProductsChanged;
        event Action<IReadOnlyList<StorePurchase>> InitialEntitlementsFetched;
        event Action<bool, string> InitialEntitlementSyncCompleted;
        event Action<StorePurchase> PurchasePending;
        event Action<string> PurchaseDeferred;
        event Action<IReadOnlyList<string>> DeferredPurchasesFetched;
        event Action<IReadOnlyList<StorePurchase>> PendingPurchasesFetched;
        event Action<StorePurchase> PurchaseConfirmed;
        event Action<StorePurchase, string> PurchaseConfirmationFailed;
        event Action<StorePurchaseFailure> PurchaseFailed;
        event Action<IReadOnlyList<StorePurchase>> PurchasesRestored;
        event Action<bool, string> RestoreCompleted;
        event Action<string> StoreDisconnected;

        void Initialize(IReadOnlyList<StoreProductDefinition> products);
        bool Purchase(string productId);
        bool ConfirmPurchase(StorePurchase purchase);
        void RestorePurchases();
        bool TryGetProduct(string productId, out StoreProductInfo product);
    }
}
