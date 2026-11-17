using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace Game.HotUpdate.Barrack
{
    // Executes the original PC HDR LUT builder and UberPost shader bytecode.
    // Constants follow the source shader contract; no host Film() approximation participates.
    sealed class BarrackNativePost : IDisposable
    {
        readonly Material lutBuilder, uber, bloom, opaque;
        readonly BarrackEnvironment settings;
        readonly int[] bloomDown = new int[5], bloomUp = new int[3];
        readonly Texture2D identityCurve, neutralCurve;
        readonly RenderTexture lut;
        bool built;
        public RenderTexture GradingLut => lut;
        public bool UsesSourceFilm => lutBuilder.IsKeywordEnabled("_TONEMAP_UNREAL_FILMIC") && uber.IsKeywordEnabled("_HDR_GRADING");
        public BarrackNativePost(BarrackEnvironment settings)
        {
            this.settings=settings;
            Material Create(string name) { var shader = Array.Find(Resources.FindObjectsOfTypeAll<Shader>(), s => s.name == name) ?? Shader.Find(name); if (shader == null || !shader.isSupported) throw new InvalidOperationException("Original post shader unavailable: " + name + " found=" + (shader != null)); return new Material(shader) { hideFlags = HideFlags.HideAndDontSave }; }
            lutBuilder = Create("Hidden/Universal Render Pipeline/LutBuilderHdr");
            uber = Create("Hidden/Universal Render Pipeline/UberPost");
            bloom = Create("Hidden/Universal Render Pipeline/Bloom");
            opaque = Create("Hidden/Universal Render Pipeline/Opaque Post");
            bloom.EnableKeyword("_BLOOM_USE_LUMINACE");
            uber.EnableKeyword("_BLOOM_TOON");uber.EnableKeyword("_RARELY_USE_VARIENT");
            opaque.EnableKeyword("_SSAO");opaque.EnableKeyword("_EXPONENTIAL_HEIGHT_FOG_OPAQUE_POST");opaque.EnableKeyword("_MRT");
            for(int i=0;i<5;i++)bloomDown[i]=Shader.PropertyToID("_BarrackNativeBloomDown"+i);
            for(int i=0;i<3;i++)bloomUp[i]=Shader.PropertyToID("_BarrackNativeBloomUp"+i);
            bloom.SetVector("_ParamsMobile",new Vector4(settings.bloomThreshold,settings.bloomIntensity,1.8f,0));
            bloom.SetVector("_ParamsMobileTintA",new Vector4(.0138f,.0138f,.0138f,0));
            bloom.SetVector("_ParamsMobileTintB",new Vector4(.138f,.138f,.138f,0));
            identityCurve = Curve(true); neutralCurve = Curve(false);
            lut = new RenderTexture(1024, 32, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { name="Original PC HDR grading LUT", filterMode=FilterMode.Bilinear, wrapMode=TextureWrapMode.Clamp, hideFlags=HideFlags.HideAndDontSave }; lut.Create();
            lutBuilder.SetVector("_Lut_Params", new Vector4(32, .5f/1024, .5f/32, 32f/31));
            lutBuilder.SetVector("_ColorBalance", Vector4.one); lutBuilder.SetVector("_ColorFilter", settings.colorFilter.linear);
            lutBuilder.SetVector("_ChannelMixerRed", new Vector4(1,0,0,0)); lutBuilder.SetVector("_ChannelMixerGreen", new Vector4(0,1,0,0)); lutBuilder.SetVector("_ChannelMixerBlue", new Vector4(0,0,1,0));
            lutBuilder.SetVector("_HueSatCon", new Vector4(0,1+settings.saturation/100,1+settings.contrast/100,0));
            lutBuilder.SetVector("_Lift", Vector4.zero); lutBuilder.SetVector("_Gamma", Vector4.one); lutBuilder.SetVector("_Gain", Vector4.one);
            foreach(var name in new[]{"_Shadows","_Midtones","_Highlights"}) lutBuilder.SetVector(name,Vector4.one);
            lutBuilder.SetVector("_ShaHiLimits",new Vector4(0,.3f,.55f,1));
            lutBuilder.SetVector("_SplitShadows", new Vector4(.5f,.5f,.5f,0)); lutBuilder.SetVector("_SplitHighlights",new Vector4(.5f,.5f,.5f,0));
            foreach(var name in new[]{"_CurveMaster","_CurveRed","_CurveGreen","_CurveBlue"}) lutBuilder.SetTexture(name,identityCurve);
            foreach(var name in new[]{"_CurveHueVsHue","_CurveHueVsSat","_CurveLumVsSat","_CurveSatVsSat"}) lutBuilder.SetTexture(name,neutralCurve);
            lutBuilder.EnableKeyword("_TONEMAP_UNREAL_FILMIC"); lutBuilder.SetFloat("_FilmSlope",settings.filmSlope); lutBuilder.SetFloat("_FilmToe",settings.filmToe); lutBuilder.SetFloat("_FilmShoulder",settings.filmShoulder); lutBuilder.SetFloat("_FilmBlackClip",settings.filmBlackClip); lutBuilder.SetFloat("_FilmWhiteClip",settings.filmWhiteClip);
            uber.EnableKeyword("_HDR_GRADING"); uber.SetFloat("_HDR_GRADING",1);
            uber.SetTexture("_InternalLut",lut); uber.SetVector("_Lut_Params",new Vector4(1f/1024,1f/32,31,Mathf.Pow(2,settings.exposure)));
            uber.SetVector("_UserLut_Params",Vector4.zero);
            uber.SetVector("_Vignette_Params1",new Vector4(settings.vignetteColor.r,settings.vignetteColor.g,settings.vignetteColor.b,1));
            uber.SetVector("_Vignette_Params2",new Vector4(.5f,.5f,settings.vignetteIntensity*3,settings.vignetteSmoothness*5));
            uber.SetFloat("_AlphaOutput",1);
        }
        static Texture2D Curve(bool linear)
        {
            var texture=new Texture2D(128,1,TextureFormat.RGBAHalf,false,true) {wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear,hideFlags=HideFlags.HideAndDontSave};
            var pixels=new Color[128]; for(int i=0;i<128;i++) { float v=linear?i/128f:.5f; pixels[i]=new Color(v,v,v,v); } texture.SetPixels(pixels);texture.Apply();return texture;
        }
        public void RenderOpaque(CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier destination, RenderTargetIdentifier depth, RenderTargetIdentifier extra, RenderTargetIdentifier ao, Camera camera, bool aoEnabled)
        {
            cmd.SetGlobalTexture("_CameraColorTexture",source);cmd.SetGlobalTexture("_CameraColorTextureExt",extra);
            cmd.SetGlobalTexture("_CameraDepthTexture",depth);cmd.SetGlobalTexture("_ScreenSpaceOcclusionTexture",ao);
            opaque.SetFloat("_AOIntensity",aoEnabled?settings.aoIntensity:0);
            opaque.SetVector("_GFFogParams",new Vector4(0,1,1f/(settings.linearFogEnd-settings.linearFogStart),-settings.linearFogStart/(settings.linearFogEnd-settings.linearFogStart)));
            opaque.SetVector("_GFFogColor",new Vector4(.247574165f,.500917256f,.547794521f,1));
            // Runtime constant captured at the calibrated default camera; compensate height while orbiting.
            opaque.SetVectorArray("_ExponentialHeightFogParameters",new[]{
                new Vector4(.000923614774f*Mathf.Pow(2,-.00247f*(camera.transform.position.y-.764f)),.00247f,66536,350),
                new Vector4(0,.0002f,0,1000),new Vector4(.00019f,1000,1,0),new Vector4(.270497859f,.445201248f,.438553274f,.934f)});
            opaque.SetMatrix("_ViewProjInv",(GL.GetGPUProjectionMatrix(camera.projectionMatrix,true)*camera.worldToCameraMatrix).inverse);
            cmd.Blit(source,destination,opaque,0);
        }
        void RenderBloom(CommandBuffer cmd,RenderTargetIdentifier source,int width,int height)
        {
            int[] widths=new int[5],heights=new int[5];
            for(int i=0;i<5;i++)
            {
                widths[i]=Mathf.Max(2,(width>>(i+1))&~1);heights[i]=Mathf.Max(2,(height>>(i+1))&~1);
                cmd.GetTemporaryRT(bloomDown[i],widths[i],heights[i],0,FilterMode.Bilinear,RenderTextureFormat.RGB111110Float,RenderTextureReadWrite.Linear);
                cmd.Blit(i==0?source:new RenderTargetIdentifier(bloomDown[i-1]),bloomDown[i],bloom,i==0?0:1);
            }
            RenderTargetIdentifier low=bloomDown[4];
            for(int i=0;i<3;i++)
            {
                int level=3-i;cmd.GetTemporaryRT(bloomUp[i],widths[level],heights[level],0,FilterMode.Bilinear,RenderTextureFormat.RGB111110Float,RenderTextureReadWrite.Linear);
                cmd.SetGlobalTexture("_MainTexLowMip",low);cmd.SetGlobalVector("_MainTexLowMip_TexelSize",new Vector4(1f/widths[level+1],1f/heights[level+1],widths[level+1],heights[level+1]));
                cmd.Blit(bloomDown[level],bloomUp[i],bloom,2);low=bloomUp[i];
            }
            cmd.SetGlobalTexture("_Bloom_Texture",low);cmd.SetGlobalTexture("_Bloom_Texture_Setup",bloomDown[0]);
        }
        public void Render(CommandBuffer cmd, RenderTargetIdentifier source, RenderTargetIdentifier destination, int width, int height)
        {
            RenderBloom(cmd,source,width,height);
            if(!built) { cmd.Blit(Texture2D.blackTexture,lut,lutBuilder,0); built=true; }
            cmd.SetGlobalTexture("_CameraColorTexture",source);
            cmd.SetGlobalVector("_CameraColorTexture_TexelSize",new Vector4(1f/width,1f/height,width,height));
            cmd.SetGlobalTexture("_InternalLut",lut);
            cmd.Blit(source,destination,uber,0);
            foreach(int id in bloomDown)cmd.ReleaseTemporaryRT(id);foreach(int id in bloomUp)cmd.ReleaseTemporaryRT(id);
        }
        public void Dispose() { UnityEngine.Object.DestroyImmediate(bloom);UnityEngine.Object.DestroyImmediate(opaque); lut.Release(); UnityEngine.Object.DestroyImmediate(lut); UnityEngine.Object.DestroyImmediate(lutBuilder); UnityEngine.Object.DestroyImmediate(uber); UnityEngine.Object.DestroyImmediate(identityCurve); UnityEngine.Object.DestroyImmediate(neutralCurve); }
    }
}

