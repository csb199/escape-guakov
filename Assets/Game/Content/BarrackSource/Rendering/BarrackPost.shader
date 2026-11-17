Shader "Hidden/Barrack/Post"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
        #include "UnityCG.cginc"
        sampler2D _BarrackSource, _BarrackDepthTexture, _BarrackAOTexture, _BarrackBloomTexture, _BarrackHistory, _BarrackPlanarTexture;
        float4 _BarrackSource_TexelSize;
        float4x4 _BarrackInverseVP, _BarrackPreviousVP, _BarrackVP, _BarrackReflectionVP;
        float4 _BarrackCamera, _BarrackAOParams, _BarrackBloomParams, _BarrackBlurDirection;
        float4 _BarrackFogParams, _BarrackFogDistance, _BarrackFogColor, _BarrackLinearFogColor;
        float4 _BarrackGrade, _BarrackColorFilter, _BarrackFilm, _BarrackVignette, _BarrackVignetteColor;
        float4 _BarrackReflection;
        float _BarrackHistoryWeight, _BarrackShadowEnabled;
        sampler2D _BarrackShadowTexture;
        float4x4 _MainLightWorldToShadow[5];
        float4 _CascadeShadowSplitSpheres0, _CascadeShadowSplitSpheres1, _CascadeShadowSplitSphereRadii, _MainLightShadowmapSize;
        // Native scene materials used a source-only player-shadow pass. Receive its actual
        // cascaded caster depth here on the stage floor when adapting that pass to Unity 6.
        float StageShadow(float3 p, float3 normal)
        {
            if (_BarrackShadowEnabled < 0.5 || normal.y < 0.7 || abs(p.y) > 0.18) return 1;
            float3 delta0 = p - _CascadeShadowSplitSpheres0.xyz;
            float3 delta1 = p - _CascadeShadowSplitSpheres1.xyz;
            int cascade = dot(delta0,delta0) < _CascadeShadowSplitSphereRadii.x ? 0 : 1;
            if (cascade == 1 && dot(delta1,delta1) >= _CascadeShadowSplitSphereRadii.y) return 1;
            float3 shadow = mul(_MainLightWorldToShadow[cascade],float4(p,1)).xyz;
            float visibility = 0;
            [unroll] for(int y=-1;y<=1;y++) [unroll] for(int x=-1;x<=1;x++)
            {
                float d = tex2D(_BarrackShadowTexture,shadow.xy + float2(x,y)*_MainLightShadowmapSize.xy).r;
                #if defined(UNITY_REVERSED_Z)
                    visibility += shadow.z + 0.0003 >= d;
                #else
                    visibility += shadow.z - 0.0003 <= d;
                #endif
            }
            return lerp(0.45,1,visibility / 9);
        }
        v2f_img Fullscreen(uint id : SV_VertexID)
        {
            v2f_img output;
            float2 uv = float2((id << 1) & 2, id & 2);
            output.pos = float4(uv * 2 - 1, 0, 1);
            #if UNITY_UV_STARTS_AT_TOP
                uv.y = 1 - uv.y;
            #endif
            output.uv = uv; return output;
        }
        float RawDepth(float2 uv) { return SAMPLE_DEPTH_TEXTURE(_BarrackDepthTexture, uv); }
        float3 World(float2 uv, float d)
        {
            float4 clip = float4(uv * 2 - 1, d, 1);
            #if UNITY_UV_STARTS_AT_TOP
                clip.y = -clip.y;
            #endif
            float4 world = mul(_BarrackInverseVP, clip);
            return world.xyz / max(abs(world.w), 0.00001) * sign(world.w);
        }
        float2 Project(float3 p, float4x4 projectionView)
        {
            float4 clip = mul(projectionView, float4(p, 1));
            float2 uv = clip.xy / max(clip.w, 0.00001);
            #if UNITY_UV_STARTS_AT_TOP
                uv.y = -uv.y;
            #endif
            return uv * 0.5 + 0.5;
        }
        float3 Normal(float3 position)
        {
            float3 n = normalize(cross(ddy(position), ddx(position)));
            return dot(n, _BarrackCamera.xyz - position) < 0 ? -n : n;
        }
        float4 AO(v2f_img i) : SV_Target
        {
            float d = RawDepth(i.uv);
            float3 p = World(i.uv, d), n = Normal(p);
            float radius = _BarrackAOParams.y;
            float pixels = clamp(radius * _ScreenParams.y / max(distance(_BarrackCamera.xyz, p), 0.5), 2, 60);
            float occlusion = 0;
            [unroll] for (int k = 0; k < 8; k++)
            {
                float a = k * 0.78539816;
                float2 uv = i.uv + float2(cos(a), sin(a)) * abs(_BarrackSource_TexelSize.xy) * pixels * (0.3 + (k % 3) * 0.25);
                float3 delta = World(uv, RawDepth(uv)) - p;
                float lengthDelta = length(delta);
                occlusion += saturate(dot(n, delta / max(lengthDelta, 0.0001)) - 0.08) * saturate(1 - lengthDelta / radius);
            }
            return saturate(1 - occlusion * _BarrackAOParams.x * 0.5);
        }
        float4 Bloom(v2f_img i) : SV_Target
        {
            float3 c = max(0, tex2D(_BarrackSource, i.uv).rgb);
            float brightness = max(c.r, max(c.g, c.b));
            float knee = max(0.001, _BarrackBloomParams.x * 0.5);
            float soft = clamp(brightness - _BarrackBloomParams.x + knee, 0, knee * 2);
            soft = soft * soft / (4 * knee + 0.0001);
            float contribution = max(soft, brightness - _BarrackBloomParams.x) / max(brightness, 0.0001);
            return float4(c * contribution, 1);
        }
        float4 Blur(v2f_img i) : SV_Target
        {
            float2 direction = _BarrackBlurDirection.xy * abs(_BarrackSource_TexelSize.xy);
            float3 result = tex2D(_BarrackSource, i.uv).rgb * 0.227027;
            result += tex2D(_BarrackSource, i.uv + direction * 1.384615).rgb * 0.316216;
            result += tex2D(_BarrackSource, i.uv - direction * 1.384615).rgb * 0.316216;
            result += tex2D(_BarrackSource, i.uv + direction * 3.230769).rgb * 0.070270;
            result += tex2D(_BarrackSource, i.uv - direction * 3.230769).rgb * 0.070270;
            return float4(result, 1);
        }
        float3 Reflection(float3 color, float2 uv, float3 position, float3 normal)
        {
            if (_BarrackReflection.x < 0.5 || normal.y < 0.8) return color;
            float3 view = normalize(position - _BarrackCamera.xyz);
            float3 direction = reflect(view, normal);
            if (abs(position.y) > 0.18) return color;
            float2 reflectedUV = Project(position, _BarrackReflectionVP);
            float3 reflected = tex2D(_BarrackPlanarTexture, reflectedUV).rgb;
            float hit = all(reflectedUV >= 0) && all(reflectedUV <= 1);
            float fresnel = 0.04 + 0.96 * pow(1 - saturate(dot(-view, normal)), 5);
            return lerp(color, reflected, hit * fresnel * _BarrackReflection.y);
        }
        float4 Composite(v2f_img i) : SV_Target
        {
            float3 c = tex2D(_BarrackSource, i.uv).rgb;
            float3 p = World(i.uv, RawDepth(i.uv));
            float3 n = Normal(p);
            c *= StageShadow(p, n);
            c = Reflection(c, i.uv, p, n);
            return float4(c, 1);
        }
        float4 Temporal(v2f_img i) : SV_Target
        {
            float3 c = tex2D(_BarrackSource, i.uv).rgb, minimum = c, maximum = c;
            [unroll] for (int y = -1; y <= 1; y++) [unroll] for (int x = -1; x <= 1; x++)
            { float3 sampleColor = tex2D(_BarrackSource, i.uv + float2(x, y) * abs(_BarrackSource_TexelSize.xy)).rgb; minimum = min(minimum, sampleColor); maximum = max(maximum, sampleColor); }
            float3 p = World(i.uv, RawDepth(i.uv)); float2 previousUV = Project(p, _BarrackPreviousVP);
            float weight = _BarrackHistoryWeight * (all(previousUV >= 0) && all(previousUV <= 1));
            float3 history = clamp(tex2D(_BarrackHistory, previousUV).rgb, minimum, maximum);
            return float4(lerp(c, history, weight), 1);
        }
        float4 FlipReflection(v2f_img i) : SV_Target { return tex2D(_BarrackSource,float2(1-i.uv.x,i.uv.y)); }
        float4 ShowDepth(v2f_img i) : SV_Target { return tex2D(_BarrackSource, i.uv).rrrr; }
        ENDHLSL
        Pass { Name "AmbientOcclusion" HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Fullscreen
            #pragma fragment AO
            ENDHLSL }
        Pass { Name "BloomThreshold" HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Fullscreen
            #pragma fragment Bloom
            ENDHLSL }
        Pass { Name "BloomBlur" HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Fullscreen
            #pragma fragment Blur
            ENDHLSL }
        Pass { Name "SourceProfileComposite" HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Fullscreen
            #pragma fragment Composite
            ENDHLSL }
        Pass { Name "TemporalResolve" HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Fullscreen
            #pragma fragment Temporal
            ENDHLSL }
        Pass { Name "DepthDiagnostic" HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Fullscreen
            #pragma fragment ShowDepth
            ENDHLSL }
        Pass { Name "ReflectionOrientation" HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Fullscreen
            #pragma fragment FlipReflection
            ENDHLSL }
        Pass { Name "ResolveActualDepth" ZWrite On
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Fullscreen
            #pragma fragment ResolveDepth
            Texture2DMS<float,2> _BarrackDepthMS;
            float ResolveDepth(v2f_img i) : SV_Depth
            {
                int2 pixel=int2(i.pos.xy);
                float a=_BarrackDepthMS.Load(pixel,0),b=_BarrackDepthMS.Load(pixel,1);
                #if defined(UNITY_REVERSED_Z)
                return max(a,b);
                #else
                return min(a,b);
                #endif
            }
            ENDHLSL }
    }
    Fallback Off
}




