using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Game.Core
{
    [Serializable] public sealed class UpdateCachePolicy
    {
        public long maxBytes = 8L * 1024 * 1024 * 1024;
        public long minimumFreeBytes = 256L * 1024 * 1024;
        public int staleHours = 48;
        public void Validate()
        {
            if (maxBytes < 1 || minimumFreeBytes < 0 || staleHours < 1) throw new ArgumentException("Invalid update cache budget");
        }
    }
    public sealed class UpdateCache
    {
        readonly string root;
        readonly UpdateCachePolicy policy;
        readonly Func<long> availableSpace;
        public UpdateCache(string root, UpdateCachePolicy policy, Func<long> availableSpace = null)
        {
            this.root = Path.GetFullPath(root); this.policy = policy; policy.Validate();
            this.availableSpace = availableSpace ?? (() => new DriveInfo(Path.GetPathRoot(this.root)).AvailableFreeSpace);
        }
        public long Bytes => Directories().Sum(Size);
        public long Maintain(ISet<string> protectedVersions, long incomingBytes = 0)
        {
            if (incomingBytes < 0) throw new ArgumentOutOfRangeException(nameof(incomingBytes));
            var folders = Directories(); long before = folders.Sum(Size);
            var candidates = folders.Where(path => !IsProtected(path, protectedVersions))
                .OrderBy(Directory.GetLastWriteTimeUtc).ToList();
            foreach (string path in candidates.ToArray())
            {
                bool obsoleteVersion = Path.GetFileName(Path.GetDirectoryName(path)) == "versions";
                if (obsoleteVersion || Directory.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddHours(-policy.staleHours))
                { SafeDirectory.DeleteTree(path); candidates.Remove(path); }
            }
            long bytes = Bytes;
            foreach (string path in candidates)
            {
                if (Fits(bytes, incomingBytes)) break;
                long size = Size(path); SafeDirectory.DeleteTree(path); bytes -= size;
            }
            if (!Fits(bytes, incomingBytes)) throw new IOException("Update disk budget exhausted; protected versions were retained");
            return before - bytes;
        }
        bool Fits(long bytes, long incoming) => incoming <= policy.maxBytes && bytes <= policy.maxBytes - incoming &&
            availableSpace() >= policy.minimumFreeBytes && incoming <= availableSpace() - policy.minimumFreeBytes;
        bool IsProtected(string path, ISet<string> versions)
        {
            string category = Path.GetFileName(Path.GetDirectoryName(path));
            return category != "repair-backups" && versions.Contains(Path.GetFileName(path));
        }
        List<string> Directories()
        {
            var result = new List<string>();
            if (!Directory.Exists(root)) return result;
            SafeDirectory.RejectLinks(root);
            foreach (string category in new[] { "versions", "staging", "repair-backups" })
            {
                string parent = Path.Combine(root, category);
                if (!Directory.Exists(parent)) continue;
                SafeDirectory.RejectLinks(parent);
                foreach (string child in Directory.GetDirectories(parent)) { SafeDirectory.RejectLinks(child); result.Add(child); }
            }
            return result;
        }
        public static long Size(string directory)
        {
            if (!Directory.Exists(directory)) return 0;
            SafeDirectory.RejectLinks(directory); long total = 0;
            foreach (string file in Directory.GetFiles(directory)) { SafeDirectory.RejectLinks(file); total = checked(total + new FileInfo(file).Length); }
            foreach (string child in Directory.GetDirectories(directory)) total = checked(total + Size(child));
            return total;
        }
    }
}
