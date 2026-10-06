using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Shared.Editor.LevelManagement
{
    [Serializable]
    public sealed class LevelImportSource
    {
        public string SourceName;
        public string Json;
    }

    public sealed class LevelImportRequest
    {
        public LevelImportSettings Settings;
        public List<LevelImportSource> Sources = new List<LevelImportSource>();
    }

    public sealed class SharedLevelImportData
    {
        public string LevelId;
        public int LevelNumber;
        public string SourceName;
        public object Payload;

        public string StableKey
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(LevelId))
                {
                    return LevelId;
                }

                return LevelNumber > 0 ? LevelNumber.ToString() : "-";
            }
        }
    }

    public sealed class SharedLevelSummary
    {
        public string LevelId;
        public int LevelNumber;
        public string AssetName;
        public string AssetPath;
        public string Difficulty;
        public bool IncludeInLoop;
        public bool IsValid;
        public string ValidationMessage;
        public UnityEngine.Object Asset;

        public string StableKey
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(LevelId))
                {
                    return LevelId;
                }

                return LevelNumber > 0 ? LevelNumber.ToString() : "-";
            }
        }
    }

    public enum SharedLevelImportResultKind
    {
        Created,
        Updated,
        Unchanged,
        Skipped
    }

    public sealed class SharedLevelImportResult
    {
        public SharedLevelImportResultKind Kind;
        public UnityEngine.Object Asset;
        public string AssetPath;
        public string Message;
    }
}
