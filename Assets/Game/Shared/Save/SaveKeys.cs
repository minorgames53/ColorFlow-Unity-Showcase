namespace Game.Shared.Save
{
    public static class SaveKeys
    {
        public const string SoundEnabled = "sound_enabled";
        public const string HapticEnabled = "haptic_enabled";
        public const string FirstAppReviewRequested = "first_app_review_requested";
        public const string LevelOneTapTutorialCompleted = "level_one_tap_tutorial_completed";
        public const string FirstCleanupTutorialCompleted = "first_cleanup_tutorial_completed";
        public const string InterstitialLoseCount = "interstitial_lose_count";

        internal const string LegacyCurrentLevelPlayerPrefs = "current_level";
        internal const string LegacyHasPendingNextLevelPlayerPrefs = "has_pending_next_level";
        internal const string LegacyPendingNextLevelPlayerPrefs = "pending_next_level";
        internal const string LegacyLastLoopContentLevelNumberPlayerPrefs =
            "last_loop_content_level_number";
        internal const string LegacySaveHasDataPlayerPrefs = "save_has_data";
        internal const string LegacySaveVersionPlayerPrefs = "save_version";
    }
}
