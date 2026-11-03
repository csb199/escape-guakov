using System.Security.Cryptography;
using System.Text;
using Game.Contracts;
using Game.Core;
using Game.HotUpdate;

static class RecoveryRegression
{
    public static async Task Run(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "framework-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var json = new JsonCodec(); var log = new QuietLog();
        using var key = RSA.Create(2048);
        var verifier = new ReleaseVerifier(json, key.ToXmlString(false), "recovery-client", "win64");
        string Sign(ReleaseManifest release)
        {
            byte[] payload = Encoding.UTF8.GetBytes(json.Encode(release));
            return json.Encode(new SignedManifest { payload = Convert.ToBase64String(payload),
                signature = Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) });
        }
        (string signed, string directory, ReleaseManifest release) Package(string version, int sequence)
        {
            string directory = Path.Combine(root, "packages", version); Directory.CreateDirectory(directory);
            string[] names = { "hot.dll", "content" };
            foreach (string name in names) File.WriteAllText(Path.Combine(directory, name), version + name);
            var release = new ReleaseManifest { version = version, resourceVersion = version, sequence = sequence,
                clientBuild = "recovery-client", platform = "win64", entryType = "Game.Entry",
                files = names.Select(name => new ReleaseFile { name = name, kind = name.EndsWith("dll") ? "assembly" : "bundle",
                    size = new FileInfo(Path.Combine(directory, name)).Length, sha256 = ReleaseVerifier.Hash(Path.Combine(directory, name)) }).ToArray() };
            return (Sign(release), directory, release);
        }
        UpdateEngine Engine(string name) => new UpdateEngine(Path.Combine(root, name), json, verifier, log);
        async Task Reject<T>(Func<Task> action, string name) where T : Exception
        {
            try { await action(); } catch (T) { check(true, name); return; }
            throw new Exception("Expected " + typeof(T).Name + ": " + name);
        }
        var v1 = Package("1.0.0", 1); var v2 = Package("1.1.0", 2); var v3 = Package("1.2.0", 3);
        using var source1 = new FileTransport(v1.directory); using var source2 = new FileTransport(v2.directory);
        using var source3 = new FileTransport(v3.directory);

        var first = Engine("first"); await first.StageAsync(v1.signed, source1, null, default); first.BeginBoot();
        first = Engine("first"); await first.StageAsync(v1.signed, source1, null, default);
        check(first.BeginBoot().version == "1.0.0", "first interrupted boot can retry a verified baseline"); first.MarkHealthy();
        await first.StageAsync(v2.signed, source2, null, default); first.BeginBoot(); first.CancelBoot();
        first = Engine("first");
        check(first.PendingVersion == "1.1.0" && first.BeginBoot().version == "1.1.0", "cleanly cancelled candidate remains retryable with an active fallback");
        first = Engine("first");
        check(first.BeginBoot().version == "1.0.0", "unclean candidate failure still falls back to healthy release");
        await Reject<InvalidOperationException>(() => first.StageAsync(v2.signed, source2, null, default), "failed candidate stays quarantined when fallback exists");

        string legacyPath = Path.Combine(root, "legacy", "state.json");
        AtomicFile.Write(legacyPath, json.Encode(new UpdateState { failed = new[] { "1.0.0" } }));
        var legacy = Engine("legacy"); await legacy.StageAsync(v1.signed, source1, null, default); legacy.BeginBoot(); legacy.MarkHealthy();
        await legacy.StageAsync(v1.signed, source1, null, default);
        check(legacy.ActiveVersion == "1.0.0", "legacy first-install quarantine is recoverable and cleared after health confirmation");

        string savePath = Path.Combine(root, "save.json");
        var store = new SaveStore<SaveData>(savePath, json, log, () => new SaveData(), SaveData.MigrateAndValidate);
        string oldSave = "{\"schema\":1,\"bestScore\":7,\"volume\":0.5}";
        File.WriteAllText(savePath + ".bak", oldSave); File.WriteAllText(savePath, "broken");
        var restored = store.Load();
        check(restored.schema == 2 && File.ReadAllText(savePath) == oldSave && File.ReadAllText(savePath + ".bak") == oldSave,
            "save recovery restores original validated bytes without rewriting schema or backup");
        store.Save(restored); File.WriteAllText(savePath, "broken again");
        check(store.Load().bestScore == 7, "save remains recoverable after recovery followed by another save and corruption");
        File.Delete(savePath);
        check(store.Load().bestScore == 7 && File.Exists(savePath), "missing primary save restores valid backup instead of resetting progress");
        File.WriteAllText(savePath, "{\"schema\":99}");
        await Reject<NotSupportedException>(() => { store.Load(); return Task.CompletedTask; }, "future save schema still blocks backup downgrade");
        File.WriteAllText(savePath, "broken"); File.WriteAllText(savePath + ".bak", "also broken");
        await Reject<System.Text.Json.JsonException>(() => { store.Load(); return Task.CompletedTask; }, "invalid backup is never installed as repaired primary");
        check(File.ReadAllText(savePath) == "broken", "failed save validation leaves primary untouched");

