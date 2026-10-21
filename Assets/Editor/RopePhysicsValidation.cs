using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Wyvernland.Editor
{
    public static class RopePhysicsValidation
    {
        [MenuItem("Tools/Wyvernland/Validate Rope Physics")]
        public static void Run()
        {
            var root=new GameObject("Rope physics validation");
            var appearance=new Material(Resources.Load<Shader>("Clay"));
            SimulationMode simulation=Physics.simulationMode;
            bool testGround=WorldBootstrap.TestGround;
            var randomState=UnityEngine.Random.state;
            try
            {
                Physics.simulationMode=SimulationMode.Script;
                ValidateSpecies(root,appearance);
                ValidateSwingGripAndGround(root,appearance);
                ValidateLongVines(root,appearance);
                ValidateMovingSupport(root,appearance);
                ValidateGeneratedVines(root,appearance);
                Debug.Log("ROPE_PHYSICS_VALIDATION_PASS: both species and radius endpoints; uniform closed tube; material mass; Rigidbody and capsule links; swing, stable length, flexible grip, release, ground contact, moving/destroyed support, generated branch spawning and rigid-object registration isolation.");
            }
            finally
            {
                Physics.simulationMode=simulation;
                typeof(WorldBootstrap).GetProperty("TestGround").SetValue(null,testGround);
                UnityEngine.Random.state=randomState;
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(appearance);
            }
        }

        public static void RunRegression()
        {
            Run();
            var failures=new List<Exception>();
            foreach(Action validation in new Action[]{TreeBranchDestructionValidation.Run,
                TestGroundValidation.Run,PickableStickValidation.Run})
            {
                try { validation(); }
                catch(Exception failure) { Debug.LogException(failure);failures.Add(failure); }
            }
            if(failures.Count>0) throw new AggregateException("Existing compatibility checks failed",failures);
            Debug.Log("ROPE_REGRESSION_VALIDATION_PASS");
        }

        public static void CapturePreview()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity",OpenSceneMode.Single);
            typeof(WorldBootstrap).GetProperty("TestGround").SetValue(null,true);
            typeof(WorldBootstrap).GetMethod("BuildPrototype",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            var tree=UnityEngine.Object.FindObjectsByType<GeneratedTree>(FindObjectsSortMode.None)
                .Single(sample=>sample.name=="Short Tree");
            var vines=UnityEngine.Object.FindObjectsByType<RopeObject>(FindObjectsSortMode.None)
                .Where(vine=>(vine.transform.position-tree.transform.position).sqrMagnitude<80f).ToArray();
            Require(vines.Length==2,"Preview tree has both vine species");
            var camera=UnityEngine.Object.FindFirstObjectByType<FirstPersonController>().PlayerCamera;
            camera.transform.SetParent(null,true);
            Vector3 separation=vines[1].transform.position-vines[0].transform.position;
            separation.y=0f;
            Vector3 viewOffset=Vector3.Cross(separation,Vector3.up).normalized*12f;
            camera.transform.position=tree.transform.position+viewOffset+Vector3.up*4.8f;
            camera.transform.LookAt(tree.transform.position+Vector3.up*3.8f);
            camera.fieldOfView=40f;
            camera.nearClipPlane=0.05f;
            var target=new RenderTexture(1400,1000,24,RenderTextureFormat.ARGB32);
            var picture=new Texture2D(1400,1000,TextureFormat.RGB24,false);
            RenderTexture previous=RenderTexture.active;
            SimulationMode simulation=Physics.simulationMode;
            try
            {
                Physics.simulationMode=SimulationMode.Script;
                Physics.SyncTransforms();
                for(int i=0;i<vines.Length;i++)
                {
                    Rigidbody tip=vines[i].Bodies[^1];
                    tip.AddForce(separation.normalized*(i==0?-1.8f:1.8f)*tip.mass,ForceMode.Impulse);
                }
                for(int step=0;step<35;step++)
                {
                    foreach(var vine in vines) vine.StepAnchors();
                    Physics.Simulate(Time.fixedDeltaTime);
                }
                foreach(var vine in vines) vine.UpdateVisual();
                camera.targetTexture=target;
                camera.Render();
                RenderTexture.active=target;
                picture.ReadPixels(new Rect(0,0,1400,1000),0,0);
                picture.Apply();
                string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"..","Artifacts","Ropes"));
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory,"hanging-vines.png"),picture.EncodeToPNG());
                Debug.Log("ROPE_PREVIEW_CAPTURED "+directory);
            }
            finally
            {
                camera.targetTexture=null;
                Physics.simulationMode=simulation;
                RenderTexture.active=previous;
                UnityEngine.Object.DestroyImmediate(picture);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        static GameObject Branch(GameObject root,Vector3 position)
        {
            var branch=GameObject.CreatePrimitive(PrimitiveType.Cube);
            branch.name="Vine support";
            branch.transform.SetParent(root.transform);
            branch.transform.position=position;
            branch.transform.localScale=new Vector3(3f,0.16f,0.4f);
            return branch;
        }

        static void ValidateSpecies(GameObject root,Material appearance)
        {
            var branch=Branch(root,new Vector3(100f,6f,0f));
            foreach(VineKind kind in Enum.GetValues(typeof(VineKind)))
            foreach(float radius in new[]{RopeObject.RadiusRange(kind).x,RopeObject.RadiusRange(kind).y})
            {
                var rope=RopeObject.Create(root.transform,kind,radius,3f,branch.transform,
                    branch.transform.position+Vector3.down*0.1f,appearance);
                var pickup=rope.GetComponent<PickableObject>();
                Require(pickup.GrabMode==PickableGrabMode.Rope&&
                    PickableObject.ChooseGrabMode(Vector3.one,rope.MaterialName)==PickableGrabMode.Rope,
                    "Vine objects and materials select Rope independently of their shape");
                Require(rope.IsAttached&&Mathf.Abs(rope.RadiusMetres-radius)<1e-6f,
                    "Both species retain their requested radius endpoints");
                Require(rope.Bodies.Count>2&&rope.Bodies.All(body=>!body.isKinematic&&body.useGravity)&&
                    rope.Colliders.All(collider=>collider.enabled&&!collider.isTrigger&&
                        Mathf.Abs(collider.radius-radius)<1e-6f),
                    "Every rope link is a dynamic body with a uniform-radius solid collider");
                Require(Mathf.Abs(rope.Bodies.Sum(body=>body.mass)-rope.MassKg)<0.0001f&&
                    Mathf.Abs(pickup.PickupMassKg-rope.MassKg)<0.0001f,
                    "Pickup and distributed body masses use the whole vine's material volume");
                Require(Physics.GetIgnoreCollision(rope.Colliders[0],rope.Colliders[1])&&
                    !Physics.GetIgnoreCollision(rope.Colliders[0],rope.Colliders[2]),
                    "Adjacent overlap is excluded; nonadjacent links can collide with one another");
                Mesh shape=rope.GetComponent<MeshFilter>().sharedMesh;
                Require(DestructionMesh.IsClosedConnected(shape)&&
                    MeshSolidVolume.TryMeasure(shape,out double volume,out bool open)&&!open&&volume>0d,
                    "The visible tube is a closed connected solid");
                Vector3[] vertices=shape.vertices;
                for(int ring=0;ring<=rope.Bodies.Count;ring++)
                for(int side=0;side<12;side++)
                {
                    Vector3 vertex=vertices[ring*12+side];
                    Require(Mathf.Abs(new Vector2(vertex.x,vertex.z).magnitude-radius)<0.00001f,
                        "All visible rings keep the same radius, without taper");
                }
                var world=root.AddComponent<PhysicalDestructionWorld>();
                world.RegisterSceneObjects();
                Require(rope.GetComponentsInChildren<PhysicalDestructible>().Length==0&&
                    rope.GetComponentsInChildren<MeshCollider>().Length==0&&
                    rope.GetComponent<Rigidbody>()==null,
                    "Rigid destruction registration cannot turn a flexible rope into a hull");
                pickup.SetNearbyDetection(true);
                Require(pickup.IsPickupDetectionCollider(rope.Colliders[^1])&&
                    pickup.PickupDetectionTouchesSphere(rope.Bodies[^1].position,0.05f)&&
                    rope.GetComponentsInChildren<MeshCollider>().Length==0,
                    "Pickup detection uses the moving capsules without replacing them");
                UnityEngine.Object.DestroyImmediate(world);
                UnityEngine.Object.DestroyImmediate(rope.gameObject);
            }
            UnityEngine.Object.DestroyImmediate(branch);
        }

        static void Step(RopeObject rope,int count,Action<int> sample=null)
        {
            Physics.SyncTransforms();
            for(int i=0;i<count;i++)
            {
                rope.StepAnchors();
                Physics.Simulate(Time.fixedDeltaTime);
                sample?.Invoke(i);
            }
            rope.UpdateVisual();
        }

        static float JointGap(RopeObject rope)
        {
            float largest=0f;
            foreach(var body in rope.Bodies)
            foreach(var joint in body.GetComponents<ConfigurableJoint>())
            {
                if(joint.connectedBody==null) continue;
                Vector3 a=body.transform.TransformPoint(joint.anchor);
                Vector3 b=joint.connectedBody.transform.TransformPoint(joint.connectedAnchor);
                largest=Mathf.Max(largest,Vector3.Distance(a,b));
            }
            return largest;
        }

        static void ValidateSwingGripAndGround(GameObject root,Material appearance)
        {
            var branch=Branch(root,new Vector3(140f,6f,0f));
            Vector3 mount=branch.transform.position+Vector3.down*0.1f;
            var rope=RopeObject.Create(root.transform,VineKind.Black,0.025f,3f,
                branch.transform,mount,appearance);
            Rigidbody tip=rope.Bodies[^1];
            float maximumSwing=0f,maximumGap=0f;
            tip.AddForce(Vector3.right*tip.mass*4f,ForceMode.Impulse);
            Step(rope,400,_=>
            {
                maximumSwing=Mathf.Max(maximumSwing,Mathf.Abs(tip.position.x-mount.x));
                maximumGap=Mathf.Max(maximumGap,JointGap(rope));
                Require(rope.Bodies.All(body=>float.IsFinite(body.position.sqrMagnitude)),
                    "Swinging rope bodies remain finite");
            });
            Require(maximumSwing>0.15f&&maximumGap<0.04f,
                $"Impulse produces a swing while joints preserve length (swing {maximumSwing}, gap {maximumGap})");
            var hand=new GameObject("Rope grip hand").transform;
            hand.SetParent(root.transform);
            hand.position=tip.position;
            var pickup=rope.GetComponent<PickableObject>();
            Require(pickup.PickUp(hand,false,tip.position)&&pickup.IsHeld,
                "A light vine can be picked up at its aimed segment");
            Require(rope.Bodies.All(body=>!body.isKinematic&&body.useGravity)&&
                rope.transform.parent==root.transform&&rope.GetComponent<Rigidbody>()==null,
                "Holding constrains a grip point without rigidifying or parenting the chain to the hand");
            hand.position+=Vector3.right*0.35f+Vector3.up*0.35f;
            Step(rope,180);
            Require(JointGap(rope)<0.04f&&Vector3.Distance(pickup.HeldGripPoint,hand.position)<0.05f,
                $"The held grip follows the hand while the chain remains connected (gap {JointGap(rope)}, grip distance {Vector3.Distance(pickup.HeldGripPoint,hand.position)}, grip {pickup.HeldGripPoint}, hand {hand.position})");
            pickup.Release(Vector3.zero,root.transform);
            Require(!pickup.IsHeld&&rope.IsAttached&&rope.Bodies.All(body=>!body.isKinematic),
                "Opening the hand leaves an attached vine hanging and dynamic");
            var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.SetParent(root.transform);
            ground.transform.position=new Vector3(140f,-0.25f,0f);
            ground.transform.localScale=new Vector3(16f,0.5f,16f);
            rope.DetachFromBranch();
            Step(rope,800);
            Require(!rope.IsAttached&&rope.Bodies.All(body=>body.position.y>=rope.RadiusMetres-0.04f)&&
                rope.Bodies.All(body=>body.position.y<0.6f)&&JointGap(rope)<0.04f,
                "A detached rope falls, folds and rests against the ground without stretching or tunnelling");
            Debug.Log($"ROPE_SWING_METRICS: maximum tip displacement={maximumSwing:0.000} m; maximum endpoint gap={maximumGap:0.0000} m");
            UnityEngine.Object.DestroyImmediate(rope.gameObject);
            UnityEngine.Object.DestroyImmediate(branch);
            UnityEngine.Object.DestroyImmediate(ground);
            UnityEngine.Object.DestroyImmediate(hand.gameObject);
        }

        static void ValidateMovingSupport(GameObject root,Material appearance)
        {
            var branch=Branch(root,new Vector3(180f,7f,0f));
            var rope=RopeObject.Create(root.transform,VineKind.Yellow,0.05f,3f,
                branch.transform,branch.transform.position+Vector3.down*0.1f,appearance);
            branch.transform.position+=Vector3.right*0.5f;
            Step(rope,200);
            var attachment=rope.Bodies[0].GetComponent<ConfigurableJoint>();
            Vector3 mount=branch.transform.position+Vector3.down*0.1f;
            Require(Vector3.Distance(rope.Bodies[0].transform.TransformPoint(attachment.anchor),mount)<0.04f,
                "A stationary branch's moved attachment carries the vine and wakes it");
            var branchBody=branch.AddComponent<Rigidbody>();
            branchBody.useGravity=false;
            branchBody.mass=20f;
            rope.StepAnchors();
            Require(attachment.connectedBody==branchBody,
                "When the branch becomes a Rigidbody the vine connects to that actual body");
            Step(rope,180);
            Require(JointGap(rope)<0.04f&&branchBody.position.y<7f,
                "A moving branch receives the hanging vine's weight through the joint");
            UnityEngine.Object.DestroyImmediate(branch);
            float before=rope.Bodies[0].position.y;
            Step(rope,60);
            Require(!rope.IsAttached&&rope.Bodies[0].position.y<before-0.5f,
                "Destroying the support releases the vine instead of pinning it in midair");
            UnityEngine.Object.DestroyImmediate(rope.gameObject);
        }

        static void ValidateLongVines(GameObject root,Material appearance)
        {
            float largestGap=0f;
            foreach(var sample in new[]{(kind:VineKind.Black,radius:0.01f),
                (kind:VineKind.Black,radius:0.05f),(kind:VineKind.Yellow,radius:0.03f),
                (kind:VineKind.Yellow,radius:0.07f)})
            {
                var branch=Branch(root,new Vector3(260f,10f,0f));
                var rope=RopeObject.Create(root.transform,sample.kind,sample.radius,6f,
                    branch.transform,branch.transform.position+Vector3.down*0.1f,appearance);
                Rigidbody tip=rope.Bodies[^1];
                tip.AddForce(new Vector3(5f,0f,4f)*tip.mass,ForceMode.Impulse);
                Step(rope,400,_=>largestGap=Mathf.Max(largestGap,JointGap(rope)));
                Require(largestGap<0.04f&&rope.Bodies.All(body=>float.IsFinite(body.position.sqrMagnitude)),
                    $"Six-metre vines remain stable at all radius endpoints (gap {largestGap})");
                UnityEngine.Object.DestroyImmediate(rope.gameObject);
                UnityEngine.Object.DestroyImmediate(branch);
            }
            Debug.Log($"ROPE_LONG_VINE_METRICS: maximum endpoint gap={largestGap:0.0000} m at the project's fixed timestep");
        }

        static void ValidateGeneratedVines(GameObject root,Material appearance)
        {
            typeof(WorldBootstrap).GetProperty("TestGround").SetValue(null,true);
            var field=root.AddComponent<ProceduralTreeField>();
            Type seedType=typeof(ProceduralTreeField).GetNestedType("TreeSeed",BindingFlags.NonPublic);
            var trees=(IList)typeof(ProceduralTreeField).GetField("trees",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(field);
            ((List<Material>)typeof(ProceduralTreeField).GetField("materials",BindingFlags.Instance|BindingFlags.NonPublic)
                .GetValue(field)).Add(new Material(appearance));
            typeof(ProceduralTreeField).GetField("darkWoodMaterial",BindingFlags.Instance|BindingFlags.NonPublic)
                .SetValue(field,new Material(appearance));
            var create=typeof(ProceduralTreeField).GetMethod("CreateTree",BindingFlags.Instance|BindingFlags.NonPublic);
            foreach(var sample in new[]{(seed:103,height:7f,dark:false),(seed:109,height:16f,dark:false),
                (seed:105,height:0f,dark:false),(seed:111,height:7f,dark:true)})
            {
                int before=root.GetComponentsInChildren<RopeObject>().Length;
                object tree=Activator.CreateInstance(seedType,true);
                seedType.GetField("seed").SetValue(tree,sample.seed);
                seedType.GetField("position").SetValue(tree,new Vector3(220f+sample.seed*3f,0f,0f));
                seedType.GetField("terrainNormal").SetValue(tree,Vector3.up);
                seedType.GetField("heightOverride").SetValue(tree,sample.height);
                seedType.GetField("darkWood").SetValue(tree,sample.dark);
                trees.Add(tree);
                create.Invoke(field,new[]{tree});
                var vines=root.GetComponentsInChildren<RopeObject>().Skip(before).ToArray();
                Require(vines.Length==0,"Generated trees no longer grow vines");
            }
            UnityEngine.Object.DestroyImmediate(field);
        }

        static void Require(bool condition,string message)
        { if(!condition) throw new Exception(message); }
    }
}
