// Hand-written ShaderLab/HLSL shader for the potion liquid surface. Deliberately not Shader Graph:
// com.unity.shadergraph isn't a direct package dependency in this project (only pulled in
// transitively by URP itself) and there's no existing Shader Graph asset convention here, so a
// plain text shader is the more predictable, reviewable choice -- same reasoning as using Shuriken
// instead of VFX Graph for the overflow VFX (see PotionOverflowVFX.cs).
//
// The big wave/tilt/overflow shape comes entirely from PotionLiquid.cs deforming the mesh itself;
// this shader only adds a small procedural normal-ripple on top for fine shimmer (per spec: shape
// must move via mesh deformation, shader is polish only, never a substitute).
Shader "Custom/PotionLiquid"
{
    // Tuned 2026-08-12 ("粘性が足りない、もう少しとろみがある感じに") -- higher alpha/deeper,
    // more saturated color reads as a dense liquid with real body rather than tinted water; toned
    // down fresnel and finer/slower micro-ripple keep the surface from sparkling like a thin,
    // watery film.
    Properties
    {
        _BaseColor("Base Color", Color) = (0.20, 0.70, 0.24, 0.93)
        _DeepColor("Deep/Shadowed Color", Color) = (0.04, 0.24, 0.08, 1)
        _Smoothness("Smoothness", Range(0,1)) = 0.9
        _FresnelPower("Fresnel Power", Range(0.5, 8)) = 4.5
        _FresnelColor("Fresnel Color", Color) = (0.55, 0.85, 0.5, 1)
        _RippleStrength("Shader Micro-Ripple Strength", Range(0, 0.05)) = 0.006
        _RippleScale("Micro-Ripple Scale", Float) = 14
        _RippleSpeed("Micro-Ripple Speed", Float) = 0.8
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 viewDirWS   : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _DeepColor;
                float _Smoothness;
                float _FresnelPower;
                float4 _FresnelColor;
                float _RippleStrength;
                float _RippleScale;
                float _RippleSpeed;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS);
                OUT.positionHCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS = GetWorldSpaceViewDir(posInputs.positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);

                // Small procedural normal perturbation -- fine shimmer only, not the actual wave shape.
                float t = _Time.y * _RippleSpeed;
                float n1 = sin((IN.positionWS.x + IN.positionWS.z) * _RippleScale + t);
                float n2 = sin((IN.positionWS.x - IN.positionWS.z) * _RippleScale * 1.3 - t * 1.4);
                float3 bump = float3(n1, 0, n2) * _RippleStrength;
                N = normalize(N + bump);

                float3 V = normalize(IN.viewDirWS);

                Light mainLight = GetMainLight();
                float3 L = mainLight.direction;
                float NdotL = saturate(dot(N, L));

                float3 H = normalize(L + V);
                float NdotH = saturate(dot(N, H));
                float spec = pow(NdotH, lerp(8.0, 128.0, _Smoothness)) * _Smoothness;

                float fresnel = pow(1.0 - saturate(dot(N, V)), _FresnelPower);

                float3 ambient = SampleSH(N) * 0.6;

                float3 baseCol = lerp(_DeepColor.rgb, _BaseColor.rgb, NdotL * 0.7 + 0.3);
                float3 color = baseCol * (ambient + NdotL * mainLight.color.rgb)
                             + spec * mainLight.color.rgb
                             + fresnel * _FresnelColor.rgb;

                float alpha = saturate(_BaseColor.a + fresnel * 0.3);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
