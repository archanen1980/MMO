Shader "MMO/OutlinedLit"
{
    Properties
    {
        _Color        ("Color", Color) = (1,1,1,1)
        _MainTex      ("Albedo", 2D)   = "white" {}
        _Glossiness   ("Smoothness", Range(0,1)) = 0.5
        _Metallic     ("Metallic",   Range(0,1)) = 0.0

        _OutlineColor ("Outline Color", Color) = (0,1,1,1)
        _OutlineWidth ("Outline Width (world)", Range(0,0.1)) = 0.02
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 300

        // ----- LIT PASS (Standard PBR) -----
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        half _Glossiness;
        half _Metallic;

        struct Input { float2 uv_MainTex; };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo     = c.rgb;
            o.Alpha      = c.a;
            o.Smoothness = _Glossiness;
            o.Metallic   = _Metallic;
        }
        ENDCG

        // ----- OUTLINE PASS (inverted hull) -----
        Pass
        {
            Name "OUTLINE"
            Tags { "LightMode"="Always" }
            Cull Front           // draw backfaces to get an expanded silhouette
            ZWrite On
            ZTest LEqual
            Offset -1, -1        // tiny depth bias to avoid z-fighting
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _OutlineColor;
            float  _OutlineWidth;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                // world-space expand along the surface normal
                float3 worldPos    = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 worldNormal = UnityObjectToWorldNormal(v.normal);
                worldPos += normalize(worldNormal) * _OutlineWidth;
                o.pos = UnityWorldToClipPos(float4(worldPos, 1));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                if (_OutlineWidth <= 0.00001) discard; // effectively off
                return _OutlineColor;
            }
            ENDCG
        }
    }

    Fallback "Diffuse"
}
