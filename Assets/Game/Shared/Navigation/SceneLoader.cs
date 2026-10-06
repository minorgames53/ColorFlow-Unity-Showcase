using System.Collections;
using Game.Shared.Ads.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Shared.Navigation
{
    [DisallowMultipleComponent]
    public sealed class SceneLoader : MonoBehaviour
    {

        #region Inspector

        [SerializeField] private SceneLoadingConfig config = null;
        [SerializeField] private TransitionScreenController transitionScreenController = null;

        #endregion

        #region State

        private SceneLoadingConfig runtimeConfig;
        private SceneId requestedSceneId;
        private bool isWaitingForSceneReady;
        private bool hasWarnedAboutMissingConfig;

        public static SceneLoader Instance { get; private set; }
        public SceneTransitionState State { get; private set; } = SceneTransitionState.Idle;

        private SceneLoadingConfig Config
        {
            get
            {
                if (runtimeConfig != null)
                {
                    return runtimeConfig;
                }

                if (config != null)
                {
                    runtimeConfig = config;
                    return runtimeConfig;
                }

                runtimeConfig = ScriptableObject.CreateInstance<SceneLoadingConfig>();
                if (!hasWarnedAboutMissingConfig)
                {
                    Debug.LogWarning($"{nameof(SceneLoader)} is missing {nameof(SceneLoadingConfig)}. Runtime defaults will be used.", this);
                    hasWarnedAboutMissingConfig = true;
                }

                return runtimeConfig;
            }
        }

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        #endregion

        #region Public API

        public void Configure(SceneLoadingConfig loadingConfig, TransitionScreenController screenController)
        {
            if (loadingConfig != null)
            {
                config = loadingConfig;
                runtimeConfig = loadingConfig;
            }

            if (screenController != null)
            {
                transitionScreenController = screenController;
            }
        }

        public void Initialize()
        {
            if (transitionScreenController == null)
            {
                transitionScreenController = FindFirstObjectByType<TransitionScreenController>();
            }

            transitionScreenController?.Initialize();
        }

        public void LoadInitialMainMenu()
        {
            LoadInitialScene(SceneId.Menu);
        }

        public void LoadInitialScene(SceneId sceneId)
        {
            LoadScene(sceneId, LoadingMode.BootScreen);
        }

        public void LoadMenu()
        {
            if (State != SceneTransitionState.Idle) return;
            // The result owner, not Game-to-Menu or the current level, schedules the ad.
            // Reserve the transition now so duplicate taps and milestone handoffs stay guarded.
            State = SceneTransitionState.LoadingScene;
            System.Action continueToMenu = () =>
            {
                if (this == null) return;
                State = SceneTransitionState.Idle;
                LoadScene(SceneId.Menu, LoadingMode.TransitionScreen);
            };
            if (AdsService.Instance != null &&
                AdsService.Instance.TryRunResultExitInterstitial(continueToMenu)) return;
            continueToMenu();
        }

        public void LoadGame()
        {
            LoadScene(SceneId.Game, LoadingMode.TransitionScreen);
        }

        public void ReloadCurrentScene()
        {
            string currentSceneName = SceneManager.GetActiveScene().name;

            if (currentSceneName == Config.MainMenuSceneName)
            {
                LoadScene(SceneId.Menu, LoadingMode.TransitionScreen);
                return;
            }

            if (currentSceneName == Config.GamePlaySceneName)
            {
                LoadScene(SceneId.Game, LoadingMode.TransitionScreen);
                return;
            }

            Debug.LogWarning($"{nameof(SceneLoader)} cannot reload unsupported scene '{currentSceneName}'.", this);
        }

        public void NotifySceneReady(SceneId sceneId)
        {
            if (State != SceneTransitionState.WaitingForSceneReady || sceneId != requestedSceneId)
            {
                return;
            }

            isWaitingForSceneReady = false;
        }

        #endregion


        #region Loading Flow

        private void LoadScene(SceneId sceneId, LoadingMode loadingMode)
        {
            if (State != SceneTransitionState.Idle)
            {
                return;
            }

            string sceneName = Config.GetSceneName(sceneId);
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                Debug.LogWarning($"{nameof(SceneLoader)} cannot load scene for {sceneId} because scene name is empty.", this);
                return;
            }

            StartCoroutine(LoadSceneRoutine(sceneId, sceneName, loadingMode));
        }

        private IEnumerator LoadSceneRoutine(SceneId sceneId, string sceneName, LoadingMode loadingMode)
        {
            requestedSceneId = sceneId;
            isWaitingForSceneReady = true;
            State = SceneTransitionState.LoadingScene;

            ShowLoadingScreen(loadingMode);
            float shownAt = UnityEngine.Time.unscaledTime;

            if (loadingMode == LoadingMode.BootScreen)
            {
                yield return WaitForMinimumVisibleDuration(shownAt, loadingMode);
            }
            else
            {
                // Let the transition canvas render before async scene loading starts.
                yield return null;
            }

            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (operation == null)
            {
                Debug.LogWarning($"{nameof(SceneLoader)} could not start async load for scene '{sceneName}'.", this);
                FinishTransition(loadingMode);
                yield break;
            }

            if (loadingMode != LoadingMode.BootScreen)
            {
                operation.allowSceneActivation = false;

                while (operation.progress < 0.9f)
                {
                    yield return null;
                }

                yield return WaitForMinimumVisibleDuration(shownAt, loadingMode);

                operation.allowSceneActivation = true;
            }

            while (!operation.isDone)
            {
                yield return null;
            }

            State = SceneTransitionState.WaitingForSceneReady;
            yield return WaitForSceneReady(sceneName);

            FinishTransition(loadingMode);
        }

        private IEnumerator WaitForSceneReady(string sceneName)
        {
            float readyWaitStartedAt = UnityEngine.Time.unscaledTime;
            float timeout = Config.ReadyTimeoutSeconds;

            while (isWaitingForSceneReady)
            {
                if (timeout > 0f && UnityEngine.Time.unscaledTime - readyWaitStartedAt >= timeout)
                {
                    Debug.LogWarning($"{nameof(SceneLoader)} timed out waiting for scene ready signal from '{sceneName}'.", this);
                    break;
                }

                yield return null;
            }
        }

        private IEnumerator WaitForMinimumVisibleDuration(float shownAt, LoadingMode loadingMode)
        {
            float visibleElapsed = UnityEngine.Time.unscaledTime - shownAt;
            float minimumVisibleDuration = loadingMode == LoadingMode.BootScreen
                ? Config.BootMinimumVisibleDuration
                : Config.MinimumVisibleDuration;
            float remainingVisibleDuration = minimumVisibleDuration - visibleElapsed;
            if (remainingVisibleDuration > 0f)
            {
                yield return new WaitForSecondsRealtime(remainingVisibleDuration);
            }
        }

        private void ShowLoadingScreen(LoadingMode loadingMode)
        {
            if (loadingMode == LoadingMode.BootScreen)
            {
                transitionScreenController?.ShowBoot();
                return;
            }

            transitionScreenController?.Show();
        }

        private void FinishTransition(LoadingMode loadingMode)
        {
            if (loadingMode == LoadingMode.BootScreen)
            {
                transitionScreenController?.CompleteBootLoading();
            }
            else
            {
                transitionScreenController?.Hide();
            }

            isWaitingForSceneReady = false;
            State = SceneTransitionState.Idle;
        }

        #endregion

        private enum LoadingMode
        {
            BootScreen,
            TransitionScreen
        }
    }
}

