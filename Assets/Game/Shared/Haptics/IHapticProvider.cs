namespace Game.Shared.Haptics
{
    public interface IHapticProvider
    {
        void Play(HapticType type);
    }
}
