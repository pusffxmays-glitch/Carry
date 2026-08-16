// パリー成功の足元リング衝撃波 (2026-08-16 追補 20)。
// 固定サイズのクアッドの UV 上でリングを外へ走らせる (Transform のスケールは変えない)。
// 色は HDR (>1) で渡すと Bloom (PotionGlow Volume) が滲ませてくれる。
Shader "Custom/ParryRing"
{
    Properties
    {
        [HDR] _Color("Color", Color) = (1, 1, 1, 1)
        _Progress("Progress", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ParryRing"
            Blend SrcAlpha One      // 加算 (下地を明るくするだけなので何にでも重なる)
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Progress;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.uv = IN.uv;
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float d = distance(IN.uv, float2(0.5, 0.5)) * 2.0;   // 0=中心, 1=クアッド縁
                // 進行度で半径 0.12→0.95 へ走るリング。進むほど太く・薄く。
                float radius = lerp(0.12, 0.95, _Progress);
                float width = 0.10 + 0.10 * _Progress;
                float ring = 1.0 - smoothstep(0.0, width, abs(d - radius));
                float fade = (1.0 - _Progress);
                fade *= fade;
                return half4(_Color.rgb, _Color.a * ring * fade);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
