using System;

namespace Game.Shared.Save
{
    public enum SaveValueType
    {
        Bool,
        Int,
        Float,
        String
    }

    [Serializable]
    public class SaveValueData
    {
        public string key = string.Empty;
        public SaveValueType type;

        public bool boolValue;
        public int intValue;
        public float floatValue;
        public string stringValue = string.Empty;
    }
}
