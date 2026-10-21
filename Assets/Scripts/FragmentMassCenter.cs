using UnityEngine;
using UnityEngine.Rendering;

namespace Wyvernland
{
    internal static class FragmentMassCenter
    {
        internal static bool Align(Rigidbody body,Transform geometryRoot=null,bool wholeSkin=false)
        {
            if(body==null) return false;
            geometryRoot=geometryRoot!=null?geometryRoot:body.transform;
            // Rigidbody.centerOfMass uses position/rotation space, without scale.
            Matrix4x4 toBody=Matrix4x4.TRS(body.position,body.rotation,Vector3.one).inverse;
            Bounds bounds=default;bool found=false;
            void Include(Vector3 point) {
                if(!float.IsFinite(point.sqrMagnitude)) return;
                if(!found) { bounds=new Bounds(point,Vector3.zero);found=true; }
                else bounds.Encapsulate(point);
            }
            foreach(var filter in geometryRoot.GetComponentsInChildren<MeshFilter>(true)) {
                var mesh=filter.sharedMesh;var renderer=filter.GetComponent<Renderer>();
                if(mesh==null||mesh.vertexCount==0||filter.GetComponentInParent<Rigidbody>()!=body||
                    filter.GetComponentInParent<TreeImpactMarker>(true)!=null||
                    renderer!=null&&(!renderer.enabled||renderer.shadowCastingMode==ShadowCastingMode.ShadowsOnly)) continue;
                Matrix4x4 frame=toBody*filter.transform.localToWorldMatrix;Bounds local=mesh.bounds;
                for(int x=-1;x<=1;x+=2) for(int y=-1;y<=1;y+=2) for(int z=-1;z<=1;z+=2)
                    Include(frame.MultiplyPoint3x4(local.center+Vector3.Scale(local.extents,new Vector3(x,y,z))));
            }
            foreach(var skin in geometryRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                if(!skin.enabled||skin.sharedMesh==null||skin.shadowCastingMode==ShadowCastingMode.ShadowsOnly) continue;
                var baked=new Mesh();
                try {
                    skin.BakeMesh(baked,true);var vertices=baked.vertices;var weights=skin.sharedMesh.boneWeights;var bones=skin.bones;
                    Matrix4x4 frame=toBody*skin.transform.localToWorldMatrix;
                    for(int i=0;i<vertices.Length;i++) {
                        if(!wholeSkin) {
                            Transform bone=skin.transform;
                            if(i<weights.Length) {
                                var w=weights[i];int index=w.boneIndex0;float maximum=w.weight0;
                                if(w.weight1>maximum) { index=w.boneIndex1;maximum=w.weight1; }
                                if(w.weight2>maximum) { index=w.boneIndex2;maximum=w.weight2; }
                                if(w.weight3>maximum) index=w.boneIndex3;
                                if(index>=0&&index<bones.Length&&bones[index]!=null) bone=bones[index];
                            }
                            if(bone.GetComponentInParent<Rigidbody>()!=body) continue;
                        }
                        Include(frame.MultiplyPoint3x4(vertices[i]));
                    }
                } finally { PlayerDamage.Release(baked); }
            }
            if(!found) return false;
            SetCenter(body,bounds.center);return true;
        }
        internal static void SetCenter(Rigidbody body,Vector3 center)
        {
            body.ResetInertiaTensor();body.automaticCenterOfMass=false;body.centerOfMass=center;
            // Replacing compound colliders can leave a kinematic body's native
            // mass frame unchanged. Refresh it synchronously, before physics runs.
            if(body.isKinematic&&(body.centerOfMass-center).sqrMagnitude>0.00000001f) {
                body.isKinematic=false;
                try { body.ResetInertiaTensor();body.centerOfMass=center; }
                finally { body.isKinematic=true; }
            }
        }
    }
}
