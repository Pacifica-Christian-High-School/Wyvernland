using System;
using System.Linq;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Wyvernland.Editor
{
    public static class ThrowSafetyValidation
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Check(bool value,string message) { if(!value) throw new InvalidOperationException("Throw safety: "+message); }
        public static void Run()
        {
            var previous=SceneManager.GetActiveScene();
            if(string.IsNullOrEmpty(previous.path)) { EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");previous=SceneManager.GetActiveScene(); }
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
            try {
                foreach(bool left in new[]{false,true}) foreach(bool host in new[]{false,true}) Validate(left,host);
                Debug.Log("THROW_SAFETY_VALIDATION_PASS: both hands, ignored own-arm contacts and fast sweeps, host/guest confirmed self-hit rejection, and damage to another actor behind the thrower.");
            } finally { SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true); }
        }
        static void Validate(bool left,bool host)
        {
            var root=new GameObject("Throw safety fixture");
            GameObject source=null;
            try {
                var actor=new GameObject("Thrower");actor.transform.SetParent(root.transform,false);actor.transform.position=new Vector3(left?300:500,1000,host?300:500);
                actor.AddComponent<Rigidbody>().isKinematic=true;
                var camera=new GameObject("Camera",typeof(Camera));camera.transform.SetParent(actor.transform,false);
                var player=actor.AddComponent<FirstPersonController>();player.Initialize(camera.transform);player.enabled=false;
                var avatar=actor.AddComponent<RiggedPlayerAvatar>();avatar.Initialize(player);avatar.enabled=false;
                var damage=actor.GetComponent<PlayerDamage>();
                var hand=(Transform)typeof(RiggedPlayerAvatar).GetField(left?"leftHandMarker":"rightHandMarker",Hidden).GetValue(avatar);
                source=GameObject.CreatePrimitive(PrimitiveType.Cube);source.transform.localScale=Vector3.one*.02f;source.transform.position=hand.position;
                var body=source.AddComponent<Rigidbody>();body.useGravity=false;var properties=PhysicalDestructible.Ensure(source);
                var pickup=source.AddComponent<PickableObject>();Check(pickup.PickUp(hand),"real hand pickup succeeds");
                var collisionChild=new GameObject("Weapon collision shape");
                collisionChild.transform.SetParent(source.transform,false);
                var childCollider=collisionChild.AddComponent<BoxCollider>();
                UnityEngine.Object.DestroyImmediate(source.GetComponent<MeshCollider>());
                typeof(PickableObject).GetField("heldColliders",Hidden).SetValue(pickup,new Collider[]{childCollider});
                Check((Collider)typeof(PickableObject).GetMethod("ImpactSourceCollider",Hidden)
                    .Invoke(pickup,new object[]{source.transform.position})==childCollider,
                    "held impact uses a child solid collider when the root has only a trigger");
                pickup.Release(Vector3.down*600,actor.transform);
                var collider=source.GetComponentsInChildren<Collider>().First(c=>c.enabled&&!c.isTrigger);
                Check(collider==childCollider&&source.GetComponent<Collider>() is { isTrigger:true },
                    "held and thrown weapons can use a child collision shape without a root solid collider");
                var part=left?PlayerBodyPart.LeftArm:PlayerBodyPart.RightArm;
                var arm=actor.GetComponentsInChildren<PlayerHitShape>().First(t=>t.Part==part).GetComponent<Collider>();
                Check(MovingImpactMotion.IgnoresContact(collider,arm),"release preserves own-arm collision exclusion");
                int hits=damage.Capture().hit;
                Check(!damage.ReceiveImpact(part,collider,arm.bounds.center,Vector3.down,Vector3.down*600),"overlap/contact path cannot damage the throwing arm");
                MovingImpactMotion.HitActor(arm,collider,arm.bounds.center,Vector3.down,Vector3.down*600);
                Check(damage.Capture().hit==hits&&damage.CanUseHand(left),"fast contact cannot bypass ignored collision pairs");
                source.transform.SetParent(root.transform,true);
                var dummy=TrainingDummy.Create(root.transform,null);dummy.transform.position=actor.transform.position+Vector3.down*6;
                Physics.SyncTransforms();
                var head=actor.GetComponentsInChildren<PlayerHitShape>().First(t=>t.Part==PlayerBodyPart.Head).GetComponent<Collider>();
                var target=dummy.GetComponentsInChildren<PlayerHitShape>().First(t=>t.Part==PlayerBodyPart.Head).GetComponent<Collider>();
                dummy.transform.position+=head.bounds.center-Vector3.up*6-target.bounds.center;
                body.isKinematic=false;body.position=head.bounds.center+Vector3.up*3;body.linearVelocity=Vector3.down*600;body.useGravity=false;
                Physics.SyncTransforms();
                var motion=MovingImpactMotion.Ensure(body);typeof(MovingImpactMotion).GetMethod("FixedUpdate",Hidden).Invoke(motion,null);
                Check(!damage.IsDead&&damage.Capture().hit==hits,"sweep passes through the thrower's excluded body without injury");
                Check(!dummy.Damage.IsDead&&dummy.Damage.Capture().headFaces.Length==1,
                    "sweep continues past ignored own-body hits to the other actor's head");dummy.Damage.Respawn();
                var session=new GameObject("Throw safety room").AddComponent<WebWorldSession>();session.transform.SetParent(root.transform,false);
                var init=session.Initialize(root,player);while(init.MoveNext()){}
                session.ReceiveWorld(JsonUtility.ToJson(new WebWorldSession.Packet { self="self",host=host?"self":"host",ready=true }));
                var impact=new PlayerDamage.Impact { id=Guid.NewGuid().ToString("N"),source=WebWorldSession.IdOf(source),target="self",life=damage.Capture().life,
                    part=(int)part,point=Vector3.zero,normal=Vector3.down,velocity=Vector3.down*600,threshold=PlayerDamage.RequiredSpeed(properties) };
                Check(!string.IsNullOrEmpty(impact.source)&&!damage.ReceiveNetworkImpact(impact),"room host report cannot bypass the thrower's collision exclusion");
                Check(damage.Capture().hit==hits&&damage.Capture().received.Contains(impact.id),"rejected room hit is acknowledged without injury");
                impact.id=Guid.NewGuid().ToString("N");impact.life=dummy.Damage.Capture().life;
                Check(dummy.Damage.ReceiveNetworkImpact(impact),"the same thrown object can damage another actor");
            } finally { if(source!=null) source.transform.SetParent(root.transform,true);UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
