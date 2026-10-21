using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Wyvernland.Editor
{
    public static class FragmentMaterialPerformanceValidation
    {
        static void Check(bool value,string message)
        { if(!value) throw new InvalidOperationException("Fragment fixes: "+message); }
        public static void Run()
        {
            var previous=SceneManager.GetActiveScene();
            if(string.IsNullOrEmpty(previous.path)) { EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");previous=SceneManager.GetActiveScene(); }
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var oldQuality=ColorOnlyTextureQuality.Level;
            try { Materials();Colors();Retirement(); }
            finally { ColorOnlyTextureQuality.SetQuality(oldQuality);SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true); }
            Debug.Log("FRAGMENT_MATERIAL_PERFORMANCE_PASS: four real spawn generations retain materials, missing-material fallback, quality cache cleanup, stone tint across batching and all quality modes, 256 settled objects leave physics/network tracking, stone hardness 2–5 and resilience 2.");
        }
        static void Materials()
        {
            ColorOnlyTextureQuality.SetQuality(TextureQualityLevel.High);
            var original=new Material(Resources.Load<Shader>("Clay"));original.SetColor("_BaseColor",new Color(.1f,.24f,.28f));
            var parent=GameObject.CreatePrimitive(PrimitiveType.Cube);parent.AddComponent<BodyFragment>();
            var material=BodyFragment.OwnAppearance(parent,new[]{original},1)[0];
            parent.GetComponent<Renderer>().sharedMaterial=material;
            UnityEngine.Object.DestroyImmediate(original);
            for(int generation=0;generation<4;generation++)
            {
                var physical=PhysicalDestructible.Ensure(parent);physical.ApplyMaterial("Flesh");
                var prepare=typeof(PhysicalDestructible).GetMethod("PrepareSpawn",BindingFlags.Instance|BindingFlags.NonPublic);
                var request=prepare.Invoke(physical,new object[]{UnityEngine.Object.Instantiate(parent.GetComponent<MeshFilter>().sharedMesh),Vector3.one*3000,
                    new[]{material},new MaterialPropertyBlock[]{new()},null,1f,Vector3.one*3000,Vector3.up,0f,0f});
                var child=(PhysicalDestructible)typeof(PhysicalDestructible).GetMethod("SpawnPrepared",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new[]{request});
                ColorOnlyTextureQuality.SetQuality(TextureQualityLevel.Medium);ColorOnlyTextureQuality.ApplyRendererQuality(parent.GetComponent<Renderer>());
                int tracked=ColorOnlyTextureQuality.TrackedRendererCount;
                DestroyMaterialFixture(parent);
                Check(ColorOnlyTextureQuality.TrackedRendererCount<tracked,"destroyed renderer is removed from quality cache");
                ColorOnlyTextureQuality.SetQuality(TextureQualityLevel.High);
                parent=child.gameObject;
                Check(material!=null&&parent.GetComponent<Renderer>().sharedMaterial==material&&material.shader.name=="Wyvernland/Unfired Clay",
                    "child survives parent destruction and restores the retained material");
            }
            DestroyMaterialFixture(parent);Check(material==null,"last owner releases material");
            var fallback=new GameObject("Missing material fixture");var recovered=BodyFragment.OwnAppearance(fallback,null,2);
            Check(recovered.All(m=>m!=null&&m.shader!=null&&m.shader.name!="Hidden/InternalErrorShader"),"fallback never renders magenta");
            UnityEngine.Object.DestroyImmediate(fallback);
        }
        // Edit-mode objects never enter the runtime lifecycle. Exercise the
        // release callbacks explicitly here; RunPlay checks real deferred cleanup.
        static void DestroyMaterialFixture(GameObject value)
        {
            foreach(var owner in value.GetComponents<RuntimeMaterialOwner>()) owner.SendMessage("OnDestroy");
            foreach(var lifetime in value.GetComponents<TextureQualityRendererLifetime>()) lifetime.SendMessage("OnDestroy");
            UnityEngine.Object.DestroyImmediate(value);
        }
        static void Colors()
        {
            ColorOnlyTextureQuality.SetQuality(TextureQualityLevel.High);
            var root=new GameObject("Pile tint fixture");var material=new Material(Resources.Load<Shader>("Clay"));
            material.SetColor("_BaseColor",Color.white);material.SetFloat("_UseVertexColor",1);
            Color authored=new(.22f,.17f,.11f);Color vertex=QualitySettings.activeColorSpace==ColorSpace.Linear?authored.linear:authored;
            for(int i=0;i<2;i++) {
                var stone=GameObject.CreatePrimitive(PrimitiveType.Cube);stone.transform.SetParent(root.transform,false);
                var mesh=UnityEngine.Object.Instantiate(stone.GetComponent<MeshFilter>().sharedMesh);
                mesh.colors=Enumerable.Repeat(vertex,mesh.vertexCount).ToArray();stone.GetComponent<MeshFilter>().sharedMesh=mesh;
                stone.AddComponent<RuntimeMeshOwner>().Initialize(mesh);stone.GetComponent<Renderer>().sharedMaterial=material;
                PhysicalDestructible.Ensure(stone).ApplyMaterial("Stone");
                var physical=stone.GetComponent<PhysicalDestructible>();
                Check(physical.Hardness>=2&&physical.Hardness<=5&&physical.Resilience==2,"reduced stone properties");
                Color represented=ColorOnlyTextureQuality.RepresentativeColor(stone.GetComponent<Renderer>(),new[]{material});
                Check(Vector3.Distance(new(represented.r,represented.g,represented.b),new(authored.r,authored.g,authored.b))<.005f,"vertex tint converts to material color correctly");
            }
            var cache=root.AddComponent<ResidentPropRendering>();cache.Initialize();Check(cache.CachedSourceCount==2,"pile meshes enter resident batching");
            var visual=root.GetComponentInChildren<ResidentRenderVisual>();
            foreach(var quality in new[]{TextureQualityLevel.Low,TextureQualityLevel.Medium,TextureQualityLevel.High}) {
                ColorOnlyTextureQuality.SetQuality(quality);
                var actual=visual.GetComponent<MeshFilter>().sharedMesh.colors[0];
                Check(Vector3.Distance(new(actual.r,actual.g,actual.b),new(vertex.r,vertex.g,vertex.b))<.005f,"resident tint stays dark in "+quality);
            }
            UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(material);
        }
        static void Retirement()
        {
            var root=new GameObject("Settled fragment stress");var session=root.AddComponent<WebWorldSession>();
            var init=session.Initialize(root,null);while(init.MoveNext()){}
            session.ReceiveWorld("{\"self\":\"host\",\"host\":\"host\",\"ready\":true}");
            for(int i=0;i<256;i++) {
                var piece=GameObject.CreatePrimitive(PrimitiveType.Cube);piece.transform.SetParent(root.transform,false);
                piece.transform.position=new Vector3(i*2,3000,0);var body=piece.AddComponent<Rigidbody>();body.useGravity=false;
                var motion=MovingImpactMotion.Ensure(body);var pickup=piece.AddComponent<PickableObject>();
                WebWorldSession.RegisterSpawn(piece);var rest=SmallFragmentRest.Ensure(piece);rest.MakeStatic(true);
                rest.MakeStatic(true);WebWorldSession.Dirty(piece);
                pickup.TrackMovement();
                var pickupMotion=(System.Collections.Generic.HashSet<PickableObject>)typeof(PickupSpatialIndex)
                    .GetField("moving",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
                Check(!pickupMotion.Contains(pickup),"late network movement cannot re-enroll a disabled settled pickup");
                Check(piece.GetComponent<Collider>()==null&&piece.GetComponent<Rigidbody>()==null&&!motion.enabled&&!pickup.enabled&&!rest.enabled,
                    "settled fragment retains geometry with no active physics helpers");
            }
            Check(session.DynamicObjectCount==0&&session.MotionObjectCount==0,"settled fragments do not remain in network motion scans");
            Check(root.GetComponentsInChildren<MeshRenderer>().Length==256,"all settled fragments remain visible");
            UnityEngine.Object.DestroyImmediate(root);
        }

        const string PlayKey="Wyvernland.FragmentFixesPlay";
        static GameObject playRoot,ancestor;
        static Material retained;
        static int playFrame,phase,trackedBefore;
        static double deadline;
        public static void RunAll() { Run();SmallFragmentRestValidation.RunRegression();RunPlay(); }
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
                if(EditorApplication.timeSinceStartup>deadline) throw new InvalidOperationException("Fragment Play validation timed out");
                if(!Application.isPlaying||Time.deltaTime<=0) return;
                if(phase==2) {
                    if(Time.frameCount<=playFrame+2) return;
                    Check(retained==null,"last descendant releases shared material after deferred destruction");
                    Check(ColorOnlyTextureQuality.TrackedRendererCount==trackedBefore,"destroyed runtime renderers leave no quality-cache entries");
                    Debug.Log("FRAGMENT_FIXES_PLAY_PASS: 256 active sweeps share one physics sync, deferred physics removal retains every visual/material, no settled network motion work, final material/cache cleanup.");FinishPlay(0);return;
                }
                if(playRoot==null) {
                    Time.timeScale=1;playRoot=new GameObject("Deferred fragment stress");
                    var session=playRoot.AddComponent<WebWorldSession>();var init=session.Initialize(playRoot,null);while(init.MoveNext()){}
                    session.ReceiveWorld("{\"self\":\"host\",\"host\":\"host\",\"ready\":true}");
                    var source=new Material(Resources.Load<Shader>("Clay"));ancestor=new GameObject("Ancestor");
                    retained=BodyFragment.OwnAppearance(ancestor,new[]{source},1)[0];UnityEngine.Object.Destroy(source);
                    trackedBefore=ColorOnlyTextureQuality.TrackedRendererCount;
                    int syncBefore=MovingImpactMotion.FixedSyncCount;
                    var sample=typeof(MovingImpactMotion).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
                    for(int i=0;i<256;i++) {
                        var piece=GameObject.CreatePrimitive(PrimitiveType.Cube);piece.transform.SetParent(playRoot.transform,false);
                        piece.transform.position=new Vector3(i*3,3000,0);piece.transform.localScale=Vector3.one*.05f;
                        piece.GetComponent<Renderer>().sharedMaterials=BodyFragment.OwnAppearance(piece,new[]{retained},1);
                        PhysicalDestructible.Ensure(piece).ApplyMaterial("Flesh");
                        var body=piece.AddComponent<Rigidbody>();body.useGravity=false;body.linearVelocity=Vector3.forward*3;
                        var motion=MovingImpactMotion.Ensure(body);sample.Invoke(motion,null);
                        WebWorldSession.RegisterSpawn(piece);
                        body.linearVelocity=body.angularVelocity=Vector3.zero;body.Sleep();
                        SmallFragmentRest.Ensure(piece).CheckRest(.2f);
                    }
                    Check(MovingImpactMotion.FixedSyncCount-syncBefore<=1,"256 moving fragments synchronize transforms at most once per physics tick");
                    ColorOnlyTextureQuality.SetQuality(TextureQualityLevel.Medium);
                    UnityEngine.Object.Destroy(ancestor);playFrame=Time.frameCount;phase=1;return;
                }
                if(Time.frameCount<=playFrame+1) return;
                Check(retained!=null&&retained.shader.name=="Wyvernland/Unfired Clay","deferred ancestor destruction keeps shared descendant material");
                Check(playRoot.GetComponentsInChildren<Rigidbody>().Length==0&&playRoot.GetComponentsInChildren<Collider>().Length==0,"all deferred fragment physics removed");
                Check(playRoot.GetComponentsInChildren<MeshRenderer>().Length==256,"all fragment visuals retained");
                var room=playRoot.GetComponent<WebWorldSession>();
                foreach(var rest in playRoot.GetComponentsInChildren<SmallFragmentRest>()) WebWorldSession.Dirty(rest.gameObject);
                Check(room.DynamicObjectCount==0&&room.MotionObjectCount==0,"dirty publication cannot re-enroll retired physics");
                UnityEngine.Object.Destroy(playRoot);phase=2;playFrame=Time.frameCount;
            } catch(Exception exception) { Debug.LogException(exception);FinishPlay(1); }
        }
        static void FinishPlay(int code)
        { SessionState.SetBool(PlayKey,false);EditorApplication.update-=PlayTick;EditorApplication.Exit(code); }
    }
}
