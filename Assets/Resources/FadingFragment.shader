Shader "Wyvernland/Fading Fragment"
{
    Properties
    {
        _BaseColor ("Fragment color", Color) = (1,1,1,1)
        _FadeAlpha ("Fade", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _FadeAlpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.color=input.color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                return half4(_BaseColor.rgb*input.color.rgb,
                    _BaseColor.a*input.color.a*_FadeAlpha);
            }
            ENDHLSL
        }
    }
}
