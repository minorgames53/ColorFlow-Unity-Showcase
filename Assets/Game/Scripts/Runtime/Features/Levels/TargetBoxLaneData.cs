using System;
using System.Collections.Generic;
using Gameplay.SourceBoxes;
using UnityEngine;

namespace Gameplay.Levels
{
    [Serializable]
    public sealed class TargetBoxLaneData
    {
        [SerializeField] private List<TargetBoxData> boxes = new List<TargetBoxData>();
        [SerializeField] private List<MarbleColorId> boxColors = new List<MarbleColorId>();

        public IReadOnlyList<TargetBoxData> Boxes
        {
            get
            {
                EnsureData();
                return boxes;
            }
        }

        public void EnsureData()
        {
            if (boxes == null)
            {
                boxes = new List<TargetBoxData>();
            }

            if (boxColors == null || boxColors.Count == 0 || boxes.Count > 0)
            {
                return;
            }

            for (int i = 0; i < boxColors.Count; i++)
            {
                boxes.Add(new TargetBoxData(boxColors[i], false));
            }

            boxColors.Clear();
        }
    }
}
