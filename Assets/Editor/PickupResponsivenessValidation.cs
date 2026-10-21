using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Wyvernland.Editor
{
    public static class PickupResponsivenessValidation
    {
        [MenuItem("Tools/Wyvernland/Validate Immediate Pickup")]
        public static void Run()
        {
            foreach(bool left in new[]{false,true})
            {
                ValidateContact(left,false);
                ValidateContact(left,true);
                ValidateReach(left);
                ValidateRoom(left,false);
                ValidateRoom(left,true);
                ValidateEarlyRelease(left,false);
                ValidateEarlyRelease(left,true);
                foreach(bool room in new[]{false,true}) {
                    ValidateBrokenPickup(left,room,false,false);
                    ValidateBrokenPickup(left,room,true,false);
                    ValidateBrokenPickup(left,room,true,true);
                }
                foreach(bool splitPacket in new[]{false,true})
                    foreach(bool partial in new[]{false,true}) ValidateRemoteBrokenPickup(left,splitPacket,partial);
            }
            ValidateTwoHands();
            Debug.Log("HELD_FRAGMENT_VALIDATION_PASS: nearest surface retention after full and partial breaks, surviving remainder comparison, both hands, registered room fragments, guest ownership transfer, and usable replacement grips.");
            Debug.Log("IMMEDIATE_PICKUP_VALIDATION_PASS: same-call contact, rendered-frame free reach, animated reach without contact stalls, both hands, delayed room claims, stale snapshots, grant/denial, competing ownership, release-before-ack and session changes.");
        }

        static GameObject Fixture(bool left,out FirstPersonController player,
            out RiggedPlayerAvatar avatar,out Transform hand,out PickableObject pickup)
        {
            var root=new GameObject("Immediate pickup validation");
            var actor=new GameObject("Player");actor.transform.SetParent(root.transform,false);
            actor.transform.position=new Vector3(2000,1000,2000);
            actor.AddComponent<Rigidbody>().useGravity=false;
            var view=new GameObject("View",typeof(Camera));view.transform.SetParent(actor.transform,false);
            player=actor.AddComponent<FirstPersonController>();player.Initialize(view.transform);
            avatar=actor.AddComponent<RiggedPlayerAvatar>();avatar.Initialize(player);
            hand=actor.GetComponentsInChildren<RightHandPickupTrigger>().Single(h=>h.name.StartsWith(left?"Left":"Right")).transform;
            var item=GameObject.CreatePrimitive(PrimitiveType.Cube);item.name="Reachable pickup";
            item.transform.SetParent(root.transform,true);item.transform.position=hand.position;
            item.transform.localScale=Vector3.one*.1f;
            var body=item.AddComponent<Rigidbody>();body.useGravity=false;body.mass=1;
            pickup=item.AddComponent<PickableObject>();
            Physics.SyncTransforms();
            return root;
        }

        static void ValidateContact(bool left,bool freeReach)
        {
            var root=Fixture(left,out _,out var avatar,out var hand,out var pickup);
            try {
                // Just outside exact tangency, within the 1 cm contact skin.
                pickup.transform.position=hand.position+Vector3.forward*4;
                Physics.SyncTransforms();
                pickup.transform.position=hand.position+Vector3.forward*.212f;
                if(freeReach) {
                    Check(avatar.BeginHandReach(left),"A free hand starts reaching");
                    avatar.UpdatePickupReach(0f);
                } else Check(avatar.BeginHandPickup(pickup,left),"Contact target is eligible");
                Check(pickup.IsHeld&&(left?avatar.HeldLeftHandItem:avatar.HeldRightHandItem)==pickup,
                    "Valid contact retrieves immediately without a trigger callback or elapsed animation time");
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static void ValidateReach(bool left)
        {
            var root=Fixture(left,out _,out var avatar,out var hand,out var pickup);
            try {
                Vector3 shoulder=left?avatar.LeftShoulderPosition:avatar.RightShoulderPosition;
                float naturalReach=Vector3.Distance(shoulder,hand.position);
                pickup.transform.position=shoulder+Vector3.forward*(naturalReach*1.25f);
                Physics.SyncTransforms();
                Check(avatar.BeginHandPickup(pickup,left),"A target beyond the resting hand starts a reach");
                Check(!pickup.IsHeld,"Distant targets still require hand contact");
                for(int frame=0;frame<90&&!pickup.IsHeld;frame++) {
                    avatar.UpdatePickupReach(1f/60f);
                    Check(pickup.IsHeld||!hand.GetComponent<RightHandPickupTrigger>().IsTouching(pickup,pickup.PickupReachPoint(hand.position)),
                        "The first rendered frame with valid contact completes pickup");
                }
                Check(pickup.IsHeld,"The animated hand reaches and retrieves without stalling; hand gap="+
                    Vector3.Distance(hand.position,pickup.NearestMeshSurfacePoint(hand.position)));
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static WebWorldSession Room(GameObject root,FirstPersonController player,bool host)
        {
            var node=new GameObject("Wyvernland Shared World");node.transform.SetParent(root.transform,false);
            var session=node.AddComponent<WebWorldSession>();
            IEnumerator initialize=session.Initialize(root,player);while(initialize.MoveNext()){}
            session.ReceiveWorld(JsonUtility.ToJson(new WebWorldSession.Packet { self="self",host=host?"self":"host",ready=true }));
            return session;
        }

        static void PrepareBreakable(PickableObject pickup,Transform hand,bool partial,bool lowerGrip)
        {
            if(partial) {
                var mesh=pickup.GetComponent<MeshFilter>();mesh.sharedMesh=null;
                UnityEngine.Object.DestroyImmediate(pickup.GetComponent<BoxCollider>());
                foreach(float y in new[]{-.3f,.3f}) {
                    var part=GameObject.CreatePrimitive(PrimitiveType.Cube);
                    part.transform.SetParent(pickup.transform,false);
                    part.transform.localPosition=new Vector3(0,y,0);
                    part.transform.localScale=Vector3.one*.16f;
                }
                pickup.transform.localScale=Vector3.one;
                pickup.transform.position=hand.position-Vector3.up*(lowerGrip?-.3f:.3f);
            } else {
                pickup.transform.localScale=new Vector3(.08f,.5f,.08f);
                pickup.transform.position=hand.position-Vector3.up*.2f;
            }
            var properties=PhysicalDestructible.Ensure(pickup.gameObject);
            properties.MaterialName="Wood";properties.Hardness=2;properties.Resilience=3;
            Physics.SyncTransforms();
        }
        static void Break(PickableObject pickup)
        {
            var properties=pickup.GetComponent<PhysicalDestructible>();
            typeof(PhysicalDestructible).GetField("nextImpact",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(properties,-1f);
            Check(properties.ApplyImpact(6,3,2,pickup.transform.position,Vector3.up,30),"Real held geometry fractures");
        }
        static void ValidateBrokenPickup(bool left,bool inRoom,bool partial,bool lowerGrip)
        {
            var root=Fixture(left,out var player,out var avatar,out var hand,out var pickup);
            var pieces=new System.Collections.Generic.List<PhysicalDestructible>();
            try {
                PrepareBreakable(pickup,hand,partial,lowerGrip);
                var session=inRoom?Room(root,player,true):null;
                Check(avatar.BeginHandPickup(pickup,left,hand.position),"Break fixture starts pickup");
                avatar.UpdatePickupReach(.5f);
                Check(pickup.IsHeld,"Break fixture holds source");
                if(session!=null) Packet(session,true,claim:new WebWorldSession.Claim { id=WebWorldSession.IdOf(pickup.gameObject),owned=true });
                Vector3 center=pickup.transform.TransformPoint(Vector3.zero),towardGrip=(hand.position-center).normalized;
                pickup.GetComponent<PhysicalDestructible>().Fragmented+=(_,fragments)=>pieces.AddRange(fragments);
                Break(pickup);
                var retained=left?avatar.HeldLeftHandItem:avatar.HeldRightHandItem;
                Check(retained!=null&&retained.IsHeld&&retained.HeldHand==hand,"The same hand keeps a working grip in the fracture call");
                Check(partial&&lowerGrip?retained==pickup:retained!=pickup,"The nearest component wins, including a surviving remainder");
                if(!partial) Check(pieces.Where(p=>p.GetComponent<PickableObject>()!=retained).All(p=>Vector3.Dot(p.transform.position-center,towardGrip)<.01f),"Full fracture keeps the half nearest the hand, not the opposite end");
                if(partial&&!lowerGrip) Check(!pickup.IsHeld&&!pickup.GetComponent<Rigidbody>().isKinematic&&pickup.GetComponent<Rigidbody>().useGravity,"The unselected remainder falls freely");
                if(session!=null&&retained!=pickup) {
                    var state=session.CaptureForValidation(retained.gameObject).objects[0];
                    Check(state.held&&state.holder=="self"&&!string.IsNullOrEmpty(state.gripSource)&&state.grip.Length==3,"Room fragment inherits confirmed ownership and the actual hand location");
                }
                if(left) avatar.ReleaseLeftHandItem(Vector3.zero);else avatar.ReleaseRightHandItem(Vector3.zero,false);
                Check(!retained.IsHeld,"Retained fragment can be dropped normally");
            } finally {
                foreach(var piece in pieces) if(piece!=null) UnityEngine.Object.DestroyImmediate(piece.gameObject);
                UnityEngine.Object.DestroyImmediate(root);Time.timeScale=1;
            }
        }
        static void ValidateRemoteBrokenPickup(bool left,bool splitPacket,bool partial)
        {
            WebWorldSession.Batch damage=null;string sourceId=null;
            var pieces=new System.Collections.Generic.List<PhysicalDestructible>();
            var root=Fixture(left,out var player,out _,out var hand,out var pickup);
            try {
                PrepareBreakable(pickup,hand,partial,false);
                var session=Room(root,player,true);
                session.ReceiveWorld("{\"self\":\"host\",\"host\":\"host\",\"ready\":true}");
                var held=session.CaptureForValidation(pickup.gameObject).objects[0];sourceId=held.id;
                held.holder="self";held.writer="self";held.held=true;held.hand=left?"left":"right";held.grip=new[]{hand.position.x,hand.position.y,hand.position.z};held.revision=1;
                session.ReceiveWorld(JsonUtility.ToJson(new WebWorldSession.Packet { self="host",host="host",ready=true,objects=new[]{held} }));
                pickup.GetComponent<PhysicalDestructible>().Fragmented+=(_,fragments)=>pieces.AddRange(fragments);
                Break(pickup);damage=session.CaptureChangesForValidation();
                Check(damage.objects.Count(s=>s.held&&s.holder=="self"&&s.gripSource==sourceId)==1,"Host preserves the guest's grip without a local hand transform");
                Check(!string.IsNullOrEmpty(damage.objects.First(s=>s.id==sourceId).gripReplacement),"Host records the retained successor on the broken source");
            } finally {
                foreach(var piece in pieces) if(piece!=null) UnityEngine.Object.DestroyImmediate(piece.gameObject);
                UnityEngine.Object.DestroyImmediate(root);
            }
            root=Fixture(left,out player,out var avatar,out hand,out pickup);
            try {
                PrepareBreakable(pickup,hand,partial,false);
                var session=Room(root,player,false);
                Check(WebWorldSession.IdOf(pickup.gameObject)==sourceId,"Guest fixture matches the shared source");
                Check(avatar.BeginHandPickup(pickup,left,hand.position),"Guest grips source");avatar.UpdatePickupReach(.5f);
                Packet(session,false,claim:new WebWorldSession.Claim { id=sourceId,owned=true });
                Check(pickup.IsHeld,"Guest source is held before the fracture snapshot; left="+left+", partial="+partial);
                foreach(var state in damage.objects) state.revision=2;
                // Source release/deletion can arrive on a previous snapshot page.
                if(splitPacket) {
                    session.ReceiveWorld(JsonUtility.ToJson(new WebWorldSession.Packet { self="self",host="host",ready=true,objects=damage.objects.Where(s=>s.id==sourceId).ToArray(),meshes=damage.meshes }));
                    Check(pickup.IsHeld&&(left?avatar.HeldLeftHandItem:avatar.HeldRightHandItem)==pickup,"The existing grip survives while replacement geometry is in flight; left="+left+", partial="+partial+", source="+JsonUtility.ToJson(damage.objects.First(s=>s.id==sourceId)));
                }
                session.ReceiveWorld(JsonUtility.ToJson(new WebWorldSession.Packet { self="self",host="host",ready=true,objects=damage.objects.Where(s=>!splitPacket||s.id!=sourceId).OrderBy(s=>s.deleted?0:1).ToArray(),meshes=damage.meshes }));
                var retained=left?avatar.HeldLeftHandItem:avatar.HeldRightHandItem;
                Check(retained!=null&&retained.IsHeld&&retained.HeldHand==hand&&WebWorldSession.HasConfirmedClaim(retained),"Guest receives replacement geometry and retains its grip before source deletion");
                if(left) avatar.ReleaseLeftHandItem(Vector3.zero);else avatar.ReleaseRightHandItem(Vector3.zero,false);
                Check(!retained.IsHeld,"Guest can release the inherited fragment");
            } finally {
                foreach(var state in damage.objects) {
                    var target=WebWorldSession.ObjectOf(state.id);
                    if(target!=null&&target.transform.root!=root.transform) UnityEngine.Object.DestroyImmediate(target);
                }
                UnityEngine.Object.DestroyImmediate(root);Time.timeScale=1;
            }
        }

        static void Packet(WebWorldSession session,bool host,WebWorldSession.ObjectState state=null,WebWorldSession.Claim claim=null)
        {
            session.ReceiveWorld(JsonUtility.ToJson(new WebWorldSession.Packet { self="self",host=host?"self":"host",ready=true,
                objects=state==null?null:new[]{state},claims=claim==null?null:new[]{claim} }));
        }

        static void ValidateRoom(bool left,bool host)
        {
            var root=Fixture(left,out var player,out var avatar,out var hand,out var pickup);
            try {
                var session=Room(root,player,host);
                var saved=session.CaptureForValidation(pickup.gameObject).objects[0];
                string id=saved.id;
                Check(avatar.BeginHandPickup(pickup,left)&&pickup.IsHeld,
                    "Room pickup retrieves on contact before any ownership reply");
                Check(!WebWorldSession.HasConfirmedClaim(pickup),"The delayed claim has not been granted");
                Check(!WebWorldSession.CanSimulate(pickup.gameObject),"Unconfirmed grips cannot change authoritative destruction or tree support");
                Check(!session.CaptureChangesForValidation().objects.Any(s=>s.id==id),
                    "Unconfirmed local pickup cannot publish provisional physics into the shared world");
                Vector3 localGrip=pickup.transform.position+Vector3.forward*.5f;
                pickup.transform.position=localGrip;saved.revision=1;
                Packet(session,host,saved);
                Check(pickup.IsHeld&&Vector3.Distance(pickup.transform.position,localGrip)<.001f,
                    "An older unowned snapshot cannot delay or pull away a pending local grip");
                Packet(session,host,claim:new WebWorldSession.Claim { id=id,owned=false });
                Check(!pickup.IsHeld&&!avatar.HasRightHandItem&&!avatar.HasLeftHandItem&&
                    Vector3.Distance(pickup.transform.position,WebWorldSession.Vector(saved.p))<.001f,
                    "A declined claim restores the shared object and frees the occupied hand");
                Check(!WebWorldSession.CanInteract(pickup.gameObject),"Declined pickup does not immediately retry against a competing grab");
                var instances=(System.Collections.IDictionary)typeof(WebWorldSession)
                    .GetField("instances",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(session);
                var sharedItem=instances[pickup.gameObject.GetInstanceID()];
                sharedItem.GetType().GetField("ClaimRejectedAt").SetValue(sharedItem,Time.unscaledTime-1f);
                Check(WebWorldSession.CanInteract(pickup.gameObject),
                    "A rejected unowned stone becomes pickable again even if its stationary world pose never changes");
                saved.revision=2;Packet(session,host,saved);
                pickup.transform.position=hand.position;Physics.SyncTransforms();
                bool beganAgain=avatar.BeginHandPickup(pickup,left);
                Check(beganAgain&&pickup.IsHeld,"The next available shared snapshot permits a fresh pickup; left="+left+
                    ", host="+host+", began="+beganAgain+", available="+WebWorldSession.CanInteract(pickup.gameObject)+", mass="+pickup.PickupMassKg);
                Packet(session,host,claim:new WebWorldSession.Claim { id=id,owned=true });
                Check(pickup.IsHeld&&WebWorldSession.HasConfirmedClaim(pickup),"Granting the claim preserves the existing grip without replaying reach");
                if(left) avatar.ReleaseLeftHandItem(Vector3.zero);else avatar.ReleaseRightHandItem(Vector3.zero,false);
                saved.revision=3;Packet(session,host,saved);
                pickup.transform.position=hand.position;Physics.SyncTransforms();
                Check(avatar.BeginHandPickup(pickup,left)&&pickup.IsHeld,"A released object can be retrieved again");
                saved.holder="other";saved.writer="other";saved.revision=4;
                Packet(session,host,saved);
                Check(!pickup.IsHeld&&!avatar.HasRightHandItem&&!avatar.HasLeftHandItem&&!WebWorldSession.CanInteract(pickup.gameObject),
                    "A competing authoritative holder wins and both local hand references are cleared");
                saved.holder="";saved.revision=5;Packet(session,host,saved);
                pickup.transform.position=hand.position;Physics.SyncTransforms();
                Check(avatar.BeginHandPickup(pickup,left)&&pickup.IsHeld,"Pickup predicts again after the other player releases");
                if(left) avatar.ReleaseLeftHandItem(Vector3.zero);else avatar.ReleaseRightHandItem(Vector3.zero,false);
                Packet(session,host,claim:new WebWorldSession.Claim { id=id,owned=true });
                Check(!pickup.IsHeld&&!avatar.HasRightHandItem&&!avatar.HasLeftHandItem,"A late claim reply cannot reattach an already released object");
                saved.revision=6;Packet(session,host,saved);
                pickup.transform.position=hand.position;Physics.SyncTransforms();
                Check(avatar.BeginHandPickup(pickup,left)&&pickup.IsHeld,"Session change fixture has a pending pickup");
                session.ReceiveWorld("{\"self\":\"replacement\",\"host\":\"host\",\"ready\":false}");
                Check(!pickup.IsHeld&&!avatar.HasRightHandItem&&!avatar.HasLeftHandItem,"Changing room identity rolls back unconfirmed hand ownership");
            } finally { UnityEngine.Object.DestroyImmediate(root);Time.timeScale=1; }
        }

        static void ValidateTwoHands()
        {
            var root=Fixture(false,out _,out var avatar,out var right,out var pickup);
            try {
                pickup.transform.localScale=Vector3.one*.26f;
                PhysicalDestructible.Ensure(pickup.gameObject).ApplyMaterial("Stone");
                Check(pickup.RequiresTwoHands&&pickup.CanPickUpWithTwoHands,"The real stone fixture weighs between 26 and 90 kg");
                var left=root.GetComponentsInChildren<RightHandPickupTrigger>().Single(h=>h.name.StartsWith("Left")).transform;
                left.position=right.position+Vector3.left;
                Physics.SyncTransforms();
                Check(avatar.BeginHandPickup(pickup,false)&&!pickup.IsHeld,"A two-hand object cannot attach after only one hand makes contact");
                left.position=right.position;
                avatar.UpdatePickupReach(0f);
                Check(pickup.IsHeld&&avatar.HeldLeftHandItem==pickup&&avatar.HeldRightHandItem==pickup,
                    "The second hand completing contact retrieves a two-hand object in that frame");
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        static void ValidateEarlyRelease(bool left,bool host)
        {
            var root=Fixture(left,out var player,out var avatar,out var hand,out var pickup);
            try {
                var session=Room(root,player,host);
                var requests=new System.Collections.Generic.List<WebWorldSession.Request>();
                session.RequestForValidation+=requests.Add;
                var saved=session.CaptureForValidation(pickup.gameObject).objects[0];
                void Grab() {
                    pickup.transform.position=hand.position;
                    Check(avatar.BeginHandPickup(pickup,left)&&pickup.IsHeld,"Rapid regrab attaches on the same call");
                }
                void Drop() { if(left) avatar.ReleaseLeftHandItem(Vector3.right*3);else avatar.ReleaseRightHandItem(Vector3.right*3,false); }
                void Grant() => Packet(session,host,claim:new WebWorldSession.Claim { id=saved.id,owned=true });
                Grab();Drop();
                Check(requests.Count==1&&requests[0].action=="claim","A provisional drop waits for the claim reply without queuing a stale release");
                Grab();Grant();
                Check(pickup.IsHeld&&requests.Count==1,"Regrabbing before the reply cancels the provisional release");
                Drop();
                Check(requests.Last().action=="release","Confirmed ownership can be released immediately");
                Grab();
                Check(requests.Last().action=="claim"&&!WebWorldSession.HasConfirmedClaim(pickup),
                    "Regrabbing after a confirmed drop requests fresh ownership after the queued release");
                Grant();Drop();saved.revision=1;Packet(session,host,saved);
                Grab();Drop();int beforeReply=requests.Count;Grant();
                Check(!pickup.IsHeld&&!avatar.HasRightHandItem&&!avatar.HasLeftHandItem&&requests.Count==beforeReply+1&&
                    requests.Last().action=="release"&&Mathf.Abs(requests.Last().state.v[0]-3f)<.001f,
                    "A dropped pending pickup sends its saved release exactly once after the late grant, preserving throw velocity");
            } finally { UnityEngine.Object.DestroyImmediate(root);Time.timeScale=1; }
        }
        static void Check(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
    }
}
