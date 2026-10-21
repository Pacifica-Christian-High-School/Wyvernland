using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Wyvernland.Editor
{
    public static class PickableStickValidation
    {
        [MenuItem("Tools/Wyvernland/Validate Stick Pickup")]
        public static void Run()
        {
            var root=new GameObject("Stick pickup validation");
            try
            {
                var player=new GameObject("Player");
                player.transform.SetParent(root.transform);
                var avatar=player.AddComponent<RiggedPlayerAvatar>();
                var massPlayer=new GameObject("Mass player");
                massPlayer.transform.SetParent(root.transform);
                var playerBody=massPlayer.AddComponent<Rigidbody>();
                playerBody.mass=75f;
                var controller=massPlayer.AddComponent<FirstPersonController>();
                typeof(FirstPersonController).GetMethod("Awake",
                    BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,null);
                Require(playerBody.mass==65f,"The player Rigidbody has a mass of 65 kg");
                var bodyCollider=player.AddComponent<CapsuleCollider>();
                var hand=new GameObject("Hand").transform;
                hand.SetParent(player.transform);
                var handCollider=hand.gameObject.AddComponent<BoxCollider>();
                var worldCollider=new GameObject("External obstacle");
                worldCollider.transform.SetParent(root.transform);
                var obstacle=worldCollider.AddComponent<BoxCollider>();
                hand.rotation=Quaternion.Euler(0f,35f,0f);
                foreach(Vector3 size in new[]{new Vector3(0.3f,0.03f,0.03f),
                    new Vector3(0.03f,0.3f,0.03f),new Vector3(0.03f,0.03f,0.3f)})
                {
                    // Each sample starts with a horizontal hand. The preceding
                    // sample deliberately tilts it while testing a strike.
                    hand.rotation=Quaternion.Euler(0f,35f,0f);
                    GameObject stick=GameObject.CreatePrimitive(PrimitiveType.Cube);
                    stick.name="Validation stick";
                    stick.transform.SetParent(root.transform);
                    stick.transform.position=new Vector3(4f,2f,1f);
                    stick.transform.rotation=Quaternion.Euler(15f,120f,7f);
                    stick.transform.localScale=size;
                    PhysicalDestructible.Ensure(stick).ApplyMaterial("Wood");
                    int axis=size.x>size.y&&size.x>size.z?0:size.y>size.z?1:2;
                    Vector3 gripSample=Vector3.zero;
                    gripSample[axis]=-0.45f;
                    gripSample[(axis+1)%3]=0.58f;
                    hand.position=stick.transform.TransformPoint(gripSample);
                    PickableObject pickup=stick.AddComponent<PickableObject>();
                    pickup.Label=PickableObjectLabel.Irregular;
                    pickup.SetNearbyDetection(true);
                    Vector3 reach=pickup.PickupReachPoint(hand.position);
                    float nearestVertexDistance=float.PositiveInfinity;
                    foreach(Vector3 vertex in stick.GetComponent<MeshFilter>().sharedMesh.vertices)
                        nearestVertexDistance=Mathf.Min(nearestVertexDistance,
                            (stick.transform.TransformPoint(vertex)-hand.position).sqrMagnitude);
                    Require(Mathf.Abs((reach-hand.position).sqrMagnitude-
                        nearestVertexDistance)<0.000001f,
                        "Stick pickup reaches toward the nearest world-space mesh vertex");
                    var trigger=stick.GetComponent<CapsuleCollider>();
                    var meshCollider=stick.GetComponent<MeshCollider>();
                    Require(trigger!=null&&trigger.isTrigger&&trigger.direction==axis,
                        "Pickup capsule follows the long axis of a stick");
                    float halfSegment=trigger.height*0.5f-trigger.radius;
                    foreach(Vector3 vertex in meshCollider.sharedMesh.vertices)
                    {
                        Vector3 local=stick.transform.InverseTransformPoint(
                            meshCollider.transform.TransformPoint(vertex))-trigger.center;
                        Vector3 nearest=Vector3.zero;
                        nearest[axis]=Mathf.Clamp(local[axis],-halfSegment,halfSegment);
                        Require((local-nearest).sqrMagnitude<trigger.radius*trigger.radius,
                            "Pickup capsule encloses the scaled mesh collider");
                    }
                    Require(pickup.PickUp(hand),"Stick fixture must be within the pickup mass limit");
                    CheckHeldCollisions(pickup,bodyCollider,handCollider,obstacle);
                    Require(pickup.GrabMode==PickableGrabMode.Stick,
                        "A single dominant axis selects Stick, including for Irregular items");
                    var contact=(Vector3)typeof(PickableObject).GetField("heldMeshContact",
                        BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pickup);
                    Require(Vector3.Distance(stick.transform.TransformPoint(contact),hand.position)<0.0001f,
                        "The nearest picked-up mesh point is held at the hand");
                    Vector3 gripLateral=contact;
                    gripLateral[axis]=0f;
                    Require(gripLateral.magnitude<0.0001f,
                        "The stick cross-section is centred in the hand instead of gripping a corner");
                    typeof(PickableObject).GetMethod("UpdateStickHeldTransform",
                        BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pickup,new object[]{1f});
                    Require(Vector3.Distance(stick.transform.TransformPoint(contact),hand.position)<0.0001f&&
                        Vector3.Dot((pickup.StickForwardPoint-hand.position).normalized,
                            hand.forward)>0.999f,
                        "The far endpoint points forward while the same grip stays at the hand");
                    hand.rotation=Quaternion.Euler(0f,115f,0f);
                    hand.position+=new Vector3(1f,0.5f,-0.5f);
                    typeof(PickableObject).GetMethod("UpdateStickHeldTransform",
                        BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pickup,new object[]{1f});
                    Require(Vector3.Distance(stick.transform.TransformPoint(contact),hand.position)<0.0001f&&
                        Vector3.Dot((pickup.StickForwardPoint-hand.position).normalized,
                            hand.forward)>0.999f,
                        "Stick pose follows the hand without changing its grip point");
                    typeof(RiggedPlayerAvatar).GetField("heldRightHandItem",
                        BindingFlags.Instance|BindingFlags.NonPublic).SetValue(avatar,pickup);
                    typeof(RiggedPlayerAvatar).GetField("striking",
                        BindingFlags.Instance|BindingFlags.NonPublic).SetValue(avatar,true);
                    Quaternion gripRotation=stick.transform.localRotation;
                    hand.rotation=Quaternion.Euler(35f,205f,12f);
                    typeof(PickableObject).GetMethod("UpdateHeldPose",
                        BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pickup,null);
                    Require(Quaternion.Angle(stick.transform.localRotation,gripRotation)<0.01f&&
                        Vector3.Distance(stick.transform.TransformPoint(contact),hand.position)<0.0001f&&
                        Vector3.Angle((pickup.StickForwardPoint-hand.position).normalized,
                            player.transform.forward)>30f,
                        "During a strike the stick follows the rotating hand instead of facing forward");
                    typeof(RiggedPlayerAvatar).GetField("striking",
                        BindingFlags.Instance|BindingFlags.NonPublic).SetValue(avatar,false);
                    typeof(RiggedPlayerAvatar).GetField("heldRightHandItem",
                        BindingFlags.Instance|BindingFlags.NonPublic).SetValue(avatar,null);
                    UnityEngine.Object.DestroyImmediate(stick);
                }
                var faceStick=GameObject.CreatePrimitive(PrimitiveType.Cube);
                faceStick.name="Long face pickup stick";
                faceStick.transform.SetParent(root.transform);
                faceStick.transform.position=new Vector3(10f,1f,2f);
                faceStick.transform.localScale=new Vector3(3f,0.08f,0.08f);
                PhysicalDestructible.Ensure(faceStick).ApplyMaterial("Wood");
                var facePickup=faceStick.AddComponent<PickableObject>();
                facePickup.SetNearbyDetection(true);
                Physics.SyncTransforms();
                Require(facePickup.IsNearPickupRange(new Vector3(4.8f,1f,2f),5f)&&
                    Vector3.Distance(facePickup.AimPoint,new Vector3(4.8f,1f,2f))>5f,
                    "A nearby long face is eligible even when its center is outside range");
                Require(Physics.Raycast(new Ray(new Vector3(10f,1f,0f),Vector3.forward),
                    out RaycastHit sideHit,4f)&&
                    sideHit.collider.GetComponentInParent<PickableObject>()==facePickup,
                    "The camera ray hits the middle of the Stick side face");
                Vector3 sideGrip=facePickup.PickupFaceGripPoint(sideHit);
                Require(Mathf.Abs(sideGrip.x-10f)<0.001f&&
                    Mathf.Abs(sideGrip.z-1.96f)<0.001f,
                    "A long face grips at its aimed middle instead of either end");
                Require(Physics.Raycast(new Ray(new Vector3(10f,2f,2f),Vector3.down),
                    out RaycastHit topHit,2f)&&
                    Mathf.Abs(facePickup.PickupFaceGripPoint(topHit).y-1.04f)<0.001f,
                    "Aiming the top face selects a grip on that face");
                Require(Physics.Raycast(new Ray(new Vector3(11.47f,1.03f,0f),
                    Vector3.forward),out RaycastHit cornerHit,4f),
                    "The camera ray hits near the Stick face vertex");
                Vector3 cornerGrip=facePickup.PickupFaceGripPoint(cornerHit);
                Require(faceStick.GetComponent<MeshFilter>().sharedMesh.vertices.Any(vertex=>
                    Vector3.Distance(faceStick.transform.TransformPoint(vertex),cornerGrip)<0.0001f),
                    "An aimed face vertex remains the exact grip when it is nearby");
                hand.position=sideGrip;
                Require(facePickup.PickUp(hand,false,sideGrip)&&
                    Vector3.Distance(faceStick.transform.TransformPoint((Vector3)
                        typeof(PickableObject).GetField("heldMeshContact",
                            BindingFlags.Instance|BindingFlags.NonPublic).GetValue(facePickup)),
                        hand.position)<0.0001f,
                    "Pickup keeps the same side-face grip used by the reach");
                UnityEngine.Object.DestroyImmediate(faceStick);
                GameObject ordinary=GameObject.CreatePrimitive(PrimitiveType.Cube);
                ordinary.transform.SetParent(root.transform);
                ordinary.transform.localScale=Vector3.one*0.1f;
                var ordinaryBody=ordinary.AddComponent<Rigidbody>();
                ordinaryBody.interpolation=RigidbodyInterpolation.Interpolate;
                var regular=ordinary.AddComponent<PickableObject>();
                regular.PickUp(hand);
                CheckHeldCollisions(regular,bodyCollider,handCollider,obstacle);
                Require(ordinaryBody.interpolation==RigidbodyInterpolation.None,
                    "A held item follows the hand without Rigidbody interpolation lag");
                Require(ordinary.GetComponent<PickableObject>().GrabMode==PickableGrabMode.Regular,
                    "A balanced object keeps Regular grabbing mode");
                regular.Release(Vector3.zero,player.transform);
                Rigidbody releasedBody=ordinary.GetComponent<Rigidbody>();
                Require(!releasedBody.isKinematic&&releasedBody.detectCollisions&&releasedBody.useGravity,
                    "Releasing an object restores dynamic Rigidbody physics");
                Require(releasedBody.interpolation==RigidbodyInterpolation.Interpolate,
                    "Releasing an item restores its original Rigidbody interpolation");
                Require(Physics.GetIgnoreCollision(ordinary.GetComponent<MeshCollider>(),bodyCollider)&&
                    Physics.GetIgnoreCollision(ordinary.GetComponent<MeshCollider>(),handCollider),
                    "Dropping an object keeps its player collision exclusions");
                foreach(var sample in new[]{
                    (material:"Stone",size:new Vector3(0.3f,0.03f,0.03f),mode:PickableGrabMode.Regular),
                    (material:"River Rock",size:new Vector3(0.3f,0.03f,0.03f),mode:PickableGrabMode.Regular),
                    (material:"Wood",size:Vector3.one*0.2f,mode:PickableGrabMode.Stick),
                    (material:"Burnt Wood",size:Vector3.one*0.2f,mode:PickableGrabMode.Stick)})
                {
                    var objectPart=GameObject.CreatePrimitive(PrimitiveType.Cube);
                    objectPart.name="Validation "+sample.material;
                    objectPart.transform.SetParent(root.transform);
                    objectPart.transform.localScale=sample.size;
                    var properties=PhysicalDestructible.Ensure(objectPart);
                    properties.ApplyMaterial(sample.material);
                    var pickup=objectPart.AddComponent<PickableObject>();
                    pickup.PickUp(hand);
                    Require(pickup.GrabMode==sample.mode,
                        sample.material+" uses its material's holding mode regardless of shape");
                    UnityEngine.Object.DestroyImmediate(objectPart);
                }
                var massProbe=new GameObject("Pickup mass boundaries");
                massProbe.transform.SetParent(root.transform);
                var massBody=massProbe.AddComponent<Rigidbody>();
                var massPickup=massProbe.AddComponent<PickableObject>();
                massBody.mass=26f;
                Require(massPickup.CanPickUpWithOneHand&&!massPickup.CanPickUpWithTwoHands,
                    "Exactly 26 kg fits one hand");
                massBody.mass=26.01f;
                Require(!massPickup.CanPickUpWithOneHand&&massPickup.CanPickUpWithTwoHands,
                    "Mass above 26 kg requires both hands");
                massBody.mass=90f;
                Require(massPickup.CanPickUpWithTwoHands&&!massPickup.RequiresDragging,"Exactly 90 kg fits both hands");
                massBody.mass=90.01f;
                Require(!massPickup.CanPickUpWithTwoHands&&massPickup.RequiresDragging,
                    "Mass above 90 kg requires dragging instead of pickup");
                foreach(var sample in new[]{(mass:90.001f,one:.3f),(mass:145f,one:.15f),
                    (mass:200f,one:0f),(mass:250f,one:0f)})
                {
                    Require(Mathf.Abs(PickableObject.DragSpeed(sample.mass,1)-sample.one)<.00001f&&
                        Mathf.Abs(PickableObject.DragSpeed(sample.mass,2)-sample.one*2f)<.00001f,
                        "Each free contacting hand contributes up to 0.3 m/s, falling linearly to zero at 200 kg");
                    Require(PickableObject.DragSpeed(sample.mass,0)==0f,
                        "Reaching without hand contact cannot move a heavy object");
                }
                massBody.mass=145f;
                Vector3 dragFrom=massBody.position;
                massPickup.DragToward(dragFrom+Vector3.right*2f,2,.1f);
                Require(!massPickup.IsHeld&&!massPickup.HasBeenPickedUp&&
                    !massBody.isKinematic&&massBody.useGravity&&massBody.detectCollisions,
                    "Dragging leaves the object dynamic, collidable and uncollected");
                var swingSpeed=typeof(RiggedPlayerAvatar).GetMethod("HeldArmSwingSpeedScale",
                    System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
                Require(Mathf.Abs((float)swingSpeed.Invoke(null,new object[]{60f})-.99f)<.000001f,
                    "A held 60 kg object retains 99 percent of normal arm swing speed");
                var swingScale=typeof(RiggedPlayerAvatar).GetMethod("HeldArmSwingScale",
                    System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
                massBody.mass=.001f;
                Require(Mathf.Abs((float)swingScale.Invoke(null,new object[]{massPickup})-.8f)<.0001f,
                    "Holding even a very light item starts with a 20 percent arm swing amplitude reduction");
                massBody.mass=145f;
                ValidateDraggingHands(root,controller,massPickup);
                ValidateDraggingPhysics();
                Debug.Log("PICKABLE_STICK_VALIDATION_PASS: X/Y/Z grips, strike orientation, held collision, 90 kg pickup boundary, held swing scaling, free-hand dragging, 90–200 kg drag speeds and obstacle collision.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void ValidateDraggingHands(GameObject root,
            FirstPersonController player,PickableObject target)
        {
            var rig=new GameObject("Drag hand availability");rig.transform.SetParent(root.transform);
            var avatar=rig.AddComponent<RiggedPlayerAvatar>();
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            void Set(string name,object value) => typeof(RiggedPlayerAvatar).GetField(name,flags).SetValue(avatar,value);
            Set("player",player);
            foreach(string name in new[]{"rightShoulder","rightElbow","rightHandMarker",
                "leftShoulder","leftElbow","leftHandMarker"})
            {
                var bone=new GameObject(name).transform;bone.SetParent(rig.transform);
                bone.position=target.transform.position;
                Set(name,bone);
            }
            var carried=new GameObject("Other hand item");carried.transform.SetParent(rig.transform);
            var item=carried.AddComponent<PickableObject>();
            Require(avatar.BeginHandPickup(target,false,target.transform.position)&&
                avatar.IsRightReaching&&avatar.IsLeftReaching,
                "Heavy objects recruit both hands when both hands are free");
            typeof(RiggedPlayerAvatar).GetMethod("TryFinishHandPickup",flags).Invoke(avatar,null);
            Require(!target.IsHeld&&avatar.IsReachingForObject,
                "Hand contact with a dragging target never converts it into a held item");
            avatar.CancelRightHandPickup();
            Set("heldRightHandItem",item);
            Require(avatar.BeginHandPickup(target,false,target.transform.position)&&
                avatar.IsLeftReaching&&!avatar.IsRightReaching&&avatar.HeldRightHandItem==item,
                "Dragging reaches with the free left hand and preserves the occupied right hand");
            avatar.CancelRightHandPickup();
            Set("heldRightHandItem",null);Set("heldLeftHandItem",item);
            Require(avatar.BeginHandPickup(target,true,target.transform.position)&&
                avatar.IsRightReaching&&!avatar.IsLeftReaching,
                "Dragging uses the free right hand even when left hand input is preferred");
            avatar.CancelRightHandPickup();
            Set("heldRightHandItem",item);
            Require(!avatar.BeginHandPickup(target,false,target.transform.position)&&!avatar.IsReaching,
                "Two occupied hands cannot start dragging");
            Set("heldRightHandItem",null);Set("heldLeftHandItem",null);
        }

        private static void ValidateDraggingPhysics()
        {
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var item=new GameObject("Heavy dragging physics");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(item,scene);
            var body=item.AddComponent<Rigidbody>();
            item.AddComponent<BoxCollider>().size=Vector3.one*.2f;
            var pickup=item.AddComponent<PickableObject>();
            var physics=scene.GetPhysicsScene();
            Vector3 origin=new Vector3(-500f,10f,-500f);
            try
            {
                foreach(var sample in new[]{(mass:90.001f,hands:1,speed:.3f),
                    (mass:90.001f,hands:2,speed:.6f),(mass:145f,hands:1,speed:.15f),
                    (mass:145f,hands:2,speed:.3f),(mass:200f,hands:2,speed:0f),
                    (mass:250f,hands:2,speed:0f),(mass:100f,hands:0,speed:0f)})
                {
                    body.mass=sample.mass;body.position=origin;
                    body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;
                    Physics.SyncTransforms();
                    for(int step=0;step<40;step++)
                    {
                        pickup.DragToward(origin+Vector3.right*20f,sample.hands,.02f);
                        physics.Simulate(.02f);
                    }
                    Require(Mathf.Abs(body.linearVelocity.x-sample.speed)<.0001f&&
                        body.linearVelocity.y<0f&&!pickup.IsHeld,
                        $"Dragging {sample.mass} kg with {sample.hands} hands reaches {sample.speed} m/s and keeps gravity active");
                }
                var wall=new GameObject("Dragging obstacle");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(wall,scene);
                wall.transform.position=origin+Vector3.right*.6f;
                wall.AddComponent<BoxCollider>().size=new Vector3(.1f,100f,2f);
                var floor=new GameObject("Dragging ground");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(floor,scene);
                floor.transform.position=origin-Vector3.up*.2f;
                floor.AddComponent<BoxCollider>().size=new Vector3(4f,.2f,4f);
                body.mass=100f;body.position=origin;body.linearVelocity=Vector3.zero;
                body.angularVelocity=Vector3.zero;body.rotation=Quaternion.identity;
                Physics.SyncTransforms();
                float maximumMovement=0f;
                for(int step=0;step<120;step++)
                {
                    pickup.DragToward(origin+Vector3.right*20f,2,.02f);
                    physics.Simulate(.02f);
                    maximumMovement=Mathf.Max(maximumMovement,body.position.x-origin.x);
                }
                Require(maximumMovement<=.46f&&maximumMovement>.1f,
                    $"Dragging uses solid collision instead of passing through an obstacle: movement {body.position-origin}");
                UnityEngine.Object.DestroyImmediate(wall);
                UnityEngine.Object.DestroyImmediate(floor);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(item);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void CheckHeldCollisions(PickableObject pickup,Collider body,
            Collider hand,Collider obstacle)
        {
            MeshCollider mesh=pickup.GetComponent<MeshCollider>();
            Require(mesh!=null&&mesh.enabled&&!mesh.isTrigger&&mesh.convex,
                "A held object's solid mesh collider stays active and convex");
            Rigidbody heldBody=pickup.GetComponent<Rigidbody>();
            Require(heldBody!=null&&heldBody.isKinematic&&heldBody.detectCollisions&&
                !heldBody.useGravity,
                "A held object is kinematic while its solid collider stays active");
            Require(Physics.GetIgnoreCollision(mesh,body)&&
                Physics.GetIgnoreCollision(mesh,hand)&&
                !Physics.GetIgnoreCollision(mesh,obstacle),
                "A held object ignores the player's colliders but can touch the world");
            var destructible=PhysicalDestructible.Ensure(pickup.gameObject);
            Require(destructible!=null&&PhysicalDestructible.Resolve(mesh)==destructible,
                "Collision damage identifies the held item as an object, not the player");
            hand.transform.position+=Vector3.forward*0.4f;
            typeof(PickableObject).GetMethod("UpdateHeldMotion",
                BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pickup,null);
            Require(pickup.HeldPointVelocity(pickup.transform.position).magnitude>1f,
                "Hand movement still supplies an impact velocity for destruction");
        }

        private static void Require(bool condition,string message)
        { if(!condition) throw new Exception(message); }
    }
}
