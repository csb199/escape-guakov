using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Game.Contracts;

namespace Game.Core
{
    [Serializable] public sealed class SignedManifest { public string payload, signature; }
    [Serializable] public sealed class ReleaseFile
    {
        public string name, sha256, kind;
        public long size;
        public string[] dependencies = Array.Empty<string>();
    }
    [Serializable] public sealed class ReleaseManifest
    {
        public string version, resourceVersion, clientBuild, platform, entryType;
        public int sequence;
        public ReleaseFile[] files;
        public ResourceAddress[] assets = Array.Empty<ResourceAddress>();
    }
    [Serializable] public sealed class UpdateState
    {
        public string active = "", previous = "", pending = "", booting = "";
        public string activeManifest = "";
        public string[] failed = Array.Empty<string>();
    }
    public sealed class ReleaseVerifier
    {
        readonly IJsonCodec json;
        readonly string publicKey, clientBuild, platform;
        static readonly Regex SafeName = new Regex(@"^[A-Za-z0-9][A-Za-z0-9_.-]{0,119}$");
        public ReleaseVerifier(IJsonCodec json, string publicKey, string clientBuild, string platform)
        { this.json = json; this.publicKey = publicKey; this.clientBuild = clientBuild; this.platform = platform; }
        public static void ValidateName(string value)
        {
            if (string.IsNullOrEmpty(value) || !SafeName.IsMatch(value) || value.Contains("..") || value.EndsWith("."))
                throw new InvalidDataException("Unsafe package path: " + value);
            string stem = value.Split('.')[0].ToUpperInvariant();
            if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem))
                throw new InvalidDataException("Reserved file name");
        }
        public ReleaseManifest Read(string signedJson)
        {
            if (signedJson == null || signedJson.Length > 1024 * 1024) throw new InvalidDataException("Manifest exceeds size limit");
            var envelope = json.Decode<SignedManifest>(signedJson) ?? throw new InvalidDataException("Empty envelope");
            byte[] payload = Convert.FromBase64String(envelope.payload), signature = Convert.FromBase64String(envelope.signature);
            using (var rsa = RSA.Create())
            {
                rsa.FromXmlString(publicKey);
                if (rsa.KeySize < 2048) throw new CryptographicException("RSA signing keys must contain at least 2048 bits");
                if (!rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                    throw new CryptographicException("Manifest signature rejected");
            }
            var release = json.Decode<ReleaseManifest>(Encoding.UTF8.GetString(payload)) ?? throw new InvalidDataException("Empty manifest");
            ValidateName(release.version); ValidateName(release.resourceVersion);
            if (release.clientBuild != clientBuild || release.platform != platform) throw new NotSupportedException("Client build/platform mismatch; full client update required");
            if (release.sequence < 1 || string.IsNullOrWhiteSpace(release.entryType) || release.files == null || release.files.Length == 0 || release.files.Length > 128)
                throw new InvalidDataException("Invalid manifest fields");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var file in release.files)
            {
                if (file == null) throw new InvalidDataException("Null file entry");
                ValidateName(file.name);
                if (new[] { "manifest.json", "manifest.json.new", "manifest.json.bak", "manifest.json.part" }.Contains(file.name, StringComparer.OrdinalIgnoreCase) || !seen.Add(file.name))
                    throw new InvalidDataException("Duplicate/reserved file");
                if (file.size <= 0 || file.size > 512L * 1024 * 1024 || (total += file.size) > 2L * 1024 * 1024 * 1024 ||
                    file.sha256 == null || !Regex.IsMatch(file.sha256, "^[a-fA-F0-9]{64}$")) throw new InvalidDataException("Invalid size/hash");
                if (file.kind != "aot" && file.kind != "assembly" && file.kind != "bundle") throw new InvalidDataException("Unsupported file kind");
                if (file.dependencies == null) throw new InvalidDataException("Missing dependency list");
            }
            foreach (var file in release.files)
                if (seen.Contains(file.name + ".part")) throw new InvalidDataException("Payload conflicts with download temporary file: " + file.name);
            OrderedAssemblies(release);
            if (!release.files.Any(f => f.kind == "assembly")) throw new InvalidDataException("Expected hot assemblies");
            new ResourceCatalog(release);
            return release;
        }
        public static IReadOnlyList<ReleaseFile> OrderedAssemblies(ReleaseManifest release)
        {
            var map = release.files.Where(f => f.kind == "assembly").ToDictionary(f => f.name, StringComparer.OrdinalIgnoreCase);
            var result = new List<ReleaseFile>(); var visiting = new HashSet<string>(); var done = new HashSet<string>();
            void Visit(string name)
            {
                if (done.Contains(name)) return;
                if (!map.TryGetValue(name, out var file)) throw new InvalidDataException("Missing assembly dependency: " + name);
                if (!visiting.Add(name)) throw new InvalidDataException("Circular assembly dependency");
                foreach (var dependency in file.dependencies) Visit(dependency);
                visiting.Remove(name); done.Add(name); result.Add(file);
            }
            foreach (var file in map.Values) Visit(file.name);
            return result;
        }
        public static string Hash(string path)
        {
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        public static void VerifyFile(string path, ReleaseFile file)
        {
            if (!File.Exists(path) || new FileInfo(path).Length != file.size || !Hash(path).Equals(file.sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("File integrity rejected: " + file.name);
        }
    }

    public sealed class UpdateEngine
    {
        readonly string root;
        readonly IJsonCodec json;
        readonly ReleaseVerifier verifier;
        readonly IGameLog log;
        readonly SemaphoreSlim mutex = new SemaphoreSlim(1, 1);
        readonly UpdateCache cache;
        UpdateState state;
        bool bootStarted;
        public string ActiveVersion => state.active;
        public string PendingVersion => state.pending;
        public UpdateEngine(string root, IJsonCodec json, ReleaseVerifier verifier, IGameLog log, UpdateCachePolicy cachePolicy = null, Func<long> availableSpace = null)
        {
            this.root = root; this.json = json; this.verifier = verifier; this.log = log;
            cache = new UpdateCache(root, cachePolicy ?? new UpdateCachePolicy(), availableSpace);
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "state.json");
            if (File.Exists(path) || File.Exists(path + ".bak"))
            {
                try { state = ReadState(path); }
                catch
                {
                    state = ReadState(path + ".bak");
                    AtomicFile.Write(path, File.ReadAllText(path + ".bak"), preserveBackup: true);
                }
            }
            else state = new UpdateState();
            if (!string.IsNullOrEmpty(state.booting))
            {
                // With no healthy fallback, retry on the next launch and still allow a remote repair release.
                if (!string.IsNullOrEmpty(state.active))
                    state.failed = state.failed.Concat(new[] { state.booting }).Distinct().ToArray();
                log.Write(GameLogLevel.Warning, "update", "update.recover", "Previous candidate did not reach readiness: " + state.booting);
                state.pending = ""; state.booting = ""; Persist();
            }
        }
        UpdateState ReadState(string path)
        {
            var value = json.Decode<UpdateState>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid update state");
            foreach (string version in new[] { value.active, value.previous, value.pending, value.booting }.Concat(value.failed ?? Array.Empty<string>()))
                if (!string.IsNullOrEmpty(version)) ReleaseVerifier.ValidateName(version);
            value.failed = value.failed ?? Array.Empty<string>();
            return value;
        }
        void Persist() => AtomicFile.Write(Path.Combine(root, "state.json"), json.Encode(state));
        HashSet<string> ProtectedVersions(string incoming = null) => new HashSet<string>(new[] { state.active, state.previous, state.pending, state.booting, incoming }.Where(v => !string.IsNullOrEmpty(v)), StringComparer.OrdinalIgnoreCase);
        public async Task<long> MaintainCacheAsync(CancellationToken cancellation)
        {
            await mutex.WaitAsync(cancellation);
            try
            {
                long removed = cache.Maintain(ProtectedVersions());
                log.Write(GameLogLevel.Information, "update", "cache.cleaned", "bytes=" + removed + " remaining=" + cache.Bytes);
                return removed;
            }
            finally { mutex.Release(); }
        }
        string ActiveManifest()
        {
            string signed = state.activeManifest;
            if (string.IsNullOrEmpty(signed)) signed = File.ReadAllText(Path.Combine(DirectoryFor(state.active), "manifest.json"));
            if (verifier.Read(signed).version != state.active) throw new InvalidDataException("Active version identity mismatch");
            if (string.IsNullOrEmpty(state.activeManifest)) { state.activeManifest = signed; Persist(); }
            return signed;
        }
        public string DirectoryFor(string version) { ReleaseVerifier.ValidateName(version); return Path.Combine(root, "versions", version); }
        public ReleaseManifest ReadInstalled(string version)
        {
            string directory = DirectoryFor(version);
            var release = verifier.Read(File.ReadAllText(Path.Combine(directory, "manifest.json")));
            if (release.version != version) throw new InvalidDataException("Installed version identity mismatch");
            foreach (var file in release.files) ReleaseVerifier.VerifyFile(Path.Combine(directory, file.name), file);
            return release;
        }
        public Task StageAsync(string signedJson, IFileTransport transport, Action<string, float> progress, CancellationToken cancellation) =>
            StageCoreAsync(signedJson, transport, progress, cancellation, false);
        // Repair is allowed only before any assembly or AssetBundle from this engine is loaded.
        public Task RepairActiveAsync(string signedJson, IFileTransport transport, Action<string, float> progress, CancellationToken cancellation) =>
            StageCoreAsync(signedJson, transport, progress, cancellation, true);
        async Task StageCoreAsync(string signedJson, IFileTransport transport, Action<string, float> progress, CancellationToken cancellation, bool repairActive)
        {
            await mutex.WaitAsync(cancellation);
            string updateId = Guid.NewGuid().ToString("N");
            try
            {
                if (!string.IsNullOrEmpty(state.booting)) throw new InvalidOperationException("Candidate is still booting");
                var release = verifier.Read(signedJson);
                if (repairActive && (bootStarted || string.IsNullOrEmpty(state.active) || release.version != state.active))
                    throw new InvalidOperationException("Repair requires the active release before boot");
                if (!string.IsNullOrEmpty(state.active) && state.failed.Contains(release.version))
                    throw new InvalidOperationException("Failed release is quarantined; publish a new version");
                if (!string.IsNullOrEmpty(state.active))
                {
                    string activeSigned = ActiveManifest();
                    var active = verifier.Read(activeSigned);
                    if (release.version == active.version)
                    {
                        if (activeSigned != signedJson)
                            throw new InvalidDataException("Published versions are immutable");
                        if (!repairActive) return;
                    }
                    else if (release.sequence <= active.sequence) throw new InvalidDataException("Release sequence must increase");
                }
                string target = DirectoryFor(release.version);
                if (Directory.Exists(target) && !repairActive)
                {
                    ReadInstalled(release.version);
                    if (File.ReadAllText(Path.Combine(target, "manifest.json")) != signedJson) throw new InvalidDataException("Published versions are immutable");
                }
                else
                {
                    string stage = Path.Combine(root, "staging", release.version);
                    // Reserve the full download: arbitrary stale staging files must never reduce the budget.
                    long needed = checked(release.files.Sum(file => file.size) + Encoding.UTF8.GetByteCount(signedJson) * 3L);
                    cache.Maintain(ProtectedVersions(release.version), needed);
                    Directory.CreateDirectory(stage);
                    for (int i = 0; i < release.files.Length; i++)
                    {
                        var file = release.files[i]; string complete = Path.Combine(stage, file.name), part = complete + ".part";
                        bool valid = false;
                        if (File.Exists(complete)) { try { ReleaseVerifier.VerifyFile(complete, file); valid = true; } catch (InvalidDataException) { File.Delete(complete); } }
                        if (!valid)
                        {
                            for (int attempt = 0; ; attempt++)
                            {
                                cancellation.ThrowIfCancellationRequested();
                                try
                                {
                                    await transport.DownloadAsync(file.name, part, bytes =>
                                    {
                                        if (bytes > file.size) throw new InvalidDataException("Download exceeds signed size: " + file.name);
                                        progress?.Invoke(file.name, (float)((i + Math.Min(1, (double)bytes / file.size)) / release.files.Length));
                                    }, cancellation);
                                    ReleaseVerifier.VerifyFile(part, file); File.Move(part, complete); break;
                                }
                                catch (OperationCanceledException) { throw; }
                                catch (Exception e) when ((e is IOException || e is System.Net.Http.HttpRequestException || e is TimeoutException) && attempt < 2)
                                {
                                    if (e is InvalidDataException && File.Exists(part)) File.Delete(part);
                                    log.Write(GameLogLevel.Warning, "update", "download.retry", file.name, updateId, exception: e);
                                    await Task.Delay(100 * (attempt + 1), cancellation);
                                }
                            }
                        }
                    }
                    foreach (var file in release.files) ReleaseVerifier.VerifyFile(Path.Combine(stage, file.name), file);
                    AtomicFile.Write(Path.Combine(stage, "manifest.json"), signedJson);
                    cancellation.ThrowIfCancellationRequested();
                    if (repairActive && bootStarted) throw new InvalidOperationException("Boot started while repair was downloading");
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    string displaced = null;
                    if (Directory.Exists(target))
                    {
                        displaced = Path.Combine(root, "repair-backups", release.version + "-" + updateId);
                        Directory.CreateDirectory(Path.GetDirectoryName(displaced)); Directory.Move(target, displaced);
                    }
                    try { Directory.Move(stage, target); }
                    catch
                    {
                        if (displaced != null && !Directory.Exists(target)) Directory.Move(displaced, target);
                        throw;
                    }
                }
                cancellation.ThrowIfCancellationRequested();
                if (!repairActive) state.pending = release.version;
                Persist();
                log.Write(GameLogLevel.Information, "update", repairActive ? "update.repaired" : "update.staged", release.version, updateId);
            }
            finally { mutex.Release(); }
        }
        // Call only during cold boot, before loading any candidate assembly.
        public ReleaseManifest BeginBoot()
        {
            if (!string.IsNullOrEmpty(state.booting)) throw new InvalidOperationException("Boot already in progress");
            string version = string.IsNullOrEmpty(state.pending) ? state.active : state.pending;
            if (string.IsNullOrEmpty(version)) throw new InvalidOperationException("No installed release");
            ReleaseManifest release;
            try { release = ReadInstalled(version); }
            catch (Exception e) when (!string.IsNullOrEmpty(state.pending) && !string.IsNullOrEmpty(state.active))
            {
                state.failed = state.failed.Concat(new[] { state.pending }).Distinct().ToArray();
                state.pending = ""; Persist(); version = state.active; release = ReadInstalled(version);
                log.Write(GameLogLevel.Warning, "update", "update.fallback", "Rejected candidate before loading", exception: e);
            }
            if (version != state.active) { state.booting = version; Persist(); }
            else ActiveManifest();
            bootStarted = true;
            return release;
        }
        // Call only after a cancelled startup has finished all module cleanup, before process exit.
        public void CancelBoot()
        {
            if (string.IsNullOrEmpty(state.booting)) return;
            state.booting = ""; Persist();
            log.Write(GameLogLevel.Information, "update", "update.boot.cancelled", "Candidate retained for next launch");
        }
        public void MarkHealthy()
        {
            if (string.IsNullOrEmpty(state.booting)) return;
            string signed = File.ReadAllText(Path.Combine(DirectoryFor(state.booting), "manifest.json"));
            if (verifier.Read(signed).version != state.booting) throw new InvalidDataException("Boot version identity mismatch");
            state.previous = state.active; state.active = state.booting; state.activeManifest = signed; state.booting = ""; state.pending = "";
            state.failed = state.failed.Where(version => version != state.active).ToArray(); Persist();
            log.Write(GameLogLevel.Information, "update", "update.healthy", state.active);
        }
    }
}
