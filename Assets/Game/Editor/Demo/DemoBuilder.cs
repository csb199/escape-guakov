using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Game.Core;
using Game.Runtime;
using HybridCLR;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Player;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Editor
{
    // Demo 内容生成和完整客户端构建入口，包含示例专属资源地址与场景布局；不是运行时框架模块。
    // 新游戏替换这里的内容构建配置；独立热更发布入口见 ../Publishing/ReleasePublisher.cs。
    public static class DemoBuilder
    {
        const BuildTarget Target = BuildTarget.StandaloneWindows64;
        static readonly string[] Scenes = { "Assets/Game/Scenes/Boot.unity", "Assets/Game/Scenes/Lobby.unity", "Assets/Game/Scenes/Arena.unity" };
        static string Root => Path.GetFullPath(".");
        [MenuItem("Game Framework/1 Prepare Demo")]
        public static void Prepare()
        {
            Directory.CreateDirectory("Assets/Game/Scenes"); Directory.CreateDirectory("Assets/Game/Content");
            Directory.CreateDirectory("Assets/Resources"); Directory.CreateDirectory("Assets/StreamingAssets"); Directory.CreateDirectory("LocalOnly");
            PlayerSettings.companyName = "FrameworkLab"; PlayerSettings.productName = "EnergyCollector";
            PlayerSettings.bundleVersion = "1.0.0"; PlayerSettings.defaultScreenWidth = 1280; PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed; PlayerSettings.runInBackground = true;
            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Standalone, ApiCompatibilityLevel.NET_Standard);
            EditorUserBuildSettings.development = false;
            ConfigureHybrid();
            if (!File.Exists("LocalOnly/demo-signing-key.xml"))
                using (var rsa = new RSACryptoServiceProvider(2048)) { File.WriteAllText("LocalOnly/demo-signing-key.xml", rsa.ToXmlString(true)); }
            string publicKey;
            using (var rsa = RSA.Create()) { rsa.FromXmlString(File.ReadAllText("LocalOnly/demo-signing-key.xml")); publicKey = rsa.ToXmlString(false); }
            var config = File.Exists("Assets/Resources/game-config.json") ? JsonUtility.FromJson<GameConfig>(File.ReadAllText("Assets/Resources/game-config.json")) : new GameConfig();
            config.publicKey = publicKey;
            File.WriteAllText("Assets/Resources/game-config.json", JsonUtility.ToJson(config, true));
            if (!File.Exists("Assets/Game/Content/Orb.prefab"))
            {
                var material = new Material(Shader.Find("Standard")); material.color = new Color(0.1f, 0.85f, 0.72f);
                material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", new Color(0.03f, 0.5f, 0.38f));
                material.SetFloat("_Metallic", 0.35f); material.SetFloat("_Glossiness", 0.85f);
                AssetDatabase.CreateAsset(material, "Assets/Game/Content/Orb.mat");
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere); sphere.name = "EnergyOrb";
                sphere.GetComponent<Renderer>().sharedMaterial = material;
                PrefabUtility.SaveAsPrefabAsset(sphere, "Assets/Game/Content/Orb.prefab"); UnityEngine.Object.DestroyImmediate(sphere);
            }
            if (!File.Exists("Assets/Game/Content/OrbV2.prefab"))
            {
                var material = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Content/Orb.mat"));
                material.color = new Color(1f, 0.65f, 0.12f); material.SetColor("_EmissionColor", new Color(0.55f, 0.23f, 0.02f));
                AssetDatabase.CreateAsset(material, "Assets/Game/Content/OrbV2.mat");
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere); sphere.name = "EnergyOrbV2";
                sphere.GetComponent<Renderer>().sharedMaterial = material;
                PrefabUtility.SaveAsPrefabAsset(sphere, "Assets/Game/Content/OrbV2.prefab"); UnityEngine.Object.DestroyImmediate(sphere);
            }
            foreach (var path in Scenes)
            {
                if (File.Exists(path)) continue;
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (path.EndsWith("Boot.unity")) new GameObject("GameHost").AddComponent<Game.Bootstrap.Bootstrap>();
                EditorSceneManager.SaveScene(scene, path);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scenes[0], true) };
            foreach (string variant in new[] { "V1", "V2", "V3" })
            foreach (string name in new[] { "Lobby", "Arena" })
            {
                string directory = "Assets/Game/HotScenes/" + variant; Directory.CreateDirectory(directory);
                string path = directory + "/" + name + ".unity";
                if (File.Exists(path)) continue;
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var landmark = GameObject.CreatePrimitive(PrimitiveType.Cube); landmark.name = "SceneContent-" + variant;
                landmark.transform.position = new Vector3(0, 0, 4);
                landmark.transform.localScale = variant == "V3" ? new Vector3(6, 1.5f, 0.5f) : new Vector3(3, 0.5f, 0.5f);
                landmark.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Content/" + (variant == "V1" ? "Orb.mat" : "OrbV2.mat"));
                EditorSceneManager.SaveScene(scene, path);
            }
            if (!File.Exists("Assets/Game/Content/UiShell.prefab"))
            {
                var shell = new GameObject("UiShell", typeof(RectTransform)); shell.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(shell, "Assets/Game/Content/UiShell.prefab"); UnityEngine.Object.DestroyImmediate(shell);
            }
            if (!File.Exists("Assets/Game/Content/chime.wav")) WriteTone("Assets/Game/Content/chime.wav", 880, 0.15f);
            if (!File.Exists("Assets/Game/Content/music.wav")) WriteTone("Assets/Game/Content/music.wav", 220, 1f);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            PreparePackages(false);
            EditorSceneManager.OpenScene(Scenes[0]);
            Debug.Log("DEMO_PREPARED");
        }
        static void ConfigureHybrid()
        {
            var settings = HybridCLR.Settings.HybridCLRSettings.instance;
            settings.hotUpdateAssemblies = new[] { "Game.HotUpdate" };
            settings.hybridclrRepoURL = "https://github.com/focus-creative-games/hybridclr";
            settings.il2cppPlusRepoURL = "https://github.com/focus-creative-games/il2cpp_plus";
            HybridCLR.Settings.HybridCLRSettings.Save();
        }
        static void PreparePackages(bool il2cpp)
        {
            BuildContent("LocalOnly/Bundles", false);
            BuildContent("LocalOnly/BundlesV2", true);
            var settings = new ScriptCompilationSettings { target = Target, group = BuildTargetGroup.Standalone, options = ScriptCompilationOptions.None };
            Directory.CreateDirectory("LocalOnly/CompiledV1");
            PlayerBuildInterface.CompilePlayerScripts(settings, "LocalOnly/CompiledV1");
            StampClientBuild(il2cpp);
            Pack("1.0.0", 1, "LocalOnly/CompiledV1/Game.HotUpdate.dll", "Assets/StreamingAssets/Baseline", il2cpp);
            SnapshotAssemblies("LocalOnly/CompiledV1", "LocalOnly/FrozenV1");
            settings.extraScriptingDefines = new[] { "DEMO_V2" };
            Directory.CreateDirectory("LocalOnly/CompiledV2");
            PlayerBuildInterface.CompilePlayerScripts(settings, "LocalOnly/CompiledV2");
            Pack("1.1.0", 2, "LocalOnly/CompiledV2/Game.HotUpdate.dll", "Assets/StreamingAssets/DemoUpdate", il2cpp);
            SnapshotAssemblies("LocalOnly/CompiledV2", "LocalOnly/FrozenV2");
            AssetDatabase.Refresh();
        }
        static void SnapshotAssemblies(string source, string destination)
        {
            // Unity can replace the previous CompilePlayerScripts output on the next compilation.
            Directory.CreateDirectory(destination);
            foreach (string old in Directory.GetFiles(destination, "*.dll")) File.Delete(old);
            foreach (string file in Directory.GetFiles(source, "*.dll")) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        }
        public static ReleaseManifest BuildContent(string output, bool updated, string sceneVariant = null)
        {
            Directory.CreateDirectory(output);
            if (sceneVariant != null && sceneVariant != "V3") throw new ArgumentException("Unsupported scene variant");
            string sceneDirectory = "Assets/Game/HotScenes/" + (sceneVariant ?? (updated ? "V2" : "V1")) + "/";
            var bundles = new[] {
                new AssetBundleBuild { assetBundleName = "gamecontent", assetNames = new[] { updated ? "Assets/Game/Content/OrbV2.prefab" : "Assets/Game/Content/Orb.prefab" }, addressableNames = new[] { "orb" } },
                new AssetBundleBuild { assetBundleName = "shared", assetNames = new[] { updated ? "Assets/Game/Content/OrbV2.mat" : "Assets/Game/Content/Orb.mat" }, addressableNames = new[] { "orb-material" } },
                new AssetBundleBuild { assetBundleName = "config", assetNames = new[] { "Assets/Game/Content/demo-config.json" }, addressableNames = new[] { "demo-config" } },
                new AssetBundleBuild { assetBundleName = "ui", assetNames = new[] { "Assets/Game/Content/UiShell.prefab" }, addressableNames = new[] { "ui-shell" } },
                new AssetBundleBuild { assetBundleName = "audio", assetNames = new[] { "Assets/Game/Content/chime.wav", "Assets/Game/Content/music.wav" }, addressableNames = new[] { "chime", "music" } },
                new AssetBundleBuild { assetBundleName = "scene-lobby", assetNames = new[] { sceneDirectory + "Lobby.unity" } },
                new AssetBundleBuild { assetBundleName = "scene-arena", assetNames = new[] { sceneDirectory + "Arena.unity" } }
            };
            var manifest = BuildPipeline.BuildAssetBundles(output, bundles, BuildAssetBundleOptions.ChunkBasedCompression, Target);
            if (manifest == null) throw new BuildFailedException("AssetBundle build failed");
            var content = new ReleaseManifest { files = manifest.GetAllAssetBundles().Select(name => new ReleaseFile
                { name = name, kind = "bundle", dependencies = manifest.GetDirectDependencies(name) }).ToArray(),
                assets = bundles.SelectMany(bundle => bundle.addressableNames == null
                    ? bundle.assetNames.Select(path => new ResourceAddress { address = Path.GetFileNameWithoutExtension(path), bundle = bundle.assetBundleName, asset = path, kind = "scene" })
                    : bundle.addressableNames.Select(address => new ResourceAddress { address = address, bundle = bundle.assetBundleName, asset = address })).ToArray() };
            File.WriteAllText(Path.Combine(output, "content-index.json"), JsonUtility.ToJson(content));
            return content;
        }
        static void WriteTone(string path, float hz, float seconds)
        {
            int count = (int)(44100 * seconds);
            using (var file = new BinaryWriter(File.Create(path)))
            {
                file.Write(Encoding.ASCII.GetBytes("RIFF")); file.Write(36 + count * 2); file.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                file.Write(16); file.Write((short)1); file.Write((short)1); file.Write(44100); file.Write(88200); file.Write((short)2); file.Write((short)16);
                file.Write(Encoding.ASCII.GetBytes("data")); file.Write(count * 2);
                for (int i = 0; i < count; i++) file.Write((short)(Math.Sin(i * 2 * Math.PI * hz / 44100) * 4000 * (1f - i / (float)count)));
            }
        }
        static void StampClientBuild(bool il2cpp)
        {
            var config = JsonUtility.FromJson<GameConfig>(File.ReadAllText("Assets/Resources/game-config.json"));
            var paths = Directory.GetFiles("LocalOnly/CompiledV1", "*.dll").Where(p => Path.GetFileName(p) != "Game.HotUpdate.dll").OrderBy(p => p, StringComparer.Ordinal);
            string fingerprint = Application.unityVersion + "|" + config.publicKey + "|" + string.Join("|", paths.Select(p => Path.GetFileName(p) + ":" + ReleaseVerifier.Hash(p)));
            if (il2cpp && Directory.Exists(SettingsUtil.GeneratedCppDir))
                fingerprint += "|" + string.Join("|", Directory.GetFiles(SettingsUtil.GeneratedCppDir).OrderBy(p => p, StringComparer.Ordinal).Select(ReleaseVerifier.Hash));
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(fingerprint))).Replace("-", "").Substring(0, 16).ToLowerInvariant();
            config.clientBuild = "unity" + Application.unityVersion + "-win64-" + (il2cpp ? "il2cpp-" : "mono-") + hash;
            File.WriteAllText("Assets/Resources/game-config.json", JsonUtility.ToJson(config, true));
        }
        static void Pack(string version, int sequence, string dll, string output, bool il2cpp)
        {
            Directory.CreateDirectory(output);
            // Regeneration removes only files produced inside this exact package directory.
            foreach (string path in Directory.GetFiles(output).Where(p => !p.EndsWith(".meta"))) File.Delete(path);
            var files = new List<ReleaseFile>();
            void Add(string source, string name, string kind)
            {
                string destination = Path.Combine(output, name); File.Copy(source, destination, true);
                files.Add(new ReleaseFile { name = name, kind = kind, size = new FileInfo(destination).Length, sha256 = ReleaseVerifier.Hash(destination) });
            }
            if (il2cpp)
            {
                string stripped = SettingsUtil.GetAssembliesPostIl2CppStripDir(Target);
                if (!Directory.Exists(stripped)) throw new BuildFailedException("Generate/All must complete before packaging AOT metadata");
                foreach (string aot in Directory.GetFiles(stripped, "*.dll").Where(p => Path.GetFileName(p) != "Game.HotUpdate.dll")) Add(aot, "aot-" + Path.GetFileName(aot), "aot");
            }
            Add(dll, "Game.HotUpdate.dll", "assembly");
            string bundleDirectory = sequence == 1 ? "LocalOnly/Bundles" : "LocalOnly/BundlesV2";
            var content = JsonUtility.FromJson<ReleaseManifest>(File.ReadAllText(Path.Combine(bundleDirectory, "content-index.json")));
            foreach (var file in content.files)
            { Add(Path.Combine(bundleDirectory, file.name), file.name, "bundle"); files[files.Count - 1].dependencies = file.dependencies; }
            var config = JsonUtility.FromJson<GameConfig>(File.ReadAllText("Assets/Resources/game-config.json"));
            var release = new ReleaseManifest { version = version, sequence = sequence, resourceVersion = version, clientBuild = config.clientBuild,
                platform = config.platform, entryType = "Game.HotUpdate.GameEntry", files = files.ToArray(), assets = content.assets };
            byte[] payload = Encoding.UTF8.GetBytes(JsonUtility.ToJson(release));
            using (var rsa = RSA.Create())
            {
                rsa.FromXmlString(File.ReadAllText("LocalOnly/demo-signing-key.xml"));
                var envelope = new SignedManifest { payload = Convert.ToBase64String(payload), signature = Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) };
                File.WriteAllText(Path.Combine(output, "manifest.json"), JsonUtility.ToJson(envelope, true));
            }
        }
        [MenuItem("Game Framework/2 Build Windows Demo (Mono validation only)")]
        public static void BuildMono()
        {
            HybridCLR.Settings.HybridCLRSettings.instance.enable = false; HybridCLR.Settings.HybridCLRSettings.Save();
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
            try { Prepare(); Build("Builds/Mono/EnergyCollector.exe"); }
            finally { HybridCLR.Settings.HybridCLRSettings.instance.enable = true; HybridCLR.Settings.HybridCLRSettings.Save(); }
        }
        [MenuItem("Game Framework/3 Build Windows HybridCLR (IL2CPP)")]
        public static void BuildHybrid()
        {
            HybridCLR.Settings.HybridCLRSettings.instance.enable = true; HybridCLR.Settings.HybridCLRSettings.Save();
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.IL2CPP);
            Prepare(); ConfigureHybrid();
            var installer = new HybridCLR.Installer.InstallerController();
            if (!installer.HasInstalledHybridCLR()) installer.InstallDefaultHybridCLR();
            HybridCLR.Commands.PrebuildCommand.GenerateAll();
            PreparePackages(true);
            Build("Builds/HybridCLR/EnergyCollector.exe");
            ReleasePublisher.ExportBaseline();
        }
        static void Build(string output)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { Scenes[0] }, locationPathName = output, target = Target, options = BuildOptions.None });
            if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("Player build failed: " + report.summary.result);
            Debug.Log("DEMO_BUILD_SUCCEEDED " + Path.GetFullPath(output));
        }
        public static void PlaySmoke()
        {
            EditorSceneManager.OpenScene(Scenes[0]); EditorApplication.isPlaying = true;
        }
    }
    // Exclude the business DLL from the baseline Mono player as well, so Assembly.Load really supplies it.
    public sealed class DemoAssemblyFilter : IFilterBuildAssemblies
    {
        public int callbackOrder => 10;
        public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies) => assemblies.Where(p => Path.GetFileName(p) != "Game.HotUpdate.dll").ToArray();
    }
}
