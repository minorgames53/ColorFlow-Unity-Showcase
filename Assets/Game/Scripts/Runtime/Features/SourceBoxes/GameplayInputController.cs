using System.Collections.Generic;
using Gameplay.Boosters;
using Gameplay.Levels;
using Gameplay.UI.World;
using Gameplay.Tutorial;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Gameplay.SourceBoxes
{
    public sealed class GameplayInputController : MonoBehaviour
    {
        [SerializeField] private Camera gameplayCamera;
        [SerializeField] private LayerMask sourceBoxLayerMask = Physics2D.DefaultRaycastLayers;
        [SerializeField] private SourceBoxBoardFullMessageView boardFullMessageView;
        [SerializeField] private SourceBoxBoardController sourceBoxBoardController;
        [SerializeField] private BoosterController boosterController;
        [SerializeField] private HandBoosterController handBoosterController;
        [SerializeField] private LevelSessionController levelSessionController;
        [SerializeField] private BoosterUnlockTutorialController boosterUnlockTutorialController;
        [SerializeField] private bool inputEnabled = true;

        private readonly List<RaycastResult> uiRaycastResults = new List<RaycastResult>();
        private InputAction pointerPositionAction;
        private InputAction pointerPressAction;
        private PointerEventData pointerEventData;
        private readonly HashSet<object> inputBlockerOwners = new HashSet<object>();

        private void Awake()
        {
            CreateActionsIfNeeded();
            CacheMissingReferences();
        }

        private void OnEnable()
        {
            CreateActionsIfNeeded();
            CacheMissingReferences();
            pointerPressAction.started -= OnPointerPressStarted;
            pointerPressAction.started += OnPointerPressStarted;
            pointerPositionAction.Enable();
            pointerPressAction.Enable();
        }

        private void OnDisable()
        {
            if (pointerPressAction != null)
            {
                pointerPressAction.started -= OnPointerPressStarted;
                pointerPressAction.Disable();
            }

            pointerPositionAction?.Disable();
            boardFullMessageView?.ResetImmediate();
        }

        private void OnDestroy()
        {
            pointerPressAction?.Dispose();
            pointerPositionAction?.Dispose();
        }

        public void SetInputEnabled(bool enabled)
        {
            inputEnabled = enabled;
        }

        public void SetInputBlocked(object owner, bool blocked)
        {
            if (owner == null) return;
            if (blocked) inputBlockerOwners.Add(owner);
            else inputBlockerOwners.Remove(owner);
        }

        private void OnPointerPressStarted(InputAction.CallbackContext context)
        {
            if (!inputEnabled || inputBlockerOwners.Count > 0)
            {
                return;
            }

            CacheMissingReferences();
            if (levelSessionController == null || !levelSessionController.IsPlaying)
            {
                return;
            }

            if (boosterUnlockTutorialController != null &&
                !boosterUnlockTutorialController.AllowsGeneralGameplayInput() &&
                boosterUnlockTutorialController.State != BoosterTutorialState.WaitForHandSourceBox)
            {
                return;
            }

            if (boosterController != null && boosterController.IsGameplayInputBlocked)
            {
                return;
            }

            bool handInputOverride = boosterController != null && boosterController.IsInputOverrideActive;
            if (handInputOverride && handBoosterController == null)
            {
                Debug.LogError($"{nameof(GameplayInputController)} on '{name}' cannot route Hand input because {nameof(HandBoosterController)} is missing.", this);
                return;
            }

            bool handTargeting = handInputOverride && handBoosterController.IsTargeting;

            if (gameplayCamera == null)
            {
                Debug.LogError($"{nameof(GameplayInputController)} on '{name}' cannot process input because Gameplay Camera reference is missing.", this);
                return;
            }

            Vector2 screenPosition = pointerPositionAction.ReadValue<Vector2>();
            if (IsPointerOverUi(screenPosition))
            {
                return;
            }

            Vector3 screenPoint = new Vector3(screenPosition.x, screenPosition.y, -gameplayCamera.transform.position.z);
            Vector2 worldPosition = gameplayCamera.ScreenToWorldPoint(screenPoint);
            if (handTargeting)
            {
                // Hand targeting enables colliders and may move presentation roots in the same frame.
                // Synchronize once before reusing the normal SourceBox overlap query.
                Physics2D.SyncTransforms();
            }

            Collider2D[] hits = Physics2D.OverlapPointAll(worldPosition, sourceBoxLayerMask);

            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D hit = hits[i];
                if (hit == null)
                {
                    continue;
                }

                SourceBox sourceBox = hit.GetComponentInParent<SourceBox>();
                if (sourceBox == null)
                {
                    continue;
                }

                if (boosterUnlockTutorialController != null &&
                    !boosterUnlockTutorialController.CanInteractWithSourceBox(sourceBox))
                {
                    return;
                }

                SourceBoxReleaseResult result;
                if (handTargeting)
                {
                    result = handBoosterController != null
                        ? handBoosterController.TryUseOnSourceBox(sourceBox)
                        : SourceBoxReleaseResult.InvalidState;
                }
                else
                {
                    result = sourceBox.IsRecoveredSourceBox
                        ? sourceBox.TryReleaseMarblesWithResult()
                        : sourceBoxBoardController != null
                        ? sourceBoxBoardController.TryReleaseSourceBoxWithResult(sourceBox)
                        : sourceBox.TryReleaseMarblesWithResult();
                }

                if (result == SourceBoxReleaseResult.BoardFull)
                {
                    boardFullMessageView?.PlayAt(sourceBox.transform);
                }
                else if (result == SourceBoxReleaseResult.Success && !handTargeting)
                {
                    levelSessionController?.MarkPlayerMoveCommitted();
                }

                if (result == SourceBoxReleaseResult.Success && handTargeting)
                {
                    boosterUnlockTutorialController?.NotifySourceBoxInteractionAccepted(sourceBox);
                }

                return;
            }
        }

        private bool IsPointerOverUi(Vector2 screenPosition)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return false;
            }

            if (pointerEventData == null)
            {
                pointerEventData = new PointerEventData(eventSystem);
            }

            pointerEventData.Reset();
            pointerEventData.position = screenPosition;

            uiRaycastResults.Clear();
            eventSystem.RaycastAll(pointerEventData, uiRaycastResults);
            return uiRaycastResults.Count > 0;
        }

        private void CreateActionsIfNeeded()
        {
            if (pointerPositionAction == null)
            {
                pointerPositionAction = new InputAction("Gameplay Pointer Position", InputActionType.Value, "<Pointer>/position");
            }

            if (pointerPressAction == null)
            {
                pointerPressAction = new InputAction("Gameplay Pointer Press", InputActionType.Button, "<Pointer>/press");
            }
        }

        private void CacheMissingReferences()
        {
            if (boardFullMessageView == null)
            {
                boardFullMessageView = FindFirstObjectByType<SourceBoxBoardFullMessageView>(FindObjectsInactive.Include);
            }

            if (sourceBoxBoardController == null)
            {
                sourceBoxBoardController = FindFirstObjectByType<SourceBoxBoardController>(FindObjectsInactive.Include);
            }

            if (boosterController == null)
            {
                boosterController = FindFirstObjectByType<BoosterController>(FindObjectsInactive.Include);
            }

            if (handBoosterController == null)
            {
                handBoosterController = FindFirstObjectByType<HandBoosterController>(FindObjectsInactive.Include);
            }

            if (levelSessionController == null)
            {
                levelSessionController = FindFirstObjectByType<LevelSessionController>(FindObjectsInactive.Include);
            }

            if (boosterUnlockTutorialController == null)
            {
                boosterUnlockTutorialController = FindFirstObjectByType<BoosterUnlockTutorialController>(FindObjectsInactive.Include);
            }
        }
    }
}
