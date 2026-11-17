Shader "Hidden/Barrack/CapturedBeam"
{
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent+100"}
  Pass
  {
   Tags {"LightMode"="UniversalForward"}
   Cull Front ZWrite Off Blend One One
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   float4 _Beam[9];
   sampler2D _CameraDepthTexture;
   struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;};
   struct v2f {float4 vertex:SV_POSITION;float3 local:TEXCOORD0;float3 cameraLocal:TEXCOORD1;float4 screen:TEXCOORD2;float2 viewFade:TEXCOORD3;float fade:TEXCOORD4;};
   float Smooth(float x){x=saturate(x);return x*x*(3-2*x);}
   v2f vert(appdata v)
   {
    v2f o;float radius=max(_Beam[4].x,_Beam[4].y);
    float z=v.vertex.z*v.vertex.z;float r=lerp(_Beam[4].x/radius,_Beam[4].y/radius,z);
    float3 position=float3(v.vertex.xy*r,z);
    float4 world=mul(unity_ObjectToWorld,float4(position,1));o.vertex=mul(UNITY_MATRIX_VP,world);o.screen=ComputeScreenPos(o.vertex);
    float3 scale=float3(radius,radius,_Beam[5].y);o.local=position*scale;
    o.cameraLocal=mul(unity_WorldToObject,float4(_WorldSpaceCameraPos,1)).xyz*scale;
    float viewZ=mul(UNITY_MATRIX_V,world).z;o.screen.z=-viewZ;
    float nearFade=1-Smooth(abs(o.local.z)/(_Beam[6].y*_Beam[5].y+.001));
    o.viewFade=float2(viewZ,lerp(v.uv.x,1,nearFade*saturate(_Beam[7].w)));
    float angle=Smooth((1-normalize(o.local-o.cameraLocal).z)*.5);
    float range=saturate((abs(o.local.z)-_Beam[5].x)/(_Beam[5].y-_Beam[5].x));
    float attenuation=lerp(Smooth(1-range),Smooth(min((1-range)*5,1))/(1+25*range*range),_Beam[4].w);
    o.fade=angle*attenuation*_Beam[5].w*step(v.uv.x,_Beam[6].w);return o;
   }
   struct output {float4 color:SV_Target0;float4 extra:SV_Target1;};
   output frag(v2f i)
   {
    float3 radial=normalize(i.local+float3(0,0,_Beam[4].z));float3 view=normalize(i.local-i.cameraLocal);
    float3 projected=normalize(-view-dot(-view,radial)*radial);
    float3 cone=float3(-normalize(i.local.xy)*_Beam[3].z,_Beam[3].w);
    float edge=Smooth(lerp(dot(cone,projected),dot(cone,-view),abs(view.z)));
    float exponent=lerp(_Beam[6].x,min((1-_Beam[6].y)*1.5,_Beam[6].x),abs(view.z));
    float intensity=pow(max(edge,1e-8),exponent);intensity=lerp(intensity,1.001,i.viewFade.y);
    float scene=LinearEyeDepth(tex2D(_CameraDepthTexture,i.screen.xy/i.screen.w).r);
    float delta=max(scene-_ProjectionParams.y,0)-max(i.screen.z-_ProjectionParams.y,0);
    float depthDistance=min(abs(i.local.z),_Beam[8].x);float depthFade=saturate(delta/max(depthDistance,1e-5));
    depthFade=lerp(1,depthFade,min(100*saturate(abs(i.local.z)/max(_Beam[8].x,1e-5)),1));
    float nearFade=lerp(Smooth((abs(i.viewFade.x)-_ProjectionParams.y)/_Beam[5].z),1,i.viewFade.y);
    float alpha=intensity*depthFade*nearFade*i.fade*_Beam[2].w;
    output o;o.color=float4(_Beam[2].rgb*_Beam[3].x,1)*alpha;o.extra=float4(.499,0,0,.4995);return o;
   }
   ENDHLSL
  }
 }
}
