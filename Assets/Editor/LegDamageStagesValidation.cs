using System;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Wyvernland.Editor
{
    public static class LegDamageStagesValidation
    {
        static void Check(bool value,string message) { if(!value) throw new InvalidOperationException("Leg stages: "+message); }
        public static void Run()
        {
            var previous=SceneManager.GetActiveScene();
            if(string.IsNullOrEmpty(previous.path)) { EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");previous=SceneManager.GetActiveScene(); }
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
            try { Validate(true);Validate(false);ValidateMotor();Debug.Log("LEG_DAMAGE_STAGES_VALIDATION_PASS: both leg orders, measured 25/50/75 percent motor speed loss, independent 20/40/60 percent smaller animation swing with unchanged cadence, fourth-hit single-leg ragdoll, mirrored right-crouch support pose and moving gait, attached disabled foot collider, replicated stage/pose, both-leg pelvis ragdoll and full respawn recovery."); }
            finally { SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true); }
        }
        static void ValidateMotor()
        {
            var root=new GameObject("Leg speed motor fixture");root.transform.position=new Vector3(2000,2000,0);
            var simulation=Physics.simulationMode;
            try {
                Physics.simulationMode=SimulationMode.Script;
                var body=root.AddComponent<Rigidbody>();body.useGravity=false;body.constraints=RigidbodyConstraints.FreezeRotation;
                var camera=new GameObject("View",typeof(Camera));camera.transform.SetParent(root.transform,false);
                var player=root.AddComponent<FirstPersonController>();player.Initialize(camera.transform);player.enabled=false;
                var avatar=root.AddComponent<RiggedPlayerAvatar>();avatar.Initialize(player);avatar.enabled=false;
                var damage=root.GetComponent<PlayerDamage>();
                var input=typeof(FirstPersonController).GetField("movementInput",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                var tick=typeof(FirstPersonController).GetMethod("FixedUpdate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                for(int stage=0;stage<=3;stage++) {
                    if(stage>0) {
                        var shot=GameObject.CreatePrimitive(PrimitiveType.Cube);shot.transform.position=root.transform.position+Vector3.right*10;
                        shot.AddComponent<Rigidbody>().isKinematic=true;PhysicalDestructible.Ensure(shot);shot.GetComponent<Rigidbody>().mass=.01f;
                        Check(damage.ReceiveImpact(PlayerBodyPart.Legs,shot.GetComponent<Collider>(),root.transform.position,Vector3.right,Vector3.right*100,3),"motor fixture leg wound");
                        UnityEngine.Object.DestroyImmediate(shot);
                    }
                    input.SetValue(player,Vector2.up);
                    for(int frame=0;frame<80;frame++) { tick.Invoke(player,null);root.scene.GetPhysicsScene().Simulate(Time.fixedDeltaTime); }
                    float expected=6f*(1f-.25f*stage);
                    Check(Mathf.Abs(player.PlanarSpeed-expected)<.03f,"actual movement speed at leg stage "+stage+": "+player.PlanarSpeed+" expected "+expected);
                }
            } finally { Physics.simulationMode=simulation;UnityEngine.Object.DestroyImmediate(root); }
        }
        static void Validate(bool firstLeft)
        {
            var root=new GameObject("Leg stages fixture");
            try {
                var dummy=TrainingDummy.Create(root.transform,null);dummy.transform.position=new Vector3(firstLeft?1000:1100,1000,0);
                var damage=dummy.Damage;var avatar=dummy.GetComponent<RiggedPlayerAvatar>();var controller=dummy.GetComponent<FirstPersonController>();
                var model=damage.Ragdoll.Model;
                Transform Bone(string name)=>model.GetComponentsInChildren<Transform>().First(t=>t.name==name);
                void Hit(bool left) {
                    Physics.SyncTransforms();
                    var shape=model.GetComponentsInChildren<PlayerHitShape>().First(t=>t.transform.parent.name==(left?"Left":"Right")+"Knee").GetComponent<Collider>();
                    var shot=GameObject.CreatePrimitive(PrimitiveType.Cube);shot.transform.position=dummy.transform.position+Vector3.right*10;shot.transform.SetParent(root.transform,true);
                    shot.AddComponent<Rigidbody>().useGravity=false;PhysicalDestructible.Ensure(shot);shot.GetComponent<Rigidbody>().mass=.01f;
                    Check(damage.ReceiveImpact(PlayerBodyPart.Legs,shot.GetComponent<Collider>(),shape.bounds.center,Vector3.right,Vector3.right*100),"moving object damages selected leg");
                }
                for(int stage=1;stage<=3;stage++) {
                    Hit(firstLeft);
                    Check(damage.CanMove&&!damage.SingleLegSupport&&!damage.IsLegDisabled(firstLeft),"first three hits do not cause Catagma");
                    Check(Mathf.Abs(damage.MovementSpeedScale-(1-.25f*stage))<.001f,"movement loses 25 percent per hit");
                    Check(damage.LegAnimationSpeed(firstLeft)==1f&&damage.LegAnimationSpeed(!firstLeft)==1f&&
                        Mathf.Abs(damage.LegAnimationSwingAmount(firstLeft)-Mathf.Max(.5f,1-.2f*stage))<.001f&&
                        damage.LegAnimationSwingAmount(!firstLeft)==1f,
                        "injured leg keeps its cadence and reduces only its swing arc");
                    Check(damage.Ragdoll.Bodies.All(b=>b==null||b.isKinematic),"wounded legs remain animated");
                }
                Hit(firstLeft);
                Check(damage.SingleLegSupport&&damage.CanMove&&!controller.MovementDisabled&&controller.IsCrouching&&!controller.IsSprinting,"one surviving leg keeps crouched locomotion");
                foreach(string joint in new[]{"Hip","Knee","Ankle"}) {
                    Check(Bone((firstLeft?"Left":"Right")+joint).GetComponent<Rigidbody>() is { isKinematic:false },"only struck leg ragdolls: "+joint);
                    var supportBody=Bone((firstLeft?"Right":"Left")+joint).GetComponent<Rigidbody>();
                    Check(supportBody==null||supportBody.isKinematic,"support leg stays animated: "+joint);
                }
                var pelvisBody=Bone("Pelvis").GetComponent<Rigidbody>();
                Check(pelvisBody==null||pelvisBody.isKinematic,"pelvis stays supported until both legs fail");
                avatar.ApplySingleLegSupportPose(1);
                string support=firstLeft?"Right":"Left";
                foreach(var target in new[]{("Hip",60f),("Knee",110f),("Ankle",-70f)})
                    Check(Quaternion.Angle(Bone(support+target.Item1).localRotation,Quaternion.Euler(target.Item2,0,0))<1,"surviving leg takes right crouch pose: "+target.Item1);
                var hip=Bone(support+"Hip");var idle=hip.localRotation;
                var movingBody=dummy.GetComponent<Rigidbody>();movingBody.constraints=RigidbodyConstraints.FreezeRotation;movingBody.linearVelocity=Vector3.forward*.75f;avatar.ApplySingleLegSupportPose(.3f);
                Check(Quaternion.Angle(idle,hip.localRotation)>2,"surviving leg animates a crouch gait while moving");
                var foot=dummy.GetComponentsInChildren<Collider>().First(c=>c.name.StartsWith(firstLeft?"Left":"Right")&&c.name.Contains("Foot BoxCollider"));
                Check(foot.transform.parent==Bone((firstLeft?"Left":"Right")+"Ankle"),"disabled foot follows its physical ankle");
                var replica=TrainingDummy.Create(root.transform,null);replica.Damage.SetReplica(true);replica.Damage.ApplyReplicaState(damage.Capture());
                Check(replica.Damage.SingleLegSupport&&replica.Damage.IsLegDisabled(firstLeft)&&replica.Damage.Capture().legHits[firstLeft?0:1]==4,"room replica keeps independent leg stages");
                var replicaHip=replica.Damage.Ragdoll.Model.GetComponentsInChildren<Transform>().First(t=>t.name==support+"Hip");
                Check(Quaternion.Angle(replicaHip.rotation,hip.rotation)<.1f,"room replica preserves the surviving leg's actual pose");
                for(int i=0;i<4;i++) Hit(!firstLeft);
                Check(!damage.CanMove&&controller.MovementDisabled&&!damage.SingleLegSupport,"both failed legs stop movement and jumping");
                foreach(string name in new[]{"Pelvis","LeftHip","LeftKnee","LeftAnkle","RightHip","RightKnee","RightAnkle"})
                    Check(Bone(name).GetComponent<Rigidbody>() is { isKinematic:false },"entire lower body ragdolls: "+name);
                damage.Respawn();Check(damage.CanMove&&damage.MovementSpeedScale==1&&damage.LegAnimationSpeed(true)==1&&damage.LegAnimationSpeed(false)==1,"respawn restores both leg stages and movement");
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
