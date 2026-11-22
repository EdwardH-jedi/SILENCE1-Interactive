Shader "Silence1/Architectural Ink"
{
    Properties
    {
        _MainTex ("Image", 2D) = "white" {}
        _Color ("Paper or ink", Color) = (1,1,1,1)
        _Relief ("Profile shading", Range(0,1)) = 0
        [HideInInspector] _DstBlend ("Destination blend", Float) = 0
        [HideInInspector] _SrcBlend ("Source blend", Float) = 1
        [HideInInspector] _ZWrite ("Depth write", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Cull Off
            ZWrite [_ZWrite]
            Blend [_SrcBlend] [_DstBlend]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _Color;
            float _Relief, _SashShadowX;
            struct Input { float4 vertex : POSITION; float2 uv : TEXCOORD0; float3 normal : NORMAL; };
            struct Varyings { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float shade : TEXCOORD1; float3 world : TEXCOORD2; };
            Varyings vert(Input v) {
                Varyings o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.world = mul(unity_ObjectToWorld,v.vertex).xyz;
                float3 n = UnityObjectToWorldNormal(v.normal);
                o.shade = lerp(1, .68 + .32 * saturate(dot(n,normalize(float3(-.35,.65,-.85)))), _Relief);
                return o;
            }
            half4 frag(Varyings i) : SV_Target {
                half4 c = tex2D(_MainTex, i.uv) * _Color;
                float atSill = 1 - smoothstep(.012,.10,abs(i.world.y-.77));
                float atStile = 1 - smoothstep(.008,.060,abs(i.world.x-_SashShadowX-.018));
                c.rgb *= i.shade * (1 - .16 * atSill * atStile * _Relief);
                if (_Color.a < .99) {
                    float reflection = 1 - smoothstep(.015,.11,abs(i.world.x*.65+i.world.y*.24+.27));
                    c.rgb = lerp(c.rgb,1,.23*reflection);
                    c.a *= .86 + .36*reflection;
                }
                return c;
            }
            ENDHLSL
        }
    }
}
