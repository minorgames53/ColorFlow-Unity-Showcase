using System;
using System.Collections.Generic;

namespace Game.Shared.Editor.LevelManagement
{
    public enum LevelImportMessageSeverity
    {
        Info,
        Warning,
        Error
    }

    [Serializable]
    public sealed class LevelImportValidationMessage
    {
        public LevelImportMessageSeverity Severity;
        public string LevelKey;
        public string SourceName;
        public string Message;
    }

    public sealed class LevelImportValidationResult
    {
        private readonly List<LevelImportValidationMessage> messages = new List<LevelImportValidationMessage>();

        public IReadOnlyList<LevelImportValidationMessage> Messages => messages;
        public bool HasErrors { get; private set; }
        public bool HasWarnings { get; private set; }

        public void Clear()
        {
            messages.Clear();
            HasErrors = false;
            HasWarnings = false;
        }

        public void Add(LevelImportMessageSeverity severity, string levelKey, string sourceName, string message)
        {
            messages.Add(new LevelImportValidationMessage
            {
                Severity = severity,
                LevelKey = string.IsNullOrWhiteSpace(levelKey) ? "-" : levelKey,
                SourceName = string.IsNullOrWhiteSpace(sourceName) ? "-" : sourceName,
                Message = message ?? string.Empty
            });

            HasErrors |= severity == LevelImportMessageSeverity.Error;
            HasWarnings |= severity == LevelImportMessageSeverity.Warning;
        }
    }
}
