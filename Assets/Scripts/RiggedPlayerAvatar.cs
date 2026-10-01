using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wyvernland
{
    // Loads the rigged player model and supplies the temporary procedural look
    // and walk cycle until authored animation clips replace it.
    public sealed class RiggedPlayerAvatar : MonoBehaviour
    {
        private const int FirstPersonHiddenLayer = 31;
        private const float CameraForwardOffset = 0.08f;
        private const float PickupRangeMetres = 5.04f;
        private const float RightHandTriggerDiameter = 0.224f;
        private const float RightHandPunchColliderDiameter = 0.18f;
        private const float PunchSpeedMultiplier = 1.4f;
        private const float PunchReadySpeedMultiplier = 1.4f;
        private const float PunchExtendedSpeedMultiplier = 1.5f;
        private const float PunchReadyDuration = 0.5f/(PunchSpeedMultiplier*PunchReadySpeedMultiplier);
        private const float PunchStageDuration = 0.1f/PunchSpeedMultiplier;
        private const float PunchExtendedDuration = PunchStageDuration/PunchExtendedSpeedMultiplier;
        private const float PunchStrikeEnd = PunchReadyDuration+PunchStageDuration;
        private const float PunchIntermediateEnd = PunchStrikeEnd+PunchStageDuration;
        private const float PunchExtendedEnd = PunchIntermediateEnd+PunchExtendedDuration;
        private const float PunchFollowThroughEnd = PunchExtendedEnd+0.4f/PunchSpeedMultiplier;
        private const float PunchReturnDuration = 0.25f/PunchSpeedMultiplier;
        private const float RightHandReachSpeed = 2.925f;
        private const float MaximumRightArmStretchScale = 1.69f;
        private const float PickupCrouchAnimationSpeedScale = 0.4f;
        private const float LegCrouchTransformSpeedScale = 0.6f;
        private const float CrouchHorizontalAnimationScale = 1.33f;
        private const float CrouchForwardSpeed = 3f;
        private const float CrouchLateralSpeed = 1.5f;
        private static readonly Vector3 RightHandPointOnElbow =
            new(0f,0.00327f,-0.00018f);
        private const float RightArmAnimationSpeedScale = 0.4f;
        private const float ThrowSpeedIncrease = 2.8f;
        private const float AdditionalThrowSpeedIncrease = 1.7f;
        private const float ThrowAnimationSpeedMultiplier = 1.5f;
        private const float PickupReachRate = 7.2f*RightArmAnimationSpeedScale;
        private const float HeldItemReturnRate = PickupReachRate*0.7f;
        private const float ThrowAnimationDuration =
            0.6f/RightArmAnimationSpeedScale/ThrowSpeedIncrease/
            AdditionalThrowSpeedIncrease/ThrowAnimationSpeedMultiplier;
        private const float ThrownHandReturnRate = 16f*RightArmAnimationSpeedScale;
        private const float MaximumThrownHandReturnDuration = 0.6f/RightArmAnimationSpeedScale;
        private const float ArmAimDistance = 50f;
        private const float ArmAimSmoothingRate = 10f;
        private const float StrikeShortPressSeconds = 0.7f;
        private const float StrikeTapDuration = 1f;
        private const float StrikeIdleCheckSeconds = 0.5f;
        private const float StrikeMinimumLookDegrees = 2f;
        private const float StrikeFastLookGraceSeconds = 0.18f;
        private const float StrikeVerticalLookScale = 1.4f;
        private const float StrikeReturnSeconds = 0.2f;
        private const float StrikeArmSmoothingRate = 12f;
        private const float StrikeCameraHandFollow = 0.2f;
        private static readonly Quaternion PunchExtendedShoulderPose =
            Quaternion.Euler(185f,-85f,-110f);
        private static readonly Quaternion PunchExtendedElbowPose =
            Quaternion.Euler(-40f,15f,-7f);
        private const float LeftThrowAimLift = 0.3f;
        private const float LeftThrowMaximumScreenCentering = 0.85f;
        private const float ArmScreenCenterDistance = 2f;
        private FirstPersonController player;
        private Material material;
        private Transform modelRoot;
        private Transform waist;
        private Transform pelvis;
        private Transform neck;
        private Transform headCameraAnchor;
        private Transform cameraTransform;
        private Quaternion waistRestRotation;
        private Quaternion waistNormalRotation;
        private Quaternion pelvisRestRotation;
        private Quaternion modelRestRotation;
        private Quaternion neckRestRotation;
        private Vector3 waistPitchAxis;
        private Vector3 neckPitchAxis;
        private Vector3 waistYawAxis;
        private Transform leftShoulder;
        private Transform rightShoulder;
        private Transform leftElbow;
        private Transform rightElbow;
        private readonly Transform[] punchDigits=new Transform[5];
        private readonly Quaternion[] punchDigitRest=new Quaternion[5];
        private readonly Quaternion[] punchDigitStart=new Quaternion[5];
        private static readonly Vector3[] PunchDigitAngles={
            new(0f,0f,-24f),new(0f,0f,75f),new(0f,0f,80f),
            new(-40f,0f,15f),new(55f,0f,0f)};
        private static readonly Vector3[] IrregularGripDigitAngles={
            new(0f,0f,26f),new(0f,0f,33f),new(0f,0f,42f),
            new(-27f,-30f,28f),new(45f,0f,0f)};
        private bool punching;
        private bool striking;
        private bool strikeEnding;
        private bool strikeLongReleasePending;
        private float strikeStartedAt;
        private float strikeEndAt;
        private float strikeFastLookUntil;
        private float strikeReturnStartedAt;
        private float strikeLookTravel;
        private float strikeAimPitch;
        private Vector3 strikeAimDirection;
        private Vector3 strikeStartHandLocalPosition;
        private Vector3 strikeCameraHandOffset;
        private Quaternion strikeShoulderPose;
        private Quaternion strikeElbowPose;
        private Quaternion strikeReturnShoulder;
        private Quaternion strikeReturnElbow;
        private float punchStartedAt;
        private bool punchImpactSlowActive;
        private float punchImpactSlowUntil;
        private const float PunchImpactSpeedScale=0.05f;
        private const float PunchImpactSlowDuration=0.2f;
        private float punchImpactElapsedAtContact;
        private bool punchImpactReturning;
        private float punchImpactReturnStartedAt;
        private Quaternion punchImpactShoulderReturnStart;
        private Quaternion punchImpactElbowReturnStart;
        private readonly Quaternion[] punchImpactDigitReturnStart=new Quaternion[5];
        private Quaternion punchShoulderStart;
        private Quaternion punchElbowStart;
        private Transform leftHandMarker;
        private Transform rightHandMarker;
        private RightHandPickupTrigger rightHandPickupTrigger;
        private RightHandPunchContactTrigger rightHandPunchContactTrigger;
        private PickableObject pickupTarget;
        private bool freeRightHandReach;
        private PickableObject heldRightHandItem;
        private bool lowPickupReach;
        private bool returningRightHandWithItem;
        private bool returningRightHandAfterThrow;
        private Quaternion pickupShoulderPose;
        private Quaternion pickupElbowPose;
        private Vector3 pickupHandGoal;
        private float rightHandLiftCharge;
        private float throwAnimationStartedAt;
        private float throwFollowThroughUntil;
        private bool throwAnimationWaitingForRelease;
        private Quaternion throwShoulderStart;
        private Quaternion throwElbowStart;
        private Quaternion leftAimShoulderPose;
        private Quaternion leftAimElbowPose;
        private Quaternion rightAimShoulderPose;
        private Quaternion rightAimElbowPose;
        private bool leftArmAimActive;
        private bool rightArmAimActive;
        private Transform leftHip;
        private Transform rightHip;
        private Transform leftKnee;
        private Transform rightKnee;
        private Transform leftAnkle;
        private Transform rightAnkle;
        private Transform leftFootCollider;
        private Transform rightFootCollider;
        private Quaternion leftAnkleToFootColliderRotation;
        private Quaternion rightAnkleToFootColliderRotation;
        private Quaternion leftShoulderRestRotation;
        private Quaternion rightShoulderRestRotation;
        private Quaternion leftElbowRestRotation;
        private Quaternion rightElbowRestRotation;
        private Vector3 rightShoulderRestPosition;
        private Vector3 rightElbowRestPosition;
        private Quaternion leftHipRestRotation;
        private Quaternion rightHipRestRotation;
        private Quaternion leftKneeRestRotation;
        private Quaternion rightKneeRestRotation;
        private Quaternion leftAnkleRestRotation;
        private Quaternion rightAnkleRestRotation;
        private Vector3 leftShoulderPitchAxis;
        private Vector3 rightShoulderPitchAxis;
        private Vector3 leftShoulderSpreadAxis;
        private Vector3 rightShoulderSpreadAxis;
        private Vector3 leftElbowPitchAxis;
        private Vector3 rightElbowPitchAxis;
        private Vector3 leftHipPitchAxis;
        private Vector3 rightHipPitchAxis;
        private Vector3 leftHipSpreadAxis;
        private Vector3 rightHipSpreadAxis;
        private Vector3 leftKneePitchAxis;
        private Vector3 rightKneePitchAxis;
        private Vector3 leftAnklePitchAxis;
        private Vector3 rightAnklePitchAxis;
        private float stepPhase;
        private float crouchStepPhase;
        private float airborneTime;
        private float cameraBobPhase;
        private float cameraBobOffset;
        private float landingShakeTimer;
        private float landingShakeStrength;
        private float fastestFallSpeed;
        private bool wasGrounded;
        private bool groundStateInitialized;
        private float leftArmReach;
        private float rightArmReach;
        private Vector3 rightElbowRestScale;
        private ProceduralMap terrain;
        private bool airborneArmSwingActive;
        private float airborneArmMovement;
        private bool airborneArmWasSprinting;
        private Mesh runtimeHeadMesh;
        private ParticleSystem slideSmoke;
        private ParticleSystem rightFootSlideSmoke;
        private ParticleSystem handSmoke;
        private ParticleSystem rightHandSmoke;
        private Material slideSmokeMaterial;
        private Texture2D slideSmokeTexture;

        public bool BothLegsInMovementSlowRange =>
            IsLocalXInRange(leftHip,150f,210f)&&IsLocalXInRange(rightHip,150f,210f);

        private static bool IsLocalXInRange(Transform joint,float minimum,float maximum)
        {
            if(joint==null) return false;
            float angle=joint.localEulerAngles.x;
            return angle>=minimum&&angle<=maximum;
        }

        public void Initialize(FirstPersonController controller)
        {
            player = controller;
            terrain = FindFirstObjectByType<ProceduralMap>();
            GameObject asset = Resources.Load<GameObject>("Player/HumanRiggedEdited");
            if (asset == null)
                asset = Resources.Load<GameObject>("Player/HumanRigged");
            if (asset == null)
            {
                Debug.LogError("Missing Player/HumanRigged FBX. Reimport the player asset.");
                enabled = false;
                return;
            }
            GameObject model = Instantiate(asset, transform, false);
            model.name = "Human (Rigged Player)";
            modelRoot = model.transform;
            modelRestRotation = modelRoot.localRotation;
            waist = FindChild(model.transform, "Waist");
            if (waist == null)
                throw new System.InvalidOperationException("Missing player joint: Waist");
            pelvis = FindChild(model.transform, "Pelvis");
            if (pelvis == null)
                throw new System.InvalidOperationException("Missing player joint: Pelvis");
            leftShoulder = FindChild(model.transform, "LeftShoulder");
            rightShoulder = FindChild(model.transform, "RightShoulder");
            leftElbow = FindChild(model.transform, "LeftElbow");
            rightElbow = FindChild(model.transform, "RightElbow");
            string[] digitNames={"RightFingers1","RightFingers2","RightFingers3",
                "RightThumb1","RightThumb2"};
            for(int i=0;i<punchDigits.Length;i++)
            {
                punchDigits[i]=FindChild(model.transform,digitNames[i]);
                if(punchDigits[i]!=null) punchDigitRest[i]=punchDigits[i].localRotation;
            }
            leftHip = FindChild(model.transform, "LeftHip");
            rightHip = FindChild(model.transform, "RightHip");
            leftKnee = FindChild(model.transform, "LeftKnee");
            rightKnee = FindChild(model.transform, "RightKnee");
            leftAnkle = FindChild(model.transform, "LeftAnkle");
            rightAnkle = FindChild(model.transform, "RightAnkle");
            if (leftShoulder == null || rightShoulder == null || leftElbow == null || rightElbow == null ||
                leftHip == null || rightHip == null || leftKnee == null || rightKnee == null ||
                leftAnkle == null || rightAnkle == null)
                throw new System.InvalidOperationException("Missing player limb joints.");
            waistRestRotation = waist.localRotation;
            waistNormalRotation = waistRestRotation;
            pelvisRestRotation = pelvis.localRotation;
            waistPitchAxis = waist.parent.InverseTransformDirection(transform.right).normalized;
            waistYawAxis = waist.parent.InverseTransformDirection(transform.up).normalized;
            leftShoulderRestRotation = leftShoulder.localRotation;
            rightShoulderRestRotation = rightShoulder.localRotation;
            leftElbowRestRotation = leftElbow.localRotation;
            rightElbowRestRotation = rightElbow.localRotation;
            rightShoulderRestPosition = rightShoulder.localPosition;
            rightElbowRestPosition = rightElbow.localPosition;
            leftHipRestRotation = leftHip.localRotation;
            rightHipRestRotation = rightHip.localRotation;
            leftKneeRestRotation = leftKnee.localRotation;
            rightKneeRestRotation = rightKnee.localRotation;
            leftAnkleRestRotation = leftAnkle.localRotation;
            rightAnkleRestRotation = rightAnkle.localRotation;
            rightElbowRestScale=rightElbow.localScale;
            leftShoulderPitchAxis = BoneAxis(leftShoulder, transform.right);
            rightShoulderPitchAxis = BoneAxis(rightShoulder, transform.right);
            leftShoulderSpreadAxis = BoneAxis(leftShoulder,transform.forward);
            rightShoulderSpreadAxis = BoneAxis(rightShoulder,transform.forward);
            leftElbowPitchAxis = BoneAxis(leftElbow, transform.right);
            rightElbowPitchAxis = BoneAxis(rightElbow, transform.right);
            leftHipPitchAxis = BoneAxis(leftHip, transform.right);
            rightHipPitchAxis = BoneAxis(rightHip, transform.right);
            leftHipSpreadAxis = BoneAxis(leftHip,transform.forward);
            rightHipSpreadAxis = BoneAxis(rightHip,transform.forward);
            leftKneePitchAxis = BoneAxis(leftKnee, transform.right);
            rightKneePitchAxis = BoneAxis(rightKnee, transform.right);
            leftAnklePitchAxis = leftAnkle.parent.InverseTransformDirection(transform.right).normalized;
            rightAnklePitchAxis = rightAnkle.parent.InverseTransformDirection(transform.right).normalized;
            CreateHandMarkers();
            CreateNeck(model);

            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = "Human Matte Skin";
            material.SetColor("_BaseColor", new Color(0.72f, 0.50f, 0.38f));
            material.SetFloat("_Smoothness", 0.08f);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Cull", (float)CullMode.Back);
            material.doubleSidedGI = false;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
            {
                var materials = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.On;
            }
            foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                renderer.updateWhenOffscreen = true;
                renderer.localBounds = new Bounds(Vector3.up * 0.9f, Vector3.one * 4f);
            }
            AttachCameraToHeadPosition(model);
            HideHeadFromPlayerCamera(model);
            CreateFootColliders();
            CreateSlideSmoke();
            CreateRightFootSlideSmoke();
            CreateHandSmoke();
        }

        public bool HasRightHandItem => heldRightHandItem!=null;
        public bool IsStrikeMode => striking&&!strikeEnding;

        public bool BeginRightHandStrike()
        {
            if(striking||!HasRightHandItem||rightShoulder==null||rightElbow==null||
                rightHandMarker==null||cameraTransform==null||headCameraAnchor==null||
                Time.time<throwFollowThroughUntil||returningRightHandAfterThrow)
                return false;
            EndRightHandPunchForReach();
            CancelRightHandPickup();
            returningRightHandWithItem=false;
            striking=true;
            strikeEnding=false;
            strikeLongReleasePending=false;
            strikeStartedAt=Time.time;
            strikeEndAt=strikeStartedAt+StrikeTapDuration;
            strikeFastLookUntil=0f;
            strikeLookTravel=0f;
            strikeAimPitch=player.LookPitch;
            strikeAimDirection=cameraTransform.forward;
            strikeStartHandLocalPosition=headCameraAnchor.InverseTransformPoint(
                rightHandMarker.position);
            strikeShoulderPose=rightShoulder.localRotation;
            strikeElbowPose=rightElbow.localRotation;
            return true;
        }

        public void UpdateRightHandStrikeInput(bool pressed,bool released)
        {
            if(!IsStrikeMode) return;
            if(pressed&&Time.time>strikeStartedAt+0.01f)
            {
                EndRightHandStrike();
                return;
            }
            if(released&&Time.time-strikeStartedAt>=StrikeShortPressSeconds)
                strikeLongReleasePending=true;
        }

        private void RecordFastStrikeLook()
        {
            if(player.RawLookSpeedDegreesPerSecond>=
                FirstPersonController.StrikeFastLookDegreesPerSecond)
                strikeFastLookUntil=Time.time+StrikeFastLookGraceSeconds;
        }

        public void CancelRightHandStrike()
        {
            striking=false;
            strikeEnding=false;
            strikeLongReleasePending=false;
        }

        private void EndRightHandStrike()
        {
            if(!striking||strikeEnding) return;
            strikeEnding=true;
            strikeReturnStartedAt=Time.time;
            strikeReturnShoulder=rightShoulder.localRotation;
            strikeReturnElbow=rightElbow.localRotation;
        }

        private void ApplyRightHandStrikePose()
        {
            if(!striking) return;
            if(!HasRightHandItem)
            {
                CancelRightHandStrike();
                return;
            }
            if(strikeEnding)
            {
                float progress=Mathf.Clamp01((Time.time-strikeReturnStartedAt)/
                    StrikeReturnSeconds);
                rightShoulder.localRotation=Quaternion.Slerp(strikeReturnShoulder,
                    rightShoulderRestRotation,progress);
                rightElbow.localRotation=Quaternion.Slerp(strikeReturnElbow,
                    rightElbowRestRotation,progress);
                rightElbow.localPosition=Vector3.Lerp(rightElbow.localPosition,
                    rightElbowRestPosition,progress);
                if(progress>=1f) CancelRightHandStrike();
                return;
            }

            Vector2 rawLook=player.RawLookDelta;
            RecordFastStrikeLook();
            if(strikeLongReleasePending)
            {
                strikeLongReleasePending=false;
                if(Time.time>=strikeFastLookUntil)
                {
                    EndRightHandStrike();
                    return;
                }
            }
            strikeLookTravel+=rawLook.magnitude;
            strikeAimDirection=Quaternion.AngleAxis(rawLook.x,Vector3.up)*strikeAimDirection;
            float minimumPitch=player.IsSliding?-60f:-70f;
            float nextPitch=Mathf.Clamp(strikeAimPitch-
                rawLook.y*StrikeVerticalLookScale,minimumPitch,89f);
            float pitchChange=nextPitch-strikeAimPitch;
            Vector3 aimRight=Vector3.Cross(Vector3.up,strikeAimDirection).normalized;
            if(aimRight.sqrMagnitude>0.0001f)
                strikeAimDirection=Quaternion.AngleAxis(pitchChange,aimRight)*strikeAimDirection;
            strikeAimPitch=nextPitch;
            if(Time.time-strikeStartedAt>=StrikeIdleCheckSeconds&&
                strikeLookTravel<StrikeMinimumLookDegrees)
            {
                EndRightHandStrike();
                return;
            }
            if(Time.time>=strikeEndAt&&Time.time>=strikeFastLookUntil)
            {
                EndRightHandStrike();
                return;
            }

            float blend=1f-Mathf.Exp(-StrikeArmSmoothingRate*Time.deltaTime);
            rightShoulder.localPosition=rightShoulderRestPosition;
            rightElbow.localPosition=Vector3.Lerp(rightElbow.localPosition,
                rightElbowRestPosition*1.15f,blend);
            rightElbow.localScale=rightElbowRestScale;
            rightHandMarker.localPosition=RightHandPointOnElbow;
            // Punch 4 Extended is the last full extension in the old punch.
            // Keep its elbow rotation while the shoulder steers the held hand.
            rightShoulder.localRotation=PunchExtendedShoulderPose;
            rightElbow.localRotation=PunchExtendedElbowPose;
            Vector3 target=cameraTransform.position+strikeAimDirection*ArmAimDistance;
            AimJointAtTarget(rightShoulder,rightHandMarker,target,1f);
            Quaternion aimedShoulder=rightShoulder.localRotation;
            strikeShoulderPose=Quaternion.Slerp(strikeShoulderPose,aimedShoulder,blend);
            strikeElbowPose=Quaternion.Slerp(strikeElbowPose,PunchExtendedElbowPose,blend);
            rightShoulder.localRotation=strikeShoulderPose;
            rightElbow.localRotation=strikeElbowPose;
        }

        public void BeginRightHandPunch()
        {
            if(punching||(HasRightHandItem&&heldRightHandItem.GrabMode!=PickableGrabMode.Regular)||
                rightShoulder==null||rightElbow==null||
                Time.time<throwFollowThroughUntil||returningRightHandAfterThrow) return;
            CancelRightHandPickup();
            punching=true;
            punchImpactSlowActive=false;
            punchImpactReturning=false;
            punchStartedAt=Time.time;
            if(rightHandPunchContactTrigger!=null)
                rightHandPunchContactTrigger.SetActive(true);
            punchShoulderStart=rightShoulder.localRotation;
            punchElbowStart=rightElbow.localRotation;
            for(int i=0;i<punchDigits.Length;i++)
                if(punchDigits[i]!=null) punchDigitStart[i]=punchDigits[i].localRotation;
        }

        private void EndRightHandPunchForReach()
        {
            if(!punching) return;
            punching=false;
            punchImpactSlowActive=false;
            punchImpactReturning=false;
            if(rightHandPunchContactTrigger!=null)
                rightHandPunchContactTrigger.SetActive(false);
        }

        private void ApplyRightHandPunchPose()
        {
            if(!punching) return;
            // Pickup reach changes the elbow's local position to lengthen the arm.
            // A punch owns the complete right-arm pose, so keep both arm joints at
            // their authored positions until the punch has fully returned to idle.
            rightShoulder.localPosition=rightShoulderRestPosition;
            rightElbow.localPosition=rightElbowRestPosition;
            rightElbow.localScale=rightElbowRestScale;
            if(rightHandMarker!=null)
                rightHandMarker.localPosition=RightHandPointOnElbow;
            if(punchImpactSlowActive)
            {
                if(Time.time<punchImpactSlowUntil)
                    // Anchor to contact time so the slowdown is independent of
                    // frame rate and the point within a frame where contact occurred.
                    punchStartedAt=Time.time-punchImpactElapsedAtContact-
                        (Time.time-(punchImpactSlowUntil-PunchImpactSlowDuration))*PunchImpactSpeedScale;
                else
                {
                    punchImpactSlowActive=false;
                    punchImpactReturning=true;
                    punchImpactReturnStartedAt=Time.time;
                    punchImpactShoulderReturnStart=rightShoulder.localRotation;
                    punchImpactElbowReturnStart=rightElbow.localRotation;
                    for(int i=0;i<punchDigits.Length;i++)
                        if(punchDigits[i]!=null)
                            punchImpactDigitReturnStart[i]=punchDigits[i].localRotation;
                }
            }
            if(punchImpactReturning)
            {
                float returnProgress=Mathf.Clamp01((Time.time-punchImpactReturnStartedAt)/
                    PunchReturnDuration);
                returnProgress=Mathf.SmoothStep(0f,1f,returnProgress);
                rightShoulder.localRotation=Quaternion.Slerp(
                    punchImpactShoulderReturnStart,rightShoulderRestRotation,returnProgress);
                rightElbow.localRotation=Quaternion.Slerp(
                    punchImpactElbowReturnStart,rightElbowRestRotation,returnProgress);
                for(int i=0;i<punchDigits.Length;i++)
                    if(punchDigits[i]!=null)
                        punchDigits[i].localRotation=Quaternion.Slerp(
                            punchImpactDigitReturnStart[i],punchDigitRest[i],returnProgress);
                if(returnProgress>=1f)
                {
                    punchImpactReturning=false;
                    punching=false;
                    if(rightHandPunchContactTrigger!=null)
                        rightHandPunchContactTrigger.SetActive(false);
                }
                ApplyHeldPunchFingerPose();
                return;
            }
            float elapsed=Mathf.Max(0f,Time.time-punchStartedAt);
            if(punchImpactSlowActive)
                elapsed=Mathf.Min(elapsed,PunchFollowThroughEnd-0.0001f);
            Quaternion readyShoulder=Quaternion.Euler(-180f,-10f,-110f);
            Quaternion readyElbow=Quaternion.Euler(-100f,200f,-200f);
            Quaternion strikeShoulder=Quaternion.Euler(150f,3f,-150f);
            Quaternion strikeElbow=Quaternion.Euler(-105f,-15f,11f);
            Quaternion intermediateShoulder=Quaternion.Euler(180f,-12f,-150f);
            Quaternion intermediateElbow=Quaternion.Euler(-100f,-170f,165f);
            Quaternion finalShoulder=PunchExtendedShoulderPose;
            Quaternion finalElbow=PunchExtendedElbowPose;
            // Post-multiplication rotates around each joint's own local X axis.
            Quaternion followShoulder=finalShoulder*Quaternion.AngleAxis(10f,Vector3.right);
            Quaternion followElbow=finalElbow*Quaternion.AngleAxis(-10f,Vector3.right);
            Quaternion shoulder;
            Quaternion elbow;
            if(elapsed<PunchReadyDuration)
            {
                shoulder=Quaternion.Slerp(punchShoulderStart,readyShoulder,
                    elapsed/PunchReadyDuration);
                elbow=Quaternion.Slerp(punchElbowStart,readyElbow,
                    elapsed/PunchReadyDuration);
            }
            else if(elapsed<PunchStrikeEnd)
            {
                shoulder=Quaternion.Slerp(readyShoulder,strikeShoulder,
                    (elapsed-PunchReadyDuration)/PunchStageDuration);
                elbow=Quaternion.Slerp(readyElbow,strikeElbow,
                    (elapsed-PunchReadyDuration)/PunchStageDuration);
            }
            else if(elapsed<PunchIntermediateEnd)
            {
                shoulder=Quaternion.Slerp(strikeShoulder,intermediateShoulder,
                    (elapsed-PunchStrikeEnd)/PunchStageDuration);
                elbow=Quaternion.Slerp(strikeElbow,intermediateElbow,
                    (elapsed-PunchStrikeEnd)/PunchStageDuration);
            }
            else if(elapsed<PunchExtendedEnd)
            {
                shoulder=Quaternion.Slerp(intermediateShoulder,finalShoulder,
                    (elapsed-PunchIntermediateEnd)/PunchExtendedDuration);
                elbow=Quaternion.Slerp(intermediateElbow,finalElbow,
                    (elapsed-PunchIntermediateEnd)/PunchExtendedDuration);
            }
            else if(elapsed<PunchFollowThroughEnd)
            {
                float followProgress=(elapsed-PunchExtendedEnd)/
                    (PunchFollowThroughEnd-PunchExtendedEnd);
                shoulder=Quaternion.Slerp(finalShoulder,followShoulder,followProgress);
                elbow=Quaternion.Slerp(finalElbow,followElbow,followProgress);
            }
            else
            {
                // Keep ownership through the return so locomotion cannot alter it.
                float progress=Mathf.Clamp01((elapsed-PunchFollowThroughEnd)/
                    PunchReturnDuration);
                shoulder=Quaternion.Slerp(followShoulder,rightShoulderRestRotation,progress);
                elbow=Quaternion.Slerp(followElbow,rightElbowRestRotation,progress);
                if(progress>=1f)
                {
                    punching=false;
                    if(rightHandPunchContactTrigger!=null)
                        rightHandPunchContactTrigger.SetActive(false);
                }
            }
            rightShoulder.localRotation=shoulder;
            rightElbow.localRotation=elbow;
            for(int i=0;i<punchDigits.Length;i++)
            {
                if(punchDigits[i]==null) continue;
                Quaternion fist=Quaternion.Euler(PunchDigitAngles[i]);
                punchDigits[i].localRotation=elapsed<PunchFollowThroughEnd?
                    Quaternion.Slerp(punchDigitStart[i],fist,
                        Mathf.Clamp01(elapsed/PunchReadyDuration)):
                    Quaternion.Slerp(fist,punchDigitRest[i],
                        Mathf.Clamp01((elapsed-PunchFollowThroughEnd)/PunchReturnDuration));
            }
            ApplyHeldPunchFingerPose();
        }

        private void ApplyHeldPunchFingerPose()
        {
            if(heldRightHandItem!=null&&heldRightHandItem.GrabMode==PickableGrabMode.Regular&&
                punchDigits[0]!=null)
                punchDigits[0].localRotation=Quaternion.Euler(0f,0f,-30f);
        }

        private void OnRightHandPunchContact(Collider other)
        {
            if(!punching||punchImpactSlowActive||punchImpactReturning||
                other==null||other.isTrigger) return;
            float elapsed=Time.time-punchStartedAt;
            if(elapsed>=PunchFollowThroughEnd) return;
            punchImpactSlowActive=true;
            punchImpactElapsedAtContact=elapsed;
            punchImpactSlowUntil=Time.time+PunchImpactSlowDuration;
            if(rightHandPunchContactTrigger!=null)
                rightHandPunchContactTrigger.SetActive(false);
        }
        public bool IsReachingForObject => pickupTarget!=null;
        public bool IsReaching => pickupTarget!=null||freeRightHandReach;
        public PickableObject HeldRightHandItem => heldRightHandItem;
        public Vector3 RightShoulderPosition => rightShoulder!=null?rightShoulder.position:transform.position;
        public float RightArmReach => rightArmReach;
        // Target detection is intentionally independent of the elbow-local hand
        // marker. That marker is an attachment point and can be very close to
        // the elbow without reducing the player's usable pickup search radius.
        public float RightPickupRange => PickupRangeMetres;

        public void SetRightHandLiftCharge(float charge)
        {
            rightHandLiftCharge=HasRightHandItem?ShapeLiftProgress(charge):0f;
            if(rightHandLiftCharge>0f)
            {
                CancelRightHandStrike();
                returningRightHandWithItem=false;
            }
        }

        private static float ShapeLiftProgress(float charge)
        {
            float progress=Mathf.Clamp01(charge);
            // The derivative of this curve falls smoothly from 1.3 at the
            // beginning to 0.5 at the top, while still ending exactly at 1.
            return 1.3f*progress-0.1f*progress*progress-
                0.2f*progress*progress*progress;
        }

        public void ReleaseRightHandItem(Vector3 velocity,bool throwing)
        {
            if(!HasRightHandItem) return;
            CancelRightHandStrike();
            bool animationAlreadyStarted=throwAnimationWaitingForRelease;
            heldRightHandItem.Release(velocity,transform);
            heldRightHandItem=null;
            rightHandLiftCharge=0f;
            returningRightHandWithItem=false;
            returningRightHandAfterThrow=throwing;
            if(throwing)
            {
                throwAnimationWaitingForRelease=false;
                if(!animationAlreadyStarted) StartRightHandThrowAnimation(false);
            }
            else
            {
                throwAnimationWaitingForRelease=false;
                throwAnimationStartedAt=0f;
                throwFollowThroughUntil=0f;
            }
        }

        public void BeginRightHandThrowAnimation()
        {
            if(!HasRightHandItem||throwAnimationWaitingForRelease) return;
            CancelRightHandStrike();
            rightHandLiftCharge=0f;
            returningRightHandWithItem=false;
            returningRightHandAfterThrow=true;
            StartRightHandThrowAnimation(true);
        }

        public void CancelPendingRightHandThrowAnimation()
        {
            if(!throwAnimationWaitingForRelease) return;
            throwAnimationWaitingForRelease=false;
            throwAnimationStartedAt=0f;
            throwFollowThroughUntil=0f;
            returningRightHandAfterThrow=false;
            returningRightHandWithItem=HasRightHandItem;
        }

        private void StartRightHandThrowAnimation(bool waitForRelease)
        {
            throwAnimationWaitingForRelease=waitForRelease;
            throwAnimationStartedAt=Time.time;
            throwFollowThroughUntil=throwAnimationStartedAt+ThrowAnimationDuration;
            throwShoulderStart=rightShoulder.localRotation;
            throwElbowStart=rightElbow.localRotation;
        }

        private void ApplyRightHandThrowPose()
        {
            if(rightHandLiftCharge>0f&&HasRightHandItem)
            {
                PoseLocalEuler(rightShoulder,new Vector3(60f,80f,30f),rightHandLiftCharge);
                PoseLocalEuler(rightElbow,new Vector3(-80f,20f,-20f),rightHandLiftCharge);
            }
            else if(Time.time<throwFollowThroughUntil)
            {
                float progress=Mathf.InverseLerp(throwAnimationStartedAt,
                    throwFollowThroughUntil,Time.time);
                progress=progress*progress*(3f-2f*progress);
                PoseLocalRotation(rightShoulder,Quaternion.Slerp(throwShoulderStart,
                    Quaternion.Euler(60f,70f,30f),progress),1f);
                PoseLocalRotation(rightElbow,Quaternion.Slerp(throwElbowStart,
                    Quaternion.Euler(0f,0f,50f),progress),1f);
            }
        }

        public bool BeginRightHandPickup(PickableObject target)
        {
            if((Time.time<throwFollowThroughUntil||returningRightHandAfterThrow)||
                target==null||target.IsHeld||HasRightHandItem||pickupTarget!=null||player.IsSliding||
                rightShoulder==null||rightHandMarker==null) return false;
            if(Vector3.Distance(rightShoulder.position,target.AimPoint)>RightPickupRange) return false;
            EndRightHandPunchForReach();
            pickupTarget=target;
            freeRightHandReach=false;
            pickupShoulderPose=rightShoulder.localRotation;
            pickupElbowPose=rightElbow.localRotation;
            pickupHandGoal=rightHandMarker.position;
            lowPickupReach=player.LookPitch>50f;
            player.SetPickupCrouch(lowPickupReach);
            return true;
        }

        public bool BeginRightHandReach()
        {
            if(IsReaching) return true;
            if(Time.time<throwFollowThroughUntil||returningRightHandAfterThrow||
                HasRightHandItem||rightShoulder==null||rightElbow==null||rightHandMarker==null)
                return false;
            EndRightHandPunchForReach();
            freeRightHandReach=true;
            pickupShoulderPose=rightShoulder.localRotation;
            pickupElbowPose=rightElbow.localRotation;
            pickupHandGoal=rightHandMarker.position;
            lowPickupReach=false;
            player.SetPickupCrouch(false);
            return true;
        }

        public void CancelRightHandPickup()
        {
            pickupTarget=null;
            freeRightHandReach=false;
            if(rightElbow!=null)
                rightElbow.localPosition=rightElbowRestPosition;
            if(rightHandMarker!=null)
                rightHandMarker.localPosition=RightHandPointOnElbow;
            RestorePickupPosture();
        }

        private void RestorePickupPosture()
        {
            lowPickupReach=false;
            if(player!=null) player.SetPickupCrouch(false);
        }

        private void LateUpdate()
        {
            if (player == null || waist == null) return;
            if(IsReaching)
            {
                bool shouldCrouch=pickupTarget!=null&&player.LookPitch>50f;
                if(lowPickupReach!=shouldCrouch)
                {
                    lowPickupReach=shouldCrouch;
                    player.SetPickupCrouch(shouldCrouch);
                }
            }
            if (player.IsSliding)
            {
                airborneTime=0f;
                fastestFallSpeed=0f;
                airborneArmSwingActive=false;
                wasGrounded=player.IsGrounded;
                groundStateInitialized=true;
                cameraBobOffset=Mathf.Lerp(cameraBobOffset,0f,
                    1f-Mathf.Exp(-16f*Time.deltaTime));
                ApplySlidingPose(player.SlideNormal);
                ApplyRightHandThrowPose();
                ApplyArmAim(rightShoulder,rightElbow,rightHandMarker,
                    ref rightAimShoulderPose,ref rightAimElbowPose,ref rightArmAimActive,
                    0f,0f);
                if(freeRightHandReach)
                {
                    UpdateRightForearmReach();
                    UpdateRightHandPickup();
                }
                if(rightHandLiftCharge>0f&&HasRightHandItem)
                    ApplyArmAim(leftShoulder,leftElbow,leftHandMarker,
                        ref leftAimShoulderPose,ref leftAimElbowPose,ref leftArmAimActive,
                        LeftThrowAimLift,rightHandLiftCharge*
                        LeftThrowMaximumScreenCentering,
                        leftShoulderRestRotation,leftElbowRestRotation);
                else leftArmAimActive=false;
                SetSlideSmoke(true);
                ApplyRightHandObjectGripPose();
                ApplyRightHandPunchPose();
                ApplyRightHandStrikePose();
                if(striking||strikeCameraHandOffset.sqrMagnitude>0.000001f)
                    UpdateCameraPosition(cameraBobOffset);
                return;
            }
            rightArmAimActive=false;
            SetSlideSmoke(false);
            SetHandSmoke(false);
            SetRightHandSmoke(false);
            if (modelRoot != null)
                modelRoot.localRotation = Quaternion.Slerp(modelRoot.localRotation,
                    modelRestRotation, 1f - Mathf.Exp(-10f * Time.deltaTime));
            if (neck != null)
                neck.localRotation = Quaternion.Slerp(neck.localRotation, neckRestRotation,
                    1f - Mathf.Exp(-10f * Time.deltaTime));
            float yaw = player.LookYawOffset * 0.62f;
            // Follow camera pitch symmetrically so the waist bends both down and up.
            float pitch = Mathf.Clamp(player.LookPitch * 0.55f,-40f,40f);
            Quaternion look = Quaternion.AngleAxis(pitch, waistPitchAxis) *
                Quaternion.AngleAxis(yaw, waistYawAxis);
            waistNormalRotation=look*waistRestRotation;
            waist.localRotation = Quaternion.Slerp(waist.localRotation,
                waistNormalRotation, 1f - Mathf.Exp(-7f * Time.deltaTime));
            bool physicallyGrounded=player.IsGrounded;
            if(!groundStateInitialized)
            {
                wasGrounded=physicallyGrounded;
                groundStateInitialized=true;
            }
            bool justLanded=physicallyGrounded&&!wasGrounded;
            bool justTookOff=!physicallyGrounded&&wasGrounded;
            if(justTookOff)
            {
                airborneArmMovement=Mathf.Clamp01(player.Speed/6f);
                airborneArmWasSprinting=player.IsSprinting;
                airborneArmSwingActive=airborneArmMovement>0.05f;
            }
            if(physicallyGrounded)
            {
                airborneArmSwingActive=false;
                if(justLanded&&airborneTime>0.12f&&fastestFallSpeed>2f)
                {
                    landingShakeTimer=0.42f;
                    landingShakeStrength=Mathf.Lerp(0.0765f,0.187f,
                        Mathf.InverseLerp(2f,16f,fastestFallSpeed));
                }
                airborneTime=0f;
                fastestFallSpeed=0f;
            }
            else
            {
                airborneTime+=Time.deltaTime;
                fastestFallSpeed=Mathf.Max(fastestFallSpeed,-player.VerticalSpeed);
            }
            wasGrounded=physicallyGrounded;
            // Sprinting over faceted terrain can miss contact for a few frames.
            // Preserve the run cycle through that tiny gap, but an actual jump's
            // upward velocity bypasses this grace period immediately.
            bool sprintContactGrace=!physicallyGrounded&&player.IsSprinting&&player.Speed>1f&&
                airborneTime<=0.22f&&player.VerticalSpeed<=1.5f;
            bool grounded=physicallyGrounded||sprintContactGrace;
            bool airborneArmSwing=!grounded&&airborneArmSwingActive&&
                airborneTime<=FirstPersonController.AirborneSwingDuration;
            bool airborneLegSwing=airborneArmSwing;
            bool holdAirborneLegPose=!grounded&&!airborneLegSwing&&airborneTime<=1f;
            float movement=grounded?Mathf.Clamp01(player.Speed/6f):0f;
            Vector3 localMovement=player.LocalMovement;
            float strafe=grounded?Mathf.Clamp(localMovement.x/6f,-1f,1f):0f;
            // Preserve the takeoff gait for the same short airborne window as
            // the arms. Long falls still transition to an independent cycle.
            float airLegMovement=airborneLegSwing?airborneArmMovement:
                !grounded&&airborneTime>1f?
                    Mathf.SmoothStep(0f,1f,(airborneTime-1f)/0.25f):0f;
            float legMovement=Mathf.Max(movement,airLegMovement);
            bool sprinting=grounded&&player.IsSprinting;
            // Sprint is the walking animation with one uniform amplitude and
            // cadence multiplier; it has no separate legacy sprint pose.
            float sprintScale=sprinting?1.35f:1f;
            float legSprintScale=airborneLegSwing&&airborneArmWasSprinting?1.35f:sprintScale;
            float stride=legMovement*legSprintScale;
            float turning = player.IsGrounded
                ? Mathf.Clamp01(Mathf.Abs(player.LowerBodyTurnRate) / 75f)
                : 0f;
            if (legMovement > 0.01f)
            {
                float cycleSpeed=grounded?6f*1.45f*legMovement*(sprinting?1.25f:1f):
                    airborneLegSwing?6f*1.45f*legMovement*
                        (airborneArmWasSprinting?1.25f:1f):6f;
                stepPhase+=cycleSpeed*Time.deltaTime;
            }
            else if (turning > 0.01f)
                stepPhase += Mathf.Abs(player.LowerBodyTurnRate) * 0.04f * Time.deltaTime;
            float cycle = Mathf.Sin(stepPhase);
            float smoothing = 1f - Mathf.Exp(-16f * Time.deltaTime);
            float hipSwing=38f*legSprintScale;
            float armMovement=airborneArmSwing?airborneArmMovement:movement;
            float armSprintScale=airborneArmSwing&&airborneArmWasSprinting?1.35f:sprintScale;
            float armSwingScale=airborneArmSwing?0.7f:1f;
            float shoulderSwing=42f*armSprintScale*
                ((airborneArmSwing&&airborneArmWasSprinting)||sprinting?1.4f:1f);
            float idleStride=grounded?1f-movement:0f;
            float legSwing=cycle*(hipSwing*legMovement+11f*turning)+idleStride*7f;
            float legSpread=Mathf.Lerp(10f,0f,legMovement);
            // Restore the alternating lateral step. Its crossing half is much
            // smaller, while the outward half reaches slightly farther than the
            // original 30-degree swing.
            float lateralPhase=cycle*strafe;
            float lateralLegSwing=lateralPhase>=0f?
                lateralPhase*14f:lateralPhase*36f;
            bool crouching=player.IsCrouching&&grounded||lowPickupReach;
            bool standingIdle=grounded&&!crouching&&!airborneArmSwing&&
                player.PlanarSpeed<=0.05f;
            float crouchBend=crouching?45f:0f;
            bool idleCrouching=crouching&&player.PlanarSpeed<=0.05f;
            float crouchPoseSmoothing=lowPickupReach?
                1f-Mathf.Exp(-16f*PickupCrouchAnimationSpeedScale*Time.deltaTime):smoothing;
            float legCrouchPoseSmoothing=crouching?
                1f-Mathf.Exp(-16f*LegCrouchTransformSpeedScale*
                    (lowPickupReach?PickupCrouchAnimationSpeedScale:1f)*Time.deltaTime):smoothing;
            if(crouching&&!idleCrouching)
            {
                float crouchMovement=Mathf.Clamp01(player.PlanarSpeed/CrouchForwardSpeed);
                float crouchCycleScale=lowPickupReach?PickupCrouchAnimationSpeedScale:1f;
                crouchStepPhase+=Mathf.Lerp(2.2f,3.4f,crouchMovement)*
                    crouchCycleScale*Time.deltaTime;
            }
            else
                crouchStepPhase=0f;
            float crouchSwitch=Mathf.SmoothStep(0f,1f,
                0.5f-0.5f*Mathf.Cos(crouchStepPhase));
            float crouchForwardAmount=crouching?
                Mathf.Clamp01(Mathf.Abs(localMovement.z)/CrouchForwardSpeed):0f;
            float crouchLateralAmount=crouching?
                Mathf.Clamp(localMovement.x/CrouchLateralSpeed,-1f,1f):0f;
            float crouchStepAmount=Mathf.Max(crouchForwardAmount,
                Mathf.Abs(crouchLateralAmount));
            float idleLegBend=grounded&&movement<=0.01f?10f:0f;
            if(!holdAirborneLegPose)
            {
                if(standingIdle)
                {
                    PoseLocalEuler(leftHip,new Vector3(180f,-20f,3f),smoothing);
                    PoseLocalEuler(rightHip,new Vector3(175f,11f,-3f),smoothing);
                }
                else if(crouching)
                {
                    if(idleCrouching)
                    {
                        PoseLocalEuler(leftHip,new Vector3(130f,10f,20f),legCrouchPoseSmoothing);
                        PoseLocalEuler(rightHip,new Vector3(60f,0f,0f),legCrouchPoseSmoothing);
                    }
                    else
                    {
                        Quaternion leftCrouchA=Quaternion.Euler(-190f,0f,10f);
                        Quaternion leftCrouchB=Quaternion.Euler(120f,0f,0f);
                        Quaternion rightCrouchA=Quaternion.Euler(120f,0f,0f);
                        Quaternion rightCrouchB=Quaternion.Euler(-190f,0f,0f);
                        Quaternion leftAnimatedPose=Quaternion.Slerp(leftCrouchA,leftCrouchB,crouchSwitch);
                        Quaternion rightAnimatedPose=Quaternion.Slerp(rightCrouchA,rightCrouchB,crouchSwitch);
                        Quaternion leftHipPose=Quaternion.Slerp(
                            Quaternion.Euler(130f,10f,20f),leftAnimatedPose,
                            crouchStepAmount);
                        Quaternion rightHipPose=Quaternion.Slerp(
                            Quaternion.Euler(60f,0f,0f),rightAnimatedPose,
                            crouchStepAmount);
                        // Keep crouch strafing exactly mirrored in both directions.
                        // The former asymmetric curve made one half of rightward
                        // movement much larger than the other half.
                        float crouchLateralPhase=Mathf.Sin(crouchStepPhase)*crouchLateralAmount;
                        float crouchHorizontalSwing=crouchLateralPhase*35f*
                            CrouchHorizontalAnimationScale;
                        leftHipPose=Quaternion.AngleAxis(crouchHorizontalSwing,
                            leftHipSpreadAxis)*leftHipPose;
                        rightHipPose=Quaternion.AngleAxis(-crouchHorizontalSwing,
                            rightHipSpreadAxis)*rightHipPose;
                        PoseLocalRotation(leftHip,leftHipPose,legCrouchPoseSmoothing);
                        PoseLocalRotation(rightHip,rightHipPose,legCrouchPoseSmoothing);
                    }
                }
                else
                {
                    PoseBoneSpread(leftHip,leftHipRestRotation,leftHipPitchAxis,
                        legSwing+idleLegBend,leftHipSpreadAxis,
                        -legSpread+lateralLegSwing,smoothing);
                    PoseBoneSpread(rightHip,rightHipRestRotation,rightHipPitchAxis,
                        -legSwing+idleLegBend,rightHipSpreadAxis,
                        legSpread-lateralLegSwing,smoothing);
                }
            }
            float armSpread=grounded||airborneArmSwing?
                Mathf.Lerp(20f,30f,armMovement):70f;
            float leftStrafeArm=strafe>0f?-strafe*30f:-strafe*20f;
            float rightStrafeArm=strafe>0f?-strafe*20f:-strafe*30f;
            float leftAirborneArmLift=grounded||airborneArmSwing?0f:-55f;
            float rightAirborneArmLift=grounded||airborneArmSwing?0f:-65f;
            float leftShoulderPitch=-cycle*shoulderSwing*armMovement*armSwingScale-idleStride*7f+
                leftAirborneArmLift;
            float rightShoulderPitch=cycle*shoulderSwing*armMovement*armSwingScale+idleStride*7f+
                rightAirborneArmLift;
            float leftShoulderSpread=-armSpread+leftStrafeArm;
            float rightShoulderSpread=armSpread+rightStrafeArm;
            const float elbowBase=18f;
            const float elbowCurve=48f;
            float leftBend=(elbowBase+Mathf.Max(0f,cycle)*elbowCurve)*
                armMovement*armSprintScale*armSwingScale;
            float rightBend=(elbowBase+Mathf.Max(0f,-cycle)*elbowCurve)*
                armMovement*armSprintScale*armSwingScale;
            bool returningAfterThrow=returningRightHandAfterThrow&&
                Time.time>=throwFollowThroughUntil;
            float rightArmSmoothing=returningRightHandWithItem?
                1f-Mathf.Exp(-HeldItemReturnRate*Time.deltaTime):
                returningAfterThrow?1f-Mathf.Exp(-ThrownHandReturnRate*Time.deltaTime):smoothing;
            Quaternion rightShoulderTarget;
            Quaternion rightElbowTarget;
            if(standingIdle)
            {
                rightShoulderTarget=Quaternion.Euler(-183f,-3f,-3f);
                rightElbowTarget=Quaternion.Euler(-10f,3f,-3f);
                PoseLocalRotation(rightShoulder,rightShoulderTarget,rightArmSmoothing);
                PoseLocalRotation(rightElbow,rightElbowTarget,rightArmSmoothing);
                PoseLocalEuler(leftShoulder,new Vector3(173f,3f,3f),smoothing);
                PoseLocalEuler(leftElbow,new Vector3(-10f,-3f,3f),smoothing);
            }
            else if(crouching)
            {
                rightShoulderTarget=rightShoulderRestRotation;
                rightElbowTarget=rightElbowRestRotation;
                PoseLocalRotation(leftShoulder,leftShoulderRestRotation,crouchPoseSmoothing);
                PoseLocalRotation(rightShoulder,rightShoulderTarget,crouchPoseSmoothing);
                PoseLocalRotation(leftElbow,leftElbowRestRotation,crouchPoseSmoothing);
                PoseLocalRotation(rightElbow,rightElbowTarget,crouchPoseSmoothing);
            }
            else
            {
                rightShoulderTarget=Quaternion.AngleAxis(rightShoulderSpread,rightShoulderSpreadAxis)*
                    Quaternion.AngleAxis(rightShoulderPitch,rightShoulderPitchAxis)*rightShoulderRestRotation;
                rightElbowTarget=Quaternion.AngleAxis(-rightBend,rightElbowPitchAxis)*rightElbowRestRotation;
                PoseBoneSpread(leftShoulder,leftShoulderRestRotation,leftShoulderPitchAxis,
                    leftShoulderPitch,leftShoulderSpreadAxis,leftShoulderSpread,smoothing);
                PoseLocalRotation(rightShoulder,rightShoulderTarget,rightArmSmoothing);
                PoseBone(leftElbow,leftElbowRestRotation,leftElbowPitchAxis,-leftBend,smoothing);
                PoseLocalRotation(rightElbow,rightElbowTarget,rightArmSmoothing);
            }
            bool rightArmReachedTarget=
                Quaternion.Angle(rightShoulder.localRotation,rightShoulderTarget)<1f&&
                Quaternion.Angle(rightElbow.localRotation,rightElbowTarget)<1f;
            bool throwReturnTimedOut=returningAfterThrow&&
                Time.time-throwFollowThroughUntil>=MaximumThrownHandReturnDuration;
            if((returningRightHandWithItem&&rightArmReachedTarget)||
                (returningAfterThrow&&(rightArmReachedTarget||throwReturnTimedOut)))
            {
                returningRightHandWithItem=false;
                returningRightHandAfterThrow=false;
            }
            const float kneeBase=10f;
            const float kneeCurve=48f;
            float leftKneeBend=(kneeBase+Mathf.Max(0f,-cycle)*kneeCurve)*
                legMovement*legSprintScale+7f*turning+idleLegBend;
            float rightKneeBend=(kneeBase+Mathf.Max(0f,cycle)*kneeCurve)*
                legMovement*legSprintScale+7f*turning+idleLegBend;
            float ankleSwing = cycle * (24f * stride + 6f * turning);
            if(!holdAirborneLegPose)
            {
                if(standingIdle)
                {
                    PoseLocalEuler(leftKnee,new Vector3(153f,0f,0f),smoothing);
                    PoseLocalEuler(rightKnee,new Vector3(11f,0f,0f),smoothing);
                }
                else if(crouching)
                {
                    Quaternion leftKneeBase=Quaternion.Euler(120f,0f,0f);
                    Quaternion rightKneeBase=Quaternion.Euler(110f,0f,0f);
                    Quaternion leftKneeCycle=Quaternion.Slerp(leftKneeBase,
                        Quaternion.Euler(110f,0f,0f),crouchSwitch);
                    Quaternion rightKneeCycle=Quaternion.Slerp(rightKneeBase,
                        Quaternion.Euler(120f,0f,0f),crouchSwitch);
                    PoseLocalRotation(leftKnee,Quaternion.Slerp(leftKneeBase,leftKneeCycle,
                        crouchStepAmount),legCrouchPoseSmoothing);
                    PoseLocalRotation(rightKnee,Quaternion.Slerp(rightKneeBase,rightKneeCycle,
                        crouchStepAmount),legCrouchPoseSmoothing);
                }
                else
                {
                    PoseBone(leftKnee,leftKneeRestRotation,leftKneePitchAxis,
                        leftKneeBend,smoothing);
                    PoseBone(rightKnee,rightKneeRestRotation,rightKneePitchAxis,
                        rightKneeBend,smoothing);
                }
                if(standingIdle)
                {
                    PoseLocalRotation(leftAnkle,leftAnkleRestRotation,smoothing);
                    PoseLocalRotation(rightAnkle,rightAnkleRestRotation,smoothing);
                    UpdateFootCollider(leftFootCollider,leftAnkle);
                    UpdateFootCollider(rightFootCollider,rightAnkle);
                }
                else if(crouching)
                {
                    Quaternion leftAnkleBase=Quaternion.Euler(26f,0f,0f);
                    Quaternion rightAnkleBase=Quaternion.Euler(-70f,0f,0f);
                    Quaternion leftAnkleCycle=Quaternion.Slerp(leftAnkleBase,
                        Quaternion.Euler(-70f,0f,0f),crouchSwitch);
                    Quaternion rightAnkleCycle=Quaternion.Slerp(rightAnkleBase,
                        Quaternion.Euler(26f,0f,0f),crouchSwitch);
                    PoseLocalRotation(leftAnkle,Quaternion.Slerp(leftAnkleBase,leftAnkleCycle,
                        crouchStepAmount),legCrouchPoseSmoothing);
                    PoseLocalRotation(rightAnkle,Quaternion.Slerp(rightAnkleBase,rightAnkleCycle,
                        crouchStepAmount),legCrouchPoseSmoothing);
                    UpdateFootCollider(leftFootCollider,leftAnkle);
                    UpdateFootCollider(rightFootCollider,rightAnkle);
                }
                else
                {
                    PoseBone(leftAnkle,leftAnkleRestRotation,leftAnklePitchAxis,
                        ankleSwing+crouchBend,smoothing);
                    PoseBone(rightAnkle,rightAnkleRestRotation,rightAnklePitchAxis,
                        -ankleSwing+crouchBend,smoothing);
                    UpdateFootCollider(leftFootCollider,leftAnkle);
                    UpdateFootCollider(rightFootCollider,rightAnkle);
                }
            }
            float cameraShake=UpdateCameraShake(grounded,movement,sprinting);
            UpdatePickupPelvisPose();
            UpdateWaistReachBow();
            UpdateRightForearmReach();
            UpdateRightHandPickup();
            ApplyRightHandThrowPose();
            if(rightHandLiftCharge>0f&&HasRightHandItem)
                ApplyArmAim(leftShoulder,leftElbow,leftHandMarker,
                    ref leftAimShoulderPose,ref leftAimElbowPose,ref leftArmAimActive,
                    LeftThrowAimLift,rightHandLiftCharge*
                    LeftThrowMaximumScreenCentering,
                    leftShoulderRestRotation,leftElbowRestRotation);
            else leftArmAimActive=false;
            ApplyRightHandObjectGripPose();
            ApplyRightHandPunchPose();
            ApplyRightHandStrikePose();
            UpdateCameraPosition(cameraShake);
        }

        private void UpdateRightHandPickup()
        {
            if(!IsReaching||rightShoulder==null||rightElbow==null||rightHandMarker==null) return;
            if(pickupTarget!=null&&
                (pickupTarget.IsHeld||Vector3.Distance(rightShoulder.position,pickupTarget.AimPoint)>RightPickupRange))
            {
                CancelRightHandPickup();
                return;
            }
            Vector3 target=RightHandReachTarget();
            // Advance the desired hand point at the requested speed, then solve
            // the shoulder/elbow chain toward the continuously extending reach.
            pickupHandGoal=Vector3.MoveTowards(pickupHandGoal,target,
                RightHandReachSpeed*Time.deltaTime);
            rightShoulder.localRotation=pickupShoulderPose;
            rightElbow.localRotation=pickupElbowPose;
            for(int pass=0;pass<8;pass++)
            {
                AimJointAtTarget(rightElbow,rightHandMarker,pickupHandGoal,1f);
                AimJointAtTarget(rightShoulder,rightHandMarker,pickupHandGoal,1f);
            }
            if(pickupTarget==null) return;
            bool touching=rightHandPickupTrigger!=null&&
                rightHandPickupTrigger.IsTouching(pickupTarget);
            // The hand's pickup trigger decides contact. Distance alone can
            // never move an object into the hand.
            if(rightHandPickupTrigger==null||!touching) return;
            heldRightHandItem=pickupTarget;
            pickupTarget.PickUp(rightHandMarker);
            rightHandPickupTrigger.Forget(pickupTarget);
            rightElbow.localPosition=rightElbowRestPosition;
            rightHandMarker.localPosition=RightHandPointOnElbow;
            returningRightHandWithItem=true;
            pickupTarget=null;
            RestorePickupPosture();
        }

        private void ApplyRightHandObjectGripPose()
        {
            if(punching) return;
            bool reaching=IsReaching;
            bool holdingIrregular=heldRightHandItem!=null&&
                heldRightHandItem.GrabMode==PickableGrabMode.Regular&&
                heldRightHandItem.Label==PickableObjectLabel.Irregular;
            float blend=1f-Mathf.Exp(-16f*Time.deltaTime);
            for(int i=0;i<punchDigits.Length;i++)
            {
                Transform digit=punchDigits[i];
                if(digit==null) continue;
                Quaternion target=punchDigitRest[i];
                if(holdingIrregular)
                    target=Quaternion.Euler(IrregularGripDigitAngles[i]);
                else if(reaching&&i==0)
                    target=Quaternion.Euler(0f,0f,10f);
                else if(reaching&&i==1)
                    target=Quaternion.Euler(0f,0f,20f);
                digit.localRotation=Quaternion.Slerp(digit.localRotation,target,blend);
            }
        }

        private void UpdatePickupPelvisPose()
        {
            if(pelvis==null) return;
            Quaternion target=lowPickupReach?Quaternion.Euler(90f,0f,0f):
                pelvisRestRotation;
            float rate=lowPickupReach?
                PickupReachRate*PickupCrouchAnimationSpeedScale:HeldItemReturnRate;
            pelvis.localRotation=Quaternion.Slerp(pelvis.localRotation,target,
                1f-Mathf.Exp(-rate*Time.deltaTime));
        }

        private void UpdateWaistReachBow()
        {
            if(!IsReaching||waist==null) return;
            Vector3 localTarget=transform.InverseTransformDirection(
                RightHandReachTarget()-waist.position);
            float horizontalDistance=Mathf.Sqrt(localTarget.x*localTarget.x+
                localTarget.z*localTarget.z);
            float verticalReachAngle=Mathf.Atan2(-localTarget.y,
                Mathf.Max(0.01f,horizontalDistance))*Mathf.Rad2Deg;
            // Pickup reach has no artificial waist-angle cap in either direction.
            // Bend on local X and rotate on local Y toward the target; objects on
            // the player's right naturally produce the rig's expected negative Y rotation.
            float bow=(18f+verticalReachAngle)*1.4f;
            float turn=-Mathf.Atan2(localTarget.x,localTarget.z)*Mathf.Rad2Deg-30f;
            Vector3 normalEuler=waistNormalRotation.eulerAngles;
            Quaternion target=Quaternion.Euler(normalEuler.x+bow,
                normalEuler.y+turn,normalEuler.z);
            waist.localRotation=Quaternion.Slerp(waist.localRotation,target,
                1f-Mathf.Exp(-PickupReachRate*Time.deltaTime));
        }

        private void UpdateRightForearmReach()
        {
            if(striking) return;
            float stretch=1f;
            if(IsReaching)
            {
                float targetDistance=Vector3.Distance(rightShoulder.position,
                    RightHandReachTarget())-RightHandTriggerDiameter*0.5f;
                stretch=Mathf.Clamp(targetDistance/Mathf.Max(0.01f,rightArmReach),
                    1f,MaximumRightArmStretchScale);
            }
            float rate=IsReaching?PickupReachRate:HeldItemReturnRate;
            rightElbow.localPosition=Vector3.Lerp(rightElbow.localPosition,
                rightElbowRestPosition*stretch,1f-Mathf.Exp(-rate*Time.deltaTime));
            rightElbow.localScale=rightElbowRestScale;
            if(rightHandPickupTrigger!=null)
                rightHandPickupTrigger.SetWorldDiameter(RightHandTriggerDiameter);
        }

        private Vector3 RightHandReachTarget()
        {
            if(pickupTarget!=null) return pickupTarget.AimPoint;
            Camera view=player!=null?player.PlayerCamera:null;
            return view!=null?view.transform.position+view.transform.forward*RightPickupRange:
                rightShoulder.position+transform.forward*RightPickupRange;
        }

        private static Vector3 BoneAxis(Transform bone, Vector3 worldAxis) =>
            bone.parent.InverseTransformDirection(worldAxis).normalized;

        private void ApplySlidingPose(Vector3 normal)
        {
            float blend=1f-Mathf.Exp(-10f*Time.deltaTime);
            normal=normal.normalized;
            float slopeAngle=Vector3.Angle(Vector3.up,normal);
            float angle=slopeAngle*1.4f;
            // Rotate around the terrain's true slope axis. This keeps the body
            // aligned with changes in both slope direction and steepness, with
            // the requested 40% stronger terrain-relative lean.
            Vector3 worldAxis=Vector3.Cross(Vector3.up,normal);
            Vector3 bodyAxis=worldAxis.sqrMagnitude>0.0001f
                ? modelRoot.parent.InverseTransformDirection(worldAxis.normalized)
                : modelRoot.parent.InverseTransformDirection(transform.right);
            // A slide uses a fixed -70 degree X-facing posture, then layers the
            // terrain-relative lean over it. The capsule uses this same pose.
            Quaternion bodyTarget=Quaternion.AngleAxis(-70f,Vector3.right)*
                Quaternion.AngleAxis(angle,bodyAxis)*modelRestRotation;
            modelRoot.localRotation=Quaternion.Slerp(modelRoot.localRotation,bodyTarget,blend);
            // Forward flexion at the waist is deliberately limited to 20 degrees;
            // the separately rebound neck supplies the requested head bend.
            Quaternion target=Quaternion.AngleAxis(20f,waistPitchAxis)*
                Quaternion.AngleAxis(player.LookYawOffset*0.7f,waistYawAxis)*waistRestRotation;
            float upperBodyBlend=1f-Mathf.Exp(-1.6f*Time.deltaTime);
            waist.localRotation=Quaternion.Slerp(waist.localRotation,target,upperBodyBlend);
            if(neck!=null)
            {
                Quaternion neckTarget=Quaternion.AngleAxis(50f,neckPitchAxis)*neckRestRotation;
                neck.localRotation=Quaternion.Slerp(neck.localRotation,neckTarget,blend);
            }
            float side=player.SlideSteer;
            float legSide=side*8f;
            float armSide=side*14f;
            bool braking=player.IsSlideBraking;
            PoseBoneSpread(leftHip,leftHipRestRotation,leftHipPitchAxis,-50f,leftHipSpreadAxis,-8f+legSide,blend);
            PoseBoneSpread(rightHip,rightHipRestRotation,rightHipPitchAxis,-10f,rightHipSpreadAxis,8f+legSide,blend);
            PoseBone(leftKnee,leftKneeRestRotation,leftKneePitchAxis,50f,blend);
            PoseBone(rightKnee,rightKneeRestRotation,rightKneePitchAxis,10f,blend);
            PoseBone(leftAnkle,leftAnkleRestRotation,leftAnklePitchAxis,-12f,blend);
            PoseBone(rightAnkle,rightAnkleRestRotation,rightAnklePitchAxis,0f,blend);
            PoseBoneSpread(rightShoulder,rightShoulderRestRotation,rightShoulderPitchAxis,
                -65f,rightShoulderSpreadAxis,20f+armSide,blend);
            PoseBone(rightElbow,rightElbowRestRotation,rightElbowPitchAxis,0f,blend);
            PoseLocalEuler(leftShoulder,new Vector3(150f,10f,80f),blend);
            PoseLocalEuler(leftElbow,ElbowContactEuler(leftElbowRestRotation),blend);
            Vector3 handContact=Vector3.zero;
            bool handTouching=braking&&
                ApplyLeftHandGroundContact(out handContact);
            SetHandSmoke(handTouching,handContact);
            // The right arm remains free to track the view during a slide.
            SetRightHandSmoke(false);
            UpdateFootCollider(leftFootCollider,leftAnkle);
            UpdateFootCollider(rightFootCollider,rightAnkle);
            UpdateCameraPosition(cameraBobOffset);
        }

        private float UpdateCameraShake(bool grounded,float movement,bool sprinting)
        {
            float targetBob=0f;
            if(grounded&&movement>0.01f)
            {
                float cadence=sprinting?13f:8.5f;
                cameraBobPhase+=cadence*Mathf.Lerp(0.45f,1f,movement)*Time.deltaTime;
                float amplitude=0.0425f*movement*(sprinting?1.5f:1f);
                targetBob=Mathf.Sin(cameraBobPhase)*amplitude;
            }
            cameraBobOffset=Mathf.Lerp(cameraBobOffset,targetBob,
                1f-Mathf.Exp(-16f*Time.deltaTime));

            float landingOffset=0f;
            if(landingShakeTimer>0f)
            {
                const float duration=0.42f;
                float remaining=Mathf.Clamp01(landingShakeTimer/duration);
                float progress=1f-remaining;
                landingOffset=-Mathf.Sin(progress*Mathf.PI*3f)*
                    landingShakeStrength*remaining;
                landingShakeTimer=Mathf.Max(0f,landingShakeTimer-Time.deltaTime);
            }
            return cameraBobOffset+landingOffset;
        }

        private void UpdateCameraPosition(float verticalOffset)
        {
            if(headCameraAnchor==null||cameraTransform==null) return;
            Vector3 handOffset=Vector3.zero;
            if(IsStrikeMode&&rightHandMarker!=null)
            {
                // Isolate arm motion from walking and head movement.
                Vector3 handMotion=headCameraAnchor.InverseTransformPoint(
                    rightHandMarker.position)-strikeStartHandLocalPosition;
                handOffset=headCameraAnchor.TransformVector(handMotion)*
                    StrikeCameraHandFollow;
            }
            strikeCameraHandOffset=Vector3.Lerp(strikeCameraHandOffset,handOffset,
                1f-Mathf.Exp(-StrikeArmSmoothingRate*Time.deltaTime));
            cameraTransform.position=headCameraAnchor.position+
                headCameraAnchor.forward*CameraForwardOffset+Vector3.up*verticalOffset+
                strikeCameraHandOffset;
        }

        private void CreateHandMarkers()
        {
            leftHandMarker=CreateHandMarker("Left Hand Ground Contact",leftShoulder,
                leftElbow,out leftArmReach);
            rightHandMarker=CreateHandMarker("Right Hand Ground Contact",rightShoulder,
                rightElbow,out rightArmReach);
            rightHandMarker.localPosition=RightHandPointOnElbow;
            rightArmReach=Vector3.Distance(rightShoulder.position,rightHandMarker.position);
            SphereCollider pickupCollider=rightHandMarker.gameObject.AddComponent<SphereCollider>();
            pickupCollider.isTrigger=true;
            rightHandPickupTrigger=rightHandMarker.gameObject.AddComponent<RightHandPickupTrigger>();
            rightHandPickupTrigger.Initialize(pickupCollider,RightHandTriggerDiameter);
            GameObject punchContact=new("Right Hand Punch Contact");
            punchContact.transform.SetParent(rightHandMarker,false);
            SphereCollider punchCollider=punchContact.AddComponent<SphereCollider>();
            punchCollider.isTrigger=true;
            rightHandPunchContactTrigger=punchContact.AddComponent<RightHandPunchContactTrigger>();
            rightHandPunchContactTrigger.Initialize(punchCollider,transform,
                RightHandPunchColliderDiameter,OnRightHandPunchContact);
        }

        private Transform CreateHandMarker(string markerName,Transform shoulder,
            Transform elbow,out float reach)
        {
            Vector3 upperArm=elbow.position-shoulder.position;
            float forearmLength=Mathf.Max(0.18f,upperArm.magnitude*0.92f);
            Vector3 forearmDirection=upperArm.sqrMagnitude>0.0001f?
                upperArm.normalized:-transform.up;
            Transform marker=new GameObject(markerName).transform;
            marker.position=elbow.position+forearmDirection*forearmLength;
            marker.rotation=elbow.rotation;
            marker.SetParent(elbow,true);
            reach=upperArm.magnitude+forearmLength;
            return marker;
        }

        private bool ApplyLeftHandGroundContact(out Vector3 contact)
        {
            return ApplyHandGroundContact(leftShoulder,leftElbow,leftHandMarker,
                leftArmReach,out contact);
        }

        private bool ApplyRightHandGroundContact(out Vector3 contact)
        {
            return ApplyHandGroundContact(rightShoulder,rightElbow,rightHandMarker,
                rightArmReach,out contact);
        }

        private bool ApplyHandGroundContact(Transform shoulder,Transform elbow,
            Transform handMarker,float armReach,out Vector3 contact)
        {
            contact=handMarker!=null?handMarker.position:transform.position;
            if(handMarker==null||terrain==null)
            {
                if(terrain==null) terrain=FindFirstObjectByType<ProceduralMap>();
                return false;
            }
            Vector3 handPosition=handMarker.position;
            if(!terrain.TryGetSurface(handPosition.x,handPosition.z,out RaycastHit hit))
                return false;

            Vector3 target=hit.point+hit.normal*0.025f;
            Vector3 shoulderToTarget=target-shoulder.position;
            if(shoulderToTarget.magnitude>armReach*1.08f)
                target=shoulder.position+shoulderToTarget.normalized*(armReach*1.08f);

            float solveBlend=1f-Mathf.Exp(-14f*Time.deltaTime);
            for(int pass=0;pass<2;pass++)
            {
                AimJointAtTarget(elbow,handMarker,target,solveBlend);
                AimJointAtTarget(shoulder,handMarker,target,solveBlend);
            }
            contact=hit.point+hit.normal*0.03f;
            return Vector3.Distance(handMarker.position,contact)<0.16f;
        }

        private static void AimJointAtTarget(Transform joint,Transform endpoint,
            Vector3 target,float blend)
        {
            Vector3 current=endpoint.position-joint.position;
            Vector3 desired=target-joint.position;
            if(current.sqrMagnitude<0.0001f||desired.sqrMagnitude<0.0001f) return;
            Quaternion correction=Quaternion.FromToRotation(current,desired);
            joint.rotation=Quaternion.Slerp(joint.rotation,correction*joint.rotation,blend);
        }

        private void ApplyArmAim(Transform shoulder,Transform elbow,Transform handMarker,
            ref Quaternion smoothedShoulderPose,ref Quaternion smoothedElbowPose,
            ref bool aimActive,float upwardLift,float screenCentering,
            Quaternion? isolatedShoulderPose=null,Quaternion? isolatedElbowPose=null)
        {
            if(cameraTransform==null||shoulder==null||elbow==null||handMarker==null) return;
            Vector3 aimDirection=(cameraTransform.forward+Vector3.up*upwardLift).normalized;
            Vector3 screenCenter=cameraTransform.position+
                cameraTransform.forward*ArmScreenCenterDistance;
            Vector3 centeredDirection=(screenCenter-shoulder.position).normalized;
            aimDirection=Vector3.Slerp(aimDirection,centeredDirection,
                Mathf.Clamp01(screenCentering));
            Vector3 target=cameraTransform.position+aimDirection*ArmAimDistance;
            Quaternion startingShoulderPose=shoulder.localRotation;
            Quaternion startingElbowPose=elbow.localRotation;
            // The lifting left arm must aim independently of the locomotion pose.
            // Solve from its neutral rig rotations so walk/sprint oscillation cannot
            // alter the cached target, while the aim cache provides its own smoothing.
            if(isolatedShoulderPose.HasValue) shoulder.localRotation=isolatedShoulderPose.Value;
            if(isolatedElbowPose.HasValue) elbow.localRotation=isolatedElbowPose.Value;
            // Solve an exact pose temporarily, then restore the animated pose so
            // the cached aim pose can ease toward the solution without snapping.
            for(int pass=0;pass<4;pass++)
            {
                AimJointAtTarget(elbow,handMarker,target,1f);
                AimJointAtTarget(shoulder,handMarker,target,1f);
            }
            Quaternion targetShoulderPose=shoulder.localRotation;
            Quaternion targetElbowPose=elbow.localRotation;
            shoulder.localRotation=startingShoulderPose;
            elbow.localRotation=startingElbowPose;
            if(!aimActive)
            {
                smoothedShoulderPose=startingShoulderPose;
                smoothedElbowPose=startingElbowPose;
                aimActive=true;
            }
            float blend=1f-Mathf.Exp(-ArmAimSmoothingRate*Time.deltaTime);
            smoothedShoulderPose=Quaternion.Slerp(smoothedShoulderPose,
                targetShoulderPose,blend);
            smoothedElbowPose=Quaternion.Slerp(smoothedElbowPose,targetElbowPose,blend);
            shoulder.localRotation=smoothedShoulderPose;
            elbow.localRotation=smoothedElbowPose;
        }

        private static void PoseBone(Transform bone, Quaternion rest, Vector3 axis,
            float angle, float smoothing)
        {
            Quaternion target = Quaternion.AngleAxis(angle, axis) * rest;
            bone.localRotation = Quaternion.Slerp(bone.localRotation, target, smoothing);
        }

        private static void PoseBoneSpread(Transform bone,Quaternion rest,Vector3 pitchAxis,
            float pitch,Vector3 spreadAxis,float spread,float smoothing)
        {
            Quaternion target=Quaternion.AngleAxis(spread,spreadAxis)*
                Quaternion.AngleAxis(pitch,pitchAxis)*rest;
            bone.localRotation=Quaternion.Slerp(bone.localRotation,target,smoothing);
        }

        private static void PoseLocalEuler(Transform bone,Vector3 localEuler,float smoothing)
        {
            bone.localRotation=Quaternion.Slerp(bone.localRotation,
                Quaternion.Euler(localEuler),smoothing);
        }

        private static void PoseLocalRotation(Transform bone,Quaternion localRotation,
            float smoothing)
        {
            bone.localRotation=Quaternion.Slerp(bone.localRotation,localRotation,smoothing);
        }

        private static Vector3 ElbowContactEuler(Quaternion rest)
        {
            float x=rest.eulerAngles.x;
            if(x>180f) x-=360f;
            return new Vector3(x,-10f,-60f);
        }

        private void CreateFootColliders()
        {
            leftFootCollider = CreateFootCollider("Left Foot BoxCollider");
            rightFootCollider = CreateFootCollider("Right Foot BoxCollider");
            leftAnkleToFootColliderRotation=Quaternion.Inverse(leftAnkle.rotation)*
                transform.rotation;
            rightAnkleToFootColliderRotation=Quaternion.Inverse(rightAnkle.rotation)*
                transform.rotation;
            UpdateFootCollider(leftFootCollider,leftAnkle);
            UpdateFootCollider(rightFootCollider,rightAnkle);
        }

        private Transform CreateFootCollider(string colliderName)
        {
            GameObject foot = new(colliderName);
            foot.layer = 2; // Ignore Raycast prevents self-hits in ground probes.
            foot.transform.SetParent(transform, false);
            BoxCollider collider = foot.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.1f,0.04f,0.26f);
            collider.center = new Vector3(0f,-0.03f,0f);
            return foot.transform;
        }

        private void UpdateFootCollider(Transform footCollider,Transform ankle)
        {
            if(footCollider==null||ankle==null) return;
            Quaternion ankleOffset=ankle==leftAnkle?leftAnkleToFootColliderRotation:
                rightAnkleToFootColliderRotation;
            Quaternion worldRotation=ankle.rotation*ankleOffset;
            Quaternion localRotation=Quaternion.Inverse(transform.rotation)*worldRotation;
            Vector3 anklePosition=transform.InverseTransformPoint(ankle.position);
            footCollider.localRotation=localRotation;
            footCollider.localPosition=anklePosition+
                localRotation*new Vector3(0f,-0.025f,0.07f);
        }

        private void HideHeadFromPlayerCamera(GameObject model)
        {
            SkinnedMeshRenderer head = model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .FirstOrDefault(renderer => renderer.name == "HumanHead");
            if (head == null) return;

            // Keep a shadow-only copy on the normal layer, while the visible head
            // uses a layer excluded only by the first-person camera.
            GameObject shadowObject = new("HumanHead Shadow");
            shadowObject.transform.SetParent(head.transform, false);
            SkinnedMeshRenderer shadow = shadowObject.AddComponent<SkinnedMeshRenderer>();
            shadow.sharedMesh = head.sharedMesh;
            shadow.rootBone = head.rootBone;
            shadow.bones = head.bones;
            shadow.sharedMaterials = head.sharedMaterials;
            shadow.localBounds = head.localBounds;
            shadow.updateWhenOffscreen = true;
            shadow.receiveShadows = false;
            shadow.shadowCastingMode = ShadowCastingMode.ShadowsOnly;

            head.gameObject.layer = FirstPersonHiddenLayer;
            player.PlayerCamera.cullingMask &= ~(1 << FirstPersonHiddenLayer);
        }

        private void AttachCameraToHeadPosition(GameObject model)
        {
            SkinnedMeshRenderer head = model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .FirstOrDefault(renderer => renderer.name == "HumanHead");
            if (head == null) throw new System.InvalidOperationException("Missing player head mesh.");

            // The head mesh is weighted only to Waist. This anchor therefore
            // follows the new neck bend as well as the waist and terrain tilt.
            headCameraAnchor = new GameObject("Head Camera Position").transform;
            headCameraAnchor.SetParent(neck != null ? neck : waist, false);
            headCameraAnchor.position = head.transform.TransformPoint(head.sharedMesh.bounds.center);
            cameraTransform = player.PlayerCamera.transform;
            cameraTransform.position = headCameraAnchor.position +
                headCameraAnchor.forward * CameraForwardOffset;
        }

        private static Transform FindChild(Transform parent, string childName)
        {
            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
                if (child.name == childName) return child;
            return null;
        }

        private void OnDestroy()
        {
            if (Application.isPlaying)
            {
                if (material != null) Destroy(material);
                if (runtimeHeadMesh != null) Destroy(runtimeHeadMesh);
                if (slideSmokeMaterial != null) Destroy(slideSmokeMaterial);
                if (slideSmokeTexture != null) Destroy(slideSmokeTexture);
            }
            else
            {
                if (material != null) DestroyImmediate(material);
                if (runtimeHeadMesh != null) DestroyImmediate(runtimeHeadMesh);
                if (slideSmokeMaterial != null) DestroyImmediate(slideSmokeMaterial);
                if (slideSmokeTexture != null) DestroyImmediate(slideSmokeTexture);
            }
        }

        private void CreateSlideSmoke()
        {
            GameObject smokeObject=new("Left Foot Slide Smoke");
            smokeObject.transform.SetParent(transform,false);
            smokeObject.transform.localPosition=new Vector3(0f,0.12f,0f);
            slideSmoke=smokeObject.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main=slideSmoke.main;
            main.loop=true;
            main.playOnAwake=false;
            main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.maxParticles=48;
            main.startLifetime=new ParticleSystem.MinMaxCurve(0.65f,1.1f);
            main.startSpeed=new ParticleSystem.MinMaxCurve(0.15f,0.55f);
            main.startSize=new ParticleSystem.MinMaxCurve(0.24f,0.52f);
            main.startRotation=new ParticleSystem.MinMaxCurve(0f,Mathf.PI*2f);
            main.startColor=new ParticleSystem.MinMaxGradient(
                new Color(0.30f,0.15f,0.07f,0.34f),
                new Color(0.50f,0.28f,0.12f,0.22f));

            ParticleSystem.EmissionModule emission=slideSmoke.emission;
            emission.enabled=false;
            emission.rateOverTime=26f;
            ParticleSystem.ShapeModule shape=slideSmoke.shape;
            shape.shapeType=ParticleSystemShapeType.Hemisphere;
            shape.radius=0.22f;
            ParticleSystem.VelocityOverLifetimeModule velocity=slideSmoke.velocityOverLifetime;
            velocity.enabled=true;
            velocity.space=ParticleSystemSimulationSpace.World;
            velocity.x=new ParticleSystem.MinMaxCurve(-0.08f,0.08f);
            velocity.y=new ParticleSystem.MinMaxCurve(0.18f,0.42f);
            velocity.z=new ParticleSystem.MinMaxCurve(-0.08f,0.08f);
            ParticleSystem.NoiseModule noise=slideSmoke.noise;
            noise.enabled=true;
            noise.quality=ParticleSystemNoiseQuality.Low;
            noise.strength=0.14f;
            noise.frequency=0.65f;

            slideSmokeTexture=new Texture2D(8,8,TextureFormat.RGBA32,false)
            {
                name="8x8 Pixelated Slide Smoke",
                filterMode=FilterMode.Point,
                wrapMode=TextureWrapMode.Clamp
            };
            Color[] pixels=new Color[64];
            for(int y=0;y<8;y++)
                for(int x=0;x<8;x++)
                {
                    float dx=(x-3.5f)/3.5f;
                    float dy=(y-3.5f)/3.5f;
                    float distance=Mathf.Sqrt(dx*dx+dy*dy);
                    float alpha=distance<0.58f?1f:distance<0.9f?0.42f:0f;
                    pixels[y*8+x]=new Color(1f,1f,1f,alpha);
                }
            slideSmokeTexture.SetPixels(pixels);
            slideSmokeTexture.Apply();

            // Loading a shader asset from Resources prevents player builds from
            // stripping it. A missing optional smoke shader must never abort the
            // rest of the world bootstrap.
            Shader smokeShader=Resources.Load<Shader>("SlideSmoke");
            if(smokeShader==null)
            {
                Debug.LogWarning("SlideSmoke shader is missing; sliding smoke is disabled.");
                Destroy(smokeObject);
                slideSmoke=null;
                return;
            }
            slideSmokeMaterial=new Material(smokeShader){name="Pixelated Brown Slide Smoke"};
            slideSmokeMaterial.SetTexture("_BaseMap",slideSmokeTexture);
            ColorOnlyTextureQuality.Apply(slideSmokeMaterial);
            ParticleSystemRenderer smokeRenderer=smokeObject.GetComponent<ParticleSystemRenderer>();
            smokeRenderer.sharedMaterial=slideSmokeMaterial;
            smokeRenderer.renderMode=ParticleSystemRenderMode.Billboard;
            smokeRenderer.sortMode=ParticleSystemSortMode.Distance;
        }

        private void CreateRightFootSlideSmoke()
        {
            if(slideSmoke==null) return;
            GameObject smokeObject=Instantiate(slideSmoke.gameObject,transform);
            smokeObject.name="Right Foot Slide Smoke";
            rightFootSlideSmoke=smokeObject.GetComponent<ParticleSystem>();
            rightFootSlideSmoke.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void CreateHandSmoke()
        {
            if(slideSmoke==null) return;
            GameObject handSmokeObject=Instantiate(slideSmoke.gameObject,transform);
            handSmokeObject.name="Left Hand Contact Smoke";
            handSmoke=handSmokeObject.GetComponent<ParticleSystem>();
            handSmoke.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main=handSmoke.main;
            main.maxParticles=24;
            main.startLifetime=new ParticleSystem.MinMaxCurve(0.4f,0.75f);
            main.startSize=new ParticleSystem.MinMaxCurve(0.12f,0.3f);
            ParticleSystem.EmissionModule emission=handSmoke.emission;
            emission.enabled=false;
            emission.rateOverTime=18f;
            ParticleSystem.ShapeModule shape=handSmoke.shape;
            shape.radius=0.09f;

            GameObject rightSmokeObject=Instantiate(handSmoke.gameObject,transform);
            rightSmokeObject.name="Right Hand Contact Smoke";
            rightHandSmoke=rightSmokeObject.GetComponent<ParticleSystem>();
            rightHandSmoke.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.EmissionModule rightEmission=rightHandSmoke.emission;
            rightEmission.enabled=false;
        }

        private void SetSlideSmoke(bool active)
        {
            SetFootSlideSmoke(slideSmoke,leftFootCollider,active);
            SetFootSlideSmoke(rightFootSlideSmoke,rightFootCollider,active);
        }

        private void SetFootSlideSmoke(ParticleSystem smoke,Transform foot,bool active)
        {
            if(smoke==null) return;
            if(active&&foot!=null)
            {
                BoxCollider footBox=foot.GetComponent<BoxCollider>();
                Vector3 soleLocal=footBox!=null?
                    footBox.center+Vector3.down*footBox.size.y*0.5f:
                    new Vector3(0f,-0.05f,0f);
                Vector3 position=foot.TransformPoint(soleLocal);
                if(terrain!=null&&terrain.TryGetSurface(position.x,position.z,out RaycastHit hit)&&
                    Mathf.Abs(hit.point.y-position.y)<1.25f)
                    position=hit.point+hit.normal*0.02f;
                smoke.transform.position=position;
            }
            ParticleSystem.EmissionModule emission=smoke.emission;
            emission.enabled=active;
            if(active&&!smoke.isPlaying) smoke.Play();
            else if(!active&&smoke.isEmitting)
                smoke.Stop(true,ParticleSystemStopBehavior.StopEmitting);
        }

        private void SetHandSmoke(bool active,Vector3 position=default)
        {
            SetContactSmoke(handSmoke,active,position);
        }

        private void SetRightHandSmoke(bool active,Vector3 position=default)
        {
            SetContactSmoke(rightHandSmoke,active,position);
        }

        private static void SetContactSmoke(ParticleSystem smoke,bool active,Vector3 position)
        {
            if(smoke==null) return;
            if(active) smoke.transform.position=position;
            ParticleSystem.EmissionModule emission=smoke.emission;
            emission.enabled=active;
            if(active&&!smoke.isPlaying) smoke.Play();
            else if(!active&&smoke.isEmitting)
                smoke.Stop(true,ParticleSystemStopBehavior.StopEmitting);
        }

        private void CreateNeck(GameObject model)
        {
            SkinnedMeshRenderer head = model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .FirstOrDefault(renderer => renderer.name == "HumanHead");
            if (head == null || head.sharedMesh == null) return;

            Transform[] bones = head.bones;
            Matrix4x4[] bindposes = head.sharedMesh.bindposes;
            int waistIndex = System.Array.IndexOf(bones, waist);
            if (waistIndex < 0 || waistIndex >= bindposes.Length) return;

            Bounds bounds = head.sharedMesh.bounds;
            Vector3 neckPoint = bounds.center;
            neckPoint.y = bounds.min.y + bounds.size.y * 0.12f;
            neck = new GameObject("Neck").transform;
            neck.position = head.transform.TransformPoint(neckPoint);
            neck.rotation = waist.rotation;
            neck.SetParent(waist, true);
            neckRestRotation = neck.localRotation;
            neckPitchAxis = BoneAxis(neck, transform.right);

            runtimeHeadMesh = Instantiate(head.sharedMesh);
            runtimeHeadMesh.name = head.sharedMesh.name + " Runtime Neck";
            Matrix4x4[] updatedBindposes = (Matrix4x4[])bindposes.Clone();
            bones[waistIndex] = neck;
            updatedBindposes[waistIndex] = neck.worldToLocalMatrix * head.transform.localToWorldMatrix;
            runtimeHeadMesh.bindposes = updatedBindposes;
            head.sharedMesh = runtimeHeadMesh;
            head.bones = bones;
        }
    }

    /// <summary>Reports physical collider contact at the centre of the right hand.</summary>
    public sealed class RightHandPickupTrigger : MonoBehaviour
    {
        private readonly System.Collections.Generic.Dictionary<PickableObject,int> contacts=new();
        private SphereCollider trigger;

        public void Initialize(SphereCollider value,float worldDiameter)
        {
            trigger=value;
            SetWorldDiameter(worldDiameter);
        }

        public void SetWorldDiameter(float diameter)
        {
            if(trigger==null) return;
            Vector3 scale=transform.lossyScale;
            float largest=Mathf.Max(0.0001f,Mathf.Max(Mathf.Abs(scale.x),
                Mathf.Max(Mathf.Abs(scale.y),Mathf.Abs(scale.z))));
            trigger.radius=diameter*0.5f/largest;
        }

        public bool IsTouching(PickableObject target)
        {
            if(target==null||trigger==null) return false;
            Vector3 centre=transform.TransformPoint(trigger.center);
            Vector3 scale=transform.lossyScale;
            float radius=trigger.radius*Mathf.Max(Mathf.Abs(scale.x),
                Mathf.Max(Mathf.Abs(scale.y),Mathf.Abs(scale.z)));
            bool touching=target.PickupDetectionTouchesSphere(centre,radius);
            if(!touching) contacts.Remove(target);
            return touching;
        }

        public void Forget(PickableObject target)
        {
            if(target!=null) contacts.Remove(target);
        }

        private void OnTriggerEnter(Collider other)
        {
            if(other==null) return;
            PickableObject target=other.GetComponentInParent<PickableObject>();
            if(target==null||target.IsHeld||!target.IsPickupDetectionCollider(other)) return;
            contacts.TryGetValue(target,out int count);
            contacts[target]=count+1;
        }

        private void OnTriggerExit(Collider other)
        {
            if(other==null) return;
            PickableObject target=other.GetComponentInParent<PickableObject>();
            if(target==null||!target.IsPickupDetectionCollider(other)||
                !contacts.TryGetValue(target,out int count)) return;
            if(count<=1) contacts.Remove(target);
            else contacts[target]=count-1;
        }
    }

    /// <summary>Detects solid objects touched by the hand during a punch.</summary>
    public sealed class RightHandPunchContactTrigger : MonoBehaviour
    {
        private SphereCollider trigger;
        private Transform playerRoot;
        private System.Action<Collider> contact;

        public void Initialize(SphereCollider value,Transform owner,float worldDiameter,
            System.Action<Collider> callback)
        {
            trigger=value;
            playerRoot=owner;
            contact=callback;
            Vector3 scale=transform.lossyScale;
            float largest=Mathf.Max(0.0001f,Mathf.Max(Mathf.Abs(scale.x),
                Mathf.Max(Mathf.Abs(scale.y),Mathf.Abs(scale.z))));
            trigger.radius=worldDiameter*0.5f/largest;
            SetActive(false);
        }

        public void SetActive(bool active)
        {
            if(trigger!=null) trigger.enabled=active;
        }

        private void OnTriggerEnter(Collider other) => Report(other);
        private void OnTriggerStay(Collider other) => Report(other);

        private void Report(Collider other)
        {
            if(other==null||other.isTrigger||playerRoot==null||
                other.transform.IsChildOf(playerRoot)) return;
            contact?.Invoke(other);
        }
    }
}
