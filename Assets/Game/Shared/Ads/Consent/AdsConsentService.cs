using System;
using System.Collections.Generic;
using Game.Shared.Ads.Core;
using Game.Shared.DeveloperTools;
using GoogleMobileAds.Api;
using GoogleMobileAds.Mediation.UnityAds.Api;
using GoogleMobileAds.Ump.Api;

namespace Game.Shared.Ads.Consent
{
    internal readonly struct PrivacyOptionsResult
    {
        public PrivacyOptionsResult(bool success, bool canRequestAds)
        {
            Success = success;
            CanRequestAds = canRequestAds;
        }

        public bool Success { get; }
        public bool CanRequestAds { get; }
    }

    internal sealed class AdsConsentService : IDisposable
    {
        private const string GdprAppliesPreferenceKey = "IABTCF_gdprApplies";
        private const string PurposeConsentsPreferenceKey = "IABTCF_PurposeConsents";
        private const string UnityAdsGdprConsentKey = "gdpr.consent";

        private readonly AdsLogger logger;
        private readonly List<Action<bool>> pendingCallbacks = new List<Action<bool>>();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private readonly AdsConfig config;
        private bool startupConsentResetApplied;
#endif
        private string lastDeveloperResult = "Consent lifecycle has not started.";

        private bool isGathering;
        private bool hasCompletedGathering;
        private bool isDisposed;
        private int operationGeneration;

        public AdsConsentService(AdsLogger logger, AdsConfig config)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            this.config = config;
#endif
        }

        public bool CanRequestAds => !isDisposed && ConsentInformation.CanRequestAds();

        public bool IsPrivacyOptionsRequired =>
            !isDisposed &&
            ConsentInformation.PrivacyOptionsRequirementStatus ==
            PrivacyOptionsRequirementStatus.Required;

        internal bool IsGatheringForDeveloperTools => isGathering;
        internal bool HasCompletedForDeveloperTools => hasCompletedGathering;
        internal string LastResultForDeveloperTools => lastDeveloperResult;

        public void GatherConsent(Action<bool> onComplete)
        {
            if (isDisposed)
            {
                onComplete?.Invoke(false);
                return;
            }

            if (onComplete != null)
            {
                pendingCallbacks.Add(onComplete);
            }

            if (hasCompletedGathering)
            {
                CompletePendingCallbacks(ReadCanRequestAdsSafely());
                return;
            }

            if (isGathering)
            {
                return;
            }

            isGathering = true;
            int generation = ++operationGeneration;
            RecordDeveloperResult("Consent information update started.");

            try
            {
                // UMP completes before MobileAds.Initialize. Its callbacks are forwarded through
                // this executor, so it must exist before the first native consent request.
                AdsMainThread.EnsureExecutorInitialized();

                ConsentRequestParameters parameters = new ConsentRequestParameters
                {
                    // Application policy: users are currently treated as not under the age of consent.
                    // Replace this hardcoded value with age-gating input if that policy changes.
                    TagForUnderAgeOfConsent = false
                };

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                ApplyConsentDebugSettings(parameters);
#endif

                ConsentInformation.Update(parameters, updateError =>
                {
                    AdsMainThread.Execute(() => HandleConsentUpdated(generation, updateError));
                });
            }
            catch (Exception exception)
            {
                logger.Warning($"Consent update could not start: {exception.Message}");
                RecordDeveloperResult($"Consent update start error: {exception.Message}");
                FinishGathering(ReadCanRequestAdsSafely());
            }
        }

        public void ShowPrivacyOptions(Action<PrivacyOptionsResult> onComplete = null)
        {
            if (isDisposed)
            {
                onComplete?.Invoke(new PrivacyOptionsResult(false, false));
                return;
            }

            if (!IsPrivacyOptionsRequired)
            {
                logger.Warning("Privacy options form is not required.");
                RecordDeveloperResult("Privacy options form is not required.");
                onComplete?.Invoke(new PrivacyOptionsResult(false, CanRequestAds));
                return;
            }

            int generation = operationGeneration;
            logger.Log("Showing privacy options form");
            RecordDeveloperResult("Privacy options form opened.");
            ConsentForm.ShowPrivacyOptionsForm(formError =>
            {
                AdsMainThread.Execute(() =>
                {
                    if (isDisposed || generation != operationGeneration)
                    {
                        return;
                    }

                    ApplyUnityAdsConsentMetadata();

                    if (formError != null)
                    {
                        logger.Warning(
                            $"Privacy options form failed: code={formError.ErrorCode}, message={formError.Message}");
                        RecordDeveloperResult(
                            $"Privacy options error {formError.ErrorCode}: {formError.Message}");
                        onComplete?.Invoke(
                            new PrivacyOptionsResult(false, CanRequestAds));
                        return;
                    }

                    logger.Log("Privacy options form closed");
                    RecordDeveloperResult("Privacy options form closed successfully.");
                    onComplete?.Invoke(
                        new PrivacyOptionsResult(true, CanRequestAds));
                });
            });
        }

        public void ResetForTesting()
        {
            if (!DeveloperPanelAvailability.IsAllowed || isDisposed)
            {
                return;
            }

            operationGeneration++;
            pendingCallbacks.Clear();
            isGathering = false;
            hasCompletedGathering = false;
            ConsentInformation.Reset();
            logger.Log("Consent information reset for testing");
            RecordDeveloperResult("Consent reset. Restart app to test startup flow.");
        }

