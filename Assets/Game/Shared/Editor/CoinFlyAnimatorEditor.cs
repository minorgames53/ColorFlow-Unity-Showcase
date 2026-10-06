#if UNITY_EDITOR
using Game.Shared.Navigation;
using Game.Shared.Save;
using Game.Shared.UI;
using UnityEditor;
using UnityEngine;

namespace Game.Shared.Editor
{
    [CustomEditor(typeof(CoinFlyAnimator))]
    public sealed class CoinFlyAnimatorEditor : UnityEditor.Editor
    {
        // Editor-instance state only: never written to the scene or player save.
        private int testAmount = 100;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            serializedObject.Update();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Editor Test — Menu", EditorStyles.boldLabel);
            testAmount = Mathf.Max(1, EditorGUILayout.IntField("Preview Amount", testAmount));
            EditorGUILayout.HelpBox(
                "Run the game in the Editor, open Menu with its popups closed, then press Test. " +
                "Coins start at Start Rect (Play Button) and fly to Target Gold HUD. " +
                "Preview Amount only controls the counter presentation; no gold is granted or saved.",
                MessageType.Info);

            var animator = (CoinFlyAnimator)target;
            string unavailableReason = GetUnavailableReason(animator);
            if (unavailableReason != null)
                EditorGUILayout.HelpBox(unavailableReason, MessageType.None);

            using (new EditorGUI.DisabledScope(unavailableReason != null))
            {
                if (GUILayout.Button("Test: Play Button → Gold HUD"))
                {
                    // Recheck at click time, without invoking any menu/progression callbacks.
                    if (GetUnavailableReason(animator) == null)
                        animator.Play(testAmount, GetReference<RectTransform>("startRect"));
                }
            }

        }

        public override bool RequiresConstantRepaint() => EditorApplication.isPlaying;

        private string GetUnavailableReason(CoinFlyAnimator animator)
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused || EditorApplication.isCompiling)
                return "Available only in Editor Play Mode, while the game is not paused or compiling.";
            if (!animator.gameObject.scene.IsValid() || animator.gameObject.scene.name != nameof(SceneId.Menu))
                return "Select the CoinFlyAnimator belonging to the Menu scene.";
            if (!animator.isActiveAndEnabled)
                return "The CoinFlyAnimator must be active and enabled.";
            if (animator.IsPlaying || CoinFlyPresentationHandoff.HasPendingPresentation)
                return "Wait for the current/pending coin presentation to finish.";

            SaveManager save = SaveManager.Instance;
            if (save == null || !save.IsInitialized)
                return "Start through Boot so the normal SaveManager is initialized.";

            RectTransform source = GetReference<RectTransform>("startRect");
            RectTransform destination = GetReference<RectTransform>("targetGoldHud");
            GoldDisplayPresenter display = GetReference<GoldDisplayPresenter>("goldDisplay");
            if (source == null || destination == null || display == null ||
                GetReference<Canvas>("animationCanvas") == null ||
                GetReference<RectTransform>("animationRoot") == null ||
                GetReference<RectTransform>("coinPrefab") == null)
                return "Assign Start Rect and all Required References first.";
            if (!source.gameObject.activeInHierarchy || !destination.gameObject.activeInHierarchy)
                return "The source and target must both be active in the Menu.";
            if (display.IsPresentationOverrideActive)
                return "The gold display is already owned by another presentation.";

            return null;
        }

        private T GetReference<T>(string propertyName) where T : Object
        {
            return serializedObject.FindProperty(propertyName).objectReferenceValue as T;
        }
    }
}
#endif
