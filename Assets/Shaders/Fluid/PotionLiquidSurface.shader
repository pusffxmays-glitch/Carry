// ============================================================================================
// Custom/PotionLiquidSurface -- Liquid Rendering (Phase 3)
//
// FLUID_DESIGN.md §14 / 仕様 §15 §16。
//
// 完成イメージ（ポーション＿神聖.png）の液体マテリアル要件:
//   深い青色（神聖感のある発光） / 内部から光る（透過＋自己発光） / 高い Smoothness /
//   明瞭な Specular Highlight / Fresnel による輪郭の強調 / 厚みで内部が翳る /
//   Subtle な内部散乱感
//
// 「厚み」は推測ではなく、Surface Reconstruction に使ったのと同じ Density Field を
// 視線方向へ積分して求める。したがって色の濃さも発光の強さも実際の液体の厚みそのもので、
// 薄い液だれは明るく光り、壺の中の本体は深い青へ沈む。
// alpha は常に 1。背景が透けて「青色の膜」に見える状態 (§16) は構造的に発生しない。
//
// 発光は「内部から光っている」ように見せるためのもので、光源に依存しない項として足す。
// 影の中でも消えないので、暗い城の中でも神聖な光り方になる。
// ============================================================================================
Shader "Custom/PotionLiquidSurface"
{
    Properties
    {
        // 深い青。薄い部分は水色〜白に近く、厚い部分は濃紺へ沈む。
        _ShallowColor("Shallow (thin) Color", Color) = (0.16, 0.56, 1.00, 1)
        _BaseColor("Base Color", Color)              = (0.01, 0.11, 0.85, 1)
        _DeepColor("Deep Color", Color)              = (0.01, 0.04, 0.26, 1)
        _RimColor("Fresnel Rim Color", Color)        = (0.45, 0.82, 1.00, 1)
        _Smoothness("Smoothness", Range(0,1))        = 0.96
        _SpecIntensity("Specular Intensity", Range(0,12)) = 7.5
        _FresnelPower("Fresnel Power", Range(0.5,8)) = 2.8
        _FresnelStrength("Fresnel Strength", Range(0,2)) = 0.85
        _Translucency("Subtle internal scatter", Range(0,2)) = 0.30
        _AmbientBoost("Ambient Boost", Range(0,2))   = 0.30

        // 内部からの発光 (神聖感)。光源に依存しない項として足すので影の中でも光る。
        _EmissionColor("Inner Glow Color", Color)    = (0.08, 0.34, 1.00, 1)
        _EmissionStrength("Inner Glow Strength", Range(0,4)) = 0.70
        _GlowRise("Glow Rise (薄いほど早く光る)", Range(0.5,12)) = 5.0
        _GlowAbsorb("Glow Absorption (厚いほど翳る)", Range(0,6)) = 2.2
        _CoreGlow("Core Glow (厚い中心のほのかな光)", Range(0,1)) = 0.18

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

            // 密度場は 3D テクスチャではなく **Sparse Brick Pool** から読む (§14)。
            // FluidSurface.compute と同じ間接参照を使う。ここを 3D テクスチャのままに
            // すると、等値面は部屋全体に出るのに厚み（＝色と発光）だけが壺の周りでしか
            // 出ない、という壊れ方をする。
            StructuredBuffer<uint>  _BrickSlot;
            StructuredBuffer<float> _Pool;
            float3 _VoxelDims;
            float3 _BrickDims;
            float  _VoxelSize;
            float  _PoolCapacity;
            float3 _FieldOrigin;
            float3 _FieldSize;
            float  _IsoValue;

            #define POOL_BRICK_VOXELS 512

            float ReadField(int3 v)
            {
                int3 dims = (int3)_VoxelDims;
                if (any(v < 0) || any(v >= dims)) return 0.0;
                uint3 b = (uint3)v >> 3u;
                uint bd = (uint)_BrickDims.x;
                uint bdy = (uint)_BrickDims.y;
                uint bi = b.x + b.y * bd + b.z * bd * bdy;
                uint slot = _BrickSlot[bi];
                if (slot >= (uint)_PoolCapacity) return 0.0;     // 未割当 = そこに液体は無い
                uint3 l = (uint3)v & 7u;
                return _Pool[slot * POOL_BRICK_VOXELS + l.x + l.y * 8u + l.z * 64u];
            }

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
                float4 _EmissionColor;
                float _EmissionStrength;
                float _GlowRise;
                float _GlowAbsorb;
                float _CoreGlow;
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

            // プールにはハードウェアのフィルタリングが無いので手でトリリニア補間する。
            float SampleDensity(float3 wp)
            {
                float3 f = (wp - _FieldOrigin) / _VoxelSize - 0.5;
                int3 b = (int3)floor(f);
                float3 t = f - (float3)b;

                float c000 = ReadField(b + int3(0,0,0));
                float c100 = ReadField(b + int3(1,0,0));
                float c010 = ReadField(b + int3(0,1,0));
                float c110 = ReadField(b + int3(1,1,0));
                float c001 = ReadField(b + int3(0,0,1));
                float c101 = ReadField(b + int3(1,0,1));
                float c011 = ReadField(b + int3(0,1,1));
                float c111 = ReadField(b + int3(1,1,1));

                float c00 = lerp(c000, c100, t.x);
                float c10 = lerp(c010, c110, t.x);
                float c01 = lerp(c001, c101, t.x);
                float c11 = lerp(c011, c111, t.x);
                return lerp(lerp(c00, c10, t.y), lerp(c01, c11, t.y), t.z);
            }

            // 表面から視線方向へ密度場を辿り、実際に液体が何メートル分あるかを測る。
            // 見た目のための近似ではなく、等値面を作ったのと同じ場を使う。
            //
            // **裏側の等値面を横切る位置は線形補間で求める。**
            // 以前は「サンプル点が内側なら stepLen を足す」という数え方をしていたので、
            // 厚みが stepLen (既定 0.45m / 12 = 37.5mm) 刻みに量子化されていた。
            // 厚みは色と発光をそのまま決めるため、量子化がそのまま**等高線状の縞**として
            // 液面に出る（ユーザー報告の「線上のノイズ」）。交点を補間すれば厚みは連続に
            // なり、縞は原理的に発生しない。サンプル数を増やすより正確でもある。
            float MeasureThickness(float3 startWS, float3 dirWS)
            {
                int steps = (int)_ThicknessSteps;
                float stepLen = _ThicknessRef / max(1.0, (float)steps);

                // 始点は表面そのものなので、ここでの密度はほぼ _IsoValue。
                float prevD = SampleDensity(startWS);
                float travelled = 0.0;

                [loop] for (int i = 1; i <= steps; i++)
                {
                    float ti = stepLen * i;
                    float d = SampleDensity(startWS + dirWS * ti);
                    if (d <= _IsoValue)
                    {
                        // 直前のサンプルとの間で等値面を抜けた。その交点までの距離が厚み。
                        float f = saturate((prevD - _IsoValue) / max(1e-6, prevD - d));
                        return ti - stepLen + f * stepLen;
                    }
                    prevD = d;
                    travelled = ti;
                }
                return travelled;   // 参照厚みより厚い = 飽和
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetCameraPositionWS() - IN.positionWS);

                float thickness = MeasureThickness(IN.positionWS, -V);
                float thick01 = saturate(thickness / max(1e-4, _ThicknessRef));

                // 厚みで明暗が変化する (§16): 薄い部分は明るい水色、厚い部分は深い青へ沈む。
                // 深色へ振り切らせないのが肝。壺の中の液体は視線方向に 0.5m 近い厚みがあるため、
                // 厚み 1.0 で _DeepColor まで行くと本体が真っ黒になる（完成イメージは「深いが
                // 読める青」であって黒ではない）。深色は上限 0.55 までの色味付けに留める。
                float3 albedo = lerp(_ShallowColor.rgb, _BaseColor.rgb, saturate(thick01 * 2.4));
                albedo = lerp(albedo, _DeepColor.rgb, saturate((thick01 - 0.22) * 1.9) * 0.80);

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

                // --- 内部からの発光 (神聖感) ---
                // 液体が薄いところほど早く明るくなり(_GlowRise)、厚くなるほど吸収されて
                // 翳る(_GlowAbsorb)。厚い中心にもわずかな残光(_CoreGlow)を残すことで、
                // 「表面が光っている」ではなく「中で光っているものが透けている」ように見せる。
                // 光源に依存しない項なので、影の中でも神聖な光り方が保たれる。
                float rise = saturate(thick01 * _GlowRise);
                float absorb = exp(-max(0.0, thick01 - 0.18) * _GlowAbsorb);
                float glow = rise * lerp(_CoreGlow, 1.0, absorb);
                float3 emission = _EmissionColor.rgb * (_EmissionStrength * glow);

                float3 ambient = SampleSH(N) * _AmbientBoost;
                float3 color = albedo * (ambient + NdotL * mainLight.color.rgb * shadow)
                             + spec * mainLight.color.rgb * shadow
                             + fresnel * _RimColor.rgb
                             + scatter
                             + emission;

                return half4(color, 1.0);   // 常に不透明 (§16)
            }
            ENDHLSL
        }
    }
    Fallback Off
}
