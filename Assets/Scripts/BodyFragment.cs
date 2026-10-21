using System;
using System.Linq;
using UnityEngine;

namespace Wyvernland
{
    // These are ordinary persistent world props, never temporary visual debris.
    public sealed class BodyFragment : MonoBehaviour
    {
        public string WorldId;
        public const double PickupVolume=.00008;
        internal static void Spawn(Mesh mesh,Material[] materials,Vector3 incoming,string identity,Transform owner=null)
        {
            if(mesh==null) return;
            string id="d"+WebWorldSession.Digest(System.Text.Encoding.UTF8.GetBytes(identity)).Substring(0,32);
            if(!WebWorldSession.IsPhysicsHost||WebWorldSession.ObjectOf(id)!=null) { PlayerDamage.Release(mesh);return; }
            var piece=new GameObject("Body fragment");
            piece.transform.SetParent(owner!=null?owner.parent:null,true);
            Vector3 center=mesh.bounds.center;var vertices=mesh.vertices;
            for(int i=0;i<vertices.Length;i++) vertices[i]-=center;
            mesh.vertices=vertices;mesh.RecalculateBounds();piece.transform.position=center;
            piece.AddComponent<BodyFragment>().WorldId=id;
            piece.AddComponent<MeshFilter>().sharedMesh=mesh;
            piece.AddComponent<RuntimeMeshOwner>().Initialize(mesh);
            var renderer=piece.AddComponent<MeshRenderer>();
            renderer.sharedMaterials=OwnAppearance(piece,materials,mesh.subMeshCount);
            ColorOnlyTextureQuality.ApplyRendererQuality(renderer);
            var shape=PhysicalDestructible.AddFragmentCollider(piece,mesh,true);
            var body=piece.AddComponent<Rigidbody>();body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            var properties=PhysicalDestructible.Ensure(piece);properties.ApplyMaterial("Flesh");
            double volume=DestructionMesh.SolidVolume(mesh);body.mass=Mathf.Max(.001f,(float)(volume*1050));
            if(volume>=PickupVolume) {
                var pickup=piece.AddComponent<PickableObject>();pickup.DisplayName="Body fragment";pickup.ForceRegularGrip=true;
            }
            var direction=(center-(owner!=null?owner.position:center-Vector3.up)).normalized;
            body.linearVelocity=Vector3.ClampMagnitude(incoming*.12f,10)+direction*2.5f+Vector3.up;
            body.angularVelocity=UnityEngine.Random.insideUnitSphere*4;
            if(volume<PhysicalDestructible.MinimumFragmentationVolume) SmallFragmentRest.Ensure(piece);
            if(owner!=null) foreach(var other in owner.GetComponentsInChildren<Collider>()) if(other!=shape) Physics.IgnoreCollision(shape,other);
            WebWorldSession.RegisterBodyFragment(piece,id);PlayerWorldLoading.NotifyChanged(piece);
        }

        internal static Material[] OwnAppearance(GameObject owner,Material[] originals,int slots)
        {
            var materials=new Material[Mathf.Max(1,Mathf.Max(slots,originals?.Length??0))];
            for(int i=0;i<materials.Length;i++)
                materials[i]=RuntimeMaterialOwner.RetainFragmentMaterial(owner,
                    originals!=null&&originals.Length>0?originals[Mathf.Min(i,originals.Length-1)]:null);
            return materials;
        }
    }
}
