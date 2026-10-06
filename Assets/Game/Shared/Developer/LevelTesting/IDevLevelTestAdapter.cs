using System;

namespace Game.Shared.Developer.LevelTesting
{
    public interface IDevLevelTestAdapter
    {
        int CurrentLevelNumber { get; }

        event Action<int> LevelChanged;

        bool TryLoadLevel(int oneBasedLevelNumber);
        bool TryTriggerWin();
        bool TryTriggerLose();
    }
}
