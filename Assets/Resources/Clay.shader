Shader "Wyvernland/Unfired Clay"
{
    Properties
    {
        _BaseColor ("Clay color", Color) = (0.43,0.16,0.075,1)
        [Toggle] _TerrainColorRegions ("Terrain color regions", Float) = 0
        [HideInInspector] _LowTextureQuality ("Low texture quality", Float) = 0
        [HideInInspector] _MediumTextureQuality ("Medium texture quality", Float) = 0
        [Toggle] _UseVertexColor ("Use vertex color", Float) = 0
        [HideInInspector] _UseTerrainPitColor ("Stone pit shading", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        // Shadows.hlsl uses LerpWhiteTo from CommonMaterial, including in the
        // standalone ShadowCaster pass which does not include Lighting.hlsl.
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
        CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor;
            float4 _DarkWoods[4];
            half4 _DarkWoodColor;
            float _TerrainColorRegions;
            float _LowTextureQuality;
            float _MediumTextureQuality;
            float _UseVertexColor;
            float _UseTerrainPitColor;
            int _RiverbedPointCount;
            float4 _RiverbedPoints[80];
        CBUFFER_END
        ENDHLSL
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float _WyvernBetterLighting;
            float4 _WyvernFogColor;
            float4 _WyvernFogParams;
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; half3 normal:TEXCOORD1; half fog:TEXCOORD2; half4 color:COLOR; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.world=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.world);
                o.normal=TransformObjectToWorldNormal(v.normalOS);
                o.color=v.color;
                o.fog=ComputeFogFactor(o.positionCS.z);
                return o;
            }
            float Hash(float3 p)
            {
                // Avoid sine precision artifacts across the kilometre-scale world.
                p=frac(p*0.1031);
                p+=dot(p,p.yzx+33.33);
                return frac((p.x+p.y)*p.z);
            }
            float Noise(float3 p)
            {
                float3 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(lerp(Hash(i),Hash(i+float3(1,0,0)),f.x),
                    lerp(Hash(i+float3(0,1,0)),Hash(i+float3(1,1,0)),f.x),f.y),
                    lerp(lerp(Hash(i+float3(0,0,1)),Hash(i+float3(1,0,1)),f.x),
                    lerp(Hash(i+float3(0,1,1)),Hash(i+float3(1,1,1)),f.x),f.y),f.z);
            }
            float RegionBand(float2 p,float2 a,float2 b,float radius,float feather)
            {
                float2 edge=b-a;
                float t=saturate(dot(p-a,edge)/max(dot(edge,edge),0.0001));
                float distanceFromBand=length(p-a-edge*t);
                // Most of each marked region is a transition. Only the innermost
                // quarter reaches the maximum tint strength.
                float fullTintRadius=radius*0.24;
                return 1-smoothstep(fullTintRadius,radius+feather,distanceFromBand);
            }
            float RiverbedSegment(float2 p,float2 a,float2 b,float widthA,float widthB)
            {
                float2 edge=b-a;
                float t=saturate(dot(p-a,edge)/max(dot(edge,edge),0.0001));
                float distanceFromBed=length(p-a-edge*t);
                float radius=lerp(widthA,widthB,t)*0.5;
                return 1-smoothstep(radius*0.55,radius+5.25,
                    distanceFromBed);
            }
            float RiverbedMask(float2 p)
            {
                float weight=0;
                [loop] for(int index=0;index<79;index++)
                {
                    if(index+1>=_RiverbedPointCount) break;
                    float4 a=_RiverbedPoints[index];
                    if(a.w<0.5) continue;
                    float4 b=_RiverbedPoints[index+1];
                    weight=max(weight,RiverbedSegment(p,a.xy,b.xy,a.z,b.z));
                }
                return weight;
            }
            void AddRegion(inout float weight,inout half3 weightedColor,
                float amount,half3 color)
            {
                weight+=amount;
                weightedColor+=color*amount;
            }
            half3 TerrainRegionColor(float3 world,half3 original,half3 normal)
            {
                // Convert world position back to the 1368 x 1824 reference-photo
                // coordinates used to trace both the coastline and color marks.
                float2 photo=float2(40+(world.x+2500)/5000*1210,
                    1660-(world.z+3250)/6500*1510);
                const half3 grey=half3(0.37,0.36,0.33);
                const half3 redBrown=half3(0.52,0.12,0.085);
                const half3 yellowBrown=half3(0.66,0.40,0.17);
                float greyWeight=0,redWeight=0,yellowWeight=0;

                // Grey areas.
                greyWeight=max(greyWeight,RegionBand(photo,float2(900,270),float2(965,295),35,18));
                greyWeight=max(greyWeight,RegionBand(photo,float2(445,900),float2(550,840),70,22));
                greyWeight=max(greyWeight,RegionBand(photo,float2(965,925),float2(1045,1060),66,22));
                greyWeight=max(greyWeight,RegionBand(photo,float2(842,1215),float2(842,1370),55,20));

                // Red-brown areas.
                redWeight=max(redWeight,RegionBand(photo,float2(225,855),float2(305,795),57,22));
                redWeight=max(redWeight,RegionBand(photo,float2(305,795),float2(395,745),62,22));
                redWeight=max(redWeight,RegionBand(photo,float2(395,745),float2(475,700),62,22));
                redWeight=max(redWeight,RegionBand(photo,float2(475,700),float2(515,625),58,22));
                redWeight=max(redWeight,RegionBand(photo,float2(515,625),float2(515,555),55,22));
                redWeight=max(redWeight,RegionBand(photo,float2(325,1245),float2(400,1175),58,22));
                redWeight=max(redWeight,RegionBand(photo,float2(400,1175),float2(420,1090),62,22));
                redWeight=max(redWeight,RegionBand(photo,float2(420,1090),float2(520,1020),60,22));
                redWeight=max(redWeight,RegionBand(photo,float2(520,1020),float2(650,975),58,22));
                redWeight=max(redWeight,RegionBand(photo,float2(650,975),float2(745,925),52,22));

                // Yellow-brown areas.
                yellowWeight=max(yellowWeight,RegionBand(photo,float2(405,660),float2(400,565),37,18));
                yellowWeight=max(yellowWeight,RegionBand(photo,float2(400,565),float2(445,495),39,18));
                yellowWeight=max(yellowWeight,RegionBand(photo,float2(445,495),float2(535,450),37,18));
                yellowWeight=max(yellowWeight,RegionBand(photo,float2(920,775),float2(1025,735),38,18));
                yellowWeight=max(yellowWeight,RegionBand(photo,float2(1025,735),float2(1090,665),38,18));
                yellowWeight=max(yellowWeight,RegionBand(photo,float2(1090,665),float2(1120,570),36,18));
                yellowWeight=max(yellowWeight,RegionBand(photo,float2(405,980),float2(405,850),32,17));
                yellowWeight=max(yellowWeight,RegionBand(photo,float2(405,1300),float2(450,1200),36,18));
                yellowWeight=max(yellowWeight,RegionBand(photo,float2(450,1200),float2(455,1100),38,18));
                yellowWeight=max(yellowWeight,RegionBand(photo,float2(455,1100),float2(540,1050),38,18));
                yellowWeight=max(yellowWeight,RegionBand(photo,float2(540,1050),float2(650,1020),38,18));
                yellowWeight=max(yellowWeight,RegionBand(photo,float2(650,1020),float2(770,965),36,18));
                yellowWeight=max(yellowWeight,RegionBand(photo,float2(1090,1150),float2(1130,1050),36,18));
                yellowWeight=max(yellowWeight,RegionBand(photo,float2(1130,1050),float2(1120,900),38,18));
                yellowWeight=max(yellowWeight,RegionBand(photo,float2(1120,900),float2(1115,830),35,18));
                yellowWeight=max(yellowWeight,RegionBand(photo,float2(780,1395),float2(775,1290),31,16));
                yellowWeight=max(yellowWeight,RegionBand(photo,float2(775,1290),float2(790,1215),31,16));
                yellowWeight=max(yellowWeight,RegionBand(photo,float2(860,275),float2(860,275),34,17));
                yellowWeight=max(yellowWeight,RegionBand(photo,float2(1005,310),float2(1005,310),34,17));
                yellowWeight=max(yellowWeight,RegionBand(photo,float2(435,1530),float2(435,1530),34,17));
                yellowWeight=max(yellowWeight,RegionBand(photo,float2(540,1540),float2(540,1540),34,17));

                half3 weighted=0;
                float total=0;
                AddRegion(total,weighted,greyWeight,grey);
                AddRegion(total,weighted,redWeight,redBrown);
                AddRegion(total,weighted,yellowWeight,yellowBrown);
                half3 mixed=weighted/max(total,0.0001);
                // Painted colors affect upward terrain faces and blend back to the
                // original clay across the soft boundary of every traced mark.
                // The traced palette is a restrained tint over the original clay,
                // rather than a replacement for it.
                float upward=smoothstep(-0.12,0.28,normal.y);
                float influence=saturate(total)*upward*
                    saturate(_TerrainColorRegions)*0.096;
                half3 regional=lerp(original,mixed,influence);
                const half3 darkBrown=half3(0.18,0.065,0.025);
                float riverInfluence=RiverbedMask(world.xz)*upward*
                    saturate(_TerrainColorRegions)*0.30;
                regional=lerp(regional,darkBrown,riverInfluence);
                float woods=0;
                for(int w=0;w<4;w++)
                {
                    float2 radius=max(_DarkWoods[w].zw,float2(0.001,0.001));
                    float d=length((world.xz-_DarkWoods[w].xy)/radius);
                    woods=max(woods,1-smoothstep(0.85,1,d));
                }
                return lerp(regional,_DarkWoodColor.rgb,woods*0.5*saturate(_TerrainColorRegions));
            }
            half4 Frag(Varyings i):SV_Target
            {
                half3 n=normalize(i.normal);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord=ComputeScreenPos(TransformWorldToHClip(i.world));
                #else
                    float4 shadowCoord=TransformWorldToShadowCoord(i.world);
                #endif
                Light sun=GetMainLight(shadowCoord,i.world,half4(1,1,1,1));
                AmbientOcclusionFactor ao=GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS));
                // Real diffuse sunlight and sky irradiance, with no glossy/specular lobe.
                half3 direct=sun.color*saturate(dot(n,sun.direction))*
                    sun.distanceAttenuation*sun.shadowAttenuation*ao.directAmbientOcclusion;
                half3 ambient=max(SampleSH(n),half3(0,0,0))*ao.indirectAmbientOcclusion;
                half3 objectBase=lerp(_BaseColor.rgb,i.color.rgb,
                    saturate(_UseVertexColor)*i.color.a);
                half3 regionalBase=TerrainRegionColor(i.world,objectBase,n);
                // Terrain vertices mark excavated stone interiors; ordinary ground is white.
                regionalBase*=lerp(half3(1,1,1),i.color.rgb,saturate(_UseTerrainPitColor));
                half3 simpleLighting=(0.42+0.58*saturate(dot(n,sun.direction)))*half3(1,0.96,0.90);
                half3 lighting=lerp(simpleLighting,direct+ambient,saturate(_WyvernBetterLighting));
                if(_MediumTextureQuality>0.5)
                {
                    // Medium keeps the biome blend, then places one discrete
                    // shadow value over it without adding surface color detail.
                    float inShadow=min(sun.shadowAttenuation,
                        step(0.18,dot(n,sun.direction)));
                    lighting=lerp(half3(0.55,0.55,0.55),half3(1,1,1),
                        step(0.75,inShadow));
                }
                half3 clay=regionalBase;
                if(_LowTextureQuality<0.5&&_MediumTextureQuality<0.5)
                {
                // World-space layers have no repeating UV tile. Rotated coordinates
                // and domain warping break up the noise lattice and regular blobs.
                float3 p=float3(dot(i.world,float3(0.80,0.0,0.60)),
                    i.world.y,dot(i.world,float3(-0.60,0.0,0.80)));
                float footprint=max(length(ddx(p)),length(ddy(p)));
                float3 warp=float3(Noise(p*0.027+17.3),Noise(p.zxy*0.031+83.1),
                    Noise(p.yzx*0.023+41.7))-0.5;
                float3 q=p+warp*18;
                float broad=Noise(q*0.012+5.7);
                float patches=Noise(q*0.085+29.4);
                float clumps=Noise(q.yzx*0.63+71.2);
                float clumpFade=1-smoothstep(0.4,2.0,footprint);
                // Sparse mineral veins follow distorted strata, not a tiled crack map.
                float strata=Noise(q*float3(0.14,0.8,0.22)+103.6);
                float vein=(1-smoothstep(0.018,0.065,abs(strata-0.52))) *
                    smoothstep(0.48,0.72,patches) * clumpFade;
                float fineFade=1-smoothstep(0.035,0.18,footprint);
                float grain=Noise(p*9.3+137.5);
                float pores=smoothstep(0.64,0.82,Noise(p.zxy*5.1+219.7))*fineFade;
                clay=lerp(regionalBase*half3(0.72,0.79,0.87),
                    regionalBase*half3(1.24,1.12,0.96),smoothstep(0.18,0.82,broad));
                clay*=1+(patches-0.5)*0.38+(clumps-0.5)*0.22*clumpFade;
                clay=lerp(clay,clay*half3(1.30,1.23,1.08),vein*0.32);
                clay*=1+(grain-0.5)*0.17*fineFade-pores*0.22;
                }
                half3 litClay=clay*lighting;
                float fogDistance=distance(i.world,_WorldSpaceCameraPos);
                float runtimeFog=saturate((fogDistance-_WyvernFogParams.x)/
                    max(0.001,_WyvernFogParams.y-_WyvernFogParams.x))*_WyvernFogParams.z;
                return half4(lerp(litClay,_WyvernFogColor.rgb,runtimeFog),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            float4 ShadowVert(Attributes v):SV_POSITION
            {
                float3 world=TransformObjectToWorld(v.positionOS.xyz);
                float3 normal=TransformObjectToWorldNormal(v.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 direction=normalize(_LightPosition-world);
                #else
                    float3 direction=_LightDirection;
                #endif
                float4 clip=TransformWorldToHClip(ApplyShadowBias(world,normal,direction));
                #if UNITY_REVERSED_Z
                    clip.z=min(clip.z,UNITY_NEAR_CLIP_VALUE*clip.w);
                #else
                    clip.z=max(clip.z,UNITY_NEAR_CLIP_VALUE*clip.w);
                #endif
                return clip;
            }
            half4 ShadowFrag():SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            float4 DepthVert(float4 p:POSITION):SV_POSITION { return TransformObjectToHClip(p.xyz); }
            half DepthFrag(float4 p:SV_POSITION):SV_Target { return p.z; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings { float4 positionCS:SV_POSITION; half3 normal:TEXCOORD0; };
            Varyings NormalsVert(Attributes v)
            {
                Varyings o;
                o.positionCS=TransformObjectToHClip(v.positionOS.xyz);
                o.normal=TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 NormalsFrag(Varyings i):SV_Target
            {
                float3 n=normalize(i.normal);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct=PackNormalOctQuadEncode(n);
                    return half4(PackFloat2To888(saturate(oct*0.5+0.5)),0);
                #else
                    return half4(n,0);
                #endif
            }
            ENDHLSL
        }
    }
}
