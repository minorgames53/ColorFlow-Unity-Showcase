using System.Collections.Generic;
using System.Text;
using Game.Shared.Analytics.Core;
using Game.Shared.Analytics.Providers;
using Game.Shared.Config;
using UnityEngine;

namespace Game.Shared.Analytics
{
    public sealed class AnalyticsBootstrap : MonoBehaviour
    {
        [SerializeField] private AnalyticsConfig config;
        [SerializeField] private bool persistAcrossScenes = true;
        [SerializeField] private Game.Shared.Ads.Core.AdsService consentOwner;

        private bool missingServiceWarningLogged;
        private TenjinAnalyticsProvider tenjin;
        private bool wasBackgrounded;

        public static AnalyticsBootstrap Instance { get; private set; }

        public AnalyticsService Service { get; private set; }

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

            InitializeAnalytics();
            if (consentOwner != null)
                consentOwner.ConsentLifecycleChangedForDeveloperTools += ApplyTenjinConsent;
        }

        private void Start() { if (Instance == this) ApplyTenjinConsent(); }

        private void ApplyTenjinConsent() => tenjin?.ApplyConsent(consentOwner != null && consentOwner.HasResolvedConsent);

        private void OnApplicationPause(bool paused)
        {
            if (Instance != this) return;
            if (paused) { wasBackgrounded = true; return; }
            if (!wasBackgrounded) return;
            wasBackgrounded = false;
            tenjin?.ApplyConsent(consentOwner != null && consentOwner.HasResolvedConsent, true);
            tenjin?.Connect(true);
        }

        internal void TrackAttribution(string name, int? value = null) => tenjin?.TrackCustom(name, value);
        internal void TrackAdRevenue(string json, string format, string currency, double revenue, string adUnit, string placement, string network) =>
            tenjin?.TrackAdRevenue(json, format, currency, revenue, adUnit, placement, network);
        internal bool CanTrackTenjinPurchase => tenjin != null && tenjin.IsReady;
        internal bool IsTenjinPurchaseDisabled => tenjin == null || !tenjin.IsCollectionEnabled || tenjin.IsConsentDenied;
        internal void TrackTenjinPurchase(Game.Shared.Save.StoreIapAnalyticsTransactionSaveData entry) => tenjin?.TrackPurchase(entry);

        private void OnDestroy()
        {
            if (consentOwner != null)
                consentOwner.ConsentLifecycleChangedForDeveloperTools -= ApplyTenjinConsent;
            if (Instance == this)
            {
                Instance = null;
                Service = null;
            }
        }

        public void Track(
            string eventName,
            IReadOnlyDictionary<string, object> properties = null)
        {
            if (Service == null)
            {
                LogMissingServiceWarning();
                return;
            }

            Service.Track(eventName, properties);
        }

        public void Track(AnalyticsEvent analyticsEvent)
        {
            if (Service == null)
            {
                LogMissingServiceWarning();
                return;
            }

            Service.Track(analyticsEvent);
        }

        internal AnalyticsProviderTrackResult TrackWithoutProviderQueue(
            AnalyticsEvent analyticsEvent)
        {
            if (Service == null)
            {
                LogMissingServiceWarning();
                return AnalyticsProviderTrackResult.SkippedProviderNotReady(
                    "AnalyticsService is not initialized.");
            }

            return Service.TrackWithoutProviderQueue(analyticsEvent);
        }

        internal void SetDefaultLevelContext(
            int displayedLevelNumber,
            int internalLevelNumber,
            object owner,
            string source)
        {
            if (Service == null)
            {
                LogMissingServiceWarning();
                return;
            }

            Service.SetDefaultLevelContext(
                displayedLevelNumber,
                internalLevelNumber,
                owner,
                source);
        }

        internal void RefreshDefaultLevelContext(
            int displayedLevelNumber,
            int internalLevelNumber,
            string source)
        {
            if (Service == null)
            {
                LogMissingServiceWarning();
                return;
            }

            Service.RefreshDefaultLevelContext(
                displayedLevelNumber,
                internalLevelNumber,
                source);
        }

        internal void ClearDefaultLevelContext(object owner, string source)
        {
            if (Service == null)
            {
                LogMissingServiceWarning();
                return;
            }

            Service.ClearDefaultLevelContext(owner, source);
        }

        internal void ResetAnalyticsData(string source = "analytics_reset")
        {
            if (Service == null)
            {
                LogMissingServiceWarning();
                return;
            }

            Service.ResetAnalyticsData(source);
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

        private void InitializeAnalytics()
        {
            if (config == null)
            {
                Debug.LogError("AnalyticsBootstrap requires an AnalyticsConfig asset.", this);
                return;
            }

            List<IAnalyticsProvider> providers = new List<IAnalyticsProvider>();
            bool collectionEnabled = FeatureConfig.ExternalServicesEnabled && config.CollectionEnabled;

            if (FeatureConfig.ExternalServicesEnabled && config.EnableFirebaseAnalytics)
            {
                providers.Add(new FirebaseAnalyticsProvider(
                    collectionEnabled,
                    config.EnableDebugLogs));
            }

            if (FeatureConfig.ExternalServicesEnabled && config.EnableTenjin)
            {
                tenjin = TenjinAnalyticsProvider.GetOrCreate(config);
                providers.Add(tenjin);
            }

            Service = new AnalyticsService(
                providers,
                collectionEnabled,
                config.EnableDebugLogs);
            Service.Initialize();

            if (config.EnableDebugLogs)
            {
                Debug.Log(
                    $"AnalyticsBootstrap initialized.\nProviders: {BuildProviderList(providers)}\nCollection Enabled: {collectionEnabled}",
                    this);
            }
        }

        private void LogMissingServiceWarning()
        {
            if (missingServiceWarningLogged || config == null || !config.EnableDebugLogs)
            {
                return;
            }

            missingServiceWarningLogged = true;
            Debug.LogWarning("AnalyticsBootstrap ignored call because AnalyticsService is not initialized.", this);
        }

        private static string BuildProviderList(IReadOnlyList<IAnalyticsProvider> providers)
        {
            if (providers == null || providers.Count == 0)
            {
                return "None";
            }

            StringBuilder builder = new StringBuilder();

            for (int i = 0; i < providers.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(providers[i].GetType().Name);
            }

            return builder.ToString();
        }
    }
}
