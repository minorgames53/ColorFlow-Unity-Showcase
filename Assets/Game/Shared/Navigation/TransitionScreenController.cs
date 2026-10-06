using UnityEngine;

namespace Game.Shared.Navigation
{
    [DisallowMultipleComponent]
    public sealed class TransitionScreenController : MonoBehaviour
    {
        #region Inspector

        [Header("Boot Loading")]
        [SerializeField] private GameObject bootScreenRoot = null;

        [Header("Scene Transition Loading")]
        [SerializeField] private GameObject transitionScreenRoot = null;

        #endregion

        #region Singleton

        public static TransitionScreenController Instance { get; private set; }

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

            MarkPersistent(bootScreenRoot);
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

        public void Initialize()
        {
            Hide();
        }

        public void ShowBoot()
        {
            if (bootScreenRoot == null)
            {
                Debug.LogWarning($"{nameof(TransitionScreenController)} cannot show boot screen because boot screen root is not assigned.", this);
                return;
            }

            bootScreenRoot.SetActive(true);
        }

        public void CompleteBootLoading()
        {
            if (bootScreenRoot == null)
            {
                return;
            }

            // Canvas Boot is a one-shot startup screen. It is intentionally destroyed after MainMenu is ready.
            Destroy(bootScreenRoot);
            bootScreenRoot = null;
        }

        public void Show()
        {
            if (transitionScreenRoot == null)
            {
                Debug.LogWarning($"{nameof(TransitionScreenController)} cannot show transition screen because transition screen root is not assigned.", this);
                return;
            }

            transitionScreenRoot.SetActive(true);
        }

        public void Hide()
        {
            if (transitionScreenRoot == null)
            {
                return;
            }

            transitionScreenRoot.SetActive(false);
        }

        #endregion

        #region Helpers

        private static void MarkPersistent(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            DontDestroyOnLoad(target);
        }

        #endregion
    }
}
