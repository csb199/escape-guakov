using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.HotUpdate.Barrack
{
    // Compiled source shaders must retain their native bundle data; cloning them to .asset drops bytecode.
    // The caller obtains bytes through ResourceService and owns this library before creating entities.
    public sealed class BarrackShaderLibrary : IDisposable
    {
        AssetBundle bundle;
        readonly List<BarrackNativeBundleLease> leases = new List<BarrackNativeBundleLease>();
        readonly Dictionary<string, Shader> shaders;
        BarrackShaderLibrary(AssetBundle bundle)
        { this.bundle = bundle; shaders = bundle.LoadAllAssets<Shader>().ToDictionary(s => s.name, StringComparer.Ordinal); }
        public static Task<BarrackShaderLibrary> LoadAsync(byte[] bytes, CancellationToken cancellation)
            => LoadAsync(new[]{bytes}, cancellation);
        public static async Task<BarrackShaderLibrary> LoadAsync(IEnumerable<byte[]> libraries, CancellationToken cancellation)
        {
            BarrackShaderLibrary result = null;
            try
            {
                foreach(var bytes in libraries)
                {
                    var lease = await BarrackNativeBundleLease.LoadAsync(bytes, cancellation);
                    try
                    {
                        if(result == null) result = new BarrackShaderLibrary(lease.Bundle);
                        else foreach(var shader in lease.Bundle.LoadAllAssets<Shader>())
                        {
                            if(result.shaders.TryGetValue(shader.name,out var existing) && existing != shader)
                                throw new InvalidOperationException("Ambiguous native shader: " + shader.name);
                            result.shaders[shader.name] = shader;
                        }
                        result.leases.Add(lease);
                    }
                    catch { lease.Dispose(); throw; }
                }
                cancellation.ThrowIfCancellationRequested();
                return result ?? throw new InvalidOperationException("Native shader libraries required");
            }
            catch { result?.Dispose(); throw; }
        }
        [Serializable] sealed class Contract { public Item[] materials; }
        [Serializable] sealed class Item { public string name; public string[] keywords, disabledPasses; public int hostRenderQueue; }
        public IDisposable Bind(GameObject instance, TextAsset contractData = null)
        {
            if (bundle == null) throw new ObjectDisposedException(nameof(BarrackShaderLibrary));
            var contract = contractData != null ? JsonUtility.FromJson<Contract>(contractData.text) : null;
            var binding = new Binding();
            try
            {
                var copies = new Dictionary<Material, Material>();
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    var original = renderer.sharedMaterials;
                    var replacement = new Material[original.Length];
                    for (var i = 0; i < original.Length; i++)
                    {
                        var material = original[i];
                        if (material == null || material.shader == null || !shaders.TryGetValue(material.shader.name, out var shader))
                            throw new InvalidOperationException("Original shader binding is unresolved: " + renderer.name + "/" + (material == null ? "<null material>" : material.name + "/" + (material.shader == null ? "<null shader>" : material.shader.name)));
                        if (!copies.TryGetValue(material, out var copy))
                        {
                            copy = new Material(material) { shader = shader, name = material.name };
                            var entry = contract?.materials.FirstOrDefault(m => m.name == material.name);
                            if (entry != null) { copy.shaderKeywords = entry.keywords; foreach(var pass in entry.disabledPasses) copy.SetShaderPassEnabled(pass, false); }
                            if (entry != null && entry.hostRenderQueue > 0) copy.renderQueue = entry.hostRenderQueue;
                            copies.Add(material, copy); binding.Materials.Add(copy);
                        }
                        replacement[i] = copy;
                    }
                    binding.Renderers.Add((renderer, original)); renderer.sharedMaterials = replacement;
                }
                return binding;
            }
            catch { binding.Dispose(); throw; }
        }
        public void Dispose() { for(int i=leases.Count-1;i>=0;i--) leases[i].Dispose(); leases.Clear(); bundle = null; shaders.Clear(); }
        sealed class Binding : IDisposable
        {
            public readonly List<(Renderer renderer, Material[] original)> Renderers = new List<(Renderer, Material[])>();
            public readonly List<Material> Materials = new List<Material>();
            public void Dispose()
            {
                foreach (var item in Renderers) if (item.renderer != null) item.renderer.sharedMaterials = item.original;
                foreach (var material in Materials) if (material != null) Object.Destroy(material);
                Renderers.Clear(); Materials.Clear();
            }
        }
    }
}

