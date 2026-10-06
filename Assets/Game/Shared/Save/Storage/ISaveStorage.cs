namespace Game.Shared.Save
{
    public interface ISaveStorage
    {
        bool Exists();
        string Load();

        bool BackupExists();
        string LoadBackup();
        void RestoreBackup();

        void Save(string json);
        void Delete();
    }
}
