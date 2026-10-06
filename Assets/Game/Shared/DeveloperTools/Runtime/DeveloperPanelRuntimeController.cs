using Game.Shared.DeveloperTools.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Shared.DeveloperTools
{
    [DisallowMultipleComponent]
    public sealed class DeveloperPanelRuntimeController : MonoBehaviour
    {
        [Header("Lifecycle")]
        [SerializeField] private Button devPanelButton;
        [SerializeField] private GameObject dimmer;
        [SerializeField] private GameObject panel;
        [SerializeField] private Button closeButton;
        [SerializeField] private TMP_Text environmentText;

        [Header("Tabs")]
        [SerializeField] private Button adsTabButton;
        [SerializeField] private Button saveTabButton;
        [SerializeField] private GameObject adsContent;
        [SerializeField] private GameObject saveContent;
        [SerializeField] private ScrollRect adsScrollRect;
        [SerializeField] private ScrollRect saveScrollRect;

        [Header("Sections")]
        [SerializeField] private DeveloperAdsSection adsSection;
        [SerializeField] private DeveloperSaveSection saveSection;

        private bool initialized;

        public bool IsOpen => panel != null && panel.activeSelf;

        private void Awake()
        {
            Initialize();
        }

        private void OnDestroy()
        {
            if (!initialized)
            {
                return;
            }

            devPanelButton?.onClick.RemoveListener(Open);
            closeButton?.onClick.RemoveListener(Close);
            adsTabButton?.onClick.RemoveListener(SelectAdsTab);
            saveTabButton?.onClick.RemoveListener(SelectSaveTab);
            adsSection?.Dispose();
            saveSection?.Dispose();
            initialized = false;
        }

        public void Initialize()
        {
            if (initialized)
            {
                return;
            }

            if (!DeveloperPanelAvailability.IsAllowed)
            {
                gameObject.SetActive(false);
                return;
            }

            initialized = true;
            devPanelButton?.onClick.AddListener(Open);
            closeButton?.onClick.AddListener(Close);
            adsTabButton?.onClick.AddListener(SelectAdsTab);
            saveTabButton?.onClick.AddListener(SelectSaveTab);
            adsSection?.Initialize();
            saveSection?.Initialize();

            if (environmentText != null)
            {
                environmentText.text = Application.isEditor
                    ? "UNITY EDITOR"
                    : Debug.isDebugBuild ? "DEVELOPMENT BUILD" : "RELEASE BUILD";
            }

            devPanelButton?.gameObject.SetActive(DeveloperPanelAvailability.IsAllowed);
            Close();
        }

        public void Open()
        {
            if (!initialized || !DeveloperPanelAvailability.IsAllowed)
            {
                return;
            }

            dimmer?.SetActive(true);
            panel?.SetActive(true);
            devPanelButton?.gameObject.SetActive(false);
            SelectAdsTab();
            adsSection?.Refresh();
            saveSection?.Refresh();
        }

        public void Close()
        {
            panel?.SetActive(false);
            dimmer?.SetActive(false);
            devPanelButton?.gameObject.SetActive(DeveloperPanelAvailability.IsAllowed);
            saveSection?.HandlePanelClosed();
        }

        public void Refresh()
        {
            if (!initialized)
            {
                return;
            }

            adsSection?.Refresh();
            saveSection?.Refresh();
        }

        public bool HasRequiredReferences()
        {
            return devPanelButton != null && dimmer != null && panel != null &&
                   closeButton != null && adsTabButton != null && saveTabButton != null &&
                   adsContent != null && saveContent != null &&
                   adsScrollRect != null && saveScrollRect != null &&
                   adsSection != null && adsSection.HasRequiredReferences() &&
                   saveSection != null && saveSection.HasRequiredReferences();
        }

        private void SelectAdsTab()
        {
            adsContent?.SetActive(true);
            saveContent?.SetActive(false);
            ResetScroll(adsScrollRect);
            adsSection?.Refresh();
        }

        private void SelectSaveTab()
        {
            adsContent?.SetActive(false);
            saveContent?.SetActive(true);
            ResetScroll(saveScrollRect);
            saveSection?.Refresh();
        }

        private static void ResetScroll(ScrollRect scrollRect)
        {
            if (scrollRect == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            scrollRect.StopMovement();
            scrollRect.verticalNormalizedPosition = 1f;
        }
    }
}
