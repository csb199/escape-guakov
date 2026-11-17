using System;
using System.Linq;
using Game.HotUpdate.Barrack;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Editor.Barrack
{
    public static class BarrackBrowsingValidation
    {
        public static void Validate(BarrackPreview host,Action<bool,string> check)
        {
            var configuration=JsonUtility.FromJson<BarrackCameraConfiguration>(host.stageDefinition.cameraConfiguration.text);
            var owner=new GameObject("Browsing validation owner");
            var cameraObject=new GameObject("Browsing validation camera");
            BarrackCameraRig rig=null;
            try
            {
                owner.transform.SetPositionAndRotation(new Vector3(7,2,-3),Quaternion.Euler(0,35,0));
                var anchor=new GameObject("CameraPoint").transform;anchor.SetParent(owner.transform,false);anchor.localPosition=new Vector3(0,1.2f,0);
                var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
                rig=new BarrackCameraRig(camera,owner.transform,anchor,_=>anchor,configuration);
                var start=camera.transform.position;var rotation=camera.transform.rotation;
                check(rig.Root.parent==owner.transform&&rig.Root.Find("ShotVirtualCamera")!=null,"browse rig belongs to presentation owner with source virtual camera child");
                check(Vector3.Distance(rig.Anchor,anchor.position)<.0001f,"browse pivot uses character CameraPoint in owner coordinates");
                rig.Change(BarrackAction.Idle,BarrackInteractionMode.Orbit);rig.Tick(.016f,0);
                check(Vector3.Distance(start,camera.transform.position)<.0001f&&Quaternion.Angle(rotation,camera.transform.rotation)<.01f,"entering browse preserves the complete calibrated pose without reframing");
                rig.Zoom(1);rig.Tick(1000,0);
                var movement=camera.transform.position-start;
                check(Vector3.Dot(movement,rotation*Vector3.forward)>.1f&&Vector3.Cross(movement,rotation*Vector3.forward).magnitude<.0001f,"wheel zoom follows the shot local forward axis without lateral drift");
                rig.Zoom(-1);rig.Tick(1000,0);
                check(Vector3.Distance(start,camera.transform.position)<.0001f,"opposite wheel steps restore the same pose");
                rig.Orbit(new Vector2(80,10));rig.Tick(1000,0);
                check(Mathf.Abs(Vector3.Distance(start,anchor.position)-Vector3.Distance(camera.transform.position,anchor.position))<.0001f,"drag rotates about source anchor with a stable radius");
                rig.Change(BarrackAction.Return,BarrackInteractionMode.Returning);rig.Tick(1000,0);
                check(Vector3.Distance(start,camera.transform.position)<.0001f&&Quaternion.Angle(rotation,camera.transform.rotation)<.01f,"return restores calibrated camera after rotation and zoom");
                rig.Change(BarrackAction.Head,BarrackInteractionMode.Action);rig.Tick(1000,0);
                check(rig.ActivePreset=="ShotVirtualCamera"&&Vector3.Distance(start,camera.transform.position)<.0001f,"head touch does not substitute the separate Look close-up camera");
                foreach(var character in host.characters)
                {
                    var contract=JsonUtility.FromJson<BarrackTouchConfiguration>(character.interactionContract.text);
                    check(contract.character==character.id&&contract.capsules.Length>0&&character.prefab.transform.Find(contract.cameraPoint)!=null,"source interaction contract and camera anchor bound: "+character.id);
                    foreach(var region in contract.capsules)
                        check(character.prefab.transform.Find(region.path)!=null&&region.radius>0&&region.height>=0,"source capsule retains a valid target and geometry: "+character.id+"/"+region.@event);
                }
                var groza=host.characters.First(character=>character.id=="GrozaSR01");
                var source=JsonUtility.FromJson<BarrackTouchConfiguration>(groza.interactionContract.text);var grozaClips=groza.ActionClips();
                check(source.Resolve(1011,grozaClips,.5f)==BarrackAction.Look,"source head region enters Look from the overview");
                check(source.Resolve(1011,grozaClips,.5f,true)==BarrackAction.Head,"source head region selects Head once focused, independent of row order");
                var reversed=new BarrackTouchConfiguration{actions=source.actions.Reverse().ToArray()};
                check(reversed.Resolve(1011,grozaClips,.5f)==BarrackAction.Look&&reversed.Resolve(1011,grozaClips,.5f,true)==BarrackAction.Head,"face routing does not depend on table row order or action id suffix");
                rig.Change(BarrackAction.Look,BarrackInteractionMode.Action,true);rig.Tick(1000,0);
                rig.Change(BarrackAction.Return,BarrackInteractionMode.Returning);rig.Tick(configuration.executor.LookAtToBaseTransitionTime*.8f,0);
                rig.Change(BarrackAction.Idle,BarrackInteractionMode.Idle);rig.Tick(configuration.executor.LookAtToBaseTransitionTime*.21f,0);
                check(Vector3.Distance(camera.transform.position,start)<.0001f,"ToIdle completion does not restart an unfinished camera return blend");
                check(source.Resolve(1510,grozaClips,.5f)==BarrackAction.Pocket&&source.Resolve(1511,grozaClips,.5f)==BarrackAction.Pocket,"both source leg events resolve to Groza Pocket");
                check(source.Resolve(1030,grozaClips,0)==BarrackAction.Stretch&&source.Resolve(1030,grozaClips,1)==BarrackAction.Around,"chest resolves its source action group rather than a nearby limb");
                var surface=host.GetComponentInChildren<BarrackInputSurface>();
                var pointer=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Right,delta=new Vector2(100,30)};
                surface.OnPointerDown(pointer);surface.OnBeginDrag(pointer);surface.OnDrag(pointer);
                check(host.Interaction.Mode==BarrackInteractionMode.Idle,"right button cannot start browsing");
                surface.OnScroll(new PointerEventData(EventSystem.current){scrollDelta=Vector2.zero});
                check(host.Interaction.Mode==BarrackInteractionMode.Idle,"zero wheel input does not claim camera ownership");
                pointer.button=PointerEventData.InputButton.Left;pointer.position=host.displayCamera.WorldToScreenPoint(host.Bone("Head_M").position);
                surface.OnPointerDown(pointer);surface.OnBeginDrag(pointer);surface.OnDrag(pointer);surface.OnEndDrag(pointer);surface.OnPointerClick(pointer);
                check(host.Interaction.Mode==BarrackInteractionMode.Orbit,"releasing a drag over a touch region does not click the character");
                host.Interaction.Return();host.Interaction.Tick(10000);host.CameraRig.Tick(10000,0);
            }
            finally
            {
                rig?.Dispose();UnityEngine.Object.Destroy(owner);UnityEngine.Object.Destroy(cameraObject);
            }
        }
    }
}
