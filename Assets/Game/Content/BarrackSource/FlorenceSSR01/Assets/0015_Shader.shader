// Editor reference only; runtime binds the original native Shader library.
Shader "gf_shader/pbr/character/ubertrans"
{
Properties
{
_BaseColor ("_BaseColor", Color) = (0.5,0.5,0.5,1)
_BaseMap ("_BaseMap", 2D) = "white" {}
_UseAlphaTest ("_UseAlphaTest", Float) = 0
_Cutoff ("_Cutoff", Float) = 0.15
_PreMulAlpha ("_PreMulAlpha", Float) = 0
_BlendMode ("_BlendMode", Float) = 5
_OutlineColor ("_OutlineColor", Color) = (0.6,0.6,0.6,0.1)
_OutlineShadowColor ("_OutlineShadowColor", Color) = (0.6,0.6,0.6,1)
_BumpMap ("_BumpMap", 2D) = "bump" {}
_UseStockingFalloff ("_UseStockingFalloff", Float) = 0
_StockingCenterColor ("_StockingCenterColor", Color) = (1,1,1,1)
_StockingFalloffColor ("_StockingFalloffColor", Color) = (0.1,0,0,1)
_StockingFalloffPower ("_StockingFalloffPower", Float) = 1
_AnisotropicGXX ("_AnisotropicGXX", Float) = 0
_UseGlitter ("_UseGlitter", Float) = 0
_GlitterMap ("_GlitterMap", 2D) = "black" {}
_GlitterDensity ("_GlitterDensity", Float) = 20
_GlitterRimFalloff ("_GlitterRimFalloff", Float) = 1
_GlitterViewWeight ("_GlitterViewWeight", Float) = 0.05
_GlitterRimMin ("_GlitterRimMin", Float) = 0
_GlitterRimMax ("_GlitterRimMax", Float) = 1
_GlitterRimIntensity ("_GlitterRimIntensity", Float) = 1
_GlitterSpecIntensity ("_GlitterSpecIntensity", Float) = 1
_GlitterSpeed ("_GlitterSpeed", Float) = 1
_GlitterRoughness ("_GlitterRoughness", Float) = 0
_GlitterMetallic ("_GlitterMetallic", Float) = 0
_GlitterCurvature ("_GlitterCurvature", Float) = 1
_RMOTex ("_RMOTex", 2D) = "white" {}
_EmissiveIntensity ("_EmissiveIntensity", Float) = 0
_StencilRefC ("_StencilRefC", Float) = 206
_StencilRefCharStart ("_StencilRefCharStart", Float) = 200
_StencilReadMask ("_StencilReadMask", Float) = 255
_StencilWriteMask ("_StencilWriteMask", Float) = 255
_DirOutlineWidthExt ("_DirOutlineWidthExt", Float) = 0
_CampColorIndex ("_CampColorIndex", Float) = 0
_UseRampMap ("_UseRampMap", Float) = 0
_RampMap ("_RampMap", 2D) = "white" {}
_UseGIFlatten ("_UseGIFlatten", Float) = 0
_OutlineWidth ("_OutlineWidth", Float) = 1
_OutlineZBias ("_OutlineZBias", Float) = 0
_OutlineIntensity ("_OutlineIntensity", Float) = 1
_AnisotropicSpecular ("_AnisotropicSpecular", Float) = 0
_Anisotropy ("_Anisotropy", Float) = 0
_AnisotropyShift ("_AnisotropyShift", Float) = 0
_HairDummyDirection ("_HairDummyDirection", Vector) = (0,0,0,0.15)
_HairDummyPosition ("_HairDummyPosition", Vector) = (0,1,0,0.15)
_UseBlendTex ("_UseBlendTex", Float) = 0
_BlendTex ("_BlendTex", 2D) = "gray" {}
_BlendSmoothness ("_BlendSmoothness", Float) = 0.1
_UseSpecularUV2 ("_UseSpecularUV2", Float) = 0
_UseDetailMap ("_UseDetailMap", Float) = 0
_DetailMask ("_DetailMask", 2D) = "white" {}
_DetailAlphaMode ("_DetailAlphaMode", Float) = 1
_DetailAlbedo ("_DetailAlbedo", 2D) = "white" {}
_DetailNormalRM ("_DetailNormalRM", 2D) = "gray" {}
_DetailAlphaIntensity ("_DetailAlphaIntensity", Float) = 1
_DetailAlbedoIntensity ("_DetailAlbedoIntensity", Float) = 1
_DetailNormalIntensity ("_DetailNormalIntensity", Float) = 0
_DetailRMIntensity ("_DetailRMIntensity", Float) = 0
_AdditionalLightShadow ("_AdditionalLightShadow", Float) = 0
_UseBillboard ("_UseBillboard", Float) = 0
_FinalTint ("_FinalTint", Color) = (1,1,1,1)
_AoeSelect ("_AoeSelect", Float) = 0
_AoeSelectColor ("_AoeSelectColor", Color) = (1,1,1,1)
_DissolveIntensity ("_DissolveIntensity", Float) = 0
_EnableHolographicScanline ("_EnableHolographicScanline", Float) = 0
_HolographicColor ("_HolographicColor", Color) = (1,1,1,1)
_HolographicIntensity ("_HolographicIntensity", Float) = 0
_HolographicWidth ("_HolographicWidth", Float) = 200
_ConcealLerp ("_ConcealLerp", Float) = 0
_OnHitColor ("_OnHitColor", Color) = (0,0,0,1)
_CharSaturation ("_CharSaturation", Float) = 1
_PaintMaskTexture ("_PaintMaskTexture", 2D) = "black" {}
_PaintInfluence ("_PaintInfluence", Float) = 0
_ColorOffset ("_ColorOffset", Float) = 0
_Moisture ("_Moisture", Float) = 0
_WetFlowStrength ("_WetFlowStrength", Float) = 0
_WetTraceStrength ("_WetTraceStrength", Float) = 0
_WetFlowSpeed ("_WetFlowSpeed", Float) = 0
_WetFlowSize ("_WetFlowSize", Float) = 0
}
SubShader { Pass { ColorMask 0 ZWrite Off } }
Fallback Off
}
