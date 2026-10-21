using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wyvernland
{
    // Loads the rigged player model and supplies the temporary procedural look
    // and walk cycle until authored animation clips replace it.
    public sealed class RiggedPlayerAvatar : MonoBehaviour
    {
        internal static readonly Color PlayerColor = new(0.19f,0.22f,0.20f);
        private const int FirstPersonHiddenLayer = 31;
        private const float CameraForwardOffset = 0.08f;
        private const float PickupRangeMetres = 5.04f;
        private const float RightHandTriggerDiameter = 0.3136f;
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
        private const float HeavyHeldItemReturnSpeedScale = 0.5f;
        private const float TwoHandReturnSpeedScale = 0.25f;
        private const float InitialHeldArmSwingScale = 0.8f;
        private const float MaximumHeldArmSwingReduction = 0.7f;
        private const float HeldArmSwingSpeedAt60Kg = 0.99f;
        private const float TwoHandGripOffset = 0.1f;
        private const float FullSpeedPickupMassKg = 2f;
        private const float ThrowAnimationDuration =
            0.6f/RightArmAnimationSpeedScale/ThrowSpeedIncrease/
            AdditionalThrowSpeedIncrease/ThrowAnimationSpeedMultiplier;
        private const float ThrownHandReturnRate = 16f*RightArmAnimationSpeedScale;
        private const float MaximumThrownHandReturnDuration = 0.6f/RightArmAnimationSpeedScale;
        private const float ArmAimDistance = 50f;
        private const float ArmAimSmoothingRate = 10f;
        private const float StrikeShortPressSeconds = 0.7f;
        private const float MaximumStrikeWaveBonus = 0.5f;
        private const float StrikeWaveBoostBuildSeconds = 1.5f;
        private const float StrikeWaveBoostFadeSeconds = 0.6f;
        private const float StrikeWaveSpeedDipGraceSeconds = 0.2f;
        private const float StrikeArmStretchScale = 1.08f;
        private const float StrikeReturnSeconds = 0.2f;
        private const float StrikeReboundSeconds = 0.12f;
        private const float MaximumStrikeReboundSpeed = 6f;
        private const float MaximumStrikeReboundTravel = 0.2f;
        private const float StrikeArmSmoothingRate = 12f;
        private const float StrikeCameraHandFollow = 0.035f;
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
        private Transform leftWrist;
        private Transform rightWrist;
        private Vector3 leftWristRestPosition;
        private Vector3 rightWristRestPosition;
        private Mesh runtimeWristMesh;
        private readonly Transform[] punchDigits=new Transform[5];
        private readonly Quaternion[] punchDigitRest=new Quaternion[5];
        private readonly Transform[] leftGripDigits=new Transform[5];
        private readonly Quaternion[] leftGripDigitRest=new Quaternion[5];
        private readonly Quaternion[] punchDigitStart=new Quaternion[5];
        private static readonly Vector3[] PunchDigitAngles={
            new(0f,0f,-24f),new(0f,0f,75f),new(0f,0f,80f),
            new(-40f,0f,15f),new(55f,0f,0f)};
        private static readonly Vector3[] IrregularGripDigitAngles={
            new(0f,0f,26f),new(0f,0f,33f),new(0f,0f,42f),
            new(-27f,-30f,28f),new(45f,0f,0f)};
        private bool punching;
        private bool punchUsingLeftHand;
        private bool striking;
        private bool strikeEnding;
        private bool strikeRebounding;
        private bool strikeDestroyedSomething;
        private bool strikeUsingLeftHand;
        private bool auxiliaryStrike,auxiliaryStrikeLeft,auxiliaryStrikeHeld,auxiliaryStrikeEnding;
        private bool auxiliaryRebounding;
        private float auxiliaryReboundStartedAt;
        private Vector3 auxiliaryReboundStart,auxiliaryReboundVelocity;
        private Quaternion auxiliaryReboundShoulder,auxiliaryReboundElbow,auxiliaryReboundWrist;
        private float auxiliaryStrikeStarted,auxiliaryStrikeEnded;
        private float auxiliaryStrikeFatigue;
        private Quaternion auxiliaryShoulderStart,auxiliaryElbowStart,auxiliaryWristStart;
        private readonly HeldWeaponSwing auxiliaryWeaponSwing=new();
        private bool strikeAttackHeld;
        private float strikeStartedAt;
        private float strikeReturnStartedAt;
        private float strikeReboundStartedAt;
        private Vector3 strikeReboundStart, strikeReboundVelocity;
        private float strikeInputYawBeforeFrame,strikeInputPitchBeforeFrame;
        private bool strikeContactConstrained;
        private Quaternion strikeReboundShoulder;
        private Quaternion strikeReboundElbow;
        private Quaternion strikeReboundWrist;
        private float strikeHandYaw;
        private float strikeHandPitch;
        private readonly HeldWeaponSwing weaponSwing=new();
        private Quaternion strikeReadyShoulder,strikeReadyElbow;
        private Quaternion strikeWristRest,strikeReturnWrist;
        private Vector3 strikeShaftInHand;
        private Vector3 strikeReturnElbowPosition;
        private float strikeWaveBonus;
        private float strikeWaveBelowSpeedElapsed;
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
        private RightHandPickupTrigger leftHandPickupTrigger;
        private RightHandPickupTrigger rightHandPickupTrigger;
        private RightHandPunchContactTrigger rightHandPunchContactTrigger;
        private PickableObject pickupTarget;
        private Vector3 pickupGripLocal;
        private bool pickupUsingLeftHand;
        private bool pickupUsingBothHands;
        private bool pickupDragging;
        private bool freeLeftHandReach;
        private bool freeRightHandReach;
        private PickableObject heldLeftHandItem;
        private PickableObject heldRightHandItem;
        private bool lowPickupReach;
        private bool reachInputHeld;
        private bool returningRightHandWithItem;
        private bool returningLeftHandWithItem;
        private float heldItemReturnRate=HeldItemReturnRate;
        private float leftHeldItemReturnRate=HeldItemReturnRate;
        private bool twoHandPrimaryLeft;
        private bool returningRightHandAfterThrow;
        private bool returningLeftHandAfterThrow;
        private Quaternion pickupShoulderPose;
        private Quaternion pickupElbowPose;
        private Vector3 pickupHandGoal;
        private Quaternion leftPickupShoulderPose;
        private Quaternion leftPickupElbowPose;
        private Vector3 leftPickupHandGoal;
        private Vector3 leftElbowRestPosition;
        private Vector3 leftHandRestPosition;
        private Vector3 leftElbowRestScale;
        private float rightHandLiftCharge;
        private float leftHandLiftCharge;
        private bool throwUsingLeftHand;
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
        private Quaternion leftHandRestRotation;
        private Quaternion rightHandRestRotation;
        private Vector3 rightShoulderRestPosition;
        private Vector3 leftShoulderRestPosition;
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
        private float leftHeldArmPhase;
        private float rightHeldArmPhase;
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
                leftGripDigits[i]=FindChild(model.transform,
                    digitNames[i].Replace("Right","Left"));
                if(leftGripDigits[i]!=null)
                    leftGripDigitRest[i]=leftGripDigits[i].localRotation;
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
            leftShoulderRestPosition = leftShoulder.localPosition;
            rightElbowRestPosition = rightElbow.localPosition;
            leftElbowRestPosition = leftElbow.localPosition;
            leftHipRestRotation = leftHip.localRotation;
            rightHipRestRotation = rightHip.localRotation;
            leftKneeRestRotation = leftKnee.localRotation;
            rightKneeRestRotation = rightKnee.localRotation;
            leftAnkleRestRotation = leftAnkle.localRotation;
            rightAnkleRestRotation = rightAnkle.localRotation;
            rightElbowRestScale=rightElbow.localScale;
            leftElbowRestScale=leftElbow.localScale;
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
            CreateWristJoints(model);
            CreateHandMarkers();
            gameObject.AddComponent<HeldWristFollower>().Initialize(this);
            CreateNeck(model);

            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = "Human Matte Skin";
            material.SetColor("_BaseColor", PlayerColor);
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
                ColorOnlyTextureQuality.ApplyRendererQuality(renderer);
            }
            foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                renderer.updateWhenOffscreen = true;
                renderer.localBounds = new Bounds(Vector3.up * 0.9f, Vector3.one * 4f);
            }
            AttachCameraToHeadPosition(model);
            HideHeadFromPlayerCamera(model);
            CreateFootColliders();
            gameObject.AddComponent<PlayerRigColliders>().Initialize(modelRoot,2,false);
            CreateSlideSmoke();
            CreateRightFootSlideSmoke();
            CreateHandSmoke();
            (GetComponent<PlayerDamage>()??gameObject.AddComponent<PlayerDamage>()).Initialize(player,this,modelRoot);
        }

        public bool CanUseHand(bool left) => GetComponent<PlayerDamage>()?.CanUseHand(left)??true;
        internal float HandMovementSpeed(Transform hand)=>ArmMovementSpeed(hand==leftHandMarker);
        public float ArmMovementSpeed(bool left)=>GetComponent<PlayerDamage>()?.ArmMovementSpeed(left)??1f;
        float leftArmClock,rightArmClock,leftClockSample,rightClockSample;
        float ArmTime(bool left) {
            float now=Time.time;
            if(left) { leftArmClock+=Mathf.Max(0,now-leftClockSample)*ArmMovementSpeed(true);leftClockSample=now;return leftArmClock; }
            rightArmClock+=Mathf.Max(0,now-rightClockSample)*ArmMovementSpeed(false);rightClockSample=now;return rightArmClock;
        }
        float ArmStep(bool left)=>Time.deltaTime*ArmMovementSpeed(left);
        float LegSpeed(bool left)=>GetComponent<PlayerDamage>()?.LegAnimationSpeed(left)??1f;
        float LegSwingAmount(bool left)=>GetComponent<PlayerDamage>()?.LegAnimationSwingAmount(left)??1f;
        float ArmSwingAmount(bool left)=>GetComponent<PlayerDamage>()?.ArmAnimationSwingAmount(left)??1f;
        float leftInjuredLegPhase,rightInjuredLegPhase,leftInjuredCrouchPhase,rightInjuredCrouchPhase,supportLegPhase;
        internal void SampleArmClocks() { ArmTime(true);ArmTime(false); }
        float ArmBlend(Transform bone,float blend) {
            var part=PlayerRagdoll.Part(bone.name);
            if(part==PlayerBodyPart.Legs&&GetComponent<PlayerDamage>() is { SingleLegSupport:true }) return 0f;
            float speed=part==PlayerBodyPart.LeftArm?ArmMovementSpeed(true):part==PlayerBodyPart.RightArm?ArmMovementSpeed(false):
                part==PlayerBodyPart.Legs&&bone.name.StartsWith("Left")?LegSpeed(true):part==PlayerBodyPart.Legs&&bone.name.StartsWith("Right")?LegSpeed(false):1f;
            return speed<=0?0f:1f-Mathf.Pow(1f-Mathf.Clamp01(blend),speed);
        }
        internal void DisableDamagedHand(bool left)
        {
            CancelRightHandPickup();CancelRightHandStrike();punching=false;
            CancelPendingRightHandThrowAnimation();
            if(throwUsingLeftHand==left) { throwAnimationStartedAt=throwFollowThroughUntil=0;throwAnimationWaitingForRelease=false; }
            if(left) { leftHandLiftCharge=0;returningLeftHandWithItem=returningLeftHandAfterThrow=false; }
            else { rightHandLiftCharge=0;returningRightHandWithItem=returningRightHandAfterThrow=false; }
            if(PlayerDamage.ShouldDropHeldItem(UnityEngine.Random.value)) {
                if(left) ReleaseLeftHandItem(Vector3.zero);
                else ReleaseRightHandItem(Vector3.zero,false);
            }
        }

        public bool HasRightHandItem => heldRightHandItem!=null;
        public bool HasLeftHandItem => heldLeftHandItem!=null;
        internal Transform NpcRightHand=>rightHandMarker;
        internal void BindNpcWeapon(PickableObject item) { heldRightHandItem=item;returningRightHandWithItem=false; }
        internal Vector3 ShockEyePosition=>headCameraAnchor!=null?headCameraAnchor.position:transform.position+Vector3.up*1.5f;
        internal Quaternion ShockEyeRotation=>headCameraAnchor!=null?headCameraAnchor.rotation:transform.rotation;
        public bool IsStrikeMode => striking&&!strikeEnding||auxiliaryStrike&&!auxiliaryStrikeEnding;
        public const float MinimumReboundSpeed = 3f;
        public PickableObject StrikingHeldItem =>
            strikeUsingLeftHand?heldLeftHandItem:heldRightHandItem;
        internal bool HasReboundSpeed => IsStrikeMode&&
            StrikingHeldItem!=null&&StrikingHeldItem.HeldSpeed>MinimumReboundSpeed;
        float strikeWaveSeconds;
        internal static float StrikeFatigueBlend(float seconds)=>Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(1.7f,5f,seconds));
        internal static bool IsFastStrikeWave(float mouseSpeed,float objectSpeed)=>
            mouseSpeed>=FirstPersonController.StrikeFullSpeedDegreesPerSecond&&
            objectSpeed>MinimumReboundSpeed;
        internal void UpdateStrikeFatigue(float mouseSpeed,float objectSpeed,float seconds)
        { if(IsStrikeMode&&strikeAttackHeld&&IsFastStrikeWave(mouseSpeed,objectSpeed)) strikeWaveSeconds+=Mathf.Max(0f,seconds); }
        public float StrikeWavingSpeedMultiplier =>
            IsStrikeMode&&strikeAttackHeld&&!strikeRebounding&&!auxiliaryRebounding?
                Mathf.Lerp(1f+strikeWaveBonus,.3f,StrikeFatigueBlend(strikeWaveSeconds)):1f;
        private float StrikeInjurySpeed(bool left)=>
            (GetComponent<PlayerDamage>()?.MovementSpeedScale??1f)*ArmSwingAmount(left);

        internal void UpdateStrikeWaveBoost(float objectSpeed,float mouseSpeed,float deltaTime)
        {
            if(!IsStrikeMode||!strikeAttackHeld)
            { strikeWaveBonus=0f;strikeWaveBelowSpeedElapsed=0f;return; }
            bool fastMouse=mouseSpeed>=FirstPersonController.StrikeFullSpeedDegreesPerSecond;
            bool addingFastMotion=IsFastStrikeWave(mouseSpeed,objectSpeed);
            if(addingFastMotion) strikeWaveBelowSpeedElapsed=0f;
            else strikeWaveBelowSpeedElapsed+=Mathf.Max(0f,deltaTime);
            // A wave briefly slows at each reversal. Retain the earned bonus
            // through that dip; only motion above the threshold adds to it.
            if(!addingFastMotion&&fastMouse&&
                strikeWaveBelowSpeedElapsed<=StrikeWaveSpeedDipGraceSeconds) return;
            strikeWaveBonus=Mathf.MoveTowards(strikeWaveBonus,
                addingFastMotion?MaximumStrikeWaveBonus:0f,
                Mathf.Max(0f,deltaTime)*MaximumStrikeWaveBonus/
                (addingFastMotion?StrikeWaveBoostBuildSeconds:StrikeWaveBoostFadeSeconds));
        }
        internal bool StrikeDestroyedSomething => striking&&strikeDestroyedSomething;
        internal void MarkStrikeDestruction()
        {
            if(striking) strikeDestroyedSomething=true;
        }
        public bool IsHoldingStrikePose(PickableObject item) => item!=null&&
            (striking&&item==(strikeUsingLeftHand?heldLeftHandItem:heldRightHandItem)||
             auxiliaryStrike&&item==(auxiliaryStrikeLeft?heldLeftHandItem:heldRightHandItem));
        public bool IsStrikingHeldItem(PickableObject item) => item!=null&&
            (striking&&!strikeEnding&&!strikeRebounding&&item==(strikeUsingLeftHand?heldLeftHandItem:heldRightHandItem)||
             auxiliaryStrike&&!auxiliaryStrikeEnding&&!auxiliaryRebounding&&item==(auxiliaryStrikeLeft?heldLeftHandItem:heldRightHandItem));

        public bool BeginRightHandStrike()
        {
            return BeginHandStrike(!HasRightHandItem||!CanUseHand(false));
        }

        public bool BeginLeftHandStrike() => BeginHandStrike(true);

        private bool BeginHandStrike(bool useLeft)
        {
            if(!(useLeft?HasLeftHandItem:HasRightHandItem)||!CanUseHand(useLeft)) return false;
            if(striking&&strikeUsingLeftHand!=useLeft&&heldRightHandItem!=heldLeftHandItem)
                return BeginAuxiliaryStrike(useLeft);
            if(striking||auxiliaryStrike||(!HasRightHandItem&&!HasLeftHandItem)||
                rightShoulder==null||rightElbow==null||rightHandMarker==null||
                leftShoulder==null||leftElbow==null||leftHandMarker==null||
                cameraTransform==null||headCameraAnchor==null||
                ArmTime(throwUsingLeftHand)<throwFollowThroughUntil||returningRightHandAfterThrow||returningLeftHandAfterThrow)
                return false;
            strikeUsingLeftHand=useLeft;
            if(heldRightHandItem==heldLeftHandItem&&twoHandPrimaryLeft!=useLeft)
            {
                heldRightHandItem.TransferToHand(useLeft?leftHandMarker:rightHandMarker,true);
                twoHandPrimaryLeft=useLeft;
            }
            Transform shoulder=strikeUsingLeftHand?leftShoulder:rightShoulder;
            Transform elbow=strikeUsingLeftHand?leftElbow:rightElbow;
            Transform hand=strikeUsingLeftHand?leftHandMarker:rightHandMarker;
            EndRightHandPunchForReach();
            CancelRightHandPickup();
            if(strikeUsingLeftHand) returningLeftHandWithItem=false;
            else returningRightHandWithItem=false;
            striking=true;
            strikeEnding=false;
            strikeRebounding=false;
            strikeDestroyedSomething=false;
            strikeAttackHeld=true;
            strikeStartedAt=ArmTime(strikeUsingLeftHand);
            weaponSwing.Reset();strikeWaveSeconds=0f;
            strikeHandYaw=HeldWeaponSwing.Guard.x;
            strikeHandPitch=HeldWeaponSwing.Guard.y;
            strikeWaveBonus=0f;
            strikeWaveBelowSpeedElapsed=0f;
            strikeAimDirection=cameraTransform.rotation*
                Quaternion.Euler(strikeHandPitch,strikeUsingLeftHand?-14f:19f,0f)*Vector3.forward;
            strikeStartHandLocalPosition=headCameraAnchor.InverseTransformPoint(
                hand.position);
            strikeShoulderPose=shoulder.localRotation;
            strikeElbowPose=elbow.localRotation;
            strikeReadyShoulder=strikeShoulderPose;
            strikeReadyElbow=strikeElbowPose;
            strikeWristRest=hand.localRotation;
            strikeShaftInHand=hand.InverseTransformDirection(StrikingHeldItem.StickDirection);
            StrikingHeldItem.CaptureStrikeGrip();
            return true;
        }

        public void UpdateRightHandStrikeInput(bool pressed,bool released)
        {
            if(!IsStrikeMode||!released) return;
            strikeAttackHeld=false;
            if(strikeRebounding) return;
            if(ArmTime(strikeUsingLeftHand)-strikeStartedAt<StrikeShortPressSeconds)
            {
                bool useLeft=strikeUsingLeftHand;
                CancelRightHandStrike();
                BeginPunch(useLeft,true);
            }
            else EndRightHandStrike();
        }

        public void UpdateHandStrikeInput(bool left,bool released)
        {
            if(auxiliaryStrike&&auxiliaryStrikeLeft==left) {
                if(released) {
                    auxiliaryStrikeHeld=false;
                    if(!auxiliaryRebounding) { auxiliaryStrikeEnding=true;auxiliaryStrikeEnded=Time.time; }
                }
            }
            else if(striking&&strikeUsingLeftHand==left)
                UpdateRightHandStrikeInput(false,released);
        }

        private bool BeginAuxiliaryStrike(bool left)
        {
            if(auxiliaryStrike||!CanUseHand(left)||heldLeftHandItem==heldRightHandItem) return false;
            var item=left?heldLeftHandItem:heldRightHandItem;
            var shoulder=left?leftShoulder:rightShoulder;
            var elbow=left?leftElbow:rightElbow;
            var hand=left?leftHandMarker:rightHandMarker;
            if(item==null||shoulder==null||elbow==null||hand==null) return false;
            auxiliaryStrike=true;auxiliaryStrikeLeft=left;auxiliaryStrikeHeld=true;
            auxiliaryStrikeEnding=false;auxiliaryRebounding=false;
            auxiliaryStrikeStarted=Time.time;auxiliaryStrikeFatigue=0f;
            auxiliaryShoulderStart=shoulder.localRotation;auxiliaryElbowStart=elbow.localRotation;
            auxiliaryWristStart=hand.localRotation;auxiliaryWeaponSwing.Reset();item.CaptureStrikeGrip();
            return true;
        }

        private void ApplyAuxiliaryStrikePose()
        {
            if(!auxiliaryStrike) return;
            var item=auxiliaryStrikeLeft?heldLeftHandItem:heldRightHandItem;
            var shoulder=auxiliaryStrikeLeft?leftShoulder:rightShoulder;
            var elbow=auxiliaryStrikeLeft?leftElbow:rightElbow;
            var hand=auxiliaryStrikeLeft?leftHandMarker:rightHandMarker;
            if(item==null||shoulder==null||elbow==null||hand==null) { auxiliaryStrike=false;return; }
            if(auxiliaryRebounding) {
                ApplyBoundedReboundPose(auxiliaryStrikeLeft,shoulder,elbow,hand,
                    auxiliaryReboundStart,auxiliaryReboundVelocity,
                    auxiliaryReboundShoulder,auxiliaryReboundElbow,auxiliaryReboundWrist,
                    Time.time-auxiliaryReboundStartedAt);
                if(Time.time-auxiliaryReboundStartedAt>=StrikeReboundSeconds) {
                    auxiliaryRebounding=false;
                    if(!auxiliaryStrikeHeld) { auxiliaryStrikeEnding=true;auxiliaryStrikeEnded=Time.time; }
                }
                return;
            }
            if(auxiliaryStrikeEnding) {
                float t=Mathf.SmoothStep(0f,1f,Mathf.Clamp01((Time.time-auxiliaryStrikeEnded)/StrikeReturnSeconds));
                shoulder.localRotation=Quaternion.Slerp(shoulder.localRotation,
                    auxiliaryStrikeLeft?leftShoulderRestRotation:rightShoulderRestRotation,t);
                elbow.localRotation=Quaternion.Slerp(elbow.localRotation,
                    auxiliaryStrikeLeft?leftElbowRestRotation:rightElbowRestRotation,t);
                hand.localRotation=Quaternion.Slerp(hand.localRotation,auxiliaryWristStart,t);
                if(t>=1f) auxiliaryStrike=false;
                return;
            }
            if(auxiliaryStrikeHeld) {
                if(IsFastStrikeWave(player.RawLookSpeedDegreesPerSecond,item.HeldSpeed))
                    auxiliaryStrikeFatigue+=Time.deltaTime;
                float gain=Mathf.Lerp(1f,.3f,StrikeFatigueBlend(auxiliaryStrikeFatigue));
                auxiliaryWeaponSwing.Step(player.RawLookDelta,Time.unscaledDeltaTime,
                    1.45f*gain*ArmMovementSpeed(auxiliaryStrikeLeft)*
                    StrikeInjurySpeed(auxiliaryStrikeLeft),item.PickupMassKg);
            }
            Vector2 angles=auxiliaryWeaponSwing.Angles;
            float side=auxiliaryStrikeLeft?-1f:1f;
            Vector3 direction=cameraTransform.rotation*
                Quaternion.Euler(angles.y,angles.x+side*17f,0f)*Vector3.forward;
            float upper=Vector3.Distance(shoulder.position,elbow.position);
            float lower=Vector3.Distance(elbow.position,hand.position);
            float reach=Mathf.Max(.05f,(upper+lower)*.82f);
            Vector3 target=shoulder.position+direction*reach;
            Vector3 pole=Vector3.ProjectOnPlane(cameraTransform.right*side*.7f-cameraTransform.up*.8f,direction).normalized;
            float along=(upper*upper+reach*reach-lower*lower)/(2f*reach);
            Vector3 elbowTarget=shoulder.position+direction*along+pole*Mathf.Sqrt(Mathf.Max(0f,upper*upper-along*along));
            shoulder.localRotation=auxiliaryStrikeLeft?leftShoulderRestRotation:rightShoulderRestRotation;
            elbow.localRotation=auxiliaryStrikeLeft?leftElbowRestRotation:rightElbowRestRotation;
            AimJointAtTarget(shoulder,elbow,elbowTarget,1f);
            AimJointAtTarget(elbow,hand,target,1f);
            float ready=Mathf.SmoothStep(0f,1f,(Time.time-auxiliaryStrikeStarted)/.16f);
            shoulder.localRotation=Quaternion.Slerp(auxiliaryShoulderStart,shoulder.localRotation,ready);
            elbow.localRotation=Quaternion.Slerp(auxiliaryElbowStart,elbow.localRotation,ready);
            hand.localRotation=auxiliaryWristStart;
        }

        public void CancelRightHandStrike()
        {
            if(striking)
            {
                Transform hand=strikeUsingLeftHand?leftHandMarker:rightHandMarker;
                if(hand!=null) hand.localRotation=strikeWristRest;
            }
            strikeContactConstrained=false;
            strikeWaveBonus=0f;
            strikeWaveBelowSpeedElapsed=0f;
            striking=false;
            strikeEnding=false;
            strikeRebounding=false;
            strikeDestroyedSomething=false;
            strikeAttackHeld=false;
        }

        public void CancelAllStrikes()
        {
            CancelRightHandStrike();
            if(auxiliaryStrike) {
                var hand=auxiliaryStrikeLeft?leftHandMarker:rightHandMarker;
                if(hand!=null) hand.localRotation=auxiliaryWristStart;
            }
            auxiliaryStrike=auxiliaryStrikeHeld=auxiliaryStrikeEnding=auxiliaryRebounding=false;
        }

        public void ReboundHeldStrike(PickableObject item,Vector3 hitDirection,float impactSpeed=0f)
        {
            if(auxiliaryStrike&&item==(auxiliaryStrikeLeft?heldLeftHandItem:heldRightHandItem)) {
                if(auxiliaryRebounding||auxiliaryStrikeEnding||
                    hitDirection.sqrMagnitude<0.000001f||
                    !(item.HeldSpeed>MinimumReboundSpeed||impactSpeed>MinimumReboundSpeed)) return;
                LimitStoppedArmPose(auxiliaryStrikeLeft);
                var auxiliaryShoulder=auxiliaryStrikeLeft?leftShoulder:rightShoulder;
                var auxiliaryElbow=auxiliaryStrikeLeft?leftElbow:rightElbow;
                var auxiliaryHand=auxiliaryStrikeLeft?leftHandMarker:rightHandMarker;
                auxiliaryRebounding=true;auxiliaryReboundStartedAt=Time.time;
                auxiliaryReboundStart=auxiliaryHand.position;
                float auxiliarySpeed=float.IsFinite(impactSpeed)&&impactSpeed>0f?impactSpeed:item.HeldSpeed;
                auxiliaryReboundVelocity=-hitDirection.normalized*Mathf.Min(auxiliarySpeed,MaximumStrikeReboundSpeed);
                auxiliaryReboundShoulder=auxiliaryShoulder.localRotation;
                auxiliaryReboundElbow=auxiliaryElbow.localRotation;
                auxiliaryReboundWrist=auxiliaryHand.localRotation;
                return;
            }
            if(!IsStrikingHeldItem(item)||strikeDestroyedSomething||
                hitDirection.sqrMagnitude<0.000001f||
                !(HasReboundSpeed||impactSpeed>MinimumReboundSpeed)) return;
            Transform shoulder=strikeUsingLeftHand?leftShoulder:rightShoulder;
            Transform elbow=strikeUsingLeftHand?leftElbow:rightElbow;
            Transform hand=strikeUsingLeftHand?leftHandMarker:rightHandMarker;
            LimitStoppedArmPose(strikeUsingLeftHand);
            strikeRebounding=true;
            strikeReboundStartedAt=Time.time;
            strikeReboundStart=hand.position;
            float speed=float.IsFinite(impactSpeed)&&impactSpeed>0f?impactSpeed:item.HeldSpeed;
            strikeReboundVelocity=-hitDirection.normalized*Mathf.Min(speed,MaximumStrikeReboundSpeed);
            strikeReboundShoulder=shoulder.localRotation;
            strikeReboundElbow=elbow.localRotation;
            strikeReboundWrist=hand.localRotation;
        }

        private void ApplyStrikeReboundPose(Transform shoulder,Transform elbow,Transform hand,float elapsed)
        {
            ApplyBoundedReboundPose(strikeUsingLeftHand,shoulder,elbow,hand,
                strikeReboundStart,strikeReboundVelocity,strikeReboundShoulder,
                strikeReboundElbow,strikeReboundWrist,elapsed);
            if(elapsed>=StrikeReboundSeconds)
            {
                strikeRebounding=false;
                if(!strikeAttackHeld) EndRightHandStrike();
            }
        }

        private void ApplyBoundedReboundPose(bool left,Transform shoulder,Transform elbow,Transform hand,
            Vector3 start,Vector3 velocity,Quaternion savedShoulder,Quaternion savedElbow,
            Quaternion savedWrist,float elapsed)
        {
            float progress=Mathf.Clamp01(elapsed/StrikeReboundSeconds);
            // The pulse returns to the exact recorded joint directions before
            // the existing held strike resumes.
            Vector3 target=start+Vector3.ClampMagnitude(
                velocity*(StrikeReboundSeconds*.5f)*Mathf.Sin(progress*Mathf.PI),
                MaximumStrikeReboundTravel);
            shoulder.localPosition=left?leftShoulderRestPosition:rightShoulderRestPosition;
            shoulder.localRotation=savedShoulder;
            elbow.localRotation=savedElbow;
            elbow.localPosition=left?leftElbowRestPosition:rightElbowRestPosition;
            hand.localPosition=left?leftHandRestPosition:RightHandPointOnElbow;
            hand.localRotation=savedWrist;
            if(progress<1f) for(int pass=0;pass<4;pass++) {
                AimJointAtTarget(elbow,hand,target,1f);
                AimJointAtTarget(shoulder,hand,target,1f);
            }
            LimitStoppedArmPose(left);
            elbow.localPosition=left?leftElbowRestPosition:rightElbowRestPosition;
            if(progress>=1f) {
                shoulder.localRotation=savedShoulder;
                elbow.localRotation=savedElbow;
                hand.localRotation=savedWrist;
            }
        }

        public void EndHeldStrikeAtTerrain(PickableObject item)
        {
            if(!IsStrikingHeldItem(item)) return;
            if(auxiliaryStrike&&item==(auxiliaryStrikeLeft?heldLeftHandItem:heldRightHandItem)) {
                auxiliaryStrikeHeld=false;auxiliaryStrikeEnding=true;auxiliaryStrikeEnded=Time.time;
                LimitStoppedArmPose(auxiliaryStrikeLeft);
                return;
            }
            strikeAttackHeld=false;
            EndRightHandStrike();
        }

        internal void ConstrainHeldStrikePose(PickableObject item,Vector3 position,Quaternion rotation,float fraction)
        {
            if(!IsStrikingHeldItem(item)) return;
            if(auxiliaryStrike&&item==(auxiliaryStrikeLeft?heldLeftHandItem:heldRightHandItem)) {
                var elbowBone=auxiliaryStrikeLeft?leftElbow:rightElbow;
                if(elbowBone!=null&&item.transform.IsChildOf(elbowBone)) {
                    elbowBone.rotation=rotation*Quaternion.Inverse(item.transform.rotation)*elbowBone.rotation;
                    elbowBone.position+=position-item.transform.position;
                } else item.transform.SetPositionAndRotation(position,rotation);
                LimitStoppedArmPose(auxiliaryStrikeLeft);
                KeepStoppedGripAtContact(item,auxiliaryStrikeLeft,position,rotation);
                return;
            }
            strikeContactConstrained=true;
            Transform shoulder=strikeUsingLeftHand?leftShoulder:rightShoulder;
            Transform elbow=strikeUsingLeftHand?leftElbow:rightElbow;
            if(elbow!=null&&item.transform.IsChildOf(elbow)) {
                elbow.rotation=rotation*Quaternion.Inverse(item.transform.rotation)*elbow.rotation;
                elbow.position+=position-item.transform.position;
                strikeShoulderPose=shoulder.localRotation;strikeElbowPose=elbow.localRotation;
            } else item.transform.SetPositionAndRotation(position,rotation);
            LimitStoppedArmPose(strikeUsingLeftHand);
            KeepStoppedGripAtContact(item,strikeUsingLeftHand,position,rotation);
            // Consume only the movement that reached the surface, so reversing
            // the mouse pulls away immediately instead of unwinding hidden input.
            strikeHandYaw=Mathf.Lerp(strikeInputYawBeforeFrame,strikeHandYaw,fraction);
            strikeHandPitch=Mathf.Lerp(strikeInputPitchBeforeFrame,strikeHandPitch,fraction);
            weaponSwing.Constrain(new Vector2(strikeHandYaw,strikeHandPitch));
            ApplyTwoHandHoldPose();
        }

        private void LimitStoppedArmPose(bool left)
        {
            var shoulder=left?leftShoulder:rightShoulder;
            var elbow=left?leftElbow:rightElbow;
            var wrist=left?leftHandMarker:rightHandMarker;
            if(shoulder==null||elbow==null||wrist==null) return;
            Quaternion shoulderRest=left?leftShoulderRestRotation:rightShoulderRestRotation;
            Quaternion elbowRest=left?leftElbowRestRotation:rightElbowRestRotation;
            Quaternion wristRest=left?leftHandRestRotation:rightHandRestRotation;
            Vector3 elbowRestPosition=left?leftElbowRestPosition:rightElbowRestPosition;
            Vector3 wristRestPosition=left?leftHandRestPosition:RightHandPointOnElbow;
            shoulder.localRotation=Quaternion.RotateTowards(shoulderRest,shoulder.localRotation,105f);
            elbow.localRotation=Quaternion.RotateTowards(elbowRest,elbow.localRotation,135f);
            wrist.localRotation=Quaternion.RotateTowards(wristRest,wrist.localRotation,65f);
            elbow.localPosition=elbowRestPosition+
                Vector3.ClampMagnitude(elbow.localPosition-elbowRestPosition,.12f);
            wrist.localPosition=wristRestPosition+
                Vector3.ClampMagnitude(wrist.localPosition-wristRestPosition,.35f);
        }

        private void KeepStoppedGripAtContact(PickableObject item,bool left,Vector3 position,Quaternion rotation)
        {
            Transform wrist=left?leftHandMarker:rightHandMarker;
            if(wrist==null||!item.transform.IsChildOf(wrist)) {
                item.transform.SetPositionAndRotation(position,rotation);
                return;
            }
            Vector3 rest=left?leftHandRestPosition:RightHandPointOnElbow;
            Vector3 correction=wrist.parent.InverseTransformVector(position-item.transform.position);
            wrist.localPosition=rest+Vector3.ClampMagnitude(wrist.localPosition+correction-rest,.35f);
            if(Vector3.Distance(item.transform.position,position)>.0001f)
                item.transform.SetPositionAndRotation(position,rotation);
        }

        private void EndRightHandStrike()
        {
            if(!striking||strikeEnding) return;
            strikeEnding=true;
            strikeReturnStartedAt=ArmTime(strikeUsingLeftHand);
            Transform shoulder=strikeUsingLeftHand?leftShoulder:rightShoulder;
            Transform elbow=strikeUsingLeftHand?leftElbow:rightElbow;
            strikeReturnShoulder=shoulder.localRotation;
            strikeReturnElbow=elbow.localRotation;
            strikeReturnElbowPosition=elbow.localPosition;
            Transform hand=strikeUsingLeftHand?leftHandMarker:rightHandMarker;
            strikeReturnWrist=hand.localRotation;
        }

        private void ApplyRightHandStrikePose()
        {
            if(!striking) { ApplyAuxiliaryStrikePose();return; }
            if(strikeUsingLeftHand?!HasLeftHandItem:!HasRightHandItem)
            {
                CancelRightHandStrike();
                ApplyAuxiliaryStrikePose();
                return;
            }
            Transform shoulder=strikeUsingLeftHand?leftShoulder:rightShoulder;
            Transform elbow=strikeUsingLeftHand?leftElbow:rightElbow;
            Transform hand=strikeUsingLeftHand?leftHandMarker:rightHandMarker;
            Quaternion shoulderRest=strikeUsingLeftHand?leftShoulderRestRotation:rightShoulderRestRotation;
            Quaternion elbowRest=strikeUsingLeftHand?leftElbowRestRotation:rightElbowRestRotation;
            Vector3 elbowRestPosition=strikeUsingLeftHand?leftElbowRestPosition:rightElbowRestPosition;
            if(strikeRebounding)
            {
                ApplyStrikeReboundPose(shoulder,elbow,hand,Time.time-strikeReboundStartedAt);
                ApplyAuxiliaryStrikePose();
                return;
            }
            if(strikeEnding)
            {
                float progress=Mathf.Clamp01((ArmTime(strikeUsingLeftHand)-strikeReturnStartedAt)/
                    StrikeReturnSeconds);
                progress=Mathf.SmoothStep(0f,1f,progress);
                shoulder.localRotation=Quaternion.Slerp(strikeReturnShoulder,
                    shoulderRest,progress);
                elbow.localRotation=Quaternion.Slerp(strikeReturnElbow,
                    elbowRest,progress);
                elbow.localPosition=Vector3.Lerp(strikeReturnElbowPosition,
                    elbowRestPosition,progress);
                hand.localRotation=Quaternion.Slerp(strikeReturnWrist,strikeWristRest,progress);
                if(progress>=1f) CancelRightHandStrike();
                ApplyAuxiliaryStrikePose();
                return;
            }

            UpdateStrikeFatigue(player.RawLookSpeedDegreesPerSecond,
                StrikingHeldItem!=null?StrikingHeldItem.HeldSpeed:0f,Time.deltaTime);
            strikeInputYawBeforeFrame=strikeHandYaw;strikeInputPitchBeforeFrame=strikeHandPitch;
            UpdateStrikeWaveBoost(StrikingHeldItem!=null?StrikingHeldItem.HeldSpeed:0f,
                player.RawLookSpeedDegreesPerSecond,ArmStep(strikeUsingLeftHand));
            if(strikeAttackHeld)
            {
                weaponSwing.Step(player.RawLookDelta,Time.unscaledDeltaTime,
                    1.45f*StrikeWavingSpeedMultiplier*ArmMovementSpeed(strikeUsingLeftHand)*
                    StrikeInjurySpeed(strikeUsingLeftHand),
                    StrikingHeldItem!=null?StrikingHeldItem.PickupMassKg:0f);
                strikeHandYaw=weaponSwing.Angles.x;
                strikeHandPitch=weaponSwing.Angles.y;
            }
            strikeAimDirection=cameraTransform.rotation*
                Quaternion.Euler(strikeHandPitch,strikeHandYaw+(strikeUsingLeftHand?-14f:19f),0f)*Vector3.forward;

            float blend=1f-Mathf.Exp(-28f*ArmStep(strikeUsingLeftHand));
            // Keep the shortened arm at contact. Extending it again each frame
            // would push into the obstacle and block small movements away.
            bool constrained=strikeContactConstrained;
            if(!strikeContactConstrained)
                elbow.localPosition=Vector3.Lerp(elbow.localPosition,
                    elbowRestPosition*StrikeArmStretchScale,blend);
            strikeContactConstrained=false;
            elbow.localScale=strikeUsingLeftHand?leftElbowRestScale:rightElbowRestScale;
            hand.localPosition=strikeUsingLeftHand?leftHandRestPosition:RightHandPointOnElbow;
            shoulder.localRotation=shoulderRest;
            elbow.localRotation=elbowRest;
            AimWeaponStrikeArm(shoulder,elbow,hand,weaponSwing.Activity);
            Quaternion aimedShoulder=shoulder.localRotation;
            Quaternion aimedElbow=elbow.localRotation;
            // Blend into guard once; the swing itself has one damping layer,
            // so fast mouse motion never fights a second slow arm follower.
            float ready=Mathf.SmoothStep(0f,1f,(ArmTime(strikeUsingLeftHand)-strikeStartedAt)/.16f);
            strikeShoulderPose=Quaternion.Slerp(strikeReadyShoulder,aimedShoulder,ready);
            strikeElbowPose=Quaternion.Slerp(strikeReadyElbow,aimedElbow,ready);
            shoulder.localRotation=strikeShoulderPose;
            elbow.localRotation=strikeElbowPose;
            if(!constrained) ApplyWeaponStrikeWrist(hand,ready);
            ApplyAuxiliaryStrikePose();
        }

        internal void ApplyWeaponStrikeWrist(Transform hand,float ready)
        {
            var item=StrikingHeldItem;
            if(item==null||item.GrabMode!=PickableGrabMode.Stick||item.Corpse!=null) return;
            float side=strikeUsingLeftHand?-1f:1f;
            Vector3 guard=new Vector3(side*.18f,.78f,.60f).normalized;
            Vector3 shaft=cameraTransform.rotation*Quaternion.Euler(
                (strikeHandPitch-HeldWeaponSwing.Guard.y)*.6f,strikeHandYaw*.7f,0)*guard;
            Quaternion basis=hand.parent.rotation*strikeWristRest;
            Quaternion target=Quaternion.FromToRotation(basis*strikeShaftInHand,shaft)*basis;
            // Keep the wrist within a modest bend. The shoulder and forearm
            // carry the stroke; the mesh never turns inside the grip.
            target=Quaternion.RotateTowards(basis,target,55f);
            hand.localRotation=Quaternion.Slerp(strikeWristRest,
                Quaternion.Inverse(hand.parent.rotation)*target,ready);
        }

        internal void AimWeaponStrikeArm(Transform shoulder,Transform elbow,Transform hand,float activity)
        {
            float upper=Vector3.Distance(shoulder.position,elbow.position);
            float lower=Vector3.Distance(elbow.position,hand.position);
            if(upper<.0001f||lower<.0001f) return;
            float reach=Mathf.Max(Mathf.Abs(upper-lower)+.0001f,
                (upper+lower)*Mathf.Lerp(.74f,.94f,Mathf.Clamp01(activity)));
            Vector3 direction=strikeAimDirection.normalized;
            Vector3 target=shoulder.position+direction*reach;
            float side=strikeUsingLeftHand?-1f:1f;
            Vector3 pole=cameraTransform.right*side*.7f-cameraTransform.up*.8f;
            Vector3 bend=Vector3.ProjectOnPlane(pole,direction).normalized;
            if(bend.sqrMagnitude<.001f) bend=cameraTransform.right*side;
            float along=(upper*upper+reach*reach-lower*lower)/(2f*reach);
            Vector3 elbowTarget=shoulder.position+direction*along+
                bend*Mathf.Sqrt(Mathf.Max(0f,upper*upper-along*along));
            AimJointAtTarget(shoulder,elbow,elbowTarget,1f);
            AimJointAtTarget(elbow,hand,target,1f);
        }

        public void BeginRightHandPunch() => BeginPunch(false,false);

        private void BeginPunch(bool useLeftHand,bool quickStrike)
        {
            if(!CanUseHand(useLeftHand)) return;
            if(punching||!quickStrike&&heldRightHandItem!=null&&
                heldRightHandItem==heldLeftHandItem||
                !quickStrike&&HasRightHandItem&&
                    heldRightHandItem.GrabMode!=PickableGrabMode.Regular||
                (useLeftHand?leftShoulder:rightShoulder)==null||
                (useLeftHand?leftElbow:rightElbow)==null||
                ArmTime(throwUsingLeftHand)<throwFollowThroughUntil||returningRightHandAfterThrow||returningLeftHandAfterThrow) return;
            CancelRightHandPickup();
            punching=true;
            punchUsingLeftHand=useLeftHand;
            punchImpactSlowActive=false;
            punchImpactReturning=false;
            punchStartedAt=ArmTime(punchUsingLeftHand);
            if(!useLeftHand&&rightHandPunchContactTrigger!=null)
                rightHandPunchContactTrigger.SetActive(true);
            Transform shoulder=useLeftHand?leftShoulder:rightShoulder;
            Transform elbow=useLeftHand?leftElbow:rightElbow;
            Transform[] digits=useLeftHand?leftGripDigits:punchDigits;
            punchShoulderStart=shoulder.localRotation;
            punchElbowStart=elbow.localRotation;
            for(int i=0;i<punchDigits.Length;i++)
                if(digits[i]!=null) punchDigitStart[i]=digits[i].localRotation;
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
            Transform shoulderBone=punchUsingLeftHand?leftShoulder:rightShoulder;
            Transform elbowBone=punchUsingLeftHand?leftElbow:rightElbow;
            Transform handMarker=punchUsingLeftHand?leftHandMarker:rightHandMarker;
            Transform[] digits=punchUsingLeftHand?leftGripDigits:punchDigits;
            Quaternion[] digitRest=punchUsingLeftHand?leftGripDigitRest:punchDigitRest;
            // Pickup reach changes the elbow's local position to lengthen the arm.
            // A punch owns the complete arm pose, so keep both arm joints at
            // their authored positions until the punch has fully returned to idle.
            shoulderBone.localPosition=punchUsingLeftHand?
                leftShoulderRestPosition:rightShoulderRestPosition;
            elbowBone.localPosition=punchUsingLeftHand?
                leftElbowRestPosition:rightElbowRestPosition;
            elbowBone.localScale=punchUsingLeftHand?
                leftElbowRestScale:rightElbowRestScale;
            if(handMarker!=null)
                handMarker.localPosition=punchUsingLeftHand?
                    leftHandRestPosition:RightHandPointOnElbow;
            if(punchImpactSlowActive)
            {
                if(ArmTime(punchUsingLeftHand)<punchImpactSlowUntil)
                    // Anchor to contact time so the slowdown is independent of
                    // frame rate and the point within a frame where contact occurred.
                    punchStartedAt=ArmTime(punchUsingLeftHand)-punchImpactElapsedAtContact-
                        (ArmTime(punchUsingLeftHand)-(punchImpactSlowUntil-PunchImpactSlowDuration))*PunchImpactSpeedScale;
                else
                {
                    punchImpactSlowActive=false;
                    punchImpactReturning=true;
                    punchImpactReturnStartedAt=ArmTime(punchUsingLeftHand);
                    punchImpactShoulderReturnStart=shoulderBone.localRotation;
                    punchImpactElbowReturnStart=elbowBone.localRotation;
                    for(int i=0;i<punchDigits.Length;i++)
                        if(digits[i]!=null)
                            punchImpactDigitReturnStart[i]=digits[i].localRotation;
                }
            }
            if(punchImpactReturning)
            {
                float returnProgress=Mathf.Clamp01((ArmTime(punchUsingLeftHand)-punchImpactReturnStartedAt)/
                    PunchReturnDuration);
                returnProgress=Mathf.SmoothStep(0f,1f,returnProgress);
                shoulderBone.localRotation=Quaternion.Slerp(
                    punchImpactShoulderReturnStart,punchUsingLeftHand?
                        leftShoulderRestRotation:rightShoulderRestRotation,returnProgress);
                elbowBone.localRotation=Quaternion.Slerp(
                    punchImpactElbowReturnStart,punchUsingLeftHand?
                        leftElbowRestRotation:rightElbowRestRotation,returnProgress);
                for(int i=0;i<punchDigits.Length;i++)
                    if(digits[i]!=null)
                        digits[i].localRotation=Quaternion.Slerp(
                            punchImpactDigitReturnStart[i],digitRest[i],returnProgress);
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
            float elapsed=Mathf.Max(0f,ArmTime(punchUsingLeftHand)-punchStartedAt);
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
            if(punchUsingLeftHand)
            {
                readyShoulder=MirrorPunchPose(readyShoulder);
                readyElbow=MirrorPunchPose(readyElbow);
                strikeShoulder=MirrorPunchPose(strikeShoulder);
                strikeElbow=MirrorPunchPose(strikeElbow);
                intermediateShoulder=MirrorPunchPose(intermediateShoulder);
                intermediateElbow=MirrorPunchPose(intermediateElbow);
                finalShoulder=MirrorPunchPose(finalShoulder);
                finalElbow=MirrorPunchPose(finalElbow);
                followShoulder=MirrorPunchPose(followShoulder);
                followElbow=MirrorPunchPose(followElbow);
            }
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
                shoulder=Quaternion.Slerp(followShoulder,punchUsingLeftHand?
                    leftShoulderRestRotation:rightShoulderRestRotation,progress);
                elbow=Quaternion.Slerp(followElbow,punchUsingLeftHand?
                    leftElbowRestRotation:rightElbowRestRotation,progress);
                if(progress>=1f)
                {
                    punching=false;
                    if(rightHandPunchContactTrigger!=null)
                        rightHandPunchContactTrigger.SetActive(false);
                }
            }
            shoulderBone.localRotation=shoulder;
            elbowBone.localRotation=elbow;
            for(int i=0;i<punchDigits.Length;i++)
            {
                if(digits[i]==null) continue;
                Quaternion fist=Quaternion.Euler(PunchDigitAngles[i]);
                if(punchUsingLeftHand) fist=MirrorPunchPose(fist);
                digits[i].localRotation=elapsed<PunchFollowThroughEnd?
                    Quaternion.Slerp(punchDigitStart[i],fist,
                        Mathf.Clamp01(elapsed/PunchReadyDuration)):
                    Quaternion.Slerp(fist,digitRest[i],
                        Mathf.Clamp01((elapsed-PunchFollowThroughEnd)/PunchReturnDuration));
            }
            ApplyHeldPunchFingerPose();
        }

        private static Quaternion MirrorPunchPose(Quaternion pose) =>
            new(pose.x,-pose.y,-pose.z,pose.w);

        internal static bool UsesStickFingerGrip(PickableObject item)=>item!=null&&item.GrabMode==PickableGrabMode.Stick&&item.Corpse==null;
        private void ApplyHeldPunchFingerPose()
        {
            PickableObject held=punchUsingLeftHand?heldLeftHandItem:heldRightHandItem;
            if(UsesStickFingerGrip(held))
            {
                Transform[] digits=punchUsingLeftHand?leftGripDigits:punchDigits;
                for(int i=0;i<digits.Length;i++)
                    if(digits[i]!=null)
                    {
                        Quaternion fist=Quaternion.Euler(PunchDigitAngles[i]);
                        digits[i].localRotation=punchUsingLeftHand?
                            MirrorPunchPose(fist):fist;
                    }
                return;
            }
            if(held!=null) {
                var digits=punchUsingLeftHand?leftGripDigits:punchDigits;
                for(int i=0;i<digits.Length;i++) if(digits[i]!=null) {
                    var pose=Quaternion.Euler(IrregularGripDigitAngles[i]);
                    digits[i].localRotation=punchUsingLeftHand?MirrorPunchPose(pose):pose;
                }
            }
        }

        private void OnRightHandPunchContact(Collider other)
        {
            if(!punching||punchUsingLeftHand||punchImpactSlowActive||punchImpactReturning||
                other==null||other.isTrigger) return;
            float elapsed=ArmTime(punchUsingLeftHand)-punchStartedAt;
            if(elapsed>=PunchFollowThroughEnd) return;
            punchImpactSlowActive=true;
            punchImpactElapsedAtContact=elapsed;
            punchImpactSlowUntil=ArmTime(punchUsingLeftHand)+PunchImpactSlowDuration;
            if(rightHandPunchContactTrigger!=null)
                rightHandPunchContactTrigger.SetActive(false);
        }
        public bool IsReachingForObject => pickupTarget!=null;
        public bool IsReaching => pickupTarget!=null||freeRightHandReach||freeLeftHandReach;
        public bool IsRightReaching => freeRightHandReach||pickupTarget!=null&&
            (!pickupUsingLeftHand||pickupUsingBothHands);
        public bool IsLeftReaching => freeLeftHandReach||pickupTarget!=null&&
            (pickupUsingLeftHand||pickupUsingBothHands);
        public bool PickupUsingLeftHand => pickupUsingLeftHand;
        public bool PickupUsingBothHands => pickupUsingBothHands;
        public PickableObject HeldRightHandItem => heldRightHandItem;
        public PickableObject HeldLeftHandItem => heldLeftHandItem;

        public bool RetainFragmentFromHeldObject(PickableObject source,PickableObject fragment)
        {
            if(source==null||fragment==null||!source.IsHeld) return false;
            bool heldRight=heldRightHandItem==source;
            bool heldLeft=heldLeftHandItem==source;
            if(!heldRight&&!heldLeft) return false;
            Transform primaryHand=source.HeldHand;
            if(primaryHand==null) return false;
            bool useBothHands=heldRight&&heldLeft&&fragment.RequiresTwoHands;
            if(!fragment.PickUp(primaryHand,useBothHands)) return false;
            float returnRate=HeldReturnRate(fragment);
            if(heldRight)
            {
                heldRightHandItem=useBothHands||primaryHand==rightHandMarker?
                    fragment:null;
                heldItemReturnRate=returnRate;
                returningRightHandWithItem=false;
            }
            if(heldLeft)
            {
                heldLeftHandItem=useBothHands||primaryHand==leftHandMarker?
                    fragment:null;
                leftHeldItemReturnRate=returnRate;
                returningLeftHandWithItem=false;
            }
            if(useBothHands)
                twoHandPrimaryLeft=primaryHand==leftHandMarker;
            source.Release(source.HeldPointVelocity(source.transform.position),transform);
            return true;
        }
        public PickableObject PickupTarget => pickupTarget;
        public Vector3 RightShoulderPosition => rightShoulder!=null?rightShoulder.position:transform.position;
        public Vector3 LeftShoulderPosition => leftShoulder!=null?leftShoulder.position:transform.position;
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

        public void SetLeftHandLiftCharge(float charge)
        {
            leftHandLiftCharge=HasLeftHandItem?ShapeLiftProgress(charge):0f;
            if(leftHandLiftCharge>0f) {
                CancelRightHandStrike();returningLeftHandWithItem=false;
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
            bool animationAlreadyStarted=throwAnimationWaitingForRelease&&!throwUsingLeftHand;
            if(heldLeftHandItem==heldRightHandItem)
            {
                heldLeftHandItem=null;
                returningLeftHandWithItem=false;
            }
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

        public void ReleaseLeftHandItem(Vector3 velocity,bool throwing=false)
        {
            if(!HasLeftHandItem) return;
            if(heldRightHandItem==heldLeftHandItem)
            {
                ReleaseRightHandItem(velocity,throwing);
                return;
            }
            CancelRightHandStrike();
            bool animationAlreadyStarted=throwAnimationWaitingForRelease&&throwUsingLeftHand;
            heldLeftHandItem.Release(velocity,transform);
            heldLeftHandItem=null;
            leftHandLiftCharge=0f;
            returningLeftHandWithItem=false;
            returningLeftHandAfterThrow=throwing;
            if(throwing) {
                throwAnimationWaitingForRelease=false;
                if(!animationAlreadyStarted) StartHandThrowAnimation(false,true);
            } else if(throwUsingLeftHand) {
                throwAnimationWaitingForRelease=false;throwAnimationStartedAt=0;throwFollowThroughUntil=0;
            }
        }

        public void SwitchHeldItems()
        {
            if(!CanUseHand(false)||!CanUseHand(true)) return;
            if(heldLeftHandItem==heldRightHandItem||
                leftHandMarker==null||rightHandMarker==null) return;
            CancelRightHandPickup();
            CancelRightHandStrike();
            CancelPendingRightHandThrowAnimation();
            PickableObject oldLeft=heldLeftHandItem;
            PickableObject oldRight=heldRightHandItem;
            oldLeft?.TransferToHand(rightHandMarker);
            oldRight?.TransferToHand(leftHandMarker);
            heldLeftHandItem=oldRight;
            heldRightHandItem=oldLeft;
            leftHeldItemReturnRate=oldRight!=null?
                HeldReturnRate(oldRight):HeldItemReturnRate;
            heldItemReturnRate=oldLeft!=null?
                HeldReturnRate(oldLeft):HeldItemReturnRate;
            returningLeftHandWithItem=oldRight!=null;
            returningRightHandWithItem=oldLeft!=null;
            rightHandLiftCharge=0f;
            leftHandLiftCharge=0f;
        }

        public void BeginRightHandThrowAnimation()
        {
            BeginHandThrowAnimation(false);
        }

        public void BeginLeftHandThrowAnimation()=>BeginHandThrowAnimation(true);

        private void BeginHandThrowAnimation(bool left)
        {
            if(!(left?HasLeftHandItem:HasRightHandItem)||throwAnimationWaitingForRelease) return;
            CancelRightHandStrike();
            if(left) { leftHandLiftCharge=0;returningLeftHandWithItem=false;returningLeftHandAfterThrow=true; }
            else { rightHandLiftCharge=0;returningRightHandWithItem=false;returningRightHandAfterThrow=true; }
            StartHandThrowAnimation(true,left);
        }

        public void CancelPendingRightHandThrowAnimation()
        {
            if(!throwAnimationWaitingForRelease) return;
            throwAnimationWaitingForRelease=false;
            throwAnimationStartedAt=0f;
            throwFollowThroughUntil=0f;
            if(throwUsingLeftHand) { returningLeftHandAfterThrow=false;returningLeftHandWithItem=HasLeftHandItem; }
            else { returningRightHandAfterThrow=false;returningRightHandWithItem=HasRightHandItem; }
        }

        private void StartRightHandThrowAnimation(bool waitForRelease)
        {
            StartHandThrowAnimation(waitForRelease,false);
        }

        private void StartHandThrowAnimation(bool waitForRelease,bool left)
        {
            throwUsingLeftHand=left;
            throwAnimationWaitingForRelease=waitForRelease;
            throwAnimationStartedAt=ArmTime(left);
            throwFollowThroughUntil=throwAnimationStartedAt+ThrowAnimationDuration;
            throwShoulderStart=(left?leftShoulder:rightShoulder).localRotation;
            throwElbowStart=(left?leftElbow:rightElbow).localRotation;
        }

        private void ApplyRightHandThrowPose()
        {
            if(rightHandLiftCharge>0f&&HasRightHandItem)
            {
                PoseLocalEuler(rightShoulder,new Vector3(60f,80f,30f),rightHandLiftCharge);
                PoseLocalEuler(rightElbow,new Vector3(-80f,20f,-20f),rightHandLiftCharge);
            }
            else if(leftHandLiftCharge>0f&&HasLeftHandItem)
            {
                PoseLocalRotation(leftShoulder,MirrorPunchPose(Quaternion.Euler(60f,80f,30f)),leftHandLiftCharge);
                PoseLocalRotation(leftElbow,MirrorPunchPose(Quaternion.Euler(-80f,20f,-20f)),leftHandLiftCharge);
            }
            else if(ArmTime(throwUsingLeftHand)<throwFollowThroughUntil)
            {
                float progress=Mathf.InverseLerp(throwAnimationStartedAt,
                    throwFollowThroughUntil,ArmTime(throwUsingLeftHand));
                progress=progress*progress*(3f-2f*progress);
                Quaternion shoulderTarget=Quaternion.Euler(60f,70f,30f),elbowTarget=Quaternion.Euler(0f,0f,50f);
                if(throwUsingLeftHand) { shoulderTarget=MirrorPunchPose(shoulderTarget);elbowTarget=MirrorPunchPose(elbowTarget); }
                PoseLocalRotation(throwUsingLeftHand?leftShoulder:rightShoulder,Quaternion.Slerp(throwShoulderStart,shoulderTarget,progress),1f);
                PoseLocalRotation(throwUsingLeftHand?leftElbow:rightElbow,Quaternion.Slerp(throwElbowStart,elbowTarget,progress),1f);
            }
        }

        private void ApplyChargingSupportArm()
        {
            if(rightHandLiftCharge>0f&&HasRightHandItem&&!HasLeftHandItem)
                ApplyArmAim(leftShoulder,leftElbow,leftHandMarker,ref leftAimShoulderPose,ref leftAimElbowPose,ref leftArmAimActive,
                    LeftThrowAimLift,rightHandLiftCharge*LeftThrowMaximumScreenCentering,leftShoulderRestRotation,leftElbowRestRotation);
            else if(leftHandLiftCharge>0f&&HasLeftHandItem&&!HasRightHandItem)
                ApplyArmAim(rightShoulder,rightElbow,rightHandMarker,ref rightAimShoulderPose,ref rightAimElbowPose,ref rightArmAimActive,
                    LeftThrowAimLift,leftHandLiftCharge*LeftThrowMaximumScreenCentering,rightShoulderRestRotation,rightElbowRestRotation);
            else leftArmAimActive=false;
        }

        public bool BeginRightHandPickup(PickableObject target)
        {
            return BeginHandPickup(target,false);
        }

        public bool BeginHandPickup(PickableObject target,bool preferLeft,
            Vector3? aimedGripPoint=null)
        {
            if((ArmTime(throwUsingLeftHand)<throwFollowThroughUntil||returningRightHandAfterThrow||returningLeftHandAfterThrow)||
                target==null||target.IsHeld||!WebWorldSession.CanInteract(target.gameObject)||
                pickupTarget!=null||player.IsSliding||
                rightShoulder==null||rightHandMarker==null||
                leftShoulder==null||leftHandMarker==null) return false;
            bool dragging=target.RequiresDragging;
            bool both=dragging?!HasRightHandItem&&!HasLeftHandItem:target.RequiresTwoHands;
            if(dragging&&(HasRightHandItem&&HasLeftHandItem)||
                !dragging&&(both&&!target.CanPickUpWithTwoHands||
                both&&(HasRightHandItem||HasLeftHandItem)||
                !both&&!target.CanPickUpWithOneHand)) return false;
            bool left=preferLeft;
            if(!CanUseHand(left)||both&&(!CanUseHand(false)||!CanUseHand(true))) return false;
            if(!both&&(left?HasLeftHandItem:HasRightHandItem)) return false;
            Transform shoulder=left?leftShoulder:rightShoulder;
            Vector3 grip=aimedGripPoint??target.PickupReachPoint(
                (left?leftHandMarker:rightHandMarker).position);
            if(Vector3.Distance(shoulder.position,grip)>RightPickupRange||
                both&&Vector3.Distance((left?rightShoulder:leftShoulder).position,
                    grip)>RightPickupRange) return false;
            if(!left||both) EndRightHandPunchForReach();
            pickupTarget=target;
            pickupGripLocal=target.transform.InverseTransformPoint(grip);
            pickupUsingLeftHand=left;
            pickupUsingBothHands=both;
            pickupDragging=dragging;
            freeRightHandReach=false;
            freeLeftHandReach=false;
            pickupShoulderPose=rightShoulder.localRotation;
            pickupElbowPose=rightElbow.localRotation;
            pickupHandGoal=rightHandMarker.position;
            leftPickupShoulderPose=leftShoulder.localRotation;
            leftPickupElbowPose=leftElbow.localRotation;
            leftPickupHandGoal=leftHandMarker.position;
            // Ask for room ownership while the arm travels, so network latency
            // does not leave a hand waiting against an otherwise reachable prop.
            if(!dragging) WebWorldSession.TryClaim(target);
            UpdateReachCrouch();
            TryFinishHandPickup();
            return true;
        }

        public bool BeginRightHandReach()
        {
            return BeginHandReach(false);
        }

        public bool BeginHandReach(bool preferLeft)
        {
            if(!CanUseHand(preferLeft)) return false;
            if(IsReaching) return true;
            if(ArmTime(throwUsingLeftHand)<throwFollowThroughUntil||returningRightHandAfterThrow||returningLeftHandAfterThrow||
                rightShoulder==null||rightElbow==null||rightHandMarker==null||
                leftShoulder==null||leftElbow==null||leftHandMarker==null)
                return false;
            bool left=preferLeft;
            if(!CanUseHand(left)) return false;
            if(left?HasLeftHandItem:HasRightHandItem) return false;
            if(!left) EndRightHandPunchForReach();
            freeRightHandReach=!left;
            freeLeftHandReach=left;
            pickupShoulderPose=rightShoulder.localRotation;
            pickupElbowPose=rightElbow.localRotation;
            pickupHandGoal=rightHandMarker.position;
            leftPickupShoulderPose=leftShoulder.localRotation;
            leftPickupElbowPose=leftElbow.localRotation;
            leftPickupHandGoal=leftHandMarker.position;
            UpdateReachCrouch();
            return true;
        }

        public void CancelRightHandPickup()
        {
            pickupTarget=null;
            pickupDragging=false;
            pickupGripLocal=Vector3.zero;
            freeRightHandReach=false;
            freeLeftHandReach=false;
            if(rightElbow!=null)
                rightElbow.localPosition=rightElbowRestPosition;
            if(leftElbow!=null)
                leftElbow.localPosition=leftElbowRestPosition;
            if(rightHandMarker!=null)
                rightHandMarker.localPosition=RightHandPointOnElbow;
            if(leftHandMarker!=null)
                leftHandMarker.localPosition=leftHandRestPosition;
            RestorePickupPosture();
        }

        private void RestorePickupPosture()
        {
            lowPickupReach=false;
            UpdateReachCrouch();
        }

        public void SetReachCrouchInput(bool active)
        {
            reachInputHeld=active;
            UpdateReachCrouch();
        }

        private void UpdateReachCrouch()
        {
            if(player==null) return;
            lowPickupReach=(IsReaching||reachInputHeld)&&player.LookPitch>50f;
            player.SetPickupCrouch(lowPickupReach,
                Mathf.InverseLerp(50f,85f,player.LookPitch));
        }

        private void LateUpdate()
        {
            if (player == null || waist == null) return;
            if(GetComponent<PlayerDamage>() is { IsRecoveringShock:true } recovering) recovering.Ragdoll.ResetAnimatedBones();
            SampleArmClocks();
            if(IsReaching||reachInputHeld)
                UpdateReachCrouch();
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
                    UpdateRightForearmReach(Time.deltaTime);
                    UpdateRightHandPickup(Time.deltaTime);
                }
                if(freeLeftHandReach)
                {
                    UpdateLeftForearmReach(Time.deltaTime);
                    UpdateLeftHandPickup(Time.deltaTime);
                }
                ApplyChargingSupportArm();
                SetSlideSmoke(true);
                ApplyRightHandObjectGripPose();
                ApplyLeftHandObjectGripPose();
                ApplyRightHandPunchPose();
                ApplyRightHandStrikePose();
                ApplyTwoHandHoldPose();
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
            float phaseAdvance=0f;
            if (legMovement > 0.01f)
            {
                float cycleSpeed=grounded?6f*1.45f*legMovement*(sprinting?1.25f:1f):
                    airborneLegSwing?6f*1.45f*legMovement*
                        (airborneArmWasSprinting?1.25f:1f):6f;
                phaseAdvance=cycleSpeed*Time.deltaTime;
            }
            else if (turning > 0.01f)
                phaseAdvance=Mathf.Abs(player.LowerBodyTurnRate)*0.04f*Time.deltaTime;
            stepPhase+=phaseAdvance;
            leftInjuredLegPhase=LegSpeed(true)==1f?stepPhase:leftInjuredLegPhase+phaseAdvance*LegSpeed(true);
            rightInjuredLegPhase=LegSpeed(false)==1f?stepPhase:rightInjuredLegPhase+phaseAdvance*LegSpeed(false);
            float leftLegCycle=Mathf.Sin(leftInjuredLegPhase)*LegSwingAmount(true),
                rightLegCycle=Mathf.Sin(rightInjuredLegPhase)*LegSwingAmount(false);
            float smoothing = 1f - Mathf.Exp(-16f * Time.deltaTime);
            float hipSwing=38f*legSprintScale;
            float armMovement=airborneArmSwing?airborneArmMovement:movement;
            float leftArmSwingScale=HeldArmSwingScale(heldLeftHandItem);
            float rightArmSwingScale=HeldArmSwingScale(heldRightHandItem);
            leftHeldArmPhase=heldLeftHandItem==null&&ArmMovementSpeed(true)==1f?stepPhase:
                leftHeldArmPhase+phaseAdvance*ArmMovementSpeed(true)*(heldLeftHandItem==null?1f:HeldArmSwingSpeedScale(heldLeftHandItem.PickupMassKg));
            rightHeldArmPhase=heldRightHandItem==null&&ArmMovementSpeed(false)==1f?stepPhase:
                rightHeldArmPhase+phaseAdvance*ArmMovementSpeed(false)*(heldRightHandItem==null?1f:HeldArmSwingSpeedScale(heldRightHandItem.PickupMassKg));
            float leftArmCycle=Mathf.Sin(leftHeldArmPhase)*ArmSwingAmount(true);
            float rightArmCycle=Mathf.Sin(rightHeldArmPhase)*ArmSwingAmount(false);
            float armSprintScale=airborneArmSwing&&airborneArmWasSprinting?1.35f:sprintScale;
            float armSwingScale=airborneArmSwing?0.7f:1f;
            float shoulderSwing=42f*armSprintScale*
                ((airborneArmSwing&&airborneArmWasSprinting)||sprinting?1.4f:1f);
            float idleStride=grounded?1f-movement:0f;
            float legSpread=Mathf.Lerp(10f,0f,legMovement);
            // Restore the alternating lateral step. Its crossing half is much
            // smaller, while the outward half reaches slightly farther than the
            // original 30-degree swing.
            bool crouching=player.IsCrouching&&grounded||lowPickupReach;
            bool standingIdle=grounded&&!crouching&&!airborneArmSwing&&
                player.PlanarSpeed<=0.05f;
            float crouchBend=crouching?45f+15f*player.PickupCrouchDepth:0f;
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
            float crouchAdvance=crouching&&!idleCrouching?Mathf.Lerp(2.2f,3.4f,Mathf.Clamp01(player.PlanarSpeed/CrouchForwardSpeed))*
                (lowPickupReach?PickupCrouchAnimationSpeedScale:1f)*Time.deltaTime:0f;
            leftInjuredCrouchPhase=!crouching||idleCrouching?0f:LegSpeed(true)==1f?crouchStepPhase:leftInjuredCrouchPhase+crouchAdvance*LegSpeed(true);
            rightInjuredCrouchPhase=!crouching||idleCrouching?0f:LegSpeed(false)==1f?crouchStepPhase:rightInjuredCrouchPhase+crouchAdvance*LegSpeed(false);
            float leftCrouchSwitch=Mathf.Lerp(.5f,Mathf.SmoothStep(0,1,.5f-.5f*Mathf.Cos(leftInjuredCrouchPhase)),LegSwingAmount(true));
            float rightCrouchSwitch=Mathf.Lerp(.5f,Mathf.SmoothStep(0,1,.5f-.5f*Mathf.Cos(rightInjuredCrouchPhase)),LegSwingAmount(false));
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
                        PoseLocalEuler(leftHip,new Vector3(
                            130f+15f*player.PickupCrouchDepth,10f,20f),legCrouchPoseSmoothing);
                        PoseLocalEuler(rightHip,new Vector3(
                            60f+15f*player.PickupCrouchDepth,0f,0f),legCrouchPoseSmoothing);
                    }
                    else
                    {
                        Quaternion leftCrouchA=Quaternion.Euler(-190f,0f,10f);
                        Quaternion leftCrouchB=Quaternion.Euler(120f,0f,0f);
                        Quaternion rightCrouchA=Quaternion.Euler(120f,0f,0f);
                        Quaternion rightCrouchB=Quaternion.Euler(-190f,0f,0f);
                        Quaternion leftAnimatedPose=Quaternion.Slerp(leftCrouchA,leftCrouchB,leftCrouchSwitch);
                        Quaternion rightAnimatedPose=Quaternion.Slerp(rightCrouchA,rightCrouchB,rightCrouchSwitch);
                        Quaternion leftHipPose=Quaternion.Slerp(
                            Quaternion.Euler(130f,10f,20f),leftAnimatedPose,
                            crouchStepAmount);
                        Quaternion rightHipPose=Quaternion.Slerp(
                            Quaternion.Euler(60f,0f,0f),rightAnimatedPose,
                            crouchStepAmount);
                        // Keep crouch strafing exactly mirrored in both directions.
                        // The former asymmetric curve made one half of rightward
                        // movement much larger than the other half.
                        leftHipPose=Quaternion.AngleAxis(Mathf.Sin(leftInjuredCrouchPhase)*LegSwingAmount(true)*crouchLateralAmount*35f*CrouchHorizontalAnimationScale,
                            leftHipSpreadAxis)*leftHipPose;
                        rightHipPose=Quaternion.AngleAxis(-Mathf.Sin(rightInjuredCrouchPhase)*LegSwingAmount(false)*crouchLateralAmount*35f*CrouchHorizontalAnimationScale,
                            rightHipSpreadAxis)*rightHipPose;
                        PoseLocalRotation(leftHip,leftHipPose,legCrouchPoseSmoothing);
                        PoseLocalRotation(rightHip,rightHipPose,legCrouchPoseSmoothing);
                    }
                }
                else
                {
                    PoseBoneSpread(leftHip,leftHipRestRotation,leftHipPitchAxis,
                        leftLegCycle*(hipSwing*legMovement+11f*turning)+idleStride*7f+idleLegBend,leftHipSpreadAxis,
                        -legSpread+(leftLegCycle*strafe>=0?leftLegCycle*strafe*14f:leftLegCycle*strafe*36f),smoothing);
                    PoseBoneSpread(rightHip,rightHipRestRotation,rightHipPitchAxis,
                        -rightLegCycle*(hipSwing*legMovement+11f*turning)-idleStride*7f+idleLegBend,rightHipSpreadAxis,
                        legSpread-(rightLegCycle*strafe>=0?rightLegCycle*strafe*14f:rightLegCycle*strafe*36f),smoothing);
                }
            }
            float armSpread=grounded||airborneArmSwing?
                Mathf.Lerp(20f,30f,armMovement):70f;
            float leftStrafeArm=(strafe>0f?-strafe*30f:-strafe*20f)*ArmSwingAmount(true);
            float rightStrafeArm=(strafe>0f?-strafe*20f:-strafe*30f)*ArmSwingAmount(false);
            float leftAirborneArmLift=grounded||airborneArmSwing?0f:-55f;
            float rightAirborneArmLift=grounded||airborneArmSwing?0f:-65f;
            float leftShoulderPitch=-leftArmCycle*shoulderSwing*armMovement*armSwingScale*
                leftArmSwingScale-idleStride*7f+
                leftAirborneArmLift;
            float rightShoulderPitch=rightArmCycle*shoulderSwing*armMovement*armSwingScale*
                rightArmSwingScale+idleStride*7f+
                rightAirborneArmLift;
            float leftShoulderSpread=-armSpread+leftStrafeArm;
            float rightShoulderSpread=armSpread+rightStrafeArm;
            const float elbowBase=18f;
            const float elbowCurve=48f;
            float leftBend=(elbowBase+Mathf.Max(0f,leftArmCycle)*elbowCurve*
                leftArmSwingScale)*
                armMovement*armSprintScale*armSwingScale;
            float rightBend=(elbowBase+Mathf.Max(0f,-rightArmCycle)*elbowCurve*
                rightArmSwingScale)*
                armMovement*armSprintScale*armSwingScale;
            bool returningAfterThrow=returningRightHandAfterThrow&&
                ArmTime(throwUsingLeftHand)>=throwFollowThroughUntil;
            bool returningLeftAfterThrow=returningLeftHandAfterThrow&&ArmTime(throwUsingLeftHand)>=throwFollowThroughUntil;
            float rightArmSmoothing=returningRightHandWithItem?
                1f-Mathf.Exp(-heldItemReturnRate*Time.deltaTime):
                returningAfterThrow?1f-Mathf.Exp(-ThrownHandReturnRate*Time.deltaTime):smoothing;
            float leftArmSmoothing=returningLeftHandWithItem?
                1f-Mathf.Exp(-leftHeldItemReturnRate*Time.deltaTime):
                returningLeftAfterThrow?1f-Mathf.Exp(-ThrownHandReturnRate*Time.deltaTime):smoothing;
            Quaternion leftShoulderTarget;
            Quaternion leftElbowTarget;
            Quaternion rightShoulderTarget;
            Quaternion rightElbowTarget;
            if(standingIdle)
            {
                leftShoulderTarget=Quaternion.Euler(173f,3f,3f);
                leftElbowTarget=Quaternion.Euler(-10f,-3f,3f);
                rightShoulderTarget=Quaternion.Euler(-183f,-3f,-3f);
                rightElbowTarget=Quaternion.Euler(-10f,3f,-3f);
                PoseLocalRotation(rightShoulder,rightShoulderTarget,rightArmSmoothing);
                PoseLocalRotation(rightElbow,rightElbowTarget,rightArmSmoothing);
                PoseLocalRotation(leftShoulder,leftShoulderTarget,leftArmSmoothing);
                PoseLocalRotation(leftElbow,leftElbowTarget,leftArmSmoothing);
            }
            else if(crouching)
            {
                leftShoulderTarget=leftShoulderRestRotation;
                leftElbowTarget=leftElbowRestRotation;
                rightShoulderTarget=rightShoulderRestRotation;
                rightElbowTarget=rightElbowRestRotation;
                PoseLocalRotation(leftShoulder,leftShoulderTarget,
                    returningLeftHandWithItem?leftArmSmoothing:crouchPoseSmoothing);
                PoseLocalRotation(rightShoulder,rightShoulderTarget,
                    returningRightHandWithItem?rightArmSmoothing:crouchPoseSmoothing);
                PoseLocalRotation(leftElbow,leftElbowTarget,
                    returningLeftHandWithItem?leftArmSmoothing:crouchPoseSmoothing);
                PoseLocalRotation(rightElbow,rightElbowTarget,
                    returningRightHandWithItem?rightArmSmoothing:crouchPoseSmoothing);
            }
            else
            {
                leftShoulderTarget=Quaternion.AngleAxis(leftShoulderSpread,leftShoulderSpreadAxis)*
                    Quaternion.AngleAxis(leftShoulderPitch,leftShoulderPitchAxis)*leftShoulderRestRotation;
                leftElbowTarget=Quaternion.AngleAxis(-leftBend,leftElbowPitchAxis)*leftElbowRestRotation;
                rightShoulderTarget=Quaternion.AngleAxis(rightShoulderSpread,rightShoulderSpreadAxis)*
                    Quaternion.AngleAxis(rightShoulderPitch,rightShoulderPitchAxis)*rightShoulderRestRotation;
                rightElbowTarget=Quaternion.AngleAxis(-rightBend,rightElbowPitchAxis)*rightElbowRestRotation;
                PoseLocalRotation(leftShoulder,leftShoulderTarget,leftArmSmoothing);
                PoseLocalRotation(rightShoulder,rightShoulderTarget,rightArmSmoothing);
                PoseLocalRotation(leftElbow,leftElbowTarget,leftArmSmoothing);
                PoseLocalRotation(rightElbow,rightElbowTarget,rightArmSmoothing);
            }
            bool leftArmReachedTarget=
                Quaternion.Angle(leftShoulder.localRotation,leftShoulderTarget)<1f&&
                Quaternion.Angle(leftElbow.localRotation,leftElbowTarget)<1f;
            bool twoHandHeld=heldLeftHandItem!=null&&heldLeftHandItem==heldRightHandItem;
            if(returningLeftHandWithItem&&leftArmReachedTarget&&(!twoHandHeld||twoHandPrimaryLeft)||
                returningLeftAfterThrow&&(leftArmReachedTarget||ArmTime(throwUsingLeftHand)-throwFollowThroughUntil>=MaximumThrownHandReturnDuration))
            {
                returningLeftHandWithItem=false;returningLeftHandAfterThrow=false;
            }
            bool rightArmReachedTarget=
                Quaternion.Angle(rightShoulder.localRotation,rightShoulderTarget)<1f&&
                Quaternion.Angle(rightElbow.localRotation,rightElbowTarget)<1f;
            bool throwReturnTimedOut=returningAfterThrow&&
                ArmTime(throwUsingLeftHand)-throwFollowThroughUntil>=MaximumThrownHandReturnDuration;
            if((returningRightHandWithItem&&rightArmReachedTarget&&
                (!twoHandHeld||!twoHandPrimaryLeft))||
                (returningAfterThrow&&(rightArmReachedTarget||throwReturnTimedOut)))
            {
                returningRightHandWithItem=false;
                returningRightHandAfterThrow=false;
            }
            const float kneeBase=10f;
            const float kneeCurve=48f;
            float leftKneeBend=(kneeBase+Mathf.Max(0f,-leftLegCycle)*kneeCurve)*
                legMovement*legSprintScale+7f*turning+idleLegBend;
            float rightKneeBend=(kneeBase+Mathf.Max(0f,rightLegCycle)*kneeCurve)*
                legMovement*legSprintScale+7f*turning+idleLegBend;
            if(!holdAirborneLegPose)
            {
                if(standingIdle)
                {
                    PoseLocalEuler(leftKnee,new Vector3(11f,0f,0f),smoothing);
                    PoseLocalEuler(rightKnee,new Vector3(11f,0f,0f),smoothing);
                }
                else if(crouching)
                {
                    Quaternion leftKneeBase=Quaternion.Euler(120f,0f,0f);
                    Quaternion rightKneeBase=Quaternion.Euler(110f,0f,0f);
                    Quaternion leftKneeCycle=Quaternion.Slerp(leftKneeBase,
                        Quaternion.Euler(110f,0f,0f),leftCrouchSwitch);
                    Quaternion rightKneeCycle=Quaternion.Slerp(rightKneeBase,
                        Quaternion.Euler(120f,0f,0f),rightCrouchSwitch);
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
                        Quaternion.Euler(-70f,0f,0f),leftCrouchSwitch);
                    Quaternion rightAnkleCycle=Quaternion.Slerp(rightAnkleBase,
                        Quaternion.Euler(26f,0f,0f),rightCrouchSwitch);
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
                        leftLegCycle*(24f*stride+6f*turning)+crouchBend,smoothing);
                    PoseBone(rightAnkle,rightAnkleRestRotation,rightAnklePitchAxis,
                        -rightLegCycle*(24f*stride+6f*turning)+crouchBend,smoothing);
                    UpdateFootCollider(leftFootCollider,leftAnkle);
                    UpdateFootCollider(rightFootCollider,rightAnkle);
                }
            }
            ApplySingleLegSupportPose();
            float cameraShake=UpdateCameraShake(grounded,movement,sprinting);
            UpdatePickupPelvisPose();
            UpdateWaistReachBow();
            UpdatePickupReach(Time.deltaTime);
            ApplyRightHandThrowPose();
            ApplyChargingSupportArm();
            ApplyRightHandObjectGripPose();
            ApplyLeftHandObjectGripPose();
            ApplyRightHandPunchPose();
            ApplyRightHandStrikePose();
            ApplyTwoHandHoldPose();
            UpdateCameraPosition(cameraShake);
        }

        internal void UpdatePickupReach(float deltaTime)
        {
            // Test current contact before any animation moves the hand away.
            TryFinishHandPickup();
            PollFreeHandContacts();
            UpdateRightForearmReach(deltaTime);
            UpdateLeftForearmReach(deltaTime);
            UpdateRightHandPickup(deltaTime);
            UpdateLeftHandPickup(deltaTime);
            PollFreeHandContacts();
        }
        internal void ApplySingleLegSupportPose(float step=-1f)
        {
            var damage=GetComponent<PlayerDamage>();
            if(damage==null||!damage.SingleLegSupport||player==null) { supportLegPhase=0;return; }
            bool left=!damage.IsLegDisabled(true);
            float dt=step>=0?step:Time.deltaTime;
            float amount=Mathf.Clamp01(player.PlanarSpeed/Mathf.Max(.05f,CrouchForwardSpeed*damage.MovementSpeedScale));
            if(amount>.01f) supportLegPhase+=Mathf.Lerp(2.2f,3.4f,amount)*dt*LegSpeed(left);else supportLegPhase=0;
            float gait=Mathf.Lerp(.5f,Mathf.SmoothStep(0,1,.5f-.5f*Mathf.Cos(supportLegPhase)),LegSwingAmount(left));
            // Either surviving leg uses the right-leg crouch pose and cycle.
            Quaternion hip=Quaternion.Slerp(Quaternion.Euler(60,0,0),Quaternion.Slerp(Quaternion.Euler(120,0,0),Quaternion.Euler(-190,0,0),gait),amount);
            Quaternion knee=Quaternion.Euler(Mathf.Lerp(110,120,gait*amount),0,0);
            Quaternion ankle=Quaternion.Euler(Mathf.Lerp(-70,26,gait*amount),0,0);
            if(left) { hip=MirrorPunchPose(hip);knee=MirrorPunchPose(knee);ankle=MirrorPunchPose(ankle); }
            var hipBone=left?leftHip:rightHip;var kneeBone=left?leftKnee:rightKnee;var ankleBone=left?leftAnkle:rightAnkle;
            float blend=1f-Mathf.Exp(-16f*dt*LegSpeed(left));
            hipBone.localRotation=Quaternion.Slerp(hipBone.localRotation,hip,blend);
            kneeBone.localRotation=Quaternion.Slerp(kneeBone.localRotation,knee,blend);
            ankleBone.localRotation=Quaternion.Slerp(ankleBone.localRotation,ankle,blend);
            UpdateFootCollider(left?leftFootCollider:rightFootCollider,ankleBone);
        }

        private void PollFreeHandContacts()
        {
            if(IsReaching&&!IsReachingForObject)
            {
                Physics.SyncTransforms();
                if(IsRightReaching) rightHandPickupTrigger?.PollContact();
                if(IsLeftReaching) leftHandPickupTrigger?.PollContact();
            }
        }

        private void UpdateRightHandPickup(float deltaTime)
        {
            deltaTime*=ArmMovementSpeed(false);
            if(!IsRightReaching||rightShoulder==null||rightElbow==null||rightHandMarker==null) return;
            if(pickupTarget!=null&&
                (pickupTarget.IsHeld||Vector3.Distance(rightShoulder.position,
                    PickupGripPoint())>RightPickupRange))
            {
                CancelRightHandPickup();
                return;
            }
            Vector3 target=RightHandReachTarget();
            // Advance the desired hand point at the requested speed, then solve
            // the shoulder/elbow chain toward the continuously extending reach.
            pickupHandGoal=Vector3.MoveTowards(pickupHandGoal,target,
                RightHandReachSpeed*deltaTime);
            rightShoulder.localRotation=pickupShoulderPose;
            rightElbow.localRotation=pickupElbowPose;
            for(int pass=0;pass<8;pass++)
            {
                AimJointAtTarget(rightElbow,rightHandMarker,pickupHandGoal,1f);
                AimJointAtTarget(rightShoulder,rightHandMarker,pickupHandGoal,1f);
            }
            TryFinishHandPickup();
        }

        private void UpdateLeftHandPickup(float deltaTime)
        {
            deltaTime*=ArmMovementSpeed(true);
            if(!IsLeftReaching||leftShoulder==null||leftElbow==null||leftHandMarker==null) return;
            if(pickupTarget!=null&&
                (pickupTarget.IsHeld||Vector3.Distance(leftShoulder.position,
                    PickupGripPoint())>RightPickupRange))
            {
                CancelRightHandPickup();
                return;
            }
            Vector3 target=pickupTarget!=null?PickupGripPoint():
                cameraTransform!=null?cameraTransform.position+cameraTransform.forward*RightPickupRange:
                leftShoulder.position+transform.forward*RightPickupRange;
            leftPickupHandGoal=Vector3.MoveTowards(leftPickupHandGoal,target,
                RightHandReachSpeed*deltaTime);
            leftShoulder.localRotation=leftPickupShoulderPose;
            leftElbow.localRotation=leftPickupElbowPose;
            for(int pass=0;pass<8;pass++)
            {
                AimJointAtTarget(leftElbow,leftHandMarker,leftPickupHandGoal,1f);
                AimJointAtTarget(leftShoulder,leftHandMarker,leftPickupHandGoal,1f);
            }
            TryFinishHandPickup();
        }

        private void TryFinishHandPickup()
        {
            if(pickupTarget==null||pickupDragging) return;
            if(!WebWorldSession.CanInteract(pickupTarget.gameObject))
            { CancelRightHandPickup();return; }
            Vector3 grip=PickupGripPoint();
            bool rightTouching=rightHandPickupTrigger!=null&&
                rightHandPickupTrigger.IsTouching(pickupTarget,grip);
            bool leftTouching=leftHandPickupTrigger!=null&&
                leftHandPickupTrigger.IsTouching(pickupTarget,grip);
            if(pickupUsingBothHands?!rightTouching||!leftTouching:
                pickupUsingLeftHand?!leftTouching:!rightTouching) return;
            // Complete pickup only when the hand sphere reaches the object.
            Transform primaryHand=pickupUsingLeftHand?leftHandMarker:rightHandMarker;
            if(pickupUsingBothHands)
                twoHandPrimaryLeft=pickupUsingLeftHand;
            if(!pickupTarget.PickUp(primaryHand,pickupUsingBothHands,
                PickupGripPoint())) return;
            float returnRate=HeldReturnRate(pickupTarget);
            if(pickupUsingLeftHand||pickupUsingBothHands)
            {
                heldLeftHandItem=pickupTarget;
                leftHeldItemReturnRate=returnRate;
                returningLeftHandWithItem=true;
            }
            if(!pickupUsingLeftHand||pickupUsingBothHands)
            {
                heldRightHandItem=pickupTarget;
                heldItemReturnRate=returnRate;
                returningRightHandWithItem=true;
            }
            rightHandPickupTrigger?.Forget(pickupTarget);
            leftHandPickupTrigger?.Forget(pickupTarget);
            rightElbow.localPosition=rightElbowRestPosition;
            leftElbow.localPosition=leftElbowRestPosition;
            rightHandMarker.localPosition=RightHandPointOnElbow;
            leftHandMarker.localPosition=leftHandRestPosition;
            pickupTarget=null;
            pickupGripLocal=Vector3.zero;
            RestorePickupPosture();
        }

        private void FixedUpdate()
        {
            if(!pickupDragging||pickupTarget==null||player==null||player.IsPaused||
                !Application.isFocused||pickupTarget.IsHeld) return;
            Vector3 grip=PickupGripPoint();
            int hands=0;
            if(IsRightReaching&&!HasRightHandItem&&rightHandPickupTrigger!=null&&
                Vector3.Distance(rightShoulder.position,grip)<=RightPickupRange&&
                rightHandPickupTrigger.IsTouching(pickupTarget,grip)) hands++;
            if(IsLeftReaching&&!HasLeftHandItem&&leftHandPickupTrigger!=null&&
                Vector3.Distance(leftShoulder.position,grip)<=RightPickupRange&&
                leftHandPickupTrigger.IsTouching(pickupTarget,grip)) hands++;
            pickupTarget.DragToward(player.transform.position,hands,Time.fixedDeltaTime,grip);
        }

        internal void OnHandPickupTriggerContact(RightHandPickupTrigger handTrigger,
            PickableObject target)
        {
            if(player==null||player.IsPaused||target==null||target.IsHeld||
                !IsReaching) return;
            bool left=handTrigger==leftHandPickupTrigger;
            bool right=handTrigger==rightHandPickupTrigger;
            if(!left&&!right||left&&!IsLeftReaching||right&&!IsRightReaching) return;
            if(pickupTarget==null)
            {
                Vector3 grip=target.NearestMeshSurfacePoint(handTrigger.transform.position);
                if(!BeginHandPickup(target,left,grip)) return;
            }
            if(pickupTarget==target) TryFinishHandPickup();
        }

        private static float HeldArmSwingScale(PickableObject heldItem)
        {
            if(heldItem!=null&&heldItem.GrabMode==PickableGrabMode.Stick) return 0f;
            return heldItem==null?1f:
                InitialHeldArmSwingScale-MaximumHeldArmSwingReduction*Mathf.Clamp01(
                    heldItem.PickupMassKg/PickableObject.TwoHandMassLimitKg);
        }

        private static float HeldArmSwingSpeedScale(float mass)
        {
            return Mathf.Lerp(1f,HeldArmSwingSpeedAt60Kg,Mathf.Clamp01(mass/60f));
        }

        private static float HeldItemReturnSpeedScale(float mass)
        {
            if(mass<=PickableObject.OneHandMassLimitKg)
                return Mathf.Lerp(1f,HeavyHeldItemReturnSpeedScale,
                    Mathf.InverseLerp(FullSpeedPickupMassKg,PickableObject.OneHandMassLimitKg,mass));
            return Mathf.Lerp(HeavyHeldItemReturnSpeedScale,TwoHandReturnSpeedScale,
                Mathf.InverseLerp(PickableObject.OneHandMassLimitKg,
                    PickableObject.TwoHandMassLimitKg,mass));
        }

        internal static float HeldReturnRate(PickableObject item)=>item.IsMineralGrip?16f:
            HeldItemReturnRate*HeldItemReturnSpeedScale(item.PickupMassKg);

        private void ApplyTwoHandHoldPose()
        {
            if(!CanUseHand(false)||!CanUseHand(true)) return;
            if(heldLeftHandItem==null||heldLeftHandItem!=heldRightHandItem) return;
            PickableObject held=heldLeftHandItem;
            if(rightShoulder==null||rightElbow==null||leftShoulder==null||
                rightHandMarker==null||leftHandMarker==null) return;
            if(!twoHandPrimaryLeft&&!IsStrikeMode&&!strikeRebounding)
            {
                // Bring the carrying hand toward the torso centre so the left
                // hand has room to reach a separate vertex on the held mesh.
                Vector3 centre=(leftShoulder.position+rightShoulder.position)*0.5f;
                float rightOfCentre=Vector3.Dot(rightHandMarker.position-centre,
                    transform.right);
                Vector3 rightTarget=rightHandMarker.position-transform.right*
                    Mathf.Min(TwoHandGripOffset,Mathf.Max(0f,rightOfCentre));
                float rightBlend=1f-Mathf.Exp(-12f*Time.deltaTime);
                for(int pass=0;pass<4;pass++)
                {
                    AimJointAtTarget(rightElbow,rightHandMarker,rightTarget,rightBlend);
                    AimJointAtTarget(rightShoulder,rightHandMarker,rightTarget,rightBlend);
                }
            }
            Transform shoulder=twoHandPrimaryLeft?rightShoulder:leftShoulder;
            Transform elbow=twoHandPrimaryLeft?rightElbow:leftElbow;
            Transform hand=twoHandPrimaryLeft?rightHandMarker:leftHandMarker;
            if(shoulder==null||elbow==null||hand==null||
                leftHandMarker==null||rightHandMarker==null) return;
            Vector3 target=held.NearestGripVertex(hand.position,
                twoHandPrimaryLeft?leftHandMarker.position:rightHandMarker.position);
            bool returning=twoHandPrimaryLeft?returningRightHandWithItem:returningLeftHandWithItem;
            float rate=returning?
                twoHandPrimaryLeft?heldItemReturnRate:leftHeldItemReturnRate:12f;
            float blend=1f-Mathf.Exp(-rate*Time.deltaTime);
            if(IsStrikeMode)
            {
                Vector3 restPosition=twoHandPrimaryLeft?
                    rightElbowRestPosition:leftElbowRestPosition;
                elbow.localPosition=Vector3.Lerp(elbow.localPosition,
                    restPosition*StrikeArmStretchScale,blend);
            }
            for(int pass=0;pass<4;pass++)
            {
                AimJointAtTarget(elbow,hand,target,blend);
                AimJointAtTarget(shoulder,hand,target,blend);
            }
            if(Vector3.Distance(hand.position,target)<0.03f)
            {
                if(twoHandPrimaryLeft) returningRightHandWithItem=false;
                else returningLeftHandWithItem=false;
            }
        }

        private void ApplyRightHandObjectGripPose()
        {
            if(punching&&!punchUsingLeftHand) return;
            bool reaching=IsRightReaching;
            bool holdingStick=UsesStickFingerGrip(heldRightHandItem);
            bool holdingIrregular=heldRightHandItem!=null&&
                !UsesStickFingerGrip(heldRightHandItem);
            float blend=1f-Mathf.Exp(-16f*ArmStep(false));
            for(int i=0;i<punchDigits.Length;i++)
            {
                Transform digit=punchDigits[i];
                if(digit==null) continue;
                Quaternion target=punchDigitRest[i];
                if(holdingStick)
                    target=Quaternion.Euler(PunchDigitAngles[i]);
                else if(holdingIrregular)
                    target=Quaternion.Euler(IrregularGripDigitAngles[i]);
                else if(reaching&&i==0)
                    target=Quaternion.Euler(0f,0f,10f);
                else if(reaching&&i==1)
                    target=Quaternion.Euler(0f,0f,20f);
                digit.localRotation=holdingStick?target:
                    Quaternion.Slerp(digit.localRotation,target,blend);
            }
        }

        private void ApplyLeftHandObjectGripPose()
        {
            if(punching&&punchUsingLeftHand) return;
            bool holdingStick=UsesStickFingerGrip(heldLeftHandItem);
            bool holdingIrregular=heldLeftHandItem!=null&&
                !UsesStickFingerGrip(heldLeftHandItem);
            float blend=1f-Mathf.Exp(-16f*ArmStep(true));
            for(int i=0;i<leftGripDigits.Length;i++)
            {
                Transform digit=leftGripDigits[i];
                if(digit==null) continue;
                Quaternion target=leftGripDigitRest[i];
                if(holdingStick)
                    target=MirrorPunchPose(Quaternion.Euler(PunchDigitAngles[i]));
                else if(holdingIrregular)
                {
                    Vector3 angle=IrregularGripDigitAngles[i];
                    target=Quaternion.Euler(angle.x,-angle.y,-angle.z);
                }
                else if(IsLeftReaching&&i<2)
                    target=Quaternion.Euler(0f,0f,i==0?-10f:-20f);
                digit.localRotation=holdingStick?target:
                    Quaternion.Slerp(digit.localRotation,target,blend);
            }
        }

        private void UpdatePickupPelvisPose()
        {
            if(pelvis==null) return;
            Quaternion target=lowPickupReach?Quaternion.Euler(
                90f+15f*player.PickupCrouchDepth,0f,0f):
                pelvisRestRotation;
            float rate=lowPickupReach?
                PickupReachRate*PickupCrouchAnimationSpeedScale:
                returningRightHandWithItem&&returningLeftHandWithItem?
                    Mathf.Min(heldItemReturnRate,leftHeldItemReturnRate):
                returningRightHandWithItem?heldItemReturnRate:
                returningLeftHandWithItem?leftHeldItemReturnRate:HeldItemReturnRate;
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

        private void UpdateRightForearmReach(float deltaTime)
        {
            deltaTime*=ArmMovementSpeed(false);
            if(striking) return;
            float stretch=1f;
            if(IsRightReaching)
            {
                float targetDistance=Vector3.Distance(rightShoulder.position,
                    RightHandReachTarget());
                stretch=Mathf.Clamp(targetDistance/Mathf.Max(0.01f,rightArmReach),
                    1f,MaximumRightArmStretchScale);
            }
            float rate=IsRightReaching?PickupReachRate:
                returningRightHandWithItem?heldItemReturnRate:HeldItemReturnRate;
            rightElbow.localPosition=Vector3.Lerp(rightElbow.localPosition,
                rightElbowRestPosition*stretch,1f-Mathf.Exp(-rate*deltaTime));
            rightElbow.localScale=rightElbowRestScale;
            if(rightHandPickupTrigger!=null)
                rightHandPickupTrigger.SetWorldDiameter(RightHandTriggerDiameter);
        }

        private void UpdateLeftForearmReach(float deltaTime)
        {
            deltaTime*=ArmMovementSpeed(true);
            if(leftElbow==null||leftShoulder==null||leftHandMarker==null) return;
            float stretch=1f;
            if(IsLeftReaching)
            {
                Vector3 target=pickupTarget!=null?PickupGripPoint():
                    cameraTransform!=null?cameraTransform.position+cameraTransform.forward*RightPickupRange:
                    leftShoulder.position+transform.forward*RightPickupRange;
                float targetDistance=Vector3.Distance(leftShoulder.position,target);
                stretch=Mathf.Clamp(targetDistance/Mathf.Max(0.01f,leftArmReach),
                    1f,MaximumRightArmStretchScale);
            }
            float rate=IsLeftReaching?PickupReachRate:
                returningLeftHandWithItem?leftHeldItemReturnRate:HeldItemReturnRate;
            leftElbow.localPosition=Vector3.Lerp(leftElbow.localPosition,
                leftElbowRestPosition*stretch,1f-Mathf.Exp(-rate*deltaTime));
            leftElbow.localScale=leftElbowRestScale;
            leftHandPickupTrigger?.SetWorldDiameter(RightHandTriggerDiameter);
        }

        private Vector3 RightHandReachTarget()
        {
            if(pickupTarget!=null) return PickupGripPoint();
            Camera view=player!=null?player.PlayerCamera:null;
            return view!=null?view.transform.position+view.transform.forward*RightPickupRange:
                rightShoulder.position+transform.forward*RightPickupRange;
        }

        private Vector3 PickupGripPoint() =>
            pickupTarget.transform.TransformPoint(pickupGripLocal);

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
            bool handTouching=braking&&!HasLeftHandItem&&!IsLeftReaching&&
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
            Transform strikingHand=strikeUsingLeftHand?leftHandMarker:rightHandMarker;
            if(IsStrikeMode&&strikingHand!=null)
            {
                // Isolate arm motion from walking and head movement.
                Vector3 handMotion=headCameraAnchor.InverseTransformPoint(
                    strikingHand.position)-strikeStartHandLocalPosition;
                handOffset=headCameraAnchor.TransformVector(handMotion)*
                    StrikeCameraHandFollow;
            }
            strikeCameraHandOffset=Vector3.Lerp(strikeCameraHandOffset,handOffset,
                1f-Mathf.Exp(-StrikeArmSmoothingRate*Time.deltaTime));
            cameraTransform.position=headCameraAnchor.position+
                headCameraAnchor.forward*CameraForwardOffset+
                Vector3.up*(verticalOffset-0.18f*player.PickupCrouchDepth)+
                strikeCameraHandOffset;
        }

        private void CreateWristJoints(GameObject model)
        {
            runtimeWristMesh=EnsureWristJoints(model);
            leftWrist=FindChild(model.transform,"LeftWrist");
            rightWrist=FindChild(model.transform,"RightWrist");
            if(leftWrist!=null) leftWristRestPosition=leftWrist.localPosition;
            if(rightWrist!=null) rightWristRestPosition=rightWrist.localPosition;
        }

        // Shared by the runtime rig and the rig editor. A saved rig already
        // containing wrists must not receive duplicate bones or skin weights.
        public static Mesh EnsureWristJoints(GameObject model)
        {
            Transform leftElbow=FindChild(model.transform,"LeftElbow");
            Transform rightElbow=FindChild(model.transform,"RightElbow");
            if(leftElbow==null||rightElbow==null) return null;
            Transform leftWrist=CreateWristJoint(leftElbow,"LeftWrist");
            Transform rightWrist=CreateWristJoint(rightElbow,"RightWrist");
            foreach(string name in new[]{"LeftFingers1","LeftThumb1"})
                FindChild(model.transform,name)?.SetParent(leftWrist,true);
            foreach(string name in new[]{"RightFingers1","RightThumb1"})
                FindChild(model.transform,name)?.SetParent(rightWrist,true);

            SkinnedMeshRenderer body=model.GetComponentsInChildren<SkinnedMeshRenderer>()
                .FirstOrDefault(renderer=>renderer.name=="HumanBody");
            if(body==null||body.sharedMesh==null||!body.sharedMesh.isReadable) return null;
            Transform[] bones=body.bones;
            bool hasLeft=System.Array.IndexOf(bones,leftWrist)>=0;
            bool hasRight=System.Array.IndexOf(bones,rightWrist)>=0;
            if(hasLeft&&hasRight) return null;
            if(hasLeft!=hasRight)
                throw new System.InvalidOperationException("The rig has only one bound wrist.");
            int leftElbowIndex=System.Array.IndexOf(bones,leftElbow);
            int rightElbowIndex=System.Array.IndexOf(bones,rightElbow);
            if(leftElbowIndex<0||rightElbowIndex<0) return null;
            Mesh wristMesh=Instantiate(body.sharedMesh);
            wristMesh.name=body.sharedMesh.name+" Wrist Joints";
            Matrix4x4[] bindposes=wristMesh.bindposes;
            var extendedBindposes=new Matrix4x4[bindposes.Length+2];
            bindposes.CopyTo(extendedBindposes,0);
            extendedBindposes[bindposes.Length]=leftWrist.worldToLocalMatrix*
                leftElbow.localToWorldMatrix*bindposes[leftElbowIndex];
            extendedBindposes[bindposes.Length+1]=rightWrist.worldToLocalMatrix*
                rightElbow.localToWorldMatrix*bindposes[rightElbowIndex];
            wristMesh.bindposes=extendedBindposes;
            Vector3[] vertices=wristMesh.vertices;
            BoneWeight[] weights=wristMesh.boneWeights;
            for(int i=0;i<weights.Length;i++)
            {
                Vector3 left=bindposes[leftElbowIndex].MultiplyPoint3x4(vertices[i]);
                Vector3 right=bindposes[rightElbowIndex].MultiplyPoint3x4(vertices[i]);
                weights[i]=MoveHandWeightToWrist(weights[i],leftElbowIndex,
                    bones.Length,WristSkinAmount(left.y));
                weights[i]=MoveHandWeightToWrist(weights[i],rightElbowIndex,
                    bones.Length+1,WristSkinAmount(right.y));
            }
            wristMesh.boneWeights=weights;
            body.bones=bones.Concat(new[]{leftWrist,rightWrist}).ToArray();
            body.sharedMesh=wristMesh;
            return wristMesh;
        }

        private static Transform CreateWristJoint(Transform elbow,string name)
        {
            Transform wrist=FindChild(elbow,name);
            if(wrist==null)
            {
                wrist=new GameObject(name).transform;
                wrist.SetParent(elbow,false);
                wrist.localPosition=new Vector3(0f,0.0027f,0f);
            }
            return wrist;
        }

        private static float WristSkinAmount(float elbowSpaceY) =>
            Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(0.00235f,0.0031f,elbowSpaceY));

        private static BoneWeight MoveHandWeightToWrist(BoneWeight original,
            int elbowIndex,int wristIndex,float amount)
        {
            if(amount<=0f) return original;
            int[] indices={original.boneIndex0,original.boneIndex1,
                original.boneIndex2,original.boneIndex3,wristIndex};
            float[] values={original.weight0,original.weight1,
                original.weight2,original.weight3,0f};
            for(int i=0;i<4;i++)
                if(indices[i]==elbowIndex)
                {
                    float moved=values[i]*amount;
                    values[i]-=moved;
                    values[4]+=moved;
                }
            if(values[4]<=0f) return original;
            System.Array.Sort(values,indices);
            float total=values[1]+values[2]+values[3]+values[4];
            return new BoneWeight{
                boneIndex0=indices[4],weight0=values[4]/total,
                boneIndex1=indices[3],weight1=values[3]/total,
                boneIndex2=indices[2],weight2=values[2]/total,
                boneIndex3=indices[1],weight3=values[1]/total};
        }

        internal void UpdateHeldWristPose()
        {
            FaceWristTowardHeldObject(leftWrist,leftElbow,heldLeftHandItem,true);
            FaceWristTowardHeldObject(rightWrist,rightElbow,heldRightHandItem,false);
            if(heldLeftHandItem!=null&&heldLeftHandItem.GrabMode==PickableGrabMode.Stick)
                heldLeftHandItem.AlignHeldContact();
            if(heldRightHandItem!=null&&heldRightHandItem!=heldLeftHandItem&&
                heldRightHandItem.GrabMode==PickableGrabMode.Stick)
                heldRightHandItem.AlignHeldContact();
        }

        internal Vector3 HeldStickGripPosition(Transform hand)
        {
            Transform wrist=hand==leftHandMarker?leftWrist:
                hand==rightHandMarker?rightWrist:null;
            if(wrist==null) return hand.position;
            Vector3 restPosition=hand==leftHandMarker?
                leftWristRestPosition:rightWristRestPosition;
            // Keep the reach marker independent of the wrist, but place the
            // stick's centre in the visible grip rather than at a mesh corner.
            Vector3 grip=hand.localPosition-restPosition;
            Transform thumb=hand==leftHandMarker?leftGripDigits[3]:punchDigits[3];
            if(thumb!=null) grip.x=wrist.InverseTransformPoint(thumb.position).x;
            // The original rig has articulated digits only on its right hand.
            // Mirror that thumb's wrist-space position for the left grip.
            else if(hand==leftHandMarker&&rightWrist!=null&&punchDigits[3]!=null)
                grip.x=-rightWrist.InverseTransformPoint(punchDigits[3].position).x;
            return wrist.TransformPoint(grip);
        }

        private void FaceWristTowardHeldObject(Transform wrist,Transform elbow,
            PickableObject held,bool leftHand)
        {
            if(wrist==null||elbow==null||!CanUseHand(leftHand)) return;
            wrist.localPosition=leftHand?leftWristRestPosition:rightWristRestPosition;
            if(held==null||!held.IsHeld)
            { wrist.localRotation=Quaternion.identity; return; }
            if(held.GrabMode==PickableGrabMode.Stick||held.GrabMode==PickableGrabMode.Rope)
            {
                Vector3 stickAxis=held.StickDirection;
                if(stickAxis.sqrMagnitude<0.0001f) stickAxis=held.transform.forward;
                Vector3 wristAxis=-stickAxis;
                // Local Y runs along the forearm and hand, not out of the palm.
                // Using that direction as a palm normal rolled the hand by 90
                // degrees around Z and displaced the wrist to hide the gap.
                Vector3 fingers=Vector3.ProjectOnPlane(elbow.up,wristAxis);
                if(fingers.sqrMagnitude<0.000001f)
                    fingers=Vector3.ProjectOnPlane(elbow.forward,wristAxis);
                Quaternion desired=Quaternion.Inverse(elbow.rotation)*
                    Quaternion.LookRotation(wristAxis,fingers.normalized);
                Quaternion twist=new Quaternion(0f,desired.y,0f,desired.w);
                if(twist.y*twist.y+twist.w*twist.w<0.000001f)
                    twist=Quaternion.identity;
                else twist=twist.normalized;
                Quaternion bend=desired*Quaternion.Inverse(twist);
                // Preserve forearm roll while limiting sideways/flexion bend.
                wrist.localRotation=Quaternion.RotateTowards(Quaternion.identity,
                    bend,35f)*twist;
                return;
            }
            // The mirrored left hand presents local +X as its palm; the right
            // hand presents -X. The held item follows the separate hand marker.
            Vector3 towardObject=held.AimPoint-wrist.position;
            if(towardObject.sqrMagnitude<0.000001f)
                towardObject=held.transform.position-wrist.position;
            if(towardObject.sqrMagnitude<0.000001f)
            { wrist.localRotation=Quaternion.identity; return; }
            Vector3 palmDirection=towardObject.normalized;
            Vector3 localPalm=leftHand?Vector3.right:-Vector3.right;
            Quaternion yTurn=Quaternion.Euler(0f,90f,0f);
            Quaternion baseRotation=elbow.rotation*yTurn;
            wrist.rotation=Quaternion.FromToRotation(
                baseRotation*localPalm,palmDirection)*baseRotation;
        }

        private void CreateHandMarkers()
        {
            leftHandMarker=CreateHandMarker("Left Hand Ground Contact",leftShoulder,
                leftElbow,out leftArmReach);
            leftHandRestPosition=leftHandMarker.localPosition;
            leftHandRestRotation=leftHandMarker.localRotation;
            rightHandMarker=CreateHandMarker("Right Hand Ground Contact",rightShoulder,
                rightElbow,out rightArmReach);
            rightHandMarker.localPosition=RightHandPointOnElbow;
            rightHandRestRotation=rightHandMarker.localRotation;
            rightArmReach=Vector3.Distance(rightShoulder.position,rightHandMarker.position);
            SphereCollider pickupCollider=rightHandMarker.gameObject.AddComponent<SphereCollider>();
            pickupCollider.isTrigger=true;
            rightHandPickupTrigger=rightHandMarker.gameObject.AddComponent<RightHandPickupTrigger>();
            rightHandPickupTrigger.Initialize(pickupCollider,RightHandTriggerDiameter,this);
            SphereCollider leftPickupCollider=leftHandMarker.gameObject.AddComponent<SphereCollider>();
            leftPickupCollider.isTrigger=true;
            leftHandPickupTrigger=leftHandMarker.gameObject.AddComponent<RightHandPickupTrigger>();
            leftHandPickupTrigger.Initialize(leftPickupCollider,RightHandTriggerDiameter,this);
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

            float solveBlend=1f-Mathf.Exp(-14f*ArmStep(shoulder==leftShoulder));
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
            float blend=1f-Mathf.Exp(-ArmAimSmoothingRate*ArmStep(shoulder==leftShoulder));
            smoothedShoulderPose=Quaternion.Slerp(smoothedShoulderPose,
                targetShoulderPose,blend);
            smoothedElbowPose=Quaternion.Slerp(smoothedElbowPose,targetElbowPose,blend);
            shoulder.localRotation=smoothedShoulderPose;
            elbow.localRotation=smoothedElbowPose;
        }

        private void PoseBone(Transform bone, Quaternion rest, Vector3 axis,
            float angle, float smoothing)
        {
            smoothing=ArmBlend(bone,smoothing);
            Quaternion target = Quaternion.AngleAxis(angle, axis) * rest;
            bone.localRotation = Quaternion.Slerp(bone.localRotation, target, smoothing);
        }

        private void PoseBoneSpread(Transform bone,Quaternion rest,Vector3 pitchAxis,
            float pitch,Vector3 spreadAxis,float spread,float smoothing)
        {
            smoothing=ArmBlend(bone,smoothing);
            Quaternion target=Quaternion.AngleAxis(spread,spreadAxis)*
                Quaternion.AngleAxis(pitch,pitchAxis)*rest;
            bone.localRotation=Quaternion.Slerp(bone.localRotation,target,smoothing);
        }

        private void PoseLocalEuler(Transform bone,Vector3 localEuler,float smoothing)
        {
            smoothing=ArmBlend(bone,smoothing);
            bone.localRotation=Quaternion.Slerp(bone.localRotation,
                Quaternion.Euler(localEuler),smoothing);
        }

        private void PoseLocalRotation(Transform bone,Quaternion localRotation,
            float smoothing)
        {
            smoothing=ArmBlend(bone,smoothing);
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
            var damage=GetComponent<PlayerDamage>();
            if(damage!=null&&damage.IsLegDisabled(ankle==leftAnkle)) return;
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
                if (runtimeWristMesh != null) Destroy(runtimeWristMesh);
                if (slideSmokeMaterial != null) Destroy(slideSmokeMaterial);
                if (slideSmokeTexture != null) Destroy(slideSmokeTexture);
            }
            else
            {
                if (material != null) DestroyImmediate(material);
                if (runtimeHeadMesh != null) DestroyImmediate(runtimeHeadMesh);
                if (runtimeWristMesh != null) DestroyImmediate(runtimeWristMesh);
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
    [DefaultExecutionOrder(200)]
    public sealed class HeldWristFollower : MonoBehaviour
    {
        private RiggedPlayerAvatar avatar;
        public void Initialize(RiggedPlayerAvatar owner) => avatar=owner;
        private void LateUpdate() => avatar?.UpdateHeldWristPose();
    }

    public sealed class RightHandPickupTrigger : MonoBehaviour
    {
        private const float ContactTolerance=0.01f;
        private readonly System.Collections.Generic.HashSet<Collider> contacts=new();
        private readonly Collider[] nearbyContacts=new Collider[64];
        private SphereCollider trigger;
        private RiggedPlayerAvatar owner;

        public void Initialize(SphereCollider value,float worldDiameter,
            RiggedPlayerAvatar playerAvatar=null)
        {
            trigger=value;
            owner=playerAvatar;
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

        public bool IsTouching(PickableObject target,Vector3 gripPoint)
        {
            if(target==null||target.IsHeld||trigger==null||!trigger.enabled) return false;
            foreach(Collider contact in contacts)
                if(contact!=null&&target.IsPickupDetectionCollider(contact)&&
                    contact.gameObject.activeInHierarchy&&
                    contact.GetComponentInParent<PickableObject>()==target)
                    return true;
            Vector3 centre=transform.TransformPoint(trigger.center);
            Vector3 scale=transform.lossyScale;
            float radius=trigger.radius*Mathf.Max(Mathf.Abs(scale.x),
                Mathf.Max(Mathf.Abs(scale.y),Mathf.Abs(scale.z)))+ContactTolerance;
            // The selected grip is a point on the object's mesh. The animated
            // hand may reach it in LateUpdate before the physics collider query
            // catches up, so count that direct hand-sphere contact too.
            bool touching=(centre-gripPoint).sqrMagnitude<=radius*radius||
                target.PickupDetectionTouchesSphere(centre,radius);
            return touching;
        }

        internal void PollContact()
        {
            if(trigger==null||!trigger.enabled) return;
            Vector3 scale=transform.lossyScale;
            float radius=trigger.radius*Mathf.Max(Mathf.Abs(scale.x),
                Mathf.Max(Mathf.Abs(scale.y),Mathf.Abs(scale.z)))+ContactTolerance;
            int count=Physics.OverlapSphereNonAlloc(transform.TransformPoint(trigger.center),
                radius,nearbyContacts,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Collide);
            for(int i=0;i<count;i++)
            {
                RegisterContact(nearbyContacts[i]);
                nearbyContacts[i]=null;
                if(owner!=null&&!owner.IsReaching) break;
            }
        }

        public void Forget(PickableObject target)
        {
            if(target==null) return;
            contacts.RemoveWhere(contact=>contact==null||
                contact.GetComponentInParent<PickableObject>()==target);
        }

        private void OnTriggerEnter(Collider other) => RegisterContact(other);
        private void OnTriggerStay(Collider other) => RegisterContact(other);

        private void RegisterContact(Collider other)
        {
            if(other==null) return;
            PickableObject target=other.GetComponentInParent<PickableObject>();
            if(target==null||target.IsHeld||
                !target.IsPickupDetectionCollider(other)) return;
            contacts.Add(other);
            owner?.OnHandPickupTriggerContact(this,target);
        }

        private void OnTriggerExit(Collider other)
        {
            if(other!=null) contacts.Remove(other);
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
