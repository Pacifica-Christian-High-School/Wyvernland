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
            half4 BakeTerrainRegionColor(float3 world,half3 original)
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
                float upward=1;
                float influence=saturate(total)*upward*
                    1*0.096;
                half3 regional=lerp(original,mixed,influence);
                const half3 darkBrown=half3(0.18,0.065,0.025);
                float riverInfluence=RiverbedMask(world.xz)*upward*
                    1*0.30;
                regional=lerp(regional,darkBrown,riverInfluence);
                float woods=0;
                for(int w=0;w<4;w++)
                {
                    float2 radius=max(_DarkWoods[w].zw,float2(0.001,0.001));
                    float d=length((world.xz-_DarkWoods[w].xy)/radius);
                    woods=max(woods,1-smoothstep(0.85,1,d));
                }
                return half4(regional,woods);
            }
