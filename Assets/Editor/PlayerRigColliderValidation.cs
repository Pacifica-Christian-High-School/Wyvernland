using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Wyvernland.Editor
{
    public static class PlayerRigColliderValidation
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        [MenuItem("Tools/Wyvernland/Validate Animated Player Colliders")]
        public static void Run()
        {
            var fixture=new GameObject("Player rig collider validation");
            try
            {
                var actor=new GameObject("Local player");actor.transform.SetParent(fixture.transform);
                actor.transform.position=new Vector3(2000f,1000f,2000f);
                var body=actor.AddComponent<Rigidbody>();body.useGravity=false;
                var view=new GameObject("View",typeof(Camera));view.transform.SetParent(actor.transform,false);
                var player=actor.AddComponent<FirstPersonController>();player.Initialize(view.transform);
                var avatar=actor.AddComponent<RiggedPlayerAvatar>();avatar.Initialize(player);
                var rig=actor.GetComponent<PlayerRigColliders>();
                Check(rig!=null&&rig.ShapeCount>=12,"Torso, head, arms, legs and articulated fingers have collision shapes");
                var model=actor.transform.Find("Human (Rigged Player)");
                int count=rig.ShapeCount,meshCount=Resources.FindObjectsOfTypeAll<Mesh>().Length;
                rig.Initialize(model,2,false);
                Check(rig.ShapeCount==count&&Resources.FindObjectsOfTypeAll<Mesh>().Length==meshCount,
                    "Refitting replaces shapes and releases its temporary baked mesh");
                var colliders=model.GetComponentsInChildren<Collider>().Where(c=>c.name.StartsWith("Rig Collider - ")).ToArray();
                Check(colliders.Length==count&&colliders.All(c=>c.enabled&&!c.isTrigger&&c.attachedRigidbody==body&&c.gameObject.layer==2),
                    "Body shapes are solid, share the movement Rigidbody, and are excluded from self ground probes");
                Check(actor.GetComponentsInChildren<BoxCollider>().Count(c=>c.name.EndsWith("Foot BoxCollider"))==2,
                    "Existing animated foot support is retained");
                foreach(string name in new[]{"RightElbow","LeftKnee","RightWrist","RightFingers1","Waist","Neck"})
                {
                    var bone=Bone(model,name);var shape=rig.ShapeFor(bone);
                    Check(shape!=null,"The visible skin has a fitted collider for "+name);
                    Vector3 before=shape.bounds.center;
                    Quaternion rotation=shape.transform.rotation;
                    bone.localRotation=Quaternion.Euler(55f,35f,75f)*bone.localRotation;
                    Physics.SyncTransforms();
                    Check(Quaternion.Angle(rotation,shape.transform.rotation)>20f,
                        name+" collider rotates with its deform bone without a frame update");
                    HitsCurrentPose(shape,name);
                    Check(Vector3.Distance(before,shape.bounds.center)>0.002f||name=="Waist",
                        name+" collider follows the moving mesh section");
                }
                var elbow=Bone(model,"RightElbow");var elbowShape=rig.ShapeFor(elbow);
                Vector3 previousSize=elbowShape.bounds.size;
                elbow.localScale*=1.6f;
                actor.transform.SetPositionAndRotation(actor.transform.position+new Vector3(2,1,-3),Quaternion.Euler(0,115,0));
                Physics.SyncTransforms();
                Check(Vector3.Distance(previousSize,elbowShape.bounds.size)>0.02f,"Limb stretch and player rotation also update collision geometry");
                HitsCurrentPose(elbowShape,"Stretched forearm");
                ValidateSkinFollowsCollider(model,rig);
                var item=GameObject.CreatePrimitive(PrimitiveType.Cube);item.name="Held object collision exclusion";
                item.transform.SetParent(fixture.transform);item.transform.localScale=Vector3.one*0.1f;
                var pickup=item.AddComponent<PickableObject>();
                var hand=actor.GetComponentsInChildren<RightHandPickupTrigger>().First(t=>t.name.StartsWith("Right"));
                Check(pickup.PickUp(hand.transform),"Held-object exclusion fixture can be picked up");
                foreach(var held in item.GetComponentsInChildren<Collider>().Where(c=>!c.isTrigger))
                    Check(colliders.All(c=>Physics.GetIgnoreCollision(c,held)),"Held objects ignore every animated player collider");
                var presence=fixture.AddComponent<WebPlayerPresence>();presence.Initialize(player);
                presence.ReceivePlayers(JsonUtility.ToJson(new WebPlayerPresence.Snapshot { players=new[]{
                    new WebPlayerPresence.PlayerState { id="collider-peer",x=2010,y=1000,z=2000,grounded=true,speed=4,crouching=true } } }));
                var remote=fixture.transform.Find("Online player collider-peer");
                Check(remote!=null&&remote.GetComponent<Rigidbody>().isKinematic,"Remote bodies collide without independently simulating their network pose");
                var remoteRig=remote.GetComponent<PlayerRigColliders>();
                Check(remoteRig.ShapeCount>=12,"Remote player meshes also have articulated collision");
                var remoteKnee=Bone(remote,"LeftKnee");var remoteShape=remoteRig.ShapeFor(remoteKnee);
                var remotes=(System.Collections.IDictionary)typeof(WebPlayerPresence).GetField("remotes",Hidden).GetValue(presence);
                var remoteState=remotes["collider-peer"];
                Vector3 oldPosition=remoteShape.bounds.center;
                remoteState.GetType().GetMethod("Tick",Hidden).Invoke(remoteState,new object[]{0.15f});
                Physics.SyncTransforms();
                Check(Vector3.Distance(oldPosition,remoteShape.bounds.center)>0.05f,"Network walk/crouch animation moves remote colliders");
                HitsCurrentPose(remoteShape,"Remote knee");
                Check(remote.GetComponentsInChildren<Collider>().All(c=>c.attachedRigidbody!=null&&c.attachedRigidbody.isKinematic&&!c.isTrigger),
                    "Remote shapes remain solid kinematic bodies while accepting replicated ragdoll poses");
                UnityEngine.Object.DestroyImmediate(rig);
                Check(!model.GetComponentsInChildren<Collider>().Any(c=>c.name.StartsWith("Rig Collider - ")),
                    "Removing the component removes its bone-owned collision shapes");
                Debug.Log("PLAYER_RIG_COLLIDER_VALIDATION_PASS: skin-fitted solid compounds, torso/head/limb/finger rotation, arm stretch, real ray hits at posed limbs, held-item exclusions, remote walk/crouch collision, temporary-mesh and collider cleanup.");
            }
            finally { UnityEngine.Object.DestroyImmediate(fixture); }
        }
        static void ValidateSkinFollowsCollider(Transform model,PlayerRigColliders rig)
        {
            var renderer=model.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.name=="HumanBody");
            var bones=renderer.bones;var weights=renderer.sharedMesh.boneWeights;
            int index=Array.FindIndex(bones,b=>b.name=="RightElbow");
            int vertex=Array.FindIndex(weights,w=>w.boneIndex0==index&&w.weight0>0.999f);
            Check(vertex>=0,"Forearm has a fully weighted skin vertex for collision validation");
            var baked=new Mesh();
            try
            {
                renderer.BakeMesh(baked,true);
                Vector3 before=renderer.transform.TransformPoint(baked.vertices[vertex]);
                bones[index].localRotation=Quaternion.Euler(0,0,60)*bones[index].localRotation;
                renderer.BakeMesh(baked,true);Physics.SyncTransforms();
                Vector3 after=renderer.transform.TransformPoint(baked.vertices[vertex]);
                var shape=rig.ShapeFor(bones[index]);
                Check(Vector3.Distance(before,after)>0.02f&&Vector3.Distance(shape.ClosestPoint(after),after)<0.09f,
                    "The animated collider follows an actual deformed mesh vertex");
            }
            finally { UnityEngine.Object.DestroyImmediate(baked); }
        }
        static Transform Bone(Transform root,string name) => root.GetComponentsInChildren<Transform>().First(t=>t.name==name);
        static void HitsCurrentPose(Collider shape,string name)
        {
            var center=shape.bounds.center;
            float span=Mathf.Max(2f,shape.bounds.extents.magnitude+0.5f);
            bool hit=shape.Raycast(new Ray(center-Vector3.right*span,Vector3.right),out _,span*2f);
            Check(hit,name+" is hit by a physics ray at its current animated position; bounds="+
                shape.bounds+", scale="+shape.transform.lossyScale+", type="+shape.GetType().Name);
            Check(shape.bounds.size.magnitude<2f,name+" collision fits the human mesh scale; bounds="+shape.bounds);
        }
        static void Check(bool value,string reason) { if(!value) throw new Exception(reason); }
    }
}
