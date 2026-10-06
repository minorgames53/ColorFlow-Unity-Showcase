using System;
using Gameplay.SourceBoxes;
using UnityEngine;

namespace Gameplay.Levels
{
    [Serializable]
    public sealed class TargetBoxData
    {
        [SerializeField] private MarbleColorId colorId = MarbleColorId.Blue;
        [SerializeField] private bool isMystery;
        [SerializeField] private int connectedTargetGroupId;
        [SerializeField] private bool isLocked;

        public MarbleColorId ColorId => colorId;
        public bool IsMystery => isMystery;
        public int ConnectedTargetGroupId => connectedTargetGroupId;
        public bool HasConnectedTargetGroup => connectedTargetGroupId > 0;
        public bool IsLocked => isLocked;

        public TargetBoxData()
        {
        }

        public TargetBoxData(MarbleColorId colorId, bool isMystery, int connectedTargetGroupId = 0, bool isLocked = false)
        {
            this.colorId = colorId;
            this.isMystery = isMystery;
            this.connectedTargetGroupId = connectedTargetGroupId;
            this.isLocked = isLocked;
        }
    }
}
