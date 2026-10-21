using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Wyvernland.Editor
{
    public static class ExtendedBodyDestructionValidation
    {
        static void Check(bool value,string message) { if(!value) throw new InvalidOperationException("Extended body destruction: "+message); }
        public static void Run()
        {
            var previous=SceneManager.GetActiveScene();if(string.IsNullOrEmpty(previous.path)) { EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");previous=SceneManager.GetActiveScene(); }
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
            var root=new GameObject("Extended body fixture");
            try {
                var dummy=TrainingDummy.Create(root.transform,null);dummy.transform.position+=Vector3.up*100;
                var damage=dummy.Damage;var model=damage.Ragdoll.Model;var skin=model.GetComponentsInChildren<SkinnedMeshRenderer>().First(s=>s.name=="HumanBody");
                bool Hit(PlayerBodyPart part,Vector3 point) {
                    var source=GameObject.CreatePrimitive(PrimitiveType.Cube);source.transform.SetParent(root.transform,false);source.transform.position=point+Vector3.right*4;source.transform.localScale=Vector3.one*.05f;
                    source.AddComponent<Rigidbody>().useGravity=false;PhysicalDestructible.Ensure(source);
                    return damage.ReceiveImpact(part,source.GetComponent<Collider>(),point,Vector3.right,Vector3.right*100);
                }
                for(int limb=1;limb<=4;limb++) {
                    damage.Respawn();int triangles=skin.sharedMesh.triangles.Length;
                    string bone=limb==1?"LeftElbow":limb==2?"RightElbow":limb==3?"LeftKnee":"RightKnee";
                    var shape=model.GetComponentsInChildren<PlayerHitShape>().First(t=>t.transform.parent.name==bone).GetComponent<Collider>();
                    var part=limb==1?PlayerBodyPart.LeftArm:limb==2?PlayerBodyPart.RightArm:PlayerBodyPart.Legs;
                    var point=shape.bounds.center;PickableObject retained=null;
                    if(limb<=2) {
                        var avatar=damage.GetComponent<RiggedPlayerAvatar>();
                        var holdingHand=dummy.GetComponentsInChildren<RightHandPickupTrigger>().First(t=>t.name.StartsWith(limb==1?"Left":"Right")).transform;
                        var held=GameObject.CreatePrimitive(PrimitiveType.Cube);held.transform.SetParent(root.transform,true);held.transform.localScale=Vector3.one*.05f;held.transform.position=holdingHand.position;
                        held.AddComponent<Rigidbody>().mass=1;retained=held.AddComponent<PickableObject>();
                        Check(avatar.BeginHandPickup(retained,limb==1,holdingHand.position),"arm fixture pickup starts");avatar.UpdatePickupReach(.2f);
                        Check(retained.IsHeld,"arm fixture holds object");
                    }
                    if(limb<=2) { Check(Hit(part,point),"first arm wound");Check(Hit(part,point),"second arm wound"); }
                    else for(int wound=0;wound<3;wound++) Check(Hit(part,point),"initial leg wound");
                    Check(Hit(part,point),"initial disabling hit");
                    if(limb<=2) Check(!damage.CanUseHand(limb==1)&&
                        retained.IsHeld==(limb==1?damage.GetComponent<RiggedPlayerAvatar>().HasLeftHandItem:
                            damage.GetComponent<RiggedPlayerAvatar>().HasRightHandItem),
                        "Catagma disables the arm and may release its held object");
                    for(int i=0;i<3;i++) Check(Hit(part,point),"additional limb hit");
                    Check(damage.Capture().severed==0,"three additional destructive hits do not detach the limb");
                    Check(Hit(part,point),"fourth additional destructive hit");
                    var state=damage.Capture();Check(state.severed==(1<<(limb-1))&&state.limbs.Length==1,"only the contacted limb detaches after more than three additional hits");
                    var piece=PlayerCorpse.Find(state.limbs[0].id);Check(piece!=null&&piece.Limb==limb,"detached limb exists as its own object");
                    if(limb<=2) Check(!retained.IsHeld&&
                        (retained.GetComponent<CorpseGrip>()==null||
                         retained.GetComponent<FixedJoint>()?.connectedBody?.transform.IsChildOf(piece.transform)==true),
                        "severed arm retains a surviving grip and leaves an already dropped object free");
                    Check(!shape.enabled&&skin.sharedMesh.triangles.Length<triangles,"living mesh and collider no longer contain the detached limb");
                    var pickup=piece.GetComponent<PickableObject>();Check(pickup.GrabMode==PickableGrabMode.Stick&&pickup.CanPickUpWithOneHand,"detached limb is a pickable stick");
                    var hand=new GameObject("Living hand").transform;hand.SetParent(root.transform,false);hand.position=piece.NearestPoint(piece.transform.position);
                    Check(pickup.PickUp(hand,false,hand.position),"limb can attach to a hand immediately");
                    var rig=piece.GetComponent<PlayerRagdoll>();Check(rig.Bodies.Count(b=>b!=null)==3&&rig.Bodies.Where(b=>b!=null).All(b=>!b.isKinematic),"held limb retains its three-bone ragdoll");
                    pickup.Release(Vector3.up*2,hand);Check(rig.Bodies.Where(b=>b!=null).All(b=>!b.isKinematic),"released limb keeps ragdoll physics");
                    var replicaState=piece.Capture();var replica=PlayerCorpse.Create(new PlayerDamage.CorpseState { id="Replica limb "+limb,limb=limb,position=replicaState.position,rotation=replicaState.rotation,pose=replicaState.pose },root.transform);
                    Check(replica.GetComponentsInChildren<SkinnedMeshRenderer>().Any(s=>s.enabled&&s.sharedMesh.triangles.Length>0),"room restoration rebuilds a visible severed limb");
                }
                damage.Respawn();int before=root.GetComponentsInChildren<BodyFragment>().Length;
                var baked=new Mesh();skin.BakeMesh(baked,true);Vector3 desired=dummy.transform.position+new Vector3(0,1.2f,.15f);
                Vector3 contact=baked.vertices.Select(p=>skin.transform.TransformPoint(p)).OrderBy(p=>(p-desired).sqrMagnitude).First();UnityEngine.Object.DestroyImmediate(baked);
                var original=skin.sharedMesh;Check(Hit(PlayerBodyPart.Torso,contact),"torso destruction accepted");
                Check(skin.sharedMesh!=original&&root.GetComponentsInChildren<BodyFragment>().Length>before,"larger Boolean torso pit produces physical flying chips");
                Check(Hit(PlayerBodyPart.Head,dummy.transform.position+Vector3.up*1.7f),"head destruction accepted");
                var debris=root.GetComponentsInChildren<BodyFragment>();
                Check(debris.Length>0&&debris.All(d=>d.GetComponent<TemporaryShatterDebris>()==null),"body chips never use automatic temporary-debris cleanup");
                Check(debris.Any(d=>d.GetComponent<PickableObject>()!=null),"large body fragments are pickable; volumes="+string.Join(",",debris.Select(d=>DestructionMesh.SolidVolume(d.GetComponent<MeshFilter>().sharedMesh))));
                foreach(var chip in debris) Check(!string.IsNullOrEmpty(chip.WorldId)&&chip.GetComponent<Rigidbody>().linearVelocity.sqrMagnitude>0,"persistent chip has stable identity and outgoing motion");
                Check(PickableObject.ChooseGrabMode(new Vector3(.02f,.6f,.02f),"Stone")==PickableGrabMode.Regular&&PickableObject.ChooseGrabMode(new Vector3(.02f,.6f,.02f),"Sulfur")==PickableGrabMode.Regular,"long stone and mineral fragments use Regular grip");
                Debug.Log("EXTENDED_BODY_DESTRUCTION_VALIDATION_PASS: four separate limbs sever on the fourth additional disabled hit, skin/collider removal, one-hand Stick pickup with live ragdoll, visible room reconstruction, enlarged real torso pit with chips, lasting head/body fragments and Regular mineral-fragment grip.");
            } finally { SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true);Time.timeScale=1; }
        }
    }
}
