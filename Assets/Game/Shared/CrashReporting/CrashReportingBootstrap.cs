using System;
using Game.Integrations.Firebase.Crashlytics;
using Game.Shared.CrashReporting.Core;
using Game.Shared.Config;
using UnityEngine;

namespace Game.Shared.CrashReporting
{
    [DisallowMultipleComponent]
    public sealed class CrashReportingBootstrap : MonoBehaviour
    {
        [SerializeField] private CrashReportingConfig config;
        [SerializeField] private bool persistAcrossScenes = true;

        private bool missingServiceWarningLogged;

        public static CrashReportingBootstrap Instance { get; private set; }

        public ICrashReportingService Service { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (persistAcrossScenes)
            {
                DontDestroyOnLoad(gameObject);
            }

            InitializeCrashReporting();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                Service = null;
            }
        }

        public void Log(string message)
        {
            if (Service == null)
            {
                LogMissingServiceWarning();
                return;
            }

            Service.Log(message);
        }

        public void LogException(Exception exception)
        {
            if (Service == null)
            {
                LogMissingServiceWarning();
                return;
            }

            Service.LogException(exception);
        }

        public void SetCustomKey(string key, string value)
        {
            if (Service == null)
            {
                LogMissingServiceWarning();
                return;
            }

            Service.SetCustomKey(key, value);
        }

        public void SetUserId(string userId)
        {
            if (Service == null)
            {
                LogMissingServiceWarning();
                return;
            }

            Service.SetUserId(userId);
        }

        public void ClearUserId()
        {
            if (Service == null)
            {
                LogMissingServiceWarning();
                return;
            }

            Service.ClearUserId();
        }

        public void SetCollectionEnabled(bool enabled)
        {
            if (Service == null)
            {
                LogMissingServiceWarning();
                return;
            }

            Service.SetCollectionEnabled(enabled && FeatureConfig.ExternalServicesEnabled);
        }

        private void InitializeCrashReporting()
        {
            if (!FeatureConfig.ExternalServicesEnabled)
            {
                return;
            }

            if (config == null)
            {
                Debug.LogError("CrashReportingBootstrap requires a CrashReportingConfig asset.", this);
                return;
            }

            Service = new FirebaseCrashlyticsService(
                config.CollectionEnabled,
                config.ReportUncaughtExceptionsAsFatal,
                config.EnableDebugLogs);

            Service.Initialize();

            if (config.EnableDebugLogs)
            {
                Debug.Log(
                    $"CrashReportingBootstrap initialized.\nService: {Service.GetType().Name}\nCollection Enabled: {config.CollectionEnabled}",
                    this);
            }
        }

        private void LogMissingServiceWarning()
        {
            if (!FeatureConfig.ExternalServicesEnabled || missingServiceWarningLogged ||
                config == null || !config.EnableDebugLogs)
            {
                return;
            }

            missingServiceWarningLogged = true;
            Debug.LogWarning("CrashReportingBootstrap ignored call because crash reporting service is not initialized.", this);
        }
    }
}
