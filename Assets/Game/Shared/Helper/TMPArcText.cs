using TMPro;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class TMPArcText : MonoBehaviour
{
    [SerializeField, Range(-100f, 100f)]
    private float arcHeight = 16f;

    [SerializeField]
    private bool rotateCharacters = true;

    private TMP_Text tmpText;

    private void OnEnable()
    {
        tmpText = GetComponent<TMP_Text>();

        if (tmpText == null)
        {
            Debug.LogError("TMPArcText aynı nesnede bir TMP_Text bileşeni gerektirir.", this);
            enabled = false;
            return;
        }

        tmpText.OnPreRenderText += ApplyArc;
        Refresh();
    }

    private void OnDisable()
    {
        if (tmpText == null)
            return;

        tmpText.OnPreRenderText -= ApplyArc;

        // Script kapatıldığında normal, düz mesh'i yeniden oluştur.
        tmpText.havePropertiesChanged = true;
        tmpText.SetVerticesDirty();
        tmpText.ForceMeshUpdate();
    }

    private void OnValidate()
    {
        if (!isActiveAndEnabled)
            return;

        if (tmpText == null)
            tmpText = GetComponent<TMP_Text>();

        Refresh();
    }

    public void Refresh()
    {
        if (tmpText == null)
            return;

        tmpText.havePropertiesChanged = true;
        tmpText.SetVerticesDirty();
        tmpText.ForceMeshUpdate();
    }

    private void ApplyArc(TMP_TextInfo textInfo)
    {
        // Arabic joins span adjacent glyphs. Rotating each glyph independently breaks
        // those connections, so keep the existing TMP RTL line intact.
        if (tmpText != null && tmpText.isRightToLeftText)
            return;

        if (textInfo.characterCount == 0)
            return;

        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;

        // Görünen ilk ve son harfin merkezini bul.
        for (int i = 0; i < textInfo.characterCount; i++)
        {
            TMP_CharacterInfo character = textInfo.characterInfo[i];

            if (!character.isVisible)
                continue;

            float centerX = (character.bottomLeft.x + character.topRight.x) * 0.5f;

            minX = Mathf.Min(minX, centerX);
            maxX = Mathf.Max(maxX, centerX);
        }

        if (minX == float.PositiveInfinity)
            return;

        float width = maxX - minX;

        if (width < 0.001f)
            return;

        for (int i = 0; i < textInfo.characterCount; i++)
        {
            TMP_CharacterInfo character = textInfo.characterInfo[i];

            if (!character.isVisible)
                continue;

            int materialIndex = character.materialReferenceIndex;
            int vertexIndex = character.vertexIndex;

            Vector3[] vertices = textInfo.meshInfo[materialIndex].vertices;

            float centerX = (character.bottomLeft.x + character.topRight.x) * 0.5f;

            // -1: sol uç, 0: orta, +1: sağ uç
            float normalizedX = Mathf.InverseLerp(minX, maxX, centerX) * 2f - 1f;

            // Ortası yukarıda olan yumuşak parabol.
            float yOffset = arcHeight * (1f - normalizedX * normalizedX);

            // Harfi eğrinin teğetine göre hafifçe döndür.
            float slope = (-4f * arcHeight * normalizedX) / width;
            float angle = rotateCharacters
                ? Mathf.Atan(slope) * Mathf.Rad2Deg
                : 0f;

            Vector3 pivot = new Vector3(centerX, character.baseLine, 0f);
            Vector3 offset = new Vector3(0f, yOffset, 0f);
            Quaternion rotation = Quaternion.Euler(0f, 0f, angle);

            for (int vertex = 0; vertex < 4; vertex++)
            {
                int index = vertexIndex + vertex;

                vertices[index] =
                    rotation * (vertices[index] - pivot)
                    + pivot
                    + offset;
            }
        }
    }
}
