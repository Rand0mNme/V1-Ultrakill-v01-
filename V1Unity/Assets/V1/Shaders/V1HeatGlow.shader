// Nailgun barrel heat (BarrelHeat): in ULTRAKILL the edges of the barrels glow, the halo is stronger
// where the surface faces away from the camera; brightness = color alpha (heat 0..1)
Shader "V1/HeatGlow"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Power ("Fresnel Power", Float) = 3
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Blend SrcAlpha One
        Cull Back ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_ST; fixed4 _Color; float _Power;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 n : TEXCOORD1; float3 v : TEXCOORD2; };
            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.n = UnityObjectToWorldNormal(v.normal);
                o.v = normalize(WorldSpaceViewDir(v.vertex));
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float rim = pow(1 - saturate(abs(dot(normalize(i.n), normalize(i.v)))), _Power);
                fixed4 c = tex2D(_MainTex, i.uv) * _Color;
                c.a *= saturate(0.15 + rim * 0.9) * 0.7;
                return c;
            }
            ENDCG
        }
    }
}
