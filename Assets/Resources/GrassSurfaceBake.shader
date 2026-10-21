Shader "Hidden/Wyvernland/Grass Surface Bake"
{
    SubShader
    {
        Pass
        {
            Cull Off ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _GrassMapBounds;
            struct Input { float4 vertex:POSITION; float3 normal:NORMAL; };
            struct Output { float4 position:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; };
            Output Vert(Input input)
            {
                Output output;
                output.world=TransformObjectToWorld(input.vertex.xyz);
                output.normal=TransformObjectToWorldNormal(input.normal);
                float2 uv=(output.world.xz-_GrassMapBounds.xy)*_GrassMapBounds.zw;
                float depth=saturate((600-output.world.y)/1600);
                #if UNITY_UV_STARTS_AT_TOP
                    uv.y=1-uv.y;
                #endif
                output.position=float4(uv*2-1,depth,1);
                return output;
            }
            float4 Frag(Output input):SV_Target
            {
                clip(input.normal.y-.25);
                return float4(input.world.y,normalize(input.normal));
            }
            ENDHLSL
        }
    }
}
