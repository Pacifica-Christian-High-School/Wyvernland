using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Wyvernland.Editor
{
    public static class SmallFragmentRestValidation
    {
        static void Check(bool value,string message)
        { if(!value) throw new InvalidOperationException("Small fragments: "+message); }

        static GameObject Piece(Transform parent)
        {
            var piece=GameObject.CreatePrimitive(PrimitiveType.Cube);
            piece.name="Persistent small fragment";piece.transform.SetParent(parent,false);
            piece.transform.localScale=Vector3.one*.05f;
            piece.AddComponent<Rigidbody>().useGravity=false;
            PhysicalDestructible.Ensure(piece).ApplyMaterial("Wood");
            piece.AddComponent<DestructionFragment>();SmallFragmentRest.Ensure(piece);
            return piece;
        }
        public static void RunRegression() { PhysicalDestructionValidation.RunSmallFragmentPersistence();Run(); }
        public static void Run()
        {
            var previous=SceneManager.GetActiveScene();
            if(string.IsNullOrEmpty(previous.path)) { EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");previous=SceneManager.GetActiveScene(); }
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
            try { ValidateRest();ValidateRoom(); }
            finally { SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true); }
            Debug.Log("SMALL_FRAGMENT_REST_VALIDATION_PASS: moving/spinning/held/loading guards, rest reset, preserved geometry/materials/scale, removed physics, independent children, cleanup key protection, persistent room state, old expiry migration and host transfer.");
        }
        static void ValidateRest()
        {
            var root=new GameObject("Fragment rest fixture");root.transform.position=new Vector3(3700,1000,0);
            try
            {
                var piece=Piece(root.transform);var rest=piece.GetComponent<SmallFragmentRest>();var body=piece.GetComponent<Rigidbody>();
                var mesh=piece.GetComponent<MeshFilter>().sharedMesh;var material=piece.GetComponent<Renderer>().sharedMaterial;
                var scale=piece.transform.localScale;
                void Moving(Vector3 linear,Vector3 angular)
                {
                    body.WakeUp();body.linearVelocity=linear;body.angularVelocity=angular;
                    for(int i=0;i<100;i++) rest.CheckRest(.1f);
                    Check(!rest.IsSettled&&piece.GetComponent<Rigidbody>()==body&&piece.GetComponent<Collider>()!=null,"moving and spinning pieces keep physics beyond old expiry");
                }
                Moving(Vector3.right,Vector3.zero);Moving(Vector3.zero,Vector3.up);Moving(Vector3.right*.00001f,Vector3.zero);
                body.linearVelocity=body.angularVelocity=Vector3.zero;body.isKinematic=true;
                rest.CheckRest(10);Check(!rest.IsSettled,"loading or replica freeze is not rest");body.isKinematic=false;
                var hand=new GameObject("Holding hand");hand.transform.SetParent(root.transform,false);hand.transform.position=piece.transform.position;
                var pickup=piece.AddComponent<PickableObject>();Check(pickup.PickUp(hand.transform),"held fragment fixture");
                rest.CheckRest(10);Check(!rest.IsSettled&&pickup.IsHeld,"held zero-speed fragment keeps physics");
                pickup.Release(Vector3.zero,hand.transform);piece.transform.SetParent(root.transform,true);body.useGravity=false;body.WakeUp();
                body.linearVelocity=body.angularVelocity=Vector3.zero;rest.CheckRest(.1f);
                Moving(Vector3.right,Vector3.zero);
                body.linearVelocity=body.angularVelocity=Vector3.zero;rest.CheckRest(.1f);
                Check(!rest.IsSettled,"a new movement resets the continuous rest interval");
                var child=GameObject.CreatePrimitive(PrimitiveType.Cube);child.transform.SetParent(piece.transform,false);
                child.AddComponent<Rigidbody>().isKinematic=true;
                var attached=GameObject.CreatePrimitive(PrimitiveType.Cube);attached.transform.SetParent(piece.transform,false);
                PhysicalDestructible.Ensure(attached);
                rest.CheckRest(.1f);
                Check(rest.IsSettled&&piece.GetComponent<Rigidbody>()==null&&piece.GetComponent<Collider>()==null,
                    $"zero-speed fragment loses its Rigidbody and colliders (settled={rest.IsSettled}, body={piece.GetComponent<Rigidbody>()!=null}, colliders={piece.GetComponents<Collider>().Length})");
                Check(piece.GetComponent<MeshFilter>().sharedMesh==mesh&&piece.GetComponent<Renderer>().sharedMaterial==material&&piece.transform.localScale==scale,"appearance stays identical");
                Check(child.GetComponent<Rigidbody>()!=null&&child.GetComponent<Collider>()!=null,"independent child physics survives");
                Check(attached.GetComponent<Collider>()!=null,"another attached object's collider survives");
                DestructionFragment.DeleteAllExceptHeld();Check(piece!=null,"cleanup key cannot delete small fragments");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        static WebWorldSession Room(GameObject root)
        {
            var session=root.AddComponent<WebWorldSession>();var init=session.Initialize(root,null);while(init.MoveNext()){}return session;
        }
        static void ValidateRoom()
        {
            var root=new GameObject("Persistent fragments room");
            try
            {
                var host=Room(root);host.ReceiveWorld("{\"self\":\"host\",\"host\":\"host\",\"ready\":true}");
                var piece=Piece(root.transform);piece.transform.position=new Vector3(3750,1000,0);
                WebWorldSession.RegisterSpawn(piece);
                var body=piece.GetComponent<Rigidbody>();body.Sleep();
                piece.GetComponent<SmallFragmentRest>().CheckRest(.2f);
                var saved=host.CaptureForValidation(piece);var state=saved.objects[0];state.revision=1;
                Check(state.smallFragment&&state.fragmentSettled&&!state.body&&state.expires==0&&!state.deleted,"settlement saves visible geometry with no physics or expiry");
                Check(saved.meshes.Length>0,"persistent fragment geometry is saved");
                UnityEngine.Object.DestroyImmediate(root);root=new GameObject("Late fragment join");var guest=Room(root);
                guest.ReceiveWorld(JsonUtility.ToJson(new WebWorldSession.Packet { self="guest",host="host",ready=true,objects=saved.objects,meshes=saved.meshes }));
                var restored=WebWorldSession.ObjectOf(state.id);
                Check(restored!=null&&restored.GetComponent<SmallFragmentRest>().IsSettled&&restored.GetComponent<Rigidbody>()==null&&restored.GetComponentsInChildren<Collider>().Length==0,"late join restores settled appearance without physics");
                Check(restored.GetComponentsInChildren<MeshFilter>().Any(filter=>filter.sharedMesh!=null&&filter.sharedMesh.vertexCount>0)&&
                    restored.GetComponentsInChildren<Renderer>().Any(renderer=>renderer.enabled),"late join preserves visible geometry");
                guest.ReceiveWorld("{\"self\":\"guest\",\"host\":\"guest\",\"ready\":true}");
                Check(restored.GetComponent<Rigidbody>()==null&&guest.CaptureForValidation(restored).objects[0].fragmentSettled,"host transfer does not recreate physics");
                state.id="d"+Guid.NewGuid().ToString("N");state.smallFragment=state.fragmentSettled=false;
                state.body=true;state.kinematic=false;state.gravity=true;state.v=new float[]{1,0,0};
                state.expires=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-10000;state.revision=2;
                guest.ReceiveWorld(JsonUtility.ToJson(new WebWorldSession.Packet { self="guest",host="other",ready=true,objects=new[]{state},meshes=saved.meshes }));
                var old=WebWorldSession.ObjectOf(state.id);
                Check(old!=null&&old.GetComponent<SmallFragmentRest>()!=null&&old.GetComponent<TemporaryShatterDebris>()!=null,"old timed debris is retained instead of expiring");
                old.GetComponent<SmallFragmentRest>().CheckRest(100);
                Check(old.GetComponent<Rigidbody>()!=null&&guest.CaptureForValidation(old).objects[0].expires==0,"guest replica keeps physics and migrated expiry is zero");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        const string PlayKey="Wyvernland.SmallFragmentRestValidation";
        static double deadline;
        static GameObject playRoot,playPiece;
        static int settledFrame;
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
            try
            {
                if(EditorApplication.timeSinceStartup>deadline) throw new InvalidOperationException("Small fragment Play validation timed out");
                if(!Application.isPlaying||Time.deltaTime<=0) return;
                if(playRoot==null)
                {
                    Time.timeScale=1;Physics.simulationMode=SimulationMode.Script;
                    playRoot=new GameObject("Actual settling fragment");playPiece=Piece(playRoot.transform);
                    playPiece.transform.position=new Vector3(0,.5f,0);var body=playPiece.GetComponent<Rigidbody>();body.useGravity=true;
                    var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.SetParent(playRoot.transform,false);
                    floor.transform.localScale=new Vector3(5,.1f,5);floor.transform.position=Vector3.down*.05f;Physics.SyncTransforms();
                    var rest=playPiece.GetComponent<SmallFragmentRest>();
                    for(int i=0;i<600&&!rest.IsSettled;i++) { SceneManager.GetActiveScene().GetPhysicsScene().Simulate(.02f);rest.CheckRest(.02f); }
                    Check(rest.IsSettled,"a real falling fragment settles on the floor");settledFrame=Time.frameCount;return;
                }
                if(Time.frameCount<=settledFrame) return;
                Check(playPiece!=null&&playPiece.GetComponent<MeshRenderer>().enabled&&playPiece.GetComponent<MeshFilter>().sharedMesh!=null,"real deferred cleanup keeps the fragment visible");
                Check(playPiece.GetComponent<Rigidbody>()==null&&playPiece.GetComponentsInChildren<Collider>().Length==0,"real deferred cleanup removes only physics");
                Debug.Log("SMALL_FRAGMENT_REST_PLAY_VALIDATION_PASS: real falling/settling physics, deferred component removal and preserved visible geometry.");FinishPlay(0);
            }
            catch(Exception exception) { Debug.LogException(exception);FinishPlay(1); }
        }
        static void FinishPlay(int code)
        {
            SessionState.SetBool(PlayKey,false);EditorApplication.update-=PlayTick;EditorApplication.Exit(code);
        }
    }
}
