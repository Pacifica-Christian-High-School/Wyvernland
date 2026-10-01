using System.Collections.Generic;
using UnityEngine;

namespace Wyvernland
{
    public enum GroundFeatureKind { StandingRock, EmbeddedRock, IndividualStone }

    [System.Serializable]
    public sealed class GroundFeatureSeed
    {
        public Vector2 Position;
        public float OriginalHeight;
        public float Size;
        public float Radius;
        public float DepressionDepth;
        public int Seed;
        public GroundFeatureKind Kind;
        public bool HasPile;
        public bool PileCenteredOnRock;
        public Vector2 PilePosition;
        public float PileRadius;
        public bool HasRockPile;
        public Vector2 RockPilePosition;
        public float RockPileRadius;
    }

    /// <summary>One deterministic layout shared by terrain carving and prop streaming.</summary>
    public static class GroundFeatureLayout
    {
        public const float TerrainCellSize = 30f;
        public const float HighAltitudePileThreshold = 250f;
        public const float StonePileFootprintScale = 3f;
        public const float RockPileFootprintScale = 4f;
        public const float IndividualStoneMinimumSize = 0.064f;
        public const float IndividualStoneMaximumSize = 0.5f;
        public static readonly List<GroundFeatureSeed> Features = new();
        private static readonly Dictionary<Vector2Int,List<GroundFeatureSeed>> carvedCells = new();
        private static readonly Dictionary<Vector2Int,List<GroundFeatureSeed>> blockerCells = new();
        private static readonly Dictionary<Vector2Int,List<GroundFeatureSeed>> rockClearanceCells = new();
        private static bool initialized;

        // Keep lowland piles at their authored size, then smoothly enlarge them
        // through the uplands. Separate factors let the footprint and stones be
        // tuned independently without changing the deterministic layout.
        public static float PileRadiusScaleAt(float altitude)
        {
            float highland=Mathf.SmoothStep(0f,1f,
                Mathf.InverseLerp(80f,WorldConfig.MaximumElevation,altitude));
            return Mathf.Lerp(1f,1.65f,highland);
        }

        public static float PileStoneScaleAt(float altitude)
        {
            float highland=Mathf.SmoothStep(0f,1f,
                Mathf.InverseLerp(80f,WorldConfig.MaximumElevation,altitude));
            return Mathf.Lerp(1f,1.7f,highland);
        }

