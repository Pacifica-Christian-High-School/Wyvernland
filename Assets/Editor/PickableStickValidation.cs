using System;
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
                player.AddComponent<RiggedPlayerAvatar>();
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
                foreach(Vector3 size in new[]{new Vector3(3f,0.3f,0.3f),
                    new Vector3(0.3f,3f,0.3f),new Vector3(0.3f,0.3f,3f)})
                {
                    GameObject stick=GameObject.CreatePrimitive(PrimitiveType.Cube);
                    stick.name="Validation stick";
                    stick.transform.SetParent(root.transform);
                    stick.transform.position=new Vector3(4f,2f,1f);
                    stick.transform.rotation=Quaternion.Euler(15f,120f,7f);
                    stick.transform.localScale=size;
                    int axis=size.x>size.y&&size.x>size.z?0:size.y>size.z?1:2;
                    Vector3 gripSample=Vector3.zero;
                    gripSample[axis]=-0.45f;
                    gripSample[(axis+1)%3]=0.58f;
                    hand.position=stick.transform.TransformPoint(gripSample);
                    PickableObject pickup=stick.AddComponent<PickableObject>();
                    pickup.Label=PickableObjectLabel.Irregular;
                    pickup.SetNearbyDetection(true);
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
                    pickup.PickUp(hand);
                    CheckHeldCollisions(pickup,bodyCollider,handCollider,obstacle);
                    Require(pickup.GrabMode==PickableGrabMode.Stick,
                        "A single dominant axis selects Stick, including for Irregular items");
                    var contact=(Vector3)typeof(PickableObject).GetField("heldMeshContact",
                        BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pickup);
                    Require(Vector3.Distance(stick.transform.TransformPoint(contact),hand.position)<0.0001f,
                        "The nearest picked-up mesh point is held at the hand");
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
                    UnityEngine.Object.DestroyImmediate(stick);
                }
                GameObject ordinary=GameObject.CreatePrimitive(PrimitiveType.Cube);
                ordinary.transform.SetParent(root.transform);
                var regular=ordinary.AddComponent<PickableObject>();
                regular.PickUp(hand);
                CheckHeldCollisions(regular,bodyCollider,handCollider,obstacle);
                Require(ordinary.GetComponent<PickableObject>().GrabMode==PickableGrabMode.Regular,
                    "A balanced object keeps Regular grabbing mode");
                regular.Release(Vector3.zero,player.transform);
                Rigidbody releasedBody=ordinary.GetComponent<Rigidbody>();
                Require(!releasedBody.isKinematic&&releasedBody.detectCollisions&&releasedBody.useGravity,
                    "Releasing an object restores dynamic Rigidbody physics");
                Require(Physics.GetIgnoreCollision(ordinary.GetComponent<MeshCollider>(),bodyCollider)&&
                    Physics.GetIgnoreCollision(ordinary.GetComponent<MeshCollider>(),handCollider),
                    "Dropping an object keeps its player collision exclusions");
                foreach(var sample in new[]{
                    (material:"Stone",size:new Vector3(3f,0.3f,0.3f),mode:PickableGrabMode.Regular),
                    (material:"River Rock",size:new Vector3(3f,0.3f,0.3f),mode:PickableGrabMode.Regular),
                    (material:"Wood",size:Vector3.one,mode:PickableGrabMode.Stick),
                    (material:"Burnt Wood",size:Vector3.one,mode:PickableGrabMode.Stick)})
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
                Debug.Log("PICKABLE_STICK_VALIDATION_PASS: elongated X/Y/Z grip, Regular mode, held mesh collision with the world, player exclusions, and motion-driven destruction input.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
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
            typeof(PickableObject).GetMethod("FixedUpdate",
                BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pickup,null);
            Require(pickup.HeldPointVelocity(pickup.transform.position).magnitude>1f,
                "Hand movement still supplies an impact velocity for destruction");
        }

        private static void Require(bool condition,string message)
        { if(!condition) throw new Exception(message); }
    }
}
