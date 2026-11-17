using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Game.HotUpdate.Barrack;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Editor.Barrack
{
    public static class BarrackHairValidation
    {
        public static void Validate(BarrackPreview host, BarrackRenderPipeline pipeline, Action<bool, string> check)
        {
            var settings = JsonUtility.FromJson<BarrackEnvironment>(host.stageDefinition.environment.text);
            var names = settings.features.Select(feature => feature.name).ToArray();
            var ordered = new[] { "CharForward", "HairShadow", "CharEyeComposite", "CharFaceShadow", "CharFaceSign", "CharHairTrans", "CharacterOutline" };
            check(ordered.Select(name => Array.IndexOf(names, name)).All(index => index >= 0) && ordered.Select(name => Array.IndexOf(names, name)).SequenceEqual(ordered.Select(name => Array.IndexOf(names, name)).OrderBy(index => index)), "captured character stencil and compositing order");
            var materials = host.GetComponentsInChildren<Renderer>(true).SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null && material.shader.name == "gf_shader/pbr/character/uber").Distinct().ToArray();
            check(materials.Any(material => material.GetShaderPassEnabled("GFHairShadow") && material.FindPass("GFHairShadow") >= 0), "native fringe stencil caster bound");
            bool temporal = pipeline.TemporalEnabled;
            var cameraPosition = host.displayCamera.transform.position;
            var cameraRotation = host.displayCamera.transform.rotation;
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGBHalf);
            target.Create();
            try
            {
                pipeline.TemporalEnabled = false;
                Render(host, target, "hair-complete.png");
                foreach (string pass in new[] { "GFHairShadow", "GFCharFaceShadow", "GFCharHairTransE" })
                {
                    var affected = materials.Where(material => material.FindPass(pass) >= 0 && material.GetShaderPassEnabled(pass)).ToArray();
                    check(affected.Length > 0, pass + " has an enabled native material");
                    double difference = 0;
                    foreach (float angle in new[] { 0f, 30f, -30f, 60f, -60f })
                    {
                        host.displayCamera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                        host.displayCamera.transform.RotateAround(host.Bone("Head_M").position, Vector3.up, angle);
                        var baseline = Render(host, target, "hair-reference-" + pass + "-" + angle + ".png");
                        try
                        {
                            foreach (var material in affected) material.SetShaderPassEnabled(pass, false);
                            var disabled = Render(host, target, "hair-without-" + pass + "-" + angle + ".png");
                            for (int index = 0; index < baseline.Length; index++) difference += Math.Abs(baseline[index].r - disabled[index].r) + Math.Abs(baseline[index].g - disabled[index].g) + Math.Abs(baseline[index].b - disabled[index].b);
                        }
                        finally { foreach (var material in affected) material.SetShaderPassEnabled(pass, true); }
                        if (difference > 1) break;
                    }
                    check(difference > 1, pass + " changes rendered pixels across orbit views; total RGB difference=" + difference.ToString("F0"));
                }
            }
            finally
            {
                pipeline.TemporalEnabled = temporal;
                host.displayCamera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                pipeline.ResetCharacterHistory(host.displayCamera);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        static Color32[] Render(BarrackPreview host, RenderTexture target, string filename)
        {
            using var capture = filename == "hair-complete.png" ? GpuCapture.Begin(target) : null;
            RenderPipeline.SubmitRenderRequest(host.displayCamera, new RenderPipeline.StandardRequest { destination = target });
            var previous = RenderTexture.active;
            var display = RenderTexture.GetTemporary(target.width, target.height, 0, RenderTextureFormat.ARGB32);
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                Graphics.Blit(target, display);
                RenderTexture.active = display;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                File.WriteAllBytes("LocalOnly/Barrack/live/" + filename, image.EncodeToPNG());
                return image.GetPixels32();
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(display); UnityEngine.Object.DestroyImmediate(image); }
        }

        sealed class GpuCapture : IDisposable
        {
            const int StartFrameCaptureIndex = 19, EndFrameCaptureIndex = 21;
            [DllImport("renderdoc.dll", CallingConvention = CallingConvention.Cdecl)]
            static extern int RENDERDOC_GetAPI(int version, out IntPtr table);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void StartCapture(IntPtr device, IntPtr window);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint EndCapture(IntPtr device, IntPtr window);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void CaptureEvent(int eventId);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void GetDevice(IntPtr texture, out IntPtr device);
            readonly EndCapture end;
            readonly StartCapture start;
            readonly CaptureEvent callback;
            readonly IntPtr device;
            uint completed;
            GpuCapture(IntPtr table, RenderTexture target)
            {
                var texture = target.GetNativeTexturePtr();
                var getDevice = Marshal.GetDelegateForFunctionPointer<GetDevice>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(texture), 3 * IntPtr.Size));
                getDevice(texture, out device);
                end = Marshal.GetDelegateForFunctionPointer<EndCapture>(Marshal.ReadIntPtr(table, EndFrameCaptureIndex * IntPtr.Size));
                start = Marshal.GetDelegateForFunctionPointer<StartCapture>(Marshal.ReadIntPtr(table, StartFrameCaptureIndex * IntPtr.Size));
                callback = eventId => { if (eventId == 0) start(device, IntPtr.Zero); else completed = end(device, IntPtr.Zero); };
                GL.IssuePluginEvent(Marshal.GetFunctionPointerForDelegate(callback), 0);
            }
            public static GpuCapture Begin(RenderTexture target)
            {
                if (Environment.GetEnvironmentVariable("BARRACK_VALIDATE_HAIR_GPU") != "1") return null;
                if (RENDERDOC_GetAPI(10000, out var table) != 1) throw new InvalidOperationException("RenderDoc capture API unavailable");
                return new GpuCapture(table, target);
            }
            public void Dispose()
            {
                GL.IssuePluginEvent(Marshal.GetFunctionPointerForDelegate(callback), 1);
                AsyncGPUReadback.Request(Texture2D.whiteTexture).WaitForCompletion();
                GC.KeepAlive(callback);
                Marshal.Release(device);
                if (completed != 1) throw new InvalidOperationException("Hair GPU capture failed");
            }
        }
    }
}
