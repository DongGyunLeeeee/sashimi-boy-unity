Shader "SashimiBoy/Stage02RockfishSurface"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Owner base colour", 2D) = "white" {}
        _BumpMap ("Owner normal", 2D) = "bump" {}
        _BumpScale ("Normal strength", Range(0,1)) = .4
        _RoughnessMap ("Owner roughness (linear)", 2D) = "white" {}
        _MetallicMap ("Owner metallic (linear)", 2D) = "black" {}
        _CutPlane ("Authored world cut plane", Vector) = (0,0,0,-1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0
        #pragma shader_feature_local _NORMALMAP
        sampler2D _MainTex, _BumpMap, _RoughnessMap, _MetallicMap;
        fixed4 _Color;
        half _BumpScale;
        float4 _CutPlane;
        struct Input { float2 uv_MainTex; float2 uv_BumpMap; float3 worldPos; };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            // The same world-space plane used by the existing fillet-cut animation.
            clip(-dot(float4(IN.worldPos, 1), _CutPlane));
            o.Albedo = tex2D(_MainTex, IN.uv_MainTex).rgb * _Color.rgb;
            fixed3 normal = UnpackNormal(tex2D(_BumpMap, IN.uv_BumpMap));
            normal.xy *= _BumpScale;
            o.Normal = normalize(normal);
            o.Smoothness = 1 - saturate(tex2D(_RoughnessMap, IN.uv_MainTex).r);
            o.Metallic = saturate(tex2D(_MetallicMap, IN.uv_MainTex).r);
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
