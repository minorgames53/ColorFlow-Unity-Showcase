#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using Game.Shared.CrashReporting;
using UnityEngine;

namespace Game.Integrations.Firebase.Crashlytics.Development
{
    public sealed class CrashlyticsTestTrigger : MonoBehaviour
    {
        [ContextMenu("Log Test Message")]
        public void LogTestMessage()
        {
            CrashReportingBootstrap.Instance?.Log("Crashlytics test message.");
        }

        [ContextMenu("Log Test Non-Fatal Exception")]
        public void LogTestNonFatalException()
        {
            CrashReportingBootstrap.Instance?.LogException(
                new InvalidOperationException("Crashlytics test non-fatal exception."));
        }

        [ContextMenu("Throw Test Exception")]
        public void ThrowTestException()
        {
            throw new InvalidOperationException("Crashlytics test fatal exception.");
        }
    }
}
#endif
