using System;
using System.Collections.Generic;
using Game.Shared.Analytics;
using Game.Shared.Analytics.Core;
using Game.Shared.Save;
using Game.Shared.UI;
using Game.Shared.UI.Panels;
using Gameplay.Analytics;
using Gameplay.Boosters;
using Gameplay.UI.Shop;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;

namespace Gameplay.UI.HUD
{
    [DisallowMultipleComponent]
    public sealed class AddBoosterPanelController : MonoBehaviour
    {
        [Serializable]
        private sealed class BoosterOffer
        {
            public BoosterType boosterType;
            public Sprite boosterSprite;
            public LocalizedString boosterName;
            public LocalizedString description;
            [Min(1)] public int quantity = 3;
            [Min(0)] public int goldCost;
        }

        [Header("Panel")]
        [SerializeField] private PanelManager panelManager;
        [SerializeField] private UIPanel panel;
        [SerializeField] private GameShopPanelController gameShopPanelController;
        [SerializeField] private GameplayHudController gameplayHudController;

        [Header("Offers")]
        [SerializeField] private BoosterOffer[] offers;

        [Header("Localization")]
        [SerializeField] private LocalizedString quantityFormat;

        [Header("Dynamic Presentation")]
        [SerializeField] private Image boosterImage;
        [SerializeField] private TMP_Text boosterNameText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private TMP_Text quantityText;
        [SerializeField] private TweenButton buyButton;
        [SerializeField] private TMP_Text priceText;
        [SerializeField] private TweenButton closeButton;

        private SaveManager saveManager;
        private BoosterOffer selectedOffer;
        private readonly Dictionary<string, object> quantityFormatValues =
            new Dictionary<string, object>();
        private object[] quantityFormatArguments;
        private bool quantityFormatSubscribed;
        private bool boosterNameSubscribed;
        private bool descriptionSubscribed;

        private void OnEnable()
        {
            BindLocalization();
            RegisterButtons();
            gameplayHudController?.BringGoldDisplayToFront();
            RefreshSelectedOffer();
        }

        private void OnDisable()
        {
            UnregisterButtons();
            UnbindLocalization();
            gameplayHudController?.RestoreGoldDisplaySortingOrder();
            saveManager = null;
        }

        private void OnDestroy()
        {
            UnbindLocalization();
        }

        public bool Open(BoosterType boosterType)
        {
            if (panelManager != null && panelManager.TryDeferOpen(panel, () => Open(boosterType))) return true;

            BoosterOffer offer = FindOffer(boosterType);
            if (offer == null)
            {
                Debug.LogWarning(
                    $"{nameof(AddBoosterPanelController)} on '{name}' has no offer configured for {boosterType}.",
                    this);
                return false;
            }

            if (panelManager == null || panel == null)
            {
                Debug.LogWarning(
                    $"{nameof(AddBoosterPanelController)} on '{name}' cannot open because its panel references are incomplete.",
                    this);
                return false;
            }

            if (selectedOffer != offer)
            {
                UnbindSelectedOfferLocalization();
                selectedOffer = offer;
                boosterNameText?.SetText(string.Empty);
                descriptionText?.SetText(string.Empty);

                if (isActiveAndEnabled)
                {
                    BindSelectedOfferLocalization();
                }
            }

            RefreshSelectedOffer();

            if (panel.gameObject.activeInHierarchy)
            {
                return true;
            }

            if (panelManager.HasOpenPanel)
            {
                panelManager.Push(panel);
            }
            else
            {
                panelManager.OpenRoot(panel);
            }

            return true;
        }

        public void Close()
        {
            if (panelManager == null || panel == null)
            {
                Debug.LogWarning(
                    $"{nameof(AddBoosterPanelController)} on '{name}' cannot close because its panel references are incomplete.",
                    this);
                return;
            }

            panelManager.TryClose(panel);
        }

        public bool TryGetOfferPresentation(
            BoosterType boosterType,
            out Sprite boosterSprite,
            out LocalizedString boosterName,
            out LocalizedString description)
        {
            BoosterOffer offer = FindOffer(boosterType);
            boosterSprite = offer != null ? offer.boosterSprite : null;
            boosterName = offer?.boosterName;
            description = offer?.description;
            return offer != null && boosterSprite != null;
        }

        private void RegisterButtons()
        {
            if (buyButton != null)
            {
                buyButton.onClick.RemoveListener(HandleBuyClicked);
                buyButton.onClick.AddListener(HandleBuyClicked);
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Close);
                closeButton.onClick.AddListener(Close);
            }
        }

        private void UnregisterButtons()
        {
            buyButton?.onClick.RemoveListener(HandleBuyClicked);
            closeButton?.onClick.RemoveListener(Close);
        }