        public static void Initialize(bool useCache=true)
        {
            if (initialized && useCache) return;
            Features.Clear();
            carvedCells.Clear();
            blockerCells.Clear();
            rockClearanceCells.Clear();
            initialized = true;
            if(useCache)
            {
                GroundFeatureCache cache=Resources.Load<GroundFeatureCache>("Terrain/GroundFeatures");
                if(cache!=null && cache.Features!=null && cache.Features.Length>0)
                {
                    // Caches created before the two footprint scales were stored
                    // used a 4x radius for both pile types.
                    float cachedStoneScale=cache.StonePileFootprintScale>0f?
                        cache.StonePileFootprintScale:4f;
                    float cachedRockScale=cache.RockPileFootprintScale>0f?
                        cache.RockPileFootprintScale:4f;
                    float cachedStoneMinimum=cache.IndividualStoneMinimumSize>0f?
                        cache.IndividualStoneMinimumSize:0.1f;
                    foreach(GroundFeatureSeed feature in cache.Features)
                    {
                        Add(ScaledCacheFeature(feature,cachedStoneScale,cachedRockScale,
                            cachedStoneMinimum));
                    }
                    return;
                }
            }
            var random = new System.Random(420791);
            float Next(float min,float max) => Mathf.Lerp(min,max,(float)random.NextDouble());

            // Rocks span the complete landmass and become much more frequent in
            // the red areas traced from asfaff.png.
            for (float z=WorldConfig.MapBounds.yMin+31f;z<WorldConfig.MapBounds.yMax;z+=62f)
            for (float x=WorldConfig.MapBounds.xMin+31f;x<WorldConfig.MapBounds.xMax;x+=62f)
            {
                Vector2 point=new(x+Next(-28f,28f),z+Next(-28f,28f));
                float rich=RockRichness(point.x,point.y);
                bool riverbed=ProceduralMap.IsRiverbed(point.x,point.y);
                float rockChance=riverbed?0.96f:Mathf.Lerp(0.40f,0.86f,rich);
                if (random.NextDouble()>rockChance) continue;
                if (!TryGround(point,4f,out float height)) continue;
                float size=random.NextDouble()<0.72?Next(2f,2.8f):Next(2f,8.5f);
                bool embedded=random.NextDouble()<0.48;
                if (embedded) size*=1.7f;
                var feature=new GroundFeatureSeed
                {
                    Position=point, OriginalHeight=height, Size=size,
                    Radius=size*Next(0.48f,0.7f), Seed=random.Next(),
                    Kind=embedded?GroundFeatureKind.EmbeddedRock:GroundFeatureKind.StandingRock,
                    DepressionDepth=embedded?size*Next(0.18f,0.3f):0f,
                    HasPile=random.NextDouble()<(riverbed?0.94f:0.68f)
                };
                if (feature.HasPile)
                {
                    feature.PileCenteredOnRock=random.NextDouble()<0.32;
                    if(feature.PileCenteredOnRock)
                    {
                        feature.PilePosition=point;
                        feature.PileRadius=Next(5f,10f)*StonePileFootprintScale;
                    }
                    else
                    {
                        float angle=Next(0f,Mathf.PI*2f),distance=Next(size+2f,size+9f);
                        feature.PilePosition=point+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*distance;
                        feature.PileRadius=Next(3f,15f)*StonePileFootprintScale;
                    }
                    if (!TryGround(feature.PilePosition,3f,out float pileHeight)) feature.HasPile=false;
                    else feature.PileRadius*=PileRadiusScaleAt(pileHeight);
                }
                // Rock piles are a separate high-altitude feature made from many
                // full-sized rocks rather than the small stones used above.
                feature.HasRockPile=height>HighAltitudePileThreshold&&random.NextDouble()<0.82;
                if(feature.HasRockPile)
                {
                    feature.RockPilePosition=point;
                    feature.RockPileRadius=Next(7f,20f)*RockPileFootprintScale;
                }
                Add(feature);
            }

            // Rare landmark rocks favor broad low-altitude plains. Their size
            // controls footprint as well as height; the mesh generator applies
            // the same squat vertical profile used by every other rock.
            for(float z=WorldConfig.MapBounds.yMin+150f;z<WorldConfig.MapBounds.yMax;z+=300f)
            for(float x=WorldConfig.MapBounds.xMin+150f;x<WorldConfig.MapBounds.xMax;x+=300f)
            {
                Vector2 point=new(x+Next(-85f,85f),z+Next(-85f,85f));
                if(random.NextDouble()>0.58||!TryGround(point,12f,out float height)||height>72f) continue;
                float size=Next(10f,30f);
                bool embedded=random.NextDouble()<0.28;
                var landmark=new GroundFeatureSeed
                {
                    Position=point,OriginalHeight=height,Size=size,
                    Radius=size*Next(0.5f,0.68f),Seed=random.Next(),
                    Kind=embedded?GroundFeatureKind.EmbeddedRock:GroundFeatureKind.StandingRock,
                    DepressionDepth=embedded?size*Next(0.10f,0.18f):0f,
                    HasPile=random.NextDouble()<0.46
                };
                if(landmark.HasPile)
                {
                    landmark.PileCenteredOnRock=random.NextDouble()<0.55;
                    if(landmark.PileCenteredOnRock)
                    {
                        landmark.PilePosition=point;
                        landmark.PileRadius=Next(7f,15f)*StonePileFootprintScale;
                    }
                    else
                    {
                        float angle=Next(0f,Mathf.PI*2f);
                        landmark.PilePosition=point+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*
                            Next(size*0.45f+3f,size*0.65f+10f);
                        landmark.PileRadius=Next(5f,12f)*StonePileFootprintScale;
                    }
                    if(!TryGround(landmark.PilePosition,5f,out float pileHeight)) landmark.HasPile=false;
                    else landmark.PileRadius*=PileRadiusScaleAt(pileHeight);
                }
                Add(landmark);
            }

            // Individual stones are smaller and more numerous, with stable jittered spacing.
            for (float z=WorldConfig.MapBounds.yMin+11f;z<WorldConfig.MapBounds.yMax;z+=22f)
            for (float x=WorldConfig.MapBounds.xMin+11f;x<WorldConfig.MapBounds.xMax;x+=22f)
            {
                Vector2 point=new(x+Next(-7f,7f),z+Next(-7f,7f));
                float rich=RockRichness(point.x,point.y);
                bool riverbed=ProceduralMap.IsRiverbed(point.x,point.y);
                float stoneChance=riverbed?0.95f:Mathf.Lerp(0.22f,0.68f,rich);
                if (random.NextDouble()>stoneChance) continue;
                if (!TryGround(point,1.5f,out float height)) continue;
                float size=Next(IndividualStoneMinimumSize,IndividualStoneMaximumSize);
                var stone=new GroundFeatureSeed
                {
                    Position=point,OriginalHeight=height,Size=size,
                    Radius=Mathf.Max(0.16f,size*0.62f),
                    // Sub-metre Boolean cuts create unstable sliver triangles in
                    // the 30 m island grid. Stones sink visually and clear grass,
                    // while true terrain carving is reserved for embedded rocks.
                    DepressionDepth=0f,Seed=random.Next(),
                    Kind=GroundFeatureKind.IndividualStone
                };
                // A few stone piles occur independently; rock piles are generated
                // separately from high-altitude full-sized rocks above.
                if (random.NextDouble()<(riverbed?0.38f:0.012f))
                {
                    float angle=Next(0f,Mathf.PI*2f),distance=Next(3f,12f);
                    stone.PilePosition=point+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*distance;
                    stone.PileRadius=Next(3f,15f)*StonePileFootprintScale;
                    stone.HasPile=TryGround(stone.PilePosition,3f,out float pileHeight);
                    if(stone.HasPile) stone.PileRadius*=PileRadiusScaleAt(pileHeight);
                }
                Add(stone);
            }
        }