        var repair = Engine("repair"); await repair.StageAsync(v1.signed, source1, null, default); repair.BeginBoot(); repair.MarkHealthy();
        await repair.StageAsync(v2.signed, source2, null, default); repair.BeginBoot(); repair.MarkHealthy();
        string installed = Path.Combine(repair.DirectoryFor("1.1.0"), "content"); File.WriteAllText(installed, "bad");
        repair = Engine("repair");
        await Reject<InvalidDataException>(() => { repair.BeginBoot(); return Task.CompletedTask; }, "damaged active release refuses to boot before repair");
        await Reject<OperationCanceledException>(() => repair.RepairActiveAsync(v2.signed, new InterruptedTransport(v2.directory), null, default),
            "interrupted active repair remains resumable");
        check(File.ReadAllText(installed) == "bad" && repair.ActiveVersion == "1.1.0", "interrupted repair does not replace active directory or version");
        await repair.RepairActiveAsync(v2.signed, source2, null, default);
        check(repair.ReadInstalled("1.1.0").version == "1.1.0" && repair.ActiveVersion == "1.1.0" && repair.PendingVersion == "",
            "active repair restores exact release without downgrading or creating a candidate");
        var state = json.Decode<UpdateState>(File.ReadAllText(Path.Combine(root, "repair/state.json")));
        check(state.previous == "1.0.0" && File.ReadAllText(savePath) == "broken", "repair preserves previous-version metadata and does not touch saves");
        File.WriteAllText(Path.Combine(repair.DirectoryFor("1.1.0"), "manifest.json"), "broken manifest");
        repair = Engine("repair"); await repair.RepairActiveAsync(v2.signed, source2, null, default);
        check(repair.BeginBoot().version == "1.1.0", "persisted signed manifest permits repairing corrupted installed metadata");
        await Reject<InvalidOperationException>(() => repair.RepairActiveAsync(v2.signed, source2, null, default), "active repair is forbidden after boot has started");
        repair = Engine("repair");
        var changed = json.Decode<ReleaseManifest>(json.Encode(v2.release)); changed.resourceVersion = "other";
        await Reject<InvalidDataException>(() => repair.RepairActiveAsync(Sign(changed), source2, null, default), "repair rejects republished content under the same version");
        var envelope = json.Decode<SignedManifest>(v2.signed); envelope.signature = Convert.ToBase64String(new byte[256]);
        await Reject<CryptographicException>(() => repair.RepairActiveAsync(json.Encode(envelope), source2, null, default), "repair still requires a valid signature");
        Directory.Move(repair.DirectoryFor("1.1.0"), Path.Combine(root, "missing-target-fixture"));
        repair = Engine("repair"); await repair.RepairActiveAsync(v2.signed, source2, null, default);
        check(repair.ReadInstalled("1.1.0").version == "1.1.0", "repair recovers when active directory is missing between installation moves");
        File.WriteAllText(installed, "bad again");
        await Reject<InvalidDataException>(() => repair.StageAsync(v1.signed, source1, null, default), "corrupt active payload does not permit signed version downgrade");
        await repair.StageAsync(v3.signed, source3, null, default);
        check(repair.BeginBoot().version == "1.2.0", "newer verified release can stage and boot despite damaged active payload");

        var race = Engine("repair-race"); await race.StageAsync(v1.signed, source1, null, default); race.BeginBoot(); race.MarkHealthy();
        race = Engine("repair-race"); var paused = new PausedTransport(v1.directory);
        Task repairing = race.RepairActiveAsync(v1.signed, paused, null, default);
        await paused.Started.Task; race.BeginBoot(); paused.Resume.SetResult(true);
        await Reject<InvalidOperationException>(() => repairing, "repair cannot replace an active directory if boot begins during download");

        foreach (string reserved in new[] { "manifest.json.new", "MANIFEST.JSON.BAK", "manifest.json.part", "HOT.DLL.part" })
        {
            var conflict = json.Decode<ReleaseManifest>(json.Encode(v1.release)); conflict.files[1].name = reserved;
            await Reject<InvalidDataException>(() => { verifier.Read(Sign(conflict)); return Task.CompletedTask; }, "package rejects internal filename conflict: " + reserved);
        }
        var finalCheck = Engine("final-verification");
        await Reject<InvalidDataException>(() => finalCheck.StageAsync(v1.signed, new TamperEarlierFileTransport(v1.directory), null, default),
            "complete file set is reverified before version directory commit");
        check(!Directory.Exists(finalCheck.DirectoryFor("1.0.0")) && finalCheck.PendingVersion == "", "failed final verification never publishes a pending release");
    }
    sealed class TamperEarlierFileTransport : IFileTransport
    {
        readonly string source;
        public TamperEarlierFileTransport(string source) { this.source = source; }
        public Task DownloadAsync(string name, string partial, Action<long> progress, CancellationToken cancellation)
        {
            File.Copy(Path.Combine(source, name), partial, true);
            if (name == "content") File.WriteAllText(Path.Combine(Path.GetDirectoryName(partial), "hot.dll"), "changed after validation");
            return Task.CompletedTask;
        }
    }
    sealed class PausedTransport : IFileTransport
    {
        readonly string source;
        public readonly TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public PausedTransport(string source) { this.source = source; }
        public async Task DownloadAsync(string name, string partial, Action<long> progress, CancellationToken cancellation)
        {
            Started.TrySetResult(true); await Resume.Task;
            File.Copy(Path.Combine(source, name), partial, true);
        }
    }
}
