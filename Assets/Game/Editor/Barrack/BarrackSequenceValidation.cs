using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Game.HotUpdate.Barrack;
namespace Game.Editor.Barrack
{
    // Each sample is a distinct Unity frame. The reference camera never writes the tested camera's history.
    [InitializeOnLoad] public static class BarrackSequenceValidation
    {
        const string Key="Barrack.SequenceValidation", Root="LocalOnly/Barrack/continuous/";
        static int frame,lastUnityFrame=-1;
        static readonly int SampleStride=1000;
        static Camera reference;
        static RenderTexture target,referenceTarget;
        static StreamWriter frames,vertices,buffers;
        static Vector3 origin;static Quaternion rotation;static Vector3 center;
        static Vector3[][] previousVertices;
        static Matrix4x4 previousVP;
        static Matrix4x4[] previousSkinMatrices;
        static Vector2 previousJitter;
        static Mesh[] baked;static SkinnedMeshRenderer[] skins;
        static Texture2D readback;
        static string action;
        static Color[] previousReference,previousDepth;
        static bool initialized, awaitingFrame;
        static string currentScenario;static float currentAnimationTime,currentYaw,currentPitch,currentDistance;
        static BarrackPreview activeHost;
        static readonly List<float> errors=new List<float>();
        static BarrackSequenceValidation(){EditorApplication.update+=Tick;RenderPipelineManager.endCameraRendering+=FrameRendered;}
        public static void Run()
        {
            Directory.CreateDirectory(Root);File.WriteAllText(Root+"status.txt","RUNNING "+DateTime.Now.ToString("O"));File.WriteAllText(Root+"camera-poses.csv","");BarrackStageImporter.Prepare();BarrackTextureContract.Validate();
            SessionState.SetBool(Key,true);SessionState.SetString(Key+".deadline",DateTime.UtcNow.AddMinutes(18).ToString("O"));
            EditorApplication.delayCall+=()=>EditorApplication.isPlaying=true;
        }
        static string F(float x)=>x.ToString("R",CultureInfo.InvariantCulture);
        static string Matrix(Matrix4x4 m)=>string.Join(";",Enumerable.Range(0,16).Select(i=>F(m[i])));
        static Color[] Read(RenderTexture rt)
        {
            if(readback==null||readback.width!=rt.width||readback.height!=rt.height){if(readback!=null)UnityEngine.Object.DestroyImmediate(readback);readback=new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true);}
            var old=RenderTexture.active;RenderTexture.active=rt;readback.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);readback.Apply();RenderTexture.active=old;return readback.GetPixels();
        }
        static void Save(RenderTexture rt,string name)
        {Read(rt);File.WriteAllBytes(Root+name+".exr",readback.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));}
        static Vector3 Project(Matrix4x4 vp,Vector3 p,int w,int h)
        {var q=vp*new Vector4(p.x,p.y,p.z,1);return new Vector3((q.x/q.w*.5f+.5f)*w,(.5f-q.y/q.w*.5f)*h,q.z/q.w);}
        static float Quantile(List<float> list,float q){if(list.Count==0)return float.NaN;list.Sort();return list[Mathf.Min(list.Count-1,(int)(list.Count*q))];}
        static void Tick()
        {
            if(!SessionState.GetBool(Key,false))return;
            try
            {
                if(DateTime.UtcNow>DateTime.Parse(SessionState.GetString(Key+".deadline","")))throw new TimeoutException("Sequence validation timeout");
                if(!EditorApplication.isPlaying)return;
                var host=UnityEngine.Object.FindFirstObjectByType<BarrackPreview>();if(host==null||!host.Ready)return;
                if(awaitingFrame||Time.frameCount==lastUnityFrame)return;lastUnityFrame=Time.frameCount;
                var pipeline=RenderPipelineManager.currentPipeline as BarrackRenderPipeline;
                if(pipeline==null)
                {
                    var bootstrap=RenderTexture.GetTemporary(640,360,24,RenderTextureFormat.ARGBHalf);
                    try{RenderPipeline.SubmitRenderRequest(host.displayCamera,new RenderPipeline.StandardRequest{destination=bootstrap});}
                    finally{RenderTexture.ReleaseTemporary(bootstrap);}return;
                }
                if(!initialized)
                {
                    host.Interaction.Tick(10000);host.CameraRig.Tick(10000,0);host.FreezeAnimation(true);host.View.SetVisible(false);host.displayCamera.enabled=false;
                    origin=host.displayCamera.transform.position;rotation=host.displayCamera.transform.rotation;center=new Vector3(0,.8f,0);
                    reference=new GameObject("Sequence current-frame reference").AddComponent<Camera>();reference.CopyFrom(host.displayCamera);reference.enabled=false;
                    target=new RenderTexture(640,360,24,RenderTextureFormat.ARGBHalf);target.Create();referenceTarget=new RenderTexture(640,360,24,RenderTextureFormat.ARGBHalf);referenceTarget.Create();
                    skins=UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None).Where(r=>r.sharedMesh!=null&&r.gameObject.activeInHierarchy).OrderBy(r=>r.name).ToArray();
                    foreach(var skin in skins)skin.vertexBufferTarget|=GraphicsBuffer.Target.Raw;
                    baked=skins.Select(r=>new Mesh()).ToArray();previousVertices=new Vector3[skins.Length][];previousSkinMatrices=new Matrix4x4[skins.Length];
                    frames=new StreamWriter(Root+"frames.csv");frames.WriteLine("sample,unityFrame,scenario,animationTime,historyUsed,vertexSamples,motionMedianPx,motionP95Px,colorMAE,characterMAE,hairMAE,nonFinite,motionNonzero,shadowMatrix,reflectionMatrix,cameraMatrix,jitterX,jitterY,trailCoefficient,trailMAE,changedPixels,disoccludedMAE,disoccludedPixels");
                    buffers=new StreamWriter(Root+"skinning-buffers.csv");buffers.WriteLine("sample,renderer,currentStride,previousStride,samples,currentPositionP95World,previousPositionP95World,previousPositionP95Px");
                    vertices=new StreamWriter(Root+"vertices.csv");vertices.WriteLine("sample,renderer,vertex,x,y,expectedX,expectedY,actualX,actualY,errorPx,depthResidual");
                    File.WriteAllText(Root+"renderers.json","["+string.Join(",",skins.Select(r=>JsonUtility.ToJson(new RendererInfo{name=r.name,vertices=r.sharedMesh.vertexCount,skinnedMotionVectors=r.skinnedMotionVectors,motionVectorMode=r.motionVectorGenerationMode.ToString()})))+"]");
                    Time.captureDeltaTime=1f/60;activeHost=host;host.displayCamera.enabled=true;host.displayCamera.targetTexture=target;initialized=true;
                }
                if(frame>=1684)
                {
                    frames.Dispose();vertices.Dispose();buffers.Dispose();SessionState.SetBool(Key,false);
                    File.WriteAllText(Root+"status.txt","COLLECTED 1684 distinct Unity frames; numerical acceptance requires analysis. "+DateTime.Now.ToString("O"));
                    EditorApplication.Exit(0);return;
                }
                string scenario=frame<32?"static":frame<96?"rapid_pan":frame<128?"settle":frame<224?"skin_stretch":frame<1668?"orbit_sweep":"camera_cut";
                string clip=scenario=="skin_stretch"?BarrackInteraction.Clips[BarrackAction.Stretch]:BarrackInteraction.Clips[BarrackAction.Idle];
                if(action!=clip){host.PlayAction(clip);host.SampleValidationPose(0);action=clip;if(scenario=="skin_stretch")host.FreezeAnimation(false);}
                float animationTime=scenario=="skin_stretch"?(frame-128)/60f:0;
                if(scenario!="skin_stretch")host.SampleValidationPose(0);else animationTime=(float)host.ValidationPoseTime;
                float yaw=scenario=="rapid_pan"?-64+(frame-32)*2:scenario=="settle"?62:scenario=="orbit_sweep"?-360+(frame-224)%361*2:scenario=="camera_cut"?(frame%2==0?0:70):0;
                float pitch=scenario=="orbit_sweep"?((frame-224)/361%2==0?-12:18.5f):0;
                float distance=scenario=="orbit_sweep"?((frame-224)/722==0?1:10):(origin-center).magnitude;
                var orbit=Quaternion.Euler(pitch,yaw,0);host.displayCamera.transform.SetPositionAndRotation(center+orbit*(origin-center).normalized*distance,orbit*rotation);
                host.displayCamera.ResetProjectionMatrix();
                pipeline.CaptureDiagnostics=true;pipeline.FrozenTime=10;pipeline.TemporalEnabled=true;

                currentScenario=scenario;currentAnimationTime=animationTime;currentYaw=yaw;currentPitch=pitch;currentDistance=distance;awaitingFrame=true;
            }
            catch(Exception e){Fail(e);}
        }
        static void Fail(Exception e)
        {frames?.Dispose();vertices?.Dispose();buffers?.Dispose();SessionState.SetBool(Key,false);File.WriteAllText(Root+"status.txt","FAIL "+e);Debug.LogException(e);EditorApplication.Exit(1);}
        static void FrameRendered(ScriptableRenderContext context,Camera camera)
        {
            if(!SessionState.GetBool(Key,false)||!awaitingFrame||activeHost==null||camera!=activeHost.displayCamera)return;
            try
            {
                var host=activeHost;var pipeline=(BarrackRenderPipeline)RenderPipelineManager.currentPipeline;
                string scenario=currentScenario;float animationTime=currentAnimationTime,yaw=currentYaw,pitch=currentPitch,distance=currentDistance;
                int w=pipeline.MotionDiagnostic.width,h=pipeline.MotionDiagnostic.height;
                var motion=Read(pipeline.MotionDiagnostic);var depth=Read(pipeline.DepthDiagnostic);var output=Read(pipeline.LatestFrame(host.displayCamera));
                var vp=pipeline.CameraMatrix;var jitter=pipeline.TemporalJitter;bool history=pipeline.TemporalHistoryUsed;
                string matrices=Matrix(pipeline.MainShadowMatrix)+","+Matrix(pipeline.ReflectionMatrix)+","+Matrix(vp);
                errors.Clear();int nonFinite=0,moving=0;foreach(var c in motion){if(float.IsNaN(c.r)||float.IsInfinity(c.r)||float.IsNaN(c.g)||float.IsInfinity(c.g))nonFinite++;if(Mathf.Abs(c.r)+Mathf.Abs(c.g)>.01f)moving++;}
                for(int r=0;r<skins.Length;r++)
                {
                    skins[r].BakeMesh(baked[r]);var points=baked[r].vertices;var world=new Vector3[points.Length];
                    for(int v=0;v<points.Length;v++)world[v]=skins[r].transform.TransformPoint(points[v]);
                    if(previousVertices[r]!=null&&frame>0)
                    for(int v=0;v<world.Length;v+=Mathf.Max(1,world.Length/SampleStride))
                    {
                        var pixel=Project(vp,world[v],w,h);int x=(int)pixel.x,y=(int)pixel.y;if(x<2||x>=w-2||y<2||y>=h-2)continue;
                        // ReadPixels is bottom-up; source MRT's pixel motion is D3D top-down.
                        int index=(h-1-y)*w+x;float residual=Mathf.Abs(depth[index].r-pixel.z);if(residual>.00015f||pixel.z<=0||pixel.z>=1)continue;
                        var old=Project(previousVP,previousVertices[r][v],w,h);
                        Vector2 expected=new Vector2(pixel.x-old.x+jitter.x-previousJitter.x,pixel.y-old.y+jitter.y-previousJitter.y);
                        var actual=new Vector2(motion[index].r,motion[index].g);float error=Vector2.Distance(expected,actual);errors.Add(error);
                        vertices.WriteLine(frame+","+skins[r].name+","+v+","+F(pixel.x)+","+F(pixel.y)+","+F(expected.x)+","+F(expected.y)+","+F(actual.x)+","+F(actual.y)+","+F(error)+","+F(residual));
                    }
                    if(scenario=="skin_stretch"&&previousVertices[r]!=null)
                    {
                        using(var now=skins[r].GetVertexBuffer())using(var old=skins[r].GetPreviousVertexBuffer())
                        {
                            if(now!=null&&old!=null)
                            {
                                var nr=AsyncGPUReadback.Request(now);var pr=AsyncGPUReadback.Request(old);nr.WaitForCompletion();pr.WaitForCompletion();
                                if(nr.hasError||pr.hasError)throw new InvalidOperationException("Skinning buffer readback failed");
                                var n=nr.GetData<float>();var p=pr.GetData<float>();
                                if(frame==160){File.WriteAllBytes(Root+skins[r].name+"-current-buffer.bin",nr.GetData<byte>().ToArray());File.WriteAllBytes(Root+skins[r].name+"-previous-buffer.bin",pr.GetData<byte>().ToArray());File.WriteAllLines(Root+skins[r].name+"-baked.csv",points.Select(v=>F(v.x)+","+F(v.y)+","+F(v.z)));}int ns=now.stride/4,ps=old.stride/4;
                                var ce=new List<float>();var pe=new List<float>();var pixelErrors=new List<float>();
                                if(ns>=3&&ps>=3)for(int v=0;v<world.Length&&v*ns+2<n.Length&&v*ps+2<p.Length;v+=Mathf.Max(1,world.Length/SampleStride))
                                {
                                    var currentPosition=(skins[r].rootBone!=null?skins[r].rootBone.localToWorldMatrix:skins[r].localToWorldMatrix).MultiplyPoint3x4(new Vector3(n[v*ns],n[v*ns+1],n[v*ns+2]));
                                    var previousPosition=previousSkinMatrices[r].MultiplyPoint3x4(new Vector3(p[v*ps],p[v*ps+1],p[v*ps+2]));
                                    ce.Add(Vector3.Distance(currentPosition,world[v]));pe.Add(Vector3.Distance(previousPosition,previousVertices[r][v]));
                                    var a=Project(previousVP,previousPosition,w,h);var b=Project(previousVP,previousVertices[r][v],w,h);pixelErrors.Add(Vector2.Distance(a,b));
                                }
                                buffers.WriteLine(frame+","+skins[r].name+","+now.stride+","+old.stride+","+ce.Count+","+F(Quantile(ce,.95f))+","+F(Quantile(pe,.95f))+","+F(Quantile(pixelErrors,.95f)));buffers.Flush();
                            }
                        }
                    }
                    previousVertices[r]=world;previousSkinMatrices[r]=skins[r].rootBone!=null?skins[r].rootBone.localToWorldMatrix:skins[r].localToWorldMatrix;
                }
                previousVP=vp;previousJitter=jitter;
                float median=Quantile(errors,.5f),p95=Quantile(errors,.95f);
                var referencePose=host.displayCamera.transform.position;var referenceRotation=host.displayCamera.transform.rotation;
                File.AppendAllText(Root+"camera-poses.csv",frame+","+F(referencePose.x)+","+F(referencePose.y)+","+F(referencePose.z)+","+F(referenceRotation.x)+","+F(referenceRotation.y)+","+F(referenceRotation.z)+","+F(referenceRotation.w)+","+F(yaw)+","+F(pitch)+","+F(distance)+"\n");
                if(frame%64==0||frame==95||frame==127||frame>=1668)
                {
                    Save(pipeline.LatestFrame(host.displayCamera),frame.ToString("D3")+"-taa");Save(pipeline.InputDiagnostic,frame.ToString("D3")+"-input");Save(pipeline.MotionDiagnostic,frame.ToString("D3")+"-motion");Save(pipeline.DepthDiagnostic,frame.ToString("D3")+"-depth");
                    Save(pipeline.ReflectionDiagnostic,frame.ToString("D3")+"-reflection");
                    if(pipeline.ShadowDiagnostic!=null&&(frame==0||frame==224||frame==384))Save(pipeline.ShadowDiagnostic,frame.ToString("D3")+"-shadow");
                }
                reference.CopyFrom(host.displayCamera);reference.enabled=false;reference.transform.SetPositionAndRotation(host.displayCamera.transform.position,host.displayCamera.transform.rotation);reference.ResetProjectionMatrix();
                pipeline.TemporalEnabled=false;pipeline.CaptureDiagnostics=false;
                RenderPipeline.SubmitRenderRequest(reference,new RenderPipeline.StandardRequest{destination=referenceTarget});
                var current=Read(pipeline.LatestFrame(reference));double mae=0,character=0,hair=0;int charCount=0,hairCount=0;
                var head=host.displayCamera.WorldToViewportPoint(host.Bone("Head_M").position);
                for(int i=0;i<output.Length;i++)
                {
                    float e=(Mathf.Abs(output[i].r-current[i].r)+Mathf.Abs(output[i].g-current[i].g)+Mathf.Abs(output[i].b-current[i].b))/3;
                    if(float.IsNaN(e)||float.IsInfinity(e)){nonFinite++;continue;}mae+=e;
                    float x=(i%w)/(float)w,y=(i/w)/(float)h;
                    if(Mathf.Abs(x-head.x)<.15f&&y<head.y+.08f&&y>head.y-.7f){character+=e;charCount++;}
                    if(Mathf.Abs(x-head.x)<.12f&&Mathf.Abs(y-head.y)<.14f){hair+=e;hairCount++;}
                }
                double trailDot=0,trailNorm=0,trailMae=0,uncoveredMae=0;int changed=0,uncovered=0;
                if(previousReference!=null)
                for(int i=0;i<output.Length;i++)
                {
                    Vector3 residual=new Vector3(output[i].r-current[i].r,output[i].g-current[i].g,output[i].b-current[i].b);
                    Vector3 old=new Vector3(previousReference[i].r-current[i].r,previousReference[i].g-current[i].g,previousReference[i].b-current[i].b);
                    float e=(Mathf.Abs(residual.x)+Mathf.Abs(residual.y)+Mathf.Abs(residual.z))/3;
                    if(old.sqrMagnitude>.0009f){trailDot+=Vector3.Dot(residual,old);trailNorm+=old.sqrMagnitude;trailMae+=e;changed++;}
                    if(previousDepth[i].r-depth[i].r>.002f){uncoveredMae+=e;uncovered++;}
                }
                previousReference=current;previousDepth=depth;
                frames.WriteLine(frame+","+Time.frameCount+","+scenario+","+F(animationTime)+","+history+","+errors.Count+","+F(median)+","+F(p95)+","+mae/output.Length+","+character/Math.Max(1,charCount)+","+hair/Math.Max(1,hairCount)+","+nonFinite+","+moving+","+matrices+","+F(jitter.x)+","+F(jitter.y)+","+trailDot/Math.Max(1e-10,trailNorm)+","+trailMae/Math.Max(1,changed)+","+changed+","+uncoveredMae/Math.Max(1,uncovered)+","+uncovered);frames.Flush();vertices.Flush();
                File.WriteAllText(Root+"progress.txt","frame="+frame+" scenario="+scenario+" motionP95="+p95+" history="+history);frame++;awaitingFrame=false;pipeline.TemporalEnabled=true;pipeline.CaptureDiagnostics=true;
            }
            catch(Exception e){frames?.Dispose();vertices?.Dispose();buffers?.Dispose();SessionState.SetBool(Key,false);File.WriteAllText(Root+"status.txt","FAIL "+e);Debug.LogException(e);EditorApplication.Exit(1);}
        }
        [Serializable] sealed class RendererInfo{public string name,motionVectorMode;public int vertices;public bool skinnedMotionVectors;}
    }
}
