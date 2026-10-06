namespace Game.Shared.Haptics
{
    public sealed class NullHapticProvider : IHapticProvider
    {
        public void Play(HapticType type)
        {
        }
    }
}
