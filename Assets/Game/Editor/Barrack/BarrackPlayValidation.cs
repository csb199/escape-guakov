using System;
using System.IO;
using System.Linq;
using Game.HotUpdate.Barrack;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Editor.Barrack
{
    [InitializeOnLoad]
    public static class BarrackPlayValidation
    {
        const string Key = "Barrack.PlayValidation";
        static double readyAt;
        static int action;
        static BarrackPlayValidation()
        {
            EditorApplication.update += Tick;
            Application.logMessageReceived += (message, stack, type) => {
                if (SessionState.GetBool(Key, false) && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
                    SessionState.SetString(Key + ".error", message);
            };
        }
        public static void Run()
        {
            BarrackStartup.Prepare();
            EditorSceneManager.OpenScene(BarrackStartup.ScenePath);
            SessionState.SetBool(Key, true);
            SessionState.SetInt(Key + ".round", 0);
            SessionState.SetString(Key + ".error", "");
            SessionState.SetString(Key + ".deadline", DateTime.UtcNow.AddMinutes(4).ToString("O"));
            EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
        }
        static void Tick()
        {
            if (!SessionState.GetBool(Key, false)) return;
            try
            {
                if (DateTime.UtcNow > DateTime.Parse(SessionState.GetString(Key + ".deadline", ""))) throw new TimeoutException("Play validation timed out");
                var error = SessionState.GetString(Key + ".error", "");
                if (error.Length > 0) throw new Exception(error);
                int round = SessionState.GetInt(Key + ".round", 0);
                if (!EditorApplication.isPlaying)
                {
                    if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                    if (round == 1) { readyAt = 0; action = 0; EditorApplication.isPlaying = true; }
                    if (round == 2)
                    {
                        if (AssetBundle.GetAllLoadedAssetBundles().Any(b => b.LoadAllAssets<AnimationClip>().Any(c => c.name == BarrackPreview.Actions[0])))
                            throw new Exception("Animation bundle leaked after leaving Play Mode");
                        File.WriteAllText("LocalOnly/Barrack/live/unity6-play-result.txt", "PASS: root Unity 6; default scene; five renderers; five source actions; nonuniform camera output; two Play Mode entries and exits; no Error/Exception logs.\n");
                        SessionState.SetBool(Key, false); EditorApplication.Exit(0);
                    }
                    return;
                }
                var host = UnityEngine.Object.FindFirstObjectByType<BarrackPreview>();
                if (host == null || !host.Ready) return;
                if (host.RendererCount != 5) throw new Exception("Unexpected renderer count");
                if (readyAt == 0) { readyAt = EditorApplication.timeSinceStartup; return; }
                if (EditorApplication.timeSinceStartup - readyAt < 1) return;
                if (action < BarrackPreview.Actions.Length)
                { host.PlayAction(BarrackPreview.Actions[action++]); readyAt = EditorApplication.timeSinceStartup; return; }
                if (action == BarrackPreview.Actions.Length) { host.PlayAction(BarrackPreview.Actions[0]); action++; readyAt = EditorApplication.timeSinceStartup; return; }
                if (round == 0) Capture(host.displayCamera);
                SessionState.SetInt(Key + ".round", round + 1);
                EditorApplication.isPlaying = false;
            }
            catch (Exception e)
            {
                SessionState.SetBool(Key, false);
                File.WriteAllText("LocalOnly/Barrack/live/unity6-play-result.txt", "FAIL: " + e);
                Debug.LogException(e); EditorApplication.Exit(1);
            }
        }
        static void Capture(Camera camera)
        {
            var previous = RenderTexture.active;
            var target = new RenderTexture(960, 720, 24);
            var image = new Texture2D(960, 720, TextureFormat.RGB24, false);
            try
            {
                if (RenderPipelineManager.currentPipeline is BarrackRenderPipeline pipeline)
                { var frame = pipeline.LatestFrame(camera); if (frame == null) throw new Exception("No rendered frame"); Graphics.Blit(frame, target); }
                else { camera.targetTexture = target; camera.Render(); }
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 960, 720), 0, 0); image.Apply();
                File.WriteAllBytes("LocalOnly/Barrack/live/unity6-default-scene.png", image.EncodeToPNG());
                if (image.GetPixels32().Select(c => ((int)c.r << 16) | ((int)c.g << 8) | c.b).Distinct().Count() < 100)
                {
                    if (RenderPipelineManager.currentPipeline is BarrackRenderPipeline rawPipeline)
                    {
                        Graphics.Blit(rawPipeline.LatestRaw(camera), target); RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 960, 720), 0, 0); image.Apply();
                        File.WriteAllBytes("LocalOnly/Barrack/live/render-raw-diagnostic.png", image.EncodeToPNG());
                    }
                    var native = UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None).First().sharedMaterial.shader;
                    Debug.Log("NATIVE_PASS_TAGS " + native.FindPassTagValue(0, new ShaderTagId("LightMode")).name + " / " + native.FindPassTagValue(0, new ShaderTagId("LIGHTMODE")).name);
                    throw new Exception("Default scene rendered an empty/uniform frame");
                }
                File.WriteAllBytes("LocalOnly/Barrack/live/unity6-default-scene.png", image.EncodeToPNG());
            }
            finally { camera.targetTexture = null; RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); target.Release(); UnityEngine.Object.DestroyImmediate(target); }
        }
    }
}
