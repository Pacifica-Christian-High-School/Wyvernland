using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wyvernland
{
    /// <summary>Generates permanently loaded deterministic rocks, stones and piles.</summary>
    public sealed class ProceduralRockField : MonoBehaviour
    {
        private const float StonePilePopulationScale = 0.35f;
        private const float StonePileRadiusScale = 0.7f;
        private const float StoneMinimumSizeScale = 0.4f;
        private const float TerrainColorBlend = 0.5f;
        private sealed class Entry
        {
            public bool PhysicallyModified;
            public GroundFeatureSeed Seed;
            public bool IsPile;
            public bool IsRockPile;
            public GameObject Instance;
            public Mesh Mesh;
            public MeshFilter Filter;
            public readonly List<Collider> Colliders=new();
            public readonly List<Mesh> ChildMeshes=new();
            public readonly List<MeshRenderer> ChildRenderers=new();
            public readonly List<PickableObject> Pickables=new();
            public readonly Dictionary<Vector2,RaycastHit> OriginalSurfaces=new();
            public Vector2 Position => IsRockPile?Seed.RockPilePosition:
                IsPile?Seed.PilePosition:Seed.Position;
        }

        private readonly List<Entry> entries = new();
        private readonly List<Material> rockMaterials = new();
        private readonly List<Material> stoneMaterials = new();
        private Material pileVertexMaterial;
        private Transform observer;
        private ProceduralMap map;
        private readonly List<(Mesh mesh,Matrix4x4 placement)> terrainPits=new();
        private int preparedTerrainPits;
        private bool terrainPitsReady;

        public void Initialize(FirstPersonController player,ProceduralMap terrain,bool deferCreation=false)
        {
            observer=player.transform;
            map=terrain;
            Shader shader=Resources.Load<Shader>("Clay");
            if (shader==null) throw new System.InvalidOperationException("Missing clay shader.");
            for (int i=0;i<10;i++)
            {
                float t=i/9f;
                Color rock=Color.Lerp(WorldConfig.GroundColor,new Color(0.34f,0.31f,0.27f),t);
                rock=Color.Lerp(rock,new Color(0.38f,0.25f,0.16f),Mathf.Sin(t*Mathf.PI)*0.28f);
                rockMaterials.Add(CreateMaterial(shader,BlendWithTerrain(rock),$"Rock earth-grey {i}"));
                Color darkStone=Color.Lerp(new Color(0.14f,0.13f,0.12f),
                    new Color(0.30f,0.28f,0.24f),t)*0.5f;
                darkStone.a=1f;
                Color stone=BlendWithTerrain(darkStone);
                stoneMaterials.Add(CreateMaterial(shader,stone,$"Stone grey {i}"));
            }
            pileVertexMaterial=CreateMaterial(shader,Color.white,"Rock-to-stone pile colors");
            pileVertexMaterial.SetFloat("_UseVertexColor",1f);
            if (WorldBootstrap.TestGround)
            {
                const float rowZ = 20f;
                GroundFeatureSeed rock = new() { Position = new Vector2(-80,rowZ), Size = 5,
                    Radius = 3, Seed = 431, Kind = GroundFeatureKind.StandingRock,
                    // Pile radii in GroundFeatureSeed include the layout footprint scale.
                    // Keep the Test Ground samples on the same contract as generated seeds,
                    // otherwise the population calculation reduces them to only 1-2 pieces.
                    PilePosition = new Vector2(-40,rowZ),
                    PileRadius = 6*GroundFeatureLayout.StonePileFootprintScale,
                    RockPilePosition = new Vector2(80,rowZ),
                    RockPileRadius = 7*GroundFeatureLayout.RockPileFootprintScale };
                entries.Add(new Entry { Seed = rock });
                entries.Add(new Entry { Seed = new GroundFeatureSeed { Position = new Vector2(-60,rowZ),
                    Size = 1.5f, Radius = 5, Seed = 432, Kind = GroundFeatureKind.EmbeddedRock } });
                entries.Add(new Entry { Seed = rock, IsPile = true });
                entries.Add(new Entry { Seed = new GroundFeatureSeed { Position = new Vector2(-20,rowZ),
                    Size = 0.8f, Seed = 433, Kind = GroundFeatureKind.IndividualStone } });
                entries.Add(new Entry { Seed = rock, IsRockPile = true });
                if(!deferCreation) { Refresh(); PreloadTerrainPits(); }
                return;
            }
            foreach (GroundFeatureSeed seed in GroundFeatureLayout.Features)
            {
                bool stoneClear=seed.Kind!=GroundFeatureKind.IndividualStone||
                    GroundFeatureLayout.AllowsStone(seed.Position.x,seed.Position.y,seed.Size*0.55f);
                if(stoneClear&&!ProceduralMonumentField.ContainsInterior(seed.Position.x,seed.Position.y,
                    3f+Mathf.Max(0.5f,seed.Radius))) entries.Add(new Entry { Seed=seed });
                if (seed.HasPile&&!ProceduralMonumentField.ContainsInterior(
                    seed.PilePosition.x,seed.PilePosition.y,3f+seed.PileRadius))
                    entries.Add(new Entry { Seed=seed,IsPile=true });
                if (seed.HasRockPile&&!ProceduralMonumentField.ContainsInterior(
                    seed.RockPilePosition.x,seed.RockPilePosition.y,3f+seed.RockPileRadius))
                    entries.Add(new Entry { Seed=seed,IsRockPile=true });
            }
            if(!deferCreation) { Refresh(); PreloadTerrainPits(); }
        }

        internal System.Collections.IEnumerator InitializeIncrementally(FirstPersonController player,ProceduralMap terrain,
            System.Action<int,int> progress=null)
        {
            Initialize(player,terrain,true);
            double batchStarted=Time.realtimeSinceStartupAsDouble;
            int done=0;
            foreach(Entry entry in entries)
            {
                if(entry.Instance==null) Create(entry);
                done++;
                if(WorldLoading.ShouldYield(ref batchStarted))
                { progress?.Invoke(done,entries.Count);yield return null;batchStarted=Time.realtimeSinceStartupAsDouble; }
            }
            progress?.Invoke(done,entries.Count);
        }

        private void PreloadTerrainPits()
        {
            var routine=PreloadTerrainPitsIncrementally();
            while(routine.MoveNext()) { }
        }

        internal System.Collections.IEnumerator PreloadTerrainPitsIncrementally(
            System.Action<int,int> progress=null)
        {
            if(terrainPitsReady) yield break;
#if UNITY_WEBGL && !UNITY_EDITOR
            if(!WorldBootstrap.TestGround)
            {
                var cached=map.LoadBrowserPitTilesIncrementally();
                while(cached.MoveNext()) yield return cached.Current;
                preparedTerrainPits=terrainPits.Count;terrainPitsReady=true;
                yield break;
            }
#endif
            map.BeginStonePitGeneration();
            double batchStarted=Time.realtimeSinceStartupAsDouble;
            while(preparedTerrainPits<terrainPits.Count)
            {
                var pit=terrainPits[preparedTerrainPits];
                Mesh cutter=StoneTerrainCutout.SimplifyCutter(pit.mesh);
                try { map.CarveStoneTerrain(cutter,pit.placement); }
                finally { DestroyGenerated(cutter); }
                preparedTerrainPits++;
                if(WorldLoading.ShouldYield(ref batchStarted))
                { progress?.Invoke(preparedTerrainPits,terrainPits.Count);yield return null;batchStarted=Time.realtimeSinceStartupAsDouble; }
            }
            progress?.Invoke(preparedTerrainPits,terrainPits.Count);
            var finish=map.FinishStonePitGenerationIncrementally();
            while(finish.MoveNext()) yield return finish.Current;
            terrainPitsReady=true;
        }

        private void QueueTerrainPit(Mesh mesh,Transform placement)
        {
            terrainPits.Add((mesh,placement.localToWorldMatrix));
            terrainPitsReady=false;
        }

        private bool SampleOriginalSurface(Entry entry,float x,float z,out RaycastHit hit)
        {
            var key=new Vector2(x,z);
            if(entry.OriginalSurfaces.TryGetValue(key,out hit)) return hit.normal.sqrMagnitude>0f;
            bool found=map.TryGetOriginalStoneSurface(x,z,out hit);
            entry.OriginalSurfaces[key]=hit;
            return found;
        }

        private static Material CreateMaterial(Shader shader,Color color,string name)
        {
            Material material=new(shader) { name=name };
            material.SetColor("_BaseColor",color);
            material.SetFloat("_TerrainColorRegions",0f);
            material.SetFloat("_Cull",0f);
            return material;
        }

        private static Color BlendWithTerrain(Color color) =>
            Color.Lerp(color,WorldConfig.GroundColor,TerrainColorBlend);

        private static Color RemoveTerrainBlend(Color color) =>
            Color.LerpUnclamped(WorldConfig.GroundColor,color,1f/TerrainColorBlend);

        private void Refresh()
        {
            foreach(Entry entry in entries) if(entry.Instance==null) Create(entry);
        }

        private static float StoneBurialFraction(float size)
        {
            float sizeProgress=Mathf.InverseLerp(
                GroundFeatureLayout.IndividualStoneMinimumSize,
                GroundFeatureLayout.IndividualStoneMaximumSize,size);
            return 0.5f*sizeProgress;
        }

        private void Create(Entry entry)
        {
            MeshBuilder builder=new();
            int salt=entry.IsRockPile?0x35ad1:entry.IsPile?0x52791:0x183b7;
            var random=new System.Random(entry.Seed.Seed^salt);
            float Next(float min,float max)=>Mathf.Lerp(min,max,(float)random.NextDouble());
            Vector3 position;
            Quaternion rotation=Quaternion.identity;
            RaycastHit individualStoneSurface=default;
            bool placeIndividualStoneHalfAboveGround=false;
            bool isStone=entry.IsPile||(!entry.IsRockPile&&
                entry.Seed.Kind==GroundFeatureKind.IndividualStone);
            int materialIndex=(entry.Seed.Seed&int.MaxValue)%stoneMaterials.Count;
            var pileStones=new List<(Vector3 centre,Quaternion rotation,Mesh mesh)>();
            var pileRocks=new List<(Vector3 centre,Quaternion rotation,Mesh mesh)>();
            if(entry.IsRockPile)
            {
                position=new Vector3(entry.Position.x,0f,entry.Position.y);
                var colorRandom=new System.Random(entry.Seed.Seed^0x6b7a5);
                float authoredRadius=entry.Seed.RockPileRadius/
                    GroundFeatureLayout.RockPileFootprintScale;
                int count=Mathf.RoundToInt(authoredRadius*authoredRadius*Next(0.35f,0.55f));
                float phase=Next(0f,Mathf.PI*2f);
                for(int i=0;i<count;i++)
                {
                    float angle=Next(0f,Mathf.PI*2f),radial=Mathf.Pow(Next(0f,1f),1.15f);
                    float outline=entry.Seed.RockPileRadius*(0.8f+
                        0.14f*Mathf.Sin(angle*3f+phase)+0.06f*Mathf.Sin(angle*5f-phase));
                    Vector2 p=entry.Position+
                        new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radial*outline;
                    if(!map.TryGetSurface(p.x,p.y,out RaycastHit surface)) continue;
                    float rockSize=random.NextDouble()<0.16?Next(3f,6.5f):Next(2f,3.4f);
                    Vector3 centre=new(p.x-position.x,
                        surface.point.y-rockSize*Next(0.08f,0.2f),p.y-position.z);
                    Quaternion orient=Quaternion.FromToRotation(Vector3.up,surface.normal)*
                        Quaternion.AngleAxis(Next(0f,360f),Vector3.up);
                    Color baseColor=RemoveTerrainBlend(rockMaterials[materialIndex].GetColor("_BaseColor"));
                    Color sampleColor=RemoveTerrainBlend(rockMaterials[colorRandom.Next(rockMaterials.Count)]
                        .GetColor("_BaseColor"));
                    Color pileColor=Color.LerpUnclamped(baseColor,sampleColor,2f);
                    pileColor.r=Mathf.Clamp01(pileColor.r);
                    pileColor.g=Mathf.Clamp01(pileColor.g);
                    pileColor.b=Mathf.Clamp01(pileColor.b);
                    pileColor.a=1f;
                    var rockBuilder=new MeshBuilder();
                    rockBuilder.SetColor(BlendWithTerrain(pileColor));
                    rockBuilder.AddStandingRock(rockSize,random);
                    Mesh rockMesh=rockBuilder.Build("Low-poly pile rock");
                    Vector3 pivot=DestructionMesh.VolumeCentroid(rockMesh);
                    Vector3[] vertices=rockMesh.vertices;
                    for(int v=0;v<vertices.Length;v++) vertices[v]-=pivot;
                    rockMesh.vertices=vertices;
                    rockMesh.RecalculateBounds();
                    pileRocks.Add((centre+orient*pivot,orient,rockMesh));
                }
            }
            else if (entry.IsPile)
            {
                position=new Vector3(entry.Position.x,0f,entry.Position.y);
                float pileRadius=entry.Seed.PileRadius*StonePileRadiusScale;
                float authoredRadius=pileRadius/
                    GroundFeatureLayout.StonePileFootprintScale;
                int count=Mathf.RoundToInt(authoredRadius*authoredRadius*
                    Next(2.1f,3.1f)*StonePilePopulationScale);
                float phase=Next(0f,Mathf.PI*2f);
                for (int i=0;i<count;i++)
                {
                    float angle=Next(0f,Mathf.PI*2f),radial=Mathf.Pow(Next(0f,1f),1.15f);
                    float outline=pileRadius*(0.78f+0.18f*Mathf.Sin(angle*3f+phase)+
                        0.09f*Mathf.Sin(angle*5f-phase));
                    Vector2 p=entry.Position+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radial*outline;
                    if (!SampleOriginalSurface(entry,p.x,p.y,out RaycastHit surface)) continue;
                    float altitudeStoneScale=GroundFeatureLayout.PileStoneScaleAt(surface.point.y);
                    float distanceToRock=Vector2.Distance(p,entry.Seed.Position);
                    float centreInfluence=entry.Seed.PileCenteredOnRock?
                        1f-Mathf.SmoothStep(entry.Seed.Size*0.4f,pileRadius,distanceToRock):0f;
                    float size=entry.Seed.PileCenteredOnRock?
                        Next(0.3f*StoneMinimumSizeScale,Mathf.Lerp(0.72f,1.1f,centreInfluence)):
                        Next(0.2f*StoneMinimumSizeScale,0.7f);
                    size*=altitudeStoneScale;
                    if(!WorldBootstrap.TestGround && !GroundFeatureLayout.AllowsStone(p.x,p.y,size*0.55f)) continue;
                    Color stoneColor=Color.Lerp(new Color(0.07f,0.065f,0.06f),
                        new Color(0.34f,0.33f,0.31f),Next(0f,1f));
                    if (entry.Seed.Kind!=GroundFeatureKind.IndividualStone)
                    {
                        Color rockColor=RemoveTerrainBlend(rockMaterials[materialIndex]
                            .GetColor("_BaseColor"));
                        float rockMix=1f-Mathf.SmoothStep(entry.Seed.Size*0.45f,
                            entry.Seed.Size+pileRadius*0.72f,distanceToRock);
                        stoneColor=Color.Lerp(stoneColor,rockColor,rockMix);
                    }
                    // Apply the same exact 50% terrain blend after the pile's
                    // independent stone and parent-rock variations are chosen.
                    Color finalStoneColor=BlendWithTerrain(stoneColor);
                    Quaternion orient=Quaternion.FromToRotation(Vector3.up,surface.normal)*
                        Quaternion.AngleAxis(Next(0f,360f),Vector3.up);
                    MeshBuilder stoneBuilder=new();
                    stoneBuilder.SetColor(finalStoneColor);
                    stoneBuilder.AddEllipsoid(Vector3.zero,Quaternion.identity,
                        size*Next(0.38f,0.55f),size*Next(0.20f,0.48f),
                        size*Next(0.35f,0.55f),random,random.Next(8,13),random.Next(5,8));
                    Mesh stoneMesh=stoneBuilder.Build("Low-poly pile stone");
                    // Put the terrain plane through the mesh's true vertical
                    // midpoint, then lift smaller stones so progressively less
                    // of their height can disappear below the terrain.
                    Vector3 worldCentre=surface.point-
                        orient*(Vector3.up*stoneMesh.bounds.center.y);
                    float burial=StoneBurialFraction(size);
                    worldCentre+=orient*(Vector3.up*stoneMesh.bounds.extents.y*
                        (1f-2f*burial));
                    Vector3 centre=worldCentre-position;
                    pileStones.Add((centre,orient,stoneMesh));
                }
            }
            else if (entry.Seed.Kind==GroundFeatureKind.IndividualStone)
            {
                SampleOriginalSurface(entry,entry.Position.x,entry.Position.y,out RaycastHit surface);
                individualStoneSurface=surface;
                placeIndividualStoneHalfAboveGround=true;
                position=surface.point;
                rotation=Quaternion.FromToRotation(Vector3.up,surface.normal)*
                    Quaternion.AngleAxis(Next(0f,360f),Vector3.up);
                builder.AddEllipsoid(Vector3.zero,Quaternion.identity,
                    entry.Seed.Size*Next(0.38f,0.55f),entry.Seed.Size*Next(0.28f,0.45f),
                    entry.Seed.Size*Next(0.36f,0.55f),random,random.Next(8,13),random.Next(5,8));
            }
            else if (entry.Seed.Kind==GroundFeatureKind.EmbeddedRock)
            {
                map.TryGetSurface(entry.Position.x,entry.Position.y,out RaycastHit surface);
                // A 500% size increase makes embedded rocks six times their original size.
                float embeddedSize=entry.Seed.Size*6f;
                position=new Vector3(entry.Position.x,
                    surface.point.y-embeddedSize*Next(0.27f,0.34f),entry.Position.y);
                rotation=Quaternion.FromToRotation(Vector3.up,surface.normal)*
                    Quaternion.AngleAxis(Next(0f,360f),Vector3.up);
                builder.AddStandingRock(embeddedSize,random);
            }
            else
            {
                map.TryGetSurface(entry.Position.x,entry.Position.y,out RaycastHit surface);
                position=surface.point;
                rotation=Quaternion.FromToRotation(Vector3.up,surface.normal)*
                    Quaternion.AngleAxis(Next(0f,360f),Vector3.up);
                builder.AddStandingRock(entry.Seed.Size,random);
            }
            if(builder.VertexCount==0&&pileStones.Count==0&&pileRocks.Count==0) return;
            if(entry.IsPile)
            {
                entry.Instance=new GameObject("Low-poly stone pile");
                entry.Instance.transform.SetParent(transform,false);
                entry.Instance.transform.SetPositionAndRotation(position,Quaternion.identity);
                entry.Instance.AddComponent<GeneratedGroundFeature>();
                for(int i=0;i<pileStones.Count;i++)
                {
                    (Vector3 centre,Quaternion stoneRotation,Mesh stoneMesh)=pileStones[i];
                    GameObject stone=new($"Stone {i+1}");
                    stone.transform.SetParent(entry.Instance.transform,false);
                    stone.transform.SetLocalPositionAndRotation(centre,stoneRotation);
                    MeshFilter stoneFilter=stone.AddComponent<MeshFilter>();
                    stoneFilter.sharedMesh=stoneMesh;
                    PhysicalDestructible.Ensure(stone).GeometryChanged+=()=>entry.PhysicallyModified=true;
                    MeshRenderer stoneRenderer=stone.AddComponent<MeshRenderer>();
                    stoneRenderer.sharedMaterial=pileVertexMaterial;
                    stoneRenderer.shadowCastingMode=ShadowCastingMode.On;
                    SphereCollider stoneCollider=stone.AddComponent<SphereCollider>();
                    stoneCollider.center=stoneMesh.bounds.center;
                    stoneCollider.radius=Mathf.Max(0.04f,stoneMesh.bounds.extents.magnitude);
                    entry.Colliders.Add(stoneCollider);
                    entry.ChildMeshes.Add(stoneMesh);
                    PickableObject pilePickable=stone.AddComponent<PickableObject>();
                    pilePickable.DisplayName="Stone";
                    pilePickable.Label=PickableObjectLabel.Irregular;
                    QueueTerrainPit(stoneMesh,stone.transform);
                    entry.Pickables.Add(pilePickable);
                }
                return;
            }
            if(entry.IsRockPile)
            {
                entry.Instance=new GameObject("Low-poly rock pile");
                entry.Instance.transform.SetParent(transform,false);
                entry.Instance.transform.SetPositionAndRotation(position,Quaternion.identity);
                entry.Instance.AddComponent<GeneratedGroundFeature>();
                for(int i=0;i<pileRocks.Count;i++)
                {
                    (Vector3 centre,Quaternion rockRotation,Mesh rockMesh)=pileRocks[i];
                    GameObject rock=new($"Rock {i+1}");
                    rock.transform.SetParent(entry.Instance.transform,false);
                    rock.transform.SetLocalPositionAndRotation(centre,rockRotation);
                    rock.AddComponent<MeshFilter>().sharedMesh=rockMesh;
                    MeshRenderer rockRenderer=rock.AddComponent<MeshRenderer>();
                    rockRenderer.sharedMaterial=pileVertexMaterial;
                    rockRenderer.shadowCastingMode=ShadowCastingMode.On;
                    MeshCollider rockCollider=rock.AddComponent<MeshCollider>();
                    rockCollider.sharedMesh=rockMesh;
                    PhysicalDestructible properties=PhysicalDestructible.Ensure(rock);
                    properties.GeometryChanged+=()=>entry.PhysicallyModified=true;
                    QueueTerrainPit(rockMesh,rock.transform);
                    entry.ChildMeshes.Add(rockMesh);
                    entry.ChildRenderers.Add(rockRenderer);
                    entry.Colliders.Add(rockCollider);
                }
                return;
            }
            Mesh generatedMesh=builder.Build(entry.IsRockPile?"Low-poly rock pile":entry.IsPile?"Low-poly stone pile":
                entry.Seed.Kind==GroundFeatureKind.IndividualStone?"Low-poly individual stone":"Low-poly rock");
            if(placeIndividualStoneHalfAboveGround)
            {
                position=individualStoneSurface.point-
                    rotation*(Vector3.up*generatedMesh.bounds.center.y);
                float burial=StoneBurialFraction(entry.Seed.Size);
                position+=rotation*(Vector3.up*generatedMesh.bounds.extents.y*
                    (1f-2f*burial));
            }
            entry.Mesh=generatedMesh;
            entry.Instance=new GameObject(entry.Mesh.name);
            entry.Instance.transform.SetParent(transform,false);
            entry.Instance.transform.SetPositionAndRotation(position,rotation);
            entry.Instance.AddComponent<GeneratedGroundFeature>();
            entry.Filter=entry.Instance.AddComponent<MeshFilter>();
            entry.Filter.sharedMesh=entry.Mesh;
            PhysicalDestructible feature=PhysicalDestructible.Ensure(entry.Instance);
            feature.GeometryChanged+=()=>entry.PhysicallyModified=true;
            QueueTerrainPit(entry.Mesh,entry.Instance.transform);
            MeshRenderer renderer=entry.Instance.AddComponent<MeshRenderer>();
            List<Material> palette=isStone?stoneMaterials:rockMaterials;
            renderer.sharedMaterial=entry.IsPile||entry.IsRockPile?
                pileVertexMaterial:palette[materialIndex];
            renderer.shadowCastingMode=ShadowCastingMode.On;
            if(!isStone||entry.IsPile)
            {
                MeshCollider collider=entry.Instance.AddComponent<MeshCollider>();
                collider.sharedMesh=entry.Mesh;
                entry.Colliders.Add(collider);
            }
            else
            {
                Bounds bounds=entry.Mesh.bounds;
                SphereCollider collider=entry.Instance.AddComponent<SphereCollider>();
                collider.center=bounds.center;
                collider.radius=Mathf.Max(0.04f,bounds.extents.magnitude);
                entry.Colliders.Add(collider);
            }
            if(!entry.IsPile&&!entry.IsRockPile&&entry.Seed.Kind==GroundFeatureKind.IndividualStone)
            {
                PickableObject stone=entry.Instance.AddComponent<PickableObject>();
                stone.DisplayName="Stone";
                stone.Label=PickableObjectLabel.Irregular;
            }
        }

        private void Release(Entry entry)
        {
            entry.Instance.SetActive(false);
            DestroyGenerated(entry.Instance); DestroyGenerated(entry.Mesh);
            entry.Instance=null; entry.Mesh=null;
            foreach(Mesh childMesh in entry.ChildMeshes) if(childMesh!=null) DestroyGenerated(childMesh);
            entry.Filter=null; entry.Colliders.Clear();entry.ChildMeshes.Clear();
            entry.ChildRenderers.Clear();entry.Pickables.Clear();
        }

        private static void DestroyGenerated(UnityEngine.Object value)
        {
            if(value==null) return;
            if(Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }

        private void OnDestroy()
        {
            foreach (Entry entry in entries) if (entry.Instance!=null) Release(entry);
            foreach (Material material in rockMaterials) DestroyGenerated(material);
            foreach (Material material in stoneMaterials) DestroyGenerated(material);
            DestroyGenerated(pileVertexMaterial);
        }

        private sealed class MeshBuilder
        {
            private readonly List<Vector3> vertices=new();
            private readonly List<int> triangles=new();
            private readonly List<Color> colors=new();
            private Color currentColor=Color.white;
            public int VertexCount=>vertices.Count;
            // Mesh colors are consumed directly in linear space, unlike
            // material Color properties which Unity converts for us.
            public void SetColor(Color color)=>currentColor=
                QualitySettings.activeColorSpace==ColorSpace.Linear?color.linear:color;

            private void Face(Vector3 a,Vector3 b,Vector3 c)
            {
                // Keep every authored topology face. The former fixed-area cutoff
                // treated valid triangles on small stones as degenerate and opened
                // visible holes in otherwise closed ellipsoid meshes.
                int start=vertices.Count;
                vertices.Add(a);vertices.Add(b);vertices.Add(c);
                colors.Add(currentColor);colors.Add(currentColor);colors.Add(currentColor);
                triangles.Add(start);triangles.Add(start+1);triangles.Add(start+2);
            }

            public void AddStandingRock(float height,System.Random random)
            {
                AddStandingRock(height,random,Vector3.zero,Quaternion.identity);
            }

            public void AddMesh(Mesh mesh,Vector3 centre,Quaternion rotation)
            {
                var points=mesh.vertices;
                var faces=mesh.triangles;
                var vertexColors=mesh.colors;
                for(int i=0;i<faces.Length;i+=3)
                {
                    if(vertexColors.Length>0) currentColor=vertexColors[faces[i]];
                    Face(centre+rotation*points[faces[i]],
                        centre+rotation*points[faces[i+1]],
                        centre+rotation*points[faces[i+2]]);
                }
            }

            public void AddStandingRock(float height,System.Random random,Vector3 centre,
                Quaternion rotation)
            {
                int sides=random.Next(9,14);
                float width=height*Mathf.Lerp(0.32f,0.58f,(float)random.NextDouble());
                float depth=width*Mathf.Lerp(0.72f,1.18f,(float)random.NextDouble());
                float vertical=height*0.62f;
                float[] levels={-vertical*0.14f,vertical*0.04f,vertical*0.21f,
                    vertical*0.40f,vertical*0.61f,vertical*0.80f,vertical};
                float[] radii={Mathf.Lerp(0.18f,0.3f,(float)random.NextDouble()),1f,
                    Mathf.Lerp(0.88f,1.08f,(float)random.NextDouble()),
                    Mathf.Lerp(0.76f,1.01f,(float)random.NextDouble()),
                    Mathf.Lerp(0.66f,0.91f,(float)random.NextDouble()),
                    Mathf.Lerp(0.52f,0.78f,(float)random.NextDouble()),
                    Mathf.Lerp(0.34f,0.62f,(float)random.NextDouble())*0.8f};
                var rings=new Vector3[levels.Length,sides];
                for (int ring=0;ring<levels.Length;ring++)
                for (int side=0;side<sides;side++)
                {
                    float angle=side*Mathf.PI*2f/sides;
                    float irregular=Mathf.Lerp(0.84f,1.16f,(float)random.NextDouble());
                    rings[ring,side]=centre+rotation*new Vector3(
                        Mathf.Cos(angle)*width*radii[ring]*irregular,
                        levels[ring],Mathf.Sin(angle)*depth*radii[ring]*irregular);
                }
                for (int ring=0;ring+1<levels.Length;ring++)
                for (int side=0;side<sides;side++)
                {
                    int next=(side+1)%sides;
                    Face(rings[ring,side],rings[ring+1,side],rings[ring+1,next]);
                    Face(rings[ring,side],rings[ring+1,next],rings[ring,next]);
                }
                for (int side=1;side+1<sides;side++)
                {
                    Face(rings[0,0],rings[0,side],rings[0,side+1]);
                    Face(rings[levels.Length-1,0],rings[levels.Length-1,side+1],
                        rings[levels.Length-1,side]);
                }
            }

            public void AddEllipsoid(Vector3 centre,Quaternion rotation,float rx,float ry,float rz,
                System.Random random,int sides=10,int bands=5)
            {
                sides=Mathf.Max(3,sides);
                bands=Mathf.Max(2,bands);
                var rings=new Vector3[bands-1,sides];
                for (int ring=0;ring<bands-1;ring++)
                {
                    float latitude=-Mathf.PI*0.5f+Mathf.PI*(ring+1)/bands;
                    for (int side=0;side<sides;side++)
                    {
                        float angle=side*Mathf.PI*2f/sides;
                        float irregular=Mathf.Lerp(0.70f,1.30f,(float)random.NextDouble());
                        float ringOffset=Mathf.Lerp(-0.18f,0.18f,(float)random.NextDouble());
                        Vector3 point=new((Mathf.Cos(angle)*irregular+ringOffset)*Mathf.Cos(latitude)*rx,
                            Mathf.Sin(latitude)*ry* Mathf.Lerp(0.86f,1.14f,(float)random.NextDouble()),
                            (Mathf.Sin(angle)*irregular-ringOffset)*Mathf.Cos(latitude)*rz);
                        rings[ring,side]=centre+rotation*point;
                    }
                }

                Vector3 bottom=centre+rotation*new Vector3(0f,
                    -ry*Mathf.Lerp(0.86f,1.14f,(float)random.NextDouble()),0f);
                Vector3 top=centre+rotation*new Vector3(0f,
                    ry*Mathf.Lerp(0.86f,1.14f,(float)random.NextDouble()),0f);
                for (int side=0;side<sides;side++)
                {
                    int next=(side+1)%sides;
                    Face(bottom,rings[0,side],rings[0,next]);
                    Face(top,rings[bands-2,next],rings[bands-2,side]);
                }

                for (int ring=0;ring<bands-2;ring++)
                for (int side=0;side<sides;side++)
                {
                    int next=(side+1)%sides;
                    Face(rings[ring,side],rings[ring+1,side],rings[ring+1,next]);
                    Face(rings[ring,side],rings[ring+1,next],rings[ring,next]);
                }
            }

            public Mesh Build(string name)
            {
                Mesh mesh=new() { name=name,indexFormat=IndexFormat.UInt32 };
                mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);
                mesh.SetColors(colors);
                mesh.RecalculateNormals();mesh.RecalculateBounds();
                return mesh;
            }
        }
    }

    public sealed class GeneratedGroundFeature : MonoBehaviour { }

    /// <summary>Rare lowland monuments inspired by the supplied broken obelisk.</summary>
    public sealed class ProceduralMonumentField : MonoBehaviour
    {
        private static readonly List<(Vector2 centre,float radius)> interiors=new();

        public static bool ContainsInterior(float worldX,float worldZ,float padding=0f)
        {
            Vector2 point=new(worldX,worldZ);
            foreach((Vector2 centre,float radius) interior in interiors)
            {
                float clearance=interior.radius+Mathf.Max(0f,padding);
                if((point-interior.centre).sqrMagnitude<clearance*clearance) return true;
            }
            return false;
        }

        private sealed class MonumentSeed
        {
            public bool PhysicallyModified;
            public Vector3 Position;
            public float Height;
            public int Seed;
            public GameObject Instance;
            public MeshCollider[] Colliders;
        }

        private readonly List<MonumentSeed> monuments=new();
        private readonly List<Material> materials=new();
        private Transform observer;
        private GameObject monumentPrefab;

        public void Initialize(FirstPersonController player,ProceduralMap map)
        {
            interiors.Clear();
            observer=player.transform;
            monumentPrefab=Resources.Load<GameObject>("Models/AncientMonument");
            if(monumentPrefab==null)
                throw new System.InvalidOperationException(
                    "Missing Resources/Models/AncientMonument model.");
            Shader shader=Resources.Load<Shader>("Clay");
            for(int i=0;i<8;i++)
            {
                float t=i/7f;
                Color yellowBrown=new(0.53f,0.34f,0.14f);
                Color grey=new(0.31f,0.32f,0.30f);
                Color color=Color.Lerp(yellowBrown,grey,Mathf.Lerp(0.32f,0.68f,t));
                color=Color.Lerp(color,WorldConfig.GroundColor,0.5f);
                Material material=new(shader){name=$"Ancient monument stone {i}"};
                material.SetColor("_BaseColor",color);
                material.SetFloat("_TerrainColorRegions",0f);
                material.SetFloat("_Cull",0f);
                materials.Add(material);
            }

            var random=new System.Random(653119);
            float Next(float min,float max)=>Mathf.Lerp(min,max,(float)random.NextDouble());
            const int monumentCount=5;
            const float minimumSeparation=700f;
            for(int attempt=0;attempt<5000&&monuments.Count<monumentCount;attempt++)
            {
                float px=Next(WorldConfig.MapBounds.xMin,WorldConfig.MapBounds.xMax);
                float pz=Next(WorldConfig.MapBounds.yMin,WorldConfig.MapBounds.yMax);
                float monumentHeight=Next(200f,500f);
                float footprintSample=monumentHeight*0.15f;
                if(!ProceduralMap.IsOnMainIsland(px,pz,footprintSample)) continue;
                float y=map.HeightAt(px,pz);
                if(!float.IsFinite(y)||y<3f||y>78f) continue;
                float hx0=map.HeightAt(px-footprintSample,pz),hx1=map.HeightAt(px+footprintSample,pz);
                float hz0=map.HeightAt(px,pz-footprintSample),hz1=map.HeightAt(px,pz+footprintSample);
                if(!float.IsFinite(hx0)||!float.IsFinite(hx1)||!float.IsFinite(hz0)||!float.IsFinite(hz1)||
                    Mathf.Abs(hx0-y)>12f||Mathf.Abs(hx1-y)>12f||
                    Mathf.Abs(hz0-y)>12f||Mathf.Abs(hz1-y)>12f) continue;
                Vector3 position=new(px,y-monumentHeight*0.04f,pz);
                if((position-observer.position).sqrMagnitude<2500f) continue;
                bool tooClose=false;
                foreach(MonumentSeed existing in monuments)
                    if((existing.Position-position).sqrMagnitude<minimumSeparation*minimumSeparation)
                    { tooClose=true; break; }
                if(tooClose) continue;
                monuments.Add(new MonumentSeed{Position=position,Height=monumentHeight,Seed=random.Next()});
                interiors.Add((new Vector2(px,pz),monumentHeight*0.21f));
            }
            if(monuments.Count!=monumentCount)
                throw new System.InvalidOperationException(
                    $"Could not place all {monumentCount} main-island monuments.");
            Refresh();
        }

        private void Refresh()
        {
            foreach(MonumentSeed monument in monuments)
                if(monument.Instance==null) Create(monument);
        }

        private void Create(MonumentSeed monument)
        {
            monument.Instance=new GameObject("Ancient Lowland Monument");
            monument.Instance.transform.SetParent(transform,false);
            monument.Instance.AddComponent<GeneratedGroundFeature>();

            GameObject model=Instantiate(monumentPrefab,monument.Instance.transform,false);
            model.name="Ancient Monument Model";
            Renderer[] renderers=model.GetComponentsInChildren<Renderer>(true);
            if(renderers.Length==0)
                throw new System.InvalidOperationException("Ancient monument model has no renderers.");

            Bounds bounds=renderers[0].bounds;
            for(int i=1;i<renderers.Length;i++) bounds.Encapsulate(renderers[i].bounds);
            float scale=monument.Height/Mathf.Max(0.001f,bounds.size.y);
            model.transform.localScale=Vector3.one*scale;
            model.transform.localPosition=Vector3.up*(-bounds.min.y*scale);

            Material material=materials[(monument.Seed&int.MaxValue)%materials.Count];
            foreach(Renderer renderer in renderers) renderer.sharedMaterial=material;
            MeshFilter[] filters=model.GetComponentsInChildren<MeshFilter>(true);
            monument.Colliders=new MeshCollider[filters.Length];
            for(int i=0;i<filters.Length;i++)
            {
                MeshCollider collider=filters[i].gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh=filters[i].sharedMesh;
                monument.Colliders[i]=collider;
                PhysicalDestructible.Ensure(filters[i].gameObject).GeometryChanged+=()=>monument.PhysicallyModified=true;
            }
            monument.Instance.transform.SetPositionAndRotation(monument.Position,
                Quaternion.Euler(0,(monument.Seed%360+360)%360,0));
        }

        private static Mesh BuildMesh(int seed,float height)
        {
            var random=new System.Random(seed);
            var vertices=new List<Vector3>();
            var triangles=new List<int>();
            float Next(float min,float max)=>Mathf.Lerp(min,max,(float)random.NextDouble());
            void Face(Vector3 a,Vector3 b,Vector3 c)
            {
                int index=vertices.Count;
                vertices.Add(a);vertices.Add(b);vertices.Add(c);
                triangles.Add(index);triangles.Add(index+1);triangles.Add(index+2);
            }
            void Prism(Vector3 bottom,Vector3 top,float lowerRadius,float upperRadius,int sides,float yaw)
            {
                var lower=new Vector3[sides];var upper=new Vector3[sides];
                for(int i=0;i<sides;i++)
                {
                    float angle=yaw+i*Mathf.PI*2f/sides;
                    float irregular=Next(0.88f,1.12f);
                    Vector3 radial=new(Mathf.Cos(angle),0,Mathf.Sin(angle));
                    lower[i]=bottom+radial*lowerRadius*irregular;
                    upper[i]=top+radial*upperRadius*Next(0.9f,1.1f);
                }
                for(int i=0;i<sides;i++)
                {
                    int next=(i+1)%sides;
                    Face(lower[i],upper[i],upper[next]);Face(lower[i],upper[next],lower[next]);
                }
                for(int i=1;i+1<sides;i++)
                {
                    // Lower cap faces down and upper cap faces up. The previous
                    // winding was reversed, so back-face culling hid both.
                    Face(lower[0],lower[i],lower[i+1]);
                    Face(upper[0],upper[i+1],upper[i]);
                }
            }
            void Wedge(Vector3 centre,float yaw,float width,float rise,float reach)
            {
                Vector3 outward=new(Mathf.Cos(yaw),0,Mathf.Sin(yaw));
                Vector3 side=new(-outward.z,0,outward.x);
                Vector3 inner=centre-outward*width*0.18f;
                Vector3 outer=centre+outward*reach;
                Vector3 a=inner-side*width*0.5f, b=inner+side*width*0.5f;
                Vector3 c=outer+side*width*0.28f, d=outer-side*width*0.28f;
                Vector3 up=Vector3.up*rise;
                Face(a,b,c);Face(a,c,d);Face(a+up,c+up,b+up);Face(a+up,d+up,c+up);
                Face(a,a+up,b+up);Face(a,b+up,b);Face(d,c,c+up);Face(d,c+up,d+up);
                Face(b,b+up,c+up);Face(b,c+up,c);Face(a,d+up,a+up);Face(a,d,d+up);
            }

            int sides=random.Next(5,8);
            float radius=height*Next(0.13f,0.19f);
            float yaw=Next(0f,Mathf.PI*2f);
            Vector3 offset=Vector3.zero;
            float[] levels={0f,Next(0.21f,0.27f),Next(0.50f,0.58f),1f};
            for(int segment=0;segment<3;segment++)
            {
                float bottom=levels[segment]*height;
                float top=levels[segment+1]*height;
                // Slight overlap keeps the monument a continuous jagged column
                // like the reference instead of three visibly floating blocks.
                if(segment>0) bottom-=height*Next(0.012f,0.03f);
                offset+=new Vector3(Next(-0.018f,0.018f)*height,0,Next(-0.018f,0.018f)*height);
                float taperBottom=1f-levels[segment]*Next(0.28f,0.42f);
                float taperTop=1f-levels[segment+1]*Next(0.28f,0.42f);
                Prism(offset+Vector3.up*bottom,offset+new Vector3(Next(-0.012f,0.012f)*height,
                    top,Next(-0.012f,0.012f)*height),radius*taperBottom,radius*taperTop,
                    sides,yaw+Next(-0.18f,0.18f));
            }
            int wedges=random.Next(2,5);
            for(int i=0;i<wedges;i++)
            {
                float t=Next(0.2f,0.8f);
                Wedge(offset+Vector3.up*height*t,yaw+Next(0f,Mathf.PI*2f),
                    radius*Next(0.45f,0.82f),height*Next(0.03f,0.075f),radius*Next(0.35f,0.78f));
            }
            Mesh mesh=new(){name=$"Random ancient monument {seed}",indexFormat=IndexFormat.UInt32};
            mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);
            mesh.RecalculateNormals();mesh.RecalculateBounds();
            return mesh;
        }

        private void Release(MonumentSeed monument)
        {
            monument.Instance.SetActive(false);
            Destroy(monument.Instance);
            monument.Instance=null;monument.Colliders=null;
        }

        private void OnDestroy()
        {
            foreach(MonumentSeed monument in monuments)
                if(monument.Instance!=null) Release(monument);
            foreach(Material material in materials) if(material!=null) Destroy(material);
        }
    }
}
