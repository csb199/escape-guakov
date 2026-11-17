using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace Game.HotUpdate.Barrack
{
    // Source capture 2625/2637/2649: half-resolution GTAO and two directional upsampling filters.
    sealed class BarrackNativeOcclusion
    {
        readonly ComputeShader shader;
        readonly int main, horizontal, vertical;
        static readonly int Half = Shader.PropertyToID("_BarrackGTAOHalf"), Wide = Shader.PropertyToID("_BarrackGTAOWide"), Ext = Shader.PropertyToID("_BarrackGTAOExt");
        public BarrackNativeOcclusion()
        {
            shader = Array.Find(Resources.FindObjectsOfTypeAll<ComputeShader>(), s => s.name == "GTAOComputeShader");
            if (shader == null) throw new InvalidOperationException("Source GTAO compute shader was not loaded");
            main = shader.FindKernel("GTAOMain"); horizontal = shader.FindKernel("BlurHorizontalMain"); vertical = shader.FindKernel("BlurVerticalMain");
        }
        static void Allocate(CommandBuffer cmd, int id, int w, int h, RenderTextureFormat format)
        {
            var d = new RenderTextureDescriptor(w,h,format,0) { enableRandomWrite=true, sRGB=false, msaaSamples=1 };
            cmd.GetTemporaryRT(id,d,FilterMode.Bilinear);
        }
        public void Render(CommandBuffer cmd, Camera camera, int width, int height, int normal, int depth, int output, int frame)
        {
            int w=(width+1)/2, h=(height+1)/2;
            Allocate(cmd,Half,w,h,RenderTextureFormat.ARGB32); Allocate(cmd,Wide,width,h,RenderTextureFormat.ARGB32); Allocate(cmd,output,width,height,RenderTextureFormat.R8);
            cmd.GetTemporaryRT(Ext,width,height,0,FilterMode.Point,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear); cmd.Blit(normal,Ext);
            float n=camera.nearClipPlane, f=camera.farClipPlane;
            cmd.SetComputeVectorParam(shader,"_ZBufferParams", SystemInfo.usesReversedZBuffer ? new Vector4(f/n-1,1,1/n-1/f,1/f) : new Vector4(1-f/n,f/n,1/f-1/n,1/n));
            cmd.SetComputeMatrixParam(shader,"glstate_matrix_projection",GL.GetGPUProjectionMatrix(camera.projectionMatrix,true));
            cmd.SetComputeMatrixParam(shader,"unity_MatrixV",camera.worldToCameraMatrix);
            cmd.SetComputeIntParam(shader,"_FrameIndex",frame%4); cmd.SetComputeIntParam(shader,"_DownsamplingFactor",2);
            cmd.SetComputeFloatParam(shader,"_SampleRadius",.5f); cmd.SetComputeFloatParam(shader,"_DistributionPower",2); cmd.SetComputeFloatParam(shader,"_FalloffRange",.4f);
            cmd.SetComputeVectorParam(shader,"_TextureSize",new Vector4(width,height,1f/width,1f/height));
            cmd.SetComputeTextureParam(shader,main,"_CameraColorTextureExt",Ext); cmd.SetComputeTextureParam(shader,main,"_DepthTexture",depth); cmd.SetComputeTextureParam(shader,main,"_RW_GTAOTexture",Half);
            cmd.DispatchCompute(shader,main,(w+31)/32,(h+31)/32,1);
            cmd.SetComputeTextureParam(shader,horizontal,"_GTAOTexture",Half); cmd.SetComputeTextureParam(shader,horizontal,"_RW_BlurTexture",Wide);
            cmd.DispatchCompute(shader,horizontal,(width+63)/64,h,1);
            cmd.SetComputeTextureParam(shader,vertical,"_GTAOTexture",Wide); cmd.SetComputeTextureParam(shader,vertical,"_RW_FinalAOTexture",output);
            cmd.DispatchCompute(shader,vertical,width,(height+63)/64,1);
            cmd.ReleaseTemporaryRT(Half);cmd.ReleaseTemporaryRT(Wide);cmd.ReleaseTemporaryRT(Ext);
        }
    }
}
