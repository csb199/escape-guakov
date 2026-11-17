using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Game.HotUpdate.Barrack
{
    [Serializable] public sealed class BarrackTouchConfiguration
    {
        [Serializable] public sealed class Region
        {
            public string path,followBone;
            public int @event,direction;
            public Vector3 center;
            public float radius,height;
            public bool enabled;
        }
        [Serializable] public sealed class ActionEntry
        {
            public int id,@event,weight;
            public string clip,horizontal,vertical,zoom;
        }
        public string character,cameraPoint;
        public Region[] capsules;
        public ActionEntry[] actions;

        public BarrackAction Resolve(int sourceEvent,IReadOnlyDictionary<BarrackAction,string> clips,float sample,bool focused=false)
        {
            var exact=actions.Where(entry=>entry.@event==sourceEvent).ToArray();
            var entries=exact.Length>0?exact:actions.Where(entry=>entry.@event==sourceEvent/10).ToArray();
            var available=entries.Where(entry=>entry.weight>0 && clips.Values.Contains(entry.clip)).ToArray();
            if(sourceEvent==1011&&clips.TryGetValue(BarrackAction.Look,out var look))
            {
                if(!focused&&available.Any(entry=>entry.clip==look))return BarrackAction.Look;
                if(focused)available=available.Where(entry=>entry.clip!=look).ToArray();
            }
            float selected=Mathf.Clamp01(sample)*available.Sum(entry=>entry.weight);
            foreach(var entry in available)
            {
                selected-=entry.weight;
                if(selected<=0)return clips.First(pair=>pair.Value==entry.clip).Key;
            }
            return BarrackAction.Idle;
        }
    }

    sealed class BarrackTouchTargets
    {
        readonly BarrackTouchConfiguration configuration;
        readonly List<(CapsuleCollider collider,int sourceEvent)> targets=new List<(CapsuleCollider,int)>();
        public Transform CameraPoint { get; }
        public BarrackTouchTargets(GameObject character,TextAsset contract,Func<string,Transform> bone)
        {
            if(contract==null)throw new InvalidOperationException("Source interaction contract is missing: "+character.name);
            configuration=JsonUtility.FromJson<BarrackTouchConfiguration>(contract.text);
            CameraPoint=character.transform.Find(configuration.cameraPoint);
            if(CameraPoint==null)throw new InvalidOperationException("Source CameraPoint is missing: "+character.name);
            foreach(var region in configuration.capsules)
            {
                var parent=string.IsNullOrEmpty(region.followBone)?character.transform.Find(region.path):bone(region.followBone);
                if(parent==null)throw new InvalidOperationException("Source touch target is missing: "+region.path);
                var target=new GameObject("Barrack touch "+region.@event);target.transform.SetParent(parent,false);
                var collider=target.AddComponent<CapsuleCollider>();collider.center=region.center;collider.radius=region.radius;collider.height=region.height;collider.direction=region.direction;collider.isTrigger=true;collider.enabled=region.enabled;
                targets.Add((collider,region.@event));
            }
        }
        public BarrackAction Hit(Ray ray,IReadOnlyDictionary<BarrackAction,string> clips,bool focused=false)
        {
            Physics.SyncTransforms();
            float closest=float.PositiveInfinity;int sourceEvent=0;
            foreach(var target in targets)
                if(target.collider.enabled && target.collider.gameObject.activeInHierarchy && target.collider.Raycast(ray,out var hit,closest))
                {closest=hit.distance;sourceEvent=target.sourceEvent;}
            return sourceEvent==0?BarrackAction.Idle:configuration.Resolve(sourceEvent,clips,UnityEngine.Random.value,focused);
        }
    }
}
