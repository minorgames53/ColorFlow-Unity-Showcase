using System.Collections.Generic;
using DG.Tweening;
using Gameplay.Levels;
using Gameplay.SourceBoxes;
using Gameplay.TargetBoxes;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;

namespace Gameplay.Tutorial
{
    [DisallowMultipleComponent]
    public sealed class LevelOneTapTutorialController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SourceBoxBoardController sourceBoxBoardController;
        [SerializeField] private TargetLaneController targetLaneController;
        [SerializeField] private LevelBuildController levelBuildController;
        [SerializeField] private GameObject handPrefab;
        [SerializeField] private Transform handParent;

        [Header("Tutorial Info")]
        [SerializeField] private GameObject tutorialInfo;
        [SerializeField] private TMP_Text tutorialInfoText;
        [SerializeField] private LocalizedString tutorialInfoMessage =
            new LocalizedString("General", "tutorial.level1.match_colors");

        [Header("Level 1 Tutorial Glow")]
        [SerializeField] private GameObject sourceGlowPrefab;
        [SerializeField] private GameObject targetGlowPrefab;
        [SerializeField, Range(0, 255)] private int minGlowAlpha = 0;
        [SerializeField, Range(0, 255)] private int maxGlowAlpha = 255;
        [Tooltip("Seconds for one full min-to-max-to-min pulse (0 to 255 to 0 by default).")]
        [SerializeField, Min(0.01f)] private float glowPulseDuration = 0.5f;

        [Header("Target")]
        [SerializeField] private int tutorialLevelNumber = 1;
        [SerializeField] private int targetCellIndex;

        [Header("Hand Transform")]
        [SerializeField] private Vector3 handLocalPosition;
        [SerializeField] private Vector3 handLocalEulerAngles;
        [SerializeField] private Vector3 handStartScale = Vector3.one;

        [Header("Hand Tween")]
        [SerializeField] private Vector3 handTapScale = new Vector3(0.85f, 0.85f, 0.85f);
        [SerializeField] private Vector3 handRelativeMove = new Vector3(0.2f, 0.2f, 0f);
        [SerializeField, Min(0f)] private float handInitialDelay = 2f;
        [SerializeField, Min(0f)] private float handRepeatInterval = 3f;
        [SerializeField, Min(0f)] private float handTweenDuration = 0.25f;
        [SerializeField] private Ease handTweenEase = Ease.Linear;
        [SerializeField] private int handTweenLoops = 4;

        private readonly List<SourceBox> blockedSourceBoxes = new List<SourceBox>();
        private SourceBox targetSourceBox;
        private GameObject spawnedHand;
        private Sequence handSequence;
        private Vector3 spawnedHandInitialLocalPosition;
        private Quaternion spawnedHandInitialLocalRotation;
        private Vector3 spawnedHandInitialLocalScale;
        private bool isRunning;
        private bool isCompleting;
        private GameObject spawnedSourceGlow;
        private GameObject spawnedTargetGlow;
        private readonly List<SpriteRenderer> glowRenderers = new List<SpriteRenderer>();
        private Tween glowTween;
        private bool tutorialInfoSubscribed;
        private Object tutorialInfoOwner;
        private LocalizedString activeTutorialInfoMessage;

        private void Awake()
        {
            if (tutorialInfo != null)
            {
                tutorialInfo.SetActive(false);
            }
        }

        private void OnEnable()
        {
            if (levelBuildController != null)
            {
                levelBuildController.LevelClearing += ClearTutorialState;
            }
        }

        private void OnValidate()
        {
            tutorialLevelNumber = Mathf.Max(1, tutorialLevelNumber);
            targetCellIndex = Mathf.Max(0, targetCellIndex);
            handInitialDelay = Mathf.Max(0f, handInitialDelay);
            handRepeatInterval = Mathf.Max(0f, handRepeatInterval);
            handTweenDuration = Mathf.Max(0f, handTweenDuration);
            handTweenLoops = Mathf.Max(0, handTweenLoops);
            NormalizeGlowSettings();
        }

        private void OnDisable()
        {
            if (levelBuildController != null)
            {
                levelBuildController.LevelClearing -= ClearTutorialState;
            }

            ClearTutorialState();
        }

        private void OnDestroy()
        {
            ClearTutorialState();
            HideTutorialInfo(tutorialInfoOwner);
        }

