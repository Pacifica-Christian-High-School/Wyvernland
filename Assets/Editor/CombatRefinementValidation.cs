using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Wyvernland.Editor
{
    public static class CombatRefinementValidation
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Check(bool value,string reason) { if(!value) throw new InvalidOperationException("Combat refinement: "+reason); }
        static void Set(object value,string field,object data)=>value.GetType().GetField(field,Private).SetValue(value,data);
        static T Read<T>(object value,string field)=>(T)value.GetType().GetField(field,Private).GetValue(value);
        public static void Run()
        {
            var previous=SceneManager.GetActiveScene();
            if(string.IsNullOrEmpty(previous.path)) { EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");previous=SceneManager.GetActiveScene(); }
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
            try { ValidateBody();ValidateRoom(); }
            finally { SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true); }
            Debug.Log("COMBAT_REFINEMENT_PASS: limb/torso collision pairs, exclusive severed skin weights, unified non-stick grip in both hands, 1.7–5s fatigue and reset, mass/speed shock, shared NPC/bar reconstruction, guest spawn requests and host transfer.");
        }
        static void ValidateBody()
        {
            var root=new GameObject("Combat body fixture");var dummy=TrainingDummy.Create(root.transform,null);
            try {
                var damage=dummy.Damage;var rig=damage.Ragdoll;var avatar=dummy.GetComponent<RiggedPlayerAvatar>();
                Collider Shape(string bone)=>rig.Model.GetComponentsInChildren<Collider>().First(c=>c.transform.parent.name==bone&&!c.isTrigger);
                rig.SetInjury(PlayerInjury.LeftArm);
                Check(Physics.GetIgnoreCollision(Shape("LeftShoulder"),Shape("Waist")),"connected arm and torso cannot launch the body apart");
                Check(rig.Bodies.Where(b=>b!=null).Select(b=>b.GetComponent<CharacterJoint>()).Where(j=>j!=null).All(j=>!j.enableCollision),"connected joints do not self-depenetrate");
                Check(rig.Bodies.Where(b=>b!=null).All(b=>b.maxAngularVelocity<=.9f),"ragdoll rotation is reduced by seventy percent");
                rig.SetFullBodyStun(true);
                Check(!Physics.GetIgnoreCollision(Shape("LeftShoulder"),Shape("RightShoulder")),"opposite ragdoll arms still collide");
                Check(!Physics.GetIgnoreCollision(Shape("LeftElbow"),Shape("Waist")),"nonadjacent arm and torso still collide");
                Check(rig.Bodies.Where(b=>b!=null).Select(b=>b.GetComponent<CharacterJoint>()).Where(j=>j!=null).All(j=>!j.autoConfigureConnectedAnchor),
                    "ragdoll joints keep their original local attachment positions");
                Check(!Physics.GetIgnoreCollision(Shape("LeftHip"),Shape("RightHip")),"opposite legs collide");
                Check(!Physics.GetIgnoreLayerCollision(Shape("LeftHip").gameObject.layer,Shape("RightHip").gameObject.layer),"collision layers permit self contact");
                rig.SetFullBodyStun(false);rig.SetInjury(PlayerInjury.None);
                foreach(var skin in rig.Model.GetComponentsInChildren<SkinnedMeshRenderer>())
                for(int limb=1;limb<=4;limb++) foreach(bool keep in new[]{true,false}) {
                    Mesh cut=PlayerDamageMesh.LimbMesh(skin,limb,keep);if(cut==null) continue;
                    Check(cut.vertexCount==cut.triangles.Distinct().Count(),"severed meshes contain no invisible vertices for collider fitting");
                    var weights=cut.boneWeights;
                    foreach(int index in cut.triangles.Distinct()) {
                        var w=weights[index];
                        foreach(var influence in new[]{(w.boneIndex0,w.weight0),(w.boneIndex1,w.weight1),(w.boneIndex2,w.weight2),(w.boneIndex3,w.weight3)})
                            if(influence.Item2>.00001f) Check(PlayerDamageMesh.InLimb(skin.bones[influence.Item1].name,limb)==keep,"no triangle remains weighted across its severed boundary");
                    }
                    UnityEngine.Object.DestroyImmediate(cut);
                }
                var item=GameObject.CreatePrimitive(PrimitiveType.Cube).AddComponent<PickableObject>();
                foreach(bool left in new[]{false,true}) {
                    Set(avatar,left?"heldLeftHandItem":"heldRightHandItem",item);Set(avatar,"punchUsingLeftHand",left);
                    var digits=Read<Transform[]>(avatar,left?"leftGripDigits":"punchDigits");
                    Set(item,"grabMode",PickableGrabMode.Regular);Set(item,"label",PickableObjectLabel.Irregular);
                    typeof(RiggedPlayerAvatar).GetMethod("ApplyHeldPunchFingerPose",Private).Invoke(avatar,null);
                    var stonePose=digits.Select(d=>d!=null?d.localRotation:Quaternion.identity).ToArray();
                    foreach(var mode in new[]{PickableGrabMode.Regular,PickableGrabMode.Rope}) {
                        Set(item,"label",PickableObjectLabel.Regular);Set(item,"grabMode",mode);
                        foreach(var digit in digits) if(digit!=null) digit.localRotation=Quaternion.identity;
                        typeof(RiggedPlayerAvatar).GetMethod("ApplyHeldPunchFingerPose",Private).Invoke(avatar,null);
                        for(int i=0;i<digits.Length;i++) if(digits[i]!=null) Check(Quaternion.Angle(stonePose[i],digits[i].localRotation)<.01f,"all non-sticks retain the stone grip");
                    }
                    Set(avatar,left?"heldLeftHandItem":"heldRightHandItem",null);
                }
                UnityEngine.Object.DestroyImmediate(item.gameObject);
                var leftItem=GameObject.CreatePrimitive(PrimitiveType.Cube).AddComponent<PickableObject>();
                var rightItem=GameObject.CreatePrimitive(PrimitiveType.Cube).AddComponent<PickableObject>();
                Set(avatar,"heldLeftHandItem",leftItem);Set(avatar,"heldRightHandItem",rightItem);
                Check(avatar.BeginRightHandStrike()&&avatar.BeginLeftHandStrike(),"both hands enter held strike at once");
                Check(avatar.IsHoldingStrikePose(leftItem)&&avatar.IsHoldingStrikePose(rightItem),"both grips hold their strike poses");
                avatar.UpdateHandStrikeInput(true,true);
                Check(avatar.IsStrikingHeldItem(rightItem),"releasing left strike keeps right strike active");
                avatar.CancelAllStrikes();Set(avatar,"heldLeftHandItem",null);Set(avatar,"heldRightHandItem",null);
                UnityEngine.Object.DestroyImmediate(leftItem.gameObject);UnityEngine.Object.DestroyImmediate(rightItem.gameObject);
                foreach(bool left in new[]{true,false}) {
                    var shoulder=Read<Transform>(avatar,left?"leftShoulder":"rightShoulder");
                    var elbow=Read<Transform>(avatar,left?"leftElbow":"rightElbow");
                    var wrist=Read<Transform>(avatar,left?"leftHandMarker":"rightHandMarker");
                    var shoulderRest=Read<Quaternion>(avatar,left?"leftShoulderRestRotation":"rightShoulderRestRotation");
                    var elbowRest=Read<Quaternion>(avatar,left?"leftElbowRestRotation":"rightElbowRestRotation");
                    var wristRest=Read<Quaternion>(avatar,left?"leftHandRestRotation":"rightHandRestRotation");
                    var elbowPosition=Read<Vector3>(avatar,left?"leftElbowRestPosition":"rightElbowRestPosition");
                    var wristPosition=left?Read<Vector3>(avatar,"leftHandRestPosition"):
                        (Vector3)typeof(RiggedPlayerAvatar).GetField("RightHandPointOnElbow",
                            BindingFlags.Static|Private).GetValue(null);
                    shoulder.localRotation=shoulderRest*Quaternion.Euler(180,0,0);
                    elbow.localRotation=elbowRest*Quaternion.Euler(180,0,0);
                    wrist.localRotation=wristRest*Quaternion.Euler(180,0,0);
                    wrist.localPosition=wristPosition+Vector3.one;
                    elbow.localPosition=elbowPosition+Vector3.one;
                    typeof(RiggedPlayerAvatar).GetMethod("LimitStoppedArmPose",Private).Invoke(avatar,new object[]{left});
                    Check(Quaternion.Angle(shoulderRest,shoulder.localRotation)<=105.01f&&
                        Quaternion.Angle(elbowRest,elbow.localRotation)<=135.01f&&
                        Quaternion.Angle(wristRest,wrist.localRotation)<=65.01f&&
                        Vector3.Distance(elbowPosition,elbow.localPosition)<=.1201f&&
                        Vector3.Distance(wristPosition,wrist.localPosition)<=.3501f,
                        "stopped waving limits every joint rotation and local displacement in both arms");
                    var shoulderPosition=Read<Vector3>(avatar,left?"leftShoulderRestPosition":"rightShoulderRestPosition");
                    var handPosition=wristPosition;
                    shoulder.localPosition=shoulderPosition;shoulder.localRotation=shoulderRest;
                    elbow.localPosition=elbowPosition;elbow.localRotation=elbowRest;
                    wrist.localPosition=handPosition;wrist.localRotation=wristRest;
                    Vector3 start=wrist.position;
                    Set(avatar,"strikeUsingLeftHand",left);
                    Set(avatar,"strikeReboundStart",start);
                    Set(avatar,"strikeReboundVelocity",Vector3.right*1000f);
                    Set(avatar,"strikeReboundShoulder",shoulderRest);
                    Set(avatar,"strikeReboundElbow",elbowRest);
                    Set(avatar,"strikeReboundWrist",wristRest);
                    typeof(RiggedPlayerAvatar).GetMethod("ApplyStrikeReboundPose",Private).Invoke(avatar,
                        new object[]{shoulder,elbow,wrist,.06f});
                    Check(Vector3.Distance(shoulderPosition,shoulder.localPosition)<.0001f&&
                        Vector3.Distance(elbowPosition,elbow.localPosition)<.0001f&&
                        Vector3.Distance(handPosition,wrist.localPosition)<.0001f&&
                        Vector3.Distance(start,wrist.position)<.25f,
                        "rebound caps hand travel and preserves arm joint local positions");
                }
                Set(avatar,"striking",true);Set(avatar,"strikeAttackHeld",true);
                avatar.UpdateStrikeFatigue(400,1f,10f);Check(Mathf.Abs(avatar.StrikeWavingSpeedMultiplier-1f)<.001f,"slow object motion does not build fatigue");
                avatar.UpdateStrikeFatigue(400,4f,1.7f);Check(Mathf.Abs(avatar.StrikeWavingSpeedMultiplier-1f)<.001f,"no fatigue before 1.7 seconds of fast waving");
                avatar.UpdateStrikeFatigue(400,4f,1.65f);Check(Mathf.Abs(avatar.StrikeWavingSpeedMultiplier-.65f)<.002f,"fatigue slows smoothly midway");
                avatar.UpdateStrikeFatigue(400,4f,1.65f);Check(Mathf.Abs(avatar.StrikeWavingSpeedMultiplier-.3f)<.001f,"five seconds of fast waving caps speed at thirty percent");
                avatar.UpdateStrikeFatigue(0,4f,10);Check(Mathf.Abs(avatar.StrikeWavingSpeedMultiplier-.3f)<.001f,"slow input does not add or reset fatigue");
                avatar.CancelRightHandStrike();Check(avatar.StrikeWavingSpeedMultiplier==1f,"release resets speed");
                Check(PlayerDamage.ShockImpulse(10,10)>PlayerDamage.ShockImpulse(1,10)&&PlayerDamage.ShockImpulse(10,20)>PlayerDamage.ShockImpulse(10,10),"shock grows with incoming mass and speed");
                Check(PlayerDamage.KnockdownImpulse==428f&&
                    !PlayerDamage.CanEnterImpactRagdoll(100f,Vector3.right*30f,Vector3.right)&&
                    !PlayerDamage.CanEnterImpactRagdoll(101f,Vector3.right*20f,Vector3.right)&&
                    PlayerDamage.CanEnterImpactRagdoll(101f,Vector3.right*20.1f,Vector3.right),
                    "only objects over 100 kg moving faster than 20 m/s trigger full ragdoll");
                Check(PlayerDamage.KnockdownSeconds(428)==2&&PlayerDamage.KnockdownSeconds(1000)==4,"knockdown is bounded to two through four seconds");
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        static void ValidateRoom()
        {
            var root=new GameObject("NPC room");
            WebWorldSession Room(GameObject node) { var room=node.AddComponent<WebWorldSession>();var init=room.Initialize(node,null);while(init.MoveNext()){}return room; }
            try {
                var host=Room(root);host.ReceiveWorld("{\"self\":\"host\",\"host\":\"host\",\"ready\":true}");
                WebWorldSession.SpawnCombatNpc(new Vector3(3000,1000,0),"regression-spawn");
                var npc=root.GetComponentInChildren<CombatNpc>();Check(npc!=null,"host creates NPC");
                var locomotion=npc.GetComponent<FirstPersonController>();
                locomotion.DriveAi(Vector2.up,Vector2.zero,true);Check(locomotion.IsSprinting,"NPC uses sprint locomotion and its shared walk/sprint animation");
                locomotion.DriveAi(Vector2.up,Vector2.zero,false);Check(!locomotion.IsSprinting,"NPC can return to walking");
                var weapon=npc.GetComponent<RiggedPlayerAvatar>().HeldRightHandItem;
                Check(weapon.GetComponent<TestGroundIronBar>()==null,"NPC equipment is excluded from replacement Test Ground bar stock");
                Check(weapon!=null&&weapon.GrabMode==PickableGrabMode.Stick&&weapon.GetComponent<PhysicalDestructible>().MaterialName=="Iron","NPC starts with a right-hand iron bar");
                WebWorldSession.SpawnCombatNpc(new Vector3(3000,1000,0),"regression-spawn");Check(root.GetComponentsInChildren<CombatNpc>().Length==1,"replayed spawn identity creates only one NPC");
                var actor=host.CaptureForValidation(npc.gameObject);var bar=host.CaptureForValidation(weapon.gameObject);
                Check(actor.objects[0].hasNpc&&actor.objects[0].dummy.damage.fullPose,"NPC pose is replicated");
                Check(bar.objects[0].npcGrip==actor.objects[0].id&&!bar.objects[0].held,"NPC grip does not masquerade as a human ownership lease");
                var states=actor.objects.Concat(bar.objects).ToArray();foreach(var state in states) state.revision=1;
                UnityEngine.Object.DestroyImmediate(root);root=new GameObject("NPC guest");var guest=Room(root);
                guest.ReceiveWorld(JsonUtility.ToJson(new WebWorldSession.Packet { self="guest",host="host",ready=true,objects=states,meshes=actor.meshes.Concat(bar.meshes).ToArray() }));
                var restored=WebWorldSession.ObjectOf(states[0].id).GetComponent<CombatNpc>();
                var restoredBar=WebWorldSession.ObjectOf(bar.objects[0].id).GetComponent<PickableObject>();
                Check(restored!=null&&!restored.enabled&&restoredBar.IsHeld&&restoredBar.NpcOwner==restored,"guest restores NPC and its real grip without running AI");
                WebWorldSession.Request request=null;guest.RequestForValidation+=value=>request=value;
                WebWorldSession.SpawnCombatNpc(Vector3.one);
                Check(request?.command?.type=="spawn-npc","guest P spawn forwards to host");
                states[0].dummy.damage.shockPhase=1;states[0].dummy.damage.shockRemaining=2.8f;
                foreach(var state in states) state.revision=2;
                guest.ReceiveWorld(JsonUtility.ToJson(new WebWorldSession.Packet { self="guest",host="host",ready=true,objects=states }));
                Check(restoredBar.IsHeld&&restoredBar.NpcOwner==restored,"replicated knockdown retains NPC equipment");
                guest.ReceiveWorld("{\"self\":\"guest\",\"host\":\"guest\",\"ready\":true}");
                Check(restored.enabled&&restored.GetComponent<PlayerDamage>().IsShocked,"new host resumes NPC simulation and remaining knockdown");
                var resumed=restored.GetComponent<PlayerDamage>();resumed.TickShock(3f);resumed.TickShock(1.3f);
                Check(!resumed.IsShocked&&resumed.CanMove,"host handoff completes the remaining get-up");
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        const string PlayKey="Wyvernland.CombatRefinementPlay";
        static double deadline;
        static GameObject playRoot;
        static PlayerDamage shockVictim,attackVictim;
        static CombatNpc attacker;
        static float phaseStarted;
        static bool sawRagdoll,sawRecovery,sawStrike,sawImpact;
        static int phase;
        public static void RunAll() { Run();BodyPersistenceValidation.Preflight();HeldWeaponSwingValidation.Run();CorpseDestructionValidation.Run();SmallFragmentRestValidation.Run();RunPlay(); }
        public static void RunPlay()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            SessionState.SetBool(PlayKey,true);EditorApplication.isPlaying=true;
        }
        [InitializeOnLoadMethod] static void ResumePlay()
        {
            if(!SessionState.GetBool(PlayKey,false)) return;
            deadline=EditorApplication.timeSinceStartup+120;EditorApplication.update+=PlayTick;
        }
        static PlayerDamage Human(Vector3 position)
        {
            var dummy=TrainingDummy.Create(playRoot.transform,null,mobile:true);var damage=dummy.Damage;
            dummy.transform.position=position;dummy.GetComponent<FirstPersonController>().ConfigureAiActor();
            UnityEngine.Object.DestroyImmediate(dummy);return damage;
        }
        static void PlayTick()
        {
            try {
                if(EditorApplication.timeSinceStartup>deadline) throw new InvalidOperationException("Combat Play validation timed out (phase="+phase+", game time="+Time.time+", delta="+Time.deltaTime+", playing="+Application.isPlaying+")");
                if(!Application.isPlaying) return;
                Time.timeScale=1;
                if(Time.deltaTime<=0) return;
                if(playRoot==null) {
                    Time.timeScale=1;playRoot=new GameObject("Combat Play fixture");playRoot.AddComponent<HeldObjectUpdates>();
                    var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.SetParent(playRoot.transform,false);
                    floor.transform.localScale=new Vector3(800,.2f,40);floor.transform.position=new Vector3(150,-.1f,0);
                    var uprightVictim=Human(new Vector3(310,.15f,0));
                    uprightVictim.ReceiveShock(100,Vector3.right*30f,Vector3.right,uprightVictim.transform.position+Vector3.up);
                    uprightVictim.ReceiveShock(101,Vector3.right*20f,Vector3.right,uprightVictim.transform.position+Vector3.up);
                    Check(!uprightVictim.IsShocked,"mass and speed boundaries do not ragdoll living actors");
                    shockVictim=Human(new Vector3(300,.15f,0));
                    var heldAvatar=shockVictim.GetComponent<RiggedPlayerAvatar>();
                    var heldItem=GameObject.CreatePrimitive(PrimitiveType.Cube);heldItem.transform.SetParent(playRoot.transform);
                    heldItem.transform.localScale=Vector3.one*.08f;heldItem.transform.position=heldAvatar.NpcRightHand.position;
                    heldItem.AddComponent<Rigidbody>().mass=1f;var held=heldItem.AddComponent<PickableObject>();
                    Check(held.PickUp(heldAvatar.NpcRightHand),"living ragdoll fixture holds an object");heldAvatar.BindNpcWeapon(held);
                    shockVictim.ReceiveShock(101,Vector3.right*20.1f,Vector3.right,shockVictim.transform.position+Vector3.up);
                    Check(shockVictim.IsShocked&&held.IsHeld&&heldAvatar.HeldRightHandItem==held,
                        "qualifying impact ragdolls without applying shock force or dropping held object");
                    phaseStarted=Time.time;phase=1;return;
                }
                if(phase==1) {
                    sawRagdoll|=shockVictim.ShockPhase==1;sawRecovery|=shockVictim.ShockPhase==2;
                    foreach(var body in shockVictim.Ragdoll.Bodies) if(body!=null)
                        Check(float.IsFinite(body.position.sqrMagnitude)&&body.linearVelocity.magnitude<150f,"self-colliding shock ragdoll stays stable");
                    if(Time.time-phaseStarted<6f) return;
                    Check(sawRagdoll&&sawRecovery&&!shockVictim.IsShocked&&shockVictim.CanMove,"actual timed knockdown transitions through gradual recovery to control");
                    Check(shockVictim.Ragdoll.Bodies.Where(b=>b!=null).All(b=>b.isKinematic),"recovered uninjured bones return to animation");
                    Check(shockVictim.GetComponent<RiggedPlayerAvatar>().HeldRightHandItem?.IsHeld==true,"living actor still holds object after recovery");
                    attackVictim=Human(new Vector3(0,.15f,0));attacker=CombatNpc.Create(playRoot.transform,new Vector3(0,.15f,-1.8f));
                    Check(attacker.FindNearestPlayer()==attackVictim.transform,"NPC targets nearest actual player");
                    var npcWeapon=attacker.GetComponent<RiggedPlayerAvatar>().HeldRightHandItem;
                    attacker.GetComponent<PlayerDamage>().ReceiveShock(101,Vector3.right*20.1f,Vector3.right,attacker.transform.position+Vector3.up);
                    Check(attacker.GetComponent<PlayerDamage>().IsShocked&&npcWeapon!=null&&npcWeapon.IsHeld&&npcWeapon.NpcOwner==attacker,
                        "NPC retains right-hand iron bar during ragdoll");
                    phaseStarted=Time.time;phase=2;return;
                }
                if(phase==2) {
                    sawStrike|=attacker.GetComponent<RiggedPlayerAvatar>().IsStrikeMode;
                    sawImpact|=attackVictim.Capture().hit>0;
                    if(Time.time-phaseStarted<9f) return;
                    Check(sawStrike,"NPC actually enters held-strike mode");
                    Check(sawImpact,
                        "NPC weapon really collides and impacts the nearest player (bar speed="+attacker.GetComponent<RiggedPlayerAvatar>().HeldRightHandItem?.HeldSpeed+")");
                    var bar=attacker.GetComponent<RiggedPlayerAvatar>().HeldRightHandItem;
                    Check(bar!=null&&bar.IsHeld,"NPC still holds weapon after ragdoll recovery and attack");
                    var lethalSource=GameObject.CreatePrimitive(PrimitiveType.Cube);
                    lethalSource.transform.SetParent(playRoot.transform);lethalSource.AddComponent<Rigidbody>().mass=1f;
                    var lethalProperties=lethalSource.AddComponent<PhysicalDestructible>();
                    float lethalSpeed=Mathf.Max(40f,PlayerDamage.RequiredSpeed(lethalProperties)+1f);
                    Check(attacker.GetComponent<PlayerDamage>().ReceiveImpact(PlayerBodyPart.Head,
                        lethalSource.GetComponent<Collider>(),attacker.transform.position+Vector3.up,
                        Vector3.right,Vector3.right*lethalSpeed)&&!attacker.GetComponent<PlayerDamage>().IsDead,
                        "first NPC head hit separates a face without killing");
                    var secondLethal=UnityEngine.Object.Instantiate(lethalSource);secondLethal.transform.position+=Vector3.up;
                    Check(attacker.GetComponent<PlayerDamage>().ReceiveImpact(PlayerBodyPart.Head,
                        secondLethal.GetComponent<Collider>(),attacker.transform.position+Vector3.up,
                        Vector3.right,Vector3.right*lethalSpeed),"second NPC head hit creates a dead ragdoll");
                    Check(attacker.GetComponent<PlayerDamage>().IsDead&&!bar.IsHeld&&
                        (bar.GetComponent<CorpseGrip>()?.IsAttached??false)==
                        (bar.GetComponent<CorpseGrip>()!=null),
                        "dead NPC releases its bar, retaining it only when the grip survives");
                    Debug.Log("COMBAT_REFINEMENT_PLAY_PASS: stable self-colliding full-body knockdown, 2–4 second stun, gradual get-up, restored control, nearest-player AI and real iron-bar strike impacts.");FinishPlay(0);
                }
            } catch(Exception exception) { Debug.LogException(exception);FinishPlay(1); }
        }
        static void FinishPlay(int result)
        { SessionState.SetBool(PlayKey,false);EditorApplication.update-=PlayTick;EditorApplication.Exit(result); }
    }
}
