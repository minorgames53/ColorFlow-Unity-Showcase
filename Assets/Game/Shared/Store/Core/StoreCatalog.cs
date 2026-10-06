using System;
using System.Collections.Generic;

namespace Game.Shared.Store
{
    public static class StoreCatalog
    {
        private static readonly StoreProductDefinition[] Definitions =
        {
            CoinPack(StoreProductIds.Coins1000, 1000),
            CoinPack(StoreProductIds.Coins5000, 5000),
            CoinPack(StoreProductIds.Coins10000, 10000),
            CoinPack(StoreProductIds.Coins25000, 25000),
            CoinPack(StoreProductIds.Coins50000, 50000),
            CoinPack(StoreProductIds.Coins100000, 100000),
            new StoreProductDefinition(
                StoreProductIds.NoAds,
                "No Ads",
                StoreProductType.NonConsumable,
                false,
                new StoreReward(StoreRewardType.NoAds)),
            new StoreProductDefinition(
                StoreProductIds.StarterPack,
                "Starter Pack",
                StoreProductType.Consumable,
                true,
                new StoreReward(StoreRewardType.Coin, 2500),
                new StoreReward(StoreRewardType.Hand),
                new StoreReward(StoreRewardType.Shuffle),
                new StoreReward(StoreRewardType.Ufo),
                new StoreReward(StoreRewardType.InfiniteLives, 60)),
            new StoreProductDefinition(
                StoreProductIds.FailOffer,
                "Fail Offer",
                StoreProductType.Consumable,
                false,
                new StoreReward(StoreRewardType.Coin, 1500),
                new StoreReward(StoreRewardType.Hand),
                new StoreReward(StoreRewardType.Shuffle),
                new StoreReward(StoreRewardType.Ufo),
                new StoreReward(StoreRewardType.ContinueLevel))
        };

        private static readonly Dictionary<string, StoreProductDefinition> ById =
            BuildLookup();

        public static IReadOnlyList<StoreProductDefinition> Products => Definitions;

        public static bool TryGet(string productId, out StoreProductDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(productId))
            {
                definition = null;
                return false;
            }

            return ById.TryGetValue(productId.Trim(), out definition);
        }

        private static StoreProductDefinition CoinPack(string productId, int amount)
        {
            return new StoreProductDefinition(
                productId,
                productId,
                StoreProductType.Consumable,
                false,
                new StoreReward(StoreRewardType.Coin, amount));
        }

        private static Dictionary<string, StoreProductDefinition> BuildLookup()
        {
            Dictionary<string, StoreProductDefinition> lookup =
                new Dictionary<string, StoreProductDefinition>(StringComparer.Ordinal);
            for (int i = 0; i < Definitions.Length; i++)
            {
                lookup.Add(Definitions[i].ProductId, Definitions[i]);
            }

            return lookup;
        }
    }
}
