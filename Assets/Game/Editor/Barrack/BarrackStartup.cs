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
    public static class BarrackStartup
    {
        public const string ScenePath = "Assets/Game/Scenes/Barrack.unity";
        const string Root = "Assets/Game/Content/BarrackSource/GrozaSR01/";
        static BarrackStartup()
        {
            EditorApplication.delayCall += () => {
                if (!EditorApplication.isPlayingOrWillChangePlaymode && !Environment.GetCommandLineArgs().Contains("--self-test"))
                    EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            };
        }
        [MenuItem("Game Framework/Barrack/Open Default Scene")]
        public static void Open()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        }
        public static void Prepare()
        {
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                // Preserve imported source UI hierarchy as evidence. The original room geometry is a separate asset.
                var layout = EditorSceneManager.OpenScene(BarrackSourceImporter.ScenePath, OpenSceneMode.Additive);
                foreach (var root in layout.GetRootGameObjects())
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                EditorSceneManager.CloseScene(layout, true);
                var host = new GameObject("Barrack Development Host").AddComponent<BarrackPreview>();
                host.characterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "GrozaSR01.prefab");
                host.shaderData = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "OriginalCharacterShaders.bytes");
                host.animationData = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "OriginalBarrackAnimations.bytes");
                var camera = new GameObject("Barrack Camera").AddComponent<Camera>();
                camera.tag = "MainCamera"; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.12f, 0.14f, 0.18f); camera.fieldOfView = 40;
                camera.nearClipPlane = 0.05f; camera.farClipPlane = 50;
                camera.cullingMask = 0; // Source materials are submitted explicitly by the development host.
                host.displayCamera = camera;
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            // Release builds retain the signed framework Boot entry. This is the editor development default.
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Game/Scenes/Boot.unity", true) };
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            AssetDatabase.SaveAssets();
            Debug.Log("BARRACK_DEFAULT_SCENE_READY");
        }
    }
}
