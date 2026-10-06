using System;
using UnityEngine;

namespace Gameplay.SourceBoxes
{
    [CreateAssetMenu(menuName = "Gameplay/Source Boxes/Marble Color Catalog")]
    public sealed class MarbleColorCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [SerializeField] private MarbleColorId colorId;
            [SerializeField] private Sprite sourceBoxSprite;
            [SerializeField] private Sprite lockedSourceBoxSprite;
            [SerializeField] private Sprite marbleSprite;
            [SerializeField] private Sprite targetBoxActiveSprite;
            [SerializeField] private Sprite targetBoxPassiveSprite;
            [SerializeField] private Color connectedBoxConnectionTint = Color.white;
            [SerializeField] private Color arrowStrokeTint = Color.clear;

            public MarbleColorId ColorId => colorId;
            public Sprite SourceBoxSprite => sourceBoxSprite;
            public Sprite LockedSourceBoxSprite => lockedSourceBoxSprite;
            public Sprite MarbleSprite => marbleSprite;
            public Sprite TargetBoxActiveSprite => targetBoxActiveSprite;
            public Sprite TargetBoxPassiveSprite => targetBoxPassiveSprite;
            public Color ConnectedBoxConnectionTint => connectedBoxConnectionTint;
            public bool HasConnectedBoxConnectionTint => connectedBoxConnectionTint.a > 0f;
            public Color ArrowStrokeTint => arrowStrokeTint;
            public bool HasArrowStrokeTint => arrowStrokeTint.a > 0f;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        public bool TryGetEntry(MarbleColorId colorId, out Entry entry)
        {
            entry = null;

            if (!IsGameplayColor(colorId))
            {
                Debug.LogError($"{nameof(MarbleColorCatalog)} '{name}' cannot resolve color '{colorId}'.", this);
                return false;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                Entry currentEntry = entries[i];
                if (currentEntry != null && currentEntry.ColorId == colorId)
                {
                    if (HasDuplicateEntry(colorId, i))
                    {
                        Debug.LogError($"{nameof(MarbleColorCatalog)} '{name}' has duplicate entries for color '{colorId}'.", this);
                        entry = null;
                        return false;
                    }

                    entry = currentEntry;
                    return true;
                }
            }

            Debug.LogError($"{nameof(MarbleColorCatalog)} '{name}' has no entry for color '{colorId}'.", this);
            return false;
        }

        public static bool IsGameplayColor(MarbleColorId colorId)
        {
            return colorId >= MarbleColorId.Blue && colorId <= MarbleColorId.Grey;
        }

        private bool HasDuplicateEntry(MarbleColorId colorId, int foundIndex)
        {
            for (int i = foundIndex + 1; i < entries.Length; i++)
            {
                Entry otherEntry = entries[i];
                if (otherEntry != null && otherEntry.ColorId == colorId)
                {
                    return true;
                }
            }

            return false;
        }

        public bool ValidateEntries()
        {
            bool isValid = true;

            for (int i = 0; i < entries.Length; i++)
            {
                Entry entry = entries[i];
                if (entry == null)
                {
                    Debug.LogError($"{nameof(MarbleColorCatalog)} '{name}' has a null entry at index {i}.", this);
                    isValid = false;
                    continue;
                }

                if (!IsGameplayColor(entry.ColorId))
                {
                    Debug.LogError($"{nameof(MarbleColorCatalog)} '{name}' has an invalid color '{entry.ColorId}' at index {i}.", this);
                    isValid = false;
                }

                for (int j = i + 1; j < entries.Length; j++)
                {
                    Entry otherEntry = entries[j];
                    if (otherEntry != null && IsGameplayColor(entry.ColorId) && entry.ColorId == otherEntry.ColorId)
                    {
                        Debug.LogError($"{nameof(MarbleColorCatalog)} '{name}' has duplicate entries for color '{entry.ColorId}' at indexes {i} and {j}.", this);
                        isValid = false;
                    }
                }
            }

            return isValid;
        }
    }
}
