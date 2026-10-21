using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Wyvernland.Editor
{
    public static class PresentationAndFragmentValidation
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Check(bool value,string message) { if(!value) throw new InvalidOperationException("Presentation/fragment validation: "+message); }
        public static void Run()
        {
            var previous=SceneManager.GetActiveScene();if(string.IsNullOrEmpty(previous.path)) { EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");previous=SceneManager.GetActiveScene(); }
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
            var quality=ColorOnlyTextureQuality.Level;
            try {
                ColorOnlyTextureQuality.SetQuality(TextureQualityLevel.High);
                ValidateCenters();ValidatePlayerPresentation();TreeBranchDestructionValidation.RunRadiusAndMassCenter();
                Debug.Log("PRESENTATION_AND_FRAGMENT_VALIDATION_PASS: detached mesh centers with rotated/scaled roots, collider-helper exclusion, nested-body exclusion, repeated/replicated geometry changes, split trunk generations, Medium dark player shadows before/after hits and quality changes, no material flashing, and closing/reopening hit eyelids.");
            } finally { ColorOnlyTextureQuality.SetQuality(quality);SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true); }
        }
        static Mesh OffsetCube(Vector3 center)
        {
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);var mesh=UnityEngine.Object.Instantiate(cube.GetComponent<MeshFilter>().sharedMesh);UnityEngine.Object.DestroyImmediate(cube);
            mesh.vertices=mesh.vertices.Select(v=>v+center).ToArray();mesh.RecalculateBounds();return mesh;
        }
        internal static void ValidateCenters()
        {
            var node=new GameObject("Scaled detached stone");node.transform.SetPositionAndRotation(new Vector3(80,1000,50),Quaternion.Euler(20,35,40));node.transform.localScale=new Vector3(2,3,.7f);
            var mesh=OffsetCube(new Vector3(3,-2,5));node.AddComponent<MeshFilter>().sharedMesh=mesh;node.AddComponent<MeshRenderer>();
            var collider=node.AddComponent<BoxCollider>();collider.center=mesh.bounds.center;collider.size=mesh.bounds.size*2;
            var body=node.AddComponent<Rigidbody>();body.isKinematic=true;body.centerOfMass=Vector3.one*30;
            var helper=new GameObject("Extra collision helper");helper.transform.SetParent(node.transform,false);helper.transform.localPosition=Vector3.one*100;helper.AddComponent<SphereCollider>();
            node.AddComponent<DestructionFragment>();Physics.SyncTransforms();
            Check(Vector3.Distance(body.worldCenterOfMass,node.transform.TransformPoint(mesh.bounds.center))<.0001f,"fragment center uses its mesh, including body scale, rather than the original pivot or collision helper");
            var nested=GameObject.CreatePrimitive(PrimitiveType.Cube);nested.transform.SetParent(node.transform,false);nested.transform.localPosition=Vector3.one*200;nested.AddComponent<Rigidbody>().isKinematic=true;
            FragmentMassCenter.Align(body);
            Check(Vector3.Distance(body.worldCenterOfMass,node.transform.TransformPoint(mesh.bounds.center))<.0001f,"an independent child Rigidbody does not pull the parent's center");
            var properties=PhysicalDestructible.Ensure(node);properties.ApplyMaterial("Stone");
            var replacement=OffsetCube(new Vector3(-4,1,2));properties.ApplyNetworkMesh(replacement,Array.Empty<Material>());Physics.SyncTransforms();
            var actual=body.worldCenterOfMass;var expected=node.transform.TransformPoint(replacement.bounds.center);
            Check(Vector3.Distance(actual,expected)<.001f,"replicated replacement geometry updates a kinematic fragment center immediately");
            UnityEngine.Object.DestroyImmediate(node);UnityEngine.Object.DestroyImmediate(mesh);
        }
        static void ValidatePlayerPresentation()
        {
            var actor=new GameObject("Presentation player");actor.transform.position=new Vector3(40,1000,40);actor.AddComponent<Rigidbody>().isKinematic=true;
            var camera=new GameObject("View",typeof(Camera));camera.transform.SetParent(actor.transform,false);
            var player=actor.AddComponent<FirstPersonController>();player.Initialize(camera.transform);player.enabled=false;
            var avatar=actor.AddComponent<RiggedPlayerAvatar>();avatar.Initialize(player);avatar.enabled=false;
            var damage=actor.GetComponent<PlayerDamage>();
            ColorOnlyTextureQuality.SetQuality(TextureQualityLevel.Medium);
            var skin=actor.GetComponentsInChildren<SkinnedMeshRenderer>().First(s=>s.name=="HumanBody");
            Color Shadow(Renderer r) { var b=new MaterialPropertyBlock();r.GetPropertyBlock(b);return b.GetColor("_ShadowColor"); }
            var shade=Shadow(skin);Check(shade.maxColorComponent>0&&shade.maxColorComponent<.22f,"Medium player shadow is dark rather than the shared material's pale fallback");
            var source=GameObject.CreatePrimitive(PrimitiveType.Cube);source.transform.position=actor.transform.position+Vector3.right*3;source.AddComponent<Rigidbody>().isKinematic=true;PhysicalDestructible.Ensure(source);
            Check(damage.ReceiveImpact(PlayerBodyPart.RightArm,source.GetComponent<Collider>(),actor.transform.position+Vector3.up,Vector3.right,Vector3.right*100),"real damage starts the eyelid feedback");
            typeof(PlayerDamage).GetMethod("LateUpdate",Hidden).Invoke(damage,null);
            Check(Shadow(skin)==shade,"a hit does not overwrite Medium's dark shadow color");
            float started=(float)typeof(PlayerDamage).GetField("hitBlinkStartedAt",Hidden).GetValue(damage);
            Check(float.IsFinite(started)&&PlayerDamage.HitEyelidClosure(.035f)>.4f&&PlayerDamage.HitEyelidClosure(.07f)==1f&&
                PlayerDamage.HitEyelidClosure(.205f)>.4f&&PlayerDamage.HitEyelidClosure(.3f)==0f,"a hit closes both eyelids and then fully reopens them within 0.3 seconds");
            var online=new GameObject("Presentation room").AddComponent<WebPlayerPresence>();online.Initialize(player);
            void Snapshot(int hit)=>online.ReceivePlayers(JsonUtility.ToJson(new WebPlayerPresence.Snapshot { players=new[]{new WebPlayerPresence.PlayerState {
                id="shade-peer",x=50,y=1000,z=40,grounded=true,damage=new PlayerDamage.State { life=1,hit=hit } } } }));
            Snapshot(0);var remote=online.GetComponentInChildren<RemotePlayerDamage>();var remoteSkin=remote.GetComponentsInChildren<SkinnedMeshRenderer>().First(s=>s.name=="HumanBody");
            var remoteShade=Shadow(remoteSkin);Snapshot(1);remote.PoseAfterAnimation();
            Check(remoteShade.maxColorComponent>0&&remoteShade.maxColorComponent<.22f&&Shadow(remoteSkin)==remoteShade,"room hit updates never flash the remote player or discard its dark shadow color");
            foreach(var level in new[]{TextureQualityLevel.High,TextureQualityLevel.Medium,TextureQualityLevel.Low,TextureQualityLevel.Medium}) {
                ColorOnlyTextureQuality.SetQuality(level);
                if(level==TextureQualityLevel.Medium) Check(Shadow(skin)==shade&&Shadow(remoteSkin)==remoteShade,"quality switches restore the same dark player shadow");
            }
            damage.Respawn();
            Check(float.IsNegativeInfinity((float)typeof(PlayerDamage).GetField("hitBlinkStartedAt",Hidden).GetValue(damage)),"respawn clears the hit blink instead of flashing the player again");
            PreviewEyelids();
            UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(online.gameObject);UnityEngine.Object.DestroyImmediate(actor);
        }
        static void PreviewEyelids()
        {
            var upper=PlayerDamage.CreateEyelidTexture(true);var lower=PlayerDamage.CreateEyelidTexture(false);
            try {
                const int width=640,height=360;string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"..","..","eyelid-preview"));Directory.CreateDirectory(folder);
                foreach(float closure in new[]{0f,.5f,1f}) {
                    var output=new Texture2D(width,height,TextureFormat.RGBA32,false);var pixels=new Color[width*height];float lidHeight=height*.68f*closure;
                    for(int y=0;y<height;y++) for(int x=0;x<width;x++) {
                        float fromTop=height-1-y,u=(float)x/(width-1);Color color=y>height*.45f?new Color(.31f,.48f,.62f):new Color(.29f,.34f,.26f);
                        if(lidHeight>0&&fromTop<lidHeight) color=Color.Lerp(color,Color.black,upper.GetPixelBilinear(u,1f-fromTop/lidHeight).a);
                        if(lidHeight>0&&fromTop>=height-lidHeight) color=Color.Lerp(color,Color.black,lower.GetPixelBilinear(u,1f-(fromTop-height+lidHeight)/lidHeight).a);
                        pixels[y*width+x]=color;
                    }
                    output.SetPixels(pixels);output.Apply();
                    if(closure==1f) Check(pixels.All(p=>p.maxColorComponent<.01f),"closed eyelids fully cover the view without a center gap");
                    File.WriteAllBytes(Path.Combine(folder,"blink-"+(int)(closure*100)+".png"),output.EncodeToPNG());UnityEngine.Object.DestroyImmediate(output);
                }
            } finally { UnityEngine.Object.DestroyImmediate(upper);UnityEngine.Object.DestroyImmediate(lower); }
        }
    }
}



