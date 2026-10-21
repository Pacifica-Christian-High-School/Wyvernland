using System;
using System.Collections;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Wyvernland.Editor
{
    public static class WebSharedWorldValidation
    {
        [MenuItem("Tools/Wyvernland/Validate Shared World")]
        public static void Run()
        {
            GameObject sourceRoot=null,replicaRoot=null;
            var sourceFragments=new System.Collections.Generic.List<GameObject>();
            try {
                var pickupWorld=Fixture(out sourceRoot,out GameObject pickedTree,true);
                pickupWorld.ReceiveWorld("{\"self\":\"host\",\"host\":\"host\",\"ready\":true}");
                var guestPickup=pickupWorld.CaptureForValidation(pickedTree).objects[0];
                guestPickup.holder="guest";guestPickup.writer="guest";guestPickup.revision=1;guestPickup.held=true;
                pickupWorld.ReceiveWorld(JsonUtility.ToJson(new WebWorldSession.Packet {
                    self="host",host="host",ready=true,objects=new[]{guestPickup} }));
                string childId=WebWorldSession.IdOf(pickedTree.transform.Find("Attached branch fixture").gameObject);
                Require(pickupWorld.CaptureChangesForValidation().objects.Any(state=>state.id==childId&&state.hasTree&&state.tree.falling),
                    "Guest pickup must publish inherited support changes for attached branches");
                UnityEngine.Object.DestroyImmediate(sourceRoot);sourceRoot=null;
                var source=Fixture(out sourceRoot,out GameObject stone);
                source.ReceiveWorld("{\"self\":\"host\",\"host\":\"host\",\"ready\":true}");
                stone.transform.position=new Vector3(3,2,-4);stone.transform.rotation=Quaternion.Euler(0,35,0);
                stone.GetComponent<PhysicalDestructible>().Hardness=3.5f;
                var body=stone.GetComponent<Rigidbody>();body.linearVelocity=new Vector3(2,0,0);
                var captured=source.CaptureForValidation(stone);
                body.linearVelocity=new Vector3(1500f,0,0);
                body.angularVelocity=new Vector3(0,1500f,0);
                var fast=source.CaptureForValidation(stone).objects[0];
                Require(fast.v.All(value=>float.IsFinite(value)&&Mathf.Abs(value)<=1000f)&&
                    fast.w.All(value=>float.IsFinite(value)&&Mathf.Abs(value)<=1000f),
                    "Fast physics and held-motion samples must fit the world protocol");
                body.linearVelocity=new Vector3(2,0,0);body.angularVelocity=Vector3.zero;
                var rope=sourceRoot.GetComponentInChildren<RopeObject>();
                var ropeSnapshot=source.CaptureForValidation(rope.gameObject);
                captured.objects=captured.objects.Concat(ropeSnapshot.objects).ToArray();
                string id=captured.objects[0].id;captured.objects[0].revision=1;
                captured.objects[1].revision=1;
                Require(captured.meshes.Length==1,"Actual geometry must be included");
                var mesh=SharedMeshCodec.Decode(captured.meshes[0],out var materials);
                Require(mesh.vertexCount>0&&mesh.triangles.Length>0,"Mesh codec round trip");
                UnityEngine.Object.DestroyImmediate(mesh);foreach(var material in materials) UnityEngine.Object.DestroyImmediate(material);
                var originalRandom=UnityEngine.Random.state;UnityEngine.Random.InitState(12);
                stone.GetComponent<PhysicalDestructible>().ApplyImpact(10f,10f,3f,stone.transform.position,Vector3.right,100f,"Iron",2d,false,20f);
                UnityEngine.Random.state=originalRandom;
                var damage=source.CaptureChangesForValidation();
                foreach(var state in damage.objects.Where(state=>state.id.StartsWith("d"))) sourceFragments.Add(WebWorldSession.ObjectOf(state.id));
                Require(damage.objects.Any(state=>state.id.StartsWith("d")),"Real destruction must publish spawned fragments");
                foreach(var state in damage.objects) state.revision=3;
                UnityEngine.Object.DestroyImmediate(sourceRoot);sourceRoot=null;
                var replica=Fixture(out replicaRoot,out GameObject other);
                Require(WebWorldSession.IdOf(other)==id,"Baseline identities must match between worlds");
                replica.ReceiveWorld(JsonUtility.ToJson(new WebWorldSession.Packet { self="visitor",host="host",ready=false,objects=captured.objects,meshes=captured.meshes }));
                Require(Vector3.Distance(other.transform.position,new Vector3(3,2,-4))<.0001f,"Late join must restore world position");
                Require(Mathf.Abs(other.GetComponent<PhysicalDestructible>().Hardness-3.5f)<.0001f,"Shared material state");
                Require(other.GetComponent<Rigidbody>().isKinematic,"Followers must not run separate object physics");
                Require(other.GetComponentsInChildren<MeshFilter>().Any(filter=>filter.sharedMesh!=null&&filter.sharedMesh.vertexCount>0),"Restored solid geometry");
                var restoredRope=replicaRoot.GetComponentInChildren<RopeObject>();
                for(int i=0;i<restoredRope.Bodies.Count;i++) Require(Vector3.Distance(restoredRope.Bodies[i].position,captured.objects[1].rope.positions[i])<.0001f,"Rope links restore their shared positions");
                var held=captured.objects[0];held.revision=2;held.holder="friend";held.writer="friend";held.p=new[]{22f,2f,-4f};held.held=true;
                replica.ReceiveWorld(JsonUtility.ToJson(new WebWorldSession.Packet { self="visitor",host="host",ready=true,objects=new[]{held} }));
                Require(!WebWorldSession.CanInteract(other),"Other players cannot take a held object");
                Require(Mathf.Abs(other.transform.position.x-22f)<.0001f,"Held object is visible at its shared position");
                replica.ReceiveWorld(JsonUtility.ToJson(new WebWorldSession.Packet { self="visitor",host="host",ready=false,objects=damage.objects,meshes=damage.meshes }));
                foreach(var state in damage.objects.Where(state=>!state.deleted&&state.id.StartsWith("d"))) {
                    var fragment=WebWorldSession.ObjectOf(state.id);Require(fragment!=null,"Late join must restore actual fragments");
                    Require(Vector3.Distance(fragment.transform.position,WebWorldSession.Vector(state.p))<.0001f,"Fragment world positions must match");
                    Require(fragment.GetComponentsInChildren<MeshFilter>().Any(filter=>filter.sharedMesh!=null),"Fragment cut meshes must match");
                }
                if(damage.objects.Any(state=>state.id==id&&state.deleted)) Require(WebWorldSession.ObjectOf(id)==null,"Source removal must be shared");
                replica.ReceiveWorld("{\"self\":\"visitor\",\"host\":\"visitor\",\"ready\":true}");
                foreach(var state in damage.objects.Where(state=>!state.deleted&&state.body&&!state.kinematic)) {
                    var restored=WebWorldSession.ObjectOf(state.id);if(restored!=null) Require(!restored.GetComponent<Rigidbody>().isKinematic,"New host resumes saved body physics");
                }
                Debug.Log("SHARED_WORLD_VALIDATION_PASS: stable IDs, actual destruction, mesh codec, late join, held visibility, ownership, host physics transfer");
            } finally {
                if(sourceRoot!=null) UnityEngine.Object.DestroyImmediate(sourceRoot);
                if(replicaRoot!=null) UnityEngine.Object.DestroyImmediate(replicaRoot);
                foreach(var fragment in sourceFragments) if(fragment!=null) UnityEngine.Object.DestroyImmediate(fragment);
                Time.timeScale=1f;
            }
        }
        static WebWorldSession Fixture(out GameObject root,out GameObject stone,bool withChild=false)
        {
            root=new GameObject("Shared validation world");
            var treeRoot=new GameObject("Tree fixture");treeRoot.transform.SetParent(root.transform,false);treeRoot.AddComponent<GeneratedTree>().Configure(.5f,3);
            stone=GameObject.CreatePrimitive(PrimitiveType.Cube);
            stone.name="Stone fixture";stone.transform.SetParent(treeRoot.transform,false);stone.transform.position=Vector3.up*.5f;
            stone.AddComponent<Rigidbody>();PhysicalDestructible.Ensure(stone);stone.AddComponent<PickableObject>().DisplayName="Stone";
            var structure=stone.AddComponent<TreeStructuralPart>();structure.SetTrunk(true);structure.SetGroundAnchor();
            if(withChild) {
                var child=GameObject.CreatePrimitive(PrimitiveType.Cube);child.name="Attached branch fixture";
                child.transform.SetParent(stone.transform,false);child.transform.localPosition=Vector3.up;
                child.transform.localScale=Vector3.one*.2f;PhysicalDestructible.Ensure(child);child.AddComponent<PickableObject>();
                child.AddComponent<TreeStructuralPart>().AttachTo(structure);
            }
            RopeObject.Create(root.transform,VineKind.Black,.02f,1f,stone.transform,Vector3.up,stone.GetComponent<Renderer>().sharedMaterial);
            var sessionRoot=new GameObject("Wyvernland Shared World");sessionRoot.transform.SetParent(root.transform,false);
            var session=sessionRoot.AddComponent<WebWorldSession>();IEnumerator initialize=session.Initialize(root,null);while(initialize.MoveNext()){}
            return session;
        }
        static void Require(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
    }
}
