using System;
using System.Collections.Generic;

namespace Game.Shared.Save
{
    public static class GameSaveDataNormalizer
    {
        public static bool Normalize(GameSaveData data)
        {
            if (data == null)
            {
                return false;
            }

            bool changed = EnsureRootData(data);
            changed |= NormalizeLevelData(data.level);
            changed |= NormalizeLives(data.lives);
            changed |= NormalizeGold(data.gold);
            changed |= NormalizeStore(data.store);
            changed |= NormalizeBoosters(data.boosters);
            changed |= NormalizeSettings(data.settings);
            changed |= GameSaveDataFactory.AddMissingDefaultEntries(data);
            return changed;
        }

        private static bool EnsureRootData(GameSaveData data)
        {
            bool changed = false;

            if (data.level == null)
            {
                data.level = new LevelSaveData();
                changed = true;
            }

            if (data.lives == null)
            {
                data.lives = new LivesSaveData();
                changed = true;
            }

            if (data.gold == null)
            {
                data.gold = new GoldSaveData();
                changed = true;
            }

            if (data.store == null)
            {
                data.store = new StoreSaveData();
                changed = true;
            }

            if (data.boosters == null)
            {
                data.boosters = new List<BoosterSaveData>();
                changed = true;
            }

            if (data.settings == null)
            {
                data.settings = new List<SettingSaveData>();
                changed = true;
            }

            return changed;
        }

        private static bool NormalizeLevelData(LevelSaveData level)
        {
            bool changed = false;

            if (level.currentLevel < 1)
            {
                level.currentLevel = GameSaveDataFactory.InitialCurrentLevel;
                changed = true;
            }

            if (level.pendingNextLevel < 0)
            {
                level.pendingNextLevel = 0;
                changed = true;
            }

            if (level.hasPendingNextLevel && level.pendingNextLevel < 1)
            {
                level.hasPendingNextLevel = false;
                changed = true;
            }

            if (level.lastLoopContentLevelNumber < 0)
            {
                level.lastLoopContentLevelNumber =
                    GameSaveDataFactory.InitialLastLoopContentLevelNumber;
                changed = true;
            }

            if (level.levels == null)
            {
                level.levels = new List<LevelProgressSaveData>();
                changed = true;
            }

            Dictionary<int, LevelProgressSaveData> uniqueLevels =
                new Dictionary<int, LevelProgressSaveData>();

            for (int i = 0; i < level.levels.Count; i++)
            {
                LevelProgressSaveData progress = level.levels[i];
                if (progress == null || progress.levelNumber < 1)
                {
                    level.levels.RemoveAt(i--);
                    changed = true;
                    continue;
                }

                changed |= NormalizeValues(progress.values, out List<SaveValueData> normalizedValues);
                progress.values = normalizedValues;

                if (!uniqueLevels.TryGetValue(
                        progress.levelNumber,
                        out LevelProgressSaveData existing))
                {
                    uniqueLevels.Add(progress.levelNumber, progress);
                    continue;
                }

                if (progress.completed && !existing.completed)
                {
                    existing.completed = true;
                }

                MergeValues(existing.values, progress.values);
                level.levels.RemoveAt(i--);
                changed = true;
            }

            return changed;
        }

        private static bool NormalizeLives(LivesSaveData lives)
        {
            bool changed = false;

            if (lives.max < 0)
            {
                lives.max = 0;
                changed = true;
            }

            int normalizedCurrent = Math.Max(0, Math.Min(lives.current, lives.max));
            if (lives.current != normalizedCurrent)
            {
                lives.current = normalizedCurrent;
                changed = true;
            }

            if (lives.nextRefillUtc < 0)
            {
                lives.nextRefillUtc = 0;
                changed = true;
            }

            return changed;
        }

        private static bool NormalizeGold(GoldSaveData gold)
        {
            if (gold.amount >= 0)
            {
                return false;
            }

            gold.amount = 0;
            return true;
        }