        public void TryStartTutorial(int displayedLevelNumber)
        {
            ClearTutorialState();

            if (!isActiveAndEnabled || displayedLevelNumber != tutorialLevelNumber)
            {
                return;
            }

            var save = Game.Shared.Save.SaveManager.Instance;
            if (save != null && save.GetBoolSetting(Game.Shared.Save.SaveKeys.LevelOneTapTutorialCompleted))
            {
                return;
            }

            if (sourceBoxBoardController == null)
            {
                Debug.LogError($"{nameof(LevelOneTapTutorialController)} on '{name}' cannot start because SourceBoxBoardController is missing.", this);
                return;
            }

            if (!sourceBoxBoardController.TryGetSourceBoxByCellIndex(targetCellIndex, out targetSourceBox))
            {
                Debug.LogError($"{nameof(LevelOneTapTutorialController)} on '{name}' cannot start because no SourceBox exists at cell index {targetCellIndex}.", this);
                targetSourceBox = null;
                return;
            }

            if (handPrefab == null)
            {
                Debug.LogError($"{nameof(LevelOneTapTutorialController)} on '{name}' cannot start because Hand Prefab is missing.", this);
                targetSourceBox = null;
                return;
            }

            if (levelBuildController == null || targetLaneController == null ||
                tutorialInfo == null || tutorialInfoText == null ||
                tutorialInfoMessage == null || tutorialInfoMessage.IsEmpty ||
                sourceGlowPrefab == null || targetGlowPrefab == null)
            {
                Debug.LogError($"{nameof(LevelOneTapTutorialController)} on '{name}' needs its tutorial UI, localization, glow prefab and level controller references.", this);
                targetSourceBox = null;
                return;
            }

            TargetBox matchingTargetBox = ResolveMatchingTargetBox();
            if (matchingTargetBox == null)
            {
                Debug.LogError($"{nameof(LevelOneTapTutorialController)} on '{name}' cannot find an active target matching source color {targetSourceBox.ColorId}.", this);
                targetSourceBox = null;
                return;
            }

            isRunning = true;
            BlockNonTargetSourceBoxes();
            targetSourceBox.ReleaseStarted += HandleTargetReleaseStarted;
            SpawnHand();
            PlayHandTween();
            ShowTutorialInfo(this, tutorialInfoMessage);
            StartGlows(matchingTargetBox);
        }

        public void CompleteTutorial()
        {
            if (!isRunning || isCompleting)
            {
                return;
            }

            isCompleting = true;
            var save = Game.Shared.Save.SaveManager.Instance;
            if (save != null && !save.GetBoolSetting(Game.Shared.Save.SaveKeys.LevelOneTapTutorialCompleted))
            {
                save.SetSetting(Game.Shared.Save.SaveKeys.LevelOneTapTutorialCompleted, true);
                save.Save();
            }
            ClearTutorialState();
        }

        private void BlockNonTargetSourceBoxes()
        {
            IReadOnlyList<SourceBox> sourceBoxes = sourceBoxBoardController.SpawnedSourceBoxes;
            for (int i = 0; i < sourceBoxes.Count; i++)
            {
                SourceBox sourceBox = sourceBoxes[i];
                if (sourceBox == null || sourceBox == targetSourceBox)
                {
                    continue;
                }

                sourceBox.SetInteractionBlocked(true);
                blockedSourceBoxes.Add(sourceBox);
            }
        }

        private TargetBox ResolveMatchingTargetBox()
        {
            IReadOnlyList<TargetBox> targets = targetLaneController.SpawnedTargetBoxes;
            for (int i = 0; i < targets.Count; i++)
            {
                TargetBox candidate = targets[i];
                if (candidate != null && candidate.CanReserve(targetSourceBox.ColorId))
                {
                    return candidate;
                }
            }

            return null;
        }

        // Share the existing HUD references without sharing tutorial progression or cleanup.
        public void ShowTutorialInfo(Object owner, LocalizedString message)
        {
            if (owner == null || tutorialInfo == null || tutorialInfoText == null ||
                message == null || message.IsEmpty)
            {
                return;
            }

            HideTutorialInfo(tutorialInfoOwner);
            tutorialInfoOwner = owner;
            activeTutorialInfoMessage = message;
            tutorialInfoText.richText = true;
            tutorialInfoText.text = string.Empty;
            tutorialInfoSubscribed = true;
            activeTutorialInfoMessage.StringChanged += HandleTutorialInfoChanged;
            activeTutorialInfoMessage.RefreshString();
            tutorialInfo.SetActive(true);
        }

        public void HideTutorialInfo(Object owner)
        {
            if (tutorialInfoOwner != owner)
            {
                return;
            }

            tutorialInfoOwner = null;
            if (tutorialInfoSubscribed && activeTutorialInfoMessage != null)
            {
                activeTutorialInfoMessage.StringChanged -= HandleTutorialInfoChanged;
            }
            tutorialInfoSubscribed = false;
            activeTutorialInfoMessage = null;
            if (tutorialInfo != null)
            {
                tutorialInfo.SetActive(false);
            }
        }

        private void HandleTutorialInfoChanged(string localizedText)
        {
            if (tutorialInfoOwner != null && tutorialInfoText != null)
            {
                tutorialInfoText.SetText(localizedText);
            }
        }

        private void NormalizeGlowSettings()
        {
            int low = Mathf.Clamp(Mathf.Min(minGlowAlpha, maxGlowAlpha), 0, 255);
            int high = Mathf.Clamp(Mathf.Max(minGlowAlpha, maxGlowAlpha), 0, 255);
            minGlowAlpha = low;
            maxGlowAlpha = high;
            glowPulseDuration = Mathf.Max(0.01f, glowPulseDuration);
        }

