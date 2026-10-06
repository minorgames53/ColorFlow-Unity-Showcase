using System;
using System.Collections.Generic;
using DG.Tweening;
using Game.Menu;
using Game.Shared.Audio;
using Game.Shared.UI;
using UnityEngine;

namespace Game.Shared.Lives.UI
{
    /// <summary>Popup-owned presentation only. Never grants lives or changes the economy.</summary>
    [DisallowMultipleComponent]
    public sealed class HeartFlyAnimator : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private RectTransform heartPrefab;
        [SerializeField] private RectTransform sourceHeart;
        [SerializeField] private MenuTopHudController livesHud;
        [SerializeField] private RectTransform animationRoot;
        [SerializeField] private UIInputLockService inputLock;

        [Header("SFX")]
        [SerializeField] private AudioKey spawnSfx = AudioKey.HeartSpawn;
        [SerializeField] private AudioKey arriveSfx = AudioKey.HeartArrive;

        [Header("Presentation")]
        [SerializeField, Min(0.01f)] private float flyDuration = 0.45f;
        [SerializeField, Range(0.07f, 0.10f)] private float spawnStagger = 0.08f;
        [SerializeField, Min(0.01f)] private float punchDuration = 0.22f;
        [SerializeField, Range(0f, 0.3f)] private float punchStrength = 0.27f;
        [SerializeField] private Vector2 curveOffset = new Vector2(80f, 140f);

        private const float SpawnDelay = 0.15f;
        private const float PopDuration = 0.12f;
        private sealed class Flight
        {
            public RectTransform Heart;
            public Vector2 Start;
            public float Delay;
            public bool Spawned;
            public bool Arrived;
        }

        private readonly List<Flight> flights = new List<Flight>();
        private Tween flightTween;
        private RectTransform targetHeart;
        private Vector3 sourceScale;
        private Vector3 targetScale;
        private Vector2 heartSize;
        private bool hasScales;
        private bool ownsInputLock;
        private bool ownsLivesForeground;
        private float lastImpactTime = -1f;
        private float lastImpactStrength;
        private Action onComplete;

        public void Prepare()
        {
            if (ownsInputLock || !CanPresent()) return;
            inputLock.LockUIInput();
            ownsInputLock = true;
            livesHud.BeginLivesRewardPresentation(this);
        }

        public void Play(int count, Action completion)
        {
            if (flightTween != null) return;
            onComplete = completion;
            try
            {
                if (count <= 0 || !CanPresent())
                {
                    Complete();
                    return;
                }

                Prepare();
                targetHeart = livesHud.LivesRewardTarget;
                sourceScale = sourceHeart.localScale;
                targetScale = targetHeart.localScale;
                hasScales = true;
                // Size in the FX canvas' units, independent of the HUD canvas scaler/camera.
                Vector2 lower = ToRootPoint(targetHeart, targetHeart.rect.min);
                Vector2 upper = ToRootPoint(targetHeart, targetHeart.rect.max);
                float targetWidth = Mathf.Abs(upper.x - lower.x);
                float aspect = heartPrefab.rect.height / Mathf.Max(1f, heartPrefab.rect.width);
                heartSize = new Vector2(targetWidth, targetWidth * aspect);
                for (int i = 0; i < count; i++)
                    flights.Add(new Flight { Delay = SpawnDelay + i * spawnStagger });

                float duration = SpawnDelay + (count - 1) * spawnStagger + PopDuration +
                                 Mathf.Max(0.01f, flyDuration) + Mathf.Max(0.01f, punchDuration);
                ownsLivesForeground = true;
                livesHud.BringForward();
                flightTween = DOVirtual.Float(0f, duration, duration, UpdatePresentation)
                    .SetEase(Ease.Linear)
                    .SetUpdate(true)
                    .SetLink(gameObject, LinkBehaviour.KillOnDestroy)
                    .OnComplete(Complete);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                Complete();
            }
        }

        private bool CanPresent()
        {
            return isActiveAndEnabled && heartPrefab != null && sourceHeart != null &&
                   animationRoot != null && animationRoot.gameObject.activeInHierarchy &&
                   inputLock != null && inputLock.isActiveAndEnabled &&
                   livesHud != null && livesHud.isActiveAndEnabled &&
                   livesHud.LivesRewardTarget != null &&
                   livesHud.LivesRewardTarget.gameObject.activeInHierarchy;
        }

