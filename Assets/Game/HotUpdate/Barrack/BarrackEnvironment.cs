using System;
using UnityEngine;
namespace Game.HotUpdate.Barrack
{
    [Serializable] public sealed class BarrackEnvironment
    {
        [Serializable] public sealed class Feature { public string name; public int eventOrder, queue, queueStart, queueEnd; public long layerMask; public string[] tags; }
        public string sourceScene, sourceProfile;
        public int shadowResolution = 4096, cameraMask = -524489;
        public float shadowDistance = 15, cascadeSplit = 0.25f, shadowDepthBias = 1, shadowNormalBias = 1, renderScale = 1.4f;
        public float[] ambientSH;
        public float ambientIntensity = 0.5f, reflectionIntensity = 1, lightmapShadow = 0.2f, sfxIntensity = 0.25f, bodyFaceAlpha = 0.96f;
        public Color rimColor = Color.white;
        public Feature[] features = Array.Empty<Feature>();
        public float bloomThreshold = 0.9f, bloomIntensity = 0.2f, contrast = 0.1f, saturation = 23.4f, exposure;
        public Color colorFilter = Color.white;
        public float aoIntensity = 0.8f, aoRadius = 0.5f, aoFalloff = 0.4f;
        public float vignetteIntensity = 0.25f, vignetteSmoothness = 0.12f;
        public Color vignetteColor = new Color(0.2f, 0.2f, 0.2f, 1);
        public float filmSlope = 0.91f, filmToe = 0.47f, filmShoulder = 0.23f, filmBlackClip, filmWhiteClip = 0.035f;
        public Color fogColor, linearFogColor, reflectionColor;
        public float fogDensity = 0.19f, fogHeight = 10, fogFalloff = 2.47f, fogOpacity = 0.066f, fogStart = 3.5f;
        public float linearFogStart = 10, linearFogEnd = 400, reflectionBlur = 1.65f;
    }
}
