using Gameplay.SourceBoxes;
using Gameplay.Spawners;
using Gameplay.BoardFeatures.GiftBoxes;
using Gameplay.BoardFeatures.ConnectedBoxes;
using Gameplay.BoardFeatures.Crates;
using Gameplay.BoardFeatures.Panels;
using Gameplay.BoardFeatures.ArrowBoxes;
using Gameplay.BoardFeatures.KeyLocks;
using Gameplay.BoardFeatures.MultiplierGates;
using UnityEngine;

namespace Gameplay.BoardFeatures
{
    [CreateAssetMenu(menuName = "Gameplay/Board Features/Board Feature Catalog")]
    public sealed class BoardFeatureCatalog : ScriptableObject
    {
        [Header("Prefabs")]
        [SerializeField] private SourceBox sourceBoxPrefab;
        [SerializeField] private SourceBoxSpawner spawnerPrefab;
        [SerializeField] private GiftBoxController giftBoxPrefab;
        [SerializeField] private CrateView cratePrefab;
        [SerializeField] private PanelView panelPrefab;
        [SerializeField] private ArrowBoxView arrowBoxPrefab;
        [SerializeField] private KeyView keyPrefab;
        [SerializeField] private LockView lockPrefab;
        [SerializeField] private MultiplierGateView multiplierGatePrefab;
        [SerializeField] private ConnectionController connectionPrefab;
        [SerializeField] private GameObject connectedBoxClosedStrokePrefab;
        [SerializeField] private GameObject connectedBoxOpenStrokePrefab;
        [SerializeField] private Marble marblePrefab;

        [Header("Mystery Visuals")]
        [SerializeField] private Sprite mysterySourceBoxSprite;

        [Header("Spawner Visuals")]
        [SerializeField] private Sprite downSprite;
        [SerializeField] private Sprite horizontalSprite;

        public SourceBox SourceBoxPrefab => sourceBoxPrefab;
        public SourceBoxSpawner SpawnerPrefab => spawnerPrefab;
        public GiftBoxController GiftBoxPrefab => giftBoxPrefab;
        public CrateView CratePrefab => cratePrefab;
        public PanelView PanelPrefab => panelPrefab;
        public ArrowBoxView ArrowBoxPrefab => arrowBoxPrefab;
        public KeyView KeyPrefab => keyPrefab;
        public LockView LockPrefab => lockPrefab;
        public MultiplierGateView MultiplierGatePrefab => multiplierGatePrefab;
        public ConnectionController ConnectionPrefab => connectionPrefab;
        public GameObject ConnectedBoxClosedStrokePrefab => connectedBoxClosedStrokePrefab;
        public GameObject ConnectedBoxOpenStrokePrefab => connectedBoxOpenStrokePrefab;
        public Marble MarblePrefab => marblePrefab;
        public Sprite MysterySourceBoxSprite => mysterySourceBoxSprite;
        public Sprite DownSprite => downSprite;
        public Sprite HorizontalSprite => horizontalSprite;

        public bool Validate(Object context)
        {
            bool isValid = true;

            if (sourceBoxPrefab == null)
            {
                Debug.LogError($"{nameof(BoardFeatureCatalog)} '{name}' is missing SourceBox prefab.", context);
                isValid = false;
            }

            if (spawnerPrefab == null)
            {
                Debug.LogError($"{nameof(BoardFeatureCatalog)} '{name}' is missing Spawner prefab.", context);
                isValid = false;
            }

            if (marblePrefab == null)
            {
                Debug.LogError($"{nameof(BoardFeatureCatalog)} '{name}' is missing Marble prefab.", context);
                isValid = false;
            }

            if (downSprite == null)
            {
                Debug.LogError($"{nameof(BoardFeatureCatalog)} '{name}' is missing Spawner down sprite.", context);
                isValid = false;
            }

            if (horizontalSprite == null)
            {
                Debug.LogError($"{nameof(BoardFeatureCatalog)} '{name}' is missing Spawner horizontal sprite.", context);
                isValid = false;
            }

            return isValid;
        }
    }
}
