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
    //
    // Alpha raised 2026-08-12, then made fully OPAQUE 2026-08-12 (third pass, "透明感変わってない。
    // 上のほうの透明度高すぎて...透けて奥のツボのふちが見えちゃってる。透明感なくていいよ"): raising
    // just the _BaseColor alpha property default wasn't enough because the actual Material ASSET
    // (Mat_PotionLiquid) had its own serialized _BaseColor override baked in, which a shader
    // Properties default can never retroactively change (same class of bug as the "changing a C#
    // field's default doesn't update an already-serialized scene instance" gotcha this project has
    // hit repeatedly -- see WORKLOG.md -- just for materials instead of components). Rather than
    // keep chasing alpha values, the user explicitly said transparency isn't wanted at all, so the
    // whole Blend/ZWrite setup below was switched to a normal opaque surface -- alpha is now
    // structurally irrelevant to how this renders, so no material-asset override can ever bring back
    // see-through again.
    Properties
    {
        _BaseColor("Base Color", Color) = (0.20, 0.70, 0.24, 1)
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
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        ZWrite On
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
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 viewDirWS   : TEXCOORD2;
                float crest        : TEXCOORD3;
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
                // PotionLiquid.cs bakes each vertex's wave-impulse height (crest positive, trough
                // negative, flat = 0.5) into vertex color R -- unpack back to a signed -1..+1 value
                // so crests can get a subtle highlight/extra-gloss and troughs a touch more depth,
                // reinforcing the mesh's own mountain/valley shape instead of a flat green plane.
                OUT.crest = IN.color.r * 2.0 - 1.0;
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

                // Crest/trough modulation from the mesh's own baked wave data -- pushed much stronger
                // 2026-08-12 (see Properties comment above) so the wave shape reads clearly on its
                // own, independent of transparency/lighting angle: wave peaks are both glossier AND
                // directly brightened/tinted, troughs are both deeper-colored AND directly darkened.
                float crestPos = saturate(IN.crest);
                float troughPos = saturate(-IN.crest);
                float smoothBoosted = saturate(_Smoothness + crestPos * 0.2);
                float spec = pow(NdotH, lerp(8.0, 128.0, smoothBoosted)) * smoothBoosted;

                float fresnel = pow(1.0 - saturate(dot(N, V)), _FresnelPower);

                float3 ambient = SampleSH(N) * 0.6;

                float3 baseCol = lerp(_DeepColor.rgb, _BaseColor.rgb, NdotL * 0.7 + 0.3);
                baseCol = lerp(baseCol, _DeepColor.rgb, troughPos * 0.6);
                // Direct, light-independent brightness push so crests/troughs are legible even under
                // flat ambient lighting, not only when catching specular highlights.
                baseCol *= (1.0 + crestPos * 0.3 - troughPos * 0.25);

                float3 color = baseCol * (ambient + NdotL * mainLight.color.rgb)
                             + spec * mainLight.color.rgb
                             + fresnel * _FresnelColor.rgb
                             + crestPos * _FresnelColor.rgb * 0.35;

                // Fully opaque now (see Properties comment) -- alpha is always 1 regardless of
                // _BaseColor.a or fresnel, so a stray material-asset override can't reintroduce
                // see-through.
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
