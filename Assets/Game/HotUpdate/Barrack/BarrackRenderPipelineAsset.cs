using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.HotUpdate.Barrack
{
    [CreateAssetMenu(menuName = "Barrack/Unity 6 Source Renderer")]
    public sealed class BarrackRenderPipelineAsset : RenderPipelineAsset
    {
        public TextAsset environment;
        public Shader depthShader, postShader;
        public bool shadows = true, ambientOcclusion = true, reflections = true, postProcessing = true;
        public override Type pipelineType => typeof(BarrackRenderPipeline);
        public override string renderPipelineShaderTag => "UniversalPipeline";
        protected override RenderPipeline CreatePipeline() => new BarrackRenderPipeline(this);
    }

    // Unity 6 execution adapter for the source pass order and stage settings. Original material shaders stay native.
    public sealed class BarrackRenderPipeline : RenderPipeline
    {
        readonly BarrackRenderPipelineAsset asset;
        readonly bool previousBatching = GraphicsSettings.useScriptableRenderPipelineBatching, previousLinearLights = GraphicsSettings.lightsUseLinearIntensity;
        public bool TemporalEnabled { get; set; } = true;
        public bool ShadowsEnabled { get; set; }
        public bool AmbientOcclusionEnabled { get; set; }
        public bool ReflectionsEnabled { get; set; }
        public bool PostProcessingEnabled { get; set; }
        readonly BarrackEnvironment settings;
        readonly Material depthMaterial, postMaterial;
        readonly BarrackNativePost nativePost;
        readonly BarrackNativeOcclusion nativeOcclusion;
        readonly BarrackNativeTemporal nativeTemporal;
        public RenderTexture SourceGradingLut => nativePost.GradingLut;
        public bool UsesSourceFilm => nativePost.UsesSourceFilm;
        public string ShadowStatus { get; private set; }
        public RenderTexture ShadowDiagnostic { get; private set; }
        public RenderTexture MotionDiagnostic { get; private set; }
        public RenderTexture DepthDiagnostic { get; private set; }
        public RenderTexture InputDiagnostic { get; private set; }
        public RenderTexture ReflectionDiagnostic => planarTexture;
        public Matrix4x4 MainShadowMatrix => shadowMatrices[0];
        public Matrix4x4 ReflectionMatrix { get; private set; }
        public Matrix4x4 CameraMatrix { get; private set; }
        public Vector2 TemporalJitter => nativeTemporal.CurrentJitter;
        public bool TemporalHistoryUsed => nativeTemporal.HistoryUsed;
        public void ResetCharacterHistory(Camera camera) => nativeTemporal.Reset(camera);
        Camera reflectionCamera;
        RenderTexture planarTexture, planarBlur;
        public bool CaptureDiagnostics { get; set; }
        public float FrozenTime { get; set; } = -1;
        readonly CommandBuffer cmd = new CommandBuffer { name = "Barrack source rendering" };
        readonly Matrix4x4[] shadowMatrices = new Matrix4x4[5];
        readonly int[] additionalVisible = new int[32];
        int additionalCount;
        readonly Matrix4x4[] additionalShadowMatrices=new Matrix4x4[256];
        readonly Vector4[] additionalShadowParams=new Vector4[256];
        static readonly int AdditionalShadowTarget=Shader.PropertyToID("_AdditionalLightsShadowmap");
        readonly Vector4[] cascadeSpheres = new Vector4[4];
        readonly Vector4[] lightPositions = new Vector4[32], lightColors = new Vector4[32], lightAttenuations = new Vector4[32], lightDirections = new Vector4[32], lightOcclusion = new Vector4[32];
        readonly Dictionary<int, History> histories = new Dictionary<int, History>();
        sealed class History { public RenderTexture color, raw; }
        static readonly int ColorTarget = Shader.PropertyToID("_BarrackColor"), DepthTarget = Shader.PropertyToID("_BarrackDepth"), DepthCopy = Shader.PropertyToID("_BarrackDepthCopy"), NormalTarget = Shader.PropertyToID("_BarrackNormals"), AuxiliaryTarget = Shader.PropertyToID("_BarrackAuxiliary");
        static readonly int ShadowTarget = Shader.PropertyToID("_MainLightShadowmapTexture"), BloomA = Shader.PropertyToID("_BarrackBloomA"), BloomB = Shader.PropertyToID("_BarrackBloomB"), PostTarget = Shader.PropertyToID("_BarrackPost"), AoTarget = Shader.PropertyToID("_BarrackAO");
        static readonly string[] SceneTags = { "UniversalForward", "UniversalForwardOnly", "SRPDefaultUnlit", "ForwardBase" };
        static readonly string[] CharacterTransparentBackTags = { "GFCharTransBack" };
        static readonly string[] CharacterTransparentFrontTags = { "UniversalForward", "SRPDefaultUnlit" };
        static readonly string[] DepthTags = { "DepthOnly", "UniversalForward", "GFCharForward", "SRPDefaultUnlit" };
        public BarrackRenderPipeline(BarrackRenderPipelineAsset asset)
        {
            this.asset = asset; ShadowsEnabled = asset.shadows; AmbientOcclusionEnabled = asset.ambientOcclusion; ReflectionsEnabled = asset.reflections; PostProcessingEnabled = asset.postProcessing;
            settings = asset.environment != null ? JsonUtility.FromJson<BarrackEnvironment>(asset.environment.text) : new BarrackEnvironment();
            if (asset.depthShader == null || asset.postShader == null) throw new InvalidOperationException("Barrack render shaders are not configured");
            depthMaterial = new Material(asset.depthShader) { hideFlags = HideFlags.HideAndDontSave };
            postMaterial = new Material(asset.postShader) { hideFlags = HideFlags.HideAndDontSave };
            nativePost = new BarrackNativePost(settings); nativeOcclusion = new BarrackNativeOcclusion(); nativeTemporal = new BarrackNativeTemporal();
            GraphicsSettings.useScriptableRenderPipelineBatching = false; // Native Unity 2019 constant-buffer layouts must use Unity's compatibility path.
            GraphicsSettings.lightsUseLinearIntensity = true;
        }
        protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
            BeginContextRendering(context, cameras);
            foreach (var camera in cameras)
            {
                BeginCameraRendering(context, camera);
                RenderCamera(context, camera);
                EndCameraRendering(context, camera);
            }
            EndContextRendering(context, cameras);
        }
        protected override bool IsRenderRequestSupported<T>(Camera camera, T request) => request is StandardRequest;
        protected override void ProcessRenderRequests<T>(ScriptableRenderContext context, Camera camera, T request)
        {
            if (!(request is StandardRequest standard) || standard.destination == null) throw new ArgumentException("A standard render request needs a destination");
            var previous = camera.targetTexture;
            try { camera.targetTexture = standard.destination; BeginCameraRendering(context, camera); RenderCamera(context, camera); EndCameraRendering(context, camera); }
            finally { camera.targetTexture = previous; }
        }
        void Execute(ScriptableRenderContext context) { context.ExecuteCommandBuffer(cmd); cmd.Clear(); }
        void RenderCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!camera.TryGetCullingParameters(out var parameters)) return;
