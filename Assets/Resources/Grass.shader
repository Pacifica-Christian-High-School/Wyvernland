Shader "Wyvernland/Pixel Grass"
{
    Properties
    {
        _DrawDistance ("Draw distance", Float) = 80
        _FadeStart ("Distance fade start", Float) = 36
        _FadeDuration ("Load fade duration", Float) = 1.25
        [HideInInspector] _SpawnTime ("Spawn time", Float) = 0
        [HideInInspector] _MediumTextureQuality ("Medium texture quality", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _DrawDistance;
            float _FadeStart;
            float _FadeDuration;
            float _SpawnTime;
            float _MediumTextureQuality;
            CBUFFER_END
            float4 _WyvernFogColor;
            float4 _WyvernFogParams;
            struct Attributes { float4 positionOS:POSITION; half4 color:COLOR; float3 shapeUV:TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS:SV_POSITION;
                half4 color:COLOR;
                float3 shapeUV:TEXCOORD0;
                float3 world:TEXCOORD1;
                half fog:TEXCOORD2;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.world=TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS=TransformWorldToHClip(output.world);
                output.color=input.color;
                output.shapeUV=input.shapeUV;
                output.fog=ComputeFogFactor(output.positionCS.z);
                return output;
            }

            float PixelRect(float2 pixel,float4 rect)
            {
                return step(rect.x,pixel.x)*step(rect.y,pixel.y)*
                    (1-step(rect.x+rect.z,pixel.x))*(1-step(rect.y+rect.w,pixel.y));
            }

            float ShapeMask(float3 shapeUV)
            {
                int type=(int)round(shapeUV.z);
                float2 dimensions=type==0?float2(11,9):
                    (type==1?float2(13,9):float2(14,13));
                float2 pixel=min(floor(saturate(shapeUV.xy)*dimensions),dimensions-1);
                float mask=0;
                if(type==0)
                {
                    mask+=PixelRect(pixel,float4(0,4,1,3));
                    mask+=PixelRect(pixel,float4(1,0,1,5));
                    mask+=PixelRect(pixel,float4(4,0,1,9));
                    mask+=PixelRect(pixel,float4(6,0,1,3));
                    mask+=PixelRect(pixel,float4(7,2,1,5));
                    mask+=PixelRect(pixel,float4(10,0,1,7));
                }
                else if(type==1)
                {
                    mask+=PixelRect(pixel,float4(0,3,2,2));
                    mask+=PixelRect(pixel,float4(1,1,2,2));
                    mask+=PixelRect(pixel,float4(2,0,2,1));
                    mask+=PixelRect(pixel,float4(4,0,1,9));
                    mask+=PixelRect(pixel,float4(3,7,1,2));
                    mask+=PixelRect(pixel,float4(7,0,1,5));
                    mask+=PixelRect(pixel,float4(8,3,1,2));
                    mask+=PixelRect(pixel,float4(11,0,1,8));
                    mask+=PixelRect(pixel,float4(12,6,1,2));
                }
                else
                {
                    mask+=PixelRect(pixel,float4(0,4,2,3));
                    mask+=PixelRect(pixel,float4(1,7,1,1));
                    mask+=PixelRect(pixel,float4(1,3,2,1));
                    mask+=PixelRect(pixel,float4(2,0,1,3));
                    mask+=PixelRect(pixel,float4(3,11,1,2));
                    mask+=PixelRect(pixel,float4(4,12,1,1));
                    mask+=PixelRect(pixel,float4(5,0,1,13));
                    mask+=PixelRect(pixel,float4(7,0,1,5));
                    mask+=PixelRect(pixel,float4(8,4,1,2));
                    mask+=PixelRect(pixel,float4(9,5,1,1));
                    mask+=PixelRect(pixel,float4(10,6,1,1));
                    mask+=PixelRect(pixel,float4(11,4,1,1));
                    mask+=PixelRect(pixel,float4(12,0,1,4));
                    mask+=PixelRect(pixel,float4(13,3,1,1));
                }
                return saturate(mask);
            }

            half4 Frag(Varyings input):SV_Target
            {
                // One quad carries the original pixel silhouette. Clipping its
                // unused pixels reduces each clump from 6–14 quads to one.
                clip(ShapeMask(input.shapeUV)-0.5);
                float fade=1-smoothstep(_FadeStart,_DrawDistance,
                    distance(input.world,_WorldSpaceCameraPos));
                float loadFade=smoothstep(0,1,saturate((_Time.y-_SpawnTime)/max(_FadeDuration,0.001)));
                float runtimeFog=saturate((distance(input.world,_WorldSpaceCameraPos)-_WyvernFogParams.x)/
                    max(0.001,_WyvernFogParams.y-_WyvernFogParams.x))*_WyvernFogParams.z;
                half3 grassColor=input.color.rgb;
                if(_MediumTextureQuality>0.5)
                {
                    const half3 litGrass=half3(0.43,0.16,0.055);
                    grassColor=litGrass;
                }
                half3 color=lerp(grassColor,_WyvernFogColor.rgb,runtimeFog);
                return half4(color,input.color.a*fade*loadFade);
            }
            ENDHLSL
        }
    }
}
