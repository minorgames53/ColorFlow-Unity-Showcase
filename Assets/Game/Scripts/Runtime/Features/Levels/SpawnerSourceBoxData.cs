using System;
using Gameplay.SourceBoxes;
using UnityEngine;

namespace Gameplay.Levels
{
    [Serializable]
    public sealed class SpawnerSourceBoxData
    {
        [SerializeField] private MarbleColorId colorId = MarbleColorId.Blue;

        public MarbleColorId ColorId => colorId;
    }
}
