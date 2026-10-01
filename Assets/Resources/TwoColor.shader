Shader "Wyvernland/Two Color Shadowed"
{
    Properties
    {
        _BaseColor ("Lit color", Color) = (1,1,1,1)
        _ShadowColor ("Shadow color", Color) = (0.55,0.55,0.55,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _ShadowColor;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings
            {
                float4 positionCS:SV_POSITION;
                float3 world:TEXCOORD0;
                half3 normal:TEXCOORD1;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.world=TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS=TransformWorldToHClip(output.world);
                output.normal=TransformObjectToWorldNormal(input.normalOS);
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord=ComputeScreenPos(input.positionCS);
                #else
                    float4 shadowCoord=TransformWorldToShadowCoord(input.world);
                #endif
                Light sun=GetMainLight(shadowCoord,input.world,half4(1,1,1,1));
                float lit=min(sun.shadowAttenuation,
                    step(0.18,dot(normalize(input.normal),sun.direction)));
                return lerp(_ShadowColor,_BaseColor,step(0.75,lit));
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 ShadowVert(float4 positionOS:POSITION):SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }
            half4 ShadowFrag():SV_Target { return 0; }
            ENDHLSL
        }
    }
}
