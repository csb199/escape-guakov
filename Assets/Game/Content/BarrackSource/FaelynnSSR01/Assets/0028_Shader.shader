// Editor reference only; runtime binds the original native Shader library.
Shader "gf_shader/vfx/UV_Effect_UVAll_Fast_2"
{
Properties
{
_ZTest ("_ZTest", Float) = 4
_ShaderMode ("_ShaderMode", Float) = 1
_BlendMode ("_BlendMode", Float) = 10
_CullMode ("_CullMode", Float) = 0
_DepthMode ("_DepthMode", Float) = 0
_ZOffset ("_ZOffset", Float) = 0
_Cutout ("_Cutout", Float) = 0.5
_Alpha ("_Alpha", Float) = 1
_MainColor ("_MainColor", Color) = (1,1,1,1)
_UseBackColor ("_UseBackColor", Float) = 0
_BackColor ("_BackColor", Color) = (1,1,1,1)
_AlphaIsR ("_AlphaIsR", Float) = 0
_MainTex ("_MainTex", 2D) = "white" {}
_MainPannerX ("_MainPannerX", Float) = 0
_MainPannerY ("_MainPannerY", Float) = 0
_UseMask ("_UseMask", Float) = 0
_MaskTex ("_MaskTex", 2D) = "white" {}
_MaskPannerX ("_MaskPannerX", Float) = 0
_MaskPannerY ("_MaskPannerY", Float) = 0
_UseMask2 ("_UseMask2", Float) = 0
_UseMask2AsColor ("_UseMask2AsColor", Float) = 0
_MaskTex2 ("_MaskTex2", 2D) = "white" {}
_MaskPannerX2 ("_MaskPannerX2", Float) = 0
_MaskPannerY2 ("_MaskPannerY2", Float) = 0
_UseTurbulence ("_UseTurbulence", Float) = 0
_TurbulenceTex ("_TurbulenceTex", 2D) = "white" {}
_TurbulencePower ("_TurbulencePower", Float) = 0
_TurbulencePannerX ("_TurbulencePannerX", Float) = 0
_TurbulencePannerY ("_TurbulencePannerY", Float) = 0
_UseDissolve ("_UseDissolve", Float) = 0
_DissolveTex ("_DissolveTex", 2D) = "white" {}
_DissolvePannerX ("_DissolvePannerX", Float) = 0
_DissolvePannerY ("_DissolvePannerY", Float) = 0
_Dissolve ("_Dissolve", Float) = 0
_DissolveHardness ("_DissolveHardness", Float) = 0
_DissolveEdgeColor ("_DissolveEdgeColor", Color) = (1,1,1,1)
_DissolveEdgeWidth ("_DissolveEdgeWidth", Float) = 0
_Brightness ("_Brightness", Float) = 1
_Contrast ("_Contrast", Float) = 1
_TAACompensation ("_TAACompensation", Float) = 1
_FogInfluence ("_FogInfluence", Float) = 1
_AdditionalAlpha ("_AdditionalAlpha", Float) = 1
_EnableHolographicScanline ("_EnableHolographicScanline", Float) = 0
_HolographicColor ("_HolographicColor", Color) = (1,1,1,1)
_HolographicIntensity ("_HolographicIntensity", Float) = 0
_HolographicWidth ("_HolographicWidth", Float) = 200
}
SubShader { Pass { ColorMask 0 ZWrite Off } }
Fallback Off
}
