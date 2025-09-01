Shader "MMO/Outline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1,1,0,1)
        _OutlineWidth ("Outline Width", Range(0,0.05)) = 0.015
    }
    SubShader
    {
        // Draw after opaque so it sits on top nicely
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" }
        // Backface expanded to create silhouette
        Cull Front
        ZWrite On
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            float _OutlineWidth;
            float4 _OutlineColor;

            v2f vert (appdata v)
            {
                // Expand vertices along normals in object space
                v2f o;
                float3 n = normalize(v.normal);
                float4 pos = v.vertex;
                pos.xyz += n * _OutlineWidth;
                o.pos = UnityObjectToClipPos(pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return _OutlineColor;
            }
            ENDCG
        }
    }
    Fallback Off
}
