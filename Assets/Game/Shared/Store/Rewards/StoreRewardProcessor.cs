using System;
using Game.Shared.Lives;
using Game.Shared.Save;
using UnityEngine;

namespace Game.Shared.Store
{
    public readonly struct StoreRewardContext
    {
        public StoreRewardContext(bool hasFailedSession, string failedSessionToken)
        {
            HasFailedSession = hasFailedSession;
            FailedSessionToken = failedSessionToken ?? string.Empty;
        }

        public bool HasFailedSession { get; }
        public string FailedSessionToken { get; }
    }

    public sealed class StoreRewardProcessor
    {
        private const string HandBoosterId = "hand";
        private const string ShuffleBoosterId = "shuffle";
        private const string UfoBoosterId = "ufo";

        private readonly SaveManager saveManager;
        private readonly LivesService livesService;

        public StoreRewardProcessor(SaveManager saveManager, LivesService livesService)
        {
            this.saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
            this.livesService = livesService ?? throw new ArgumentNullException(nameof(livesService));
        }

        public void ApplyPersistentRewards(StoreProductDefinition product)
        {
            if (product == null)
            {
                throw new ArgumentNullException(nameof(product));
            }

            for (int i = 0; i < product.Rewards.Count; i++)
            {
                StoreReward reward = product.Rewards[i];
                switch (reward.Type)
                {
                    case StoreRewardType.Coin:
                        saveManager.AddGold(reward.Amount);
                        break;

                    case StoreRewardType.Hand:
                        saveManager.AddBooster(HandBoosterId, reward.Amount);
                        break;

                    case StoreRewardType.Shuffle:
                        saveManager.AddBooster(ShuffleBoosterId, reward.Amount);
                        break;

                    case StoreRewardType.Ufo:
                        saveManager.AddBooster(UfoBoosterId, reward.Amount);
                        break;

                    case StoreRewardType.InfiniteLives:
                        livesService.AddInfiniteLivesForStore(TimeSpan.FromMinutes(reward.Amount));
                        break;

                    case StoreRewardType.NoAds:
                        saveManager.SetHasNoAds(true);
                        break;

                    case StoreRewardType.ContinueLevel:
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(reward.Type),
                            reward.Type,
                            "Unsupported store reward type.");
                }
            }

            if (product.LocallyOneTime)
            {
                saveManager.SetStarterPackPurchased(true);
            }
        }

        public bool ApplyGameplayRewards(
            StoreProductDefinition product,
            StoreRewardContext context)
        {
            bool success = true;
            for (int i = 0; i < product.Rewards.Count; i++)
            {
                if (product.Rewards[i].Type != StoreRewardType.ContinueLevel)
                {
                    continue;
                }

                if (!context.HasFailedSession ||
                    !StoreGameplayActionRegistry.TryContinueFailedSession(
                        context.FailedSessionToken))
                {
                    Debug.LogWarning(
                        $"[Store] Continue reward for '{product.ProductId}' could not be applied because the captured fail session is no longer valid.");
                    success = false;
                }
            }

            return success;
        }
    }
}