        private static GroundFeatureSeed ScaledCacheFeature(GroundFeatureSeed source,
            float cachedStoneScale,float cachedRockScale,float cachedStoneMinimum)
        {
            float size=source.Size;
            if(source.Kind==GroundFeatureKind.IndividualStone)
            {
                float normalized=Mathf.InverseLerp(cachedStoneMinimum,
                    IndividualStoneMaximumSize,source.Size);
                size=Mathf.Lerp(IndividualStoneMinimumSize,
                    IndividualStoneMaximumSize,normalized);
            }
            return new GroundFeatureSeed
            {
                Position=source.Position,
                OriginalHeight=source.OriginalHeight,
                Size=size,
                Radius=source.Radius,
                DepressionDepth=source.DepressionDepth,
                Seed=source.Seed,
                Kind=source.Kind,
                HasPile=source.HasPile,
                PileCenteredOnRock=source.PileCenteredOnRock,
                PilePosition=source.PilePosition,
                PileRadius=source.PileRadius*StonePileFootprintScale/cachedStoneScale,
                HasRockPile=source.HasRockPile,
                RockPilePosition=source.RockPilePosition,
                RockPileRadius=source.RockPileRadius*RockPileFootprintScale/cachedRockScale
            };
        }

        private static float RockRichness(float worldX,float worldZ)
        {
            Vector2 p=new(40f+(worldX+WorldConfig.MapWidth*0.5f)/WorldConfig.MapWidth*1210f,
                1660f-(worldZ+WorldConfig.MapLength*0.5f)/WorldConfig.MapLength*1510f);
            float Band(Vector2 a,Vector2 b,float radius)
            {
                Vector2 edge=b-a;
                float t=Mathf.Clamp01(Vector2.Dot(p-a,edge)/Mathf.Max(0.0001f,edge.sqrMagnitude));
                float normalized=1f-Vector2.Distance(p,a+edge*t)/radius;
                return Mathf.SmoothStep(0f,1f,Mathf.Clamp01(normalized));
            }
            float density=0f;
            density=Mathf.Max(density,Band(new Vector2(520,470),new Vector2(470,720),78));
            density=Mathf.Max(density,Band(new Vector2(270,775),new Vector2(240,920),72));
            density=Mathf.Max(density,Band(new Vector2(535,930),new Vector2(705,830),76));
            density=Mathf.Max(density,Band(new Vector2(850,940),new Vector2(850,1015),92));
            density=Mathf.Max(density,Band(new Vector2(1070,550),new Vector2(1070,550),58));
            density=Mathf.Max(density,Band(new Vector2(320,1080),new Vector2(380,1215),86));
            density=Mathf.Max(density,Band(new Vector2(740,1110),new Vector2(740,1110),58));
            return density;
        }

