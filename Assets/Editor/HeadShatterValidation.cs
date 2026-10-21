using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Wyvernland.Editor
{
    public static class HeadShatterValidation
    {
        static void Check(bool value,string message) { if(!value) throw new InvalidOperationException("Head shatter: "+message); }
        [MenuItem("Tools/Wyvernland/Validate Head Shatter")]
        public static void Run()
        {
            var previous=SceneManager.GetActiveScene();
            if(string.IsNullOrEmpty(previous.path)) { EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");previous=SceneManager.GetActiveScene(); }
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
            try {
                var actor=new GameObject("Head shatter fixture");actor.transform.position=new Vector3(4300,1000,0);
                actor.AddComponent<Rigidbody>().useGravity=false;
                var view=new GameObject("View",typeof(Camera)).transform;view.SetParent(actor.transform,false);
                var player=actor.AddComponent<FirstPersonController>();player.Initialize(view);
                var avatar=actor.AddComponent<RiggedPlayerAvatar>();avatar.Initialize(player);avatar.enabled=false;
                var original=actor.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r=>r.name=="HumanHead").sharedMesh;
                var source=GameObject.CreatePrimitive(PrimitiveType.Cube);source.transform.position=actor.transform.position+Vector3.right*5;
                source.AddComponent<Rigidbody>().useGravity=false;PhysicalDestructible.Ensure(source).ApplyMaterial("Stone");
                var damage=actor.GetComponent<PlayerDamage>();
                int fragments=UnityEngine.Object.FindObjectsByType<BodyFragment>(FindObjectsSortMode.None).Length;
                var spot=actor.transform.position+Vector3.up*1.7f;
                Check(damage.ReceiveImpact(PlayerBodyPart.Head,source.GetComponent<Collider>(),spot,Vector3.right,Vector3.right*100)&&damage.IsDead,
                    "one qualifying head impact kills");
                var corpse=UnityEngine.Object.FindObjectsByType<PlayerCorpse>(FindObjectsSortMode.None).Single(c=>c.gameObject.scene==scene);
                var head=corpse.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r=>r.name=="HumanHead");
                Check(!head.enabled,"the entire head is removed from the corpse");
                Check(UnityEngine.Object.FindObjectsByType<BodyFragment>(FindObjectsSortMode.None).Length>fragments,"the complete head produces fragments");
                Check(corpse.GetComponentsInChildren<PlayerHitShape>().Where(t=>t.Part==PlayerBodyPart.Head).All(t=>!t.GetComponent<Collider>().enabled),"shattered head has no retained collider");
                var state=corpse.Capture(false);state.id="Head shatter replica";state.position+=Vector3.right*5;
                fragments=UnityEngine.Object.FindObjectsByType<BodyFragment>(FindObjectsSortMode.None).Length;
                var remote=PlayerCorpse.Create(state,null);
                var remoteHead=remote.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r=>r.name=="HumanHead");
                Check(!remoteHead.enabled,"remote/saved corpse reconstructs the fully shattered head");
                Check(UnityEngine.Object.FindObjectsByType<BodyFragment>(FindObjectsSortMode.None).Length==fragments,"reconstruction does not duplicate fragments");
                remote.GetComponent<PlayerRagdoll>().SetMissingLimbs(1);
                Check(remote.GetComponentsInChildren<PlayerHitShape>().Where(t=>t.Part==PlayerBodyPart.Head).All(t=>!t.GetComponent<Collider>().enabled),"updating missing limbs does not recreate head collision");
                damage.Respawn();
                Check(actor.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r=>r.name=="HumanHead").sharedMesh==original,"respawn restores the living player's intact head");
                Debug.Log("HEAD_SHATTER_VALIDATION_PASS: one qualifying hit kills; complete head fragments, no retained head collision, deterministic remote/saved reconstruction without duplicate fragments, and intact respawn.");
            } finally { SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true); }
        }
    }
}
