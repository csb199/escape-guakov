using System;
using System.IO;
using System.Linq;
using Game.HotUpdate.Barrack;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Game.Editor.Barrack
{
    public static class BarrackStageImporter
    {
        const string Root = "Assets/Game/Content/BarrackSource/Stage/";
        [Serializable] sealed class Plan { public string scenePath; public string[] paths; }
        public static void Prepare()
        {
            var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(Root + "import-plan.json"));
            var definition = AssetDatabase.LoadAssetAtPath<BarrackStageDefinition>(Root + "Stage.asset");
            if (definition == null) { definition = ScriptableObject.CreateInstance<BarrackStageDefinition>(); AssetDatabase.CreateAsset(definition, Root + "Stage.asset"); }
            definition.scenePath = plan.scenePath;
            definition.characterLayer=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Content/BarrackSource/GrozaSR01/GrozaSR01.prefab").layer;
            definition.bundles = plan.paths.Select(p => AssetDatabase.LoadAssetAtPath<TextAsset>(p) ?? throw new FileNotFoundException(p)).ToArray();
            definition.beamContract=AssetDatabase.LoadAssetAtPath<TextAsset>(Root+"beam-contract.json");definition.beamShader=Shader.Find("Hidden/Barrack/CapturedBeam");
            definition.reflectionReceiver=AssetDatabase.LoadAssetAtPath<TextAsset>(Root+"reflection-receiver.json");
            definition.characterLightProbes=AssetDatabase.LoadAssetAtPath<TextAsset>(Root+"character-light-probes.json") ?? throw new FileNotFoundException("Source character light probes required");
            definition.environment = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "environment.json");
            definition.sourceUi = BarrackSourceUiImporter.Prepare();
            definition.materialContract = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Content/BarrackSource/GrozaSR01/material-contract.json");
            definition.cameraConfiguration = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "camera-config.json");
            var pipeline = AssetDatabase.LoadAssetAtPath<BarrackRenderPipelineAsset>(Root + "RenderPipeline.asset");
            if (pipeline == null) { pipeline = ScriptableObject.CreateInstance<BarrackRenderPipelineAsset>(); AssetDatabase.CreateAsset(pipeline, Root + "RenderPipeline.asset"); }
            pipeline.environment = definition.environment;
            pipeline.depthShader = Shader.Find("Hidden/Barrack/Depth"); pipeline.postShader = Shader.Find("Hidden/Barrack/Post");
            definition.pipeline = pipeline; EditorUtility.SetDirty(pipeline);
            PlayerSettings.colorSpace = ColorSpace.Linear;
            EditorUtility.SetDirty(definition);
            var scene = EditorSceneManager.OpenScene(BarrackStartup.ScenePath);
            var host = UnityEngine.Object.FindFirstObjectByType<BarrackPreview>(); host.stageDefinition = definition;
            host.displayCamera.transform.SetParent(host.transform, true);
            PrefabUtility.SaveAsPrefabAsset(host.gameObject, Root + "BarrackPresentation.prefab");
            EditorUtility.SetDirty(host); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Debug.Log("BARRACK_STAGE_PREPARED " + definition.bundles.Length);
        }
    }
}