        private static bool TryGround(Vector2 point,float sample,out float height)
        {
            height=ProceduralMap.BaseHeightAt(point.x,point.y);
            if (!float.IsFinite(height) || height<2f) return false;
            float hx=ProceduralMap.BaseHeightAt(point.x+sample,point.y);
            float hz=ProceduralMap.BaseHeightAt(point.x,point.y+sample);
            return float.IsFinite(hx)&&float.IsFinite(hz)&&
                Mathf.Abs(hx-height)<sample*0.85f&&Mathf.Abs(hz-height)<sample*0.85f;
        }

        private static void Add(GroundFeatureSeed feature)
        {
            if(feature.Kind==GroundFeatureKind.IndividualStone)
            {
                if(!AllowsStone(feature.Position.x,feature.Position.y,feature.Radius)) return;
            }
            else if(!AllowsRock(feature.Position,RockRadius(feature))) return;

            Features.Add(feature);
            if(feature.Kind!=GroundFeatureKind.IndividualStone)
            {
                float radius=RockRadius(feature)+0.5f+1.1f;
                int clearanceMinX=Mathf.FloorToInt((feature.Position.x-radius)/TerrainCellSize);
                int clearanceMaxX=Mathf.FloorToInt((feature.Position.x+radius)/TerrainCellSize);
                int clearanceMinZ=Mathf.FloorToInt((feature.Position.y-radius)/TerrainCellSize);
                int clearanceMaxZ=Mathf.FloorToInt((feature.Position.y+radius)/TerrainCellSize);
                for(int z=clearanceMinZ;z<=clearanceMaxZ;z++)
                for(int x=clearanceMinX;x<=clearanceMaxX;x++)
                {
                    Vector2Int key=new(x,z);
                    if(!rockClearanceCells.TryGetValue(key,out List<GroundFeatureSeed> list))
                        rockClearanceCells[key]=list=new List<GroundFeatureSeed>();
                    list.Add(feature);
                }
            }
            RegisterBlocker(feature.Position,RockRadius(feature));
            if (feature.HasPile) RegisterBlocker(feature.PilePosition,feature.PileRadius);
            if (feature.HasRockPile) RegisterBlocker(feature.RockPilePosition,feature.RockPileRadius);
            if (feature.DepressionDepth<=0f) return;
            Vector2 map=WorldToMap(feature.Position);
            int minX=Mathf.FloorToInt((map.x-feature.Radius)/TerrainCellSize);
            int maxX=Mathf.FloorToInt((map.x+feature.Radius)/TerrainCellSize);
            int minZ=Mathf.FloorToInt((map.y-feature.Radius)/TerrainCellSize);
            int maxZ=Mathf.FloorToInt((map.y+feature.Radius)/TerrainCellSize);
            for (int z=minZ;z<=maxZ;z++)
            for (int x=minX;x<=maxX;x++)
            {
                Vector2Int key=new(x,z);
                if (!carvedCells.TryGetValue(key,out List<GroundFeatureSeed> list))
                    carvedCells[key]=list=new List<GroundFeatureSeed>();
                list.Add(feature);
            }
        }

        private static void RegisterBlocker(Vector2 position,float radius)
        {
            GroundFeatureSeed feature=Features[Features.Count-1];
            int minX=Mathf.FloorToInt((position.x-radius)/TerrainCellSize);
            int maxX=Mathf.FloorToInt((position.x+radius)/TerrainCellSize);
            int minZ=Mathf.FloorToInt((position.y-radius)/TerrainCellSize);
            int maxZ=Mathf.FloorToInt((position.y+radius)/TerrainCellSize);
            for(int z=minZ;z<=maxZ;z++)
            for(int x=minX;x<=maxX;x++)
            {
                Vector2Int key=new(x,z);
                if (!blockerCells.TryGetValue(key,out List<GroundFeatureSeed> list))
                    blockerCells[key]=list=new List<GroundFeatureSeed>();
                if (!list.Contains(feature)) list.Add(feature);
            }
        }

        private static Vector2 WorldToMap(Vector2 world) => new(
            world.x+WorldConfig.MapWidth*0.5f,world.y+WorldConfig.MapLength*0.5f);

        private static float RockRadius(GroundFeatureSeed feature) =>
            feature.Size*(feature.Kind==GroundFeatureKind.EmbeddedRock?6f:1f)*0.685f;

