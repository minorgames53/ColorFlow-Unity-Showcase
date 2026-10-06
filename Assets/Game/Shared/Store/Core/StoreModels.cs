using System;
using System.Collections.Generic;

namespace Game.Shared.Store
{
    public enum StoreProductType
    {
        Consumable = 0,
        NonConsumable = 1
    }

    public enum StoreRewardType
    {
        Coin = 0,
        Hand = 1,
        Shuffle = 2,
        Ufo = 3,
        InfiniteLives = 4,
        NoAds = 5,
        ContinueLevel = 6
    }

    public enum StoreInitializationState
    {
        NotInitialized = 0,
        Initializing = 1,
        Ready = 2,
        Failed = 3
    }

    public enum StoreEntitlementSyncState
    {
        NotStarted = 0,
        Syncing = 1,
        Succeeded = 2,
        Failed = 3
    }

    public enum StorePurchaseLifecycleState
    {
        Idle = 0,
        Purchasing = 1,
        Deferred = 2,
        AwaitingConfirmation = 3,
        GameplayFulfillmentPending = 4,
        FulfillmentPending = 5
    }

    public enum StoreProductUiState
    {
        Loading = 0,
        Available = 1,
        Owned = 2,
        Purchasing = 3,
        Deferred = 4,
        Resolving = 5,
        Unavailable = 6
    }

    public enum StorePurchaseFailureReason
    {
        Unknown = 0,
        StoreUnavailable = 1,
        ProductUnavailable = 2,
        PurchaseAlreadyInProgress = 3,
        NotEligible = 4,
        UserCancelled = 5,
        InvalidTransaction = 6,
        RewardProcessingFailed = 7,
        StoreError = 8,
        InvalidFailSession = 9
    }

    [Serializable]
    public readonly struct StoreReward
    {
        public StoreReward(StoreRewardType type, int amount = 1)
        {
            Type = type;
            Amount = amount;
        }

        public StoreRewardType Type { get; }
        public int Amount { get; }
    }

    public sealed class StoreProductDefinition
    {
        public StoreProductDefinition(
            string productId,
            string configName,
            StoreProductType productType,
            bool locallyOneTime,
            params StoreReward[] rewards)
        {
            ProductId = productId;
            ConfigName = configName;
            ProductType = productType;
            LocallyOneTime = locallyOneTime;
            Rewards = rewards ?? Array.Empty<StoreReward>();
        }

        public string ProductId { get; }
        public string ConfigName { get; }
        public StoreProductType ProductType { get; }
        public bool LocallyOneTime { get; }
        public IReadOnlyList<StoreReward> Rewards { get; }
    }

    public readonly struct StoreProductInfo
    {
        public StoreProductInfo(
            string productId,
            bool availableToPurchase,
            string localizedPrice,
            string localizedTitle)
        {
            ProductId = productId;
            AvailableToPurchase = availableToPurchase;
            LocalizedPrice = localizedPrice ?? string.Empty;
            LocalizedTitle = localizedTitle ?? string.Empty;
        }

        public string ProductId { get; }
        public bool AvailableToPurchase { get; }
        public string LocalizedPrice { get; }
        public string LocalizedTitle { get; }
    }

    public sealed class StorePurchase
    {
        public StorePurchase(
            string productId,
            string transactionId,
            string receipt,
            double localizedPrice,
            string currency,
            object nativeOrder)
        {
            ProductId = productId ?? string.Empty;
            TransactionId = transactionId ?? string.Empty;
            Receipt = receipt ?? string.Empty;
            LocalizedPriceValue = localizedPrice;
            Currency = currency ?? string.Empty;
            NativeOrder = nativeOrder;
        }

        public string ProductId { get; }
        public string TransactionId { get; }
        public string Receipt { get; }
        public double LocalizedPriceValue { get; }
        public string Currency { get; }
        internal object NativeOrder { get; }
    }

    public readonly struct StorePurchaseFailure
    {
        public StorePurchaseFailure(
            string productId,
            StorePurchaseFailureReason reason,
            string message)
        {
            ProductId = productId ?? string.Empty;
            Reason = reason;
            Message = message ?? string.Empty;
        }

        public string ProductId { get; }
        public StorePurchaseFailureReason Reason { get; }
        public string Message { get; }
        public bool IsCancellation => Reason == StorePurchaseFailureReason.UserCancelled;
    }
}
