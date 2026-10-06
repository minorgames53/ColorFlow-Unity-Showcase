using Gameplay.SourceBoxes;
using UnityEngine;

namespace Gameplay.BoardFeatures.MultiplierGates
{
    [RequireComponent(typeof(Collider2D))]
    public sealed class MultiplierGateTrigger : MonoBehaviour
    {
        private MultiplierGateBoardController controller;
        private MultiplierGateView gateView;
        private int gateRuntimeId = -1;

        public void Initialize(MultiplierGateBoardController owner, int runtimeId, MultiplierGateView ownerView)
        {
            controller = owner;
            gateRuntimeId = runtimeId;
            gateView = ownerView;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            Marble marble = other != null ? other.GetComponentInParent<Marble>() : null;
            if (marble != null && controller != null && controller.NotifyMarbleCrossed(gateRuntimeId, marble))
            {
                gateView?.PlayMarblePulse();
            }
        }

        private void OnDisable()
        {
            controller = null;
            gateView = null;
            gateRuntimeId = -1;
        }
    }
}