        private static bool AllowsRock(Vector2 point,float radius)
        {
            int minX=Mathf.FloorToInt((point.x-radius)/TerrainCellSize);
            int maxX=Mathf.FloorToInt((point.x+radius)/TerrainCellSize);
            int minZ=Mathf.FloorToInt((point.y-radius)/TerrainCellSize);
            int maxZ=Mathf.FloorToInt((point.y+radius)/TerrainCellSize);
            var checkedRocks=new HashSet<GroundFeatureSeed>();
            for(int z=minZ;z<=maxZ;z++)
            for(int x=minX;x<=maxX;x++)
            {
                if(!rockClearanceCells.TryGetValue(new Vector2Int(x,z),out List<GroundFeatureSeed> rocks))
                    continue;
                foreach(GroundFeatureSeed rock in rocks)
                {
                    if(!checkedRocks.Add(rock)) continue;
                    float clearance=RockRadius(rock)+radius+0.5f;
                    if((point-rock.Position).sqrMagnitude<clearance*clearance) return false;
                }
            }
            return true;
        }

        public static bool AllowsStone(float worldX,float worldZ,float stoneRadius)
        {
            Vector2 point=new(worldX,worldZ);
            Vector2Int key=new(Mathf.FloorToInt(worldX/TerrainCellSize),
                Mathf.FloorToInt(worldZ/TerrainCellSize));
            if(!rockClearanceCells.TryGetValue(key,out List<GroundFeatureSeed> rocks)) return true;
            foreach(GroundFeatureSeed rock in rocks)
            {
                float clearance=RockRadius(rock)+0.5f+stoneRadius;
                if((point-rock.Position).sqrMagnitude<clearance*clearance) return false;
            }
            return true;
        }

        public static bool AllowsTree(float worldX,float worldZ,float trunkRadius=1.5f) =>
            AllowsStone(worldX,worldZ,trunkRadius);

        public static float DepressionAt(Vector2 mapPoint)
        {
            Vector2Int key=new(Mathf.FloorToInt(mapPoint.x/TerrainCellSize),
                Mathf.FloorToInt(mapPoint.y/TerrainCellSize));
            if (!carvedCells.TryGetValue(key,out List<GroundFeatureSeed> list)) return 0f;
            Vector2 world=mapPoint-new Vector2(WorldConfig.MapWidth*0.5f,WorldConfig.MapLength*0.5f);
            float depression=0f;
            foreach (GroundFeatureSeed feature in list)
            {
                float t=1f-Vector2.Distance(world,feature.Position)/feature.Radius;
                if (t>0f) depression=Mathf.Max(depression,
                    feature.DepressionDepth*Mathf.SmoothStep(0f,1f,t));
            }
            return depression;
        }

        public static bool AllowsGrass(float worldX,float worldZ)
        {
            Vector2 point=new(worldX,worldZ);
            Vector2Int centre=new(Mathf.FloorToInt(worldX/TerrainCellSize),
                Mathf.FloorToInt(worldZ/TerrainCellSize));
            for (int z=-1;z<=1;z++)
            for (int x=-1;x<=1;x++)
            {
                if (!blockerCells.TryGetValue(centre+new Vector2Int(x,z),out List<GroundFeatureSeed> list)) continue;
                foreach (GroundFeatureSeed feature in list)
                {
                    if(feature.Kind==GroundFeatureKind.IndividualStone) continue;
                    float clearance=RockRadius(feature)+0.12f;
                if ((point-feature.Position).sqrMagnitude<clearance*clearance) return false;
                }
            }
            return true;
        }

        // Negative means the point is not inside a pile. Otherwise this is the
        // deliberately sparse probability for short grass between pile stones.
        public static float PileGrassDensity(float worldX,float worldZ)
        {
            Vector2 point=new(worldX,worldZ);
            Vector2Int centre=new(Mathf.FloorToInt(worldX/TerrainCellSize),
                Mathf.FloorToInt(worldZ/TerrainCellSize));
            float density=-1f;
            for(int z=-1;z<=1;z++)
            for(int x=-1;x<=1;x++)
            {
                if(!blockerCells.TryGetValue(centre+new Vector2Int(x,z),out List<GroundFeatureSeed> list)) continue;
                foreach(GroundFeatureSeed feature in list)
                {
                    if(!feature.HasPile) continue;
                    float normalized=1f-Vector2.Distance(point,feature.PilePosition)/feature.PileRadius;
                    if(normalized<0f) continue;
                    density=Mathf.Max(density,0.22f*Mathf.SmoothStep(0f,1f,normalized));
                }
            }
            return density;
        }
    }
}
