using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Wyvernland.Editor
{
    public static class PhysicalDestructionValidation
    {
        [MenuItem("Tools/Wyvernland/Validate Held Strike Reaction")]
        public static void RunHeldStrikeReaction()
        {
            var initialRandomState=UnityEngine.Random.state;
            UnityEngine.Random.InitState(20260927);
            var root=new GameObject("Held strike reaction validation");
            try
            {
                ValidateMetalDestructionChance();
                ValidateHeldStrikeDecision(root);
                ValidateSlowStrikeContact(root);
                Debug.Log("HELD_STRIKE_REACTION_VALIDATION_PASS: solid contact and relative speed >3 m/s required; rebound speed and travel capped over 0.12 seconds with fixed local arm anchors; moving-target velocity, threshold boundary, triggers, no-hit waving, destruction, and terrain checked.");
            }
            finally
            {
                UnityEngine.Random.state=initialRandomState;
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static void ValidateMetalDestructionChance()
        {
            Require(WorldConfig.IsMetalMaterial("Iron")&&
                WorldConfig.IsMetalMaterial("native copper")&&
                !WorldConfig.IsMetalMaterial("Hematite")&&
                !WorldConfig.IsMetalMaterial("Stone"),
                "The metal list distinguishes metals from ores and stone");
            for(int resilience=4;resilience<=8;resilience++)
                Require(Mathf.Abs(PhysicalDestructible.MetalDestructionChanceScale(
                    "Iron",resilience)-(8f-resilience)/4f)<.0001f,
                    "Each resilience point from four through eight removes another quarter of metal destruction chance");
            Require(PhysicalDestructible.MetalDestructionChanceScale("Iron",3f)==1f&&
                PhysicalDestructible.MetalDestructionChanceScale("Iron",9f)==0f&&
                PhysicalDestructible.MetalDestructionChanceScale("Stone",8f)==1f,
                "Metal chance clamps at its ends without changing nonmetals");
        }

        [MenuItem("Tools/Wyvernland/Validate Small Shatter Persistence")]
        public static void RunSmallFragmentPersistence()
        {
            var initialRandomState=UnityEngine.Random.state;UnityEngine.Random.InitState(20260927);
            var root=new GameObject("Small Shatter persistence validation");
            try { ValidateUndersizedFragmentCollision(root);Debug.Log("SMALL_SHATTER_PERSISTENCE_VALIDATION_PASS"); }
            finally { UnityEngine.Random.state=initialRandomState;UnityEngine.Object.DestroyImmediate(root); }
        }

        [MenuItem("Tools/Wyvernland/Validate Physical Destruction")]
        public static void Run()
        {
            var initialRandomState=UnityEngine.Random.state;
            UnityEngine.Random.InitState(20260927);
            var root=new GameObject("Physical destruction validation");
            try
            {
                ValidateProgressiveFragments(root);
                ValidateFragmentReimpact(root);
                ValidateUndersizedFragmentCollision(root);
                ValidateSurfaceGuard(root);
                ValidateHeldFragmentRetention(root);
                ValidateHeldStrikeDecision(root);
                UnityEngine.Random.InitState(20260927);
                Require(Mathf.Approximately(PhysicalDestructible.ShatterOffsetMultiplier(10f),1f)&&
                    Mathf.Approximately(PhysicalDestructible.ShatterOffsetMultiplier(5f),1.35f)&&
                    Mathf.Approximately(PhysicalDestructible.ShatterOffsetMultiplier(0f),1.7f),
                    "Shatter offset increases by up to 70 percent as hardness falls from ten to zero");
                Require(PhysicalDestructible.ShatterDividingPoint(Vector3.left,Vector3.zero,1f,0f).x>0.69f,
                    "Shatter dividing point can pass through the center");
                Require(PhysicalDestructible.Effects.Length==5,"Five destruction effects are listed");
                Require(PhysicalDestructible.Classify(2.5f,7,false,5.5f,5)==PhysicalDestructEffect.Fracture,"Wood fractures against stone");
                Require(PhysicalDestructible.Classify(2,3,false,6,7)==PhysicalDestructEffect.Shatter,"Higher hardness and resilience shatters");
                Require(PhysicalDestructible.Classify(2,3,false,6,4)==PhysicalDestructEffect.Fracture,"Similar resilience fractures");
                Require(PhysicalDestructible.Classify(2,3,true,6,7)==PhysicalDestructEffect.Depression,"Soft takes priority");
                Require(PhysicalDestructible.Classify(6,3,false,6,7)==PhysicalDestructEffect.None,"Equal hardness does not destroy");
                Require(PhysicalDestructible.AreSimilar(5,5,7,3)&&
                    PhysicalDestructible.AreSimilar(5,5,3,7)&&
                    !PhysicalDestructible.AreSimilar(5,5,7.01f,5)&&
                    !PhysicalDestructible.AreSimilar(5,5,5,7.01f),
                    "Similarity includes both property boundaries at plus or minus two");
                Require(PhysicalDestructible.Classify(5,5,false,5,5)==PhysicalDestructEffect.Fracture&&
                    PhysicalDestructible.Classify(5,5,false,3,7)==PhysicalDestructEffect.Fracture&&
                    PhysicalDestructible.Classify(5,5,true,5,5)==PhysicalDestructEffect.Depression,
                    "Similar objects can both select destruction, while Soft still selects Depression");
                Require(Mathf.Approximately(PhysicalDestructible.SimilarDestructionChance(0,0,0,0),.3f)&&
                    Mathf.Approximately(PhysicalDestructible.SimilarDestructionChance(5,5,5,5),.155f)&&
                    Mathf.Approximately(PhysicalDestructible.CollisionDestructionChance(10,10,10,10,1,2),.01f)&&
                    Mathf.Approximately(PhysicalDestructible.CollisionDestructionChance(10,10,10,10,2,1),.01f),
                    "Similar collisions fall from 30 percent to one percent without size bonuses overriding the floor");
                Require(Mathf.Abs(PhysicalDestructible.HardnessAfterCollision(5f,6f,0f)-4.85f)<0.0001f&&
                    Mathf.Abs(PhysicalDestructible.HardnessAfterCollision(5f,7f,0f)-4.8575f)<0.0001f&&
                    Mathf.Abs(PhysicalDestructible.HardnessAfterCollision(5f,8f,0f)-4.864625f)<0.0001f&&
                    PhysicalDestructible.HardnessAfterCollision(5f,7f,0.1f)==5f&&
                    PhysicalDestructible.HardnessAfterCollision(5f,20f,0f)<5f&&
                    PhysicalDestructible.HardnessAfterCollision(5f,0f,0.9f)==5f&&
                    PhysicalDestructible.CollisionWearChance(0f)>PhysicalDestructible.CollisionWearChance(4f)&&
                    PhysicalDestructible.CollisionWearChance(4f)>PhysicalDestructible.CollisionWearChance(7f)&&
                    PhysicalDestructible.CollisionWearChance(7f)>PhysicalDestructible.CollisionWearChance(20f),
                    "Collision wear removes three percent, reduced another five percent per resilience above six, with decreasing chance and no resilience immunity");
                foreach(var targetRecord in WorldConfig.ThermalMaterials)
                {
                    var target=new GameObject("Physical validation "+targetRecord.Material);
                    target.transform.SetParent(root.transform);
                    var targetProperties=PhysicalDestructible.Ensure(target);
                    targetProperties.ApplyMaterial(targetRecord.Material);
                    Require(targetProperties.Hardness>=targetRecord.MinimumHardness&&
                        targetProperties.Hardness<=targetRecord.MaximumHardness&&
                        targetProperties.Resilience>=targetRecord.MinimumResilience&&
                        targetProperties.Resilience<=targetRecord.MaximumResilience,
                        "Material hardness and resilience match catalog: "+targetRecord.Material);
                    foreach(var attackerRecord in WorldConfig.ThermalMaterials)
                    {
                        float attackerHardness=(attackerRecord.MinimumHardness+attackerRecord.MaximumHardness)*0.5f;
                        float required=targetProperties.RequiredImpactSpeed(attackerHardness,
                            attackerRecord.Resilience,attackerRecord.Material);
                        bool similar=PhysicalDestructible.AreSimilar(targetProperties.Hardness,
                            targetProperties.Resilience,attackerHardness,attackerRecord.Resilience);
                        Require(attackerHardness>targetProperties.Hardness||similar?
                            !float.IsInfinity(required)&&required>=targetProperties.MinimumImpactSpeed:
                            float.IsPositiveInfinity(required),
                            "Material collision speed follows hardness gate: "+targetRecord.Material+
                            " against "+attackerRecord.Material);
                    }
                    UnityEngine.Object.DestroyImmediate(target);
                }
                var woodThreshold=new GameObject("Physical validation Wood threshold");
                woodThreshold.transform.SetParent(root.transform);
                var woodThresholdProperties=PhysicalDestructible.Ensure(woodThreshold);
                var limestoneThreshold=new GameObject("Physical validation Limestone threshold");
                limestoneThreshold.transform.SetParent(root.transform);
                var limestoneThresholdProperties=PhysicalDestructible.Ensure(limestoneThreshold);
                Require(Mathf.Abs(woodThresholdProperties.RequiredImpactSpeed(5.5f,5f,"Stone")-14f)<0.001f&&
                    Mathf.Abs(limestoneThresholdProperties.RequiredImpactSpeed(5.5f,5f,"Stone")-8f)<0.001f&&
                    Mathf.Abs(woodThresholdProperties.InitialImpactSpeed(5.5f,"Stone")-14f)<0.001f&&
                    Mathf.Abs(limestoneThresholdProperties.InitialImpactSpeed(5.5f,"Stone")-8f)<0.001f,
                    "Wood and limestone require the calibrated Stone midpoint collision speeds");
                Require(float.IsFinite(limestoneThresholdProperties.InitialImpactSpeed(1f,"Wood"))&&
                    float.IsPositiveInfinity(limestoneThresholdProperties.RequiredImpactSpeed(1f,7f,"Wood")),
                    "Fast collisions can cause hardness wear even without an eligible destruction effect");
                foreach(float hardness in new[]{4f,5.5f,7f})
                {
                    Require(woodThresholdProperties.RequiredImpactSpeed(hardness,5f,"Stone")>=14f&&
                        limestoneThresholdProperties.RequiredImpactSpeed(hardness,5f,"Stone")>=8f,
                        "Wood and limestone speed floors hold throughout the Stone hardness range");
                }
                UnityEngine.Object.DestroyImmediate(woodThreshold);
                UnityEngine.Object.DestroyImmediate(limestoneThreshold);
                Vector3 sourceCenter=new Vector3(1,2,3);
                Vector3 pieceCenter=sourceCenter+Vector3.right;
                Vector3 slowImpulse=PhysicalDestructible.FragmentImpulse(sourceCenter,pieceCenter,
                    Vector3.up,4f,2f,0.08f);
                Vector3 fastImpulse=PhysicalDestructible.FragmentImpulse(sourceCenter,pieceCenter,
                    Vector3.up,8f,2f,0.08f);
                Require(Vector3.Dot(slowImpulse,Vector3.right)>0f&&
                    Mathf.Abs(fastImpulse.magnitude-2f*slowImpulse.magnitude)<0.0001f,
                    "Fragments move away from the source center and impulse rises with relative speed");
                Require(PhysicalDestructible.FragmentImpulse(sourceCenter,sourceCenter,
                    Vector3.forward,4f,2f,0.08f).z>0f,
                    "A fragment at the source center uses the collision normal");
                Vector3 woodVelocity=new Vector3(0f,1f,0f);
                Vector3 woodImpulse=new Vector3(4f,40f,0f);
                PhysicalDestructible.LimitWoodFragmentFlight(ref woodVelocity,ref woodImpulse,2f);
                Require(woodImpulse.x==2f&&
                    woodVelocity.y+woodImpulse.y/2f<=
                        PhysicalDestructible.WoodFragmentMaximumUpwardSpeed+0.0001f,
                    "Wood fragments keep outward motion without launching high");
                float rockTotal=0f,stoneTotal=0f,firstRock=0f;
                var rockValues=new HashSet<float>(); var stoneValues=new HashSet<float>();
                for(int i=0;i<64;i++)
                {
                    var rock=new GameObject("Physical validation Rock");
                    rock.transform.SetParent(root.transform);
                    rock.transform.position=new Vector3(i*1.37f,0f,i*0.41f);
                    var stone=new GameObject("Physical validation Stone");
                    stone.transform.SetParent(root.transform);
                    stone.transform.position=new Vector3(i*1.37f,0f,i*0.41f);
                    var rockProperties=PhysicalDestructible.Ensure(rock);
                    var stoneProperties=PhysicalDestructible.Ensure(stone);
                    Require(rockProperties.MaterialName=="Stone"&&stoneProperties.MaterialName=="Stone"&&
                        rockProperties.Resilience==2f&&stoneProperties.Resilience==2f,
                        "Generic rocks and stones use the Stone material record");
                    Require(rockProperties.Hardness>=2f&&rockProperties.Hardness<=5f&&
                        stoneProperties.Hardness>=2f&&stoneProperties.Hardness<=5f,
                        "Every Stone-material hardness stays in the 4–7 range");
                    float originalRock=rockProperties.Hardness;
                    if(i==0) firstRock=originalRock;
                    rockProperties.ApplyMaterial("Stone");
                    Require(rockProperties.Hardness==originalRock,
                        "Reapplying the material keeps this object's hardness");
                    rockValues.Add(originalRock); stoneValues.Add(stoneProperties.Hardness);
                    rockTotal+=originalRock; stoneTotal+=stoneProperties.Hardness;
                    UnityEngine.Object.DestroyImmediate(rock); UnityEngine.Object.DestroyImmediate(stone);
                }
                Require(rockValues.Count>50&&stoneValues.Count>50&&rockTotal<stoneTotal,
                    "Rocks vary independently and are normally softer than stones");
                var repeat=new GameObject("Physical validation Rock");
                repeat.transform.SetParent(root.transform);
                repeat.transform.position=Vector3.zero;
                Require(PhysicalDestructible.Ensure(repeat).Hardness==firstRock,
                    "A streamed rock gets the same hardness at the same location");
                UnityEngine.Object.DestroyImmediate(repeat);
                var player=new GameObject("Excluded player"); player.transform.SetParent(root.transform);
                player.AddComponent<FirstPersonController>();
                var playerBody=GameObject.CreatePrimitive(PrimitiveType.Cube); playerBody.transform.SetParent(player.transform);
                Require(PhysicalDestructible.Ensure(player)==null&&PhysicalDestructible.Ensure(playerBody)==null&&
                    PhysicalDestructible.Resolve(playerBody.GetComponent<Collider>())==null,
                    "Player and child colliders are excluded from physical destruction");
                var preexisting=player.AddComponent<PhysicalDestructible>();
                Require(!preexisting.ApplyImpact(10,10,1,Vector3.zero,Vector3.up,20),
                    "An old player component cannot apply destruction");
                player.SetActive(false);
                Require(PhysicalDestructible.Ensure(playerBody)==null&&
                    PhysicalDestructible.Resolve(playerBody.GetComponent<Collider>())==null,
                    "Inactive player child colliders stay excluded");
                UnityEngine.Object.DestroyImmediate(player);
                var cube=Cube(root,"Cube",1);
                var original=cube.GetComponent<MeshFilter>().sharedMesh;
                Require(DestructionMesh.IsClosedConnected(original),"A complete cube is closed and face-connected");
                Require(PhysicalDestructible.FragmentHasMinimumSize(original,
                    Matrix4x4.Scale(Vector3.one*0.01f))&&
                    !PhysicalDestructible.FragmentHasMinimumSize(original,
                        Matrix4x4.Scale(new Vector3(0f,1f,1f))),
                    "Small solid fragments are allowed, while collapsed meshes remain invalid");
                var openMesh=UnityEngine.Object.Instantiate(original);
                openMesh.triangles=openMesh.triangles.Skip(6).ToArray();
                Require(!DestructionMesh.IsClosedConnected(openMesh),
                    "A fragment with an open edge is rejected");
                var cappedMesh=DestructionMesh.CloseOpenBoundaries(openMesh);
                Require(cappedMesh!=null&&DestructionMesh.IsClosedConnected(cappedMesh)&&
                    Mathf.Abs(Volume(cappedMesh)-1f)<0.0001f,
                    "Missing cube faces are capped into one complete solid");
                UnityEngine.Object.DestroyImmediate(cappedMesh);
                UnityEngine.Object.DestroyImmediate(openMesh);
                var singleFace=new Mesh();
                singleFace.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up};
                singleFace.triangles=new[]{0,1,2};
                Require(DestructionMesh.CloseOpenBoundaries(singleFace)==null,
                    "A partial face cannot be passed off as a solid fragment");
                UnityEngine.Object.DestroyImmediate(singleFace);
                var flatShell=new Mesh();
                flatShell.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up};
                flatShell.triangles=new[]{0,1,2,2,1,0};
                Require(DestructionMesh.CloseOpenBoundaries(flatShell)==null,
                    "Two-sided flat faces are not a complete solid fragment");
                UnityEngine.Object.DestroyImmediate(flatShell);
                // A bent sheet has all three bounds dimensions, and reversing
                // its triangles pairs every edge. It still contains no material.
                foreach(var offset in new[]{Vector3.zero,new Vector3(1000f,2000f,3000f)})
                {
                    var folded=new Mesh();
                    folded.vertices=new[]{new Vector3(0,0,0)+offset,new Vector3(1,0,0)+offset,
                        new Vector3(0,1,0)+offset,new Vector3(0,1,1)+offset};
                    folded.triangles=new[]{0,1,2,0,2,3,2,1,0,3,2,0};
                    folded.RecalculateBounds();
                    Require(!DestructionMesh.IsClosedConnected(folded)&&
                        DestructionMesh.CloseOpenBoundaries(folded)==null,
                        "Translated double-sided bent sheets cannot become solid fragments");
                    UnityEngine.Object.DestroyImmediate(folded);
                }
                var collapsed=UnityEngine.Object.Instantiate(original);
                var prepare=typeof(PhysicalDestructible).GetMethod("PrepareFragmentMesh",
                    System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
                Require(!(bool)prepare.Invoke(null,new object[]{collapsed,
                    Matrix4x4.Scale(new Vector3(1f,0.000001f,1f)),Vector3.zero}),
                    "Spawn preparation rejects a mesh collapsed by its world transform");
                UnityEngine.Object.DestroyImmediate(collapsed);
                var distant=UnityEngine.Object.Instantiate(original);
                Require((bool)prepare.Invoke(null,new object[]{distant,
                    Matrix4x4.TRS(new Vector3(1000000f,1000000f,1000000f),
                        Quaternion.Euler(13f,27f,41f),new Vector3(-0.15f,0.2f,0.3f)),Vector3.zero})&&
                    DestructionMesh.IsClosedConnected(distant),
                    "Small mirrored fragments stay solid at distant world positions");
                UnityEngine.Object.DestroyImmediate(distant);
                var nonManifold=UnityEngine.Object.Instantiate(original);
                nonManifold.triangles=nonManifold.triangles.Concat(nonManifold.triangles.Take(3)).ToArray();
                Require(!DestructionMesh.IsClosedConnected(nonManifold),
                    "A fragment with more than two faces on an edge is rejected");
                UnityEngine.Object.DestroyImmediate(nonManifold);
                var disconnectedMesh=new Mesh();
                disconnectedMesh.CombineMeshes(new[]{
                    new CombineInstance {mesh=original,transform=Matrix4x4.identity},
                    new CombineInstance {mesh=original,transform=Matrix4x4.Translate(Vector3.right*3f)}
                },true,true);
                Require(!DestructionMesh.IsClosedConnected(disconnectedMesh),
                    "A fragment with disconnected closed shells is rejected");
                UnityEngine.Object.DestroyImmediate(disconnectedMesh);
                var islands=Cube(root,"Physical validation Detached Islands",1);
                var islandsMesh=new Mesh();
                islandsMesh.CombineMeshes(new[]{
                    new CombineInstance {mesh=original,transform=Matrix4x4.identity},
                    new CombineInstance {mesh=original,transform=Matrix4x4.Translate(Vector3.up*2f)}
                },true,true);
                islands.GetComponent<MeshFilter>().sharedMesh=islandsMesh;
                // Keep this a Fracture-sized impact so it tests shell separation
                // independently of the partial-Shatter fragment volume budget.
                Require(PhysicalDestructible.Ensure(islands).ApplyImpact(10,5,2,
                    islands.transform.position,Vector3.right,30),
                    "Disconnected closed shells can separate on impact");
                var elevated=Fragments("Physical validation Detached Islands");
                Require(elevated.Length==1&&elevated[0].transform.position.y>1.5f&&
                    islands.GetComponentInChildren<MeshCollider>()!=null,
                    "The higher shell becomes one complete fragment and the lower shell remains");
                Closed(elevated[0].GetComponent<MeshFilter>().sharedMesh);
                UnityEngine.Object.DestroyImmediate(elevated[0].gameObject);
                UnityEngine.Object.DestroyImmediate(islands);
                UnityEngine.Object.DestroyImmediate(islandsMesh);
                var undersized=Cube(root,"Physical validation Undersized",0.15f);
                Require(PhysicalDestructible.Ensure(undersized).ApplyImpact(10f,5f,1f,
                    undersized.transform.position,Vector3.right,30f)&&
                    Fragments("Physical validation Undersized").Length>=2,
                    "A small source above the volume floor can spawn sub-0.1 m fragments");
                foreach(var fragment in Fragments("Physical validation Undersized"))
                    UnityEngine.Object.DestroyImmediate(fragment.gameObject);
                UnityEngine.Object.DestroyImmediate(undersized);
                foreach(var normal in new[]{Vector3.right,new Vector3(1,2,3).normalized,Vector3.up})
                {
                    Require(DestructionMesh.Split(original,new Plane(normal,Vector3.zero),out var a,out var b),"Split succeeds");
                    Closed(a); Closed(b);
                    Require(Mathf.Abs(Volume(a)+Volume(b)-1)<0.0001f,"Fracture conserves volume");
                    Require(WithinBounds(a.bounds,original.bounds)&&WithinBounds(b.bounds,original.bounds),
                        "Fracture faces stay inside the source mesh bounds");
                    UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b);
                }
                Require(!DestructionMesh.Split(original,new Plane(Vector3.up,Vector3.up*10),out _,out _),"Non-intersecting plane leaves source intact");
                var chips=DestructionMesh.Chip(original,new Vector3(0,0.5f,0),0.2f,0.1f,out var remainder);
                Require(chips.Count>=2,"Localized impact creates several closed chips even on coarse faces");
                Require(chips.Any(chip=>chip.triangles.Length/3>8),
                    "Localized Shatter merges adjacent faces into larger fragments");
                Closed(remainder); foreach(var chip in chips) Closed(chip);
                Require(Mathf.Abs(Volume(remainder)+chips.Sum(Volume)-1)<0.0001f,"Localized shatter conserves volume");
                Require(WithinBounds(remainder.bounds,original.bounds)&&
                    chips.All(chip=>WithinBounds(chip.bounds,original.bounds)),
                    "Partial Shatter faces stay inside the source mesh bounds");
                Require(remainder.bounds.min==original.bounds.min,"Unaffected outer faces remain");
                foreach(var chip in chips) UnityEngine.Object.DestroyImmediate(chip);
                UnityEngine.Object.DestroyImmediate(remainder); UnityEngine.Object.DestroyImmediate(cube);

                var overdeepChips=DestructionMesh.Chip(original,Vector3.up*0.5f,
                    0.2f,2f,out var overdeepRemainder);
                Require(overdeepChips.Count==0&&
                    WithinBounds(overdeepRemainder.bounds,original.bounds),
                    "An overdeep cavity cannot form faces beyond the original mesh");
                foreach(var chip in overdeepChips) UnityEngine.Object.DestroyImmediate(chip);
                UnityEngine.Object.DestroyImmediate(overdeepRemainder);

                var cavitySource=Cube(root,"Physical validation Deep cavity",1f);
                var initialChips=DestructionMesh.Chip(cavitySource.GetComponent<MeshFilter>().sharedMesh,
                    Vector3.up*0.5f,0.2f,0.7f,out var cavity);
                Require(initialChips.Count>0,"Deep cavity is created");
                foreach(var chip in initialChips) UnityEngine.Object.DestroyImmediate(chip);
                Closed(cavity);
                var floorChips=DestructionMesh.Chip(cavity,new Vector3(0f,-0.2f,0f),
                    0.12f,0.05f,out var deeperCavity);
                Require(floorChips.Count>0,"A new face past the bounds centre can be destroyed again");
                Closed(deeperCavity);
                foreach(var chip in floorChips) { Closed(chip); UnityEngine.Object.DestroyImmediate(chip); }
                UnityEngine.Object.DestroyImmediate(cavity);
                UnityEngine.Object.DestroyImmediate(deeperCavity);
                UnityEngine.Object.DestroyImmediate(cavitySource);

                var woodChipSource=Cube(root,"Physical validation Wood chip density",2);
                Mesh woodMesh=woodChipSource.GetComponent<MeshFilter>().sharedMesh;
                var ordinaryChips=DestructionMesh.Chip(woodMesh,Vector3.up*0.5f,0.375f,0.15f,
                    out var ordinaryRemainder,2);
                var woodChips=DestructionMesh.Chip(woodMesh,Vector3.up*0.5f,0.375f,0.15f,
                    out var woodRemainder,PhysicalDestructible.WoodShatterFacesPerChip);
                Debug.Log($"WOOD_CHIP_DENSITY ordinary={ordinaryChips.Count} wood={woodChips.Count}");
                Require(woodChips.Count<=ordinaryChips.Count*0.3f,
                    "Wood localized Shatter spawns at least 70 percent fewer chips");
                foreach(var chip in woodChips) Closed(chip);
                Closed(woodRemainder);
                Require(Mathf.Abs(Volume(woodRemainder)+woodChips.Sum(Volume)-1f)<0.0001f,
                    "Larger Wood chips conserve the source volume");
                foreach(var chip in ordinaryChips) UnityEngine.Object.DestroyImmediate(chip);
                foreach(var chip in woodChips) UnityEngine.Object.DestroyImmediate(chip);
                UnityEngine.Object.DestroyImmediate(ordinaryRemainder);
                UnityEngine.Object.DestroyImmediate(woodRemainder);
                Mesh impactSource=woodMesh;
                for(int impact=0;impact<2;impact++)
                {
                    var limitedChips=PhysicalDestructible.ChipWithinFragmentBudget(impactSource,
                        new Vector3(impact==0?-0.35f:0.35f,0.5f,0),0.375f,
                        out var nextRemainder,2,8);
                    Require(limitedChips.Count>0&&limitedChips.Count<=8,
                        "Each localized impact gets its own eight-fragment budget");
                    Closed(nextRemainder);
                    foreach(var chip in limitedChips) Closed(chip);
                    Require(Mathf.Abs(Volume(nextRemainder)+limitedChips.Sum(Volume)-Volume(impactSource))<0.0001f,
                        "Capped localized Shatter conserves volume on successive impacts");
                    foreach(var chip in limitedChips) UnityEngine.Object.DestroyImmediate(chip);
                    if(impactSource!=woodMesh) UnityEngine.Object.DestroyImmediate(impactSource);
                    impactSource=nextRemainder;
                }
                UnityEngine.Object.DestroyImmediate(impactSource);
                UnityEngine.Object.DestroyImmediate(woodChipSource);

                var incomplete=Cube(root,"Physical validation Incomplete Wood",1);
                var incompleteMesh=UnityEngine.Object.Instantiate(incomplete.GetComponent<MeshFilter>().sharedMesh);
                incompleteMesh.triangles=incompleteMesh.triangles.Skip(6).ToArray();
                incomplete.GetComponent<MeshFilter>().sharedMesh=incompleteMesh;
                var incompleteProperties=PhysicalDestructible.Ensure(incomplete);
                Require(incompleteProperties.ApplyImpact(6,5,1,Vector3.zero,Vector3.right,30),
                    "A source with a simple missing face can be repaired before destruction");
                foreach(var fragment in Fragments("Physical validation Incomplete Wood"))
                { Closed(fragment.GetComponent<MeshFilter>().sharedMesh);
                    UnityEngine.Object.DestroyImmediate(fragment.gameObject); }
                if(incomplete!=null) UnityEngine.Object.DestroyImmediate(incomplete);
                UnityEngine.Object.DestroyImmediate(incompleteMesh);

                var wood=Cube(root,"Physical validation Wood",1); var props=PhysicalDestructible.Ensure(wood);
                wood.AddComponent<Rigidbody>().mass=12;
                bool notified=false; props.GeometryChanged+=()=>notified=true;
                int destructionNotices=0;
                PhysicalDestructEffect shownEffect=PhysicalDestructEffect.None;
                Action<PhysicalDestructEffect> onDestruction=effect=>
                { destructionNotices++; shownEffect=effect; };
                PhysicalDestructible.DestructionOccurred+=onDestruction;
                try
                {
                    Require(!props.ApplyImpact(5.5f,5,1,Vector3.up*0.5f,Vector3.up,13.99f,"Stone")&&
                        destructionNotices==0,"Sub-threshold impacts do not announce destruction");
                    Require(props.ApplyImpact(5.5f,5,1,Vector3.up*0.5f,Vector3.up,14f,"Stone")&&
                        destructionNotices==1&&shownEffect==PhysicalDestructEffect.Fracture,
                        "A completed fracture announces its effect name exactly once");
                }
                finally { PhysicalDestructible.DestructionOccurred-=onDestruction; }
                var fragments=Fragments("Physical validation Wood");
                Require(fragments.Length==2&&notified&&wood==null,"Exactly two bodies replace source and notify streaming");
                Require(Mathf.Abs(fragments.Sum(f=>f.GetComponent<Rigidbody>().mass)-650f)<0.01f,"Wood fragment mass matches mesh volume times density");
                foreach(var fragment in fragments)
                    Require(fragment.HasMaterialMass&&fragment.MassKg>0f&&
                        Mathf.Abs(fragment.MassKg-fragment.GetComponent<Rigidbody>().mass)<0.01f,
                        "Fragment mass properties agree with its physical body");
                foreach(var fragment in fragments)
                { Require(fragment.GetComponent<MeshCollider>().convex,"Convex fragment collider"); Require(fragment.Hardness==2.5f&&fragment.Resilience==7,"Properties inherited"); Closed(fragment.GetComponent<MeshFilter>().sharedMesh); Require(PhysicalDestructible.FragmentHasMinimumSize(fragment.GetComponent<MeshFilter>().sharedMesh,fragment.transform.localToWorldMatrix),"Spawned fragment axes meet the 0.1 m floor"); Centered(fragment); UnityEngine.Object.DestroyImmediate(fragment.gameObject); }

                var largeWood=Cube(root,"Physical validation Large Wood",2);
                var largeWoodProperties=PhysicalDestructible.Ensure(largeWood);
                Require(!largeWoodProperties.ApplyImpact(4.5f,5,1,largeWood.transform.position,
                    Vector3.right,30)&&largeWood!=null&&
                    Fragments("Physical validation Large Wood").Length==0,
                    "Fracture stays blocked at half the target's size when hardness lead is two");
                Require(largeWoodProperties.ApplyImpact(6,5,1.01f,largeWood.transform.position+Vector3.up,
                    Vector3.right,30),
                    "A large object can still receive localized Fracture damage");
                Require(largeWood!=null&&!largeWoodProperties.HasBeenDestroyed&&largeWoodProperties.HasBeenModified,
                    "An object above one cubic metre keeps a remainder after Fracture");
                foreach(var fragment in Fragments("Physical validation Large Wood"))
                    UnityEngine.Object.DestroyImmediate(fragment.gameObject);
                UnityEngine.Object.DestroyImmediate(largeWood);

                foreach(bool shatter in new[]{false,true})
                {
                    string volumeName="Physical validation Volume Limit "+shatter;
                    var volumeLimited=Cube(root,volumeName,1.1f);
                    var volumeProperties=PhysicalDestructible.Ensure(volumeLimited);
                    Require(volumeProperties.SolidVolumeCubicMetres>1d,"Volume restriction uses mesh solid volume");
                    bool volumeDamaged=volumeProperties.ApplyImpact(6f,shatter?10f:5f,10f,Vector3.up*0.55f,Vector3.up,30f);
                    Require(volumeLimited!=null&&!volumeProperties.HasBeenDestroyed&&
                        (!volumeDamaged||volumeProperties.HasBeenModified)&&
                        volumeProperties.LastEffect==(shatter?PhysicalDestructEffect.Shatter:PhysicalDestructEffect.Fracture),
                        "A large impact cannot completely destroy an object over one cubic metre in either mode");
                    foreach(var fragment in Fragments(volumeName)) UnityEngine.Object.DestroyImmediate(fragment.gameObject);
                    UnityEngine.Object.DestroyImmediate(volumeLimited);
                }

                var hardHitWood=Cube(root,"Physical validation Hard Hit Large Wood",2);
                var hardHitProperties=PhysicalDestructible.Ensure(hardHitWood);
                Require(hardHitProperties.ApplyImpact(6,5,1,Vector3.up,Vector3.up,30)&&
                    hardHitProperties.LastEffect==PhysicalDestructEffect.Shatter&&
                    hardHitWood!=null&&hardHitProperties.HasBeenModified,
                    "A hardness lead above two turns size-blocked Fracture into localized Shatter");
                var localWoodFragments=Fragments("Physical validation Hard Hit Large Wood");
                Require(localWoodFragments.Length>0&&localWoodFragments.Length<=4,
                    "Actual localized Wood Shatter spawns at least 70 percent fewer fragments");
                foreach(var fragment in localWoodFragments)
                { Closed(fragment.GetComponent<MeshFilter>().sharedMesh); UnityEngine.Object.DestroyImmediate(fragment.gameObject); }
                UnityEngine.Object.DestroyImmediate(hardHitWood);

                var limestone=Cube(root,"Physical validation Limestone",1);
                var limestoneProperties=PhysicalDestructible.Ensure(limestone);
                Require(!limestoneProperties.ApplyImpact(5.5f,5f,1,Vector3.up*0.5f,
                    Vector3.up,7.99f,"Stone"),"Limestone survives a Stone impact just below 8 m/s");
                bool limestoneDestroyed=false;
                for(int attempt=0;attempt<100&&!limestoneDestroyed;attempt++)
                    limestoneDestroyed=limestoneProperties.ApplyImpact(5.5f,5f,1,
                        Vector3.up*0.5f,Vector3.up,8f,"Stone");
                Require(limestoneDestroyed&&limestoneProperties.LastEffect==PhysicalDestructEffect.Fracture,
                    "Limestone can fracture at an 8 m/s Stone impact after its similarity roll");
                foreach(var fragment in Fragments("Physical validation Limestone"))
                    UnityEngine.Object.DestroyImmediate(fragment.gameObject);

                var randomState=UnityEngine.Random.state;
                int bothAffected=0,oneAffected=0,neitherAffected=0;
                try
                {
                    UnityEngine.Random.InitState(761);
                    for(int trial=0;trial<40;trial++)
                    {
                        string leftName="Physical validation Similar Wood Left "+trial;
                        string rightName="Physical validation Similar Wood Right "+trial;
                        var left=Cube(root,leftName,1);
                        var right=Cube(root,rightName,1);
                        bool leftAffected=PhysicalDestructible.Ensure(left).ApplyImpact(2.5f,8f,
                            1,Vector3.zero,Vector3.right,30,"Wood");
                        bool rightAffected=PhysicalDestructible.Ensure(right).ApplyImpact(2.5f,8f,
                            1,Vector3.zero,Vector3.left,30,"Wood");
                        if(leftAffected&&rightAffected) bothAffected++;
                        else if(leftAffected||rightAffected) oneAffected++;
                        else neitherAffected++;
                        if(left!=null) UnityEngine.Object.DestroyImmediate(left);
                        if(right!=null) UnityEngine.Object.DestroyImmediate(right);
                        foreach(var fragment in Fragments(leftName))
                            UnityEngine.Object.DestroyImmediate(fragment.gameObject);
                        foreach(var fragment in Fragments(rightName))
                            UnityEngine.Object.DestroyImmediate(fragment.gameObject);
                    }
                }
                finally { UnityEngine.Random.state=randomState; }
                Require(bothAffected>0&&oneAffected>0&&neitherAffected>0,
                    "Similar objects independently can both fracture, only one fracture, or neither fracture");
                Require(Mathf.Abs((2f*bothAffected+oneAffected)/80f-0.4f)<0.2f,
                    "Similarity outcomes stay near the 40 percent chance per object");

                var tinyWood=Cube(root,"Physical validation Tiny Wood",1);
                tinyWood.transform.localScale=new Vector3(0.1f,0.1f,0.199f);
                var tinyWoodProperties=PhysicalDestructible.Ensure(tinyWood);
                Require(!tinyWoodProperties.ApplyImpact(6,5,1,tinyWood.transform.position,
                    Vector3.right,30)&&tinyWood!=null&&
                    Fragments("Physical validation Tiny Wood").Length==0,
                    "Fracture is blocked below 0.002 cubic metres");
                UnityEngine.Object.DestroyImmediate(tinyWood);
                var tinyObsidian=Cube(root,"Physical validation Tiny Obsidian",1);
                tinyObsidian.transform.localScale=new Vector3(0.1f,0.1f,0.199f);
                var tinyObsidianProperties=PhysicalDestructible.Ensure(tinyObsidian);
                Require(!tinyObsidianProperties.ApplyImpact(7,7,1,tinyObsidian.transform.position,
                    Vector3.right,30)&&tinyObsidian!=null&&
                    Fragments("Physical validation Tiny Obsidian").Length==0,
                    "Shatter is blocked below 0.002 cubic metres");
                UnityEngine.Object.DestroyImmediate(tinyObsidian);
                foreach(string material in new[]{"Wood","Burnt Wood","Iron"})
                {
                    string name="Physical validation Thin "+material;
                    var thin=Cube(root,name,1f);
                    thin.transform.localScale=new Vector3(0.03f,0.03f,0.3f);
                    var thinProperties=PhysicalDestructible.Ensure(thin);
                    thinProperties.ApplyMaterial(material);
                    Require(thinProperties.SolidVolumeCubicMetres<
                        PhysicalDestructible.MinimumFragmentationVolume&&
                        thinProperties.ApplyImpact(10f,5f,1f,thin.transform.position,
                            Vector3.right,30f)&&Fragments(name).Length>=2,
                        "A thin small "+material+" stick can still break");
                    if(thinProperties.LastEffect==PhysicalDestructEffect.Fracture)
                        foreach(var fragment in Fragments(name))
                        {
                            Vector3 size=fragment.GetComponent<Renderer>().bounds.size;
                            Require(size.z<0.3f&&size.x>0.025f&&size.y>0.025f,
                                "Stick Fracture cuts across its long axis instead of splitting lengthwise");
                        }
                    foreach(var fragment in Fragments(name))
                        UnityEngine.Object.DestroyImmediate(fragment.gameObject);
                    if(thin!=null) UnityEngine.Object.DestroyImmediate(thin);
                }
                var nearLimitWood=Cube(root,"Physical validation Limit Wood",1);
                nearLimitWood.transform.localScale=new Vector3(0.1f,0.1f,0.201f);
                var nearLimitProperties=PhysicalDestructible.Ensure(nearLimitWood);
                Require(nearLimitProperties.ApplyImpact(6,5,1,nearLimitWood.transform.position,
                    Vector3.right,30)&&Fragments("Physical validation Limit Wood").Length>0,
                    "An object above the volume threshold can now make sub-0.1 m fragments");
                foreach(var fragment in Fragments("Physical validation Limit Wood"))
                    UnityEngine.Object.DestroyImmediate(fragment.gameObject);
                UnityEngine.Object.DestroyImmediate(nearLimitWood);
                var eligibleWood=Cube(root,"Physical validation Eligible Wood",0.21f);
                Require(PhysicalDestructible.Ensure(eligibleWood).ApplyImpact(6,5,1,
                    eligibleWood.transform.position,Vector3.right,30),
                    "A source large enough for 0.1 m fragments can fracture");
                foreach(var fragment in Fragments("Physical validation Eligible Wood"))
                {
                    Require(PhysicalDestructible.FragmentHasMinimumSize(
                        fragment.GetComponent<MeshFilter>().sharedMesh,
                        fragment.transform.localToWorldMatrix),
                    "Randomly oriented fracture fragments remain three-dimensional");
                    UnityEngine.Object.DestroyImmediate(fragment.gameObject);
                }
                var tinySoft=Cube(root,"Physical validation Tiny Clay",1);
                tinySoft.transform.localScale=Vector3.one*0.1f;
                var tinySoftProperties=PhysicalDestructible.Ensure(tinySoft);
                Require(!tinySoftProperties.ApplyImpact(6,5,1,tinySoft.transform.position,
                    Vector3.right,30)&&tinySoftProperties.LastEffect==PhysicalDestructEffect.Depression,
                    "Small soft objects still select Depression for its future implementation");
                UnityEngine.Object.DestroyImmediate(tinySoft);

                var stoneSource=Cube(root,"Physical validation Stone",1);
                stoneSource.transform.position=new Vector3(0f,3f,0f);
                float sourceHardness=PhysicalDestructible.Ensure(stoneSource).Hardness;
                Require(stoneSource.GetComponent<PhysicalDestructible>().ApplyImpact(10,5,1,
                    stoneSource.transform.position,Vector3.right,30),"Stone source fractures");
                fragments=Fragments("Physical validation Stone");
                Require(fragments.Length==2&&fragments.All(f=>f.Hardness==sourceHardness),
                    "Stone fragments inherit the original sampled hardness");
                foreach(var fragment in fragments) UnityEngine.Object.DestroyImmediate(fragment.gameObject);

                var brittle=Cube(root,"Physical validation Obsidian",1); props=PhysicalDestructible.Ensure(brittle);
                Require(props.ApplyImpact(7,7,1,Vector3.up*0.5f,Vector3.up,30),"Full shatter applied");
                fragments=Fragments("Physical validation Obsidian");
                Require(fragments.Length>=3&&fragments.Length<=8&&brittle==null,
                    "Full shatter replaces source with several complete pieces");
                foreach(var fragment in fragments) { Closed(fragment.GetComponent<MeshFilter>().sharedMesh); Centered(fragment); UnityEngine.Object.DestroyImmediate(fragment.gameObject); }

                var shatteredRock=Cube(root,"Physical validation Rock shatter",1);
                Require(PhysicalDestructible.Ensure(shatteredRock).ApplyImpact(10,10,1,
                    shatteredRock.transform.position+Vector3.up*0.5f,Vector3.up,30),
                    "Rock shatter applied");
                fragments=Fragments("Physical validation Rock shatter");
                Require(fragments.Length>=2&&
                    fragments.Length<=PhysicalDestructible.RockFullShatterFragmentsPerImpact,
                    "Rock full Shatter emits about 30 percent fewer pieces");
                foreach(var fragment in fragments)
                { Closed(fragment.GetComponent<MeshFilter>().sharedMesh); UnityEngine.Object.DestroyImmediate(fragment.gameObject); }

                var shallow=Cube(root,"Physical validation Shallow Obsidian",1);
                shallow.transform.localScale=new Vector3(0.3f,0.3f,0.15f);
                Require(PhysicalDestructible.Ensure(shallow).ApplyImpact(7,7,1,
                    shallow.transform.position,Vector3.right,30),
                    "Shatter keeps valid larger pieces when further splitting would make a thin fragment");
                fragments=Fragments("Physical validation Shallow Obsidian");
                Require(fragments.Length>=2&&fragments.Length<=8&&fragments.All(fragment=>
                    PhysicalDestructible.FragmentHasMinimumSize(
                        fragment.GetComponent<MeshFilter>().sharedMesh,
                        fragment.transform.localToWorldMatrix)),
                    "Shallow full Shatter produces valid small solids");
                foreach(var fragment in fragments) UnityEngine.Object.DestroyImmediate(fragment.gameObject);

                var fullWood=Cube(root,"Physical validation Full Wood Shatter",1);
                Require(PhysicalDestructible.Ensure(fullWood).ApplyImpact(6,10,1,
                    Vector3.up*0.5f,Vector3.up,30),"Wood can fully Shatter");
                fragments=Fragments("Physical validation Full Wood Shatter");
                Require(fragments.Length==2&&fullWood==null,
                    "Full Wood Shatter spawns two larger pieces rather than eight");
                foreach(var fragment in fragments)
                { Closed(fragment.GetComponent<MeshFilter>().sharedMesh); Centered(fragment); UnityEngine.Object.DestroyImmediate(fragment.gameObject); }

                var large=Cube(root,"Physical validation Large Obsidian",2); props=PhysicalDestructible.Ensure(large);
                Require(props.ApplyImpact(7,7,1,Vector3.up,Vector3.up,30),"Exactly twice the dimension selects localized shatter");
                Require(large!=null&&props.HasBeenModified&&!props.HasBeenDestroyed,"Large source survives with a recessed surface");
                Require(!large.GetComponent<Collider>().enabled,"Old source collider disabled");
                Require(large.GetComponentsInChildren<MeshCollider>().Any(c=>c.enabled),"Replacement collider active");
                foreach(var fragment in Fragments("Physical validation Large Obsidian"))
                {
                    Centered(fragment);
                    Require(fragment.GetComponent<Renderer>().bounds.size.magnitude<1.5f,
                        "Localized chips stay smaller than the one-metre impactor");
                }
                foreach(var f in root.GetComponentsInChildren<MeshFilter>()) if(f.GetComponent<Renderer>().enabled) Closed(f.sharedMesh);
                UnityEngine.Object.DestroyImmediate(large);
                foreach(var fragment in Fragments("Physical validation Large Obsidian")) UnityEngine.Object.DestroyImmediate(fragment.gameObject);

                var scaledParent=new GameObject("Scaled parent"); scaledParent.transform.SetParent(root.transform);
                scaledParent.transform.localScale=new Vector3(1.7f,0.8f,2.3f);
                scaledParent.transform.localRotation=Quaternion.Euler(9,37,4);
                var scaled=Cube(scaledParent,"Physical validation Scaled Wood",1);
                scaled.transform.localPosition=new Vector3(2,1,-3);
                scaled.transform.localRotation=Quaternion.Euler(20,28,13);
                scaled.transform.localScale=new Vector3(0.8f,0.6f,0.28f);
                Bounds expected=scaled.GetComponent<Renderer>().bounds;
                props=PhysicalDestructible.Ensure(scaled);
                Require(props.ApplyImpact(6,5,props.WorldSize()*0.6f,expected.center,
                    scaled.transform.up,30),
                    "Scaled and rotated source fractures");
                fragments=Fragments("Physical validation Scaled Wood");
                Require(fragments.Length==2,"Scaled source creates two fragments");
                Bounds union=fragments[0].GetComponent<Renderer>().bounds;
                foreach(var fragment in fragments)
                {
                    Centered(fragment);
                    union.Encapsulate(fragment.GetComponent<Renderer>().bounds);
                    Require(SameBounds(fragment.GetComponent<Renderer>().bounds,
                        fragment.GetComponent<MeshCollider>().bounds,0.001f),
                        "Rendered fragment and collider have matching world size");
                }
                Require(SameBounds(expected,union,0.001f),
                    "Fractured pieces occupy exactly the original world bounds under scaled, rotated parents");
                foreach(var fragment in fragments) UnityEngine.Object.DestroyImmediate(fragment.gameObject);

                var multipart=new GameObject("Physical validation Multipart Wood");
                multipart.transform.SetParent(root.transform);
                foreach(float x in new[]{-2f,2f,4f})
                {
                    var section=Cube(multipart,"section",1);
                    section.transform.localPosition=new Vector3(x,0f,0f);
                }
                multipart.AddComponent<Rigidbody>().mass=9f;
                props=PhysicalDestructible.Ensure(multipart);
                Require(props.ApplyImpact(6,5,props.WorldSize()*0.6f,Vector3.zero,Vector3.right,30),
                    "A source with separate mesh islands fractures");
                fragments=Fragments("Physical validation Multipart Wood");
                Require(fragments.Length==2,"The lower island stays on the source while separate islands receive bodies");
                var centers=fragments.Select(f=>f.transform.position.x).OrderBy(x=>x).ToArray();
                Require(Mathf.Abs(centers[0]-2f)<0.001f&&
                    Mathf.Abs(centers[1]-4f)<0.001f,"Each detached body pivots around its own island");
                Require(Mathf.Abs(fragments.Sum(f=>f.GetComponent<Rigidbody>().mass)+
                    multipart.GetComponent<Rigidbody>().mass-1950f)<0.02f,
                    "Splitting disconnected islands conserves density-derived mass");
                foreach(var fragment in fragments)
                {
                    Require(DestructionMesh.SeparateComponents(fragment.GetComponent<MeshFilter>().sharedMesh).Count==1,
                        "No fragment contains disconnected faces");
                    Closed(fragment.GetComponent<MeshFilter>().sharedMesh); Centered(fragment);
                    UnityEngine.Object.DestroyImmediate(fragment.gameObject);
                }

                var tetra=new Mesh();
                tetra.vertices=new[]{Vector3.zero,new Vector3(2,0,0),new Vector3(0,1,0),
                    new Vector3(0,0,1)};
                tetra.triangles=new[]{0,2,1,0,1,3,0,3,2,1,2,3};
                tetra.RecalculateNormals(); tetra.RecalculateBounds();
                Require(Vector3.Distance(DestructionMesh.VolumeCentroid(tetra),
                    new Vector3(0.5f,0.25f,0.25f))<0.0001f,
                    "Volume center differs from the bounding-box midpoint for a wedge");
                var skewed=new GameObject("Physical validation Skewed Wood");
                skewed.transform.SetParent(root.transform);
                skewed.AddComponent<MeshFilter>().sharedMesh=tetra;
                skewed.AddComponent<MeshRenderer>().sharedMaterial=Cube(root,"temporary material",1).GetComponent<MeshRenderer>().sharedMaterial;
                skewed.AddComponent<MeshCollider>().sharedMesh=tetra;
                props=PhysicalDestructible.Ensure(skewed);
                Require(props.ApplyImpact(6,5,props.WorldSize()*0.6f,
                    Vector3.zero,Vector3.right,30),
                    "Asymmetric object fractures");
                fragments=Fragments("Physical validation Skewed Wood");
                Require(fragments.Length==2,"Asymmetric object creates two fragments");
                foreach(var fragment in fragments)
                {
                    Centered(fragment); Closed(fragment.GetComponent<MeshFilter>().sharedMesh);
                    UnityEngine.Object.DestroyImmediate(fragment.gameObject);
                }
                UnityEngine.Object.DestroyImmediate(tetra);

                var shader=Shader.Find("Sprites/Default")??Shader.Find("Standard")??
                    Shader.Find("Hidden/InternalErrorShader");
                Require(shader!=null,"Validation shader is available");
                var bark=new Material(shader); bark.SetColor("_Color",new Color(0.7f,0.3f,0.1f));
                var mineral=new Material(shader); mineral.SetColor("_Color",new Color(0.2f,0.4f,0.8f));
                var sourceColor=new Color(0.25f,0.5f,0.75f,1f);
                var overallTint=new Color(0.8f,0.6f,0.3f,1f);
                var secondTint=new Color(0.4f,0.9f,0.2f,1f);
                var colored=Cube(root,"Physical validation Colored Wood",1);
                var sourceMesh=UnityEngine.Object.Instantiate(colored.GetComponent<MeshFilter>().sharedMesh);
                var triangles=sourceMesh.triangles;
                sourceMesh.subMeshCount=2;
                sourceMesh.SetTriangles(triangles.Take(triangles.Length/2).ToArray(),0);
                sourceMesh.SetTriangles(triangles.Skip(triangles.Length/2).ToArray(),1);
                sourceMesh.colors=Enumerable.Repeat(sourceColor,sourceMesh.vertexCount).ToArray();
                colored.GetComponent<MeshFilter>().sharedMesh=sourceMesh;
                var coloredRenderer=colored.GetComponent<MeshRenderer>();
                coloredRenderer.sharedMaterials=new[]{bark,mineral};
                var overallBlock=new MaterialPropertyBlock(); overallBlock.SetColor("_Color",overallTint);
                coloredRenderer.SetPropertyBlock(overallBlock);
                var secondBlock=new MaterialPropertyBlock(); secondBlock.SetColor("_Color",secondTint);
                coloredRenderer.SetPropertyBlock(secondBlock,1);
                props=PhysicalDestructible.Ensure(colored);
                Require(props.ApplyImpact(6,5,1,Vector3.zero,Vector3.right,30),"Colored source fractures");
                fragments=Fragments("Physical validation Colored Wood");
                Require(fragments.Length==2,"Colored source creates two fragments");
                foreach(var fragment in fragments)
                {
                    CheckAppearance(fragment.GetComponent<MeshRenderer>(),fragment.GetComponent<MeshFilter>().sharedMesh,
                        bark,mineral,sourceColor,overallTint,secondTint);
                    UnityEngine.Object.DestroyImmediate(fragment.gameObject);
                }
                UnityEngine.Object.DestroyImmediate(sourceMesh);

                colored=Cube(root,"Physical validation Colored Obsidian",2);
                sourceMesh=UnityEngine.Object.Instantiate(colored.GetComponent<MeshFilter>().sharedMesh);
                triangles=sourceMesh.triangles; sourceMesh.subMeshCount=2;
                sourceMesh.SetTriangles(triangles.Take(triangles.Length/2).ToArray(),0);
                sourceMesh.SetTriangles(triangles.Skip(triangles.Length/2).ToArray(),1);
                sourceMesh.colors=Enumerable.Repeat(sourceColor,sourceMesh.vertexCount).ToArray();
                colored.GetComponent<MeshFilter>().sharedMesh=sourceMesh;
                coloredRenderer=colored.GetComponent<MeshRenderer>();
                coloredRenderer.sharedMaterials=new[]{bark,mineral};
                coloredRenderer.SetPropertyBlock(overallBlock);
                coloredRenderer.SetPropertyBlock(secondBlock,1);
                props=PhysicalDestructible.Ensure(colored);
                Require(props.ApplyImpact(7,7,1,Vector3.up,Vector3.up,30),"Colored source shatters locally");
                fragments=Fragments("Physical validation Colored Obsidian");
                Require(fragments.Length>=2,"Colored source creates several chips");
                foreach(var fragment in fragments)
                {
                    CheckAppearance(fragment.GetComponent<MeshRenderer>(),fragment.GetComponent<MeshFilter>().sharedMesh,
                        bark,mineral,sourceColor,overallTint,secondTint);
                    UnityEngine.Object.DestroyImmediate(fragment.gameObject);
                }
                var retained=colored.GetComponentsInChildren<MeshRenderer>().Single(r=>r.enabled);
                CheckAppearance(retained,retained.GetComponent<MeshFilter>().sharedMesh,
                    bark,mineral,sourceColor,overallTint,secondTint);
                UnityEngine.Object.DestroyImmediate(colored);
                UnityEngine.Object.DestroyImmediate(sourceMesh);
                UnityEngine.Object.DestroyImmediate(bark); UnityEngine.Object.DestroyImmediate(mineral);
                Debug.Log("PHYSICAL_DESTRUCTION_VALIDATION_PASS: closed connected fragment topology and incomplete-source rejection, independent 40-percent similar-property outcomes, minimum world-volume gate and fragment re-impact, transient non-colliding Shatter debris, outward speed-scaled fragment impulse, disconnected-island separation, per-fragment volume centroids, Stone hardness range, Rock/Stone bias and streaming stability, fragment inheritance, classification, player exclusion, caps, volume, bounded localized chips, thresholds, rigidbodies, colliders, source replacement, world-size preservation, material slots, material tint, renderer overrides, vertex color, streaming notification.");
            }
            finally
            {
                UnityEngine.Random.state=initialRandomState;
                foreach(var fragment in UnityEngine.Object.FindObjectsByType<PhysicalDestructible>(FindObjectsSortMode.None))
                    if(fragment.name.StartsWith("Physical validation ",StringComparison.Ordinal))
                        UnityEngine.Object.DestroyImmediate(fragment.gameObject);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
        static PhysicalDestructible[] Fragments(string sourceName) =>
            UnityEngine.Object.FindObjectsByType<PhysicalDestructible>(FindObjectsSortMode.None)
                .Where(f=>f.name==sourceName+" fragment").ToArray();
        static void ValidateHeldStrikeDecision(GameObject root)
        {
            var held=Cube(root,"Physical validation Strike Impactor",0.4f);
            var heldProperties=PhysicalDestructible.Ensure(held);
            heldProperties.ApplyMaterial("Iron");
            var weak=Cube(root,"Physical validation Strike Target",0.4f);
            string weakName=weak.name;
            weak.transform.position=new Vector3(100f,0f,0f);
            var weakProperties=PhysicalDestructible.Ensure(weak);
            weakProperties.ApplyMaterial("Wood");
            var strike=typeof(PhysicalDestructible).GetMethod("TryHeldStrikeDestruction",
                System.Reflection.BindingFlags.Instance|
                System.Reflection.BindingFlags.NonPublic);
            Require((bool)strike.Invoke(heldProperties,new object[]{weakProperties,
                    weak.transform.position-Vector3.right*0.2f,Vector3.right*30f})&&
                Fragments(weakName).Length>0,
                "A held strike destroys a target when its material and geometry rules allow it");
            foreach(var fragment in Fragments(weakName))
                UnityEngine.Object.DestroyImmediate(fragment.gameObject);
            if(weak!=null) UnityEngine.Object.DestroyImmediate(weak);

            var strong=Cube(root,"Physical validation Strike Blocker",0.4f);
            strong.transform.position=new Vector3(102f,0f,0f);
            var strongProperties=PhysicalDestructible.Ensure(strong);
            strongProperties.Hardness=10f;
            strongProperties.Resilience=10f;
            Require(!(bool)strike.Invoke(heldProperties,new object[]{strongProperties,
                    strong.transform.position-Vector3.right*0.2f,Vector3.right})&&
                !strongProperties.HasBeenDestroyed&&!heldProperties.HasBeenDestroyed,
                "An impact below destruction speed leaves both solids intact for rebound");

            var actor=new GameObject("Physical validation Strike Avatar");
            actor.transform.SetParent(root.transform);
            var avatar=actor.AddComponent<RiggedPlayerAvatar>();
            Transform shoulder=new GameObject("Strike Shoulder").transform;
            shoulder.SetParent(actor.transform);
            Transform elbow=new GameObject("Strike Elbow").transform;
            elbow.SetParent(shoulder);
            Transform hand=new GameObject("Strike Hand").transform;
            hand.SetParent(elbow);
            var heldBar=Cube(root,"Physical validation Held Strike Bar",1f);
            heldBar.transform.localScale=new Vector3(0.03f,0.03f,0.3f);
            PhysicalDestructible.Ensure(heldBar).ApplyMaterial("Iron");
            var heldPickup=heldBar.AddComponent<PickableObject>();
            Require(heldPickup.PickUp(hand),"Strike validation bar can be held");
            var avatarFlags=System.Reflection.BindingFlags.Instance|
                System.Reflection.BindingFlags.NonPublic;
            typeof(RiggedPlayerAvatar).GetField("rightShoulder",avatarFlags).SetValue(avatar,shoulder);
            typeof(RiggedPlayerAvatar).GetField("rightElbow",avatarFlags).SetValue(avatar,elbow);
            typeof(RiggedPlayerAvatar).GetField("rightHandMarker",avatarFlags).SetValue(avatar,hand);
            typeof(RiggedPlayerAvatar).GetField("heldRightHandItem",avatarFlags).SetValue(avatar,heldPickup);
            typeof(RiggedPlayerAvatar).GetField("striking",avatarFlags).SetValue(avatar,true);
            typeof(PickableObject).GetField("heldLinearVelocity",avatarFlags)
                .SetValue(heldPickup,Vector3.right);
            var resolve=typeof(PickableObject).GetMethod("TryResolveStrikeImpact",avatarFlags);
            var weakHeld=Cube(root,"Physical validation Held Strike Target",0.4f);
            string weakHeldName=weakHeld.name;
            weakHeld.transform.position=new Vector3(104f,0f,0f);
            PhysicalDestructible.Ensure(weakHeld).ApplyMaterial("Wood");
            Require((bool)resolve.Invoke(heldPickup,new object[]{weakHeld.GetComponent<Collider>(),
                    weakHeld.transform.position-Vector3.right*0.2f,Vector3.zero,
                    (Vector3?) (Vector3.right*30f)})&&
                avatar.IsStrikeMode&&
                !(bool)typeof(RiggedPlayerAvatar).GetField("strikeRebounding",avatarFlags)
                    .GetValue(avatar),
                "A destructive held strike leaves the hand moving through its strike");
            Require((bool)resolve.Invoke(heldPickup,new object[]{strong.GetComponent<Collider>(),
                    strong.transform.position-Vector3.right*0.2f,Vector3.zero,
                    (Vector3?)Vector3.right})&&avatar.IsStrikeMode&&
                !(bool)typeof(RiggedPlayerAvatar).GetField("strikeRebounding",avatarFlags)
                    .GetValue(avatar),
                "A later collision cannot rebound a strike that already caused destruction");
            avatar.CancelRightHandStrike();
            typeof(RiggedPlayerAvatar).GetField("striking",avatarFlags).SetValue(avatar,true);
            var speedSource=new GameObject("Strike mouse speed source");
            speedSource.transform.SetParent(root.transform);
            speedSource.SetActive(false);
            var strikePlayer=speedSource.AddComponent<FirstPersonController>();
            typeof(RiggedPlayerAvatar).GetField("player",avatarFlags)
                .SetValue(avatar,strikePlayer);
            var lookSpeed=typeof(FirstPersonController).GetField(
                "<RawLookSpeedDegreesPerSecond>k__BackingField",avatarFlags);
            lookSpeed.SetValue(strikePlayer,1000f);
            var linearVelocity=typeof(PickableObject).GetField("heldLinearVelocity",avatarFlags);
            var angularVelocity=typeof(PickableObject).GetField("heldAngularVelocity",avatarFlags);
            linearVelocity.SetValue(heldPickup,Vector3.zero);
            angularVelocity.SetValue(heldPickup,Vector3.up*30f);
            Require(heldPickup.HeldSpeed>RiggedPlayerAvatar.MinimumReboundSpeed,
                "Rotation contributes the moving tip's speed even when the object centre is stationary");
            angularVelocity.SetValue(heldPickup,Vector3.zero);
            var carrierReference=typeof(PickableObject).GetField("heldForwardReference",avatarFlags);
            carrierReference.SetValue(heldPickup,speedSource.transform);
            speedSource.GetComponent<Rigidbody>().linearVelocity=Vector3.right*6f;
            linearVelocity.SetValue(heldPickup,Vector3.right*6f);
            Quaternion measuredPose=heldPickup.transform.rotation;
            Require(Mathf.Abs(heldPickup.HeldSpeed-6f)<0.001f&&
                heldPickup.transform.rotation==measuredPose&&
                speedSource.GetComponent<Rigidbody>().linearVelocity==Vector3.right*6f,
                "Reading held-object speed includes world movement and leaves the pose and carrier velocity unchanged");
            carrierReference.SetValue(heldPickup,null);
            foreach(float speed in new[]{0f,1f,RiggedPlayerAvatar.MinimumReboundSpeed})
            {
                linearVelocity.SetValue(heldPickup,Vector3.right*speed);
                Require((bool)resolve.Invoke(heldPickup,new object[]{strong.GetComponent<Collider>(),
                        strong.transform.position-Vector3.right*0.2f,Vector3.zero,null})&&
                    avatar.IsStrikeMode&&
                    !(bool)typeof(RiggedPlayerAvatar).GetField("strikeRebounding",avatarFlags)
                        .GetValue(avatar),
                    "Fast mouse input cannot rebound an object at or below the minimum rebound speed");
            }
            linearVelocity.SetValue(heldPickup,Vector3.right*(RiggedPlayerAvatar.MinimumReboundSpeed+0.01f));
            Require(avatar.IsStrikeMode&&
                !(bool)typeof(RiggedPlayerAvatar).GetField("strikeRebounding",avatarFlags).GetValue(avatar),
                "Fast waving without a collision does not bounce the hand");
            Collider solid=strong.GetComponent<Collider>();
            solid.isTrigger=true;
            Require(!(bool)resolve.Invoke(heldPickup,new object[]{solid,
                    strong.transform.position,Vector3.zero,null})&&avatar.IsStrikeMode,
                "A trigger overlap never rebounds the holding hand");
            solid.isTrigger=false;
            float heldHardness=heldPickup.GetComponent<PhysicalDestructible>().Hardness;
            foreach(float hardness in new[]{heldHardness,heldHardness-1f})
            {
                strongProperties.Hardness=hardness;
                Require((bool)resolve.Invoke(heldPickup,new object[]{solid,
                        strong.transform.position-Vector3.right*0.2f,Vector3.zero,null})&&
                    avatar.IsStrikeMode&&
                    !(bool)typeof(RiggedPlayerAvatar).GetField("strikeRebounding",avatarFlags)
                        .GetValue(avatar),
                    "A fast hit on an equally hard or softer solid does not rebound the hand");
            }
            strongProperties.Hardness=10f;
            lookSpeed.SetValue(strikePlayer,0f);
            Require((bool)resolve.Invoke(heldPickup,new object[]{strong.GetComponent<Collider>(),
                    strong.transform.position-Vector3.right*0.2f,Vector3.zero,null})&&
                avatar.IsStrikeMode&&
                (bool)typeof(RiggedPlayerAvatar).GetField("strikeRebounding",avatarFlags)
                    .GetValue(avatar),
                "A harder solid hit briefly rebounds without ending the held strike");
            var reboundPose=typeof(RiggedPlayerAvatar).GetMethod("ApplyStrikeReboundPose",avatarFlags);
            Vector3 handRest=(Vector3)typeof(RiggedPlayerAvatar).GetField("RightHandPointOnElbow",
                System.Reflection.BindingFlags.Static|avatarFlags).GetValue(null);
            typeof(RiggedPlayerAvatar).GetField("rightElbowRestPosition",avatarFlags)
                .SetValue(avatar,Vector3.forward*.4f);
            foreach(float speed in new[]{4f,12f,60f})
            {
                avatar.CancelRightHandStrike();
                shoulder.localPosition=Vector3.zero;shoulder.localRotation=Quaternion.identity;
                elbow.localPosition=Vector3.forward*.4f;elbow.localRotation=Quaternion.identity;
                hand.localPosition=handRest;
                typeof(RiggedPlayerAvatar).GetField("striking",avatarFlags).SetValue(avatar,true);
                typeof(RiggedPlayerAvatar).GetField("strikeAttackHeld",avatarFlags).SetValue(avatar,true);
                Vector3 start=hand.position;
                Quaternion savedShoulder=shoulder.localRotation,savedElbow=elbow.localRotation,
                    savedWrist=hand.localRotation;
                avatar.ReboundHeldStrike(heldPickup,Vector3.right,speed);
                Require(((Vector3)typeof(RiggedPlayerAvatar).GetField("strikeReboundVelocity",avatarFlags)
                    .GetValue(avatar)).magnitude<=6.001f,
                    "Rebound velocity is capped even for a fast collision");
                foreach(float elapsed in new[]{0f,.0001f,.02f,.04f,.119f,.12f,.121f,.2f})
                {
                    reboundPose.Invoke(avatar,new object[]{shoulder,elbow,hand,elapsed});
                    Require(Vector3.Distance(hand.position,start)<.25f&&
                        Vector3.Distance(shoulder.localPosition,Vector3.zero)<.0001f&&
                        Vector3.Distance(elbow.localPosition,Vector3.forward*.4f)<.0001f&&
                        Vector3.Distance(hand.localPosition,handRest)<.0001f,
                        "Rebound travel is bounded and all arm joints keep their local anchors");
                    Require((bool)typeof(RiggedPlayerAvatar).GetField("strikeRebounding",avatarFlags).GetValue(avatar)==(elapsed<.12f),
                        "Every rebound lasts exactly 0.12 seconds regardless of collision speed");
                }
                Require(avatar.IsStrikeMode&&
                    (bool)typeof(RiggedPlayerAvatar).GetField("strikeAttackHeld",avatarFlags).GetValue(avatar)&&
                    Quaternion.Angle(shoulder.localRotation,savedShoulder)<.01f&&
                    Quaternion.Angle(elbow.localRotation,savedElbow)<.01f&&
                    Quaternion.Angle(hand.localRotation,savedWrist)<.01f,
                    "Rebound restores every joint direction and continues the held strike");
            }
            avatar.CancelRightHandStrike();
            typeof(RiggedPlayerAvatar).GetField("striking",avatarFlags).SetValue(avatar,true);
            Vector3 relativeStart=hand.position;
            linearVelocity.SetValue(heldPickup,Vector3.zero);
            Require((bool)resolve.Invoke(heldPickup,new object[]{solid,strong.transform.position,
                    Vector3.left*4,(Vector3?)(Vector3.right*8)}),"Moving-target rebound resolves");
            reboundPose.Invoke(avatar,new object[]{shoulder,elbow,hand,.04f});
            Require(Vector3.Distance(hand.position,relativeStart)<.25f&&
                ((Vector3)typeof(RiggedPlayerAvatar).GetField("strikeReboundVelocity",avatarFlags)
                    .GetValue(avatar)).magnitude<=6.001f,
                "Moving-target relative contact velocity still respects the rebound cap");
            avatar.CancelRightHandStrike();
            typeof(RiggedPlayerAvatar).GetField("striking",avatarFlags).SetValue(avatar,true);
            strongProperties.IsTerrain=true;
            Require((bool)resolve.Invoke(heldPickup,new object[]{strong.GetComponent<Collider>(),
                    strong.transform.position-Vector3.right*0.2f,Vector3.zero,
                    (Vector3?)Vector3.right})&&
                (bool)typeof(RiggedPlayerAvatar).GetField("strikeEnding",avatarFlags)
                    .GetValue(avatar)&&
                !(bool)typeof(RiggedPlayerAvatar).GetField("strikeRebounding",avatarFlags)
                    .GetValue(avatar),
                "A held object returns normally instead of rebounding from terrain");
            strongProperties.IsTerrain=false;
            UnityEngine.Object.DestroyImmediate(heldBar);
            avatar.CancelRightHandStrike();
            {
                actor.transform.position=strong.transform.position-Vector3.right*0.2f;
                var fragileHeld=Cube(root,"Physical validation Fragile Held Strike",0.25f);
                // A compact 0.25 m cube is below the fragmentation-volume gate.
                // Use a real slender stick so destruction is eligible here.
                fragileHeld.transform.localScale=new Vector3(0.08f,0.08f,0.6f);
                fragileHeld.transform.position=hand.position;
                PhysicalDestructible.Ensure(fragileHeld).ApplyMaterial("Wood");
                var fragilePickup=fragileHeld.AddComponent<PickableObject>();
                Require(fragilePickup.PickUp(hand),"A fragile strike object can be held");
                typeof(RiggedPlayerAvatar).GetField("heldRightHandItem",avatarFlags)
                    .SetValue(avatar,fragilePickup);
                typeof(RiggedPlayerAvatar).GetField("striking",avatarFlags).SetValue(avatar,true);
                var fragileProperties=fragilePickup.GetComponent<PhysicalDestructible>();
                // Pickup installs a 0.25 s geometry cooldown. Editor tests do
                // not advance Time.time, so expire it before testing an impact.
                typeof(PhysicalDestructible).GetField("nextImpact",avatarFlags)
                    .SetValue(fragileProperties,-1f);
                bool fragileResolved=(bool)resolve.Invoke(fragilePickup,new object[]{strong.GetComponent<Collider>(),
                        fragileHeld.transform.position,Vector3.zero,
                        (Vector3?)(Vector3.right*30f)});
                Require(fragileResolved&&
                    Fragments("Physical validation Fragile Held Strike").Length>0&&
                    !(bool)typeof(RiggedPlayerAvatar).GetField("strikeRebounding",avatarFlags)
                        .GetValue(avatar),
                    $"A stronger target destroys the held strike object before a fast-wave rebound (resolved={fragileResolved}, fragments={Fragments("Physical validation Fragile Held Strike").Length})");
                foreach(var fragment in Fragments("Physical validation Fragile Held Strike"))
                    UnityEngine.Object.DestroyImmediate(fragment.gameObject);
                if(fragileHeld!=null) UnityEngine.Object.DestroyImmediate(fragileHeld);
            }
            UnityEngine.Object.DestroyImmediate(actor);
            foreach(var fragment in Fragments(weakHeldName))
                UnityEngine.Object.DestroyImmediate(fragment.gameObject);
            if(weakHeld!=null) UnityEngine.Object.DestroyImmediate(weakHeld);
            UnityEngine.Object.DestroyImmediate(strong);
            UnityEngine.Object.DestroyImmediate(held);
        }
        internal static void ValidateSlowStrikeContact(GameObject root)
        {
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            foreach(bool left in new[]{false,true}) {
                var actor=new GameObject("Slow strike actor");actor.transform.SetParent(root.transform);
                actor.transform.position=new Vector3(left?310:300,100,0);
                var avatar=actor.AddComponent<RiggedPlayerAvatar>();
                var shoulder=new GameObject("Shoulder").transform;shoulder.SetParent(actor.transform,false);
                var elbow=new GameObject("Elbow").transform;elbow.SetParent(shoulder,false);
                var hand=new GameObject("Hand").transform;hand.SetParent(elbow,false);
                void Set(string field,object value)=>typeof(RiggedPlayerAvatar).GetField(field,flags).SetValue(avatar,value);
                string side=left?"left":"right";
                Set(side+"Shoulder",shoulder);Set(side+"Elbow",elbow);Set(side+"HandMarker",hand);Set("strikeUsingLeftHand",left);
                var held=GameObject.CreatePrimitive(PrimitiveType.Cube);held.name="Slow held bar";
                held.transform.position=hand.position;held.transform.localScale=new Vector3(.1f,.1f,.8f);
                PhysicalDestructible.Ensure(held).ApplyMaterial("Wood");
                var pickup=held.AddComponent<PickableObject>();pickup.ForceRegularGrip=true;
                Require(pickup.PickUp(hand),"Slow-strike fixture can pick up the bar");
                Set(left?"heldLeftHandItem":"heldRightHandItem",pickup);
                Vector3 grip=held.transform.InverseTransformPoint(hand.position),localGripPose=held.transform.localPosition;
                var shape=held.GetComponents<Collider>().First(c=>c.enabled&&!c.isTrigger);
                var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.SetParent(root.transform);
                var obstacle=wall.GetComponent<Collider>();
                void Reset() {
                    avatar.CancelRightHandStrike();elbow.localPosition=Vector3.zero;elbow.localRotation=Quaternion.identity;
                    shoulder.localRotation=Quaternion.identity;hand.localRotation=Quaternion.identity;
                    held.transform.localPosition=localGripPose;held.transform.localRotation=Quaternion.identity;
                    obstacle.isTrigger=false;wall.transform.position=actor.transform.position+Vector3.up*10;
                    pickup.SampleHeldMotion(.02f);Physics.SyncTransforms();Set("striking",true);
                }
                float Depth() {
                    Physics.SyncTransforms();return Physics.ComputePenetration(shape,shape.transform.position,shape.transform.rotation,
                        obstacle,obstacle.transform.position,obstacle.transform.rotation,out _,out float d)?d:0;
                }
                Reset();
                wall.transform.localScale=new Vector3(2,2,.02f);
                wall.transform.position=new Vector3(held.transform.position.x,held.transform.position.y,shape.bounds.max.z+.04f);
                Vector3 before=held.transform.position;
                hand.position+=Vector3.forward*.08f;pickup.SampleHeldMotion(.08f);
                Require(Depth()<=.00021f&&held.transform.position.z>before.z&&held.transform.position.z<before.z+.08f,
                    $"Slow translation stops at the solid surface with either holding hand (left={left}, depth={Depth()}, start={before}, end={held.transform.position})");
                for(int i=0;i<20;i++) {
                    Vector3 old=held.transform.position;hand.position+=Vector3.forward*.01f;pickup.SampleHeldMotion(.02f);
                    float advance=held.transform.position.z-old.z;
                    Require(advance>=-.00001f&&advance<=.01001f,
                        $"Solid contact bounds each 1 cm step without reversing the held item (left={left}, step={i}, advance={advance}, depth={Depth()})");
                }
                Require(Depth()<=.00021f,$"Repeated slow pressure cannot creep deeper into a collider (left={left}, depth={Depth()}, hand={hand.position}, held={held.transform.position})");
                Require(Vector3.Distance(held.transform.TransformPoint(grip),hand.position)<.0003f,
                    "Collision stopping keeps the holding hand attached to its grip");
                before=held.transform.position;hand.position-=Vector3.forward*.03f;pickup.SampleHeldMotion(.1f);
                Require(held.transform.position.z<before.z-.029f&&Depth()==0,"Pulling away releases the contact immediately");

                Reset();wall.transform.localScale=new Vector3(.02f,2,.3f);
                wall.transform.position=held.transform.position+new Vector3(.14f,0,.3f);
                Quaternion original=held.transform.rotation;
                hand.rotation=Quaternion.AngleAxis(30,Vector3.up)*hand.rotation;pickup.SampleHeldMotion(1f);
                float turned=Quaternion.Angle(original,held.transform.rotation);
                Require(Depth()<=.00021f&&turned>1&&turned<29.9f,"Slow angular motion stops the bar before rotating through the solid");
                hand.rotation=Quaternion.AngleAxis(-5,Vector3.up)*hand.rotation;pickup.SampleHeldMotion(.2f);
                Require(Quaternion.Angle(original,held.transform.rotation)<turned-4.9f,"Rotation away from contact remains free");

                Reset();wall.transform.localScale=new Vector3(.005f,2,2);
                wall.transform.position=held.transform.position+Vector3.right*.2f;before=held.transform.position;
                hand.position+=Vector3.right*.6f;pickup.SampleHeldMotion(1f);
                Require(Depth()<=.00021f&&held.transform.position.x<before.x+.2f,
                    "Slow movement cannot cross a thin wall even when the requested end pose is beyond it");

                Reset();wall.transform.localScale=new Vector3(.02f,2,2);
                wall.transform.position=held.transform.position+Vector3.right*.2f;obstacle.isTrigger=true;before=held.transform.position;
                hand.position+=Vector3.right*.6f;pickup.SampleHeldMotion(1f);
                Require(Mathf.Abs(held.transform.position.x-before.x-.6f)<.0003f,"Triggers do not stop slow strikes");

                Reset();wall.transform.position=held.transform.position+Vector3.right*.2f;before=held.transform.position;
                hand.position+=Vector3.right*.6f;pickup.SampleHeldMotion(.001f);
                Require(Depth()<=.00021f&&held.transform.position.x<before.x+.2f,"Fast motion also stops smoothly at an indestructible collider");
                Reset();PhysicalDestructible.Ensure(wall).ApplyMaterial("Wood");
                wall.transform.position=held.transform.position+Vector3.right*.2f;before=held.transform.position;
                hand.position+=Vector3.right*.6f;pickup.SampleHeldMotion(.001f);
                Require(Mathf.Abs(held.transform.position.x-before.x-.6f)<.0003f,"Destructible fast impacts retain their damage and rebound handling");

                Reset();PhysicalDestructible.Ensure(wall).ApplyMaterial("Wood");
                wall.transform.position=held.transform.position+Vector3.right*.1f;before=held.transform.position;
                hand.position+=Vector3.right*.08f;pickup.SampleHeldMotion(.016f);
                Require(Depth()<=.00021f&&held.transform.position.x<before.x+.079f,
                    "Contact stops non-destructive material impacts above 3 m/s but below the material damage threshold");

                Reset();avatar.MarkStrikeDestruction();
                wall.transform.position=held.transform.position+Vector3.right*.1f;before=held.transform.position;
                hand.position+=Vector3.right*.08f;pickup.SampleHeldMotion(.016f);
                Require(Depth()<=.00021f&&held.transform.position.x<before.x+.079f,
                    "An earlier destruction or host-pending strike cannot bypass subsequent slow solid contact");

                Reset();var hard=PhysicalDestructible.Ensure(wall);hard.Hardness=10;hard.Resilience=10;
                wall.transform.localScale=new Vector3(2,2,.02f);
                wall.transform.position=new Vector3(held.transform.position.x,held.transform.position.y,shape.bounds.max.z+.04f);
                pickup.CaptureStrikeGrip();before=held.transform.position;
                hand.position+=Vector3.forward*.08f;pickup.UpdateHeldPose();
                Vector3 eased=held.transform.position;
                Require(Depth()<=.00021f&&eased.z>before.z&&eased.z<before.z+.08f&&
                    !(bool)typeof(RiggedPlayerAvatar).GetField("strikeRebounding",flags).GetValue(avatar),
                    "Rendered held pose brakes before an indestructible impact can trigger abrupt rebound");
                pickup.SampleHeldMotion(.08f);
                Require(Vector3.Distance(held.transform.position,eased)<.0003f,"Final motion sampling does not apply the soft brake twice");

                var input=new GameObject("Contact animation input");input.transform.SetParent(root.transform);input.SetActive(false);
                Set("player",input.AddComponent<FirstPersonController>());
                var camera=new GameObject("Contact animation camera").transform;camera.SetParent(root.transform);camera.position=actor.transform.position;
                Set("cameraTransform",camera);Set(side+"ShoulderRestRotation",Quaternion.identity);Set(side+"ElbowRestRotation",Quaternion.identity);
                Set(side+"ElbowRestScale",Vector3.one);Set(side+"ElbowRestPosition",Vector3.forward*.25f);
                Vector3 stoppedElbow=elbow.localPosition;
                typeof(RiggedPlayerAvatar).GetMethod("ApplyRightHandStrikePose",flags).Invoke(avatar,null);
                Require(Vector3.Distance(elbow.localPosition,stoppedElbow)<.00001f,
                    "The following animated frame cannot re-extend a stopped arm into contact and prevent withdrawal");
            }
            Debug.Log("SLOW_STRIKE_CONTACT_VALIDATION_PASS: both hands, translation and rotation, no sustained-pressure creep, immediate withdrawal, retained grip, thin-wall crossing, trigger exclusion, progressive braking at indestructible surfaces and destructible fast-impact preservation.");
        }

        static void ValidateSurfaceGuard(GameObject root)
        {
            var surface=GameObject.CreatePrimitive(PrimitiveType.Cube);
            surface.transform.SetParent(root.transform);
            surface.transform.position=new Vector3(1000f,1000f,1000f);
            surface.transform.localScale=new Vector3(4f,1f,4f);
            var dropped=GameObject.CreatePrimitive(PrimitiveType.Cube);
            dropped.transform.SetParent(root.transform);
            dropped.transform.position=new Vector3(1000f,1000.6f,1000f);
            dropped.transform.localScale=Vector3.one*0.5f;
            var body=dropped.AddComponent<Rigidbody>();
            var guard=dropped.AddComponent<DynamicObjectSurfaceGuard>();
            typeof(DynamicObjectSurfaceGuard).GetMethod("Awake",
                System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)
                .Invoke(guard,null);
            Physics.SyncTransforms();
            Require(guard.ResolveStaticOverlap()&&body.position.y>1000.74f,
                "A dropped object embedded in a larger static collider is lifted clear");
            UnityEngine.Object.DestroyImmediate(dropped);
            UnityEngine.Object.DestroyImmediate(surface);
        }

        static void ValidateHeldFragmentRetention(GameObject root)
        {
            var hand=new GameObject("Physical validation held hand");
            hand.transform.SetParent(root.transform);
            hand.transform.position=new Vector3(2000f,1000f,0f);
            var source=Cube(root,"Physical validation held fracture",0.2f);
            source.transform.position=hand.transform.position;
            var properties=PhysicalDestructible.Ensure(source);
            properties.MaterialName="Wood";
            properties.Hardness=2f;
            properties.Resilience=3f;
            var pickup=source.AddComponent<PickableObject>();
            Require(pickup.PickUp(hand.transform),"Validation object can be held");
            // The pickup cooldown would have elapsed before a gameplay strike;
            // Time.time stays fixed throughout this edit-mode validation.
            typeof(PhysicalDestructible).GetField("nextImpact",
                System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)
                .SetValue(properties,-1f);
            Require(properties.ApplyImpact(6f,3f,1f,source.transform.position,
                Vector3.up,30f),"Held source fractures");
            var fragments=Fragments("Physical validation held fracture");
            Require(fragments.Length>=2&&
                fragments.Count(fragment=>fragment.GetComponent<PickableObject>()?.IsHeld==true)==1&&
                fragments.Single(fragment=>fragment.GetComponent<PickableObject>()?.IsHeld==true)
                    .transform.parent==hand.transform,
                "Exactly one fragment remains attached to the hand after destruction");
            foreach(var fragment in fragments)
                UnityEngine.Object.DestroyImmediate(fragment.gameObject);
            UnityEngine.Object.DestroyImmediate(hand);
        }

        static void ValidateUndersizedFragmentCollision(GameObject root)
        {
            var small=Cube(root,"Physical validation Transient Small Fragment",1f);
            small.transform.localScale=new Vector3(0.03f,0.03f,0.3f);
            var large=Cube(root,"Physical validation Transient Large Fragment",0.3f);
            var smallProperties=PhysicalDestructible.Ensure(small);
            var largeProperties=PhysicalDestructible.Ensure(large);
            var smallFragment=small.AddComponent<DestructionFragment>();
            var largeFragment=large.AddComponent<DestructionFragment>();
            // Edit-mode components need the same collider registration that
            // Awake performs automatically in the running game.
            var fragmentAwake=typeof(DestructionFragment).GetMethod("Awake",
                System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
            fragmentAwake.Invoke(smallFragment,null);fragmentAwake.Invoke(largeFragment,null);
            Require(smallProperties.SolidVolumeCubicMetres<PhysicalDestructible.MinimumFragmentationVolume&&
                largeProperties.SolidVolumeCubicMetres>=PhysicalDestructible.MinimumFragmentationVolume,
                "The collision sample straddles the minimum destructible volume");
            var collisionMethod=typeof(PhysicalDestructible).GetMethod("TrySpecialFragmentCollision",
                System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
            Require((bool)collisionMethod.Invoke(smallProperties,new object[]{largeProperties,
                small.transform.position,Vector3.up,10f}),
                "Colliding fragments trigger special Shatter for the undersized one");
            var debris=Fragments("Physical validation Transient Small Fragment");
            Require(debris.Length>=2&&debris.All(piece=>piece.GetComponent<TemporaryShatterDebris>()!=null),
                "The undersized fragment is completely replaced with persistent Shatter pieces");
            foreach(var piece in debris)
            {
                var temporary=piece.GetComponent<TemporaryShatterDebris>();
                typeof(TemporaryShatterDebris).GetMethod("Awake",
                    System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)
                    .Invoke(temporary,null);
                Require(Physics.GetIgnoreCollision(piece.GetComponent<Collider>(),large.GetComponent<Collider>()),
                    "Small Shatter pieces keep ignoring other fragments");
                var rest=piece.GetComponent<SmallFragmentRest>();
                var body=piece.GetComponent<Rigidbody>();body.WakeUp();
                body.linearVelocity=Vector3.right;body.angularVelocity=Vector3.zero;
                Vector3 scale=piece.transform.localScale;
                var material=piece.GetComponent<MeshRenderer>().sharedMaterial;
                for(int step=0;step<100;step++) rest.CheckRest(.1f);
                Require(piece!=null&&!rest.IsSettled&&piece.transform.localScale==scale&&
                    piece.GetComponent<MeshRenderer>().sharedMaterial==material,
                    "Moving debris remains opaque and full size beyond the old expiry");
                body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;
                rest.CheckRest(.1f);rest.CheckRest(.1f);
                Require(piece!=null&&rest.IsSettled&&piece.GetComponent<Rigidbody>()==null&&
                    piece.GetComponentsInChildren<Collider>().Length==0&&
                    piece.GetComponent<MeshRenderer>().sharedMaterial==material&&piece.transform.localScale==scale,
                    "Stopped debris retains its mesh and appearance after only physics is removed");
                UnityEngine.Object.DestroyImmediate(piece.gameObject);
            }
            var compact=Cube(root,"Physical validation Compact Small Fragment",0.11f);
            var compactProperties=PhysicalDestructible.Ensure(compact);
            compact.AddComponent<DestructionFragment>();
            Require(!(bool)collisionMethod.Invoke(compactProperties,new object[]{largeProperties,
                    compact.transform.position,Vector3.up,30f})&&
                !compactProperties.ApplyImpact(10f,10f,1f,compact.transform.position,
                    Vector3.up,30f)&&!compactProperties.HasBeenDestroyed,
                "A small compact fragment stays intact after a strong collision");
            foreach(bool terrain in new[]{false,true})
            {
                string name=terrain?"Physical validation Tiny Terrain Hit":
                    "Physical validation Tiny Rock Hit";
                var tiny=Cube(root,name,1f);
                tiny.transform.localScale=new Vector3(0.03f,0.03f,0.3f);
                var target=Cube(root,name+" target",0.3f);
                var tinyProperties=PhysicalDestructible.Ensure(tiny);
                var targetProperties=PhysicalDestructible.Ensure(target);
                targetProperties.IsTerrain=terrain;
                tiny.AddComponent<DestructionFragment>();
                Require((bool)collisionMethod.Invoke(tinyProperties,new object[]{targetProperties,
                    tiny.transform.position,Vector3.up,10f})&&Fragments(name).Length>=2,
                    "An undersized fragment Shatters against "+(terrain?"terrain":"an ordinary rock"));
                foreach(var piece in Fragments(name)) UnityEngine.Object.DestroyImmediate(piece.gameObject);
            }
            var branch=Cube(root,"Physical validation Tiny Branch Fragment",1f);
            string branchName=branch.name;
            branch.transform.localScale=new Vector3(0.03f,0.03f,0.3f);
            var branchProperties=PhysicalDestructible.Ensure(branch);
            branchProperties.ApplyMaterial("Wood");
            branch.AddComponent<DestructionFragment>().IsBranchFragment=true;
            var weak=Cube(root,"Physical validation Weak Branch Impactor",0.3f);
            var weakProperties=PhysicalDestructible.Ensure(weak);
            weakProperties.Hardness=4.9f;
            Require((bool)collisionMethod.Invoke(branchProperties,new object[]{weakProperties,
                branch.transform.position,Vector3.up,30f})&&
                !branchProperties.ApplyImpact(4.9f,weakProperties.Resilience,0.3f,
                    branch.transform.position,Vector3.up,30f)&&
                !branchProperties.HasBeenDestroyed&&Fragments(branchName).Length==0,
                "An undersized branch fragment ignores impacts below twice its hardness");
            weakProperties.Hardness=10f;
            Require((bool)collisionMethod.Invoke(branchProperties,new object[]{weakProperties,
                branch.transform.position,Vector3.up,1f})&&
                !branchProperties.HasBeenDestroyed&&Fragments(branchName).Length==0,
                "Maximum hardness still needs sufficient impact speed");
            Require((bool)collisionMethod.Invoke(branchProperties,new object[]{weakProperties,
                branch.transform.position,Vector3.up,30f})&&
                branchProperties.HasBeenDestroyed&&Fragments(branchName).Length>=2&&
                Fragments(branchName).All(piece=>piece.GetComponent<TemporaryShatterDebris>()!=null),
                "A qualifying high-speed impact replaces the small branch with fading debris");
            foreach(var piece in Fragments(branchName)) UnityEngine.Object.DestroyImmediate(piece.gameObject);
        }
        static void ValidateProgressiveFragments(GameObject root)
        {
            Require(Mathf.Approximately(PhysicalDestructible.FragmentGrowthAfterBreak(0f,0f),0.005f)&&
                Mathf.Approximately(PhysicalDestructible.FragmentGrowthAfterBreak(0f,1f),0.025f)&&
                Mathf.Approximately(PhysicalDestructible.FragmentGrowthAfterBreak(0.299f,1f),0.3f)&&
                Mathf.Approximately(PhysicalDestructible.FragmentGrowthAfterBreak(0.3f,0f),0.3f),
                "Growth adds 0.5 to 2.5 percentage points and never exceeds 30 percent total");
            Require(PhysicalDestructible.CollisionMassSizeGrowth(-1f)==0f&&
                PhysicalDestructible.CollisionMassSizeGrowth(0f)==0f&&
                Mathf.Approximately(PhysicalDestructible.CollisionMassSizeGrowth(1f),0.0002f)&&
                Mathf.Approximately(PhysicalDestructible.CollisionMassSizeGrowth(500f),0.1f)&&
                Mathf.Approximately(PhysicalDestructible.CollisionMassSizeGrowth(1000f),0.2f)&&
                Mathf.Approximately(PhysicalDestructible.CollisionMassSizeGrowth(10000f),0.2f),
                "Other-object mass adds 0.02 percent per kilogram and caps at 20 percent at 1000 kg");
            float cappedGrowth=0f;
            for(int hit=0;hit<100;hit++) cappedGrowth=PhysicalDestructible.FragmentGrowthAfterBreak(cappedGrowth,0f);
            Require(Mathf.Approximately(cappedGrowth,0.3f),"Repeated minimum increases reach and retain the cap");
            foreach(string material in new[]{"Stone","Wood","Limestone"})
            {
                var source=Cube(root,"Physical validation Progressive "+material,4f);
                var properties=PhysicalDestructible.Ensure(source);
                properties.ApplyMaterial(material);
                var growthField=typeof(PhysicalDestructible).GetField("fragmentSizeGrowth",
                    System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                growthField.SetValue(properties,0.4f);
                Require(Mathf.Approximately(properties.FragmentSizeMultiplierForImpact(1000f),1.5f)&&
                    Mathf.Approximately(properties.FragmentSizeMultiplierForImpact(0f),1.3f),
                    "Old history is clamped to 30 percent and the independent mass bonus does not persist");
                growthField.SetValue(properties,0f);
                float previousMultiplier=1f;
                double initialVolume=properties.SolidVolumeCubicMetres,totalEmitted=0d;
                for(int hit=0;hit<3;hit++)
                {
                    // Exercise both effects against one retained object, avoiding
                    // the cooldown without advancing the editor's clock.
                    typeof(PhysicalDestructible).GetField("nextImpact",
                        System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)
                        .SetValue(properties,0f);
                    Vector3 direction=hit==0?Vector3.up:hit==1?Vector3.right:Vector3.forward;
                    Require(properties.ApplyImpact(20f,properties.Resilience+(hit==1?3f:0f),
                        2.01f,direction*2f,direction,50f),
                        "Repeated Fracture/Shatter remains possible for "+material+" hit "+hit);
                    var pieces=Fragments(source.name);
                    Require(properties.LastEffect==(hit==1?PhysicalDestructEffect.Shatter:PhysicalDestructEffect.Fracture),
                        "Fragment growth history is shared by both effects");
                    Require(pieces.Length>0&&properties.FragmentationCount==hit+1&&
                        properties.FragmentSizeMultiplier>=previousMultiplier+0.005f-0.000001f&&
                        properties.FragmentSizeMultiplier<=previousMultiplier+0.025f+0.000001f&&
                        properties.FragmentSizeMultiplier<=1.3f,
                        "Successful breaks increase the shared fragment-size history");
                    float emitted=0f;
                    foreach(var piece in pieces)
                    {
                        float volume=(float)piece.SolidVolumeCubicMetres;
                        Require(piece.FragmentationCount==properties.FragmentationCount&&
                            Mathf.Approximately(piece.FragmentSizeMultiplier,properties.FragmentSizeMultiplier),
                            "Every new piece inherits capped growth history");
                        emitted+=volume;
                        UnityEngine.Object.DestroyImmediate(piece.gameObject);
                    }
                    previousMultiplier=properties.FragmentSizeMultiplier;
                    totalEmitted+=emitted;
                    Require(Math.Abs(properties.SolidVolumeCubicMetres+totalEmitted-initialVolume)<0.01d,
                        "Progressively larger fragments conserve material volume");
                }
                UnityEngine.Object.DestroyImmediate(source);
            }
        }
        static void ValidateFragmentReimpact(GameObject root)
        {
            foreach(string material in new[]{"Stone","Wood","Limestone"})
            {
                var source=Cube(root,"Physical validation Reimpact "+material,1f);
                var target=PhysicalDestructible.Ensure(source);
                target.ApplyMaterial(material);
                var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                typeof(PhysicalDestructible).GetField("fragmentationCount",flags).SetValue(target,20);
                typeof(PhysicalDestructible).GetField("fragmentSizeGrowth",flags).SetValue(target,0.3f);
                for(int hit=0;hit<3;hit++)
                {
                    string sourceName=target.name;
                    double volumeBefore=target.SolidVolumeCubicMetres;
                    typeof(PhysicalDestructible).GetField("nextImpact",flags).SetValue(target,0f);
                    Require(target.ApplyImpact(20f,target.Resilience,10f,
                        target.GetComponent<Renderer>().bounds.center,Vector3.up,50f,otherMassKg:1000f),
                        "Eligible "+material+" fragments remain destructible at both growth caps, hit "+hit);
                    var pieces=Fragments(sourceName);
                    Require(pieces.Length==2&&pieces.All(piece=>piece.FragmentationCount==21+hit)&&
                        Math.Abs(pieces.Sum(piece=>piece.SolidVolumeCubicMetres)-volumeBefore)<0.0001d,
                        "Repeated fragment breaks inherit history and conserve volume");
                    target=pieces.OrderByDescending(piece=>piece.SolidVolumeCubicMetres).First();
                    foreach(var piece in pieces) if(piece!=target) UnityEngine.Object.DestroyImmediate(piece.gameObject);
                }
                UnityEngine.Object.DestroyImmediate(target.gameObject);
            }
        }
        static GameObject Cube(GameObject root,string name,float size)
        {
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name=name;
            cube.transform.SetParent(root.transform); cube.transform.localScale=Vector3.one*size; return cube;
        }
        static float Volume(Mesh mesh)
        {
            var p=mesh.vertices; var t=mesh.triangles; float value=0;
            for(int i=0;i<t.Length;i+=3) value+=Vector3.Dot(p[t[i]],Vector3.Cross(p[t[i+1]],p[t[i+2]]))/6;
            return Mathf.Abs(value);
        }
        static void Centered(PhysicalDestructible piece)
        {
            Require(DestructionMesh.VolumeCentroid(piece.GetComponent<MeshFilter>().sharedMesh)
                .sqrMagnitude<1e-6f&&Vector3.Distance(piece.GetComponent<Rigidbody>().worldCenterOfMass,
                    piece.transform.TransformPoint(piece.GetComponent<MeshFilter>().sharedMesh.bounds.center))<.0001f,
                "Fragment pivot preserves volume centering and Rigidbody center matches the detached mesh center");
        }
        static bool SameBounds(Bounds a,Bounds b,float tolerance) =>
            Vector3.Distance(a.min,b.min)<tolerance&&Vector3.Distance(a.max,b.max)<tolerance;
        static bool WithinBounds(Bounds inner,Bounds outer)
        {
            const float tolerance=0.00002f;
            return inner.min.x>=outer.min.x-tolerance&&inner.max.x<=outer.max.x+tolerance&&
                inner.min.y>=outer.min.y-tolerance&&inner.max.y<=outer.max.y+tolerance&&
                inner.min.z>=outer.min.z-tolerance&&inner.max.z<=outer.max.z+tolerance;
        }
        static void CheckAppearance(MeshRenderer renderer,Mesh mesh,Material first,Material second,
            Color vertexColor,Color overallTint,Color secondTint)
        {
            Require(renderer.sharedMaterials.Length==2&&renderer.sharedMaterials[0]==first&&
                renderer.sharedMaterials[1]==second,"Source material slots are preserved");
            Require(mesh.colors.Length==mesh.vertexCount&&
                mesh.colors.All(c=>CloseColor(c,vertexColor)),"Source vertex color is preserved");
            var block=new MaterialPropertyBlock(); renderer.GetPropertyBlock(block,0);
            Require(CloseColor(block.GetColor("_Color"),overallTint),
                "Renderer-wide color override survives on the first material slot");
            renderer.GetPropertyBlock(block,1);
            Require(CloseColor(block.GetColor("_Color"),secondTint),
                "Per-material color override survives on the second slot");
        }
        static bool CloseColor(Color a,Color b) =>
            Mathf.Abs(a.r-b.r)<0.001f&&Mathf.Abs(a.g-b.g)<0.001f&&
            Mathf.Abs(a.b-b.b)<0.001f&&Mathf.Abs(a.a-b.a)<0.001f;
        static void Closed(Mesh mesh)
        {
            Require(DestructionMesh.IsClosedConnected(mesh),
                "Every fragment face and edge belongs to one closed, connected shell");
            var points=mesh.vertices; var indices=mesh.triangles;
            var edges=new Dictionary<(Vector3Int,Vector3Int),int>();
            Vector3Int Key(Vector3 p)=>new(Mathf.RoundToInt(p.x*10000),Mathf.RoundToInt(p.y*10000),Mathf.RoundToInt(p.z*10000));
            for(int i=0;i<indices.Length;i+=3)
            {
                for(int j=0;j<3;j++)
                {
                    var a=Key(points[indices[i+j]]); var b=Key(points[indices[i+(j+1)%3]]);
                    var key=(a,b); if(!edges.ContainsKey(key)) edges[key]=0; edges[key]++;
                }
            }
            Require(edges.All(e=>edges.TryGetValue((e.Key.Item2,e.Key.Item1),out int count)&&count==e.Value),"Every mesh edge has an oppositely wound partner (closed faces)");
            Require(Volume(mesh)>0.000001f,"Fragment has nonzero volume");
        }
        static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
    }
}
