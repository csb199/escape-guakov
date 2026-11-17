using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace Game.HotUpdate.Barrack
{
    // Native event 3074/3091 contract. Histories belong to each camera and are released with the pipeline.
    sealed class BarrackNativeTemporal : IDisposable
    {
        public Vector2 CurrentJitter { get; private set; }
        public bool HistoryUsed { get; private set; }
        readonly Material material;
        readonly Mesh triangle;
        readonly Dictionary<int, History> histories=new Dictionary<int, History>();
        sealed class History
        {
            public RenderTexture color,luminance,depth,nextColor,nextLuminance;
            public Matrix4x4 vp,projection; public Vector3 position; public Quaternion rotation;
            public Vector2 jitter; public int frame; public bool valid, enabled, automaticProjection;
            public void Dispose() { foreach(var rt in new[]{color,luminance,depth,nextColor,nextLuminance}) if(rt!=null){rt.Release();UnityEngine.Object.DestroyImmediate(rt);} }
        }
        static readonly int Ext=Shader.PropertyToID("_BarrackTemporalExt"), Motion=Shader.PropertyToID("_BarrackTemporalMotion");
        public BarrackNativeTemporal()
        {
            var shader=Array.Find(Resources.FindObjectsOfTypeAll<Shader>(),s=>s.name=="Hidden/Universal Render Pipeline/TemporalAntiAliasing");
            if(shader==null||!shader.isSupported)throw new InvalidOperationException("Source temporal shader unavailable");
            material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};material.EnableKeyword("_TAA_MEDIUM_QUALITY");material.EnableKeyword("_MOTION_VECTOR");material.EnableKeyword("_MRT");material.SetFloat("_Sharpen",5);
            triangle=new Mesh{name="Source temporal fullscreen triangle",hideFlags=HideFlags.HideAndDontSave};
            triangle.vertices=new[]{new Vector3(-1,-1,0),new Vector3(-1,3,0),new Vector3(3,-1,0)};
            triangle.uv=new[]{new Vector2(0,1),new Vector2(0,-1),new Vector2(2,1)};// The native pass culls back faces; D3D render-target winding must face the camera.
            triangle.triangles=new[]{0,2,1};triangle.UploadMeshData(true);
        }
        static RenderTexture Create(int w,int h,RenderTextureFormat f,string name,int depthBits=0)
        {var r=new RenderTexture(w,h,depthBits,f,RenderTextureReadWrite.Linear){name=name,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};r.Create();return r;}
        static float Halton(int index,int radix){float result=0,f=1;while(index>0){f/=radix;result+=f*(index%radix);index/=radix;}return result;}
        public void Reset(Camera camera) { if(histories.TryGetValue(camera.GetInstanceID(),out var value)) { value.valid=false;value.frame=0; } }
        public void Prepare(CommandBuffer cmd,Camera camera,int w,int h,bool enabled)
        {
            int id=camera.GetInstanceID();if(!histories.TryGetValue(id,out var v))histories.Add(id,v=new History());
            if(v.color==null||v.color.width!=w||v.color.height!=h)
            {
                v.Dispose();v.color=Create(w,h,RenderTextureFormat.ARGBHalf,"Source TAA history",24);v.nextColor=Create(w,h,RenderTextureFormat.ARGBHalf,"Source TAA result",24);
                v.luminance=Create(w,h,RenderTextureFormat.RHalf,"Source TAA luminance");v.nextLuminance=Create(w,h,RenderTextureFormat.RHalf,"Source TAA next luminance");v.depth=Create(w,h,RenderTextureFormat.RHalf,"Source TAA depth");v.valid=false;
            }
            if(v.enabled!=enabled||Vector3.Distance(v.position,camera.transform.position)>.5f||Quaternion.Angle(v.rotation,camera.transform.rotation)>15||Mathf.Abs(v.projection.m00-camera.projectionMatrix.m00)>.01f)v.valid=false;
            v.enabled=enabled;v.projection=camera.projectionMatrix;v.automaticProjection=!camera.usePhysicalProperties && v.projection==Matrix4x4.Perspective(camera.fieldOfView,camera.aspect,camera.nearClipPlane,camera.farClipPlane);camera.nonJitteredProjectionMatrix=v.projection;camera.depthTextureMode|=DepthTextureMode.MotionVectors | DepthTextureMode.Depth;
            Vector2 previous=v.jitter;v.jitter=enabled?new Vector2(Halton(++v.frame%1024+1,2)-.5f,Halton(v.frame%1024+1,3)-.5f):Vector2.zero;
            CurrentJitter=v.jitter;
            var projection=v.projection;projection.m02+=v.jitter.x*2/w;projection.m12+=v.jitter.y*2/h;camera.projectionMatrix=projection;
            var vp=GL.GetGPUProjectionMatrix(projection,true)*camera.worldToCameraMatrix;
            cmd.SetGlobalMatrix("_PrevViewProjMatrix",v.valid?v.vp:GL.GetGPUProjectionMatrix(v.projection,true)*camera.worldToCameraMatrix);cmd.SetGlobalVector("_TAAOffsets",new Vector4(v.jitter.x,v.jitter.y,previous.x,previous.y));
            cmd.EnableShaderKeyword("_MOTION_VECTOR");
        }
        public void Resolve(CommandBuffer cmd,Camera camera,int w,int h,int source,int normals,int motion,int depth,int destination)
        {
            var v=histories[camera.GetInstanceID()];
            cmd.GetTemporaryRT(Ext,w,h,0,FilterMode.Point,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);cmd.Blit(normals,Ext);
            cmd.GetTemporaryRT(Motion,w,h,0,FilterMode.Point,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);cmd.Blit(motion,Motion);
            cmd.SetGlobalTexture("_CameraColorTexture",source);cmd.SetGlobalTexture("_CameraColorTextureExt",Ext);cmd.SetGlobalTexture("_CameraDepthTexture",depth);cmd.SetGlobalTexture("_CameraMotionVector",Motion);
            cmd.SetGlobalTexture("_HistoryBuffer",v.color);cmd.SetGlobalTexture("_HistoryLuminance",v.luminance);cmd.SetGlobalTexture("_HistoryDepthTexture",v.depth);
            cmd.SetGlobalVector("_TextureSize",new Vector4(w,h,1f/w,1f/h));
            var vp=GL.GetGPUProjectionMatrix(camera.projectionMatrix,true)*camera.worldToCameraMatrix;
            cmd.SetGlobalMatrix("_InvCameraViewProj",vp.inverse);
            cmd.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.identity);cmd.SetGlobalMatrix("unity_MatrixVP",Matrix4x4.identity);
            HistoryUsed=v.valid&&v.enabled;
            if(HistoryUsed)
            {
                // Unity 6 D3D11 can leave the preceding target bound for a depthless MRT.
                // Supply an owned, size-matched depth attachment on both ping-pong colors.
                // The native pass uses ZTest Always / ZWrite Off, so depth does not alter its result.
                cmd.SetRenderTarget(new RenderTargetIdentifier[]{v.nextColor,v.nextLuminance},v.nextColor);cmd.SetViewport(new Rect(0,0,w,h));cmd.DrawMesh(triangle,Matrix4x4.identity,material,0,0);
            }
            else {cmd.Blit(source,v.nextColor);cmd.SetRenderTarget(v.nextLuminance);cmd.ClearRenderTarget(false,true,Color.clear);}
            cmd.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.identity);cmd.SetGlobalMatrix("unity_MatrixVP",Matrix4x4.identity);
            cmd.SetRenderTarget(v.depth);cmd.SetViewport(new Rect(0,0,w,h));cmd.DrawMesh(triangle,Matrix4x4.identity,material,0,1);
            cmd.Blit(v.nextColor,destination);
            (v.color,v.nextColor)=(v.nextColor,v.color);(v.luminance,v.nextLuminance)=(v.nextLuminance,v.luminance);
            // Source vertex motion uses the previous unjittered VP; pixel pass adds current jitter.
            v.vp=GL.GetGPUProjectionMatrix(v.projection,true)*camera.worldToCameraMatrix;v.position=camera.transform.position;v.rotation=camera.transform.rotation;v.valid=v.enabled;
            if(v.automaticProjection)camera.ResetProjectionMatrix();else camera.projectionMatrix=v.projection;cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix,camera.projectionMatrix);
            cmd.ReleaseTemporaryRT(Ext);cmd.ReleaseTemporaryRT(Motion);cmd.DisableShaderKeyword("_MOTION_VECTOR");
        }
        public void Dispose(){foreach(var v in histories.Values)v.Dispose();histories.Clear();UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(triangle);}
    }
}
