using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Wyvernland.Editor
{
    public static class RoomObjectPhysicsValidation
    {
        [MenuItem("Tools/Wyvernland/Validate Room Object Gravity")]
        public static void Run()
        {
            var previous=SceneManager.GetActiveScene();
            // Batch snapshots can open on an untitled startup scene. Unity
            // requires a saved scene before creating an additive editor fixture.
            if(Application.isBatchMode&&string.IsNullOrEmpty(previous.path)) {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
                previous=SceneManager.GetActiveScene();
            }
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var simulation=Physics.simulationMode;
            float scale=Time.timeScale;
            try {
                Physics.simulationMode=SimulationMode.Script;Time.timeScale=1;
                ValidateFollowerAndTakeover();
                ValidateReservations();
                ValidateHeldAndReleased();
                ValidateFrozenSave(false);
                ValidateFrozenSave(true);
                ValidateSupportedTree();
                Debug.Log("ROOM_OBJECT_GRAVITY_VALIDATION_PASS: real simulated falls during reservations, cancelled reach, guest release, legacy frozen state recovery, follower physics capture, host takeover, and supported/held object preservation.");
            } finally {
                Physics.simulationMode=simulation;Time.timeScale=scale;
                SceneManager.SetActiveScene(previous);
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene,true);
            }
        }

        static WebWorldSession Fixture(out GameObject root,out PickableObject pickup,bool body=true)
        {
            root=new GameObject("Room gravity validation");
            var stone=GameObject.CreatePrimitive(PrimitiveType.Cube);stone.name="Loose stone";
            stone.transform.SetParent(root.transform,false);stone.transform.position=new Vector3(3000,1000,3000);
            stone.transform.localScale=Vector3.one*.1f;
            if(body) stone.AddComponent<Rigidbody>();
            pickup=stone.AddComponent<PickableObject>();
            var node=new GameObject("Wyvernland Shared World");node.transform.SetParent(root.transform,false);
            return node.AddComponent<WebWorldSession>();
        }
        static void Initialize(WebWorldSession session,GameObject root,bool host)
        {
            IEnumerator initialize=session.Initialize(root,null);while(initialize.MoveNext()){}
            Packet(session,host);
        }
        static void Packet(WebWorldSession session,bool host,WebWorldSession.ObjectState state=null,WebWorldSession.Claim claim=null)
        {
            session.ReceiveWorld(JsonUtility.ToJson(new WebWorldSession.Packet { self="self",host=host?"self":"host",ready=true,
                objects=state==null?null:new[]{state},claims=claim==null?null:new[]{claim} }));
        }
        static void Fall(Rigidbody body,string reason)
        {
            Check(body!=null&&!body.isKinematic&&body.useGravity,reason+" enables dynamic gravity");
            body.WakeUp();Physics.SyncTransforms();float start=body.position.y;
            var physics=body.gameObject.scene.GetPhysicsScene();
            for(int i=0;i<15;i++) physics.Simulate(.02f);
            Check(body.position.y<start-.15f&&body.linearVelocity.y<-.5f,reason+" actually falls under physics");
        }
        static void ValidateFollowerAndTakeover()
        {
            var session=Fixture(out var root,out var pickup);
            try {
                Initialize(session,root,false);
                Check(pickup.GetComponent<Rigidbody>().isKinematic,"A guest observes host physics without duplicating simulation");
                var saved=session.CaptureForValidation(pickup.gameObject).objects[0];
                Check(!saved.kinematic&&saved.gravity,"A follower must capture authoritative dynamic flags instead of its temporary kinematic replica flags");
                saved.revision=1;Packet(session,false,saved);
                Packet(session,true);
                Fall(pickup.GetComponent<Rigidbody>(),"Host takeover");
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        static void ValidateReservations()
        {
            var session=Fixture(out var root,out var pickup);
            try {
                Initialize(session,root,true);
                var state=session.CaptureForValidation(pickup.gameObject).objects[0];
                state.holder="guest";state.writer="guest";state.revision=1;
                Packet(session,true,state);
                Fall(pickup.GetComponent<Rigidbody>(),"An unheld guest reservation");
                state.holder="";state.revision=2;Packet(session,true,state);
                Check(WebWorldSession.TryClaim(pickup),"The host can reserve an available stone");
                Packet(session,true,claim:new WebWorldSession.Claim { id=state.id,owned=true });
                // Existing saved objects may retain a previous guest's writer.
                state.holder="self";state.writer="guest";state.revision=3;Packet(session,true,state);
                var requests=new List<WebWorldSession.Request>();session.RequestForValidation+=requests.Add;
                // Exercise the same maintenance pass used by LateUpdate without a real-time wait.
                session.MaintainOwnedObjects(Time.unscaledTime+3f);
                Check(requests.Count==1&&requests[0].action=="unclaim","A cancelled reach releases its reservation without turning a static object into a dropped prop");
                Check(WebWorldSession.CanInteract(pickup.gameObject),"Cancelling a reservation restores availability");
                Fall(pickup.GetComponent<Rigidbody>(),"Cancelled host reservation");
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        static void ValidateHeldAndReleased()
        {
            var session=Fixture(out var root,out var pickup);
            try {
                Initialize(session,root,true);
                var state=session.CaptureForValidation(pickup.gameObject).objects[0];
                state.holder="guest";state.writer="guest";state.held=true;state.kinematic=true;state.gravity=false;state.revision=1;
                Packet(session,true,state);
                var body=pickup.GetComponent<Rigidbody>();float start=body.position.y;
                Physics.SyncTransforms();for(int i=0;i<5;i++) body.gameObject.scene.GetPhysicsScene().Simulate(.02f);
                Check(body.isKinematic&&Mathf.Abs(body.position.y-start)<.001f,"A held guest object stays at the network hand pose");
                Check(session.CaptureForValidation(pickup.gameObject).objects[0].held,
                    "A host snapshot preserves a guest's actual held state instead of treating its replica as an unheld reservation");
                state.holder="";state.held=false;state.kinematic=false;state.gravity=true;state.sleeping=false;
                state.v=new[]{2f,0f,0f};state.revision=2;Packet(session,true,state);
                Check(body.linearVelocity.x>1.9f,"A guest release preserves throw velocity on the host");
                Fall(body,"Guest drop");
                var drop=session.CaptureChangesForValidation().objects;
                Check(Array.Exists(drop,s=>s.id==state.id&&!s.held&&!s.kinematic&&s.gravity),"The host publishes falling poses for guests and late joiners");
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        static void ValidateFrozenSave(bool orphanedGrip)
        {
            var session=Fixture(out var root,out var pickup);
            try {
                Initialize(session,root,true);
                var state=session.CaptureForValidation(pickup.gameObject).objects[0];
                state.holder="";state.writer="old-host";state.held=orphanedGrip;state.kinematic=true;state.gravity=false;state.sleeping=false;state.revision=1;
                Packet(session,true,state);
                Fall(pickup.GetComponent<Rigidbody>(),"Previously saved loose floating stone");
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        static void ValidateSupportedTree()
        {
            var session=Fixture(out var root,out var pickup,false);
            try {
                var structure=pickup.gameObject.AddComponent<TreeStructuralPart>();structure.SetTrunk(true);structure.SetGroundAnchor();
                Initialize(session,root,true);
                var state=session.CaptureForValidation(pickup.gameObject).objects[0];
                Check(!state.body&&state.hasTree&&!state.tree.falling,"Supported tree fixture starts without a dynamic body");
                state.holder="guest";state.writer="guest";state.revision=1;Packet(session,true,state);
                Check(pickup.GetComponent<Rigidbody>()==null&&!structure.CaptureNetworkStructure().falling,
                    "A guest reserving a branch cannot detach its support or create a frozen Rigidbody");
                state.holder="";state.revision=2;Packet(session,true,state);
                Check(pickup.GetComponent<Rigidbody>()==null,"Cancelling a reservation preserves static authored support");
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        static void Check(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
    }
}
