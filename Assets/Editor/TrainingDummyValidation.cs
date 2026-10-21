using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Wyvernland.Editor
{
    public static class TrainingDummyValidation
    {
        [MenuItem("Tools/Wyvernland/Validate Training Dummy")]
        public static void Run()
        {
            var root=new GameObject("Dummy validation world");
            try {
                var dummy=TrainingDummy.Create(root.transform,null);var damage=dummy.Damage;
                var player=dummy.GetComponent<FirstPersonController>();var avatar=dummy.GetComponent<RiggedPlayerAvatar>();
                Check(dummy.transform.position==new Vector3(-10,.15f,-10),"dummy stands at Test Ground X-10 Z-10");
                Check(!player.PlayerCamera.enabled&&damage.SuppressPlayerOverlay,"dummy never takes over the camera or damage overlay");
                Check(dummy.GetComponentsInChildren<PlayerHitShape>().Select(s=>s.Part).Distinct().Count()==5,"dummy has all five real player hit regions");
                var source=GameObject.CreatePrimitive(PrimitiveType.Cube);source.transform.SetParent(root.transform,false);
                source.transform.position=new Vector3(-5,3,-10);source.AddComponent<Rigidbody>().useGravity=false;
                var properties=PhysicalDestructible.Ensure(source);source.GetComponent<Rigidbody>().mass=.01f;float speed=PlayerDamage.RequiredSpeed(properties)+1;
                var hit=source.GetComponent<Collider>();var point=dummy.transform.position+Vector3.up;
                Check(!damage.ReceiveImpact(PlayerBodyPart.Head,hit,point,Vector3.right,Vector3.zero),"stationary props cannot hurt the dummy");
                var hand=dummy.GetComponentsInChildren<RightHandPickupTrigger>().First(h=>h.name.StartsWith("Right")).transform;
                var held=GameObject.CreatePrimitive(PrimitiveType.Cube);held.transform.SetParent(root.transform,false);held.transform.localScale=Vector3.one*.08f;held.transform.position=hand.position;
                held.AddComponent<Rigidbody>().mass=1;var pickup=held.AddComponent<PickableObject>();
                Check(avatar.BeginHandPickup(pickup,false,hand.position)&&pickup.IsHeld,"dummy uses the same hand grip as the player");
                Check(damage.ReceiveImpact(PlayerBodyPart.RightArm,hit,hand.position,Vector3.right,Vector3.right*speed)&&pickup.IsHeld&&avatar.CanUseHand(false)&&damage.ArmMovementSpeed(false)==1f&&damage.ArmAnimationSwingAmount(false)==.8f,"first arm wound keeps the item and shortens its swing arc");
                var strikeInjury=typeof(RiggedPlayerAvatar).GetMethod("StrikeInjurySpeed",BindingFlags.Instance|BindingFlags.NonPublic);
                Check(Mathf.Abs((float)strikeInjury.Invoke(avatar,new object[]{false})-.8f)<.001f,
                    "arm damage also slows the injured hand's weapon wave");
                held.transform.SetParent(root.transform,true);
                damage.Respawn();
                Check(damage.ReceiveImpact(PlayerBodyPart.Legs,hit,point,Vector3.right,Vector3.right*speed)&&damage.CanMove&&damage.MovementSpeedScale==.75f,"first leg wound slows movement without ragdoll");
                Check(Mathf.Abs((float)strikeInjury.Invoke(avatar,new object[]{false})-.75f)<.001f,
                    "leg movement loss also slows weapon waving");
                damage.Respawn();
                var torsoPoint=dummy.transform.position+new Vector3(0,1.2f,.15f);
                Check(damage.ReceiveImpact(PlayerBodyPart.Torso,hit,torsoPoint,Vector3.right,Vector3.right*speed)&&
                    damage.Capture().pits.Length==1&&Mathf.Abs(damage.MovementSpeedScale-.9f)<.001f,
                    "first torso destruction slows all movement by ten percent");
                var secondTorsoSource=UnityEngine.Object.Instantiate(hit.gameObject);
                secondTorsoSource.transform.SetParent(root.transform,true);
                Check(damage.ReceiveImpact(PlayerBodyPart.Torso,secondTorsoSource.GetComponent<Collider>(),torsoPoint,
                    Vector3.right,Vector3.right*speed)&&Mathf.Abs(damage.MovementSpeedScale-.81f)<.001f&&damage.Capture().torsoHits==2,
                    "each torso destruction compounds another ten percent movement slowdown");
                typeof(PlayerDamage).GetField("torsoHits",BindingFlags.Instance|BindingFlags.NonPublic)
                    .SetValue(damage,100);
                Check(Mathf.Abs(damage.MovementSpeedScale-.4f)<.001f&&
                    Mathf.Abs((float)typeof(RiggedPlayerAvatar).GetMethod("StrikeInjurySpeed",
                        BindingFlags.Instance|BindingFlags.NonPublic).Invoke(avatar,new object[]{false})-.4f)<.001f,
                    "torso loss stops at sixty percent and also slows weapon waving");
                damage.Respawn();
                float before=Time.unscaledTime;
                Check(damage.ReceiveImpact(PlayerBodyPart.Head,hit,point,Vector3.right,Vector3.right*speed)&&damage.IsDead&&damage.Capture().corpses.Length==1,
                    "one head impact creates a fully shattered ragdoll corpse");
                float at=(float)typeof(PlayerDamage).GetField("respawnAt",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(damage);
                Check(Mathf.Abs(at-before-2f)<.02f,"dummy uses the real two-second respawn timer");
                dummy.transform.position+=Vector3.right*5;damage.Respawn();
                Check(!damage.IsDead&&damage.CanMove&&Vector3.Distance(dummy.transform.position,new Vector3(-10,.15f,-10))<.001f,"respawn restores health and the requested standing position: "+dummy.transform.position);
                var session=Session(root,player,"host","host");
                string beforeDummy=(string)typeof(WebWorldSession).GetField("baseline",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(session);
                var second=TrainingDummy.Create(root.transform,null);second.name="Additional validation actor";
                UnityEngine.Object.DestroyImmediate(session.gameObject);
                session=Session(root,player,"host","host");
                string withDummy=(string)typeof(WebWorldSession).GetField("baseline",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(session);
                Check(beforeDummy==withDummy,"Adding an actor preserves the existing room terrain/prop signature");
                UnityEngine.Object.DestroyImmediate(second.gameObject);
                Check(damage.ReceiveImpact(PlayerBodyPart.LeftArm,hit,point,Vector3.right,Vector3.right*speed),"host simulates dummy injuries");
                var saved=session.CaptureForValidation(dummy.gameObject);saved.objects[0].revision=1;
                string id=saved.objects[0].id;
                UnityEngine.Object.DestroyImmediate(root);root=new GameObject("Dummy validation world");
                dummy=TrainingDummy.Create(root.transform,null);player=dummy.GetComponent<FirstPersonController>();
                session=Session(root,player,"guest","host");
                Check(WebWorldSession.IdOf(dummy.gameObject)==id,"dummy has a stable shared-world identity");
                session.ReceiveWorld(JsonUtility.ToJson(new WebWorldSession.Packet { self="guest",host="host",ready=true,objects=saved.objects,meshes=saved.meshes }));
                Check(dummy.Damage.CanUseHand(true)&&dummy.Damage.ArmMovementSpeed(true)==1f&&dummy.Damage.ArmAnimationSwingAmount(true)==.8f&&!player.enabled&&dummy.GetComponent<Rigidbody>().isKinematic,"guests receive injuries without driving a second dummy simulation");
                session.ReceiveWorld("{\"self\":\"guest\",\"host\":\"guest\",\"ready\":true}");
                Check(player.enabled&&!dummy.GetComponent<Rigidbody>().isKinematic&&dummy.Damage.CanUseHand(true)&&dummy.Damage.ArmMovementSpeed(true)==1f&&dummy.Damage.ArmAnimationSwingAmount(true)==.8f,"a new host resumes the same injured dummy");
                Debug.Log("TRAINING_DUMMY_VALIDATION_PASS: X-10 Z-10 placement, real body hit regions, moving-object gate, staged arm damage and retained grip, lower-body ragdoll, torso damage, head death, two-second respawn, shared injuries and host takeover.");
            } finally { if(root!=null) UnityEngine.Object.DestroyImmediate(root);Time.timeScale=1; }
        }
        static WebWorldSession Session(GameObject root,FirstPersonController player,string self,string host)
        {
            var session=new GameObject("Shared session").AddComponent<WebWorldSession>();session.transform.SetParent(root.transform,false);
            IEnumerator init=session.Initialize(root,player);while(init.MoveNext()){}
            session.ReceiveWorld(JsonUtility.ToJson(new WebWorldSession.Packet { self=self,host=host,ready=true }));return session;
        }
        static void Check(bool value,string message) { if(!value) throw new InvalidOperationException("Training dummy: "+message); }
    }
}
