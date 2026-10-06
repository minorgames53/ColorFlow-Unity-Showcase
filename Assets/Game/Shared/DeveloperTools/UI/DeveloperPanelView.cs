using Game.Shared.DeveloperPanel.Haptics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Shared.DeveloperTools.UI
{
    public sealed class DeveloperPanelView : MonoBehaviour
    {
        [Header("General")]
        [SerializeField] private GameObject backgroundOverlay;
        [SerializeField] private GameObject eventBlocker;
        [SerializeField] private GameObject fullScreenPanel;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button floatingDevButton;

        [Header("Top Info")]
        [SerializeField] private TMP_Text sceneValueText;
        [SerializeField] private TMP_Text buildValueText;
        [SerializeField] private TMP_Text environmentValueText;

        [Header("Tabs")]
        [SerializeField] private Button analyticsTabButton;
        [SerializeField] private Button crashTabButton;
        [SerializeField] private Button saveTabButton;
        [SerializeField] private Button deviceTabButton;
        [SerializeField] private Button hapticTabButton;
        [SerializeField] private GameObject analyticsTabActiveVisual;
        [SerializeField] private GameObject crashTabActiveVisual;
        [SerializeField] private GameObject saveTabActiveVisual;
        [SerializeField] private GameObject deviceTabActiveVisual;
        [SerializeField] private GameObject hapticTabActiveVisual;

        [Header("Pages")]
        [SerializeField] private GameObject analyticsPage;
        [SerializeField] private GameObject crashPage;
        [SerializeField] private GameObject savePage;
        [SerializeField] private GameObject devicePage;
        [SerializeField] private GameObject hapticPage;
        [SerializeField] private HapticDeveloperPanelView hapticPanelView;

        [Header("Analytics Providers")]
        [SerializeField] private AnalyticsProviderMiniCardView firebaseProviderCard;

        [Header("Recent Events")]
        [SerializeField] private TMP_Text totalEventsText;
        [SerializeField] private Transform eventsListContainer;
        [SerializeField] private AnalyticsEventRowView eventRowPrefab;

        [Header("Selected Event Details")]
        [SerializeField] private TMP_Text selectedEventNameValueText;
        [SerializeField] private Transform propertyRowsContainer;
        [SerializeField] private AnalyticsPropertyRowView propertyRowPrefab;
        [SerializeField] private TMP_Text firebaseResultValueText;
        [SerializeField] private TMP_Text emptySelectionText;

        [Header("Actions")]
        [SerializeField] private Button refreshStatusButton;
        [SerializeField] private Button sendSmokeTestButton;
        [SerializeField] private Button copyAllButton;
        [SerializeField] private Button clearLogButton;

        public GameObject BackgroundOverlay => backgroundOverlay;
        public GameObject EventBlocker => eventBlocker;
        public GameObject FullScreenPanel => fullScreenPanel;
        public Button CloseButton => closeButton;
        public Button FloatingDevButton => floatingDevButton;
        public TMP_Text SceneValueText => sceneValueText;
        public TMP_Text BuildValueText => buildValueText;
        public TMP_Text EnvironmentValueText => environmentValueText;
        public Button AnalyticsTabButton => analyticsTabButton;
        public Button CrashTabButton => crashTabButton;
        public Button SaveTabButton => saveTabButton;
        public Button DeviceTabButton => deviceTabButton;
        public Button HapticTabButton => hapticTabButton;
        public GameObject AnalyticsTabActiveVisual => analyticsTabActiveVisual;
        public GameObject CrashTabActiveVisual => crashTabActiveVisual;
        public GameObject SaveTabActiveVisual => saveTabActiveVisual;
        public GameObject DeviceTabActiveVisual => deviceTabActiveVisual;
        public GameObject HapticTabActiveVisual => hapticTabActiveVisual;
        public GameObject AnalyticsPage => analyticsPage;
        public GameObject CrashPage => crashPage;
        public GameObject SavePage => savePage;
        public GameObject DevicePage => devicePage;
        public GameObject HapticPage => hapticPage;
        public HapticDeveloperPanelView HapticPanelView => hapticPanelView;
        public AnalyticsProviderMiniCardView FirebaseProviderCard => firebaseProviderCard;
        public TMP_Text TotalEventsText => totalEventsText;
        public Transform EventsListContainer => eventsListContainer;
        public AnalyticsEventRowView EventRowPrefab => eventRowPrefab;
        public TMP_Text SelectedEventNameValueText => selectedEventNameValueText;
        public Transform PropertyRowsContainer => propertyRowsContainer;
        public AnalyticsPropertyRowView PropertyRowPrefab => propertyRowPrefab;
        public TMP_Text FirebaseResultValueText => firebaseResultValueText;
        public TMP_Text EmptySelectionText => emptySelectionText;
        public Button RefreshStatusButton => refreshStatusButton;
        public Button SendSmokeTestButton => sendSmokeTestButton;
        public Button CopyAllButton => copyAllButton;
        public Button ClearLogButton => clearLogButton;

        public bool HasRequiredReferences()
        {
            bool hasAllReferences = true;

            CheckReference(backgroundOverlay, nameof(backgroundOverlay), ref hasAllReferences);
            CheckReference(eventBlocker, nameof(eventBlocker), ref hasAllReferences);
            CheckReference(fullScreenPanel, nameof(fullScreenPanel), ref hasAllReferences);
            CheckReference(closeButton, nameof(closeButton), ref hasAllReferences);
            CheckReference(floatingDevButton, nameof(floatingDevButton), ref hasAllReferences);
            CheckReference(sceneValueText, nameof(sceneValueText), ref hasAllReferences);
            CheckReference(buildValueText, nameof(buildValueText), ref hasAllReferences);
            CheckReference(environmentValueText, nameof(environmentValueText), ref hasAllReferences);
            CheckReference(analyticsTabButton, nameof(analyticsTabButton), ref hasAllReferences);
            CheckReference(crashTabButton, nameof(crashTabButton), ref hasAllReferences);
            CheckReference(saveTabButton, nameof(saveTabButton), ref hasAllReferences);
            CheckReference(deviceTabButton, nameof(deviceTabButton), ref hasAllReferences);
            CheckReference(hapticTabButton, nameof(hapticTabButton), ref hasAllReferences);
            CheckReference(analyticsTabActiveVisual, nameof(analyticsTabActiveVisual), ref hasAllReferences);
            CheckReference(crashTabActiveVisual, nameof(crashTabActiveVisual), ref hasAllReferences);
            CheckReference(saveTabActiveVisual, nameof(saveTabActiveVisual), ref hasAllReferences);
            CheckReference(deviceTabActiveVisual, nameof(deviceTabActiveVisual), ref hasAllReferences);
            CheckReference(hapticTabActiveVisual, nameof(hapticTabActiveVisual), ref hasAllReferences);
            CheckReference(analyticsPage, nameof(analyticsPage), ref hasAllReferences);
            CheckReference(crashPage, nameof(crashPage), ref hasAllReferences);
            CheckReference(savePage, nameof(savePage), ref hasAllReferences);
            CheckReference(devicePage, nameof(devicePage), ref hasAllReferences);
            CheckReference(hapticPage, nameof(hapticPage), ref hasAllReferences);
            CheckReference(hapticPanelView, nameof(hapticPanelView), ref hasAllReferences);
            CheckReference(firebaseProviderCard, nameof(firebaseProviderCard), ref hasAllReferences);
            CheckReference(totalEventsText, nameof(totalEventsText), ref hasAllReferences);
            CheckReference(eventsListContainer, nameof(eventsListContainer), ref hasAllReferences);
            CheckReference(eventRowPrefab, nameof(eventRowPrefab), ref hasAllReferences);
            CheckReference(selectedEventNameValueText, nameof(selectedEventNameValueText), ref hasAllReferences);
            CheckReference(propertyRowsContainer, nameof(propertyRowsContainer), ref hasAllReferences);
            CheckReference(propertyRowPrefab, nameof(propertyRowPrefab), ref hasAllReferences);
            CheckReference(firebaseResultValueText, nameof(firebaseResultValueText), ref hasAllReferences);
            CheckReference(refreshStatusButton, nameof(refreshStatusButton), ref hasAllReferences);
            CheckReference(sendSmokeTestButton, nameof(sendSmokeTestButton), ref hasAllReferences);
            CheckReference(copyAllButton, nameof(copyAllButton), ref hasAllReferences);
            CheckReference(clearLogButton, nameof(clearLogButton), ref hasAllReferences);

            return hasAllReferences;
        }

        private void CheckReference(Object reference, string fieldName, ref bool hasAllReferences)
        {
            if (reference != null)
            {
                return;
            }

            hasAllReferences = false;
            Debug.LogWarning($"DeveloperPanelView is missing required reference: {fieldName}.", this);
        }
    }
}
