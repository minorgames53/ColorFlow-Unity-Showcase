using System;
using System.Collections.Generic;

namespace Game.Shared.Editor.LevelManagement
{
    public interface ISharedLevelImportAdapter
    {
        string AdapterName { get; }

        IReadOnlyList<SharedLevelImportData> ParseInput(LevelImportRequest request, LevelImportValidationResult validation);
        void ValidateLevels(IReadOnlyList<SharedLevelImportData> levels, LevelImportSettings settings, LevelImportValidationResult validation);
        IReadOnlyList<SharedLevelSummary> GetExistingLevelSummaries(LevelImportSettings settings);
        SharedLevelImportResult CreateOrUpdateLevel(SharedLevelImportData level, SharedLevelSummary existingLevel, LevelImportSettings settings);
        void RebuildCatalog(LevelImportSettings settings, LevelImportReport report);
        void PingLevelAsset(SharedLevelSummary level);
        void SelectLevelAsset(SharedLevelSummary level);
        bool DeleteLevelAsset(SharedLevelSummary level);
    }

    public static class SharedLevelImportAdapterRegistry
    {
        private static readonly List<Func<ISharedLevelImportAdapter>> factories = new List<Func<ISharedLevelImportAdapter>>();

        public static void Register(Func<ISharedLevelImportAdapter> factory)
        {
            if (factory == null || factories.Contains(factory))
            {
                return;
            }

            factories.Add(factory);
        }

        public static ISharedLevelImportAdapter CreateFirst()
        {
            for (int i = factories.Count - 1; i >= 0; i--)
            {
                ISharedLevelImportAdapter adapter = factories[i]?.Invoke();
                if (adapter != null)
                {
                    return adapter;
                }
            }

            return null;
        }
    }
}
