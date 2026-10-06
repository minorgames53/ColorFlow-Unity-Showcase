using DG.Tweening;
using Game.Shared.Audio;
using Game.Shared.Haptics;
using Game.Shared.UI.Panels;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Shared.UI
{
    public class TweenButton : Button
    {
        [SerializeField] private UIConfig uiConfig;

        private Vector3 defaultScale;
        private Tween scaleTween;
        private bool isPointerDown;

        protected override void Awake()
        {
            base.Awake();
            transition = Transition.None;
            defaultScale = transform.localScale;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            ResetImmediately();
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);

            if (!IsActive() || !IsInteractable())
            {
                return;
            }

            isPointerDown = true;
            PlayPressedAnimation();
        }

        public override void OnPointerUp(PointerEventData eventData)
        {
            base.OnPointerUp(eventData);

            if (!isPointerDown)
            {
                return;
            }

            AudioManager.Instance?.PlaySfx(AudioKey.ButtonClick);
            HapticManager.Instance?.Play(HapticType.Selection);

            isPointerDown = false;
            PlayReleasedAnimation();
        }

        private void OnApplicationPause(bool isPaused)
        {
#if UNITY_ANDROID || UNITY_IOS
            if (isPaused)
            {
                ResetImmediately();
            }
#endif
        }

        protected override void OnDisable()
        {
            ResetImmediately();
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            KillCurrentTween();
            base.OnDestroy();
        }

        private void PlayPressedAnimation()
        {
            KillCurrentTween();

            scaleTween = transform
                .DOScale(defaultScale * PressedScale, PressDuration)
                .SetEase(PressEase)
                .SetUpdate(true);
        }

        private void PlayReleasedAnimation()
        {
            KillCurrentTween();

            scaleTween = DOTween.Sequence()
                .Append(
                    transform
                        .DOScale(defaultScale * ReleaseOvershootScale, OvershootDuration)
                        .SetEase(OvershootEase)
                )
                .Append(
                    transform
                        .DOScale(defaultScale, ReturnDuration)
                        .SetEase(ReturnEase)
                )
                .SetUpdate(true);
        }

        private void ResetImmediately()
        {
            isPointerDown = false;
            KillCurrentTween();
            transform.localScale = defaultScale;
        }

        private void KillCurrentTween()
        {
            if (scaleTween == null)
            {
                return;
            }

            scaleTween.Kill();
            scaleTween = null;
        }

        private UIConfig Config => uiConfig != null ? uiConfig : PanelManager.CurrentConfig;

        private float PressedScale => Config != null ? Config.ButtonPressedScale : 0.95f;
        private float ReleaseOvershootScale => Config != null ? Config.ButtonReleaseOvershootScale : 1.05f;
        private float PressDuration => Config != null ? Config.ButtonPressDuration : 0.08f;
        private float OvershootDuration => Config != null ? Config.ButtonOvershootDuration : 0.10f;
        private float ReturnDuration => Config != null ? Config.ButtonReturnDuration : 0.10f;
        private Ease PressEase => Config != null ? Config.ButtonPressEase : Ease.OutQuad;
        private Ease OvershootEase => Config != null ? Config.ButtonOvershootEase : Ease.OutQuad;
        private Ease ReturnEase => Config != null ? Config.ButtonReturnEase : Ease.OutBack;
    }
}
