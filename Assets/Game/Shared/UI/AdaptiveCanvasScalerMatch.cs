using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Updates CanvasScaler.matchWidthOrHeight according to the current screen
/// aspect ratio. Attach this component to the same Canvas GameObject that
/// contains the CanvasScaler component.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasScaler))]
public sealed class AdaptiveCanvasScalerMatch : MonoBehaviour
{
    [Header("Canvas Scaler Setup")]
    [Tooltip("Keeps the CanvasScaler configured as Scale With Screen Size / Match Width Or Height.")]
    [SerializeField] private bool enforceRequiredScalerModes = true;

    [Header("Aspect Ratio Mapping")]
    [Tooltip("Short side / long side ratio. Devices at or below this value use Phone Match.")]
    [SerializeField, Range(0.30f, 1f)]
    private float phoneAspectThreshold = 0.60f;

    [Tooltip("Short side / long side ratio. Devices at or above this value use Tablet Match.")]
    [SerializeField, Range(0.30f, 1f)]
    private float tabletAspectThreshold = 0.70f;

    [Tooltip("CanvasScaler Match value used for narrow phone screens.")]
    [SerializeField, Range(0f, 1f)]
    private float phoneMatch = 0f;

    [Tooltip("CanvasScaler Match value used for tablet-like screens.")]
    [SerializeField, Range(0f, 1f)]
    private float tabletMatch = 1f;

    [Tooltip("Smooths the transition for aspect ratios between the phone and tablet thresholds.")]
    [SerializeField]
    private bool useSmoothInterpolation = true;

    [Header("Editor Preview")]
    [Tooltip("Updates the Canvas Scaler while testing different screens in Device Simulator.")]
    [SerializeField]
    private bool previewInEditMode = true;

    private CanvasScaler _canvasScaler;

    private int _lastScreenWidth = -1;
    private int _lastScreenHeight = -1;

    private float _lastPhoneAspectThreshold = -1f;
    private float _lastTabletAspectThreshold = -1f;
    private float _lastPhoneMatch = -1f;
    private float _lastTabletMatch = -1f;

    private bool _lastUseSmoothInterpolation;
    private bool _lastEnforceRequiredScalerModes;

    private void OnEnable()
    {
        CacheCanvasScaler();

        Canvas.willRenderCanvases -= RefreshIfNeeded;
        Canvas.willRenderCanvases += RefreshIfNeeded;

        ApplyMatch();
    }

    private void OnDisable()
    {
        Canvas.willRenderCanvases -= RefreshIfNeeded;
    }

    private void Update()
    {
        RefreshIfNeeded();
    }

    private void OnRectTransformDimensionsChange()
    {
        RefreshIfNeeded();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        CacheCanvasScaler();
        ApplyMatch();
    }
#endif

    [ContextMenu("Apply Canvas Scaler Match Now")]
    public void ApplyMatch()
    {
        CacheCanvasScaler();

        if (_canvasScaler == null || Screen.width <= 0 || Screen.height <= 0)
        {
            return;
        }

        if (!Application.isPlaying && !previewInEditMode)
        {
            return;
        }

        if (enforceRequiredScalerModes)
        {
            _canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _canvasScaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        }

        float shortSide = Mathf.Min(Screen.width, Screen.height);
        float longSide = Mathf.Max(Screen.width, Screen.height);
        float normalizedAspect = shortSide / longSide;

        float interpolation = Mathf.InverseLerp(
            phoneAspectThreshold,
            tabletAspectThreshold,
            normalizedAspect
        );

        if (useSmoothInterpolation)
        {
            interpolation = Mathf.SmoothStep(0f, 1f, interpolation);
        }

        _canvasScaler.matchWidthOrHeight = Mathf.Lerp(
            phoneMatch,
            tabletMatch,
            interpolation
        );

        RememberCurrentValues();
    }

    private void RefreshIfNeeded()
    {
        if (NeedsRefresh())
        {
            ApplyMatch();
        }
    }

    private bool NeedsRefresh()
    {
        return Screen.width != _lastScreenWidth
            || Screen.height != _lastScreenHeight
            || !Mathf.Approximately(
                phoneAspectThreshold,
                _lastPhoneAspectThreshold
            )
            || !Mathf.Approximately(
                tabletAspectThreshold,
                _lastTabletAspectThreshold
            )
            || !Mathf.Approximately(phoneMatch, _lastPhoneMatch)
            || !Mathf.Approximately(tabletMatch, _lastTabletMatch)
            || useSmoothInterpolation != _lastUseSmoothInterpolation
            || enforceRequiredScalerModes != _lastEnforceRequiredScalerModes;
    }

    private void RememberCurrentValues()
    {
        _lastScreenWidth = Screen.width;
        _lastScreenHeight = Screen.height;

        _lastPhoneAspectThreshold = phoneAspectThreshold;
        _lastTabletAspectThreshold = tabletAspectThreshold;
        _lastPhoneMatch = phoneMatch;
        _lastTabletMatch = tabletMatch;

        _lastUseSmoothInterpolation = useSmoothInterpolation;
        _lastEnforceRequiredScalerModes = enforceRequiredScalerModes;
    }

    private void CacheCanvasScaler()
    {
        if (_canvasScaler == null)
        {
            _canvasScaler = GetComponent<CanvasScaler>();
        }
    }
}