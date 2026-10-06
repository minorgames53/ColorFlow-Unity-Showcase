using Game.Shared.Ads.Core;
using GoogleMobileAds.Ump.Api;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Shared.DeveloperTools.Runtime
{
    [DisallowMultipleComponent]
    public sealed class DeveloperAdsSection : MonoBehaviour
    {
        private const string PrivacyPolicyUrl =
            "https://example.com/privacy";

        [Header("Status")]
        [SerializeField] private TMP_Text consentStatusText;
        [SerializeField] private TMP_Text privacyOptionsStatusText;
        [SerializeField] private TMP_Text canRequestAdsText;
        [SerializeField] private TMP_Text mobileAdsStatusText;
        [SerializeField] private TMP_Text lastResultText;

        [Header("Actions")]
        [SerializeField] private Button resetConsentButton;
        [SerializeField] private Button refreshUmpButton;
        [SerializeField] private Button resetAndRefreshButton;
        [SerializeField] private Button showPrivacyOptionsButton;
        [SerializeField] private Button openPrivacyPolicyButton;

        private bool initialized;
        private bool operationInProgress;
        private AdsService subscribedService;

        public void Initialize()
        {
            if (initialized || !DeveloperPanelAvailability.IsAllowed)
            {
                return;
            }

            initialized = true;
            resetConsentButton?.onClick.AddListener(HandleResetConsent);
            refreshUmpButton?.onClick.AddListener(HandleRefreshUmp);
            resetAndRefreshButton?.onClick.AddListener(HandleResetAndRefresh);
            showPrivacyOptionsButton?.onClick.AddListener(HandleShowPrivacyOptions);
            openPrivacyPolicyButton?.onClick.AddListener(HandleOpenPrivacyPolicy);
            BindToAdsService();
            Refresh();
        }

        public void Dispose()
        {
            if (!initialized)
            {
                return;
            }

            resetConsentButton?.onClick.RemoveListener(HandleResetConsent);
            refreshUmpButton?.onClick.RemoveListener(HandleRefreshUmp);
            resetAndRefreshButton?.onClick.RemoveListener(HandleResetAndRefresh);
            showPrivacyOptionsButton?.onClick.RemoveListener(HandleShowPrivacyOptions);
            openPrivacyPolicyButton?.onClick.RemoveListener(HandleOpenPrivacyPolicy);
            UnbindFromAdsService();
            initialized = false;
        }

        public void Refresh()
        {
            if (!DeveloperPanelAvailability.IsAllowed) return;
            BindToAdsService();

            try
            {
                SetText(consentStatusText, ConsentInformation.ConsentStatus.ToString());
                SetText(
                    privacyOptionsStatusText,
                    ConsentInformation.PrivacyOptionsRequirementStatus.ToString());
                SetText(canRequestAdsText, ConsentInformation.CanRequestAds().ToString());

                AdsService service = AdsService.Instance;
                string mobileAdsStatus = service == null
                    ? "Service unavailable"
                    : $"{service.InitializationState} / SDK " +
                      (service.IsMobileAdsInitializedForDeveloperTools
                          ? "Initialized"
                          : "Not initialized");
                SetText(mobileAdsStatusText, mobileAdsStatus);

                if (service != null && !operationInProgress)
                {
                    SetText(
                        lastResultText,
                        $"Lifecycle: {service.ConsentLifecycleForDeveloperTools}\n" +
                        service.LastConsentResultForDeveloperTools);
                }

                RefreshButtonStates();
            }
            catch (System.Exception exception)
            {
                SetText(lastResultText, $"Status refresh failed: {exception.Message}");
                RefreshButtonStates();
            }
        }

        public bool HasRequiredReferences()
        {
            return consentStatusText != null && privacyOptionsStatusText != null &&
                   canRequestAdsText != null && mobileAdsStatusText != null &&
                   lastResultText != null && resetConsentButton != null &&
                   refreshUmpButton != null && resetAndRefreshButton != null &&
                   showPrivacyOptionsButton != null && openPrivacyPolicyButton != null;
        }

        private void HandleResetConsent()
        {
            AdsService service = AdsService.Instance;
            if (IsConsentBusy(service))
            {
                SetResult("AdsService is unavailable or consent is busy.");
                return;
            }

            bool reset = service.ResetConsentOnlyForDeveloperTools();
            SetResult(reset
                ? "Consent reset. Restart app to test startup flow."
                : "Consent reset rejected because another consent operation is running.");
            Refresh();
        }

        private void HandleRefreshUmp()
        {
            AdsService service = AdsService.Instance;
            if (!TryBeginOperation(service, "Refreshing UMP consent..."))
            {
                return;
            }

            if (!service.RefreshConsentForDeveloperTools(HandleConsentOperationCompleted))
            {
                EndOperation("UMP refresh is already running.");
            }
        }

        private void HandleResetAndRefresh()
        {
            AdsService service = AdsService.Instance;
            if (!TryBeginOperation(service, "Resetting consent and refreshing UMP..."))
            {
                return;
            }

            if (!service.ResetAndRefreshConsentForDeveloperTools(
                    HandleConsentOperationCompleted))
            {
                EndOperation("Reset + Refresh is already running.");
            }
        }

        private void HandleShowPrivacyOptions()
        {
            AdsService service = AdsService.Instance;
            if (IsConsentBusy(service) ||
                ConsentInformation.PrivacyOptionsRequirementStatus !=
                PrivacyOptionsRequirementStatus.Required)
            {
                SetResult("UMP privacy options are not currently required/available.");
                Refresh();
                return;
            }

            operationInProgress = true;
            SetResult("Opening UMP privacy options...");
            RefreshButtonStates();
            service.ShowPrivacyOptions(success =>
            {
                EndOperation(success
                    ? "Privacy options form completed."
                    : "Privacy options form did not complete.");
            });
        }

        private static void HandleOpenPrivacyPolicy()
        {
            if (DeveloperPanelAvailability.IsAllowed) Application.OpenURL(PrivacyPolicyUrl);
        }

        private bool TryBeginOperation(AdsService service, string status)
        {
            if (IsConsentBusy(service))
            {
                SetResult("AdsService is unavailable or consent is busy.");
                return false;
            }

            operationInProgress = true;
            SetResult(status);
            RefreshButtonStates();
            return true;
        }

        private void HandleConsentOperationCompleted(bool canRequestAds)
        {
            EndOperation($"UMP flow completed. CanRequestAds: {canRequestAds}.");
        }

        private void EndOperation(string result)
        {
            operationInProgress = false;
            SetResult(result);
            Refresh();
        }

        private void SetResult(string result)
        {
            SetText(lastResultText, result);
        }

        private void RefreshButtonStates()
        {
            bool consentBusy = IsConsentBusy(AdsService.Instance);
            SetInteractable(resetConsentButton, !consentBusy);
            SetInteractable(refreshUmpButton, !consentBusy);
            SetInteractable(resetAndRefreshButton, !consentBusy);
            SetInteractable(
                showPrivacyOptionsButton,
                !consentBusy &&
                ConsentInformation.PrivacyOptionsRequirementStatus ==
                PrivacyOptionsRequirementStatus.Required);
            SetInteractable(openPrivacyPolicyButton, DeveloperPanelAvailability.IsAllowed);
        }

        private bool IsConsentBusy(AdsService service)
        {
            return !DeveloperPanelAvailability.IsAllowed || operationInProgress || service == null ||
                   service.IsConsentOperationRunningForDeveloperTools;
        }

        private void BindToAdsService()
        {
            AdsService currentService = AdsService.Instance;
            if (subscribedService == currentService)
            {
                return;
            }

            UnbindFromAdsService();
            subscribedService = currentService;
            if (subscribedService != null)
            {
                subscribedService.ConsentLifecycleChangedForDeveloperTools +=
                    HandleConsentLifecycleChanged;
            }
        }

        private void UnbindFromAdsService()
        {
            if (subscribedService != null)
            {
                subscribedService.ConsentLifecycleChangedForDeveloperTools -=
                    HandleConsentLifecycleChanged;
                subscribedService = null;
            }
        }

        private void HandleConsentLifecycleChanged()
        {
            Refresh();
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null)
            {
                text.text = value ?? string.Empty;
            }
        }

        private static void SetInteractable(Selectable selectable, bool interactable)
        {
            if (selectable != null)
            {
                selectable.interactable = interactable;
            }
        }
    }
}
