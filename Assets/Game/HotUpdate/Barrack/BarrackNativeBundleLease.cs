using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Game.HotUpdate.Barrack
{
    // Main-thread leases shared by source scene, shader and animation libraries.
    public sealed class BarrackNativeBundleLease : IDisposable
    {
        sealed class Entry { public Task<AssetBundle> loading; public int references; }
        static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
        readonly string key;
        Entry entry;
        public AssetBundle Bundle { get; private set; }
        BarrackNativeBundleLease(string key, Entry entry, AssetBundle bundle) { this.key = key; this.entry = entry; Bundle = bundle; }
        public static async Task<BarrackNativeBundleLease> LoadAsync(byte[] bytes, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            string key;
            using (var hash = SHA256.Create()) key = Convert.ToBase64String(hash.ComputeHash(bytes));
            if (!entries.TryGetValue(key, out var entry)) { entry = new Entry { loading = Load(bytes) }; entries.Add(key, entry); }
            entry.references++;
            try
            {
                var bundle = await entry.loading;
                cancellation.ThrowIfCancellationRequested();
                return new BarrackNativeBundleLease(key, entry, bundle);
            }
            catch { Release(key, entry); throw; }
        }
        static async Task<AssetBundle> Load(byte[] bytes)
        {
            var request = AssetBundle.LoadFromMemoryAsync(bytes);
            if (!request.isDone)
            {
                var completion = new TaskCompletionSource<bool>();
                request.completed += _ => completion.TrySetResult(true);
                await completion.Task;
            }
            return request.assetBundle != null ? request.assetBundle : throw new InvalidOperationException("Native resource bundle load failed");
        }
        static void Release(string key, Entry entry)
        {
            if (--entry.references != 0) return;
            entries.Remove(key);
            if (entry.loading.Status == TaskStatus.RanToCompletion && entry.loading.Result != null) entry.loading.Result.Unload(true);
        }
        public void Dispose() { if (entry == null) return; Release(key, entry); entry = null; Bundle = null; }
    }
}
