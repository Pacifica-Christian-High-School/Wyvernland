using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Wyvernland.Editor
{
    public static class ResidentWorldValidation
    {
        public static void RunPropColorValidation()
        {
            ValidatePropDrawCache();
            ValidateVertexRockColors();
            Debug.Log("RESIDENT_PROP_COLOR_VALIDATION_PASS");
        }

        private static void ValidateVertexRockColors()
        {
            var root=new GameObject("Vertex rock color validation");
            var material=new Material(Resources.Load<Shader>("Clay"));
            var previous=ColorOnlyTextureQuality.Level;
            Mesh authored=null;
            try
            {
                material.SetColor("_BaseColor",Color.white);material.SetFloat("_UseVertexColor",1f);
                for(int i=0;i<2;i++)
                {
                    var rock=GameObject.CreatePrimitive(PrimitiveType.Cube);
                    rock.transform.SetParent(root.transform);rock.transform.position=new Vector3(i*2,0,0);
                    authored??=UnityEngine.Object.Instantiate(rock.GetComponent<MeshFilter>().sharedMesh);
                    var colors=Enumerable.Repeat(new Color(.45f,.28f,.16f),authored.vertexCount).ToArray();
                    authored.colors=colors;rock.GetComponent<MeshFilter>().sharedMesh=authored;
                    rock.GetComponent<MeshRenderer>().sharedMaterial=material;PhysicalDestructible.Ensure(rock);
                }
                ColorOnlyTextureQuality.SetQuality(TextureQualityLevel.High);
                var draw=root.AddComponent<ResidentPropRendering>();draw.Initialize();
                var visual=root.GetComponentInChildren<ResidentRenderVisual>();
                var mesh=visual.GetComponent<MeshFilter>().sharedMesh;
                Color high=mesh.colors[0],expected=new Color(.45f,.28f,.16f);
                if(QualitySettings.activeColorSpace==ColorSpace.Linear) expected=expected.linear;
                foreach(var quality in new[]{TextureQualityLevel.Low,TextureQualityLevel.Medium})
                {
                    ColorOnlyTextureQuality.SetQuality(quality);
                    Require(Vector4.Distance(mesh.colors[0],expected)<.001f,
                        "Cached rocks match the individual low/medium tint instead of appearing pale");
                }
                ColorOnlyTextureQuality.SetQuality(TextureQualityLevel.High);
                Require(Vector4.Distance(mesh.colors[0],high)<.001f,
                    "Returning to High restores detailed authored vertex colors");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                if(authored!=null) UnityEngine.Object.DestroyImmediate(authored);
                UnityEngine.Object.DestroyImmediate(material);
                ColorOnlyTextureQuality.SetQuality(previous);
            }
        }

        [MenuItem("Tools/Wyvernland/Validate Resident World")]
        public static void Run()
        {
            StoneTerrainCutoutValidation.Run();
            StonePitLoadingValidation.Run();
            TreeBranchDestructionValidation.Run();
            GoldenStoneLaunchValidation.Run();
            ValidatePickupMovement();
            ValidateGrassSurface();
            ValidatePropDrawCache();
            ValidateTerrainDrawing();
            Debug.Log("RESIDENT_WORLD_VALIDATION_PASS");
        }

        private static void ValidateTerrainDrawing()
        {
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh draw=null;
            try
            {
                Mesh source=cube.GetComponent<MeshFilter>().sharedMesh;
                var type=typeof(ProceduralMap).Assembly.GetType("Wyvernland.TerrainDrawMesh");
                draw=(Mesh)type.GetMethod("Build").Invoke(null,new object[]{source});
                Require(draw.vertexCount<source.vertexCount,"Terrain draw vertices should be shared");
                int[] originalIndices=source.triangles,indexed=draw.triangles;
                Vector3[] original=source.vertices,vertices=draw.vertices;
                Require(indexed.Length==originalIndices.Length,"Terrain faces must remain unchanged");
                for(int i=0;i<indexed.Length;i++)
                    Require(vertices[indexed[i]]==original[originalIndices[i]],
                        "Indexed terrain must preserve each triangle corner and winding");
            }
            finally
            {
                if(draw!=null) UnityEngine.Object.DestroyImmediate(draw);
                UnityEngine.Object.DestroyImmediate(cube);
            }
        }

        private static void ValidatePropDrawCache()
        {
            var root=new GameObject("Resident prop draw validation");
            var hand=new GameObject("Draw cache pickup hand");
            var objects=new GameObject[3];
            var originals=new Mesh[3];
            var materials=new Material[3];
            var spawned=new System.Collections.Generic.List<PhysicalDestructible>();
            try
            {
                for(int i=0;i<objects.Length;i++)
                {
                    var sample=objects[i]=GameObject.CreatePrimitive(PrimitiveType.Cube);
                    sample.transform.SetParent(root.transform);
                    sample.transform.position=new Vector3(2+i,1,2);
                    sample.transform.localScale=Vector3.one*(i==0?.4f:.1f);
                    materials[i]=new Material(Resources.Load<Shader>("Clay"));
                    materials[i].SetColor("_BaseColor",Color.Lerp(Color.red,Color.blue,i*.5f));
                    sample.GetComponent<MeshRenderer>().sharedMaterial=materials[i];
                    originals[i]=sample.GetComponent<MeshFilter>().sharedMesh;
                    PhysicalDestructible.Ensure(sample);
                }
                var draw=root.AddComponent<ResidentPropRendering>();
                draw.Initialize();
                Require(draw.CachedSourceCount==3&&draw.BatchCount==1,
                    "Different prop colors should share one stationary draw batch");
                var visual=root.GetComponentInChildren<ResidentRenderVisual>();
                Mesh combined=visual.GetComponent<MeshFilter>().sharedMesh;
                Bounds geometryBounds=GeometryUtility.CalculateBounds(combined.vertices,
                    visual.transform.localToWorldMatrix);
                Vector3 centre=geometryBounds.center;
                Require(Vector3.Distance(centre,new Vector3(2.925f,1,2))<.01f,
                    "Combined geometry must preserve source world positions before the first rendered frame");
                int indices=(int)combined.GetIndexCount(0);
                Require(indices==(int)originals[0].GetIndexCount(0)*3,
                    "Draw cache must contain each source once");
                foreach(var sample in objects)
                    Require(sample.activeInHierarchy&&sample.GetComponent<Collider>().enabled&&
                        !sample.GetComponent<MeshRenderer>().enabled,
                        "Batched gameplay objects and colliders must stay active");
                ColorOnlyTextureQuality.SetQuality(TextureQualityLevel.Low);
                Require(visual.GetComponent<MeshRenderer>().sharedMaterial.shader.name==
                    "Wyvernland/Unfired Clay","Low quality must preserve cached vertex colors");
                ColorOnlyTextureQuality.SetQuality(TextureQualityLevel.Medium);
                ColorOnlyTextureQuality.SetQuality(TextureQualityLevel.High);
                var target=objects[0].GetComponent<PhysicalDestructible>();
                target.Fragmented+=(_,fragments)=>spawned.AddRange(fragments);
                Require(target.ApplyImpact(12,12,3,objects[0].transform.position,Vector3.up,20),
                    "Cached geometry must support real destruction snapshots");
                Require(draw.CachedSourceCount==2&&
                    (objects[0]==null||objects[0].GetComponent<MeshRenderer>().enabled)&&
                    combined.GetIndexCount(0)==indices*2/3,
                    "Geometry change must remove only its source from the draw cache");
                var pickup=objects[1].AddComponent<PickableObject>();
                Require(pickup.PickUp(hand.transform),"Cached prop pickup fixture must be holdable");
                Require(draw.CachedSourceCount==1&&objects[1].GetComponent<MeshRenderer>().enabled&&
                    !objects[1].GetComponent<MeshRenderer>().forceRenderingOff&&
                    combined.GetIndexCount(0)==indices/3,
                    "Picked-up prop must draw at the hand without leaving an unpickable cached copy at its spawn");
                DynamicObjectSurfaceGuard.Ensure(objects[2].AddComponent<Rigidbody>());
                Require(draw.CachedSourceCount==0&&objects[2].GetComponent<MeshRenderer>().enabled&&
                    combined.GetIndexCount(0)==0,
                    "Falling props must leave the draw cache without duplicate geometry");
                for(int i=1;i<objects.Length;i++)
                    Require(objects[i].GetComponent<MeshFilter>().sharedMesh==originals[i],
                        "Draw batching must preserve the original gameplay mesh");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(hand);
                foreach(var sample in objects) if(sample!=null) UnityEngine.Object.DestroyImmediate(sample);
                foreach(var material in materials) if(material!=null) UnityEngine.Object.DestroyImmediate(material);
                foreach(var fragment in spawned) if(fragment!=null) UnityEngine.Object.DestroyImmediate(fragment.gameObject);
            }
        }

        private static void ValidatePickupMovement()
        {
            var sample=GameObject.CreatePrimitive(PrimitiveType.Cube);
            var hand=new GameObject("Resident pickup test hand");
            var index=typeof(PickableObject).Assembly.GetType("Wyvernland.PickupSpatialIndex");
            var collect=index.GetMethod("Collect",BindingFlags.Static|BindingFlags.Public);
            var nearby=new System.Collections.Generic.List<PickableObject>();
            PickableObject pickup=null;
            try
            {
                sample.transform.position=new Vector3(7000,0,7000);
                sample.transform.localScale=Vector3.one*0.1f;
                pickup=sample.AddComponent<PickableObject>();
                // Edit-mode fixtures invoke the runtime registration lifecycle.
                typeof(PickableObject).GetMethod("OnEnable",BindingFlags.Instance|BindingFlags.NonPublic)
                    .Invoke(pickup,null);
                collect.Invoke(null,new object[]{sample.transform.position,2f,nearby});
                Require(nearby.Contains(pickup),"Static pickup missing from spatial lookup");
                Require(pickup.PickUp(hand.transform),"Movement validation sample must be pickable");
                hand.transform.position=new Vector3(7200,1,7200);
                typeof(PickableObject).GetMethod("UpdateHeldPose",BindingFlags.Instance|BindingFlags.NonPublic)
                    .Invoke(pickup,null);
                nearby.Clear();
                collect.Invoke(null,new object[]{sample.transform.position,2f,nearby});
                Require(nearby.Contains(pickup),"Held pickup missing after crossing lookup cells");
                pickup.Release(Vector3.zero,hand.transform);
                sample.GetComponent<Rigidbody>().position=new Vector3(7400,1,7400);
                Physics.SyncTransforms();
                nearby.Clear();
                collect.Invoke(null,new object[]{sample.transform.position,2f,nearby});
                Require(nearby.Contains(pickup),"Dropped pickup missing after moving");
                sample.SetActive(false);
                nearby.Clear();
                collect.Invoke(null,new object[]{sample.transform.position,2f,nearby});
                Require(!nearby.Contains(pickup),"Disabled pickup remained in spatial lookup");
            }
            finally
            {
                if(pickup!=null)
                    typeof(PickableObject).GetMethod("OnDisable",BindingFlags.Instance|BindingFlags.NonPublic)
                        .Invoke(pickup,null);
                UnityEngine.Object.DestroyImmediate(sample);
                UnityEngine.Object.DestroyImmediate(hand);
            }
        }
        private static void ValidateGrassSurface()
        {
            bool previous=WorldBootstrap.TestGround;
            var root=new GameObject("Resident grass surface validation");
            Texture2D readback=null;
            RenderTexture previousTarget=RenderTexture.active;
            try
            {
                typeof(WorldBootstrap).GetProperty("TestGround").SetValue(null,true);
                var map=root.AddComponent<ProceduralMap>();
                map.Build();
                var playerObject=new GameObject("Grass validation player");
                playerObject.transform.SetParent(root.transform);
                var player=playerObject.AddComponent<FirstPersonController>();
                var cameraObject=new GameObject("Grass validation camera");
                cameraObject.transform.SetParent(playerObject.transform);
                cameraObject.AddComponent<Camera>();
                player.Initialize(cameraObject.transform);
                var grass=root.AddComponent<ProceduralGrass>();
                grass.Initialize(player,map);
                var texture=(RenderTexture)typeof(ProceduralGrass).GetField("surfaceMap",
                    BindingFlags.Instance|BindingFlags.NonPublic).GetValue(grass);
                RenderTexture.active=texture;
                readback=new Texture2D(texture.width,texture.height,TextureFormat.RGBAFloat,false,true);
                readback.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);
                readback.Apply();
                Color centre=readback.GetPixel(texture.width/2,texture.height/2);
                Require(Mathf.Abs(centre.r)<0.001f&&centre.b>0.99f,
                    "Grass surface atlas must contain flat ground height and upward normal");
                Require(grass.ResidentPatchCount==64,"All test-ground grass patches remain resident");
            }
            finally
            {
                RenderTexture.active=previousTarget;
                if(readback!=null) UnityEngine.Object.DestroyImmediate(readback);
                UnityEngine.Object.DestroyImmediate(root);
                typeof(WorldBootstrap).GetProperty("TestGround").SetValue(null,previous);
            }
        }
        private static void Require(bool value,string message)
        { if(!value) throw new InvalidOperationException(message); }
    }
}
