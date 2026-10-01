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
                Require(!player.IsCrouching,"Looking down during target-free reaching must not force crouching.");
                avatar.CancelRightHandPickup();
                Require(!avatar.IsReaching,"Cancelling must end target-free reaching.");
                reachTarget.transform.position=player.GetComponentInChildren<RightHandPickupTrigger>().transform.position;
                var pickable=reachTarget.AddComponent<PickableObject>();
                Require(avatar.BeginRightHandPickup(pickable)&&player.IsCrouching,
                    "Looking down while reaching for an object must still crouch.");
                avatar.CancelRightHandPickup();
                avatar.BeginRightHandReach();
                lateUpdate.Invoke(avatar,null);
                Require(!player.IsCrouching,"Losing the object target must clear automatic crouching.");
                Debug.Log("REACH_CROUCH_VALIDATION_PASS");
            }
            finally
            {
                avatar.CancelRightHandPickup();
                UnityEngine.Object.DestroyImmediate(reachTarget);
                pitchField.SetValue(player,originalPitch);
            }
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
                Require(handTriggers.Length == 1,
                    "Expected exactly one pickup trigger on the right hand.");
                Vector3 rightHandPoint = handTriggers[0].transform.localPosition;
                Require(Vector3.Distance(rightHandPoint,new Vector3(0f,0.00327f,-0.00018f)) < 0.000001f,
                    $"Right-hand reach and hold point is wrong ({rightHandPoint}).");
                SphereCollider handTrigger = handTriggers[0].GetComponent<SphereCollider>();
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
                float handTriggerDiameter = handTrigger.radius * 2f * Mathf.Max(
                    Mathf.Abs(handTrigger.transform.lossyScale.x),
                    Mathf.Abs(handTrigger.transform.lossyScale.y),
                    Mathf.Abs(handTrigger.transform.lossyScale.z));
                Require(Mathf.Abs(handTriggerDiameter - 0.224f) < 0.001f,
                    $"Right-hand pickup trigger must be 0.224 m in world space (was {handTriggerDiameter:F3} m).");
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
                string[] expectedBones = { "LeftAnkle", "LeftElbow", "LeftHip", "LeftKnee", "LeftShoulder",
                    "Pelvis", "RightAnkle", "RightElbow", "RightFingers1", "RightFingers2", "RightFingers3", "RightHip", "RightKnee", "RightShoulder", "RightThumb1", "RightThumb2", "Waist" };
                Require(deformBones.Select(bone => bone.name).OrderBy(name => name)
                    .SequenceEqual(expectedBones),
                    "Expected body joints and the five right-hand deform bones.");
                var baked = new Mesh();
                body.BakeMesh(baked);
                Require(baked.bounds.size.magnitude < 5f, "Exploding skin weights.");
                UnityEngine.Object.DestroyImmediate(baked);

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


