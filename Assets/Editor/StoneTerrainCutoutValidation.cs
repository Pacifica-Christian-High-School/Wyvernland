using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Wyvernland.Editor
{
    public static class StoneTerrainCutoutValidation
    {
        [MenuItem("Tools/Wyvernland/Validate Stone Terrain Cutouts")]
        public static void Run()
        {
            var root=new GameObject("Cutout validation");
            var colliders=new List<MeshCollider>();
            var surface=new Mesh();
            surface.vertices=new[]{new Vector3(-100,0,-100),new Vector3(-100,0,100),
                new Vector3(100,0,100),new Vector3(100,0,-100)};
            surface.triangles=new[]{0,1,2,0,2,3};
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh stone=cube.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(cube);
            using var terrain=new StoneTerrainCutout(root.transform);
            try
            {
                terrain.AddSurface(surface,null,"Ground",colliders.Add);
                var renderers=root.GetComponentsInChildren<MeshRenderer>();
                Require(renderers.Length==1&&colliders.Count==1,"The original surface must remain whole");
                Physics.SyncTransforms();
                CheckHeight(colliders,10,10,0);
                var before=colliders.ToDictionary(c=>c,c=>c.sharedMesh);
                terrain.BeginBatch();
                terrain.Subtract(stone,Matrix4x4.TRS(new Vector3(10,0,10),Quaternion.identity,Vector3.one));
                CheckHeight(colliders,10,10,0);
                terrain.EndBatch();
                CheckHeight(colliders,10,10,-0.5f);
                Require(renderers.Sum(r=>r.GetComponent<MeshFilter>().sharedMesh.triangles.Length)==
                    colliders.Sum(c=>c.sharedMesh.triangles.Length),"Render batches did not update after the pit cut");
                Require(renderers.Any(r=>r.GetComponent<MeshFilter>().sharedMesh.colors.Any(c=>c.r<0.6f)),
                    "Batched rendering lost the dark pit interior");
                CheckHeight(colliders,10.6f,10,0);
                Require(terrain.LastRebuiltSurfaceCount==1,"One surface rebuilt");
                Require(colliders.Count(c=>c.sharedMesh!=before[c])==1,"Other surface meshes unchanged");
                Mesh carved=colliders.Single(c=>c.sharedMesh!=before[c]).sharedMesh;
                Require(carved.colors.Any(c=>Mathf.Abs(c.r-0.55f)<0.001f)&&carved.colors.Any(c=>c==Color.white),
                    "Pit facets are darkened while original ground remains white");
                Require(carved.normals.All(n=>n.sqrMagnitude>0.99f),"Every clipped face has a valid normal");
                terrain.Subtract(stone,Matrix4x4.TRS(new Vector3(10.4f,-0.1f,10),Quaternion.identity,Vector3.one));
                CheckHeight(colliders,9.6f,10,-0.5f);
                CheckHeight(colliders,10.2f,10,-0.6f);
                terrain.Subtract(stone,Matrix4x4.TRS(new Vector3(0,0,0),Quaternion.identity,Vector3.one));
                Require(terrain.LastRebuiltSurfaceCount==1,"A cut rebuilds one whole surface");
                CheckHeight(colliders,-0.1f,0.1f,-0.5f);
                CheckHeight(colliders,0.1f,-0.1f,-0.5f);
                terrain.Subtract(stone,Matrix4x4.TRS(new Vector3(20,0,20),Quaternion.identity,Vector3.one*0.064f));
                CheckHeight(colliders,20,20,-0.032f);
                terrain.Subtract(stone,Matrix4x4.TRS(new Vector3(30,3,30),Quaternion.identity,Vector3.one));
                Require(terrain.LastRebuiltSurfaceCount==0,"Floating stone leaves terrain unchanged");
                CheckHeight(colliders,30,30,0);
                CheckHeight(colliders,90,90,0);
                terrain.Subtract(stone,Matrix4x4.TRS(new Vector3(40,0,40),Quaternion.Euler(0,45,0),Vector3.one));
                CheckHeight(colliders,40,40,-0.5f);
                CheckHeight(colliders,40.6f,40.6f,0);
                Require(terrain.Raycast(new Ray(new Vector3(40,10,40),Vector3.down),100,out var indexedHit)
                    &&Mathf.Abs(indexedHit.point.y+0.5f)<0.001f,"Indexed height query sees pit");
                Require(terrain.Raycast(new Ray(new Vector3(0,10,0),Vector3.down),100,out indexedHit)
                    &&Mathf.Abs(indexedHit.point.y+0.5f)<0.001f,"Indexed height query sees successive pit");
                Debug.Log("STONE_CUTOUT_VALIDATION_PASS: collider depth, locality, overlapping cuts, successive cuts, small stones, floating stones, rotated shape, indexed height queries.");
                Debug.Log($"RESIDENT_TERRAIN_PASS: {colliders.Count} whole surfaces rendered using {renderers.Length} renderers; pit geometry and colors updated.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(surface);
            }
        }
        private static void CheckHeight(List<MeshCollider> colliders,float x,float z,float expected)
        {
            var ray=new Ray(new Vector3(x,10,z),Vector3.down);
            float height=float.NegativeInfinity;
            foreach(var collider in colliders)
                if(collider.Raycast(ray,out var hit,100)) height=Mathf.Max(height,hit.point.y);
            Require(Mathf.Abs(height-expected)<0.001f,$"Height at {x},{z}: expected {expected}, got {height}");
        }
        private static void Require(bool condition,string message)
        { if(!condition) throw new Exception(message); }
    }
}
