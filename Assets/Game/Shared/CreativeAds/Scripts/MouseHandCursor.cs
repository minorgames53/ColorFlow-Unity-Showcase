using System.Collections;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public sealed class MouseHandCursor : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RectTransform follower;
    [SerializeField] private RectTransform visual;
    [SerializeField] private ParticleSystem ringFx;

    [Header("Cursor Settings")]
    [SerializeField] private Vector2 screenOffset = Vector2.zero;
    [SerializeField] private bool hideSystemCursor = true;

    [Header("Click Animation")]
    [SerializeField, Range(0.5f, 1f)]
    private float pressedScale = 0.88f;

    [SerializeField]
    private float pressedRotation = -8f;

    [SerializeField, Min(0.01f)]
    private float pressDuration = 0.06f;

    [SerializeField, Min(0.01f)]
    private float releaseDuration = 0.10f;

    private Vector3 idleScale;
    private Quaternion idleRotation;
    private Coroutine clickRoutine;

    private void Reset()
    {
        follower = transform as RectTransform;

        if (transform.childCount > 0)
        {
            visual = transform.GetChild(0) as RectTransform;
        }
    }

    private void Awake()
    {
        if (follower == null)
        {
            follower = transform as RectTransform;
        }

        if (visual == null && transform.childCount > 0)
        {
            visual = transform.GetChild(0) as RectTransform;
        }

        if (follower == null || visual == null)
        {
            Debug.LogError(
                "MouseHandCursor: Follower ve Visual alanlarını Inspector'dan ata.",
                this
            );

            enabled = false;
            return;
        }

        idleScale = visual.localScale;
        idleRotation = visual.localRotation;
    }

    private void OnEnable()
    {
        if (hideSystemCursor)
        {
            Cursor.visible = false;
        }
    }

    private void OnDisable()
    {
        if (hideSystemCursor)
        {
            Cursor.visible = true;
        }

        if (visual != null)
        {
            visual.localScale = idleScale;
            visual.localRotation = idleRotation;
        }
    }

    private void Update()
    {
        if (!TryReadMouse(out Vector2 mousePosition, out bool leftClick))
        {
            return;
        }

        // Overlay Canvas kullanıldığı için ekran koordinatını
        // doğrudan RectTransform pozisyonuna verebiliriz.
        follower.position = mousePosition + screenOffset;

        if (leftClick)
        {
            if (clickRoutine != null)
            {
                StopCoroutine(clickRoutine);
            }

            clickRoutine = StartCoroutine(PlayClickAnimation());
        }
    }

    private static bool TryReadMouse(
        out Vector2 position,
        out bool leftClick)
    {
#if ENABLE_INPUT_SYSTEM

        if (Mouse.current == null)
        {
            position = Vector2.zero;
            leftClick = false;
            return false;
        }

        position = Mouse.current.position.ReadValue();
        leftClick = Mouse.current.leftButton.wasPressedThisFrame;

        return true;

#else

        position = Input.mousePosition;
        leftClick = Input.GetMouseButtonDown(0);

        return true;

#endif
    }

    private IEnumerator PlayClickAnimation()
    {
        Vector3 targetScale = idleScale * pressedScale;

        Quaternion targetRotation =
            idleRotation *
            Quaternion.Euler(0f, 0f, pressedRotation);

        ringFx.Stop();
        ringFx.Play();
        yield return AnimateVisual(
            targetScale,
            targetRotation,
            pressDuration
        );

        yield return AnimateVisual(
            idleScale,
            idleRotation,
            releaseDuration
        );

        clickRoutine = null;
    }

    private IEnumerator AnimateVisual(
        Vector3 targetScale,
        Quaternion targetRotation,
        float duration)
    {
        Vector3 startScale = visual.localScale;
        Quaternion startRotation = visual.localRotation;

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;

            float t = Mathf.Clamp01(elapsed / duration);

            // Ease-out
            t = 1f - (1f - t) * (1f - t);

            visual.localScale = Vector3.LerpUnclamped(
                startScale,
                targetScale,
                t
            );

            visual.localRotation = Quaternion.SlerpUnclamped(
                startRotation,
                targetRotation,
                t
            );

            yield return null;
        }

        visual.localScale = targetScale;
        visual.localRotation = targetRotation;
    }
}