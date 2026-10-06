using UnityEngine;

namespace Gameplay.SourceBoxes
{
    public sealed class SourceBoxPlaceholder : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Sprite availableSprite;
        [SerializeField] private Transform sourceBoxAnchor;

        private int cellIndex = -1;
        private Color baseColor = Color.white;
        private bool hasBaseColor;

        public int CellIndex => cellIndex;
        public Transform SourceBoxAnchor => sourceBoxAnchor;

        private void Reset()
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            sourceBoxAnchor = transform.Find("SourceBoxAnchor");
        }

        public void Initialize(int newCellIndex)
        {
            cellIndex = newCellIndex;
            SetSpriteVisible(true);
            CacheBaseColor();
            if (spriteRenderer != null)
            {
                spriteRenderer.sprite = availableSprite;
            }
        }

        public void SetSpriteVisible(bool visible)
        {
            if (spriteRenderer != null)
            {
                spriteRenderer.enabled = visible;
            }
        }

        public void SetEmptyTintValue(float value)
        {
            CacheBaseColor();
            SetSpriteColorWithValue(value);
        }

        public void SetBlockedTintValue(float value)
        {
            CacheBaseColor();
            SetSpriteColorWithValue(value);
        }

        public void ResetTint()
        {
            CacheBaseColor();
            if (spriteRenderer != null)
            {
                spriteRenderer.color = baseColor;
            }
        }

        private void CacheBaseColor()
        {
            if (hasBaseColor || spriteRenderer == null)
            {
                return;
            }

            baseColor = spriteRenderer.color;
            hasBaseColor = true;
        }

        private void SetSpriteColorWithValue(float value)
        {
            if (spriteRenderer == null)
            {
                return;
            }

            Color.RGBToHSV(baseColor, out float hue, out float saturation, out _);
            Color color = Color.HSVToRGB(hue, saturation, Mathf.Clamp01(value));
            color.a = baseColor.a;
            spriteRenderer.color = color;
        }
    }
}
