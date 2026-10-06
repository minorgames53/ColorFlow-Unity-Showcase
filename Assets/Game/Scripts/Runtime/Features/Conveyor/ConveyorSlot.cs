using Gameplay.SourceBoxes;
using UnityEngine;

namespace Gameplay.Conveyor
{
    public enum ConveyorSlotState
    {
        Empty,
        ReservedForEntry,
        Occupied,
        ReservedForCatchUp,
        BlockedByOutgoing
    }

    public sealed class ConveyorSlot : MonoBehaviour
    {
        [SerializeField] private int slotIndex;
        [SerializeField] private Transform marbleAnchor;

        public int SlotIndex => slotIndex;
        public Transform MarbleAnchor => marbleAnchor;
        public Marble CurrentMarble { get; private set; }
        public Marble IncomingMarble { get; private set; }
        public Marble OutgoingMarble { get; private set; }
        public ConveyorSlotState State { get; private set; } = ConveyorSlotState.Empty;
        public bool IsEmpty => State == ConveyorSlotState.Empty;
        public bool IsReserved => State == ConveyorSlotState.ReservedForEntry || State == ConveyorSlotState.ReservedForCatchUp;
        public bool IsOccupied => State == ConveyorSlotState.Occupied;
        public bool IsBlocked => State == ConveyorSlotState.BlockedByOutgoing;
        public bool CanReserveForCatchUp => IsEmpty || (IsBlocked && IncomingMarble == null);

        public void Initialize(int newSlotIndex, Transform newMarbleAnchor)
        {
            slotIndex = newSlotIndex;
            marbleAnchor = newMarbleAnchor;
            CurrentMarble = null;
            IncomingMarble = null;
            OutgoingMarble = null;
            State = ConveyorSlotState.Empty;
        }

        public bool TryReserveForEntry()
        {
            if (!IsEmpty)
            {
                return false;
            }

            State = ConveyorSlotState.ReservedForEntry;
            return true;
        }

        public bool TryReserveForCatchUp(Marble incomingMarble)
        {
            if (!CanReserveForCatchUp || incomingMarble == null)
            {
                return false;
            }

            IncomingMarble = incomingMarble;
            if (IsEmpty)
            {
                State = ConveyorSlotState.ReservedForCatchUp;
            }

            return true;
        }

        public bool TryBeginOutgoing(out Marble outgoingMarble)
        {
            outgoingMarble = null;
            if (!IsOccupied || CurrentMarble == null)
            {
                return false;
            }

            outgoingMarble = CurrentMarble;
            OutgoingMarble = CurrentMarble;
            State = ConveyorSlotState.BlockedByOutgoing;
            return true;
        }

        public void AssignMarble(Marble marble)
        {
            if (marble == null)
            {
                CancelReservation();
                return;
            }

            CurrentMarble = marble;
            IncomingMarble = null;
            OutgoingMarble = null;
            State = ConveyorSlotState.Occupied;
        }

        public void CompleteOutgoing()
        {
            if (!IsBlocked)
            {
                return;
            }

            CurrentMarble = null;
            OutgoingMarble = null;
            State = IncomingMarble != null ? ConveyorSlotState.ReservedForCatchUp : ConveyorSlotState.Empty;
        }

        public bool HasCatchUpReservation(Marble marble)
        {
            return marble != null &&
                   IncomingMarble == marble &&
                   (State == ConveyorSlotState.ReservedForCatchUp || State == ConveyorSlotState.BlockedByOutgoing);
        }

        public Marble ReleaseMarble()
        {
            Marble marble = CurrentMarble;
            CurrentMarble = null;
            IncomingMarble = null;
            OutgoingMarble = null;
            State = ConveyorSlotState.Empty;
            return marble;
        }

        public void CancelReservation()
        {
            if (IsBlocked)
            {
                IncomingMarble = null;
                return;
            }

            if (IsOccupied)
            {
                return;
            }

            CurrentMarble = null;
            IncomingMarble = null;
            OutgoingMarble = null;
            State = ConveyorSlotState.Empty;
        }

        public void Clear()
        {
            CurrentMarble = null;
            IncomingMarble = null;
            OutgoingMarble = null;
            State = ConveyorSlotState.Empty;
        }
    }
}
