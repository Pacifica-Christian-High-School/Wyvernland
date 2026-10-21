using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Wyvernland.Editor
{
    public static class PlayerWorldLoadingValidation
    {
        [MenuItem("Tools/Wyvernland/Validate Player Loading Range")]
        public static void Run()
        {
            var previous=SceneManager.GetActiveScene();
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            GameObject root=null,other=null;
            Material material=null;
            var detached=new List<GameObject>();
            float previousScale=Time.timeScale;
            var previousSimulation=Physics.simulationMode;
            try {
                Time.timeScale=1;
                Physics.simulationMode=SimulationMode.Script;
                root=new GameObject("Local world");
                var eye=new GameObject("Observer");eye.transform.SetParent(root.transform);
                var near=Cube(root,"Near",new Vector3(10,0,10));
                var far=Cube(root,"Far",new Vector3(90,0,10));
                var middleTerrain=GameObject.CreatePrimitive(PrimitiveType.Cube);
                middleTerrain.name="Middle terrain tile";middleTerrain.transform.SetParent(root.transform);
                middleTerrain.transform.position=new Vector3(140,0,0);
                PhysicalDestructible.Ensure(middleTerrain).IsTerrain=true;
                var distantTerrain=GameObject.CreatePrimitive(PrimitiveType.Cube);
                distantTerrain.name="Distant terrain tile";distantTerrain.transform.SetParent(root.transform);
                distantTerrain.transform.position=new Vector3(170,0,0);
                PhysicalDestructible.Ensure(distantTerrain).IsTerrain=true;
                material=new Material(Resources.Load<Shader>("Clay"));
                near.GetComponent<Renderer>().sharedMaterial=material;far.GetComponent<Renderer>().sharedMaterial=material;
                var disabled=far.AddComponent<SphereCollider>();disabled.enabled=false;
                var mover=Cube(root,"Moving",new Vector3(1000,0,100));
                var body=mover.AddComponent<Rigidbody>();body.useGravity=false;body.WakeUp();
                var neighbor=Cube(root,"Collision neighbor",mover.transform.position+Vector3.right*19);
                var above=Cube(root,"Vertical neighbor",mover.transform.position+Vector3.up*19);
                var diagonal=Cube(root,"Outside sphere",mover.transform.position+new Vector3(19,19,0));
                var tooHigh=Cube(root,"Above sphere",mover.transform.position+Vector3.up*22);
                var swept=Cube(root,"Fast throw obstacle",mover.transform.position+Vector3.right*55);
                var sleeper=Cube(root,"Sleeping",new Vector3(2000,0,100));
                var sleepingBody=sleeper.AddComponent<Rigidbody>();sleepingBody.useGravity=false;sleepingBody.Sleep();
                var support=Cube(root,"Tree support",new Vector3(3000,0,100));
                var supportPart=support.AddComponent<TreeStructuralPart>();supportPart.SetTrunk(true);supportPart.SetGroundAnchor();
                var trunk=Cube(root,"Upper trunk",support.transform.position+Vector3.up);
                var trunkPart=trunk.AddComponent<TreeStructuralPart>();trunkPart.SetTrunk(true);trunkPart.AttachTo(supportPart);
                var trunkNeighbor=Cube(root,"Falling trunk neighbor",trunk.transform.position+Vector3.right*19);
                var rope=RopeObject.Create(root.transform,VineKind.Black,.02f,1f,support.transform,
                    support.transform.position+Vector3.up,support.GetComponent<Renderer>().sharedMaterial);
                var draw=root.AddComponent<ResidentPropRendering>();draw.Initialize();
                Require(draw.CachedSourceCount==2,"Near and far sources must share a draw cache");
                var combined=root.GetComponentInChildren<ResidentRenderVisual>().GetComponent<MeshFilter>().sharedMesh;
                int oneSource=(int)near.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0);
                var loader=root.AddComponent<PlayerWorldLoading>();Drain(loader.Initialize(root,eye.transform,50));
                Require(loader.IsLoaded(near)&&!loader.IsLoaded(far),"Range must select individual objects inside the same batch");
                Require(loader.IsLoaded(middleTerrain)&&!loader.IsLoaded(distantTerrain)&&
                    PlayerWorldLoading.TerrainRangeMultiplier==3f,
                    "Terrain tiles load to three times the selected object range");
                Require(middleTerrain.GetComponent<Collider>().enabled&&
                    !distantTerrain.GetComponent<Collider>().enabled,
                    "Terrain collision follows the expanded loading range");
                Require(combined.GetIndexCount(0)==oneSource,"Unloaded source triangles must leave the combined draw mesh");
                Require(far.activeInHierarchy&&!far.GetComponent<Collider>().enabled&&!far.GetComponent<PhysicalDestructible>().enabled&&
                    far.GetComponent<Renderer>().forceRenderingOff,"Distant props must suspend rendering, collision and scripts while retaining identity");
                Require(loader.IsLoaded(mover)&&loader.IsLoaded(neighbor)&&loader.IsLoaded(above)&&
                    !loader.IsLoaded(diagonal)&&!loader.IsLoaded(tooHigh),"Moving collision bubbles must be true 20 m spheres in three dimensions");
                Require(!loader.IsLoaded(sleeper)&&sleepingBody.IsSleeping(),"Sleeping distant bodies must unload without waking");
                sleepingBody.WakeUp();loader.Refresh();Require(loader.IsLoaded(sleeper),"A woken distant body must reload");
                body.linearVelocity=Vector3.right*2000;loader.Refresh();
                Require(loader.IsLoaded(swept),"Fast throws must load collision geometry along their next physics step");
                body.linearVelocity=Vector3.zero;body.Sleep();loader.Refresh();
                Require(!loader.IsLoaded(mover)&&!loader.IsLoaded(neighbor),"Stopped objects must release their collision bubble");
                Require(!loader.IsLoaded(rope.gameObject)&&rope.LoadingSupport==support.transform&&rope.Bodies.All(b=>b.IsSleeping()),
                    "Unloaded ropes must preserve attachment and sleep");
                PlayerWorldLoading.BeforeChange(trunk);
                Require(loader.IsLoaded(trunk)&&loader.IsLoaded(support),"Immediate edits must activate structural dependencies");
                Require(trunkPart.BreakTrunkSupport(),"Fixture upper trunk must detach");detached.Add(trunk);
                loader.Refresh();
                Require(trunk.transform.parent==null&&loader.IsLoaded(trunk)&&loader.IsLoaded(trunkNeighbor),
                    "A detached trunk with a new body must retain its collision bubble outside the world root");
                var fragment=Cube(null,"Root fragment",new Vector3(4000,0,100));detached.Add(fragment);
                var fragmentBody=fragment.AddComponent<Rigidbody>();fragmentBody.useGravity=false;fragmentBody.WakeUp();
                WebWorldSession.RegisterSpawn(fragment);loader.Refresh();
                Require(loader.IsLoaded(fragment),"Explicitly spawned root objects must register without ancestry");
                int registered=loader.RegisteredObjectCount;UnityEngine.Object.DestroyImmediate(fragment);loader.Refresh();
                Require(loader.RegisteredObjectCount==registered-1,"Offline destruction must remove residency registry entries");
                var breakable=Cube(root,"Offline breakable",new Vector3(4500,0,100));
                PlayerWorldLoading.NotifyChanged(breakable);loader.Refresh();
                var pieces=new List<PhysicalDestructible>();breakable.GetComponent<PhysicalDestructible>().Fragmented+=(_,values)=>pieces.AddRange(values);
                var random=UnityEngine.Random.state;UnityEngine.Random.InitState(12);
                try { breakable.GetComponent<PhysicalDestructible>().ApplyImpact(10f,10f,3f,breakable.transform.position,Vector3.right,100f,"Iron",2d,false,20f); }
                finally { UnityEngine.Random.state=random; }
                loader.Refresh();
                Require(pieces.Count>0&&pieces.All(p=>loader.IsLoaded(p.gameObject)),"Real offline destruction must register every new moving fragment");
                detached.AddRange(pieces.Select(p=>p.gameObject));
                loader.SetRange(100);Require(loader.IsLoaded(far)&&combined.GetIndexCount(0)==oneSource*2,
                    "Increasing range must restore the individual cached source");
                Require(loader.IsLoaded(distantTerrain),"Increasing object range must expand terrain coverage threefold");
                Require(!disabled.enabled,"Reload must preserve intentionally disabled colliders");
                loader.SetRange(50);
                var replacement=UnityEngine.Object.Instantiate(far.GetComponent<MeshFilter>().sharedMesh);
                var vertices=replacement.vertices;for(int i=0;i<vertices.Length;i++) vertices[i]*=.5f;
                replacement.vertices=vertices;replacement.RecalculateBounds();
                far.GetComponent<PhysicalDestructible>().ApplyNetworkMesh(replacement,new[]{material});
                PlayerWorldLoading.NotifyChanged(far);loader.Refresh();
                System.Threading.Thread.Sleep(350);loader.Refresh();
                Require(!loader.IsLoaded(far),"Edited distant objects must unload again after the collision-safe mutation");
                loader.SetRange(100);
                Require(far.GetComponentsInChildren<MeshFilter>().Any(f=>f.sharedMesh==replacement)&&
                    far.GetComponentsInChildren<MeshCollider>().Any(c=>c.sharedMesh==replacement&&c.enabled)&&!disabled.enabled,
                    "Destruction geometry and disabled collider state must survive unloading and reloading");
                loader.SetRange(50);
                var projectile=Cube(root,"Collision projectile",new Vector3(5000,0,100),false);
                var wall=Cube(root,"Collision wall",projectile.transform.position+Vector3.right*5,false);
                wall.transform.localScale=new Vector3(1,5,5);
                var projectileBody=projectile.AddComponent<Rigidbody>();projectileBody.useGravity=false;
                projectileBody.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;projectileBody.linearVelocity=Vector3.right*5;
                PlayerWorldLoading.NotifyChanged(projectile);PlayerWorldLoading.NotifyChanged(wall);loader.Refresh();
                var physics=scene.GetPhysicsScene();Physics.SyncTransforms();
                for(int tick=0;tick<100;tick++) { loader.Refresh();physics.Simulate(.02f); }
                Require(projectile.transform.position.x<wall.transform.position.x-0.8f&&projectileBody.linearVelocity.x<.2f,
                    "A moving body beyond the player's range must actually collide with its loaded neighbors");
                UnityEngine.Object.DestroyImmediate(loader);
                // Separate worlds model separate browsers: a range change must
                // never mutate another client's object activation decisions.
                other=new GameObject("Other client's world");
                var otherEye=new GameObject("Other observer");otherEye.transform.SetParent(other.transform);otherEye.transform.position=Vector3.right*250;
                var otherNear=Cube(other,"Other near",Vector3.right*10);
                var otherFar=Cube(other,"Other far",Vector3.right*250);
                var first=root.AddComponent<PlayerWorldLoading>();Drain(first.Initialize(root,eye.transform,50,false));
                var second=other.AddComponent<PlayerWorldLoading>();Drain(second.Initialize(other,otherEye.transform,50,false));
                Require(first.IsLoaded(near)&&!first.IsLoaded(far)&&!second.IsLoaded(otherNear)&&second.IsLoaded(otherFar),
                    "Each client must use its own observer position");
                second.SetRange(300);Require(second.IsLoaded(otherNear)&&!first.IsLoaded(far),"Each client's loading range must be independent");
                SharedWorld();
                MenuSynchronization();
                Debug.Log("PLAYER_LOADING_RANGE_VALIDATION_PASS: independent clients, individual batch masks, 3D 20m bubbles, fast throws, sleeping bodies, detached trunks, root fragments, offline cleanup, retained damage/anchors, real collision beyond range");
            } finally {
                if(root!=null) UnityEngine.Object.DestroyImmediate(root);
                if(other!=null) UnityEngine.Object.DestroyImmediate(other);
                foreach(var target in detached) if(target!=null) UnityEngine.Object.DestroyImmediate(target);
                if(material!=null) UnityEngine.Object.DestroyImmediate(material);
                Physics.simulationMode=previousSimulation;Time.timeScale=previousScale;SceneManager.SetActiveScene(previous);
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene,true);
            }
        }
        static GameObject Cube(GameObject root,string name,Vector3 position,bool destructible=true)
        {
            var target=GameObject.CreatePrimitive(PrimitiveType.Cube);target.name=name;
            if(root!=null) target.transform.SetParent(root.transform);
            target.transform.position=position;
            if(destructible) PhysicalDestructible.Ensure(target);
            target.AddComponent<PickableObject>();return target;
        }
        static void Drain(IEnumerator routine) { while(routine.MoveNext()){} }
        static void MenuSynchronization()
        {
            var root=new GameObject("Menu synchronization fixture");
            try {
                var player=root.AddComponent<FirstPersonController>();
                var menu=root.AddComponent<PauseMenu>();
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                typeof(PauseMenu).GetField("player",flags).SetValue(menu,player);
                typeof(PauseMenu).GetField("initialized",flags).SetValue(menu,true);
                var page=typeof(PauseMenu).GetField("page",flags);
                page.SetValue(menu,Enum.Parse(page.FieldType,"Graphic"));
                player.SetPaused(false);
                typeof(PauseMenu).GetMethod("Update",flags).Invoke(menu,null);
                Require(player.IsPaused&&Cursor.lockState==CursorLockMode.None,
                    "A synchronization resume must never capture input behind an open Graphic menu");
                page.SetValue(menu,Enum.Parse(page.FieldType,"Closed"));player.SetPaused(false);
                typeof(PauseMenu).GetMethod("Update",flags).Invoke(menu,null);
                Require(!player.IsPaused,"A closed menu must allow normal gameplay controls");
            } finally { UnityEngine.Object.DestroyImmediate(root);Cursor.lockState=CursorLockMode.None;Cursor.visible=true; }
        }
        static void SharedWorld()
        {
            var root=new GameObject("Shared range fixture");
            try {
                var eye=new GameObject("Shared observer");eye.transform.SetParent(root.transform);
                var prop=Cube(root,"Shared distant prop",Vector3.right*1000);
                var attached=Cube(prop,"Attached distant part",Vector3.right*1050);
                var attachedNeighbor=Cube(root,"Attached part collision neighbor",Vector3.right*1069);
                var body=prop.AddComponent<Rigidbody>();body.useGravity=false;body.Sleep();
                var session=root.AddComponent<WebWorldSession>();Drain(session.Initialize(root,null));
                session.ReceiveWorld("{\"self\":\"host\",\"host\":\"host\",\"ready\":true}");
                var saved=session.CaptureForValidation(prop).objects[0];string id=WebWorldSession.IdOf(prop);
                var loader=root.AddComponent<PlayerWorldLoading>();Drain(loader.Initialize(root,eye.transform,50));
                Require(!loader.IsLoaded(prop),"A shared sleeping prop may unload locally");
                loader.SetRange(1500);loader.SetRange(50);
                Require(WebWorldSession.IdOf(prop)==id&&session.CaptureChangesForValidation().objects.Length==0,
                    "Changing local range must preserve shared identity and publish no destruction or world edits");
                saved.holder="friend";saved.writer="friend";saved.held=true;saved.revision=1;
                session.ReceiveWorld(JsonUtility.ToJson(new WebWorldSession.Packet {self="visitor",host="host",ready=true,objects=new[]{saved}}));
                loader.Refresh();
                Require(loader.IsLoaded(prop)&&body.isKinematic,"A distant prop held by another player must stay loaded on a follower");
                Require(loader.IsLoaded(attached)&&loader.IsLoaded(attachedNeighbor),
                    "Parts inheriting a held kinematic body must also stay loaded with their own collision neighborhood");
                saved.holder="";saved.held=false;saved.sleeping=true;saved.kinematic=false;saved.revision=2;
                session.ReceiveWorld(JsonUtility.ToJson(new WebWorldSession.Packet {self="visitor",host="host",ready=true,objects=new[]{saved}}));
                typeof(WebWorldSession).GetMethod("LateUpdate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(session,null);
                loader.Refresh();System.Threading.Thread.Sleep(350);loader.Refresh();
                Require(!loader.IsLoaded(prop),"A remotely stopped object must unload after its interpolation ends");
                session.ReceiveWorld("{\"self\":\"visitor\",\"host\":\"visitor\",\"ready\":true}");loader.Refresh();
                Require(!body.isKinematic&&body.IsSleeping(),"Host transfer must preserve sleeping distant bodies");
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        static void Require(bool value,string message) { if(!value) throw new InvalidOperationException(message); }
    }
}
