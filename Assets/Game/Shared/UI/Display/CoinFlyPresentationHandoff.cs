using UnityEngine;

namespace Game.Shared.UI
{
    public static class CoinFlyPresentationHandoff
    {
        private static bool pendingCoinFly;
        private static int rewardAmount;
        private static bool requestFirstAppReviewAfterPresentation;
        private static bool requestNoAdsOfferAfterPresentation;

        public static bool HasPendingPresentation => pendingCoinFly;
        public static bool HasPendingFirstAppReview =>
            pendingCoinFly && requestFirstAppReviewAfterPresentation;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            pendingCoinFly = false;
            rewardAmount = 0;
            requestFirstAppReviewAfterPresentation = false;
            requestNoAdsOfferAfterPresentation = false;
        }

        public static void Defer(
            int amount,
            bool requestFirstAppReview = false,
            bool requestNoAdsOffer = false)
        {
            pendingCoinFly = amount > 0;
            rewardAmount = pendingCoinFly ? amount : 0;
            requestFirstAppReviewAfterPresentation =
                pendingCoinFly && requestFirstAppReview;
            requestNoAdsOfferAfterPresentation = requestNoAdsOffer;
        }

        public static bool TryConsumeNoAdsOfferRequest()
        {
            bool shouldOpen = requestNoAdsOfferAfterPresentation;
            requestNoAdsOfferAfterPresentation = false;
            return shouldOpen;
        }

        public static void Cancel()
        {
            ResetRuntimeState();
        }

        public static bool TryConsume(out int amount)
        {
            return TryConsume(out amount, out _);
        }

        public static bool TryConsume(
            out int amount,
            out bool requestFirstAppReview)
        {
            amount = rewardAmount;
            requestFirstAppReview = requestFirstAppReviewAfterPresentation;
            bool shouldPlay = pendingCoinFly && amount > 0;
            pendingCoinFly = false;
            rewardAmount = 0;
            requestFirstAppReviewAfterPresentation = false;
            return shouldPlay;
        }
    }
}