        private void HandleBuyClicked()
        {
            saveManager = SaveManager.Instance;
            if (selectedOffer == null || saveManager == null || !saveManager.IsInitialized)
            {
                return;
            }

            string inventoryId = GetInventoryId(selectedOffer.boosterType);
            if (string.IsNullOrEmpty(inventoryId))
            {
                Debug.LogError(
                    $"{nameof(AddBoosterPanelController)} cannot grant unsupported booster type {selectedOffer.boosterType}.",
                    this);
                return;
            }

            int goldCost = Mathf.Max(0, selectedOffer.goldCost);
            int goldBeforePurchase = saveManager.Gold;
            int boosterAmountBeforePurchase = saveManager.GetBoosterAmount(inventoryId);
            LevelAnalyticsTracker analyticsTracker = LevelAnalyticsTracker.Instance;
            int displayedLevelNumber = 0;
            int internalLevelNumber = 0;
            bool hasAnalyticsLevelContext = analyticsTracker != null &&
                                            analyticsTracker.TryGetLevelContext(
                                                out displayedLevelNumber,
                                                out internalLevelNumber);
            if (saveManager.Gold < goldCost || !saveManager.SpendGold(goldCost))
            {
                gameShopPanelController?.OpenForInsufficientGold();
                return;
            }

            saveManager.AddBooster(inventoryId, Mathf.Max(1, selectedOffer.quantity));
            if (hasAnalyticsLevelContext && goldCost > 0 &&
                saveManager.Gold < goldBeforePurchase &&
                saveManager.GetBoosterAmount(inventoryId) > boosterAmountBeforePurchase)
            {
                AnalyticsBootstrap.Instance?.Track(
                    AnalyticsEventFactory.CreateBoosterPurchased(
                        displayedLevelNumber,
                        internalLevelNumber,
                        inventoryId));
            }

            Close();
        }

        private void RefreshSelectedOffer()
        {
            if (selectedOffer == null)
            {
                return;
            }

            if (boosterImage != null)
            {
                boosterImage.sprite = selectedOffer.boosterSprite;
            }

            selectedOffer.boosterName?.RefreshString();
            selectedOffer.description?.RefreshString();

            quantityFormatValues["quantity"] = Mathf.Max(1, selectedOffer.quantity);
            quantityFormat?.RefreshString();
            priceText?.SetText("{0}", Mathf.Max(0, selectedOffer.goldCost));
        }

        private void BindLocalization()
        {
            if (!quantityFormatSubscribed && quantityFormat != null && !quantityFormat.IsEmpty)
            {
                if (!quantityFormatValues.ContainsKey("quantity"))
                {
                    quantityFormatValues["quantity"] = 0;
                }

                if (quantityFormatArguments == null)
                {
                    quantityFormatArguments = new object[] { quantityFormatValues };
                }

                quantityFormat.Arguments = quantityFormatArguments;
                quantityFormat.StringChanged += HandleQuantityFormatChanged;
                quantityFormatSubscribed = true;
            }

            BindSelectedOfferLocalization();
        }

        private void BindSelectedOfferLocalization()
        {
            if (selectedOffer == null)
            {
                return;
            }

            if (!boosterNameSubscribed &&
                selectedOffer.boosterName != null &&
                !selectedOffer.boosterName.IsEmpty)
            {
                selectedOffer.boosterName.StringChanged += HandleBoosterNameChanged;
                boosterNameSubscribed = true;
            }

            if (!descriptionSubscribed &&
                selectedOffer.description != null &&
                !selectedOffer.description.IsEmpty)
            {
                selectedOffer.description.StringChanged += HandleDescriptionChanged;
                descriptionSubscribed = true;
            }
        }

        private void UnbindLocalization()
        {
            UnbindSelectedOfferLocalization();

            if (!quantityFormatSubscribed)
            {
                return;
            }

            quantityFormat.StringChanged -= HandleQuantityFormatChanged;
            quantityFormatSubscribed = false;
        }

        private void UnbindSelectedOfferLocalization()
        {
            if (selectedOffer == null)
            {
                boosterNameSubscribed = false;
                descriptionSubscribed = false;
                return;
            }

            if (boosterNameSubscribed)
            {
                selectedOffer.boosterName.StringChanged -= HandleBoosterNameChanged;
                boosterNameSubscribed = false;
            }

            if (descriptionSubscribed)
            {
                selectedOffer.description.StringChanged -= HandleDescriptionChanged;
                descriptionSubscribed = false;
            }
        }

        private void HandleBoosterNameChanged(string localizedText)
        {
            boosterNameText?.SetText(localizedText);
        }

        private void HandleDescriptionChanged(string localizedText)
        {
            descriptionText?.SetText(localizedText);
        }

        private void HandleQuantityFormatChanged(string localizedText)
        {
            quantityText?.SetText(localizedText);
        }

        private BoosterOffer FindOffer(BoosterType boosterType)
        {
            if (offers == null)
            {
                return null;
            }

            for (int i = 0; i < offers.Length; i++)
            {
                BoosterOffer offer = offers[i];
                if (offer != null && offer.boosterType == boosterType)
                {
                    return offer;
                }
            }

            return null;
        }

        private static string GetInventoryId(BoosterType boosterType)
        {
            switch (boosterType)
            {
                case BoosterType.Hand:
                    return "hand";
                case BoosterType.Shuffle:
                    return "shuffle";
                case BoosterType.Ufo:
                    return "ufo";
                default:
                    return string.Empty;
            }
        }
    }
}