        private void StartGlows(TargetBox matchingTargetBox)
        {
            NormalizeGlowSettings();
            spawnedSourceGlow = SpawnGlow(sourceGlowPrefab, targetSourceBox.transform);
            spawnedTargetGlow = SpawnGlow(targetGlowPrefab, matchingTargetBox.transform);
            float lowAlpha = minGlowAlpha / 255f;
            SetGlowAlpha(lowAlpha);
            glowTween = DOTween.To(() => lowAlpha, SetGlowAlpha, maxGlowAlpha / 255f, glowPulseDuration * 0.5f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        }

        private GameObject SpawnGlow(GameObject prefab, Transform parent)
        {
            // Preserve the prefab's authored local transform under its box.
            GameObject instance = Instantiate(prefab, parent, false);
            glowRenderers.AddRange(instance.GetComponentsInChildren<SpriteRenderer>(true));
            foreach (Collider2D collider in instance.GetComponentsInChildren<Collider2D>(true))
            {
                collider.enabled = false;
            }
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }
            return instance;
        }

        private void SetGlowAlpha(float alpha)
        {
            for (int i = 0; i < glowRenderers.Count; i++)
            {
                SpriteRenderer renderer = glowRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Color color = renderer.color;
                color.a = alpha;
                renderer.color = color;
            }
        }

        private void SpawnHand()
        {
            Transform parent = handParent != null ? handParent : targetSourceBox.transform;
            spawnedHand = Instantiate(handPrefab, parent);
            spawnedHand.name = $"{handPrefab.name}_LevelOneTapTutorial";

            Transform handTransform = spawnedHand.transform;
            if (handParent != null)
            {
                handTransform.position = targetSourceBox.transform.position;
                handTransform.localPosition += handLocalPosition;
            }
            else
            {
                handTransform.localPosition = handLocalPosition;
            }

            handTransform.localRotation = Quaternion.Euler(handLocalEulerAngles);
            handTransform.localScale = handStartScale;
            spawnedHandInitialLocalPosition = handTransform.localPosition;
            spawnedHandInitialLocalRotation = handTransform.localRotation;
            spawnedHandInitialLocalScale = handTransform.localScale;
        }

        private void PlayHandTween()
        {
            if (spawnedHand == null)
            {
                return;
            }

            Transform handTransform = spawnedHand.transform;
            handSequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDisable);

            if (handInitialDelay > 0f)
            {
                handSequence.AppendInterval(handInitialDelay);
            }

            handSequence.Append(CreateHandTapSequence(handTransform));

            if (handRepeatInterval > 0f)
            {
                handSequence.AppendInterval(handRepeatInterval);
            }

            handSequence.SetLoops(-1, LoopType.Restart);
        }

        private Sequence CreateHandTapSequence(Transform handTransform)
        {
            Sequence sequence = DOTween.Sequence();
            sequence.AppendCallback(() =>
            {
                if (handTransform == null)
                {
                    return;
                }

                handTransform.localPosition = spawnedHandInitialLocalPosition;
                handTransform.localRotation = spawnedHandInitialLocalRotation;
                handTransform.localScale = spawnedHandInitialLocalScale;
            });

            sequence
                .Join(handTransform
                    .DOScale(handTapScale, handTweenDuration)
                    .SetEase(handTweenEase))
                .Join(handTransform
                    .DOMove(handRelativeMove, handTweenDuration)
                    .SetEase(handTweenEase)
                    .SetRelative())
                .SetLoops(handTweenLoops, LoopType.Yoyo);

            return sequence;
        }

        private void HandleTargetReleaseStarted(SourceBox sourceBox)
        {
            if (sourceBox != targetSourceBox)
            {
                return;
            }

            CompleteTutorial();
        }

        private void ClearTutorialState()
        {
            // Invalidate callbacks before cancelling localization and animation work.
            isRunning = false;
            HideTutorialInfo(this);

            glowTween?.Kill(false);
            glowTween = null;
            SetGlowAlpha(0f);
            glowRenderers.Clear();
            if (spawnedSourceGlow != null)
            {
                Destroy(spawnedSourceGlow);
                spawnedSourceGlow = null;
            }
            if (spawnedTargetGlow != null)
            {
                Destroy(spawnedTargetGlow);
                spawnedTargetGlow = null;
            }

            if (targetSourceBox != null)
            {
                targetSourceBox.ReleaseStarted -= HandleTargetReleaseStarted;
                targetSourceBox = null;
            }

            handSequence?.Kill(false);
            handSequence = null;

            if (spawnedHand != null)
            {
                Destroy(spawnedHand);
                spawnedHand = null;
            }

            for (int i = 0; i < blockedSourceBoxes.Count; i++)
            {
                SourceBox sourceBox = blockedSourceBoxes[i];
                if (sourceBox != null)
                {
                    sourceBox.SetInteractionBlocked(false);
                }
            }

            blockedSourceBoxes.Clear();
            isRunning = false;
            isCompleting = false;
        }
    }
}
