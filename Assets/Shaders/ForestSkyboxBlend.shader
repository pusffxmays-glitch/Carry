Shader "Carry/Forest Skybox Blend"
{
    Properties
    {
        [NoScaleOffset] _TextureA ("Forest panorama A", 2D) = "grey" {}
        [NoScaleOffset] _TextureB ("Forest panorama B", 2D) = "grey" {}
        _Blend ("Crossfade", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _TextureA, _TextureB;
            float _Blend;
            struct v2f { float4 position : SV_POSITION; float3 direction : TEXCOORD0; };
            v2f vert(float4 position : POSITION)
            {
                v2f o;
                o.position = UnityObjectToClipPos(position);
                o.direction = position.xyz;
                return o;
            }
            half4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.direction);
                float2 uv = float2(0.75 - atan2(d.z, d.x) / (2.0 * UNITY_PI),
                                   1.0 - acos(clamp(d.y, -1.0, 1.0)) / UNITY_PI);
                return half4(lerp(tex2D(_TextureA, uv).rgb, tex2D(_TextureB, uv).rgb, _Blend), 1);
            }
            ENDCG
        }
    }
}
