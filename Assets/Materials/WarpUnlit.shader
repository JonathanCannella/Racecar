Shader "Warp/WarpUnlit_Streaks_Transparent"
{
    Properties
    {
        _MainTex ("MainTex", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        // Transparent draws after opaque geometry, so meshes occlude the warp.
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        ZWrite Off          // don't write depth
        ZTest LEqual        // respect depth; shows over skybox, hidden by meshes
        Blend One One       // additive glow
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;
            float4 _Color;

            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };

            v2f vert (appdata v) {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target {
                fixed4 c = tex2D(_MainTex, i.uv) * _Color;
                c.rgb *= c.a; // premultiply feel
                return c;
            }
            ENDCG
        }
    }
}