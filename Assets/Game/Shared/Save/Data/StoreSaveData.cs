using System;
using System.Collections.Generic;

namespace Game.Shared.Save
{
    [Serializable]
    public sealed class StoreGameplayFulfillmentSaveData
    {
        public string transactionId = string.Empty;
        public string productId = string.Empty;
        public string failedSessionToken = string.Empty;
        public bool gameplayRewardApplied;
    }

    [Serializable]
    public sealed class StoreUnconfirmedTransactionSaveData
    {
        public string transactionId = string.Empty;
        public string productId = string.Empty;
    }

    [Serializable]
    public sealed class StoreIapAnalyticsIntentSaveData
    {
        public string productId = string.Empty;
        public int levelDisplayedNumber;
        public int levelNumber;
        public bool isFailOffer;
    }

    [Serializable]
    public sealed class StoreIapAnalyticsTransactionSaveData
    {
        public string transactionId = string.Empty;
        public string productId = string.Empty;
        public int levelDisplayedNumber;
        public int levelNumber;
        public bool isFailOffer;
        public double localizedPriceValue;
        public string currency = string.Empty;
        public bool confirmed;
        public bool levelIapPurchaseAccepted;
        public bool failOfferPurchasedAccepted;
        // Defaults deliberately false: historical ledger rows never enroll on update/restore.
        public bool tenjinPurchasePending;
        public bool tenjinPurchaseDispatchReserved;
        public string tenjinReceipt = string.Empty;
        public string tenjinSignature = string.Empty;
        public int tenjinQuantity = 1;
    }

    [Serializable]
    public sealed class StoreSaveData
    {
        public bool hasNoAds;
        public bool noAdsIntroShown;
        public bool starterPackPurchased;
        public long infiniteLivesEndUtc;
        public int pendingFailOfferContinueCredits;
        public int reservedFailOfferContinueCredits;
        public List<string> processedIapTransactions = new List<string>();
        public List<StoreUnconfirmedTransactionSaveData> unconfirmedIapTransactions =
            new List<StoreUnconfirmedTransactionSaveData>();
        public List<StoreGameplayFulfillmentSaveData> iapGameplayFulfillments =
            new List<StoreGameplayFulfillmentSaveData>();
        public List<StoreIapAnalyticsIntentSaveData> pendingIapAnalyticsIntents =
            new List<StoreIapAnalyticsIntentSaveData>();
        public List<StoreIapAnalyticsTransactionSaveData> iapAnalyticsTransactions =
            new List<StoreIapAnalyticsTransactionSaveData>();
    }
}
