using Game.Shared.DeveloperTools;
using Game.Shared.UI.Panels;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Shared.Developer.LevelTesting
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class DevLevelTestPanelTrigger : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private DevLevelTestPanel panelPrefab;
        [SerializeField] private GameObject devPanelButton;
        [SerializeField] private RectTransform spawnParent;
        [SerializeField] private PanelManager panelManager;
        [SerializeField] private MonoBehaviour adapterBehaviour;

        [Header("Unlock")]
        [SerializeField, Min(1)] private int requiredTapCount = 4;
        [SerializeField, Min(0f)] private float tapResetDuration = 2f;

        private Button triggerButton;
        private IDevLevelTestAdapter adapter;
        private DevLevelTestPanel spawnedPanel;
        private int currentTapCount;
        private float lastTapTime;
        private bool listenerRegistered;
        private bool hasLoggedInvalidAdapter;
        private bool hasLoggedMissingReferences;

        private void Awake()
        {
            CacheReferences();
            ValidateReferences(true);
        }

        private void OnEnable()
        {
            CacheReferences();
            RegisterListener();
        }

        private void OnDisable()
        {
            UnregisterListener();
        }

        private void OnValidate()
        {
            ClampSettings();
        }

        private void RegisterListener()
        {
            if (listenerRegistered || triggerButton == null)
            {
                return;
            }

            triggerButton.onClick.AddListener(HandleTriggerClicked);
            listenerRegistered = true;
        }

        private void UnregisterListener()
        {
            if (!listenerRegistered || triggerButton == null)
            {
                listenerRegistered = false;
                return;
            }

            triggerButton.onClick.RemoveListener(HandleTriggerClicked);
            listenerRegistered = false;
        }

        private void HandleTriggerClicked()
        {
            RegisterTap();
        }

        public void RegisterTap()
        {
            ClampSettings();

            if (!DeveloperPanelAvailability.IsAllowed)
            {
                return;
            }

            float now = UnityEngine.Time.unscaledTime;
            if (currentTapCount <= 0 || now - lastTapTime > tapResetDuration)
            {
                currentTapCount = 1;
            }
            else
            {
                currentTapCount++;
            }

            lastTapTime = now;

            if (currentTapCount < requiredTapCount)
            {
                return;
            }

            currentTapCount = 0;
            lastTapTime = 0f;
            devPanelButton.SetActive(true);
            OpenPanel();
        }

        public void OpenPanel()
        {
            if (!DeveloperPanelAvailability.IsAllowed) return;
            CacheReferences();
            if (panelManager != null && panelManager.TryDeferOpen(null, OpenPanel)) return;

            if (!ValidateReferences(true))
            {
                return;
            }

            if (spawnedPanel == null)
            {
                spawnedPanel = Instantiate(panelPrefab, spawnParent);
                Transform panelTransform = spawnedPanel.transform;
                panelTransform.localPosition = Vector3.zero;
                panelTransform.localRotation = Quaternion.identity;
                panelTransform.localScale = Vector3.one;
                spawnedPanel.name = panelPrefab.name;
                spawnedPanel.Initialize(adapter);
            }
            else
            {
                spawnedPanel.Initialize(adapter);
            }

            UIPanel uiPanel = spawnedPanel.UIPanel != null
                ? spawnedPanel.UIPanel
                : spawnedPanel.GetComponent<UIPanel>();
            if (uiPanel == null)
            {
                if (!hasLoggedMissingReferences)
                {
                    hasLoggedMissingReferences = true;
                    Debug.LogError($"{nameof(DevLevelTestPanelTrigger)} on '{name}' cannot open because spawned panel has no {nameof(UIPanel)}.", this);
                }

                return;
            }

            spawnedPanel.Refresh();
            panelManager.OpenRoot(uiPanel);
        }

        private void CacheReferences()
        {
            if (triggerButton == null)
            {
                triggerButton = GetComponent<Button>();
            }

            if (panelManager == null)
            {
                panelManager = GetComponentInParent<PanelManager>(true);
            }

            adapter = adapterBehaviour as IDevLevelTestAdapter;

            if (adapter != null)
            {
                hasLoggedInvalidAdapter = false;
            }

            if (triggerButton != null && adapterBehaviour != null && panelPrefab != null && spawnParent != null && panelManager != null)
            {
                hasLoggedMissingReferences = false;
            }
        }

        private bool ValidateReferences(bool logErrors)
        {
            bool isValid = true;

            if (triggerButton == null)
            {
                isValid = false;
            }

            if (adapterBehaviour != null && adapter == null)
            {
                isValid = false;
                if (logErrors && !hasLoggedInvalidAdapter)
                {
                    hasLoggedInvalidAdapter = true;
                    Debug.LogError($"{nameof(DevLevelTestPanelTrigger)} on '{name}' has adapterBehaviour '{adapterBehaviour.name}' but it does not implement {nameof(IDevLevelTestAdapter)}.", this);
                }
            }

            if (adapterBehaviour == null || panelPrefab == null || spawnParent == null || panelManager == null)
            {
                isValid = false;
                if (logErrors && !hasLoggedMissingReferences)
                {
                    hasLoggedMissingReferences = true;
                    Debug.LogError($"{nameof(DevLevelTestPanelTrigger)} on '{name}' is missing required references. Assign panelPrefab, spawnParent, panelManager, and adapterBehaviour.", this);
                }
            }

            return isValid;
        }

        private void ClampSettings()
        {
            requiredTapCount = Mathf.Max(1, requiredTapCount);
            tapResetDuration = Mathf.Max(0f, tapResetDuration);
        }
    }
}
