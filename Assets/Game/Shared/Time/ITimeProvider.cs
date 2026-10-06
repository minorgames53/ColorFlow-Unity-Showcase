namespace Game.Shared.Time
{
    public interface ITimeProvider
    {
        long GetUtcNowUnixSeconds();
    }
}
