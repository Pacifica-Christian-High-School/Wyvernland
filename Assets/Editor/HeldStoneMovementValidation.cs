using System;
using System.Linq;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Wyvernland.Editor
{
    public static class HeldStoneMovementValidation
    {
        static void Check(bool value,string message) { if(!value) throw new InvalidOperationException("Held stone movement: "+message); }
        public static void Run()
        {
            var previous=SceneManager.GetActiveScene();
            if(string.IsNullOrEmpty(previous.path)) { EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");previous=SceneManager.GetActiveScene(); }
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
            var simulation=Physics.simulationMode;
            try { Physics.simulationMode=SimulationMode.Script;Validate(false);Validate(true);Debug.Log("HELD_STONE_MOVEMENT_VALIDATION_PASS: both hands retain a Regular stone grip while striking and moving the holder Rigidbody through physics steps; explicit world-position drift recovery and continued collision constraints."); }
            finally { Physics.simulationMode=simulation;SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true); }
        }
        static void Validate(bool left)
        {
            var actor=new GameObject("Stone strike walker");actor.transform.position=new Vector3(left?3100:3000,1000,0);
            var body=actor.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
            var camera=new GameObject("View",typeof(Camera));camera.transform.SetParent(actor.transform,false);
            var player=actor.AddComponent<FirstPersonController>();player.Initialize(camera.transform);player.enabled=false;
            var avatar=actor.AddComponent<RiggedPlayerAvatar>();avatar.Initialize(player);avatar.enabled=false;
            var hand=actor.GetComponentsInChildren<RightHandPickupTrigger>().First(t=>t.name.StartsWith(left?"Left":"Right")).transform;
            var stone=GameObject.CreatePrimitive(PrimitiveType.Sphere);stone.name="Strike stone";stone.transform.localScale=Vector3.one*.12f;
            stone.transform.position=hand.position+Vector3.forward*.06f;stone.AddComponent<Rigidbody>().useGravity=false;PhysicalDestructible.Ensure(stone).ApplyMaterial("Stone");
            var pickup=stone.AddComponent<PickableObject>();
            Check(avatar.BeginHandPickup(pickup,left,hand.position),"stone pickup starts");avatar.UpdatePickupReach(.2f);
            Check(pickup.IsHeld&&pickup.GrabMode==PickableGrabMode.Regular&&avatar.BeginRightHandStrike(),"selected hand holds a striking stone");
            var grip=(Vector3)typeof(PickableObject).GetField("heldMeshContact",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pickup);
            pickup.UpdateHeldPose();pickup.SampleHeldMotion(.02f);Vector3 start=stone.transform.position;
            for(int step=0;step<12;step++) {
                body.position+=new Vector3(.04f,.015f,.03f);Physics.SyncTransforms();actor.scene.GetPhysicsScene().Simulate(.02f);
                pickup.UpdateHeldPose();pickup.SampleHeldMotion(.02f);
                Check(Vector3.Distance(stone.transform.TransformPoint(grip),hand.position)<.001f,"walking does not leave the striking stone at an old world position");
            }
            Check(Vector3.Distance(start,stone.transform.position)>.4f,"striking stone actually travels with the holder");
            stone.transform.position-=Vector3.right*.2f;pickup.UpdateHeldPose();
            Check(Vector3.Distance(stone.transform.TransformPoint(grip),hand.position)<.001f,"strike mode restores a stale kinematic world pose to the hand");
            var obstacle=GameObject.CreatePrimitive(PrimitiveType.Cube);obstacle.transform.localScale=new Vector3(.02f,2,2);obstacle.transform.position=stone.transform.position+Vector3.right*.15f;
            var shape=stone.GetComponentsInChildren<Collider>().First(c=>c.enabled&&!c.isTrigger);
            pickup.SampleHeldMotion(.1f);body.position+=Vector3.right*.12f;Physics.SyncTransforms();pickup.UpdateHeldPose();pickup.SampleHeldMotion(.2f);Physics.SyncTransforms();
            bool overlap=Physics.ComputePenetration(shape,shape.transform.position,shape.transform.rotation,obstacle.GetComponent<Collider>(),obstacle.transform.position,obstacle.transform.rotation,out _,out float depth);
            Check(!overlap||depth<=.00021f,"following the holder still respects slow solid contact");
            UnityEngine.Object.DestroyImmediate(obstacle);avatar.CancelRightHandStrike();
            ValidateGripDirection(avatar,pickup,hand);
            UnityEngine.Object.DestroyImmediate(stone);UnityEngine.Object.DestroyImmediate(actor);
        }

        static void ValidateGripDirection(RiggedPlayerAvatar avatar,PickableObject pickup,Transform hand)
        {
            const BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic;
            foreach(int kind in new[]{0,1,2}) {
                typeof(PickableObject).GetField("grabMode",fields).SetValue(pickup,
                    kind==2?PickableGrabMode.Stick:PickableGrabMode.Regular);
                pickup.Label=kind==1?PickableObjectLabel.Irregular:PickableObjectLabel.Regular;
                var original=Quaternion.Euler(17,31,-13);
                pickup.transform.localRotation=original;
                Check(avatar.BeginRightHandStrike(),"grip-direction strike starts");
                foreach(string phase in new[]{"attack","strikeRebounding","strikeEnding"}) {
                    if(phase!="attack") typeof(RiggedPlayerAvatar).GetField(phase,fields).SetValue(avatar,true);
                    hand.rotation=Quaternion.Euler(28,-43,19)*hand.rotation;
                    pickup.UpdateHeldPose();
                    Check(Quaternion.Angle(pickup.transform.localRotation,original)<.01f,
                        $"grip {kind} retains its local direction during {phase}");
                    Check(Quaternion.Angle(pickup.transform.rotation,hand.rotation*original)<.01f,
                        "object follows the rotating hand without turning in its grip");
                }
                avatar.CancelRightHandStrike();
            }
        }
    }
}
