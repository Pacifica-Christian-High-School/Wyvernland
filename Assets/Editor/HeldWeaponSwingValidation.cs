using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace Wyvernland.Editor
{
    public static class HeldWeaponSwingValidation
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Check(bool value,string message)
        { if(!value) throw new InvalidOperationException("Weapon swing: "+message); }
        static void Set(object value,string field,object data) => value.GetType().GetField(field,Private).SetValue(value,data);
        static T Read<T>(object value,string field) => (T)value.GetType().GetField(field,Private).GetValue(value);

        public static void Run()
        {
            ValidateMotion();
            var previous=SceneManager.GetActiveScene();
            if(string.IsNullOrEmpty(previous.path)) { EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");previous=SceneManager.GetActiveScene(); }
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
            try { ValidateRig(false);ValidateRig(true); }
            finally { SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true); }
            StrikeWaveBoostValidation.Run();
            Debug.Log("HELD_WEAPON_SWING_VALIDATION_PASS: responsive diagonal strokes, decaying directional follow-through, right guard correction, smooth reversals, held direction/reach without idle return, 30/60/144Hz, bounded heavy swings, contact backlog removal, actual rig bent-arm arcs, camera pitch, mirrored hands, fixed grip and unchanged click/hold controls.");
        }

        const string ControlsPlayKey="Wyvernland.LeftAimStrikeValidation";
        static double controlsDeadline;
        public static void RunControlsPlay()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            SessionState.SetBool(ControlsPlayKey,true);EditorApplication.isPlaying=true;
        }
        [InitializeOnLoadMethod] static void ResumeControlsPlay()
        {
            if(!SessionState.GetBool(ControlsPlayKey,false)) return;
            controlsDeadline=EditorApplication.timeSinceStartup+90;EditorApplication.update+=ControlsPlayTick;
        }
        static void ControlsPlayTick()
        {
            int result=0;
            try
            {
                if(EditorApplication.timeSinceStartup>controlsDeadline) throw new InvalidOperationException("Left Aim control validation timed out");
                if(!EditorApplication.isPlaying||Time.deltaTime<=0) return;
                ValidateControls();
            }
            catch(Exception exception) { Debug.LogException(exception);result=1; }
            SessionState.SetBool(ControlsPlayKey,false);EditorApplication.update-=ControlsPlayTick;
            EditorApplication.Exit(result);
        }

        static void ValidateControls()
        {
            var previousKeyboard=Keyboard.current;var previousMouse=Mouse.current;
            var keyboard=InputSystem.AddDevice<Keyboard>();var mouse=InputSystem.AddDevice<Mouse>();
            var actor=new GameObject("Left strike controls");actor.transform.position=new Vector3(3500,1000,0);
            actor.AddComponent<Rigidbody>().isKinematic=true;
            var view=new GameObject("View",typeof(Camera));view.transform.SetParent(actor.transform,false);
            var player=actor.AddComponent<FirstPersonController>();player.Initialize(view.transform);player.enabled=false;
            var avatar=actor.AddComponent<RiggedPlayerAvatar>();avatar.Initialize(player);avatar.enabled=false;
            var pickup=actor.AddComponent<PlayerPickupController>();pickup.Initialize(player,avatar);pickup.enabled=false;
            var left=Read<Transform>(avatar,"leftHandMarker");var right=Read<Transform>(avatar,"rightHandMarker");
            PickableObject MakeStick(Transform hand)
            {
                var stick=GameObject.CreatePrimitive(PrimitiveType.Cube);stick.transform.localScale=new Vector3(.06f,.7f,.06f);
                stick.transform.position=hand.position;stick.AddComponent<Rigidbody>().useGravity=false;
                PhysicalDestructible.Ensure(stick).ApplyMaterial("Wood");
                var item=stick.AddComponent<PickableObject>();Check(item.PickUp(hand),"control fixture grip");return item;
            }
            var leftItem=MakeStick(left);var rightItem=MakeStick(right);
            Set(avatar,"heldLeftHandItem",leftItem);Set(avatar,"heldRightHandItem",rightItem);
            var aimProperty=typeof(FirstPersonController).GetProperty("AimKey");
            var originalAim=player.AimKey;
            void Dispatch(bool interacting=false,bool attackPressed=false,bool attackReleased=false) =>
                pickup.UpdateStrikeInput(interacting,attackPressed,attackReleased,player.AimPressed,player.AimReleased);
            void LongHold() => Set(avatar,"strikeStartedAt",-100f);
            // A headless Editor has no focused Game view, so Update() selects
            // Editor input buffers even in Play Mode. Exercise player frames explicitly.
            void InputFrame() => typeof(InputSystem).GetMethod("Update",BindingFlags.Static|BindingFlags.NonPublic,
                null,new[]{typeof(InputUpdateType)},null).Invoke(null,new object[]{InputUpdateType.Dynamic});
            try
            {
                aimProperty.SetValue(player,Key.None);
                InputFrame();InputState.Change(mouse,new MouseState().WithButton(MouseButton.Right),InputUpdateType.Dynamic);mouse.MakeCurrent();
                Check(player.AimHeld&&player.AimPressed,$"default Aim press detected (held={player.AimHeld}, pressed={player.AimPressed}, update={InputState.currentUpdateType})");
                Quaternion grip=leftItem.transform.localRotation;
                Dispatch();Check(avatar.IsStrikeMode&&avatar.StrikingHeldItem==leftItem,"Aim selects left despite occupied right hand");
                leftItem.UpdateHeldPose();Check(Quaternion.Angle(grip,leftItem.transform.localRotation)<.01f,"left strike retains local grip");
                LongHold();pickup.UpdateStrikeInput(false,false,true,false,false);
                Check(avatar.IsStrikeMode,"Attack release cannot end an Aim strike");
                InputFrame();InputState.Change(mouse,new MouseState(),InputUpdateType.Dynamic);
                Check(player.AimReleased&&!player.AimHeld,"default Aim release detected");
                Dispatch();Check(Read<bool>(avatar,"strikeEnding"),"Aim release ends its long hold");avatar.CancelRightHandStrike();

                aimProperty.SetValue(player,Key.F);
                InputFrame();InputState.Change(keyboard,new KeyboardState(Key.F),InputUpdateType.Dynamic);keyboard.MakeCurrent();
                Check(player.AimPressed&&player.AimHeld,"custom keyboard Aim detected");
                Dispatch(true);Check(!avatar.IsStrikeMode&&PlayerPickupController.ReachWithLeftHand(false,player.AimHeld),"Aim plus Interact preserves left pickup");
                Dispatch();Check(avatar.IsStrikeMode&&avatar.StrikingHeldItem==leftItem,"custom Aim starts left strike");
                LongHold();InputFrame();InputState.Change(keyboard,new KeyboardState(),InputUpdateType.Dynamic);
                Check(player.AimReleased,"custom Aim release detected");Dispatch();
                Check(Read<bool>(avatar,"strikeEnding"),"custom Aim release ends its strike");avatar.CancelRightHandStrike();

                pickup.UpdateStrikeInput(false,true,false,false,false);
                Check(avatar.IsStrikeMode&&avatar.StrikingHeldItem==rightItem,"Attack still selects right hand");
                LongHold();pickup.UpdateStrikeInput(false,false,false,false,true);
                Check(avatar.IsStrikeMode,"Aim release cannot end Attack strike");
                pickup.UpdateStrikeInput(false,false,true,false,false);Check(Read<bool>(avatar,"strikeEnding"),"Attack release still works");
                avatar.CancelRightHandStrike();

                pickup.UpdateStrikeInput(false,false,false,true,false);
                pickup.UpdateStrikeInput(false,false,false,false,true);
                Check(Read<bool>(avatar,"punching")&&Read<bool>(avatar,"punchUsingLeftHand"),"short Aim click uses left Punch");
                Set(avatar,"punching",false);
                pickup.UpdateStrikeInput(false,false,false,true,false);
                pickup.UpdateStrikeInput(true,false,false,false,false);
                Check(!avatar.IsStrikeMode,"Interact cancels left strike");

                Set(avatar,"heldLeftHandItem",null);
                pickup.UpdateStrikeInput(false,false,false,true,false);
                Check(!avatar.IsStrikeMode,"empty left hand does not strike with right item on Aim");
                Set(avatar,"heldLeftHandItem",leftItem);Set(avatar,"heldRightHandItem",null);
                pickup.UpdateStrikeInput(false,true,false,false,false);
                Check(avatar.IsStrikeMode&&avatar.StrikingHeldItem==leftItem,"Attack retains left-only fallback");avatar.CancelRightHandStrike();

                Set(avatar,"heldRightHandItem",leftItem);Set(avatar,"twoHandPrimaryLeft",true);
                grip=leftItem.transform.localRotation;
                Check(avatar.BeginRightHandStrike()&&leftItem.HeldHand==right,"two-hand Attack selects right lead");avatar.CancelRightHandStrike();
                Check(Quaternion.Angle(grip,leftItem.transform.localRotation)<.01f,"two-hand right lead retains local grip direction");
                Check(avatar.BeginLeftHandStrike()&&leftItem.HeldHand==left&&Read<bool>(avatar,"twoHandPrimaryLeft"),"two-hand Aim selects left lead");
                Check(Quaternion.Angle(grip,leftItem.transform.localRotation)<.01f,"two-hand left lead retains local grip direction");
                avatar.CancelRightHandStrike();
                var gait=typeof(RiggedPlayerAvatar).GetMethod("HeldArmSwingScale",BindingFlags.Static|BindingFlags.NonPublic);
                Check((float)gait.Invoke(null,new object[]{leftItem})==0f&&(float)gait.Invoke(null,new object[]{rightItem})==0f,
                    "either stick grip suppresses movement swing");
                Check((float)gait.Invoke(null,new object[]{null})==1f,"free hand retains movement swing");
                Debug.Log("LEFT_AIM_STRIKE_VALIDATION_PASS: default/custom Aim, dual and two-hand grips, fixed local grip, separate releases, short click, Interact pickup, left-only Attack fallback and stick gait.");
            }
            finally
            {
                aimProperty.SetValue(player,originalAim);
                UnityEngine.Object.DestroyImmediate(leftItem.gameObject);UnityEngine.Object.DestroyImmediate(rightItem.gameObject);UnityEngine.Object.DestroyImmediate(actor);
                InputSystem.RemoveDevice(keyboard);InputSystem.RemoveDevice(mouse);previousKeyboard?.MakeCurrent();previousMouse?.MakeCurrent();
            }
        }

        static void ValidateMotion()
        {
            ValidateCoasting();
            var swing=new HeldWeaponSwing();
            for(int i=0;i<6;i++) swing.Step(new Vector2(2,1),.01f,1.45f,2);
            Check(swing.Angles.x>7f&&swing.Angles.y<HeldWeaponSwing.Guard.y-3f,"immediate diagonal stroke uses both axes");
            float start=swing.Angles.x;
            for(int i=0;i<8;i++) swing.Step(new Vector2(-2,-1),.01f,1.45f,2);
            Check(swing.Angles.x<start-5f,"reversal follows promptly without a wind-up delay");
            var before=swing.Angles;
            swing.Step(Vector2.zero,.01f,1.45f,2);
            Check(Vector2.Distance(swing.Angles,before)<4f,"stopping the mouse has continuous follow-through");
            float heldReach=swing.Activity;
            for(int i=0;i<40;i++) swing.Step(Vector2.zero,.01f,1.45f,2);
            Vector2 heldAngles=swing.Angles;
            for(int i=0;i<300;i++) swing.Step(Vector2.zero,.01f,1.45f,2);
            Check(Vector2.Distance(swing.Angles,heldAngles)<.001f&&Vector2.Distance(swing.Angles,HeldWeaponSwing.Guard)>3f,
                "idle weapon holds the steered direction instead of returning to guard");
            Check(Mathf.Abs(swing.Activity-heldReach)<.000001f,"idle weapon holds its reach instead of retracting the hand");
            swing.Constrain(new Vector2(4,-10));swing.Step(new Vector2(-1,0),.016f,1.45f,2);
            Check(swing.Angles.x<4,"contact discards hidden input so withdrawal is immediate");
            Vector2 baseline=Vector2.zero;
            foreach(int hz in new[]{30,60,144}) {
                swing.Reset();float step=1f/hz;
                for(int i=0;i<hz;i++) swing.Step(new Vector2(35,20)*step,step,1.45f,8);
                if(hz==30) baseline=swing.Angles;
                else Check(Vector2.Distance(swing.Angles,baseline)<.6f,"mouse travel remains consistent across frame rates");
            }
            foreach(float mass in new[]{.1f,2f,60f,1000f}) {
                swing.Reset();
                for(int i=0;i<12;i++) swing.Step(new Vector2(30,-30),1f/60,1.45f,mass);
                Check(float.IsFinite(swing.Angles.x)&&swing.Angles.x<=70.01f&&swing.Angles.y<=60.01f,"heavy strokes remain finite and bounded");
                Check(swing.Angles.x>50f,"heavy objects stay responsive");
            }
        }

        static void ValidateCoasting()
        {
            Vector2 final=Vector2.zero;
            foreach(int hz in new[]{30,60,144}) {
                var swing=new HeldWeaponSwing();float dt=1f/hz;
                for(float t=0f;t<.1f-.000001f;t+=dt) {
                    float inputStep=Mathf.Min(dt,.1f-t);
                    swing.Step(new Vector2(240,120)*inputStep,inputStep,1f,2);
                }
                Vector2 start=swing.Angles,previous=start;
                Vector2 direction=new Vector2(2,-1).normalized;
                float early=0f,late=0f;
                for(int i=0;i<hz;i++) {
                    swing.Step(Vector2.zero,dt,1f,2);Vector2 travel=swing.Angles-previous;
                    Check(Vector2.Dot(travel,direction)>=-.00001f,"follow-through keeps the rapid stroke direction");
                    if(i==0) early=travel.magnitude/dt;
                    if(i==hz/5) late=travel.magnitude/dt;
                    previous=swing.Angles;
                }
                Check(Vector2.Distance(start,swing.Angles)>5f&&Vector2.Distance(start,swing.Angles)<25f,"rapid stroke has short, bounded follow-through");
                Check(late<early*.3f,"follow-through speed decays");
                Vector2 stopped=swing.Angles;swing.Step(Vector2.zero,.2f,1f,2);
                Check(Vector2.Distance(stopped,swing.Angles)<.0001f,"follow-through stops at its new pose");
                // Start from identical mouse travel despite non-divisible frame counts.
                if(hz==30) final=swing.Angles;
                if(hz!=30) Check(Vector2.Distance(final,swing.Angles)<.2f,"coasting is frame-rate independent");
                swing.Constrain(new Vector2(4,-10));swing.Step(Vector2.zero,.2f,1f,2);
                Check(Vector2.Distance(swing.Angles,new Vector2(4,-10))<.0001f,"contact clears coasting momentum");
                swing.Step(new Vector2(-.1f,0),dt,1f,2);
                Check(swing.Angles.x<4,"slow new input immediately takes control");
            }
            var slow=new HeldWeaponSwing();for(int i=0;i<20;i++) slow.Step(new Vector2(.2f,0),.01f,1f,2);
            for(int i=0;i<50;i++) slow.Step(Vector2.zero,.01f,1f,2);
            Check(Mathf.Abs(slow.Angles.x-4f)<.001f,"slow input adds no extra drift");
        }

        static void ValidateRig(bool left)
        {
            var actor=new GameObject("Weapon swing rig");actor.transform.position=new Vector3(left?3300:3200,1000,0);
            actor.AddComponent<Rigidbody>().isKinematic=true;
            var view=new GameObject("View",typeof(Camera));view.transform.SetParent(actor.transform,false);
            var player=actor.AddComponent<FirstPersonController>();player.Initialize(view.transform);player.enabled=false;
            var avatar=actor.AddComponent<RiggedPlayerAvatar>();avatar.Initialize(player);avatar.enabled=false;
            var bindings=(Key[])player.Bindings.Clone();float sensitivity=player.Sensitivity;
            var hand=actor.GetComponentsInChildren<RightHandPickupTrigger>().First(t=>t.name.StartsWith(left?"Left":"Right")).transform;
            var stick=GameObject.CreatePrimitive(PrimitiveType.Cube);stick.transform.localScale=new Vector3(.06f,.7f,.06f);
            stick.transform.position=hand.position+Vector3.forward*.03f;
            stick.AddComponent<Rigidbody>().useGravity=false;PhysicalDestructible.Ensure(stick).ApplyMaterial("Wood");
            var item=stick.AddComponent<PickableObject>();
            try {
                Check(avatar.BeginHandPickup(item,left,hand.position),"pickup starts");avatar.UpdatePickupReach(.2f);
                Check(item.IsHeld&&item.GrabMode==PickableGrabMode.Stick&&(left?avatar.BeginLeftHandStrike():avatar.BeginRightHandStrike()),"actual held weapon enters strike");
                Set(item,"heldLinearVelocity",Vector3.right*100f);
                Check(item.HeldPointVelocity(item.transform.position).magnitude<=PickableObject.MaximumWaveSpeed+.001f,
                    "held wave impact velocity is capped at twenty metres per second");
                Set(item,"heldLinearVelocity",Vector3.zero);
                Quaternion grip=stick.transform.localRotation;
                Set(avatar,"strikeStartedAt",-100f);
                typeof(RiggedPlayerAvatar).GetMethod("ApplyRightHandStrikePose",Private).Invoke(avatar,null);
                Vector3 guard=Quaternion.Inverse(view.transform.rotation)*Read<Vector3>(avatar,"strikeAimDirection");
                Check(Mathf.Abs(Mathf.Atan2(guard.x,guard.z)*Mathf.Rad2Deg-(left?-14f:19f))<.01f,
                    "right guard shifts five degrees right while left guard is preserved");
                var shoulder=Read<Transform>(avatar,left?"leftShoulder":"rightShoulder");
                var elbow=Read<Transform>(avatar,left?"leftElbow":"rightElbow");
                var restShoulder=shoulder.localRotation;var restElbow=elbow.localRotation;
                float upper=Vector3.Distance(shoulder.position,elbow.position),lower=Vector3.Distance(elbow.position,hand.position);
                foreach(float pitch in new[]{-60f,0f,60f})
                foreach(var pose in new[]{new Vector2(0,-12),new Vector2(-60,-12),new Vector2(60,-12),new Vector2(-30,-55),new Vector2(25,50)})
                {
                    shoulder.localRotation=restShoulder;elbow.localRotation=restElbow;
                    view.transform.localRotation=Quaternion.Euler(pitch,0,0);
                    Vector3 direction=view.transform.rotation*Quaternion.Euler(pose.y,pose.x+(left?-14:14),0)*Vector3.forward;
                    Set(avatar,"strikeAimDirection",direction);
                    avatar.AimWeaponStrikeArm(shoulder,elbow,hand,.75f);
                    Set(avatar,"strikeHandYaw",pose.x);Set(avatar,"strikeHandPitch",pose.y);
                    avatar.ApplyWeaponStrikeWrist(hand,1f);
                    Check(Vector3.Distance(hand.position,shoulder.position+direction*(upper+lower)*.89f)<.004f,"authored rig follows the curved target");
                    Check(Mathf.Abs(Vector3.Distance(shoulder.position,elbow.position)-upper)<.001f&&
                        Mathf.Abs(Vector3.Distance(elbow.position,hand.position)-lower)<.001f,"swing preserves bone lengths");
                    Check(Vector3.Angle(elbow.position-shoulder.position,hand.position-elbow.position)>15f,"elbow stays naturally bent");
                    Check(Quaternion.Angle(hand.localRotation,Read<Quaternion>(avatar,"strikeWristRest"))<=55.01f,"wrist stays within a restrained bend");
                    item.UpdateHeldPose();
                    Check(Quaternion.Angle(grip,stick.transform.localRotation)<.01f,"weapon does not rotate inside the grip");
                    Check(Vector3.Distance(stick.transform.TransformPoint(Read<Vector3>(item,"heldMeshContact")),avatar.HeldStickGripPosition(hand))<.001f,"weapon remains seated in the hand");
                }
                Set(avatar,"strikeStartedAt",-100f);avatar.UpdateRightHandStrikeInput(false,true);
                Check(Read<bool>(avatar,"strikeEnding"),"long hold release recovers");
                avatar.CancelRightHandStrike();Check(avatar.BeginRightHandStrike(),"another strike starts");
                avatar.UpdateRightHandStrikeInput(false,true);Check(Read<bool>(avatar,"punching"),"short click retains punch behavior");
                Check(bindings.SequenceEqual(player.Bindings)&&Mathf.Abs(player.Sensitivity-sensitivity)<.00001f,"bindings and sensitivity remain unchanged");
            } finally { UnityEngine.Object.DestroyImmediate(stick);UnityEngine.Object.DestroyImmediate(actor); }
        }
    }
}
