using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Wyvernland.Editor
{
    public static class TreeBranchDestructionValidation
    {
        [MenuItem("Tools/Wyvernland/Validate Tree Radius and Mass Center")]
        public static void RunRadiusAndMassCenter()
        {
            var root=new GameObject("Tree radius and mass center validation");
            var detached=new List<GameObject>();
            try
            {
                var field=root.AddComponent<ProceduralTreeField>();
                var material=new Material(Shader.Find("Standard")??Shader.Find("Sprites/Default"));
                ((List<Material>)typeof(ProceduralTreeField).GetField("materials",
                    BindingFlags.Instance|BindingFlags.NonPublic).GetValue(field)).Add(material);
                Type seedType=typeof(ProceduralTreeField).GetNestedType("TreeSeed",BindingFlags.NonPublic);
                var seeds=(IList)typeof(ProceduralTreeField).GetField("trees",
                    BindingFlags.Instance|BindingFlags.NonPublic).GetValue(field);
                var create=typeof(ProceduralTreeField).GetMethod("CreateTree",
                    BindingFlags.Instance|BindingFlags.NonPublic);
                var trees=new List<GeneratedTree>();
                foreach(int seed in new[]{101,103,109,113,127,131})
                {
                    object data=Activator.CreateInstance(seedType,true);
                    seedType.GetField("seed").SetValue(data,seed);
                    seedType.GetField("position").SetValue(data,new Vector3(seed*2f,0f,0f));
                    seedType.GetField("terrainNormal").SetValue(data,Vector3.up);
                    seeds.Add(data);
                    create.Invoke(field,new[]{data});
                    trees.Add(((GameObject)seedType.GetField("instance").GetValue(data)).GetComponent<GeneratedTree>());
                }
                typeof(ProceduralTreeField).GetMethod("ConfigureTrunkBreakThresholds",
                    BindingFlags.Instance|BindingFlags.NonPublic).Invoke(field,null);
                float minimum=trees.Min(tree=>tree.AverageTrunkRadius);
                float maximum=trees.Max(tree=>tree.AverageTrunkRadius);
                Require(maximum>minimum&&trees.OrderBy(tree=>tree.AverageTrunkRadius).First().ImpactsToBreak==3&&
                    trees.OrderBy(tree=>tree.AverageTrunkRadius).Last().ImpactsToBreak==15,
                    "Smallest and largest randomly generated average trunk radii require 3 and 15 destructions");
                foreach(var tree in trees)
                    Require(tree.ImpactsToBreak==Mathf.RoundToInt(Mathf.Lerp(3f,15f,
                        Mathf.InverseLerp(minimum,maximum,tree.AverageTrunkRadius))),
                        "Generated trunk thresholds increase linearly with average radius");
                var probe=root.AddComponent<GeneratedTree>();
                foreach(var sample in new[]{(radius:.1f,hits:3),(radius:.2f,hits:3),
                    (radius:.6f,hits:9),(radius:1f,hits:15),(radius:2f,hits:15)})
                {
                    probe.Configure(sample.radius,3);probe.ConfigureRadiusRange(.2f,1f);
                    Require(probe.ImpactsToBreak==sample.hits,"Radius scaling clamps to 3–15 and maps the midpoint to 9");
                }
                probe.ConfigureRadiusRange(2f,2f);
                Require(probe.ImpactsToBreak==3,"A uniform radius population has a valid minimum threshold");

                var upper=trees[1].GetComponentsInChildren<TreeStructuralPart>()
                    .First(part=>part.IsTrunk&&part.CanBreakTrunkSupport);
                Require(upper.BreakTrunkSupport(),"A generated upper trunk detaches at a structural seam");
                detached.Add(upper.gameObject);
                var body=upper.GetComponent<Rigidbody>();
                var renderers=upper.GetComponentsInChildren<MeshRenderer>()
                    .Where(renderer=>renderer.GetComponentInParent<TreeStructuralPart>() is { IsTrunk: true } part&&
                        renderer.GetComponentInParent<PhysicalDestructible>()==part.GetComponent<PhysicalDestructible>()&&
                        renderer.GetComponent<MeshFilter>()?.sharedMesh!=null).ToArray();
                Bounds expected=renderers[0].bounds;
                foreach(var renderer in renderers.Skip(1)) expected.Encapsulate(renderer.bounds);
                Require(renderers.Length>1&&upper.GetComponentsInChildren<TreeStructuralPart>().Any(part=>part.IsBranch),
                    "Center validation includes multiple trunk meshes and offset branches");
                Require(Vector3.Distance(body.worldCenterOfMass,expected.center)<.001f,
                    "Detached trunk center of mass matches the connected trunk mesh center despite branches and the split sphere");
                upper.GetComponent<PhysicalDestructible>().RefreshMass();
                Require(Vector3.Distance(body.worldCenterOfMass,expected.center)<.001f,
                    "Refreshing material mass preserves the trunk mesh center of mass");
                foreach(var rotation in new[]{Quaternion.identity,
                    Quaternion.Euler(0f,0f,90f),Quaternion.Euler(37f,61f,112f)})
                    ValidateRepeatedHalfSplits(root,rotation);
                RunTrunkSplitFalling();
                Debug.Log("TREE_RADIUS_MASS_CENTER_VALIDATION_PASS: radius endpoints, linear 3–15 scaling, trunk mesh mass center, three generations of independent crosswise cuts at upright/sideways/tilted rotations, and seam/mesh split falling.");
            }
            finally
            {
                foreach(var part in detached) if(part!=null) UnityEngine.Object.DestroyImmediate(part);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static void ValidateRepeatedHalfSplits(GameObject root,Quaternion rotation)
        {
            var owner=new GameObject("Repeatable tree");owner.transform.SetParent(root.transform,false);
            owner.transform.localPosition=Vector3.right*1000f;
            owner.transform.localRotation=rotation;
            var tree=owner.AddComponent<GeneratedTree>();tree.Configure(.5f,3);
            var trunk=GameObject.CreatePrimitive(PrimitiveType.Cube);
            trunk.name="Repeatable trunk";trunk.transform.SetParent(owner.transform,false);
            trunk.transform.localScale=new Vector3(1f,8f,1f);
            var solid=PhysicalDestructible.Ensure(trunk);solid.ApplyMaterial("Wood");
            var structure=trunk.AddComponent<TreeStructuralPart>();structure.SetTrunk(true);structure.SetGroundAnchor();
            var fragments=new List<PhysicalDestructible>();
            var current=new List<PhysicalDestructible>{solid};
            try
            {
                for(int generation=0;generation<3;generation++)
                {
                    var next=new List<PhysicalDestructible>();
                    foreach(var part in current)
                    {
                        var tracker=part.GetComponent<TreeStructuralPart>().Tree;
                        Require(tracker!=null&&tracker.TrunkBreakProgress==0&&tracker.DestructiveImpacts.Count==0,
                            "Each resulting half starts with an independent zero destruction count");
                        part.Fragmented+=(_,pieces)=>{ next.AddRange(pieces);fragments.AddRange(pieces); };
                        Require(part.GetComponent<TreeStructuralPart>().TryGetSolidBounds(out Bounds bounds),
                            "A repeatable half retains solid bounds");
                        Vector3 point=bounds.center;
                        double volume=part.SolidVolumeCubicMetres;
                        for(int hit=0;hit<tracker.ImpactsToBreak;hit++)
                        {
                            tracker.RecordDestructiveImpact(point);
                            if(hit<2)
                            {
                                Require(part!=null&&!part.HasBeenDestroyed&&tracker.TrunkBreakProgress==hit+1,
                                    "Each half requires a fresh full set of destruction points");
                                part.RefreshMass();
                                Require(Math.Abs(part.SolidVolumeCubicMetres-volume)<.000001d,
                                    "Destruction point markers do not become part of the trunk's wood geometry or mass");
                            }
                        }
                    }
                    Require(next.Count==current.Count*2,
                        $"Repeat split: rotation {rotation.eulerAngles}, generation {generation+1}, got {next.Count} halves of {current.Count*2}; "+
                        string.Join("; ",current.Where(part=>part!=null&&!part.HasBeenDestroyed).Select(part=>
                            $"{part.name}: {part.GetComponent<TreeStructuralPart>().Tree.TrunkBreakProgress}, {part.GetComponent<TreeStructuralPart>().Tree.LastBreakReason}")));
                    foreach(var half in next)
                    {
                        Require(Quaternion.Angle(half.transform.rotation,rotation)<.01f,
                            "Every generation preserves the tree's rotated local cutting frame");
                        Bounds meshBounds=half.GetComponent<MeshFilter>().sharedMesh.bounds;
                        Require(Mathf.Abs(meshBounds.size.y-8f/Mathf.Pow(2f,generation+1))<.15f&&
                            Mathf.Abs(meshBounds.size.x-1f)<.005f&&Mathf.Abs(meshBounds.size.z-1f)<.005f,
                            $"Crosswise local cut: rotation {rotation.eulerAngles}, generation {generation+1}, size {meshBounds.size}");
                        Mesh mesh=half.GetComponent<MeshFilter>().sharedMesh;
                        Vector3[] vertices=mesh.vertices;
                        foreach(int slot in Enumerable.Range(0,mesh.subMeshCount))
                        {
                            int[] indices=mesh.GetTriangles(slot);
                            for(int triangle=0;triangle<indices.Length;triangle+=3)
                            {
                                Vector3 normal=Vector3.Cross(vertices[indices[triangle+1]]-vertices[indices[triangle]],
                                    vertices[indices[triangle+2]]-vertices[indices[triangle]]).normalized;
                                if(Mathf.Abs(normal.y)<=.5f) continue;
                                Require(Mathf.Abs(normal.x)<.001f&&Mathf.Abs(normal.y)>.99f,
                                    "All cut caps stay crosswise with shallow division angles rotated around local X");
                            }
                        }
                        var tracker=half.GetComponent<TreeStructuralPart>().Tree;
                        Require(tracker!=null&&!tracker.IsBroken&&tracker.TrunkBreakProgress==0&&
                            tracker.DestructiveImpacts.Count==0&&tracker.ImpactsToBreak==3,
                            "Every new half resets its counter and preserves its radius-based threshold");
                        var body=half.GetComponent<Rigidbody>();
                        if(body!=null)
                            Require(Vector3.Distance(body.centerOfMass,half.GetComponent<MeshFilter>().sharedMesh.bounds.center)<.001f,
                                "Repeatedly split dynamic trunks keep the mesh center of mass");
                    }
                    current=next;
                }
                Require(current.Count==8,"Three generations leave eight independently destructible halves");
            }
            finally
            {
                foreach(var fragment in fragments) if(fragment!=null) UnityEngine.Object.DestroyImmediate(fragment.gameObject);
            }
        }

        [MenuItem("Tools/Wyvernland/Validate Trunk Split Falling")]
        public static void RunTrunkSplitFalling()
        {
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var root=new GameObject("Trunk split falling validation");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
            var pieces=new List<PhysicalDestructible>();
            var random=UnityEngine.Random.state;
            try
            {
                foreach(bool seam in new[]{true,false})
                {
                    var treeRoot=new GameObject("Split falling tree");treeRoot.transform.SetParent(root.transform,false);
                    treeRoot.transform.localPosition=Vector3.right*(seam?0f:10f);
                    var tree=treeRoot.AddComponent<GeneratedTree>();
                    TreeStructuralPart Section(float height,float length)
                    {
                        var part=GameObject.CreatePrimitive(PrimitiveType.Cube);
                        part.transform.SetParent(treeRoot.transform,false);
                        part.transform.localPosition=Vector3.up*height;
                        part.transform.localScale=new Vector3(.5f,length,.5f);
                        var physical=PhysicalDestructible.Ensure(part);physical.ApplyMaterial("Wood");
                        physical.Fragmented+=(_,fragments)=>
                        {
                            pieces.AddRange(fragments);
                            foreach(var fragment in fragments)
                                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(fragment.gameObject,scene);
                        };
                        var structure=part.AddComponent<TreeStructuralPart>();structure.SetTrunk(true);
                        return structure;
                    }
                    TreeStructuralPart upper;
                    if(seam)
                    {
                        var lower=Section(0.5f,1f);lower.SetGroundAnchor();
                        upper=Section(2.5f,3f);upper.AttachTo(lower);
                        Physics.SyncTransforms();
                        Require(upper.BreakTrunkSupport(),"A segment seam releases the upper trunk");
                    }
                    else
                    {
                        var trunk=Section(2f,4f);trunk.SetGroundAnchor();
                        Physics.SyncTransforms();
                        Require(tree.BreakAtMidpoint(),"A solid trunk splits through its midpoint");
                        upper=pieces.Where(piece=>
                            { var candidate=piece!=null?piece.GetComponent<Rigidbody>():null; return candidate!=null&&!candidate.isKinematic; })
                            .OrderByDescending(piece=>piece.GetComponent<Collider>().bounds.center.y)
                            .First().GetComponent<TreeStructuralPart>();
                    }
                    var body=upper.GetComponent<Rigidbody>();
                    var sphere=upper.transform.Find("Trunk split sphere")?.GetComponent<SphereCollider>();
                    Require(sphere!=null&&sphere.enabled&&!sphere.isTrigger&&sphere.attachedRigidbody==body,
                        "The upper half carries a solid sphere collider at its division point");
                    float cutHeight=seam?1f:2f;
                    Require(Mathf.Abs(sphere.transform.position.y-cutHeight)<.001f&&
                        Mathf.Abs(sphere.transform.lossyScale.x-1f)<.001f&&
                        upper.GetComponentsInChildren<MeshFilter>().Length==1,
                        "The small sphere stays at the cut with world scale and leaves destruction meshes intact");
                    double volume=upper.GetComponent<PhysicalDestructible>().SolidVolumeCubicMetres;
                    upper.GetComponent<PhysicalDestructible>().RefreshMass();
                    Require(Math.Abs(upper.GetComponent<PhysicalDestructible>().SolidVolumeCubicMetres-volume)<.000001d,
                        "The physics sphere does not add material volume");
                    float startHeight=body.worldCenterOfMass.y;
                    var physics=scene.GetPhysicsScene();
                    Physics.SyncTransforms();
                    float tilt=0f;
                    for(int step=0;step<250;step++)
                    {
                        physics.Simulate(.02f);
                        tilt=Mathf.Max(tilt,Vector3.Angle(body.rotation*Vector3.up,Vector3.up));
                    }
                    Require(tilt>30f&&
                        body.worldCenterOfMass.y<startHeight-.5f,
                        $"A straight upper trunk tips and falls instead of balancing on the stump (seam={seam}, tilt={tilt:F1}, drop={startHeight-body.worldCenterOfMass.y:F2})");
                    UnityEngine.Object.DestroyImmediate(upper.gameObject);
                    UnityEngine.Object.DestroyImmediate(treeRoot);
                }
                Debug.Log("TREE_SPLIT_FALLING_VALIDATION_PASS: seam and mesh splits tip and fall; sphere colliders preserve wood geometry and material volume.");
            }
            finally
            {
                UnityEngine.Random.state=random;
                foreach(var piece in pieces) if(piece!=null) UnityEngine.Object.DestroyImmediate(piece.gameObject);
                UnityEngine.Object.DestroyImmediate(root);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [MenuItem("Tools/Wyvernland/Validate Repeated Trunk Cuts")]
        public static void RunRepeatedTrunkCuts()
        {
            var random=UnityEngine.Random.state;
            var branches=new List<Mesh>();var sections=new List<Mesh>();var parents=new List<int>();
            object[] args={103,false,Vector3.up,false,branches,null,0,parents,sections,7f};
            Mesh treeMesh=(Mesh)typeof(ProceduralTreeField).GetMethod("GenerateTreeMesh",
                BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
            try
            {
                ValidateRepeatedGeneratedChips(sections[1]);
                Debug.Log("TREE_REPEATED_CUT_VALIDATION_PASS: the real 7 m short tree splits on its ninth damaging hit after repeated chipping.");
            }
            finally
            {
                UnityEngine.Random.state=random;
                UnityEngine.Object.DestroyImmediate(treeMesh);
                UnityEngine.Object.DestroyImmediate((Mesh)args[5]);
                foreach(var mesh in branches) UnityEngine.Object.DestroyImmediate(mesh);
                foreach(var mesh in sections) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
        static void ValidateRepeatedGeneratedChips(Mesh source)
        {
            var root=new GameObject("Generic tree validation repeatedly chipped short tree");
            var tree=root.AddComponent<GeneratedTree>();tree.Configure(.22f,9);
            var trunk=new GameObject("Generic tree validation repeatedly chipped trunk");trunk.transform.SetParent(root.transform,false);
            var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));
            trunk.AddComponent<MeshFilter>().sharedMesh=source;
            trunk.AddComponent<MeshRenderer>().sharedMaterial=material;
            trunk.AddComponent<MeshCollider>().sharedMesh=source;
            var properties=PhysicalDestructible.Ensure(trunk);properties.ApplyMaterial("Wood");
            var structure=trunk.AddComponent<TreeStructuralPart>();structure.SetTrunk(true);structure.SetGroundAnchor();
            var fragments=new List<PhysicalDestructible>();
            properties.Fragmented+=(_,pieces)=>fragments.AddRange(pieces);
            try
            {
                for(int hit=0;hit<9;hit++)
                {
                    Require(trunk!=null&&!tree.IsBroken,"The short trunk stays supported before its ninth damaging hit");
                    // Edit-mode tests do not advance Time.time between impacts.
                    typeof(PhysicalDestructible).GetField("nextImpact",BindingFlags.Instance|BindingFlags.NonPublic)
                        .SetValue(properties,-1f);
                    Physics.SyncTransforms();
                    Vector3 query=source.bounds.center;
                    query.y=Mathf.Lerp(source.bounds.min.y,source.bounds.max.y,.2f+(hit%4)*.12f);query.z-=3f;
                    Vector3 point=default;bool contact=false;
                    foreach(var collider in trunk.GetComponentsInChildren<Collider>())
                        if(collider.enabled&&collider.Raycast(new Ray(query,Vector3.forward),out RaycastHit impact,10f))
                        { point=impact.point;contact=true;break; }
                    Require(contact&&properties.ApplyImpact(7f,8f,1.4f,point,Vector3.forward,20f,otherSolidVolume:.0025d),
                        $"Short-tree damaging hit {hit+1} chips the actual contacted solid");
                    if(hit<8)
                    {
                        Mesh live=trunk.GetComponentsInChildren<MeshFilter>()
                            .Last(filter=>filter.sharedMesh!=null&&filter.GetComponent<Renderer>().enabled).sharedMesh;
                        ValidateGeneratedRemainderCut(live,point,hit+1);
                    }
                }
                Require(tree.IsBroken&&tree.TrunkBreakProgress==0&&tree.LastBreakReason.Contains("Tree split"),
                    "The real generated tree splits at 9/9 instead of rejecting a repeatedly chipped remainder");
                foreach(var piece in fragments)
                    if(piece!=null)
                        Require(DestructionMesh.IsClosedConnected(piece.GetComponent<MeshFilter>().sharedMesh),
                            "Repeated chipping and the final tree cut emit closed solid geometry");
            }
            finally
            {
                foreach(var piece in fragments) if(piece!=null) UnityEngine.Object.DestroyImmediate(piece.gameObject);
                UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(material);
            }
        }
        static void ValidateGeneratedRemainderCut(Mesh live,Vector3 point,int hit)
        {
            var sample=new GameObject("Generic tree validation repeated chip cut");
            var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));
            sample.AddComponent<MeshFilter>().sharedMesh=live;
            sample.AddComponent<MeshRenderer>().sharedMaterial=material;
            sample.AddComponent<MeshCollider>().sharedMesh=live;
            var properties=PhysicalDestructible.Ensure(sample);properties.ApplyMaterial("Wood");
            double volume=properties.SolidVolumeCubicMetres;
            var pieces=new List<PhysicalDestructible>();properties.Fragmented+=(_,p)=>pieces.AddRange(p);
            try
            {
                Require(properties.ApplyImpact(10f,10f,1f,point,Vector3.up,30f,treeSplitPoint:point),
                    $"Short-tree remainder after damaging hit {hit} can split at last hit {point}");
                Require(pieces.All(piece=>DestructionMesh.IsClosedConnected(piece.GetComponent<MeshFilter>().sharedMesh))&&
                    Math.Abs(pieces.Sum(piece=>piece.SolidVolumeCubicMetres)-volume)<=Math.Max(.00001d,volume*.002d),
                    "The cut preserves closed fragment topology and the remaining wood volume");
            }
            finally
            {
                foreach(var piece in pieces) if(piece!=null) UnityEngine.Object.DestroyImmediate(piece.gameObject);
                if(sample!=null) UnityEngine.Object.DestroyImmediate(sample);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }
        [MenuItem("Tools/Wyvernland/Validate Tree Break Feedback")]
        public static void RunBreakFeedback()
        {
            var root=new GameObject("Tree break feedback validation");
            var randomState=UnityEngine.Random.state;
            try
            {
                var tree=root.AddComponent<GeneratedTree>();tree.Configure(0.1f,4);
                Require(tree.LastBreakReason.Contains("No destruction"),"New tree has no last destruction");
                tree.RecordDestructiveImpact(Vector3.zero,false);
                Require(tree.TrunkBreakProgress==0&&tree.LastBreakReason.Contains("Only trunk"),
                    "Branch and root destruction does not count toward trunk splitting");
                tree.RecordDestructiveImpact(Vector3.zero);
                Require(tree.TrunkBreakProgress==1&&tree.LastBreakReason.Contains("Need 3"),
                    "HUD reports missing nearby trunk destructions");
                for(int i=1;i<4;i++) tree.RecordDestructiveImpact(Vector3.right*i*5f);
                Require(tree.TrunkBreakProgress==1&&tree.LastBreakReason.Contains("too far apart"),
                    "Scattered trunk destruction explains the cluster rule");
                for(int i=0;i<3;i++) tree.RecordDestructiveImpact(Vector3.right*15f);
                Require(!tree.IsBroken&&tree.LastBreakReason.Contains("No supported trunk"),
                    "A threshold without remaining trunk at the hit height explains failed support");
                var compactRoot=new GameObject("Compact tree feedback sample");
                compactRoot.transform.SetParent(root.transform);
                var compactTree=compactRoot.AddComponent<GeneratedTree>();compactTree.Configure(.1f,4);
                var compact=GameObject.CreatePrimitive(PrimitiveType.Cube);
                compact.transform.SetParent(compactRoot.transform,false);
                compact.transform.localScale=Vector3.one*.05f;
                PhysicalDestructible.Ensure(compact).ApplyMaterial("Wood");
                compact.AddComponent<TreeStructuralPart>().SetTrunk(true);
                for(int i=0;i<4;i++) compactTree.RecordDestructiveImpact(Vector3.zero);
                Require(!compactTree.IsBroken&&compactTree.LastBreakReason.Contains("valid solid halves"),
                    "A protected compact trunk reports the failed solid cut instead of claiming success");
                ValidateImpactCluster(root);
                ValidateImpactClusterAcrossTrunkGap(root);
                ValidateChippedRemainderBreak(root);
                Debug.Log("TREE_BREAK_FEEDBACK_VALIDATION_PASS: branch exclusions, nearby hit deficit, scattered hits, unsupported cut height, real trunk splitting, gap handling and chipped child remainders.");
            }
            finally
            {
                UnityEngine.Random.state=randomState;
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static void ValidateChippedRemainderBreak(GameObject root)
        {
            var treeRoot=new GameObject("Generic tree validation chipped remainder tree");
            treeRoot.transform.SetParent(root.transform);
            var tree=treeRoot.AddComponent<GeneratedTree>();tree.Configure(.5f,4);
            var trunk=GameObject.CreatePrimitive(PrimitiveType.Cube);
            trunk.name="Generic tree validation chipped remainder trunk";
            trunk.transform.SetParent(treeRoot.transform,false);
            trunk.transform.localScale=new Vector3(2f,4f,2f);
            var properties=PhysicalDestructible.Ensure(trunk);properties.ApplyMaterial("Wood");
            var structure=trunk.AddComponent<TreeStructuralPart>();structure.SetTrunk(true);structure.SetGroundAnchor();
            var fragments=new List<PhysicalDestructible>();
            properties.Fragmented+=(_,pieces)=>fragments.AddRange(pieces);
            try
            {
                Vector3 hit=new(1f,.2f,0f);
                Require(properties.ApplyImpact(12f,12f,.3f,hit,Vector3.right,30f,
                    otherSolidVolume:.027d),"A small hard impact creates a real chipped trunk remainder");
                Require(!properties.HasBeenDestroyed&&trunk.GetComponent<MeshFilter>().sharedMesh==null&&
                    trunk.transform.Find("Shattered remainder")!=null,
                    "Chipping moves the live trunk mesh to a child, leaving the original renderer empty");
                Require(structure.TryGetSolidBounds(out Bounds live)&&live.min.y<hit.y&&live.max.y>hit.y,
                    "Live bounds include the remaining child trunk geometry at the destruction height");
                for(int i=0;i<3;i++) tree.RecordDestructiveImpact(hit+Vector3.up*.01f*i);
                Require(tree.IsBroken&&tree.TrunkBreakProgress==0&&
                    tree.LastBreakReason.Contains("Tree split"),
                    "The required nearby destructions split the chipped trunk instead of claiming that it is absent");
            }
            finally
            {
                foreach(var fragment in fragments) if(fragment!=null) UnityEngine.Object.DestroyImmediate(fragment.gameObject);
                UnityEngine.Object.DestroyImmediate(treeRoot);
            }
        }

        [MenuItem("Tools/Wyvernland/Validate Generic Tree Destruction")]
        public static void Run()
        {
            var randomState=UnityEngine.Random.state;
            var root=new GameObject("Generic tree destruction validation");
            try
            {
                ValidateGeneratedParts(root);
                ValidateFallingConnections(root);
                ValidateDisconnectedTrunkSupport(root);
                ValidateBranchImpactDetachment(root);
                ValidateNearbyResinFalls(root);
                ValidateSmallTrunkImpacts(root);
                ValidateImpactCluster(root);
                ValidateImpactClusterAcrossTrunkGap(root);
                ValidateChippedTrunkCenter(root);
                ValidateUncuttableTrunkSupport(root);
                ValidateMidpointBreak(root);
                foreach(var sample in new[]{(size:1f,attacker:1f,hardness:6f,resilience:5f,speed:30f,ground:false),
                    (size:1f,attacker:0.2f,hardness:4.5f,resilience:5f,speed:30f,ground:false),
                    (size:1f,attacker:0.3f,hardness:10f,resilience:10f,speed:30f,ground:false),
                    (size:2f,attacker:1.01f,hardness:6f,resilience:5f,speed:30f,ground:false),
                    (size:1f,attacker:10f,hardness:0.5f,resilience:8f,speed:2.5f,ground:true)})
                    ValidateParity(root,sample.size,sample.attacker,sample.hardness,sample.resilience,sample.speed,sample.ground);
                Debug.Log("TREE_GENERIC_DESTRUCTION_VALIDATION_PASS: generated trunks and branches use normal Wood properties; one or two hits detach each branch as one object without damaging the trunk; nearby destruction drops resin; clustered trunk impacts split at the last destruction point; normal fragments and closed meshes; modified-tree streaming state.");
            }
            finally
            {
                UnityEngine.Random.state=randomState;
                foreach(var fragment in UnityEngine.Object.FindObjectsByType<PhysicalDestructible>(
                    FindObjectsInactive.Include,FindObjectsSortMode.None))
                    if(fragment!=null&&fragment.name.StartsWith("Generic tree validation ",StringComparison.Ordinal))
                        UnityEngine.Object.DestroyImmediate(fragment.gameObject);
                // Some support tests intentionally detach sections from the
                // validation root. Clean those, including leftovers from a
                // previous interrupted validation run.
                foreach(var part in UnityEngine.Object.FindObjectsByType<TreeStructuralPart>(
                    FindObjectsInactive.Include,FindObjectsSortMode.None))
                {
                    if(part==null) continue;
                    Vector3 position=part.transform.position;
                    bool supportTest=part.name.StartsWith("Support test ",StringComparison.Ordinal)&&
                        Mathf.Abs(position.x-400f)<1f;
                    if(supportTest)
                        UnityEngine.Object.DestroyImmediate(part.gameObject);
                }
                foreach(var leftover in UnityEngine.Object.FindObjectsByType<GameObject>(
                    FindObjectsInactive.Include,FindObjectsSortMode.None))
                    if(leftover!=null&&leftover.name=="Generic tree destruction validation")
                        UnityEngine.Object.DestroyImmediate(leftover);
            }
        }
        static void ValidateGeneratedParts(GameObject root)
        {
            var streaming=root.AddComponent<ProceduralTreeField>();
            var material=new Material(Shader.Find("Standard")??Shader.Find("Sprites/Default"));
            typeof(ProceduralTreeField).GetField("darkWoodMaterial",BindingFlags.Instance|BindingFlags.NonPublic)
                .SetValue(streaming,material);
            ((List<Material>)typeof(ProceduralTreeField).GetField("materials",BindingFlags.Instance|BindingFlags.NonPublic)
                .GetValue(streaming)).Add(material);
            Type seedType=typeof(ProceduralTreeField).GetNestedType("TreeSeed",BindingFlags.NonPublic);
            var trees=(IList)typeof(ProceduralTreeField).GetField("trees",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(streaming);
            var create=typeof(ProceduralTreeField).GetMethod("CreateTree",BindingFlags.Instance|BindingFlags.NonPublic);
            foreach(var variant in new[]{(seed:101,woodland:false,dark:false),
                (seed:105,woodland:false,dark:false),(seed:110,woodland:true,dark:false),
                (seed:111,woodland:true,dark:true)})
            {
                int seed=variant.seed;
                object tree=Activator.CreateInstance(seedType,true);
                seedType.GetField("seed").SetValue(tree,seed);
                seedType.GetField("woodland").SetValue(tree,variant.woodland);
                seedType.GetField("darkWood").SetValue(tree,variant.dark);
                seedType.GetField("position").SetValue(tree,new Vector3(seed*2f,0f,0f));
                seedType.GetField("terrainNormal").SetValue(tree,Vector3.up);
                trees.Add(tree);
                create.Invoke(streaming,new[]{tree});
                var instance=(GameObject)seedType.GetField("instance").GetValue(tree);
                var tracker=instance.GetComponent<GeneratedTree>();
                Require(tracker!=null&&tracker.AverageTrunkRadius>0f&&
                    tracker.ImpactsToBreak>=3&&tracker.ImpactsToBreak<=15,
                    "Each tree variant has an impact tracker sized to its own trunk");
                var parts=instance.GetComponentsInChildren<TreeStructuralPart>()
                    .Select(part=>part.GetComponent<PhysicalDestructible>()).ToArray();
                Require(parts.Length>0&&instance.GetComponent<PhysicalDestructible>()==null,
                    "Distant tree visuals remain separate from the actual solid objects");
                var growths=instance.GetComponentsInChildren<TreeResinGrowth>();
                float treeHeight=(float)seedType.GetField("height").GetValue(tree);
                int resinLimit=treeHeight<10f?1:treeHeight<24f?2:3;
                Require(growths.Length<=(variant.dark||!variant.woodland&&seed%7==0?
                    0:resinLimit),
                    "Only full normal trees carry up to three size-limited Black Resin growths");
                foreach(var growth in growths)
                {
                    var resin=growth.GetComponent<PhysicalDestructible>();
                    var resinMesh=growth.GetComponent<MeshFilter>()?.sharedMesh;
                    Require(growth.GetComponentInParent<TreeStructuralPart>()?.IsTrunk==true&&
                        growth.GetComponent<PickableObject>()?.DisplayName=="Black Resin"&&
                        resin?.MaterialName=="Black Resin"&&resin.IsSoft&&resin.MassKg>0f&&
                        resinMesh!=null&&DestructionMesh.IsClosedConnected(resinMesh),
                        "Black Resin grows on a trunk as a closed, soft, pickable solid");
                }
                Require(parts.All(part=>variant.dark?
                    part.MaterialName=="Burnt Wood"&&part.Hardness>=1.5f&&part.Hardness<=2.5f&&
                        part.Resilience>=3f&&part.Resilience<=7f:
                    part.MaterialName=="Wood"&&part.Hardness==2.5f&&part.Resilience==7f),
                    "Generated tree parts use the correct ordinary or burnt wood ranges");
                Require(parts.All(part=>part.GetComponent<TreeStructuralPart>()!=null),
                    "Every generated shell participates in tree support tracking");
                int trunkSections=(int)seedType.GetField("trunkSections").GetValue(tree);
                var trunkMeshes=(IList)seedType.GetField("trunkPartMeshes").GetValue(tree);
                var supportField=typeof(TreeStructuralPart).GetField("parent",
                    BindingFlags.Instance|BindingFlags.NonPublic);
                Require(trunkSections>=2&&trunkMeshes.Count>trunkSections,
                    "Generated trunk has multiple linked sections and separate root anchors");
                for(int i=0;i<trunkMeshes.Count;i++)
                {
                    var section=Array.Find(instance.GetComponentsInChildren<TreeStructuralPart>(),
                        part=>part.name==$"Wood trunk or root {i+1}");
                    Require(section!=null,"Every trunk and root shell has a structural part");
                    Require(section.IsTrunk==(i<trunkSections),
                        "Only actual trunk sections use the trunk impact limit");
                    var expected=i>0&&i<trunkSections?
                        Array.Find(instance.GetComponentsInChildren<TreeStructuralPart>(),
                            part=>part.name==$"Wood trunk or root {i}"):null;
                    Require((TreeStructuralPart)supportField.GetValue(section)==expected,
                        "Upper trunk sections follow their lower section; roots remain anchored");
                    if(expected!=null)
                        Require(section.transform.parent==expected.transform,
                            "Each upper trunk is a Transform child of its supporting section");
                }
                var branchParents=(IList)seedType.GetField("branchParents").GetValue(tree);
                Require(instance.GetComponentsInChildren<TreeStructuralPart>()
                    .Where(part=>part.name.StartsWith("Wood branch ",StringComparison.Ordinal))
                    .All(part=>part.IsBranch&&part.BranchHitsToDetach>=1&&
                        part.BranchHitsToDetach<=2),
                    "Every generated branch has its own one or two hit attachment threshold");
                for(int i=0;i<branchParents.Count;i++)
                {
                    var branchPart=instance.transform.Find($"Wood branch {i+1}")?
                        .GetComponent<TreeStructuralPart>();
                    // Nested branches are no longer direct children of the tree root.
                    if(branchPart==null)
                        branchPart=Array.Find(instance.GetComponentsInChildren<TreeStructuralPart>(),
                            part=>part.name==$"Wood branch {i+1}");
                    int index=(int)branchParents[i];
                    if(index<0) continue;
                    var parentBranch=Array.Find(instance.GetComponentsInChildren<TreeStructuralPart>(),
                        part=>part.name==$"Wood branch {index+1}");
                    Require(branchPart!=null&&parentBranch!=null&&
                        branchPart.transform.parent==parentBranch.transform,
                        "A child branch follows the branch from which it grows");
                }
                foreach(var part in parts)
                {
                    var mesh=part.GetComponent<MeshFilter>().sharedMesh;
                    Require(DestructionMesh.IsClosedConnected(mesh)&&part.GetComponent<MeshCollider>().sharedMesh==mesh,
                        "Generated geometry retains complete meshes and matching colliders");
                    Require(PhysicalDestructible.Resolve(part.GetComponent<MeshCollider>())==part,
                        "Tree parts resolve through the normal object collision path");
                }
                var branch=parts.Where(part=>part.name.StartsWith("Wood branch ",StringComparison.Ordinal))
                    .OrderBy(part=>part.GetComponent<PickableObject>()?.PickupMassKg??float.PositiveInfinity)
                    .FirstOrDefault(part=>part.GetComponent<PickableObject>() is { } item&&
                        (item.CanPickUpWithOneHand||item.CanPickUpWithTwoHands));
                if(branch!=null)
                {
                    var hand=new GameObject("Generic pickup hand").transform;
                    hand.SetParent(root.transform);
                    hand.position=branch.transform.position;
                    var pickup=branch.GetComponent<PickableObject>();
                    pickup.PickUp(hand,pickup.RequiresTwoHands);
                    Require(pickup.IsHeld&&branch.GetComponent<Rigidbody>().isKinematic&&
                        (bool)seedType.GetField("physicallyModified").GetValue(tree),
                        "Normal pickup marks streamed geometry modified and holds the individual object");
                    pickup.Release(Vector3.zero,hand);
                    Require(!branch.GetComponent<Rigidbody>().isKinematic,
                        "An ordinary dropped branch has a dynamic body");
                    UnityEngine.Object.DestroyImmediate(branch.gameObject);
                }
                if(seed==101)
                {
                    Require(growths.Length>0,"The ordinary test tree carries a visible Black Resin sample");
                    var hand=new GameObject("Black Resin pickup hand").transform;
                    hand.SetParent(root.transform);
                    hand.position=growths[0].transform.position;
                    var resinPickup=growths[0].GetComponent<PickableObject>();
                    Require(resinPickup.PickUp(hand)&&resinPickup.IsHeld&&
                        !resinPickup.transform.IsChildOf(instance.transform),
                        "Picked Black Resin remains independent of its resident source tree");
                    resinPickup.Release(Vector3.zero,hand);
                    UnityEngine.Object.DestroyImmediate(resinPickup.gameObject);
                }
                if(seed==101||seed==110)
                {
                    var cutSection=instance.GetComponentsInChildren<TreeStructuralPart>()
                        .First(part=>part.IsTrunk&&part.CanBreakTrunkSupport);
                    var originalStructures=instance.GetComponentsInChildren<TreeStructuralPart>();
                    string cutSectionName=cutSection.name;
                    var cutProperties=cutSection.GetComponent<PhysicalDestructible>();
                    Vector3 cutPoint=cutSection.GetComponent<Renderer>().bounds.center;
                    var prior=new HashSet<PhysicalDestructible>(
                        UnityEngine.Object.FindObjectsByType<PhysicalDestructible>(
                            FindObjectsSortMode.None));
                    for(int hit=0;hit<tracker.ImpactsToBreak;hit++)
                        tracker.RecordDestructiveImpact(cutPoint);
                    Require(tracker.IsBroken,
                        "A generated trunk breaks after its configured number of real hits");
                    if(seed==110)
                    {
                        var cutFragments=UnityEngine.Object.FindObjectsByType<PhysicalDestructible>(
                            FindObjectsSortMode.None).Where(piece=>piece!=null&&
                                !prior.Contains(piece)&&piece.name.StartsWith(cutSectionName+
                                    " fragment",StringComparison.Ordinal)).ToArray();
                        Require((cutSection==null||cutProperties.HasBeenDestroyed)&&
                            cutFragments.Length>=2&&
                            Mathf.Abs(instance.transform.Find("Tree destruction center point")
                                .position.y-cutPoint.y)<0.001f,
                            "A large generated trunk splits through its last destruction point instead of detaching at a section joint");
                    }
                    foreach(var piece in UnityEngine.Object.FindObjectsByType<PhysicalDestructible>(
                        FindObjectsSortMode.None))
                        if(piece!=null&&!prior.Contains(piece)&&
                            piece.name.Contains("Wood trunk or root"))
                            UnityEngine.Object.DestroyImmediate(piece.gameObject);
                    foreach(var part in originalStructures)
                        if(part!=null&&!part.transform.IsChildOf(root.transform))
                            UnityEngine.Object.DestroyImmediate(part.gameObject);
                }
            }
            streaming.RequestBreakAllTreesAtMidpoint();
            foreach(object tree in trees)
                Require((bool)seedType.GetField("breakAtMidpoint").GetValue(tree),
                    "The global midpoint action marks every generated tree variant");
        }
        static void ValidateFallingConnections(GameObject root)
        {
            GameObject Section(string name,float y,bool structural=true)
            {
                var section=GameObject.CreatePrimitive(PrimitiveType.Cube);
                section.name=name;
                section.transform.SetParent(root.transform);
                section.transform.position=new Vector3(400f,y,0f);
                PhysicalDestructible.Ensure(section).ApplyMaterial("Wood");
                if(structural) section.AddComponent<TreeStructuralPart>();
                return section;
            }
            var basePart=Section("Support test base",0f);
            var middle=Section("Support test middle",1f);
            var tip=Section("Support test tip",2f);
            var baseStructure=basePart.GetComponent<TreeStructuralPart>();
            var middleStructure=middle.GetComponent<TreeStructuralPart>();
            var tipStructure=tip.GetComponent<TreeStructuralPart>();
            middleStructure.AttachTo(baseStructure);
            tipStructure.AttachTo(middleStructure);
            var firstFragment=Section("Support test fallen base",0f,false);
            firstFragment.AddComponent<Rigidbody>();
            basePart.SetActive(false);
            var fragmented=typeof(TreeStructuralPart).GetMethod("OnFragmented",
                BindingFlags.Instance|BindingFlags.NonPublic);
            fragmented.Invoke(baseStructure,new object[]{basePart.GetComponent<PhysicalDestructible>(),
                new PhysicalDestructible[]{firstFragment.GetComponent<PhysicalDestructible>()}});
            Require(middle.transform.parent==firstFragment.transform&&
                tip.transform.parent==middle.transform&&
                middle.GetComponent<Rigidbody>()==null&&tip.GetComponent<Rigidbody>()==null,
                "Detached trunk carries its connected child sections as one body");
            var secondFragment=Section("Support test fallen middle",1f,false);
            secondFragment.AddComponent<Rigidbody>();
            middle.SetActive(false);
            fragmented.Invoke(middleStructure,new object[]{middle.GetComponent<PhysicalDestructible>(),
                new PhysicalDestructible[]{secondFragment.GetComponent<PhysicalDestructible>()}});
            Require(tip.transform.parent==secondFragment.transform&&
                tip.GetComponent<Rigidbody>()==null,
                "A later cut transfers the upper section to the newly fallen support");
        }
        static void ValidateDisconnectedTrunkSupport(GameObject root)
        {
            TreeStructuralPart Section(string name,float height)
            {
                var section=GameObject.CreatePrimitive(PrimitiveType.Cube);
                section.name="Generic tree validation "+name;
                section.transform.SetParent(root.transform);
                section.transform.position=new Vector3(600f,height,0f);
                PhysicalDestructible.Ensure(section).ApplyMaterial("Wood");
                var structure=section.AddComponent<TreeStructuralPart>();
                structure.SetTrunk(true);
                return structure;
            }
            var bottom=Section("support audit bottom",0f);
            bottom.SetGroundAnchor();
            var middle=Section("support audit middle",1f);
            var top=Section("support audit top",2f);
            var branch=Section("support audit attached object",3f);
            branch.SetTrunk(false);
            branch.ConfigureBranch(1);
            middle.AttachTo(bottom);
            top.AttachTo(middle);
            branch.AttachTo(top);
            middle.GetComponent<BoxCollider>().center=Vector3.up*0.4f;
            middle.ValidateTrunkSupport();
            Require(middle.transform.parent==null&&
                middle.GetComponent<Rigidbody>() is { isKinematic: false }&&
                top.transform.parent==middle.transform&&branch.transform.parent==top.transform&&
                top.GetComponent<Rigidbody>()==null&&branch.GetComponent<Rigidbody>()==null,
                "A trunk with no physical link to its lower section falls with every attached object");

            bottom.GetComponent<BoxCollider>().center=Vector3.up*0.4f;
            bottom.ValidateTrunkSupport();
            Require(bottom.GetComponent<Rigidbody>() is { isKinematic: false },
                "A base section that no longer touches its original ground anchor falls");
        }

        static void ValidateBranchImpactDetachment(GameObject root)
        {
            var treeRoot=new GameObject("Generic tree validation branch hits");
            treeRoot.transform.SetParent(root.transform);
            var tracker=treeRoot.AddComponent<GeneratedTree>();
            tracker.Configure(0.5f,4);
            var trunk=GameObject.CreatePrimitive(PrimitiveType.Cube);
            trunk.name="Generic tree validation branch support trunk";
            trunk.transform.SetParent(treeRoot.transform,false);
            var trunkProperties=PhysicalDestructible.Ensure(trunk);
            trunkProperties.ApplyMaterial("Wood");
            var trunkPart=trunk.AddComponent<TreeStructuralPart>();
            trunkPart.SetTrunk(true);
            foreach(int threshold in new[]{1,2})
            {
                var branch=GameObject.CreatePrimitive(PrimitiveType.Cube);
                branch.name="Generic tree validation individual branch";
                branch.transform.SetParent(treeRoot.transform,false);
                branch.transform.localPosition=new Vector3(threshold*2f,1f,0f);
                var properties=PhysicalDestructible.Ensure(branch);
                properties.ApplyMaterial("Wood");
                branch.AddComponent<PickableObject>();
                var structure=branch.AddComponent<TreeStructuralPart>();
                structure.ConfigureBranch(threshold);
                structure.AttachTo(trunkPart);
                GameObject childBranch=null;
                if(threshold==1)
                {
                    childBranch=GameObject.CreatePrimitive(PrimitiveType.Cube);
                    childBranch.name="Generic tree validation child branch";
                    childBranch.transform.SetParent(treeRoot.transform,false);
                    childBranch.transform.localPosition=branch.transform.position+Vector3.up;
                    var child=childBranch.AddComponent<TreeStructuralPart>();
                    child.ConfigureBranch(2);
                    child.AttachTo(structure);
                }
                Mesh originalMesh=branch.GetComponent<MeshFilter>().sharedMesh;
                for(int hit=1;hit<=threshold;hit++)
                {
                    Require(properties.ApplyImpact(6f,5f,2f,branch.transform.position,
                        Vector3.up,30f,"Stone",1d),
                        "A qualifying impact damages only its own branch attachment");
                    Require(structure.BranchDestructionHits==hit&&
                        branch.GetComponent<MeshFilter>().sharedMesh==originalMesh&&
                        !trunkProperties.HasBeenModified,
                        "Branch hits retain one intact branch object and leave the trunk undamaged");
                    if(hit<threshold)
                    {
                        Require(branch.transform.parent==trunk.transform&&
                            branch.GetComponent<Rigidbody>()==null,
                            "The branch remains attached until its own threshold");
                        typeof(PhysicalDestructible).GetField("nextImpact",
                            BindingFlags.Instance|BindingFlags.NonPublic).SetValue(properties,0f);
                    }
                }
                Require(branch.transform.parent==null&&
                    branch.GetComponent<Rigidbody>() is { isKinematic: false }&&
                    branch.GetComponent<PickableObject>()!=null,
                    "A one or two hit branch falls as one dynamic pickable object");
                if(childBranch!=null)
                {
                    Require(childBranch.transform.parent==null&&
                        childBranch.GetComponent<Rigidbody>() is { isKinematic: false },
                        "Unsupported child branches fall as separate bodies");
                    UnityEngine.Object.DestroyImmediate(childBranch);
                }
                UnityEngine.Object.DestroyImmediate(branch);
            }
            Require(tracker.DestructiveImpacts.Count==3&&
                treeRoot.GetComponentsInChildren<MeshRenderer>().Count(renderer=>
                    renderer.name.StartsWith("Tree destruction collision point",
                        StringComparison.Ordinal))==3,
                "Branch destruction points are marked without breaking the trunk");
            UnityEngine.Object.DestroyImmediate(treeRoot);
        }

        static void ValidateNearbyResinFalls(GameObject root)
        {
            var treeRoot=new GameObject("Generic tree validation resin release");
            treeRoot.transform.SetParent(root.transform);
            var tracker=treeRoot.AddComponent<GeneratedTree>();
            bool modified=false;
            TreeResinGrowth Growth(string name,float x)
            {
                GameObject resin=GameObject.CreatePrimitive(PrimitiveType.Cube);
                resin.name=name;
                resin.transform.SetParent(treeRoot.transform,false);
                resin.transform.localPosition=Vector3.right*x;
                resin.transform.localScale=Vector3.one*0.2f;
                resin.AddComponent<PhysicalDestructible>().ApplyMaterial("Black Resin");
                resin.AddComponent<PickableObject>().DisplayName="Black Resin";
                var growth=resin.AddComponent<TreeResinGrowth>();
                growth.Initialize(treeRoot.transform,()=>modified=true);
                return growth;
            }
            TreeResinGrowth near=Growth("Generic tree validation near resin",0f);
            TreeResinGrowth far=Growth("Generic tree validation far resin",3f);
            Physics.SyncTransforms();
            tracker.RecordDestructiveImpact(new Vector3(1.15f,0f,0f),false);
            Require(near.transform.IsChildOf(treeRoot.transform)&&
                far.transform.IsChildOf(treeRoot.transform)&&!modified,
                "Destruction beyond one metre leaves attached resin in place");
            tracker.RecordDestructiveImpact(new Vector3(0.95f,0f,0f),false);
            Require(near.transform.parent==null&&
                near.GetComponent<Rigidbody>() is { isKinematic: false, useGravity: true }&&
                near.GetComponent<PickableObject>()!=null&&modified&&
                far.transform.IsChildOf(treeRoot.transform),
                "Destruction within one metre drops only nearby resin as a dynamic pickable object");
            UnityEngine.Object.DestroyImmediate(near.gameObject);
            UnityEngine.Object.DestroyImmediate(treeRoot);
        }
        static void ValidateSmallTrunkImpacts(GameObject root)
        {
            GameObject Trunk(string name,float size)
            {
                var trunk=GameObject.CreatePrimitive(PrimitiveType.Cube);
                trunk.name="Generic tree validation "+name;
                trunk.transform.SetParent(root.transform);
                trunk.transform.position=new Vector3(500f,0f,0f);
                trunk.transform.localScale=Vector3.one*size;
                PhysicalDestructible.Ensure(trunk).ApplyMaterial("Wood");
                trunk.AddComponent<TreeStructuralPart>().SetTrunk(true);
                return trunk;
            }
            var blocked=Trunk("small fracture blocked",2f);
            var blockedProperties=blocked.GetComponent<PhysicalDestructible>();
            Require(!blockedProperties.ApplyImpact(4.5f,5f,2f,blocked.transform.position+Vector3.up,
                Vector3.up,30f,"Stone",0.059d)&&!blockedProperties.HasBeenModified,
                "A sub-0.06 cubic metre impactor cannot fracture a trunk");
            var chipped=Trunk("small partial shatter",2f);
            var chipProperties=chipped.GetComponent<PhysicalDestructible>();
            Require(chipProperties.ApplyImpact(6f,5f,2f,chipped.transform.position+Vector3.up,
                Vector3.up,30f,"Stone",0.059d)&&
                chipProperties.LastEffect==PhysicalDestructEffect.Shatter&&
                chipProperties.HasBeenModified&&!chipProperties.HasBeenDestroyed,
                "A qualifying small impactor can only chip a trunk with partial Shatter");
            var boundary=Trunk("volume boundary fracture",2f);
            var boundaryProperties=boundary.GetComponent<PhysicalDestructible>();
            Require(boundaryProperties.ApplyImpact(6f,5f,2f,boundary.transform.position+Vector3.up,
                Vector3.up,30f,"Stone",0.06d)&&
                boundaryProperties.LastEffect==PhysicalDestructEffect.Fracture,
                "An impactor at 0.06 cubic metres retains normal fracture behavior");
            foreach(var piece in Fragments(blocked.name).Concat(Fragments(chipped.name))
                .Concat(Fragments(boundary.name)))
                UnityEngine.Object.DestroyImmediate(piece.gameObject);
        }
        static void ValidateImpactCluster(GameObject root)
        {
            var sampleHits=new[]{new Vector3(0f,0.8f,0f),
                new Vector3(0.1f,0.85f,0f),new Vector3(-0.1f,0.75f,0f),
                new Vector3(5f,-1f,0f),new Vector3(5.1f,-1f,0f)};
            Vector3 densest=GeneratedTree.DensestImpactCenter(sampleHits,0.25f,
                out int nearbyCount);
            Require(nearbyCount==3&&Mathf.Abs(densest.y-0.8f)<0.001f&&
                Mathf.Abs(densest.x)<0.001f,
                "The cut center comes from the densest nearby impact group");
            var treeRoot=new GameObject("Generic tree validation clustered tree");
            treeRoot.transform.SetParent(root.transform);
            var tracker=treeRoot.AddComponent<GeneratedTree>();
            tracker.Configure(0.5f,4);
            var trunk=GameObject.CreatePrimitive(PrimitiveType.Cube);
            trunk.name="Generic tree validation clustered trunk";
            string trunkName=trunk.name;
            trunk.transform.SetParent(treeRoot.transform,false);
            trunk.transform.localScale=new Vector3(2f,4f,2f);
            var destructible=PhysicalDestructible.Ensure(trunk);
            destructible.ApplyMaterial("Wood");
            trunk.AddComponent<TreeStructuralPart>().SetTrunk(true);
            tracker.ShowBreakingPointAtMidpoint();
            Transform halfPoint=treeRoot.transform.Find("Tree midpoint break point");
            Require(halfPoint!=null&&Mathf.Abs(halfPoint.position.y)<0.001f&&
                treeRoot.GetComponentsInChildren<TextMesh>().Any(label=>
                    label.text=="Breaking Point"),
                "The breaking point marks the trunk's halfway height before any damage");
            tracker.RecordDestructiveImpact(new Vector3(1f,0.65f,0f));
            tracker.RecordDestructiveImpact(new Vector3(1.1f,0.6f,0f));
            tracker.RecordDestructiveImpact(new Vector3(1f,0.55f,0.1f));
            tracker.RecordDestructiveImpact(new Vector3(-3f,0.6f,0f));
            Require(!destructible.HasBeenDestroyed&&tracker.DestructiveImpacts.Count==4&&
                Mathf.Approximately(tracker.TrunkBreakRadius,2.5f)&&
                tracker.TrunkBreakProgress==3,
                "Five trunk radii define the range and a distant hit leaves three qualifying hits");
            var markers=treeRoot.GetComponentsInChildren<MeshRenderer>()
                .Where(renderer=>renderer.name.StartsWith("Tree destruction collision point",
                    StringComparison.Ordinal)).ToArray();
            Require(markers.Length==4&&markers.All(marker=>
                marker.GetComponent<Collider>()==null&&
                Mathf.Abs(marker.transform.lossyScale.x-0.08f)<0.001f&&
                (marker.sharedMaterial.HasProperty("_BaseColor")?
                    marker.sharedMaterial.GetColor("_BaseColor"):
                    marker.sharedMaterial.GetColor("_Color")).g>0.9f),
                "Every recorded tree impact has a small non-colliding bright green ball");
            // This hit is within the five-radius range (2.5 m).
            Vector3 finalHit=new(-0.6f,0.5f,-0.1f);
            tracker.RecordDestructiveImpact(finalHit);
            var pieces=Fragments(trunkName);
            Require(tracker.IsBroken&&pieces.Length==2&&
                tracker.DestructiveImpacts.Count==0,
                "Four nearby destructive hits break the trunk into two pieces");
            Require(treeRoot.GetComponentsInChildren<MeshRenderer>().Count(renderer=>
                renderer.name.StartsWith("Tree destruction collision point",
                    StringComparison.Ordinal))==5,
                "Impact markers remain visible after the tree splits");
            Transform centerMarker=treeRoot.transform.Find("Tree destruction center point");
            Require(centerMarker!=null&&
                Vector3.Distance(centerMarker.position,finalHit)<0.001f&&
                Mathf.Abs(centerMarker.position.y-halfPoint.position.y)>=0.5f&&
                centerMarker.GetComponentInChildren<TextMesh>() is { text: "BREAK POINT" }&&
                centerMarker.GetComponentsInChildren<Collider>().Length==0,
                "The split labels the last destruction point without adding collision geometry");
            Require(pieces.Any(piece=>Mathf.Abs(piece.GetComponent<Collider>().bounds.min.y-finalHit.y)<0.02f)&&
                pieces.Any(piece=>Mathf.Abs(piece.GetComponent<Collider>().bounds.max.y-finalHit.y)<0.02f),
                "The two halves meet at the last destruction point, above the tree midpoint");
            foreach(var piece in pieces)
            {
                Require(DestructionMesh.IsClosedConnected(piece.GetComponent<MeshFilter>().sharedMesh),
                "The final-hit tree cut produces closed solid halves");
                UnityEngine.Object.DestroyImmediate(piece.gameObject);
            }
            UnityEngine.Object.DestroyImmediate(treeRoot);
        }
        static void ValidateImpactClusterAcrossTrunkGap(GameObject root)
        {
            var treeRoot=new GameObject("Generic tree validation gapped tree");
            treeRoot.transform.SetParent(root.transform);
            var tracker=treeRoot.AddComponent<GeneratedTree>();
            tracker.Configure(0.5f,4);
            TreeStructuralPart Section(string name,float height)
            {
                var trunk=GameObject.CreatePrimitive(PrimitiveType.Cube);
                trunk.name=name;
                trunk.transform.SetParent(treeRoot.transform,false);
                trunk.transform.localPosition=Vector3.up*height;
                PhysicalDestructible.Ensure(trunk).ApplyMaterial("Wood");
                var structure=trunk.AddComponent<TreeStructuralPart>();
                structure.SetTrunk(true);
                return structure;
            }
            var lower=Section("Generic tree validation gapped lower trunk",0f);
            var upper=Section("Generic tree validation gapped upper trunk",2f);
            upper.AttachTo(lower);
            for(int i=0;i<4;i++)
                tracker.RecordDestructiveImpact(new Vector3(0f,1f+i*0.01f,0f));
            Require(!tracker.IsBroken&&upper.transform.parent==lower.transform&&
                treeRoot.transform.Find("Tree destruction center point")==null,
                "A final destruction point in an empty trunk gap cannot break at a different joint");
            UnityEngine.Object.DestroyImmediate(upper.gameObject);
            UnityEngine.Object.DestroyImmediate(treeRoot);
        }
        static void ValidateChippedTrunkCenter(GameObject root)
        {
            var treeRoot=new GameObject("Generic tree validation chipped center");
            treeRoot.transform.SetParent(root.transform);
            var tracker=treeRoot.AddComponent<GeneratedTree>();
            tracker.Configure(0.5f,4);
            TreeStructuralPart Section(string name,float height)
            {
                var trunk=GameObject.CreatePrimitive(PrimitiveType.Cube);
                trunk.name=name;
                trunk.transform.SetParent(treeRoot.transform,false);
                trunk.transform.localPosition=Vector3.up*height;
                PhysicalDestructible.Ensure(trunk).ApplyMaterial("Wood");
                var structure=trunk.AddComponent<TreeStructuralPart>();
                structure.SetTrunk(true);
                return structure;
            }
            var lower=Section("Generic tree validation chipped lower",0f);
            var upper=Section("Generic tree validation chipped upper",1.2f);
            upper.AttachTo(lower);
            // Hits span two trunk shells; the last hit lies on their support
            // joint, where no solid mesh remains to cut.
            tracker.RecordDestructiveImpact(new Vector3(0f,0.47f,0f));
            tracker.RecordDestructiveImpact(new Vector3(0f,0.48f,0f));
            tracker.RecordDestructiveImpact(new Vector3(0f,0.72f,0f));
            tracker.RecordDestructiveImpact(new Vector3(0f,0.7f,0f));
            Require(tracker.IsBroken&&upper.transform.parent==null&&
                upper.GetComponent<Rigidbody>() is { isKinematic: false },
                "Enough real trunk hits sever support even when the center is chipped away");
            UnityEngine.Object.DestroyImmediate(upper.gameObject);
            UnityEngine.Object.DestroyImmediate(treeRoot);
        }

        static void ValidateUncuttableTrunkSupport(GameObject root)
        {
            var treeRoot=new GameObject("Generic tree validation uncuttable tree");
            treeRoot.transform.SetParent(root.transform);
            var tracker=treeRoot.AddComponent<GeneratedTree>();
            tracker.Configure(0.1f,4);
            TreeStructuralPart Section(string name,float height)
            {
                var trunk=GameObject.CreatePrimitive(PrimitiveType.Cube);
                trunk.name=name;
                trunk.transform.SetParent(treeRoot.transform,false);
                trunk.transform.localPosition=Vector3.up*height;
                trunk.transform.localScale=Vector3.one*0.1f;
                PhysicalDestructible.Ensure(trunk).ApplyMaterial("Wood");
                var structure=trunk.AddComponent<TreeStructuralPart>();
                structure.SetTrunk(true);
                return structure;
            }
            var lower=Section("Generic tree validation uncuttable lower",0f);
            var upper=Section("Generic tree validation uncuttable upper",0.1f);
            upper.AttachTo(lower);
            for(int i=0;i<3;i++)
                tracker.RecordDestructiveImpact(new Vector3(0f,i*0.001f,0f));
            tracker.RecordDestructiveImpact(new Vector3(0f,0.05f,0f));
            Require(tracker.IsBroken&&upper.transform.parent==null&&
                upper.GetComponent<Rigidbody>() is { isKinematic: false },
                "An unsplittable trunk section releases its nearby upper support");
            UnityEngine.Object.DestroyImmediate(upper.gameObject);
            UnityEngine.Object.DestroyImmediate(treeRoot);
        }

        static void ValidateMidpointBreak(GameObject root)
        {
            var treeRoot=new GameObject("Generic tree validation midpoint tree");
            treeRoot.transform.SetParent(root.transform);
            var tree=treeRoot.AddComponent<GeneratedTree>();
            TreeStructuralPart Section(string name,float height)
            {
                var objectPart=GameObject.CreatePrimitive(PrimitiveType.Cube);
                objectPart.name=name;
                objectPart.transform.SetParent(treeRoot.transform,false);
                objectPart.transform.localPosition=Vector3.up*height;
                PhysicalDestructible.Ensure(objectPart).ApplyMaterial("Wood");
                var structure=objectPart.AddComponent<TreeStructuralPart>();
                structure.SetTrunk(true);
                return structure;
            }
            var lower=Section("Generic tree validation lower trunk",0f);
            var upper=Section("Generic tree validation upper trunk",1f);
            upper.AttachTo(lower);
            Require(tree.BreakAtMidpoint()&&tree.IsBroken&&
                upper.transform.parent==null&&
                upper.GetComponent<Rigidbody>() is { isKinematic: false }&&
                !lower.GetComponent<PhysicalDestructible>().HasBeenModified,
                "A midpoint on a trunk seam releases the upper half without damaging the base");
            Transform marker=treeRoot.transform.Find("Tree midpoint break point");
            Transform markerDot=marker?.Find("Tree midpoint break dot");
            MeshRenderer markerRenderer=markerDot?.GetComponent<MeshRenderer>();
            Require(marker!=null&&Mathf.Abs(marker.position.y-0.5f)<0.001f&&
                markerDot!=null&&markerDot.GetComponent<Collider>()==null&&
                markerDot.GetComponent<TreeImpactMarker>()!=null&&
                markerRenderer!=null&&
                (markerRenderer.sharedMaterial.HasProperty("_BaseColor")?
                    markerRenderer.sharedMaterial.GetColor("_BaseColor"):
                    markerRenderer.sharedMaterial.GetColor("_Color")).b>0.9f,
                "The midpoint break is labeled by a non-colliding blue ball");
            UnityEngine.Object.DestroyImmediate(upper.gameObject);
            UnityEngine.Object.DestroyImmediate(treeRoot);
        }
        static void ValidateParity(GameObject root,float size,float attacker,float hardness,float resilience,float speed,bool ground)
        {
            var treeRoot=new GameObject("Generic tree validation hierarchy");
            treeRoot.transform.SetParent(root.transform);
            treeRoot.AddComponent<GeneratedTree>();
            GameObject Cube(string name,Transform parent)
            {
                var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name=name; cube.transform.SetParent(parent,false); cube.transform.localScale=Vector3.one*size;
                PhysicalDestructible.Ensure(cube).ApplyMaterial("Wood");
                return cube;
            }
            var treeObject=Cube("Generic tree validation Wood trunk",treeRoot.transform);
            var ordinaryObject=Cube("Generic tree validation Wood ordinary",root.transform);
            var treeProperties=treeObject.GetComponent<PhysicalDestructible>();
            var ordinaryProperties=ordinaryObject.GetComponent<PhysicalDestructible>();
            UnityEngine.Random.InitState(20260928);
            bool treeResult=treeProperties.ApplyImpact(hardness,resilience,attacker,Vector3.up*size*0.5f,
                Vector3.up,speed,"Stone",0.001d,ground,100f);
            UnityEngine.Random.InitState(20260928);
            bool ordinaryResult=ordinaryProperties.ApplyImpact(hardness,resilience,attacker,Vector3.up*size*0.5f,
                Vector3.up,speed,"Stone",0.001d,ground,100f);
            Require(treeResult==ordinaryResult,"Tree ancestry cannot change whether an impact destroys an object");
            var treePieces=Fragments("Generic tree validation Wood trunk");
            var ordinaryPieces=Fragments("Generic tree validation Wood ordinary");
            Require(treePieces.Length==ordinaryPieces.Length,"Trees use the normal fragment count and budget");
            if(treeObject!=null&&ordinaryObject!=null)
                Require(treeProperties.LastEffect==ordinaryProperties.LastEffect&&
                    treeProperties.HasBeenModified==ordinaryProperties.HasBeenModified&&
                    Math.Abs(treeProperties.SolidVolumeCubicMetres-ordinaryProperties.SolidVolumeCubicMetres)<0.0001d,
                    "Size limits, retained volume and effect classification match ordinary Wood objects");
            var treeVolumes=treePieces.Select(piece=>piece.SolidVolumeCubicMetres).OrderBy(volume=>volume).ToArray();
            var ordinaryVolumes=ordinaryPieces.Select(piece=>piece.SolidVolumeCubicMetres).OrderBy(volume=>volume).ToArray();
            for(int i=0;i<treeVolumes.Length;i++) Require(Math.Abs(treeVolumes[i]-ordinaryVolumes[i])<0.0001d,
                "Tree fragments have the same dimensions and material volume as normal fragments");
            foreach(var piece in treePieces.Concat(ordinaryPieces))
            {
                Require(piece.GetComponent<GeneratedTree>()==null&&
                    DestructionMesh.IsClosedConnected(piece.GetComponent<MeshFilter>().sharedMesh),
                    "All emitted pieces are ordinary complete fragments");
                UnityEngine.Object.DestroyImmediate(piece.gameObject);
            }
            Require(!UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)
                .Any(body=>body.name=="Wood upper trunk with branches"),"No tree-specific upper assembly is created");
            if(ordinaryObject!=null) UnityEngine.Object.DestroyImmediate(ordinaryObject);
            UnityEngine.Object.DestroyImmediate(treeRoot);
        }
        static PhysicalDestructible[] Fragments(string name) =>
            UnityEngine.Object.FindObjectsByType<PhysicalDestructible>(FindObjectsSortMode.None)
                .Where(part=>part.name==name+" fragment").ToArray();
        static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
    }
}
