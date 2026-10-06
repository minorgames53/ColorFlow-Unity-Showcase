using System;
using System.Collections.Generic;
using Game.Shared.Audio;
using UnityEngine;

namespace Game.Shared.Save
{
    [DisallowMultipleComponent]
    public sealed class SaveManager : MonoBehaviour
    {
        public const int MaxProcessedIapTransactions = 128;

        #region State

        private GameSaveData data;
        private ISaveStorage saveStorage;
        private bool isDirty;
        private bool writeBlockedByUnsupportedVersion;
        private bool blockedWriteWasLogged;
        private bool backupRestorePending;

        public static SaveManager Instance { get; private set; }
        public bool IsInitialized { get; private set; }
        public bool IsDirty => isDirty;
        public bool IsWriteBlocked => writeBlockedByUnsupportedVersion;

        /// <summary>
        /// Exposes the live model for diagnostics and read access. Gameplay mutations should use
        /// SaveManager methods so validation, dirty state, and change events stay consistent.
        /// </summary>
        public GameSaveData Data => data;

        public int CurrentLevel =>
            data?.level != null
                ? data.level.currentLevel
                : GameSaveDataFactory.InitialCurrentLevel;

        public bool HasPendingNextLevel => data?.level != null && data.level.hasPendingNextLevel;
        public int PendingNextLevel => data?.level != null ? data.level.pendingNextLevel : 0;

        public int LastLoopContentLevelNumber =>
            data?.level != null
                ? data.level.lastLoopContentLevelNumber
                : GameSaveDataFactory.InitialLastLoopContentLevelNumber;

        public int Gold => data?.gold != null ? data.gold.amount : GameSaveDataFactory.InitialGold;
        public int Lives => data?.lives != null ? data.lives.current : GameSaveDataFactory.InitialLives;
        public int MaxLives =>
            data?.lives != null ? data.lives.max : GameSaveDataFactory.InitialMaxLives;
        public long NextLifeRefillUtcUnixSeconds =>
            data?.lives != null ? data.lives.nextRefillUtc : 0;
        public bool HasNoAds => data?.store != null && data.store.hasNoAds;
        public bool NoAdsIntroShown => data?.store != null && data.store.noAdsIntroShown;
        public bool StarterPackPurchased =>
            data?.store != null && data.store.starterPackPurchased;
        public long InfiniteLivesEndUtcUnixSeconds =>
            data?.store != null ? data.store.infiniteLivesEndUtc : 0;
        public int PendingFailOfferContinueCredits =>
            data?.store != null ? data.store.pendingFailOfferContinueCredits : 0;
        public bool HasInfiniteLives =>
            InfiniteLivesEndUtcUnixSeconds > DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public bool SoundEnabled => GetBoolSettingFromData(
            data,
            SaveKeys.SoundEnabled,
            GameSaveDataFactory.InitialSoundEnabled);

        public bool HapticEnabled => GetBoolSettingFromData(
            data,
            SaveKeys.HapticEnabled,
            GameSaveDataFactory.InitialHapticEnabled);

        public event Action OnSaveLoaded;

        public event Action<int> CurrentLevelChanged;
        public event Action<int> GoldChanged;
        public event Action<int, int> LivesChanged;
        public event Action<string, int> BoosterAmountChanged;
        public event Action<string> SettingChanged;
        public event Action StoreStateChanged;

