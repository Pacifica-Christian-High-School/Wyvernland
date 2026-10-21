using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Wyvernland.Editor
{
    public sealed class HumanRigImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!assetPath.EndsWith("/HumanRigged.fbx")) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = false;
            importer.optimizeGameObjects = false;
            importer.isReadable = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }
    }

    public static class HumanRigValidation
    {
        public static void ValidateReachCrouch(FirstPersonController player,RiggedPlayerAvatar avatar)
        {
            var pitchField=typeof(FirstPersonController).GetField("pitch",BindingFlags.Instance|BindingFlags.NonPublic);
            var lateUpdate=typeof(RiggedPlayerAvatar).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
            float originalPitch=player.LookPitch;
            var reachTarget=new GameObject("Reach crouch target");
            try
            {
                pitchField.SetValue(player,65f);
                Require(avatar.BeginRightHandReach()&&avatar.IsReaching&&
                    !avatar.IsReachingForObject&&!avatar.HasRightHandItem,
                    "The empty right hand must reach without a pickable target.");
                lateUpdate.Invoke(avatar,null);
                Require(player.IsCrouching&&player.PickupCrouchDepth>0f,
                    "Looking down while holding interact and attack crouches during a free reach.");
                float firstDepth=player.PickupCrouchDepth;
                pitchField.SetValue(player,85f);
                lateUpdate.Invoke(avatar,null);
                Require(player.PickupCrouchDepth>firstDepth,
                    "The reach crouch deepens as the player looks farther down.");
                avatar.CancelRightHandPickup();
                Require(!avatar.IsReaching&&!player.IsCrouching,
                    "Cancelling must end target-free reaching and its crouch.");
                pitchField.SetValue(player,65f);
                reachTarget.transform.position=player.GetComponentInChildren<RightHandPickupTrigger>().transform.position;
                var pickable=reachTarget.AddComponent<PickableObject>();
                Require(avatar.BeginRightHandPickup(pickable)&&player.IsCrouching,
                    "Looking down while reaching for an object must still crouch.");
                avatar.CancelRightHandPickup();
                avatar.BeginRightHandReach();
                lateUpdate.Invoke(avatar,null);
                Require(player.IsCrouching,
                    "Losing the object target keeps the held reach crouched.");
                avatar.CancelRightHandPickup();
                Require(!player.IsCrouching,"Releasing the reach restores the standing posture.");
                avatar.SetReachCrouchInput(true);
                Require(player.IsCrouching,
                    "Held interact and attack crouches even when neither hand can reach.");
                avatar.SetReachCrouchInput(false);
                Require(!player.IsCrouching,
                    "Releasing interact and attack clears the forced crouch.");
                Debug.Log("REACH_CROUCH_VALIDATION_PASS");
            }
            finally
            {
                avatar.CancelRightHandPickup();
                avatar.SetReachCrouchInput(false);
                UnityEngine.Object.DestroyImmediate(reachTarget);
                pitchField.SetValue(player,originalPitch);
            }
        }

        private static void ValidateTriggerPickup(GameObject root,RiggedPlayerAvatar avatar,
            RightHandPickupTrigger handTrigger,bool solidContact=false)
        {
            GameObject item=GameObject.CreatePrimitive(PrimitiveType.Cube);
            item.name="Trigger contact pickup validation";
            item.transform.SetParent(root.transform);
            item.transform.position=handTrigger.transform.position;
            item.transform.localScale=Vector3.one*0.1f;
            try
            {
                PickableObject pickup=item.AddComponent<PickableObject>();
                if(!solidContact) pickup.SetNearbyDetection(true);
                Collider objectTrigger=solidContact?item.GetComponent<BoxCollider>():item.GetComponent<CapsuleCollider>();
                Physics.SyncTransforms();
                Require(objectTrigger!=null&&pickup.IsPickupDetectionCollider(objectTrigger)&&
                    pickup.PickupDetectionTouchesSphere(handTrigger.transform.position,0.1568f),
                    "Solid and trigger object colliders must both support hand contact.");
                MethodInfo enter=typeof(RightHandPickupTrigger).GetMethod("OnTriggerEnter",
                    BindingFlags.Instance|BindingFlags.NonPublic);
                MethodInfo stay=typeof(RightHandPickupTrigger).GetMethod("OnTriggerStay",
                    BindingFlags.Instance|BindingFlags.NonPublic);
                enter.Invoke(handTrigger,new object[]{objectTrigger});
                Require(!pickup.IsHeld,"Trigger contact alone does not pick up without reach input.");
                bool left=handTrigger.name.StartsWith("Left");
                Require(avatar.BeginHandReach(left),"The free hand can reach for trigger contact.");
                stay.Invoke(handTrigger,new object[]{objectTrigger});
                Require(pickup.IsHeld&&(left?avatar.HeldLeftHandItem:avatar.HeldRightHandItem)==pickup,
                    "Either hand picks up an object when it touches a valid collider during reach.");
            }
            finally
            {
                if(avatar.HeldRightHandItem!=null)
                    avatar.ReleaseRightHandItem(Vector3.zero,false);
                if(avatar.HeldLeftHandItem!=null)
                    avatar.ReleaseLeftHandItem(Vector3.zero);
                avatar.CancelRightHandPickup();
                UnityEngine.Object.DestroyImmediate(item);
            }
        }

        [MenuItem("Wyvernland/Validate Pickup Fixes")]
        public static void RunInteractionFixValidation()
        {
            Require(!PlayerPickupController.ReachWithLeftHand(true,false)&&
                PlayerPickupController.ReachWithLeftHand(false,true)&&
                !PlayerPickupController.ReachWithLeftHand(true,true),
                "Interact plus Attack selects only the right hand; Interact plus Aim selects only the left hand.");
            var root=new GameObject("Pickup contact validation");
            try
            {
                root.AddComponent<Rigidbody>().useGravity=false;
                var view=new GameObject("View",typeof(Camera));
                view.transform.SetParent(root.transform,false);
                var player=root.AddComponent<FirstPersonController>();
                player.Initialize(view.transform);
                var avatar=root.AddComponent<RiggedPlayerAvatar>();
                avatar.Initialize(player);
                Require(avatar.BeginHandReach(false)&&avatar.IsRightReaching&&!avatar.IsLeftReaching,
                    "Attack reaches with the right hand only.");
                avatar.CancelRightHandPickup();
                Require(avatar.BeginHandReach(true)&&avatar.IsLeftReaching&&!avatar.IsRightReaching,
                    "Aim reaches with the left hand only.");
                avatar.CancelRightHandPickup();
                foreach(var hand in root.GetComponentsInChildren<RightHandPickupTrigger>())
                {
                    ValidateTriggerPickup(root,avatar,hand);
                    ValidateTriggerPickup(root,avatar,hand,true);
                }
                Debug.Log("PICKUP_CONTACT_VALIDATION_PASS: solid/trigger contact pickup with either hand.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [MenuItem("Wyvernland/Validate Held Wrist Grip")]
        public static void RunHeldGripValidation()
        {
            var root=new GameObject("Held grip validation");
            try
            {
                root.AddComponent<Rigidbody>().useGravity=false;
                var view=new GameObject("View",typeof(Camera));
                view.transform.SetParent(root.transform,false);
                view.transform.localPosition=new Vector3(0f,1.65f,0f);
                var player=root.AddComponent<FirstPersonController>();
                player.Initialize(view.transform);
                var avatar=root.AddComponent<RiggedPlayerAvatar>();
                avatar.Initialize(player);
                var triggers=root.GetComponentsInChildren<RightHandPickupTrigger>();
                var bones=root.GetComponentsInChildren<SkinnedMeshRenderer>()
                    .SelectMany(renderer=>renderer.bones).Distinct().ToArray();
                ValidateHeldWristPoses(root,avatar,triggers,bones);
                Debug.Log("HELD_GRIP_VALIDATION_PASS: mirrored wrists stay attached, upright bar has no Z kink, rotating grip has no drift, Regular pose unchanged.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void ValidateHeldWristPoses(GameObject root,RiggedPlayerAvatar avatar,
            RightHandPickupTrigger[] handTriggers,Transform[] deformBones)
        {
            RightHandPickupTrigger rightTrigger=handTriggers.Single(trigger=>
                trigger.name=="Right Hand Ground Contact");
            var stick=GameObject.CreatePrimitive(PrimitiveType.Cube);
            stick.name="Wrist direction validation stick";
            stick.transform.SetParent(root.transform.parent);
            stick.transform.position=rightTrigger.transform.position+Vector3.forward*0.1f;
            stick.transform.localScale=new Vector3(0.03f,0.03f,0.3f);
            PhysicalDestructible.Ensure(stick).ApplyMaterial("Wood");
            var heldStick=stick.AddComponent<PickableObject>();
            Require(heldStick.PickUp(rightTrigger.transform),
                "Wrist validation Stick can be held.");
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var heldField=typeof(RiggedPlayerAvatar).GetField("heldRightHandItem",flags);
            var leftHeldField=typeof(RiggedPlayerAvatar).GetField("heldLeftHandItem",flags);
            typeof(RiggedPlayerAvatar).GetField("striking",flags).SetValue(avatar,true);
            foreach(bool left in new[]{false,true})
            {
                typeof(RiggedPlayerAvatar).GetField("strikeUsingLeftHand",flags)
                    .SetValue(avatar,left);
                Transform hand=handTriggers.Single(trigger=>trigger.name==
                    (left?"Left Hand Ground Contact":"Right Hand Ground Contact")).transform;
                if(left) heldStick.TransferToHand(hand);
                heldField.SetValue(avatar,left?null:heldStick);
                leftHeldField.SetValue(avatar,left?heldStick:null);
                typeof(PickableObject).GetMethod("UpdateStickHeldTransform",flags)
                    .Invoke(heldStick,new object[]{1f});
                Quaternion objectRotation=stick.transform.rotation;
                Transform wrist=deformBones.Single(bone=>bone.name==
                    (left?"LeftWrist":"RightWrist"));
                Vector3 wristRestPosition=wrist.localPosition;
                Vector3 markerPosition=hand.position;
                Transform elbow=wrist.parent;
                Vector3 gripOnWrist=hand.localPosition-wristRestPosition;
                Transform thumb=avatar.GetComponentsInChildren<Transform>(true).SingleOrDefault(bone=>bone.name==
                    (left?"LeftThumb1":"RightThumb1"));
                Transform rightThumb=root.GetComponentsInChildren<Transform>(true)
                    .Single(bone=>bone.name=="RightThumb1");
                Transform rightWrist=deformBones.Single(bone=>bone.name=="RightWrist");
                float thumbX=thumb!=null?wrist.InverseTransformPoint(thumb.position).x:
                    -rightWrist.InverseTransformPoint(rightThumb.position).x;
                gripOnWrist.x=thumbX;
                typeof(RiggedPlayerAvatar).GetMethod("UpdateHeldWristPose",flags)
                    .Invoke(avatar,null);
                Require(Vector3.Angle(wrist.up,elbow.up)<=35.01f&&
                    Vector3.Distance(wrist.localPosition,wristRestPosition)<0.000001f&&
                    Vector3.Distance(hand.position,markerPosition)<0.000001f&&
                    Vector3.Distance(wrist.TransformPoint(gripOnWrist),
                    heldStick.HeldGripPoint)<0.001f&&
                    Quaternion.Angle(stick.transform.rotation,objectRotation)<0.001f,
                    "The wrist stays attached to the forearm, limits its bend, and keeps the Stick on the posed palm without steering it.");
                Require(Mathf.Abs(wrist.InverseTransformPoint(heldStick.HeldGripPoint).x-
                    thumbX)<0.000001f,
                    "Both hands centre the stick at the thumb's local X position");
                stick.transform.rotation=Quaternion.AngleAxis(35f,Vector3.up)*
                objectRotation;
                typeof(PickableObject).GetMethod("AlignHeldContact",flags)
                    .Invoke(heldStick,null);
                typeof(RiggedPlayerAvatar).GetMethod("UpdateHeldWristPose",flags)
                    .Invoke(avatar,null);
                Require(Vector3.Angle(wrist.up,elbow.up)<=35.01f&&
                    Vector3.Distance(wrist.localPosition,wristRestPosition)<0.000001f&&
                    Vector3.Distance(wrist.TransformPoint(gripOnWrist),
                    heldStick.HeldGripPoint)<0.001f,
                    "A rotating Stick stays at the palm without dislocating or overbending the wrist.");
                Quaternion elbowRotation=elbow.rotation;
                elbow.rotation=Quaternion.LookRotation(Vector3.down,Vector3.forward);
                stick.transform.rotation=Quaternion.LookRotation(Vector3.up,Vector3.back);
                stick.transform.rotation=Quaternion.FromToRotation(
                    heldStick.StickDirection,Vector3.up)*stick.transform.rotation;
                // The fixture's long axis is local Z. This reproduces the
                // upright bar and forward forearm shown in the bug report.
                typeof(PickableObject).GetMethod("AlignHeldContact",flags)
                    .Invoke(heldStick,null);
                for(int pass=0;pass<3;pass++)
                typeof(RiggedPlayerAvatar).GetMethod("UpdateHeldWristPose",flags)
                    .Invoke(avatar,null);
                Require(Quaternion.Angle(wrist.localRotation,Quaternion.identity)<0.01f&&
                    Vector3.Distance(wrist.localPosition,wristRestPosition)<0.000001f&&
                    Vector3.Distance(wrist.TransformPoint(gripOnWrist),heldStick.HeldGripPoint)<0.001f,
                    "An upright bar keeps the forward forearm and wrist straight, without a sideways Z bend or contact drift.");
                elbow.rotation=elbowRotation;
                stick.transform.rotation=objectRotation;
                typeof(PickableObject).GetMethod("AlignHeldContact",flags)
                    .Invoke(heldStick,null);
            }
            typeof(RiggedPlayerAvatar).GetField("striking",flags).SetValue(avatar,false);
            heldField.SetValue(avatar,null);
            leftHeldField.SetValue(avatar,null);
            typeof(RiggedPlayerAvatar).GetMethod("UpdateHeldWristPose",flags)
                .Invoke(avatar,null);
            UnityEngine.Object.DestroyImmediate(stick);

            var regular=GameObject.CreatePrimitive(PrimitiveType.Cube);
            regular.name="Wrist direction validation regular object";
            regular.transform.SetParent(root.transform.parent);
            regular.transform.position=rightTrigger.transform.position+
            avatar.transform.right*0.1f;
            regular.transform.localScale=Vector3.one*0.04f;
            PhysicalDestructible.Ensure(regular).ApplyMaterial("Stone");
            var heldRegular=regular.AddComponent<PickableObject>();
            Require(heldRegular.PickUp(rightTrigger.transform)&&
                heldRegular.GrabMode==PickableGrabMode.Regular,
                "Wrist validation regular object can be held.");
            heldField.SetValue(avatar,heldRegular);
            typeof(RiggedPlayerAvatar).GetMethod("UpdateHeldWristPose",flags)
                .Invoke(avatar,null);
            Transform regularWrist=deformBones.Single(bone=>bone.name=="RightWrist");
            Vector3 regularDirection=(heldRegular.AimPoint-regularWrist.position).normalized;
            Quaternion regularBase=regularWrist.parent.rotation*
            Quaternion.Euler(0f,90f,0f);
            Quaternion regularFacing=Quaternion.FromToRotation(
                regularBase*-Vector3.right,regularDirection)*regularBase;
            Require(Vector3.Dot(-regularWrist.right,regularDirection)>0.999f&&
                Quaternion.Angle(regularWrist.rotation,regularFacing)<0.01f,
                "Regular objects face the palm with a local Y turn and no Stick wrist roll.");
            heldField.SetValue(avatar,null);
            typeof(RiggedPlayerAvatar).GetMethod("UpdateHeldWristPose",flags)
                .Invoke(avatar,null);
            UnityEngine.Object.DestroyImmediate(regular);

        }

        [MenuItem("Wyvernland/Validate Human Rig")]
        public static void Run()
        {
            GameObject root = new("Rig Validation");
            try
            {
                Rigidbody playerBody = root.AddComponent<Rigidbody>();
                playerBody.useGravity = false;
                playerBody.constraints = RigidbodyConstraints.FreezeRotation;
                var cameraObject = new GameObject("View", typeof(Camera));
                cameraObject.transform.SetParent(root.transform, false);
                cameraObject.transform.localPosition = new Vector3(0, 1.65f, 0);
                var player = root.AddComponent<FirstPersonController>();
                player.Initialize(cameraObject.transform);
                var avatar = root.AddComponent<RiggedPlayerAvatar>();
                avatar.Initialize(player);
                ValidateReachCrouch(player,avatar);
                RightHandPickupTrigger[] handTriggers = root.GetComponentsInChildren<RightHandPickupTrigger>();
                Require(handTriggers.Length == 2,
                    "Expected one pickup trigger on each hand.");
                RightHandPickupTrigger rightTrigger=handTriggers.Single(trigger=>
                    trigger.name=="Right Hand Ground Contact");
                RightHandPickupTrigger leftTrigger=handTriggers.Single(trigger=>
                    trigger.name=="Left Hand Ground Contact");
                ValidateTriggerPickup(root,avatar,rightTrigger);
                ValidateTriggerPickup(root,avatar,rightTrigger,true);
                ValidateTriggerPickup(root,avatar,leftTrigger,true);
                Vector3 rightHandPoint = rightTrigger.transform.localPosition;
                Require(Vector3.Distance(rightHandPoint,new Vector3(0f,0.00327f,-0.00018f)) < 0.000001f,
                    $"Right-hand reach and hold point is wrong ({rightHandPoint}).");
                SphereCollider handTrigger = rightTrigger.GetComponent<SphereCollider>();
                Require(handTrigger != null && handTrigger.isTrigger,
                    "The right-hand pickup volume must be a trigger SphereCollider.");
                RightHandPunchContactTrigger punchContact =
                    root.GetComponentInChildren<RightHandPunchContactTrigger>(true);
                Require(punchContact != null,
                    "The right hand must have a dedicated punch contact sphere.");
                SphereCollider punchSphere=punchContact.GetComponent<SphereCollider>();
                Require(punchSphere!=null&&punchSphere.isTrigger,
                    "The right-hand punch contact sphere must be a trigger.");
                float punchDiameter=punchSphere.radius*2f*Mathf.Max(
                    Mathf.Abs(punchSphere.transform.lossyScale.x),
                    Mathf.Abs(punchSphere.transform.lossyScale.y),
                    Mathf.Abs(punchSphere.transform.lossyScale.z));
                Require(Mathf.Abs(punchDiameter-0.18f)<0.001f,
                    $"Right-hand punch sphere must be 0.18 m (was {punchDiameter:F3} m).");
                avatar.BeginRightHandPunch();
                Require(punchSphere.enabled,"Punch contact starts during a punch.");
                Require(avatar.BeginRightHandReach()&&avatar.IsReaching&&!punchSphere.enabled,
                    "Holding reach after a punch takes control of the hand and disables punch contact.");
                avatar.CancelRightHandPickup();
                foreach(RightHandPickupTrigger pickupTrigger in new[]{rightTrigger,leftTrigger})
                {
                    SphereCollider sphere=pickupTrigger.GetComponent<SphereCollider>();
                    float diameter=sphere.radius*2f*Mathf.Max(
                        Mathf.Abs(sphere.transform.lossyScale.x),
                        Mathf.Abs(sphere.transform.lossyScale.y),
                        Mathf.Abs(sphere.transform.lossyScale.z));
                    Require(Mathf.Abs(diameter-0.3136f)<0.001f,
                        $"{pickupTrigger.name} pickup trigger must be 0.3136 m in world space (was {diameter:F4} m).");
                }
                Require(avatar.RightPickupRange > avatar.RightArmReach,
                    "The right hand's pickup range must include its extended forearm reach.");
                Require(Mathf.Approximately(avatar.RightPickupRange,5.04f),
                    "Pickable-object detection must use its 5.04 m search radius.");
                BoxCollider[] footColliders = root.GetComponentsInChildren<BoxCollider>();
                Require(footColliders.Length == 2 && footColliders.All(collider =>
                    Vector3.Distance(collider.size,new Vector3(0.1f,0.04f,0.26f))<0.000001f &&
                    Vector3.Distance(collider.center,new Vector3(0f,-0.03f,0f))<0.000001f),
                    "Expected one correctly sized and centered BoxCollider on each foot.");
                Require(root.GetComponentsInChildren<MeshCollider>().Length==0,
                    "Foot MeshColliders must be fully replaced.");
                var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>();
                Require(renderers.Count(r => r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) == 2,
                    "Expected one continuous body mesh plus the hidden head mesh.");
                Require(!root.GetComponentsInChildren<MeshRenderer>().Any(),
                    "Player body must not contain rigid cut-apart mesh sections.");
                SkinnedMeshRenderer visibleHead = renderers.Single(r => r.name == "HumanHead");
                int waistIndex = System.Array.FindIndex(visibleHead.bones, bone => bone.name == "Waist");
                Require(waistIndex >= 0 && visibleHead.sharedMesh.boneWeights.All(weight =>
                    (weight.weight0 < 0.0001f || weight.boneIndex0 == waistIndex) &&
                    (weight.weight1 < 0.0001f || weight.boneIndex1 == waistIndex) &&
                    (weight.weight2 < 0.0001f || weight.boneIndex2 == waistIndex) &&
                    (weight.weight3 < 0.0001f || weight.boneIndex3 == waistIndex)),
                    "The player head must be weighted only to Waist.");
                Require((cameraObject.GetComponent<Camera>().cullingMask & (1 << visibleHead.gameObject.layer)) == 0,
                    "The first-person camera can still render the player's head.");
                Require(renderers.Any(r => r.name == "HumanHead Shadow" &&
                    r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly),
                    "The hidden head has no shadow caster.");
                var body = renderers.Single(r => r.name == "HumanBody");
                Transform[] deformBones = renderers
                    .Where(r => r.name != "HumanHead Shadow")
                    .SelectMany(r => r.bones)
                    .Distinct()
                    .ToArray();
                string[] expectedBones = { "LeftAnkle", "LeftElbow", "LeftHip", "LeftKnee", "LeftShoulder", "LeftWrist",
                    "Pelvis", "RightAnkle", "RightElbow", "RightFingers1", "RightFingers2", "RightFingers3", "RightHip", "RightKnee", "RightShoulder", "RightThumb1", "RightThumb2", "RightWrist", "Waist" };
                Require(deformBones.Select(bone => bone.name).OrderBy(name => name)
                    .SequenceEqual(expectedBones),
                    "Expected body joints, both wrist joints and the five right-hand deform bones.");
                var baked = new Mesh();
                body.BakeMesh(baked);
                Require(baked.bounds.size.magnitude < 5f, "Exploding skin weights.");
                Vector3[] neutralVertices=baked.vertices;
                var posedWrist=new Mesh();
                foreach(string wristName in new[]{"LeftWrist","RightWrist"})
                {
                    Transform wrist=deformBones.Single(bone=>bone.name==wristName);
                    Transform handMarker=handTriggers.Single(trigger=>
                        trigger.name.StartsWith(wristName.StartsWith("Left")?"Left":"Right"))
                        .transform;
                    Require(!handMarker.IsChildOf(wrist),
                        "The held object anchor must be independent of wrist rotation.");
                    Vector3 markerPosition=handMarker.position;
                    Quaternion rest=wrist.localRotation;
                    wrist.localRotation=Quaternion.Euler(25f,0f,0f);
                    body.BakeMesh(posedWrist);
                    Require(neutralVertices.Zip(posedWrist.vertices,
                        (a,b)=>(a-b).sqrMagnitude).Max()>0.0000000001f&&
                        Vector3.Distance(markerPosition,handMarker.position)<0.000001f,
                        wristName+" deforms the hand without moving the held object.");
                    wrist.localRotation=rest;
                }
                UnityEngine.Object.DestroyImmediate(posedWrist);
                UnityEngine.Object.DestroyImmediate(baked);

                ValidateHeldWristPoses(root,avatar,handTriggers,deformBones);

                SkinnedMeshRenderer[] visibleRenderers = renderers.Where(r => r.shadowCastingMode !=
                    UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly).ToArray();
                foreach (SkinnedMeshRenderer renderer in visibleRenderers)
                    renderer.ResetLocalBounds();
                Bounds combinedBounds = visibleRenderers[0].bounds;
                foreach (Renderer renderer in visibleRenderers.Skip(1))
                {
                    combinedBounds.Encapsulate(renderer.bounds);
                }
                float height = combinedBounds.size.y;
                // Animated foot bounds include a small safety margin around the
                // ankle swing even though the source geometry remains 1.8 m tall.
                Require(height > 1.7f && height < 2f,
                    $"Model axis or approximate 1.8 metre scale is wrong (bounds {height:F3}m).");
                Debug.Log("HUMAN_RIG_VALIDATION_PASS: continuous elbow and knee bending across the unified body.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}


