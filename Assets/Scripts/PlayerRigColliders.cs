using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wyvernland
{
    // Compound collision fitted once from the posed skin. Parenting each shape
    // to its deform bone follows animation, wrist roll and limb stretch without
    // baking/cooking an animated triangle mesh on every frame.
    [ExecuteAlways,DisallowMultipleComponent]
    public sealed class PlayerRigColliders : MonoBehaviour
    {
        readonly Dictionary<Transform,Collider> shapes=new();
        internal int ShapeCount => shapes.Count;
        internal Collider ShapeFor(Transform bone) => shapes.TryGetValue(bone,out var shape)?shape:null;

        public void Initialize(Transform model,int collisionLayer,bool includeFeet=true)
        {
            foreach(var shape in shapes.Values)
                if(shape!=null) { shape.enabled=false;Release(shape.gameObject); }
            shapes.Clear();
            if(model==null) return;
            var bounds=new Dictionary<Transform,Bounds>();
            var baked=new Mesh { name="Player collider fitting skin" };
            try
            {
                foreach(var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if(renderer.sharedMesh==null||renderer.shadowCastingMode==ShadowCastingMode.ShadowsOnly) continue;
                    // The imported FBX uses a 100x renderer transform. Compensate
                    // for that scale before converting baked vertices to world.
                    baked.Clear();renderer.BakeMesh(baked,true);
                    var vertices=baked.vertices;
                    var weights=renderer.sharedMesh.boneWeights;
                    var bones=renderer.bones;
                    for(int i=0;i<vertices.Length;i++)
                    {
                        Transform bone=renderer.rootBone!=null?renderer.rootBone:renderer.transform;
                        if(i<weights.Length)
                        {
                            BoneWeight weight=weights[i];
                            int index=weight.boneIndex0;float maximum=weight.weight0;
                            if(weight.weight1>maximum) { index=weight.boneIndex1;maximum=weight.weight1; }
                            if(weight.weight2>maximum) { index=weight.boneIndex2;maximum=weight.weight2; }
                            if(weight.weight3>maximum) { index=weight.boneIndex3;maximum=weight.weight3; }
                            if(maximum>0f&&index>=0&&index<bones.Length&&bones[index]!=null) bone=bones[index];
                        }
                        if(!includeFeet&&IsFoot(bone)) continue;
                        Vector3 point=bone.InverseTransformPoint(renderer.transform.TransformPoint(vertices[i]));
                        if(bounds.TryGetValue(bone,out Bounds fit)) { fit.Encapsulate(point);bounds[bone]=fit; }
                        else bounds.Add(bone,new Bounds(point,Vector3.zero));
                    }
                }
                foreach(var pair in bounds) AddShape(pair.Key,pair.Value,collisionLayer);
            }
            finally { Release(baked); }
        }

        static bool IsFoot(Transform bone)
        {
            for(var parent=bone;parent!=null;parent=parent.parent)
                if(parent.name=="LeftAnkle"||parent.name=="RightAnkle") return true;
            return false;
        }
        void AddShape(Transform bone,Bounds bounds,int layer)
        {
            var node=new GameObject("Rig Collider - "+bone.name);node.layer=layer;
            node.transform.SetParent(bone,false);
            Vector3 scale=bone.lossyScale;
            // A small world-space skin covers seams between adjacent bones.
            Vector3 padding=new Vector3(0.003f/Mathf.Max(Mathf.Abs(scale.x),0.000001f),
                0.003f/Mathf.Max(Mathf.Abs(scale.y),0.000001f),
                0.003f/Mathf.Max(Mathf.Abs(scale.z),0.000001f));
            Vector3 size=bounds.size+padding*2f;
            bool limb=bone.name.Contains("Shoulder")||bone.name.Contains("Elbow")||
                bone.name.Contains("Hip")||bone.name.Contains("Knee")||
                bone.name.Contains("Fingers")||bone.name.Contains("Thumb");
            Collider shape;
            if(limb)
            {
                var capsule=node.AddComponent<CapsuleCollider>();
                int axis=size.x>size.y?(size.x>size.z?0:2):(size.y>size.z?1:2);
                capsule.direction=axis;capsule.center=bounds.center;
                capsule.radius=0.5f*Mathf.Max(size[(axis+1)%3],size[(axis+2)%3]);
                capsule.height=Mathf.Max(size[axis],capsule.radius*2f);shape=capsule;
            }
            else
            {
                var box=node.AddComponent<BoxCollider>();box.center=bounds.center;box.size=size;shape=box;
            }
            shape.contactOffset=0.003f;
            shapes.Add(bone,shape);
        }
        void OnDestroy()
        {
            // Shapes live under the bones, which may outlive this component.
            foreach(var shape in shapes.Values)
                if(shape!=null) { shape.enabled=false;Release(shape.gameObject); }
            shapes.Clear();
        }
        static void Release(Object value)
        { if(Application.isPlaying) Destroy(value);else DestroyImmediate(value); }
    }
}
