using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;

namespace Gameplay.UI.World
{
    [DisallowMultipleComponent]
    public sealed class SourceBoxBoardFullMessageView : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameObject messageRoot;
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private Transform animatedTransform;

        [Header("Localization")]
        [SerializeField] private string stringTable = "UI";
        [SerializeField] private string localizationKey = "ui_board_is_full";

        [Header("Position")]
        [SerializeField] private Vector3 targetPositionOffset;

        [Header("Scale")]
        [SerializeField] private Vector3 startScale = Vector3.zero;
        [SerializeField] private Vector3 visibleScale = Vector3.one;
        [SerializeField, Min(0f)] private float scaleUpDuration = 0.15f;
        [SerializeField] private Ease scaleUpEase = Ease.OutBack;

        [Header("Fade")]
        [SerializeField, Min(0f)] private float visibleDuration = 1f;
        [SerializeField] private Vector3 fadeMoveOffset = new Vector3(0f, 0.5f, 0f);
        [SerializeField, Min(0f)] private float fadeMoveDuration = 0.25f;
        [SerializeField] private Ease fadeMoveEase = Ease.OutQuad;

        private Sequence activeSequence;
        private Vector3 animatedInitialLocalPosition;
        private bool hasCapturedAnimatedInitialLocalPosition;

        private void Reset()
        {
            messageRoot = gameObject;
            messageText = GetComponentInChildren<TMP_Text>(true);
            animatedTransform = transform;
        }

        private void Awake()
        {
            CacheMissingReferences();
            ResetImmediate();
        }

        private void OnEnable()
        {
            LocalizationSettings.SelectedLocaleChanged += HandleSelectedLocaleChanged;
        }

        private void OnDisable()
        {
            LocalizationSettings.SelectedLocaleChanged -= HandleSelectedLocaleChanged;
            ResetImmediate();
        }

        private void OnValidate()
        {
            scaleUpDuration = Mathf.Max(0f, scaleUpDuration);
            visibleDuration = Mathf.Max(0f, visibleDuration);
            fadeMoveDuration = Mathf.Max(0f, fadeMoveDuration);
        }

        public void PlayAt(Transform target)
        {
            if (target == null)
            {
                return;
            }

            PlayAt(target.position);
        }

        public void PlayAt(Vector3 worldPosition)
        {
            CacheMissingReferences();
            if (!ValidateReferences())
            {
                return;
            }

            activeSequence?.Kill(false);
            activeSequence = null;

            SetLocalizedText();
            messageRoot.SetActive(true);
            messageRoot.transform.position = worldPosition + targetPositionOffset;
            ResetAnimatedTransformPosition();
            animatedTransform.localScale = startScale;
            SetTextAlpha(1f);

            activeSequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDisable);

            activeSequence
                .Append(animatedTransform
                    .DOScale(visibleScale, scaleUpDuration)
                    .SetEase(scaleUpEase))
                .AppendInterval(visibleDuration)
                .Append(animatedTransform
                    .DOLocalMove(animatedInitialLocalPosition + fadeMoveOffset, fadeMoveDuration)
                    .SetEase(fadeMoveEase))
                .Join(messageText.DOFade(0f, fadeMoveDuration))
                .OnComplete(() =>
                {
                    activeSequence = null;
                    if (messageRoot != null)
                    {
                        messageRoot.SetActive(false);
                    }
                });
        }

        public void ResetImmediate()
        {
            activeSequence?.Kill(false);
            activeSequence = null;

            CacheMissingReferences();
            if (animatedTransform != null)
            {
                ResetAnimatedTransformPosition();
                animatedTransform.localScale = startScale;
            }

            SetTextAlpha(1f);

            if (messageRoot != null)
            {
                messageRoot.SetActive(false);
            }
        }

        private void HandleSelectedLocaleChanged(UnityEngine.Localization.Locale _)
        {
            SetLocalizedText();
        }

        private void SetLocalizedText()
        {
            if (messageText == null || string.IsNullOrWhiteSpace(stringTable) || string.IsNullOrWhiteSpace(localizationKey))
            {
                return;
            }

            string localizedText = LocalizationSettings.StringDatabase.GetLocalizedString(stringTable, localizationKey);
            messageText.text = string.IsNullOrEmpty(localizedText) ? localizationKey : localizedText;
        }

        private void SetTextAlpha(float alpha)
        {
            if (messageText == null)
            {
                return;
            }

            Color color = messageText.color;
            color.a = alpha;
            messageText.color = color;
        }

        private void CacheMissingReferences()
        {
            if (messageRoot == null)
            {
                messageRoot = gameObject;
            }

            if (messageText == null)
            {
                messageText = GetComponentInChildren<TMP_Text>(true);
            }

            if (animatedTransform == null && messageText != null)
            {
                animatedTransform = transform;
            }

            if (animatedTransform != null && !hasCapturedAnimatedInitialLocalPosition)
            {
                animatedInitialLocalPosition = animatedTransform.localPosition;
                hasCapturedAnimatedInitialLocalPosition = true;
            }
        }

        private void ResetAnimatedTransformPosition()
        {
            if (animatedTransform == null || messageRoot == null)
            {
                return;
            }

            if (animatedTransform == messageRoot.transform)
            {
                return;
            }

            animatedTransform.localPosition = animatedInitialLocalPosition;
        }

        private bool ValidateReferences()
        {
            bool isValid = true;
            if (messageRoot == null)
            {
                Debug.LogError($"{nameof(SourceBoxBoardFullMessageView)} on '{name}' is missing Message Root reference.", this);
                isValid = false;
            }

            if (messageText == null)
            {
                Debug.LogError($"{nameof(SourceBoxBoardFullMessageView)} on '{name}' is missing TMP_Text reference.", this);
                isValid = false;
            }

            if (animatedTransform == null)
            {
                Debug.LogError($"{nameof(SourceBoxBoardFullMessageView)} on '{name}' is missing Animated Transform reference.", this);
                isValid = false;
            }

            return isValid;
        }
    }
}