        private static bool NormalizeStore(StoreSaveData store)
        {
            bool changed = false;

            if (store.infiniteLivesEndUtc < 0)
            {
                store.infiniteLivesEndUtc = 0;
                changed = true;
            }

            if (store.pendingFailOfferContinueCredits < 0)
            {
                store.pendingFailOfferContinueCredits = 0;
                changed = true;
            }

            if (store.reservedFailOfferContinueCredits < 0)
            {
                store.reservedFailOfferContinueCredits = 0;
                changed = true;
            }

            // A reservation only spans the synchronous "save -> continue -> save" delivery
            // window. If the process stopped in that window, the gameplay continuation did not
            // survive the restart, so return the durable value to the pending recovery pool.
            if (store.reservedFailOfferContinueCredits > 0)
            {
                long recoveredCredits =
                    (long)store.pendingFailOfferContinueCredits +
                    store.reservedFailOfferContinueCredits;
                store.pendingFailOfferContinueCredits =
                    recoveredCredits > int.MaxValue
                        ? int.MaxValue
                        : (int)recoveredCredits;
                store.reservedFailOfferContinueCredits = 0;
                changed = true;
            }

            if (store.processedIapTransactions == null)
            {
                store.processedIapTransactions = new List<string>();
                changed = true;
            }

            HashSet<string> uniqueTransactions = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < store.processedIapTransactions.Count; i++)
            {
                string normalizedId = NormalizeKey(store.processedIapTransactions[i]);
                if (normalizedId.Length == 0 || !uniqueTransactions.Add(normalizedId))
                {
                    store.processedIapTransactions.RemoveAt(i--);
                    changed = true;
                    continue;
                }

                if (!string.Equals(
                        store.processedIapTransactions[i],
                        normalizedId,
                        StringComparison.Ordinal))
                {
                    store.processedIapTransactions[i] = normalizedId;
                    changed = true;
                }
            }

            if (store.unconfirmedIapTransactions == null)
            {
                store.unconfirmedIapTransactions =
                    new List<StoreUnconfirmedTransactionSaveData>();
                changed = true;
            }

            HashSet<string> uniqueUnconfirmedTransactions =
                new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < store.unconfirmedIapTransactions.Count; i++)
            {
                StoreUnconfirmedTransactionSaveData entry =
                    store.unconfirmedIapTransactions[i];
                string transactionId = NormalizeKey(entry?.transactionId);
                string productId = NormalizeKey(entry?.productId);
                if (entry == null || transactionId.Length == 0 || productId.Length == 0 ||
                    !uniqueTransactions.Contains(transactionId) ||
                    !uniqueUnconfirmedTransactions.Add(transactionId))
                {
                    store.unconfirmedIapTransactions.RemoveAt(i--);
                    changed = true;
                    continue;
                }

                if (!string.Equals(entry.transactionId, transactionId, StringComparison.Ordinal) ||
                    !string.Equals(entry.productId, productId, StringComparison.Ordinal))
                {
                    entry.transactionId = transactionId;
                    entry.productId = productId;
                    changed = true;
                }
            }

            while (store.unconfirmedIapTransactions.Count >
                   SaveManager.MaxProcessedIapTransactions)
            {
                string removedId = store.unconfirmedIapTransactions[0].transactionId;
                store.unconfirmedIapTransactions.RemoveAt(0);
                uniqueUnconfirmedTransactions.Remove(removedId);
                changed = true;
            }

            while (store.processedIapTransactions.Count > SaveManager.MaxProcessedIapTransactions)
            {
                int removableIndex = -1;
                for (int i = 0; i < store.processedIapTransactions.Count; i++)
                {
                    if (!uniqueUnconfirmedTransactions.Contains(
                            store.processedIapTransactions[i]))
                    {
                        removableIndex = i;
                        break;
                    }
                }

                if (removableIndex < 0)
                {
                    break;
                }

                string removedId = store.processedIapTransactions[removableIndex];
                store.processedIapTransactions.RemoveAt(removableIndex);
                uniqueTransactions.Remove(removedId);
                changed = true;
            }

            if (store.iapGameplayFulfillments == null)
            {
                store.iapGameplayFulfillments =
                    new List<StoreGameplayFulfillmentSaveData>();
                changed = true;
            }

