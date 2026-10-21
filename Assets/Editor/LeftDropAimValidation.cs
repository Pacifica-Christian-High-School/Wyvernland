using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Wyvernland.Editor
{
    public static class LeftDropAimValidation
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Check(bool value,string reason) { if(!value) throw new InvalidOperationException("Left Drop+Aim: "+reason); }
        static void Set(object target,string field,object value) => target.GetType().GetField(field,Private).SetValue(target,value);
        static T Read<T>(object target,string field) => (T)target.GetType().GetField(field,Private).GetValue(target);
        static void Drop(PlayerPickupController pickup,bool aim=false,bool aimPressed=false,bool aimReleased=false,
            bool drop=false,bool dropPressed=false,bool dropReleased=false) =>
            pickup.HandleDropInput(aim,aimPressed,aimReleased,drop,dropPressed,dropReleased);

        [MenuItem("Tools/Wyvernland/Validate Left Drop Aim")]
        public static void Run()
        {
            var root=new GameObject("Left Drop+Aim fixture");
            try
            {
                var actor=TrainingDummy.Create(root.transform,null);
                var player=actor.GetComponent<FirstPersonController>();
                var avatar=actor.GetComponent<RiggedPlayerAvatar>();
                Check(player.RebindAim(Key.Q),"Aim can be bound to a test key");
                var pickup=actor.gameObject.AddComponent<PlayerPickupController>();
                pickup.Initialize(player,avatar);
                var leftHand=Read<Transform>(avatar,"leftHandMarker");
                PickableObject Hold(string name)
                {
                    var item=GameObject.CreatePrimitive(PrimitiveType.Cube);item.name=name;
                    item.transform.SetParent(root.transform,false);
                    item.transform.position=leftHand.position;
                    item.transform.localScale=Vector3.one*.08f;
                    item.AddComponent<Rigidbody>();
                    var held=item.AddComponent<PickableObject>();
                    Check(held.PickUp(leftHand),"left-hand fixture grips "+name);
                    Set(avatar,"heldLeftHandItem",held);
                    return held;
                }
                var dropped=Hold("Tap drop item");
                Drop(pickup);
                Drop(pickup,drop:true,dropPressed:true);
                Drop(pickup,aim:true,aimPressed:true,drop:true);
                Check(Read<bool>(pickup,"leftAimDropGesture")&&Read<bool>(pickup,"chargingLeftHand"),
                    "Drop held while Aim is pressed selects the left hand");
                Drop(pickup,aimReleased:true,drop:true);
                Check(!dropped.IsHeld&&avatar.HeldLeftHandItem==null,
                    "releasing Aim quickly drops only the left-hand item");
                var thrown=Hold("Charged throw item");
                Drop(pickup);
                Drop(pickup,drop:true,dropPressed:true);
                Drop(pickup,aim:true,aimPressed:true,drop:true);
                Set(pickup,"dropStartedAt",Time.time-1f);
                Drop(pickup,aimReleased:true,drop:true);
                Check(Read<bool>(pickup,"throwPending")&&avatar.HeldLeftHandItem==thrown,
                    "holding Aim charges the left throw before release");
                Set(pickup,"throwReleaseAt",Time.time-1f);
                Drop(pickup);
                Check(!thrown.IsHeld&&avatar.HeldLeftHandItem==null&&thrown.GetComponent<Rigidbody>().linearVelocity.magnitude>1f,
                    "charged Aim release throws the left-hand item");
                Debug.Log("LEFT_DROP_AIM_VALIDATION_PASS: Drop+Aim tap drops left item; held Aim charges and throws it.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
