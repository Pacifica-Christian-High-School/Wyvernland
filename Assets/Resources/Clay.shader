Shader "Wyvernland/Unfired Clay"
{
    Properties
    {
        _BaseColor ("Clay color", Color) = (0.43,0.16,0.075,1)
        [HideInInspector] _DarkWoodColor ("Dark woodland color", Color) = (0.1,0.04,0.02,1)
        [Toggle] _TerrainColorRegions ("Terrain color regions", Float) = 0
        [HideInInspector] _LowTextureQuality ("Low texture quality", Float) = 0
        [HideInInspector] _MediumTextureQuality ("Medium texture quality", Float) = 0
        [Toggle] _UseVertexColor ("Use vertex color", Float) = 0
        [HideInInspector] _ResidentPropDraw ("Resident prop draw", Float) = 0
        [HideInInspector] _FlatTerrainNormal ("Flat terrain faces", Float) = 0
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
            half4 _DarkWoodColor;
            float _TerrainColorRegions;
            float _LowTextureQuality;
            float _MediumTextureQuality;
            float _UseVertexColor;
            float _ResidentPropDraw;
            float _FlatTerrainNormal;
            float _UseTerrainPitColor;
        CBUFFER_END
        TEXTURE3D(_ClayNoise); SAMPLER(sampler_ClayNoise);
        TEXTURE2D(_TerrainRegionMap); SAMPLER(sampler_TerrainRegionMap);
        ENDHLSL
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
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
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; half3 normal:TEXCOORD1; half fog:TEXCOORD2; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            Varyings Vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
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
                float3 fraction=frac(p);
                fraction=fraction*fraction*(3-2*fraction);
                return SAMPLE_TEXTURE3D_LOD(_ClayNoise,sampler_ClayNoise,
                    (floor(p)+fraction+0.5)/64,0).r;
            }
            half3 TerrainRegionColor(float3 world,half3 original,half3 normal)
            {
                if(_TerrainColorRegions<0.5) return original;
                float2 uv=(world.xz+float2(2500,3250))/float2(5000,6500);
                half4 region=SAMPLE_TEXTURE2D(_TerrainRegionMap,sampler_TerrainRegionMap,uv);
                half3 regional=lerp(original,region.rgb,smoothstep(-0.12,0.28,normal.y));
                return lerp(regional,_DarkWoodColor.rgb,region.a*0.5);
            }
            half4 Frag(Varyings i):SV_Target
            {
                half3 n=normalize(i.normal);
                if(_FlatTerrainNormal>.5)
                {
                    half3 face=normalize(cross(ddy(i.world),ddx(i.world)));
                    n=dot(face,n)<0?-face:face;
                }
                half3 objectBase=lerp(_BaseColor.rgb,i.color.rgb,
                    saturate(_UseVertexColor)*i.color.a);
                if(_ResidentPropDraw>.5&&_LowTextureQuality>.5) return half4(objectBase,1);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord=ComputeScreenPos(TransformWorldToHClip(i.world));
                #else
                    float4 shadowCoord=TransformWorldToShadowCoord(i.world);
                #endif
                Light sun=GetMainLight(shadowCoord,i.world,half4(1,1,1,1));
                if(_ResidentPropDraw>.5&&_MediumTextureQuality>.5)
                {
                    float lit=min(sun.shadowAttenuation,step(.18,dot(n,sun.direction)));
                    return half4(objectBase*lerp(.55,1,step(.75,lit)),1);
                }
                AmbientOcclusionFactor ao=GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS));
                // Real diffuse sunlight and sky irradiance, with no glossy/specular lobe.
                half3 direct=sun.color*saturate(dot(n,sun.direction))*
                    sun.distanceAttenuation*sun.shadowAttenuation*ao.directAmbientOcclusion;
                half3 ambient=max(SampleSH(n),half3(0,0,0))*ao.indirectAmbientOcclusion;
                half3 regionalBase=TerrainRegionColor(i.world,objectBase,n);
                // Terrain vertices mark excavated stone interiors; ordinary ground is white.
                regionalBase*=lerp(half3(1,1,1),i.color.rgb,saturate(_UseTerrainPitColor));
                half3 simpleLighting=(0.42+0.58*saturate(dot(n,sun.direction)))*half3(1,0.96,0.90);
                half3 lighting=lerp(simpleLighting,direct+ambient,saturate(_WyvernBetterLighting));
                half3 naturalLighting=lighting;
                if(_MediumTextureQuality>0.5)
                {
                    float inShadow=min(sun.shadowAttenuation,step(0.18,dot(n,sun.direction)));
                    lighting=lerp(half3(0.55,0.55,0.55),half3(1,1,1),step(0.75,inShadow));
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
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 ShadowVert(Attributes v):SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
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
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            struct DepthInput { float4 vertex:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 DepthVert(DepthInput input):SV_POSITION
            { UNITY_SETUP_INSTANCE_ID(input); return TransformObjectToHClip(input.vertex.xyz); }
            half DepthFrag(float4 p:SV_POSITION):SV_Target { return p.z; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            HLSLPROGRAM
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; half3 normal:TEXCOORD0; float3 world:TEXCOORD1; };
            Varyings NormalsVert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o;
                o.positionCS=TransformObjectToHClip(v.positionOS.xyz);
                o.normal=TransformObjectToWorldNormal(v.normalOS);
                o.world=TransformObjectToWorld(v.positionOS.xyz);
                return o;
            }
            half4 NormalsFrag(Varyings i):SV_Target
            {
                float3 n=normalize(i.normal);
                if(_FlatTerrainNormal>.5)
                {
                    float3 face=normalize(cross(ddy(i.world),ddx(i.world)));
                    n=dot(face,n)<0?-face:face;
                }
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
