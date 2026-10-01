using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace Wyvernland.Editor {
 public static class DestructionPerformanceValidation {
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
     if(collider is not BoxCollider||densePiece.GetComponent<MeshCollider>()!=null)
      throw new Exception("Dense dynamic fragments must use a box collision proxy");
     densePiece.AddComponent<MeshFilter>().sharedMesh=flat;
     densePiece.AddComponent<MeshRenderer>();
     densePiece.AddComponent<DestructionFragment>();
     var pickup=densePiece.AddComponent<PickableObject>();
     pickup.SetNearbyDetection(true);
     pickup.PickUp(hand.transform);
     if(densePiece.GetComponent<MeshCollider>()!=null||
      densePiece.GetComponent<BoxCollider>()==null)
      throw new Exception("Pickup must preserve the dense fragment's box proxy");
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
