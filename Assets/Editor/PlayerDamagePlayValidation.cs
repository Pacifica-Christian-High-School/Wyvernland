using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Wyvernland.Editor
{
    // Separate batch entry point: exercise real collision callbacks and the
    // elapsed-time respawn path, which editor Physics.Simulate cannot dispatch.
    public static class PlayerDamagePlayValidation
    {
        const string Key="Wyvernland.PlayerDamage.PlayValidation";
        static PlayerDamage damage;static int phase;static float started,died;static double deadline;
        static Transform sweepingHand;static PickableObject sweepingItem;static bool kinematicVerified;static int preparedFrame;
        static PlayerCorpse permanentCorpse;
        static PlayerCorpse severedLimb;
        static Rigidbody hostProjectile;static WebPlayerPresence online;
        const string Peer="33333333-3333-4333-8333-333333333333";
        public static void Run()
        {
            HeldStoneMovementValidation.Run();LegDamageStagesValidation.Run();PresentationAndFragmentValidation.Run();ThrowSafetyValidation.Run();InteractionRefinementValidation.Run();RoomThrowAndJointValidation.Run();ExtendedBodyDestructionValidation.Run();BodyPersistenceValidation.Run();PlayerDamageValidation.Run();PhysicalDestructionValidation.RunHeldStrikeReaction();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
        }
        [InitializeOnLoadMethod] static void Resume()
        {
            if(!SessionState.GetBool(Key,false)) return;
            deadline=EditorApplication.timeSinceStartup+90;EditorApplication.update+=Tick;
        }
        static void Tick()
        {
            try {
                if(EditorApplication.timeSinceStartup>deadline) throw new InvalidOperationException("Runtime damage validation timed out");
                if(!EditorApplication.isPlaying) return;
                if(phase==0) {
                    if(Time.deltaTime<=0) return;
                    Time.timeScale=1;
                    var contactFixture=new GameObject("Runtime animated contact validation");
                    try { PhysicalDestructionValidation.ValidateSlowStrikeContact(contactFixture); }
                    finally { UnityEngine.Object.Destroy(contactFixture); }
                    var actor=new GameObject("Runtime damage player");actor.transform.position=new Vector3(50,100,50);
                    var body=actor.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
                    var view=new GameObject("Camera",typeof(Camera));view.transform.SetParent(actor.transform,false);
                    var controller=actor.AddComponent<FirstPersonController>();controller.Initialize(view.transform);controller.enabled=false;
                    var avatar=actor.AddComponent<RiggedPlayerAvatar>();avatar.Initialize(controller);avatar.enabled=false;
                    damage=actor.GetComponent<PlayerDamage>();
                    var head=actor.GetComponentsInChildren<PlayerHitShape>().First(t=>t.Part==PlayerBodyPart.Head).GetComponent<Collider>();
                    var projectile=GameObject.CreatePrimitive(PrimitiveType.Sphere);projectile.transform.localScale=Vector3.one*.012f;
                    projectile.transform.position=head.bounds.center+Vector3.up*4.5f;
                    var moving=projectile.AddComponent<Rigidbody>();moving.useGravity=false;moving.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
                    var material=projectile.AddComponent<PhysicalDestructible>();
                    MovingImpactMotion.Ensure(moving);moving.linearVelocity=Vector3.down*600;
                    Physics.SyncTransforms();started=Time.unscaledTime;phase=1;
                } else if(phase==1) {
                    if(damage.IsDead) { permanentCorpse=UnityEngine.Object.FindObjectsByType<PlayerCorpse>(FindObjectsSortMode.None).First();died=Time.unscaledTime;phase=2; }
                    else if(Time.unscaledTime-started>1.5f) throw new InvalidOperationException("A real fast moving object did not shatter the contacted head");
                } else if(phase==2) {
                    if(!damage.IsDead) {
                        if(Time.unscaledTime-died<1.85f||Time.unscaledTime-died>2.3f||!damage.CanMove) throw new InvalidOperationException("Runtime respawn did not restore the player after two seconds");
                        if(!kinematicVerified) {
                            var head=damage.GetComponentsInChildren<PlayerHitShape>().First(t=>t.Part==PlayerBodyPart.Head).GetComponent<Collider>();
                            sweepingHand=new GameObject("Fast held object hand").transform;sweepingHand.position=head.bounds.center+Vector3.up*4.5f;
                            var item=GameObject.CreatePrimitive(PrimitiveType.Cube);item.transform.localScale=Vector3.one*.012f;item.transform.position=sweepingHand.position;
                            item.AddComponent<Rigidbody>().useGravity=false;PhysicalDestructible.Ensure(item);sweepingItem=item.AddComponent<PickableObject>();
                            if(!sweepingItem.PickUp(sweepingHand)) throw new InvalidOperationException("Held sweep fixture could not pick up object");
                            MovingImpactMotion.Ensure(item.GetComponent<Rigidbody>());sweepingItem.SampleHeldMotion(.02f);preparedFrame=Time.frameCount;phase=5;
                        } else {
                            var shape=damage.GetComponentsInChildren<PlayerHitShape>().First(t=>t.Part==PlayerBodyPart.LeftArm).GetComponent<Collider>();
                            for(int i=0;i<7;i++) {
                                var source=GameObject.CreatePrimitive(PrimitiveType.Cube);source.transform.position=damage.transform.position+Vector3.up*(8+i);
                                source.AddComponent<Rigidbody>().isKinematic=true;PhysicalDestructible.Ensure(source);
                                if(!damage.ReceiveImpact(PlayerBodyPart.LeftArm,source.GetComponent<Collider>(),shape.bounds.center,Vector3.right,Vector3.right*100))
                                    throw new InvalidOperationException("Runtime repeated limb destruction rejected");
                            }
                            severedLimb=PlayerCorpse.Find(damage.Capture().limbs.Single().id);
                            preparedFrame=Time.frameCount;phase=7;
                        }
                    } else if(Time.unscaledTime-died>3f) throw new InvalidOperationException("Player did not respawn");
                } else if(phase==5) {
                    if(Time.frameCount<=preparedFrame+2) return;
                    sweepingHand.position-=Vector3.up*9;sweepingItem.SampleHeldMotion(Mathf.Max(.001f,Time.deltaTime));phase=6;started=Time.unscaledTime;
                } else if(phase==6) {
                    if(damage.IsDead) { sweepingItem.Release(Vector3.zero,sweepingHand);kinematicVerified=true;died=Time.unscaledTime;phase=2; }
                    else if(Time.unscaledTime-started>1.5f) throw new InvalidOperationException("Held object crossed the head between frames without damage");
                } else if(phase==7) {
                    if(Time.frameCount<=preparedFrame+3) return;
                    var rig=severedLimb.GetComponent<PlayerRagdoll>();
                    var bones=rig.Bodies.Where(b=>b!=null).ToArray();
                    if(bones.Length!=3||bones.Any(b=>b.isKinematic)||
                        bones.Count(b=> { var joint=b.GetComponent<CharacterJoint>();return joint!=null&&bones.Contains(joint.connectedBody); })!=2)
                        throw new InvalidOperationException("Severed limb lost articulated joints after deferred runtime cleanup");
                    var hand=new GameObject("Runtime limb pickup hand").transform;hand.position=severedLimb.GripPoint;
                    var pickup=severedLimb.GetComponent<PickableObject>();
                    if(!pickup.PickUp(hand,false,hand.position)||pickup.GrabMode!=PickableGrabMode.Stick)
                        throw new InvalidOperationException("Runtime severed limb cannot be picked up as a stick");
                    pickup.Release(Vector3.right*2,hand);damage.Respawn();phase=4;started=Time.unscaledTime;
                } else if(phase==4) {
                    if(Time.unscaledTime-started<31f) return;
                    if(permanentCorpse==null) throw new InvalidOperationException("Corpse disappeared after the previous 30-second lifetime");
                    var hand=new GameObject("Living corpse pickup hand").transform;hand.position=permanentCorpse.NearestPoint(permanentCorpse.transform.position);
                    var pickup=permanentCorpse.GetComponent<PickableObject>();
                    if(!pickup.PickUp(hand,true,hand.position)||permanentCorpse.GetComponent<PlayerRagdoll>().Bodies.Any(b=>b!=null&&b.isKinematic))
                        throw new InvalidOperationException("Persistent corpse could not be picked up as an articulated ragdoll");
                    pickup.Release(Vector3.zero,null);SetupHostCollision();phase=3;started=Time.unscaledTime;
                } else if(phase==3) {
                    var impact=WebPlayerPresence.CaptureImpacts()?.FirstOrDefault(h=>h.target==Peer);
                    if(impact!=null) {
                        if(impact.part!=(int)PlayerBodyPart.Head||impact.velocity.magnitude<impact.threshold||impact.life!=17)
                            throw new InvalidOperationException("Host collision did not preserve head identity, victim life and pre-solver source motion");
                        if(hostProjectile.linearVelocity.magnitude>=impact.threshold) throw new InvalidOperationException("Stopped projectile fixture did not actually stop at the remote avatar");
                        impact.target=WebWorldSession.LocalPlayerId;impact.life=damage.Capture().life;
                        online.ReceivePlayers(JsonUtility.ToJson(new WebPlayerPresence.Snapshot { players=new[]{new WebPlayerPresence.PlayerState {
                            id="impact-host",x=70,y=100,z=50,damage=new PlayerDamage.State { impacts=new[]{impact} } } } }));
                        if(!damage.IsDead) throw new InvalidOperationException("Victim did not receive the host-confirmed stopped-projectile impact");
                        Debug.Log("PLAYER_DAMAGE_PLAY_VALIDATION_PASS: tiny projectile crossing 12 meters per step, held object crossing nine meters between frames, persistent corpse beyond 30 seconds, two-handed ragdoll pickup, automatic two-second respawn, and a host-confirmed head hit that reaches the victim after the projectile stops.");Finish(0);
                    } else if(Time.unscaledTime-started>2f) throw new InvalidOperationException("Host did not report projectile collision. Position="+hostProjectile.position+", velocity="+hostProjectile.linearVelocity+", kinematic="+hostProjectile.isKinematic+", id="+WebWorldSession.IdOf(hostProjectile.gameObject));
                }
            } catch(Exception exception) { Debug.LogException(exception);Finish(1); }
        }
        static void SetupHostCollision()
        {
            online=new GameObject("Runtime host presence").AddComponent<WebPlayerPresence>();online.Initialize(damage.GetComponent<FirstPersonController>());
            online.ReceivePlayers(JsonUtility.ToJson(new WebPlayerPresence.Snapshot { players=new[]{new WebPlayerPresence.PlayerState {
                id=Peer,x=50,y=100,z=55,grounded=true,damage=new PlayerDamage.State { life=17 } } } }));
            var remote=online.transform.Find("Online player "+Peer);
            var head=remote.GetComponentsInChildren<PlayerHitShape>().First(t=>t.Part==PlayerBodyPart.Head).GetComponent<Collider>();
            var projectile=GameObject.CreatePrimitive(PrimitiveType.Sphere);projectile.name="Host projectile";projectile.transform.localScale=Vector3.one*.12f;
            projectile.transform.position=head.bounds.center+Vector3.up*(head.bounds.extents.y+.2f);
            hostProjectile=projectile.AddComponent<Rigidbody>();hostProjectile.useGravity=false;hostProjectile.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            var material=projectile.AddComponent<PhysicalDestructible>();
            var world=new GameObject("Runtime shared world").AddComponent<WebWorldSession>();
            var initialize=world.Initialize(projectile,null);while(initialize.MoveNext()) {}
            world.ReceiveWorld(JsonUtility.ToJson(new WebWorldSession.Packet { self="host",host="host",ready=true }));
            hostProjectile.linearVelocity=Vector3.down*(PlayerDamage.RequiredSpeed(material)+8);Physics.SyncTransforms();
        }
        static void Finish(int code) { SessionState.EraseBool(Key);EditorApplication.update-=Tick;EditorApplication.Exit(code); }
    }
}
