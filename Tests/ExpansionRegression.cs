using System.Security.Cryptography;
using Game.Core;

static class ExpansionRegression
{
    public static async Task Run(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "framework-expansion-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var json = new JsonCodec();
        void Reject<T>(Action action, string name) where T : Exception
        {
            try { action(); } catch (T) { check(true, name); return; }
            throw new Exception("Expected " + typeof(T).Name + ": " + name);
        }
        var catalog = new ReleaseManifest { files = new[] {
            new ReleaseFile { name = "content", kind = "bundle", dependencies = new[] { "shared" } },
            new ReleaseFile { name = "ui", kind = "bundle", dependencies = new[] { "shared" } },
            new ReleaseFile { name = "shared", kind = "bundle" } },
            assets = new[] { new ResourceAddress { address = "orb", asset = "orb", bundle = "content" }, new ResourceAddress { address = "window", asset = "window", bundle = "ui" } } };
        var graph = new ResourceCatalog(catalog);
        check(string.Join(",", graph.LoadOrder("content")) == "shared,content", "bundle graph loads dependencies before dependents");
        check(graph.Resolve("window").bundle == "ui", "resource address resolves its own bundle");
        graph.Resolve("window").bundle = "missing";
        check(graph.Resolve("window").bundle == "ui", "resolved addresses cannot mutate the validated catalog");
        Reject<InvalidDataException>(() => graph.Resolve("missing"), "unknown resource addresses fail before loading");
        catalog.files[2].dependencies = new[] { "content" };
        Reject<InvalidDataException>(() => new ResourceCatalog(catalog), "circular bundle dependencies are rejected");
        catalog.files[2].dependencies = new[] { "missing" };
        Reject<InvalidDataException>(() => new ResourceCatalog(catalog), "missing bundle dependencies are rejected");
        catalog.files[2].dependencies = Array.Empty<string>(); catalog.assets[1].address = "orb";
        Reject<InvalidDataException>(() => new ResourceCatalog(catalog), "duplicate resource addresses are rejected");
        catalog.assets = Array.Empty<ResourceAddress>();
        Reject<InvalidDataException>(() => new ResourceCatalog(catalog), "multiple bundles require an explicit address catalog");

        string cacheRoot = Path.Combine(root, "cache");
        string Make(string category, string name, int bytes)
        {
            string path = Path.Combine(cacheRoot, category, name); Directory.CreateDirectory(path);
            File.WriteAllBytes(Path.Combine(path, "file"), new byte[bytes]); return path;
        }
        var protectedVersions = new HashSet<string>(new[] { "active", "previous", "pending", "booting" });
        foreach (string name in protectedVersions) Make("versions", name, 10);
        string old = Make("versions", "obsolete", 15), partial = Make("staging", "pending", 10), stale = Make("staging", "abandoned", 10);
        string backup = Make("repair-backups", "old-corrupt", 10);
        Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-3)); Directory.SetLastWriteTimeUtc(backup, DateTime.UtcNow.AddDays(-3));
        var cache = new UpdateCache(cacheRoot, new UpdateCachePolicy { maxBytes = 100, minimumFreeBytes = 0, staleHours = 24 }, () => 10000);
        check(cache.Maintain(protectedVersions) == 35 && !Directory.Exists(old) && !Directory.Exists(stale) && !Directory.Exists(backup), "cache cleanup removes obsolete versions and expired partial/repair directories");
        check(protectedVersions.All(name => Directory.Exists(Path.Combine(cacheRoot, "versions", name))) && Directory.Exists(partial), "cache cleanup protects active previous pending and booting versions");
        Reject<IOException>(() => cache.Maintain(protectedVersions, 60), "cache budget refuses incoming package instead of deleting protected versions");
        check(cache.Bytes == 50, "budget failure preserves protected cache bytes");
        cache = new UpdateCache(cacheRoot, new UpdateCachePolicy { maxBytes = 1000, minimumFreeBytes = 100 }, () => 110);
        Reject<IOException>(() => cache.Maintain(protectedVersions, 20), "free disk reserve is checked before update writes");
        Reject<ArgumentException>(() => new UpdateCache(cacheRoot, new UpdateCachePolicy { maxBytes = 0 }), "invalid cache budgets are rejected");

        string compiled = Path.Combine(root, "compiled"), metadata = Path.Combine(root, "metadata"); Directory.CreateDirectory(compiled); Directory.CreateDirectory(metadata);
        File.WriteAllText(Path.Combine(compiled, "Core.dll"), "stable client"); File.WriteAllText(Path.Combine(metadata, "aot-Core.dll"), "stripped metadata");
        using var key = RSA.Create(2048);
        var baseline = new ClientBaseline { clientBuild = "client", platform = "win64", unityVersion = "2022.3", publicKey = key.ToXmlString(false),
            aotAssemblies = new[] { new BaselineFile { name = "Core.dll", sha256 = ReleaseVerifier.Hash(Path.Combine(compiled, "Core.dll")) } },
            metadata = new[] { new BaselineFile { name = "aot-Core.dll", sha256 = ReleaseVerifier.Hash(Path.Combine(metadata, "aot-Core.dll")) } },
            allowedHotReferences = new[] { "known-api" } };
        ReleasePublishing.CheckBaseline(baseline, compiled, metadata, "2022.3", "win64", new[] { "known-api" });
        check(true, "publisher accepts unchanged client baseline and allowed API references");
        File.WriteAllText(Path.Combine(compiled, "New.dll"), "new AOT assembly");
        Reject<NotSupportedException>(() => ReleasePublishing.CheckBaseline(baseline, compiled, metadata, "2022.3", "win64", new[] { "known-api" }), "publisher rejects additional AOT assemblies");
        File.Delete(Path.Combine(compiled, "New.dll"));
        Reject<NotSupportedException>(() => ReleasePublishing.CheckBaseline(baseline, compiled, metadata, "2022.3", "win64", new[] { "new-api" }), "publisher rejects new references outside frozen compatibility baseline");
        Reject<NotSupportedException>(() => ReleasePublishing.CheckBaseline(baseline, compiled, metadata, "2023", "win64", Array.Empty<string>()), "publisher rejects changed engine version");
        File.AppendAllText(Path.Combine(compiled, "Core.dll"), "changed");
        Reject<NotSupportedException>(() => ReleasePublishing.CheckBaseline(baseline, compiled, metadata, "2022.3", "win64", Array.Empty<string>()), "publisher rejects changed AOT assemblies");
        string hot = Path.Combine(root, "hot.dll"), bundle = Path.Combine(root, "bundle"); File.WriteAllText(hot, "hot code"); File.WriteAllText(bundle, "content");
        var release = new ReleaseManifest { version = "1.2.0", resourceVersion = "1.2.0", sequence = 3, clientBuild = "client", platform = "win64", entryType = "Game.Entry",
            files = new[] { new ReleaseFile { name = "hot.dll", kind = "assembly" }, new ReleaseFile { name = "bundle", kind = "bundle" } } };
        var sources = new Dictionary<string, string> { ["hot.dll"] = hot, ["bundle"] = bundle };
        string output = Path.Combine(root, "published");
        ReleasePublishing.WritePackage(output, release, sources, key.ToXmlString(true), baseline.publicKey, json);
        var verified = new ReleaseVerifier(json, baseline.publicKey, "client", "win64").Read(File.ReadAllText(Path.Combine(output, "manifest.json")));
        foreach (var file in verified.files) ReleaseVerifier.VerifyFile(Path.Combine(output, file.name), file);
        check(verified.sequence == 3 && verified.clientBuild == "client", "independent publisher emits a signed verifiable package for the original client");
        string signed = File.ReadAllText(Path.Combine(output, "manifest.json"));
        string engineRoot = Path.Combine(root, "budget-engine");
        string dirtyStage = Path.Combine(engineRoot, "staging", release.version); Directory.CreateDirectory(dirtyStage);
        File.WriteAllBytes(Path.Combine(dirtyStage, "unrelated.part"), new byte[500]);
        long required = release.files.Sum(f => f.size) + System.Text.Encoding.UTF8.GetByteCount(signed) * 3L;
        var engine = new UpdateEngine(engineRoot, json, new ReleaseVerifier(json, baseline.publicKey, "client", "win64"), new QuietLog(),
            new UpdateCachePolicy { maxBytes = required + 499, minimumFreeBytes = 0 }, () => 10000000);
        bool budgetRejected = false;
        using (var source = new FileTransport(output))
            try { await engine.StageAsync(signed, source, null, default); } catch (IOException) { budgetRejected = true; }
        check(budgetRejected && File.Exists(Path.Combine(dirtyStage, "unrelated.part")) && string.IsNullOrEmpty(engine.PendingVersion),
            "unrelated staging bytes cannot hide the space required by an incoming release");
        Reject<IOException>(() => ReleasePublishing.WritePackage(output, release, sources, key.ToXmlString(true), baseline.publicKey, json), "publisher refuses to overwrite a published directory");
        using var other = RSA.Create(2048);
        Reject<CryptographicException>(() => ReleasePublishing.WritePackage(output + "-wrong-key", release, sources, other.ToXmlString(true), baseline.publicKey, json), "publisher rejects a signing key not trusted by the client");

        var scope = new StateScope("registration", new QuietLog()); int calls = 0;
        var registration = scope.OwnAsync(() => { calls++; return Task.CompletedTask; }); registration.Dispose();
        scope.OwnAsync(() => { calls += 10; registration.Dispose(); return Task.CompletedTask; });
        await scope.DisposeAsync();
        check(calls == 10, "completed child resources can detach ownership without growing parent cleanup lists");
    }
}
