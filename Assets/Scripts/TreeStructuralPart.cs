using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wyvernland
{
    // Tracks support through the generated tree's capped trunk and branch shells.
    // A detached section becomes a Rigidbody. Its connected descendants are
    // Transform children, so the entire supported subtree follows that body.
    public sealed class TreeStructuralPart : MonoBehaviour
    {
        readonly List<TreeStructuralPart> children=new();
        TreeStructuralPart parent;
        PhysicalDestructible destructible;
        bool attachmentInitialized;
        bool falling;
        bool groundAnchor;
        readonly List<Vector3> groundSupportPoints=new();
        [SerializeField] bool isTrunk;
        [SerializeField] bool isBranch;
        [SerializeField,Range(1,2)] int branchHitsToDetach=1;
        int branchDestructionHits;
        public bool IsTrunk => isTrunk;
        public bool IsBranch => isBranch;
        public bool IsAttachedBranch => isBranch&&parent!=null&&!falling;
        public bool CanBreakTrunkSupport => isTrunk&&parent!=null;
        public int BranchHitsToDetach => branchHitsToDetach;
        public int BranchDestructionHits => branchDestructionHits;
        public void SetTrunk(bool value) { EnsureInitialized(); isTrunk=value; }
        public void ConfigureBranch(int hitsToDetach)
        {
            isBranch=true;
            branchHitsToDetach=Mathf.Clamp(hitsToDetach,1,2);
        }

        public void SetGroundAnchor()
        {
            EnsureInitialized();
            groundAnchor=true;
            groundSupportPoints.Clear();
            foreach(Vector3 sample in attachmentSamples)
                groundSupportPoints.Add(transform.TransformPoint(sample));
            if(groundSupportPoints.Count==0)
                groundSupportPoints.Add(AttachmentPoint);
        }

        // Damage to a branch's own solid weakens only its attachment.
        // The branch remains one mesh and one pickable body when it falls.
        public bool RecordBranchDestruction()
        {
            if(!IsAttachedBranch) return false;
            branchDestructionHits++;
            if(branchDestructionHits<branchHitsToDetach) return false;
            ReleaseDescendantBranches();
            Detach();
            return true;
        }

        void ReleaseDescendantBranches()
        {
            foreach(var child in children.ToArray())
            {
                if(child==null) continue;
                child.ReleaseDescendantBranches();
                child.Detach();
            }
        }
        Vector3 attachmentLocal;
        readonly List<Vector3> attachmentSamples=new();
        const float ConnectionTolerance=0.06f;

        public Vector3 AttachmentPoint
        {
            get { EnsureInitialized(); return transform.TransformPoint(attachmentLocal); }
        }
        bool Owns(Collider collider) =>
            collider!=null&&collider.GetComponentInParent<TreeStructuralPart>(true)==this;
        static Vector3 ColliderPoint(Collider collider,Vector3 worldPoint) =>
            collider is MeshCollider { convex: false }?
                collider.bounds.ClosestPoint(worldPoint):collider.ClosestPoint(worldPoint);

        void Awake() => EnsureInitialized();
        void EnsureInitialized()
        {
            if(attachmentInitialized) return;
            attachmentInitialized=true;
            destructible=GetComponent<PhysicalDestructible>();
            if(destructible!=null) destructible.Fragmented+=OnFragmented;
            Mesh mesh=GetComponent<MeshFilter>()?.sharedMesh;
            if(mesh==null||mesh.vertexCount==0) return;
            Bounds bounds=mesh.bounds;
            float band=Mathf.Max(0.002f,bounds.size.y*0.005f);
            Vector3 sum=Vector3.zero;
            int count=0;
            foreach(Vector3 vertex in mesh.vertices)
                if(vertex.y<=bounds.min.y+band) { sum+=vertex; count++; }
            attachmentLocal=count>0?sum/count:bounds.center;
            foreach(Vector3 vertex in mesh.vertices)
                if(vertex.y<=bounds.min.y+band)
                    attachmentSamples.Add(vertex);
        }

        void OnDestroy()
        {
            if(destructible!=null) destructible.Fragmented-=OnFragmented;
            if(parent!=null) parent.children.Remove(this);
        }

        public float DistanceTo(Vector3 worldPoint)
        {
            if(!gameObject.activeInHierarchy) return float.PositiveInfinity;
            float nearest=float.PositiveInfinity;
            foreach(var collider in GetComponentsInChildren<Collider>())
            {
                if(!collider.enabled||collider.isTrigger||!Owns(collider)) continue;
                float distance=Vector3.Distance(ColliderPoint(collider,worldPoint),worldPoint);
                if(distance<nearest) nearest=distance;
            }
            return nearest;
        }

        Vector3 ClosestPoint(Vector3 worldPoint)
        {
            Vector3 best=worldPoint;
            float distance=float.PositiveInfinity;
            foreach(var collider in GetComponentsInChildren<Collider>())
            {
                if(!collider.enabled||collider.isTrigger||!Owns(collider)) continue;
                Vector3 point=ColliderPoint(collider,worldPoint);
                float candidate=(point-worldPoint).sqrMagnitude;
                if(candidate>=distance) continue;
                distance=candidate; best=point;
            }
            return best;
        }

        int SupportCount(TreeStructuralPart candidate,out float nearest)
        {
            EnsureInitialized();
            nearest=float.PositiveInfinity;
            int count=0;
            foreach(Vector3 sample in attachmentSamples)
            {
                Vector3 worldSample=transform.TransformPoint(sample);
                if(isTrunk&&gameObject.activeInHierarchy&&
                    DistanceTo(worldSample)>ConnectionTolerance) continue;
                float distance=candidate.DistanceTo(worldSample);
                nearest=Mathf.Min(nearest,distance);
                if(distance<=ConnectionTolerance) count++;
            }
            return count;
        }

        bool HasSolidCollider()
        {
            foreach(var collider in GetComponentsInChildren<Collider>())
                if(collider.enabled&&!collider.isTrigger&&Owns(collider)) return true;
            return false;
        }

        bool HasParentSupport()
        {
            if(parent==null||!HasSolidCollider()||!parent.HasSolidCollider()) return true;
            foreach(Vector3 sample in attachmentSamples)
            {
                Vector3 point=transform.TransformPoint(sample);
                if(DistanceTo(point)<=ConnectionTolerance&&
                    parent.DistanceTo(point)<=ConnectionTolerance) return true;
            }
            return false;
        }

        bool HasGroundSupport()
        {
            if(!HasSolidCollider()) return true;
            foreach(Vector3 point in groundSupportPoints)
                if(DistanceTo(point)<=ConnectionTolerance) return true;
            return false;
        }

        public void ValidateTrunkSupport()
        {
            if(!isTrunk||falling||!gameObject.activeInHierarchy) return;
            if(parent!=null)
            {
                if(parent.falling) { MarkInheritedFall(true); return; }
                if(!HasParentSupport())
                {
                    Detach();
                    destructible?.NotifyGeometryChanged();
                }
            }
            else if(groundAnchor)
            {
                if(!HasGroundSupport())
                {
                    StartFalling();
                    destructible?.NotifyGeometryChanged();
                }
            }
            else
            {
                StartFalling();
                destructible?.NotifyGeometryChanged();
            }
        }

        public void AttachTo(TreeStructuralPart newParent)
        {
            EnsureInitialized();
            if(newParent!=null) newParent.EnsureInitialized();
            if(newParent==this||newParent==parent) return;
            if(parent==null&&newParent!=null&&attachmentSamples.Count>0)
                for(int i=0;i<attachmentSamples.Count;i++)
                    attachmentSamples[i]=transform.InverseTransformPoint(newParent.ClosestPoint(
                        transform.TransformPoint(attachmentSamples[i])));
            if(parent!=null) parent.children.Remove(this);
            parent=newParent;
            if(parent!=null) groundAnchor=false;
            if(parent!=null) parent.children.Add(this);
            transform.SetParent(parent!=null?parent.transform:null,true);
            if(parent!=null)
            {
                ReleaseOwnBody();
                MarkInheritedFall(parent.falling);
            }
        }

        void Detach()
        {
            if(parent!=null) parent.children.Remove(this);
            parent=null;
            groundAnchor=false;
            transform.SetParent(null,true);
            StartFalling();
        }

        public bool BreakTrunkSupport()
        {
            if(!isTrunk||parent==null) return false;
            Detach();
            destructible?.NotifyGeometryChanged();
            return true;
        }

        internal void ReleaseChildren()
        {
            foreach(var child in children.ToArray())
                if(child!=null) child.Detach();
        }

        public void OnPickedUp()
        {
            if(parent!=null) parent.children.Remove(this);
            parent=null;
            // PickableObject already made this object's body kinematic.
            MarkInheritedFall(true);
        }

        void OnFragmented(PhysicalDestructible source,
            IReadOnlyList<PhysicalDestructible> pieces)
        {
            var candidates=new List<TreeStructuralPart>();
            if(!source.HasBeenDestroyed) candidates.Add(this);
            foreach(var piece in pieces)
            {
                if(piece==null) continue;
                var structure=piece.gameObject.AddComponent<TreeStructuralPart>();
                structure.SetTrunk(IsTrunk);
                structure.falling=true; // Spawn already supplied a dynamic body.
                candidates.Add(structure);
            }
            if(parent!=null||groundAnchor)
            {
                TreeStructuralPart supported=null;
                int mostContacts=0;
                foreach(var candidate in candidates)
                {
                    int contacts=0;
                    if(parent!=null)
                    {
                        foreach(Vector3 sample in attachmentSamples)
                        {
                            Vector3 point=transform.TransformPoint(sample);
                            if(candidate.DistanceTo(point)<=ConnectionTolerance&&
                                parent.DistanceTo(point)<=ConnectionTolerance) contacts++;
                        }
                    }
                    else
                    {
                        foreach(Vector3 point in groundSupportPoints)
                            if(candidate.DistanceTo(point)<=ConnectionTolerance) contacts++;
                    }
                    if(contacts<=mostContacts) continue;
                    mostContacts=contacts; supported=candidate;
                }
                if(supported!=null&&supported!=this)
                {
                    if(parent!=null)
                    {
                        supported.attachmentSamples.Clear();
                        foreach(Vector3 sample in attachmentSamples)
                            supported.attachmentSamples.Add(supported.transform.InverseTransformPoint(
                                transform.TransformPoint(sample)));
                        supported.AttachTo(parent);
                        if(!parent.falling) supported.SetGrounded();
                    }
                    else
                    {
                        supported.groundAnchor=true;
                        supported.groundSupportPoints.Clear();
                        supported.groundSupportPoints.AddRange(groundSupportPoints);
                        supported.SetGrounded();
                    }
                }
                if(!source.HasBeenDestroyed&&supported!=this)
                {
                    if(parent!=null) Detach();
                    else StartFalling();
                }
            }
            foreach(var child in children.ToArray())
            {
                if(child==null) continue;
                TreeStructuralPart best=null;
                float distance=float.PositiveInfinity;
                int supports=0;
                foreach(var candidate in candidates)
                {
                    int count=child.SupportCount(candidate,out float measured);
                    if(count<supports||count==supports&&measured>=distance) continue;
                    supports=count; distance=measured; best=candidate;
                }
                if(best==null||supports==0) child.Detach();
                else if(best!=this) child.AttachTo(best);
                else if(falling) child.MarkInheritedFall(true);
            }
        }

        void SetGrounded()
        {
            ReleaseOwnBody();
            MarkInheritedFall(false);
        }

        void StartFalling()
        {
            groundAnchor=false;
            if(parent==null&&GetComponentInParent<GeneratedTree>()!=null)
                transform.SetParent(null,true);
            MarkInheritedFall(true);
            Rigidbody body=GetComponent<Rigidbody>();
            if(body==null) body=gameObject.AddComponent<Rigidbody>();
            body.isKinematic=false;
            body.useGravity=true;
            body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            destructible?.RefreshMass();
        }

        void MarkInheritedFall(bool moving)
        {
            falling=moving;
            if(moving) ConfigureDynamicColliders();
            foreach(var child in children.ToArray())
                if(child!=null) child.MarkInheritedFall(moving);
        }

        void ReleaseOwnBody()
        {
            Rigidbody body=GetComponent<Rigidbody>();
            if(body==null) return;
            if(TryGetComponent<PickableObject>(out var pickup)&&pickup.IsHeld) return;
            body.isKinematic=true;
            body.detectCollisions=false;
            if(Application.isPlaying) Destroy(body);
            else DestroyImmediate(body);
        }

        internal void RefreshFallingGeometry()
        {
            if(falling) ConfigureDynamicColliders();
        }

        void ConfigureDynamicColliders()
        {
            foreach(var mesh in GetComponentsInChildren<MeshCollider>())
            {
                if(!mesh.enabled||mesh.isTrigger||!Owns(mesh)) continue;
                Mesh shape=mesh.sharedMesh;
                if(shape!=null&&shape.triangles.Length/3>200)
                {
                    var box=mesh.gameObject.AddComponent<BoxCollider>();
                    box.center=shape.bounds.center;
                    box.size=shape.bounds.size;
                    mesh.enabled=false;
                }
                else mesh.convex=true;
            }
        }

    }
}
