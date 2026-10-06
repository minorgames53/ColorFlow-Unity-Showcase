using System;
using UnityEngine;

namespace Game.Shared.Save
{
    public static class SaveJsonSerializer
    {
        [Serializable]
        private sealed class SaveVersionEnvelope
        {
            public int version;
        }

        public static string ToJson(GameSaveData data, bool prettyPrint)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            return JsonUtility.ToJson(data, prettyPrint);
        }

        public static bool TryFromJson(
            string json,
            out GameSaveData data,
            out bool requiresSave,
            out bool unsupportedVersion,
            out string error)
        {
            data = null;
            requiresSave = false;
            unsupportedVersion = false;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Save JSON is empty.";
                return false;
            }

            string trimmedJson = json.Trim();
            if (trimmedJson.Length < 2 ||
                trimmedJson[0] != '{' ||
                trimmedJson[trimmedJson.Length - 1] != '}')
            {
                error = "Save JSON does not contain a valid object root.";
                return false;
            }

            try
            {
                SaveVersionEnvelope versionEnvelope =
                    JsonUtility.FromJson<SaveVersionEnvelope>(trimmedJson);
                int schemaVersion = versionEnvelope != null ? versionEnvelope.version : 0;

                if (!IsSchemaVersionSupported(schemaVersion, out error))
                {
                    unsupportedVersion = true;
                    return false;
                }

                // Start from centralized defaults so fields omitted by a partial JSON retain
                // safe values instead of becoming CLR zero/null defaults. Schema validation is
                // performed first so a missing version is treated as version zero, not as the
                // current version inherited from these defaults.
                data = GameSaveDataFactory.CreateDeserializationTemplate();
                JsonUtility.FromJsonOverwrite(trimmedJson, data);

                bool normalizationChanged = GameSaveDataNormalizer.Normalize(data);
                requiresSave = normalizationChanged;
                return true;
            }
            catch (Exception exception)
            {
                data = null;
                error = $"Save JSON could not be parsed: {exception.Message}";
                return false;
            }
        }

        public static bool IsSchemaVersionSupported(int schemaVersion, out string error)
        {
            if (schemaVersion == SaveVersion.Current)
            {
                error = string.Empty;
                return true;
            }

            if (schemaVersion > SaveVersion.Current)
            {
                error =
                    $"Save schema version {schemaVersion} is newer than supported version " +
                    $"{SaveVersion.Current}.";
                return false;
            }

            error =
                $"Save schema version {schemaVersion} is unsupported. No schema upgrade path " +
                $"to version {SaveVersion.Current} is available.";
            return false;
        }
    }
}
