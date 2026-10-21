Shader "Wyvernland/Collision Spark"
{
    Properties { _BaseColor("Spark glow",Color)=(2,1.6,1,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION;half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION;half4 color:COLOR; };
            Varyings Vert(Attributes input) { Varyings o;o.positionCS=TransformObjectToHClip(input.positionOS.xyz);o.color=input.color*_BaseColor;return o; }
            half4 Frag(Varyings input):SV_Target { return input.color; }
            ENDHLSL
        }
    }
}
