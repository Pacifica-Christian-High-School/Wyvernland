using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Wyvernland
{
    // Keeps only lightweight tree seeds outside the visible radius. Other scene
    // objects retain their state while inactive so scripts and physics can resume.
    public sealed class WorldObjectStreaming : MonoBehaviour
    {
        public float LoadingRange { get; private set; }
        private Transform observer;
        private FirstPersonController playerController;
        private ProceduralMap map;
        private readonly List<TreeSeed> trees = new();
        private readonly Queue<TreeSeed> midpointBreakQueue=new();
        private float nextMidpointBreakTime;
        private readonly Dictionary<Vector2Int,List<Vector2>> treeGrassCells = new();
        private readonly List<TrackedObject> objects = new();
        private readonly HashSet<GameObject> registered = new();
        private readonly List<Material> materials = new();
        private Material darkWoodMaterial;
        private float nextDiscovery;

        // Centre lines of the blue brush strokes in 3qr3d.png. Density reaches
        // zero at each brush edge and rises smoothly toward its centre. Overlaps
        // use the strongest stroke, preserving holes such as the eastern loop.
        private static readonly ForestBand[] ForestBands =
        {
            new(56f, new Vector2(558,555), new Vector2(556,645), new Vector2(600,735),
                new Vector2(705,785), new Vector2(835,812), new Vector2(955,785),
                new Vector2(1055,720), new Vector2(1110,635), new Vector2(1125,570)),
            new(55f, new Vector2(315,782), new Vector2(420,770), new Vector2(520,770),
                new Vector2(600,798), new Vector2(685,842)),
            new(64f, new Vector2(270,955), new Vector2(345,915), new Vector2(450,930),
                new Vector2(550,920), new Vector2(625,878), new Vector2(690,840)),
            new(55f, new Vector2(365,1320), new Vector2(425,1260),
                new Vector2(445,1130), new Vector2(450,1035)),
            new(70f, new Vector2(790,1445), new Vector2(825,1350), new Vector2(815,1220),
                new Vector2(775,1100), new Vector2(760,970)),
            new(52f, new Vector2(820,890), new Vector2(920,875),
                new Vector2(1020,830), new Vector2(1090,790)),
            new(57f, new Vector2(1080,815), new Vector2(1150,850), new Vector2(1180,950),
                new Vector2(1160,1080), new Vector2(1090,1150), new Vector2(1015,1130),
                new Vector2(960,1060), new Vector2(885,1010), new Vector2(850,920))
        };

        // Dense woodland painted in lime green on dfsfs.png. The broad strokes
        // are represented as overlapping rounded bands so their density also
        // falls smoothly at the hand-painted edges.
        private static readonly ForestBand[] WoodlandBands =
        {
            new(72f, new Vector2(205,520), new Vector2(300,548), new Vector2(325,625),
                new Vector2(285,680), new Vector2(190,695)),
            new(66f, new Vector2(540,510), new Vector2(590,548), new Vector2(645,570)),
            new(74f, new Vector2(930,625), new Vector2(985,615)),
            new(63f, new Vector2(1110,555), new Vector2(1135,660)),
            new(55f, new Vector2(292,810), new Vector2(270,900), new Vector2(260,965)),
            new(72f, new Vector2(742,805), new Vector2(680,850), new Vector2(585,870)),
            new(78f, new Vector2(805,1110), new Vector2(815,1160))
        };

        private sealed class ForestBand
        {
            public readonly float Radius;
            public readonly Vector2[] Points;
            public ForestBand(float radius, params Vector2[] points)
            {
                Radius = radius;
                Points = points;
            }

            public float Density(Vector2 point)
            {
                float squaredDistance = float.PositiveInfinity;
                for (int i = 0; i + 1 < Points.Length; i++)
                {
                    Vector2 a = Points[i], edge = Points[i+1] - a;
                    float t = Mathf.Clamp01(Vector2.Dot(point-a, edge) /
                        Mathf.Max(0.0001f, edge.sqrMagnitude));
                    squaredDistance = Mathf.Min(squaredDistance, (point-a-edge*t).sqrMagnitude);
                }
                float edgeFade = Mathf.Clamp01(1f - Mathf.Sqrt(squaredDistance) / Radius);
                return edgeFade * edgeFade * (3f - 2f * edgeFade);
            }
        }

        private sealed class TreeSeed
        {
            public bool physicallyModified;
            public bool breakAtMidpoint,queuedForMidpointBreak;
            public int midpointBreakAttempts;
            public Vector3 position;
            public Vector3 terrainNormal;
            public int seed;
            public bool woodland;
            public bool darkWood;
            public GameObject instance;
            public Mesh mesh;
            public Mesh trunkMesh;
            public readonly List<Mesh> trunkPartMeshes=new();
            public readonly List<Mesh> branchMeshes=new();
            public readonly List<int> branchParents=new();
            public int trunkSections;
            public Mesh distantMesh;
            public MeshFilter filter;
            public MeshRenderer distantRenderer;
            public readonly List<MeshRenderer> nearRenderers=new();
            public readonly List<MeshCollider> nearColliders=new();
            public readonly Dictionary<string,float> wornHardness=new();
            public bool distant;
        }

        private sealed class TrackedObject
        {
            public GameObject root;
            public Collider[] colliders;
            public bool[] enabled;
            public Rigidbody body;
            public bool kinematic, collisions, suspended, hidden;
            public Vector3 velocity, angularVelocity;

            public void RefreshPhysicsComponents()
            {
                Rigidbody currentBody=root!=null?root.GetComponent<Rigidbody>():null;
                if(currentBody==body&&colliders!=null&&
                    System.Array.TrueForAll(colliders,collider=>collider!=null)) return;
                body=currentBody;
                colliders=body!=null?root.GetComponentsInChildren<Collider>(true):
                    root.GetComponents<Collider>();
                enabled=new bool[colliders.Length];
                for(int i=0;i<colliders.Length;i++) enabled[i]=colliders[i].enabled;
                suspended=false;
            }

            public bool IsMoving
            {
                get
                {
                    if(body==null) return false;
                    Vector3 currentVelocity=suspended?velocity:body.linearVelocity;
                    return currentVelocity.sqrMagnitude>0f;
                }
            }

            public void PhysicsActive(bool active)
            {
                if (suspended == !active) return;
                if (!active)
                {
                    for (int i = 0; i < colliders.Length; i++)
                        if (colliders[i] != null) { enabled[i] = colliders[i].enabled; colliders[i].enabled = false; }
                    if (body != null)
                    {
                        kinematic = body.isKinematic;
                        collisions = body.detectCollisions;
                        velocity = body.linearVelocity;
                        angularVelocity = body.angularVelocity;
                        body.Sleep();
                        body.isKinematic = true;
                        body.detectCollisions = false;
                    }
                }
                else
                {
                    for (int i = 0; i < colliders.Length; i++)
                        if (colliders[i] != null) colliders[i].enabled = enabled[i];
                    if (body != null)
                    {
                        body.isKinematic = kinematic;
                        body.detectCollisions = collisions;
                        if (!kinematic)
                        {
                            body.linearVelocity = velocity;
                            body.angularVelocity = angularVelocity;
                            body.WakeUp();
                        }
                    }
                }
                suspended = !active;
            }
        }

        public void Initialize(FirstPersonController player, ProceduralMap terrain)
        {
            playerController=player;
            observer = player.transform;
            map = terrain;
            LoadingRange = Mathf.Clamp(PlayerPrefs.GetFloat("Options.LoadingRange", 500f), 50f, 3000f);
            Shader shader = Resources.Load<Shader>("Clay");
            darkWoodMaterial=new Material(shader){name="Very dark brown woodland"};
            darkWoodMaterial.SetColor("_BaseColor",EnvironmentRegions.DarkBrown);
            for (int i = 0; i < 12; i++)
            {
                Material material = new(shader) { name = $"Tree brown {i}" };
                Color bark=Color.Lerp(new Color(0.247f,0.0715f,0.0325f),
                    new Color(0.288f,0.192f,0.076f),i/11f);
                material.SetColor("_BaseColor",Color.Lerp(bark,WorldConfig.GroundColor,0.5f));
                materials.Add(material);
            }
            if (WorldBootstrap.TestGround)
            {
                // Ordinary stumps use multiples of seven; burnt stumps use seed % 100 < 40.
                int[] seeds = { 101, 105, 150, 110 };
                string[] names = { "Tree", "Stump", "Burnt Tree", "Burnt Stump" };
                for (int i = 0; i < seeds.Length; i++)
                {
                    // Positions 4–7 in the nine-object display row.
                    TreeSeed tree = new() { position = new Vector3(i*20,0,20),
                        terrainNormal = Vector3.up, seed = seeds[i], darkWood = i >= 2 };
                    trees.Add(tree);
                    CreateTree(tree);
                    tree.instance.name = names[i];
                }
                return;
            }
            void PlaceTrees(float spacing, float probability, bool woodland, int randomSeed,bool darkWood=false)
            {
                var random = new System.Random(randomSeed);
                float jitter = spacing * 0.34f;
                for (float z = WorldConfig.MapBounds.yMin + spacing*0.5f; z < WorldConfig.MapBounds.yMax; z += spacing)
                for (float x = WorldConfig.MapBounds.xMin + spacing*0.5f; x < WorldConfig.MapBounds.xMax; x += spacing)
                {
                    float px = x + ((float)random.NextDouble()*2f-1f)*jitter;
                    float pz = z + ((float)random.NextDouble()*2f-1f)*jitter;
                    float darkDensity=EnvironmentRegions.WoodsDensity(px,pz);
                    if(!darkWood&&darkDensity>0f) continue;
                    float density = darkWood?darkDensity:woodland ? WoodlandDensity(px,pz) : ForestDensity(px,pz);
                    // The dense pass replaces ordinary placements inside woods.
                    if (!darkWood&&!woodland && WoodlandDensity(px,pz)>0f) continue;
                    if (random.NextDouble() > probability*density) continue;
                    if (ProceduralMap.IsRiverbed(px,pz,2f)) continue;
                    if (!map.TryGetSurface(px,pz,out RaycastHit surface)) continue;
                    float y = surface.point.y;
                    if (!float.IsFinite(y) || y<4f || (!darkWood&&y>285f)) continue;
                    if(ProceduralMonumentField.ContainsInterior(px,pz,3f)) continue;
                    if(!GroundFeatureLayout.AllowsTree(px,pz)) continue;
                    float hx = map.HeightAt(px+3f,pz), hz = map.HeightAt(px,pz+3f);
                    if (!float.IsFinite(hx) || !float.IsFinite(hz) || Mathf.Abs(hx-y)>2f || Mathf.Abs(hz-y)>2f) continue;
                    Vector3 position = surface.point-surface.normal*0.02f;
                    if ((position-observer.position).sqrMagnitude<100f) continue;
                    trees.Add(new TreeSeed { position=position, terrainNormal=surface.normal.normalized,
                        seed=random.Next(), woodland=woodland,darkWood=darkWood });
                    Vector2Int grassCell=new(Mathf.FloorToInt(px/8f),Mathf.FloorToInt(pz/8f));
                    if (!treeGrassCells.TryGetValue(grassCell,out List<Vector2> nearby))
                        treeGrassCells[grassCell]=nearby=new List<Vector2>();
                    nearby.Add(new Vector2(px,pz));
                }
            }
            PlaceTrees(45f,0.31f,false,74129);
            PlaceTrees(27f,0.78f,true,91357);
            PlaceTrees(24f,0.88f,true,824613,true);
            Refresh();
        }

        private static float ForestDensity(float worldX, float worldZ)
        {
            Vector2 photoPoint = new(
                40f + (worldX + WorldConfig.MapWidth * 0.5f) / WorldConfig.MapWidth * 1210f,
                1660f - (worldZ + WorldConfig.MapLength * 0.5f) / WorldConfig.MapLength * 1510f);
            float density = 0f;
            foreach (ForestBand band in ForestBands)
                density = Mathf.Max(density, band.Density(photoPoint));
            return density;
        }

        private static float WoodlandDensity(float worldX,float worldZ)
        {
            Vector2 photoPoint = new(
                40f+(worldX+WorldConfig.MapWidth*0.5f)/WorldConfig.MapWidth*1210f,
                1660f-(worldZ+WorldConfig.MapLength*0.5f)/WorldConfig.MapLength*1510f);
            float density=0f;
            foreach(ForestBand band in WoodlandBands)
                density=Mathf.Max(density,band.Density(photoPoint));
            return density;
        }

        public float GrassAroundTrees(float worldX,float worldZ)
        {
            Vector2 point=new(worldX,worldZ);
            Vector2Int centre=new(Mathf.FloorToInt(worldX/8f),Mathf.FloorToInt(worldZ/8f));
            float density=0f;
            for(int z=-1;z<=1;z++)
            for(int x=-1;x<=1;x++)
            {
                if(!treeGrassCells.TryGetValue(centre+new Vector2Int(x,z),out List<Vector2> positions)) continue;
                foreach(Vector2 tree in positions)
                {
                    float distance=Vector2.Distance(point,tree);
                    if(distance<0.7f||distance>5f) continue;
                    density=Mathf.Max(density,Mathf.Lerp(0.72f,0.96f,
                        Mathf.SmoothStep(0f,1f,1f-distance/5f)));
                }
            }
            return density;
        }

        public void SetRange(float value)
        {
            LoadingRange = Mathf.Clamp(value, 50f, 3000f);
            PlayerPrefs.SetFloat("Options.LoadingRange", LoadingRange);
            Refresh();
        }

        // Before the next physics step, including after loading a saved position.
        private void FixedUpdate() => Refresh();
        private void Update()
        {
            if (observer == null) return;
            if(playerController!=null&&!playerController.IsPaused&&
                Keyboard.current?.lKey.wasPressedThisFrame==true)
                RequestBreakAllTreesAtMidpoint();
            if (Time.unscaledTime >= nextDiscovery)
            {
                DiscoverObjects();
                nextDiscovery = Time.unscaledTime + 1f;
                Refresh();
            }
            else if (Time.timeScale == 0f) Refresh();
            ProcessMidpointBreak();
        }

        public void RequestBreakAllTreesAtMidpoint()
        {
            foreach(TreeSeed tree in trees)
            {
                tree.breakAtMidpoint=true;
                tree.midpointBreakAttempts=0;
                QueueMidpointBreak(tree);
            }
        }

        private void QueueMidpointBreak(TreeSeed tree)
        {
            if(observer==null||tree.instance==null||tree.queuedForMidpointBreak||
                !tree.breakAtMidpoint||tree.midpointBreakAttempts>=3||
                (tree.position-observer.position).sqrMagnitude>LoadingRange*LoadingRange)
                return;
            tree.queuedForMidpointBreak=true;
            midpointBreakQueue.Enqueue(tree);
        }

        private void ProcessMidpointBreak()
        {
            if(midpointBreakQueue.Count==0||Time.unscaledTime<nextMidpointBreakTime||
                playerController!=null&&playerController.IsPaused) return;
            nextMidpointBreakTime=Time.unscaledTime+0.1f;
            TreeSeed tree=midpointBreakQueue.Dequeue();
            tree.queuedForMidpointBreak=false;
            if(!tree.breakAtMidpoint||tree.instance==null||
                (tree.position-observer.position).sqrMagnitude>LoadingRange*LoadingRange)
                return;
            tree.midpointBreakAttempts++;
            GeneratedTree generated=tree.instance.GetComponent<GeneratedTree>();
            if(tree.distant)
            {
                tree.distantRenderer.enabled=false;
                foreach(var renderer in tree.nearRenderers)
                    if(renderer!=null) renderer.enabled=true;
                foreach(var collider in tree.nearColliders)
                    if(collider!=null) collider.enabled=true;
                tree.distant=false;
            }
            if(generated!=null&&generated.BreakAtMidpoint())
            {
                tree.breakAtMidpoint=false;
                tree.physicallyModified=true;
            }
        }

        private void DiscoverObjects()
        {
            foreach (Collider collider in FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (map.IsMapCollider(collider) || collider.transform.IsChildOf(observer) ||
                    collider.GetComponentInParent<GeneratedTree>() != null ||
                    collider.GetComponentInParent<GeneratedGroundFeature>() != null) continue;
                GameObject root = collider.attachedRigidbody != null ? collider.attachedRigidbody.gameObject : collider.gameObject;
                // Never deactivate an ancestor containing the terrain or player.
                if (observer.IsChildOf(root.transform) || map.transform.IsChildOf(root.transform) || !registered.Add(root)) continue;
                Collider[] colliders = collider.attachedRigidbody != null ? root.GetComponentsInChildren<Collider>() : root.GetComponents<Collider>();
                objects.Add(new TrackedObject { root = root, colliders = colliders,
                    enabled = new bool[colliders.Length], body = collider.attachedRigidbody });
            }
        }

        private int treeCreationFrame=-1;
        private int treesCreatedThisFrame;
        public void Refresh()
        {
            if (observer == null) return;
            if(treeCreationFrame!=Time.frameCount)
            { treeCreationFrame=Time.frameCount; treesCreatedThisFrame=0; }
            float physicsSquared = LoadingRange * LoadingRange;
            float visibleSquared = physicsSquared * 4f;
            foreach (TreeSeed tree in trees)
            {
                float distance = (tree.position - observer.position).sqrMagnitude;
                if(tree.breakAtMidpoint) QueueMidpointBreak(tree);
                if(tree.physicallyModified)
                {
                    if(tree.instance!=null)
                    {
                        bool visible=distance<=visibleSquared;
                        if(tree.instance.activeSelf!=visible) tree.instance.SetActive(visible);
                        foreach(var collider in tree.nearColliders)
                            if(collider!=null&&collider.transform.IsChildOf(tree.instance.transform))
                                collider.enabled=visible&&distance<=physicsSquared;
                    }
                    continue;
                }
                if (distance > visibleSquared)
                {
                    if (tree.instance != null)
                    {
                        tree.instance.SetActive(false);
                        Destroy(tree.instance); ReleaseTreeMeshes(tree);
                        tree.instance=null; tree.filter=null; tree.distantRenderer=null;
                        tree.nearRenderers.Clear(); tree.nearColliders.Clear();
                    }
                    continue;
                }
                // Budget mesh creation to avoid a large frame spike at startup.
                if (tree.instance == null && treesCreatedThisFrame < 12)
                {
                    CreateTree(tree); treesCreatedThisFrame++;
                }
                bool useDistantMesh=distance>physicsSquared;
                if(tree.filter!=null&&tree.distant!=useDistantMesh)
                {
                    tree.distantRenderer.enabled=useDistantMesh;
                    foreach(var renderer in tree.nearRenderers)
                        if(renderer!=null) renderer.enabled=!useDistantMesh;
                    tree.distant=useDistantMesh;
                }
                foreach(var collider in tree.nearColliders)
                    if(collider!=null&&collider.enabled!=(distance<=physicsSquared))
                        collider.enabled=distance<=physicsSquared;
                if(tree.breakAtMidpoint) QueueMidpointBreak(tree);
            }
            foreach (TrackedObject item in objects)
            {
                if (item.root == null) continue;
                var destruction=item.root.GetComponentInParent<PhysicalDestructible>();
                if(destruction!=null&&destruction.HasBeenModified) continue;
                if (!item.hidden && !item.root.activeInHierarchy) continue;
                item.RefreshPhysicsComponents();
                float distance = (item.root.transform.position - observer.position).sqrMagnitude;
                bool moving=item.IsMoving;
                bool visible = moving||distance <= visibleSquared;
                if (visible && item.hidden) { item.root.SetActive(true); item.hidden = false; }
                item.PhysicsActive(moving||distance <= physicsSquared);
                if (!visible && !item.hidden) { item.root.SetActive(false); item.hidden = true; }
            }
        }

        private void CreateTree(TreeSeed tree)
        {
            tree.mesh=GenerateTreeMesh(tree.seed,tree.woodland,tree.terrainNormal,
                tree.darkWood,tree.branchMeshes,out tree.trunkMesh,out tree.trunkSections,
                tree.branchParents,tree.trunkPartMeshes);
            tree.distant=false;
            tree.distantMesh=MeshFaceReduction.Create(tree.mesh,0.2f,"Distant tree");
            tree.instance = new GameObject(!tree.woodland&&IsStump(tree.seed) ? "Procedural Stump" :
                tree.woodland ? "Woodland Tree" : "Procedural Tree");
            tree.instance.transform.SetParent(transform, false);
            tree.instance.transform.position = tree.position;
            var generatedTree=tree.instance.AddComponent<GeneratedTree>();
            tree.filter=tree.instance.AddComponent<MeshFilter>();
            tree.filter.sharedMesh=tree.distantMesh;
            Material bark=tree.darkWood?darkWoodMaterial:materials[tree.seed % materials.Count];
            tree.distantRenderer=tree.instance.AddComponent<MeshRenderer>();
            tree.distantRenderer.sharedMaterial=bark;
            tree.distantRenderer.enabled=false;
            var trunkParts=new List<TreeStructuralPart>();
            var branchParts=new List<TreeStructuralPart>();
            TreeStructuralPart Part(string partName,Mesh mesh)
            {
                var part=new GameObject(partName);
                part.transform.SetParent(tree.instance.transform,false);
                bool branch=partName.StartsWith("Wood branch ");
                if(branch)
                {
                    // Generated vertices are in tree-local coordinates. Move the
                    // branch pivot to its actual volume centre without moving its
                    // visible geometry or collider in the world.
                    Vector3 centre=DestructionMesh.VolumeCentroid(mesh);
                    Vector3[] vertices=mesh.vertices;
                    for(int i=0;i<vertices.Length;i++) vertices[i]-=centre;
                    mesh.vertices=vertices;
                    mesh.RecalculateBounds();
                    part.transform.localPosition=centre;
                }
                part.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=part.AddComponent<MeshRenderer>(); renderer.sharedMaterial=bark;
                tree.nearRenderers.Add(renderer);
                var collider=part.AddComponent<MeshCollider>(); collider.sharedMesh=mesh;
                tree.nearColliders.Add(collider);
                PhysicalDestructible properties=tree.darkWood?
                    part.AddComponent<PhysicalDestructible>():PhysicalDestructible.Ensure(part);
                if(tree.darkWood) properties.ApplyMaterial("Burnt Wood");
                var structure=part.AddComponent<TreeStructuralPart>();
                if(branch)
                {
                    part.AddComponent<PickableObject>().DisplayName="Wood branch";
                }
                if(tree.wornHardness.TryGetValue(partName,out float remainingHardness))
                    properties.Hardness=remainingHardness;
                properties.HardnessChanged+=remaining=>tree.wornHardness[partName]=remaining;
                properties.GeometryChanged+=()=>
                {
                    tree.physicallyModified=true;
                    generatedTree.ValidateTrunkConnections();
                };
                return structure;
            }
            for(int i=0;i<tree.trunkPartMeshes.Count;i++)
            {
                var structure=Part($"Wood trunk or root {i+1}",tree.trunkPartMeshes[i]);
                structure.SetTrunk(i<tree.trunkSections);
                if(i==0) structure.SetGroundAnchor();
                trunkParts.Add(structure);
            }
            float radiusSum=0f;
            for(int i=0;i<tree.trunkSections;i++)
            {
                Bounds bounds=tree.trunkPartMeshes[i].bounds;
                radiusSum+=(bounds.extents.x+bounds.extents.z)*0.5f;
            }
            generatedTree.Configure(radiusSum/Mathf.Max(1,tree.trunkSections),
                4+tree.seed%7);
            for(int i=0;i<tree.branchMeshes.Count;i++)
            {
                var branch=Part($"Wood branch {i+1}",tree.branchMeshes[i]);
                branch.ConfigureBranch(1+(tree.seed+i)%2);
                branchParts.Add(branch);
            }
            // Every capped trunk segment has its own collider. Link them from
            // the stump upward; the root shells after them remain ground anchors.
            for(int i=1;i<Mathf.Min(tree.trunkSections,trunkParts.Count);i++)
                trunkParts[i].AttachTo(trunkParts[i-1]);
            for(int i=0;i<branchParts.Count;i++)
            {
                int parent=i<tree.branchParents.Count?tree.branchParents[i]:-1;
                if(parent>=0&&parent<branchParts.Count)
                    branchParts[i].AttachTo(branchParts[parent]);
                else
                {
                    TreeStructuralPart nearest=null;
                    float distance=float.PositiveInfinity;
                    for(int j=0;j<Mathf.Min(tree.trunkSections,trunkParts.Count);j++)
                    {
                        float candidate=trunkParts[j].DistanceTo(branchParts[i].AttachmentPoint);
                        if(candidate>=distance) continue;
                        distance=candidate; nearest=trunkParts[j];
                    }
                    if(nearest!=null) branchParts[i].AttachTo(nearest);
                }
            }
        }

        private void ReleaseTreeMeshes(TreeSeed tree)
        {
            if(tree.mesh!=null) Destroy(tree.mesh);
            if(tree.trunkMesh!=null) Destroy(tree.trunkMesh);
            foreach(var part in tree.trunkPartMeshes)
                if(part!=null&&part!=tree.trunkMesh) Destroy(part);
            if(tree.distantMesh!=null) Destroy(tree.distantMesh);
            foreach(var branch in tree.branchMeshes) if(branch!=null) Destroy(branch);
            tree.mesh=null; tree.trunkMesh=null; tree.distantMesh=null;
            tree.branchMeshes.Clear(); tree.trunkPartMeshes.Clear();
            tree.branchParents.Clear();
        }

        private static bool IsStump(int seed) => seed % 7 == 0;

        public static Mesh GenerateTreeMesh(int seed) =>
            GenerateTreeMesh(seed,false,Vector3.up,false,null,out _,out _);

        private static Mesh GenerateTreeMesh(int seed,bool woodland,Vector3 terrainNormal,
            bool darkWood,List<Mesh> branchMeshes,out Mesh trunkMesh,out int trunkSections,
            List<int> branchParents=null,List<Mesh> trunkPartMeshes=null)
        {
            trunkSections=0;
            var random = new System.Random(seed);
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var branchRanges=new List<(int start,int end)>();
            var trunkSectionRanges=new List<(int start,int end)>();
            var rootRanges=new List<(int start,int end)>();
            var rootTips = new List<(int start,int end,Vector3 outward,float threshold,float depth)>();
            float Next(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
            void Face(Vector3 a, Vector3 b, Vector3 c)
            {
                int index = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                triangles.Add(index); triangles.Add(index+1); triangles.Add(index+2);
            }
            void Limb(Vector3 a, Vector3 b, float r0, float r1, int sides)
            {
                Quaternion rotation = Quaternion.FromToRotation(Vector3.up, (b-a).normalized);
                for (int i = 0; i < sides; i++)
                {
                    float angle = i * Mathf.PI * 2f / sides, next = (i+1) * Mathf.PI * 2f / sides;
                    Vector3 u = rotation * new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    Vector3 v = rotation * new Vector3(Mathf.Cos(next), 0, Mathf.Sin(next));
                    Face(a+u*r0, b+u*r1, b+v*r1);
                    Face(a+u*r0, b+v*r1, a+v*r0);
                    Face(a, a+u*r0, a+v*r0);
                    Face(b, b+v*r1, b+u*r1);
                }
            }
            void TrunkLimb(Vector3 a,Vector3 b,float r0,float r1,int sides)
            {
                int start=triangles.Count;
                Limb(a,b,r0,r1,sides);
                trunkSectionRanges.Add((start,triangles.Count));
            }
            int BranchLimb(Vector3 a,Vector3 b,float r0,float r1,int sides,int parentBranch=-1)
            {
                int index=branchRanges.Count;
                int start=triangles.Count;
                Limb(a,b,r0,r1,sides);
                branchRanges.Add((start,triangles.Count));
                branchParents?.Add(parentBranch);
                return index;
            }
            void Root(Vector3 outward, float length, float width, float rootHeight, float bend)
            {
                int rootTriangleStart=triangles.Count;
                int rootVertexStart=vertices.Count;
                Vector3 side=Vector3.Cross(Vector3.up,outward).normalized;
                Vector3[] centres=
                {
                    outward*width*0.08f,
                    outward*length*0.52f+side*bend,
                    outward*length+side*bend*0.55f
                };
                float[] halfWidths={width*0.5f,width*0.32f,width*0.12f};
                float[] heights={rootHeight,rootHeight*0.48f,rootHeight*0.08f};
                var lowerLeft=new Vector3[3]; var lowerRight=new Vector3[3];
                var upperLeft=new Vector3[3]; var upperRight=new Vector3[3];
                for(int i=0;i<3;i++)
                {
                    // A tiny overlap with the terrain keeps the roots visually
                    // planted even on the coarse, faceted ground mesh.
                    float lowerY=i==2?-rootHeight*0.05f:-rootHeight*0.025f;
                    lowerLeft[i]=centres[i]-side*halfWidths[i]+Vector3.up*lowerY;
                    lowerRight[i]=centres[i]+side*halfWidths[i]+Vector3.up*lowerY;
                    upperLeft[i]=centres[i]-side*halfWidths[i]+Vector3.up*heights[i];
                    upperRight[i]=centres[i]+side*halfWidths[i]+Vector3.up*heights[i];
                }
                for(int i=0;i<2;i++)
                {
                    // Top faces wind upward so the tree material's back-face
                    // culling cannot make roots disappear when seen from above.
                    Face(upperLeft[i],upperRight[i+1],upperRight[i]);
                    Face(upperLeft[i],upperLeft[i+1],upperRight[i+1]);
                    Face(lowerLeft[i],upperLeft[i+1],upperLeft[i]);
                    Face(lowerLeft[i],lowerLeft[i+1],upperLeft[i+1]);
                    Face(lowerRight[i],upperRight[i],upperRight[i+1]);
                    Face(lowerRight[i],upperRight[i+1],lowerRight[i+1]);
                    Face(lowerLeft[i],lowerRight[i+1],lowerLeft[i+1]);
                    Face(lowerLeft[i],lowerRight[i],lowerRight[i+1]);
                }
                // Close the root where it disappears into the trunk. Without
                // this cap the independent root shell cannot form fragments.
                Face(lowerLeft[0],upperLeft[0],upperRight[0]);
                Face(lowerLeft[0],upperRight[0],lowerRight[0]);
                Face(lowerLeft[2],upperRight[2],upperLeft[2]);
                Face(lowerLeft[2],lowerRight[2],upperRight[2]);
                // Record every duplicated vertex near this terminal cross-section.
                // They are buried after the tree's height-normalization pass.
                rootTips.Add((rootVertexStart,vertices.Count,outward,length*0.85f,
                    Mathf.Max(0.03f,rootHeight*0.12f)));
                rootRanges.Add((rootTriangleStart,triangles.Count));
            }
            // Full trees use the requested uniform height range. Trunk width,
            // branch length and bends continue to scale from this value.
            float height = woodland ? Next(10f,40f) : Next(3f,20f);
            float radius = height*Next(0.018f,0.06f);
            bool stump = darkWood?seed%100<40:!woodland&&IsStump(seed);
            // Keep the cut trunk visible above its broad roots. The previous
            // 0.24 m minimum could flatten some stumps into the ground.
            float targetHeight = stump ? Next(0.8f, 1.92f) : height;
            float rootBaseRadius;
            if (stump)
            {
                radius = Mathf.Max(0.12f,radius*Next(0.75f,1.35f));
                Vector3 shoulder = Vector3.up * targetHeight * 0.22f;
                // Wide root flare and a slightly slanted, capped cut surface.
                int stumpSides=random.Next(5,9);
                rootBaseRadius=radius*Next(1.15f,2f);
                TrunkLimb(Vector3.zero,shoulder,rootBaseRadius,radius*Next(0.8f,1.2f),stumpSides);
                TrunkLimb(shoulder,new Vector3(radius*Next(-0.45f,0.45f),targetHeight,
                    radius*Next(-0.45f,0.45f)),radius*Next(0.8f,1.2f),
                    radius*Next(0.55f,1f),stumpSides);
            }
            else
            {
            int style=random.Next(5);
            int trunkSegments=style==2?random.Next(5,8):random.Next(3,7);
            int trunkSides=random.Next(5,9);
            float leanAngle=Next(0f,Mathf.PI*2f);
            Vector2 lean=new(Mathf.Cos(leanAngle),Mathf.Sin(leanAngle));
            Vector2 curve=new(-lean.y,lean.x);
            float leanStrength=style switch
            {
                2=>Next(0.09f,0.18f),
                3=>Next(0f,0.035f),
                _=>Next(0.015f,0.075f)
            };
            float curveStrength=style switch
            {
                2=>Next(0.035f,0.085f),
                1=>Next(0.02f,0.06f),
                _=>Next(-0.035f,0.035f)
            };
            float curvePower=Next(1.25f,2.4f);
            Vector3[] trunk=new Vector3[trunkSegments+1];
            float[] trunkRadii=new float[trunkSegments+1];
            trunkRadii[0]=radius*Next(1.25f,2.05f);
            rootBaseRadius=trunkRadii[0];
            for(int i=1;i<=trunkSegments;i++)
            {
                float t=i/(float)trunkSegments;
                float leanDistance=height*leanStrength*Mathf.Pow(t,curvePower);
                float curveDistance=height*curveStrength*Mathf.Sin(t*Mathf.PI);
                Vector2 horizontal=lean*leanDistance+curve*curveDistance+
                    new Vector2(Next(-0.008f,0.008f),Next(-0.008f,0.008f))*height*t;
                trunk[i]=new Vector3(horizontal.x,height*t,horizontal.y);
                float taper=Mathf.Pow(1f-t,Next(0.7f,1.45f));
                trunkRadii[i]=radius*Mathf.Lerp(0.055f,1f,taper)*Next(0.82f,1.18f);
                TrunkLimb(trunk[i-1],trunk[i],trunkRadii[i-1],trunkRadii[i],trunkSides);
            }
            // Every full tree has a clearly readable fork made from two thick,
            // rising trunk continuations. Side limbs are added separately below.
            float forkHeight=Next(0.58f,0.7f);
            float forkScaled=forkHeight*trunkSegments;
            int forkSegment=Mathf.Min(trunkSegments-1,Mathf.FloorToInt(forkScaled));
            float forkAlongSegment=forkScaled-forkSegment;
            Vector3 forkStart=Vector3.Lerp(trunk[forkSegment],trunk[forkSegment+1],
                forkAlongSegment);
            float forkRadius=Mathf.Lerp(trunkRadii[forkSegment],
                trunkRadii[forkSegment+1],forkAlongSegment);
            float forkPhase=Next(0f,Mathf.PI*2f);
            float forkOpening=Next(0.72f,1.08f);
            for(int forkArm=0;forkArm<2;forkArm++)
            {
                float angle=forkPhase+(forkArm==0?-forkOpening:forkOpening);
                Vector3 outward=new(Mathf.Cos(angle),0f,Mathf.Sin(angle));
                float reach=height*Next(0.085f,0.15f);
                Vector3 forkMiddle=forkStart+outward*reach*0.55f+
                    Vector3.up*height*Next(0.12f,0.17f);
                Vector3 forkEnd=forkStart+outward*reach+
                    Vector3.up*height*Next(0.25f,0.34f);
                int lowerFork=BranchLimb(forkStart,forkMiddle,forkRadius*Next(0.68f,0.82f),
                    forkRadius*Next(0.38f,0.5f),trunkSides);
                BranchLimb(forkMiddle,forkEnd,forkRadius*Next(0.38f,0.5f),
                    radius*Next(0.045f,0.09f),Mathf.Max(4,trunkSides-1),lowerFork);
            }
            float[][] profileHeights=
            {
                new[]{0.36f,0.73f,0.76f}, // high fork and one low limb
                new[]{0.31f,0.58f,0.79f}, // alternating climbing limbs
                new[]{0.27f,0.53f,0.77f}, // curved trunk with upright arms
                new[]{0.43f,0.69f,0.83f}, // tall trunk with short side limbs
                new[]{0.46f,0.75f,0.78f}  // narrow crown fork and mid limb
            };
            float[][] profileAngles=
            {
                new[]{0.15f,-1.15f,1.15f},
                new[]{-2.2f,-0.8f,1.15f},
                new[]{3.0f,1.35f,-1.15f},
                new[]{-1.55f,1.45f,-0.15f},
                new[]{-2.45f,-0.92f,0.92f}
            };
            int branches=darkWood?1:profileHeights[style].Length;
            float spread = Next(0.12f, 0.25f) * height;
            float phase = Next(0f, Mathf.PI*2f);
            for (int i = 0; i < branches; i++)
            {
                float t=Mathf.Clamp(profileHeights[style][i]+Next(-0.055f,0.055f),0.22f,0.88f);
                float scaled=t*trunkSegments;
                int segment=Mathf.Min(trunkSegments-1,Mathf.FloorToInt(scaled));
                Vector3 start=Vector3.Lerp(trunk[segment],trunk[segment+1],scaled-segment);
                float angle=phase+profileAngles[style][i]+Next(-0.28f,0.28f);
                Vector3 outward = new(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                float horizontalScale=style==3?Next(0.35f,0.62f):
                    style==2?Next(0.38f,0.72f):Next(0.6f,1.08f);
                float rise=style==2?Next(0.18f,0.34f):
                    (i>0&&style!=3?Next(0.12f,0.27f):Next(0.05f,0.18f));
                Vector3 end=start+outward*spread*horizontalScale+Vector3.up*height*rise;
                int sideBranch=BranchLimb(start,end,radius*Next(0.2f,0.42f),radius*Next(0.035f,0.09f),random.Next(4,7));
                if (!darkWood&&random.NextDouble()<(style==0||style==4?0.5:0.32))
                {
                    Vector3 fork = end + Quaternion.Euler(0,Next(-65f,65f),0)*outward*spread*Next(0.2f,0.45f) + Vector3.up*height*Next(0.03f,0.1f);
                    BranchLimb(end,fork,radius*0.07f,radius*0.018f,5,sideBranch);
                }
            }
            }
            int rootCount=random.Next(4,8);
            float rootPhase=Next(0f,Mathf.PI*2f);
            for(int i=0;i<rootCount;i++)
            {
                float angle=rootPhase+i*Mathf.PI*2f/rootCount+Next(-0.22f,0.22f);
                Vector3 outward=new(Mathf.Cos(angle),0f,Mathf.Sin(angle));
                float rootLength=rootBaseRadius*Next(2.2f,4.3f)*0.6f;
                float rootWidth=rootBaseRadius*Next(0.8f,1.35f)*1.08f;
                float rootHeight=Mathf.Min(targetHeight*0.38f,
                    rootBaseRadius*Next(1.05f,1.75f))*0.6f;
                Root(outward,rootLength,rootWidth,rootHeight,
                    rootBaseRadius*Next(-0.5f,0.5f)*0.6f);
            }
            // Include leaning limbs and their thickness in the requested height.
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            foreach (Vector3 vertex in vertices)
            {
                minY = Mathf.Min(minY, vertex.y);
                maxY = Mathf.Max(maxY, vertex.y);
            }
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 vertex = vertices[i];
                vertex.y = (vertex.y-minY) * targetHeight / (maxY-minY);
                vertices[i] = vertex;
            }
            foreach((int start,int end,Vector3 outward,float threshold,float depth) tip in rootTips)
            {
                float lowest=float.PositiveInfinity,highest=float.NegativeInfinity;
                for(int i=tip.start;i<tip.end;i++)
                {
                    Vector3 vertex=vertices[i];
                    if(Vector3.Dot(new Vector3(vertex.x,0f,vertex.z),tip.outward)<tip.threshold) continue;
                    lowest=Mathf.Min(lowest,vertex.y); highest=Mathf.Max(highest,vertex.y);
                }
                for(int i=tip.start;i<tip.end;i++)
                {
                    Vector3 vertex=vertices[i];
                    if(Vector3.Dot(new Vector3(vertex.x,0f,vertex.z),tip.outward)<tip.threshold) continue;
                    // Keep a thin buried cross-section. Flattening every terminal
                    // vertex to one height collapses the cap into open triangles.
                    vertex.y=-tip.depth+0.015f*Mathf.InverseLerp(lowest,highest,vertex.y);
                    vertices[i]=vertex;
                }
            }
            // Roots lie in the terrain's tangent plane. The lower trunk begins
            // along that same normal, then smoothly curves back toward world-up
            // so trees grow skyward instead of remaining perpendicular to a slope.
            Quaternion terrainRotation=Quaternion.FromToRotation(Vector3.up,
                terrainNormal.sqrMagnitude>0.0001f?terrainNormal.normalized:Vector3.up);
            float uprightHeight=Mathf.Max(0.01f,targetHeight*0.45f);
            for(int i=0;i<vertices.Count;i++)
            {
                Vector3 vertex=vertices[i];
                float uprightBlend=Mathf.SmoothStep(0f,1f,
                    Mathf.Clamp01(vertex.y/uprightHeight));
                vertices[i]=Quaternion.Slerp(terrainRotation,Quaternion.identity,uprightBlend)*vertex;
            }
            Mesh mesh = new() { name = $"Random low-poly {(stump ? "stump" : "tree")} {seed}" };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles,0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            trunkMesh=null;
            if(branchMeshes!=null)
            {
                Mesh PartMesh(List<(int start,int end)> ranges,string partName)
                {
                    var partVertices=new List<Vector3>();
                    var partTriangles=new List<int>();
                    foreach(var range in ranges)
                    for(int i=range.start;i<range.end;i++)
                    {
                        partVertices.Add(vertices[triangles[i]]);
                        partTriangles.Add(partTriangles.Count);
                    }
                    var part=new Mesh {name=partName};
                    part.SetVertices(partVertices); part.SetTriangles(partTriangles,0);
                    part.RecalculateNormals(); part.RecalculateBounds();
                    return part;
                }
                int cursor=0;
                var trunkRanges=new List<(int start,int end)>();
                for(int i=0;i<branchRanges.Count;i++)
                {
                    var range=branchRanges[i];
                    if(cursor<range.start) trunkRanges.Add((cursor,range.start));
                    branchMeshes.Add(PartMesh(new List<(int start,int end)>{range},
                        $"Wood branch {i+1} mesh"));
                    cursor=range.end;
                }
                if(cursor<triangles.Count) trunkRanges.Add((cursor,triangles.Count));
                trunkMesh=PartMesh(trunkRanges,"Wood trunk and roots mesh");
                if(trunkPartMeshes!=null)
                {
                    trunkSections=trunkSectionRanges.Count;
                    foreach(var range in trunkSectionRanges)
                        trunkPartMeshes.Add(PartMesh(new List<(int start,int end)>{range},
                            $"Wood trunk section {trunkPartMeshes.Count+1} mesh"));
                    foreach(var range in rootRanges)
                        trunkPartMeshes.Add(PartMesh(new List<(int start,int end)>{range},
                            $"Wood root {trunkPartMeshes.Count+1-trunkSections} mesh"));
                }
            }
            return mesh;
        }

        private void OnDestroy()
        {
            if(darkWoodMaterial!=null) Destroy(darkWoodMaterial);
            foreach (TreeSeed tree in trees)
                ReleaseTreeMeshes(tree);
            foreach (Material material in materials) if (material != null) Destroy(material);
            foreach (TrackedObject item in objects)
                if (item.root != null) { if (item.hidden) item.root.SetActive(true); item.PhysicsActive(true); }
        }
    }

    // Marks generated visuals for streaming; destruction uses PhysicalDestructible.
    public sealed class GeneratedTree : MonoBehaviour
    {
        readonly List<Vector3> destructiveImpacts=new();
        readonly List<Vector3> trunkImpacts=new();
        readonly List<GameObject> impactMarkers=new();
        [SerializeField,Min(0.01f)] float averageTrunkRadius=0.5f;
        [SerializeField,Range(4,10)] int impactsToBreak=4;
        const float MarkerDiameter=0.08f;
        Material markerMaterial;
        bool broken;
        int trunkBreakProgress;
        float nextSupportCheck;

        void FixedUpdate()
        {
            if(Time.time<nextSupportCheck) return;
            nextSupportCheck=Time.time+0.5f;
            ValidateTrunkConnections();
        }

        public void ValidateTrunkConnections()
        {
            foreach(var part in GetComponentsInChildren<TreeStructuralPart>())
                part.ValidateTrunkSupport();
        }

        public float AverageTrunkRadius => averageTrunkRadius;
        public float TrunkBreakRadius => 6f*averageTrunkRadius;
        public int ImpactsToBreak => impactsToBreak;
        public int TrunkBreakProgress => trunkBreakProgress;
        public bool IsBroken => broken;
        public IReadOnlyList<Vector3> DestructiveImpacts => destructiveImpacts;

        public void Configure(float radius,int threshold)
        {
            averageTrunkRadius=Mathf.Max(0.01f,radius);
            impactsToBreak=Mathf.Clamp(threshold,4,10);
        }

        public bool BreakAtMidpoint()
        {
            if(broken) return true;
            TreeStructuralPart crossing=null,upper=null;
            float minimum=float.PositiveInfinity,maximum=float.NegativeInfinity;
            var sections=GetComponentsInChildren<TreeStructuralPart>();
            foreach(var part in sections)
            {
                if(!part.IsTrunk) continue;
                Mesh mesh=part.GetComponent<MeshFilter>()?.sharedMesh;
                if(mesh==null) continue;
                Bounds bounds=mesh.bounds;
                float bottom=part.transform.TransformPoint(new Vector3(bounds.center.x,
                    bounds.min.y,bounds.center.z)).y;
                float top=part.transform.TransformPoint(new Vector3(bounds.center.x,
                    bounds.max.y,bounds.center.z)).y;
                minimum=Mathf.Min(minimum,bottom,top);
                maximum=Mathf.Max(maximum,bottom,top);
            }
            if(!float.IsFinite(minimum)||!float.IsFinite(maximum)||maximum<=minimum)
                return false;
            float middle=(minimum+maximum)*0.5f;
            float nearestCut=float.PositiveInfinity,nearestJoint=float.PositiveInfinity;
            foreach(var part in sections)
            {
                if(!part.IsTrunk) continue;
                Mesh mesh=part.GetComponent<MeshFilter>()?.sharedMesh;
                if(mesh==null) continue;
                Bounds bounds=mesh.bounds;
                float a=part.transform.TransformPoint(new Vector3(bounds.center.x,
                    bounds.min.y,bounds.center.z)).y;
                float b=part.transform.TransformPoint(new Vector3(bounds.center.x,
                    bounds.max.y,bounds.center.z)).y;
                float low=Mathf.Min(a,b),high=Mathf.Max(a,b);
                float centre=Mathf.Abs((low+high)*0.5f-middle);
                if(middle>low+0.001f&&middle<high-0.001f&&centre<nearestCut)
                { crossing=part; nearestCut=centre; }
                if(part.CanBreakTrunkSupport&&Mathf.Abs(low-middle)<nearestJoint)
                { upper=part; nearestJoint=Mathf.Abs(low-middle); }
            }
            Vector3 cutPoint=new(transform.position.x,middle,transform.position.z);
            if(crossing!=null&&crossing.GetComponent<PhysicalDestructible>() is { } solid&&
                solid.ApplyImpact(10f,10f,1f,cutPoint,Vector3.up,30f,
                    treeSplitPoint:cutPoint))
                broken=true;
            else if(upper!=null&&upper.BreakTrunkSupport())
                broken=true;
            if(!broken) return false;
            trunkBreakProgress=impactsToBreak;
            trunkImpacts.Clear();
            return true;
        }

        public void RecordDestructiveImpact(Vector3 point,bool trunkImpact=true)
        {
            destructiveImpacts.Add(point);
            if(trunkImpact) trunkImpacts.Add(point);
            ShowImpactMarker(point);
            // Limit history on long-lived trees without discarding recent hits.
            if(destructiveImpacts.Count>64) destructiveImpacts.RemoveAt(0);
            if(trunkImpacts.Count>64) trunkImpacts.RemoveAt(0);
            if(impactMarkers.Count>64)
            {
                GameObject oldest=impactMarkers[0];
                impactMarkers.RemoveAt(0);
                if(Application.isPlaying) Destroy(oldest);
                else DestroyImmediate(oldest);
            }
            if(broken||!trunkImpact) return;
            float collisionRange=TrunkBreakRadius;
            float radiusSquared=collisionRange*collisionRange;
            trunkBreakProgress=0;
            for(int i=0;i<trunkImpacts.Count;i++)
            {
                Vector3 sum=Vector3.zero;
                int count=0;
                for(int j=0;j<trunkImpacts.Count;j++)
                    if((trunkImpacts[j]-trunkImpacts[i]).sqrMagnitude<=radiusSquared)
                    { sum+=trunkImpacts[j]; count++; }
                Vector3 center=sum/count;
                // Every counted point must be in the radius around the actual cut.
                sum=Vector3.zero; count=0;
                for(int j=0;j<trunkImpacts.Count;j++)
                    if((trunkImpacts[j]-center).sqrMagnitude<=radiusSquared)
                    { sum+=trunkImpacts[j]; count++; }
                trunkBreakProgress=Mathf.Max(trunkBreakProgress,count);
                if(count<impactsToBreak) continue;
                center=sum/count;
                TreeStructuralPart target=null,upper=null;
                float nearest=float.PositiveInfinity,nearestJoint=float.PositiveInfinity;
                foreach(var part in GetComponentsInChildren<TreeStructuralPart>())
                {
                    if(!part.IsTrunk||part.GetComponent<PhysicalDestructible>() is not { HasBeenDestroyed: false })
                        continue;
                    Mesh mesh=part.GetComponent<MeshFilter>()?.sharedMesh;
                    if(mesh==null) continue;
                    Bounds bounds=mesh.bounds;
                    Vector3 bottom=part.transform.TransformPoint(new Vector3(bounds.center.x,bounds.min.y,bounds.center.z));
                    Vector3 top=part.transform.TransformPoint(new Vector3(bounds.center.x,bounds.max.y,bounds.center.z));
                    float jointDistance=Mathf.Abs(Mathf.Min(bottom.y,top.y)-center.y);
                    if(part.CanBreakTrunkSupport&&jointDistance<nearestJoint)
                    { nearestJoint=jointDistance; upper=part; }
                    if(center.y<=Mathf.Min(bottom.y,top.y)+0.001f||
                        center.y>=Mathf.Max(bottom.y,top.y)-0.001f) continue;
                    float distance=part.DistanceTo(center);
                    if(distance>=nearest) continue;
                    nearest=distance; target=part;
                }
                // A chipped section may no longer enclose the average hit height.
                // In that case release the nearest trunk joint so accumulated
                // damage still severs the supported upper tree.
                bool split=target!=null&&target.GetComponent<PhysicalDestructible>()
                    .ApplyImpact(10f,10f,1f,center,Vector3.up,30f,
                        treeSplitPoint:center);
                if(!split&&upper!=null) split=upper.BreakTrunkSupport();
                if(!split) split=BreakAtMidpoint();
                if(!split) continue;
                broken=true;
                trunkBreakProgress=impactsToBreak;
                destructiveImpacts.Clear();
                trunkImpacts.Clear();
                return;
            }
        }

        void ShowImpactMarker(Vector3 point)
        {
            if(markerMaterial==null)
            {
                Shader shader=Shader.Find("Universal Render Pipeline/Unlit")??
                    Shader.Find("Unlit/Color")??Shader.Find("Sprites/Default")??
                    Shader.Find("Standard");
                markerMaterial=new Material(shader) { name="Tree impact bright green" };
                if(markerMaterial.HasProperty("_BaseColor"))
                    markerMaterial.SetColor("_BaseColor",Color.green);
                if(markerMaterial.HasProperty("_Color"))
                    markerMaterial.SetColor("_Color",Color.green);
            }
            GameObject marker=GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name=$"Tree destruction collision point {impactMarkers.Count+1}";
            marker.AddComponent<TreeImpactMarker>();
            marker.transform.SetParent(transform,true);
            marker.transform.position=point;
            marker.transform.localScale=Vector3.one*MarkerDiameter;
            var collider=marker.GetComponent<Collider>();
            collider.enabled=false;
            if(Application.isPlaying) Destroy(collider);
            else DestroyImmediate(collider);
            var renderer=marker.GetComponent<MeshRenderer>();
            renderer.sharedMaterial=markerMaterial;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows=false;
            impactMarkers.Add(marker);
        }

        void OnDestroy()
        {
            if(markerMaterial==null) return;
            if(Application.isPlaying) Destroy(markerMaterial);
            else DestroyImmediate(markerMaterial);
        }
    }
    // Visual-only marker: the destruction world's renderer scan must not turn it into a solid.
    public sealed class TreeImpactMarker : MonoBehaviour { }
    public static class MeshFaceReduction
    {
        public static Mesh Create(Mesh source,float fraction,string prefix)
        {
            int[] sourceTriangles=source.triangles;
            Vector3[] sourceVertices=source.vertices;
            Vector3[] sourceNormals=source.normals;
            Color[] sourceColors=source.colors;
            int sourceFaces=sourceTriangles.Length/3;
            int targetFaces=Mathf.Max(1,Mathf.RoundToInt(sourceFaces*Mathf.Clamp01(fraction)));
            var vertices=new List<Vector3>(targetFaces*3);
            var normals=new List<Vector3>(targetFaces*3);
            var colors=new List<Color>(targetFaces*3);
            var triangles=new List<int>(targetFaces*3);
            bool hasNormals=sourceNormals.Length==sourceVertices.Length;
            bool hasColors=sourceColors.Length==sourceVertices.Length;
            for(int face=0;face<targetFaces;face++)
            {
                int sourceFace=Mathf.Min(sourceFaces-1,
                    Mathf.FloorToInt((face+0.5f)*sourceFaces/targetFaces));
                for(int corner=0;corner<3;corner++)
                {
                    int sourceIndex=sourceTriangles[sourceFace*3+corner];
                    triangles.Add(vertices.Count);
                    vertices.Add(sourceVertices[sourceIndex]);
                    if(hasNormals) normals.Add(sourceNormals[sourceIndex]);
                    if(hasColors) colors.Add(sourceColors[sourceIndex]);
                }
            }
            Mesh mesh=new(){name=$"{prefix} (20% faces)",indexFormat=source.indexFormat};
            mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);
            if(hasNormals) mesh.SetNormals(normals); else mesh.RecalculateNormals();
            if(hasColors) mesh.SetColors(colors);
            mesh.bounds=source.bounds;
            return mesh;
        }
    }
}
