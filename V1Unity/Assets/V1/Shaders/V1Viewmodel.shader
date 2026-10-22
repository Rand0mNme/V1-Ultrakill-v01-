// V1's weapons and arm. In ULTRAKILL the weapon is drawn by a separate camera on top of the frame, without fog or post-processing,
// and its lighting is bright and almost independent of the level. Same here: the mod draws the arm with a command buffer
// after all RoR2 post-processing, and the light is its own: texture color x soft light from a fixed direction
// relative to the camera + an emissive texture (_EmissionMap).
Shader "V1/Viewmodel"
{
    Properties
    {
        _MainTex ("Base", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _EmissionMap ("Emission", 2D) = "black" {}
        _EmissionColor ("Emission Color", Color) = (1,1,1,1)
        _Ambient ("Ambient", Range(0,1.5)) = 0.6
        _Diffuse ("Diffuse", Range(0,1.5)) = 0.5
        _OffsetFactor ("Depth Offset Factor", Float) = 0
        _OffsetUnits ("Depth Offset Units", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="Always" }
            // the battery screen lies flush against the body; a small depth offset keeps the body from covering it
            Offset [_OffsetFactor], [_OffsetUnits]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_ST; float4 _Color;
            sampler2D _EmissionMap; float4 _EmissionColor;
            float _Ambient; float _Diffuse;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 nview : TEXCOORD1; };
            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.nview = normalize(mul((float3x3)UNITY_MATRIX_IT_MV, v.normal));
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float3 l = normalize(float3(0.35, 0.6, 0.7)); // in camera space +z points toward the camera
                float ndl = saturate(dot(normalize(i.nview), l));
                fixed4 c = tex2D(_MainTex, i.uv) * _Color;
                c.rgb = c.rgb * (_Ambient + _Diffuse * ndl) + tex2D(_EmissionMap, i.uv).rgb * _EmissionColor.rgb;
                c.a = 1;
                return c;
            }
            ENDCG
        }
    }
}
