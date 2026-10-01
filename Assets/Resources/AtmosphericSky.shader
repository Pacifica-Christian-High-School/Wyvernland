Shader "Wyvernland/Atmospheric Sky"
{
    Properties
    {
        [HDR] _ZenithColor ("Zenith", Color) = (0.075, 0.35, 0.78, 1)
        [HDR] _HorizonColor ("Horizon", Color) = (1, 1, 1, 1)
        [HDR] _GroundColor ("Lower sky", Color) = (1, 1, 1, 1)
        [HDR] _SunColor ("Sun", Color) = (4.5, 3.6, 2.5, 1)
        _SunDirection ("Sun direction", Vector) = (0, 1, 0, 0)
        _SunSize ("Sun size", Range(0.1, 3)) = 0.65
        _Haze ("Horizon haze", Range(0, 1)) = 0.42
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "RenderPipeline"="UniversalPipeline" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half4 _ZenithColor;
            half4 _HorizonColor;
            half4 _GroundColor;
            half4 _SunColor;
            float4 _SunDirection;
            float _SunSize;
            float _Haze;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                float height = direction.y;

                // The entire lower hemisphere stays pure white. Above the horizon,
                // smoothstep begins with a zero slope so no seam appears at y = 0.
                float upperBlend = smoothstep(0.0, 0.62, saturate(height));
                upperBlend = pow(upperBlend, 0.72);
                half3 upperSky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, upperBlend);
                half3 sky = height > 0.0 ? upperSky : _GroundColor.rgb;

                // A wide white haze extends into the blue half without tinting the
                // lower hemisphere or creating a sharp horizon band.
                float horizonHaze = exp(-max(height, 0.0) * 7.0) * _Haze;
                sky = height > 0.0 ? lerp(sky, _HorizonColor.rgb, horizonHaze) : sky;

                float sunDot = dot(direction, normalize(_SunDirection.xyz));
                float outerAngle = radians(_SunSize * 1.8);
                float innerAngle = radians(_SunSize * 0.72);
                float sunDisc = smoothstep(cos(outerAngle), cos(innerAngle), sunDot);
                float sunHalo = pow(saturate(sunDot), 280.0) * 0.23;
                sky += _SunColor.rgb * (sunDisc + sunHalo) * smoothstep(-0.08, 0.05, height);

                return half4(sky, 1.0);
            }
            ENDHLSL
        }
    }
}
