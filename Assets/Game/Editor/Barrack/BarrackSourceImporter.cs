using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Editor.Barrack
{
    // Import boundary: native layout only. Missing game scripts/render resources are not substituted.
    public static class BarrackSourceImporter
    {
        public const string LayoutPath = "Assets/Game/Content/BarrackSource/layout.json";
        public const string ScenePath = "Assets/Game/Scenes/BarrackSource.unity";
        [Serializable] public sealed class Layout
        {
            public int schemaVersion;
            public string sourceBundle, sourceAsset, status;
            public Node[] nodes;
            public string[] missingDependencies;
        }
        [Serializable] public sealed class Node
        {
            public int id, parent;
            public string name, sourcePathId;
            public bool active, rect, hasCamera, hasLight;
            public Vector3 position, scale;
            public Quaternion rotation;
            public Vector2 anchorMin, anchorMax, anchoredPosition, sizeDelta, pivot;
            public CameraData camera;
            public LightData light;
        }
        [Serializable] public sealed class CameraData
        {
            public float fieldOfView, near, far, orthographicSize;
            public bool orthographic, hdr;
            public Color background;
            public int clearFlags;
        }
        [Serializable] public sealed class LightData
        {
            public int type;
            public Color color;
            public float intensity, range, spotAngle;
            public bool enabled;
        }
        static Layout Read()
        {
            var data = JsonUtility.FromJson<Layout>(File.ReadAllText(LayoutPath));
            if (data == null || data.schemaVersion != 1 || data.nodes == null || data.nodes.Length == 0)
                throw new InvalidDataException("Invalid source layout");
            for (int i = 0; i < data.nodes.Length; i++)
                if (data.nodes[i].id != i || data.nodes[i].parent < -1 || data.nodes[i].parent >= i ||
                    string.IsNullOrEmpty(data.nodes[i].sourcePathId))
                    throw new InvalidDataException("Invalid source hierarchy at " + i);
            if (data.nodes.Count(n => n.parent == -1) != 1) throw new InvalidDataException("Expected one source root");
            return data;
        }
        [MenuItem("Game Framework/Source Import/Create Barrack Source Layout")]
        public static void Import()
        {
            // Do not overwrite an edited scene or close the user's current scene.
            if (File.Exists(ScenePath)) throw new IOException("Source scene already exists: " + ScenePath);
            var data = Read();
            var previous = SceneManager.GetActiveScene();
            if (!Application.isBatchMode && string.IsNullOrEmpty(previous.path))
                throw new InvalidOperationException("Save the current untitled scene before importing.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var transforms = new Transform[data.nodes.Length];
                foreach (var node in data.nodes)
                {
                    var go = new GameObject(node.name, node.rect ? typeof(RectTransform) : typeof(Transform));
                    var t = go.transform; transforms[node.id] = t;
                    if (node.parent >= 0) t.SetParent(transforms[node.parent], false);
                    t.localPosition = node.position; t.localRotation = node.rotation; t.localScale = node.scale;
                    if (t is RectTransform rect)
                    {
                        rect.anchorMin = node.anchorMin; rect.anchorMax = node.anchorMax;
                        rect.pivot = node.pivot; rect.sizeDelta = node.sizeDelta; rect.anchoredPosition = node.anchoredPosition;
                        t.localPosition = node.position;
                    }
                    if (node.hasCamera)
                    {
                        var c = go.AddComponent<Camera>(); var d = node.camera;
                        c.fieldOfView = d.fieldOfView; c.nearClipPlane = d.near; c.farClipPlane = d.far;
                        c.orthographic = d.orthographic; c.orthographicSize = d.orthographicSize;
                        c.backgroundColor = d.background; c.clearFlags = (CameraClearFlags)d.clearFlags; c.allowHDR = d.hdr;
                        // Source layer assignments are unavailable; do not assign guessed project layers.
                        c.enabled = false;
                    }
                    if (node.hasLight)
                    {
                        var light = go.AddComponent<Light>(); var d = node.light;
                        light.type = (LightType)d.type; light.color = d.color; light.intensity = d.intensity;
                        light.range = d.range; light.spotAngle = d.spotAngle; light.enabled = d.enabled;
                    }
                    go.SetActive(node.active);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Failed saving source scene");
                ValidateScene(scene, data);
                Debug.Log("BARRACK_SOURCE_LAYOUT_PASS nodes=" + data.nodes.Length +
                    "; source dependency gaps=" + data.missingDependencies.Length +
                    "; character/rendering/UI behavior NOT validated");
            }
            finally
            {
                if (SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }
        [MenuItem("Game Framework/Source Import/Validate Barrack Source Layout")]
        public static void Validate()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath,
                Application.isBatchMode ? OpenSceneMode.Single : OpenSceneMode.Additive);
            try { ValidateScene(scene, Read()); Debug.Log("BARRACK_SOURCE_LAYOUT_PASS"); }
            finally { if (opened && SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(scene, true); }
        }
        static void ValidateScene(Scene scene, Layout data)
        {
            var roots = scene.GetRootGameObjects();
            if (roots.Length != 1) throw new InvalidDataException("Unexpected root count");
            var actual = roots[0].GetComponentsInChildren<Transform>(true);
            if (actual.Length != data.nodes.Length) throw new InvalidDataException("Source node count differs");
            for (int i = 0; i < actual.Length; i++)
            {
                var t = actual[i]; var n = data.nodes[i];
                if (t.name != n.name || Vector3.Distance(t.localPosition, n.position) > .0001f ||
                    Quaternion.Angle(t.localRotation, n.rotation) > .01f ||
                    Vector3.Distance(t.localScale, n.scale) > .0001f || t.gameObject.activeSelf != n.active ||
                    (n.parent >= 0 && t.parent != actual[n.parent]))
                    throw new InvalidDataException("Source transform differs: " + n.name);
                if ((t.GetComponent<Camera>() != null) != n.hasCamera || (t.GetComponent<Light>() != null) != n.hasLight)
                    throw new InvalidDataException("Source component count differs: " + n.name);
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) != 0)
                    throw new InvalidDataException("Missing script in source layout");
            }
            if (EditorBuildSettings.scenes.Any(s => s.enabled && s.path == ScenePath))
                throw new InvalidDataException("An incomplete source layout must not enter the Player build");
        }
    }
}
