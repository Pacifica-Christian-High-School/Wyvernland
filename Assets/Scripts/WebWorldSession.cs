using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Wyvernland
{
    // Deterministic untouched objects stay in the local generated baseline.
    // The room stores changed objects, actual cut geometry, and removal records.
    [DefaultExecutionOrder(400)]
    public sealed class WebWorldSession : MonoBehaviour
    {
        [Serializable] public sealed class ObjectState
        {
            public string npcGrip;public float[] npcLocalP,npcLocalQ;
            public string id,name,parent,mesh,holder,writer,pickableName,hand,gripSource,gripReplacement,releaseId;
            public float[] p,q,s,v,w,grip;
            public bool deleted,body,kinematic,gravity,held,pickable,sleeping;
            public bool hasPhysical,hasTree,hasHistory,hasRope;
            public bool hasNpc,hasDummy,hasCorpse,testBar,bodyFragment,regularGrip;
            public bool smallFragment,shatterDebris,fragmentSettled;
            public PlayerDamage.CorpseState corpse;
            public TrainingDummy.State dummy;
            public float mass;
            public long revision,expires;
            public int label;
            public PhysicalDestructible.NetworkProperties physical;
            public TreeStructuralPart.NetworkStructure tree;
            public GeneratedTree.NetworkHistory history;
            public RopeObject.NetworkRope rope;
        }
        [Serializable] public sealed class MeshData { public string hash,data; }
        [Serializable] public sealed class Claim { public string id;public bool owned; }
        [Serializable] public sealed class ReleaseAck { public string id,releaseId;public bool accepted;public long revision; }
        [Serializable] public sealed class Command
        {
            public string type,@object,target,commandId,player;
            public float[] point,incoming;
            public int hands;
            public bool hasStrikeRoll;
            public float strikeRoll;
        }
        [Serializable] public sealed class Packet
        {
            public string self,host,error;
            public bool ready;
            public ObjectState[] objects;
            public MeshData[] meshes;
            public Command[] commands;
            public Claim[] claims;
            public ReleaseAck[] releases;
            public int playerCount;
        }
        [Serializable] public sealed class Batch
        { public ObjectState[] objects;public MeshData[] meshes;public string[] ack; }
        [Serializable] public sealed class Request
        { public string action,@object;public ObjectState state;public Command command; }
        sealed class Item
        {
            public string Id,Owner="",Mesh="",PhysicalJson="",TreeJson="",HistoryJson="",GripSource="",GripReplacement="";
            public GameObject Target;
            public Transform OriginalParent;
            public ObjectState Last;
            public bool Geometry,Pending,ClaimRejected,GrabbedPending;
            public float ClaimRejectedAt;
            public ObjectState ClaimSnapshot,PendingRelease,OutgoingRelease;
            public string ReleaseId="";public long ReleaseRevision;
            public float Granted;
            public long Expires;
            public Material[] Materials;
            public Rigidbody Body;
            public RopeObject Rope;
            public PickableObject Pickup;
            public TrainingDummy Dummy;
            public PlayerCorpse Corpse;
            public int ColliderBodyId;
            public bool ColliderGeometryChanged;
        }
        static WebWorldSession current;
        readonly Dictionary<string,Item> items=new();
        readonly Dictionary<int,Item> instances=new();
        readonly Dictionary<string,string> geometry=new();
        readonly HashSet<string> dirty=new(),motion=new(),acks=new(),executed=new();
        readonly HashSet<string> deferredRelations=new();
        readonly Dictionary<string,(ObjectState State,float Until)> deferredGrips=new();
        readonly Dictionary<string,ObjectState> removed=new();
        readonly HashSet<Item> dynamics=new(),owned=new(),pendingClaims=new();
        readonly Dictionary<string,float> lastCommand=new();
        FirstPersonController player;
        Transform world;
        string baseline,self="",host="",error;
        bool ready,applying,closing,pausedForSync,transferringGrip;
        readonly HashSet<string> removedCorpseIds=new();
        float nextSend,nextScan;
        internal int CapturedStateCount { get; private set; }
        internal int LastMotionProbeCount { get; private set; }
        internal int ColliderRefreshCount { get; private set; }
#if UNITY_EDITOR
        internal event Action<Request> RequestForValidation;
#endif
        public bool IsHost => ready&&!string.IsNullOrEmpty(self)&&self==host;
        public bool IsReady => ready;
        public static bool IsPhysicsHost=>current==null||current.IsHost;
        public static string LocalPlayerId=>current?.self??"";
        public static int RoomPlayerCount=>current!=null&&current.ready?current.roomPlayerCount:0;
        int roomPlayerCount=1;
        public static bool IsUnclaimed(GameObject target)=>current==null||string.IsNullOrEmpty(current.Owner(target));
        public static bool CanSimulate(GameObject target) => current==null||current.applying||current.IsHost&&
            (target==null||!current.instances.TryGetValue(target.GetInstanceID(),out var item)||!item.Pending);
        public static bool CanSimulateRope(GameObject target) => current==null||current.applying||
            target!=null&&current.instances.TryGetValue(target.GetInstanceID(),out var item)&&
            current.SimulateRope(item,item.Last!=null&&item.Last.held);
        bool SimulateRope(Item item,bool held) => ready&&
            (item.OutgoingRelease!=null||IsHost&&!held||item.Owner==self&&item.Pickup!=null&&item.Pickup.IsHeld);
        public static bool CanInteract(GameObject target) => current==null||current.ready&&
            (string.IsNullOrEmpty(current.Owner(target))||current.Owner(target)==current.self)&&
            (target==null||!current.instances.TryGetValue(target.GetInstanceID(),out var item)||
                !item.ClaimRejected||Time.unscaledTime-item.ClaimRejectedAt>=0.5f);
        public static bool HasConfirmedClaim(PickableObject pickup) => current==null||
            pickup!=null&&current.instances.TryGetValue(pickup.gameObject.GetInstanceID(),out var item)&&
            !item.Pending&&item.Owner==current.self;
        string Owner(GameObject target) => target!=null&&instances.TryGetValue(target.GetInstanceID(),out var item)?item.Owner:"";
        public static string IdOf(GameObject target) => target!=null&&current!=null&&current.instances.TryGetValue(target.GetInstanceID(),out var item)?item.Id:"";
        public static GameObject ObjectOf(string id) => !string.IsNullOrEmpty(id)&&current!=null&&current.items.TryGetValue(id,out var item)?item.Target:null;
        internal static bool LoadingMotion(GameObject target,out Vector3 velocity)
        {
            velocity=Vector3.zero;
            if(current==null||target==null||!current.instances.TryGetValue(target.GetInstanceID(),out var item)||item.Last==null) return false;
            if(target.GetComponent<SmallFragmentRest>() is { IsSettled:true }) return false;
            var state=item.Last;velocity=Vector(state.v);
            if(!string.IsNullOrEmpty(item.Owner)||state.held||current.motion.Contains(item.Id)) return true;
            if(state.hasRope&&state.rope!=null) {
                var rope=state.rope;
                if(rope.sleeping!=null) foreach(bool asleep in rope.sleeping) if(!asleep) return true;
                return false;
            }
            return state.body&&!state.kinematic&&!state.sleeping;
        }
        internal static bool ReplicaMotion(GameObject target,out Vector3 velocity,out Vector3 angular)
        {
            velocity=angular=Vector3.zero;
            if(current==null||target==null||!current.instances.TryGetValue(target.GetInstanceID(),out var item)||item.Last==null||
                current.IsHost&&!(item.Last.held&&item.Owner!=current.self)) return false;
            velocity=Vector(item.Last.v);angular=Vector(item.Last.w);return true;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void WyvernlandWorldReady(string baseline);
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void WyvernlandPublishWorld(string json);
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void WyvernlandWorldRequest(string json);
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void WyvernlandSetHosting(int value);
#endif
        internal IEnumerator Initialize(GameObject root,FirstPersonController controller)
        {
            current=this;world=root.transform;player=controller;
            var candidates=new HashSet<GameObject>();
            foreach(var component in root.GetComponentsInChildren<PhysicalDestructible>(true))
                if(!component.IsTerrain) candidates.Add(component.gameObject);
            foreach(var component in root.GetComponentsInChildren<PickableObject>(true)) candidates.Add(component.gameObject);
            foreach(var component in root.GetComponentsInChildren<RopeObject>(true)) candidates.Add(component.gameObject);
            foreach(var component in root.GetComponentsInChildren<GeneratedTree>(true)) candidates.Add(component.gameObject);
            foreach(var component in root.GetComponentsInChildren<TrainingDummy>(true)) candidates.Add(component.gameObject);
            double batch=Time.realtimeSinceStartupAsDouble;
            var fingerprints=new List<string>();
            foreach(var target in candidates.OrderBy(target=>PathOf(target.transform),StringComparer.Ordinal))
            {
                string path=PathOf(target.transform),id=target.GetComponent<PlayerCorpse>() is {} corpse?CorpseId(corpse.name):target.GetComponent<BodyFragment>() is {} debris?debris.WorldId:"b"+Digest(Encoding.UTF8.GetBytes(path)).Substring(0,24);
                Register(target,id,target.GetComponent<BodyFragment>()!=null);
                var initialFilter=target.GetComponent<MeshFilter>();var mesh=initialFilter!=null?initialFilter.sharedMesh:null;
                Vector3 point=target.transform.position;
                // Adding an actor must not invalidate existing terrain/prop rooms.
                if(target.GetComponent<TrainingDummy>()==null&&target.GetComponent<PlayerCorpse>()==null&&target.GetComponent<BodyFragment>()==null) fingerprints.Add(id+":"+Mathf.RoundToInt(point.x*100)+","+Mathf.RoundToInt(point.y*100)+","+
                    Mathf.RoundToInt(point.z*100)+":"+(mesh!=null?mesh.vertexCount:0));
                if(WorldLoading.ShouldYield(ref batch)) { yield return null;batch=Time.realtimeSinceStartupAsDouble; }
            }
            baseline=Digest(Encoding.UTF8.GetBytes("Wyvernland shared world 1\n"+string.Join("\n",fingerprints)));
            // Static baseline props already have their finished colliders. Only
            // simulated objects need a pose snapshot and a physics handoff.
            foreach(var item in dynamics) {
                item.Last=Capture(item,false,null);SetPhysics(item,item.Last);
                if(WorldLoading.ShouldYield(ref batch)) { yield return null;batch=Time.realtimeSinceStartupAsDouble; }
            }
        }
        string PathOf(Transform target)
        {
            var path=new List<string>();
            while(target!=null&&target!=world) { path.Add(target.GetSiblingIndex()+":"+target.name);target=target.parent; }
            path.Reverse();return string.Join("/",path);
        }
        internal void BeginRoom()
        {
            pausedForSync=true;player?.SetPaused(true);
#if UNITY_WEBGL && !UNITY_EDITOR
            WyvernlandWorldReady(baseline);
#endif
        }
        Item Register(GameObject target,string id,bool includeGeometry)
        {
            if(instances.TryGetValue(target.GetInstanceID(),out var existing)) return existing;
            var item=new Item { Id=id,Target=target,OriginalParent=target.transform.parent,Geometry=includeGeometry,
                Expires=0,
                Body=target.GetComponent<Rigidbody>(),Rope=target.GetComponent<RopeObject>(),Pickup=target.GetComponent<PickableObject>(),Dummy=target.GetComponent<TrainingDummy>(),Corpse=target.GetComponent<PlayerCorpse>() };
            var assembly=target.GetComponentInParent<Rigidbody>();item.ColliderBodyId=assembly!=null?assembly.GetInstanceID():0;
            if(assembly!=null) MovingImpactMotion.Ensure(assembly);
            if(items.ContainsKey(id)) throw new InvalidOperationException("Duplicate shared object identity: "+id);
            items.Add(id,item);instances.Add(target.GetInstanceID(),item);
            if(target.TryGetComponent<PhysicalDestructible>(out var properties)) {
                properties.GeometryChanged+=()=> { if(!applying) { item.Geometry=true;Dirty(target); } };
                properties.HardnessChanged+=_=>Dirty(target);
                properties.Fragmented+=(_,pieces)=> { if(applying) return;
                    foreach(var piece in pieces) if(piece!=null) RegisterSpawn(piece.gameObject);Dirty(target); };
            }
            if(item.Body!=null||item.Rope!=null) dynamics.Add(item);
            if(item.Corpse!=null) item.Corpse.WorldRegistered=true;
            if(includeGeometry||item.Corpse!=null) dirty.Add(id);
            return item;
        }
        public static void RegisterSpawn(GameObject target)
        {
            PlayerWorldLoading.NotifyChanged(target);
            if(current==null||current.applying||!current.IsHost||target==null) return;
            if(target.GetComponent<BodyFragment>() is {} debris) RegisterBodyFragment(target,debris.WorldId);
            else current.Register(target,"d"+Guid.NewGuid().ToString("N"),true);
        }
        internal static void RegisterBodyFragment(GameObject target,string id)
        {
            if(current==null||!current.IsHost||target==null) return;
            target.transform.SetParent(current.world,true);current.Register(target,id,true);
        }
        static string CorpseId(string name)=>"d"+Digest(Encoding.UTF8.GetBytes(name)).Substring(0,32);
        internal static bool IsCorpseRemoved(string name)=>current!=null&&!string.IsNullOrEmpty(name)&&current.removedCorpseIds.Contains(CorpseId(name));
        internal static void RegisterCorpse(PlayerCorpse corpse)
        {
            if(current==null||corpse==null||corpse.WorldRegistered||current.closing) return;
            corpse.transform.SetParent(current.world,true);
            var item=current.Register(corpse.gameObject,CorpseId(corpse.name),false);
            item.Last=current.Capture(item,false,null);
            if(current.IsHost) current.dirty.Add(item.Id);
            current.SetPhysics(item,item.Last);
        }
        internal static bool TryGetHeldGrip(PickableObject source,out Transform hand,out Vector3 grip)
        {
            hand=source!=null&&source.IsHeld?source.HeldHand:null;
            grip=hand!=null?hand.position:Vector3.zero;
            if(hand!=null) return true;
            if(current==null||!current.IsHost||source==null||
                !current.instances.TryGetValue(source.gameObject.GetInstanceID(),out var item)||
                string.IsNullOrEmpty(item.Owner)||item.Last==null||!item.Last.held) return false;
            grip=item.Last.grip?.Length==3?Vector(item.Last.grip):Vector(item.Last.p);
            return true;
        }
        internal static bool TransferFragmentGrip(PickableObject source,PickableObject fragment,
            Transform hand,Vector3 grip)
        {
            RegisterSpawn(fragment.gameObject);
            if(current!=null&&current.IsHost&&
                current.instances.TryGetValue(source.gameObject.GetInstanceID(),out var original)&&
                current.instances.TryGetValue(fragment.gameObject.GetInstanceID(),out var replacement))
            {
                replacement.Owner=original.Owner;
                replacement.GripSource=original.Id;
                original.GripReplacement=replacement.Id;
                replacement.Last=new ObjectState { held=true,hand=original.Last?.hand??"right",grip=V(grip) };
                if(replacement.Owner==current.self) current.owned.Add(replacement);
                if(hand==null) {
                    var body=fragment.GetComponent<Rigidbody>();
                    body.isKinematic=true;body.useGravity=false;
                    if(source.TryGetComponent<Rigidbody>(out var remainderBody)) {
                        remainderBody.isKinematic=false;remainderBody.useGravity=true;
                    }
                }
                original.Owner="";current.owned.Remove(original);
                if(original.Last!=null) original.Last.held=false;
                current.dirty.Add(original.Id);current.dirty.Add(replacement.Id);
            }
            if(hand==null) return true;
            bool previous=current!=null&&current.transferringGrip;
            if(current!=null) current.transferringGrip=true;
            try {
                var avatar=hand.GetComponentInParent<RiggedPlayerAvatar>();
                if(avatar!=null) return avatar.RetainFragmentFromHeldObject(source,fragment);
                if(!fragment.PickUp(hand,fragment.RequiresTwoHands)) return false;
                source.Release(source.HeldPointVelocity(source.transform.position),hand);
                return true;
            } finally { if(current!=null) current.transferringGrip=previous; }
        }
        internal static void RetireFragmentPhysics(GameObject target)
        {
            if(current==null||target==null||!current.instances.TryGetValue(target.GetInstanceID(),out var item)) return;
            item.Body=null;current.dynamics.Remove(item);current.motion.Remove(item.Id);
        }
        internal int DynamicObjectCount=>dynamics.Count;
        internal int MotionObjectCount=>motion.Count;
        public static void Dirty(GameObject target)
        {
            PlayerWorldLoading.NotifyChanged(target);
            if(current==null||current.applying||!current.IsHost||target==null) return;
            if(!current.instances.TryGetValue(target.GetInstanceID(),out var item)) {
                item=current.Register(target,"d"+Guid.NewGuid().ToString("N"),true);
            }
            current.dirty.Add(item.Id);
            if(target.GetComponent<SmallFragmentRest>() is { IsSettled:true }) RetireFragmentPhysics(target);
            else {
                item.Body=target.GetComponent<Rigidbody>();
                if(item.Body!=null||item.Rope!=null) current.dynamics.Add(item);
            }
            var tree=target.GetComponent<TreeStructuralPart>()?.Tree;
            if(tree!=null&&tree.gameObject!=target&&current.instances.TryGetValue(tree.gameObject.GetInstanceID(),out var tracker)) current.dirty.Add(tracker.Id);
        }
        internal static void RemoveRoomObject(GameObject target)
        {
            if(current!=null&&target!=null&&current.instances.TryGetValue(target.GetInstanceID(),out var item)) {
                bool previous=current.applying;current.applying=true;
                try { current.ReleaseLocal(item); } finally { current.applying=previous; }
            }
            Removed(target);PlayerDamage.Release(target);
        }
        public static void Removed(GameObject target)
        {
            PlayerWorldLoading.Forget(target);
            if(current==null||current.closing||target==null||!current.instances.TryGetValue(target.GetInstanceID(),out var item)) return;
            if(item.Corpse!=null) current.removedCorpseIds.Add(item.Id);
            if(current.IsHost&&!current.applying) current.removed[item.Id]=new ObjectState { id=item.Id,deleted=true,writer=current.self,gripSource=item.GripSource,gripReplacement=item.GripReplacement };
            current.instances.Remove(target.GetInstanceID());current.items.Remove(item.Id);current.dynamics.Remove(item);
            current.owned.Remove(item);
            current.pendingClaims.Remove(item);
            current.dirty.Remove(item.Id);current.motion.Remove(item.Id);
            if(item.Materials!=null) foreach(var material in item.Materials) ReleaseNetworkAsset(material);
        }
        public static bool TryClaim(PickableObject pickup)
        {
            if(pickup!=null&&pickup.NpcOwner!=null) return current==null||current.IsHost||current.applying;
            if(current==null) return true;
            if(pickup==null||!current.ready||!CanInteract(pickup.gameObject)||!current.instances.TryGetValue(pickup.gameObject.GetInstanceID(),out var item)) return false;
            item.OutgoingRelease=null;
            if(item.Pending) { item.PendingRelease=null;return true; }
            if(item.Owner==current.self) return true;
            if(!item.Pending) {
                item.ClaimSnapshot=current.Capture(item,false,null);
                item.Pending=true;current.pendingClaims.Add(item);
                // A reach has already selected this object's local pose. Stop
                // interpolating an older room sample while the hand travels.
                current.motion.Remove(item.Id);
                current.RequestAction(new Request { action="claim",@object=item.Id,state=item.ClaimSnapshot });
            }
            // Show an eligible local grip immediately. Only confirmed ownership
            // may publish it; a declined claim restores the authoritative pose.
            return true;
        }

        void RejectClaim(Item item)
        {
            item.Pending=false;item.PendingRelease=null;item.GrabbedPending=false;pendingClaims.Remove(item);owned.Remove(item);
            item.ClaimRejected=true;item.ClaimRejectedAt=Time.unscaledTime;
            ReleaseLocal(item);
            var snapshot=item.ClaimSnapshot;item.ClaimSnapshot=null;
            if(snapshot!=null&&item.Target!=null) {
                item.Last=null;
                Apply(snapshot,false);
                item.ClaimRejected=true;
                item.ClaimRejectedAt=Time.unscaledTime;
            }
        }
        public static void Released(PickableObject pickup,Vector3 velocity)
        {
            if(current==null||current.applying||current.transferringGrip||pickup==null||!current.instances.TryGetValue(pickup.gameObject.GetInstanceID(),out var item)) return;
            item.GripSource="";
            item.GripReplacement="";
            var state=current.Capture(item,false,null);state.v=Velocity(velocity);state.held=false;state.kinematic=pickup.Corpse!=null;state.body=true;state.gravity=pickup.Corpse==null;state.sleeping=false;
            // A regrab before the claim reply cancels this provisional release.
            // Otherwise send it as soon as ownership is actually granted.
            if(item.Pending) { item.PendingRelease=state;item.GrabbedPending=true;return; }
            current.SendRelease(item,state);
        }
        void SendRelease(Item item,ObjectState state)
        {
            state.releaseId=Guid.NewGuid().ToString("N");item.ReleaseId=state.releaseId;
            item.OutgoingRelease=state;item.ReleaseRevision=0;
            item.Owner="";owned.Remove(item);
            state.holder="";
            state.revision=item.Last!=null?item.Last.revision:0;
            item.Last=state;
            RequestAction(new Request { action="release",@object=item.Id,state=state });
            SetPhysics(item,state);
            Dirty(item.Target);
        }
        void CancelReservation(Item item)
        {
            item.Owner="";owned.Remove(item);
            RequestAction(new Request { action="unclaim",@object=item.Id });
            // A reach that never grabbed the object must preserve its original
            // physics and support, rather than manufacturing a dropped body.
            if(item.Last!=null) { item.Last.holder="";SetPhysics(item,item.Last); }
        }
        public static void SpawnCombatNpc(Vector3 position,string identity=null)
        {
            if(!float.IsFinite(position.sqrMagnitude)) return;
            if(current!=null&&!current.IsHost) {
                if(current.ready) current.RequestAction(new Request { action="command",command=new Command { type="spawn-npc",point=V(position),incoming=V(Vector3.zero) } });
                return;
            }
            var map=UnityEngine.Object.FindFirstObjectByType<ProceduralMap>();
            if(map!=null) position.y=map.HeightAt(position.x,position.z)+.15f;
            string npcId="d"+Digest(Encoding.UTF8.GetBytes(identity??Guid.NewGuid().ToString("N"))).Substring(0,32);
            if(ObjectOf(npcId)!=null) return;
            var npc=CombatNpc.Create(current!=null?current.world:UnityEngine.Object.FindFirstObjectByType<FirstPersonController>()?.transform.parent,position,equip:false);
            if(current!=null) current.Register(npc.gameObject,npcId,true);
            npc.SendMessage("Equip");Dirty(npc.gameObject);
        }
        public static bool ForwardStrike(PhysicalDestructible source,PhysicalDestructible target,Vector3 point,Vector3 incoming,float strikeRoll=-1f)
        {
            if(current==null||current.IsHost||current.applying) return false;
            if(current.ready&&source!=null&&target!=null) current.SendCommand("strike",source.gameObject,target.gameObject,point,incoming,0,strikeRoll);
            return true;
        }
        internal static bool ForwardCorpseImpact(PhysicalDestructible source,PlayerCorpse target,Vector3 point,Vector3 incoming)
        {
            if(current==null||current.IsHost||current.applying) return false;
            if(current.ready&&source!=null&&target!=null) current.SendCommand("strike",source.gameObject,target.gameObject,point,incoming,0);
            return true;
        }
        public static bool ForwardDrag(PickableObject pickup,Vector3 position,int hands)
        {
            if(current==null||current.IsHost||current.applying) return false;
            if(current.ready&&CanInteract(pickup.gameObject)) current.SendCommand("drag",pickup.gameObject,null,position,Vector3.zero,hands);
            return true;
        }
        public static bool ForwardGolden(PickableObject pickup,Camera camera)
        {
            if(current==null||current.IsHost||current.applying) return false;
            if(current.ready) current.SendCommand("golden",pickup.gameObject,null,camera.transform.position,camera.transform.forward,0);
            return true;
        }
        void SendCommand(string type,GameObject source,GameObject target,Vector3 point,Vector3 incoming,int hands,float strikeRoll=-1f)
        {
            string id=IdOf(source);if(string.IsNullOrEmpty(id)) return;
            string key=type+id+IdOf(target);
            if(lastCommand.TryGetValue(key,out float last)&&Time.unscaledTime-last<.15f) return;
            lastCommand[key]=Time.unscaledTime;
            RequestAction(new Request { action="command",command=new Command { type=type,@object=id,target=IdOf(target),point=V(point),incoming=Velocity(incoming),hands=hands,hasStrikeRoll=strikeRoll>=0f,strikeRoll=strikeRoll } });
        }
        void RequestAction(Request request)
        {
#if UNITY_EDITOR
            RequestForValidation?.Invoke(request);
#endif
#if UNITY_WEBGL && !UNITY_EDITOR
            WyvernlandWorldRequest(JsonUtility.ToJson(request));
#endif
        }
        static float[] V(Vector3 value) => new[]{value.x,value.y,value.z};
        static float[] Velocity(Vector3 value) => V(float.IsFinite(value.x)&&float.IsFinite(value.y)&&float.IsFinite(value.z)?
            Vector3.ClampMagnitude(value,1000f):Vector3.zero);
        static float[] Q(Quaternion value) { value=value.normalized;return new[]{value.x,value.y,value.z,value.w}; }
        internal static Vector3 Vector(float[] value) => value?.Length==3?new Vector3(value[0],value[1],value[2]):Vector3.zero;
        internal static Quaternion Rotation(float[] value) => value?.Length==4?new Quaternion(value[0],value[1],value[2],value[3]).normalized:Quaternion.identity;
        internal static string Digest(byte[] value) { using var hash=SHA256.Create();return BitConverter.ToString(hash.ComputeHash(value)).Replace("-","").ToLowerInvariant(); }
        ObjectState Capture(Item item,bool captureMesh,List<MeshData> meshes)
        {
            CapturedStateCount++;
            if(item.Target==null) return new ObjectState { id=item.Id,deleted=true,writer=self,gripSource=item.GripSource,gripReplacement=item.GripReplacement };
            var target=item.Target;var properties=target.GetComponent<PhysicalDestructible>();
            if(properties!=null&&properties.HasBeenDestroyed) return new ObjectState { id=item.Id,deleted=true,writer=self,gripSource=item.GripSource,gripReplacement=item.GripReplacement };
            var body=target.GetComponent<Rigidbody>();var pickup=target.GetComponent<PickableObject>();var rope=target.GetComponent<RopeObject>();
            var fragmentRest=target.GetComponent<SmallFragmentRest>();
            bool fragmentSettled=fragmentRest!=null&&fragmentRest.IsSettled;
            if(fragmentSettled) body=null;
            item.Body=body;item.Pickup=pickup;item.Rope=rope;
            // Guest Rigidbodies are kinematic only to avoid duplicate simulation.
            // That local replica setting must never become saved world physics.
            var replica=!fragmentSettled&&!IsHost&&(pickup==null||!pickup.IsHeld)?item.Last:null;
            bool held=pickup!=null&&pickup.IsHeld||!string.IsNullOrEmpty(item.Owner)&&
                item.Owner!=self&&item.Last!=null&&item.Last.held;
            if(body!=null||rope!=null) dynamics.Add(item);
            string parent=target.transform.parent==item.OriginalParent?"@":IdOf(target.transform.parent!=null?target.transform.parent.gameObject:null);
            var state=new ObjectState { id=item.Id,name=target.name,parent=parent,writer=self,holder=item.Owner,mesh=item.Mesh,
                releaseId=item.ReleaseId,gripSource=item.GripSource,gripReplacement=item.GripReplacement,grip=pickup!=null&&pickup.HeldHand!=null?V(pickup.HeldHand.position):item.Last?.grip,
                p=V(target.transform.position),q=Q(target.transform.rotation),s=V(target.transform.lossyScale),
                v=replica!=null?replica.v:Velocity(pickup!=null&&pickup.IsHeld?pickup.HeldPointVelocity(target.transform.position):body!=null?body.linearVelocity:Vector3.zero),
                w=replica!=null?replica.w:Velocity(pickup!=null&&pickup.IsHeld?pickup.HeldAngularVelocity:body!=null?body.angularVelocity:Vector3.zero),body=replica!=null?replica.body:body!=null,
                kinematic=replica!=null?replica.kinematic:body!=null&&body.isKinematic,
                gravity=replica!=null?replica.gravity:body!=null&&body.useGravity,mass=body!=null?body.mass:1,held=held,
                sleeping=replica!=null?replica.sleeping:body!=null&&!body.isKinematic&&body.IsSleeping(),
                bodyFragment=target.GetComponent<BodyFragment>()!=null,regularGrip=pickup!=null&&pickup.ForceRegularGrip,testBar=target.GetComponent<TestGroundIronBar>()!=null,pickable=pickup!=null,pickableName=pickup?.DisplayName,label=pickup!=null?(int)pickup.Label:0,hand="right",expires=item.Expires,
                hasPhysical=properties!=null,hasTree=target.GetComponent<TreeStructuralPart>()!=null,
                smallFragment=fragmentRest!=null,shatterDebris=target.GetComponent<TemporaryShatterDebris>()!=null,fragmentSettled=fragmentSettled,
                hasHistory=target.GetComponent<GeneratedTree>()!=null,hasRope=rope!=null,
                hasNpc=target.GetComponent<CombatNpc>()!=null,
                npcGrip=pickup!=null&&pickup.IsHeld&&pickup.NpcOwner!=null?IdOf(pickup.NpcOwner.gameObject):null,
                npcLocalP=pickup!=null&&pickup.IsHeld&&pickup.NpcOwner!=null?V(pickup.transform.localPosition):null,
                npcLocalQ=pickup!=null&&pickup.IsHeld&&pickup.NpcOwner!=null?Q(pickup.transform.localRotation):null,
                hasDummy=item.Dummy!=null,dummy=item.Dummy!=null?item.Dummy.Capture():null,
                hasCorpse=item.Corpse!=null,corpse=item.Corpse!=null?item.Corpse.Capture():null,
                physical=properties?.CaptureNetworkProperties(),tree=target.GetComponent<TreeStructuralPart>()?.CaptureNetworkStructure(),
                history=target.GetComponent<GeneratedTree>()?.CaptureNetworkHistory(),rope=rope?.CaptureNetworkRope() };
            if(!string.IsNullOrEmpty(state.npcGrip)) { state.held=false;state.holder="";state.kinematic=true;state.gravity=false; }
            if(pickup!=null&&pickup.HeldHand!=null&&pickup.HeldHand.name.IndexOf("left",StringComparison.OrdinalIgnoreCase)>=0) state.hand="left";
            if(captureMesh&&item.Geometry&&rope==null) {
                Mesh mesh=null;Material[] materials=null;
                if(properties!=null) mesh=properties.CaptureNetworkMesh(out materials);
                else { var filter=target.GetComponent<MeshFilter>();var renderer=target.GetComponent<Renderer>();
                    mesh=filter!=null?filter.sharedMesh:null;materials=renderer!=null?renderer.sharedMaterials:null; }
                if(mesh!=null&&mesh.vertexCount>0) { var data=SharedMeshCodec.Encode(mesh,materials);state.mesh=item.Mesh=data.hash;meshes?.Add(data); }
                if(properties!=null&&mesh!=null) ReleaseNetworkAsset(mesh);
                item.Geometry=false;
            }
            return state;
        }
        static bool RepairLoosePhysics(ObjectState state)
        {
            // Older room saves could capture a temporary network freeze as the
            // permanent physics of a loose stone or detached tree fragment.
            if(!string.IsNullOrEmpty(state.npcGrip)) return false;
            bool orphanedGrip=state.held&&string.IsNullOrEmpty(state.holder);
            if(!state.body||!state.pickable||!state.kinematic&&!orphanedGrip||state.held&&!orphanedGrip||state.hasRope||state.hasCorpse||
                state.hasTree&&(state.tree==null||!state.tree.falling)) return false;
            state.held=false;state.kinematic=false;state.gravity=true;state.sleeping=false;return true;
        }
        void SetPhysics(Item item,ObjectState state)
        {
            if(item.Target==null||state==null) return;
            if(item.Dummy!=null) { item.Dummy.SetAuthority(IsHost);return; }
            if(!string.IsNullOrEmpty(state.npcGrip)) {
                var heldBody=item.Target.GetComponent<Rigidbody>();if(heldBody!=null) { heldBody.isKinematic=true;heldBody.useGravity=false; }
                return;
            }
            if(item.Corpse!=null) { bool simulateCorpse=SimulateRope(item,state.held);item.Corpse.SetReplica(!simulateCorpse);if(simulateCorpse) item.Corpse.ApplyMotion(state.corpse?.motion);return; }
            if(item.Pending&&(item.PendingRelease!=null||item.Pickup!=null&&item.Pickup.IsHeld)) return;
            if(item.Pickup!=null&&item.Pickup.IsHeld&&item.Owner==self) return;
            if(state.hasNpc||state.fragmentSettled||item.Target.GetComponent<SmallFragmentRest>() is { IsSettled:true })
            {
                SmallFragmentRest.Ensure(item.Target).MakeStatic(false,
                    item.ColliderGeometryChanged||item.Target.GetComponent<Rigidbody>()!=null);
                item.ColliderGeometryChanged=false;
                item.Body=null;dynamics.Remove(item);motion.Remove(item.Id);return;
            }
            RepairLoosePhysics(state);
            var rope=item.Target.GetComponent<RopeObject>();
            if(rope!=null) { bool ropeSimulation=SimulateRope(item,state.held);
                rope.SetNetworkPhysics(ropeSimulation);if(ropeSimulation&&state.hasRope) rope.ApplyNetworkRope(state.rope);return; }
            var body=item.Target.GetComponent<Rigidbody>();
            if(!state.body&&string.IsNullOrEmpty(item.Owner)&&body!=null) {
                body.isKinematic=true;ReleaseNetworkAsset(body);return;
            }
            if(body==null&&state.body) body=item.Target.AddComponent<Rigidbody>();
            item.Body=body;
            if(body==null) { RefreshColliders(item);return; }
            bool simulate=(IsHost||item.OutgoingRelease!=null)&&!state.held;
            body.isKinematic=!simulate||state.kinematic;
            body.useGravity=state.gravity;body.mass=Mathf.Clamp(state.mass,.001f,1e9f);
            if(!body.isKinematic) { body.linearVelocity=Vector(state.v);body.angularVelocity=Vector(state.w); }
            RefreshColliders(item);
            if(!body.isKinematic&&state.sleeping) body.Sleep();
            dynamics.Add(item);
        }
        void RefreshColliders(Item item)
        {
            var assembly=item.Target.GetComponentInParent<Rigidbody>();int id=assembly!=null?assembly.GetInstanceID():0;
            if(id==item.ColliderBodyId&&!item.ColliderGeometryChanged) return;
            item.ColliderBodyId=id;item.ColliderGeometryChanged=false;ColliderRefreshCount++;
            item.Target.GetComponent<PhysicalDestructible>()?.RefreshNetworkColliders();
        }
        [UnityEngine.Scripting.Preserve] public void ReceiveWorld(string json)
        {
            Packet packet;
            try { packet=JsonUtility.FromJson<Packet>(json); } catch(Exception exception) { Debug.LogException(exception);return; }
            if(packet==null) return;
            bool wasHost=IsHost,wasReady=ready;
            if(self!=(packet.self??"")) {
                applying=true;
                try { foreach(var pending in pendingClaims.ToArray()) RejectClaim(pending); }
                finally { applying=false; }
                owned.Clear();foreach(var item in items.Values) item.OutgoingRelease=null;
            }
            self=packet.self??"";host=packet.host??"";ready=packet.ready;error=packet.error;
            if(packet.playerCount>0) roomPlayerCount=Mathf.Clamp(packet.playerCount,1,128);
            if(wasHost&&!IsHost) { executed.Clear();acks.Clear(); }
            if(packet.meshes!=null) foreach(var mesh in packet.meshes) geometry[mesh.hash]=mesh.data;
            applying=true;
            try {
                if(packet.releases!=null) foreach(var ack in packet.releases) if(items.TryGetValue(ack.id,out var releasedItem)&&releasedItem.OutgoingRelease?.releaseId==ack.releaseId) {
                    releasedItem.ReleaseRevision=ack.revision;
                    if(!ack.accepted) { releasedItem.OutgoingRelease=null;SetPhysics(releasedItem,releasedItem.Last); }
                }
                if(packet.claims!=null) foreach(var claim in packet.claims) if(items.TryGetValue(claim.id,out var item)) {
                    if(!item.Pending) continue;
                    if(!claim.owned) { RejectClaim(item);continue; }
                    var released=item.PendingRelease;item.PendingRelease=null;
                    item.Pending=false;pendingClaims.Remove(item);item.ClaimSnapshot=null;
                    item.ClaimRejected=false;item.Owner=self;owned.Add(item);item.Granted=Time.unscaledTime;
                    if(item.Pickup!=null&&(item.Pickup.IsHeld||item.GrabbedPending)) {
                        applying=false;
                        try { item.Target.GetComponent<TreeStructuralPart>()?.OnPickedUp();Dirty(item.Target); }
                        finally { applying=true; }
                    }
                    item.GrabbedPending=false;
                    if(released!=null) SendRelease(item,released);
                }
                if(packet.objects!=null) {
                    // Allocate all fragment and container identities before restoring parents.
                    foreach(var state in packet.objects) if(state.deleted&&state.id!=null&&state.id.StartsWith("d",StringComparison.Ordinal)) removedCorpseIds.Add(state.id);
                    foreach(var state in packet.objects) if(!state.deleted&&!removedCorpseIds.Contains(state.id)&&!items.ContainsKey(state.id)&&state.id.StartsWith("d",StringComparison.Ordinal)) {
                        var target=state.hasNpc?CombatNpc.Create(world,Vector(state.p),equip:false).gameObject:
                            state.hasCorpse&&state.corpse!=null?PlayerCorpse.Create(state.corpse,world).gameObject:new GameObject(state.name??"Shared fragment");
                        target.transform.SetParent(world,false);Register(target,state.id,false);
                    }
                    bool authoritative=wasReady&&wasHost&&IsHost;
                    // Install replacement geometry and transfer local hands before
                    // the source's release/deletion can clear the avatar's grip.
                    foreach(var state in packet.objects)
                        if(!state.deleted&&!string.IsNullOrEmpty(state.gripSource)) Apply(state,authoritative);
                    foreach(var state in packet.objects) {
                        if(state.deleted||!state.held||state.holder!=self||string.IsNullOrEmpty(state.gripSource)) continue;
                        string originalId=state.gripSource;
                        var original=ObjectOf(originalId)?.GetComponent<PickableObject>();
                        // Several fractures can finish before the next room poll.
                        // Follow deleted intermediate pieces to the existing grip.
                        for(int depth=0;depth<32&&(original==null||!original.IsHeld);depth++) {
                            var ancestor=packet.objects.FirstOrDefault(s=>s.id==originalId);
                            if(ancestor==null&&deferredGrips.TryGetValue(originalId,out var pending)) ancestor=pending.State;
                            if(string.IsNullOrEmpty(ancestor?.gripSource)||ancestor.gripSource==originalId) break;
                            originalId=ancestor.gripSource;original=ObjectOf(originalId)?.GetComponent<PickableObject>();
                        }
                        var replacement=ObjectOf(state.id)?.GetComponent<PickableObject>();
                        if(original==null||!original.IsHeld||replacement==null||replacement.IsHeld) continue;
                        var hand=original.HeldHand;
                        var avatar=hand!=null?hand.GetComponentInParent<RiggedPlayerAvatar>():null;
                        if(avatar!=null) avatar.RetainFragmentFromHeldObject(original,replacement);
                        else if(hand!=null&&replacement.PickUp(hand,replacement.RequiresTwoHands)) original.Release(Vector3.zero,hand);
                    }
                    foreach(var state in packet.objects)
                        if(state.deleted||string.IsNullOrEmpty(state.gripSource)) Apply(state,authoritative);
                    RestoreDeferredGrips();
                }
                if(!(wasReady&&wasHost&&IsHost)) RestoreReferences();
                if(wasHost!=IsHost||wasReady!=ready) foreach(var item in dynamics) {
                    if(item.Target!=null) SetPhysics(item,item.Last);
                }
            } catch(Exception exception) { Debug.LogException(exception);ready=false;error="Could not restore the shared world. Reload to rejoin."; }
            finally { applying=false; }
            if(IsHost&&packet.commands!=null) foreach(var command in packet.commands) {
                if(executed.Add(command.commandId)) Execute(command);
                acks.Add(command.commandId);
            }
            if(!ready&&!pausedForSync) { pausedForSync=true;player?.SetPaused(true); }
            else if(ready&&pausedForSync) { pausedForSync=false;player?.SetPaused(false); }
#if UNITY_WEBGL && !UNITY_EDITOR
            WyvernlandSetHosting(IsHost?1:0);
#endif
        }
        void Apply(ObjectState state,bool authoritative)
        {
            if(!items.TryGetValue(state.id,out var item)||item.Target==null) return;
            if(state.revision>0&&item.Last!=null&&state.revision<=item.Last.revision) return;
            bool newRelease=!string.IsNullOrEmpty(state.releaseId)&&state.releaseId!=item.ReleaseId&&!state.held;
            if(item.OutgoingRelease!=null&&!state.deleted) {
                // Older hand snapshots cannot revoke an already displayed throw.
                if(state.releaseId!=item.OutgoingRelease.releaseId) return;
                // A guest predicts flight until the host has actually simulated
                // this release, rather than freezing at the release HTTP echo.
                if(!IsHost&&(state.writer!=host||state.revision<=item.ReleaseRevision)) return;
                item.OutgoingRelease=null;
            }
            PlayerWorldLoading.BeforeChange(item.Target);
            if(!string.IsNullOrEmpty(state.gripReplacement)&&
                (state.deleted||state.holder!=self)&&item.Target.GetComponent<PickableObject>() is { IsHeld:true }&&
                ObjectOf(state.gripReplacement)?.GetComponent<PickableObject>() is not { IsHeld:true }) {
                deferredGrips[state.id]=(state,Time.unscaledTime+5f);return;
            }
            deferredGrips.Remove(state.id);
            if(string.IsNullOrEmpty(state.npcGrip)&&item.Target.GetComponent<PickableObject>() is { IsHeld:true,NpcOwner:not null }) ReleaseLocal(item);
            if(state.expires>0&&state.physical is { fragment:true })
            { state.smallFragment=true;state.shatterDebris=true; }
            if(state.hasCorpse||state.bodyFragment||state.smallFragment||state.physical is { fragment:true }) state.expires=0;
            if(state.deleted||state.expires>0&&state.expires<DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) {
                ResidentPropRendering.Release(item.Target);ReleaseLocal(item);
                if(state.expires>0) removed[item.Id]=new ObjectState { id=item.Id,deleted=true,writer=self };
                ReleaseNetworkAsset(item.Target);return;
            }
            if(item.Dummy!=null&&!state.hasNpc&&state.p!=null&&state.p.Length==3&&(Mathf.Abs(state.p[0]+10)>.001f||Mathf.Abs(state.p[2]+10)>.001f)) {
                var offset=new Vector3(-10-state.p[0],0,-10-state.p[2]);state.p[0]=-10;state.p[2]=-10;
                var pose=state.dummy?.damage?.pose;
                if(pose!=null) for(int i=0;i+6<pose.Length;i+=7) { pose[i]+=offset.x;pose[i+2]+=offset.z; }
            }
            bool repaired=RepairLoosePhysics(state);
            if(authoritative&&state.writer==self&&(state.holder??"")==item.Owner) {
                item.ClaimRejected=false;
                if(repaired) SetPhysics(item,state);
                if(item.Last!=null) item.Last.revision=state.revision;return;
            }
            string owner=state.holder??"";
            bool locallyHeld=item.Target.GetComponent<PickableObject>() is { IsHeld:true };
            if(item.Pending&&string.IsNullOrEmpty(owner)) {
                // A read sent before the claim can arrive during the reach or
                // after hand contact. Keep it for rollback without moving the
                // selected object away from the hand.
                item.ClaimSnapshot=state;item.Last=state;return;
            }
            if(item.Pending&&!string.IsNullOrEmpty(owner)&&owner!=self) {
                item.Pending=false;pendingClaims.Remove(item);item.ClaimSnapshot=null;item.PendingRelease=null;item.GrabbedPending=false;
            }
            item.ClaimRejected=false;
            if(locallyHeld&&owner!=self) ReleaseLocal(item);
            if(IsHost&&state.held&&!string.IsNullOrEmpty(owner)&&
                (owner!=item.Owner||item.Last==null||!item.Last.held)) {
                // A guest pickup is an authoritative tree interaction. Include
                // every attached part whose support state changes with it.
                applying=false;
                try { item.Target.GetComponent<TreeStructuralPart>()?.OnPickedUp(); }
                finally { applying=true; }
                dirty.Add(item.Id);
            }
            item.Owner=owner;item.ReleaseId=state.releaseId??"";
            item.GripSource=state.gripSource??"";
            item.GripReplacement=state.gripReplacement??"";
            if(owner==self&&!string.IsNullOrEmpty(self)) owned.Add(item);else owned.Remove(item);
            if(!string.IsNullOrEmpty(owner)&&item.Target.GetComponent<CorpseGrip>() is {} corpseGrip) { corpseGrip.Detach();PlayerDamage.Release(corpseGrip); }
            if(state.held) state.body=true;
            if(!authoritative) {
                if(state.body&&item.Target.GetComponent<Rigidbody>()==null) item.Target.AddComponent<Rigidbody>().isKinematic=true;
                if(state.hasPhysical&&state.physical!=null) {
                    var properties=item.Target.GetComponent<PhysicalDestructible>()??item.Target.AddComponent<PhysicalDestructible>();
                    string signature=JsonUtility.ToJson(state.physical);
                    if(signature!=item.PhysicalJson) { properties.ApplyNetworkProperties(state.physical);item.PhysicalJson=signature; }
                }
                if(state.hasHistory&&state.history!=null) (item.Target.GetComponent<GeneratedTree>()??item.Target.AddComponent<GeneratedTree>()).ApplyNetworkHistory(state.history);
                if(state.bodyFragment) (item.Target.GetComponent<BodyFragment>()??item.Target.AddComponent<BodyFragment>()).WorldId=state.id;
                if(state.testBar&&item.Target.GetComponent<TestGroundIronBar>()==null) item.Target.AddComponent<TestGroundIronBar>();
                if(state.pickable) {
                    var pickup=item.Target.GetComponent<PickableObject>()??item.Target.AddComponent<PickableObject>();
                    pickup.DisplayName=state.pickableName;pickup.ForceRegularGrip=state.regularGrip;pickup.Label=(PickableObjectLabel)state.label;
                }
                if(!string.IsNullOrEmpty(state.mesh)&&state.mesh!=item.Mesh) {
                    if(!geometry.TryGetValue(state.mesh,out string data)) throw new InvalidOperationException("Shared geometry is missing");
                    var mesh=SharedMeshCodec.Decode(new MeshData { hash=state.mesh,data=data },out var materials);
                    var properties=item.Target.GetComponent<PhysicalDestructible>()??item.Target.AddComponent<PhysicalDestructible>();
                    properties.ApplyNetworkMesh(mesh,materials);item.Mesh=state.mesh;
                    item.ColliderGeometryChanged=true;
                    if(item.Materials!=null) foreach(var material in item.Materials) ReleaseNetworkAsset(material);item.Materials=materials;
                }
                if(state.hasTree&&state.tree!=null) (item.Target.GetComponent<TreeStructuralPart>()??item.Target.AddComponent<TreeStructuralPart>()).ApplyNetworkStructure(state.tree);
            }
            if(!(locallyHeld&&owner==self)) {
                ResidentPropRendering.Release(item.Target);
                Transform parent=null;
                if(!string.IsNullOrEmpty(owner)) parent=world;
                else if(state.parent=="@") parent=item.OriginalParent;
                else parent=ObjectOf(state.parent)?.transform??world;
                if(parent==item.Target.transform||parent!=null&&parent.IsChildOf(item.Target.transform)) parent=world;
                item.Target.transform.SetParent(parent,true);
                if(state.hasNpc||state.fragmentSettled||item.Last==null||IsHost&&newRelease||Vector3.Distance(item.Target.transform.position,Vector(state.p))>10f||!ready) item.Target.transform.SetPositionAndRotation(Vector(state.p),Rotation(state.q));
                if(IsHost&&newRelease&&item.Body!=null) {
                    item.Body.position=Vector(state.p);item.Body.rotation=Rotation(state.q);
                }
                Vector3 scale=Vector(state.s),parentScale=parent!=null?parent.lossyScale:Vector3.one;
                item.Target.transform.localScale=new Vector3(scale.x/Mathf.Max(.00001f,parentScale.x),scale.y/Mathf.Max(.00001f,parentScale.y),scale.z/Mathf.Max(.00001f,parentScale.z));
                SetPhysics(item,state);
                if(state.hasNpc||state.fragmentSettled||item.Target.GetComponent<SmallFragmentRest>() is { IsSettled:true }) motion.Remove(item.Id);
                else motion.Add(item.Id);
                if(state.hasRope) item.Target.GetComponent<RopeObject>()?.ApplyNetworkRope(state.rope);
                if(state.hasDummy&&!authoritative) item.Dummy?.Apply(state.dummy);
                if(state.hasCorpse&&(!authoritative||!string.IsNullOrEmpty(owner))) item.Corpse?.Apply(state.corpse);
                item.Target.GetComponent<PickableObject>()?.TrackMovement();
            }
            if(!string.IsNullOrEmpty(state.npcGrip)&&ObjectOf(state.npcGrip)?.GetComponent<CombatNpc>() is {} npc&&
                item.Target.GetComponent<PickableObject>() is {} npcWeapon)
                npc.BindWeapon(npcWeapon,Vector(state.npcLocalP),Rotation(state.npcLocalQ));
            if(state.smallFragment||state.physical is { fragment:true }&&
                item.Target.GetComponent<PhysicalDestructible>() is {} fragmentPhysical&&
                fragmentPhysical.SolidVolumeCubicMetres<PhysicalDestructible.MinimumFragmentationVolume)
                SmallFragmentRest.Ensure(item.Target);
            if(state.shatterDebris&&item.Target.GetComponent<TemporaryShatterDebris>()==null)
                item.Target.AddComponent<TemporaryShatterDebris>();
            if(state.fragmentSettled) SmallFragmentRest.Ensure(item.Target).MakeStatic(false);
            item.Expires=state.expires;item.Last=state;
            PlayerWorldLoading.NotifyChanged(item.Target);
            if(!authoritative) deferredRelations.Add(item.Id);
        }
        void RestoreReferences()
        {
            foreach(string id in deferredRelations.ToArray()) {
                if(!items.TryGetValue(id,out var item)||item.Target==null||item.Last==null) { deferredRelations.Remove(id);continue; }
                var state=item.Last;bool waiting=false;
                if(string.IsNullOrEmpty(item.Owner)&&state.parent!="@"&&!string.IsNullOrEmpty(state.parent)) {
                    var parent=ObjectOf(state.parent);
                    if(parent==null) waiting=true;
                    else if(parent.transform!=item.Target.transform&&!parent.transform.IsChildOf(item.Target.transform)) item.Target.transform.SetParent(parent.transform,true);
                }
                if(state.hasTree&&state.tree!=null) {
                    if(!string.IsNullOrEmpty(state.tree.parent)&&ObjectOf(state.tree.parent)==null) waiting=true;
                    if(!string.IsNullOrEmpty(state.tree.tree)&&ObjectOf(state.tree.tree)?.GetComponent<GeneratedTree>()==null) waiting=true;
                    item.Target.GetComponent<TreeStructuralPart>()?.ApplyNetworkStructure(state.tree);
                }
                if(state.hasRope&&state.rope!=null&&state.rope.attached) {
                    if(ObjectOf(state.rope.support)==null) waiting=true;
                    else item.Target.GetComponent<RopeObject>()?.ApplyNetworkRope(state.rope);
                }
                RefreshColliders(item);
                if(!waiting) deferredRelations.Remove(id);
            }
        }
        void RestoreDeferredGrips()
        {
            foreach(var pair in deferredGrips.ToArray()) {
                var source=ObjectOf(pair.Key)?.GetComponent<PickableObject>();
                if(source==null) { deferredGrips.Remove(pair.Key);continue; }
                if(source.IsHeld&&Time.unscaledTime<pair.Value.Until) continue;
                deferredGrips.Remove(pair.Key);
                var state=pair.Value.State;state.gripReplacement="";
                Apply(state,false);
            }
        }
        void ReleaseLocal(Item item)
        {
            var pickup=item.Target.GetComponent<PickableObject>();
            if(pickup==null) return;
            var avatar=pickup.HeldHand!=null?pickup.HeldHand.GetComponentInParent<RiggedPlayerAvatar>():player?.GetComponent<RiggedPlayerAvatar>();
            if(avatar!=null&&avatar.PickupTarget==pickup) avatar.CancelRightHandPickup();
            if(!pickup.IsHeld) return;
            if(avatar!=null&&avatar.HeldRightHandItem==pickup) avatar.ReleaseRightHandItem(Vector3.zero,false);
            else if(avatar!=null&&avatar.HeldLeftHandItem==pickup) avatar.ReleaseLeftHandItem(Vector3.zero);
            else pickup.Release(Vector3.zero,player!=null?player.transform:transform);
        }
        void Execute(Command command)
        {
            if(command.type=="spawn-npc") { SpawnCombatNpc(Vector(command.point),command.commandId);return; }
            var source=ObjectOf(command.@object);if(source==null) return;
            PlayerWorldLoading.BeforeChange(source);
            PlayerWorldLoading.BeforeChange(ObjectOf(command.target));
            if(command.type=="strike") {
                var target=ObjectOf(command.target);
                if(target?.GetComponent<PlayerCorpse>() is {} corpse)
                    corpse.TryShatterFromImpact(source.GetComponent<PhysicalDestructible>(),Vector(command.point),Vector(command.incoming));
                else source.GetComponent<PhysicalDestructible>()?.ResolveHeldStrikeDestruction(target?.GetComponent<PhysicalDestructible>(),Vector(command.point),Vector(command.incoming),command.hasStrikeRoll?command.strikeRoll:-1f);
            } else if(command.type=="drag") source.GetComponent<PickableObject>()?.DragToward(Vector(command.point),Mathf.Clamp(command.hands,1,2),.15f);
            else if(command.type=="golden") {
                var reference=new GameObject("Shared launch direction");var camera=reference.AddComponent<Camera>();camera.enabled=false;
                reference.transform.position=Vector(command.point);reference.transform.rotation=Quaternion.LookRotation(Vector(command.incoming).normalized);
                source.GetComponent<PickableObject>()?.LaunchGoldenStoneCopy(camera,null,true);ReleaseNetworkAsset(reference);
            }
            Dirty(source);
        }
        void LateUpdate()
        {
            if(closing) return;
            if(deferredGrips.Count>0) RestoreDeferredGrips();
            if(ready&&!WorldLoading.IsLoading) Time.timeScale=1f;
            foreach(string id in motion.ToArray()) {
                if(!items.TryGetValue(id,out var item)||item.Target==null||item.Last==null) { motion.Remove(id);continue; }
                if(item.OutgoingRelease!=null||IsHost&&!item.Last.held||item.Pending||
                    (item.Owner==self||item.Pending)&&item.Target.GetComponent<PickableObject>() is { IsHeld:true }) { motion.Remove(id);continue; }
                if(item.Corpse!=null) { motion.Remove(id);continue; }
                var position=Vector(item.Last.p);var rotation=Rotation(item.Last.q);
                float blend=1f-Mathf.Exp(-20f*Time.unscaledDeltaTime);
                item.Target.transform.SetPositionAndRotation(Vector3.Lerp(item.Target.transform.position,position,blend),Quaternion.Slerp(item.Target.transform.rotation,rotation,blend));
                item.Target.GetComponent<PickableObject>()?.TrackMovement();
                if((item.Target.transform.position-position).sqrMagnitude<.00001f) { item.Target.transform.SetPositionAndRotation(position,rotation);motion.Remove(id); }
            }
            if(!ready||Time.unscaledTime<nextSend) return;
            nextSend=Time.unscaledTime+.15f;
            MaintainOwnedObjects(Time.unscaledTime);
            if(!IsHost) return;
            LastMotionProbeCount=0;
            foreach(var item in dynamics) {
                if(item.Target==null) { Removed(item.Target);continue; }
                if(item.Pending) continue;
                if(!string.IsNullOrEmpty(item.Owner)&&item.Owner!=self&&item.Last!=null&&item.Last.held) continue;
                LastMotionProbeCount++;
                if(MotionChanged(item)) dirty.Add(item.Id);
            }
            var states=new List<ObjectState>();var meshData=new List<MeshData>();
            // Publish replacement identities before removal records, even when
            // the 64-object native message is split into smaller HTTP batches.
            foreach(string id in dirty.Where(id=>items.TryGetValue(id,out var item)&&
                !string.IsNullOrEmpty(item.GripSource)&&!string.IsNullOrEmpty(item.Owner)).Take(64).ToArray()) {
                if(!items.TryGetValue(id,out var item)||item.Pending) continue;
                dirty.Remove(id);var state=Capture(item,true,meshData);item.Last=state;states.Add(state);
            }
            foreach(var removal in removed.Values.Take(64-states.Count).ToArray()) { states.Add(removal);removed.Remove(removal.id); }
            foreach(string id in dirty.Take(64-states.Count).ToArray()) {
                dirty.Remove(id);if(!items.TryGetValue(id,out var item)) continue;
                if(item.Pending) { dirty.Add(id);continue; }
                var state=Capture(item,true,meshData);item.Last=state;states.Add(state);
            }
            if(states.Count>0||acks.Count>0) {
                var batch=new Batch { objects=states.ToArray(),meshes=meshData.ToArray(),ack=acks.ToArray() };acks.Clear();
#if UNITY_WEBGL && !UNITY_EDITOR
                WyvernlandPublishWorld(JsonUtility.ToJson(batch));
#endif
            }
        }
        internal void MaintainOwnedObjects(float now)
        {
            foreach(var item in owned.ToArray()) {
                if(item.Target==null||item.Pending) continue;
                var pickup=item.Target.GetComponent<PickableObject>();
                if(pickup!=null&&pickup.IsHeld) RequestAction(new Request { action="held",@object=item.Id,state=Capture(item,false,null) });
                else if(pickup!=null&&now-item.Granted>2f) {
                    if(item.Last!=null&&item.Last.held) Released(pickup,Vector3.zero);
                    else CancelReservation(item);
                    item.Granted=now;
                }
            }
        }

        bool MotionChanged(Item item)
        {
            var last=item.Last;if(last==null) return true;
            if(item.Dummy!=null&&item.Dummy.Changed(last.dummy)) return true;
            if(item.Corpse!=null&&item.Corpse.Changed(last.corpse)) return true;
            var target=item.Target.transform;var body=item.Body;var pickup=item.Pickup;
            if(body==null) item.Body=body=item.Target.GetComponent<Rigidbody>();
            if(last.body!=(body!=null)||last.kinematic!=(body!=null&&body.isKinematic)||
                last.sleeping!=(body!=null&&!body.isKinematic&&body.IsSleeping())||
                body!=null&&((Vector(last.v)-body.linearVelocity).sqrMagnitude>.000025f||(Vector(last.w)-body.angularVelocity).sqrMagnitude>.000025f)||
                last.held!=(pickup!=null&&pickup.IsHeld)||
                (Vector(last.p)-target.position).sqrMagnitude>.000025f||Quaternion.Angle(Rotation(last.q),target.rotation)>.3f) return true;
            string parent=target.parent==item.OriginalParent?"@":IdOf(target.parent!=null?target.parent.gameObject:null);
            if(last.parent!=parent) return true;
            return item.Rope!=null&&item.Rope.NetworkChanged(last.rope);
        }
        void OnGUI()
        {
            if(ready||closing||player==null) return;
            GUI.Box(new Rect(20,20,Screen.width-40,52),string.IsNullOrEmpty(error)?"Synchronizing shared world…":error);
        }
        void OnDisable() { closing=true; }
        void OnDestroy() { closing=true;foreach(var item in items.Values) if(item.Materials!=null)
            foreach(var material in item.Materials) ReleaseNetworkAsset(material);if(current==this) current=null; }
        static void ReleaseNetworkAsset(UnityEngine.Object value) { if(value==null) return;if(Application.isPlaying) Destroy(value);else DestroyImmediate(value); }
        internal Batch CaptureForValidation(GameObject target)
        {
            var item=instances[target.GetInstanceID()];item.Geometry=true;var meshes=new List<MeshData>();
            return new Batch { objects=new[]{Capture(item,true,meshes)},meshes=meshes.ToArray(),ack=Array.Empty<string>() };
        }
        internal Batch CaptureChangesForValidation()
        {
            var meshes=new List<MeshData>();var states=new List<ObjectState>(removed.Values);
            foreach(var item in dynamics) if(!item.Pending&&item.Target!=null&&MotionChanged(item)) dirty.Add(item.Id);
            foreach(string id in dirty.ToArray()) if(items.TryGetValue(id,out var item)&&!item.Pending) states.Add(Capture(item,true,meshes));
            return new Batch { objects=states.ToArray(),meshes=meshes.ToArray(),ack=Array.Empty<string>() };
        }
    }
}
