using System;
using UnityEngine;

namespace Game.HotUpdate.Barrack
{
    public enum BarrackCameraMode { Default, Head, Weapon, Pocket, Inspect, Around, Look, Orbit, Returning }
    public sealed class BarrackCameraRig : IDisposable
    {
        readonly Camera camera;
        readonly Func<string, Transform> bone;
        readonly BarrackCameraConfiguration configuration;
        readonly Vector3 anchor, defaultPosition;
        readonly Quaternion defaultRotation;
        readonly float defaultFov;
        readonly Transform shot;
        BarrackCameraConfiguration.Preset preset;
        Vector3 focus, focusVelocity, blendStart, orbitStart, orbitValues, orbitTarget, orbitVelocity;
        Quaternion blendRotation, orbitRotation;
        float blendFov, blendElapsed, blendDuration, orbitDepth;
        bool near;
        public Transform Root { get; }
        public Vector3 Anchor => Root.parent.TransformPoint(anchor);
        public BarrackCameraMode Mode { get; private set; }
        public string ActivePreset => preset.name;

        public BarrackCameraRig(Camera camera, Transform owner, Transform cameraPoint, Func<string, Transform> bone, BarrackCameraConfiguration configuration)
        {
            this.camera=camera;this.bone=bone;this.configuration=configuration;
            if(configuration.executor==null||cameraPoint==null)throw new InvalidOperationException("Source camera executor and CameraPoint are required");
            var initial=configuration.capturedDefault??configuration.Get("ShotVirtualCamera");
            configuration.Get("ShotVirtualCamera");configuration.Get("CharacterNearVirtualCamera");
            defaultPosition=initial.position;defaultRotation=initial.rotation;defaultFov=initial.fieldOfView;
            anchor=owner.InverseTransformPoint(cameraPoint.position);focus=cameraPoint.position;
            Root=new GameObject("VirtualCameraRoot").transform;Root.SetParent(owner,false);
            shot=new GameObject("ShotVirtualCamera").transform;shot.SetParent(Root,false);
            Change(BarrackAction.Idle,BarrackInteractionMode.Idle);Tick(configuration.blendTime,0);
        }

        public void Change(BarrackAction action, BarrackInteractionMode state,bool focused=false)
        {
            if(state==BarrackInteractionMode.Orbit)
            {
                if(Mode==BarrackCameraMode.Orbit)return;
                Mode=BarrackCameraMode.Orbit;Root.localPosition=Vector3.zero;Root.localRotation=Quaternion.identity;
                orbitStart=Root.InverseTransformPoint(camera.transform.position);
                orbitRotation=Quaternion.Inverse(Root.rotation)*camera.transform.rotation;
                orbitDepth=Vector3.Dot(anchor-orbitStart,orbitRotation*Vector3.forward);
                orbitValues=orbitTarget=orbitVelocity=Vector3.zero;
                return;
            }
            bool wasNear=near,wasOrbit=Mode==BarrackCameraMode.Orbit;
            Mode=action switch {
                BarrackAction.Head=>BarrackCameraMode.Head, BarrackAction.Look=>BarrackCameraMode.Look,
                BarrackAction.Weapon=>BarrackCameraMode.Weapon, BarrackAction.Pocket=>BarrackCameraMode.Pocket,
                BarrackAction.Inspect=>BarrackCameraMode.Inspect, BarrackAction.InspectWeapon=>BarrackCameraMode.Inspect,
                BarrackAction.Around=>BarrackCameraMode.Around, BarrackAction.Return=>BarrackCameraMode.Returning,
                _=>BarrackCameraMode.Default };
            near=Mode==BarrackCameraMode.Look||focused;
            bool initialized=preset!=null;
            preset=configuration.Get(near?"CharacterNearVirtualCamera":"ShotVirtualCamera");
            if(initialized&&!wasOrbit&&wasNear==near)return;
            if(near&&!wasNear){focus=FocusTarget();focusVelocity=Vector3.zero;}
            blendStart=camera.transform.position;blendRotation=camera.transform.rotation;blendFov=camera.fieldOfView;blendElapsed=0;
            blendDuration=near?configuration.executor.BaseToLookAtTransitionTime:wasNear?configuration.executor.LookAtToBaseTransitionTime:configuration.blendTime;
        }
        Vector3 FocusTarget()
        {var head=bone("Head_M");return head!=null?head.position+configuration.executor.nearCameraOffset+Vector3.up*configuration.executor.globalLookAtCameraYOffset:Anchor;}

        public void Orbit(Vector2 delta)
        {
            if(Mode!=BarrackCameraMode.Orbit)return;
            var executor=configuration.executor;
            orbitTarget.x=Mathf.Clamp(orbitTarget.x+delta.x*executor.RotateSpeed*executor.Sensity,executor.HorizontalRotateAngleRange.x,executor.HorizontalRotateAngleRange.y);
            orbitTarget.y=Mathf.Clamp(orbitTarget.y-delta.y*executor.RotateSpeed*executor.Sensity,executor.VerticalRotateAngleRange.x,executor.VerticalRotateAngleRange.y);
        }

        public void Zoom(float scroll)
        {
            if(Mode!=BarrackCameraMode.Orbit)return;
            var executor=configuration.executor;
            orbitTarget.z=orbitDepth-Mathf.Clamp(orbitDepth-orbitTarget.z-scroll*executor.Sensity,executor.NearestDistance,executor.FarthestDistance);
        }

        public void Tick(float deltaTime,float actionProgress)
        {
            var executor=configuration.executor;
            if(Mode==BarrackCameraMode.Orbit)
            {
                orbitValues=Vector3.SmoothDamp(orbitValues,orbitTarget,ref orbitVelocity,executor.smoothDampDuration,float.PositiveInfinity,deltaTime);
                var orbit=Quaternion.AngleAxis(orbitValues.x,Vector3.up)*Quaternion.AngleAxis(orbitValues.y,orbitRotation*Vector3.right);
                Root.localRotation=orbit;Root.localPosition=anchor-orbit*anchor;
                shot.localPosition=orbitStart+orbitRotation*Vector3.forward*orbitValues.z;shot.localRotation=orbitRotation;
                camera.transform.SetPositionAndRotation(shot.position,shot.rotation);
                return;
            }
            Root.localPosition=Vector3.zero;Root.localRotation=Quaternion.identity;
            shot.localPosition=defaultPosition;shot.localRotation=defaultRotation;
            if(near)
            {
                focus=Vector3.SmoothDamp(focus,FocusTarget(),ref focusVelocity,executor.LookAtFollowDuration,float.PositiveInfinity,deltaTime);
                shot.localRotation=defaultRotation*preset.rotation;
                shot.position=focus-shot.forward*preset.distance;
            }
            blendElapsed+=deltaTime;float amount=Mathf.Clamp01(blendElapsed/Mathf.Max(.001f,blendDuration));amount=amount*amount*(3-2*amount);
            camera.transform.SetPositionAndRotation(Vector3.Lerp(blendStart,shot.position,amount),Quaternion.Slerp(blendRotation,shot.rotation,amount));
            camera.fieldOfView=Mathf.Lerp(blendFov,near?preset.fieldOfView:defaultFov,amount);
        }

        public void Dispose(){if(Root!=null){Root.gameObject.SetActive(false);UnityEngine.Object.Destroy(Root.gameObject);}}
    }
}