#if UNITY_EDITOR
            if (camera.cameraType == CameraType.SceneView) ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
#endif
            parameters.shadowDistance = Mathf.Min(settings.shadowDistance, camera.farClipPlane);
            var culling = context.Cull(ref parameters);
            int width = Mathf.Max(16, Mathf.RoundToInt(camera.pixelWidth * settings.renderScale));
            int height = Mathf.Max(16, Mathf.RoundToInt(camera.pixelHeight * settings.renderScale));
            SetupLights(ref culling, out int mainLight);
            bool hasShadow = RenderShadows(context, ref culling, mainLight);
            bool hasAdditionalShadow=RenderAdditionalShadows(context,ref culling);
            ShadowStatus = "enabled=" + ShadowsEnabled + " main=" + mainLight + " rendered=" + hasShadow + " caster=" + (mainLight >= 0 && culling.GetShadowCasterBounds(mainLight, out var caster) ? caster.ToString() : "none") + " spheres=" + cascadeSpheres[0] + "/" + cascadeSpheres[1];
            if (hasShadow && CaptureDiagnostics)
            {
                if (ShadowDiagnostic == null) { ShadowDiagnostic = new RenderTexture(1024, 1024, 0, RenderTextureFormat.ARGBFloat); ShadowDiagnostic.Create(); }
                PostPass(ShadowTarget, ShadowDiagnostic, 5, settings.shadowResolution, settings.shadowResolution, 1024, 1024); Execute(context);
            }
            if (ReflectionsEnabled && camera.cameraType != CameraType.Reflection) RenderPlanar(context, camera, 2048, 2048);
            cmd.SetGlobalTexture("_ReflectionTex", ReflectionsEnabled && planarTexture != null ? planarTexture : Texture2D.blackTexture);
            context.SetupCameraProperties(camera);
            SetupEnvironment(camera, width, height);
            nativeTemporal.Prepare(cmd,camera,width,height,TemporalEnabled && PostProcessingEnabled);
            context.SetupCameraProperties(camera);
            cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix,camera.projectionMatrix);
            cmd.SetGlobalMatrix("unity_MatrixVP",GL.GetGPUProjectionMatrix(camera.projectionMatrix,true)*camera.worldToCameraMatrix);
            cmd.SetGlobalFloat("_BarrackShadowEnabled", hasShadow ? 1 : 0);
            if (hasShadow) cmd.SetGlobalTexture("_BarrackShadowTexture", ShadowTarget);
            cmd.SetGlobalFloat("_StaticSceneShadowMapEnable", 0);
            cmd.SetGlobalFloat("_GF_PlayerShadowEnabled", 0);
            cmd.GetTemporaryRT(ColorTarget, width, height, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear, 2);
            cmd.GetTemporaryRT(NormalTarget, width, height, 0, FilterMode.Point, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear, 2);
            cmd.GetTemporaryRT(AuxiliaryTarget, width, height, 0, FilterMode.Point, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear, 2);
            cmd.GetTemporaryRT(DepthTarget, new RenderTextureDescriptor(width,height,RenderTextureFormat.Depth,32) { depthStencilFormat=UnityEngine.Experimental.Rendering.GraphicsFormat.D32_SFloat_S8_UInt, msaaSamples=2, bindMS=true },FilterMode.Point);
            cmd.GetTemporaryRT(DepthCopy, width, height, 24, FilterMode.Point, RenderTextureFormat.Depth);
            
            cmd.SetGlobalTexture("_CameraDepthTexture", DepthCopy);
            cmd.SetRenderTarget(new RenderTargetIdentifier[] { ColorTarget, NormalTarget, AuxiliaryTarget }, DepthTarget);
            cmd.ClearRenderTarget(true, true, Color.clear);
            cmd.EnableShaderKeyword("_MRT"); Execute(context);
            Draw(context, ref culling, camera, SceneTags, RenderQueueRange.opaque);
            foreach (var feature in settings.features) if (feature.eventOrder < 450) DrawFeature(context, ref culling, camera, feature);
            cmd.DrawRendererList(context.CreateSkyboxRendererList(camera)); Execute(context);
            cmd.SetGlobalTexture("_BarrackDepthMS",DepthTarget,RenderTextureSubElement.Depth);
            PostPass(ColorTarget,DepthCopy,7,width,height,width,height);
            cmd.SetGlobalTexture("_CameraOpaqueTexture", ColorTarget);
            cmd.SetGlobalTexture("_ScreenGrabTexture", ColorTarget);
            // Transparent objects cannot sample a render target they also write; snapshot the opaque surface.
            cmd.GetTemporaryRT(PostTarget, width, height, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
            cmd.SetGlobalTexture("_BarrackDepthTexture", DepthCopy);
            cmd.SetGlobalMatrix("_BarrackVP", GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix);
            cmd.SetGlobalVector("_BarrackReflection", new Vector4(ReflectionsEnabled ? 1 : 0, settings.reflectionColor.a, settings.reflectionBlur, 0));
            cmd.SetGlobalMatrix("_BarrackInverseVP", (GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix).inverse);
            cmd.SetGlobalVector("_BarrackCamera", camera.transform.position);
            cmd.SetGlobalVector("_BarrackAOParams", new Vector4(AmbientOcclusionEnabled ? 1 : 0, settings.aoRadius, settings.aoFalloff, 0));
            cmd.SetGlobalVector("_BarrackFogParams", new Vector4(settings.fogDensity, settings.fogHeight, settings.fogFalloff, settings.fogOpacity));
            cmd.SetGlobalVector("_BarrackFogDistance", new Vector4(settings.fogStart, settings.linearFogStart, settings.linearFogEnd, 0));
            cmd.SetGlobalColor("_BarrackFogColor", settings.fogColor); cmd.SetGlobalColor("_BarrackLinearFogColor", settings.linearFogColor);
            nativeOcclusion.Render(cmd,camera,width,height,NormalTarget,DepthCopy,AoTarget,FrozenTime >= 0 ? 2 : Time.frameCount);
            cmd.SetGlobalTexture("_BarrackAOTexture", AoTarget);
            cmd.Blit(ColorTarget, PostTarget);
            if (PostProcessingEnabled)
            {
                nativePost.RenderOpaque(cmd,PostTarget,ColorTarget,DepthCopy,NormalTarget,AoTarget,camera,AmbientOcclusionEnabled);
                cmd.Blit(ColorTarget,PostTarget);
            }
            cmd.SetGlobalTexture("_CameraOpaqueTexture", PostTarget); cmd.SetGlobalTexture("_ScreenGrabTexture", PostTarget);
            cmd.SetRenderTarget(new RenderTargetIdentifier[] { ColorTarget, NormalTarget, AuxiliaryTarget }, DepthTarget); Execute(context);
            foreach (var feature in settings.features) if (feature.eventOrder >= 450) DrawFeature(context, ref culling, camera, feature);
            // Native ubertrans front uses UniversalForward; back is a separate LightMode.
            Draw(context, ref culling, camera, CharacterTransparentBackTags, RenderQueueRange.transparent);
            Draw(context, ref culling, camera, SceneTags, RenderQueueRange.transparent);
            cmd.DisableShaderKeyword("_MRT");
            Post(context, camera, width, height);
            cmd.ReleaseTemporaryRT(PostTarget); cmd.ReleaseTemporaryRT(ColorTarget); cmd.ReleaseTemporaryRT(NormalTarget); cmd.ReleaseTemporaryRT(AuxiliaryTarget); cmd.ReleaseTemporaryRT(DepthTarget); cmd.ReleaseTemporaryRT(DepthCopy);
            if (hasShadow) cmd.ReleaseTemporaryRT(ShadowTarget);
            if(hasAdditionalShadow)cmd.ReleaseTemporaryRT(AdditionalShadowTarget);
            Execute(context);
            cmd.SetRenderTarget(camera.targetTexture != null ? new RenderTargetIdentifier(camera.targetTexture) : BuiltinRenderTextureType.CameraTarget);
            cmd.SetViewport(camera.pixelRect); Execute(context);
            if (camera.cameraType == CameraType.Game) context.DrawUIOverlay(camera);
            context.Submit();
        }
        void DrawFeature(ScriptableRenderContext context, ref CullingResults culling, Camera camera, BarrackEnvironment.Feature feature)
        {
            cmd.EnableShaderKeyword("_MRT");
            cmd.SetRenderTarget(new RenderTargetIdentifier[] { ColorTarget, NormalTarget, AuxiliaryTarget }, DepthTarget); Execute(context);
            DrawFeaturePass(context, ref culling, camera, feature);
            cmd.SetRenderTarget(new RenderTargetIdentifier[] { ColorTarget, NormalTarget, AuxiliaryTarget }, DepthTarget); cmd.EnableShaderKeyword("_MRT"); Execute(context);
        }
        void DrawFeaturePass(ScriptableRenderContext context, ref CullingResults culling, Camera camera, BarrackEnvironment.Feature feature)
        {
            var range = feature.queue == 0 ? RenderQueueRange.opaque : feature.queue == 1 ? RenderQueueRange.transparent : RenderQueueRange.all;
            if (feature.queueEnd > 0) range = new RenderQueueRange { lowerBound = feature.queueStart, upperBound = feature.queueEnd };
            cmd.BeginSample(feature.name); Execute(context);
            Draw(context, ref culling, camera, feature.tags, range, unchecked((int)feature.layerMask));
            cmd.EndSample(feature.name); Execute(context);
        }
        static void Draw(ScriptableRenderContext context, ref CullingResults culling, Camera camera, string[] tags, RenderQueueRange range, int layerMask = -1, Material replacement = null)
        {
            if (tags == null || tags.Length == 0) return;
            var sorting = new SortingSettings(camera) { criteria = range.lowerBound >= 2501 ? SortingCriteria.CommonTransparent : SortingCriteria.CommonOpaque };
            var drawing = new DrawingSettings(new ShaderTagId(tags[0]), sorting) {
                perObjectData = PerObjectData.MotionVectors | PerObjectData.Lightmaps | PerObjectData.LightProbe | PerObjectData.ReflectionProbes | PerObjectData.LightData | PerObjectData.LightIndices | PerObjectData.OcclusionProbe | PerObjectData.ShadowMask,
                enableInstancing = false, enableDynamicBatching = false, overrideMaterial = replacement };
            for (int i = 1; i < tags.Length && i < 16; i++) drawing.SetShaderPassName(i, new ShaderTagId(tags[i]));
            var filtering = new FilteringSettings(range, layerMask);
#pragma warning disable CS0618
            context.DrawRenderers(culling, ref drawing, ref filtering);
#pragma warning restore CS0618
        }
        void SetupLights(ref CullingResults culling, out int main)
        {
            main = -1; int additional = 0;
            var visible = culling.visibleLights;
            var map = culling.GetLightIndexMap(Allocator.Temp);
            // Select once, then include other directional lights in the per-object light map.
            for (int i = 0; i < visible.Length; i++)
                if (visible[i].lightType == LightType.Directional && (main < 0 || visible[i].light == RenderSettings.sun || visible[i].light.name == "main_light")) main = i;
            for (int i = 0; i < visible.Length; i++)
            {
                map[i] = -1; var light = visible[i];
                if (i == main) continue;
                if (additional >= 32 || (light.lightType != LightType.Point && light.lightType != LightType.Spot && light.lightType != LightType.Directional)) continue;
                int index = additional++; map[i] = index; additionalVisible[index]=i;
                var position = light.localToWorldMatrix.GetColumn(3); position.w = 1;
                if (light.lightType == LightType.Directional) { position = -light.localToWorldMatrix.GetColumn(2); position.w = 0; }
                lightPositions[index] = position; lightColors[index] = light.finalColor;
                var forward = -light.localToWorldMatrix.GetColumn(2);
                lightDirections[index] = forward;
                float outer = Mathf.Cos(Mathf.Deg2Rad * light.spotAngle * 0.5f);
                float inner = Mathf.Cos(Mathf.Deg2Rad * (light.light != null ? light.light.innerSpotAngle : light.spotAngle * 0.8f) * 0.5f);
                float inverse = 1 / Mathf.Max(inner - outer, 0.001f);
                lightAttenuations[index] = new Vector4(light.lightType == LightType.Directional ? 0 : 1 / Mathf.Max(light.range * light.range, 0.001f), 1f / (1f - 0.8f * 0.8f), light.lightType == LightType.Spot ? inverse : 0, light.lightType == LightType.Spot ? -outer * inverse : 1);
                lightOcclusion[index] = new Vector4(0, 1, -1, -1);
            }
            additionalCount=additional;
            culling.SetLightIndexMap(map); map.Dispose();
            cmd.SetGlobalVector("_MainLightPosition", main >= 0 ? -visible[main].localToWorldMatrix.GetColumn(2) : new Vector4(0, 1, 0, 0));
            cmd.SetGlobalColor("_MainLightColor", main >= 0 ? visible[main].finalColor : Color.black);
            cmd.SetGlobalVector("_AdditionalLightsCount", new Vector4(Mathf.Min(4, additional), 0, 0, 0));
            cmd.SetGlobalVectorArray("_AdditionalLightsPosition", lightPositions); cmd.SetGlobalVectorArray("_AdditionalLightsColor", lightColors);
            cmd.SetGlobalVectorArray("_AdditionalLightsAttenuation", lightAttenuations); cmd.SetGlobalVectorArray("_AdditionalLightsSpotDir", lightDirections); cmd.SetGlobalVectorArray("_AdditionalLightsOcclusionProbes", lightOcclusion);
            cmd.DisableShaderKeyword("_ADDITIONAL_LIGHT_SHADOWS");
            if (additional > 0) cmd.EnableShaderKeyword("_ADDITIONAL_LIGHTS"); else cmd.DisableShaderKeyword("_ADDITIONAL_LIGHTS");
        }
        bool RenderAdditionalShadows(ScriptableRenderContext context,ref CullingResults culling)
        {
            Array.Clear(additionalShadowParams,0,additionalShadowParams.Length);
            var candidates=new List<int>();
            for(int slot=0;slot<additionalCount;slot++)
            {
                var light=culling.visibleLights[additionalVisible[slot]];
                if(ShadowsEnabled&&light.lightType==LightType.Spot&&light.light!=null&&light.light.shadows!=LightShadows.None&&culling.GetShadowCasterBounds(additionalVisible[slot],out _))candidates.Add(slot);
            }
            if(candidates.Count==0){cmd.DisableShaderKeyword("_ADDITIONAL_LIGHT_SHADOWS");return false;}
            int grid=Mathf.CeilToInt(Mathf.Sqrt(candidates.Count)),size=512,tile=size/grid;
            cmd.GetTemporaryRT(AdditionalShadowTarget,size,size,24,FilterMode.Bilinear,RenderTextureFormat.Shadowmap);
            cmd.SetRenderTarget(AdditionalShadowTarget);cmd.ClearRenderTarget(true,false,Color.clear);Execute(context);
            var views=new Matrix4x4[candidates.Count];var projections=new Matrix4x4[candidates.Count];
            var splits=new NativeArray<ShadowSplitData>(candidates.Count,Allocator.Temp);
            var infos=new NativeArray<LightShadowCasterCullingInfo>(culling.visibleLights.Length,Allocator.Temp);
            try
            {
                for(int i=0;i<candidates.Count;i++)
                {
                    int visible=additionalVisible[candidates[i]];
                    culling.ComputeSpotShadowMatricesAndCullingPrimitives(visible,out views[i],out projections[i],out var split);splits[i]=split;
                    infos[visible]=new LightShadowCasterCullingInfo {splitRange=new RangeInt(i,1),projectionType=BatchCullingProjectionType.Perspective};
                }
                context.CullShadowCasters(culling,new ShadowCastersCullingInfos{splitBuffer=splits,perLightInfos=infos});
            }
            finally{splits.Dispose();infos.Dispose();}
            for(int i=0;i<candidates.Count;i++)
            {
                int slot=candidates[i],visible=additionalVisible[slot];var light=culling.visibleLights[visible];var projection=projections[i];
                float texel=2*Mathf.Tan(light.spotAngle*Mathf.Deg2Rad*.5f)*light.range/tile;
                cmd.SetGlobalVector("_ShadowBias",new Vector4(-light.light.shadowBias*texel,-light.light.shadowNormalBias*texel,0,0));
                cmd.SetGlobalVector("_LightDirection",-light.localToWorldMatrix.GetColumn(2));cmd.SetGlobalVector("_LightPosition",light.localToWorldMatrix.GetColumn(3));
                cmd.SetViewport(new Rect(i%grid*tile,i/grid*tile,tile,tile));cmd.SetViewProjectionMatrices(views[i],projection);Execute(context);
                var draw=new ShadowDrawingSettings(culling,visible){splitIndex=0};cmd.DrawRendererList(context.CreateShadowRendererList(ref draw));Execute(context);
                if(SystemInfo.usesReversedZBuffer){projection.m20=-projection.m20;projection.m21=-projection.m21;projection.m22=-projection.m22;projection.m23=-projection.m23;}
                var texture=Matrix4x4.identity;texture.m00=texture.m11=texture.m22=.5f;texture.m03=texture.m13=texture.m23=.5f;
                var atlas=Matrix4x4.identity;atlas.m00=atlas.m11=1f/grid;atlas.m03=(float)(i%grid)/grid;atlas.m13=(float)(i/grid)/grid;
                additionalShadowMatrices[slot]=atlas*texture*projection*views[i];additionalShadowParams[slot]=new Vector4(light.light.shadowStrength,light.light.shadows==LightShadows.Soft?1:0,0,0);
            }
            cmd.SetGlobalMatrixArray("_AdditionalLightsWorldToShadow",additionalShadowMatrices);cmd.SetGlobalVectorArray("_AdditionalShadowParams",additionalShadowParams);
            cmd.SetGlobalVector("_AdditionalShadowmapSize",new Vector4(1f/size,1f/size,size,size));cmd.SetGlobalTexture("_AdditionalLightsShadowmapTexture",AdditionalShadowTarget);cmd.SetGlobalTexture("_AdditionalLightsShadowmap",AdditionalShadowTarget);
            cmd.EnableShaderKeyword("_ADDITIONAL_LIGHT_SHADOWS");Execute(context);return true;
        }
        bool RenderShadows(ScriptableRenderContext context, ref CullingResults culling, int index)
        {
            cmd.DisableShaderKeyword("_MAIN_LIGHT_SHADOWS"); cmd.DisableShaderKeyword("_MAIN_LIGHT_SHADOWS_CASCADE"); cmd.DisableShaderKeyword("_SHADOWS_SOFT");
            if (!ShadowsEnabled || index < 0 || !culling.GetShadowCasterBounds(index, out _)) { cmd.SetGlobalVector("_MainLightShadowParams", Vector4.zero); Execute(context); return false; }
            int size = settings.shadowResolution, tile = size;
            cmd.GetTemporaryRT(ShadowTarget, size, size, 24, FilterMode.Bilinear, RenderTextureFormat.Shadowmap);
            cmd.SetRenderTarget(ShadowTarget); cmd.ClearRenderTarget(true, false, Color.clear); Execute(context);
            var views = new Matrix4x4[1]; var projections = new Matrix4x4[1];
            var splits = new NativeArray<ShadowSplitData>(1, Allocator.Temp);
            var lights = new NativeArray<LightShadowCasterCullingInfo>(culling.visibleLights.Length, Allocator.Temp);
            try
            {
                for (int cascade = 0; cascade < 1; cascade++)
                {
                    if (!culling.ComputeDirectionalShadowMatricesAndCullingPrimitives(index, cascade, 1, new Vector3(settings.cascadeSplit, 0, 0), tile, 0.2f, out views[cascade], out projections[cascade], out var split)) continue;
                    splits[cascade] = split; cascadeSpheres[cascade] = split.cullingSphere;
                }
                lights[index] = new LightShadowCasterCullingInfo { splitRange = new RangeInt(0, 1), projectionType = BatchCullingProjectionType.Orthographic };
                context.CullShadowCasters(culling, new ShadowCastersCullingInfos { splitBuffer = splits, perLightInfos = lights });
            }
            finally { splits.Dispose(); lights.Dispose(); }
            for (int cascade = 0; cascade < 1; cascade++)
            {
                var view = views[cascade]; var projection = projections[cascade];
                var draw = new ShadowDrawingSettings(culling, index) { splitIndex = cascade };
                float texelSize = 2 / projection.m00 / tile;
                cmd.SetGlobalVector("_ShadowBias", new Vector4(-settings.shadowDepthBias * texelSize, -settings.shadowNormalBias * texelSize, 0, 0));
                cmd.SetGlobalVector("_LightDirection", -culling.visibleLights[index].localToWorldMatrix.GetColumn(2));
                cmd.SetViewport(new Rect(cascade * tile, 0, tile, tile)); cmd.SetViewProjectionMatrices(view, projection);
                // Source captured ShadowCaster raster state: depthBias=0, slopeScaledDepthBias=0.
                cmd.SetGlobalDepthBias(0, 0); Execute(context);
                cmd.DrawRendererList(context.CreateShadowRendererList(ref draw)); Execute(context);
                cmd.SetGlobalDepthBias(0, 0);
                if (SystemInfo.usesReversedZBuffer) { projection.m20 = -projection.m20; projection.m21 = -projection.m21; projection.m22 = -projection.m22; projection.m23 = -projection.m23; }
                var texture = Matrix4x4.identity; texture.m00 = texture.m11 = texture.m22 = 0.5f; texture.m03 = texture.m13 = texture.m23 = 0.5f;
                var atlas = Matrix4x4.identity; atlas.m00 = atlas.m11 = 1;
                shadowMatrices[cascade] = atlas * texture * projection * view;
            }
            var noop = Matrix4x4.zero; noop.m22 = SystemInfo.usesReversedZBuffer ? 1 : 0;
            for (int i = 1; i < 5; i++) shadowMatrices[i] = noop;
            cmd.SetGlobalMatrixArray("_MainLightWorldToShadow", shadowMatrices);
            for (int i = 0; i < 4; i++) cmd.SetGlobalVector("_CascadeShadowSplitSpheres" + i, cascadeSpheres[i]);
            cmd.SetGlobalVector("_CascadeShadowSplitSphereRadii", new Vector4(cascadeSpheres[0].w * cascadeSpheres[0].w, cascadeSpheres[1].w * cascadeSpheres[1].w, 0, 0));
            cmd.SetGlobalVector("_MainLightShadowmapSize", new Vector4(1f / size, 1f / size, size, size));
            float distanceSquared = settings.shadowDistance * settings.shadowDistance;
            cmd.SetGlobalVector("_MainLightShadowParams", new Vector4(1, 1, 0, 0));
            cmd.EnableShaderKeyword("_MAIN_LIGHT_SHADOWS");  Execute(context); return true;
        }
        void RenderPlanar(ScriptableRenderContext context, Camera camera, int width, int height)
        {
            if (reflectionCamera == null)
            {
                var owner = new GameObject("Barrack planar reflection") { hideFlags = HideFlags.HideAndDontSave };
                reflectionCamera = owner.AddComponent<Camera>(); reflectionCamera.enabled = false;
            }
            if (planarTexture == null || planarTexture.width != width || planarTexture.height != height)
            {
                if (planarTexture != null) { planarTexture.Release(); UnityEngine.Object.DestroyImmediate(planarTexture); planarBlur.Release(); UnityEngine.Object.DestroyImmediate(planarBlur); }
                planarTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "Barrack planar reflection", hideFlags = HideFlags.HideAndDontSave }; planarTexture.Create();
                planarBlur = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf) { hideFlags = HideFlags.HideAndDontSave }; planarBlur.Create();
            }
            reflectionCamera.CopyFrom(camera); reflectionCamera.enabled = false; reflectionCamera.cameraType = CameraType.Reflection; reflectionCamera.targetTexture = planarTexture; reflectionCamera.aspect = camera.aspect;
            reflectionCamera.cullingMask &= ~(1 << 19); reflectionCamera.cullingMask &= ~(1 << 4);
            // Source layout ReflectionPlane y=.0015; captured clip z=w intersects this exact plane.
            const float planeHeight=.0015f;
            var mirror = Matrix4x4.identity; mirror.m11 = -1; mirror.m13=2*planeHeight;
            reflectionCamera.transform.position = mirror.MultiplyPoint(camera.transform.position);
            reflectionCamera.transform.rotation = Quaternion.LookRotation(mirror.MultiplyVector(camera.transform.forward), mirror.MultiplyVector(camera.transform.up));
            reflectionCamera.worldToCameraMatrix = camera.worldToCameraMatrix * mirror;
            var view = reflectionCamera.worldToCameraMatrix;
            var point = view.MultiplyPoint(new Vector3(0, planeHeight, 0)); var normal = view.MultiplyVector(Vector3.up).normalized;
            reflectionCamera.projectionMatrix = camera.nonJitteredProjectionMatrix;
            if (!reflectionCamera.TryGetCullingParameters(out var parameters)) return;
            reflectionCamera.projectionMatrix = camera.CalculateObliqueMatrix(new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(point, normal)));
            parameters.shadowDistance = 0; var culling = context.Cull(ref parameters);
            context.SetupCameraProperties(reflectionCamera);
            SetupEnvironment(reflectionCamera, width, height);
            cmd.DisableShaderKeyword("_MRT"); cmd.SetInvertCulling(true); cmd.SetRenderTarget(planarTexture); cmd.SetViewport(new Rect(0, 0, width, height)); cmd.ClearRenderTarget(true, true, Color.black); Execute(context);
            Draw(context, ref culling, reflectionCamera, SceneTags, RenderQueueRange.opaque);
            foreach (var feature in settings.features)
            {
                DrawFeaturePass(context, ref culling, reflectionCamera, feature);
            }
            cmd.DrawRendererList(context.CreateSkyboxRendererList(reflectionCamera)); Execute(context);
            // Source capture includes both sides of transparent character clothing in the planar view.
            Draw(context, ref culling, reflectionCamera, CharacterTransparentBackTags, RenderQueueRange.transparent, (1 << 13) | (1 << 14));
            Draw(context, ref culling, reflectionCamera, CharacterTransparentFrontTags, RenderQueueRange.transparent, (1 << 13) | (1 << 14));
            // The planar source excludes its own transparent reflection receiver.
            cmd.SetInvertCulling(false);
            // Captured receiver has _blurSize=0; do not add the former host Gaussian blur.
            PostPass(planarTexture, planarBlur, 6, width, height, width, height);cmd.Blit(planarBlur,planarTexture);
            cmd.SetGlobalTexture("_BarrackPlanarTexture", planarTexture);
            cmd.SetGlobalTexture("_PlanarReflectionTexture", planarTexture);
            cmd.SetGlobalTexture("_ReflectionTex", planarTexture);
            ReflectionMatrix=GL.GetGPUProjectionMatrix(reflectionCamera.projectionMatrix, true)*view;
            cmd.SetGlobalMatrix("_BarrackReflectionVP", ReflectionMatrix);
            Execute(context);
        }
        void SetupEnvironment(Camera camera, int width, int height)
        {
            cmd.SetGlobalVector("_ScaledScreenParams", new Vector4(width, height, 1f + 1f / width, 1f + 1f / height));
            cmd.SetGlobalVector("_TAAOffsets", Vector4.zero);
            if (FrozenTime >= 0) { float t = FrozenTime; cmd.SetGlobalVector("_Time", new Vector4(t / 20, t, t * 2, t * 3)); cmd.SetGlobalVector("_SinTime", new Vector4(Mathf.Sin(t / 8), Mathf.Sin(t / 4), Mathf.Sin(t / 2), Mathf.Sin(t))); }
            cmd.SetGlobalVector("_WorldSpaceCameraPos", camera.transform.position);
            cmd.SetGlobalFloat("GlobalCharOutlineZBias",3);
            cmd.SetGlobalFloat("_ShadowSHBoost", 0); cmd.SetGlobalFloat("_ShadowSHBoostColorInfluence", 0); cmd.SetGlobalFloat("_ShadowMaskAddationalLight", 0);
            cmd.SetGlobalColor("_GlobalSceneColor", Color.black); cmd.SetGlobalFloat("_GlobalSceneColorWeight", 0);
            cmd.SetGlobalFloat("_GlobalSfxIntensityScale", settings.sfxIntensity);
            cmd.SetGlobalFloat("_CharBodyFaceAlphaScale", settings.bodyFaceAlpha);
            cmd.SetGlobalColor("_CharRimColor", settings.rimColor);
            cmd.DisableShaderKeyword("_EXPONENTIAL_HEIGHT_FOG"); cmd.DisableShaderKeyword("_LINEAR_FOG_ONLY"); // Native opaque post applies the captured fog before transparency.
            var sh = RenderSettings.ambientProbe;
            string[] a = { "unity_SHAr", "unity_SHAg", "unity_SHAb" }, b = { "unity_SHBr", "unity_SHBg", "unity_SHBb" };
            for (int c = 0; c < 3; c++) { cmd.SetGlobalVector(a[c], new Vector4(sh[c, 3], sh[c, 1], sh[c, 2], sh[c, 0] - sh[c, 6])); cmd.SetGlobalVector(b[c], new Vector4(sh[c, 4], sh[c, 5], sh[c, 6] * 3, sh[c, 7])); }
            cmd.SetGlobalVector("unity_SHC", new Vector4(sh[0, 8], sh[1, 8], sh[2, 8], 1));
            if (RenderSettings.customReflectionTexture != null)
            { cmd.SetGlobalTexture("unity_SpecCube0", RenderSettings.customReflectionTexture); cmd.SetGlobalVector("unity_SpecCube0_HDR", new Vector4(ReflectionsEnabled ? settings.reflectionIntensity : 0, 1, 0, 0)); }
        }
        void Post(ScriptableRenderContext context, Camera camera, int width, int height)
        {
            cmd.Blit(ColorTarget,PostTarget);
            if(CaptureDiagnostics)
            {
                MotionDiagnostic=DiagnosticTarget(MotionDiagnostic,width,height,RenderTextureFormat.ARGBFloat,"Motion diagnostic");
                DepthDiagnostic=DiagnosticTarget(DepthDiagnostic,width,height,RenderTextureFormat.RFloat,"Depth diagnostic");
                InputDiagnostic=DiagnosticTarget(InputDiagnostic,width,height,RenderTextureFormat.ARGBHalf,"Temporal input diagnostic");
                cmd.Blit(AuxiliaryTarget,MotionDiagnostic);PostPass(DepthCopy,DepthDiagnostic,5,width,height,width,height);cmd.Blit(PostTarget,InputDiagnostic);
                CameraMatrix=GL.GetGPUProjectionMatrix(camera.projectionMatrix,true)*camera.worldToCameraMatrix;
            }
            nativeTemporal.Resolve(cmd,camera,width,height,PostTarget,NormalTarget,AuxiliaryTarget,DepthCopy,ColorTarget);
            int id=camera.GetInstanceID();
            if(!histories.TryGetValue(id,out var history))histories.Add(id,history=new History());
            if(history.color==null||history.color.width!=width||history.color.height!=height)
            {
                foreach(var rt in new[]{history.color,history.raw})if(rt!=null){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
                history.color=new RenderTexture(width,height,0,RenderTextureFormat.ARGBHalf){hideFlags=HideFlags.HideAndDontSave};history.color.Create();
                history.raw=new RenderTexture(width,height,0,RenderTextureFormat.ARGBHalf){hideFlags=HideFlags.HideAndDontSave};history.raw.Create();
            }
            cmd.Blit(ColorTarget,history.raw);cmd.Blit(ColorTarget,PostTarget);
            if(PostProcessingEnabled)nativePost.Render(cmd,PostTarget,ColorTarget,width,height);
            cmd.Blit(ColorTarget,history.color);cmd.Blit(ColorTarget,BuiltinRenderTextureType.CameraTarget);
            cmd.ReleaseTemporaryRT(AoTarget);  Execute(context);
        }
        void PostPass(RenderTargetIdentifier source, RenderTargetIdentifier destination, int pass, int sourceWidth, int sourceHeight, int width, int height)
        {
            cmd.SetGlobalTexture("_BarrackSource", source);
            cmd.SetGlobalVector("_BarrackSource_TexelSize", new Vector4(1f / sourceWidth, 1f / sourceHeight, sourceWidth, sourceHeight));
            cmd.SetRenderTarget(destination); cmd.SetViewport(new Rect(0, 0, width, height));
            cmd.DrawProcedural(Matrix4x4.identity, postMaterial, pass, MeshTopology.Triangles, 3);
        }
        static RenderTexture DiagnosticTarget(RenderTexture rt,int width,int height,RenderTextureFormat format,string name)
        {
            if(rt!=null&&rt.width==width&&rt.height==height)return rt;
            if(rt!=null){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
            rt=new RenderTexture(width,height,0,format,RenderTextureReadWrite.Linear){name=name,hideFlags=HideFlags.HideAndDontSave};rt.Create();return rt;
        }
        public RenderTexture LatestRaw(Camera camera) => histories.TryGetValue(camera.GetInstanceID(), out var history) ? history.raw : null;
        public RenderTexture LatestFrame(Camera camera) => histories.TryGetValue(camera.GetInstanceID(), out var history) ? history.color : null;
        protected override void Dispose(bool disposing)
        {
            if (reflectionCamera != null) UnityEngine.Object.DestroyImmediate(reflectionCamera.gameObject);
            if (planarTexture != null) { planarTexture.Release(); planarBlur.Release(); UnityEngine.Object.DestroyImmediate(planarTexture); UnityEngine.Object.DestroyImmediate(planarBlur); }
            if (ShadowDiagnostic != null) { ShadowDiagnostic.Release(); UnityEngine.Object.DestroyImmediate(ShadowDiagnostic); }
            foreach(var rt in new[]{MotionDiagnostic,DepthDiagnostic,InputDiagnostic})if(rt!=null){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
            nativeTemporal.Dispose(); nativePost.Dispose(); cmd.Release(); UnityEngine.Object.DestroyImmediate(depthMaterial); UnityEngine.Object.DestroyImmediate(postMaterial);
            foreach (var history in histories.Values) if (history.color != null) { history.color.Release(); UnityEngine.Object.DestroyImmediate(history.color); if (history.raw != null) { history.raw.Release(); UnityEngine.Object.DestroyImmediate(history.raw); } }
            histories.Clear(); GraphicsSettings.useScriptableRenderPipelineBatching = previousBatching; GraphicsSettings.lightsUseLinearIntensity = previousLinearLights; base.Dispose(disposing);
        }
    }
}

