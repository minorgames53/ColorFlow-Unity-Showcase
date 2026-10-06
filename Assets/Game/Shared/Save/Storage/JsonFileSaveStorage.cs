using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Game.Shared.Save
{
    public sealed class JsonFileSaveStorage : ISaveStorage
    {
        public const string SaveFileName = "save.json";
        public const string BackupFileName = "save.bak";
        public const string TempFileName = "save.tmp";

        private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);

        public string DirectoryPath { get; }
        public string SavePath { get; }
        public string BackupPath { get; }
        public string TempPath { get; }

        public static string DefaultSavePath =>
            Path.Combine(Application.persistentDataPath, SaveFileName);

        public JsonFileSaveStorage()
            : this(Application.persistentDataPath)
        {
        }

        public JsonFileSaveStorage(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                throw new ArgumentException("A save directory is required.", nameof(directoryPath));
            }

            DirectoryPath = Path.GetFullPath(directoryPath);
            SavePath = Path.Combine(DirectoryPath, SaveFileName);
            BackupPath = Path.Combine(DirectoryPath, BackupFileName);
            TempPath = Path.Combine(DirectoryPath, TempFileName);
        }

        public bool Exists()
        {
            return File.Exists(SavePath);
        }

        public string Load()
        {
            return File.ReadAllText(SavePath, Utf8WithoutBom);
        }

        public bool BackupExists()
        {
            return File.Exists(BackupPath);
        }

        public string LoadBackup()
        {
            return File.ReadAllText(BackupPath, Utf8WithoutBom);
        }

        public void RestoreBackup()
        {
            if (!File.Exists(BackupPath))
            {
                throw new FileNotFoundException("The save backup does not exist.", BackupPath);
            }

            Directory.CreateDirectory(DirectoryPath);
            WriteTempFile(File.ReadAllText(BackupPath, Utf8WithoutBom));

            try
            {
                if (File.Exists(SavePath))
                {
                    ReplacePrimaryWithTemp();
                }
                else
                {
                    File.Move(TempPath, SavePath);
                }
            }
            finally
            {
                if (File.Exists(TempPath))
                {
                    File.Delete(TempPath);
                }
            }
        }

        public void Save(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            Directory.CreateDirectory(DirectoryPath);
            WriteTempFile(json);

            try
            {
                if (!File.Exists(SavePath))
                {
                    // A primary-less save starts a new save lineage. Do not leave a stale backup
                    // that could later be mistaken for the backup of this new primary.
                    DeleteIfExists(BackupPath);
                    File.Move(TempPath, SavePath);
                    return;
                }

                File.Copy(SavePath, BackupPath, true);
                ReplacePrimaryWithTemp();
            }
            finally
            {
                if (File.Exists(TempPath))
                {
                    File.Delete(TempPath);
                }
            }
        }

        public void Delete()
        {
            DeleteIfExists(TempPath);
            DeleteIfExists(SavePath);
            DeleteIfExists(BackupPath);
        }

        private void WriteTempFile(string json)
        {
            DeleteIfExists(TempPath);

            using FileStream stream = new FileStream(
                TempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            using StreamWriter writer = new StreamWriter(stream, Utf8WithoutBom);

            writer.Write(json);
            writer.Flush();
            stream.Flush(true);
        }

        private void ReplacePrimaryWithTemp()
        {
            try
            {
                File.Replace(TempPath, SavePath, null, true);
            }
            catch (PlatformNotSupportedException)
            {
                ReplacePrimaryWithPortableFallback();
            }
            catch (NotSupportedException)
            {
                ReplacePrimaryWithPortableFallback();
            }
            catch (IOException)
            {
                ReplacePrimaryWithPortableFallback();
            }
        }

        private void ReplacePrimaryWithPortableFallback()
        {
            DeleteIfExists(SavePath);
            File.Move(TempPath, SavePath);
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
