using System;
using System.IO;
using Game.Contracts;

namespace Game.Core
{
    public static class AtomicFile
    {
        public static void Write(string path, string text, bool preserveBackup = false)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp = path + ".new";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(text);
                stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, preserveBackup ? null : path + ".bak");
            else File.Move(temp, path);
        }
    }
    // The storage mechanism is AOT; schemas, migrations and validation belong to the caller.
    public sealed class SaveStore<T> where T : class
    {
        readonly string path;
        readonly IJsonCodec json;
        readonly IGameLog log;
        readonly Func<T> createDefault;
        readonly Func<T, T> migrateAndValidate;
        public SaveStore(string path, IJsonCodec json, IGameLog log, Func<T> createDefault, Func<T, T> migrateAndValidate)
        {
            this.path = path; this.json = json; this.log = log;
            this.createDefault = createDefault; this.migrateAndValidate = migrateAndValidate;
        }
        public T Load()
        {
            if (!File.Exists(path) && !File.Exists(path + ".bak")) return migrateAndValidate(createDefault());
            try { return Parse(File.ReadAllText(path)); }
            catch (NotSupportedException) { throw; } // Never overwrite a save created by a newer client.
            catch (Exception e)
            {
                log.Write(GameLogLevel.Warning, "save", "save.recover", "Restoring backup after missing or corrupt save", exception: e);
                if (!File.Exists(path + ".bak")) throw new InvalidDataException("Save is corrupt and has no backup.", e);
                string backup = File.ReadAllText(path + ".bak");
                var value = Parse(backup);
                // Restore the validated original schema without replacing the only good backup.
                AtomicFile.Write(path, backup, preserveBackup: true);
                return value;
            }
        }
        public T Parse(string source) => migrateAndValidate(json.Decode<T>(source) ?? throw new InvalidDataException("Empty save"));
        public void Save(T save)
        {
            string data = json.Encode(migrateAndValidate(save));
            AtomicFile.Write(path, data);
            log.Write(GameLogLevel.Information, "save", "save.write", "Progress committed");
        }
    }
}
