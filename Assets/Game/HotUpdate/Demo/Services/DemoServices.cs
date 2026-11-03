using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Game.Contracts;
using Game.Core;
using Game.Runtime;
using UnityEngine;

namespace Game.HotUpdate
{
    // Business resources are owned by a hot-update module, not by the AOT service container.
    public sealed class DemoServices : IModule
    {
        readonly GameRuntime runtime;
        AssetLease<GameObject> orb;
        bool disposed;
        public string Id => "demo.gameplay";
        public IReadOnlyList<string> Dependencies { get; } = new[] { "entities", "ui", "audio", "scenes" };
        public DemoConfig Config { get; private set; }
        public SaveStore<SaveData> Saves { get; }
        public SaveData Save { get; private set; }
        public Material OrbMaterial => orb.Asset.GetComponent<Renderer>().sharedMaterial;
        public IScoreTransport Transport { get; }
        public ScoreClient Scores { get; }
        public DemoServices(GameRuntime runtime, IScoreTransport transport)
        {
            this.runtime = runtime; Transport = transport ?? throw new ArgumentNullException(nameof(transport));
            Scores = new ScoreClient(transport, runtime.Log);
            Saves = new SaveStore<SaveData>(Path.Combine(runtime.DataDirectory, "save.json"), new UnityJson(), runtime.Log,
                () => new SaveData(), SaveData.MigrateAndValidate);
        }
        public async Task InitializeAsync(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            using (var config = await runtime.Resources.LoadAsync<TextAsset>("demo-config", cancellation))
                Config = JsonUtility.FromJson<DemoConfig>(config.Asset.text);
            Config.Validate(); Save = Saves.Load(); ApplyAudio();
            runtime.Audio.AddGroup("Music", 2, 0.35f); runtime.Audio.AddGroup("UI", 8); runtime.Audio.AddGroup("Sfx", 16);
            orb = await runtime.Resources.LoadAsync<GameObject>("orb", cancellation);
            runtime.Entities.AddGroup("Energy", Config.poolCapacity);
            runtime.Entities.AddGroup("AttachmentProbe", 3, new EntityCachePolicy { maxCached = 0 });
        }
        public Task StartAsync(CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellation) => Task.CompletedTask;
        void ApplyAudio() { runtime.Audio.SetMaster(Save.volume, Save.mute); }
        public void SaveSettings(float volume, bool mute)
        {
            if (float.IsNaN(volume) || volume < 0 || volume > 1) throw new ArgumentOutOfRangeException(nameof(volume));
            Save.volume = volume; Save.mute = mute; ApplyAudio(); Saves.Save(Save);
        }
        public void RecordRun(int score)
        {
            if (score < 0) throw new ArgumentOutOfRangeException(nameof(score));
            Save.bestScore = Math.Max(Save.bestScore, score); Save.totalCollected = checked(Save.totalCollected + score);
            Save.completedRuns = checked(Save.completedRuns + 1); Saves.Save(Save);
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            orb?.Dispose();
        }
    }
}
