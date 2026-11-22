Shader "Silence1/Section stock"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Surface { float4 vertex : POSITION; float3 normal : NORMAL; float4 color : COLOR; };
            struct Lit { float4 position : SV_POSITION; float3 tint : TEXCOORD0; };
            Lit vert(Surface v)
            {
                Lit o;
                o.position = UnityObjectToClipPos(v.vertex);
                float3 n = UnityObjectToWorldNormal(v.normal);
                float sun = saturate(dot(n,normalize(float3(-.35,.76,-.55))));
                float3 world = mul(unity_ObjectToWorld,v.vertex).xyz;
                float depth = .78 + .22 * smoothstep(.824,.91,world.y);
                float3 ink = v.color.rgb * (.74 + .26 * sun) * depth;
                #ifndef UNITY_COLORSPACE_GAMMA
                ink = GammaToLinearSpace(ink);
                #endif
                o.tint = ink;
                return o;
            }
            float4 frag(Lit i) : SV_Target { return float4(i.tint,1); }
            ENDHLSL
        }
    }
}
