using System;
using System.Collections.Generic;

namespace Game.Shared.Editor.LevelManagement
{
    public enum LevelImportReportGroup
    {
        Created,
        Updated,
        Unchanged,
        Skipped,
        Warning,
        Error
    }

    [Serializable]
    public sealed class LevelImportReportEntry
    {
        public LevelImportReportGroup Group;
        public string LevelKey;
        public string SourceName;
        public string Result;
        public string Message;
    }

    public sealed class LevelImportReport
    {
        private readonly List<LevelImportReportEntry> entries = new List<LevelImportReportEntry>();

        public IReadOnlyList<LevelImportReportEntry> Entries => entries;

        public int CreatedCount => Count(LevelImportReportGroup.Created);
        public int UpdatedCount => Count(LevelImportReportGroup.Updated);
        public int UnchangedCount => Count(LevelImportReportGroup.Unchanged);
        public int SkippedCount => Count(LevelImportReportGroup.Skipped);
        public int WarningCount => Count(LevelImportReportGroup.Warning);
        public int ErrorCount => Count(LevelImportReportGroup.Error);

        public void Clear()
        {
            entries.Clear();
        }

        public void Add(LevelImportReportGroup group, string levelKey, string sourceName, string result, string message)
        {
            entries.Add(new LevelImportReportEntry
            {
                Group = group,
                LevelKey = string.IsNullOrWhiteSpace(levelKey) ? "-" : levelKey,
                SourceName = string.IsNullOrWhiteSpace(sourceName) ? "-" : sourceName,
                Result = result ?? group.ToString(),
                Message = message ?? string.Empty
            });
        }

        public void AddValidation(LevelImportValidationResult validation)
        {
            if (validation == null)
            {
                return;
            }

            for (int i = 0; i < validation.Messages.Count; i++)
            {
                LevelImportValidationMessage message = validation.Messages[i];
                if (message.Severity == LevelImportMessageSeverity.Info)
                {
                    continue;
                }

                Add(
                    message.Severity == LevelImportMessageSeverity.Error ? LevelImportReportGroup.Error : LevelImportReportGroup.Warning,
                    message.LevelKey,
                    message.SourceName,
                    message.Severity.ToString(),
                    message.Message);
            }
        }

        private int Count(LevelImportReportGroup group)
        {
            int count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Group == group)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
