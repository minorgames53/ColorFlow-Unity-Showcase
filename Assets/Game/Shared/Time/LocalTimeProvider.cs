using System;

namespace Game.Shared.Time
{
    public sealed class LocalTimeProvider : ITimeProvider
    {
        public long GetUtcNowUnixSeconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }
}
