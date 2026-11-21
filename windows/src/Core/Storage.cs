using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace TodoWidget.Core
{
    /// <summary>%APPDATA%\TodoWidget\todos.json 에 데이터를 원자적으로 저장/복구한다.</summary>
    public sealed class Storage
    {
        public string Directory { get; private set; }
        public string FilePath { get; private set; }
        string BackupPath { get { return FilePath + ".bak"; } }
        string TempPath { get { return FilePath + ".tmp"; } }

        public Storage(string directory)
        {
            Directory = directory;
            System.IO.Directory.CreateDirectory(directory);
            FilePath = Path.Combine(directory, "todos.json");
        }

        public static string DefaultDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TodoWidget");
            }
        }

        public AppData Load()
        {
            if (File.Exists(FilePath))
            {
                try { return Read(FilePath); }
                catch (Exception)
                {
                    // 손상된 파일은 지우지 않고 따로 보관한 뒤 백업에서 복구한다.
                    TryCopy(FilePath, Path.Combine(Directory,
                        "todos.corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json"));
                }
            }
            if (File.Exists(BackupPath))
            {
                try { return Read(BackupPath); }
                catch (Exception) { }
            }
            return new AppData();
        }

        public void Save(AppData data)
        {
            var serializer = new DataContractJsonSerializer(typeof(AppData));
            using (var fs = new FileStream(TempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(fs, new UTF8Encoding(false), false, true))
                {
                    serializer.WriteObject(writer, data);
                    writer.Flush();
                }
                fs.Flush(true);
            }

            if (File.Exists(FilePath))
                File.Replace(TempPath, FilePath, BackupPath, true);
            else
                File.Move(TempPath, FilePath);
        }

        static AppData Read(string path)
        {
            var serializer = new DataContractJsonSerializer(typeof(AppData));
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var data = serializer.ReadObject(fs) as AppData;
                if (data == null) throw new InvalidDataException("empty data");
                data.Normalize();
                return data;
            }
        }

        static void TryCopy(string from, string to)
        {
            try { File.Copy(from, to, true); } catch (Exception) { }
        }
    }
}
