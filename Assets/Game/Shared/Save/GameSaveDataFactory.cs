using System;

namespace Game.Shared.Save
{
    public static class GameSaveDataFactory
    {
        public const int InitialCurrentLevel = 1;
        public const int InitialLives = 5;
        public const int InitialMaxLives = 5;
        public const int InitialGold = 500;
        public const int InitialLastLoopContentLevelNumber = 0;
        public const bool InitialSoundEnabled = true;
        public const bool InitialHapticEnabled = true;
        public const string HandBoosterId = "hand";
        public const string ShuffleBoosterId = "shuffle";
        public const string UfoBoosterId = "ufo";

        private static readonly string[] DefaultBoosterIds =
        {
            HandBoosterId,
            ShuffleBoosterId,
            UfoBoosterId
        };

        public static GameSaveData CreateDefault()
        {
            GameSaveData data = new GameSaveData
            {
                version = SaveVersion.Current,
                level = new LevelSaveData
                {
                    currentLevel = InitialCurrentLevel,
                    lastLoopContentLevelNumber = InitialLastLoopContentLevelNumber
                },
                lives = new LivesSaveData
                {
                    current = InitialLives,
                    max = InitialMaxLives
                },
                gold = new GoldSaveData
                {
                    amount = InitialGold
                },
                store = new StoreSaveData
                {
                    hasNoAds = false,
                    noAdsIntroShown = false,
                    starterPackPurchased = false,
                    infiniteLivesEndUtc = 0,
                    pendingFailOfferContinueCredits = 0,
                    reservedFailOfferContinueCredits = 0
                }
            };

            AddMissingDefaultEntries(data);
            return data;
        }

        internal static GameSaveData CreateDeserializationTemplate()
        {
            return CreateDefault();
        }

        internal static bool AddMissingDefaultEntries(GameSaveData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            bool changed = false;

            for (int i = 0; i < DefaultBoosterIds.Length; i++)
            {
                string id = DefaultBoosterIds[i];
                if (ContainsBooster(data, id))
                {
                    continue;
                }

                data.boosters.Add(new BoosterSaveData
                {
                    id = id,
                    amount = 0,
                    unlocked = false
                });
                changed = true;
            }

            if (!ContainsSetting(data, SaveKeys.SoundEnabled))
            {
                data.settings.Add(CreateBoolSetting(SaveKeys.SoundEnabled, InitialSoundEnabled));
                changed = true;
            }

            if (!ContainsSetting(data, SaveKeys.HapticEnabled))
            {
                data.settings.Add(CreateBoolSetting(SaveKeys.HapticEnabled, InitialHapticEnabled));
                changed = true;
            }

            return changed;
        }

        private static SettingSaveData CreateBoolSetting(string key, bool value)
        {
            return new SettingSaveData
            {
                key = key,
                type = SaveValueType.Bool,
                boolValue = value,
                stringValue = string.Empty
            };
        }

        private static bool ContainsBooster(GameSaveData data, string id)
        {
            if (data.boosters == null)
            {
                return false;
            }

            for (int i = 0; i < data.boosters.Count; i++)
            {
                BoosterSaveData booster = data.boosters[i];
                if (booster != null && string.Equals(booster.id, id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsSetting(GameSaveData data, string key)
        {
            if (data.settings == null)
            {
                return false;
            }

            for (int i = 0; i < data.settings.Count; i++)
            {
                SettingSaveData setting = data.settings[i];
                if (setting != null && string.Equals(setting.key, key, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
