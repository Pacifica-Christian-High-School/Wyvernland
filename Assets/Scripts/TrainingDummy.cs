using System;
using UnityEngine;

namespace Wyvernland
{
    public sealed class TrainingDummy : MonoBehaviour
    {
        [Serializable] public sealed class State { public PlayerDamage.State damage; }
        internal PlayerDamage Damage { get; private set; }
        FirstPersonController controller;
        Rigidbody body;
        public static TrainingDummy Create(Transform parent,ProceduralMap map,bool mobile=false)
        {
            // Retain the legacy internal name so existing rooms keep the same object ID.
            var node=new GameObject("Test Ground damage dummy (20,20)");node.transform.SetParent(parent,false);
            node.transform.position=new Vector3(-10,(map!=null?map.HeightAt(-10,-10):0)+.15f,-10);
            node.transform.rotation=Quaternion.Euler(0,180,0);
            var body=node.AddComponent<Rigidbody>();body.useGravity=false;body.mass=FirstPersonController.PlayerMassKg;
            body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            if(mobile) {
                var shape=new GameObject("NPC central capsule");shape.layer=2;shape.transform.SetParent(node.transform,false);
                var capsule=shape.AddComponent<CapsuleCollider>();capsule.radius=.18f;capsule.height=.8f;capsule.center=Vector3.up*.7f;
            }
            var view=new GameObject("Dummy view",typeof(Camera));view.transform.SetParent(node.transform,false);
            view.GetComponent<Camera>().enabled=false;
            var controller=node.AddComponent<FirstPersonController>();controller.Initialize(view.transform);controller.ConfigureStationaryActor();
            var avatar=node.AddComponent<RiggedPlayerAvatar>();avatar.Initialize(controller);
            foreach(var renderer in node.GetComponentsInChildren<Renderer>()) renderer.gameObject.layer=0;
            var dummy=node.AddComponent<TrainingDummy>();dummy.body=body;dummy.controller=controller;
            dummy.Damage=node.GetComponent<PlayerDamage>();dummy.Damage.SuppressPlayerOverlay=true;
            return dummy;
        }
        internal State Capture()
        {
            var damage=Damage.Capture();damage.impacts=null;damage.received=null;
            return new State { damage=damage };
        }
        internal bool Changed(State previous)=>JsonUtility.ToJson(Capture())!=JsonUtility.ToJson(previous);
        internal void SetAuthority(bool authority)
        {
            Damage.SetReplica(!authority);controller.enabled=authority;
            body.isKinematic=!authority||Damage.IsDead||Damage.IsShocked;
            if(GetComponent<CombatNpc>() is {} npc) { npc.enabled=authority;GetComponent<RiggedPlayerAvatar>().enabled=authority&&!Damage.IsDead; }
        }
        internal void Apply(State state)
        {
            if(state?.damage==null) return;
            SetAuthority(false);Damage.ApplyReplicaState(state.damage);
            SetAuthority(WebWorldSession.IsPhysicsHost);
        }
    }
}
