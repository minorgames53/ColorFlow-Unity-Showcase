using Solo.MOST_IN_ONE;

namespace Game.Shared.Haptics
{
    public sealed class MobileHapticProvider : IHapticProvider
    {
        public void Play(HapticType type)
        {
            MOST_HapticFeedback.Generate(MapHapticType(type));
        }

        private static MOST_HapticFeedback.HapticTypes MapHapticType(HapticType type)
        {
            return type switch
            {
                HapticType.Selection =>
                    MOST_HapticFeedback.HapticTypes.Selection,

                HapticType.Light =>
                    MOST_HapticFeedback.HapticTypes.LightImpact,

                HapticType.Medium =>
                    MOST_HapticFeedback.HapticTypes.MediumImpact,

                HapticType.Heavy =>
                    MOST_HapticFeedback.HapticTypes.HeavyImpact,

                HapticType.Rigid =>
                    MOST_HapticFeedback.HapticTypes.RigidImpact,

                HapticType.Soft =>
                    MOST_HapticFeedback.HapticTypes.SoftImpact,

                HapticType.Success =>
                    MOST_HapticFeedback.HapticTypes.Success,

                HapticType.Warning =>
                    MOST_HapticFeedback.HapticTypes.Warning,

                HapticType.Failure =>
                    MOST_HapticFeedback.HapticTypes.Failure,

                _ =>
                    MOST_HapticFeedback.HapticTypes.Selection
            };
        }
    }
}
