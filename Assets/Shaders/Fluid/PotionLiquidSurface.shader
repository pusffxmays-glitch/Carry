// ============================================================================================
// Custom/PotionLiquidSurface -- Liquid Rendering (Phase 3)
//
// FLUID_DESIGN.md §14 / 仕様 §15 §16。
//
// 完成イメージの液体マテリアル要件:
//   濃い緑色 / 不透明寄り（厚みを感じる） / 高い Smoothness / 明確な Specular Highlight /
//   Fresnel による縁の反射 / 厚みで明暗が変化 / Subtle な内部散乱感
//
// 「厚み」は推測ではなく、Surface Reconstruction に使ったのと同じ Density Field を
// 視線方向へ積分して求める。したがって色の濃さは実際の液体の厚みそのものであり、
// 薄い液だれは明るく、壺の中の本体は深く濃い緑になる。
// alpha は常に 1。背景が透けて「緑色の膜」に見える状態 (§16) は構造的に発生しない。
// ============================================================================================
Shader "Custom/PotionLiquidSurface"
{
    Properties
    {
        _ShallowColor("Shallow (thin) Color", Color) = (0.42, 0.86, 0.30, 1)
        _BaseColor("Base Color", Color)              = (0.13, 0.52, 0.14, 1)
        _DeepColor("Deep Color", Color)              = (0.05, 0.24, 0.07, 1)
        _RimColor("Fresnel Rim Color", Color)        = (0.55, 1.0, 0.55, 1)
        _Smoothness("Smoothness", Range(0,1))        = 0.95
        _SpecIntensity("Specular Intensity", Range(0,12)) = 6.0
        _FresnelPower("Fresnel Power", Range(0.5,8)) = 3.2
        _FresnelStrength("Fresnel Strength", Range(0,2)) = 0.45
        _Translucency("Subtle internal scatter", Range(0,2)) = 0.35
        _AmbientBoost("Ambient Boost", Range(0,2))   = 0.85
        _ThicknessRef("Thickness reference (m)", Float) = 0.45
        _ThicknessSteps("Thickness samples", Range(2,24)) = 12
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+10" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "PotionLiquidForward"
            Tags { "LightMode" = "UniversalForward" }
            // Cull Off にしている理由: 四面体分解で生成した三角形は、ケースによって巻き順が
            // 揃わないことがある。法線は三角形からではなく Density Field の勾配から取っている
            // ので、巻き順に依存する必要がそもそも無い。カリングを切れば巻き順の不一致が
            // 表面の筋・欠けとして現れなくなる（不透明かつ ZWrite On なので前後関係は
            // 深度テストが正しく解決する）。
            Cull Off
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct SurfaceVertex
            {
                float3 position;
                float3 normal;
            };
            StructuredBuffer<SurfaceVertex> _SurfaceVertices;

            TEXTURE3D(_DensityField);
            SAMPLER(sampler_DensityField);
            float3 _FieldOrigin;
            float3 _FieldSize;
            float  _IsoValue;

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _BaseColor;
                float4 _DeepColor;
                float4 _RimColor;
                float _Smoothness;
                float _SpecIntensity;
                float _FresnelPower;
                float _FresnelStrength;
                float _Translucency;
                float _AmbientBoost;
                float _ThicknessRef;
                float _ThicknessSteps;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            Varyings vert(uint vid : SV_VertexID)
            {
                Varyings o;
                SurfaceVertex v = _SurfaceVertices[vid];
                o.positionWS = v.position;
                o.normalWS = v.normal;
                o.positionCS = TransformWorldToHClip(v.position);
                return o;
            }

            float SampleDensity(float3 wp)
            {
                float3 uvw = (wp - _FieldOrigin) / max(_FieldSize, 1e-5);
                if (any(uvw < 0.0) || any(uvw > 1.0)) return 0.0;
                return SAMPLE_TEXTURE3D_LOD(_DensityField, sampler_DensityField, uvw, 0).r;
            }

            // 表面から視線方向へ密度場を積分し、実際に液体が何メートル分あるかを測る。
            // 見た目のための近似ではなく、等値面を作ったのと同じ場を使う。
            float MeasureThickness(float3 startWS, float3 dirWS)
            {
                int steps = (int)_ThicknessSteps;
                float stepLen = _ThicknessRef / max(1.0, (float)steps);
                float thickness = 0.0;
                [loop] for (int i = 1; i <= steps; i++)
                {
                    float3 p = startWS + dirWS * (stepLen * i);
                    if (SampleDensity(p) > _IsoValue) thickness += stepLen;
                }
                return thickness;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetCameraPositionWS() - IN.positionWS);

                float thickness = MeasureThickness(IN.positionWS, -V);
                float thick01 = saturate(thickness / max(1e-4, _ThicknessRef));

                // 厚みで明暗が変化する (§16): 薄い部分はやや明るい緑、厚い部分は濃い深緑。
                // 深色へ振り切らせないのが肝。壺の中の液体は視線方向に 0.5m 近い厚みがあるため、
                // 厚み 1.0 で _DeepColor まで行くと本体が真っ黒になる（完成イメージは「濃いが
                // 読める緑」であって黒ではない）。深色は上限 0.55 までの色味付けに留める。
                float3 albedo = lerp(_ShallowColor.rgb, _BaseColor.rgb, saturate(thick01 * 2.4));
                albedo = lerp(albedo, _DeepColor.rgb, saturate((thick01 - 0.35) * 1.6) * 0.55);

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float3 L = mainLight.direction;
                float shadow = lerp(1.0, mainLight.shadowAttenuation, 0.5);
                float NdotL = saturate(dot(N, L));
                float3 H = normalize(L + V);
                float NdotH = saturate(dot(N, H));
                float NdotV = saturate(dot(N, V));

                float specPower = lerp(16.0, 600.0, saturate(_Smoothness));
                float spec = pow(NdotH, specPower) * _SpecIntensity * _Smoothness;

                float fresnel = pow(1.0 - NdotV, _FresnelPower) * _FresnelStrength;

                // Subtle な内部散乱感: 薄いところだけ、逆光側でほんのり明るくなる。
                float backLight = saturate(dot(-N, L) * 0.5 + 0.5);
                float3 scatter = _ShallowColor.rgb * backLight * (1.0 - thick01) * _Translucency;

                float3 ambient = SampleSH(N) * _AmbientBoost;
                float3 color = albedo * (ambient + NdotL * mainLight.color.rgb * shadow)
                             + spec * mainLight.color.rgb * shadow
                             + fresnel * _RimColor.rgb
                             + scatter;

                return half4(color, 1.0);   // 常に不透明 (§16)
            }
            ENDHLSL
        }
    }
    Fallback Off
}
