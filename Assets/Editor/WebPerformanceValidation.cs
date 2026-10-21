using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Wyvernland.Editor
{
    public static class WebPerformanceValidation
    {
        [MenuItem("Tools/Wyvernland/Validate Browser Performance")]
        public static void Run()
        {
            GameObject root=null;
            float previousScale=Time.timeScale;
            try {
                Time.timeScale=1f;
                root=new GameObject("Browser performance fixture");
                for(int i=0;i<3000;i++) {
                    var target=GameObject.CreatePrimitive(PrimitiveType.Cube);target.name="Static fixture "+i;
                    target.transform.SetParent(root.transform,false);target.transform.position=new Vector3(i%100*3,0,i/100*3);
                    PhysicalDestructible.Ensure(target);target.AddComponent<PickableObject>();
                }
                var support=GameObject.CreatePrimitive(PrimitiveType.Cube);support.name="Rope support";
                support.transform.SetParent(root.transform,false);support.transform.position=Vector3.up*4;
                PhysicalDestructible.Ensure(support);
                for(int i=0;i<16;i++) RopeObject.Create(root.transform,VineKind.Black,.02f,2f,
                    support.transform,new Vector3(i*2,4,0),support.GetComponent<Renderer>().sharedMaterial);
                var ropes=root.GetComponentsInChildren<RopeObject>();
                var renderers=root.GetComponentsInChildren<MeshRenderer>();
                var partitions=ResidentPropRendering.PartitionGroup(renderers).ToArray();
                Require(partitions.Length>1&&partitions.Sum(part=>part.Count)==renderers.Length&&
                    partitions.All(part=>part.Sum(renderer=>renderer.GetComponent<MeshFilter>().sharedMesh.vertexCount)<=32768),
                    "Dense browser prop cells must split into bounded batches instead of bypassing the draw cache");
                Require(ropes.All(rope=>rope.Bodies.All(body=>body.IsSleeping())),"Generated ropes start asleep");
                var session=root.AddComponent<WebWorldSession>();
                var clock=Stopwatch.StartNew();IEnumerator initialization=session.Initialize(root,null);while(initialization.MoveNext()){}
                Require(session.CapturedStateCount==ropes.Length,"Static props must not allocate complete network snapshots during startup");
                Require(session.ColliderRefreshCount==0,"Unchanged baseline colliders must not be rebuilt for synchronization");
                session.ReceiveWorld("{\"self\":\"host\",\"host\":\"host\",\"ready\":true}");
                Require(ropes.All(rope=>rope.Bodies.All(body=>body.IsSleeping())),"Room activation must retain sleeping rope islands");
                int snapshots=session.CapturedStateCount,ropeSnapshots=ropes.Sum(rope=>rope.NetworkCaptureCount);
                for(int tick=0;tick<50;tick++) {
                    var changes=session.CaptureChangesForValidation();
                    Require(changes.objects.Length==0,"Idle ropes must not produce world updates");
                }
                Require(session.CapturedStateCount==snapshots&&ropes.Sum(rope=>rope.NetworkCaptureCount)==ropeSnapshots,
                    "Idle polling must not allocate object or rope snapshots");
                var moving=ropes[0];moving.Bodies[0].WakeUp();moving.Bodies[0].linearVelocity=Vector3.right*3;
                var movement=session.CaptureChangesForValidation();
                Require(movement.objects.Any(state=>state.id==WebWorldSession.IdOf(moving.gameObject)),"Moving ropes must still publish");
                foreach(var state in movement.objects) state.revision=1;
                session.ReceiveWorld(JsonUtility.ToJson(new WebWorldSession.Packet {
                    self="visitor",host="host",ready=true,objects=movement.objects,meshes=movement.meshes }));
                int meshes=ropes.Sum(rope=>rope.VisualUpdateCount);
                var lateUpdate=typeof(RopeObject).GetMethod("LateUpdate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                for(int frame=0;frame<50;frame++) foreach(var rope in ropes) lateUpdate.Invoke(rope,null);
                Require(ropes.Sum(rope=>rope.VisualUpdateCount)==meshes,"Idle follower ropes must not rebuild meshes");
                session.ReceiveWorld("{\"self\":\"visitor\",\"host\":\"visitor\",\"ready\":true}");
                Require(!moving.Bodies[0].IsSleeping()&&moving.Bodies[0].linearVelocity.x>2.9f,
                    "Host transfer must preserve moving rope momentum");
                var area=WorldConfig.MapBounds;
                foreach(var eye in new[]{Vector3.zero,new Vector3(250,2,350),new Vector3(area.xMin,0,area.yMin),new Vector3(area.xMax,0,area.yMax)}) {
                    RectInt window=ProceduralGrass.PatchWindow(area,eye,ProceduralGrass.BrowserDrawDistance);
                    Require(window.width*window.height<=121,"Browser grass must visit a bounded nearby patch window");
                }
                var grassRoot=new GameObject("Grass initialization fixture");grassRoot.transform.SetParent(root.transform);
                var grass=grassRoot.AddComponent<ProceduralGrass>();
                var camera=grassRoot.AddComponent<Camera>();
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                // Match the state at the first coroutine yield: camera, bounds
                // and material exist, but the mesh and patch list do not.
                typeof(ProceduralGrass).GetField("playerCamera",flags).SetValue(grass,camera);
                typeof(ProceduralGrass).GetField("bounds",flags).SetValue(grass,area);
                typeof(ProceduralGrass).GetField("material",flags).SetValue(grass,new Material(Resources.Load<Shader>("Grass")));
                for(int frame=0;frame<3;frame++) typeof(ProceduralGrass).GetMethod("LateUpdate",flags).Invoke(grass,null);
                Require(grass.LastVisitedPatchCount==0&&grass.LastDrawnPatchCount==0,
                    "Grass must wait for its patch grid while incremental initialization yields");
                UnityEngine.Debug.Log($"WEB_PERFORMANCE_VALIDATION_PASS: 3000 static props, {ropes.Length} ropes, 50 idle polls: zero snapshots, zero collider refreshes, zero follower mesh rebuilds; moving host transfer; grass <=121 candidate patches and safe initialization; {clock.Elapsed.TotalMilliseconds:F1}ms native fixture");
            } finally {
                if(root!=null) UnityEngine.Object.DestroyImmediate(root);
                Time.timeScale=previousScale;
            }
        }
        static void Require(bool value,string message) { if(!value) throw new InvalidOperationException(message); }
    }
}
