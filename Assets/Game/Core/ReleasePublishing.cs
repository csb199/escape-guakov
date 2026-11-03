using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Game.Contracts;

namespace Game.Core
{
    [Serializable] public sealed class BaselineFile { public string name, sha256; }
    [Serializable] public sealed class ClientBaseline
    {
        public string clientBuild, platform, unityVersion, publicKey, entryType;
        public string aotFingerprintFormat = "sha256";
        public int minimumSequence;
        public BaselineFile[] aotAssemblies, metadata;
        public string[] allowedHotReferences;
    }
    public static class ReleasePublishing
    {
        public static void CheckBaseline(ClientBaseline baseline, string compiledDirectory, string metadataDirectory,
            string unityVersion, string platform, IEnumerable<string> hotReferences, Func<string, string> compiledFingerprint = null)
        {
            if (baseline == null || baseline.unityVersion != unityVersion || baseline.platform != platform)
                throw new NotSupportedException("Client baseline engine/platform mismatch");
            if (baseline.aotFingerprintFormat != "sha256" &&
                (baseline.aotFingerprintFormat != "dnlib-normalized-v1" || compiledFingerprint == null))
                throw new NotSupportedException("Unsupported AOT fingerprint format");
            CheckFiles(baseline.aotAssemblies, compiledDirectory, compiledFingerprint);
            var expected = new HashSet<string>(baseline.aotAssemblies.Select(f => f.name), StringComparer.OrdinalIgnoreCase);
            if (Directory.GetFiles(compiledDirectory, "*.dll").Any(p => Path.GetFileName(p) != "Game.HotUpdate.dll" && !expected.Contains(Path.GetFileName(p))))
                throw new NotSupportedException("New AOT assembly requires a new client baseline");
            CheckFiles(baseline.metadata, metadataDirectory);
            var allowed = new HashSet<string>(baseline.allowedHotReferences ?? Array.Empty<string>(), StringComparer.Ordinal);
            var added = hotReferences.Where(reference => !allowed.Contains(reference)).ToArray();
            if (added.Length > 0) throw new NotSupportedException("Hot code requires references outside the frozen client baseline: " + string.Join("; ", added));
        }
        static void CheckFiles(BaselineFile[] files, string directory, Func<string, string> fingerprint = null)
        {
            if (files == null || files.Length == 0) throw new InvalidDataException("Empty client baseline");
            foreach (var file in files)
            {
                ReleaseVerifier.ValidateName(file.name);
                if (!File.Exists(Path.Combine(directory, file.name)) || (fingerprint ?? ReleaseVerifier.Hash)(Path.Combine(directory, file.name)) != file.sha256)
                    throw new NotSupportedException("Frozen client file changed: " + file.name);
            }
        }
        // Export into a new directory. A published release is never overwritten.
        public static void WritePackage(string output, ReleaseManifest release, IReadOnlyDictionary<string, string> sources,
            string privateKey, string publicKey, IJsonCodec json)
        {
            if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Release output already exists: " + output);
            using (var rsa = RSA.Create())
            {
                rsa.FromXmlString(privateKey);
                if (rsa.ToXmlString(false) != publicKey) throw new CryptographicException("Signing key does not match the client baseline");
                foreach (var file in release.files)
                {
                    ReleaseVerifier.ValidateName(file.name);
                    string source = sources[file.name];
                    file.size = new FileInfo(source).Length; file.sha256 = ReleaseVerifier.Hash(source);
                }
                byte[] payload = Encoding.UTF8.GetBytes(json.Encode(release));
                string signed = json.Encode(new SignedManifest { payload = Convert.ToBase64String(payload),
                    signature = Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) });
                new ReleaseVerifier(json, publicKey, release.clientBuild, release.platform).Read(signed);
                string staging = Path.GetFullPath(output) + ".publishing-" + Guid.NewGuid().ToString("N");
                Directory.CreateDirectory(staging);
                try
                {
                    foreach (var file in release.files)
                    {
                        string target = Path.Combine(staging, file.name); File.Copy(sources[file.name], target);
                        ReleaseVerifier.VerifyFile(target, file);
                    }
                    AtomicFile.Write(Path.Combine(staging, "manifest.json"), signed);
                    Directory.Move(staging, Path.GetFullPath(output));
                }
                catch { SafeDirectory.DeleteTree(staging); throw; }
            }
        }
    }
    public static class SafeDirectory
    {
        // Never follow junctions/symlinks when removing generated/cache directories.
        public static void DeleteTree(string directory)
        {
            if (!Directory.Exists(directory)) return;
            RejectLinks(directory);
            foreach (var file in Directory.GetFiles(directory)) { RejectLinks(file); File.Delete(file); }
            foreach (var child in Directory.GetDirectories(directory)) DeleteTree(child);
            Directory.Delete(directory);
        }
        public static void RejectLinks(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Refusing linked cache path: " + path);
        }
    }
}
