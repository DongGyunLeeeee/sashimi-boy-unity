Shader "SashimiBoy/Stage01FailureBlur"
{
    Properties { _MainTex ("Scene", 2D) = "white" {} _Amount ("Amount", Range(0,1)) = 0 }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Amount;
            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 d = _MainTex_TexelSize.xy * (2 + 10 * _Amount);
                fixed4 c = tex2D(_MainTex, i.uv) * .2;
                c += tex2D(_MainTex, i.uv + d * float2(1,0)) * .1;
                c += tex2D(_MainTex, i.uv - d * float2(1,0)) * .1;
                c += tex2D(_MainTex, i.uv + d * float2(0,1)) * .1;
                c += tex2D(_MainTex, i.uv - d * float2(0,1)) * .1;
                c += tex2D(_MainTex, i.uv + d) * .1;
                c += tex2D(_MainTex, i.uv - d) * .1;
                c += tex2D(_MainTex, i.uv + d * float2(1,-1)) * .1;
                c += tex2D(_MainTex, i.uv + d * float2(-1,1)) * .1;
                float vignette = saturate(dot(i.uv - .5, i.uv - .5) * 2);
                c.rgb *= 1 - _Amount * (.45 + .5 * vignette);
                return c;
            }
            ENDCG
        }
    }
}
