using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Keeps a UI RectTransform inside the device safe area vertically.
/// Attach this to a full-stretch SafeAreaRoot under the Canvas.
/// Place visible UI elements such as Logo and LoadingText under SafeAreaRoot.
/// Keep full-screen backgrounds outside SafeAreaRoot.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class TopBottomSafeArea : MonoBehaviour
{
    [Header("Device Safe Area")]
    [Tooltip("Uses Screen.safeArea reported by the device or Device Simulator.")]
    [SerializeField] private bool useDeviceSafeArea = true;

    [Tooltip("Apply the device safe-area inset at the top of the screen.")]
    [SerializeField] private bool protectTop = true;

    [Tooltip("Apply the device safe-area inset at the bottom of the screen.")]
    [SerializeField] private bool protectBottom = true;

    [Header("Extra Padding (Canvas Units)")]
    [Tooltip("Additional spacing below the top safe-area boundary. This uses Canvas UI units, so it works naturally with Canvas Scaler.")]
    [SerializeField, Min(0f)] private float extraTopPadding = 0f;

    [Tooltip("Additional spacing above the bottom safe-area boundary. This uses Canvas UI units, so it works naturally with Canvas Scaler.")]
    [SerializeField, Min(0f)] private float extraBottomPadding = 0f;

    private RectTransform _rectTransform;
    private Rect _lastSafeArea = new Rect(-1f, -1f, -1f, -1f);
    private int _lastScreenWidth = -1;
    private int _lastScreenHeight = -1;
    private bool _lastUseDeviceSafeArea;
    private bool _lastProtectTop;
    private bool _lastProtectBottom;
    private float _lastExtraTopPadding = -1f;
    private float _lastExtraBottomPadding = -1f;

    private void OnEnable()
    {
        CacheRectTransform();
        ApplySafeArea();
    }

    private void Update()
    {
        if (NeedsRefresh())
        {
            ApplySafeArea();
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        CacheRectTransform();
        ApplySafeArea();
    }
#endif

    [Button]
    public void ApplySafeArea()
    {
        CacheRectTransform();

        if (_rectTransform == null || Screen.width <= 0 || Screen.height <= 0)
        {
            return;
        }

        Rect safeArea = GetSafeArea();

        float minY = protectBottom ? safeArea.yMin / Screen.height : 0f;
        float maxY = protectTop ? safeArea.yMax / Screen.height : 1f;

        minY = Mathf.Clamp01(minY);
        maxY = Mathf.Clamp01(maxY);

        if (minY > maxY)
        {
            minY = maxY;
        }

        // Only adjust the vertical safe area. Keep the full available width.
        _rectTransform.anchorMin = new Vector2(0f, minY);
        _rectTransform.anchorMax = new Vector2(1f, maxY);

        // Extra padding is applied in Canvas UI units after Canvas Scaler.
        _rectTransform.offsetMin = new Vector2(0f, extraBottomPadding);
        _rectTransform.offsetMax = new Vector2(0f, -extraTopPadding);

        RememberCurrentValues(safeArea);
    }

    private Rect GetSafeArea()
    {
        return useDeviceSafeArea
            ? Screen.safeArea
            : new Rect(0f, 0f, Screen.width, Screen.height);
    }

    private bool NeedsRefresh()
    {
        Rect currentSafeArea = GetSafeArea();

        return Screen.width != _lastScreenWidth
            || Screen.height != _lastScreenHeight
            || currentSafeArea != _lastSafeArea
            || useDeviceSafeArea != _lastUseDeviceSafeArea
            || protectTop != _lastProtectTop
            || protectBottom != _lastProtectBottom
            || !Mathf.Approximately(extraTopPadding, _lastExtraTopPadding)
            || !Mathf.Approximately(extraBottomPadding, _lastExtraBottomPadding);
    }

    private void RememberCurrentValues(Rect safeArea)
    {
        _lastScreenWidth = Screen.width;
        _lastScreenHeight = Screen.height;
        _lastSafeArea = safeArea;
        _lastUseDeviceSafeArea = useDeviceSafeArea;
        _lastProtectTop = protectTop;
        _lastProtectBottom = protectBottom;
        _lastExtraTopPadding = extraTopPadding;
        _lastExtraBottomPadding = extraBottomPadding;
    }

    private void CacheRectTransform()
    {
        if (_rectTransform == null)
        {
            _rectTransform = GetComponent<RectTransform>();
        }
    }
}
