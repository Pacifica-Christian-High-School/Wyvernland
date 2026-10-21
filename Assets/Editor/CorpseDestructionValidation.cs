using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Wyvernland.Editor
{
    public static class CorpseDestructionValidation
    {
        static void Check(bool value,string message)
        { if(!value) throw new InvalidOperationException("Corpse destruction: "+message); }
        static PlayerCorpse Body(Transform parent,int limb=0)
        {
            var corpse=PlayerCorpse.Create(new PlayerDamage.CorpseState {
                id="Collision corpse "+Guid.NewGuid().ToString("N"),position=new Vector3(0,1000,0),rotation=Quaternion.identity,limb=limb },parent);
            corpse.SetReplica(false);
            typeof(PlayerCorpse).GetField("destructibleAfter",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(corpse,-1f);
            return corpse;
        }
        static PhysicalDestructible Impactor(Transform parent)
        {
            var node=GameObject.CreatePrimitive(PrimitiveType.Cube);node.name="Corpse impactor";node.transform.SetParent(parent,false);
            node.transform.position=new Vector3(0,1010,0);node.transform.localScale=Vector3.one*.2f;
            var body=node.AddComponent<Rigidbody>();body.useGravity=false;body.mass=100;
            var material=PhysicalDestructible.Ensure(node);material.ApplyMaterial("Stone");return material;
        }
        public static void Run()
        {
            var previous=SceneManager.GetActiveScene();
            if(string.IsNullOrEmpty(previous.path)) { EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");previous=SceneManager.GetActiveScene(); }
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
            var mode=Physics.simulationMode;float timeScale=Time.timeScale;
            try { Physics.simulationMode=SimulationMode.Script;Time.timeScale=1;ValidateAnatomy();ValidateNetwork();ValidateLimb(); }
            finally { Physics.simulationMode=mode;Time.timeScale=timeScale;SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true); }
            Debug.Log("CORPSE_DESTRUCTION_VALIDATION_PASS: separate arms/legs/torso in posed and damaged bodies, no missing-limb regrowth, safe slow contacts, released loot, no ragdoll/joints/skinning, guest-to-host impact, one-time deletion, persistent geometry, late join and corpse tombstones.");
        }
        public static void RunCombined() { Run();SmallFragmentRestValidation.RunRegression(); }
        static void ValidateAnatomy()
        {
            var root=new GameObject("Anatomical corpse fragments");
            try
            {
                foreach(int missing in new[]{0,1,10,15})
                {
                    var corpse=PlayerCorpse.Create(new PlayerDamage.CorpseState {
                        id="Anatomy "+Guid.NewGuid().ToString("N"),position=new Vector3(0,1000,0),rotation=Quaternion.Euler(20,35,15),missing=missing },root.transform);
                    var model=corpse.GetComponent<PlayerRagdoll>().Model;
                    foreach(string name in new[]{"LeftShoulder","RightShoulder","LeftHip","RightHip"})
                    {
                        var joint=model.GetComponentsInChildren<Transform>().First(t=>t.name==name);
                        joint.localRotation*=Quaternion.Euler(35,20,name.StartsWith("Left")?45:-45);
                    }
                    var original=model.GetComponentsInChildren<SkinnedMeshRenderer>().Select(s=>s.sharedMesh).ToArray();
                    var pieces=CorpseDestruction.Bake(model,corpse.transform.position,4,out _,true,missing);
                    try
                    {
                        Check(pieces.Count==5-Enumerable.Range(0,4).Count(i=>(missing&(1<<i))!=0),"each existing limb separates from the torso");
                        string[] names={"Corpse left arm","Corpse right arm","Corpse left leg","Corpse right leg"};
                        string[] tips={"LeftWrist","RightWrist","LeftAnkle","RightAnkle"};
                        for(int limb=0;limb<4;limb++)
                        {
                            var mesh=pieces.FirstOrDefault(p=>p.name==names[limb]);
                            if((missing&(1<<limb))!=0) { Check(mesh==null,"previously missing limbs are not recreated");continue; }
                            Check(mesh!=null,"each arm and leg has its own fragment");
                            var tip=model.GetComponentsInChildren<Transform>().First(t=>t.name==tips[limb]);
                            Check(mesh.bounds.SqrDistance(tip.position)<.03f,"limb geometry follows its actual folded world pose");
                            Check(mesh.bounds.size.magnitude<1.7f,"unused torso vertices do not inflate limb bounds or colliders");
                            Check(mesh.triangles.Distinct().Count()==mesh.vertexCount,"every baked limb vertex belongs to the fragment");
                        }
                        Check(pieces.Any(p=>p.name=="Corpse torso"),"torso remains independent");
                        Check(model.GetComponentsInChildren<SkinnedMeshRenderer>().Select(s=>s.sharedMesh).SequenceEqual(original),"baking does not modify the damaged source skins");
                    }
                    finally { foreach(var mesh in pieces) PlayerDamage.Release(mesh);UnityEngine.Object.DestroyImmediate(corpse.gameObject); }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        const string PlayKey="Wyvernland.CorpseDestruction.PlayValidation";
        static double deadline;
        public static void RunPlay()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            SessionState.SetBool(PlayKey,true);EditorApplication.isPlaying=true;
        }
        [InitializeOnLoadMethod] static void ResumePlay()
        {
            if(!SessionState.GetBool(PlayKey,false)) return;
            deadline=EditorApplication.timeSinceStartup+90;EditorApplication.update+=PlayTick;
        }
        static void PlayTick()
        {
            try {
                if(EditorApplication.timeSinceStartup>deadline) throw new InvalidOperationException("Corpse collision play validation timed out");
                if(!EditorApplication.isPlaying||Time.deltaTime<=0) return;
                Time.timeScale=1;Physics.simulationMode=SimulationMode.Script;
                ValidatePhysics(SceneManager.GetActiveScene());
                Debug.Log("CORPSE_COLLISION_PLAY_VALIDATION_PASS: real fast collision callbacks completely replace the body with independent rigid fragments and disable the ragdoll.");
                FinishPlay(0);
            } catch(Exception exception) { Debug.LogException(exception);FinishPlay(1); }
        }
        static void FinishPlay(int code)
        {
            SessionState.SetBool(PlayKey,false);EditorApplication.update-=PlayTick;
            EditorApplication.Exit(code);
        }
        static void ValidateNetwork()
        {
            var root=new GameObject("Corpse destruction world");
            try {
                var corpse=Body(root.transform);string name=corpse.name;
                var source=Impactor(root.transform);
                var session=new GameObject("Corpse world session").AddComponent<WebWorldSession>();session.transform.SetParent(root.transform,false);
                var init=session.Initialize(root,null);while(init.MoveNext()){}
                session.ReceiveWorld("{\"self\":\"guest\",\"host\":\"host\",\"ready\":true}");
                string id=WebWorldSession.IdOf(corpse.gameObject);
                var oldCorpse=session.CaptureForValidation(corpse.gameObject).objects[0];
                float threshold=PlayerCorpse.DestructionSpeed(source);
                Check(!corpse.TryShatterFromImpact(source,corpse.transform.position,Vector3.right*(threshold-.1f)),"ordinary contacts preserve the body");
                WebWorldSession.Command command=null;
                session.RequestForValidation+=request=> { if(request.action=="command") command=request.command; };
                Check(corpse.TryShatterFromImpact(source,corpse.transform.position,Vector3.right*(threshold+5)),"guest forwards a destructive collision");
                Check(corpse!=null&&!corpse.IsShattered&&root.GetComponentsInChildren<BodyFragment>().Length==0,"guest creates no duplicate fragments");
                Check(command!=null&&command.type=="strike"&&command.target==id,"existing collision command targets the corpse");
                // A corpse-held item must become free loot when the bones vanish.
                session.ReceiveWorld("{\"self\":\"host\",\"host\":\"host\",\"ready\":true}");
                var loot=GameObject.CreatePrimitive(PrimitiveType.Cube);loot.transform.SetParent(root.transform,false);loot.transform.localScale=Vector3.one*.08f;
                loot.transform.position=corpse.GetComponent<PlayerRagdoll>().Hand(false).position;
                loot.AddComponent<Rigidbody>();var pickup=loot.AddComponent<PickableObject>();corpse.Grip(pickup,false);
                Check(loot.GetComponent<FixedJoint>()!=null,"fixture has corpse-held loot");
                command.commandId="corpse-impact-one";
                session.ReceiveWorld(JsonUtility.ToJson(new WebWorldSession.Packet { self="host",host="host",ready=true,commands=new[]{command} }));
                Check(corpse==null||corpse.IsShattered,"host destroys the original corpse");
                Check(loot!=null&&loot.GetComponent<FixedJoint>()==null,"loot survives without a joint to deleted bones");
                var fragments=root.GetComponentsInChildren<BodyFragment>();
                Check(fragments.Length==5,"both arms and both legs separate from the torso");
                foreach(var fragment in fragments) {
                    Check(fragment.GetComponent<Rigidbody>() is { isKinematic:false }&&fragment.GetComponent<Collider>() is { enabled:true },"chunk has independent solid physics");
                    Check(fragment.GetComponentsInChildren<Joint>().Length==0&&fragment.GetComponentsInChildren<SkinnedMeshRenderer>().Length==0,"chunks have no ragdoll effect");
                    Check(DestructionMesh.SolidVolume(fragment.GetComponent<MeshFilter>().sharedMesh)>.0001,"chunks retain substantial body geometry");
                }
                var saved=session.CaptureChangesForValidation();
                Check(saved.objects.Any(o=>o.id==id&&o.deleted),"corpse deletion is published");
                Check(saved.objects.Count(o=>o.bodyFragment&&!o.deleted)==fragments.Length&&saved.meshes.Length>=fragments.Length,"all replacement geometry is published");
                Check(saved.objects.Where(o=>o.bodyFragment).All(o=>o.expires==0),"large chunks persist");
                session.ReceiveWorld(JsonUtility.ToJson(new WebWorldSession.Packet { self="host",host="host",ready=true,commands=new[]{command} }));
                Check(root.GetComponentsInChildren<BodyFragment>().Length==fragments.Length,"repeated command cannot shatter twice");
                UnityEngine.Object.DestroyImmediate(root);root=new GameObject("Corpse fragment late join");
                session=new GameObject("Late join session").AddComponent<WebWorldSession>();session.transform.SetParent(root.transform,false);
                init=session.Initialize(root,null);while(init.MoveNext()){}
                session.ReceiveWorld(JsonUtility.ToJson(new WebWorldSession.Packet { self="guest",host="host",ready=true,objects=saved.objects,meshes=saved.meshes }));
                Check(WebWorldSession.IsCorpseRemoved(name)&&WebWorldSession.ObjectOf(id)==null,"late join retains the corpse tombstone");
                Check(saved.objects.Where(o=>o.bodyFragment&&!o.deleted).All(o=> {
                    var target=WebWorldSession.ObjectOf(o.id);
                    return target!=null&&target.GetComponentsInChildren<MeshFilter>().Any(f=>f!=null&&f.sharedMesh!=null);
                }),"late join restores each static-mesh chunk");
                session.ReceiveWorld(JsonUtility.ToJson(new WebWorldSession.Packet { self="guest",host="host",ready=true,objects=new[]{oldCorpse} }));
                Check(PlayerCorpse.Find(name)==null,"stale state cannot resurrect the shattered ragdoll");
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        static void ValidatePhysics(Scene scene)
        {
            var root=new GameObject("Actual corpse collision");
            try {
                var corpse=Body(root.transform);
                foreach(var body in corpse.GetComponent<PlayerRagdoll>().Bodies) if(body!=null) body.isKinematic=true;
                var shape=corpse.GetComponentsInChildren<PlayerHitShape>().First(t=>t.Part==PlayerBodyPart.Torso&&t.GetComponent<Collider>().enabled).GetComponent<Collider>();
                var source=Impactor(root.transform);var bodySource=source.GetComponent<Rigidbody>();
                bodySource.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
                bodySource.position=shape.bounds.center-Vector3.forward*.8f;
                bodySource.linearVelocity=Vector3.forward*(PlayerCorpse.DestructionSpeed(source)+15);
                Physics.SyncTransforms();
                for(int i=0;i<15&&corpse!=null&&!corpse.IsShattered;i++) scene.GetPhysicsScene().Simulate(.01f);
                Check(corpse==null||corpse.IsShattered,"real collision callbacks shatter the body");
                Check(root.GetComponentsInChildren<PlayerRagdoll>().Length==0||root.GetComponentsInChildren<PlayerRagdoll>().All(r=>!r.enabled),"no active ragdoll survives");
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        static void ValidateLimb()
        {
            var root=new GameObject("Severed limb collision");
            try {
                var limb=Body(root.transform,1);var source=Impactor(root.transform);
                Check(limb.TryShatterFromImpact(source,limb.transform.position,Vector3.right*100),"severed dead limb can also shatter");
                Check(root.GetComponentsInChildren<BodyFragment>().Length is >=2 and <=3,"limb becomes a few large solid chunks");
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