        public bool RefreshForTesting(Action<bool> onComplete)
        {
            if (!DeveloperPanelAvailability.IsAllowed || isDisposed || isGathering)
            {
                return false;
            }

            hasCompletedGathering = false;
            GatherConsent(onComplete);
            return true;
        }

        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            operationGeneration++;
            pendingCallbacks.Clear();
        }

        private void HandleConsentUpdated(int generation, FormError updateError)
        {
            if (!IsCurrentOperation(generation))
            {
                return;
            }

            if (updateError != null)
            {
                logger.Warning(
                    $"Consent update failed: code={updateError.ErrorCode}, message={updateError.Message}");
                RecordDeveloperResult(
                    $"Consent update error {updateError.ErrorCode}: {updateError.Message}");
                FinishGathering(ReadCanRequestAdsSafely());
                return;
            }

            try
            {
                logger.Log($"Consent updated: {ConsentInformation.ConsentStatus}");
                RecordDeveloperResult(
                    $"Consent updated: {ConsentInformation.ConsentStatus}. Checking required form.");

                ConsentForm.LoadAndShowConsentFormIfRequired(formError =>
                {
                    AdsMainThread.Execute(() => HandleConsentFormCompleted(generation, formError));
                });
            }
            catch (Exception exception)
            {
                logger.Warning($"Consent form could not start: {exception.Message}");
                RecordDeveloperResult($"Consent form start error: {exception.Message}");
                FinishGathering(ReadCanRequestAdsSafely());
            }
        }

        private void HandleConsentFormCompleted(int generation, FormError formError)
        {
            if (!IsCurrentOperation(generation))
            {
                return;
            }

            if (formError != null)
            {
                logger.Warning(
                    $"Consent form failed: code={formError.ErrorCode}, message={formError.Message}");
                RecordDeveloperResult(
                    $"Consent form error {formError.ErrorCode}: {formError.Message}");
            }
            else
            {
                logger.Log("Consent form flow completed");
                RecordDeveloperResult("Consent update/form flow completed successfully.");
            }

            FinishGathering(ReadCanRequestAdsSafely());
        }

        private bool IsCurrentOperation(int generation)
        {
            return !isDisposed && isGathering && generation == operationGeneration;
        }

        private void FinishGathering(bool canRequestAds)
        {
            ApplyUnityAdsConsentMetadata();
            isGathering = false;
            hasCompletedGathering = true;
            CompletePendingCallbacks(canRequestAds);
        }

        private bool ReadCanRequestAdsSafely()
        {
            try
            {
                return CanRequestAds;
            }
            catch (Exception exception)
            {
                logger.Warning($"CanRequestAds could not be read: {exception.Message}");
                RecordDeveloperResult($"CanRequestAds read error: {exception.Message}");
                return false;
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void ApplyConsentDebugSettings(ConsentRequestParameters parameters)
        {
            if (parameters == null || config == null ||
                !config.ForceEeaConsentDebugMode)
            {
                return;
            }

            if (config.ResetConsentOnStartupForTesting && !startupConsentResetApplied)
            {
                startupConsentResetApplied = true;
                ConsentInformation.Reset();
                logger.Log("Consent information reset for EEA startup testing");
            }

            List<string> testDeviceIds = new List<string>();
            string testDeviceHashedId = config.UmpTestDeviceHashedId?.Trim();
            if (!string.IsNullOrEmpty(testDeviceHashedId))
            {
                testDeviceIds.Add(testDeviceHashedId);
            }

            parameters.ConsentDebugSettings = new ConsentDebugSettings
            {
                DebugGeography = DebugGeography.EEA,
                TestDeviceHashedIds = testDeviceIds
            };

            logger.Log("UMP EEA consent debug mode enabled");
        }
#endif

        private void ApplyUnityAdsConsentMetadata()
        {
#if UNITY_ANDROID || UNITY_IOS
            try
            {
                if (ApplicationPreferences.GetInt(GdprAppliesPreferenceKey) != 1)
                {
                    return;
                }

                string purposeConsents =
                    ApplicationPreferences.GetString(PurposeConsentsPreferenceKey);
                bool hasPurposeOneConsent =
                    !string.IsNullOrEmpty(purposeConsents) && purposeConsents[0] == '1';

                UnityAds.SetConsentMetaData(UnityAdsGdprConsentKey, hasPurposeOneConsent);
                logger.Log("Applied UMP GDPR consent metadata to Unity Ads mediation");
            }
            catch (Exception exception)
            {
                logger.Warning(
                    $"Failed to apply Unity Ads consent metadata: {exception.Message}");
            }
#endif
        }

        private void CompletePendingCallbacks(bool canRequestAds)
        {
            if (pendingCallbacks.Count == 0)
            {
                return;
            }

            Action<bool>[] callbacks = pendingCallbacks.ToArray();
            pendingCallbacks.Clear();

            for (int i = 0; i < callbacks.Length; i++)
            {
                try
                {
                    callbacks[i]?.Invoke(canRequestAds);
                }
                catch (Exception exception)
                {
                    logger.Warning($"Consent completion callback threw: {exception.Message}");
                }
            }
        }

        private void RecordDeveloperResult(string result)
        {
            lastDeveloperResult = string.IsNullOrWhiteSpace(result)
                ? "No consent result."
                : result;
        }
    }
}
