Shader "Hidden/Wyvernland/Terrain Region Bake"
{
    SubShader
    {
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            half4 _BaseColor;
            half4 _DarkWoodColor;
            float4 _DarkWoods[4];
            int _RiverbedPointCount;
            float4 _RiverbedPoints[80];
            #include "ClayRegions.hlsl"
            struct Input { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct Output { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; };
            Output Vert(Input input)
            {
                Output output;
                output.vertex=TransformObjectToHClip(input.vertex.xyz);
                output.uv=input.uv;
                return output;
            }
            half4 Frag(Output input):SV_Target
            {
                float3 world=float3(input.uv.x*5000-2500,0,input.uv.y*6500-3250);
                return BakeTerrainRegionColor(world,_BaseColor.rgb);
            }
            ENDHLSL
        }
    }
}
