using System.Text;

namespace Gameplay.Levels
{
    public enum LevelDifficulty
    {
        Normal,
        Hard,
        VeryHard
    }

    public static class LevelDifficultyParser
    {
        public static LevelDifficulty Parse(string value, out bool isUnknown)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                isUnknown = false;
                return LevelDifficulty.Normal;
            }

            string trimmedValue = value.Trim();
            StringBuilder normalizedValue = new StringBuilder(trimmedValue.Length);
            for (int i = 0; i < trimmedValue.Length; i++)
            {
                char character = trimmedValue[i];
                if (character == '_' || char.IsWhiteSpace(character))
                {
                    continue;
                }

                normalizedValue.Append(char.ToLowerInvariant(character));
            }

            switch (normalizedValue.ToString())
            {
                case "normal":
                    isUnknown = false;
                    return LevelDifficulty.Normal;

                case "hard":
                case "zor":
                    isUnknown = false;
                    return LevelDifficulty.Hard;

                case "veryhard":
                case "cokzor":
                    isUnknown = false;
                    return LevelDifficulty.VeryHard;

                default:
                    isUnknown = true;
                    return LevelDifficulty.Normal;
            }
        }
    }
}
