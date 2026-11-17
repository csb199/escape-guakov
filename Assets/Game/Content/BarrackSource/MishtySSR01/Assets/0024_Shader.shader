// Editor reference only; runtime binds the original native Shader library.
Shader "gf_shader/pbr/character/eyeblend_add"
{
Properties
{
_MainColor ("_MainColor", Color) = (1,1,1,0.85)
_MainTex ("_MainTex", 2D) = "white" {}
_SpecularIntensity ("_SpecularIntensity", Float) = 1
_ShadowBiasDistance ("_ShadowBiasDistance", Float) = 0.1
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
