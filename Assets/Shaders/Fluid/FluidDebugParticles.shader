// PHASE 1 DEBUG ONLY -- 完成形では使用しない。
//
// 仕様 §10「Particle を直接表示しない」に照らして: これは Phase 1（物理が安定して動くか）を
// 検証するための開発用ビューであり、最終的な液体表現ではない。Phase 2 で Density Field +
// Marching Cubes の Surface が入った時点で、この表示は既定でオフになる。
Shader "Hidden/Fluid/DebugParticles"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+1" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "FluidDebugParticles"
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            StructuredBuffer<float3> _Points;
            float _PointRadius;
            int _PointCount;
            float4 _PointColor;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 centerVS   : TEXCOORD1;
            };

            struct FragOut { half4 color : SV_Target; float depth : SV_Depth; };

            Varyings vert(uint vid : SV_VertexID)
            {
                Varyings o;
                uint pid = vid / 6;
                uint corner = vid % 6;
                const float2 offsets[6] =
                {
                    float2(-1,-1), float2(-1,1), float2(1,-1),
                    float2(1,-1),  float2(-1,1), float2(1,1)
                };
                float2 off = offsets[corner];
                float3 posWS = (pid < (uint)_PointCount) ? _Points[pid] : float3(0, -1e6, 0);
                float3 cVS = TransformWorldToView(posWS);
                o.centerVS = cVS;
                o.uv = off;
                o.positionCS = TransformWViewToHClip(cVS + float3(off * _PointRadius, 0));
                return o;
            }

            FragOut frag(Varyings i)
            {
                FragOut o;
                float r2 = dot(i.uv, i.uv);
                if (r2 > 1.0) discard;

                float3 n = float3(i.uv, sqrt(1.0 - r2));
                float3 vs = i.centerVS + float3(0, 0, n.z * _PointRadius);
                float3 nWS = normalize(mul((float3x3)UNITY_MATRIX_I_V, n));

                Light L = GetMainLight();
                float ndl = saturate(dot(nWS, L.direction)) * 0.8 + 0.2;
                o.color = half4(_PointColor.rgb * ndl, 1);

                float4 cs = TransformWViewToHClip(vs);
                o.depth = cs.z / cs.w;
                return o;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
