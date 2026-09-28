Shader "SashimiBoy/Stage01FixedFilletSurface"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal", 2D) = "bump" {}
        _MetallicGlossMap ("Metallic / Smoothness", 2D) = "white" {}
        _GlossMapScale ("Smoothness Scale", Range(0,1)) = 1
        _CutPlane ("Authored world plane", Vector) = (0,1,0,-1000)
        _Glossiness ("Smoothness", Range(0,1)) = .25
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0
        sampler2D _MainTex, _BumpMap, _MetallicGlossMap;
        float4 _CutPlane;
        half _Glossiness, _GlossMapScale;
        struct Input { float2 uv_MainTex; float2 uv_BumpMap; float3 worldPos; };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            clip(-dot(float4(IN.worldPos, 1), _CutPlane));
            o.Albedo = tex2D(_MainTex, IN.uv_MainTex).rgb;
            fixed3 normal = UnpackNormal(tex2D(_BumpMap, IN.uv_BumpMap));
            normal.xy *= .35;
            o.Normal = normalize(normal);
            fixed4 packed = tex2D(_MetallicGlossMap, IN.uv_MainTex);
            o.Metallic = 0;
            o.Smoothness = min(.4, packed.a * _GlossMapScale);
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
