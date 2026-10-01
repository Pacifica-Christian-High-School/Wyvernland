using System;
using UnityEngine;

namespace Wyvernland.Editor
{
    public static class GoldenStoneLaunchValidation
    {
        static void Require(bool condition,string message)
        { if(!condition) throw new Exception(message); }
        public static void Run()
        {
            var player=new GameObject("Launch test player");
            player.AddComponent<BoxCollider>();
            var camera=player.AddComponent<Camera>();
            var hand=new GameObject("Hand"); hand.transform.SetParent(player.transform);
            hand.transform.localPosition=new Vector3(0.5f,-0.2f,1f);
            var target=GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.transform.position=new Vector3(0f,0f,20f);
            var stone=GameObject.CreatePrimitive(PrimitiveType.Cube);
            stone.transform.localScale=Vector3.one*0.3f;
            var destructible=PhysicalDestructible.Ensure(stone);
            destructible.MaterialName="Stone"; destructible.Hardness=10f; destructible.Resilience=10f;
            var pickup=stone.AddComponent<PickableObject>();
            pickup.DisplayName="Golden Stone";
            Rigidbody launched=null;
            try
            {
                Require(pickup.LaunchGoldenStoneCopy(camera,player.transform)==null,"Unheld stone launched");
                pickup.PickUp(hand.transform);
                Physics.SyncTransforms();
                Vector3 origin=stone.transform.position;
                launched=pickup.LaunchGoldenStoneCopy(camera,player.transform);
                Require(launched!=null,"Held golden stone failed to launch");
                Require(pickup.IsHeld&&stone.transform.parent==hand.transform,"Original was released");
                Require(Vector3.Distance(origin,launched.position)<0.00001f,"Copy spawn position changed");
                Require(Mathf.Abs(launched.linearVelocity.magnitude-50f)<0.0001f,"Wrong launch speed");
                Require(Vector3.Dot(launched.linearVelocity.normalized,(new Vector3(0f,0f,19.5f)-origin).normalized)>0.99999f,"Wrong aim direction");
                Require(!launched.isKinematic&&launched.useGravity,"Copy is not dynamic");
                Require(!launched.GetComponent<PickableObject>().IsHeld,"Copy remained held");
                Require(launched.GetComponent<MeshFilter>().sharedMesh!=stone.GetComponent<MeshFilter>().sharedMesh,"Copy shares destructible mesh");
                var collider=launched.GetComponent<MeshCollider>();
                Require(collider.enabled&&Physics.GetIgnoreCollision(collider,stone.GetComponent<MeshCollider>()),"Copy collides with held stone");
                Require(Physics.GetIgnoreCollision(collider,player.GetComponent<BoxCollider>()),"Copy collides with player");
                Require(launched.GetComponent<PhysicalDestructible>().Hardness==10f,"Copy lost material properties");
                pickup.DisplayName="Ordinary stone";
                Require(pickup.LaunchGoldenStoneCopy(camera,player.transform)==null,"Ordinary stone launched");
                Debug.Log("GOLDEN_STONE_LAUNCH_VALIDATION_PASS");
            }
            finally
            {
                if(launched!=null) UnityEngine.Object.DestroyImmediate(launched.gameObject);
                UnityEngine.Object.DestroyImmediate(stone);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(player);
            }
        }
    }
}
