namespace Game.Shared.Navigation
{
    public static class InitialSceneRouting
    {
        public const int MenuUnlockLevel = 11;

        public static SceneId Resolve(int currentLevel)
        {
            return currentLevel >= MenuUnlockLevel
                ? SceneId.Menu
                : SceneId.Game;
        }
    }
}
