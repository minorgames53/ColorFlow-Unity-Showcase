using System;
using UnityEngine;

namespace Game.Shared.Editor.LevelManagement
{
    public enum LevelImportMode
    {
        JsonFiles = 0,
        Folder = 2,
        ClipboardJson = 3
    }

    [Serializable]
    public sealed class LevelImportSettings
    {
        public LevelImportMode ImportMode = LevelImportMode.JsonFiles;
        public string DestinationFolder = "Assets/Game/Data/Levels";
        public UnityEngine.Object CatalogAsset;
        public bool CreateMissing = true;
        public bool OverwriteExisting = true;
        public bool IncludeSubfolders = true;
        public bool DryRun = true;

        public void Normalize()
        {
            if (!Enum.IsDefined(typeof(LevelImportMode), ImportMode))
            {
                ImportMode = LevelImportMode.JsonFiles;
            }

            if (string.IsNullOrWhiteSpace(DestinationFolder))
            {
                DestinationFolder = "Assets/Game/Data/Levels";
            }

            DestinationFolder = DestinationFolder.Replace('\\', '/').TrimEnd('/');
            if (!DestinationFolder.StartsWith("Assets", StringComparison.Ordinal))
            {
                DestinationFolder = "Assets/Game/Data/Levels";
            }
        }
    }
}
