using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.HotUpdate.Barrack
{
    // Retains the source's compressed animation data. Editor .anim serialization can discard that data.
    public sealed class BarrackAnimationLibrary : IDisposable
    {
        AssetBundle bundle;
        BarrackNativeBundleLease lease;
        readonly Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
        BarrackAnimationLibrary(AssetBundle bundle) { this.bundle = bundle; }
        public static async Task<BarrackAnimationLibrary> LoadAsync(byte[] bytes, CancellationToken cancellation)
        {
            var lease = await BarrackNativeBundleLease.LoadAsync(bytes, cancellation);
            try { return new BarrackAnimationLibrary(lease.Bundle) { lease = lease }; }
            catch { lease.Dispose(); throw; }
        }
        public AnimationClip Get(string name)
        {
            if (bundle == null) throw new ObjectDisposedException(nameof(BarrackAnimationLibrary));
            if (clips.TryGetValue(name, out var cached)) return cached;
            var source = bundle.LoadAllAssets<AnimationClip>().SingleOrDefault(c => c.name == name);
            if (source == null || source.empty || source.length <= 0) throw new InvalidOperationException("Animation is empty or missing: " + name);
            // Do not clone or rewrite imported compressed clips: doing so discards native curve data.
            // BarrackCharacterLogic disables Animator.fireEvents; host interaction logic owns callbacks.
            clips.Add(name, source); return source;
        }
        public void Dispose()
        {
            clips.Clear(); lease?.Dispose(); lease = null; bundle = null;
        }
    }
}
