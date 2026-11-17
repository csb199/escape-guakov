using UnityEngine;
namespace Game.HotUpdate.Barrack
{
    [CreateAssetMenu(menuName = "Barrack/Source Stage")]
    public sealed class BarrackStageDefinition : ScriptableObject
    {
        public string scenePath;
        public int characterLayer=13; // Captured CharForward accepts layers 13 and 14; default barrack actors use 13.
        public TextAsset[] bundles;
        public TextAsset characterLightProbes;
        public TextAsset environment, cameraConfiguration, reflectionReceiver, beamContract;
        public Shader beamShader;
        public BarrackSourceUiDefinition sourceUi;
        public TextAsset materialContract;
        public BarrackRenderPipelineAsset pipeline;
    }
}

