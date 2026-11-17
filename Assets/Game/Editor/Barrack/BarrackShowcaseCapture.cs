using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.HotUpdate.Barrack;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Editor.Barrack
{
    [InitializeOnLoad]
    public static class BarrackShowcaseCapture
    {
        const string Key = "Barrack.ShowcaseCapture";
        static bool ShortEnding => Environment.GetEnvironmentVariable("BARRACK_SHOWCASE_ENDING") == "1";
        static string Output => ShortEnding ? "LocalOnly/Barrack/ShowcaseEnding/" : "LocalOnly/Barrack/ShowcaseCaptureFinal/";
        const int Width = 2560, Height = 1440, FramesPerSecond = 20;
        static bool running;
        static readonly List<string> errors = new List<string>();
        [Serializable] public sealed class ClipRecord
        {
            public string character, displayName, action, clip, file;
            public float sourceDuration, outputDuration;
            public int frames;
        }
        [Serializable] public sealed class Report
        {
            public string status, timestamp;
            public int width = Width, height = Height, fps = FramesPerSecond;
            public List<ClipRecord> clips = new List<ClipRecord>();
        }
        static BarrackShowcaseCapture()
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
            File.WriteAllText(Output + "status.txt", "STARTING " + DateTimeOffset.Now.ToString("O"));
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
                var warmup = RenderTexture.GetTemporary(640, 360, 24, RenderTextureFormat.ARGBHalf);
                try { RenderPipeline.SubmitRenderRequest(host.displayCamera, new RenderPipeline.StandardRequest { destination = warmup }); }
                finally { RenderTexture.ReleaseTemporary(warmup); }
                return;
            }
            running = true;
            Capture(host);
        }
        static void FrameWholeAction(BarrackPreview host, float duration)
        {
            var renderers = host.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy).ToArray();
            var mesh = new Mesh();
            var bounds = new Bounds();
            bool first = true;
            try
            {
                for (int sample = 0; sample <= 24; sample++)
                {
                    host.SampleValidationPose(duration * sample / 24f);
                    foreach (var renderer in renderers)
                    {
                        renderer.BakeMesh(mesh);
                        foreach (var vertex in mesh.vertices)
                        {
                            var point = renderer.transform.TransformPoint(vertex);
                            if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                            else bounds.Encapsulate(point);
                        }
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
            if (first) throw new InvalidOperationException("No character geometry to frame");
            var camera = host.displayCamera;
            camera.fieldOfView = 40;
            float distance = Mathf.Max(bounds.extents.y, bounds.extents.x / (Width / (float)Height)) / Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f);
            distance = distance * 1.18f + bounds.extents.z;
            var forward = Quaternion.Euler(2, 0, 0) * Vector3.forward;
            camera.transform.SetPositionAndRotation(bounds.center - forward * distance, Quaternion.LookRotation(forward));
        }
        static async void Capture(BarrackPreview host)
        {
            var report = new Report { status = "RUNNING", timestamp = DateTimeOffset.Now.ToString("O") };
            var pipeline = (BarrackRenderPipeline)RenderPipelineManager.currentPipeline;
            bool temporal = pipeline.TemporalEnabled;
            float frozenTime = pipeline.FrozenTime;
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBHalf);
            var display = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            target.Create(); display.Create();
            int exitCode = 0;
            try
            {
                pipeline.TemporalEnabled = false;
                host.displayCamera.enabled = false;
                foreach (string id in ShortEnding ? new[] { "GrozaSR01", "CheyanneSSR01" } : new[] { "GrozaSR01", "FlorenceSSR01" })
                {
                    int selected = Array.FindIndex(host.characters, candidate => candidate.id == id);
                    if (selected < 0) throw new InvalidOperationException("Missing character " + id);
                    if (host.CharacterId != id) await host.SelectCharacterAsync(selected);
                    if (host.CharacterId != id || !host.Ready) throw new InvalidOperationException("Switch failed " + id + ": " + host.Failure);
                    host.Interaction.Tick(10000);
                    host.CameraRig.Tick(10000, 0);
                    host.FreezeAnimation(true);
                    host.View.SetVisible(false);
                    var definition = host.characters[selected];
                    using (var library = await BarrackAnimationLibrary.LoadAsync(definition.animations.bytes, CancellationToken.None))
                    {
                        var actions = definition.ActionClips();
                        var order = new[] { BarrackAction.Entrance, BarrackAction.Idle, BarrackAction.Stretch, BarrackAction.Inspect, BarrackAction.InspectWeapon, BarrackAction.Head, BarrackAction.Weapon, BarrackAction.Pocket, BarrackAction.Around, BarrackAction.Look, BarrackAction.Return };
                        if (ShortEnding) order = new[] { BarrackAction.Look, BarrackAction.Stretch, BarrackAction.Head };
                        var scheduled = order.Where(actions.ContainsKey).Select(action => new KeyValuePair<string, string>(action.ToString(), actions[action])).ToList();
                        foreach (string extra in (ShortEnding ? Array.Empty<string>() : definition.clips ?? Array.Empty<string>()).Where(name => !actions.Values.Contains(name)))
                            scheduled.Insert(scheduled.Count - 1, new KeyValuePair<string, string>(extra.Replace("c_" + id + "_", ""), extra));
                        foreach (var scheduledClip in scheduled)
                        {
                            string clipName = scheduledClip.Value;
                            string actionName = scheduledClip.Key;
                            var clip = library.Get(clipName);
                            bool closeup = ShortEnding && actionName == "Look";
                            float duration = closeup ? Mathf.Max(6, clip.length + 1) : clip.length;
                            int frameCount = Mathf.CeilToInt(duration * FramesPerSecond) + 1;
                            string fileName = id + "-" + actionName + ".mp4";
                            var record = new ClipRecord { character = id, displayName = definition.displayName, action = actionName, clip = clipName, sourceDuration = clip.length, outputDuration = frameCount / (float)FramesPerSecond, frames = frameCount, file = fileName };
                            host.Interaction.Return();
                            host.Interaction.Tick(10000);
                            host.CameraRig.Tick(10000, 0);
                            host.PlayAction(closeup ? actions[BarrackAction.Idle] : clipName);
                            if (closeup) host.SampleValidationPose(0);
                            else FrameWholeAction(host, clip.length);
                            if (ShortEnding && !closeup) host.CameraRig.Change(BarrackAction.Idle, BarrackInteractionMode.Orbit);
                            var cameraConfiguration = JsonUtility.FromJson<BarrackCameraConfiguration>(host.stageDefinition.cameraConfiguration.text);
                            float previousYaw = 0;
                            bool clicked = false;
                            pipeline.ResetCharacterHistory(host.displayCamera);
                            string encoder = Environment.GetEnvironmentVariable("BARRACK_FFMPEG");
                            if (string.IsNullOrWhiteSpace(encoder)) throw new InvalidOperationException("BARRACK_FFMPEG must identify ffmpeg");
                            var start = new ProcessStartInfo(encoder, "-v error -y -f rawvideo -pixel_format rgb24 -video_size 2560x1440 -framerate 20 -i pipe:0 -vf vflip -an -c:v libx264 -preset fast -crf 17 -threads 6 -pix_fmt yuv420p -movflags +faststart \"" + Path.GetFullPath(Output + fileName) + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true };
                            using (var process = Process.Start(start))
                            {
                                var encoderErrors = process.StandardError.ReadToEndAsync();
                                for (int frame = 0; frame < frameCount; frame++)
                                {
                                    float elapsed = frame / (float)FramesPerSecond;
                                    if (closeup && !clicked && elapsed >= .6f)
                                    {
                                        Vector3 face = host.displayCamera.WorldToScreenPoint(host.Bone("Head_M").position);
                                        if (!host.TryTouch(new Vector2(face.x, face.y)) || host.Interaction.Action != BarrackAction.Look)
                                            throw new InvalidOperationException("Face click did not enter Look: " + id);
                                        clicked = true;
                                    }
                                    float seconds = Mathf.Min(closeup ? Mathf.Max(0, elapsed - .6f) : elapsed, clip.length);
                                    host.SampleValidationPose(seconds);
                                    if (ShortEnding)
                                    {
                                        if (!closeup)
                                        {
                                            float progress = frame / (float)(frameCount - 1);
                                            float yaw = 32 * Mathf.Sin(progress * Mathf.PI * 2);
                                            float sensitivity = cameraConfiguration.executor.RotateSpeed * cameraConfiguration.executor.Sensity;
                                            host.CameraRig.Orbit(new Vector2((yaw - previousYaw) / sensitivity, 0));
                                            previousYaw = yaw;
                                        }
                                        host.CameraRig.Tick(1f / FramesPerSecond, seconds / clip.length);
                                    }
                                    pipeline.FrozenTime = seconds;
                                    foreach (var lighting in host.GetComponentsInChildren<BarrackCharacterLighting>(true)) lighting.Refresh();
                                    RenderPipeline.SubmitRenderRequest(host.displayCamera, new RenderPipeline.StandardRequest { destination = target });
                                    Graphics.Blit(target, display);
                                    var previous = RenderTexture.active;
                                    try
                                    {
                                        RenderTexture.active = display;
                                        pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                                        pixels.Apply();
                                    }
                                    finally { RenderTexture.active = previous; }
                                    byte[] bytes = pixels.GetRawTextureData<byte>().ToArray();
                                    process.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                                    if (frame == frameCount / 2) File.WriteAllBytes(Output + id + "-" + actionName + ".png", pixels.EncodeToPNG());
                                    if (ShortEnding && (frame == frameCount / 4 || frame == frameCount * 3 / 4)) File.WriteAllBytes(Output + id + "-" + actionName + "-" + frame + ".png", pixels.EncodeToPNG());
                                    if (frame % 40 == 0) File.WriteAllText(Output + "status.txt", id + " " + actionName + " frame=" + frame + "/" + frameCount);
                                    await Task.Yield();
                                }
                                process.StandardInput.Close();
                                while (!process.HasExited) await Task.Delay(100);
                                string encoderLog = await encoderErrors;
                                if (process.ExitCode != 0) throw new InvalidOperationException("ffmpeg failed: " + encoderLog);
                            }
                            report.clips.Add(record);
                            File.WriteAllText(Output + "manifest.json", JsonUtility.ToJson(report, true));
                        }
                    }
                }
                if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors.Distinct()));
                report.status = "PASS";
            }
            catch (Exception exception)
            {
                exitCode = 1;
                report.status = "FAIL " + exception;
            }
            finally
            {
                pipeline.TemporalEnabled = temporal;
                pipeline.FrozenTime = frozenTime;
                target.Release(); display.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(display);
                UnityEngine.Object.DestroyImmediate(pixels);
                try { await host.StopAsync(); }
                catch (Exception exception) { exitCode = 1; report.status += "\nSTOP FAILED " + exception; }
                File.WriteAllText(Output + "manifest.json", JsonUtility.ToJson(report, true));
                File.WriteAllText(Output + "status.txt", report.status);
                SessionState.SetBool(Key, false);
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
            }
        }
    }
}

