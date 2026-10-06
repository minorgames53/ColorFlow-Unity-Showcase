using DG.Tweening;
using UnityEngine;

namespace Game.Shared.UI
{
    [CreateAssetMenu(
        fileName = "UIConfig",
        menuName = "Game/Shared/UI Config")]
    public sealed class UIConfig : ScriptableObject
    {
        [Header("Connectivity")]
        [SerializeField, Min(0.1f)] private float internetReachabilityCheckIntervalSeconds = 1f;
        public float InternetReachabilityCheckIntervalSeconds => Mathf.Max(0.1f, internetReachabilityCheckIntervalSeconds);

        [Header("Panel Scale")]
        [SerializeField, Range(0.5f, 1f)] private float panelInitialScale = 0.95f;
        [SerializeField, Range(1f, 1.5f)] private float panelOvershootScale = 1.10f;

        [Header("Panel Duration")]
        [SerializeField, Min(0f)] private float panelGrowDuration = 0.14f;
        [SerializeField, Min(0f)] private float panelSettleDuration = 0.10f;
        [SerializeField, Min(0f)] private float panelCloseDuration = 0.10f;

        [Header("Panel Ease")]
        [SerializeField] private Ease panelGrowEase = Ease.OutQuad;
        [SerializeField] private Ease panelSettleEase = Ease.OutBack;
        [SerializeField] private Ease panelCloseEase = Ease.InQuad;

        [Header("Button Scale")]
        [SerializeField, Range(0.5f, 1f)] private float buttonPressedScale = 0.95f;
        [SerializeField, Range(1f, 1.5f)] private float buttonReleaseOvershootScale = 1.05f;

        [Header("Button Duration")]
        [SerializeField, Min(0f)] private float buttonPressDuration = 0.08f;
        [SerializeField, Min(0f)] private float buttonOvershootDuration = 0.10f;
        [SerializeField, Min(0f)] private float buttonReturnDuration = 0.10f;

        [Header("Button Ease")]
        [SerializeField] private Ease buttonPressEase = Ease.OutQuad;
        [SerializeField] private Ease buttonOvershootEase = Ease.OutQuad;
        [SerializeField] private Ease buttonReturnEase = Ease.OutBack;

        public float PanelInitialScale => panelInitialScale;
        public float PanelOvershootScale => panelOvershootScale;
        public float PanelGrowDuration => panelGrowDuration;
        public float PanelSettleDuration => panelSettleDuration;
        public float PanelCloseDuration => panelCloseDuration;
        public Ease PanelGrowEase => panelGrowEase;
        public Ease PanelSettleEase => panelSettleEase;
        public Ease PanelCloseEase => panelCloseEase;

        public float ButtonPressedScale => buttonPressedScale;
        public float ButtonReleaseOvershootScale => buttonReleaseOvershootScale;
        public float ButtonPressDuration => buttonPressDuration;
        public float ButtonOvershootDuration => buttonOvershootDuration;
        public float ButtonReturnDuration => buttonReturnDuration;
        public Ease ButtonPressEase => buttonPressEase;
        public Ease ButtonOvershootEase => buttonOvershootEase;
        public Ease ButtonReturnEase => buttonReturnEase;
    }
}
