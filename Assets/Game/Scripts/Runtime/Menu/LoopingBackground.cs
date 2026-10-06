using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RawImage))]
public class LoopingBackground : MonoBehaviour
{
    [SerializeField] private float scrollSpeed = 0.02f;

    private RawImage rawImage;
    private float offsetX;

    private void Awake()
    {
        rawImage = GetComponent<RawImage>();
    }

    private void Update()
    {
        // Negatif = görsel sağa doğru akar
        offsetX = Mathf.Repeat(
            offsetX - scrollSpeed * Time.unscaledDeltaTime,
            1f
        );

        Rect uv = rawImage.uvRect;
        uv.x = offsetX;
        rawImage.uvRect = uv;
    }
}