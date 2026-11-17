using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.HotUpdate.Barrack;
using Game.Core;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Game.Editor.Barrack
{
    [InitializeOnLoad]
    public static class BarrackFullValidation
    {
        const string Key = "Barrack.FullValidation", Root = "LocalOnly/Barrack/live/";
        static double next;
        static int step;
        static Color32[] temporalReference;
        static Task closeTask;
        static Vector3 cameraBefore;
        static Color32[] baseline;
        static RenderTexture validationTarget;
        static readonly List<string> checks = new List<string>();
        static readonly BarrackAction[] actions = { BarrackAction.Stretch, BarrackAction.Inspect, BarrackAction.InspectWeapon, BarrackAction.Head, BarrackAction.Weapon, BarrackAction.Pocket, BarrackAction.Around, BarrackAction.Look };
        static BarrackFullValidation()
        {
            EditorApplication.update += Tick;
            Application.logMessageReceived += (message, stack, type) => {
                if (SessionState.GetBool(Key, false) && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)) SessionState.SetString(Key + ".error", message);
            };
        }
        public static void Run()
        {
            BarrackStageImporter.Prepare();
            BarrackTextureContract.Validate();
            SessionState.SetBool(Key, true); SessionState.SetInt(Key + ".round", 0); SessionState.SetString(Key + ".error", "");
            SessionState.SetString(Key + ".deadline", DateTime.UtcNow.AddMinutes(8).ToString("O"));
            EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
        }
        static void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add(name); }
        static void Tick()
        {
            if (!SessionState.GetBool(Key, false)) return;
            try
            {
                string error = SessionState.GetString(Key + ".error", "");
                if (error.Length > 0) throw new Exception(error);
                if (DateTime.UtcNow > DateTime.Parse(SessionState.GetString(Key + ".deadline", ""))) throw new TimeoutException("Full barrack validation timed out");
                int round = SessionState.GetInt(Key + ".round", 0);
                if (!EditorApplication.isPlaying)
                {
                    if (EditorApplication.isPlayingOrWillChangePlaymode || round == 0) return;
                    Check(SceneManager.sceneCount == 1, "native background scene unloaded");
                    Check(!AssetBundle.GetAllLoadedAssetBundles().Any(), "all native scene/shader/animation leases released");
                    if (round <= 2) { step = 0; next = 0; EditorApplication.isPlaying = true; return; }
                    SessionState.SetBool(Key, false);
                    File.WriteAllText(Root + "full-validation.txt", "PASS\n" + SessionState.GetString(Key + ".checks", "") + string.Join("\n", checks)); EditorApplication.Exit(0); return;
                }
                var host = UnityEngine.Object.FindFirstObjectByType<BarrackPreview>();
                File.WriteAllText(Root + "full-validation-progress.txt", "step=" + step + " round=" + round + " ready=" + host?.Ready + " failure=" + host?.Failure + " pipeline=" + RenderPipelineManager.currentPipeline?.GetType().Name);
                if (round == 2 && step == 0 && host != null)
                {
                    _ = host.EnsureInitializedAsync(); // Reproduce Start winning the race before framework ownership is attached.
                    var scope = new StateScope("Barrack.CancelDuringLoad", null); scope.OwnAsync(host.StopAsync);
                    _ = host.EnsureInitializedAsync(scope.Token); closeTask = scope.DisposeAsync(); step = 26; return;
                }
                if (step == 26)
                {
                    if (!closeTask.IsCompleted) return;
                    closeTask.GetAwaiter().GetResult();
                    Check(!host.Ready && host.Failure == null, "late owner cancellation stops an already-started load");
                    Check(!AssetBundle.GetAllLoadedAssetBundles().Any(), "cancelled initialization leaves no native bundle leases");
                    SessionState.SetString(Key + ".checks", SessionState.GetString(Key + ".checks", "") + string.Join("\n", checks) + "\n"); checks.Clear();
                    SessionState.SetInt(Key + ".round", 3); EditorApplication.isPlaying = false; return;
                }
                if (step == 25)
                {
                    if (!closeTask.IsCompleted) return;
                    closeTask.GetAwaiter().GetResult();
                    Check(!host.Ready && host.Interaction.Mode == BarrackInteractionMode.Closed, "StateScope cancels presentation and awaits shutdown");
                    Check(SceneManager.sceneCount == 1, "scope shutdown unloads native background before owner release");
                    Check(!AssetBundle.GetAllLoadedAssetBundles().Any(), "scope shutdown releases every native lease");
                    SessionState.SetString(Key + ".checks", SessionState.GetString(Key + ".checks", "") + string.Join("\n", checks) + "\n"); checks.Clear();
                    SessionState.SetInt(Key + ".round", round + 1); EditorApplication.isPlaying = false; return;
                }
                if (host == null || !host.Ready) return;

                if (validationTarget == null) { validationTarget = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGBHalf); validationTarget.Create(); }
                var request = new RenderPipeline.StandardRequest { destination = validationTarget };
                if (!RenderPipeline.SupportsRenderRequest(host.displayCamera, request)) throw new Exception("Pipeline does not support the validation render request");
                RenderPipeline.SubmitRenderRequest(host.displayCamera, request);
                var pipeline = RenderPipelineManager.currentPipeline as BarrackRenderPipeline;
                if (pipeline == null) return;
                if (EditorApplication.timeSinceStartup < next) return;
                if (step == 0)
                {
                    File.WriteAllLines(Root+"reflection-receivers.txt",UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(r=>r.sharedMaterials.Any(m=>m!=null&&m.shader.name.Contains("Rerlection"))).Select(r=>r.name+" enabled="+r.enabled+" active="+r.gameObject.activeInHierarchy+" layer="+r.gameObject.layer+" mask="+host.displayCamera.cullingMask+" materials="+string.Join(";",r.sharedMaterials.Select(m=>m.name+" q="+m.renderQueue+" "+string.Join(",",m.GetTexturePropertyNames().Select(n=>n+":"+m.GetTexture(n)))))));
                    Check(host.RendererCount == 5, "five source character renderers");
                    Check(host.View.ActionButtonCount == 8, "eight diagnostic action controls");
                    var sourceUi=host.GetComponent<BarrackSourceUi>();
                    Check(sourceUi!=null && sourceUi.SourceNodeCount==554 && sourceUi.ReturnButton!=null,"source UI hierarchy and original return control loaded");
                    Check(QualitySettings.activeColorSpace==ColorSpace.Linear,"Linear rendering and captured texture SRGB contract verified");
                    Check(host.stageDefinition.sourceUi.fonts.Length==2&&host.stageDefinition.sourceUi.capturedTextures.Length>=20,"source fonts and captured presentation assets bound");
                    Check(UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Any(r=>r.name=="Source reflection receiver"),"runtime reflection receiver restored from captured mesh");
                    Check(pipeline.UsesSourceFilm,"native Unreal Filmic and HDR grading variants active");
                    Check(host.CameraRig.ActivePreset=="ShotVirtualCamera","source default virtual camera preset active");
                    Capture(pipeline.SourceGradingLut,"source-grading-lut.png");
                    Check(SceneManager.GetActiveScene().path.Contains("04Aimo_Training_Platform"), "real source background scene active");
                    Check(LightmapSettings.lightmaps.Length > 0 && LightmapSettings.lightmaps[0].lightmapColor != null, "source baked lightmap present");
                    Check(LightmapSettings.lightProbes != null && LightmapSettings.lightProbes.count == 8, "eight source light probes present");
                    Check(RenderSettings.customReflectionTexture != null, "source HDR reflection cubemap present");
                    host.Interaction.Tick(10000); Check(host.Interaction.Mode == BarrackInteractionMode.Idle, "entrance reaches idle");
                    BarrackBrowsingValidation.Validate(host,Check);
                    step++; next = EditorApplication.timeSinceStartup + 2; return;
                }
                if (step >= 1 && step <= 16)
                {
                    int index = (step - 1) / 2;
                    if ((step & 1) == 1)
                    {
                        cameraBefore = host.displayCamera.transform.position;
                        host.View.InvokeAction(actions[index]); Check(host.Interaction.Action == actions[index] && host.Interaction.Mode == BarrackInteractionMode.Action, "UI activates " + actions[index]);
                        Check(!host.Interaction.Request(BarrackAction.Head) && !host.Interaction.BeginOrbit(), "busy action rejects overlapping input " + actions[index]);
                        step++; next = EditorApplication.timeSinceStartup + 0.6; return;
                    }
                    if (actions[index] == BarrackAction.Look)
                        Check(Vector3.Distance(cameraBefore, host.displayCamera.transform.position) > 0.05f, "focus camera moves " + actions[index]);
                    if (round == 0 && actions[index] == BarrackAction.Look) Capture(pipeline.LatestFrame(host.displayCamera), "face-focus.png");
                    host.View.InvokeReturn(); Check(host.Interaction.Mode == BarrackInteractionMode.Returning, "back button starts source ToIdle " + actions[index]);
                    host.Interaction.Tick(10000); Check(host.Interaction.Mode == BarrackInteractionMode.Idle, "return finishes " + actions[index]);
                    step++; next = EditorApplication.timeSinceStartup + 0.35; return;
                }
                if (step == 17)
                {
                    var head = host.displayCamera.WorldToScreenPoint(host.Bone("Head_M").position);
                    var surface = host.GetComponentInChildren<BarrackInputSurface>(); var pointer = new PointerEventData(EventSystem.current) { position = head, button = PointerEventData.InputButton.Left };
                    ExecuteEvents.Execute(surface.gameObject, pointer, ExecuteEvents.pointerDownHandler); ExecuteEvents.Execute(surface.gameObject, pointer, ExecuteEvents.pointerClickHandler);
                    Check(host.Interaction.Action == BarrackAction.Look && host.Interaction.Mode == BarrackInteractionMode.Action, "pointer ray hits moving head bone and starts Look");
                    host.Interaction.Return(); host.Interaction.Tick(10000);
                    cameraBefore = host.displayCamera.transform.position;
                    ExecuteEvents.Execute(surface.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                    pointer.delta = new Vector2(180, 25); ExecuteEvents.Execute(surface.gameObject, pointer, ExecuteEvents.beginDragHandler); ExecuteEvents.Execute(surface.gameObject, pointer, ExecuteEvents.dragHandler);
                    Check(host.Interaction.Mode == BarrackInteractionMode.Orbit, "viewport drag enters orbit");
                    step++; next = EditorApplication.timeSinceStartup + 0.5; return;
                }
                if (step == 18)
                {
                    Check(Vector3.Distance(cameraBefore, host.displayCamera.transform.position) > 0.05f, "orbit changes camera position");
                    Capture(pipeline.LatestFrame(host.displayCamera),"barrack-browse.png");
                    cameraBefore=host.displayCamera.transform.position;
                    var surface=host.GetComponentInChildren<BarrackInputSurface>();
                    ExecuteEvents.Execute(surface.gameObject,new PointerEventData(EventSystem.current){scrollDelta=Vector2.up},ExecuteEvents.scrollHandler);
                    step=29;next=EditorApplication.timeSinceStartup+.5;return;
                }
                if(step==29)
                {
                    Check(Vector3.Distance(cameraBefore,host.displayCamera.transform.position)>.05f,"wheel event zooms the already rotated view");
                    Capture(pipeline.LatestFrame(host.displayCamera),"barrack-browse-zoom.png");
                    host.GetComponent<BarrackSourceUi>().ReturnButton.onClick.Invoke();host.Interaction.Tick(10000);step=19;next=EditorApplication.timeSinceStartup+3;return;
                }
                if (step == 19)
                {
                    Capture(validationTarget, "barrack-ui.png");
                    pipeline.CaptureDiagnostics = true; host.CameraRig.Tick(10000,0); host.FreezeAnimation(true); host.View.SetVisible(false);
                    pipeline.TemporalEnabled = true; pipeline.FrozenTime = Time.time; step=27; next = EditorApplication.timeSinceStartup + 2; return;
                }
                if(step==27)
                {
                    File.AppendAllText(Root+"temporal-camera.txt","27 "+host.displayCamera.transform.position+" "+host.displayCamera.transform.rotation+" mode="+host.CameraRig.Mode+"\n");
                    temporalReference=Capture(pipeline.LatestFrame(host.displayCamera),"barrack-taa.png");
                    step=28;next=EditorApplication.timeSinceStartup+1;return;
                }
                if(step==28)
                {
                    File.AppendAllText(Root+"temporal-camera.txt","28 "+host.displayCamera.transform.position+" "+host.displayCamera.transform.rotation+" mode="+host.CameraRig.Mode+"\n");
                    var stable=Capture(pipeline.LatestFrame(host.displayCamera),"barrack-taa-later.png");
                    double drift=0;for(int i=0;i<stable.Length;i++)drift+=Math.Abs(stable[i].r-temporalReference[i].r)+Math.Abs(stable[i].g-temporalReference[i].g)+Math.Abs(stable[i].b-temporalReference[i].b);
                    drift/=stable.Length*3;Check(drift<3,"static temporal history remains stable; mean drift="+drift.ToString("F5"));
                    pipeline.TemporalEnabled=false;step=20;next=EditorApplication.timeSinceStartup+.5;return;
                }
                if (step == 20)
                {
                    if (round == 0) BarrackHairValidation.Validate(host, pipeline, Check);
                    File.WriteAllText(Root + "shadow-status.txt", pipeline.ShadowStatus);
                    if (pipeline.ShadowDiagnostic != null) Capture(pipeline.ShadowDiagnostic, "shadow-atlas.png");
                    baseline = Capture(pipeline.LatestFrame(host.displayCamera), "barrack-full.png");
                    Difference(baseline,temporalReference,"native HDR temporal resolve");
                    var face = host.displayCamera.WorldToViewportPoint(host.Bone("Head_M").position);
                    int fx = Mathf.Clamp((int)(face.x * 1280), 8, 1271), fy = Mathf.Clamp((int)(face.y * 720), 8, 711);
                    double faceLight = 0;
                    for (int y = fy - 8; y < fy + 8; y++) for (int x = fx - 8; x < fx + 8; x++) { var pixel = baseline[y * 1280 + x]; faceLight += Math.Max(pixel.r, Math.Max(pixel.g, pixel.b)); }
                    Check(faceLight / 256 > 25, "source face shading remains visible (no black silhouette)");
                    Check(baseline.Select(c => ((int)c.r << 16) | ((int)c.g << 8) | c.b).Distinct().Count() > 1000, "real render has nonuniform detailed pixels");
                    pipeline.ShadowsEnabled = false; step++; next = EditorApplication.timeSinceStartup + 0.4; return;
                }
                if (step == 21)
                {
                    Difference(baseline, Capture(pipeline.LatestFrame(host.displayCamera), "barrack-no-shadows.png"), "shadows"); pipeline.ShadowsEnabled = true; pipeline.AmbientOcclusionEnabled = false;
                    step++; next = EditorApplication.timeSinceStartup + 0.4; return;
                }
                if (step == 22)
                {
                    Difference(baseline, Capture(pipeline.LatestFrame(host.displayCamera), "barrack-no-ao.png"), "ambient occlusion"); pipeline.AmbientOcclusionEnabled = true; pipeline.ReflectionsEnabled = false;
                    step++; next = EditorApplication.timeSinceStartup + 0.4; return;
                }
                if (step == 23)
                {
                    Difference(baseline, Capture(pipeline.LatestFrame(host.displayCamera), "barrack-no-reflections.png"), "reflections"); pipeline.ReflectionsEnabled = true; pipeline.PostProcessingEnabled = false;
                    step++; next = EditorApplication.timeSinceStartup + 0.4; return;
                }
                if (step == 24)
                {
                    Difference(baseline, Capture(pipeline.LatestFrame(host.displayCamera), "barrack-no-post.png"), "post processing");
                    pipeline.PostProcessingEnabled = true; pipeline.FrozenTime = -1; host.FreezeAnimation(false); host.View.SetVisible(true);
                    var scope = new StateScope("Barrack.Editor.Validation", null); scope.OwnAsync(host.StopAsync);
                    closeTask = scope.DisposeAsync(); step = 25;
                }
            }
            catch (Exception e) { SessionState.SetBool(Key, false); File.WriteAllText(Root + "full-validation.txt", "FAIL " + e + "\n" + string.Join("\n", checks)); Debug.LogException(e); EditorApplication.Exit(1); }
        }
        static void Difference(Color32[] a, Color32[] b, string effect)
        {
            double difference = 0; for (int i = 0; i < a.Length; i++) difference += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b);
            difference /= a.Length * 3; Check(difference > 0.005, effect + " changes actual pixels; mean absolute difference=" + difference.ToString("F5"));
        }
        static Color32[] Capture(RenderTexture source, string name)
        {
            if (source == null) throw new Exception("No completed pipeline frame");
            var previous = RenderTexture.active; var target = RenderTexture.GetTemporary(1280, 720, 0, RenderTextureFormat.ARGB32); var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try { Graphics.Blit(source, target); RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply(); File.WriteAllBytes(Root + name, image.EncodeToPNG()); return image.GetPixels32(); }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(image); }
        }
    }
}
