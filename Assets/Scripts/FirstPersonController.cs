using UnityEngine;
using UnityEngine.InputSystem;

namespace Wyvernland
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class FirstPersonController : MonoBehaviour
    {
        public const float PlayerMassKg = 65f;
        public const float AirborneSwingDuration = 0.8f;

        [Header("Movement (metres / second)")]
        [SerializeField] private float walkSpeed = 6f;
        [SerializeField] private float sprintSpeed = 11f;
        [SerializeField] private float groundAcceleration = 32f;
        [SerializeField] private float airAcceleration = 8f;
        [SerializeField] private float jumpHeight = 1.6f;
        [SerializeField] private float gravity = -25f;

        [Header("Look")]
        [SerializeField] private float mouseSensitivity = 0.12f;
        [SerializeField] private float lookSpeedScale = 0.85f;
        [SerializeField] private float lookSmoothing = 16f;
        [SerializeField] private float maximumLookAngle = 89f;
        [SerializeField] private float maximumUpperBodyYaw = 25f;
        private const float AirborneTurnSpeedScale = 0.1f;
        private const float MaximumAirborneTurnSpeed = 30f;
        private const float AirborneTurnBlendRate = 12f;
        private const float FullSpeedAirborneLookRange = 100f;
        private const float StrikeLookSpeedScale = 0.04032f;
        private const float StrikeLookSmoothing = 5f;
        private const float StrikeFastLookSpeedScale = 0.1512f;
        private const float StrikeFastLookSmoothing = 10f;
        private const float StrikeLookRecoverySeconds = 0.6f;
        private const float StrikeCameraBoostStartDegreesPerSecond = 25f;
        internal const float StrikeFullSpeedDegreesPerSecond = 140f;

        private Rigidbody playerBody;
        private CapsuleCollider bodyCollider;
        private Transform bodyCapsule;
        private Quaternion bodyCapsuleRestRotation;
        private float bodyCapsuleStandingHeight;
        private Vector3 bodyCapsuleStandingCenter;
        private Transform view;
        private Vector3 planarVelocity;
        private float verticalVelocity;
        private Vector2 movementInput;
        private bool jumpRequested;
        private bool jumpHeld;
        private bool crouchHeld;
        private bool pickupCrouchForced;
        private float pickupCrouchDepth;
        private bool slideInputLatched;
        private bool grounded;
        private ProceduralMap terrain;
        private RiggedPlayerAvatar movementAvatar;
        private bool sliding;
        private Vector3 slideNormal = Vector3.up;
        private Vector3 slideMoveDirection;
        private bool slideBraking;
        private float pitch;
        private float lookYawOffset;
        private bool airborneRotationSlowed;
        private float airborneTurnBlend;
        private float airborneLookYawOffset;
        private Vector2 smoothedLookDelta;
        private bool strikeLookActiveLastFrame;
        private float strikeCameraSpeedBlend;
        private float strikeLookRecoveryRemaining;
        private float strikeLookRecoveryStartScale=1f;
        private Vector3 spawnPosition;
        private bool paused;
        private bool stationaryActor,aiControlled;
        internal void ConfigureAiActor()
        { aiControlled=true;stationaryActor=false;playerBody.constraints=RigidbodyConstraints.FreezeRotation; }
        internal void AimAiAt(Vector3 point)
        { var delta=point-view.position;pitch=Mathf.Clamp(-Mathf.Atan2(delta.y,new Vector2(delta.x,delta.z).magnitude)*Mathf.Rad2Deg,-50f,50f);UpdateViewRotation(); }
        internal void DriveAi(Vector2 movement,Vector2 look,bool sprint=false)
        { movementInput=movement;IsSprinting=sprint&&movement.y>0f&&!SingleLegSupport;RawLookDelta=look;RawLookSpeedDegreesPerSecond=look.magnitude/Mathf.Max(.001f,Time.deltaTime); }

        internal void ConfigureStationaryActor()
        {
            stationaryActor=true;grounded=true;
            playerBody.constraints=RigidbodyConstraints.FreezeRotation|RigidbodyConstraints.FreezePositionX|RigidbodyConstraints.FreezePositionZ;
        }
        private int inputBlockedFrame = -1;
        public static readonly string[] BindingNames =
            { "Forward", "Backward", "Left", "Right", "Sprint", "Jump", "Crouch", "Interact", "Drop", "Switch", "Console" };
        private static readonly Key[] DefaultKeys =
            { Key.W, Key.S, Key.A, Key.D, Key.LeftShift, Key.Space, Key.C, Key.E, Key.G, Key.H, Key.Backquote };
        public const int InteractBindingIndex = 7;
        public const int DropBindingIndex = 8;
        public const int SwitchBindingIndex = 9;
        public const int ConsoleBindingIndex = 10;
        public const int AimBindingIndex = 11;
        public Key[] Bindings { get; } = (Key[])DefaultKeys.Clone();
        public Key AimKey { get; private set; } = Key.None;
        public bool AimHeld => AimKey==Key.None?
            Mouse.current!=null&&Mouse.current.rightButton.isPressed:
            Keyboard.current!=null&&Keyboard.current[AimKey].isPressed;
        public bool AimPressed => AimKey==Key.None?
            Mouse.current!=null&&Mouse.current.rightButton.wasPressedThisFrame:
            Keyboard.current!=null&&Keyboard.current[AimKey].wasPressedThisFrame;
        public bool AimReleased => AimKey==Key.None?
            Mouse.current!=null&&Mouse.current.rightButton.wasReleasedThisFrame:
            Keyboard.current!=null&&Keyboard.current[AimKey].wasReleasedThisFrame;
        public string AimBindingName => AimKey==Key.None?"Right Mouse":AimKey.ToString();
        public float Sensitivity { get => mouseSensitivity; set => mouseSensitivity = Mathf.Clamp(value, 0.03f, 0.4f); }
        public Camera PlayerCamera => view.GetComponent<Camera>();
        public bool IsPaused => paused;
        public bool MovementDisabled => GetComponent<PlayerDamage>() is { CanMove:false };
        internal bool SingleLegSupport=>GetComponent<PlayerDamage>() is { SingleLegSupport:true };
        internal float InjuryMovementScale=>GetComponent<PlayerDamage>()?.MovementSpeedScale??1f;
        internal void RefreshInjuryLocomotion() {
            if(SingleLegSupport) { IsSprinting=false;IsCrouching=true;StopSliding(); }
        }
        public bool IsDead => GetComponent<PlayerDamage>() is { IsDead:true };
        internal void SetIncapacitated(bool dead)
        {
            movementInput=Vector2.zero;jumpRequested=false;jumpHeld=false;StopSliding();
            playerBody.isKinematic=dead;
        }
        internal void RespawnAfterDeath()
        {
            playerBody.isKinematic=false;
            if(stationaryActor) transform.position=spawnPosition;
            RestoreSaveData(new PlayerSaveData { pose=new PlayerPoseData { position=spawnPosition,yaw=transform.eulerAngles.y,pitch=12 } });
            UpdateCapsuleDimensions();
        }

        public void SetPaused(bool value)
        {
            paused = value;
            inputBlockedFrame = Time.frameCount;
            movementInput=Vector2.zero;
            jumpRequested=false;
            jumpHeld=false;
            crouchHeld=false;
            IsSprinting=false;
            planarVelocity=Vector3.zero;
            StopSliding();
            // Pointer lock/menu focus can swallow key-up events in the browser.
            // Start both sides of the pause boundary with fresh device state.
            if(Keyboard.current!=null) InputSystem.ResetDevice(Keyboard.current);
            if(Mouse.current!=null) InputSystem.ResetDevice(Mouse.current);
            if (value)
            {
                verticalVelocity=0f;
                if(playerBody!=null&&!playerBody.isKinematic) playerBody.linearVelocity=Vector3.zero;
                smoothedLookDelta = Vector2.zero;
                RawLookDelta = Vector2.zero;
                RawLookSpeedDegreesPerSecond = 0f;
                strikeLookActiveLastFrame = false;
                strikeCameraSpeedBlend = 0f;
            }
            SetCursorLocked(!value);
        }

        public void Rebind(int action, Key key)
        {
            if (key == Key.None || key == Key.Escape) return;
            int other = System.Array.IndexOf(Bindings, key);
            if (other >= 0) Bindings[other] = Bindings[action];
            if(key==AimKey) AimKey=Key.None;
            Bindings[action] = key;
            SaveControls();
        }

        public void ResetBindings()
        {
            System.Array.Copy(DefaultKeys, Bindings, Bindings.Length);
            AimKey=Key.None;
            SaveControls();
        }

        public bool RebindAim(Key key)
        {
            if(key==Key.Escape||key!=Key.None&&System.Array.IndexOf(Bindings,key)>=0)
                return false;
            AimKey=key;
            SaveControls();
            return true;
        }

        public void SaveControls()
        {
            for (int i = 0; i < Bindings.Length; i++)
                PlayerPrefs.SetInt("Controls." + BindingNames[i], (int)Bindings[i]);
            PlayerPrefs.SetInt("Controls.Aim",(int)AimKey);
            PlayerPrefs.SetFloat("Controls.Sensitivity", mouseSensitivity);
        }

        public bool IsSprinting { get; private set; }
        public bool IsCrouching { get; private set; }
        public float PickupCrouchDepth => pickupCrouchForced?pickupCrouchDepth:0f;
        public float Speed => playerBody != null ? playerBody.linearVelocity.magnitude : 0f;
        public float PlanarSpeed => playerBody != null
            ? new Vector2(playerBody.linearVelocity.x,playerBody.linearVelocity.z).magnitude : 0f;
        public bool IsGrounded => grounded;
        public bool IsSliding => sliding;
        public bool IsSlideBraking => slideBraking;
        public Vector3 SlideNormal => slideNormal;
        public float SlideSteer { get; private set; }
        public float VerticalSpeed => verticalVelocity;
        public float LookPitch => pitch;
        public float LookYawOffset => lookYawOffset;
        public Vector2 RawLookDelta { get; private set; }
        public float RawLookSpeedDegreesPerSecond { get; private set; }
        public float LowerBodyTurnRate { get; private set; }
        public Vector3 LocalMovement => transform.InverseTransformDirection(planarVelocity);

        public void SetPickupCrouch(bool active,float depth=0f)
        {
            pickupCrouchForced=active;
            pickupCrouchDepth=active?Mathf.Clamp01(depth):0f;
            if(active) IsSprinting=false;
            IsCrouching=(crouchHeld||pickupCrouchForced||SingleLegSupport)&&!IsSprinting;
            UpdateCapsuleDimensions();
        }

        public PlayerSaveData CaptureSaveData()
        {
            return new PlayerSaveData
            {
                pose = new PlayerPoseData
                {
                    position = transform.position,
                    yaw = transform.eulerAngles.y,
                    pitch = pitch,
                    lookYaw = lookYawOffset
                }
            };
        }

        public bool RestoreSaveData(PlayerSaveData data)
        {
            Vector3 position = data.pose.position;
            if (!IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(position.z) ||
                !IsFinite(data.pose.yaw) || !IsFinite(data.pose.pitch) || !IsFinite(data.pose.lookYaw))
                return false;

            Rect bounds = terrain != null ? terrain.Bounds : WorldConfig.MapBounds;
            position.x = Mathf.Clamp(position.x, bounds.xMin + 1f, bounds.xMax - 1f);
            position.z = Mathf.Clamp(position.z, bounds.yMin + 1f, bounds.yMax - 1f);
            playerBody.position = position;
            playerBody.rotation = Quaternion.Euler(0f, data.pose.yaw, 0f);
            playerBody.linearVelocity = Vector3.zero;
            pitch = Mathf.Clamp(data.pose.pitch, -maximumLookAngle, maximumLookAngle);
            lookYawOffset = Mathf.Clamp(data.pose.lookYaw, -maximumUpperBodyYaw, maximumUpperBodyYaw);
            airborneRotationSlowed = false;
            airborneTurnBlend = 0f;
            airborneLookYawOffset = 0f;
            smoothedLookDelta = Vector2.zero;
            UpdateViewRotation();
            planarVelocity = Vector3.zero;
            verticalVelocity = 0f;
            movementInput = Vector2.zero;
            jumpRequested = false;
            jumpHeld = false;
            crouchHeld = false;
            pickupCrouchForced = false;
            pickupCrouchDepth = 0f;
            slideInputLatched = false;
            IsCrouching = false;
            sliding = false;
            slideBraking = false;
            slideMoveDirection = Vector3.zero;
            SlideSteer = 0f;
            return true;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public void Initialize(Transform cameraTransform)
        {
            if(playerBody==null) playerBody=GetComponent<Rigidbody>();
            view = cameraTransform;
            terrain = FindFirstObjectByType<ProceduralMap>();
            CapsuleCollider capsule = GetComponentInChildren<CapsuleCollider>();
            if (capsule != null)
            {
                bodyCollider = capsule;
                bodyCapsule = capsule.transform;
                bodyCapsuleRestRotation = bodyCapsule.localRotation;
                bodyCapsuleStandingHeight = capsule.height;
                bodyCapsuleStandingCenter = capsule.center;
            }
            spawnPosition = transform.position;
            pitch = 12f;
            lookYawOffset = 0f;
            airborneRotationSlowed = false;
            airborneTurnBlend = 0f;
            airborneLookYawOffset = 0f;
            smoothedLookDelta = Vector2.zero;
            UpdateViewRotation();
        }

        private void Awake()
        {
            playerBody = GetComponent<Rigidbody>();
            playerBody.mass = PlayerMassKg;
            for (int i = 0; i < Bindings.Length; i++)
            {
                int saved = PlayerPrefs.GetInt("Controls." + BindingNames[i], (int)DefaultKeys[i]);
                if (System.Enum.IsDefined(typeof(Key), saved) && saved != (int)Key.None && saved != (int)Key.Escape)
                    Bindings[i] = (Key)saved;
            }
            int savedAim=PlayerPrefs.GetInt("Controls.Aim",(int)Key.None);
            if(System.Enum.IsDefined(typeof(Key),savedAim)&&savedAim!=(int)Key.Escape)
                AimKey=(Key)savedAim;
            Sensitivity = PlayerPrefs.GetFloat("Controls.Sensitivity", mouseSensitivity);
        }

        private void Start()
        {
            // Bootstrap may have already opened the main menu before Start runs.
            if(!stationaryActor&&!aiControlled) SetCursorLocked(!paused);
        }

        private void Update()
        {
            LowerBodyTurnRate = 0f;
            if(stationaryActor||aiControlled) return;
            if (paused || Time.frameCount == inputBlockedFrame || Keyboard.current == null)
                return;

            HandleCursor();
            if(IsDead||GetComponent<PlayerDamage>() is { IsShocked:true }) return;
            if(Application.isFocused&&Keyboard.current.pKey.wasPressedThisFrame) WebWorldSession.SpawnCombatNpc(transform.position+transform.forward*4f);
            if(Application.isFocused&&Keyboard.current.kKey.wasPressedThisFrame)
                DestructionFragment.DeleteAllExceptHeld();
            bool outsideFullSpeedAirborneLook=airborneRotationSlowed&&
                Mathf.Abs(airborneLookYawOffset)>=FullSpeedAirborneLookRange;
            airborneTurnBlend=Mathf.Lerp(airborneTurnBlend,
                outsideFullSpeedAirborneLook?1f:0f,
                1f-Mathf.Exp(-AirborneTurnBlendRate*Time.deltaTime));
            HandleLook();
            CaptureMovementInput();
            KeepInsideMap();
            if (transform.position.y < ProceduralMap.FallLimit)
            {
                playerBody.position = spawnPosition;
                playerBody.linearVelocity = Vector3.zero;
                planarVelocity = Vector3.zero;
                verticalVelocity = 0f;
                sliding = false;
                slideBraking = false;
                slideInputLatched = false;
                IsCrouching = false;
                crouchHeld = false;
                airborneRotationSlowed = false;
                airborneTurnBlend = 0f;
                airborneLookYawOffset = 0f;
                slideMoveDirection = Vector3.zero;
                SlideSteer = 0f;
            }
        }

        private void FixedUpdate()
        {
            if (playerBody == null) return;
            if(paused)
            {
                // Shared rooms keep physics running while this player's menu
                // is open. Cancel locomotion but continue falling normally.
                if(!playerBody.isKinematic)
                {
                    verticalVelocity=Mathf.Min(playerBody.linearVelocity.y,0f)+gravity*Time.fixedDeltaTime;
                    playerBody.linearVelocity=Vector3.up*verticalVelocity;
                }
                return;
            }
            if(stationaryActor) {
                grounded=true;
                if(!playerBody.isKinematic) playerBody.AddForce(Vector3.up*gravity,ForceMode.Acceleration);
                return;
            }
            if(MovementDisabled) {
                movementInput=Vector2.zero;jumpRequested=false;jumpHeld=false;StopSliding();
                if(!playerBody.isKinematic) playerBody.AddForce(Vector3.up*gravity,ForceMode.Acceleration);
                return;
            }
            UpdateCapsuleDimensions();
            UpdateGrounded();
            UpdateSliding();
            UpdateSlidingCollider();

            if (sliding)
            {
                // Holding Jump while the feet are supported becomes a continuous
                // hand-and-foot brake; it never launches or ends a slide by time.
                slideBraking=jumpHeld&&grounded;
                Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, slideNormal);
                Vector3 downhillPlanar = Vector3.ProjectOnPlane(downhill, Vector3.up);
                SlideSteer = 0f;
                if(slideBraking)
                    planarVelocity=Vector3.MoveTowards(planarVelocity,Vector3.zero,
                        groundAcceleration*0.35f*Time.fixedDeltaTime);
                if (downhillPlanar.sqrMagnitude > 0.0001f)
                {
                    downhillPlanar.Normalize();
                    // Lock the trajectory when sliding begins. Mouse look and
                    // movement input cannot rotate the body or steer this vector.
                    if (slideMoveDirection.sqrMagnitude <= 0.0001f)
                        slideMoveDirection = downhillPlanar;
                    if (!slideBraking)
                    {
                        float slopeAngle = Vector3.Angle(Vector3.up, slideNormal);
                        float slopeAmount = Mathf.InverseLerp(15f, 75f, slopeAngle);
                        float forcedSpeed = Mathf.Lerp(walkSpeed, sprintSpeed * 1.35f, slopeAmount)
                            * InjuryMovementScale;
                        forcedSpeed = Mathf.Max(forcedSpeed,
                            Vector3.Dot(planarVelocity, slideMoveDirection));
                        Vector3 desiredSlide = slideMoveDirection * forcedSpeed;
                        planarVelocity = Vector3.MoveTowards(planarVelocity, desiredSlide,
                            groundAcceleration * 0.55f * Time.fixedDeltaTime);
                    }
                }

                if(planarVelocity.magnitude<1f)
                {
                    planarVelocity=Vector3.zero;
                    StopSliding();
                }

                verticalVelocity = Mathf.Min(playerBody.linearVelocity.y, -1.5f);
                verticalVelocity += gravity * Time.fixedDeltaTime;
                jumpRequested = false;
                playerBody.linearVelocity = planarVelocity + Vector3.up * verticalVelocity;
                return;
            }

            float targetSpeed = IsCrouching ? walkSpeed*0.5f:
                IsSprinting ? sprintSpeed : walkSpeed;
            targetSpeed*=InjuryMovementScale;
            if(grounded&&!IsCrouching&&movementInput.sqrMagnitude>0.001f)
            {
                if(movementAvatar==null) movementAvatar=GetComponent<RiggedPlayerAvatar>();
                if(movementAvatar!=null&&movementAvatar.BothLegsInMovementSlowRange)
                    targetSpeed*=0.5f;
            }
            float lateralSpeed=IsCrouching?targetSpeed*0.5f:targetSpeed;
            Vector3 desired=transform.forward*movementInput.y*targetSpeed+
                transform.right*movementInput.x*lateralSpeed;
            float acceleration = grounded ? groundAcceleration : airAcceleration;
            planarVelocity = Vector3.MoveTowards(planarVelocity, desired, acceleration * Time.fixedDeltaTime);

            verticalVelocity = playerBody.linearVelocity.y;
            if (grounded && verticalVelocity < 0f) verticalVelocity = -1f;
            if (jumpRequested && grounded)
            {
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                BeginAirborneLookTracking();
                grounded = false;
            }
            jumpRequested = false;
            verticalVelocity += gravity * Time.fixedDeltaTime;
            playerBody.linearVelocity = planarVelocity + Vector3.up * verticalVelocity;
        }

        private void UpdateSlidingCollider()
        {
            if (bodyCapsule == null) return;
            Quaternion target = bodyCapsuleRestRotation;
            if (sliding)
            {
                Vector3 worldAxis = Vector3.Cross(Vector3.up, slideNormal.normalized);
                if (worldAxis.sqrMagnitude > 0.0001f)
                {
                    Vector3 localAxis = transform.InverseTransformDirection(worldAxis.normalized);
                    float angle = Vector3.Angle(Vector3.up, slideNormal) * 1.4f;
                    target = Quaternion.AngleAxis(-70f, Vector3.right) *
                        Quaternion.AngleAxis(angle, localAxis) * bodyCapsuleRestRotation;
                }
            }
            float blend = 1f - Mathf.Exp(-10f * Time.fixedDeltaTime);
            bodyCapsule.localRotation = Quaternion.Slerp(bodyCapsule.localRotation, target, blend);
        }

        private void UpdateCapsuleDimensions()
        {
            if (bodyCollider != null)
            {
                float targetHeight = IsCrouching || sliding
                    ? bodyCapsuleStandingHeight *
                        (sliding?0.5f:Mathf.Lerp(0.5f,0.38f,PickupCrouchDepth))
                    : bodyCapsuleStandingHeight;
                bodyCollider.height = targetHeight;
                bodyCollider.center = new Vector3(bodyCapsuleStandingCenter.x,
                    bodyCapsuleStandingCenter.y * (IsCrouching || sliding ? 1.3f : 1f),
                    bodyCapsuleStandingCenter.z);
            }
        }

        private void UpdateSliding()
        {
            const float enterMinimumSlope = 20f;
            const float continueMinimumSlope = 15f;
            const float continueMaximumSlope = 78f;

            // Physics may redirect the player upward when the locked slide path
            // reaches rising terrain. Stop before applying another slide impulse.
            if(sliding&&playerBody.linearVelocity.y>0f)
            {
                StopSliding();
                return;
            }

            if (terrain == null)
                terrain = FindFirstObjectByType<ProceduralMap>();

            bool hasNearbySurface = false;
            RaycastHit hit = default;
            float slopeAngle = 0f;
            if (terrain != null && terrain.TryGetSurface(transform.position.x,
                transform.position.z, out hit))
            {
                float gap = transform.position.y - hit.point.y;
                hasNearbySurface = gap >= -0.15f && gap <= (sliding ? 1f : 0.75f);
                slopeAngle = Vector3.Angle(hit.normal, Vector3.up);
            }

            if (!sliding)
            {
                if (!slideInputLatched&&grounded&&IsSprinting&&crouchHeld&&
                    hasNearbySurface &&
                    slopeAngle >= enterMinimumSlope && slopeAngle <= continueMaximumSlope)
                {
                    sliding = true;
                    slideInputLatched=true;
                    slideNormal = hit.normal;
                }
                return;
            }

            bool validSlideSlope = hasNearbySurface &&
                slopeAngle >= continueMinimumSlope && slopeAngle <= continueMaximumSlope;
            if (validSlideSlope)
                slideNormal = hit.normal;
        }

        private void StopSliding()
        {
            sliding=false;
            slideBraking=false;
            slideMoveDirection=Vector3.zero;
            SlideSteer=0f;
        }

        private void HandleCursor()
        {
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                SetCursorLocked(true);
        }

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void HandleLook()
        {
            if (view == null || Mouse.current == null || Cursor.lockState != CursorLockMode.Locked)
            {
                smoothedLookDelta = Vector2.zero;
                RawLookDelta = Vector2.zero;
                RawLookSpeedDegreesPerSecond = 0f;
                strikeLookActiveLastFrame = false;
                strikeCameraSpeedBlend = 0f;
                strikeLookRecoveryRemaining = 0f;
                return;
            }

            RawLookDelta = Mouse.current.delta.ReadValue() * mouseSensitivity * lookSpeedScale;
            RawLookSpeedDegreesPerSecond = RawLookDelta.magnitude /
                Mathf.Max(Time.unscaledDeltaTime,0.001f);
            if(movementAvatar==null) movementAvatar=GetComponent<RiggedPlayerAvatar>();
            bool striking=movementAvatar!=null&&movementAvatar.IsStrikeMode;
            if(striking!=strikeLookActiveLastFrame)
            {
                if(striking) {
                    strikeCameraSpeedBlend=0f;
                    strikeLookRecoveryRemaining=0f;
                } else {
                    strikeLookRecoveryStartScale=Mathf.Lerp(StrikeLookSpeedScale,
                        StrikeFastLookSpeedScale,strikeCameraSpeedBlend);
                    strikeLookRecoveryRemaining=StrikeLookRecoverySeconds;
                }
                strikeLookActiveLastFrame=striking;
            }
            if(striking)
            {
                float fastAmount=Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(
                    StrikeCameraBoostStartDegreesPerSecond,StrikeFullSpeedDegreesPerSecond,
                    RawLookSpeedDegreesPerSecond));
                strikeCameraSpeedBlend=Mathf.Lerp(strikeCameraSpeedBlend,fastAmount,
                    1f-Mathf.Exp(-10f*Time.unscaledDeltaTime));
            }
            float strikeScale=Mathf.Lerp(StrikeLookSpeedScale,StrikeFastLookSpeedScale,
                strikeCameraSpeedBlend);
            float lookScale=1f;
            if(striking) lookScale=strikeScale;
            else if(strikeLookRecoveryRemaining>0f) {
                strikeLookRecoveryRemaining=Mathf.Max(0f,
                    strikeLookRecoveryRemaining-Time.unscaledDeltaTime);
                lookScale=Mathf.Lerp(strikeLookRecoveryStartScale,1f,
                    1f-strikeLookRecoveryRemaining/StrikeLookRecoverySeconds);
            }
            Vector2 targetDelta = RawLookDelta * lookScale;
            if(striking) targetDelta.y*=1.4f;
            float smoothing=striking?Mathf.Lerp(StrikeLookSmoothing,
                StrikeFastLookSmoothing,strikeCameraSpeedBlend):lookSmoothing;
            float lookBlend = 1f - Mathf.Exp(-smoothing *
                Time.unscaledDeltaTime);
            smoothedLookDelta = Vector2.Lerp(smoothedLookDelta, targetDelta, lookBlend);
            Vector2 lookDelta = smoothedLookDelta;
            float slowYaw=Mathf.Clamp(lookDelta.x*AirborneTurnSpeedScale,
                -MaximumAirborneTurnSpeed*Time.deltaTime,MaximumAirborneTurnSpeed*Time.deltaTime);
            lookDelta.x=Mathf.Lerp(lookDelta.x,slowYaw,airborneTurnBlend);
            if(airborneRotationSlowed)
                airborneLookYawOffset+=lookDelta.x;
            float minimumPitch=sliding?-60f:-70f;
            pitch = Mathf.Clamp(pitch - lookDelta.y, minimumPitch, maximumLookAngle);
            float allowedYaw = sliding ? 40f : maximumUpperBodyYaw;
            float requestedYaw = lookYawOffset + lookDelta.x;
            float bodyTurn = 0f;
            if (!sliding)
            {
                if (requestedYaw > allowedYaw)
                    bodyTurn = requestedYaw - allowedYaw;
                else if (requestedYaw < -allowedYaw)
                    bodyTurn = requestedYaw + allowedYaw;
                RotateLowerBody(bodyTurn);
                LowerBodyTurnRate += bodyTurn / Mathf.Max(Time.deltaTime, 0.0001f);
            }
            lookYawOffset = Mathf.Clamp(requestedYaw, -allowedYaw, allowedYaw);
            UpdateViewRotation();
        }

        private void UpdateViewRotation()
        {
            if (view != null)
                view.localRotation = Quaternion.Euler(pitch, lookYawOffset, 0f);
        }

        private void CaptureMovementInput()
        {
            Vector2 input = Vector2.zero;
            if (Keyboard.current[Bindings[0]].isPressed) input.y += 1f;
            if (Keyboard.current[Bindings[1]].isPressed) input.y -= 1f;
            if (Keyboard.current[Bindings[3]].isPressed) input.x += 1f;
            if (Keyboard.current[Bindings[2]].isPressed) input.x -= 1f;
            input = Vector2.ClampMagnitude(input, 1f);
            movementInput = input;

            // While moving, smoothly bring the rigid pelvis/lower body toward
            // the camera's horizontal aim. Reducing the local camera offset by
            // the same amount keeps the view direction continuous.
            if (!sliding&&input.sqrMagnitude > 0.001f &&
                Mathf.Abs(lookYawOffset) > 0.001f)
            {
                float turnScale=Mathf.Lerp(1f,AirborneTurnSpeedScale,airborneTurnBlend);
                float turn = lookYawOffset * (1f - Mathf.Exp(-3f * turnScale * Time.deltaTime));
                RotateLowerBody(turn);
                lookYawOffset -= turn;
                LowerBodyTurnRate += turn / Mathf.Max(Time.deltaTime, 0.0001f);
                UpdateViewRotation();
            }

            IsSprinting = !pickupCrouchForced&&!SingleLegSupport&&(Keyboard.current[Bindings[4]].isPressed ||
                (Bindings[4] == Key.LeftShift && Keyboard.current.rightShiftKey.isPressed))
                && input.y > 0f;
            bool defaultCrouchAlternate=Bindings[6]==Key.C&&Keyboard.current.leftCtrlKey.isPressed;
            crouchHeld=Keyboard.current[Bindings[6]].isPressed||defaultCrouchAlternate;
            IsCrouching=(crouchHeld||pickupCrouchForced||SingleLegSupport)&&!IsSprinting;
            if(!IsSprinting||!crouchHeld) slideInputLatched=false;
            jumpHeld=Keyboard.current[Bindings[5]].isPressed;
            if (Keyboard.current[Bindings[5]].wasPressedThisFrame)
                jumpRequested = true;
        }

        private void RotateLowerBody(float angle)
        {
            if (Mathf.Abs(angle) < 0.0001f) return;
            Quaternion turn = Quaternion.AngleAxis(angle, Vector3.up);
            if (playerBody != null)
                playerBody.rotation = turn * playerBody.rotation;
            else
                transform.rotation = turn * transform.rotation;
        }

        private void UpdateGrounded()
        {
            // Ground/air detection probes are 50% larger for more reliable
            // support detection across small gaps and uneven terrain.
            const float radius = 0.1275f;
            const float supportRadius = 0.165f;
            float distance = 0.28f;
            Vector3 detectionPoint=transform.position;
            float originLift=0.15f;
            if(IsCrouching&&bodyCollider!=null)
            {
                Vector3 capsuleAxis=bodyCollider.direction switch
                {
                    0=>Vector3.right,
                    2=>Vector3.forward,
                    _=>Vector3.up
                };
                Vector3 localBottom=bodyCollider.center-
                    capsuleAxis*bodyCollider.height*0.5f;
                detectionPoint=bodyCollider.transform.TransformPoint(localBottom);
                originLift=radius;
                // The crouched capsule bottom is intentionally raised. Extend
                // the cast by that height so its probe still reaches the feet.
                distance+=Mathf.Max(0f,detectionPoint.y-transform.position.y);
            }
            Vector3 baseOrigin = detectionPoint + Vector3.up * originLift;
            int mask = ~(1 << 2); // Foot colliders use Ignore Raycast.
            Vector3 leftOrigin=baseOrigin-transform.right*0.105f;
            Vector3 rightOrigin=baseOrigin+transform.right*0.105f;
            bool leftFootGrounded=Physics.SphereCast(leftOrigin,radius,Vector3.down,out _,distance,
                mask,QueryTriggerInteraction.Ignore)||Physics.CheckSphere(
                detectionPoint-transform.right*0.105f+Vector3.up*0.025f,supportRadius,mask,
                QueryTriggerInteraction.Ignore);
            bool rightFootGrounded=Physics.SphereCast(rightOrigin,radius,Vector3.down,out _,distance,
                mask,QueryTriggerInteraction.Ignore)||Physics.CheckSphere(
                detectionPoint+transform.right*0.105f+Vector3.up*0.025f,supportRadius,mask,
                QueryTriggerInteraction.Ignore);
            bool centreSupport=Physics.SphereCast(baseOrigin,supportRadius,Vector3.down,out _,distance,mask,
                QueryTriggerInteraction.Ignore);
            grounded=leftFootGrounded||rightFootGrounded||centreSupport;
            if(!grounded&&IsCrouching)
            {
                if(terrain==null) terrain=FindFirstObjectByType<ProceduralMap>();
                if(terrain!=null&&terrain.TryGetSurface(detectionPoint.x,detectionPoint.z,
                    out RaycastHit surface))
                {
                    float surfaceGap=detectionPoint.y-surface.point.y;
                    grounded=surface.normal.y>0.15f&&surfaceGap>=-0.1f&&
                        surfaceGap<=distance+radius+0.05f;
                }
            }
            if(!grounded)
                BeginAirborneLookTracking();
            else if(playerBody==null||playerBody.linearVelocity.y<=0.05f)
            {
                airborneRotationSlowed=false;
                airborneLookYawOffset=0f;
            }
        }

        private void BeginAirborneLookTracking()
        {
            if(airborneRotationSlowed) return;
            airborneRotationSlowed=true;
            airborneLookYawOffset=0f;
        }

        private void KeepInsideMap()
        {
            Vector3 position = transform.position;
            const float margin = 1f;
            Rect bounds = terrain != null ? terrain.Bounds : WorldConfig.MapBounds;
            float x = Mathf.Clamp(position.x, bounds.xMin + margin, bounds.xMax - margin);
            float z = Mathf.Clamp(position.z, bounds.yMin + margin, bounds.yMax - margin);

            if (!Mathf.Approximately(x, position.x) || !Mathf.Approximately(z, position.z))
            {
                playerBody.position = new Vector3(x, position.y, z);
                playerBody.linearVelocity = Vector3.zero;
                planarVelocity = Vector3.zero;
                sliding = false;
                slideBraking = false;
                slideMoveDirection = Vector3.zero;
            }
        }
    }

    public enum PickableObjectLabel { Regular, Irregular }
    public enum PickableGrabMode { Regular, Stick, Rope }

    /// <summary>Marks a world object as collectible by the player's right hand.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class PickableObject : MonoBehaviour
    {
        public const float OneHandMassLimitKg=26f;
        public const float TwoHandMassLimitKg=90f;
        public const float MaximumDragMassKg=200f;
        public const float DragSpeedPerHand=0.3f;
        public static readonly System.Collections.Generic.HashSet<PickableObject> ActiveObjects=new();
        internal static readonly System.Collections.Generic.List<PickableObject> HeldObjects=new();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetActiveObjects()
        { ActiveObjects.Clear(); HeldObjects.Clear(); PickupSpatialIndex.Clear(); }
        private Renderer[] pickupRenderers;
        private void OnEnable()
        {
            ActiveObjects.Add(this);
            PickupSpatialIndex.Register(this);
            pickupRenderers=null;
        }
        private void OnDisable()
        { ActiveObjects.Remove(this); HeldObjects.Remove(this); PickupSpatialIndex.Remove(this); }
        public void TrackMovement()
        {
            if(!isActiveAndEnabled) return;
            PickupSpatialIndex.TrackMovement(this);PlayerWorldLoading.TrackMovement(gameObject);
        }
        private const float HeldDirectionAlignmentRate = 2.16f;
        private const float StrikeImpactMinimumSpeed = 3f;
        private const float StrikeImpactProtectionSeconds = 0.45f;
        private const float StickAxisRatio = 2.5f;
        private const float PickupTriggerWorldRadius = 0.11f;
        [SerializeField] private string displayName="Object";
        [SerializeField] private PickableObjectLabel label=PickableObjectLabel.Regular;
        [SerializeField,HideInInspector] private CapsuleCollider detectionTrigger;
        public string DisplayName { get => displayName; set => displayName=value; }
        public PickableObjectLabel Label { get => label; set => label=value; }
        public bool IsHeld { get; private set; }
        public Transform HeldHand => heldHand;
        public bool HasBeenPickedUp { get; private set; }
        public Rigidbody LaunchGoldenStoneCopy(Camera camera,Transform playerRoot,bool replicated=false)
        {
            if((!IsHeld&&!replicated)||displayName!="Golden Stone"||camera==null) return null;
            if(!replicated&&WebWorldSession.ForwardGolden(this,camera)) return null;
            Vector3 origin=transform.position;
            var ray=new Ray(camera.transform.position,camera.transform.forward);
            Vector3 target=ray.GetPoint(1000f);
            float nearest=1000f;
            foreach(var hit in Physics.RaycastAll(ray,1000f,~0,QueryTriggerInteraction.Ignore))
            {
                if(hit.transform.IsChildOf(transform)||
                    playerRoot!=null&&hit.transform.IsChildOf(playerRoot)) continue;
                if(hit.distance<nearest) { nearest=hit.distance; target=hit.point; }
            }
            var copy=Instantiate(gameObject,origin,transform.rotation);
            copy.name="Golden Stone";
            copy.transform.SetParent(null,true);
            copy.transform.localScale=transform.lossyScale;
            var pickup=copy.GetComponent<PickableObject>();
            pickup.IsHeld=false;
            pickup.HasBeenPickedUp=false;
            pickup.heldHand=null;
            pickup.heldMotionSampleValid=false;
            pickup.detectionTrigger.enabled=false;
            var meshOwner=copy.GetComponent<GoldenStoneCopyMeshes>();
            if(meshOwner==null) meshOwner=copy.AddComponent<GoldenStoneCopyMeshes>();
            meshOwner.CopyMeshes();
            var body=copy.GetComponent<Rigidbody>();
            if(body==null) body=copy.AddComponent<Rigidbody>();
            body.isKinematic=false;
            body.useGravity=true;
            body.detectCollisions=true;
            body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            copy.GetComponent<PhysicalDestructible>()?.RefreshMass();
            Vector3 direction=target-origin;
            body.linearVelocity=(direction.sqrMagnitude>0.000001f?direction.normalized:ray.direction)*50f;
            body.angularVelocity=Vector3.zero;
            pickup.TrackMovement();
            WebWorldSession.RegisterSpawn(copy);
            var originalColliders=GetComponentsInChildren<Collider>();
            var playerColliders=playerRoot!=null?playerRoot.GetComponentsInChildren<Collider>():System.Array.Empty<Collider>();
            foreach(var collider in copy.GetComponentsInChildren<Collider>())
            {
                if(!collider.enabled||collider.isTrigger) continue;
                foreach(var original in originalColliders)
                    Physics.IgnoreCollision(collider,original);
                foreach(var playerCollider in playerColliders)
                    Physics.IgnoreCollision(collider,playerCollider);
            }
            return body;
        }
        private RopeObject ropeObject;
        private PickableGrabMode grabMode=PickableGrabMode.Regular;
        internal CombatNpc NpcOwner;
        public bool ForceRegularGrip;
        internal bool IsMineralGrip {
            get {
                string material=GetComponent<PhysicalDestructible>()?.MaterialName;
                return material!=null&&(System.Array.IndexOf(WorldConfig.MineralMaterials,material)>=0||
                    material.IndexOf("Stone",System.StringComparison.OrdinalIgnoreCase)>=0);
            }
        }
        public PlayerCorpse Corpse => GetComponent<PlayerCorpse>();
        public RopeObject Rope => ropeObject!=null?ropeObject:ropeObject=GetComponent<RopeObject>();
        public PickableGrabMode GrabMode
        { get => Corpse!=null?(Corpse.IsLimb?PickableGrabMode.Stick:PickableGrabMode.Regular):ForceRegularGrip?PickableGrabMode.Regular:Rope!=null?PickableGrabMode.Rope:grabMode; private set => grabMode=value; }
        public Vector3 StickForwardPoint => Corpse!=null?Corpse.LimbEnd: transform.TransformPoint(stickForwardPoint);
        public Vector3 HeldGripPoint => Corpse!=null?Corpse.GripPoint:Rope!=null?Rope.GripPoint:transform.TransformPoint(heldMeshContact);
        public Vector3 StickDirection => Corpse!=null?(Corpse.LimbEnd-Corpse.GripPoint).normalized:Rope!=null?Rope.GripDirection:(StickForwardPoint-HeldGripPoint).normalized;
        public Vector3 Size => measuredSize.sqrMagnitude>0f?measuredSize:MeasureSize();
        private Collider[] heldColliders;
        private bool[] colliderEnabledStates;
        private Vector3 previousHeldPosition;
        private Quaternion previousHeldRotation;
        private bool heldMotionSampleValid;
        private int strikeConstraintFrame=-1;
        private Vector3 heldLinearVelocity;
        private Vector3 heldAngularVelocity;
        private float strikeImpactProtectedUntil;
        private Vector3 previousStrikeProbePosition;
        internal const float MaximumWaveSpeed=20f;
        private Collider[] strikeObstacles;
        private float[] strikeStartDepth=System.Array.Empty<float>();
        public Vector3 HeldPointVelocity(Vector3 worldPoint)
        {
            if(!IsHeld) return Vector3.zero;
            Vector3 velocity=heldLinearVelocity+Vector3.Cross(heldAngularVelocity,worldPoint-transform.position);
            return heldHand!=null&&heldHand.GetComponentInParent<RiggedPlayerAvatar>() is {} avatar&&
                avatar.IsStrikingHeldItem(this)?Vector3.ClampMagnitude(velocity,MaximumWaveSpeed):velocity;
        }
        public Vector3 HeldAngularVelocity => IsHeld?heldAngularVelocity:Vector3.zero;
        public float HeldSpeed => IsHeld?
            (Rope!=null?Rope.GripVelocity:HeldPointVelocity(StrikeProbePosition())).magnitude:0f;
        internal bool IsStrikeImpactProtected => IsHeld&&
            (Time.time<strikeImpactProtectedUntil||heldHand!=null&&
             heldHand.GetComponentInParent<RiggedPlayerAvatar>() is
                 { } avatar&&avatar.IsStrikingHeldItem(this));

        internal bool TryResolveStrikeImpact(Collider targetCollider,Vector3 point,
            Vector3 otherVelocity,Vector3? sampledVelocity=null)
        {
            if(!IsHeld||heldHand==null||!IsStrikeTarget(targetCollider)) return false;
            RiggedPlayerAvatar avatar=heldHand.GetComponentInParent<RiggedPlayerAvatar>();
            if(avatar==null||!avatar.IsStrikingHeldItem(this))
                return IsStrikeImpactProtected;
            Vector3 incoming=(sampledVelocity??HeldPointVelocity(point))-otherVelocity;
            PhysicalDestructible heldProperties=GetComponent<PhysicalDestructible>();
            PhysicalDestructible target=PhysicalDestructible.Resolve(targetCollider);
            var corpse=targetCollider.GetComponentInParent<PlayerCorpse>();
            if(corpse!=null&&corpse.TryShatterFromImpact(heldProperties,point,incoming))
            { avatar.MarkStrikeDestruction();return true; }
            CollisionSparks.TryEmit(heldProperties,target,point,-incoming,incoming.magnitude);
            Collider impactShape=ImpactSourceCollider(point);
            if(impactShape!=null&&
                (targetCollider.GetComponent<PlayerHitShape>()!=null||
                 targetCollider.GetComponentInParent<RemotePlayerDamage>()!=null))
                MovingImpactMotion.HitActor(targetCollider,impactShape,point,
                    incoming.normalized,sampledVelocity??HeldPointVelocity(point));
            if(heldProperties!=null&&heldProperties.TryHeldStrikeDestruction(
                target,point,incoming))
            {
                avatar.MarkStrikeDestruction();
                return true;
            }
            if(avatar.StrikeDestroyedSomething) return true;
            if(target!=null&&target.IsTerrain)
            {
                avatar.EndHeldStrikeAtTerrain(this);
                return true;
            }
            if(target!=null&&heldProperties!=null&&
                (target.Hardness>heldProperties.Hardness&&incoming.magnitude>RiggedPlayerAvatar.MinimumReboundSpeed||
                 heldProperties.CanDestroySimilarHeldStrike(target,incoming.magnitude)))
            {
                strikeImpactProtectedUntil=Time.time+StrikeImpactProtectionSeconds;
                Vector3 reboundDirection=incoming.sqrMagnitude>0.000001f?
                    incoming:point-heldHand.position;
                if(reboundDirection.sqrMagnitude<0.000001f)
                    reboundDirection=heldHand.forward;
                avatar.ReboundHeldStrike(this,reboundDirection,incoming.magnitude);
            }
            return true;
        }
        private Collider ImpactSourceCollider(Vector3 point)
        {
            Collider nearest=null;
            float distance=float.PositiveInfinity;
            if(heldColliders!=null) foreach(var shape in heldColliders)
            {
                if(shape==null||!shape.enabled||shape.isTrigger) continue;
                float candidate=(shape.ClosestPoint(point)-point).sqrMagnitude;
                if(candidate>=distance) continue;
                distance=candidate;nearest=shape;
            }
            return nearest;
        }
        private Vector3 measuredSize;
        private bool meshCollidersPrepared;
        private Transform heldHand;
        private RigidbodyInterpolation unheldInterpolation;
        private Vector3 heldMeshContact;
        private Vector3 stickForwardAxis;
        private Vector3 stickUpAxis;
        private Vector3 stickForwardPoint;
        private Transform heldForwardReference;
        private Quaternion strikeGripLocalRotation;
        private Vector3 strikeGripLocalPosition;

        internal void CaptureStrikeGrip()
        {
            strikeGripLocalRotation=transform.localRotation;
            strikeGripLocalPosition=transform.localPosition;
        }

        private void Awake()
        {
            EnsureDetectionTrigger();
            detectionTrigger.enabled=false;
        }

        internal void UpdateHeldPose()
        {
            if(NpcOwner!=null&&!WebWorldSession.IsPhysicsHost) return;
            if(!IsHeld||heldHand==null) return;
            if(Rope!=null||Corpse!=null) return; // Only the grip is constrained; every link remains dynamic.
            RiggedPlayerAvatar avatar=heldHand.GetComponentInParent<RiggedPlayerAvatar>();
            bool striking=avatar!=null&&avatar.IsStrikingHeldItem(this);
            if(avatar!=null&&avatar.IsHoldingStrikePose(this))
            {
                // The arm carries the object; its grip must not settle or
                // reorient during attack, rebound, or return.
                transform.localRotation=strikeGripLocalRotation;
                if(GrabMode!=PickableGrabMode.Stick&&label==PickableObjectLabel.Irregular)
                    transform.localPosition=strikeGripLocalPosition;
                else AlignHeldContact();
                if(striking) { ConstrainSlowStrike(Time.deltaTime);strikeConstraintFrame=Time.frameCount; }
                DetectStrikeContact(striking);
                return;
            }
            if(GrabMode==PickableGrabMode.Stick)
            {
                // The stick is parented to the hand. During the whole strike
                // animation, let that hand rotation carry its held grip pose.
                if(avatar==null||!avatar.IsHoldingStrikePose(this))
                    UpdateStickHeldTransform(GripFollowBlend(Time.deltaTime));
                DetectStrikeContact(striking);
                return;
            }
            if(striking)
            {
                // A kinematic stone needs its grip refreshed after its holder's
                // Rigidbody moves, just as it does outside strike mode.
                AlignHeldContact();
                DetectStrikeContact(true);
                return;
            }
            if(label==PickableObjectLabel.Irregular)
            {
                UpdateIrregularHeldTransform();
                DetectStrikeContact(false);
                return;
            }
            transform.localRotation=Quaternion.Slerp(transform.localRotation,
                Quaternion.identity,GripFollowBlend(Time.deltaTime));
            AlignHeldContact();
            DetectStrikeContact(false);
        }

        private void DetectStrikeContact(bool striking)
        {
            Vector3 probe=StrikeProbePosition();
            Vector3 previousProbe=previousStrikeProbePosition;
            Vector3 travel=probe-previousProbe;
            previousStrikeProbePosition=probe;
            float distance=travel.magnitude;
            if(!striking||distance<StrikeImpactMinimumSpeed*
                Mathf.Max(Time.deltaTime,0.0001f)) return;

            float radius=Mathf.Clamp(Mathf.Min(measuredSize.x,
                Mathf.Min(measuredSize.y,measuredSize.z))*0.25f,0.015f,0.12f);
            Vector3 probeVelocity=Vector3.ClampMagnitude(
                travel/Mathf.Max(Time.deltaTime,0.0001f),MaximumWaveSpeed);
            bool foundSweepHit=false;
            RaycastHit nearestHit=default;
            foreach(RaycastHit hit in Physics.SphereCastAll(previousProbe,
                radius,travel/distance,distance,~0,QueryTriggerInteraction.Ignore))
            {
                if(!IsStrikeTarget(hit.collider)||
                    foundSweepHit&&hit.distance>=nearestHit.distance) continue;
                nearestHit=hit;
                foundSweepHit=true;
            }
            if(foundSweepHit)
            {
                Rigidbody body=nearestHit.collider.attachedRigidbody;
                TryResolveStrikeImpact(nearestHit.collider,nearestHit.point,
                    body!=null?body.GetPointVelocity(nearestHit.point):Vector3.zero,
                    probeVelocity);
                return;
            }

            // A rotating part of the object can overlap a surface even when
            // its forward probe did not cross that surface.
            foreach(Collider heldCollider in heldColliders)
            {
                if(heldCollider==null||!heldCollider.enabled) continue;
                Bounds bounds=heldCollider.bounds;
                foreach(Collider other in Physics.OverlapBox(bounds.center,bounds.extents,
                    Quaternion.identity,~0,QueryTriggerInteraction.Ignore))
                {
                    if(!IsStrikeTarget(other)||!Physics.ComputePenetration(
                        heldCollider,heldCollider.transform.position,heldCollider.transform.rotation,
                        other,other.transform.position,other.transform.rotation,
                        out _,out _)) continue;
                    Vector3 point=other.ClosestPoint(bounds.center);
                    Rigidbody body=other.attachedRigidbody;
                    TryResolveStrikeImpact(other,point,
                        body!=null?body.GetPointVelocity(point):Vector3.zero,probeVelocity);
                    return;
                }
            }
        }


        private bool IsStrikeTarget(Collider candidate) => candidate!=null&&
            candidate.enabled&&!candidate.isTrigger&&
            !candidate.transform.IsChildOf(transform)&&
            (candidate.GetComponentInParent<FirstPersonController>()==null||candidate.GetComponentInParent<FirstPersonController>()!=heldHand?.GetComponentInParent<FirstPersonController>());

        private Vector3 StrikeProbePosition()
        {
            if(GrabMode==PickableGrabMode.Stick) return StickForwardPoint;
            if(heldColliders!=null)
                foreach(Collider collider in heldColliders)
                    if(collider!=null&&collider.enabled&&!collider.isTrigger)
                        return collider.bounds.center;
            return transform.position;
        }

        internal void UpdateHeldMotion()
        {
            SampleHeldMotion(Time.fixedDeltaTime);
        }

        internal void SampleHeldMotion(float step)
        {
            if(!IsHeld||heldHand==null||step<=0f) return;
            if(Rope!=null||Corpse!=null) return;
            ConstrainSlowStrike(step,strikeConstraintFrame!=Time.frameCount);
            Vector3 position=transform.position;
            Quaternion rotation=transform.rotation;
            if(heldMotionSampleValid)
            {
                float inverseStep=1f/Mathf.Max(step,0.0001f);
                heldLinearVelocity=(position-previousHeldPosition)*inverseStep;
                (rotation*Quaternion.Inverse(previousHeldRotation)).ToAngleAxis(
                    out float angle,out Vector3 axis);
                if(angle>180f) angle-=360f;
                heldAngularVelocity=axis.sqrMagnitude>0.0001f&&float.IsFinite(angle)?
                    axis*angle*Mathf.Deg2Rad*inverseStep:Vector3.zero;
            }
            previousHeldPosition=position;
            previousHeldRotation=rotation;
            heldMotionSampleValid=true;
        }

        private void ConstrainSlowStrike(float step,bool soften=true)
        {
            var avatar=heldHand.GetComponentInParent<RiggedPlayerAvatar>();
            if(!heldMotionSampleValid||heldColliders==null||avatar==null||
                !avatar.IsStrikingHeldItem(this)) return;
            Vector3 desiredPosition=transform.position;
            Quaternion desiredRotation=transform.rotation;
            float travel=Vector3.Distance(previousHeldPosition,desiredPosition);
            float angle=Quaternion.Angle(previousHeldRotation,desiredRotation)*Mathf.Deg2Rad;
            if(travel<.000001f&&angle<.000001f) return;
            strikeObstacles ??= new Collider[64];
            Physics.SyncTransforms();
            float radius=.01f;
            foreach(var shape in heldColliders) if(shape!=null&&shape.enabled)
                radius=Mathf.Max(radius,(shape.bounds.center-desiredPosition).magnitude+shape.bounds.extents.magnitude);
            Vector3 center=(previousHeldPosition+desiredPosition)*.5f;
            int count;
            while(true) {
                count=Physics.OverlapSphereNonAlloc(center,radius+travel*.5f+.005f,
                    strikeObstacles,~0,QueryTriggerInteraction.Ignore);
                if(count<strikeObstacles.Length) break;
                strikeObstacles=new Collider[strikeObstacles.Length*2];
            }
            var properties=GetComponent<PhysicalDestructible>();
            Quaternion inverseDesired=Quaternion.Inverse(desiredRotation);
            int slowCount=0;
            for(int i=0;i<count;i++) {
                var other=strikeObstacles[i];if(!IsStrikeTarget(other)) continue;
                var target=PhysicalDestructible.Resolve(other);
                float limit=StrikeImpactMinimumSpeed;
                if(properties!=null&&target!=null)
                    limit=Mathf.Max(limit,Mathf.Min(properties.InitialImpactSpeed(target.Hardness,target.MaterialName),
                        target.InitialImpactSpeed(properties.Hardness,properties.MaterialName)));
                Vector3 point=other.bounds.ClosestPoint(desiredPosition);
                Vector3 priorPoint=previousHeldPosition+previousHeldRotation*(inverseDesired*(point-desiredPosition));
                Vector3 otherVelocity=Vector3.zero;
                var otherPickup=other.GetComponentInParent<PickableObject>();
                var body=other.attachedRigidbody;
                if(otherPickup!=null&&otherPickup.IsHeld) otherVelocity=otherPickup.HeldPointVelocity(point);
                else if(body!=null) {
                    if(body.isKinematic&&WebWorldSession.ReplicaMotion(body.gameObject,out var v,out var w))
                        otherVelocity=v+Vector3.Cross(w,point-body.position);
                    else otherVelocity=body.GetPointVelocity(point);
                }
                bool actor=other.GetComponent<PlayerHitShape>()!=null||other.GetComponentInParent<RemotePlayerDamage>()!=null||other.GetComponentInParent<PlayerCorpse>()!=null;
                if(actor) continue;
                bool solid=(target==null||target.IsTerrain||properties==null||
                    float.IsPositiveInfinity(target.RequiredImpactSpeed(properties.Hardness,properties.Resilience,properties.MaterialName)));
                if(!solid&&((point-priorPoint)/Mathf.Max(step,.0001f)-otherVelocity).magnitude>limit) continue;
                strikeObstacles[slowCount++]=other;
            }
            if(slowCount==0) return;
            int pairs=heldColliders.Length*slowCount;
            if(strikeStartDepth.Length<pairs) strikeStartDepth=new float[pairs];
            for(int s=0;s<heldColliders.Length;s++) for(int o=0;o<slowCount;o++)
                strikeStartDepth[s*slowCount+o]=StrikePenetration(heldColliders[s],strikeObstacles[o],
                    previousHeldPosition,previousHeldRotation,desiredPosition,inverseDesired);
            bool Deeper(float fraction) {
                Vector3 position=Vector3.LerpUnclamped(previousHeldPosition,desiredPosition,fraction);
                Quaternion rotation=Quaternion.SlerpUnclamped(previousHeldRotation,desiredRotation,fraction);
                for(int s=0;s<heldColliders.Length;s++) for(int o=0;o<slowCount;o++)
                    if(StrikePenetration(heldColliders[s],strikeObstacles[o],position,rotation,desiredPosition,inverseDesired)>
                        Mathf.Max(strikeStartDepth[s*slowCount+o],.0002f)) return true;
                return false;
            }
            // Check the entire rotation/translation path, including a thin
            // obstacle that lies between two non-overlapping end poses.
            float pathLength=Mathf.Max(.000001f,travel+angle*radius);
            float lookahead=1f+.05f/pathLength;
            int steps=Mathf.Clamp(Mathf.CeilToInt((pathLength+.05f)/.005f),1,256);
            float accepted=0;
            for(int i=1;i<=steps;i++) {
                float next=(float)i/steps*lookahead;
                if(!Deeper(next)) { accepted=next;continue; }
                float blocked=next;
                for(int pass=0;pass<12;pass++) {
                    float middle=(accepted+blocked)*.5f;
                    if(Deeper(middle)) blocked=middle;else accepted=middle;
                }
                // Ease toward the safe surface over a short braking distance.
                // The accepted pose remains outside the collider throughout.
                accepted=Mathf.Min(1f,accepted)*(soften?1f-Mathf.Exp(-accepted*pathLength/.035f):1f);
                avatar.ConstrainHeldStrikePose(this,Vector3.Lerp(previousHeldPosition,desiredPosition,accepted),
                    Quaternion.Slerp(previousHeldRotation,desiredRotation,accepted),accepted);
                previousStrikeProbePosition=StrikeProbePosition();
                return;
            }
        }

        private static float StrikePenetration(Collider shape,Collider other,Vector3 position,Quaternion rotation,
            Vector3 desiredPosition,Quaternion inverseDesired)
        {
            if(shape==null||!shape.enabled||other==null||!other.enabled) return 0;
            Vector3 shapePosition=position+rotation*(inverseDesired*(shape.transform.position-desiredPosition));
            Quaternion shapeRotation=rotation*inverseDesired*shape.transform.rotation;
            return Physics.ComputePenetration(shape,shapePosition,shapeRotation,other,
                other.transform.position,other.transform.rotation,out _,out float depth)?depth:0;
        }

        public Vector3 AimPoint
        {
            get
            {
                if(Corpse!=null) return Corpse.NearestPoint(transform.position);
                if(Rope!=null) return GetComponent<Renderer>().bounds.center;
                if(detectionTrigger!=null)
                    return transform.TransformPoint(detectionTrigger.center);
                Renderer renderer=GetComponentInChildren<Renderer>();
                return renderer!=null?renderer.bounds.center:transform.position;
            }
        }

        public Vector3 PickupReachPoint(Vector3 handPosition)
        {
            if(Corpse!=null) return Corpse.NearestPoint(handPosition);
            if(Rope!=null) return Rope.NearestPoint(handPosition);
            if(ChooseGrabMode(Size,GetComponent<PhysicalDestructible>()?.MaterialName)!=
                PickableGrabMode.Stick) return AimPoint;
            Vector3 closest=AimPoint;
            float bestDistance=float.PositiveInfinity;
            foreach(MeshFilter filter in GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh=filter.sharedMesh;
                if(mesh==null||!mesh.isReadable||
                    filter.GetComponentInParent<PickableObject>()!=this) continue;
                foreach(Vector3 vertex in mesh.vertices)
                {
                    Vector3 point=filter.transform.TransformPoint(vertex);
                    float distance=(point-handPosition).sqrMagnitude;
                    if(distance>=bestDistance) continue;
                    bestDistance=distance;
                    closest=point;
                }
            }
            return closest;
        }

        public Vector3 PickupFaceGripPoint(RaycastHit hit)
        {
            Vector3 closest=hit.point;
            Vector3 vertex=hit.point;
            float faceDistance=float.PositiveInfinity;
            float vertexDistance=float.PositiveInfinity;
            foreach(MeshFilter filter in GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh=filter.sharedMesh;
                if(mesh==null||!mesh.isReadable||
                    filter.GetComponentInParent<PickableObject>()!=this) continue;
                Vector3[] vertices=mesh.vertices;
                int[] triangles=mesh.triangles;
                for(int i=0;i+2<triangles.Length;i+=3)
                {
                    Vector3 a=filter.transform.TransformPoint(vertices[triangles[i]]);
                    Vector3 b=filter.transform.TransformPoint(vertices[triangles[i+1]]);
                    Vector3 c=filter.transform.TransformPoint(vertices[triangles[i+2]]);
                    Vector3 point=ClosestTrianglePoint(hit.point,a,b,c);
                    float distance=(point-hit.point).sqrMagnitude;
                    if(distance>=faceDistance) continue;
                    faceDistance=distance;
                    closest=point;
                    vertex=a;
                    vertexDistance=(a-hit.point).sqrMagnitude;
                    float candidate=(b-hit.point).sqrMagnitude;
                    if(candidate<vertexDistance)
                    {
                        vertexDistance=candidate;
                        vertex=b;
                    }
                    candidate=(c-hit.point).sqrMagnitude;
                    if(candidate<vertexDistance)
                    {
                        vertexDistance=candidate;
                        vertex=c;
                    }
                }
            }
            // Long, low-poly faces have vertices only at their ends. In that
            // case the aimed point on the face is the usable grip contact.
            return vertexDistance<=PickupTriggerWorldRadius*PickupTriggerWorldRadius?
                vertex:closest;
        }

        public bool IsNearPickupRange(Vector3 position,float range)
        {
            float squared=range*range;
            pickupRenderers ??= GetComponentsInChildren<Renderer>();
            foreach(Renderer renderer in pickupRenderers)
                if(renderer!=null&&renderer.GetComponentInParent<PickableObject>()==this&&
                    (renderer.bounds.ClosestPoint(position)-position).sqrMagnitude<=squared)
                    return true;
            return (AimPoint-position).sqrMagnitude<=squared;
        }

        public Vector3 NearestGripVertex(Vector3 handPosition,Vector3 otherHandPosition)
        {
            if(Corpse!=null) return Corpse.NearestPoint(handPosition);
            Vector3 closest=AimPoint;
            float bestDistance=float.PositiveInfinity;
            Vector3 nearest=closest;
            float nearestDistance=float.PositiveInfinity;
            // Keep the two hands on distinct parts of the mesh when possible.
            foreach(MeshFilter filter in GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh=filter.sharedMesh;
                if(mesh==null||!mesh.isReadable) continue;
                foreach(Vector3 vertex in mesh.vertices)
                {
                    Vector3 point=filter.transform.TransformPoint(vertex);
                    float distance=(point-handPosition).sqrMagnitude;
                    if(distance<nearestDistance)
                    {
                        nearestDistance=distance;
                        nearest=point;
                    }
                    if((point-otherHandPosition).sqrMagnitude<0.0025f) continue;
                    if(distance>=bestDistance) continue;
                    bestDistance=distance;
                    closest=point;
                }
            }
            return float.IsPositiveInfinity(bestDistance)?nearest:closest;
        }

        public bool IsPickupDetectionCollider(Collider collider)
        {
            if(collider==null||!collider.enabled||!collider.gameObject.activeInHierarchy||
                collider.GetComponentInParent<PickableObject>()!=this) return false;
            if(Rope!=null) return !collider.isTrigger&&collider.GetComponentInParent<RopeObject>()==Rope;
            if(collider==detectionTrigger) return detectionTrigger.isTrigger;
            return !collider.isTrigger;
        }

        public bool PickupDetectionTouchesSphere(Vector3 worldCentre,float worldRadius)
        {
            float radiusSquared=worldRadius*worldRadius;
            if(Rope!=null) return (Rope.NearestPoint(worldCentre)-worldCentre).sqrMagnitude<=radiusSquared;
            if(detectionTrigger!=null&&detectionTrigger.enabled&&detectionTrigger.isTrigger)
            {
                Vector3 closest=detectionTrigger.ClosestPoint(worldCentre);
                if((closest-worldCentre).sqrMagnitude<=radiusSquared) return true;
            }
            foreach(Collider collider in GetComponentsInChildren<Collider>())
            {
                if(!IsPickupDetectionCollider(collider)||collider.isTrigger) continue;
                Vector3 closest=collider is MeshCollider meshCollider&&!meshCollider.convex?
                    collider.bounds.ClosestPoint(worldCentre):collider.ClosestPoint(worldCentre);
                if((closest-worldCentre).sqrMagnitude<=radiusSquared) return true;
            }
            return false;
        }

        public bool CanPickUpWithOneHand
        {
            get
            {
                float mass=PickupMassKg;
                return float.IsFinite(mass)&&mass<=OneHandMassLimitKg;
            }
        }

        public bool RequiresTwoHands => PickupMassKg>OneHandMassLimitKg;
        public bool RequiresDragging => PickupMassKg>TwoHandMassLimitKg;

        public static float DragSpeed(float mass,int contactingHands)
        {
            if(!float.IsFinite(mass)||mass<=TwoHandMassLimitKg) return 0f;
            return Mathf.Clamp(contactingHands,0,2)*DragSpeedPerHand*
                (1f-Mathf.InverseLerp(TwoHandMassLimitKg,MaximumDragMassKg,mass));
        }

        // Keep gravity and solid collision active. Only the horizontal velocity
        // toward the player is driven; contact with the terrain still constrains it.
        public void DragToward(Vector3 playerPosition,int contactingHands,float deltaTime,
            Vector3? gripPoint=null)
        {
            if(WebWorldSession.ForwardDrag(this,playerPosition,contactingHands)) return;
            float speed=DragSpeed(PickupMassKg,contactingHands);
            if(IsHeld||speed<=0f||deltaTime<=0f) return;
            var structure=GetComponent<TreeStructuralPart>();
            if(structure!=null&&!structure.CanDrag) return;
            Rigidbody body=GetComponentInParent<Rigidbody>();
            if(Rope!=null)
            {
                body=null;
                Vector3 contact=gripPoint??Rope.NearestPoint(playerPosition);
                float nearest=float.PositiveInfinity;
                foreach(var link in Rope.Bodies)
                {
                    if(link==null) continue;
                    float contactDistance=(link.worldCenterOfMass-contact).sqrMagnitude;
                    if(contactDistance>=nearest) continue;
                    nearest=contactDistance;body=link;
                }
                if(body==null) return;
            }
            if(body==null)
            {
                ResidentPropRendering.Release(gameObject);
                ReplaceSolidCollidersWithMeshColliders();
                body=gameObject.AddComponent<Rigidbody>();
                PhysicalDestructible.Ensure(gameObject)?.RefreshMass();
                GetComponent<PhysicalDestructible>()?.NotifyGeometryChanged();
                body.useGravity=true;
                body.detectCollisions=true;
                body.interpolation=RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
                DynamicObjectSurfaceGuard.Ensure(body);
            }
            if(body.isKinematic) return;
            TrackMovement();
            Vector3 toward=Vector3.ProjectOnPlane(playerPosition-body.worldCenterOfMass,Vector3.up);
            float distance=toward.magnitude;
            if(distance<=0.05f) return;
            toward/=distance;
            speed=Mathf.Min(speed,(distance-0.05f)/deltaTime);
            float current=Vector3.Dot(body.linearVelocity,toward);
            body.AddForce(toward*(speed-current),ForceMode.VelocityChange);
            body.WakeUp();
        }
        public bool CanPickUpWithTwoHands
        {
            get
            {
                float mass=PickupMassKg;
                return float.IsFinite(mass)&&mass>OneHandMassLimitKg&&mass<=TwoHandMassLimitKg;
            }
        }

        public float PickupMassKg
        {
            get
            {
                if(Corpse!=null) return Corpse.MassKg;
                if(Rope!=null) return Rope.MassKg;
                PhysicalDestructible properties=GetComponent<PhysicalDestructible>() ??
                    PhysicalDestructible.Ensure(gameObject);
                if(properties!=null&&properties.HasMaterialMass) return properties.MassKg;
                Rigidbody body=GetComponent<Rigidbody>();
                return body!=null?body.mass:0f;
            }
        }

        public bool PickUp(Transform rightHand,bool useBothHands=false,
            Vector3? aimedGripPoint=null)
        {
            if(IsHeld||rightHand==null||
                !(useBothHands?CanPickUpWithTwoHands:CanPickUpWithOneHand)) return false;
            if(!WebWorldSession.TryClaim(this)) return false;
            if(GetComponent<CorpseGrip>() is {} corpseGrip) { corpseGrip.Detach();PlayerDamage.Release(corpseGrip); }
            if(Corpse!=null)
            {
                if(!Corpse.BeginGrab(rightHand,aimedGripPoint??Corpse.NearestPoint(rightHand.position))) return false;
                IsHeld=true;HasBeenPickedUp=true;heldHand=rightHand;
                if(!HeldObjects.Contains(this)) HeldObjects.Add(this);
                TrackMovement();return true;
            }
            if(Rope!=null)
            {
                if(!Rope.BeginGrab(rightHand,aimedGripPoint??Rope.NearestPoint(rightHand.position))) return false;
                heldForwardReference=rightHand.GetComponentInParent<FirstPersonController>()?.transform;
                measuredSize=MeasureSize();
                IsHeld=true;
                HasBeenPickedUp=true;
                heldHand=rightHand;
                if(!HeldObjects.Contains(this)) HeldObjects.Add(this);
                TrackMovement();
                return true;
            }
            ResidentPropRendering.Release(gameObject);
            Rigidbody previousAssembly=GetComponentInParent<Rigidbody>();
            GetComponent<PhysicalDestructible>()?.NotifyGeometryChanged();
            EnsureDetectionTrigger();
            // Capture world dimensions before parenting so the HUD measurement
            // remains stable while the hand and object rotate.
            measuredSize=MeasureSize();
            GrabMode=ForceRegularGrip?PickableGrabMode.Regular:ChooseGrabMode(measuredSize,
                GetComponent<PhysicalDestructible>()?.MaterialName);
            heldMeshContact=transform.InverseTransformPoint(aimedGripPoint??
                (GrabMode==PickableGrabMode.Stick?
                    PickupReachPoint(rightHand.position):FindNearestMeshPoint(rightHand.position)));
            heldForwardReference=rightHand.GetComponentInParent<FirstPersonController>()?.transform;
            // Capture the player's colliders before the item becomes a child of
            // the hand, so its own mesh colliders are not mistaken for player ones.
            Transform playerRoot=heldForwardReference!=null?heldForwardReference:
                rightHand.GetComponentInParent<RiggedPlayerAvatar>()?.transform;
            if(playerRoot==null) playerRoot=rightHand;
            Collider[] playerColliders=playerRoot.GetComponentsInChildren<Collider>(true);
            if(GrabMode==PickableGrabMode.Stick) ConfigureStickGrip();
            IsHeld=true;
            if(!HeldObjects.Contains(this)) HeldObjects.Add(this);
            TrackMovement();
            HasBeenPickedUp=true;
            ReplaceSolidCollidersWithMeshColliders();
            heldColliders=System.Array.FindAll(GetComponentsInChildren<Collider>(true),
                collider=>collider.enabled&&!collider.isTrigger);
            colliderEnabledStates=new bool[heldColliders.Length];
            Rigidbody body=GetComponent<Rigidbody>();
            if(body==null) body=gameObject.AddComponent<Rigidbody>();
            PhysicalDestructible.Ensure(gameObject)?.RefreshMass();
            if(!body.isKinematic)
            {
                body.linearVelocity=Vector3.zero;
                body.angularVelocity=Vector3.zero;
            }
            unheldInterpolation=body.interpolation;
            // The hand is posed in LateUpdate. Rigidbody interpolation would
            // render the bar at an older physics pose during a fast strike.
            body.interpolation=RigidbodyInterpolation.None;
            body.isKinematic=true;
            body.useGravity=false;
            body.detectCollisions=true;
            body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
            for(int i=0;i<heldColliders.Length;i++)
            {
                colliderEnabledStates[i]=heldColliders[i].enabled;
                if(heldColliders[i].isTrigger) continue;
                if(heldColliders[i] is MeshCollider heldMesh) heldMesh.convex=true;
                heldColliders[i].enabled=true;
                foreach(Collider playerCollider in playerColliders)
                    if(playerCollider!=null&&playerCollider!=heldColliders[i])
                        Physics.IgnoreCollision(heldColliders[i],playerCollider);
            }
            detectionTrigger.enabled=false;
            transform.SetParent(rightHand,true);
            if(previousAssembly!=null&&previousAssembly.gameObject!=gameObject)
                previousAssembly.GetComponent<PhysicalDestructible>()?.RefreshMass();
            heldHand=rightHand;
            strikeImpactProtectedUntil=0f;
            if(GrabMode==PickableGrabMode.Stick||label!=PickableObjectLabel.Irregular)
                AlignHeldContact();
            previousStrikeProbePosition=StrikeProbePosition();
            previousHeldPosition=transform.position;
            previousHeldRotation=transform.rotation;
            heldMotionSampleValid=true;
            heldLinearVelocity=Vector3.zero;
            heldAngularVelocity=Vector3.zero;
            if(WebWorldSession.HasConfirmedClaim(this)) GetComponent<TreeStructuralPart>()?.OnPickedUp();
            return true;
        }

        public void TransferToHand(Transform hand,bool preserveLocalDirection=false)
        {
            if(!IsHeld||hand==null||heldHand==hand) return;
            if(Corpse!=null) { Corpse.TransferGrab(hand);heldHand=hand;return; }
            if(Rope!=null)
            {
                Rope.TransferGrip(hand);heldHand=hand;
                heldForwardReference=hand.GetComponentInParent<FirstPersonController>()?.transform;
                return;
            }
            Quaternion localDirection=transform.localRotation;
            transform.SetParent(hand,true);
            if(preserveLocalDirection) transform.localRotation=localDirection;
            heldHand=hand;
            heldForwardReference=hand.GetComponentInParent<FirstPersonController>()?.transform;
            if(GrabMode==PickableGrabMode.Stick||label!=PickableObjectLabel.Irregular)
                AlignHeldContact();
            previousStrikeProbePosition=StrikeProbePosition();
            previousHeldPosition=transform.position;
            previousHeldRotation=transform.rotation;
            heldMotionSampleValid=false;
        }

        public static PickableGrabMode ChooseGrabMode(Vector3 size)
        {
            float longest=Mathf.Max(size.x,Mathf.Max(size.y,size.z));
            float second=size.x+size.y+size.z-longest-
                Mathf.Min(size.x,Mathf.Min(size.y,size.z));
            return longest>0f&&longest>=StickAxisRatio*Mathf.Max(second,0.0001f)?
                PickableGrabMode.Stick:PickableGrabMode.Regular;
        }

        public static PickableGrabMode ChooseGrabMode(Vector3 size,string materialName)
        {
            if(!string.IsNullOrEmpty(materialName))
            {
                if(materialName.IndexOf("stone",System.StringComparison.OrdinalIgnoreCase)>=0||
                    System.Array.Exists(WorldConfig.MineralMaterials,name=>string.Equals(name,materialName,System.StringComparison.OrdinalIgnoreCase))) return PickableGrabMode.Regular;
                if(materialName.Equals("Black Vine",System.StringComparison.OrdinalIgnoreCase)||
                    materialName.Equals("Yellow Vine",System.StringComparison.OrdinalIgnoreCase)||
                    materialName.Equals("Rope",System.StringComparison.OrdinalIgnoreCase))
                    return PickableGrabMode.Rope;
                if(materialName.Equals("Wood",System.StringComparison.OrdinalIgnoreCase)||
                    materialName.Equals("Burnt Wood",System.StringComparison.OrdinalIgnoreCase))
                    return PickableGrabMode.Stick;
                switch(materialName.ToLowerInvariant())
                {
                    case "stone": case "river rock": case "chalk stone":
                    case "limestone": case "obsidian": case "basalt":
                    case "quartz": case "magnetite": case "hematite":
                    case "pyrite": case "chalcocite": case "limonite":
                    case "cassiterite": case "galena": case "meteorite":
                    case "coal":
                        return PickableGrabMode.Regular;
                }
            }
            return ChooseGrabMode(size);
        }

        private void ConfigureStickGrip()
        {
            if(!TryMeasureRelativeBounds(out Bounds bounds)) return;
            int axis=measuredSize.x>=measuredSize.y&&measuredSize.x>=measuredSize.z?0:
                measuredSize.y>=measuredSize.z?1:2;
            Vector3 direction=axis==0?Vector3.right:axis==1?Vector3.up:Vector3.forward;
            float grip=heldMeshContact[axis];
            heldMeshContact=FindStickSectionCenter(bounds,axis,grip);
            float towardMinimum=grip-bounds.min[axis];
            float towardMaximum=bounds.max[axis]-grip;
            bool forwardIsMaximum=towardMaximum>towardMinimum;
            if(Mathf.Abs(towardMaximum-towardMinimum)<0.001f)
            {
                Vector3 playerForward=heldForwardReference!=null?
                    heldForwardReference.forward:transform.forward;
                forwardIsMaximum=Vector3.Dot(transform.TransformVector(direction),playerForward)>=0f;
            }
            stickForwardPoint=FindStickForwardPoint(bounds,axis,forwardIsMaximum);
            stickForwardAxis=(stickForwardPoint-heldMeshContact).normalized;
            if(stickForwardAxis.sqrMagnitude<0.0001f)
                stickForwardAxis=forwardIsMaximum?direction:-direction;
            stickUpAxis=Vector3.ProjectOnPlane(axis==1?Vector3.forward:Vector3.up,
                stickForwardAxis).normalized;
            if(stickUpAxis.sqrMagnitude<0.0001f) stickUpAxis=Vector3.right;
        }

        private Vector3 FindStickForwardPoint(Bounds bounds,int axis,bool maximum)
            => FindStickSectionCenter(bounds,axis,maximum?bounds.max[axis]:bounds.min[axis]);

        private Vector3 FindStickSectionCenter(Bounds bounds,int axis,float coordinate)
        {
            Vector3 point=bounds.center;
            point[axis]=coordinate;
            float inset=Mathf.Max(0.0000001f,bounds.size[axis]*0.00001f);
            float plane=Mathf.Clamp(coordinate+inset,bounds.min[axis]+inset,bounds.max[axis]-inset);
            float epsilon=inset*0.01f;
            Vector3 sum=Vector3.zero;
            float weight=0f;
            var intersections=new Vector3[3];
            int count=0;
            void Edge(Vector3 a,Vector3 b)
            {
                float da=a[axis]-plane,db=b[axis]-plane;
                if(da>epsilon&&db>epsilon||da<-epsilon&&db<-epsilon||
                    Mathf.Abs(da-db)<=epsilon) return;
                Vector3 p=Vector3.LerpUnclamped(a,b,Mathf.Clamp01(da/(da-db)));
                for(int i=0;i<count;i++)
                    if((intersections[i]-p).sqrMagnitude<=epsilon*epsilon) return;
                if(count<3) intersections[count++]=p;
            }
            // Weight the actual slice boundary instead of the bounds centre:
            // a curved branch can be off-centre inside its overall bounds.
            foreach(MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh=filter.sharedMesh;
                if(mesh==null||!mesh.isReadable) continue;
                Matrix4x4 toLocal=transform.worldToLocalMatrix*filter.transform.localToWorldMatrix;
                Vector3[] vertices=mesh.vertices;
                int[] triangles=mesh.triangles;
                for(int i=0;i+2<triangles.Length;i+=3)
                {
                    Vector3 a=toLocal.MultiplyPoint3x4(vertices[triangles[i]]);
                    Vector3 b=toLocal.MultiplyPoint3x4(vertices[triangles[i+1]]);
                    Vector3 c=toLocal.MultiplyPoint3x4(vertices[triangles[i+2]]);
                    count=0;Edge(a,b);Edge(b,c);Edge(c,a);
                    if(count!=2) continue;
                    float length=Vector3.Distance(intersections[0],intersections[1]);
                    sum+=(intersections[0]+intersections[1])*0.5f*length;
                    weight+=length;
                }
            }
            if(weight>epsilon) { point=sum/weight;point[axis]=coordinate; }
            return point;
        }

        private void UpdateStickHeldTransform(float blend)
        {
            Vector3 forward=heldForwardReference!=null?
                heldForwardReference.forward:heldHand.forward;
            forward.y=0f;
            if(forward.sqrMagnitude<0.0001f) forward=Vector3.forward;
            Quaternion worldFrame=Quaternion.LookRotation(forward.normalized,Vector3.up);
            // The mesh may be stretched along its long axis. Align the actual
            // scaled grip-to-tip vector, rather than its unscaled local angle.
            Vector3 scaledForward=Vector3.Scale(stickForwardAxis,transform.localScale).normalized;
            Vector3 scaledUp=Vector3.ProjectOnPlane(
                Vector3.Scale(stickUpAxis,transform.localScale),scaledForward).normalized;
            Quaternion localFrame=Quaternion.LookRotation(scaledForward,scaledUp);
            Quaternion targetRotation=worldFrame*Quaternion.Inverse(localFrame);
            transform.rotation=Quaternion.Slerp(transform.rotation,targetRotation,blend);
            AlignHeldContact();
        }

        private void UpdateIrregularHeldTransform()
        {
            float blend=GripFollowBlend(Time.deltaTime);
            transform.localRotation=Quaternion.Slerp(transform.localRotation,
                Quaternion.Euler(0f,0f,90f),blend);
            // Local X position offset : object-relative Y size = -1 : 2.
            transform.localPosition=Vector3.Lerp(transform.localPosition,
                new Vector3(-measuredSize.y*0.5f,0f,0f),blend);
        }

        internal float GripFollowBlend(float step) {
            float speed=heldHand!=null?heldHand.GetComponentInParent<RiggedPlayerAvatar>()?.HandMovementSpeed(heldHand)??1f:1f;
            return IsMineralGrip?1f:1f-Mathf.Exp(-HeldDirectionAlignmentRate*step*speed);
        }

        internal void AlignHeldContact()
        {
            if(heldHand==null) return;
            RiggedPlayerAvatar avatar=heldHand.GetComponentInParent<RiggedPlayerAvatar>();
            Vector3 contact=GrabMode==PickableGrabMode.Stick&&avatar!=null?
                avatar.HeldStickGripPosition(heldHand):heldHand.position;
            // Rotate around the original grip point as the held pose settles.
            transform.position+=contact-transform.TransformPoint(heldMeshContact);
        }

        private Vector3 FindNearestMeshPoint(Vector3 handPosition)
        {
            Vector3 closest=transform.position;
            float bestDistance=float.PositiveInfinity;
            foreach(MeshFilter filter in GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh=filter.sharedMesh;
                if(mesh==null||!mesh.isReadable) continue;
                Vector3[] vertices=mesh.vertices;
                int[] triangles=mesh.triangles;
                for(int i=0;i+2<triangles.Length;i+=3)
                {
                    // World space makes the distance correct for scaled child meshes.
                    Vector3 a=filter.transform.TransformPoint(vertices[triangles[i]]);
                    Vector3 b=filter.transform.TransformPoint(vertices[triangles[i+1]]);
                    Vector3 c=filter.transform.TransformPoint(vertices[triangles[i+2]]);
                    Vector3 point=ClosestTrianglePoint(handPosition,a,b,c);
                    float distance=(point-handPosition).sqrMagnitude;
                    if(distance>=bestDistance) continue;
                    bestDistance=distance;
                    closest=point;
                }
            }
            // Non-readable imported meshes can still expose a collision surface.
            if(float.IsPositiveInfinity(bestDistance))
                foreach(Collider collider in GetComponentsInChildren<Collider>())
                {
                    if(collider.isTrigger||!collider.enabled) continue;
                    Vector3 point=collider is MeshCollider { convex: false }?
                        collider.bounds.ClosestPoint(handPosition):collider.ClosestPoint(handPosition);
                    float distance=(point-handPosition).sqrMagnitude;
                    if(distance>=bestDistance) continue;
                    bestDistance=distance;
                    closest=point;
                }
            return closest;
        }

        public Vector3 NearestMeshSurfacePoint(Vector3 position) =>
            FindNearestMeshPoint(position);

        internal static Vector3 ClosestTrianglePoint(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
        {
            Vector3 ab=b-a,ac=c-a,normal=Vector3.Cross(ab,ac);
            float normalSquared=normal.sqrMagnitude;
            if(normalSquared>1e-12f)
            {
                Vector3 projected=p-normal*(Vector3.Dot(p-a,normal)/normalSquared);
                float u=Vector3.Dot(Vector3.Cross(b-projected,c-projected),normal)/normalSquared;
                float v=Vector3.Dot(Vector3.Cross(c-projected,a-projected),normal)/normalSquared;
                if(u>=0f&&v>=0f&&u+v<=1f) return projected;
            }
            Vector3 first=ClosestSegmentPoint(p,a,b);
            Vector3 second=ClosestSegmentPoint(p,b,c);
            Vector3 third=ClosestSegmentPoint(p,c,a);
            Vector3 closest=(first-p).sqrMagnitude<(second-p).sqrMagnitude?first:second;
            return (third-p).sqrMagnitude<(closest-p).sqrMagnitude?third:closest;
        }

        private static Vector3 ClosestSegmentPoint(Vector3 p,Vector3 a,Vector3 b)
        {
            Vector3 edge=b-a;
            return a+edge*Mathf.Clamp01(Vector3.Dot(p-a,edge)/Mathf.Max(edge.sqrMagnitude,1e-12f));
        }

        public void Release(Vector3 velocity,Transform owner)
        {
            if(!IsHeld) return;
            if(Corpse!=null) {
                Corpse.EndGrab(velocity);IsHeld=false;HeldObjects.Remove(this);heldHand=null;
                TrackMovement();WebWorldSession.Released(this,velocity);return;
            }
            if(Rope!=null)
            {
                Rope.EndGrab(velocity);
                IsHeld=false;NpcOwner=null;
                HeldObjects.Remove(this);
                heldHand=null;
                TrackMovement();
                WebWorldSession.Released(this,velocity);
                return;
            }
            transform.SetParent(null,true);
            IsHeld=false;NpcOwner=null;
            HeldObjects.Remove(this);
            TrackMovement();
            strikeImpactProtectedUntil=0f;
            heldHand=null;
            heldForwardReference=null;
            heldMotionSampleValid=false;
            heldLinearVelocity=Vector3.zero;
            heldAngularVelocity=Vector3.zero;
            for(int i=0;i<heldColliders.Length;i++)
            {
                Collider collider=heldColliders[i];
                if(collider==null||collider==detectionTrigger) continue;
                if(collider is MeshCollider mesh) mesh.convex=true;
                collider.enabled=colliderEnabledStates[i];
                foreach(Collider playerCollider in owner.GetComponentsInChildren<Collider>(true))
                    Physics.IgnoreCollision(collider,playerCollider);
            }
            Rigidbody body=GetComponent<Rigidbody>();
            if(body==null) body=gameObject.AddComponent<Rigidbody>();
            PhysicalDestructible.Ensure(gameObject)?.RefreshMass();
            body.isKinematic=false;
            body.interpolation=unheldInterpolation;
            body.detectCollisions=true;
            body.useGravity=true;
            body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity=velocity;
            body.angularVelocity=Vector3.zero;
            DynamicObjectSurfaceGuard.Ensure(body);
            WebWorldSession.Released(this,velocity);
            Physics.SyncTransforms();
            body.GetComponent<DynamicObjectSurfaceGuard>()?.ResolveStaticOverlap();
            body.WakeUp();
        }

        public void RefreshHeldCollisionGeometry()
        {
            if(Rope!=null) return;
            UpdateDetectionTriggerSize();
            if(!IsHeld||heldHand==null) return;
            Transform playerRoot=heldForwardReference!=null?heldForwardReference:
                heldHand.GetComponentInParent<RiggedPlayerAvatar>()?.transform;
            if(playerRoot==null) playerRoot=heldHand;
            Collider[] playerColliders=playerRoot.GetComponentsInChildren<Collider>(true);
            foreach(Collider mesh in GetComponentsInChildren<Collider>(true))
            {
                if(mesh==null||!mesh.enabled||mesh.isTrigger||
                    System.Array.IndexOf(heldColliders,mesh)>=0) continue;
                int length=heldColliders.Length;
                System.Array.Resize(ref heldColliders,length+1);
                System.Array.Resize(ref colliderEnabledStates,length+1);
                heldColliders[length]=mesh;
                colliderEnabledStates[length]=true;
                if(mesh is MeshCollider heldMesh) heldMesh.convex=true;
                mesh.enabled=true;
                foreach(Collider playerCollider in playerColliders)
                    if(playerCollider!=null&&!playerCollider.transform.IsChildOf(transform))
                        Physics.IgnoreCollision(mesh,playerCollider);
            }
        }

        private Vector3 MeasureSize()
        {
            if(Corpse!=null) {
                Bounds bounds=new Bounds(transform.position,Vector3.zero);
                foreach(var c in GetComponentsInChildren<Collider>()) if(c.enabled&&!c.isTrigger) bounds.Encapsulate(c.bounds);
                return bounds.size;
            }
            if(!TryMeasureRelativeBounds(out Bounds relativeBounds)) return Vector3.zero;

            // Express the dimensions along this object's own X/Y/Z directions.
            // World rotation must not swap or inflate any of these measurements.
            return new Vector3(
                relativeBounds.size.x*transform.TransformVector(Vector3.right).magnitude,
                relativeBounds.size.y*transform.TransformVector(Vector3.up).magnitude,
                relativeBounds.size.z*transform.TransformVector(Vector3.forward).magnitude);
        }

        private bool TryMeasureRelativeBounds(out Bounds relativeBounds)
        {
            relativeBounds=default;
            bool hasBounds=false;
            foreach(MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
            {
                if(filter.sharedMesh==null) continue;
                EncapsulateRelativeBounds(ref relativeBounds,ref hasBounds,
                    filter.sharedMesh.bounds,filter.transform);
            }
            foreach(SkinnedMeshRenderer renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                EncapsulateRelativeBounds(ref relativeBounds,ref hasBounds,
                    renderer.localBounds,renderer.transform);
            return hasBounds;
        }

        private void EncapsulateRelativeBounds(ref Bounds target,ref bool hasBounds,
            Bounds sourceBounds,Transform source)
        {
            Vector3 minimum=sourceBounds.min;
            Vector3 maximum=sourceBounds.max;
            for(int corner=0;corner<8;corner++)
            {
                Vector3 sourcePoint=new(
                    (corner&1)==0?minimum.x:maximum.x,
                    (corner&2)==0?minimum.y:maximum.y,
                    (corner&4)==0?minimum.z:maximum.z);
                Vector3 relativePoint=transform.InverseTransformPoint(
                    source.TransformPoint(sourcePoint));
                if(!hasBounds)
                {
                    target=new Bounds(relativePoint,Vector3.zero);
                    hasBounds=true;
                }
                else target.Encapsulate(relativePoint);
            }
        }

        private void ReplaceSolidCollidersWithMeshColliders()
        {
            if(meshCollidersPrepared) return;
            // Fragments already have their cooked convex collision geometry.
            // Preserve it when pickup proximity checks inspect the visual mesh.
            if(GetComponent<DestructionFragment>()!=null&&
                System.Array.Exists(GetComponentsInChildren<Collider>(true),c=>!c.isTrigger))
            { meshCollidersPrepared=true; return; }
            bool createdMeshCollider=false;
            foreach(MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
            {
                if(filter.sharedMesh==null) continue;
                MeshCollider meshCollider=filter.GetComponent<MeshCollider>();
                if(meshCollider==null) meshCollider=filter.gameObject.AddComponent<MeshCollider>();
                meshCollider.sharedMesh=filter.sharedMesh;
                meshCollider.convex=true;
                meshCollider.isTrigger=false;
                meshCollider.enabled=true;
                createdMeshCollider=true;
            }
            if(!createdMeshCollider) return;
            foreach(Collider collider in GetComponentsInChildren<Collider>(true))
            {
                if(collider==detectionTrigger||collider.isTrigger||collider is MeshCollider) continue;
                collider.enabled=false;
                if(Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }
            meshCollidersPrepared=true;
        }

        public void SetNearbyDetection(bool active)
        {
            if(Rope!=null||Corpse!=null) { if(detectionTrigger!=null) detectionTrigger.enabled=false;return; }
            if(IsHeld) active=false;
            if(active&&detectionTrigger==null)
            {
                EnsureDetectionTrigger();
                detectionTrigger.enabled=false;
            }
            if(detectionTrigger!=null&&detectionTrigger.enabled==active) return;
            if(active) ReplaceSolidCollidersWithMeshColliders();
            if(active) UpdateDetectionTriggerSize();
            if(detectionTrigger!=null) detectionTrigger.enabled=active;
        }

        internal void RefreshDetectionGeometry()
        {
            pickupRenderers=null;
            UpdateDetectionTriggerSize();
        }

        private void EnsureDetectionTrigger()
        {
            // Replace pickup spheres from older scenes with a capsule that can
            // follow a stick or branch without covering a huge empty sphere.
            foreach(SphereCollider oldSphere in GetComponents<SphereCollider>())
            {
                if(!oldSphere.isTrigger) continue;
                oldSphere.enabled=false;
                if(Application.isPlaying) Destroy(oldSphere);
                else DestroyImmediate(oldSphere);
            }
            if(detectionTrigger==null)
            {
                foreach(CapsuleCollider collider in GetComponents<CapsuleCollider>())
                {
                    if(!collider.isTrigger) continue;
                    detectionTrigger=collider;
                    break;
                }
            }
            if(detectionTrigger==null)
            {
                detectionTrigger=gameObject.AddComponent<CapsuleCollider>();
            }
            detectionTrigger.isTrigger=true;
            UpdateDetectionTriggerSize();
        }

        private void UpdateDetectionTriggerSize()
        {
            if(detectionTrigger==null) return;
            Bounds bounds=default;
            bool found=false;
            foreach(MeshCollider meshCollider in GetComponentsInChildren<MeshCollider>(true))
            {
                if(meshCollider==null||meshCollider.sharedMesh==null||meshCollider.isTrigger||
                    meshCollider.GetComponentInParent<PickableObject>(true)!=this) continue;
                Bounds local=meshCollider.sharedMesh.bounds;
                for(int corner=0;corner<8;corner++)
                {
                    Vector3 point=transform.InverseTransformPoint(
                        meshCollider.transform.TransformPoint(new Vector3(
                        (corner&1)==0?local.min.x:local.max.x,
                        (corner&2)==0?local.min.y:local.max.y,
                        (corner&4)==0?local.min.z:local.max.z)));
                    if(!found) { bounds=new Bounds(point,Vector3.zero); found=true; }
                    else bounds.Encapsulate(point);
                }
            }
            if(!found)
                foreach(Renderer renderer in GetComponentsInChildren<Renderer>(true))
                {
                    if(renderer.GetComponentInParent<PickableObject>(true)!=this) continue;
                    Bounds world=renderer.bounds;
                    for(int corner=0;corner<8;corner++)
                    {
                        Vector3 point=transform.InverseTransformPoint(new Vector3(
                            (corner&1)==0?world.min.x:world.max.x,
                            (corner&2)==0?world.min.y:world.max.y,
                            (corner&4)==0?world.min.z:world.max.z));
                        if(!found) { bounds=new Bounds(point,Vector3.zero); found=true; }
                        else bounds.Encapsulate(point);
                    }
                }
            Vector3 scale=transform.lossyScale;
            float[] worldSizes={bounds.size.x*Mathf.Abs(scale.x),
                bounds.size.y*Mathf.Abs(scale.y),bounds.size.z*Mathf.Abs(scale.z)};
            int axis=worldSizes[0]>=worldSizes[1]&&worldSizes[0]>=worldSizes[2]?0:
                worldSizes[1]>=worldSizes[2]?1:2;
            int first=(axis+1)%3,second=(axis+2)%3;
            float[] localExtents={bounds.extents.x,bounds.extents.y,bounds.extents.z};
            float radialScale=Mathf.Max(0.0001f,Mathf.Max(Mathf.Abs(scale[first]),
                Mathf.Abs(scale[second])));
            float radius=Mathf.Max(PickupTriggerWorldRadius/radialScale,
                Mathf.Sqrt(localExtents[first]*localExtents[first]+
                    localExtents[second]*localExtents[second])+0.03f/radialScale);
            detectionTrigger.center=found?bounds.center:Vector3.zero;
            detectionTrigger.direction=axis;
            detectionTrigger.radius=radius;
            detectionTrigger.height=Mathf.Max(radius*2f,
                (found?bounds.size[axis]:0f)+radius*2f);
        }
    }

    // A launched copy must survive replacement or disposal of the held stone's mesh.
    public sealed class GoldenStoneCopyMeshes : MonoBehaviour
    {
        readonly System.Collections.Generic.List<Mesh> owned=new();
        public void CopyMeshes()
        {
            var copies=new System.Collections.Generic.Dictionary<Mesh,Mesh>();
            foreach(var filter in GetComponentsInChildren<MeshFilter>())
            {
                var source=filter.sharedMesh;
                if(source==null) continue;
                if(!copies.TryGetValue(source,out var mesh))
                { mesh=Instantiate(source); copies.Add(source,mesh); owned.Add(mesh); }
                filter.sharedMesh=mesh;
            }
            foreach(var collider in GetComponentsInChildren<MeshCollider>())
                if(collider.sharedMesh!=null&&copies.TryGetValue(collider.sharedMesh,out var mesh))
                    collider.sharedMesh=mesh;
        }
        void OnDestroy()
        {
            foreach(var mesh in owned)
                if(mesh!=null) { if(Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); }
        }
    }

    /// <summary>Finds aimed pickables within the right arm's reachable shoulder volume.</summary>
    public sealed class PlayerPickupController : MonoBehaviour
    {
        private const float NearbyTriggerRange=5.04f;
        private const float MaximumThrowChargeSeconds=5f;
        private const float ThrowChargeSpeedMultiplier=2.25f;
        private const float ThrowReleaseDelaySeconds=0.1f;
        private FirstPersonController player;
        private RiggedPlayerAvatar avatar;
        private PickableObject aimedObject;
        private float nextTriggerRefresh;
        private bool chargingDrop;
        private bool chargingLeftHand;
        private float dropStartedAt;
        private Key chargingDropKey;
        private bool attackConsumedThisFrame;
        private bool aimConsumedThisFrame,leftAimDropGesture;
        private bool strikeUsesAimInput;
        private bool throwPending;
        private float throwReleaseAt;
        private Vector3 pendingThrowVelocity;
        [SerializeField] private float minimumThrowSpeed=8f;
        [SerializeField] private float throwSpeedPerSecond=6f;
        private readonly System.Collections.Generic.List<PickableObject> nearbyPickables=new();
        private readonly System.Collections.Generic.List<PickableObject> pickupCandidates=new();
        private readonly RaycastHit[] pickupAimHits=new RaycastHit[64];

        public PickableObject AimedObject => aimedObject;
        public PickableObject HeldObject => avatar!=null?avatar.HeldRightHandItem:null;
        public PickableObject HeldLeftObject => avatar!=null?avatar.HeldLeftHandItem:null;
        public bool HasRightHandItem => avatar!=null&&avatar.HasRightHandItem;
        public bool HasLeftHandItem => avatar!=null&&avatar.HasLeftHandItem;

        public void Initialize(FirstPersonController controller,RiggedPlayerAvatar playerAvatar)
        {
            player=controller;
            avatar=playerAvatar;
        }

        private void Update()
        {
            if(player==null||avatar==null) return;
            if(player.IsDead) { CancelDrop();return; }
            if(player.IsPaused||Keyboard.current==null||!Application.isFocused)
            {
                CancelDrop();
                avatar.CancelRightHandPickup();
                avatar.CancelRightHandStrike();
                avatar.SetReachCrouchInput(false);
                return;
            }
            attackConsumedThisFrame=false;
            aimConsumedThisFrame=false;
            if(Keyboard.current[player.Bindings[FirstPersonController.SwitchBindingIndex]].wasPressedThisFrame)
            {
                CancelDrop();
                avatar.SwitchHeldItems();
            }
            HandleDrop();
            bool interacting=Keyboard.current[player.Bindings[FirstPersonController.InteractBindingIndex]].isPressed;
            if(!interacting&&Mouse.current!=null&&Mouse.current.rightButton.wasPressedThisFrame)
                HeldObject?.LaunchGoldenStoneCopy(player.PlayerCamera,player.transform);
            if(Time.unscaledTime>=nextTriggerRefresh)
            {
                nextTriggerRefresh=Time.unscaledTime+0.25f;
                RefreshNearbyTriggers();
            }
            aimedObject=FindAimedPickable(out RaycastHit aimHit);
            bool attackHeld=Mouse.current!=null&&Mouse.current.leftButton.isPressed;
            bool aimHeld=player.AimHeld;
            bool attackPressed=Mouse.current!=null&&Mouse.current.leftButton.wasPressedThisFrame;
            bool attackReleased=Mouse.current!=null&&Mouse.current.leftButton.wasReleasedThisFrame;
            UpdateStrikeInput(interacting,attackPressed,attackReleased,
                !aimConsumedThisFrame&&player.AimPressed,!aimConsumedThisFrame&&player.AimReleased);
            bool reaching=!attackConsumedThisFrame&&interacting&&(attackHeld||aimHeld);
            bool reachLeft=ReachWithLeftHand(attackHeld,aimHeld);
            if(avatar.IsReaching&&(!reaching||
                reachLeft&&!avatar.IsLeftReaching||!reachLeft&&!avatar.IsRightReaching))
                avatar.CancelRightHandPickup();
            if(reaching)
            {
                // Keep the selected grip while the hand travels. The camera ray
                // can slip off the face as the player crouches or the arm moves.
                if(aimedObject!=null&&!avatar.IsReachingForObject)
                {
                    if(avatar.IsReaching) avatar.CancelRightHandPickup();
                    avatar.BeginHandPickup(aimedObject,reachLeft,
                        aimedObject.PickupFaceGripPoint(aimHit));
                }
                if(!avatar.IsReaching)
                    avatar.BeginHandReach(reachLeft);
            }
            avatar.SetReachCrouchInput(reaching);
        }

        internal static bool ReachWithLeftHand(bool attackHeld,bool aimHeld)=>aimHeld&&!attackHeld;

        internal void UpdateStrikeInput(bool interacting,bool attackPressed,bool attackReleased,
            bool aimPressed,bool aimReleased)
        {
            if(interacting) { avatar.CancelAllStrikes();return; }
            avatar.UpdateHandStrikeInput(true,aimReleased);
            avatar.UpdateHandStrikeInput(false,attackReleased);
            if(attackConsumedThisFrame||interacting) return;
            if(aimPressed&&HasLeftHandItem)
            {
                avatar.BeginLeftHandStrike();
            }
            if(attackPressed)
            {
                if(HasRightHandItem||HasLeftHandItem)
                {
                    avatar.BeginRightHandStrike();
                }
                else if(!avatar.IsStrikeMode) avatar.BeginRightHandPunch();
            }
        }

        private void CancelDrop()
        {
            chargingDrop=false;
            chargingLeftHand=false;
            leftAimDropGesture=false;
            throwPending=false;
            pendingThrowVelocity=Vector3.zero;
            if(avatar!=null)
            {
                avatar.CancelPendingRightHandThrowAnimation();
                avatar.SetRightHandLiftCharge(0f);
                avatar.SetLeftHandLiftCharge(0f);
            }
        }

        private void OnDisable()
        {
            CancelDrop();
            if(avatar!=null)
            {
                avatar.CancelRightHandPickup();
                avatar.SetReachCrouchInput(false);
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            if(focused) return;
            CancelDrop();
            if(avatar!=null) avatar.CancelRightHandPickup();
        }

        private void HandleDrop()
        {
            if(Keyboard.current==null) return;
            Key key=player.Bindings[FirstPersonController.DropBindingIndex];
            var button=Keyboard.current[key];
            HandleDropInput(player.AimHeld,player.AimPressed,player.AimReleased,
                button.isPressed,button.wasPressedThisFrame,button.wasReleasedThisFrame);
        }

        internal void HandleDropInput(bool aimDown,bool aimPressed,bool aimReleased,
            bool dropDown,bool dropPressed,bool dropReleased)
        {
            if(!HasRightHandItem&&!HasLeftHandItem) { CancelDrop(); return; }
            if((chargingDrop||throwPending)&&!avatar.CanUseHand(chargingLeftHand)) { CancelDrop();return; }
            if(throwPending)
            {
                if(Time.time<throwReleaseAt) return;
                throwPending=false;
                if(chargingLeftHand) avatar.ReleaseLeftHandItem(pendingThrowVelocity,true);
                else avatar.ReleaseRightHandItem(pendingThrowVelocity,true);
                pendingThrowVelocity=Vector3.zero;
                avatar.SetRightHandLiftCharge(0f);
                avatar.SetLeftHandLiftCharge(0f);
                chargingLeftHand=false;
                attackConsumedThisFrame=true;
                return;
            }
            Key key=player.Bindings[FirstPersonController.DropBindingIndex];
            if(dropDown&&aimPressed&&HasLeftHandItem&&HeldLeftObject!=HeldObject&&avatar.CanUseHand(true))
            {
                chargingDrop=true;chargingLeftHand=true;leftAimDropGesture=true;
                chargingDropKey=key;dropStartedAt=Time.time;
                avatar.SetRightHandLiftCharge(0f);aimConsumedThisFrame=true;
            }
            if(leftAimDropGesture) aimConsumedThisFrame=true;
            if(chargingDrop&&key!=chargingDropKey) { CancelDrop(); return; }
            if(dropPressed&&!leftAimDropGesture)
            {
                chargingDrop=true;
                chargingLeftHand=HasLeftHandItem&&(!HasRightHandItem||
                    aimDown&&HeldLeftObject!=HeldObject);
                if(!avatar.CanUseHand(chargingLeftHand)) {
                    chargingLeftHand=!chargingLeftHand;
                    if(!avatar.CanUseHand(chargingLeftHand)||!(chargingLeftHand?HasLeftHandItem:HasRightHandItem)) { CancelDrop();return; }
                }
                chargingDropKey=key;
                dropStartedAt=Time.time;
            }
            if(!chargingDrop) return;
            float duration=Mathf.Max(0f,Time.time-dropStartedAt);
            float chargedDuration=duration*ThrowChargeSpeedMultiplier*avatar.ArmMovementSpeed(chargingLeftHand);
            float cappedDuration=Mathf.Min(chargedDuration,MaximumThrowChargeSeconds);
            if(!chargingLeftHand)
                avatar.SetRightHandLiftCharge(cappedDuration/MaximumThrowChargeSeconds);
            else avatar.SetLeftHandLiftCharge(cappedDuration/MaximumThrowChargeSeconds);
            bool throwReady=chargedDuration>=1f;
            bool throwAction=leftAimDropGesture?(aimReleased||!aimDown||!dropDown):
                Mouse.current!=null&&Mouse.current.leftButton.wasPressedThisFrame;
            if(throwReady&&throwAction)
            {
                float speed=minimumThrowSpeed+(cappedDuration-1f)*throwSpeedPerSecond;
                pendingThrowVelocity=player.PlayerCamera.transform.forward*speed*avatar.ArmMovementSpeed(chargingLeftHand);
                throwReleaseAt=Time.time+ThrowReleaseDelaySeconds/avatar.ArmMovementSpeed(chargingLeftHand);
                throwPending=true;
                chargingDrop=false;
                if(!chargingLeftHand) avatar.BeginRightHandThrowAnimation();
                else avatar.BeginLeftHandThrowAnimation();
                attackConsumedThisFrame=true;
                return;
            }
            if(leftAimDropGesture&&(aimReleased||!aimDown||!dropDown)||dropReleased)
            {
                if(!throwReady)
                {
                    if(chargingLeftHand) avatar.ReleaseLeftHandItem(Vector3.zero);
                    else avatar.ReleaseRightHandItem(Vector3.zero,false);
                }
                CancelDrop();
            }
            else if(!dropDown) CancelDrop();
        }

        private PickableObject FindAimedPickable(out RaycastHit aimedHit)
        {
            aimedHit=default;
            Camera camera=player.PlayerCamera;
            if(camera==null||avatar.RightPickupRange<=0f) return null;
            int count=Physics.RaycastNonAlloc(new Ray(camera.transform.position,
                camera.transform.forward),pickupAimHits,avatar.RightPickupRange+1f,
                Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            float nearest=float.PositiveInfinity;
            for(int i=0;i<count;i++)
            {
                RaycastHit hit=pickupAimHits[i];
                Collider collider=hit.collider;
                if(collider==null||hit.distance>=nearest||
                    collider.GetComponentInParent<FirstPersonController>()!=null) continue;
                nearest=hit.distance;
                aimedHit=hit;
            }
            if(aimedHit.collider==null) return null;
            PickableObject candidate=aimedHit.collider.GetComponentInParent<PickableObject>();
            if(candidate==null||candidate.IsHeld||!WebWorldSession.CanInteract(candidate.gameObject)||
                !candidate.IsPickupDetectionCollider(aimedHit.collider)) return null;
            Vector3 point=aimedHit.point;
            float range=avatar.RightPickupRange;
            if(Vector3.Distance(point,avatar.RightShoulderPosition)>range&&
                Vector3.Distance(point,avatar.LeftShoulderPosition)>range) return null;
            return candidate;
        }

        private void RefreshNearbyTriggers()
        {
            pickupCandidates.Clear();
            PickupSpatialIndex.Collect(transform.position,NearbyTriggerRange,pickupCandidates);
            for(int i=pickupCandidates.Count-1;i>=0;i--)
            {
                PickableObject pickable=pickupCandidates[i];
                bool nearby=!pickable.IsHeld&&WebWorldSession.CanInteract(pickable.gameObject)&&pickable.IsNearPickupRange(transform.position,NearbyTriggerRange);
                pickable.SetNearbyDetection(nearby);
                if(!nearby) pickupCandidates.RemoveAt(i);
            }
            foreach(PickableObject previous in nearbyPickables)
                if(previous!=null&&!pickupCandidates.Contains(previous)) previous.SetNearbyDetection(false);
            nearbyPickables.Clear();
            nearbyPickables.AddRange(pickupCandidates);
        }

    }
}
