using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Writer;
using System.Security.Cryptography;
using Game.Core;
using Game.Runtime;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Player;
using UnityEngine;

namespace Game.Editor
{
    public static class ReleasePublisher
    {
        const BuildTarget Target = BuildTarget.StandaloneWindows64;
        static string Argument(string name) => Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.Ordinal))?.Substring(name.Length + 1);
        static string Required(string name) => Argument(name) ?? throw new BuildFailedException("Missing " + name);
        static string[] References(string assembly)
        {
            using (var module = ModuleDefMD.Load(assembly))
            {
                // A conservative allowlist also catches new generic instantiations and native interop declarations.
                var generics = new List<string>();
                for (uint rid = 1; rid <= module.Metadata.TablesStream.MethodSpecTable.Rows; rid++)
                {
                    var method = module.ResolveMethodSpec(rid);
                    if (method.DeclaringType.DefinitionAssembly?.Name != module.Assembly.Name)
                        generics.Add("method-spec:" + method.FullName + "@" + method.DeclaringType.DefinitionAssembly?.Name);
                }
                for (uint rid = 1; rid <= module.Metadata.TablesStream.TypeSpecTable.Rows; rid++)
                {
                    var type = module.ResolveTypeSpec(rid);
                    if (type.DefinitionAssembly?.Name != module.Assembly.Name)
                        generics.Add("type-spec:" + type.FullName + "@" + type.DefinitionAssembly?.Name);
                }
                return module.GetMemberRefs().Where(m => m.DeclaringType.DefinitionAssembly?.Name != module.Assembly.Name)
                    .Select(m => "member:" + m.FullName + "@" + m.DeclaringType.DefinitionAssembly?.Name)
                    .Concat(module.GetTypeRefs().Where(t => t.DefinitionAssembly?.Name != module.Assembly.Name).Select(t => "type:" + t.FullName + "@" + t.DefinitionAssembly?.Name))
                    .Concat(module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasImplMap || m.CustomAttributes.Any(a => a.TypeFullName.Contains("MonoPInvokeCallback")))
                        .Select(m => "native:" + m.FullName))
                    .Concat(generics).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToArray();
            }
        }
        static string AotFingerprint(string path)
        {
            // Preserve IL, metadata, attributes and resources; discard build IDs and debug records only.
            using (var module = ModuleDefMD.Load(path))
            using (var bytes = new MemoryStream())
            using (var sha = SHA256.Create())
            {
                module.Mvid = Guid.Empty;
                var options = new ModuleWriterOptions(module) { WritePdb = false };
                options.PEHeadersOptions.TimeDateStamp = 0;
                module.Write(bytes, options);
                return BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "").ToLowerInvariant();
            }
        }
        public static void ExportBaseline()
        {
            var config = JsonUtility.FromJson<GameConfig>(File.ReadAllText("Assets/Resources/game-config.json"));
            var references = References("LocalOnly/FrozenV1/Game.HotUpdate.dll");
            if (References("LocalOnly/FrozenV2/Game.HotUpdate.dll").Except(references).Any())
                throw new BuildFailedException("V2 introduces references not covered by the V1 client generation; include them in the client before building");
            string output = Path.Combine("Builds/ClientBaselines", config.clientBuild);
            if (Directory.Exists(output)) return;
            string stage = output + ".new-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(Path.Combine(stage, "metadata"));
            var verifier = new ReleaseVerifier(new UnityJson(), config.publicKey, config.clientBuild, config.platform);
            var release = verifier.Read(File.ReadAllText("Assets/StreamingAssets/Baseline/manifest.json"));
            foreach (var file in release.files.Where(f => f.kind == "aot"))
                File.Copy(Path.Combine("Assets/StreamingAssets/Baseline", file.name), Path.Combine(stage, "metadata", file.name));
            var baseline = new ClientBaseline { clientBuild = config.clientBuild, platform = config.platform, publicKey = config.publicKey,
                unityVersion = Application.unityVersion, entryType = release.entryType, minimumSequence = 2, aotFingerprintFormat = "dnlib-normalized-v1",
                aotAssemblies = Directory.GetFiles("LocalOnly/FrozenV1", "*.dll").Where(p => Path.GetFileName(p) != "Game.HotUpdate.dll")
                    .Select(p => new BaselineFile { name = Path.GetFileName(p), sha256 = AotFingerprint(p) }).OrderBy(f => f.name).ToArray(),
                metadata = release.files.Where(f => f.kind == "aot").Select(f => new BaselineFile { name = f.name, sha256 = f.sha256 }).ToArray(),
                allowedHotReferences = references.OrderBy(s => s).ToArray() };
            File.WriteAllText(Path.Combine(stage, "baseline.json"), JsonUtility.ToJson(baseline, true));
            File.Copy("Assets/StreamingAssets/DemoUpdate/manifest.json", Path.Combine(stage, "latest-manifest.json"));
            Directory.Move(stage, output);
            Debug.Log("CLIENT_BASELINE_EXPORTED " + Path.GetFullPath(output));
        }
        // Command-line publish never calls Prepare, StampClientBuild, GenerateAll or BuildPlayer.
        public static void Publish()
        {
            string baselineDirectory = Path.GetFullPath(Required("--client-baseline"));
            string output = Path.GetFullPath(Required("--release-output"));
            var baseline = JsonUtility.FromJson<ClientBaseline>(File.ReadAllText(Path.Combine(baselineDirectory, "baseline.json")));
            string version = Required("--release-version"); ReleaseVerifier.ValidateName(version);
            int sequence = int.Parse(Required("--release-sequence"));
            string previousPath = Argument("--previous-manifest") ?? Path.Combine(baselineDirectory, "latest-manifest.json");
            var previous = new ReleaseVerifier(new UnityJson(), baseline.publicKey, baseline.clientBuild, baseline.platform).Read(File.ReadAllText(previousPath));
            if (sequence <= Math.Max(baseline.minimumSequence, previous.sequence) || version == previous.version)
                throw new BuildFailedException("New release requires a new version and increasing sequence");
            string work = Path.Combine("LocalOnly/Publish", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
            string define = Argument("--release-define");
            if (define != null && define != "DEMO_V2" && define != "DEMO_V3") throw new BuildFailedException("Unsupported demo variant");
            PlayerBuildInterface.CompilePlayerScripts(new ScriptCompilationSettings { target = Target, group = BuildTargetGroup.Standalone,
                options = ScriptCompilationOptions.None, extraScriptingDefines = define == null ? Array.Empty<string>() : new[] { define } }, work);
            if (baseline.aotFingerprintFormat != "dnlib-normalized-v1") throw new BuildFailedException("Re-export the client baseline with the current publisher");
            ReleasePublishing.CheckBaseline(baseline, work, Path.Combine(baselineDirectory, "metadata"), Application.unityVersion, Target.ToString(), References(Path.Combine(work, "Game.HotUpdate.dll")), AotFingerprint);
            string bundles = Path.Combine(work, "bundles");
            var content = DemoBuilder.BuildContent(bundles, define != null, define == "DEMO_V3" ? "V3" : null);
            var sources = new Dictionary<string, string>(); var files = new List<ReleaseFile>();
            foreach (var metadata in baseline.metadata)
            { sources.Add(metadata.name, Path.Combine(baselineDirectory, "metadata", metadata.name)); files.Add(new ReleaseFile { name = metadata.name, kind = "aot" }); }
            sources.Add("Game.HotUpdate.dll", Path.Combine(work, "Game.HotUpdate.dll")); files.Add(new ReleaseFile { name = "Game.HotUpdate.dll", kind = "assembly" });
            foreach (var file in content.files) { sources.Add(file.name, Path.Combine(bundles, file.name)); files.Add(file); }
            var release = new ReleaseManifest { version = version, resourceVersion = Argument("--resource-version") ?? version, sequence = sequence,
                clientBuild = baseline.clientBuild, platform = baseline.platform, entryType = baseline.entryType, files = files.ToArray(), assets = content.assets };
            ReleasePublishing.WritePackage(output, release, sources, File.ReadAllText(Required("--signing-key")), baseline.publicKey, new UnityJson());
            File.WriteAllText(output + ".report.json", JsonUtility.ToJson(new PublishReport { clientBuild = baseline.clientBuild, version = version,
                sequence = sequence, manifestSha256 = ReleaseVerifier.Hash(Path.Combine(output, "manifest.json")), baseline = baselineDirectory }, true));
            Debug.Log("HOT_RELEASE_PUBLISHED " + output);
        }
        [Serializable] sealed class PublishReport { public string clientBuild, version, manifestSha256, baseline; public int sequence; }
    }
}
