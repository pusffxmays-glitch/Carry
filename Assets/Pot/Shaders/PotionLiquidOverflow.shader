// Translucent variant of Custom/PotionLiquid, added 2026-08-12 for the Overflow stream/droplets only
// (PotionOverflowStream) -- NOT used by the InsideLiquid pool surface, which stays the fully opaque
// Custom/PotionLiquid per explicit request ("透明感なくていいよ": the pool must never let the far
// side of the pot's rim show through it). That request was specifically about a deep pool of liquid
// letting background geometry bleed through; it does not conflict with the separate, later request
// for "半透明感" (a sense of translucency) as part of the liquid's overall material quality, which is
// really about thin strands/edges of liquid reading as glassy and lit-through rather than painted-on
// flat color -- exactly what a falling stream or droplet (a thin shape hanging in open air, with
// nothing behind it that shouldn't show through) can safely have without reintroducing the "see
// through to the pot" complaint, since these shapes never overlap the pot's own geometry.
Shader "Custom/PotionLiquidOverflow"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.20, 0.70, 0.24, 0.82)
        _DeepColor("Deep/Shadowed Color", Color) = (0.04, 0.24, 0.08, 1)
        _Smoothness("Smoothness", Range(0,1)) = 0.92
        _FresnelPower("Fresnel Power", Range(0.5, 8)) = 4.0
        _FresnelColor("Fresnel Color", Color) = (0.6, 0.9, 0.55, 1)
        _RippleStrength("Shader Micro-Ripple Strength", Range(0, 0.05)) = 0.008
        _RippleScale("Micro-Ripple Scale", Float) = 18
        _RippleSpeed("Micro-Ripple Speed", Float) = 1.1
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

                // Genuinely translucent (unlike the pool's Custom/PotionLiquid) -- but only ever
                // applied to thin open-air shapes (stream/droplet), so there's nothing behind them
                // that shouldn't show through.
                float alpha = saturate(_BaseColor.a + fresnel * 0.3);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
