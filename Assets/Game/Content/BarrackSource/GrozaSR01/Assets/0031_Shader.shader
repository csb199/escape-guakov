// Editor reference only. BarrackShaderLibrary binds original native bytecode before display.
Shader "gf_shader/pbr/character/eye"
{
    Properties
    {
        _MainTex ("_MainTex", 2D) = "white" {}
        _BaseColor ("_BaseColor", Color) = (1,1,1,1)
        _ShadowIntensity ("_ShadowIntensity", Float) = 0.25
        _Specularmap ("_Specularmap", 2D) = "white" {}
        _SpecularIntensity ("_SpecularIntensity", Float) = 1.5
        _CorneaParallax ("_CorneaParallax", Float) = 0.3
        _SpecularParallax ("_SpecularParallax", Float) = 0.3
        _ShadowBiasDistance ("_ShadowBiasDistance", Float) = 0.1
        _QueueOffset ("_QueueOffset", Float) = 1
        _StencilRefE ("_StencilRefE", Float) = 204
        _FinalTint ("_FinalTint", Color) = (1,1,1,1)
        _EnableHolographicScanline ("_EnableHolographicScanline", Float) = 0
        _HolographicColor ("_HolographicColor", Color) = (1,1,1,1)
        _HolographicIntensity ("_HolographicIntensity", Float) = 0
        _HolographicWidth ("_HolographicWidth", Float) = 200
        _ConcealLerp ("_ConcealLerp", Float) = 0
        _CharSaturation ("_CharSaturation", Float) = 0
    }
    SubShader { Pass { ColorMask 0 ZWrite Off } }
    Fallback Off
}
