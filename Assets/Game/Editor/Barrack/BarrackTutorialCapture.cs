using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Game.HotUpdate.Barrack;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Editor.Barrack
{
    [InitializeOnLoad]
    public static class BarrackTutorialCapture
    {
        const string Key = "Barrack.TutorialCapture";
        const string Output = "LocalOnly/Barrack/TutorialCaptureV3/";
        static bool running;
        static readonly List<string> captured = new List<string>();
        static readonly List<string> errors = new List<string>();

        static BarrackTutorialCapture()
        {
            EditorApplication.update += Tick;
            Application.logMessageReceived += (message, trace, type) =>
            {
                if (SessionState.GetBool(Key, false) && (type == LogType.Exception || type == LogType.Error)) errors.Add(message);
            };
        }

        public static void Run()
        {
            Directory.CreateDirectory(Output);
            EditorSceneManager.OpenScene(BarrackStartup.ScenePath);
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || running) return;
            var host = UnityEngine.Object.FindFirstObjectByType<BarrackPreview>();
            if (host == null || !host.Ready) return;
            if (!(RenderPipelineManager.currentPipeline is BarrackRenderPipeline))
            {
                var warmup = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGBHalf);
                try { RenderPipeline.SubmitRenderRequest(host.displayCamera, new RenderPipeline.StandardRequest { destination = warmup }); }
                finally { RenderTexture.ReleaseTemporary(warmup); }
                return;
            }
            running = true;
            CaptureAll(host);
        }

        static async void CaptureAll(BarrackPreview host)
        {
            RenderTexture target = null;
            Camera camera = host.displayCamera;
            var position = camera.transform.position;
            var rotation = camera.transform.rotation;
            float fieldOfView = camera.fieldOfView;
            var pipeline = (BarrackRenderPipeline)RenderPipelineManager.currentPipeline;
            bool temporal = pipeline.TemporalEnabled;
            bool shadows = pipeline.ShadowsEnabled;
            bool occlusion = pipeline.AmbientOcclusionEnabled;
            bool reflections = pipeline.ReflectionsEnabled;
            bool post = pipeline.PostProcessingEnabled;
            int exitCode = 0;
            try
            {
                if (host.CharacterId != "GrozaSR01")
                {
                    int index = Array.FindIndex(host.characters, character => character.id == "GrozaSR01");
                    if (index < 0) throw new InvalidOperationException("GrozaSR01 missing");
                    await host.SelectCharacterAsync(index);
                }
                host.Interaction.Tick(1000);
                host.CameraRig.Tick(1000, 0);
                host.PlayAction(host.characters[host.SelectedCharacter].ActionClips()[BarrackAction.Idle]);
                host.SampleValidationPose(1);
                host.View.SetVisible(false);
                foreach (var light in host.GetComponentsInChildren<BarrackCharacterLighting>(true)) light.Refresh();
                position = camera.transform.position;
                rotation = camera.transform.rotation;
                fieldOfView = camera.fieldOfView;
                target = new RenderTexture(3840, 2160, 24, RenderTextureFormat.ARGBHalf) { name = "Tutorial 4K real camera capture" };
                target.Create();
                pipeline.TemporalEnabled = false;
                pipeline.FrozenTime = 1;
                await Task.Delay(500);
                Save(camera, target, "full");
                pipeline.ShadowsEnabled = false;
                Save(camera, target, "no-shadow");
                pipeline.ShadowsEnabled = shadows;
                pipeline.AmbientOcclusionEnabled = false;
                Save(camera, target, "no-ao");
                pipeline.AmbientOcclusionEnabled = occlusion;
                pipeline.ReflectionsEnabled = false;
                Save(camera, target, "no-reflection");
                pipeline.ReflectionsEnabled = reflections;
                pipeline.PostProcessingEnabled = false;
                Save(camera, target, "no-post");
                pipeline.PostProcessingEnabled = post;
                pipeline.TemporalEnabled = true;
                pipeline.ResetCharacterHistory(camera);
                for (int frame = 0; frame < 12; frame++) RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                Save(camera, target, "taa-on");
                pipeline.TemporalEnabled = false;
                pipeline.ResetCharacterHistory(camera);
                var head = host.Bone("Head_M");
                camera.fieldOfView = 30;
                camera.transform.SetPositionAndRotation(head.position - rotation * Vector3.forward * 0.95f, rotation);
                Save(camera, target, "face");
                var materials = host.GetComponentsInChildren<Renderer>(true).SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null).Distinct().ToArray();
                foreach (var pass in new[] { "GFHairShadow", "GFCharFaceShadow", "GFCharHairTransE", "GFOutline" })
                {
                    var affected = materials.Where(material => material.FindPass(pass) >= 0 && material.GetShaderPassEnabled(pass)).ToArray();
                    if (affected.Length == 0) throw new InvalidOperationException("No material for " + pass);
                    try
                    {
                        foreach (var material in affected) material.SetShaderPassEnabled(pass, false);
                        Save(camera, target, "face-without-" + pass);
                    }
                    finally { foreach (var material in affected) material.SetShaderPassEnabled(pass, true); }
                }
                string[] stages = { "GFHairShadow", "GFCharFaceShadow", "GFCharFaceSign", "GFCharHairTransE", "GFOutline" };
                var changes = new List<(Material material, string pass)>();
                try
                {
                    foreach (var pass in stages)
                        foreach (var material in materials.Where(material => material.FindPass(pass) >= 0 && material.GetShaderPassEnabled(pass)))
                        {
                            changes.Add((material, pass));
                            material.SetShaderPassEnabled(pass, false);
                        }
                    Save(camera, target, "face-stage-0-body");
                    for (int stage = 0; stage < stages.Length; stage++)
                    {
                        foreach (var change in changes.Where(change => change.pass == stages[stage])) change.material.SetShaderPassEnabled(change.pass, true);
                        Save(camera, target, "face-stage-" + (stage + 1) + "-" + stages[stage]);
                    }
                }
                finally { foreach (var change in changes) change.material.SetShaderPassEnabled(change.pass, true); }
                camera.transform.RotateAround(head.position, Vector3.up, 30);
                Save(camera, target, "face-angle30");
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fieldOfView;
                foreach (var path in new[] { "Assets/Game/Content/BarrackSource/GrozaSR01/Assets/0004_Texture2D.asset", "Assets/Game/Content/BarrackSource/GrozaSR01/Assets/0005_Texture2D.asset", "Assets/Game/Content/BarrackSource/GrozaSR01/Assets/0006_Texture2D.asset", "Assets/Game/Content/BarrackSource/GrozaSR01/Assets/0007_Texture2D.asset" })
                {
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (texture == null) throw new FileNotFoundException(path);
                    var preview = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
                    try { Graphics.Blit(texture, preview); Write(preview, "texture-" + texture.name); }
                    finally { RenderTexture.ReleaseTemporary(preview); }
                }
                if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors.Distinct()));
                File.WriteAllText(Output + "capture-result.txt", "PASS\n" + DateTimeOffset.Now.ToString("O") + "\nCharacter=GrozaSR01\nCameraOutput=3840x2160\nIdlePoseSeconds=1\nTAA=off except taa-on\n" + string.Join("\n", captured));
            }
            catch (Exception exception)
            {
                exitCode = 1;
                File.WriteAllText(Output + "capture-result.txt", "FAIL\n" + exception);
                Debug.LogException(exception);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fieldOfView;
                pipeline.TemporalEnabled = temporal;
                pipeline.ShadowsEnabled = shadows;
                pipeline.AmbientOcclusionEnabled = occlusion;
                pipeline.ReflectionsEnabled = reflections;
                pipeline.PostProcessingEnabled = post;
                pipeline.FrozenTime = -1;
                pipeline.ResetCharacterHistory(camera);
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                try { await host.StopAsync(); }
                catch (Exception exception) { exitCode = 1; File.AppendAllText(Output + "capture-result.txt", "\nSTOP FAILED\n" + exception); }
                SessionState.SetBool(Key, false);
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
            }
        }

        static void Save(Camera camera, RenderTexture target, string name)
        {
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            Write(target, name);
        }

        static void Write(RenderTexture source, string name)
        {
            var previous = RenderTexture.active;
            var display = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            var image = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
            try
            {
                Graphics.Blit(source, display);
                RenderTexture.active = display;
                image.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                image.Apply();
                File.WriteAllBytes(Output + name + ".png", image.EncodeToPNG());
                captured.Add(name + "=" + source.width + "x" + source.height);
                File.WriteAllText(Output + "progress.txt", string.Join("\n", captured));
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(display);
                UnityEngine.Object.DestroyImmediate(image);
            }
        }
    }
}