            HashSet<string> uniqueGameplayTransactions =
                new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < store.iapGameplayFulfillments.Count; i++)
            {
                StoreGameplayFulfillmentSaveData fulfillment =
                    store.iapGameplayFulfillments[i];
                string transactionId = NormalizeKey(fulfillment?.transactionId);
                if (fulfillment == null || transactionId.Length == 0 ||
                    !uniqueGameplayTransactions.Add(transactionId) ||
                    !uniqueTransactions.Contains(transactionId))
                {
                    store.iapGameplayFulfillments.RemoveAt(i--);
                    changed = true;
                    continue;
                }

                string productId = NormalizeKey(fulfillment.productId);
                string sessionToken = NormalizeKey(fulfillment.failedSessionToken);
                if (!string.Equals(
                        fulfillment.transactionId,
                        transactionId,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        fulfillment.productId,
                        productId,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        fulfillment.failedSessionToken,
                        sessionToken,
                        StringComparison.Ordinal))
                {
                    fulfillment.transactionId = transactionId;
                    fulfillment.productId = productId;
                    fulfillment.failedSessionToken = sessionToken;
                    changed = true;
                }
            }

            while (store.iapGameplayFulfillments.Count > SaveManager.MaxProcessedIapTransactions)
            {
                int removableIndex = -1;
                for (int i = 0; i < store.iapGameplayFulfillments.Count; i++)
                {
                    if (!uniqueUnconfirmedTransactions.Contains(
                            store.iapGameplayFulfillments[i].transactionId))
                    {
                        removableIndex = i;
                        break;
                    }
                }

                if (removableIndex < 0)
                {
                    break;
                }

                store.iapGameplayFulfillments.RemoveAt(removableIndex);
                changed = true;
            }

            if (store.pendingIapAnalyticsIntents == null)
            {
                store.pendingIapAnalyticsIntents =
                    new List<StoreIapAnalyticsIntentSaveData>();
                changed = true;
            }

            HashSet<string> uniqueIntentProducts =
                new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < store.pendingIapAnalyticsIntents.Count; i++)
            {
                StoreIapAnalyticsIntentSaveData intent =
                    store.pendingIapAnalyticsIntents[i];
                string productId = NormalizeKey(intent?.productId);
                if (intent == null || productId.Length == 0 ||
                    intent.levelDisplayedNumber <= 0 || intent.levelNumber <= 0 ||
                    !uniqueIntentProducts.Add(productId))
                {
                    store.pendingIapAnalyticsIntents.RemoveAt(i--);
                    changed = true;
                    continue;
                }

                if (!string.Equals(intent.productId, productId, StringComparison.Ordinal))
                {
                    intent.productId = productId;
                    changed = true;
                }
            }

            if (store.iapAnalyticsTransactions == null)
            {
                store.iapAnalyticsTransactions =
                    new List<StoreIapAnalyticsTransactionSaveData>();
                changed = true;
            }

            HashSet<string> uniqueAnalyticsTransactions =
                new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < store.iapAnalyticsTransactions.Count; i++)
            {
                StoreIapAnalyticsTransactionSaveData entry =
                    store.iapAnalyticsTransactions[i];
                string transactionId = NormalizeKey(entry?.transactionId);
                string productId = NormalizeKey(entry?.productId);
                if (entry == null || transactionId.Length == 0 || productId.Length == 0 ||
                    entry.levelDisplayedNumber < 0 || entry.levelNumber < 0 ||
                    !uniqueAnalyticsTransactions.Add(transactionId))
                {
                    store.iapAnalyticsTransactions.RemoveAt(i--);
                    changed = true;
                    continue;
                }

                string currency = NormalizeKey(entry.currency);
                if (!string.Equals(entry.transactionId, transactionId, StringComparison.Ordinal) ||
                    !string.Equals(entry.productId, productId, StringComparison.Ordinal) ||
                    !string.Equals(entry.currency, currency, StringComparison.Ordinal))
                {
                    entry.transactionId = transactionId;
                    entry.productId = productId;
                    entry.currency = currency;
                    changed = true;
                }

                if (entry.localizedPriceValue < 0d)
                {
                    entry.localizedPriceValue = 0d;
                    changed = true;
                }
            }

            while (store.iapAnalyticsTransactions.Count >
                   SaveManager.MaxProcessedIapTransactions)
            {
                int removableIndex = FindCompletedIapAnalyticsTransactionIndex(
                    store.iapAnalyticsTransactions);
                if (removableIndex < 0)
                {
                    break;
                }

                store.iapAnalyticsTransactions.RemoveAt(removableIndex);
                changed = true;
            }

            return changed;
        }

        private static int FindCompletedIapAnalyticsTransactionIndex(
            IReadOnlyList<StoreIapAnalyticsTransactionSaveData> transactions)
        {
            for (int i = 0; i < transactions.Count; i++)
            {
                StoreIapAnalyticsTransactionSaveData entry = transactions[i];
                if (entry != null && entry.confirmed && !entry.tenjinPurchasePending && entry.levelIapPurchaseAccepted &&
                    (!entry.isFailOffer || entry.failOfferPurchasedAccepted))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool NormalizeBoosters(List<BoosterSaveData> boosters)
        {
            bool changed = false;
            Dictionary<string, BoosterSaveData> uniqueBoosters =
                new Dictionary<string, BoosterSaveData>(StringComparer.Ordinal);

            for (int i = 0; i < boosters.Count; i++)
            {
                BoosterSaveData booster = boosters[i];
                if (booster == null)
                {
                    boosters.RemoveAt(i--);
                    changed = true;
                    continue;
                }

                string normalizedId = NormalizeKey(booster.id);
                if (normalizedId.Length == 0)
                {
                    boosters.RemoveAt(i--);
                    changed = true;
                    continue;
                }

                if (!string.Equals(booster.id, normalizedId, StringComparison.Ordinal))
                {
                    booster.id = normalizedId;
                    changed = true;
                }

                if (booster.amount < 0)
                {
                    booster.amount = 0;
                    changed = true;
                }

                changed |= NormalizeValues(booster.values, out List<SaveValueData> normalizedValues);
                booster.values = normalizedValues;

                if (!uniqueBoosters.TryGetValue(booster.id, out BoosterSaveData existing))
                {
                    uniqueBoosters.Add(booster.id, booster);
                    continue;
                }

                existing.amount = Math.Max(existing.amount, booster.amount);
                existing.unlocked |= booster.unlocked;
                MergeValues(existing.values, booster.values);
                boosters.RemoveAt(i--);
                changed = true;
            }

            return changed;
        }

        private static bool NormalizeSettings(List<SettingSaveData> settings)
        {
            bool changed = false;
            HashSet<string> uniqueKeys = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < settings.Count; i++)
            {
                SettingSaveData setting = settings[i];
                if (setting == null)
                {
                    settings.RemoveAt(i--);
                    changed = true;
                    continue;
                }

                string normalizedKey = NormalizeKey(setting.key);
                if (normalizedKey.Length == 0 || !uniqueKeys.Add(normalizedKey))
                {
                    settings.RemoveAt(i--);
                    changed = true;
                    continue;
                }

                if (!string.Equals(setting.key, normalizedKey, StringComparison.Ordinal))
                {
                    setting.key = normalizedKey;
                    changed = true;
                }

                changed |= NormalizeValue(setting);
            }

            return changed;
        }

        private static bool NormalizeValues(
            List<SaveValueData> values,
            out List<SaveValueData> normalizedValues)
        {
            bool changed = false;
            normalizedValues = values;

            if (normalizedValues == null)
            {
                normalizedValues = new List<SaveValueData>();
                return true;
            }

            HashSet<string> uniqueKeys = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < normalizedValues.Count; i++)
            {
                SaveValueData value = normalizedValues[i];
                if (value == null)
                {
                    normalizedValues.RemoveAt(i--);
                    changed = true;
                    continue;
                }

                string normalizedKey = NormalizeKey(value.key);
                if (normalizedKey.Length == 0 || !uniqueKeys.Add(normalizedKey))
                {
                    normalizedValues.RemoveAt(i--);
                    changed = true;
                    continue;
                }

                if (!string.Equals(value.key, normalizedKey, StringComparison.Ordinal))
                {
                    value.key = normalizedKey;
                    changed = true;
                }

                changed |= NormalizeValue(value);
            }

            return changed;
        }

        private static bool NormalizeValue(SaveValueData value)
        {
            bool changed = false;

            int rawType = (int)value.type;
            if (rawType < (int)SaveValueType.Bool || rawType > (int)SaveValueType.String)
            {
                value.type = SaveValueType.String;
                changed = true;
            }

            if (value.stringValue == null)
            {
                value.stringValue = string.Empty;
                changed = true;
            }

            switch (value.type)
            {
                case SaveValueType.Bool:
                    changed |= ClearIntValue(value);
                    changed |= ClearFloatValue(value);
                    changed |= ClearStringValue(value);
                    break;

                case SaveValueType.Int:
                    changed |= ClearBoolValue(value);
                    changed |= ClearFloatValue(value);
                    changed |= ClearStringValue(value);
                    break;

                case SaveValueType.Float:
                    changed |= ClearBoolValue(value);
                    changed |= ClearIntValue(value);
                    changed |= ClearStringValue(value);
                    break;

                case SaveValueType.String:
                    changed |= ClearBoolValue(value);
                    changed |= ClearIntValue(value);
                    changed |= ClearFloatValue(value);
                    break;
            }

            return changed;
        }

        private static bool ClearBoolValue(SaveValueData value)
        {
            if (!value.boolValue)
            {
                return false;
            }

            value.boolValue = false;
            return true;
        }

        private static bool ClearIntValue(SaveValueData value)
        {
            if (value.intValue == 0)
            {
                return false;
            }

            value.intValue = 0;
            return true;
        }

        private static bool ClearFloatValue(SaveValueData value)
        {
            if (value.floatValue.Equals(0f))
            {
                return false;
            }

            value.floatValue = 0f;
            return true;
        }

        private static bool ClearStringValue(SaveValueData value)
        {
            if (string.IsNullOrEmpty(value.stringValue))
            {
                return false;
            }

            value.stringValue = string.Empty;
            return true;
        }

        private static void MergeValues(List<SaveValueData> target, List<SaveValueData> source)
        {
            if (target == null || source == null || source.Count == 0)
            {
                return;
            }

            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < target.Count; i++)
            {
                SaveValueData value = target[i];
                if (value != null)
                {
                    keys.Add(value.key);
                }
            }

            for (int i = 0; i < source.Count; i++)
            {
                SaveValueData value = source[i];
                if (value != null && keys.Add(value.key))
                {
                    target.Add(value);
                }
            }
        }

        private static string NormalizeKey(string key)
        {
            return string.IsNullOrWhiteSpace(key) ? string.Empty : key.Trim();
        }
    }
}
