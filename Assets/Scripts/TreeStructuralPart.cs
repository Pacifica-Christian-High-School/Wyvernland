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
        GeneratedTree sourceTree;
        bool attachmentInitialized;
        bool falling;
        float branchLaunchLimitUntil;
        const float BranchLaunchLimitSeconds=0.5f;
        bool groundAnchor;
        readonly List<Vector3> groundSupportPoints=new();
        [SerializeField] bool isTrunk;
        [SerializeField] bool isBranch;
        [SerializeField,Range(1,2)] int branchHitsToDetach=1;
        int branchDestructionHits;
        public bool IsTrunk => isTrunk;
        public GeneratedTree Tree => sourceTree!=null?sourceTree:GetComponentInParent<GeneratedTree>();
        public bool IsBranch => isBranch;
        public bool IsAttachedBranch => isBranch&&parent!=null&&!falling;
        public bool CanBreakTrunkSupport => isTrunk&&parent!=null;
        public bool CanDrag => falling;
        internal bool IsStructuralRoot => parent==null;
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
            if(!WebWorldSession.CanSimulate(gameObject)) return false;
            WebWorldSession.Dirty(gameObject);
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
            collider!=null&&collider.GetComponentInParent<TreeStructuralPart>(true)==this&&
            collider.GetComponentInParent<PhysicalDestructible>(true)==destructible;

        public bool TryGetSolidBounds(out Bounds bounds) => TryGetSolidBoundsInFrame(null,out bounds);

        public bool TryGetSolidBoundsInFrame(Transform frame,out Bounds bounds)
        {
            EnsureInitialized();
            bounds=default;
            bool found=false;
            Matrix4x4 toFrame=frame!=null?frame.worldToLocalMatrix:Matrix4x4.identity;
            foreach(Collider collider in GetComponentsInChildren<Collider>())
            {
                if(!collider.enabled||collider.isTrigger||!Owns(collider)) continue;
                if(collider.name=="Trunk split sphere"||
                    collider.GetComponentInParent<TreeImpactMarker>(true)!=null) continue;
                Bounds current;
                if(collider is MeshCollider mesh)
                {
                    if(mesh.sharedMesh==null) continue;
                    current=SolidBounds(mesh.sharedMesh.bounds,toFrame*collider.transform.localToWorldMatrix);
                }
                else if(collider is BoxCollider box)
                    current=SolidBounds(new Bounds(box.center,box.size),toFrame*box.transform.localToWorldMatrix);
                else current=SolidBounds(collider.bounds,toFrame);
                if(!found) { bounds=current;found=true; } else bounds.Encapsulate(current);
            }
            return found;
        }
        static Bounds SolidBounds(Bounds local,Matrix4x4 frame)
        {
            Vector3 x=frame.MultiplyVector(Vector3.right*local.extents.x);
            Vector3 y=frame.MultiplyVector(Vector3.up*local.extents.y);
            Vector3 z=frame.MultiplyVector(Vector3.forward*local.extents.z);
            Vector3 extents=new(Mathf.Abs(x.x)+Mathf.Abs(y.x)+Mathf.Abs(z.x),
                Mathf.Abs(x.y)+Mathf.Abs(y.y)+Mathf.Abs(z.y),
                Mathf.Abs(x.z)+Mathf.Abs(y.z)+Mathf.Abs(z.z));
            return new Bounds(frame.MultiplyPoint3x4(local.center),extents*2f);
        }
        static Vector3 ColliderPoint(Collider collider,Vector3 worldPoint) =>
            collider is MeshCollider { convex: false }?
                collider.bounds.ClosestPoint(worldPoint):collider.ClosestPoint(worldPoint);

        void Awake() { EnsureInitialized(); enabled=false; }
        internal static void ProcessTerrainContact(Collision collision)
        {
            // Compound-body callbacks arrive at the fallen trunk. Route each
            // actual contact to its branch, including nested branch colliders.
            for(int i=0;i<collision.contactCount;i++)
            {
                ContactPoint contact=collision.GetContact(i);
                DetachBranchTouchingTerrain(contact.thisCollider,contact.otherCollider);
                DetachBranchTouchingTerrain(contact.otherCollider,contact.thisCollider);
            }
        }

        static void DetachBranchTouchingTerrain(Collider branchCollider,Collider terrainCollider)
        {
            if(branchCollider==null||terrainCollider==null||
                terrainCollider.GetComponentInParent<PhysicalDestructible>() is not { IsTerrain: true })
                return;
            TreeStructuralPart branch=branchCollider.GetComponentInParent<TreeStructuralPart>();
            if(branch==null||!branch.isBranch||!branch.falling||branch.parent==null) return;
            Rigidbody source=branchCollider.attachedRigidbody;
            if(source==null||source.isKinematic) return;
            Vector3 velocity=source.linearVelocity;
            Vector3 angularVelocity=source.angularVelocity;
            Vector3 center=source.worldCenterOfMass;
            branch.Detach();
            Rigidbody detached=branch.GetComponent<Rigidbody>();
            // Keep the motion at the branch's centre as it leaves the compound
            // body; gravity then acts on it independently of the trunk.
            detached.linearVelocity=velocity+Vector3.Cross(angularVelocity,
                detached.worldCenterOfMass-center);
            detached.angularVelocity=angularVelocity;
            source.GetComponent<PhysicalDestructible>()?.RefreshMass();
            source.WakeUp();
            detached.WakeUp();
        }

        void FixedUpdate()
        {
            if(!isBranch||!falling||Time.time>=branchLaunchLimitUntil)
            { enabled=false; return; }
            Rigidbody body=GetComponent<Rigidbody>();
            if(body==null||body.isKinematic) return;
            Vector3 velocity=body.linearVelocity;
            if(velocity.y<=PhysicalDestructible.WoodFragmentMaximumUpwardSpeed) return;
            velocity.y=PhysicalDestructible.WoodFragmentMaximumUpwardSpeed;
            body.linearVelocity=velocity;
        }
        void EnsureInitialized()
        {
            if(attachmentInitialized) return;
            attachmentInitialized=true;
            sourceTree=GetComponentInParent<GeneratedTree>();
            sourceTree?.RegisterPart(this);
            destructible=GetComponent<PhysicalDestructible>();
            if(destructible!=null) destructible.Fragmented+=OnFragmented;
            MeshFilter filter=GetComponent<MeshFilter>();Mesh mesh=filter!=null?filter.sharedMesh:null;
            if(mesh==null) foreach(var childFilter in GetComponentsInChildren<MeshFilter>())
                if(childFilter.sharedMesh!=null) { mesh=childFilter.sharedMesh;break; }
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
            sourceTree?.UnregisterPart(this);
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

        internal Transform LoadingSupport=>parent!=null?parent.transform:null;
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
            if(!WebWorldSession.CanSimulate(gameObject)) return;
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
            WebWorldSession.Dirty(gameObject);
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
            var previousBody=GetComponentInParent<Rigidbody>();
            if(parent!=null) parent.children.Remove(this);
            parent=null;
            groundAnchor=false;
            transform.SetParent(null,true);
            StartFalling();
            if(previousBody!=null&&previousBody!=GetComponent<Rigidbody>())
                previousBody.GetComponent<PhysicalDestructible>()?.RefreshMass();
        }

        public bool BreakTrunkSupport()
        {
            if(!isTrunk||parent==null) return false;
            Vector3 splitPoint=AttachmentPoint;
            Detach();
            AddTrunkSplitSphere(splitPoint);
            destructible?.NotifyGeometryChanged();
            Tree?.ResetSplitParts();
            return true;
        }

        internal void AssignTree(GeneratedTree tree)
        {
            sourceTree?.UnregisterPart(this);
            sourceTree=tree;
            tree?.RegisterPart(this);
            foreach(var child in children.ToArray())
                if(child!=null) child.AssignTree(tree);
        }

        internal void AddTrunkSplitSphere(Vector3 splitPoint)
        {
            Rigidbody body=GetComponent<Rigidbody>();
            if(!isTrunk||!falling||transform.Find("Trunk split sphere")!=null||
                body==null||body.isKinematic||
                !TryGetSolidBounds(out Bounds bounds)) return;
            // A rounded contact below the cut face prevents the upper half
            // resting flat on its stump. Keep this physics helper out of the
            // render meshes used for material volume and later destruction.
            float radius=Mathf.Clamp(Mathf.Min(bounds.size.x,bounds.size.z)*0.15f,0.025f,0.15f);
            var sphere=new GameObject("Trunk split sphere");
            Vector3 cutNormal=transform.worldToLocalMatrix.transpose.MultiplyVector(Vector3.up).normalized;
            sphere.transform.position=bounds.center-cutNormal*Vector3.Dot(bounds.center-splitPoint,cutNormal);
            sphere.transform.SetParent(transform,true);
            var collider=sphere.AddComponent<SphereCollider>();
            collider.radius=radius;
            GetComponent<DestructionFragment>()?.TrackCollider(collider);
            // Even a perfectly straight trunk needs a small initial tilt to
            // leave its balanced position on the rounded contact.
            Vector3 lean=Vector3.ProjectOnPlane(body.worldCenterOfMass-sphere.transform.position,Vector3.up);
            if(lean.sqrMagnitude<0.0001f) lean=Vector3.right;
            body.angularVelocity+=Vector3.Cross(Vector3.up,lean.normalized)*0.75f;
            body.linearVelocity+=lean.normalized*0.25f;
            body.WakeUp();
        }

        internal void ReleaseChildren()
        {
            foreach(var child in children.ToArray())
                if(child!=null) child.Detach();
        }

        public void OnPickedUp()
        {
            if(!WebWorldSession.CanSimulate(gameObject)) return;
            WebWorldSession.Dirty(gameObject);
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
                structure.AssignTree(Tree);
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
            foreach(var candidate in candidates)
                if(candidate!=null) candidate.RefreshTrunkCenterOfMass();
            if(source.HasBeenDestroyed&&isTrunk) Tree?.ResetSplitParts();
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
            DynamicObjectSurfaceGuard.Ensure(body);
            if(isBranch) { branchLaunchLimitUntil=Time.time+BranchLaunchLimitSeconds; enabled=true; }
            destructible?.RefreshMass();
        }

        void MarkInheritedFall(bool moving)
        {
            WebWorldSession.Dirty(gameObject);
            falling=moving;
            if(moving) { ConfigureDynamicColliders(); GetComponent<PickableObject>()?.TrackMovement(); }
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

        [Serializable] public sealed class NetworkStructure
        {
            public string parent,tree;
            public bool falling,ground,trunk,branch;
            public int hits,threshold;
            public Vector3[] supports;
        }
        internal NetworkStructure CaptureNetworkStructure() => new() {
            parent=WebWorldSession.IdOf(parent!=null?parent.gameObject:null),
            tree=WebWorldSession.IdOf(Tree!=null?Tree.gameObject:null),falling=falling,
            ground=groundAnchor,trunk=isTrunk,branch=isBranch,hits=branchDestructionHits,
            threshold=branchHitsToDetach,supports=groundSupportPoints.ToArray(),
        };
        internal void ApplyNetworkStructure(NetworkStructure value)
        {
            if(value==null) return;
            EnsureInitialized();
            var next=WebWorldSession.ObjectOf(value.parent)?.GetComponent<TreeStructuralPart>();
            if(parent!=next) { if(parent!=null) parent.children.Remove(this);parent=next;
                if(parent!=null&&!parent.children.Contains(this)) parent.children.Add(this); }
            var tracker=WebWorldSession.ObjectOf(value.tree)?.GetComponent<GeneratedTree>();
            if(tracker!=null&&tracker!=sourceTree) AssignTree(tracker);
            falling=value.falling;groundAnchor=value.ground;isTrunk=value.trunk;isBranch=value.branch;
            branchDestructionHits=value.hits;branchHitsToDetach=value.threshold;
            groundSupportPoints.Clear();if(value.supports!=null) groundSupportPoints.AddRange(value.supports);
        }

        internal void RefreshTrunkCenterOfMass()
        {
            Rigidbody body=GetComponentInParent<Rigidbody>();
            if(body==null||body.GetComponent<TreeStructuralPart>() is not { IsTrunk: true }) return;
            // Measure only the connected trunk's live render meshes in body space.
            // Branches, resin, markers and the cut-face sphere must not move its center.
            Bounds bounds=default;
            bool found=false;
            Matrix4x4 toBody=Matrix4x4.TRS(body.position,body.rotation,Vector3.one).inverse;
            foreach(var filter in body.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh=filter.sharedMesh;
                if(mesh==null||mesh.vertexCount==0||
                    filter.GetComponentInParent<TreeImpactMarker>(true)!=null||
                    filter.GetComponentInParent<TreeStructuralPart>() is not { IsTrunk: true } part||
                    filter.GetComponentInParent<PhysicalDestructible>()!=part.destructible||
                    filter.GetComponentInParent<Rigidbody>()!=body) continue;
                Matrix4x4 frame=toBody*filter.transform.localToWorldMatrix;
                Bounds local=mesh.bounds;
                for(int x=-1;x<=1;x+=2)
                for(int y=-1;y<=1;y+=2)
                for(int z=-1;z<=1;z+=2)
                {
                    Vector3 point=frame.MultiplyPoint3x4(local.center+
                        Vector3.Scale(local.extents,new Vector3(x,y,z)));
                    if(!found) { bounds=new Bounds(point,Vector3.zero); found=true; }
                    else bounds.Encapsulate(point);
                }
            }
            if(!found) return;
            FragmentMassCenter.SetCenter(body,bounds.center);
        }

        void ConfigureDynamicColliders()
        {
            foreach(var mesh in GetComponentsInChildren<MeshCollider>())
            {
                if(!mesh.enabled||mesh.isTrigger||!Owns(mesh)) continue;
                Mesh shape=mesh.sharedMesh;
                if(shape!=null&&shape.triangles.Length/3>PhysicalDestructible.MaximumConvexFragmentTriangles)
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
