Shader "SashimiBoy/RockfishFlesh"
{
    Properties
    {
        _MainTex ("Fiber coordinates", 2D) = "white" {}
        _Color ("Flesh", Color) = (.89,.77,.68,1)
        _Glossiness ("Smoothness", Range(0,1)) = .22
        _CutPlane ("Cut plane", Vector) = (0,0,0,-1000)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        struct Input { float2 uv_MainTex; float3 worldPos; };
        sampler2D _MainTex;
        fixed4 _Color;
        half _Glossiness;
        float4 _CutPlane;
        void surf(Input IN,inout SurfaceOutputStandard o)
        {
            clip(-dot(float4(IN.worldPos,1),_CutPlane));
            float ridge=abs(sin((IN.uv_MainTex.x+abs(IN.uv_MainTex.y-.5)*.27)*140));
            float fiber=smoothstep(.88,.99,ridge);
            o.Albedo=lerp(_Color.rgb,_Color.rgb*1.13,fiber*.25);
            o.Metallic=0;o.Smoothness=_Glossiness;o.Alpha=1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
