Shader "Carry/Woodland Foliage"
{
    Properties {
        _BaseMap("Baked plant",2D)="white"{}
        _Cutoff("Cutout",Range(0,1))=0.35
    }
    SubShader {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off ZWrite On
        Pass {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half _Cutoff;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; half fog:TEXCOORD1; };
            V Vert(A i) {
                V o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz);
                o.uv=i.uv; o.color=i.color; o.fog=ComputeFogFactor(o.positionCS.z); return o;
            }
            half4 Frag(V i):SV_Target {
                half4 c=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv);
                clip(c.a-_Cutoff);
                return half4(MixFog(c.rgb*i.color.rgb,i.fog),1);
            }
            ENDHLSL
        }
    }
}
