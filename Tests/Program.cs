using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Game.Contracts;
using Game.Core;
using Game.HotUpdate;

var suite = new Suite();
await suite.Run();

sealed class JsonCodec : IJsonCodec
{
    static readonly JsonSerializerOptions Options = new() { IncludeFields = true };
    public string Encode<T>(T value) => JsonSerializer.Serialize(value, Options);
    public T Decode<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}
sealed class QuietLog : IGameLog
{
    public void Write(GameLogLevel level, string module, string operation, string message, string correlationId = "", long durationMs = 0, Exception exception = null) { }
}
sealed class TestModule : IModule
{
    readonly List<string> history;
    readonly bool fail;
    public string Id { get; }
    public IReadOnlyList<string> Dependencies { get; }
    public TestModule(string id, string[] dependencies, List<string> history, bool fail = false)
    { Id = id; Dependencies = dependencies; this.history = history; this.fail = fail; }
    public Task InitializeAsync(CancellationToken cancellation) { history.Add(Id + ":init"); return Task.CompletedTask; }
    public Task StartAsync(CancellationToken cancellation) { history.Add(Id + ":start"); if (fail) throw new InvalidOperationException("start"); return Task.CompletedTask; }
    public Task StopAsync(CancellationToken cancellation) { history.Add(Id + ":stop"); return Task.CompletedTask; }
    public void Dispose() => history.Add(Id + ":dispose");
}
sealed class InterruptedTransport : IFileTransport
{
    readonly string source;
    public InterruptedTransport(string source) { this.source = source; }
    public Task DownloadAsync(string relativePath, string partialPath, Action<long> progress, CancellationToken cancellation)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(source, relativePath));
        File.WriteAllBytes(partialPath, bytes.Take(2).ToArray());
        throw new OperationCanceledException("Simulated process interruption");
    }
}
sealed class Suite
{
    readonly JsonCodec json = new();
    readonly QuietLog log = new();
    readonly RSA key = RSA.Create(2048);
    readonly string root = Path.Combine(Path.GetTempPath(), "energy-framework-tests-" + Guid.NewGuid().ToString("N"));
    readonly List<string> passed = new();
    ReleaseVerifier Verifier => new(json, key.ToXmlString(false), "client-1", "win64");
    void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed.Add(name); Console.WriteLine("PASS " + name); }
    async Task Reject<T>(Func<Task> action, string name) where T : Exception
    {
        try { await action(); } catch (T) { Check(true, name); return; }
        throw new Exception("Expected " + typeof(T).Name + ": " + name);
    }
    string Sign(ReleaseManifest manifest)
    {
        byte[] payload = Encoding.UTF8.GetBytes(json.Encode(manifest));
        return json.Encode(new SignedManifest { payload = Convert.ToBase64String(payload), signature = Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) });
    }
    (ReleaseManifest release, string signed, string directory) Package(string version, int sequence)
    {
        string directory = Path.Combine(root, "packages", version); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Game.HotUpdate.dll"), "test assembly bytes: " + version);
        File.WriteAllText(Path.Combine(directory, "gamecontent"), "resource bytes");
        var files = new[] { "Game.HotUpdate.dll", "gamecontent" }.Select(name => new ReleaseFile
        { name = name, kind = name.EndsWith("dll") ? "assembly" : "bundle", size = new FileInfo(Path.Combine(directory, name)).Length, sha256 = ReleaseVerifier.Hash(Path.Combine(directory, name)) }).ToArray();
        var release = new ReleaseManifest { version = version, resourceVersion = "1.0.0", clientBuild = "client-1", platform = "win64", sequence = sequence, entryType = "Game.Entry", files = files };
        string signed = Sign(release); File.WriteAllText(Path.Combine(directory, "manifest.json"), signed); return (release, signed, directory);
    }
    public async Task Run()
    {
        await ExpansionRegression.Run(Check);
        await SceneEntityRegression.Run(Check);
        await RecoveryRegression.Run(Check);
        await ProcedureRegression.Run(Check);
        await EntityRegression.Run(Check);
        Directory.CreateDirectory(root);
        await LifecycleRegression.Run(Check);
        await EntryRegression.Run(Check);
        BarrackRegression.Run(Check);
        var history = new List<string>(); var modules = new ModuleHost(log);
        modules.Add(new TestModule("ui", new[] { "content" }, history)); modules.Add(new TestModule("content", Array.Empty<string>(), history));
        await modules.StartAsync(default); await modules.StopAsync(default);
        Check(string.Join(",", history) == "content:init,content:start,ui:init,ui:start,ui:stop,ui:dispose,content:stop,content:dispose", "module dependency order and reverse cleanup");
        history.Clear(); modules = new ModuleHost(log); modules.Add(new TestModule("base", Array.Empty<string>(), history)); modules.Add(new TestModule("bad", new[] { "base" }, history, true));
        await Reject<InvalidOperationException>(() => modules.StartAsync(default), "startup failure surfaced");
        Check(history.Contains("bad:dispose") && history.Contains("base:dispose") && modules.States["bad"] == ModuleState.Failed, "partial initialization cleaned and failure retained");
        modules = new ModuleHost(log); modules.Add(new TestModule("a", new[] { "b" }, history)); modules.Add(new TestModule("b", new[] { "a" }, history));
        await Reject<InvalidOperationException>(() => modules.StartAsync(default), "cycle rejected before startup");
        modules = new ModuleHost(log); modules.Add(new TestModule("a", new[] { "missing" }, history));
        await Reject<InvalidOperationException>(() => modules.StartAsync(default), "missing dependency rejected");
        var bus = new EventBus(); int observed = 0; var subscription = bus.Subscribe<int>(x => observed += x);
        bus.Publish(3); subscription.Dispose(); subscription.Dispose(); bus.Publish(3);
        Check(observed == 3 && bus.SubscriptionCount == 0, "event unsubscription is idempotent");
        using (bus.Subscribe<int>(_ => throw new InvalidOperationException())) using (bus.Subscribe<int>(x => observed += x))
            await Reject<AggregateException>(() => { bus.Publish(2); return Task.CompletedTask; }, "one failing subscriber does not hide failures");
        Check(observed == 5, "other subscribers still receive events");

        string savePath = Path.Combine(root, "save.json"); var store = new SaveStore<SaveData>(savePath, json, log, () => new SaveData(), SaveData.MigrateAndValidate);
        var save = store.Parse("{\"schema\":1,\"bestScore\":7,\"volume\":0.5}");
        Check(save.schema == 2 && save.totalCollected == 7, "v1 save migrates to v2");
        store.Save(save); save.bestScore = 9; store.Save(save); File.WriteAllText(savePath, "broken");
        Check(store.Load().bestScore == 7, "corrupt save recovers atomic backup");
        File.WriteAllText(savePath, "{\"schema\":99}");
        await Reject<NotSupportedException>(() => { store.Load(); return Task.CompletedTask; }, "newer save is never silently downgraded");
        var inventoryStore = new SaveStore<InventoryData>(Path.Combine(root, "inventory.json"), json, log,
            () => new InventoryData(), value => value.coins >= 0 ? value : throw new InvalidDataException("Negative balance"));
        inventoryStore.Save(new InventoryData { coins = 42 });
        Check(inventoryStore.Load().coins == 42, "AOT storage supports a separately defined business schema");

        var v1 = Package("1.0.0", 1); var v2 = Package("1.1.0", 2);
        Check(Verifier.Read(v1.signed).version == "1.0.0", "valid signed manifest");
        using (var weak = RSA.Create(1024))
            await Reject<CryptographicException>(() => { new ReleaseVerifier(json, weak.ToXmlString(false), "client-1", "win64").Read(v1.signed); return Task.CompletedTask; }, "weak RSA key rejected");
        var envelope = json.Decode<SignedManifest>(v1.signed); envelope.payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("tampered"));
        await Reject<CryptographicException>(() => { Verifier.Read(json.Encode(envelope)); return Task.CompletedTask; }, "tampered manifest rejected");
        v1.release.clientBuild = "wrong";
        await Reject<NotSupportedException>(() => { Verifier.Read(Sign(v1.release)); return Task.CompletedTask; }, "client build mismatch rejected");
        v1.release.clientBuild = "client-1"; v1.release.platform = "android";
        await Reject<NotSupportedException>(() => { Verifier.Read(Sign(v1.release)); return Task.CompletedTask; }, "platform mismatch rejected");
        v1.release.platform = "win64"; v1.release.files[0].name = "../escape.dll";
        await Reject<InvalidDataException>(() => { Verifier.Read(Sign(v1.release)); return Task.CompletedTask; }, "package path traversal rejected");
        v1.release.files[0].name = "CON.dll";
        await Reject<InvalidDataException>(() => { Verifier.Read(Sign(v1.release)); return Task.CompletedTask; }, "Windows device path rejected");
        v1.release.files[0].name = "Game.HotUpdate.dll"; v1.release.files[0].dependencies = new[] { "Game.HotUpdate.dll" };
        await Reject<InvalidDataException>(() => { Verifier.Read(Sign(v1.release)); return Task.CompletedTask; }, "assembly dependency cycle rejected");
        v1.release.files[0].dependencies = Array.Empty<string>();

        var engine = new UpdateEngine(Path.Combine(root, "updates"), json, Verifier, log);
        await Reject<OperationCanceledException>(() => engine.StageAsync(v1.signed, new InterruptedTransport(v1.directory), null, default), "interrupted download leaves recoverable partial file");
        Check(new FileInfo(Path.Combine(root, "updates/staging/1.0.0/Game.HotUpdate.dll.part")).Length == 2, "partial bytes preserved");
        using var source1 = new FileTransport(v1.directory); using var source2 = new FileTransport(v2.directory);
        await engine.StageAsync(v1.signed, source1, null, default);
        Check(engine.BeginBoot().version == "1.0.0", "resumed package verifies and boots"); engine.MarkHealthy();
        await engine.StageAsync(v2.signed, source2, null, default);
        Check(engine.ActiveVersion == "1.0.0" && engine.PendingVersion == "1.1.0", "staging never changes running version");
        Check(engine.BeginBoot().version == "1.1.0", "cold boot selects new candidate");
        var recovered = new UpdateEngine(Path.Combine(root, "updates"), json, Verifier, log);
        Check(recovered.BeginBoot().version == "1.0.0", "crash before readiness falls back to healthy release");
        await Reject<InvalidOperationException>(() => recovered.StageAsync(v2.signed, source2, null, default), "failed version quarantined");
        var v3 = Package("1.2.0", 3); using var source3 = new FileTransport(v3.directory);
        await recovered.StageAsync(v3.signed, source3, null, default); recovered.BeginBoot(); recovered.MarkHealthy();
        Check(new UpdateEngine(Path.Combine(root, "updates"), json, Verifier, log).ActiveVersion == "1.2.0", "healthy activation persists across restart");
        await Reject<InvalidDataException>(() => recovered.StageAsync(v1.signed, source1, null, default), "signed downgrade rejected");
        var v4 = Package("1.3.0", 4); using var source4 = new FileTransport(v4.directory);
        await recovered.StageAsync(v4.signed, source4, null, default);
        File.AppendAllText(Path.Combine(recovered.DirectoryFor("1.3.0"), "gamecontent"), "corruption");
        Check(recovered.BeginBoot().version == "1.2.0", "installed candidate reverified before load");
        var corrupt = Package("1.4.0", 5); File.WriteAllText(Path.Combine(corrupt.directory, "gamecontent"), "bad"); using var corruptSource = new FileTransport(corrupt.directory);
        await Reject<InvalidDataException>(() => recovered.StageAsync(corrupt.signed, corruptSource, null, default), "bad download never activates");
        Check(recovered.ActiveVersion == "1.2.0", "healthy release survives rejected download");
        await HttpResume(true);
        await HttpResume(false);

        var mock = new MockScoreTransport { FailuresRemaining = 1, DelayMs = 1 }; var scores = new ScoreClient(mock, log);
        var replacement = new ScoreClient(new AlternateScoreTransport(), log);
        Check(await replacement.SubmitAsync("replacement", 1, TimeSpan.FromSeconds(1), default) == 321,
            "leaderboard transport can be replaced without changing the client");
        await scores.SubmitAsync("same-request", 5, TimeSpan.FromSeconds(1), default); await scores.SubmitAsync("same-request", 5, TimeSpan.FromSeconds(1), default);
        Check(mock.UniqueSubmissions == 1, "score retry uses stable idempotency key");
        mock.DelayMs = 100;
        await Reject<TimeoutException>(() => scores.SubmitAsync("slow", 5, TimeSpan.FromMilliseconds(5), default), "network deadline classified as timeout");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Reject<OperationCanceledException>(() => scores.SubmitAsync("cancel", 5, TimeSpan.FromSeconds(1), cancel.Token), "caller cancellation preserved");
        string logPath = Path.Combine(root, "Logs/game.jsonl"); var logger = new GameLog(logPath, "test", json, _ => { }, 300);
        logger.Write(GameLogLevel.Error, "auth", "request", "token=abc password=xyz Authorization: Bearer private-bearer-value", "trace-123", exception: new Exception("secret=qwerty"));
        string row = File.ReadAllText(logPath);
        Check(!row.Contains("abc") && !row.Contains("xyz") && !row.Contains("qwerty") && !row.Contains("private-bearer-value") && row.Contains("trace-123"), "structured log correlation and basic secret redaction");
        for (int i = 0; i < 20; i++) logger.Write(GameLogLevel.Information, "log", "rotate", new string('a', 200));
        Check(Directory.GetFiles(Path.GetDirectoryName(logPath)).Length <= 4 && File.Exists(logPath + ".3"), "bounded log rotation");
        string report = JsonSerializer.Serialize(new { passed = passed.Count, failed = 0, checks = passed }, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory("TestResults"); File.WriteAllText("TestResults/core-tests.json", report);
        Console.WriteLine($"ALL {passed.Count} CHECKS PASSED");
        key.Dispose();
    }
    async Task HttpResume(bool honorRange)
    {
        var portFinder = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        portFinder.Start(); int port = ((System.Net.IPEndPoint)portFinder.LocalEndpoint).Port; portFinder.Stop();
        using var server = new System.Net.HttpListener();
        string url = "http://127.0.0.1:" + port + "/"; server.Prefixes.Add(url); server.Start();
        byte[] original = Encoding.UTF8.GetBytes("verified-package-payload");
        string path = Path.Combine(root, "http-" + honorRange + ".part"); File.WriteAllBytes(path, original.Take(4).ToArray());
        var serve = Task.Run(async () =>
        {
            var context = await server.GetContextAsync();
            if (context.Request.Headers["Range"] != "bytes=4-") throw new Exception("Expected resumed HTTP range");
            int offset = honorRange ? 4 : 0;
            context.Response.StatusCode = honorRange ? 206 : 200;
            if (honorRange) context.Response.Headers.Add("Content-Range", $"bytes 4-{original.Length - 1}/{original.Length}");
            context.Response.ContentLength64 = original.Length - offset;
            await context.Response.OutputStream.WriteAsync(original, offset, original.Length - offset); context.Response.Close();
        });
        using var transport = new FileTransport(url); using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await transport.DownloadAsync("package.dll", path, null, deadline.Token); await serve;
        Check(File.ReadAllBytes(path).SequenceEqual(original), honorRange ? "HTTP 206 resumes partial file" : "HTTP 200 restarts instead of corrupt append");
    }
}
