using System;
using Game.Shared.Store.UI;
using Game.Shared.UI;
using Game.Shared.UI.Panels;
using UnityEngine;

namespace Gameplay.UI.Shop
{
    [DisallowMultipleComponent]
    public sealed class GameShopPanelController : MonoBehaviour
    {
        [SerializeField] private PanelManager panelManager;
        [SerializeField] private UIPanel shopPanel;
        [SerializeField] private TweenButton closeButton;
        [SerializeField] private ShopContentController shopContent;

        public bool IsOpen => shopPanel != null && shopPanel.gameObject.activeInHierarchy;
        public event Action Opened;
        public event Action Closed;

        private void OnEnable()
        {
            RegisterCloseButton();
            shopContent?.RefreshAll();
            Opened?.Invoke();
        }

        private void OnDisable()
        {
            UnregisterCloseButton();
            Closed?.Invoke();
        }

        public void Open()
        {
            OpenInternal();
        }

        public void OpenForInsufficientGold()
        {
            OpenInternal();
        }

        public void Close()
        {
            if (panelManager == null || shopPanel == null)
            {
                Debug.LogWarning(
                    $"{nameof(GameShopPanelController)} cannot close because its panel references are incomplete.",
                    this);
                return;
            }

            panelManager.TryClose(shopPanel);
        }

        private void OpenInternal()
        {
            if (panelManager != null && panelManager.TryDeferOpen(shopPanel, OpenInternal)) return;

            if (panelManager == null || shopPanel == null)
            {
                Debug.LogWarning(
                    $"{nameof(GameShopPanelController)} cannot open because its panel references are incomplete.",
                    this);
                return;
            }

            if (IsOpen)
            {
                shopContent?.RefreshAll();
                return;
            }

            if (panelManager.HasOpenPanel)
            {
                panelManager.Push(shopPanel);
            }
            else
            {
                panelManager.OpenRoot(shopPanel);
            }
        }

        private void RegisterCloseButton()
        {
            if (closeButton == null)
            {
                return;
            }

            closeButton.onClick.RemoveListener(Close);
            closeButton.onClick.AddListener(Close);
        }

        private void UnregisterCloseButton()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Close);
            }
        }
    }
}
