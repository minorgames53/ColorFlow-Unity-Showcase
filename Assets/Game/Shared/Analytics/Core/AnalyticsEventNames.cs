namespace Game.Shared.Analytics.Core
{
    public static class AnalyticsEventNames
    {
        public const string BootCompleted = "boot_completed";
        public const string BootFailed = "boot_failed";

        public const string LevelStarted = "level_started";
        public const string LevelCompleted = "level_completed";
        public const string LevelFailed = "level_failed";
        public const string LevelExited = "level_exited";

        public const string FailOfferShown = "fail_offer_shown";
        public const string FailOfferClicked = "fail_offer_clicked";
        public const string FailOfferPurchased = "fail_offer_purchased";
        public const string BoosterUsed = "booster_used";
        public const string BoosterPurchased = "booster_purchased";
        public const string CleanupUsed = "cleanup_used";
        public const string RewardedAdRequested = "rewarded_ad_requested";
        public const string RewardedAdCompleted = "rewarded_ad_completed";
        public const string RewardedAdFailed = "rewarded_ad_failed";
        public const string LevelIapPurchase = "level_iap_purchase";
    }

    public static class AnalyticsParameterNames
    {
        public const string DurationSeconds = "duration_seconds";
        public const string FailureStep = "failure_step";
        public const string ErrorCode = "error_code";
        public const string LevelDisplayedNumber = "level_displayed_number";
        public const string LevelNumber = "level_number";
        public const string AttemptNumber = "attempt_number";
        public const string ProgressPercent = "progress_percent";
        public const string BoosterType = "booster_type";
        public const string AdPlacement = "ad_placement";
        public const string ProductId = "product_id";
        public const string Value = "value";
        public const string Currency = "currency";
    }

    public static class AnalyticsAdPlacements
    {
        public const string RewardedLife = "rewarded_life";
        public const string RewardedDoubleGold = "rewarded_double_gold";
    }

    public static class BootFailureSteps
    {
        public const string Initialization = "initialization";
    }

    public static class BootErrorCodes
    {
        public const string InitializationFailed = "initialization_failed";
    }

    public static class AnalyticsEventFactory
    {
        public static AnalyticsEvent CreateBootCompleted(double durationSeconds)
        {
            return new AnalyticsEvent(
                AnalyticsEventNames.BootCompleted,
                new System.Collections.Generic.Dictionary<string, object>
                {
                    { AnalyticsParameterNames.DurationSeconds, NormalizeDuration(durationSeconds) }
                });
        }

        public static AnalyticsEvent CreateBootFailed(double durationSeconds, string failureStep, string errorCode)
        {
            return new AnalyticsEvent(
                AnalyticsEventNames.BootFailed,
                new System.Collections.Generic.Dictionary<string, object>
                {
                    { AnalyticsParameterNames.DurationSeconds, NormalizeDuration(durationSeconds) },
                    { AnalyticsParameterNames.FailureStep, failureStep },
                    { AnalyticsParameterNames.ErrorCode, errorCode }
                });
        }

        public static AnalyticsEvent CreateLevelStarted(int levelDisplayedNumber, int levelNumber, int attemptNumber)
        {
            return new AnalyticsEvent(
                AnalyticsEventNames.LevelStarted,
                new System.Collections.Generic.Dictionary<string, object>
                {
                    { AnalyticsParameterNames.LevelDisplayedNumber, levelDisplayedNumber },
                    { AnalyticsParameterNames.LevelNumber, levelNumber },
                    { AnalyticsParameterNames.AttemptNumber, attemptNumber }
                });
        }

        public static AnalyticsEvent CreateLevelCompleted(int levelDisplayedNumber, int levelNumber, int attemptNumber, double durationSeconds)
        {
            return new AnalyticsEvent(
                AnalyticsEventNames.LevelCompleted,
                new System.Collections.Generic.Dictionary<string, object>
                {
                    { AnalyticsParameterNames.LevelDisplayedNumber, levelDisplayedNumber },
                    { AnalyticsParameterNames.LevelNumber, levelNumber },
                    { AnalyticsParameterNames.AttemptNumber, attemptNumber },
                    { AnalyticsParameterNames.DurationSeconds, NormalizeDuration(durationSeconds) }
                });
        }

        public static AnalyticsEvent CreateLevelFailed(int levelDisplayedNumber, int levelNumber, int attemptNumber, double durationSeconds, int progressPercent)
        {
            return CreateTerminalLevelEvent(
                AnalyticsEventNames.LevelFailed,
                levelDisplayedNumber,
                levelNumber,
                attemptNumber,
                durationSeconds,
                progressPercent);
        }

        public static AnalyticsEvent CreateLevelExited(int levelDisplayedNumber, int levelNumber, int attemptNumber, double durationSeconds, int progressPercent)
        {
            return CreateTerminalLevelEvent(
                AnalyticsEventNames.LevelExited,
                levelDisplayedNumber,
                levelNumber,
                attemptNumber,
                durationSeconds,
                progressPercent);
        }

        public static AnalyticsEvent CreateFailOfferShown(int levelDisplayedNumber, int levelNumber)
        {
            return CreateLevelContextEvent(
                AnalyticsEventNames.FailOfferShown,
                levelDisplayedNumber,
                levelNumber);
        }

        public static AnalyticsEvent CreateFailOfferClicked(int levelDisplayedNumber, int levelNumber)
        {
            return CreateLevelContextEvent(
                AnalyticsEventNames.FailOfferClicked,
                levelDisplayedNumber,
                levelNumber);
        }

        public static AnalyticsEvent CreateFailOfferPurchased(int levelDisplayedNumber, int levelNumber)
        {
            return CreateLevelContextEvent(
                AnalyticsEventNames.FailOfferPurchased,
                levelDisplayedNumber,
                levelNumber);
        }

        public static AnalyticsEvent CreateBoosterUsed(
            int levelDisplayedNumber,
            int levelNumber,
            string boosterType)
        {
            return CreateBoosterEvent(
                AnalyticsEventNames.BoosterUsed,
                levelDisplayedNumber,
                levelNumber,
                boosterType);
        }

        public static AnalyticsEvent CreateBoosterPurchased(
            int levelDisplayedNumber,
            int levelNumber,
            string boosterType)
        {
            return CreateBoosterEvent(
                AnalyticsEventNames.BoosterPurchased,
                levelDisplayedNumber,
                levelNumber,
                boosterType);
        }

        public static AnalyticsEvent CreateCleanupUsed(int levelDisplayedNumber, int levelNumber)
        {
            return CreateLevelContextEvent(
                AnalyticsEventNames.CleanupUsed,
                levelDisplayedNumber,
                levelNumber);
        }

        public static AnalyticsEvent CreateRewardedAdRequested(
            int levelDisplayedNumber,
            int levelNumber,
            string adPlacement)
        {
            return CreateRewardedAdEvent(
                AnalyticsEventNames.RewardedAdRequested,
                levelDisplayedNumber,
                levelNumber,
                adPlacement);
        }

        public static AnalyticsEvent CreateRewardedAdCompleted(
            int levelDisplayedNumber,
            int levelNumber,
            string adPlacement)
        {
            return CreateRewardedAdEvent(
                AnalyticsEventNames.RewardedAdCompleted,
                levelDisplayedNumber,
                levelNumber,
                adPlacement);
        }

        public static AnalyticsEvent CreateRewardedAdFailed(
            int levelDisplayedNumber,
            int levelNumber,
            string adPlacement)
        {
            return CreateRewardedAdEvent(
                AnalyticsEventNames.RewardedAdFailed,
                levelDisplayedNumber,
                levelNumber,
                adPlacement);
        }

        public static AnalyticsEvent CreateLevelIapPurchase(
            int levelDisplayedNumber,
            int levelNumber,
            string productId,
            double value,
            string currency)
        {
            return new AnalyticsEvent(
                AnalyticsEventNames.LevelIapPurchase,
                new System.Collections.Generic.Dictionary<string, object>
                {
                    { AnalyticsParameterNames.LevelDisplayedNumber, levelDisplayedNumber },
                    { AnalyticsParameterNames.LevelNumber, levelNumber },
                    { AnalyticsParameterNames.ProductId, productId },
                    { AnalyticsParameterNames.Value, value },
                    { AnalyticsParameterNames.Currency, currency }
                });
        }

        private static AnalyticsEvent CreateLevelContextEvent(
            string eventName,
            int levelDisplayedNumber,
            int levelNumber)
        {
            return new AnalyticsEvent(
                eventName,
                new System.Collections.Generic.Dictionary<string, object>
                {
                    { AnalyticsParameterNames.LevelDisplayedNumber, levelDisplayedNumber },
                    { AnalyticsParameterNames.LevelNumber, levelNumber }
                });
        }

        private static AnalyticsEvent CreateBoosterEvent(
            string eventName,
            int levelDisplayedNumber,
            int levelNumber,
            string boosterType)
        {
            return new AnalyticsEvent(
                eventName,
                new System.Collections.Generic.Dictionary<string, object>
                {
                    { AnalyticsParameterNames.LevelDisplayedNumber, levelDisplayedNumber },
                    { AnalyticsParameterNames.LevelNumber, levelNumber },
                    { AnalyticsParameterNames.BoosterType, boosterType }
                });
        }

        private static AnalyticsEvent CreateRewardedAdEvent(
            string eventName,
            int levelDisplayedNumber,
            int levelNumber,
            string adPlacement)
        {
            return new AnalyticsEvent(
                eventName,
                new System.Collections.Generic.Dictionary<string, object>
                {
                    { AnalyticsParameterNames.LevelDisplayedNumber, levelDisplayedNumber },
                    { AnalyticsParameterNames.LevelNumber, levelNumber },
                    { AnalyticsParameterNames.AdPlacement, adPlacement }
                });
        }

        private static AnalyticsEvent CreateTerminalLevelEvent(
            string eventName,
            int levelDisplayedNumber,
            int levelNumber,
            int attemptNumber,
            double durationSeconds,
            int progressPercent)
        {
            return new AnalyticsEvent(
                eventName,
                new System.Collections.Generic.Dictionary<string, object>
                {
                    { AnalyticsParameterNames.LevelDisplayedNumber, levelDisplayedNumber },
                    { AnalyticsParameterNames.LevelNumber, levelNumber },
                    { AnalyticsParameterNames.AttemptNumber, attemptNumber },
                    { AnalyticsParameterNames.DurationSeconds, NormalizeDuration(durationSeconds) },
                    { AnalyticsParameterNames.ProgressPercent, ClampProgress(progressPercent) }
                });
        }

        private static double NormalizeDuration(double durationSeconds)
        {
            return System.Math.Round(System.Math.Max(0d, durationSeconds), 3);
        }

        private static int ClampProgress(int progressPercent)
        {
            if (progressPercent < 0)
            {
                return 0;
            }

            return progressPercent > 100 ? 100 : progressPercent;
        }
    }
}
