using System;
using System.Collections;
using Game.Shared.Ads.Core;
using Game.Shared.Bootstrap;
using Game.Shared.Navigation;
using Game.Shared.Save;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Shared.DeveloperTools.Runtime
{
    [DisallowMultipleComponent]
    public sealed class DeveloperSaveSection : MonoBehaviour
    {
        [Header("Value Inputs")]
        [SerializeField] private TMP_InputField levelInput;
        [SerializeField] private TMP_InputField goldInput;
        [SerializeField] private TMP_InputField livesInput;
        [SerializeField] private TMP_InputField handInput;
        [SerializeField] private TMP_InputField shuffleInput;
        [SerializeField] private TMP_InputField ufoInput;

        [Header("Value Buttons")]
        [SerializeField] private Button levelMinusButton;
        [SerializeField] private Button levelPlusButton;
        [SerializeField] private Button goldMinusButton;
        [SerializeField] private Button goldPlusButton;
        [SerializeField] private Button livesMinusButton;
        [SerializeField] private Button livesPlusButton;
        [SerializeField] private Button handMinusButton;
        [SerializeField] private Button handPlusButton;
        [SerializeField] private Button shuffleMinusButton;
        [SerializeField] private Button shufflePlusButton;
        [SerializeField] private Button ufoMinusButton;
        [SerializeField] private Button ufoPlusButton;

        [Header("Quick Actions")]
        [SerializeField] private Button add1000GoldButton;
        [SerializeField] private Button fullLivesButton;
        [SerializeField] private Button add10AllBoostersButton;
        [SerializeField] private Button setZeroLivesButton;
        [SerializeField] private Button setNoAdsButton;
        [SerializeField] private Button clearNoAdsButton;
        [SerializeField] private Button setStarterPackPurchasedButton;
        [SerializeField] private Button clearStarterPackPurchasedButton;
        [SerializeField] private Button startInfiniteLives60mButton;
        [SerializeField] private Button clearInfiniteLivesButton;
        [SerializeField] private Button unlockAdsNowButton;
        [SerializeField] private Button refreshRuntimeUiButton;

        [Header("Full Reset")]
        [SerializeField] private Button resetEntireSaveButton;
        [SerializeField] private TMP_Text resetEntireSaveButtonText;
        [SerializeField] private TMP_Text resetWarningText;
        [SerializeField, Min(1f)] private float resetConfirmationSeconds = 4f;

        [Header("Tuning")]
        [SerializeField, Min(1)] private int goldStep = 100;

        private bool initialized;
        private bool resetConfirmationArmed;
        private Coroutine resetConfirmationCoroutine;

        public void Initialize()
        {
            if (initialized || !DeveloperPanelAvailability.IsAllowed)
            {
                return;
            }

            initialized = true;
            ConfigureNumericInput(levelInput, HandleLevelInput);
            ConfigureNumericInput(goldInput, HandleGoldInput);
            ConfigureNumericInput(livesInput, HandleLivesInput);
            ConfigureNumericInput(handInput, value => HandleBoosterInput(GameSaveDataFactory.HandBoosterId, value));
            ConfigureNumericInput(shuffleInput, value => HandleBoosterInput(GameSaveDataFactory.ShuffleBoosterId, value));
            ConfigureNumericInput(ufoInput, value => HandleBoosterInput(GameSaveDataFactory.UfoBoosterId, value));

            Wire(levelMinusButton, () => ChangeLevel(-1));
            Wire(levelPlusButton, () => ChangeLevel(1));
            Wire(goldMinusButton, () => ChangeGold(-goldStep));
            Wire(goldPlusButton, () => ChangeGold(goldStep));
            Wire(livesMinusButton, () => ChangeLives(-1));
            Wire(livesPlusButton, () => ChangeLives(1));
            Wire(handMinusButton, () => ChangeBooster(GameSaveDataFactory.HandBoosterId, -1));
            Wire(handPlusButton, () => ChangeBooster(GameSaveDataFactory.HandBoosterId, 1));
            Wire(shuffleMinusButton, () => ChangeBooster(GameSaveDataFactory.ShuffleBoosterId, -1));
            Wire(shufflePlusButton, () => ChangeBooster(GameSaveDataFactory.ShuffleBoosterId, 1));
            Wire(ufoMinusButton, () => ChangeBooster(GameSaveDataFactory.UfoBoosterId, -1));
            Wire(ufoPlusButton, () => ChangeBooster(GameSaveDataFactory.UfoBoosterId, 1));

            Wire(add1000GoldButton, () => ChangeGold(1000));
            Wire(fullLivesButton, HandleFullLives);
            Wire(add10AllBoostersButton, HandleAdd10AllBoosters);
            Wire(setZeroLivesButton, () => SetLives(0));
            Wire(setNoAdsButton, () => SetNoAds(true));
            Wire(clearNoAdsButton, () => SetNoAds(false));
            Wire(setStarterPackPurchasedButton, () => SetStarterPackPurchased(true));
            Wire(clearStarterPackPurchasedButton, () => SetStarterPackPurchased(false));
            Wire(startInfiniteLives60mButton, HandleStartInfiniteLives);
            Wire(clearInfiniteLivesButton, HandleClearInfiniteLives);
            Wire(unlockAdsNowButton, HandleUnlockAdsNow);
            Wire(refreshRuntimeUiButton, HandleRefreshRuntimeUi);
            Wire(resetEntireSaveButton, HandleResetEntireSave);
            CancelResetConfirmation();
            Refresh();
        }

        public void Dispose()
        {
            if (!initialized)
            {
                return;
            }

            levelInput?.onEndEdit.RemoveAllListeners();
            goldInput?.onEndEdit.RemoveAllListeners();
            livesInput?.onEndEdit.RemoveAllListeners();
            handInput?.onEndEdit.RemoveAllListeners();
            shuffleInput?.onEndEdit.RemoveAllListeners();
            ufoInput?.onEndEdit.RemoveAllListeners();
            RemoveAllButtonListeners();
            CancelResetConfirmation();
            initialized = false;
        }

        public void HandlePanelClosed()
        {
            CancelResetConfirmation();
        }

        public void Refresh()
        {
            SaveManager saveManager = GetSaveManager();
            if (saveManager == null)
            {
                return;
            }

            SetInput(levelInput, saveManager.CurrentLevel);
            SetInput(goldInput, saveManager.Gold);
            SetInput(livesInput, saveManager.Lives);
            SetInput(handInput, saveManager.GetBoosterAmount(GameSaveDataFactory.HandBoosterId));
            SetInput(shuffleInput, saveManager.GetBoosterAmount(GameSaveDataFactory.ShuffleBoosterId));
            SetInput(ufoInput, saveManager.GetBoosterAmount(GameSaveDataFactory.UfoBoosterId));
        }

        public bool HasRequiredReferences()
        {
            return levelInput != null && goldInput != null && livesInput != null &&
                   handInput != null && shuffleInput != null && ufoInput != null &&
                   levelMinusButton != null && levelPlusButton != null &&
                   goldMinusButton != null && goldPlusButton != null &&
                   livesMinusButton != null && livesPlusButton != null &&
                   handMinusButton != null && handPlusButton != null &&
                   shuffleMinusButton != null && shufflePlusButton != null &&
                   ufoMinusButton != null && ufoPlusButton != null &&
                   add1000GoldButton != null && fullLivesButton != null &&
                   add10AllBoostersButton != null && setZeroLivesButton != null &&
                   setNoAdsButton != null && clearNoAdsButton != null &&
                   setStarterPackPurchasedButton != null &&
                   clearStarterPackPurchasedButton != null &&
                   startInfiniteLives60mButton != null && clearInfiniteLivesButton != null &&
                   unlockAdsNowButton != null && refreshRuntimeUiButton != null &&
                   resetEntireSaveButton != null && resetEntireSaveButtonText != null &&
                   resetWarningText != null;
        }

        private void HandleLevelInput(string value)
        {
            SaveManager saveManager = GetSaveManager();
            if (saveManager == null || !TryParseNonNegative(value, out int parsed))
            {
                Refresh();
                return;
            }

            saveManager.SetCurrentLevel(Mathf.Max(1, parsed));
            saveManager.Save();
            Refresh();
        }

        private void HandleGoldInput(string value)
        {
            SaveManager saveManager = GetSaveManager();
            if (saveManager == null || !TryParseNonNegative(value, out int parsed))
            {
                Refresh();
                return;
            }

            saveManager.SetGold(parsed);
            saveManager.Save();
            Refresh();
        }

        private void HandleLivesInput(string value)
        {
            if (!TryParseNonNegative(value, out int parsed))
            {
                Refresh();
                return;
            }

            SetLives(parsed);
        }

        private void HandleBoosterInput(string boosterId, string value)
        {
            SaveManager saveManager = GetSaveManager();
            if (saveManager == null || !TryParseNonNegative(value, out int parsed))
            {
                Refresh();
                return;
            }

            saveManager.SetBoosterAmount(boosterId, parsed);
            saveManager.Save();
            Refresh();
        }

        private void ChangeLevel(int delta)
        {
            SaveManager saveManager = GetSaveManager();
            if (saveManager == null)
            {
                return;
            }

            long next = (long)saveManager.CurrentLevel + delta;
            saveManager.SetCurrentLevel((int)Math.Max(1L, Math.Min(int.MaxValue, next)));
            saveManager.Save();
            Refresh();
        }

        private void ChangeGold(int delta)
        {
            SaveManager saveManager = GetSaveManager();
            if (saveManager == null)
            {
                return;
            }

            long next = (long)saveManager.Gold + delta;
            saveManager.SetGold((int)Math.Max(0L, Math.Min(int.MaxValue, next)));
            saveManager.Save();
            Refresh();
        }

        private void ChangeLives(int delta)
        {
            SaveManager saveManager = GetSaveManager();
            if (saveManager != null)
            {
                SetLives(saveManager.Lives + delta);
            }
        }

        private void SetLives(int value)
        {
            SaveManager saveManager = GetSaveManager();
            if (saveManager == null)
            {
                return;
            }

            int clamped = Mathf.Clamp(value, 0, saveManager.MaxLives);
            if (SharedSystemsBootstrap.Instance?.LivesService != null)
            {
                SharedSystemsBootstrap.Instance.LivesService.SetLives(clamped);
            }
            else
            {
                saveManager.SetLives(clamped);
                saveManager.Save();
            }

            Refresh();
        }

        private void ChangeBooster(string boosterId, int delta)
        {
            SaveManager saveManager = GetSaveManager();
            if (saveManager == null)
            {
                return;
            }

            long next = (long)saveManager.GetBoosterAmount(boosterId) + delta;
            saveManager.SetBoosterAmount(
                boosterId,
                (int)Math.Max(0L, Math.Min(int.MaxValue, next)));
            saveManager.Save();
            Refresh();
        }

        private void HandleFullLives()
        {
            SaveManager saveManager = GetSaveManager();
            if (saveManager != null)
            {
                SetLives(saveManager.MaxLives);
            }
        }

        private void HandleAdd10AllBoosters()
        {
            ChangeBoosterWithoutRefresh(GameSaveDataFactory.HandBoosterId, 10);
            ChangeBoosterWithoutRefresh(GameSaveDataFactory.ShuffleBoosterId, 10);
            ChangeBoosterWithoutRefresh(GameSaveDataFactory.UfoBoosterId, 10);
            GetSaveManager()?.Save();
            Refresh();
        }

        private void ChangeBoosterWithoutRefresh(string boosterId, int amount)
        {
            GetSaveManager()?.AddBooster(boosterId, amount);
        }

        private void SetNoAds(bool enabledState)
        {
            SaveManager saveManager = GetSaveManager();
            if (saveManager == null)
            {
                return;
            }

            saveManager.SetHasNoAds(enabledState);
            saveManager.Save();
            AdsService.Instance?.RefreshRuntimeStateForDeveloperTools();
            Refresh();
        }

        private void SetStarterPackPurchased(bool purchased)
        {
            SaveManager saveManager = GetSaveManager();
            if (saveManager == null)
            {
                return;
            }

            saveManager.SetStarterPackPurchased(purchased);
            saveManager.Save();
            Refresh();
        }

        private void HandleStartInfiniteLives()
        {
            SaveManager saveManager = GetSaveManager();
            if (saveManager == null)
            {
                return;
            }

            saveManager.SetInfiniteLivesEndUtc(
                DateTimeOffset.UtcNow.AddMinutes(60).ToUnixTimeSeconds());
            saveManager.Save();
            SharedSystemsBootstrap.Instance?.LivesService?.Refresh();
            Refresh();
        }

        private void HandleClearInfiniteLives()
        {
            SaveManager saveManager = GetSaveManager();
            if (saveManager == null)
            {
                return;
            }

            saveManager.SetInfiniteLivesEndUtc(0);
            saveManager.Save();
            SharedSystemsBootstrap.Instance?.LivesService?.Refresh();
            Refresh();
        }

        private void HandleUnlockAdsNow()
        {
            SaveManager saveManager = GetSaveManager();
            if (saveManager == null)
            {
                return;
            }

            saveManager.SetCurrentLevel(Mathf.Max(
                saveManager.CurrentLevel,
                AdsService.MonetizationUnlockCompletedLevel + 1));
            saveManager.Save();
            AdsService.Instance?.RefreshRuntimeStateForDeveloperTools();
            Refresh();
        }

        private void HandleRefreshRuntimeUi()
        {
            if (!DeveloperPanelAvailability.IsAllowed) return;
            SaveManager saveManager = GetSaveManager();
            saveManager?.RefreshRuntimeStateForDeveloperTools();
            SharedSystemsBootstrap.Instance?.LivesService?.Refresh();
            AdsService.Instance?.RefreshRuntimeStateForDeveloperTools();
            Refresh();
        }

        private void HandleResetEntireSave()
        {
            if (!DeveloperPanelAvailability.IsAllowed) return;
            if (!resetConfirmationArmed)
            {
                resetConfirmationArmed = true;
                SetText(resetEntireSaveButtonText, "CONFIRM RESET");
                SetText(resetWarningText, "Press again before timeout to reset the complete save.");
                if (resetConfirmationCoroutine != null)
                {
                    StopCoroutine(resetConfirmationCoroutine);
                }

                resetConfirmationCoroutine = StartCoroutine(ResetConfirmationTimeout());
                return;
            }

            SaveManager saveManager = GetSaveManager();
            if (saveManager == null)
            {
                CancelResetConfirmation();
                return;
            }

            CancelResetConfirmation();
            saveManager.ResetSave();
            SharedSystemsBootstrap.Instance?.LivesService?.Refresh();
            AdsService.Instance?.RefreshRuntimeStateForDeveloperTools();
            Refresh();
            DeveloperPanelBootstrap.Instance?.Close();
            SceneLoader.Instance?.ReloadCurrentScene();
        }

        private IEnumerator ResetConfirmationTimeout()
        {
            yield return new WaitForSecondsRealtime(resetConfirmationSeconds);
            resetConfirmationCoroutine = null;
            CancelResetConfirmation();
        }

        private void CancelResetConfirmation()
        {
            if (resetConfirmationCoroutine != null)
            {
                StopCoroutine(resetConfirmationCoroutine);
                resetConfirmationCoroutine = null;
            }

            resetConfirmationArmed = false;
            SetText(resetEntireSaveButtonText, "RESET ENTIRE SAVE");
            SetText(resetWarningText, string.Empty);
        }

        private static SaveManager GetSaveManager()
        {
            return DeveloperPanelAvailability.IsAllowed &&
                   SaveManager.Instance != null && SaveManager.Instance.IsInitialized
                ? SaveManager.Instance
                : null;
        }

        private static void ConfigureNumericInput(
            TMP_InputField input,
            UnityEngine.Events.UnityAction<string> onEndEdit)
        {
            if (input == null)
            {
                return;
            }

            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.characterValidation = TMP_InputField.CharacterValidation.Integer;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.onEndEdit.RemoveAllListeners();
            input.onEndEdit.AddListener(onEndEdit);
        }

        private static bool TryParseNonNegative(string value, out int parsed)
        {
            return int.TryParse(value, out parsed) && parsed >= 0;
        }

        private static void SetInput(TMP_InputField input, int value)
        {
            input?.SetTextWithoutNotify(value.ToString());
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null)
            {
                text.text = value ?? string.Empty;
            }
        }

        private static void Wire(Button button, UnityEngine.Events.UnityAction action)
        {
            button?.onClick.AddListener(action);
        }

        private void RemoveAllButtonListeners()
        {
            Button[] buttons =
            {
                levelMinusButton, levelPlusButton, goldMinusButton, goldPlusButton,
                livesMinusButton, livesPlusButton, handMinusButton, handPlusButton,
                shuffleMinusButton, shufflePlusButton, ufoMinusButton, ufoPlusButton,
                add1000GoldButton, fullLivesButton, add10AllBoostersButton,
                setZeroLivesButton, setNoAdsButton, clearNoAdsButton,
                setStarterPackPurchasedButton, clearStarterPackPurchasedButton,
                startInfiniteLives60mButton, clearInfiniteLivesButton,
                unlockAdsNowButton, refreshRuntimeUiButton, resetEntireSaveButton
            };

            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i]?.onClick.RemoveAllListeners();
            }
        }
    }
}
