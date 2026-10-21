Shader "Wyvernland/Pixel Grass"
{
    Properties { [HideInInspector] _MediumTextureQuality ("Medium texture quality",Float)=0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="TransparentCutout" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            TEXTURE2D(_GrassSurfaceMap); SAMPLER(sampler_GrassSurfaceMap);
            TEXTURE2D(_GrassDensityMap); SAMPLER(sampler_GrassDensityMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _GrassMapBounds;
            float4 _GrassSurfaceGrid;
            float _GrassDensity;
            float _GrassPixelsPerMetre;
            float _MediumTextureQuality;
            CBUFFER_END
            float4 _WyvernFogColor;
            float4 _WyvernFogParams;
            struct Input { float4 position:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Output { float4 position:SV_POSITION; half4 color:COLOR; float3 shapeUV:TEXCOORD0; float3 world:TEXCOORD1; };
            float Hash(float2 p,float salt)
            {
                float3 h=frac(float3(p.x,p.y,salt)*.1031);
                h+=dot(h,h.yzx+33.33);
                return frac((h.x+h.y)*h.z);
            }
            half3 Palette(float id)
            {
                half3 color=id<1?half3(.7451,.4784,.3373):
                    (id<2?half3(.6902,.3098,.1216):half3(.502,.2157,.098));
                #ifndef UNITY_COLORSPACE_GAMMA
                    color=SRGBToLinear(color);
                #endif
                return color;
            }
            Output Vert(Input input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Output output=(Output)0;
                float3 cell=TransformObjectToWorld(input.position.xyz);
                float2 xz=cell.xz+.25+float2(Hash(cell.xz,11),Hash(cell.xz,23))*.5;
                float2 mapUV=(xz-_GrassMapBounds.xy)*_GrassMapBounds.zw;
                half3 distribution=SAMPLE_TEXTURE2D_LOD(_GrassDensityMap,sampler_GrassDensityMap,mapUV,0).rgb;
                float4 surface=SAMPLE_TEXTURE2D_LOD(_GrassSurfaceMap,sampler_GrassSurfaceMap,mapUV,0);
                // Project from the sampled terrain plane to this clump's actual
                // position so a compact height atlas doesn't produce stepped grass.
                float2 sampleXZ=_GrassMapBounds.xy+(floor(mapUV*_GrassSurfaceGrid.xy)+.5)*_GrassSurfaceGrid.zw;
                surface.x-=dot(surface.yw,xz-sampleXZ)/max(.25,surface.z);
                float2 patch=floor(xz/7);
                int first=(int)(Hash(patch,37)*3);
                int count=1+(int)(Hash(patch,43)*3);
                int shape=(first+(int)(Hash(xz,47)*count))%3;
                float height=distribution.g>.5?lerp(.18,.45,Hash(xz,53)):
                    lerp(distribution.b>.5?.5:.4,1,Hash(xz,53));
                float3 centre=float3(xz.x,surface.x,xz.y);
                float distanceToEye=distance(centre,_WorldSpaceCameraPos);
                bool valid=all(mapUV>=0)&&all(mapUV<=1)&&surface.z>.25&&
                    Hash(xz,59)<distribution.r*_GrassDensity&&height*_GrassPixelsPerMetre/max(1,distanceToEye)>.5;
                if(!valid) { output.position=float4(-2,-2,0,1);return output; }
                float3 normal=normalize(surface.yzw);
                float angle=Hash(xz,61)*6.2831853;
                float3 right=normalize(cross(normal,float3(sin(angle),0,cos(angle))));
                float3 up=normalize(cross(right,cross(normal,right)));
                float width=shape==0?11./9.:(shape==1?13./9.:14./13.);
                output.world=centre+normal*.002+right*(input.uv.x-.5)*width*height+up*input.uv.y*height;
                output.position=TransformWorldToHClip(output.world);
                float2 region=xz/266;
                float2 grid=floor(region),blend=smoothstep(0,1,frac(region));
                half3 a=lerp(Palette(floor(Hash(grid,67)*3)),Palette(floor(Hash(grid+float2(1,0),67)*3)),blend.x);
                half3 b=lerp(Palette(floor(Hash(grid+float2(0,1),67)*3)),Palette(floor(Hash(grid+1,67)*3)),blend.x);
                output.color=half4(lerp(a,b,blend.y)*lerp(.936,1.064,Hash(xz,71)),lerp(.4,.6,Hash(xz,73)));
                output.shapeUV=float3(input.uv,shape);
                return output;
            }
            #include "GrassShapes.hlsl"
            half4 Frag(Output input):SV_Target
            {
                clip(ShapeMask(input.shapeUV)-.5);
                // Depth-writing coverage retains the translucent appearance without
                // sorting and repeatedly blending thousands of overlapping quads.
                float coverage=frac(dot(floor(input.position.xy),float2(.754877666,.569840296)));
                clip(input.color.a-coverage);
                half3 color=input.color.rgb;
                if(_MediumTextureQuality>.5) color=half3(.43,.16,.055);
                float fog=saturate((distance(input.world,_WorldSpaceCameraPos)-_WyvernFogParams.x)/
                    max(.001,_WyvernFogParams.y-_WyvernFogParams.x))*_WyvernFogParams.z;
                return half4(lerp(color,_WyvernFogColor.rgb,fog),1);
            }
            ENDHLSL
        }
    }
}
