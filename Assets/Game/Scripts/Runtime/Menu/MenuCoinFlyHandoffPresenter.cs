using System.Collections;
using Game.Integrations.RateUs;
using Game.Shared.Config;
using Game.Shared.Navigation;
using Game.Shared.Save;
using Game.Shared.UI;
using Game.Shared.UI.Panels;
using UnityEngine;

namespace Game.Menu
{
    [DisallowMultipleComponent]
    public sealed class MenuCoinFlyHandoffPresenter : MonoBehaviour
    {
        [SerializeField] private SceneReadyNotifier sceneReadyNotifier;
        [SerializeField] private CoinFlyAnimator coinFlyAnimator;
        [SerializeField] private RectTransform target;
        [SerializeField] private PanelManager panelManager;
        [SerializeField] private FeatureConfig featureConfig;
        [SerializeField] private UIInputLockService uiInputLockService;

        private Coroutine pendingPresentation;
        private bool ownsCoinFlyInputLock;
        private bool ownsAppReviewInputLock;
        private int appReviewRequestVersion;

        private void OnEnable()
        {
            if (CoinFlyPresentationHandoff.HasPendingPresentation)
            {
                SetCoinFlyInputLocked(true);
            }

            if (sceneReadyNotifier != null)
            {
                sceneReadyNotifier.Ready -= HandleMenuReady;
                sceneReadyNotifier.Ready += HandleMenuReady;
            }
        }

        private void OnDisable()
        {
            appReviewRequestVersion++;
            if (sceneReadyNotifier != null)
            {
                sceneReadyNotifier.Ready -= HandleMenuReady;
            }

            if (pendingPresentation != null)
            {
                StopCoroutine(pendingPresentation);
                pendingPresentation = null;
            }

            SetCoinFlyInputLocked(false);
            SetAppReviewInputLocked(false);
        }

        private void HandleMenuReady()
        {
            if (pendingPresentation != null)
            {
                StopCoroutine(pendingPresentation);
            }

            pendingPresentation = StartCoroutine(PlayWhenPanelsAreClosed());
        }

        private IEnumerator PlayWhenPanelsAreClosed()
        {
            yield return new WaitForEndOfFrame();

            while (panelManager != null && panelManager.IsPanelInputBlocked)
            {
                yield return null;
            }

            pendingPresentation = null;
            if (!CoinFlyPresentationHandoff.TryConsume(
                    out int amount,
                    out bool requestFirstAppReview))
            {
                SetCoinFlyInputLocked(false);
                yield break;
            }

            bool shouldRequestFirstAppReview =
                requestFirstAppReview && CanRequestFirstAppReview();

            if (coinFlyAnimator == null)
            {
                SetCoinFlyInputLocked(false);
                yield break;
            }

            coinFlyAnimator.Play(
                amount,
                null,
                target,
                null,
                () => HandleCoinFlyCompleted(shouldRequestFirstAppReview));

            if (!coinFlyAnimator.IsPlaying)
            {
                SetCoinFlyInputLocked(false);
            }
        }

        private bool CanRequestFirstAppReview()
        {
            if (featureConfig == null || !featureConfig.EnableRateUs)
            {
                return false;
            }

            SaveManager saveManager = SaveManager.Instance;
            return saveManager != null && saveManager.IsInitialized &&
                   saveManager.CurrentLevel >
                   Game.Shared.Ads.Core.AdsService.RewardedUnlockCompletedLevel &&
                   !saveManager.GetBoolSetting(SaveKeys.FirstAppReviewRequested);
        }

        private void HandleCoinFlyCompleted(bool shouldRequestFirstAppReview)
        {
            if (!shouldRequestFirstAppReview)
            {
                SetCoinFlyInputLocked(false);
                return;
            }

            SetAppReviewInputLocked(true);
            SetCoinFlyInputLocked(false);

#if UNITY_EDITOR
            pendingPresentation = StartCoroutine(SimulateEditorAppReview());
#else
            int requestVersion = ++appReviewRequestVersion;
            if (!AppReviewRequest.TryStart(
                    this,
                    succeeded => HandleAppReviewFinished(requestVersion, succeeded)))
            {
                HandleAppReviewFinished(requestVersion, false);
            }
#endif
        }

        private void HandleAppReviewFinished(int requestVersion, bool succeeded)
        {
            if (requestVersion != appReviewRequestVersion)
            {
                return;
            }

            if (succeeded)
            {
                MarkFirstAppReviewRequested();
            }

            SetAppReviewInputLocked(false);
        }

#if UNITY_EDITOR
        private IEnumerator SimulateEditorAppReview()
        {
            yield return new WaitForSecondsRealtime(2f);
            MarkFirstAppReviewRequested();
            SetAppReviewInputLocked(false);
            pendingPresentation = null;
        }
#endif

        private static void MarkFirstAppReviewRequested()
        {
            SaveManager saveManager = SaveManager.Instance;
            if (saveManager == null || !saveManager.IsInitialized)
            {
                return;
            }

            saveManager.SetSetting(SaveKeys.FirstAppReviewRequested, true);
            saveManager.Save();
        }

        private void SetCoinFlyInputLocked(bool locked)
        {
            if (ownsCoinFlyInputLock == locked)
            {
                return;
            }

            ownsCoinFlyInputLock = locked;
            SetCentralInputLocked(locked);
        }

        private void SetAppReviewInputLocked(bool locked)
        {
            if (ownsAppReviewInputLock == locked)
            {
                return;
            }

            ownsAppReviewInputLock = locked;
            SetCentralInputLocked(locked);
        }

        private void SetCentralInputLocked(bool locked)
        {
            if (uiInputLockService == null)
            {
                return;
            }

            if (locked)
            {
                uiInputLockService.LockUIInput();
            }
            else
            {
                uiInputLockService.UnlockUIInput();
            }
        }
    }
}
