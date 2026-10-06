using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Shared.UI.Panels
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIPanel))]
    public sealed class NoConnectionPanelController : MonoBehaviour
    {
        [SerializeField] private UIPanel panel;
        [SerializeField] private Button refreshButton;

        public UIPanel Panel => panel;
        public bool IsVisible => panel != null && panel.gameObject.activeInHierarchy;
        public event Action RefreshRequested;

        private void OnEnable()
        {
            refreshButton.onClick.AddListener(HandleRefresh);
            refreshButton.interactable = true;
            KeepSelectionInsidePanel();
        }

        private void OnDisable()
        {
            refreshButton.onClick.RemoveListener(HandleRefresh);
        }

        private void LateUpdate()
        {
            // Background raycasts do not block keyboard/controller Submit navigation.
            KeepSelectionInsidePanel();
        }

        private void KeepSelectionInsidePanel()
        {
            EventSystem events = EventSystem.current;
            if (events == null) return;
            GameObject selected = events.currentSelectedGameObject;
            if (selected == null || !selected.transform.IsChildOf(transform))
                events.SetSelectedGameObject(refreshButton.gameObject);
        }

        private void HandleRefresh()
        {
            RefreshRequested?.Invoke();
        }
    }
}