        private void UpdatePresentation(float elapsed)
        {
            try
            {
                if (!CanPresent() || targetHeart == null)
                {
                    Complete();
                    return;
                }

                sourceHeart.localScale = sourceScale * PunchFactor(elapsed - 0.05f, 0.20f, 0.12f);
                Vector2 target = ToRootPoint(targetHeart, targetHeart.rect.center);
                for (int i = 0; i < flights.Count; i++)
                {
                    Flight flight = flights[i];
                    if (flight.Arrived || elapsed < flight.Delay) continue;
                    if (!flight.Spawned)
                    {
                        flight.Start = ToRootPoint(sourceHeart, sourceHeart.rect.center);
                        flight.Heart = Instantiate(heartPrefab, animationRoot, false);
                        flight.Spawned = true;
                        flight.Heart.anchorMin = flight.Heart.anchorMax = animationRoot.pivot;
                        flight.Heart.pivot = new Vector2(0.5f, 0.5f);
                        flight.Heart.sizeDelta = heartSize;
                        // The supplied prefab has a sorting Canvas. Inherit the existing FX root order.
                        Canvas canvas = flight.Heart.GetComponent<Canvas>();
                        if (canvas != null) canvas.overrideSorting = false;
                        flight.Heart.gameObject.SetActive(true);
                        AudioManager.Instance?.PlaySfx(spawnSfx);
                    }

                    float age = elapsed - flight.Delay;
                    float t = Mathf.Clamp01((age - PopDuration) / Mathf.Max(0.01f, flyDuration));
                    Vector2 control = (flight.Start + target) * 0.5f + curveOffset;
                    Vector2 point = (1f - t) * (1f - t) * flight.Start +
                                    2f * (1f - t) * t * control + t * t * target;
                    if (flight.Heart != null)
                    {
                        flight.Heart.anchoredPosition3D = new Vector3(point.x, point.y, 0f);
                        float scale = age < PopDuration
                            ? Mathf.Lerp(0.8f, 1.1f, Mathf.Sin(age / PopDuration * Mathf.PI * 0.5f))
                            : Mathf.Lerp(1.1f, 0.95f, t);
                        flight.Heart.localScale = Vector3.one * scale;
                    }

                    if (t < 1f) continue;
                    flight.Arrived = true;
                    AudioManager.Instance?.PlaySfx(arriveSfx);
                    if (flight.Heart != null) Destroy(flight.Heart.gameObject);
                    lastImpactTime = flight.Delay + PopDuration + Mathf.Max(0.01f, flyDuration);
                    lastImpactStrength = punchStrength * (i == flights.Count - 1 ? 1f : 0.75f);
                    if (i == flights.Count - 1) livesHud.EndLivesRewardPresentation(this);
                }

                if (lastImpactTime >= 0f)
                    targetHeart.localScale = targetScale *
                        PunchFactor(elapsed - lastImpactTime, punchDuration, lastImpactStrength);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                Complete();
            }
        }

        private static float PunchFactor(float elapsed, float duration, float strength)
        {
            float t = elapsed / Mathf.Max(0.01f, duration);
            if (t <= 0f || t >= 1f) return 1f;
            if (t < 0.35f) return Mathf.Lerp(1f, 1f + strength, Mathf.Sin(t / 0.35f * Mathf.PI * 0.5f));
            if (t < 0.7f) return Mathf.Lerp(1f + strength, 0.95f, (t - 0.35f) / 0.35f);
            return Mathf.Lerp(0.95f, 1f, (t - 0.7f) / 0.3f);
        }

        private Vector2 ToRootPoint(RectTransform rect, Vector2 localPoint)
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(
                GetCanvasCamera(rect), rect.TransformPoint(localPoint));
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    animationRoot, screen, GetCanvasCamera(animationRoot), out Vector2 point))
                throw new InvalidOperationException("Heart reward could not project onto its FX canvas.");
            return point;
        }

        private static Camera GetCanvasCamera(RectTransform rect)
        {
            Canvas canvas = rect.GetComponentInParent<Canvas>(true);
            if (canvas == null) return null;
            Canvas root = canvas.rootCanvas;
            return root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        }

        private void Complete()
        {
            Action completion = onComplete;
            Cancel();
            completion?.Invoke();
        }

        public void Cancel()
        {
            onComplete = null;
            flightTween?.Kill(false);
            flightTween = null;
            foreach (Flight flight in flights)
                if (flight.Heart != null) Destroy(flight.Heart.gameObject);
            flights.Clear();
            if (hasScales)
            {
                if (sourceHeart != null) sourceHeart.localScale = sourceScale;
                if (targetHeart != null) targetHeart.localScale = targetScale;
            }
            hasScales = false;
            targetHeart = null;
            lastImpactTime = -1f;
            if (ownsLivesForeground)
            {
                ownsLivesForeground = false;
                if (livesHud != null) livesHud.SendBack();
            }
            if (ownsInputLock)
            {
                ownsInputLock = false;
                if (inputLock != null) inputLock.UnlockUIInput();
            }
            if (livesHud != null) livesHud.EndLivesRewardPresentation(this);
        }

        private void OnDisable() => Cancel();
        private void OnDestroy() => Cancel();
    }
}
