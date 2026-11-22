Shader "Silence1/Section glass"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Cull Back ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Glass { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct Surface { float4 pos : SV_POSITION; float3 normal : TEXCOORD0; float3 world : TEXCOORD1; };
            Surface vert(Glass v)
            {
                Surface o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.world = mul(unity_ObjectToWorld,v.vertex).xyz;
                return o;
            }
            float4 frag(Surface i) : SV_Target
            {
                float cap = saturate(i.normal.y);
                float stripe = 1 - smoothstep(.004,.027,abs(frac((i.world.x+.32*i.world.y)*4)-.45));
                float3 tint = lerp(float3(.41,.59,.56),float3(.72,.86,.78), cap);
                tint += stripe * .09;
                #ifndef UNITY_COLORSPACE_GAMMA
                tint = GammaToLinearSpace(tint);
                #endif
                return float4(tint,lerp(.28,.92,cap));
            }
            ENDHLSL
        }
    }
}
