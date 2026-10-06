using UnityEngine;

namespace Game.Shared.CrashReporting.Core
{
    [CreateAssetMenu(
        menuName = "Game/Crash Reporting/Crash Reporting Config",
        fileName = "CrashReportingConfig")]
    public sealed class CrashReportingConfig : ScriptableObject
    {
        [SerializeField] private bool collectionEnabled = true;
        [SerializeField] private bool reportUncaughtExceptionsAsFatal = true;
        [SerializeField] private bool enableDebugLogs = true;

        public bool CollectionEnabled => collectionEnabled;
        public bool ReportUncaughtExceptionsAsFatal => reportUncaughtExceptionsAsFatal;
        public bool EnableDebugLogs => enableDebugLogs;
    }
}
