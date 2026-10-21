using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Wyvernland.Editor
{
    public static class RoomThrowAndJointValidation
    {
        static void Check(bool value,string message) { if(!value) throw new InvalidOperationException("Room throw/joints: "+message); }
        static WebWorldSession.ObjectState Copy(WebWorldSession.ObjectState state)=>JsonUtility.FromJson<WebWorldSession.ObjectState>(JsonUtility.ToJson(state));
        static void Packet(WebWorldSession session,bool host,WebWorldSession.ObjectState state=null,WebWorldSession.Claim claim=null,WebWorldSession.ReleaseAck ack=null)
            =>session.ReceiveWorld(JsonUtility.ToJson(new WebWorldSession.Packet { self="self",host=host?"self":"host",ready=true,
                objects=state==null?null:new[]{state},claims=claim==null?null:new[]{claim},releases=ack==null?null:new[]{ack} }));
        static WebWorldSession Session(GameObject root,bool host)
        {
            var node=new GameObject("Shared session");node.transform.SetParent(root.transform,false);var session=node.AddComponent<WebWorldSession>();
            IEnumerator init=session.Initialize(root,null);while(init.MoveNext()){} Packet(session,host);return session;
        }
        public static void Run()
        {
            var previous=SceneManager.GetActiveScene();if(string.IsNullOrEmpty(previous.path)) { EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");previous=SceneManager.GetActiveScene(); }
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
            var mode=Physics.simulationMode;Physics.simulationMode=SimulationMode.Script;
            try {
                ValidateThrow(true);ValidateThrow(false);ValidateGuestLaunch();ValidateJoints();ValidateBars();
                Debug.Log("ROOM_THROW_AND_JOINT_VALIDATION_PASS: host and guest throws keep flight across stale held snapshots, release acknowledgements and host handoff; joint-specific limits resist forced hyperextension; Test Ground bars follow room player count and host authority.");
            } finally { Physics.simulationMode=mode;SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true);Time.timeScale=1; }
        }
        static void ValidateThrow(bool host)
        {
            var root=new GameObject("Throw fixture");var item=GameObject.CreatePrimitive(PrimitiveType.Cube);item.transform.SetParent(root.transform,false);item.transform.position=new Vector3(3000,1000,3000);item.transform.localScale=Vector3.one*.1f;
            var body=item.AddComponent<Rigidbody>();var pickup=item.AddComponent<PickableObject>();var hand=new GameObject("Living hand").transform;hand.SetParent(root.transform,false);hand.position=item.transform.position;
            try {
                var session=Session(root,host);var requests=new List<WebWorldSession.Request>();session.RequestForValidation+=requests.Add;
                Check(pickup.PickUp(hand),"pickup fixture");var id=WebWorldSession.IdOf(item);
                Packet(session,host,claim:new WebWorldSession.Claim { id=id,owned=true });
                var stale=Copy(session.CaptureForValidation(item).objects[0]);stale.holder="self";stale.writer="self";stale.revision=1;
                var launch=item.transform.position;pickup.Release(new Vector3(12,8,3),hand);
                var released=Copy(requests.Last().state);Check(released.releaseId?.Length==32,"throw has unique acknowledgement token");
                Check(!body.isKinematic&&body.linearVelocity.x>11.9f,"host and guest show flight immediately");
                Physics.SyncTransforms();for(int i=0;i<15;i++) item.scene.GetPhysicsScene().Simulate(.02f);
                Check(body.position.x>launch.x+3&&body.position.y>launch.y+1,"real projectile travels away from the hand");
                var inFlight=body.position;Packet(session,host,stale);
                typeof(WebWorldSession).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(session,null);
                Check(!body.isKinematic&&Vector3.Distance(body.position,inFlight)<.01f,"stale held echo cannot freeze or rewind the throw");
                int before=requests.Count;session.MaintainOwnedObjects(Time.unscaledTime+3);
                Check(requests.Count==before,"stale held echo cannot manufacture a zero-speed second release");
                Packet(session,host,ack:new WebWorldSession.ReleaseAck { id=id,releaseId=released.releaseId,accepted=true,revision=2 });
                released.revision=2;released.writer="self";Packet(session,host,released);
                Check(!body.isKinematic,"HTTP release echo does not stop predicted flight");
                if(!host) {
                    var authoritative=Copy(released);authoritative.p=new[]{inFlight.x,inFlight.y,inFlight.z};authoritative.writer="host";authoritative.revision=3;
                    Packet(session,false,authoritative);Check(body.isKinematic,"guest hands simulation back to host only after host flight arrives");
                }
            } finally { if(item!=null) item.transform.SetParent(root.transform,true);UnityEngine.Object.DestroyImmediate(root); }
        }
        static void ValidateGuestLaunch()
        {
            var root=new GameObject("Host launch fixture");var item=GameObject.CreatePrimitive(PrimitiveType.Cube);item.transform.SetParent(root.transform,false);item.transform.position=new Vector3(3100,1000,3100);item.transform.localScale=Vector3.one*.1f;var body=item.AddComponent<Rigidbody>();item.AddComponent<PickableObject>();
            try {
                var session=Session(root,true);var held=Copy(session.CaptureForValidation(item).objects[0]);held.holder="guest";held.writer="guest";held.held=true;held.kinematic=true;held.revision=1;Packet(session,true,held);
                var release=Copy(held);release.held=false;release.holder="";release.kinematic=false;release.gravity=true;release.releaseId=Guid.NewGuid().ToString("N");release.revision=2;release.p[0]+=2;release.v=new[]{20f,8f,0f};
                Packet(session,true,release);
                Check(Mathf.Abs(body.position.x-release.p[0])<.001f&&body.linearVelocity.x>19.9f,"host launches from exact guest release point and velocity: "+body.position+" / "+body.linearVelocity+" expected X="+release.p[0]);
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        static void ValidateJoints()
        {
            var root=new GameObject("Joint limits fixture");
            try {
                var dummy=TrainingDummy.Create(root.transform,null);dummy.transform.position+=Vector3.up*1000;
                var rig=dummy.Damage.Ragdoll;var axes=(List<Vector3>)typeof(PlayerRagdoll).GetField("jointAxes",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(rig);
                var bones=rig.Model.GetComponentsInChildren<Transform>();
                var rests=PlayerRagdoll.BoneNames.Select(n=>bones.First(t=>t.name==n).localRotation).ToArray();
                int knee=Array.IndexOf(PlayerRagdoll.BoneNames,"LeftKnee");var leftKnee=bones.First(t=>t.name=="LeftKnee");leftKnee.localRotation=rests[knee]*Quaternion.AngleAxis(60,axes[knee]);
                rig.SetInjury(PlayerInjury.Dead);
                Check(Mathf.Abs(leftKnee.GetComponent<CharacterJoint>().lowTwistLimit.limit+25)<.1f&&Mathf.Abs(leftKnee.GetComponent<CharacterJoint>().highTwistLimit.limit-60)<.1f,"pre-bent knee limits use the new tighter neutral-pose range");
                rig.Bodies[0].isKinematic=true;foreach(var b in rig.Bodies) if(b!=null) b.useGravity=false;
                foreach(string name in new[]{"LeftElbow","RightElbow","LeftKnee","RightKnee"}) {
                    var joint=bones.First(t=>t.name==name).GetComponent<CharacterJoint>();Check(joint.swing1Limit.limit<=3.01f&&joint.swing2Limit.limit<=3.01f,"hinge cannot fold sideways: "+name);
                }
                Physics.SyncTransforms();
                for(int direction=-1;direction<=1;direction+=2) {
                    for(int step=0;step<100;step++) {
                        foreach(string name in new[]{"LeftElbow","RightElbow","LeftKnee","RightKnee"}) {
                            int i=Array.IndexOf(PlayerRagdoll.BoneNames,name);var b=rig.Bodies[i];b.AddTorque(b.transform.TransformDirection(axes[i])*direction*120,ForceMode.Force);
                        }
                        dummy.gameObject.scene.GetPhysicsScene().Simulate(.02f);
                    }
                    foreach(string name in new[]{"LeftElbow","RightElbow","LeftKnee","RightKnee"}) {
                        int i=Array.IndexOf(PlayerRagdoll.BoneNames,name);float angle=PlayerRagdoll.TwistAngle(Quaternion.Inverse(rests[i])*rig.Bodies[i].transform.localRotation,axes[i]);
                        Check(angle>=-8&&angle<=(name.Contains("Knee")?93:88),"forced hinge remains in anatomical range: "+name+" angle="+angle);
                    }
                }
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        static void ValidateBars()
        {
            var root=new GameObject("Bar count fixture");
            try {
                var player=new GameObject("Player").AddComponent<FirstPersonController>();player.transform.SetParent(root.transform,false);
                var first=WorldBootstrap.CreateIronTestBar(root,null,player);var bars=root.AddComponent<TestGroundBars>();bars.Initialize(null,player,first);
                var session=Session(root,true);
                session.ReceiveWorld("{\"self\":\"self\",\"host\":\"self\",\"ready\":true,\"playerCount\":3}");bars.Reconcile(WebWorldSession.RoomPlayerCount);
                Check(root.GetComponentsInChildren<TestGroundIronBar>().Length==3,"three room players get three shared iron bars");
                var spawned=session.CaptureChangesForValidation();Check(spawned.objects.Count(s=>!s.deleted&&s.testBar)==2,"extra bars are registered with shared geometry and identity");
                session.ReceiveWorld("{\"self\":\"self\",\"host\":\"host\",\"ready\":true,\"playerCount\":1}");bars.Reconcile(1);
                Check(root.GetComponentsInChildren<TestGroundIronBar>().Length==3,"guest cannot independently add or remove room bars");
                session.ReceiveWorld("{\"self\":\"self\",\"host\":\"self\",\"ready\":true,\"playerCount\":1}");bars.Reconcile(WebWorldSession.RoomPlayerCount);
                Check(root.GetComponentsInChildren<TestGroundIronBar>().Length==1,"departures reduce the room to one bar per player");
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
