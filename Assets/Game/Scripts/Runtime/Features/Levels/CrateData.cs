using System;
using UnityEngine;

namespace Gameplay.Levels
{
    [Serializable]
    public sealed class CrateData
    {
        [SerializeField] private int row;
        [SerializeField] private int col;

        public int Row => row;
        public int Col => col;
    }
}
