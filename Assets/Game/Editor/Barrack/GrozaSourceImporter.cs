using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Editor.Barrack
{
    public static class GrozaSourceImporter
    {
        static string Output;
        [Serializable] sealed class Binding { public string renderer, assetKey, bundlePath; }
        [Serializable] sealed class TextureBinding { public string property,assetKey,name,rawDataPath; public bool srgb; }
        [Serializable] sealed class MaterialBinding { public string name,keywords; public TextureBinding[] textures; }
        [Serializable] sealed class Plan { public string[] paths, animationNames, additionalShaderLibraryPaths; public string variant, sourceCharacterName, sourceTransformPath, sourceAssetName, shaderLibraryPath, animationLibraryPath; public Binding[] meshBindings; public MaterialBinding[] materialBindings; }
        [Serializable] sealed class Report
        {
            public string status = "source-assets-with-native-shader-library-rendering-unverified";
            public string model = "GrozaSR01";
            public int renderers, meshes, boneReferences, materials, textures, shaders, clips;
            public bool avatar;
            public string[] actions;
        }
        // Replaces persisted native mesh content without changing prefab references or asset GUIDs.
        public static void UpdateVerifiedMeshes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first");
            var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(Environment.GetEnvironmentVariable("GF2_IMPORT_PLAN")));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Content/BarrackSource/" + plan.variant + "/" + plan.variant + ".prefab");
            if (prefab == null) throw new InvalidDataException("Existing imported prefab required");
            foreach (var binding in plan.meshBindings)
            {
                var renderer = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == binding.renderer);
                var bundle = AssetBundle.LoadFromFile(binding.bundlePath);
                if (bundle == null) throw new InvalidDataException("Mesh bundle unavailable");
                try
                {
                    var source = bundle.LoadAsset<Mesh>(binding.assetKey);
                    if (source == null || source.name != renderer.name || source.bindposes.Length != renderer.bones.Length || !EditorUtility.IsPersistent(renderer.sharedMesh))
                        throw new InvalidDataException("Verified mesh update mismatch: " + renderer.name);
                    EditorUtility.CopySerialized(source, renderer.sharedMesh);
                    EditorUtility.SetDirty(renderer.sharedMesh);
                }
                finally { bundle.Unload(true); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("BARRACK_VERIFIED_MESH_UPDATE " + plan.variant);
        }
        // Extraction is run in an isolated project. Runtime assets must be rebuilt by the host publisher.
        public static void Import()
        {
            var planPath = Environment.GetEnvironmentVariable("GF2_IMPORT_PLAN");
            if (string.IsNullOrWhiteSpace(planPath)) throw new InvalidOperationException("Set GF2_IMPORT_PLAN");
            
            var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(planPath));
            plan.variant = string.IsNullOrEmpty(plan.variant) ? "GrozaSR01" : plan.variant;
            Output = "Assets/Game/Content/BarrackSource/" + plan.variant;
            if (Directory.Exists(Output)) throw new IOException("Refusing to overwrite recovered assets: " + Output);
            if (string.IsNullOrEmpty(plan.sourceAssetName) || plan.meshBindings == null) throw new InvalidDataException("Exact default-prefab and mesh binding plan required");
            var bundles = new List<AssetBundle>(); GameObject instance = null;
            var previousPipeline = Shader.globalRenderPipeline;
            var persisted = new Dictionary<Object, Object>(); var textureData = new Dictionary<Texture, string>(); var textureColor = new Dictionary<Texture, bool>(); var report = new Report();
            Shader.globalRenderPipeline = "UniversalPipeline";
            try
            {
                foreach (var path in plan.paths)
                {
                    var bundle = AssetBundle.LoadFromFile(path);
                    if (bundle == null) throw new InvalidDataException("Cannot load bundle: " + path);
                    bundles.Add(bundle);
                }
                var source = bundles.Single(b => b.GetAllAssetNames().Contains(plan.sourceAssetName)).LoadAsset<GameObject>(plan.sourceAssetName);
                if (!string.IsNullOrEmpty(plan.sourceTransformPath)) source = source.transform.Find(plan.sourceTransformPath).gameObject;
                else if (source.name != plan.variant) source = source.GetComponentsInChildren<Transform>(true).Single(t => t.name == plan.variant).gameObject;
                if(source.name!=plan.variant)throw new InvalidDataException("Source subtree identity differs: "+plan.variant);
                instance = Object.Instantiate(source); instance.name = plan.variant; instance.SetActive(false);
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); instance.transform.localScale = Vector3.one;
                report.model = plan.variant;
                foreach (var binding in plan.meshBindings)
                {
                    var renderer = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == binding.renderer);
                    var mesh = bundles.Single(b => b.GetAllAssetNames().Contains(binding.assetKey)).LoadAsset<Mesh>(binding.assetKey);
                    if (mesh == null || mesh.name != binding.renderer || mesh.bindposes.Length != renderer.bones.Length)
                        throw new InvalidDataException("Mesh/bone binding mismatch: " + binding.renderer);
                    renderer.sharedMesh = mesh;
                }
                foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                foreach (var c in instance.GetComponentsInChildren<Component>(true))
                    if (c != null && !(c is Transform) && !(c is SkinnedMeshRenderer) && !(c is MeshRenderer) && !(c is MeshFilter) && !(c is Animator))
                        Object.DestroyImmediate(c);
                // MeshResList import selects LOD0 for both skinned characters and rigid attachments.
                foreach (var lowerLod in instance.GetComponentsInChildren<Renderer>(true).Where(r => System.Text.RegularExpressions.Regex.IsMatch(r.name, @"_lod(?:1|2|m\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
                    Object.DestroyImmediate(lowerLod);
                foreach (var r in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (r.name.EndsWith("lod1", StringComparison.OrdinalIgnoreCase) || r.name.EndsWith("lod2", StringComparison.OrdinalIgnoreCase) || System.Text.RegularExpressions.Regex.IsMatch(r.name, @"_lodm\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    { Object.DestroyImmediate(r); continue; }
                    if (r.sharedMesh == null || r.bones.Any(b => b == null || !b.IsChildOf(instance.transform)))
                        throw new InvalidDataException("Mesh/bone ownership incomplete: " + r.name);
                    report.boneReferences += r.bones.Length;
                }
                var renderers = instance.GetComponentsInChildren<Renderer>(true);
                foreach (var material in renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct())
                {
                    var binding = plan.materialBindings.Single(b => b.name == material.name);
                    foreach (var texture in binding.textures)
                    {
                        var original = bundles.Single(b => b.GetAllAssetNames().Contains(texture.assetKey)).LoadAsset<Texture>(texture.assetKey);
                        if (original == null || original.name != texture.name) throw new InvalidDataException("Texture unresolved: " + texture.name);
                        material.SetTexture(texture.property, original); textureData[original] = texture.rawDataPath; textureColor[original] = texture.srgb;
                    }
                }
                foreach (var r in renderers)
                {
                    var mesh = r is SkinnedMeshRenderer skinned ? skinned.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null || r.sharedMaterials.Length < mesh.subMeshCount || r.sharedMaterials.Any(m => m == null || m.shader == null))
                        throw new InvalidDataException("Incomplete renderer: " + r.name);
                    if (r.sharedMaterials.Any(m => !m.shader.isSupported || (m.FindPass("GFCharForward") < 0 && !(m.shader.name == "gf_shader/scene/pbr_env_standard" && m.FindPass("ForwardLit") >= 0) && !(m.shader.name == "gf_shader/vfx/UV_Effect_UVAll_Fast_2" && m.passCount > 0) && !(m.shader.name == "gf_shader/pbr/character/ubertrans" && m.FindPass("GFCharTransBack") >= 0 && m.FindPass("GFCharTransFront") >= 0))))
                        throw new InvalidDataException("Original character shader pass unavailable: " + r.name + " " + string.Join("; ", r.sharedMaterials.Select(m => m.name + " shader=" + m.shader.name + " supported=" + m.shader.isSupported + " passes=" + string.Join(",", Enumerable.Range(0,m.passCount).Select(m.GetPassName)))));
                }
                Directory.CreateDirectory(Output + "/Assets");
                File.Copy(plan.shaderLibraryPath, Output + "/OriginalCharacterShaders.bytes", false);
                for(int i=0;i<(plan.additionalShaderLibraryPaths?.Length??0);i++)
                    File.Copy(plan.additionalShaderLibraryPaths[i],Output+"/OriginalCharacterShadersExtra"+i+".bytes",false);
                File.Copy(plan.animationLibraryPath, Output + "/OriginalBarrackAnimations.bytes", false);
                Object Persist(Object original)
                {
                    if (original == null) return null;
                    if (persisted.TryGetValue(original, out var existing)) return existing;
                    if (!(original is Material) && !(original is Texture) && !(original is Shader) && !(original is Mesh) && !(original is Avatar))
                        throw new NotSupportedException("Unmapped source asset: " + original.GetType().Name);
                    if (original is Shader sourceShader)
                    {
                        var reference = BarrackShaderReference.Save(sourceShader,
                            Output + "/Assets/" + (persisted.Count + 1).ToString("D4") + "_Shader.shader");
                        persisted.Add(original, reference); report.shaders++; return reference;
                    }
                    Object copy;
                    if (original is Texture2D texture)
                    {
                        if (!textureData.TryGetValue(texture, out var rawDataPath)) throw new InvalidDataException("Missing exact raw texture binding");
                        var restored = new Texture2D(texture.width, texture.height, texture.format, texture.mipmapCount,
                            !textureColor[texture]);
                        restored.LoadRawTextureData(File.ReadAllBytes(rawDataPath)); restored.Apply(false, false);
                        restored.filterMode = texture.filterMode; restored.anisoLevel = texture.anisoLevel;
                        restored.wrapModeU = texture.wrapModeU; restored.wrapModeV = texture.wrapModeV; restored.mipMapBias = texture.mipMapBias;
                        copy = restored;
                    }
                    else copy = Object.Instantiate(original);
                    copy.name = original.name;
                    persisted.Add(original, copy);
                    var assetPath = Output + "/Assets/" + persisted.Count.ToString("D4") + "_" + original.GetType().Name + ".asset";
                    if (copy is Material material)
                    {
                        material.shader = (Shader)Persist(((Material)original).shader); report.materials++;
                        var sourceMaterial = (Material)original;
                        foreach (var property in sourceMaterial.GetTexturePropertyNames())
                            if (sourceMaterial.GetTexture(property) != null) material.SetTexture(property, (Texture)Persist(sourceMaterial.GetTexture(property)));
                    }
                    else if (copy is Texture) report.textures++;
                    else if (copy is Shader) report.shaders++;
                    else if (copy is Mesh) report.meshes++;
                    AssetDatabase.CreateAsset(copy, assetPath);
                    copy.name = original.name; EditorUtility.SetDirty(copy);
                    return copy;
                }
                foreach (var renderer in renderers)
                {
                    if (renderer is SkinnedMeshRenderer skinned) skinned.sharedMesh = (Mesh)Persist(skinned.sharedMesh);
                    else { var filter = renderer.GetComponent<MeshFilter>(); filter.sharedMesh = (Mesh)Persist(filter.sharedMesh); }
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(m => (Material)Persist(m)).ToArray(); report.renderers++;
                }
                foreach (var animator in instance.GetComponentsInChildren<Animator>(true))
                {
                    animator.runtimeAnimatorController = null; animator.enabled = false;
                    if (animator.avatar != null) { animator.avatar = (Avatar)Persist(animator.avatar); report.avatar = true; }
                }
                var animationBundle = bundles[Array.FindIndex(plan.paths, p => Path.GetFullPath(p) == Path.GetFullPath(plan.animationLibraryPath))];
                var animations = animationBundle.LoadAllAssets<AnimationClip>()
                    .Where(c => plan.animationNames != null ? plan.animationNames.Contains(c.name) : c.name.StartsWith("c_" + plan.variant + "_") && (c.name.Contains("Barrack") || c.name.EndsWith("ToIdle")))
                    .GroupBy(c => c.name).Select(g => g.Single()).ToArray();
                if (plan.animationNames != null && animations.Length != plan.animationNames.Length)
                    throw new InvalidDataException("Missing planned character animations");
                report.actions = animations.Select(c => c.name).ToArray();
                foreach (var clip in animations)
                {
                    if (clip.empty || clip.length <= 0) throw new InvalidDataException("Empty source animation: " + clip.name);
                    report.clips++;
                }
                if (report.renderers == 0 || report.clips == 0) throw new InvalidDataException("Empty recovery");
                PrefabUtility.SaveAsPrefabAsset(instance, Output + "/" + plan.variant + ".prefab"); AssetDatabase.SaveAssets();
                File.WriteAllText(Output + "/import-report.json", JsonUtility.ToJson(report, true));
                Debug.Log("CHARACTER_SOURCE_IMPORT " + JsonUtility.ToJson(report));
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                foreach (var bundle in bundles) bundle.Unload(true);
                Shader.globalRenderPipeline = previousPipeline;
            }
        }
    }
}
