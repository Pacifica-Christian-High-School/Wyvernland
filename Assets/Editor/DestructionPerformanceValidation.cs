using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace Wyvernland.Editor {
 public static class DestructionPerformanceValidation {
  [MenuItem("Tools/Wyvernland/Validate Repeated Destruction Cost")]
  public static void RunRepeatedDestructionCost() {
   var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);
   cube.name="Repeated destruction cost validation";
   Mesh source=cube.GetComponent<MeshFilter>().sharedMesh;
   Mesh dense=DenseChipSource();
   var material=cube.GetComponent<MeshRenderer>().sharedMaterial;
   var properties=PhysicalDestructible.Ensure(cube); properties.ApplyMaterial("Wood");
   cube.AddComponent<DestructionFragment>();
   var active=typeof(DestructionFragment).GetProperty("ActiveColliders",BindingFlags.Static|BindingFlags.NonPublic);
   int registered=((System.Collections.Generic.IEnumerable<Collider>)active.GetValue(null)).Count();
   var replace=typeof(PhysicalDestructible).GetMethod("ReplaceGeometry",BindingFlags.Instance|BindingFlags.NonPublic);
   var snapshot=typeof(PhysicalDestructible).GetMethod("Snapshot",BindingFlags.Instance|BindingFlags.NonPublic);
   try {
    for(int hit=0;hit<80;hit++) {
     replace.Invoke(properties,new object[]{UnityEngine.Object.Instantiate(source),new[]{material},new MaterialPropertyBlock[1],null});
     if(cube.transform.childCount!=1||cube.GetComponentsInChildren<Collider>(true).Length!=2)
      throw new Exception("Repeated partial breaks must keep only the current remainder and original collider");
     if(((System.Collections.Generic.IEnumerable<Collider>)active.GetValue(null)).Count()!=registered+1)
      throw new Exception("Superseded colliders must also leave the debris collision registry");
    }
    var ready=typeof(PhysicalDestructible).GetMethod("GeometryAttemptReady",BindingFlags.Instance|BindingFlags.NonPublic);
    typeof(PhysicalDestructible).GetField("failedGeometryAttempts",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(properties,4);
    typeof(PhysicalDestructible).GetField("nextGeometryAttempt",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(properties,Time.time+100f);
    if((bool)ready.Invoke(properties,new object[]{Vector3.zero})||
       !(bool)ready.Invoke(properties,new object[]{Vector3.right}))
     throw new Exception("Failed contact backoff must allow damage at another contact point");
    cube.transform.position=Vector3.right*10f;
    if((bool)ready.Invoke(properties,new object[]{cube.transform.position}))
     throw new Exception("Moving the object must not retry the same failed local contact");
    cube.transform.position=Vector3.zero;
    typeof(PhysicalDestructible).GetMethod("NotifyGeometryChanged",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(properties,null);
    if(!(bool)ready.Invoke(properties,new object[]{Vector3.zero}))
     throw new Exception("Geometry changes must reset failed-contact backoff");
    object[] snapshotArgs={null,null};
    Mesh live=(Mesh)snapshot.Invoke(properties,snapshotArgs);
    if(live==null||!DestructionMesh.IsClosedConnected(live)) throw new Exception("Remainder cleanup must retain a closed destructible solid");
    UnityEngine.Object.DestroyImmediate(live);
    var refine=typeof(DestructionMesh).GetMethod("RefineNear",BindingFlags.Static|BindingFlags.NonPublic);
    if(!DestructionMesh.IsClosedConnected(dense)) throw new Exception("The dense cost probe must be a closed solid");
    var faces=(System.Collections.ICollection)refine.Invoke(null,new object[]{dense,Vector3.up*.5f,.00001f});
    if(faces.Count>4096) throw new Exception("A tiny chip radius must not inflate the mesh beyond the refinement budget");
    var timer=Stopwatch.StartNew();
    for(int attempt=0;attempt<5;attempt++) {
     var chips=PhysicalDestructible.ChipWithinFragmentBudget(dense,Vector3.one*1000f,.01f,out var remainder,20,9);
     if(chips.Count!=0||remainder!=null) throw new Exception("A missed cut must leave the source intact");
    }
    timer.Stop();
    double failedCutMs=timer.Elapsed.TotalMilliseconds/5;
    replace.Invoke(properties,new object[]{UnityEngine.Object.Instantiate(dense),new[]{material},new MaterialPropertyBlock[1],null});
    cube.transform.localScale=Vector3.one*.04f;
    properties.RefreshMass();
    typeof(PhysicalDestructible).GetField("nextImpact",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(properties,-1f);
    long allocated=GC.GetAllocatedBytesForCurrentThread();
    timer.Restart();
    for(int hit=0;hit<100;hit++)
     if(properties.ApplyImpact(10f,10f,2f,Vector3.up*.02f,Vector3.up,30f))
      throw new Exception("A compact fragment below the volume floor must remain protected");
    timer.Stop();
    if((GC.GetAllocatedBytesForCurrentThread()-allocated)/100>10000)
     throw new Exception("Protected compact fragments must skip expensive mesh snapshots and topology validation");
    UnityEngine.Debug.Log("REPEATED_DESTRUCTION_COST_VALIDATION_PASS: 80 replacements retain one shell; refinement stays bounded; dense failed-cut average ms="+failedCutMs.ToString("F3")+"; protected-fragment collision average ms="+(timer.Elapsed.TotalMilliseconds/100).ToString("F3"));
   } finally { UnityEngine.Object.DestroyImmediate(cube); UnityEngine.Object.DestroyImmediate(dense); }
  }
  static Mesh DenseChipSource() {
   const int sides=768;
   var vertices=new Vector3[sides*2+2];
   vertices[sides*2]=Vector3.up*.5f; vertices[sides*2+1]=Vector3.down*.5f;
   var triangles=new System.Collections.Generic.List<int>(sides*12);
   for(int i=0;i<sides;i++) {
    float angle=i*Mathf.PI*2f/sides;
    vertices[i]=new Vector3(Mathf.Cos(angle)*.5f,.5f,Mathf.Sin(angle)*.5f);
    vertices[i+sides]=vertices[i]+Vector3.down;
    int next=(i+1)%sides;
    triangles.AddRange(new[]{sides*2,next,i,sides*2+1,i+sides,next+sides,
      i,next,i+sides,next,next+sides,i+sides});
   }
   var mesh=new Mesh { vertices=vertices,triangles=triangles.ToArray() };
   mesh.RecalculateBounds();return mesh;
  }
  static void Measure(string name,int count,Action action) {
   action(); GC.Collect();
   long bytes=GC.GetAllocatedBytesForCurrentThread();
   var timer=Stopwatch.StartNew();
   for(int i=0;i<count;i++) action();
   timer.Stop();
   UnityEngine.Debug.Log("DESTRUCTION_BENCH "+name+" ms="+(timer.Elapsed.TotalMilliseconds/count).ToString("F3")+" bytes="+((GC.GetAllocatedBytesForCurrentThread()-bytes)/count));
  }
  public static void Run() {
   var sphere=GameObject.CreatePrimitive(PrimitiveType.Sphere);
   var original=sphere.GetComponent<MeshFilter>().sharedMesh;
   var vertices=original.vertices; var triangles=original.triangles;
   var flat=new Mesh {vertices=triangles.Select(index=>vertices[index]).ToArray(),triangles=Enumerable.Range(0,triangles.Length).ToArray()};
   flat.RecalculateBounds();
   UnityEngine.Object.DestroyImmediate(sphere);
   try {
    var densePiece=new GameObject("Dense fragment collision validation");
    var hand=new GameObject("Dense fragment pickup hand");
    try {
     var addCollider=typeof(PhysicalDestructible).GetMethod("AddFragmentCollider",
      BindingFlags.Static|BindingFlags.NonPublic);
     var collider=(Collider)addCollider.Invoke(null,new object[]{densePiece,flat,true});
     if(collider is not MeshCollider { convex:true } precise||precise.sharedMesh==flat||
      densePiece.GetComponent<BoxCollider>()!=null)
      throw new Exception("Dense dynamic fragments must use a fitted convex collision proxy");
     densePiece.transform.SetPositionAndRotation(new Vector3(12,3,-7),Quaternion.Euler(13,42,26));
     Physics.SyncTransforms();
     if(Vector3.Distance(collider.bounds.center,densePiece.transform.TransformPoint(flat.bounds.center))>.08f)
      throw new Exception("Dense fragment collider must stay centered on the visible mesh after rotation");
     densePiece.AddComponent<MeshFilter>().sharedMesh=flat;
     densePiece.AddComponent<MeshRenderer>();
     densePiece.AddComponent<DestructionFragment>();
     var pickup=densePiece.AddComponent<PickableObject>();
     pickup.SetNearbyDetection(true);
     pickup.PickUp(hand.transform);
     if(densePiece.GetComponent<MeshCollider>()!=precise||
      densePiece.GetComponent<BoxCollider>()!=null||precise.sharedMesh==flat)
      throw new Exception("Pickup must preserve the dense fragment's fitted collision proxy");
    } finally {
     UnityEngine.Object.DestroyImmediate(densePiece);
     UnityEngine.Object.DestroyImmediate(hand);
    }
    Measure("closed_flat_sphere",12,()=>{ if(!DestructionMesh.IsClosedConnected(flat)) throw new Exception("Sphere must remain closed"); });
    Measure("mass_flat_sphere",12,()=>{ if(!MeshSolidVolume.TryMeasure(flat,out _,out bool open)||open) throw new Exception("Sphere must have material mass"); });
    Measure("wood_local_chip",3,()=>{
     var pieces=PhysicalDestructible.ChipWithinFragmentBudget(flat,Vector3.up*0.5f,0.15f,out var remainder,20,9);
     foreach(var piece in pieces) UnityEngine.Object.DestroyImmediate(piece);
     if(remainder!=null) UnityEngine.Object.DestroyImmediate(remainder);
    });
    Measure("wood_impact",3,()=>{
     UnityEngine.Random.InitState(20260928);
     var cube=GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name="Performance Wood"; cube.transform.localScale=Vector3.one*2f;
     var properties=PhysicalDestructible.Ensure(cube);
     properties.ApplyImpact(10f,10f,0.5f,Vector3.up,Vector3.up,30f,"Stone");
     if(cube!=null) UnityEngine.Object.DestroyImmediate(cube);
     foreach(var piece in UnityEngine.Object.FindObjectsByType<DestructionFragment>(FindObjectsSortMode.None)) UnityEngine.Object.DestroyImmediate(piece.gameObject);
    });
    UnityEngine.Debug.Log("DESTRUCTION_PERFORMANCE_VALIDATION_PASS");
   } finally { UnityEngine.Object.DestroyImmediate(flat); }
  }
 }
}
