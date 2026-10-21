using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Wyvernland.Editor
{
    public static class PlayerDamageValidation
    {
        [MenuItem("Tools/Wyvernland/Validate Player Damage")]
        public static void Run()
        {
            var previous=SceneManager.GetActiveScene();
            if(Application.isBatchMode&&string.IsNullOrEmpty(previous.path)) { EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");previous=SceneManager.GetActiveScene(); }
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
            var simulation=Physics.simulationMode;
            try { Physics.simulationMode=SimulationMode.Script;Validate();Debug.Log("PLAYER_DAMAGE_VALIDATION_PASS: source-motion thresholds, three-stage arm wounds, slowdown and retained Catagma grip, joint-driven arm and pelvis/legs, Boolean skinned torso cavity, head shatter/death, retained corpse grip and looting, two-second respawn, replicated bone poses, and non-destructive hard collision sparks."); }
            finally { Physics.simulationMode=simulation;SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true); }
        }
        static void Check(bool condition,string message) { if(!condition) throw new InvalidOperationException("Player damage: "+message); }
        static void Validate()
        {
            var actor=new GameObject("Damage validation player");actor.transform.position=new Vector3(50,100,50);
            actor.AddComponent<Rigidbody>().useGravity=false;
            var view=new GameObject("Camera",typeof(Camera));view.transform.SetParent(actor.transform,false);
            var player=actor.AddComponent<FirstPersonController>();player.Initialize(view.transform);
            var avatar=actor.AddComponent<RiggedPlayerAvatar>();avatar.Initialize(player);
            var damage=actor.GetComponent<PlayerDamage>();var model=actor.transform.Find("Human (Rigged Player)");
            var source=GameObject.CreatePrimitive(PrimitiveType.Cube);source.transform.position=actor.transform.position+Vector3.right*3;
            source.AddComponent<Rigidbody>().useGravity=false;var material=source.AddComponent<PhysicalDestructible>();
            var collider=source.GetComponent<Collider>();float threshold=PlayerDamage.RequiredSpeed(material);
            Check(threshold>=18&&threshold>material.InitialImpactSpeed(1,"Flesh"),"player threshold exceeds ordinary object destruction");
            player.GetComponent<Rigidbody>().linearVelocity=Vector3.right*40;
            Check(!damage.ReceiveImpact(PlayerBodyPart.Head,collider,actor.transform.position,Vector3.right,Vector3.zero),"running into stationary objects cannot hurt");
            player.GetComponent<Rigidbody>().linearVelocity=Vector3.zero;
            Check(!damage.ReceiveImpact(PlayerBodyPart.Head,collider,actor.transform.position,Vector3.right,Vector3.right*(threshold-.1f)),"below-threshold impacts cannot hurt");
            Check(!damage.ReceiveImpact(PlayerBodyPart.Head,collider,actor.transform.position,Vector3.right,Vector3.left*(threshold+1)),"objects travelling away cannot hurt");
            var hand=actor.GetComponentsInChildren<RightHandPickupTrigger>().First(t=>t.name.StartsWith("Right")).transform;
            var item=GameObject.CreatePrimitive(PrimitiveType.Cube);item.transform.localScale=Vector3.one*.08f;item.transform.position=hand.position;
            item.AddComponent<Rigidbody>().mass=1;var pickup=item.AddComponent<PickableObject>();
            Check(avatar.BeginHandPickup(pickup,false,hand.position),"hand starts pickup");avatar.UpdatePickupReach(.1f);
            Check(pickup.IsHeld&&avatar.HasRightHandItem,"arm damage fixture holds an object");
            var armSkin=model.GetComponentsInChildren<SkinnedMeshRenderer>().First(s=>s.name=="HumanBody");var intactArmMesh=armSkin.sharedMesh;
            Check(damage.ReceiveImpact(PlayerBodyPart.RightArm,collider,hand.position,Vector3.right,Vector3.right*(threshold+1)),"fast moving object damages arm");
            Check(pickup.IsHeld&&avatar.CanUseHand(false)&&damage.ArmMovementSpeed(false)==1f&&damage.ArmAnimationSwingAmount(false)==.8f&&damage.ArmAnimationSwingAmount(true)==1f,"first arm wound retains grip and reduces only its swing arc by 20 percent");
            Check(damage.Ragdoll.Bodies.All(b=>b==null||b.isKinematic)&&armSkin.sharedMesh!=intactArmMesh,"first arm destruction makes a real Boolean wound without ragdoll");
            var secondArmHit=UnityEngine.Object.Instantiate(source);var third=UnityEngine.Object.Instantiate(source);
            Check(damage.ReceiveImpact(PlayerBodyPart.RightArm,secondArmHit.GetComponent<Collider>(),hand.position,Vector3.right,Vector3.right*(threshold+1)),"second arm hit");
            Check(pickup.IsHeld&&avatar.CanUseHand(false)&&damage.ArmMovementSpeed(false)==1f&&damage.ArmAnimationSwingAmount(false)==.5f&&damage.Capture().pits.Length==2,"second arm wound retains grip and halves its swing arc");
            Check(damage.ReceiveImpact(PlayerBodyPart.RightArm,third.GetComponent<Collider>(),hand.position,Vector3.right,Vector3.right*(threshold+1)),"third arm hit");
            Check(!avatar.CanUseHand(false)&&avatar.CanUseHand(true)&&
                pickup.IsHeld==avatar.HasRightHandItem&&
                PlayerDamage.ShouldDropHeldItem(.699f)&&!PlayerDamage.ShouldDropHeldItem(.7f),
                "third arm hit causes Catagma and a seventy-percent grip-failure chance");
            Check(damage.Ragdoll.Bodies.Where(b=>b!=null).All(b=>
                    Mathf.Abs(b.maxAngularVelocity-PlayerRagdoll.MaximumJointAngularSpeed)<.001f),
                "ragdoll joints have a bounded rotation speed");
            UnityEngine.Object.DestroyImmediate(secondArmHit);UnityEngine.Object.DestroyImmediate(third);
            Check(!avatar.BeginHandReach(false)&&!avatar.BeginHandPickup(pickup,false),"damaged hand cannot reach or pick up");
            var shoulder=model.GetComponentsInChildren<Transform>().First(t=>t.name=="RightShoulder");
            var elbow=model.GetComponentsInChildren<Transform>().First(t=>t.name=="RightElbow");
            Check(!shoulder.GetComponent<Rigidbody>().isKinematic&&!elbow.GetComponent<Rigidbody>().isKinematic&&shoulder.GetComponent<CharacterJoint>()!=null,"arm physics starts at shoulder with connected joints");
            var before=elbow.rotation;Physics.SyncTransforms();for(int i=0;i<25;i++) sceneSimulate(actor,.02f);
            Check(Quaternion.Angle(before,elbow.rotation)>.1f,"injured arm actually responds to physics");
            var armState=damage.Capture();
            var online=new GameObject("Remote damage fixture").AddComponent<WebPlayerPresence>();online.Initialize(player);
            online.ReceivePlayers(JsonUtility.ToJson(new WebPlayerPresence.Snapshot { players=new[]{new WebPlayerPresence.PlayerState {
                id="damage-peer",x=50,y=100,z=50,grounded=true,damage=armState } } }));
            var remote=online.transform.Find("Online player damage-peer");
            remote.GetComponent<RemotePlayerDamage>().PoseAfterAnimation();
            var remoteElbow=remote.GetComponentsInChildren<Transform>().First(t=>t.name=="RightElbow");
            Check(Vector3.Distance(remoteElbow.position,elbow.position)<.001f&&remoteElbow.GetComponent<Rigidbody>().isKinematic,"remote injured arm follows owner pose without duplicate simulation");
            Check(remote.GetComponent<PlayerRagdoll>().Hand(false)!=null&&remote.GetComponentsInChildren<Transform>().Any(t=>t.name=="Neck"),"remote rigs have real wrist and neck joints for corpse grips and head collision");
            // The replica deliberately overlaps this player for the pose check.
            // Remove it before testing an unobstructed corpse fall.
            UnityEngine.Object.DestroyImmediate(online.gameObject);
            damage.Respawn();
            var confirmed=new PlayerDamage.Impact { id=Guid.NewGuid().ToString("N"),source="d"+Guid.NewGuid().ToString("N"),life=damage.Capture().life,
                part=(int)PlayerBodyPart.LeftArm,point=actor.transform.InverseTransformPoint(hand.position),normal=Vector3.right,velocity=Vector3.right*(threshold+1),threshold=threshold };
            Check(damage.ReceiveNetworkImpact(confirmed),"host-confirmed hit survives a stopped or already shattered projectile");
            Check(damage.CanUseHand(true)&&damage.ArmMovementSpeed(true)==1f&&damage.ArmAnimationSwingAmount(true)==.8f&&!damage.ReceiveNetworkImpact(confirmed),"host impact applies once and shortens the correct arm swing");
            Check(damage.Capture().received.Contains(confirmed.id),"victim acknowledges confirmed hits so the host can retire them");
            damage.Respawn();
            Check(!damage.ReceiveNetworkImpact(confirmed),"delayed hits cannot damage a respawned life");
            foreach(string side in new[]{"Left","Right"}) {
                var kneeShape=model.GetComponentsInChildren<PlayerHitShape>().First(t=>t.transform.parent.name==side+"Knee").GetComponent<Collider>();
                for(int stage=0;stage<4;stage++) {
                    var legSource=UnityEngine.Object.Instantiate(source);
                    Check(damage.ReceiveImpact(PlayerBodyPart.Legs,legSource.GetComponent<Collider>(),kneeShape.bounds.center,Vector3.right,Vector3.right*(threshold+1),side=="Left"?3:4),"staged leg hit accepted");
                    UnityEngine.Object.DestroyImmediate(legSource);
                }
            }
            Check(!damage.CanMove&&player.MovementDisabled,"leg damage prevents movement and jumping");
            foreach(string name in new[]{"Pelvis","LeftHip","RightHip","LeftKnee","RightKnee","LeftAnkle","RightAnkle"})
                Check(!model.GetComponentsInChildren<Transform>().First(t=>t.name==name).GetComponent<Rigidbody>().isKinematic,"lower joint ragdolls: "+name);
            damage.Respawn();
            var skin=model.GetComponentsInChildren<SkinnedMeshRenderer>().First(s=>s.name=="HumanBody");
            var baked=new Mesh();skin.BakeMesh(baked,true);
            Vector3 desired=actor.transform.position+new Vector3(0,1.2f,.15f);
            Vector3 contact=baked.vertices.Select(p=>skin.transform.TransformPoint(p)).OrderBy(p=>(p-desired).sqrMagnitude).First();
            var original=skin.sharedMesh;var owned=new System.Collections.Generic.List<Mesh>();
            Check(PlayerDamage.CutTorso(model,contact,owned),"torso performs a real Boolean subtraction");
            Check(skin.sharedMesh!=original&&skin.sharedMesh.boneWeights.Length==skin.sharedMesh.vertexCount&&skin.sharedMesh.bindposes.Length==original.bindposes.Length,"pit geometry retains complete skin bindings");
            damage.Respawn();foreach(var mesh in owned) UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(baked);
            item.transform.position=hand.position;
            if(!pickup.IsHeld) { Check(avatar.BeginHandPickup(pickup,false,hand.position),"death fixture pickup starts");avatar.UpdatePickupReach(.1f); }
            Check(pickup.IsHeld,"death fixture holds object");
            int headFragments=UnityEngine.Object.FindObjectsByType<BodyFragment>(FindObjectsSortMode.None).Length;
            Check(damage.ReceiveImpact(PlayerBodyPart.Head,collider,contact,Vector3.right,Vector3.right*(threshold+1))&&
                damage.IsDead,"one qualifying head hit kills");
            Check(UnityEngine.Object.FindObjectsByType<BodyFragment>(FindObjectsSortMode.None).Length>headFragments,
                "fatal head hit produces shatter fragments");
            Check(damage.IsDead&&player.IsDead&&PlayerDamage.RespawnDelay==2,"death blocks input until two-second respawn");
            var corpse=UnityEngine.Object.FindFirstObjectByType<PlayerCorpse>();
            Check(corpse!=null&&corpse.GetComponent<PlayerRagdoll>().Bodies.All(b=>b==null||!b.isKinematic),"entire corpse is dynamic ragdoll");
            Check(corpse.GetComponentsInChildren<CharacterJoint>().All(j=>j.connectedBody==null||j.connectedBody.transform.IsChildOf(corpse.transform)),"corpse joints never remain attached to the living player's movement body");
            Check(!pickup.IsHeld,"death releases the living hand grip");
            if(pickup.GetComponent<CorpseGrip>()==null) {
                Check(pickup.GetComponent<FixedJoint>()==null,"failed death grip drops the object");
                corpse.Grip(pickup,true);
            }
            Check(pickup.GetComponent<FixedJoint>()!=null&&pickup.GetComponent<CorpseGrip>()!=null,
                "a surviving or restored corpse grip remains lootable");
            var grip=pickup.GetComponent<FixedJoint>().connectedBody;var gripOffset=grip.transform.InverseTransformPoint(pickup.transform.position);
            float BodyHeight()=>corpse.GetComponent<PlayerRagdoll>().Bodies.Where(b=>b!=null).Sum(b=>b.worldCenterOfMass.y*b.mass)/corpse.GetComponent<PlayerRagdoll>().Bodies.Where(b=>b!=null).Sum(b=>b.mass);
            float corpseHeight=BodyHeight();
            Physics.SyncTransforms();for(int i=0;i<15;i++) sceneSimulate(actor,.02f);
            Check(BodyHeight()<corpseHeight-.04f,"dead body center of mass actually falls under gravity: "+corpseHeight+" -> "+BodyHeight());
            float gripError=Vector3.Distance(pickup.transform.position,grip.transform.TransformPoint(gripOffset));
            Check(gripError<.08f,"corpse retains physical object grip while falling (error "+gripError+" m)");
            var other=new GameObject("Living looter");
            Check(pickup.PickUp(other.transform),"living player can immediately take corpse-held object");
            Check(pickup.GetComponent<FixedJoint>()==null,"looting removes corpse constraint");
            damage.Respawn();Check(!damage.IsDead&&damage.CanMove&&avatar.CanUseHand(false)&&model.gameObject.activeSelf,"respawn restores a healthy animated player");
            Check(CollisionSparks.Qualifies(5.1f,5.1f,4)&&!CollisionSparks.Qualifies(5,9,40)&&!CollisionSparks.Qualifies(9,9,3.9f),"strict hardness and fast impact spark thresholds");
            material.Hardness=7;var second=GameObject.CreatePrimitive(PrimitiveType.Cube).AddComponent<PhysicalDestructible>();second.Hardness=8;
            Check(CollisionSparks.TryEmit(material,second,contact,Vector3.up,5),"non-destructive hard impact spawns sparks");
            Check(!material.HasBeenDestroyed&&!second.HasBeenDestroyed,"sparks do not depend on destruction");
            Check(!CollisionSparks.TryEmit(second,material,contact,Vector3.up,5),"paired collider callbacks do not double sparks");
        }
        static void sceneSimulate(GameObject actor,float dt)=>actor.scene.GetPhysicsScene().Simulate(dt);
    }
}
