using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.Contracts;
using Game.Core;
using UnityEngine;

namespace Game.Runtime
{
    public sealed class SoundHandle
    {
        internal readonly AudioService Service;
        internal readonly TaskCompletionSource<bool> loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task stopping;
        internal AudioSource source;
        internal AssetLease<AudioClip> lease;
        internal float volume;
        internal float fade = 1;
        internal bool playing;
        internal IDisposable ownership;
        public int Id { get; }
        public string Group { get; }
        public StateScope Scope { get; }
        public bool IsPlaying => playing && stopping == null && Scope.IsActive;
        public float EffectiveVolume => source == null ? 0 : source.volume;
        internal SoundHandle(AudioService service, int id, string group, StateScope scope, float volume)
        { Service = service; Id = id; Group = group; Scope = scope; this.volume = volume; }
        public Task StopAsync(float fadeSeconds = 0) { Scope.EnsureCanDispose(); return Service.StopSoundAsync(this, fadeSeconds); }
        public void RequestStop() => Service.RequestStop(this);
    }
    public sealed class AudioService : IModule
    {
        sealed class Group { public float Volume; public bool Mute; public int Capacity; }
        readonly ResourceService resources;
        readonly Transform root;
        readonly IGameLog log;
        readonly Dictionary<string, Group> groups = new Dictionary<string, Group>();
        readonly Dictionary<int, SoundHandle> voices = new Dictionary<int, SoundHandle>();
        AudioDriver driver;
        bool stopped;
        int serial;
        float masterVolume = 1;
        bool masterMute;
        Task stopTask;
        public string Id => "audio";
        public IReadOnlyList<string> Dependencies { get; } = new[] { "resources" };
        public int ActiveCount => voices.Values.Count(v => v.IsPlaying);
        public int PendingCount => voices.Values.Count(v => !v.loaded.Task.IsCompleted);
        public int InstanceCount => voices.Count;
        public float MasterVolume => masterVolume;
        public bool MasterMute => masterMute;
        public AudioService(ResourceService resources, Transform root, IGameLog log) { this.resources = resources; this.root = root; this.log = log; }
        public Task InitializeAsync(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var value = new GameObject("AudioService"); value.transform.SetParent(root); driver = value.AddComponent<AudioDriver>(); driver.Service = this;
            return Task.CompletedTask;
        }
        public Task StartAsync(CancellationToken cancellation) => Task.CompletedTask;
        public void AddGroup(string name, int capacity, float volume = 1)
        {
            ValidateVolume(volume);
            if (stopped || string.IsNullOrWhiteSpace(name) || capacity < 1) throw new ArgumentException("Invalid sound group");
            groups.Add(name, new Group { Capacity = capacity, Volume = volume });
        }
        static void ValidateVolume(float volume) { if (float.IsNaN(volume) || volume < 0 || volume > 1) throw new ArgumentOutOfRangeException(nameof(volume)); }
        public void SetMaster(float volume, bool mute) { ValidateVolume(volume); masterVolume = volume; masterMute = mute; RefreshVolumes(); }
        public void SetGroup(string name, float volume, bool mute) { ValidateVolume(volume); groups[name].Volume = volume; groups[name].Mute = mute; RefreshVolumes(); }
        public Task<SoundHandle> PlayAsync(string address, string group, StateScope owner, bool loop = false, float volume = 1)
        {
            ValidateVolume(volume);
            if (stopped || !owner.IsActive) throw new InvalidOperationException("Audio owner is closing");
            if (!groups.TryGetValue(group, out var settings)) throw new InvalidOperationException("Unknown audio group: " + group);
            if (voices.Values.Count(v => v.Group == group) >= settings.Capacity) throw new InvalidOperationException("Audio group capacity reached: " + group);
            var handle = new SoundHandle(this, checked(++serial), group, owner.CreateChild("audio/" + group), volume);
            voices.Add(handle.Id, handle); handle.ownership = owner.OwnAsync(() => handle.StopAsync());
            return PlayCoreAsync(handle, address, loop);
        }
        async Task<SoundHandle> PlayCoreAsync(SoundHandle sound, string address, bool loop)
        {
            bool success = false;
            try
            {
                sound.lease = await resources.LoadAsync<AudioClip>(address, sound.Scope.Token); sound.Scope.Token.ThrowIfCancellationRequested();
                var instance = new GameObject("Sound-" + sound.Id); instance.transform.SetParent(driver.transform);
                sound.source = instance.AddComponent<AudioSource>(); sound.source.playOnAwake = false;
                sound.source.clip = sound.lease.Asset; sound.source.loop = loop;
                ApplyVolume(sound); sound.source.Play(); sound.playing = true; success = true; return sound;
            }
            finally { sound.loaded.TrySetResult(true); if (!success) await StopSoundAsync(sound, 0); }
        }
        void ApplyVolume(SoundHandle sound)
        {
            var group = groups[sound.Group];
            if (sound.source != null) sound.source.volume = masterMute || group.Mute ? 0 : masterVolume * group.Volume * sound.volume * sound.fade;
        }
        void RefreshVolumes() { foreach (var voice in voices.Values) ApplyVolume(voice); }
        public bool TryGet(int id, out SoundHandle handle) => voices.TryGetValue(id, out handle);
        internal void Tick()
        {
            foreach (var voice in voices.Values.ToArray())
                if (!voice.Scope.IsActive || (voice.playing && !voice.source.loop && !voice.source.isPlaying)) RequestStop(voice);
        }
        internal Task StopSoundAsync(SoundHandle sound, float fadeSeconds)
        {
            if (sound.Service != this) throw new ArgumentException("Foreign sound handle");
            if (sound.stopping != null) return sound.stopping;
            if (float.IsNaN(fadeSeconds) || float.IsInfinity(fadeSeconds) || fadeSeconds < 0) throw new ArgumentOutOfRangeException(nameof(fadeSeconds));
            if (!sound.Scope.IsActive) fadeSeconds = 0;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            sound.stopping = completion.Task; sound.Scope.Cancel();
            StateScope.Drive(() => StopCoreAsync(sound, fadeSeconds, completion)); return completion.Task;
        }
        public void RequestStop(SoundHandle sound) { _ = Observe(StopSoundAsync(sound, 0)); }
        async Task Observe(Task task) { try { await task; } catch (Exception e) { log.Write(GameLogLevel.Error, "audio", "audio.stop.failed", e.Message, exception: e); } }
        async Task StopCoreAsync(SoundHandle sound, float seconds, TaskCompletionSource<bool> completion)
        {
            await Task.Yield(); Exception failure = null;
            try
            {
                await sound.loaded.Task; await sound.Scope.DisposeAsync();
                for (float elapsed = 0; elapsed < seconds && !stopped && sound.source != null; elapsed += Time.unscaledDeltaTime)
                { sound.fade = 1 - elapsed / seconds; ApplyVolume(sound); await Task.Yield(); }
            }
            catch (Exception e) { failure = e; }
            finally
            {
                if (sound.source != null) { sound.source.Stop(); sound.source.clip = null; UnityEngine.Object.Destroy(sound.source.gameObject); }
                sound.lease?.Dispose(); sound.lease = null; sound.playing = false; voices.Remove(sound.Id);
                sound.ownership.Dispose();
            }
            if (failure == null) completion.TrySetResult(true); else completion.TrySetException(failure);
        }
        public Task StopAsync(CancellationToken cancellation)
        {
            if (stopTask != null) return stopTask;
            stopped = true;
            foreach (var voice in voices.Values) voice.Scope.Cancel();
            stopTask = Task.WhenAll(voices.Values.ToArray().Select(v => StopSoundAsync(v, 0))); return stopTask;
        }
        public void Dispose()
        {
            if (voices.Count != 0) throw new InvalidOperationException("Audio must stop before disposal");
            if (driver != null) { driver.Service = null; UnityEngine.Object.Destroy(driver.gameObject); driver = null; }
        }
    }
    public sealed class AudioDriver : MonoBehaviour
    {
        internal AudioService Service;
        void Update() => Service?.Tick();
    }
}
