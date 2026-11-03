using System;
using System.IO;

namespace Game.HotUpdate
{
    [Serializable]
    public sealed class SaveData
    {
        public int schema = 2;
        public int bestScore;
        public int totalCollected;
        public int completedRuns;
        public float volume = 0.7f;
        public bool mute;
        public static SaveData MigrateAndValidate(SaveData save)
        {
            if (save == null) throw new InvalidDataException("Empty save");
            if (save.schema > 2) throw new NotSupportedException("Save belongs to a newer client.");
            if (save.schema < 1 || save.bestScore < 0 || save.totalCollected < 0 || save.completedRuns < 0 ||
                float.IsNaN(save.volume) || save.volume < 0 || save.volume > 1) throw new InvalidDataException("Invalid save fields");
            if (save.schema == 1) { save.schema = 2; save.totalCollected = save.bestScore; }
            return save;
        }
    }
    [Serializable]
    public sealed class DemoConfig
    {
        public int targetScore = 5;
        public float roundSeconds = 30;
        public int poolCapacity = 12;
        public void Validate()
        {
            if (targetScore < 1 || targetScore > 100 || float.IsNaN(roundSeconds) || roundSeconds < 5 || roundSeconds > 600 ||
                poolCapacity < Math.Max(5, targetScore) || poolCapacity > 200) throw new InvalidDataException("Invalid demo settings");
        }
    }
}
