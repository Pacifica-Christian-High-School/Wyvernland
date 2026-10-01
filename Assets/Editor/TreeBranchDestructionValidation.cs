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
                ValidateSmallTrunkImpacts(root);
                ValidateImpactCluster(root);
                ValidateImpactClusterAcrossTrunkGap(root);
                ValidateMidpointBreak(root);
                foreach(var sample in new[]{(size:1f,attacker:1f,hardness:6f,resilience:5f,speed:30f,ground:false),
                    (size:1f,attacker:0.2f,hardness:4.5f,resilience:5f,speed:30f,ground:false),
                    (size:1f,attacker:0.3f,hardness:10f,resilience:10f,speed:30f,ground:false),
                    (size:2f,attacker:1.01f,hardness:6f,resilience:5f,speed:30f,ground:false),
                    (size:1f,attacker:10f,hardness:0.5f,resilience:8f,speed:2.5f,ground:true)})
                    ValidateParity(root,sample.size,sample.attacker,sample.hardness,sample.resilience,sample.speed,sample.ground);
                Debug.Log("TREE_GENERIC_DESTRUCTION_VALIDATION_PASS: generated trunks and branches use normal Wood properties; one or two hits detach each branch as one object without damaging the trunk; clustered trunk impacts split at their average position; normal fragments and closed meshes; modified-tree streaming state.");
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
            var streaming=root.AddComponent<WorldObjectStreaming>();
            var material=new Material(Shader.Find("Standard")??Shader.Find("Sprites/Default"));
            typeof(WorldObjectStreaming).GetField("darkWoodMaterial",BindingFlags.Instance|BindingFlags.NonPublic)
                .SetValue(streaming,material);
            ((List<Material>)typeof(WorldObjectStreaming).GetField("materials",BindingFlags.Instance|BindingFlags.NonPublic)
                .GetValue(streaming)).Add(material);
            Type seedType=typeof(WorldObjectStreaming).GetNestedType("TreeSeed",BindingFlags.NonPublic);
            var trees=(IList)typeof(WorldObjectStreaming).GetField("trees",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(streaming);
            var create=typeof(WorldObjectStreaming).GetMethod("CreateTree",BindingFlags.Instance|BindingFlags.NonPublic);
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
                    tracker.ImpactsToBreak>=4&&tracker.ImpactsToBreak<=10,
                    "Each tree variant has an impact tracker sized to its own trunk");
                var parts=instance.GetComponentsInChildren<PhysicalDestructible>();
                Require(parts.Length>0&&instance.GetComponent<PhysicalDestructible>()==null,
                    "Distant tree visuals remain separate from the actual solid objects");
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
                var branch=parts.FirstOrDefault(part=>part.name.StartsWith("Wood branch ",StringComparison.Ordinal));
                if(branch!=null)
                {
                    var hand=new GameObject("Generic pickup hand").transform;
                    hand.SetParent(root.transform);
                    hand.position=branch.transform.position;
                    var pickup=branch.GetComponent<PickableObject>();
                    pickup.PickUp(hand);
                    Require(pickup.IsHeld&&branch.GetComponent<Rigidbody>().isKinematic&&
                        (bool)seedType.GetField("physicallyModified").GetValue(tree),
                        "Normal pickup marks streamed geometry modified and holds the individual object");
                    pickup.Release(Vector3.zero,hand);
                    Require(!branch.GetComponent<Rigidbody>().isKinematic,
                        "An ordinary dropped branch has a dynamic body");
                    UnityEngine.Object.DestroyImmediate(branch.gameObject);
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
            tracker.RecordDestructiveImpact(new Vector3(1f,0f,0f));
            tracker.RecordDestructiveImpact(new Vector3(1.1f,0.05f,0f));
            tracker.RecordDestructiveImpact(new Vector3(1f,-0.05f,0.1f));
            tracker.RecordDestructiveImpact(new Vector3(-3f,0f,0f));
            Require(!destructible.HasBeenDestroyed&&tracker.DestructiveImpacts.Count==4&&
                Mathf.Approximately(tracker.TrunkBreakRadius,3f)&&
                tracker.TrunkBreakProgress==3,
                "Six trunk radii define the range and a distant hit leaves three qualifying hits");
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
            // This hit is within the expanded six-radius range (3 m).
            tracker.RecordDestructiveImpact(new Vector3(-0.6f,0f,-0.1f));
            var pieces=Fragments(trunkName);
            Require(tracker.IsBroken&&pieces.Length==2&&
                tracker.DestructiveImpacts.Count==0,
                "Four nearby destructive hits break the trunk into two pieces");
            Require(treeRoot.GetComponentsInChildren<MeshRenderer>().Count(renderer=>
                renderer.name.StartsWith("Tree destruction collision point",
                    StringComparison.Ordinal))==5,
                "Impact markers remain visible after the tree splits");
            Require(pieces.Any(piece=>Mathf.Abs(piece.GetComponent<Collider>().bounds.min.y)<0.01f)&&
                pieces.Any(piece=>Mathf.Abs(piece.GetComponent<Collider>().bounds.max.y)<0.01f),
                "The two halves meet at the average impact height");
            foreach(var piece in pieces)
            {
                Require(DestructionMesh.IsClosedConnected(piece.GetComponent<MeshFilter>().sharedMesh),
                    "The centered tree cut produces closed solid halves");
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
            Require(tracker.IsBroken&&upper.transform.parent==null&&
                upper.GetComponent<Rigidbody>() is { isKinematic: false },
                "Clustered damage across a missing trunk band releases the upper trunk");
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
