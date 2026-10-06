using System;
using Game.Shared.Save;

namespace Game.Shared.Lives
{
    public enum LivesPurchaseResult
    {
        Success,
        InsufficientGold,
        NoRefillNeeded,
        UnlimitedLives
    }

    public sealed class LivesPurchaseService
    {
        private readonly SaveManager saveManager;
        private readonly LivesService livesService;

        public LivesPurchaseService(SaveManager saveManager, LivesService livesService)
        {
            this.saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
            this.livesService = livesService ?? throw new ArgumentNullException(nameof(livesService));
        }

        public int MissingLives => Math.Max(0, livesService.MaxLives - livesService.CurrentLives);

        public int FillToMaxGoldCost
        {
            get
            {
                long cost = (long)MissingLives * LivesEconomyConfig.LifeGoldCost;
                return cost > int.MaxValue ? int.MaxValue : (int)cost;
            }
        }

        public LivesPurchaseResult TryFillToMaxWithGold()
        {
            if (livesService.HasInfiniteLives)
            {
                return LivesPurchaseResult.UnlimitedLives;
            }

            int missingLives = MissingLives;
            if (missingLives <= 0)
            {
                return LivesPurchaseResult.NoRefillNeeded;
            }

            int goldCost = FillToMaxGoldCost;
            if (!saveManager.SpendGold(goldCost))
            {
                return LivesPurchaseResult.InsufficientGold;
            }

            livesService.AddLives(missingLives);
            return LivesPurchaseResult.Success;
        }

        public void GrantRewardedLives()
        {
            if (livesService.HasInfiniteLives || livesService.IsFull)
            {
                return;
            }

            livesService.AddLives(LivesEconomyConfig.RewardedAdLifeGain);
        }
    }
}
