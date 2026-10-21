using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Wyvernland.Editor
{
    public static class BodyPersistenceValidation
    {
        static void Check(bool value,string message) { if(!value) throw new InvalidOperationException("Body persistence: "+message); }
        static void Invoke(object value,string method)=>value.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(value,null);
        public static void Preflight()
        {
            HeldStoneMovementValidation.Run();LegDamageStagesValidation.Run();PresentationAndFragmentValidation.Run();ThrowSafetyValidation.Run();InteractionRefinementValidation.Run();RoomThrowAndJointValidation.Run();ExtendedBodyDestructionValidation.Run();Run();PlayerDamageValidation.Run();TrainingDummyValidation.Run();
            RoomObjectPhysicsValidation.Run();HumanRigValidation.RunInteractionFixValidation();PickupResponsivenessValidation.Run();
            PlayerRigColliderValidation.Run();PhysicalDestructionValidation.RunHeldStrikeReaction();SimilarHeldStrikeValidation.Run();
            BrowserAndTileValidation.Run();WebSharedWorldValidation.Run();WebPerformanceValidation.Run();PlayerWorldLoadingValidation.Run();
            Debug.Log("BODY_FIX_PREFLIGHT_PASS");
        }
        public static void Run()
        {
            if(Application.isBatchMode&&string.IsNullOrEmpty(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path)) EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            var previous=UnityEngine.SceneManagement.SceneManager.GetActiveScene();UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            try {
                var terrain=GameObject.CreatePrimitive(PrimitiveType.Cube).AddComponent<PhysicalDestructible>();terrain.IsTerrain=true;terrain.Hardness=5;
                for(int i=0;i<1000;i++) Invoke(terrain,"ApplyCollisionWear");
                Check(terrain.Hardness==5,"terrain cannot lose hardness through repeated collisions");
                var properties=terrain.CaptureNetworkProperties();properties.hardness=1;terrain.ApplyNetworkProperties(properties);
                Check(terrain.Hardness==5,"old network properties cannot reduce terrain hardness");UnityEngine.Object.DestroyImmediate(terrain.gameObject);
                var root=new GameObject("Body world");var dummy=TrainingDummy.Create(root.transform,null);var damage=dummy.Damage;
                var source=GameObject.CreatePrimitive(PrimitiveType.Sphere);source.transform.localScale=Vector3.one*.04f;
                var body=source.AddComponent<Rigidbody>();body.useGravity=false;body.mass=.01f;var physical=source.AddComponent<PhysicalDestructible>();
                var motion=MovingImpactMotion.Ensure(body);
                foreach(var part in new[]{PlayerBodyPart.LeftArm,PlayerBodyPart.RightArm,PlayerBodyPart.Legs,PlayerBodyPart.Torso,PlayerBodyPart.Head}) {
                    damage.Respawn();
                    Collider shape=null;Vector3 direction=Vector3.forward;bool visible=false;
                    foreach(var tag in dummy.GetComponentsInChildren<PlayerHitShape>().Where(t=>t.Part==part&&t.GetComponent<Collider>().enabled)) {
                        shape=tag.GetComponent<Collider>();
                        foreach(var candidate in new[]{Vector3.forward,Vector3.back,Vector3.right,Vector3.left,Vector3.up,Vector3.down}) {
                            body.position=shape.bounds.center+candidate*3;Physics.SyncTransforms();
                            var first=body.SweepTestAll(-candidate,6,QueryTriggerInteraction.Ignore).Where(h=>!h.collider.transform.IsChildOf(source.transform)).OrderBy(h=>h.distance).FirstOrDefault();
                            if(first.collider?.GetComponent<PlayerHitShape>() is {} target&&target.Owner==damage&&target.Part==part) { direction=candidate;visible=true;break; }
                        }
                        if(visible) break;
                    }
                    Check(visible,"fixture finds an unobstructed "+part+" surface");
                    body.position=shape.bounds.center+direction*3;body.linearVelocity=-direction*300;Physics.SyncTransforms();
                    int before=damage.Capture().hit;Invoke(motion,"FixedUpdate");
                    Check(damage.Capture().hit>before,"tiny fast projectile cannot tunnel through "+part);
                    Check(part==PlayerBodyPart.Head?damage.IsDead:part==PlayerBodyPart.Legs?damage.CanMove&&damage.MovementSpeedScale==.75f:part==PlayerBodyPart.Torso?damage.Capture().pits.Length>0:damage.CanUseHand(part==PlayerBodyPart.LeftArm)&&damage.ArmMovementSpeed(part==PlayerBodyPart.LeftArm)==1f&&damage.ArmAnimationSwingAmount(part==PlayerBodyPart.LeftArm)==.8f,"sweep damages the contacted region: "+part);
                }
                damage.Respawn();
                var head=dummy.GetComponentsInChildren<PlayerHitShape>().First(t=>t.Part==PlayerBodyPart.Head).GetComponent<Collider>();
                var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=head.bounds.center+Vector3.up*1.5f;wall.transform.localScale=new Vector3(4,.1f,4);
                body.position=head.bounds.center+Vector3.up*3;body.linearVelocity=Vector3.down*300;Physics.SyncTransforms();Invoke(motion,"FixedUpdate");
                Check(!damage.IsDead,"wall blocks swept damage to head behind it");UnityEngine.Object.DestroyImmediate(wall);
                body.position=head.bounds.center+new Vector3(3,3,0);body.linearVelocity=Vector3.down*300;Physics.SyncTransforms();Invoke(motion,"FixedUpdate");
                Check(!damage.IsDead,"fast near miss cannot cause damage");
                for(int i=0;i<4;i++) {
                    damage.Respawn();Check(damage.ReceiveImpact(PlayerBodyPart.Head,source.GetComponent<Collider>(),head.bounds.center,Vector3.down,Vector3.down*100)&&damage.IsDead,"single head hit fixture");
                }
                var bodies=UnityEngine.Object.FindObjectsByType<PlayerCorpse>(FindObjectsSortMode.None);
                Check(bodies.Length>=4,"more than three deaths preserve old corpses");
                damage.Respawn();var corpse=bodies[0];var pickup=corpse.GetComponent<PickableObject>();
                var hand=new GameObject("Living hand").transform;hand.position=corpse.NearestPoint(corpse.transform.position);
                Check(pickup.RequiresTwoHands&&pickup.PickUp(hand,true,hand.position),"corpse can be picked up using two hands");
                Check(corpse.GetComponent<PlayerRagdoll>().Bodies.All(b=>b==null||!b.isKinematic),"held corpse keeps ragdoll simulation");
                pickup.Release(Vector3.right,null);
                Check(!pickup.IsHeld&&corpse.GetComponent<PlayerRagdoll>().Bodies.All(b=>b==null||!b.isKinematic),"released corpse remains ragdoll");
                foreach(var c in bodies) c.transform.SetParent(root.transform,true);
                var session=new GameObject("Body session").AddComponent<WebWorldSession>();session.transform.SetParent(root.transform,false);
                var init=session.Initialize(root,null);while(init.MoveNext()){}
                session.ReceiveWorld("{\"self\":\"host\",\"host\":\"host\",\"ready\":true}");
                var saved=session.CaptureForValidation(corpse.gameObject);Check(saved.objects[0].hasCorpse&&saved.objects[0].expires==0,"corpses save without expiration");
                var identity=saved.objects[0].id;var corpseName=corpse.name;
                UnityEngine.Object.DestroyImmediate(dummy.gameObject);Check(corpse!=null,"owner removal preserves body and its mesh");
                UnityEngine.Object.DestroyImmediate(root);root=new GameObject("Restored body world");
                session=new GameObject("Restored session").AddComponent<WebWorldSession>();session.transform.SetParent(root.transform,false);
                init=session.Initialize(root,null);while(init.MoveNext()){}
                session.ReceiveWorld(JsonUtility.ToJson(new WebWorldSession.Packet { self="guest",host="host",ready=true,objects=saved.objects }));
                var restored=WebWorldSession.ObjectOf(identity)?.GetComponent<PlayerCorpse>();
                Check(restored!=null&&restored.name==corpseName&&restored.GetComponent<PickableObject>()!=null,"late join reconstructs persistent pickable corpse");
                Check(restored.GetComponent<PlayerRagdoll>().Bodies.All(b=>b==null||b.isKinematic),"guest corpse follows authoritative pose");
                session.ReceiveWorld("{\"self\":\"guest\",\"host\":\"guest\",\"ready\":true}");
                Check(restored.GetComponent<PlayerRagdoll>().Bodies.All(b=>b==null||!b.isKinematic),"new host resumes corpse physics");
                Debug.Log("BODY_PERSISTENCE_VALIDATION_PASS: terrain immunity, all body-region sweeps, wall and near-miss exclusions, unlimited corpses, two-hand ragdoll pickup, owner removal, room reconstruction and authority transfer.");
            } finally { UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true);Time.timeScale=1; }
        }
    }
}
