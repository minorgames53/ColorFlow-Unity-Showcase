using Game.Shared.Ads.Core;
using Game.Shared.Navigation;
using Gameplay.SourceBoxes;
using UnityEngine;
using DeviceApplication = UnityEngine.Device.Application;

namespace Game.Shared.UI.Panels
{
    // Scene-local monitoring owner. The panel remains an ordinary scene instance
    // managed by PanelManager; no connection-approved or progression state is saved.
    [DisallowMultipleComponent]
    public sealed class InternetConnectionGuard : MonoBehaviour
    {
        [SerializeField] private PanelManager panelManager;
        [SerializeField] private NoConnectionPanelController noConnectionPanel;
        [SerializeField] private SceneReadyNotifier sceneReadyNotifier;
        [SerializeField] private UIInputLockService uiInputLockService;
        [SerializeField] private GameplayInputController gameplayInputController;

        private AdsService adsService;
        private bool sceneReady;
        private bool requiresInternet;
        private bool applicationPaused;
        private double nextCheckTime;

        public bool RequiresInternet => requiresInternet;

        private void OnEnable()
        {
            sceneReadyNotifier.Ready += HandleSceneReady;
            panelManager.BlockingPanelStateChanged += HandleBlockingPanelStateChanged;
            noConnectionPanel.RefreshRequested += HandleRefresh;
            sceneReady = sceneReadyNotifier.IsReady;
            BindAdsService();
            ReevaluateRequirement();
        }

        private void OnDisable()
        {
            sceneReadyNotifier.Ready -= HandleSceneReady;
            panelManager.BlockingPanelStateChanged -= HandleBlockingPanelStateChanged;
            noConnectionPanel.RefreshRequested -= HandleRefresh;
            if (adsService != null) adsService.AdPresentationStateChanged -= ReevaluateRequirement;
            adsService = null;
            requiresInternet = false;
            panelManager.CloseBlockingPanel(noConnectionPanel.Panel, this, false);
            ReleaseInputScope();
        }

        private void Update()
        {
            // Startup service binding is not an internet poll (e.g. direct scene launch).
            if (adsService == null && AdsService.Instance != null)
            {
                BindAdsService();
                ReevaluateRequirement();
            }

            if (!sceneReady || !requiresInternet || applicationPaused ||
                noConnectionPanel.IsVisible || panelManager.HasBlockingPanel) return;

            if (UnityEngine.Time.unscaledTimeAsDouble >= nextCheckTime) EvaluateConnectivity();
        }

        private void BindAdsService()
        {
            if (adsService != null) return;
            adsService = AdsService.Instance;
            if (adsService != null) adsService.AdPresentationStateChanged += ReevaluateRequirement;
        }

        private void HandleSceneReady()
        {
            sceneReady = true;
            BindAdsService();
            bool wasRequired = requiresInternet;
            ReevaluateRequirement();
            if (wasRequired) EvaluateConnectivity();
        }

        private void ReevaluateRequirement()
        {
            bool required = adsService != null && adsService.IsMonetizationUnlocked && !adsService.HasNoAds;
            bool becameRequired = required && !requiresInternet;
            requiresInternet = required;
            if (!required)
            {
                panelManager.CloseBlockingPanel(noConnectionPanel.Panel, this, false);
                ReleaseInputScope();
                return;
            }
            if (becameRequired) EvaluateConnectivity();
        }

        private void EvaluateConnectivity()
        {
            // Also covers foreground and entitlement events: never read reachability
            // automatically while the panel is visible, including its closing tween.
            if (!isActiveAndEnabled || !sceneReady || !requiresInternet || applicationPaused ||
                noConnectionPanel.IsVisible || panelManager.HasBlockingPanel) return;

            ScheduleNextCheck();
            if (DeviceApplication.internetReachability == NetworkReachability.NotReachable)
                panelManager.OpenBlockingPanel(noConnectionPanel.Panel, this);
        }

        private void HandleRefresh()
        {
            if (!requiresInternet || !noConnectionPanel.IsVisible) return;
            if (DeviceApplication.internetReachability == NetworkReachability.NotReachable) return;
            panelManager.CloseBlockingPanel(noConnectionPanel.Panel, this);
        }

        private void HandleBlockingPanelStateChanged()
        {
            bool blocking = requiresInternet && noConnectionPanel.IsVisible && panelManager.HasBlockingPanel;
            gameplayInputController?.SetInputBlocked(this, blocking);
            uiInputLockService.SetModalInputAllowed(this, blocking);
            if (!blocking) ScheduleNextCheck();
        }

        private void ReleaseInputScope()
        {
            gameplayInputController?.SetInputBlocked(this, false);
            uiInputLockService.SetModalInputAllowed(this, false);
        }

        private void ScheduleNextCheck()
        {
            nextCheckTime = UnityEngine.Time.unscaledTimeAsDouble + panelManager.Config.InternetReachabilityCheckIntervalSeconds;
        }

        private void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            if (!paused) EvaluateConnectivity();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (focused) EvaluateConnectivity();
        }
    }
}
