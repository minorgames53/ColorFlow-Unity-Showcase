using Game.Shared.Save;
using TMPro;
using UnityEngine;

namespace Game.Shared.UI
{
    [DisallowMultipleComponent]
    public sealed class GoldDisplayPresenter : MonoBehaviour
    {
        [SerializeField] private TMP_Text goldText;

        private bool presentationOverrideActive;

        public bool IsPresentationOverrideActive => presentationOverrideActive;

        public void BeginPresentation(int amount)
        {
            presentationOverrideActive = true;
            SetText(amount);
        }

        public void SetPresentationAmount(int amount)
        {
            if (presentationOverrideActive)
            {
                SetText(amount);
            }
        }

        public void SetAuthoritativeAmount(int amount)
        {
            if (!presentationOverrideActive)
            {
                SetText(amount);
            }
        }

        public void SnapToAuthoritativeAmount()
        {
            presentationOverrideActive = false;
            SaveManager saveManager = SaveManager.Instance;
            if (saveManager != null && saveManager.IsInitialized)
            {
                SetText(saveManager.Gold);
            }
        }

        private void OnDisable()
        {
            if (presentationOverrideActive)
            {
                SnapToAuthoritativeAmount();
            }
        }

        private void SetText(int amount)
        {
            goldText?.SetText("{0}", Mathf.Max(0, amount));
        }
    }
}
