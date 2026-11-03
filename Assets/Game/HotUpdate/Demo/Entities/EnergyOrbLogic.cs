using Game.Contracts;
using Game.Core;
using UnityEngine;

namespace Game.HotUpdate
{
    public sealed class OrbSpawnData
    {
        public DemoContext Context;
        public StateScope AudioOwner;
        public Vector3 Position;
        public string RunId;
        public bool Collectible;
    }
    public sealed class EnergyOrbLogic : EntityLogic<GameObject>, IEntityInteraction
    {
        public const int ScorePerOrb =
#if DEMO_V3
            3;
#elif DEMO_V2
            2;
#else
            1;
#endif
        OrbSpawnData data;
        bool collected;
        public override void OnShow(object userData)
        {
            data = (OrbSpawnData)userData; collected = false;
            Instance.transform.localScale = Vector3.one;
            Instance.transform.position = data.Position; Instance.transform.rotation = Quaternion.identity;
        }
        public override void OnUpdate(float deltaTime) => Instance.transform.Rotate(0, 35 * deltaTime, 0);
        public void OnInteract()
        {
            if (collected || !data.Collectible || !Entity.IsShown) return;
            collected = true;
            var id = Entity.Id;
            Entity.RequestHide();
            var audio = data.Context.Game.Audio; var owner = data.AudioOwner;
            _ = owner.Run(async token => { await audio.PlayAsync("chime", "Sfx", owner); });
            data.Context.Game.Log.Write(GameLogLevel.Debug, "entity", "orb.collected", "entity=" + id, data.RunId);
            data.Context.Game.Events.Publish(new EnergyCollected(id, ScorePerOrb, data.RunId));
        }
        public override void OnRecycle() { data = null; collected = false; }
    }
}