        // Backward-compatible events used by the existing bootstrap and settings UI.
        public event Action<bool> OnSoundEnabledChanged;
        public event Action<bool> OnHapticEnabledChanged;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                SaveIfDirty();
            }
        }

        private void OnApplicationQuit()
        {
            SaveIfDirty();
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            SaveIfDirty();
            Instance = null;
        }

        #endregion

        #region Initialization

        public void Initialize()
        {
            if (IsInitialized)
            {
                return;
            }

            EnsureServices();
            LoadOrCreateSave(true);
        }

        /// <summary>
        /// Allows tests or another bootstrap to provide a different storage implementation.
        /// The storage must be supplied before this manager has initialized.
        /// </summary>
        public void Initialize(ISaveStorage storage)
        {
            if (storage == null)
            {
                throw new ArgumentNullException(nameof(storage));
            }

            if (IsInitialized)
            {
                return;
            }

            saveStorage = storage;
            Initialize();
        }

        public void Reload()
        {
            EnsureServices();
            LoadOrCreateSave(true);
        }

        // Backward-compatible name from the former provider-based implementation.
        public void ReloadFromProvider()
        {
            Reload();
        }

        #endregion

        #region Persistence API

        public void Save()
        {
            TrySave();
        }

        public bool TrySave()
        {
            EnsureInitialized();
            return WriteCurrentData();
        }

        internal string CaptureRuntimeSnapshot()
        {
            EnsureInitialized();
            return SaveJsonSerializer.ToJson(data, false);
        }

        internal bool RestoreRuntimeSnapshot(string json, bool dirtyBeforeSnapshot)
        {
            if (!SaveJsonSerializer.TryFromJson(
                    json,
                    out GameSaveData restoredData,
                    out _,
                    out _,
                    out string error))
            {
                Debug.LogError($"Runtime save snapshot could not be restored: {error}", this);
                return false;
            }

            GameSaveData previousData = data;
            data = restoredData;
            isDirty = dirtyBeforeSnapshot;
            PublishLoadedEvents(previousData);
            return true;
        }

        // Backward-compatible name from the former provider-based implementation.
        public void SaveNow()
        {
            Save();
        }

        public void ResetSave()
        {
            EnsureServices();

            GameSaveData previousData = data;

            try
            {
                saveStorage.Delete();
            }
            catch (Exception exception)
            {
                Debug.LogError($"Save reset failed while deleting existing files: {exception.Message}", this);
                return;
            }

            data = GameSaveDataFactory.CreateDefault();
            IsInitialized = true;
            isDirty = true;
            writeBlockedByUnsupportedVersion = false;
            blockedWriteWasLogged = false;
            backupRestorePending = false;

            WriteCurrentData();
            PublishLoadedEvents(previousData);
        }

        // Retained for callers that intentionally need the physical save removed.
        public void DeleteSave()
        {
            EnsureServices();

            try
            {
                saveStorage.Delete();
            }
            catch (Exception exception)
            {
                Debug.LogError($"Save delete failed: {exception.Message}", this);
                return;
            }

            GameSaveData previousData = data;
            data = GameSaveDataFactory.CreateDefault();
            IsInitialized = true;
            isDirty = false;
            writeBlockedByUnsupportedVersion = false;
            blockedWriteWasLogged = false;
            backupRestorePending = false;
            PublishLoadedEvents(previousData);
        }

        #endregion

        #region Store API

        public void SetHasNoAds(bool hasNoAds)
        {
            EnsureInitialized();
            if (data.store.hasNoAds == hasNoAds)
            {
                return;
            }

            data.store.hasNoAds = hasNoAds;
            MarkDirty();
            StoreStateChanged?.Invoke();
        }

        public bool MarkNoAdsIntroShown()
        {
            EnsureInitialized();
            if (data.store.noAdsIntroShown)
            {
                return false;
            }

            data.store.noAdsIntroShown = true;
            MarkDirty();
            StoreStateChanged?.Invoke();
            SaveIfDirty();
            return true;
        }

        public void SetStarterPackPurchased(bool purchased)
        {
            EnsureInitialized();
            if (data.store.starterPackPurchased == purchased)
            {
                return;
            }

            data.store.starterPackPurchased = purchased;
            MarkDirty();
            StoreStateChanged?.Invoke();
        }

        public void AddInfiniteLives(TimeSpan duration)
        {
            EnsureInitialized();
            if (duration <= TimeSpan.Zero)
            {
                return;
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long baseTimestamp = Math.Max(now, data.store.infiniteLivesEndUtc);
            long durationSeconds = Math.Max(1L, (long)Math.Ceiling(duration.TotalSeconds));
            long endTimestamp = baseTimestamp > long.MaxValue - durationSeconds
                ? long.MaxValue
                : baseTimestamp + durationSeconds;
            SetInfiniteLivesEndUtc(endTimestamp);
        }

        public void SetInfiniteLivesEndUtc(long utcUnixSeconds)
        {
            EnsureInitialized();
            long normalizedTimestamp = Math.Max(0, utcUnixSeconds);
            if (data.store.infiniteLivesEndUtc == normalizedTimestamp)
            {
                return;
            }

            data.store.infiniteLivesEndUtc = normalizedTimestamp;
            MarkDirty();
            StoreStateChanged?.Invoke();
        }

        internal void RefreshRuntimeStateForDeveloperTools()
        {
            if (!Game.Shared.DeveloperTools.DeveloperPanelAvailability.IsAllowed) return;
            EnsureInitialized();
            PublishLoadedEvents(null);
        }

        public bool IsIapTransactionProcessed(string transactionId)
        {
            EnsureInitialized();
            string normalizedId = NormalizeIdentifier(transactionId);
            return normalizedId.Length > 0 &&
                   data.store.processedIapTransactions.Contains(normalizedId);
        }

        public bool MarkIapTransactionProcessed(string transactionId)
        {
            EnsureInitialized();
            string normalizedId = NormalizeIdentifier(transactionId);
            if (normalizedId.Length == 0 ||
                data.store.processedIapTransactions.Contains(normalizedId))
            {
                return false;
            }

            data.store.processedIapTransactions.Add(normalizedId);
            while (data.store.processedIapTransactions.Count > MaxProcessedIapTransactions)
            {
                int removableIndex = FindOldestConfirmedTransactionIndex();
                if (removableIndex < 0)
                {
                    break;
                }

                string removedTransactionId =
                    data.store.processedIapTransactions[removableIndex];
                data.store.processedIapTransactions.RemoveAt(removableIndex);
                RemoveIapGameplayFulfillmentInternal(removedTransactionId);
            }

            MarkDirty();
            return true;
        }

        internal bool MarkIapTransactionUnconfirmed(
            string transactionId,
            string productId)
        {
            EnsureInitialized();
            string normalizedTransactionId = NormalizeIdentifier(transactionId);
            string normalizedProductId = NormalizeIdentifier(productId);
            if (normalizedTransactionId.Length == 0 || normalizedProductId.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < data.store.unconfirmedIapTransactions.Count; i++)
            {
                StoreUnconfirmedTransactionSaveData existing =
                    data.store.unconfirmedIapTransactions[i];
                if (existing != null && string.Equals(
                        existing.transactionId,
                        normalizedTransactionId,
                        StringComparison.Ordinal))
                {
                    if (string.Equals(
                            existing.productId,
                            normalizedProductId,
                            StringComparison.Ordinal))
                    {
                        return false;
                    }

                    existing.productId = normalizedProductId;
                    MarkDirty();
                    return true;
                }
            }

            if (data.store.unconfirmedIapTransactions.Count >=
                MaxProcessedIapTransactions)
            {
                throw new InvalidOperationException(
                    "The unconfirmed IAP transaction journal is full.");
            }

            data.store.unconfirmedIapTransactions.Add(
                new StoreUnconfirmedTransactionSaveData
                {
                    transactionId = normalizedTransactionId,
                    productId = normalizedProductId
                });
            MarkDirty();
            return true;
        }

        internal bool ClearIapTransactionUnconfirmed(string transactionId)
        {
            EnsureInitialized();
            string normalizedId = NormalizeIdentifier(transactionId);
            for (int i = data.store.unconfirmedIapTransactions.Count - 1; i >= 0; i--)
            {
                StoreUnconfirmedTransactionSaveData entry =
                    data.store.unconfirmedIapTransactions[i];
                if (entry != null && string.Equals(
                        entry.transactionId,
                        normalizedId,
                        StringComparison.Ordinal))
                {
                    data.store.unconfirmedIapTransactions.RemoveAt(i);
                    MarkDirty();
                    return true;
                }
            }

            return false;
        }

        internal bool ReconcileUnconfirmedIapTransactions(
            ISet<string> pendingTransactionIds)
        {
            EnsureInitialized();
            bool changed = false;
            for (int i = data.store.unconfirmedIapTransactions.Count - 1; i >= 0; i--)
            {
                StoreUnconfirmedTransactionSaveData entry =
                    data.store.unconfirmedIapTransactions[i];
                if (entry == null || pendingTransactionIds == null ||
                    !pendingTransactionIds.Contains(entry.transactionId))
                {
                    data.store.unconfirmedIapTransactions.RemoveAt(i);
                    changed = true;
                }
            }

            if (changed)
            {
                MarkDirty();
            }

            return changed;
        }

        internal IReadOnlyList<StoreUnconfirmedTransactionSaveData>
            GetUnconfirmedIapTransactions()
        {
            EnsureInitialized();
            return data.store.unconfirmedIapTransactions;
        }

        internal bool SetPendingIapAnalyticsIntent(
            string productId,
            int levelDisplayedNumber,
            int levelNumber,
            bool isFailOffer)
        {
            EnsureInitialized();
            string normalizedProductId = NormalizeIdentifier(productId);
            if (normalizedProductId.Length == 0 || levelDisplayedNumber <= 0 ||
                levelNumber <= 0)
            {
                return normalizedProductId.Length > 0 &&
                       ClearPendingIapAnalyticsIntent(normalizedProductId);
            }

            StoreIapAnalyticsIntentSaveData intent =
                FindPendingIapAnalyticsIntent(normalizedProductId);
            if (intent != null && intent.levelDisplayedNumber == levelDisplayedNumber &&
                intent.levelNumber == levelNumber && intent.isFailOffer == isFailOffer)
            {
                return false;
            }

            if (intent == null)
            {
                if (data.store.pendingIapAnalyticsIntents.Count >=
                    MaxProcessedIapTransactions)
                {
                    return false;
                }

                intent = new StoreIapAnalyticsIntentSaveData
                {
                    productId = normalizedProductId
                };
                data.store.pendingIapAnalyticsIntents.Add(intent);
            }

            intent.levelDisplayedNumber = levelDisplayedNumber;
            intent.levelNumber = levelNumber;
            intent.isFailOffer = isFailOffer;
            MarkDirty();
            return true;
        }

        internal bool ClearPendingIapAnalyticsIntent(string productId)
        {
            EnsureInitialized();
            string normalizedProductId = NormalizeIdentifier(productId);
            if (normalizedProductId.Length == 0)
            {
                return false;
            }

            for (int i = data.store.pendingIapAnalyticsIntents.Count - 1; i >= 0; i--)
            {
                StoreIapAnalyticsIntentSaveData intent =
                    data.store.pendingIapAnalyticsIntents[i];
                if (intent != null && string.Equals(
                        intent.productId,
                        normalizedProductId,
                        StringComparison.Ordinal))
                {
                    data.store.pendingIapAnalyticsIntents.RemoveAt(i);
                    MarkDirty();
                    return true;
                }
            }

            return false;
        }

        internal bool BindIapAnalyticsTransaction(
            string transactionId,
            string productId,
            double localizedPriceValue,
            string currency,
            int fallbackLevelDisplayedNumber,
            int fallbackLevelNumber,
            bool fallbackIsFailOffer)
        {
            EnsureInitialized();
            string normalizedTransactionId = NormalizeIdentifier(transactionId);
            string normalizedProductId = NormalizeIdentifier(productId);
            if (normalizedTransactionId.Length == 0 || normalizedProductId.Length == 0)
            {
                return false;
            }

            bool hasCompletePriceMetadata = TryNormalizeIapPriceMetadata(
                localizedPriceValue,
                currency,
                out double normalizedPrice,
                out string normalizedCurrency);

            StoreIapAnalyticsTransactionSaveData existing =
                FindIapAnalyticsTransaction(normalizedTransactionId);
            if (existing != null)
            {
                if (!string.Equals(
                        existing.productId,
                        normalizedProductId,
                        StringComparison.Ordinal))
                {
                    return false;
                }

                bool changed = false;
                if (hasCompletePriceMetadata &&
                    (!existing.localizedPriceValue.Equals(normalizedPrice) ||
                     !string.Equals(
                         existing.currency,
                         normalizedCurrency,
                         StringComparison.Ordinal)))
                {
                    existing.localizedPriceValue = normalizedPrice;
                    existing.currency = normalizedCurrency;
                    changed = true;
                }

                changed |= ClearMatchingPendingIapAnalyticsIntent(normalizedProductId);
                if (changed)
                {
                    MarkDirty();
                }

                return true;
            }

            StoreIapAnalyticsIntentSaveData intent =
                FindPendingIapAnalyticsIntent(normalizedProductId);
            bool hasMatchingIntent = intent != null && intent.levelDisplayedNumber > 0 &&
                intent.levelNumber > 0;
            int levelDisplayedNumber = hasMatchingIntent
                ? intent.levelDisplayedNumber
                : fallbackLevelDisplayedNumber;
            int levelNumber = hasMatchingIntent ? intent.levelNumber : fallbackLevelNumber;
            bool isFailOffer = hasMatchingIntent ? intent.isFailOffer : fallbackIsFailOffer;
            if (levelDisplayedNumber <= 0 || levelNumber <= 0)
            {
                // Attribution revenue is also valid for purchases outside gameplay.
                levelDisplayedNumber = 0;
                levelNumber = 0;
            }

            if (data.store.iapAnalyticsTransactions.Count >= MaxProcessedIapTransactions)
            {
                int removableIndex = FindOldestCompletedIapAnalyticsTransactionIndex();
                if (removableIndex < 0)
                {
                    return false;
                }

                data.store.iapAnalyticsTransactions.RemoveAt(removableIndex);
            }

            data.store.iapAnalyticsTransactions.Add(
                new StoreIapAnalyticsTransactionSaveData
                {
                    transactionId = normalizedTransactionId,
                    productId = normalizedProductId,
                    levelDisplayedNumber = levelDisplayedNumber,
                    levelNumber = levelNumber,
                    isFailOffer = isFailOffer,
                    levelIapPurchaseAccepted = levelDisplayedNumber == 0,
                    failOfferPurchasedAccepted = levelDisplayedNumber == 0,
                    localizedPriceValue = hasCompletePriceMetadata
                        ? normalizedPrice
                        : 0d,
                    currency = hasCompletePriceMetadata
                        ? normalizedCurrency
                        : string.Empty
                });
            ClearMatchingPendingIapAnalyticsIntent(normalizedProductId);
            MarkDirty();
            return true;
        }

        internal bool MarkIapAnalyticsTransactionConfirmed(
            string transactionId,
            string productId,
            double localizedPriceValue,
            string currency)
        {
            EnsureInitialized();
            StoreIapAnalyticsTransactionSaveData entry =
                FindIapAnalyticsTransaction(NormalizeIdentifier(transactionId));
            string normalizedProductId = NormalizeIdentifier(productId);
            if (entry == null || normalizedProductId.Length == 0 || !string.Equals(
                    entry.productId,
                    normalizedProductId,
                    StringComparison.Ordinal))
            {
                return false;
            }

            bool changed = false;
            if (TryNormalizeIapPriceMetadata(
                    localizedPriceValue,
                    currency,
                    out double normalizedPrice,
                    out string normalizedCurrency) &&
                (!entry.localizedPriceValue.Equals(normalizedPrice) ||
                 !string.Equals(
                     entry.currency,
                     normalizedCurrency,
                     StringComparison.Ordinal)))
            {
                entry.localizedPriceValue = normalizedPrice;
                entry.currency = normalizedCurrency;
                changed = true;
            }

            if (!entry.confirmed)
            {
                entry.confirmed = true;
                changed = true;
            }

            if (changed)
            {
                MarkDirty();
            }

            return changed;
        }

        internal void CaptureTenjinPurchase(string transactionId, string receipt, string signature, int quantity)
        {
            EnsureInitialized();
            var entry = FindIapAnalyticsTransaction(NormalizeIdentifier(transactionId));
            if (entry == null || entry.confirmed || entry.tenjinPurchaseDispatchReserved ||
                IsIapTransactionProcessed(transactionId) || string.IsNullOrEmpty(receipt) || quantity <= 0) return;
            entry.tenjinPurchasePending = true;
            entry.tenjinReceipt = receipt;
            entry.tenjinSignature = signature ?? string.Empty;
            entry.tenjinQuantity = quantity;
            MarkDirty();
        }

        internal bool ReserveTenjinPurchaseDispatch(string transactionId)
        {
            var entry = FindIapAnalyticsTransaction(NormalizeIdentifier(transactionId));
            if (entry == null || !entry.confirmed || !entry.tenjinPurchasePending || entry.tenjinPurchaseDispatchReserved) return false;
            entry.tenjinPurchasePending = false;
            entry.tenjinPurchaseDispatchReserved = true;
            MarkDirty();
            if (TrySave()) return true;
            entry.tenjinPurchasePending = true;
            entry.tenjinPurchaseDispatchReserved = false;
            MarkDirty();
            return false;
        }

        internal void ClearTenjinReceipt(string transactionId)
        {
            var entry = FindIapAnalyticsTransaction(NormalizeIdentifier(transactionId));
            if (entry == null) return;
            entry.tenjinReceipt = string.Empty;
            entry.tenjinSignature = string.Empty;
            MarkDirty();
            TrySave();
        }

        internal IReadOnlyList<StoreIapAnalyticsTransactionSaveData>
            GetIapAnalyticsTransactions()
        {
            EnsureInitialized();
            return data.store.iapAnalyticsTransactions;
        }

        internal bool HasIapAnalyticsTransaction(
            string transactionId,
            string productId)
        {
            EnsureInitialized();
            StoreIapAnalyticsTransactionSaveData entry =
                FindIapAnalyticsTransaction(NormalizeIdentifier(transactionId));
            return entry != null && string.Equals(
                entry.productId,
                NormalizeIdentifier(productId),
                StringComparison.Ordinal);
        }

        internal bool MarkIapAnalyticsEventAccepted(
            string transactionId,
            bool failOfferPurchasedEvent)
        {
            EnsureInitialized();
            StoreIapAnalyticsTransactionSaveData entry =
                FindIapAnalyticsTransaction(NormalizeIdentifier(transactionId));
            if (entry == null || !entry.confirmed)
            {
                return false;
            }

            if (failOfferPurchasedEvent)
            {
                if (!entry.isFailOffer || entry.failOfferPurchasedAccepted)
                {
                    return false;
                }

                entry.failOfferPurchasedAccepted = true;
            }
            else
            {
                if (entry.levelIapPurchaseAccepted)
                {
                    return false;
                }

                entry.levelIapPurchaseAccepted = true;
            }

            MarkDirty();
            return true;
        }

        internal bool TryGetIapGameplayFulfillment(
            string transactionId,
            out string productId,
            out string failedSessionToken,
            out bool gameplayRewardApplied)
        {
            EnsureInitialized();
            string normalizedId = NormalizeIdentifier(transactionId);
            for (int i = 0; i < data.store.iapGameplayFulfillments.Count; i++)
            {
                StoreGameplayFulfillmentSaveData fulfillment =
                    data.store.iapGameplayFulfillments[i];
                if (fulfillment != null && string.Equals(
                        fulfillment.transactionId,
                        normalizedId,
                        StringComparison.Ordinal))
                {
                    productId = fulfillment.productId ?? string.Empty;
                    failedSessionToken = fulfillment.failedSessionToken ?? string.Empty;
                    gameplayRewardApplied = fulfillment.gameplayRewardApplied;
                    return true;
                }
            }

            productId = string.Empty;
            failedSessionToken = string.Empty;
            gameplayRewardApplied = false;
            return false;
        }

        internal void SetIapGameplayFulfillment(
            string transactionId,
            string productId,
            string failedSessionToken,
            bool gameplayRewardApplied)
        {
            EnsureInitialized();
            string normalizedId = NormalizeIdentifier(transactionId);
            if (normalizedId.Length == 0)
            {
                throw new ArgumentException(
                    "A gameplay fulfillment requires a transaction ID.",
                    nameof(transactionId));
            }

            for (int i = 0; i < data.store.iapGameplayFulfillments.Count; i++)
            {
                StoreGameplayFulfillmentSaveData existing =
                    data.store.iapGameplayFulfillments[i];
                if (existing == null || !string.Equals(
                        existing.transactionId,
                        normalizedId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                string normalizedProductId = NormalizeIdentifier(productId);
                string normalizedSessionToken = NormalizeIdentifier(failedSessionToken);
                if (string.Equals(existing.productId, normalizedProductId, StringComparison.Ordinal) &&
                    string.Equals(existing.failedSessionToken, normalizedSessionToken, StringComparison.Ordinal) &&
                    existing.gameplayRewardApplied == gameplayRewardApplied)
                {
                    return;
                }

                existing.productId = normalizedProductId;
                existing.failedSessionToken = normalizedSessionToken;
                existing.gameplayRewardApplied = gameplayRewardApplied;
                MarkDirty();
                return;
            }

            data.store.iapGameplayFulfillments.Add(
                new StoreGameplayFulfillmentSaveData
                {
                    transactionId = normalizedId,
                    productId = NormalizeIdentifier(productId),
                    failedSessionToken = NormalizeIdentifier(failedSessionToken),
                    gameplayRewardApplied = gameplayRewardApplied
                });
            while (data.store.iapGameplayFulfillments.Count > MaxProcessedIapTransactions)
            {
                int removableIndex = -1;
                for (int i = 0; i < data.store.iapGameplayFulfillments.Count; i++)
                {
                    if (!IsIapTransactionUnconfirmed(
                            data.store.iapGameplayFulfillments[i].transactionId))
                    {
                        removableIndex = i;
                        break;
                    }
                }

                if (removableIndex < 0)
                {
                    break;
                }

                data.store.iapGameplayFulfillments.RemoveAt(removableIndex);
            }

            MarkDirty();
        }

        internal bool GrantFailOfferRecoveryContinueCredit()
        {
            EnsureInitialized();
            if (data.store.pendingFailOfferContinueCredits == int.MaxValue)
            {
                return false;
            }

            data.store.pendingFailOfferContinueCredits++;
            MarkDirty();
            StoreStateChanged?.Invoke();
            return true;
        }

        internal bool TryReserveFailOfferRecoveryContinueCredit()
        {
            EnsureInitialized();
            if (data.store.pendingFailOfferContinueCredits <= 0 ||
                data.store.reservedFailOfferContinueCredits == int.MaxValue)
            {
                return false;
            }

            data.store.pendingFailOfferContinueCredits--;
            data.store.reservedFailOfferContinueCredits++;
            MarkDirty();
            StoreStateChanged?.Invoke();
            return true;
        }

        internal bool CompleteReservedFailOfferRecoveryContinueCredit()
        {
            EnsureInitialized();
            if (data.store.reservedFailOfferContinueCredits <= 0)
            {
                return false;
            }

            data.store.reservedFailOfferContinueCredits--;
            MarkDirty();
            StoreStateChanged?.Invoke();
            return true;
        }

        internal bool ReleaseReservedFailOfferRecoveryContinueCredit()
        {
            EnsureInitialized();
            if (data.store.reservedFailOfferContinueCredits <= 0 ||
                data.store.pendingFailOfferContinueCredits == int.MaxValue)
            {
                return false;
            }

            data.store.reservedFailOfferContinueCredits--;
            data.store.pendingFailOfferContinueCredits++;
            MarkDirty();
            StoreStateChanged?.Invoke();
            return true;
        }

        public int ProcessedIapTransactionCount
        {
            get
            {
                EnsureInitialized();
                return data.store.processedIapTransactions.Count;
            }
        }

        private void RemoveIapGameplayFulfillmentInternal(string transactionId)
        {
            if (data?.store?.iapGameplayFulfillments == null)
            {
                return;
            }

            for (int i = data.store.iapGameplayFulfillments.Count - 1; i >= 0; i--)
            {
                StoreGameplayFulfillmentSaveData fulfillment =
                    data.store.iapGameplayFulfillments[i];
                if (fulfillment != null && string.Equals(
                        fulfillment.transactionId,
                        transactionId,
                        StringComparison.Ordinal))
                {
                    data.store.iapGameplayFulfillments.RemoveAt(i);
                }
            }
        }

        private int FindOldestConfirmedTransactionIndex()
        {
            for (int i = 0; i < data.store.processedIapTransactions.Count; i++)
            {
                string transactionId = data.store.processedIapTransactions[i];
                bool unconfirmed = false;
                for (int j = 0; j < data.store.unconfirmedIapTransactions.Count; j++)
                {
                    StoreUnconfirmedTransactionSaveData entry =
                        data.store.unconfirmedIapTransactions[j];
                    if (entry != null && string.Equals(
                            entry.transactionId,
                            transactionId,
                            StringComparison.Ordinal))
                    {
                        unconfirmed = true;
                        break;
                    }
                }

                if (!unconfirmed)
                {
                    return i;
                }
            }

            return -1;
        }

        private bool IsIapTransactionUnconfirmed(string transactionId)
        {
            for (int i = 0; i < data.store.unconfirmedIapTransactions.Count; i++)
            {
                StoreUnconfirmedTransactionSaveData entry =
                    data.store.unconfirmedIapTransactions[i];
                if (entry != null && string.Equals(
                        entry.transactionId,
                        transactionId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private StoreIapAnalyticsTransactionSaveData FindIapAnalyticsTransaction(
            string transactionId)
        {
            for (int i = 0; i < data.store.iapAnalyticsTransactions.Count; i++)
            {
                StoreIapAnalyticsTransactionSaveData entry =
                    data.store.iapAnalyticsTransactions[i];
                if (entry != null && string.Equals(
                        entry.transactionId,
                        transactionId,
                        StringComparison.Ordinal))
                {
                    return entry;
                }
            }

            return null;
        }

        private bool ClearMatchingPendingIapAnalyticsIntent(string productId)
        {
            for (int i = data.store.pendingIapAnalyticsIntents.Count - 1; i >= 0; i--)
            {
                StoreIapAnalyticsIntentSaveData intent =
                    data.store.pendingIapAnalyticsIntents[i];
                if (intent != null && string.Equals(
                        intent.productId,
                        productId,
                        StringComparison.Ordinal))
                {
                    data.store.pendingIapAnalyticsIntents.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        private StoreIapAnalyticsIntentSaveData FindPendingIapAnalyticsIntent(
            string productId)
        {
            for (int i = 0; i < data.store.pendingIapAnalyticsIntents.Count; i++)
            {
                StoreIapAnalyticsIntentSaveData intent =
                    data.store.pendingIapAnalyticsIntents[i];
                if (intent != null && string.Equals(
                        intent.productId,
                        productId,
                        StringComparison.Ordinal))
                {
                    return intent;
                }
            }

            return null;
        }

        private int FindOldestCompletedIapAnalyticsTransactionIndex()
        {
            for (int i = 0; i < data.store.iapAnalyticsTransactions.Count; i++)
            {
                StoreIapAnalyticsTransactionSaveData entry =
                    data.store.iapAnalyticsTransactions[i];
                if (entry != null && entry.confirmed && !entry.tenjinPurchasePending && entry.levelIapPurchaseAccepted &&
                    (!entry.isFailOffer || entry.failOfferPurchasedAccepted))
                {
                    return i;
                }
            }

            return -1;
        }

        #endregion

        #region Level API

        public void SetCurrentLevel(int level)
        {
            EnsureInitialized();

            int normalizedLevel = Mathf.Max(1, level);
            if (data.level.currentLevel == normalizedLevel)
            {
                return;
            }

            data.level.currentLevel = normalizedLevel;
            MarkDirty();

            // Level progression was immediately persisted by the previous SaveManager.
            Save();
            CurrentLevelChanged?.Invoke(normalizedLevel);
        }

        public void SetLastLoopContentLevelNumber(int levelNumber)
        {
            EnsureInitialized();

            int normalizedLevelNumber = Mathf.Max(0, levelNumber);
            if (data.level.lastLoopContentLevelNumber == normalizedLevelNumber)
            {
                return;
            }

            data.level.lastLoopContentLevelNumber = normalizedLevelNumber;
            MarkDirty();

            // Retains the former immediate-persistence behavior.
            Save();
        }

        public void SetPendingNextLevel(int level)
        {
            EnsureInitialized();

            if (level < 1)
            {
                ClearPendingNextLevel();
                return;
            }

            if (data.level.hasPendingNextLevel && data.level.pendingNextLevel == level)
            {
                return;
            }

            data.level.hasPendingNextLevel = true;
            data.level.pendingNextLevel = level;
            MarkDirty();
        }

        public void ClearPendingNextLevel()
        {
            EnsureInitialized();

            if (!data.level.hasPendingNextLevel && data.level.pendingNextLevel == 0)
            {
                return;
            }

            data.level.hasPendingNextLevel = false;
            data.level.pendingNextLevel = 0;
            MarkDirty();
        }

        public bool IsLevelCompleted(int levelNumber)
        {
            EnsureInitialized();
            LevelProgressSaveData progress = FindLevelProgress(data, levelNumber);
            return progress != null && progress.completed;
        }

        public void SetLevelCompleted(int levelNumber, bool completed = true)
        {
            EnsureInitialized();
            if (levelNumber < 1)
            {
                return;
            }

            LevelProgressSaveData progress = FindLevelProgress(data, levelNumber);
            if (progress == null)
            {
                progress = new LevelProgressSaveData
                {
                    levelNumber = levelNumber,
                    completed = completed
                };
                data.level.levels.Add(progress);
                MarkDirty();
                return;
            }

            if (progress.completed == completed)
            {
                return;
            }

            progress.completed = completed;
            MarkDirty();
        }

        #endregion

        #region Gold API

        public void AddGold(int amount)
        {
            EnsureInitialized();
            if (amount <= 0)
            {
                return;
            }

            SetGold(SaturatingAdd(data.gold.amount, amount));
        }

        public bool SpendGold(int amount)
        {
            EnsureInitialized();
            if (amount < 0 || data.gold.amount < amount)
            {
                return false;
            }

            if (amount == 0)
            {
                return true;
            }

            data.gold.amount -= amount;
            MarkDirty();
            GoldChanged?.Invoke(data.gold.amount);
            AudioManager.Instance?.PlaySfx(AudioKey.CoinSpend);
            return true;
        }

        public void SetGold(int amount)
        {
            EnsureInitialized();

            int normalizedAmount = Mathf.Max(0, amount);
            if (data.gold.amount == normalizedAmount)
            {
                return;
            }

            data.gold.amount = normalizedAmount;
            MarkDirty();
            GoldChanged?.Invoke(normalizedAmount);
        }

        #endregion

        #region Lives API

        public bool TrySpendLife(int amount = 1)
        {
            EnsureInitialized();
            if (amount < 0 || data.lives.current < amount)
            {
                return false;
            }

            if (amount == 0)
            {
                return true;
            }

            data.lives.current -= amount;
            MarkDirty();
            LivesChanged?.Invoke(data.lives.current, data.lives.max);
            return true;
        }

        public void AddLives(int amount)
        {
            EnsureInitialized();
            if (amount <= 0)
            {
                return;
            }

            long addedLives = (long)data.lives.current + amount;
            int normalizedLives = (int)Math.Min(data.lives.max, addedLives);
            SetLives(normalizedLives);
        }

        public void SetLives(int amount)
        {
            EnsureInitialized();

            int normalizedLives = Mathf.Clamp(amount, 0, data.lives.max);
            if (data.lives.current == normalizedLives)
            {
                return;
            }

            data.lives.current = normalizedLives;
            MarkDirty();
            LivesChanged?.Invoke(data.lives.current, data.lives.max);
        }

        public void SetLivesState(int amount, long nextRefillUtcUnixSeconds)
        {
            EnsureInitialized();
            SetLivesState(amount, data.lives.max, nextRefillUtcUnixSeconds);
        }

        public void SetLivesState(int amount, int maximum, long nextRefillUtcUnixSeconds)
        {
            EnsureInitialized();

            int normalizedMax = Mathf.Max(0, maximum);
            int normalizedLives = Mathf.Clamp(amount, 0, normalizedMax);
            long normalizedNextRefillUtc = Math.Max(0, nextRefillUtcUnixSeconds);

            bool livesChanged =
                data.lives.current != normalizedLives ||
                data.lives.max != normalizedMax;
            bool refillChanged = data.lives.nextRefillUtc != normalizedNextRefillUtc;
            if (!livesChanged && !refillChanged)
            {
                return;
            }

            data.lives.current = normalizedLives;
            data.lives.max = normalizedMax;
            data.lives.nextRefillUtc = normalizedNextRefillUtc;
            MarkDirty();

            if (livesChanged)
            {
                LivesChanged?.Invoke(data.lives.current, data.lives.max);
            }
        }

        #endregion

        #region Booster API

        public int GetBoosterAmount(string id)
        {
            EnsureInitialized();
            BoosterSaveData booster = FindBooster(data, NormalizeIdentifier(id));
            return booster != null ? booster.amount : 0;
        }

        public bool IsBoosterUnlocked(string id)
        {
            EnsureInitialized();
            BoosterSaveData booster = FindBooster(data, NormalizeIdentifier(id));
            return booster != null && booster.unlocked;
        }

        public void AddBooster(string id, int amount = 1)
        {
            EnsureInitialized();

            string normalizedId = NormalizeIdentifier(id);
            if (normalizedId.Length == 0 || amount <= 0)
            {
                return;
            }

            BoosterSaveData booster = FindBooster(data, normalizedId);
            int currentAmount = booster != null ? booster.amount : 0;
            SetBoosterAmount(normalizedId, SaturatingAdd(currentAmount, amount));
        }

        public bool UseBooster(string id, int amount = 1)
        {
            EnsureInitialized();

            string normalizedId = NormalizeIdentifier(id);
            if (normalizedId.Length == 0 || amount < 0)
            {
                return false;
            }

            BoosterSaveData booster = FindBooster(data, normalizedId);
            if (booster == null || booster.amount < amount)
            {
                return false;
            }

            if (amount == 0)
            {
                return true;
            }

            booster.amount -= amount;
            MarkDirty();
            BoosterAmountChanged?.Invoke(normalizedId, booster.amount);
            return true;
        }

        public void SetBoosterAmount(string id, int amount)
        {
            EnsureInitialized();

            string normalizedId = NormalizeIdentifier(id);
            if (normalizedId.Length == 0)
            {
                return;
            }

            int normalizedAmount = Mathf.Max(0, amount);
            BoosterSaveData booster = GetOrCreateBooster(normalizedId, out bool created);
            if (!created && booster.amount == normalizedAmount)
            {
                return;
            }

            int previousAmount = booster.amount;
            booster.amount = normalizedAmount;
            MarkDirty();

            if (previousAmount != normalizedAmount)
            {
                BoosterAmountChanged?.Invoke(normalizedId, normalizedAmount);
            }
        }

        public void SetBoosterUnlocked(string id, bool unlocked)
        {
            EnsureInitialized();

            string normalizedId = NormalizeIdentifier(id);
            if (normalizedId.Length == 0)
            {
                return;
            }

            BoosterSaveData booster = GetOrCreateBooster(normalizedId, out bool created);
            if (!created && booster.unlocked == unlocked)
            {
                return;
            }

            booster.unlocked = unlocked;
            MarkDirty();
        }

        #endregion

        #region Settings API

        public bool GetBoolSetting(string key, bool defaultValue = false)
        {
            EnsureInitialized();
            return GetBoolSettingFromData(data, NormalizeIdentifier(key), defaultValue);
        }

        public int GetIntSetting(string key, int defaultValue = 0)
        {
            EnsureInitialized();
            SettingSaveData setting = FindSetting(data, NormalizeIdentifier(key));
            return setting != null && setting.type == SaveValueType.Int
                ? setting.intValue
                : defaultValue;
        }

        public float GetFloatSetting(string key, float defaultValue = 0f)
        {
            EnsureInitialized();
            SettingSaveData setting = FindSetting(data, NormalizeIdentifier(key));
            return setting != null && setting.type == SaveValueType.Float
                ? setting.floatValue
                : defaultValue;
        }

        public string GetStringSetting(string key, string defaultValue = "")
        {
            EnsureInitialized();
            SettingSaveData setting = FindSetting(data, NormalizeIdentifier(key));
            return setting != null && setting.type == SaveValueType.String
                ? setting.stringValue ?? string.Empty
                : defaultValue;
        }

        public void SetSetting(string key, bool value)
        {
            EnsureInitialized();
            if (TrySetBoolSetting(key, value, out string normalizedKey))
            {
                MarkDirty();
                PublishSettingChanged(normalizedKey);
            }
        }

        public void SetSetting(string key, int value)
        {
            EnsureInitialized();
            if (TrySetIntSetting(key, value, out string normalizedKey))
            {
                MarkDirty();
                PublishSettingChanged(normalizedKey);
            }
        }

        public void SetSetting(string key, float value)
        {
            EnsureInitialized();
            if (TrySetFloatSetting(key, value, out string normalizedKey))
            {
                MarkDirty();
                PublishSettingChanged(normalizedKey);
            }
        }

        public void SetSetting(string key, string value)
        {
            EnsureInitialized();
            if (TrySetStringSetting(key, value, out string normalizedKey))
            {
                MarkDirty();
                PublishSettingChanged(normalizedKey);
            }
        }

        public void SetSoundEnabled(bool enabled)
        {
            EnsureInitialized();
            if (!TrySetBoolSetting(SaveKeys.SoundEnabled, enabled, out string normalizedKey))
            {
                return;
            }

            MarkDirty();
            Save();
            PublishSettingChanged(normalizedKey);
        }

        public void SetHapticEnabled(bool enabled)
        {
            EnsureInitialized();
            if (!TrySetBoolSetting(SaveKeys.HapticEnabled, enabled, out string normalizedKey))
            {
                return;
            }

            MarkDirty();
            Save();
            PublishSettingChanged(normalizedKey);
        }

        #endregion

        #region Load Flow

        private void LoadOrCreateSave(bool publishEvents)
        {
            GameSaveData previousData = data;
            GameSaveData loadedData = null;

            bool primaryExists = false;
            bool backupExists = false;
            bool storageUnavailable = false;
            bool unsupportedVersionFound = false;
            bool requiresSave = false;
            bool recoveredFromBackup = false;
            bool createdDefaults = false;

            writeBlockedByUnsupportedVersion = false;
            blockedWriteWasLogged = false;
            backupRestorePending = false;

            try
            {
                primaryExists = saveStorage.Exists();
                backupExists = primaryExists && saveStorage.BackupExists();
            }
            catch (Exception exception)
            {
                storageUnavailable = true;
                Debug.LogError($"Save load failed while checking storage: {exception.Message}", this);
            }

            if (!storageUnavailable && primaryExists)
            {
                if (!TryLoadCandidate(
                        false,
                        out loadedData,
                        out requiresSave,
                        out bool unsupportedVersion,
                        out string error))
                {
                    unsupportedVersionFound |= unsupportedVersion;
                    Debug.LogError(
                        unsupportedVersion
                            ? $"Unsupported primary save schema: {error} " +
                              "The primary file was left unchanged, backup recovery was skipped, " +
                              "and save writes are blocked."
                            : $"Primary save load failed: {error}",
                        this);
                }
            }

            if (loadedData == null &&
                !storageUnavailable &&
                primaryExists &&
                !unsupportedVersionFound &&
                backupExists)
            {
                if (TryLoadCandidate(
                        true,
                        out loadedData,
                        out requiresSave,
                        out bool unsupportedVersion,
                        out string error))
                {
                    recoveredFromBackup = true;
                    unsupportedVersionFound = false;

                    try
                    {
                        saveStorage.RestoreBackup();
                        Debug.LogWarning("Save recovered from backup.", this);
                    }
                    catch (Exception exception)
                    {
                        backupRestorePending = true;
                        Debug.LogError($"Backup save was readable but could not be restored: {exception.Message}", this);
                    }
                }
                else
                {
                    unsupportedVersionFound |= unsupportedVersion;
                    Debug.LogError(
                        unsupportedVersion
                            ? $"Unsupported backup save schema: {error} Save writes are blocked."
                            : $"Backup save load failed: {error}",
                        this);
                }
            }

            if (loadedData == null)
            {
                if (!storageUnavailable && !primaryExists && !unsupportedVersionFound)
                {
                    DeleteLegacyPlayerPrefsSaveKeys();
                }

                loadedData = GameSaveDataFactory.CreateDefault();
                createdDefaults = true;

                if (!storageUnavailable && !unsupportedVersionFound)
                {
                    requiresSave = true;
                }
            }

            data = loadedData;
            IsInitialized = true;
            writeBlockedByUnsupportedVersion = storageUnavailable || unsupportedVersionFound;
            isDirty = requiresSave || backupRestorePending;

            bool writeSucceeded = !requiresSave || WriteCurrentData();

            if (createdDefaults && requiresSave && writeSucceeded)
            {
                Debug.Log("New save.json created with default data.", this);
            }

            if (recoveredFromBackup && backupRestorePending)
            {
                // Keep runtime data usable. A later Save will retry restoring the backup first.
                isDirty = true;
            }

            if (publishEvents)
            {
                PublishLoadedEvents(previousData);
            }
        }

        private bool TryLoadCandidate(
            bool backup,
            out GameSaveData loadedData,
            out bool requiresSave,
            out bool unsupportedVersion,
            out string error)
        {
            loadedData = null;
            requiresSave = false;
            unsupportedVersion = false;
            error = string.Empty;

            try
            {
                string json = backup ? saveStorage.LoadBackup() : saveStorage.Load();
                return SaveJsonSerializer.TryFromJson(
                    json,
                    out loadedData,
                    out requiresSave,
                    out unsupportedVersion,
                    out error);
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private bool WriteCurrentData()
        {
            if (data == null)
            {
                data = GameSaveDataFactory.CreateDefault();
                isDirty = true;
            }

            if (writeBlockedByUnsupportedVersion)
            {
                if (!blockedWriteWasLogged)
                {
                    Debug.LogError(
                        "Save write is blocked to avoid overwriting an unsupported or inaccessible save. " +
                        "Use ResetSave only if replacing it is intentional.",
                        this);
                    blockedWriteWasLogged = true;
                }

                return false;
            }

            if (!SaveJsonSerializer.IsSchemaVersionSupported(
                    data.version,
                    out string schemaError))
            {
                writeBlockedByUnsupportedVersion = true;
                blockedWriteWasLogged = true;
                isDirty = true;
                Debug.LogError(
                    $"Save write blocked: {schemaError} Existing save files were not changed. " +
                    "Use ResetSave only if replacing them is intentional.",
                    this);
                return false;
            }

            try
            {
                if (backupRestorePending)
                {
                    saveStorage.RestoreBackup();
                    backupRestorePending = false;
                    Debug.LogWarning("Save recovered from backup.", this);
                }

                bool normalizationChanged = GameSaveDataNormalizer.Normalize(data);
                if (normalizationChanged)
                {
                    isDirty = true;
                }

                saveStorage.Save(SaveJsonSerializer.ToJson(data, ShouldPrettyPrintJson));
                isDirty = false;
                return true;
            }
            catch (Exception exception)
            {
                isDirty = true;
                Debug.LogError($"Save write failed: {exception.Message}", this);
                return false;
            }
        }

        private static void DeleteLegacyPlayerPrefsSaveKeys()
        {
            try
            {
                bool changed = false;
                changed |= DeletePlayerPrefsKeyIfPresent(SaveKeys.LegacyCurrentLevelPlayerPrefs);
                changed |= DeletePlayerPrefsKeyIfPresent(SaveKeys.HapticEnabled);
                changed |= DeletePlayerPrefsKeyIfPresent(SaveKeys.SoundEnabled);
                changed |= DeletePlayerPrefsKeyIfPresent(
                    SaveKeys.LegacyHasPendingNextLevelPlayerPrefs);
                changed |= DeletePlayerPrefsKeyIfPresent(
                    SaveKeys.LegacyPendingNextLevelPlayerPrefs);
                changed |= DeletePlayerPrefsKeyIfPresent(
                    SaveKeys.LegacyLastLoopContentLevelNumberPlayerPrefs);
                changed |= DeletePlayerPrefsKeyIfPresent(SaveKeys.LegacySaveHasDataPlayerPrefs);
                changed |= DeletePlayerPrefsKeyIfPresent(SaveKeys.LegacySaveVersionPlayerPrefs);

                if (changed)
                {
                    PlayerPrefs.Save();
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Old save PlayerPrefs keys could not be cleared: {exception.Message}");
            }
        }

        private static bool DeletePlayerPrefsKeyIfPresent(string key)
        {
            if (!PlayerPrefs.HasKey(key))
            {
                return false;
            }

            PlayerPrefs.DeleteKey(key);
            return true;
        }

        #endregion

        #region Event Helpers

        private void PublishLoadedEvents(GameSaveData previousData)
        {
            OnSaveLoaded?.Invoke();

            if (previousData?.level == null ||
                previousData.level.currentLevel != data.level.currentLevel)
            {
                CurrentLevelChanged?.Invoke(data.level.currentLevel);
            }

            if (previousData?.gold == null || previousData.gold.amount != data.gold.amount)
            {
                GoldChanged?.Invoke(data.gold.amount);
            }

            if (previousData?.lives == null ||
                previousData.lives.current != data.lives.current ||
                previousData.lives.max != data.lives.max)
            {
                LivesChanged?.Invoke(data.lives.current, data.lives.max);
            }

            PublishBoosterDifferences(previousData, data);
            PublishSettingDifferences(previousData, data);

            if (previousData?.store == null ||
                previousData.store.hasNoAds != data.store.hasNoAds ||
                previousData.store.noAdsIntroShown != data.store.noAdsIntroShown ||
                previousData.store.starterPackPurchased !=
                data.store.starterPackPurchased ||
                previousData.store.infiniteLivesEndUtc != data.store.infiniteLivesEndUtc)
            {
                StoreStateChanged?.Invoke();
            }
        }

        private void PublishBoosterDifferences(GameSaveData previousData, GameSaveData currentData)
        {
            for (int i = 0; i < currentData.boosters.Count; i++)
            {
                BoosterSaveData current = currentData.boosters[i];
                BoosterSaveData previous = FindBooster(previousData, current.id);
                bool amountChanged = previous != null
                    ? previous.amount != current.amount
                    : previousData == null || current.amount != 0;

                if (amountChanged)
                {
                    BoosterAmountChanged?.Invoke(current.id, current.amount);
                }
            }

            if (previousData?.boosters == null)
            {
                return;
            }

            for (int i = 0; i < previousData.boosters.Count; i++)
            {
                BoosterSaveData previous = previousData.boosters[i];
                if (previous != null &&
                    previous.amount != 0 &&
                    FindBooster(currentData, previous.id) == null)
                {
                    BoosterAmountChanged?.Invoke(previous.id, 0);
                }
            }
        }

        private void PublishSettingDifferences(GameSaveData previousData, GameSaveData currentData)
        {
            for (int i = 0; i < currentData.settings.Count; i++)
            {
                SettingSaveData current = currentData.settings[i];
                SettingSaveData previous = FindSetting(previousData, current.key);
                if (previous == null || !AreSettingValuesEqual(previous, current))
                {
                    PublishSettingChanged(current.key);
                }
            }

            if (previousData?.settings == null)
            {
                return;
            }

            for (int i = 0; i < previousData.settings.Count; i++)
            {
                SettingSaveData previous = previousData.settings[i];
                if (previous != null && FindSetting(currentData, previous.key) == null)
                {
                    PublishSettingChanged(previous.key);
                }
            }
        }

        private void PublishSettingChanged(string key)
        {
            SettingChanged?.Invoke(key);

            if (string.Equals(key, SaveKeys.SoundEnabled, StringComparison.Ordinal))
            {
                OnSoundEnabledChanged?.Invoke(SoundEnabled);
            }
            else if (string.Equals(key, SaveKeys.HapticEnabled, StringComparison.Ordinal))
            {
                OnHapticEnabledChanged?.Invoke(HapticEnabled);
            }
        }

        #endregion

        #region Data Helpers

        private bool TrySetBoolSetting(string key, bool value, out string normalizedKey)
        {
            normalizedKey = NormalizeIdentifier(key);
            if (normalizedKey.Length == 0)
            {
                return false;
            }

            SettingSaveData setting = GetOrCreateSetting(normalizedKey, out bool created);
            bool changed = created ||
                           setting.type != SaveValueType.Bool ||
                           setting.boolValue != value;

            if (!changed)
            {
                return false;
            }

            ClearSettingValues(setting);
            setting.type = SaveValueType.Bool;
            setting.boolValue = value;
            return true;
        }

        private bool TrySetIntSetting(string key, int value, out string normalizedKey)
        {
            normalizedKey = NormalizeIdentifier(key);
            if (normalizedKey.Length == 0)
            {
                return false;
            }

            SettingSaveData setting = GetOrCreateSetting(normalizedKey, out bool created);
            bool changed = created ||
                           setting.type != SaveValueType.Int ||
                           setting.intValue != value;

            if (!changed)
            {
                return false;
            }

            ClearSettingValues(setting);
            setting.type = SaveValueType.Int;
            setting.intValue = value;
            return true;
        }

        private bool TrySetFloatSetting(string key, float value, out string normalizedKey)
        {
            normalizedKey = NormalizeIdentifier(key);
            if (normalizedKey.Length == 0)
            {
                return false;
            }

            SettingSaveData setting = GetOrCreateSetting(normalizedKey, out bool created);
            bool changed = created ||
                           setting.type != SaveValueType.Float ||
                           !setting.floatValue.Equals(value);

            if (!changed)
            {
                return false;
            }

            ClearSettingValues(setting);
            setting.type = SaveValueType.Float;
            setting.floatValue = value;
            return true;
        }

        private bool TrySetStringSetting(string key, string value, out string normalizedKey)
        {
            normalizedKey = NormalizeIdentifier(key);
            if (normalizedKey.Length == 0)
            {
                return false;
            }

            string normalizedValue = value ?? string.Empty;
            SettingSaveData setting = GetOrCreateSetting(normalizedKey, out bool created);
            bool changed = created ||
                           setting.type != SaveValueType.String ||
                           !string.Equals(setting.stringValue, normalizedValue, StringComparison.Ordinal);

            if (!changed)
            {
                return false;
            }

            ClearSettingValues(setting);
            setting.type = SaveValueType.String;
            setting.stringValue = normalizedValue;
            return true;
        }

        private SettingSaveData GetOrCreateSetting(string key, out bool created)
        {
            SettingSaveData setting = FindSetting(data, key);
            if (setting != null)
            {
                created = false;
                return setting;
            }

            setting = new SettingSaveData
            {
                key = key,
                stringValue = string.Empty
            };
            data.settings.Add(setting);
            created = true;
            return setting;
        }

        private BoosterSaveData GetOrCreateBooster(string id, out bool created)
        {
            BoosterSaveData booster = FindBooster(data, id);
            if (booster != null)
            {
                created = false;
                return booster;
            }

            booster = new BoosterSaveData
            {
                id = id,
                amount = 0,
                unlocked = false
            };
            data.boosters.Add(booster);
            created = true;
            return booster;
        }

        private static LevelProgressSaveData FindLevelProgress(GameSaveData source, int levelNumber)
        {
            if (source?.level?.levels == null || levelNumber < 1)
            {
                return null;
            }

            for (int i = 0; i < source.level.levels.Count; i++)
            {
                LevelProgressSaveData progress = source.level.levels[i];
                if (progress != null && progress.levelNumber == levelNumber)
                {
                    return progress;
                }
            }

            return null;
        }

        private static BoosterSaveData FindBooster(GameSaveData source, string id)
        {
            if (source?.boosters == null || string.IsNullOrEmpty(id))
            {
                return null;
            }

            for (int i = 0; i < source.boosters.Count; i++)
            {
                BoosterSaveData booster = source.boosters[i];
                if (booster != null && string.Equals(booster.id, id, StringComparison.Ordinal))
                {
                    return booster;
                }
            }

            return null;
        }

        private static SettingSaveData FindSetting(GameSaveData source, string key)
        {
            if (source?.settings == null || string.IsNullOrEmpty(key))
            {
                return null;
            }

            for (int i = 0; i < source.settings.Count; i++)
            {
                SettingSaveData setting = source.settings[i];
                if (setting != null && string.Equals(setting.key, key, StringComparison.Ordinal))
                {
                    return setting;
                }
            }

            return null;
        }

        private static bool GetBoolSettingFromData(
            GameSaveData source,
            string key,
            bool defaultValue)
        {
            SettingSaveData setting = FindSetting(source, key);
            return setting != null && setting.type == SaveValueType.Bool
                ? setting.boolValue
                : defaultValue;
        }

        private static bool AreSettingValuesEqual(SaveValueData left, SaveValueData right)
        {
            return left.type == right.type &&
                   left.boolValue == right.boolValue &&
                   left.intValue == right.intValue &&
                   left.floatValue.Equals(right.floatValue) &&
                   string.Equals(left.stringValue, right.stringValue, StringComparison.Ordinal);
        }

        private static void ClearSettingValues(SaveValueData setting)
        {
            setting.boolValue = false;
            setting.intValue = 0;
            setting.floatValue = 0f;
            setting.stringValue = string.Empty;
        }

        private static string NormalizeIdentifier(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static bool TryNormalizeIapPriceMetadata(
            double localizedPriceValue,
            string currency,
            out double normalizedPrice,
            out string normalizedCurrency)
        {
            normalizedPrice = 0d;
            normalizedCurrency = NormalizeIdentifier(currency);
            if (double.IsNaN(localizedPriceValue) ||
                double.IsInfinity(localizedPriceValue) ||
                localizedPriceValue < 0d ||
                normalizedCurrency.Length == 0)
            {
                normalizedCurrency = string.Empty;
                return false;
            }

            normalizedPrice = localizedPriceValue;
            return true;
        }

        private static int SaturatingAdd(int current, int amount)
        {
            long result = (long)current + amount;
            return result >= int.MaxValue ? int.MaxValue : (int)result;
        }

        private void MarkDirty()
        {
            isDirty = true;
        }

        private void SaveIfDirty()
        {
            if (IsInitialized && isDirty)
            {
                WriteCurrentData();
            }
        }

        private void EnsureInitialized()
        {
            if (!IsInitialized)
            {
                Initialize();
            }
        }

        private void EnsureServices()
        {
            saveStorage ??= new JsonFileSaveStorage();
        }

        private static bool ShouldPrettyPrintJson
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return true;
#else
                return false;
#endif
            }
        }

        #endregion
    }
}
