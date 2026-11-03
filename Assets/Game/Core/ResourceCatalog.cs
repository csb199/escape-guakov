using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Game.Core
{
    [Serializable] public sealed class ResourceAddress { public string address, bundle, asset; public string kind = "asset"; }
    public sealed class ResourceCatalog
    {
        readonly Dictionary<string, ReleaseFile> bundles;
        readonly Dictionary<string, ResourceAddress> assets;
        readonly Dictionary<string, string[]> orders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        public ResourceCatalog(ReleaseManifest release)
        {
            bundles = release.files.Where(f => f.kind == "bundle").ToDictionary(f => f.name, StringComparer.OrdinalIgnoreCase);
            if (bundles.Count == 0) throw new InvalidDataException("No resource bundles");
            assets = new Dictionary<string, ResourceAddress>(StringComparer.Ordinal);
            foreach (var asset in release.assets ?? Array.Empty<ResourceAddress>())
            {
                if (asset == null || string.IsNullOrWhiteSpace(asset.address) || asset.address.Length > 256 || string.IsNullOrWhiteSpace(asset.asset) ||
                    string.IsNullOrEmpty(asset.bundle) || !bundles.ContainsKey(asset.bundle) || assets.ContainsKey(asset.address))
                    throw new InvalidDataException("Invalid or duplicate resource address");
                string kind = asset.kind ?? "asset";
                if (kind != "asset" && kind != "scene") throw new InvalidDataException("Unknown resource address kind");
                if (kind == "scene" && (!asset.asset.StartsWith("Assets/", StringComparison.Ordinal) || !asset.asset.EndsWith(".unity", StringComparison.Ordinal) ||
                    asset.asset.Contains("..") || asset.asset.Contains("\\"))) throw new InvalidDataException("Invalid scene asset path");
                assets.Add(asset.address, new ResourceAddress { address = asset.address, asset = asset.asset, bundle = asset.bundle, kind = kind });
            }
            if (bundles.Count > 1 && assets.Count == 0) throw new InvalidDataException("Multiple bundles require an address catalog");
            foreach (string bundle in bundles.Keys) BuildOrder(bundle);
        }
        string[] BuildOrder(string root)
        {
            if (orders.TryGetValue(root, out var cached)) return cached;
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            void Visit(string name)
            {
                if (done.Contains(name)) return;
                if (!bundles.TryGetValue(name, out var file)) throw new InvalidDataException("Missing bundle dependency: " + name);
                if (!visiting.Add(name)) throw new InvalidDataException("Circular bundle dependency: " + name);
                foreach (string dependency in file.dependencies ?? throw new InvalidDataException("Missing bundle dependencies")) Visit(dependency);
                visiting.Remove(name); done.Add(name); result.Add(file.name);
            }
            Visit(root); return orders[root] = result.ToArray();
        }
        public ResourceAddress Resolve(string address)
        {
            if (assets.TryGetValue(address, out var asset)) return new ResourceAddress { address = asset.address, asset = asset.asset, bundle = asset.bundle, kind = asset.kind };
            if (assets.Count == 0 && bundles.Count == 1) return new ResourceAddress { address = address, asset = address, bundle = bundles.Keys.Single() };
            throw new InvalidDataException("Unknown resource address: " + address);
        }
        public string[] LoadOrder(string bundle) => (string[])orders[bundle].Clone();
    }
}
