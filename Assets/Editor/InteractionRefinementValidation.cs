using System;
using System.Linq;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Wyvernland.Editor
{
    public static class InteractionRefinementValidation
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Check(bool value,string message) { if(!value) throw new InvalidOperationException("Interaction refinements: "+message); }
        static void Set(object owner,string name,object value)=>owner.GetType().GetField(name,Hidden).SetValue(owner,value);
        static object Get(object owner,string name)=>owner.GetType().GetField(name,Hidden).GetValue(owner);
        static PickableObject Item(Transform parent,string material,float size=.2f)
        {
            var item=GameObject.CreatePrimitive(PrimitiveType.Cube);item.transform.SetParent(parent,false);item.transform.localScale=Vector3.one*size;
            item.AddComponent<Rigidbody>().useGravity=false;PhysicalDestructible.Ensure(item).ApplyMaterial(material);
            return item.AddComponent<PickableObject>();
        }
        public static void Run()
        {
            var previous=SceneManager.GetActiveScene();if(string.IsNullOrEmpty(previous.path)) { EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");previous=SceneManager.GetActiveScene(); }
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
            var root=new GameObject("Interaction refinement fixture");root.transform.position=new Vector3(300,1000,0);
            try {
                foreach(bool left in new[]{false,true}) {
                    var actor=new GameObject("Throw actor");actor.transform.SetParent(root.transform,false);
                    var avatar=actor.AddComponent<RiggedPlayerAvatar>();
                    var shoulder=new GameObject("Shoulder").transform;shoulder.SetParent(actor.transform,false);
                    var elbow=new GameObject("Elbow").transform;elbow.SetParent(shoulder,false);
                    var hand=new GameObject("Hand").transform;hand.SetParent(elbow,false);
                    string side=left?"left":"right";Set(avatar,side+"Shoulder",shoulder);Set(avatar,side+"Elbow",elbow);Set(avatar,side+"HandMarker",hand);
                    var stone=Item(root.transform,"Stone");stone.transform.position=hand.position;
                    Check(stone.PickUp(hand),"stone can be picked up");Set(avatar,left?"heldLeftHandItem":"heldRightHandItem",stone);
                    stone.Label=PickableObjectLabel.Irregular;
                    for(int i=0;i<4;i++) {
                        hand.position+=new Vector3(.4f,.1f,-.2f);hand.rotation=Quaternion.Euler(i*10,30+i*15,0);
                        stone.UpdateHeldPose();
                        Check(Quaternion.Angle(stone.transform.rotation,hand.rotation*Quaternion.Euler(0,0,90))<.01f,
                            "stone rotation follows its hand in the same pose update");
                        Check(Vector3.Distance(stone.transform.position,hand.TransformPoint(new Vector3(-stone.Size.y*.5f,0,0)))<.0001f,
                            "stone translation follows its hand without exponential lag");
                    }
                    Check(RiggedPlayerAvatar.HeldReturnRate(stone)>=12,"heavy stones retain a responsive arm return rate");
                    if(left) avatar.SetLeftHandLiftCharge(.7f);else avatar.SetRightHandLiftCharge(.7f);
                    typeof(RiggedPlayerAvatar).GetMethod("ApplyRightHandThrowPose",Hidden).Invoke(avatar,null);
                    Check(Quaternion.Angle(shoulder.localRotation,Quaternion.identity)>20,"either hand visibly charges its throw");
                    if(left) avatar.BeginLeftHandThrowAnimation();else avatar.BeginRightHandThrowAnimation();
                    Check((bool)Get(avatar,"throwAnimationWaitingForRelease")&&(bool)Get(avatar,"throwUsingLeftHand")==left,"the correct arm starts the release animation");
                    avatar.CancelPendingRightHandThrowAnimation();Check(stone.IsHeld,"cancelling either charge preserves the held object");
                    if(left) avatar.BeginLeftHandThrowAnimation();else avatar.BeginRightHandThrowAnimation();
                    Vector3 launch=new Vector3(1,2,22);
                    if(left) avatar.ReleaseLeftHandItem(launch,true);else avatar.ReleaseRightHandItem(launch,true);
                    Check(!stone.IsHeld&&!stone.GetComponent<Rigidbody>().isKinematic&&Vector3.Distance(stone.GetComponent<Rigidbody>().linearVelocity,launch)<.0001f,
                        "both hands launch with the requested velocity rather than dropping");
                    stone.transform.SetParent(root.transform,true);
                }
                ValidateActivity(root);
                Debug.Log("INTERACTION_REFINEMENT_VALIDATION_PASS: immediate stone grip motion, responsive heavy-stone arm return, both charge/throw animations and launch velocities, sleeping ragdoll pose/injury retention, inclusive 10m moving-object wake, stationary-object exclusion, moving/held-body safety and sleeping-corpse pickup.");
            } finally { SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true); }
        }
        static void ValidateActivity(GameObject root)
        {
            var dummy=TrainingDummy.Create(root.transform,null);dummy.transform.position=new Vector3(1000,1000,0);
            var rig=dummy.Damage.Ragdoll;
            var prop=Item(root.transform,"Wood",.05f);var body=prop.GetComponent<Rigidbody>();body.position=dummy.transform.position+Vector3.right*20;
            var point=rig.Model.GetComponentsInChildren<PlayerHitShape>().First(s=>s.Part==PlayerBodyPart.LeftArm).GetComponent<Collider>().bounds.center;
            for(int i=0;i<3;i++) {
                var strike=Item(root.transform,"Wood",.05f);strike.transform.position=dummy.transform.position+Vector3.right*20;
                Check(dummy.Damage.ReceiveImpact(PlayerBodyPart.LeftArm,strike.GetComponent<Collider>(),point,Vector3.right,Vector3.right*100),"three-hit Catagma fixture");
            }
            foreach(var bone in rig.Bodies) if(bone!=null) { bone.linearVelocity=Vector3.zero;bone.angularVelocity=Vector3.zero; }
            rig.UpdateSimulationActivity(0);var pose=rig.CapturePose();rig.UpdateSimulationActivity(1);rig.UpdateSimulationActivity(2);
            Check(rig.SimulationSleeping&&rig.Bodies.All(b=>b==null||b.isKinematic),"quiet settled ragdoll disables simulation");
            Check(pose.SequenceEqual(rig.CapturePose())&&!dummy.Damage.CanUseHand(true),"sleeping preserves the injury and exact pose");
            var motion=MovingImpactMotion.Ensure(body);
            void Sample()=>typeof(MovingImpactMotion).GetMethod("Sample",Hidden).Invoke(motion,null);
            body.position=dummy.transform.position+Vector3.right*10.01f;body.linearVelocity=Vector3.right;Sample();rig.UpdateSimulationActivity(3);
            Check(rig.SimulationSleeping,"a moving object beyond 10m does not wake the ragdoll");
            body.position=dummy.transform.position+Vector3.right*10f;Sample();rig.UpdateSimulationActivity(4);
            Check(!rig.SimulationSleeping&&rig.Bodies.Any(b=>b!=null&&!b.isKinematic),"the 10m boundary wakes physics");
            body.position=dummy.transform.position+Vector3.right*10.1f;Sample();rig.UpdateSimulationActivity(5);rig.UpdateSimulationActivity(6);
            Check(rig.SimulationSleeping,"physics sleeps again after the nearby mover leaves");
            body.position=dummy.transform.position+Vector3.right*3;body.linearVelocity=Vector3.zero;Sample();rig.UpdateSimulationActivity(7);
            Check(rig.SimulationSleeping,"nearby stationary objects do not enable ragdoll");
            var hand=new GameObject("Moving held prop hand").transform;hand.SetParent(root.transform,false);hand.position=prop.transform.position;
            Check(prop.PickUp(hand),"moving held prop fixture");hand.position+=Vector3.right*.5f;prop.SampleHeldMotion(.1f);Sample();rig.UpdateSimulationActivity(8);
            Check(!rig.SimulationSleeping,"a moving held kinematic object wakes nearby ragdoll");
            prop.Release(Vector3.zero,hand);Sample();rig.UpdateSimulationActivity(9);rig.UpdateSimulationActivity(10);
            rig.SetInjury(rig.Injury,Vector3.right*100,point);rig.UpdateSimulationActivity(11);
            Check(!rig.SimulationSleeping&&rig.Bodies.Any(b=>b!=null&&!b.isKinematic&&b.linearVelocity.sqrMagnitude>0),"moving or newly struck limbs remain simulated");
            body.linearVelocity=Vector3.zero;Sample();
            var corpse=PlayerCorpse.Create(new PlayerDamage.CorpseState { id="Refinement sleeping corpse",position=new Vector3(1100,1000,0),rotation=Quaternion.identity },root.transform);
            corpse.SetReplica(false);var corpseRig=corpse.GetComponent<PlayerRagdoll>();corpseRig.UpdateSimulationActivity(1);corpseRig.UpdateSimulationActivity(2);
            Check(corpseRig.SimulationSleeping,"settled corpse can sleep");
            hand.position=corpse.NearestPoint(corpse.transform.position);
            Check(corpse.GetComponent<PickableObject>().PickUp(hand,true,hand.position)&&!corpseRig.SimulationSleeping&&corpseRig.Bodies.All(b=>b==null||!b.isKinematic),
                "picking up a sleeping corpse immediately restores articulated physics");
        }
    }
}
