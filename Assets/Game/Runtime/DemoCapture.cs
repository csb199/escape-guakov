using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Game.Runtime
{
    // Offscreen rendering also works for hidden automated validation windows.
    public static class DemoCapture
    {
        public static void Save(Camera world, Transform uiRoot, string path)
        {
            var texture = RenderTexture.GetTemporary(1280, 720, 24);
            var previous = RenderTexture.active;
            var worldTarget = world.targetTexture;
            var canvasList = uiRoot.GetComponentsInChildren<Canvas>().Where(c => c.isActiveAndEnabled).ToArray();
            var previousModes = canvasList.Select(c => c.renderMode).ToArray();
            var previousCameras = canvasList.Select(c => c.worldCamera).ToArray();
            var cameraObject = new GameObject("CaptureCamera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>(); camera.enabled = false;
            camera.clearFlags = CameraClearFlags.Depth; camera.cullingMask = 1 << 5;
            camera.transform.position = new Vector3(0, 1000, -1000); camera.targetTexture = texture;
            Texture2D image = null;
            try
            {
                RenderTexture.active = texture; GL.Clear(true, true, new Color(0.035f, 0.055f, 0.09f));
                world.targetTexture = texture; world.Render();
                foreach (var canvas in canvasList)
                {
                    foreach (var transform in canvas.GetComponentsInChildren<Transform>()) transform.gameObject.layer = 5;
                    canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                }
                Canvas.ForceUpdateCanvases(); camera.Render();
                RenderTexture.active = texture;
                image = new Texture2D(1280, 720, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                for (int i = 0; i < canvasList.Length; i++) { canvasList[i].renderMode = previousModes[i]; canvasList[i].worldCamera = previousCameras[i]; }
                world.targetTexture = worldTarget; RenderTexture.active = previous;
                if (image != null) UnityEngine.Object.Destroy(image);
                UnityEngine.Object.Destroy(cameraObject); RenderTexture.ReleaseTemporary(texture);
            }
        }
    }
}
