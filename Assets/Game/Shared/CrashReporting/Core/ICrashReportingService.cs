using System;

namespace Game.Shared.CrashReporting.Core
{
    public interface ICrashReportingService
    {
        bool IsReady { get; }
        bool IsCollectionEnabled { get; }

        void Initialize();
        void SetCollectionEnabled(bool enabled);

        void Log(string message);
        void LogException(Exception exception);

        void SetCustomKey(string key, string value);
        void SetUserId(string userId);
        void ClearUserId();
    }
}
