namespace Gameplay.Levels
{
    public static class LevelWinRewardConfig
    {
        public const int NormalReward = 25;
        public const int HardReward = 50;
        public const int VeryHardReward = 75;

        public static int GetGoldReward(LevelDifficulty difficulty)
        {
            switch (difficulty)
            {
                case LevelDifficulty.Hard:
                    return HardReward;

                case LevelDifficulty.VeryHard:
                    return VeryHardReward;

                case LevelDifficulty.Normal:
                default:
                    return NormalReward;
            }
        }
    }
}
