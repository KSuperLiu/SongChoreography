Shader "Choreo/Practice Outfit"
{
    Properties { _MainTex("Skin",2D)="white"{} }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        sampler2D _MainTex;
        struct Input { float2 uv_MainTex; float4 color:COLOR; };
        void surf(Input IN,inout SurfaceOutputStandard o)
        {
            fixed3 skin=tex2D(_MainTex,IN.uv_MainTex).rgb;
            o.Albedo=lerp(skin,IN.color.rgb,IN.color.a);
            o.Smoothness=lerp(.27,.16,IN.color.a);
            o.Metallic=0;o.Alpha=1;
        }
        ENDCG
    }
    Fallback "Standard"
}
