using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Core
{
    [Serializable] public sealed class EntityCachePolicy
    {
        public int maxCached = 32, maxReleasePerPass = 16;
        public double expireSeconds = 60, releaseIntervalSeconds = 5;
        internal EntityCachePolicy Snapshot()
        {
            if (maxCached < 0 || maxReleasePerPass < 1 || double.IsNaN(expireSeconds) || double.IsInfinity(expireSeconds) || expireSeconds < 0 ||
                double.IsNaN(releaseIntervalSeconds) || double.IsInfinity(releaseIntervalSeconds) || releaseIntervalSeconds <= 0)
                throw new ArgumentException("Invalid entity cache policy");
            return (EntityCachePolicy)MemberwiseClone();
        }
    }
    public sealed class EntityCacheInfo
    {
        public int Id { get; internal set; }
        public string Asset { get; internal set; }
        public Type LogicType { get; internal set; }
        public int Priority { get; internal set; }
        public bool Locked { get; internal set; }
        public double LastUse { get; internal set; }
    }
    public partial class EntityManager<T> where T : class
    {
        public void SetCacheProperties(EntityHandle<T> entity, int priority, bool locked)
        { RequireShown(entity); entity.Slot.Priority = priority; entity.Slot.Locked = locked; }
        public bool SetCachedProperties(string group, int cacheId, int priority, bool locked)
        {
            var slot = groups[group].Idle.Values.SelectMany(s => s).FirstOrDefault(s => s.CacheId == cacheId);
            if (slot == null) return false; slot.Priority = priority; slot.Locked = locked; return true;
        }
        public void SetCachePolicy(string name, EntityCachePolicy policy)
        {
            var group = groups[name]; group.Policy = policy.Snapshot(); group.NextRelease = clock() + group.Policy.releaseIntervalSeconds;
            TrimCache(name);
        }
        public EntityCacheInfo[] GetCachedInstances(string name) => groups[name].Idle.Values.SelectMany(s => s).Select(s =>
            new EntityCacheInfo { Id = s.CacheId, Asset = s.Key.Item1, LogicType = s.Key.Item2, Priority = s.Priority, Locked = s.Locked, LastUse = s.LastUse }).ToArray();
        public int MaintainCaches()
        {
            if (stopped) return 0;
            int released = 0; double now = clock();
            foreach (var pair in groups.ToArray())
                if (now >= pair.Value.NextRelease)
                { pair.Value.NextRelease = now + pair.Value.Policy.releaseIntervalSeconds; released += TrimCache(pair.Key); }
            return released;
        }
        public int ReleaseAllUnused()
        {
            int released = 0; foreach (string name in groups.Keys.ToArray()) released += TrimCache(name, true); return released;
        }
        public int TrimCache(string name, bool releaseAllUnused = false)
        {
            var group = groups[name]; if (group.Cached == 0) return 0;
            double now = clock(); var policy = group.Policy;
            var candidates = group.Idle.Values.SelectMany(s => s).Where(s => !s.Locked)
                .OrderBy(s => now - s.LastUse >= policy.expireSeconds ? 0 : 1).ThenBy(s => s.Priority).ThenBy(s => s.LastUse).ToArray();
            var selected = new HashSet<Slot>(); int limit = releaseAllUnused ? int.MaxValue : policy.maxReleasePerPass;
            foreach (var slot in candidates)
            {
                if (selected.Count >= limit) break;
                if (releaseAllUnused || now - slot.LastUse >= policy.expireSeconds || group.Cached - selected.Count > policy.maxCached) selected.Add(slot);
            }
            if (selected.Count == 0) return 0;
            // Remove from the pool before invoking user OnDestroy callbacks.
            foreach (var key in group.Idle.Keys.ToArray())
            {
                var stack = new Stack<Slot>(group.Idle[key].Reverse().Where(s => !selected.Contains(s)));
                if (stack.Count == 0) group.Idle.Remove(key); else group.Idle[key] = stack;
            }
            group.Cached -= selected.Count; var errors = new List<Exception>();
            foreach (var slot in selected) DestroySlot(slot, errors);
            if (selected.Count > 0) log.Write(Game.Contracts.GameLogLevel.Debug, "entity", "entity.cache.released", name + " / " + selected.Count);
            if (errors.Count > 0) throw new AggregateException("Entity cache release failed", errors);
            return selected.Count;
        }
    }
}
