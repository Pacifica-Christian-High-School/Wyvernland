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
        private const float StrikeLookSpeedScale = 0.112f;
        private const float StrikeLookSmoothing = 5f;
        private const float StrikeFastLookSpeedScale = 0.42f;
        private const float StrikeFastLookSmoothing = 10f;
        private const float StrikeCameraBoostStartDegreesPerSecond = 25f;
        private const float StrikeFullSpeedDegreesPerSecond = 140f;
        public const float StrikeFastLookDegreesPerSecond = 45f;

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
        private Vector3 spawnPosition;
        private bool paused;
        private int inputBlockedFrame = -1;
        public static readonly string[] BindingNames =
            { "Forward", "Backward", "Left", "Right", "Sprint", "Jump", "Crouch", "Interact", "Drop" };
        private static readonly Key[] DefaultKeys =
            { Key.W, Key.S, Key.A, Key.D, Key.LeftShift, Key.Space, Key.C, Key.E, Key.G };
        public const int InteractBindingIndex = 7;
        public const int DropBindingIndex = 8;
        public Key[] Bindings { get; } = (Key[])DefaultKeys.Clone();
        public float Sensitivity { get => mouseSensitivity; set => mouseSensitivity = Mathf.Clamp(value, 0.03f, 0.4f); }
        public Camera PlayerCamera => view.GetComponent<Camera>();
        public bool IsPaused => paused;

        public void SetPaused(bool value)
        {
            paused = value;
            inputBlockedFrame = Time.frameCount;
            if (value)
            {
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
            Bindings[action] = key;
            SaveControls();
        }

        public void ResetBindings()
        {
            System.Array.Copy(DefaultKeys, Bindings, Bindings.Length);
            SaveControls();
        }

        public void SaveControls()
        {
            for (int i = 0; i < Bindings.Length; i++)
                PlayerPrefs.SetInt("Controls." + BindingNames[i], (int)Bindings[i]);
            PlayerPrefs.SetFloat("Controls.Sensitivity", mouseSensitivity);
        }

        public bool IsSprinting { get; private set; }
        public bool IsCrouching { get; private set; }
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

        public void SetPickupCrouch(bool active)
        {
            pickupCrouchForced=active;
            if(active) IsSprinting=false;
            IsCrouching=(crouchHeld||pickupCrouchForced)&&!IsSprinting;
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
            Sensitivity = PlayerPrefs.GetFloat("Controls.Sensitivity", mouseSensitivity);
        }

        private void Start()
        {
            // Bootstrap may have already opened the main menu before Start runs.
            SetCursorLocked(!paused);
        }

        private void Update()
        {
            LowerBodyTurnRate = 0f;
            if (paused || Time.frameCount == inputBlockedFrame || Keyboard.current == null)
                return;

            HandleCursor();
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
            if (playerBody == null || paused) return;
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
                        float forcedSpeed = Mathf.Lerp(walkSpeed, sprintSpeed * 1.35f, slopeAmount);
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
                    ? bodyCapsuleStandingHeight * 0.5f
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
                return;
            }

            RawLookDelta = Mouse.current.delta.ReadValue() * mouseSensitivity * lookSpeedScale;
            RawLookSpeedDegreesPerSecond = RawLookDelta.magnitude /
                Mathf.Max(Time.unscaledDeltaTime,0.001f);
            if(movementAvatar==null) movementAvatar=GetComponent<RiggedPlayerAvatar>();
            bool striking=movementAvatar!=null&&movementAvatar.IsStrikeMode;
            if(striking!=strikeLookActiveLastFrame)
            {
                smoothedLookDelta=Vector2.zero;
                strikeCameraSpeedBlend=0f;
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
            Vector2 targetDelta = RawLookDelta * (striking?
                Mathf.Lerp(StrikeLookSpeedScale,StrikeFastLookSpeedScale,
                    strikeCameraSpeedBlend):1f);
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

            IsSprinting = !pickupCrouchForced&&(Keyboard.current[Bindings[4]].isPressed ||
                (Bindings[4] == Key.LeftShift && Keyboard.current.rightShiftKey.isPressed))
                && input.y > 0f;
            bool defaultCrouchAlternate=Bindings[6]==Key.C&&Keyboard.current.leftCtrlKey.isPressed;
            crouchHeld=Keyboard.current[Bindings[6]].isPressed||defaultCrouchAlternate;
            IsCrouching=(crouchHeld||pickupCrouchForced)&&!IsSprinting;
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
    public enum PickableGrabMode { Regular, Stick }

    /// <summary>Marks a world object as collectible by the player's right hand.</summary>
    public sealed class PickableObject : MonoBehaviour
    {
        public static readonly System.Collections.Generic.HashSet<PickableObject> ActiveObjects=new();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetActiveObjects() => ActiveObjects.Clear();
        private void OnEnable() => ActiveObjects.Add(this);
        private void OnDisable() => ActiveObjects.Remove(this);
        private const float HeldDirectionAlignmentRate = 1.2f;
        private const float StickAxisRatio = 2.5f;
        private const float PickupTriggerWorldRadius = 0.11f;
        [SerializeField] private string displayName="Object";
        [SerializeField] private PickableObjectLabel label=PickableObjectLabel.Regular;
        [SerializeField,HideInInspector] private CapsuleCollider detectionTrigger;
        public string DisplayName { get => displayName; set => displayName=value; }
        public PickableObjectLabel Label { get => label; set => label=value; }
        public bool IsHeld { get; private set; }
        public bool HasBeenPickedUp { get; private set; }
        public Rigidbody LaunchGoldenStoneCopy(Camera camera,Transform playerRoot)
        {
            if(!IsHeld||displayName!="Golden Stone"||camera==null) return null;
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
        public PickableGrabMode GrabMode { get; private set; } = PickableGrabMode.Regular;
        public Vector3 StickForwardPoint => transform.TransformPoint(stickForwardPoint);
        public Vector3 Size => measuredSize.sqrMagnitude>0f?measuredSize:MeasureSize();
        private Collider[] heldColliders;
        private bool[] colliderEnabledStates;
        private Vector3 previousHeldPosition;
        private Quaternion previousHeldRotation;
        private bool heldMotionSampleValid;
        private Vector3 heldLinearVelocity;
        private Vector3 heldAngularVelocity;
        public Vector3 HeldPointVelocity(Vector3 worldPoint) =>
            IsHeld?heldLinearVelocity+Vector3.Cross(heldAngularVelocity,worldPoint-transform.position):Vector3.zero;
        public Vector3 HeldAngularVelocity => IsHeld?heldAngularVelocity:Vector3.zero;
        private Vector3 measuredSize;
        private bool meshCollidersPrepared;
        private Transform heldHand;
        private Vector3 heldMeshContact;
        private Vector3 stickForwardAxis;
        private Vector3 stickUpAxis;
        private Vector3 stickForwardPoint;
        private Transform heldForwardReference;
        private ProceduralMap stonePitMap;
        private Mesh stonePitMesh;
        private Matrix4x4 stonePitSpawnMatrix;

        public void ConfigureStonePit(ProceduralMap map,Mesh mesh,Transform meshTransform)
        {
            stonePitMap=map;
            stonePitMesh=mesh;
            stonePitSpawnMatrix=meshTransform.localToWorldMatrix;
        }
        private void Awake()
        {
            EnsureDetectionTrigger();
            detectionTrigger.enabled=false;
        }

        private void LateUpdate()
        {
            if(!IsHeld||heldHand==null) return;
            if(GrabMode==PickableGrabMode.Stick)
            {
                UpdateStickHeldTransform(1f-Mathf.Exp(-HeldDirectionAlignmentRate*Time.deltaTime));
                return;
            }
            if(label==PickableObjectLabel.Irregular)
            {
                UpdateIrregularHeldTransform();
                return;
            }
            transform.localRotation=Quaternion.Slerp(transform.localRotation,
                Quaternion.identity,1f-Mathf.Exp(-HeldDirectionAlignmentRate*Time.deltaTime));
            AlignHeldContact();
        }

        private void FixedUpdate()
        {
            if(!IsHeld||heldHand==null) return;
            Vector3 position=transform.position;
            Quaternion rotation=transform.rotation;
            if(heldMotionSampleValid)
            {
                float inverseStep=1f/Mathf.Max(Time.fixedDeltaTime,0.0001f);
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

        public Vector3 AimPoint
        {
            get
            {
                if(detectionTrigger!=null)
                    return transform.TransformPoint(detectionTrigger.center);
                Renderer renderer=GetComponentInChildren<Renderer>();
                return renderer!=null?renderer.bounds.center:transform.position;
            }
        }

        public bool IsPickupDetectionCollider(Collider collider)
        {
            if(collider==null||!collider.enabled) return false;
            if(collider==detectionTrigger) return detectionTrigger.isTrigger;
            return collider is MeshCollider&&!collider.isTrigger;
        }

        public bool PickupDetectionTouchesSphere(Vector3 worldCentre,float worldRadius)
        {
            float radiusSquared=worldRadius*worldRadius;
            if(detectionTrigger!=null&&detectionTrigger.enabled&&detectionTrigger.isTrigger)
            {
                Vector3 closest=detectionTrigger.ClosestPoint(worldCentre);
                if((closest-worldCentre).sqrMagnitude<=radiusSquared) return true;
            }
            foreach(MeshCollider meshCollider in GetComponentsInChildren<MeshCollider>())
            {
                if(!meshCollider.enabled||meshCollider.isTrigger) continue;
                Vector3 closest=meshCollider.convex?
                    meshCollider.ClosestPoint(worldCentre):
                    meshCollider.bounds.ClosestPoint(worldCentre);
                if((closest-worldCentre).sqrMagnitude<=radiusSquared) return true;
            }
            return false;
        }

        public void PickUp(Transform rightHand)
        {
            if(IsHeld||rightHand==null) return;
            Rigidbody previousAssembly=GetComponentInParent<Rigidbody>();
            GetComponent<PhysicalDestructible>()?.NotifyGeometryChanged();
            EnsureDetectionTrigger();
            if(!HasBeenPickedUp&&stonePitMap!=null&&stonePitMesh!=null)
            {
                Mesh cutter=StoneTerrainCutout.SimplifyCutter(stonePitMesh);
                try { stonePitMap.CarvePickedUpStone(cutter,stonePitSpawnMatrix); }
                finally
                {
                    if(Application.isPlaying) Destroy(cutter); else DestroyImmediate(cutter);
                }
            }
            // Capture world dimensions before parenting so the HUD measurement
            // remains stable while the hand and object rotate.
            measuredSize=MeasureSize();
            heldMeshContact=transform.InverseTransformPoint(
                FindNearestMeshPoint(rightHand.position));
            GrabMode=ChooseGrabMode(measuredSize,
                GetComponent<PhysicalDestructible>()?.MaterialName);
            heldForwardReference=rightHand.GetComponentInParent<FirstPersonController>()?.transform;
            // Capture the player's colliders before the item becomes a child of
            // the hand, so its own mesh colliders are not mistaken for player ones.
            Transform playerRoot=heldForwardReference!=null?heldForwardReference:
                rightHand.GetComponentInParent<RiggedPlayerAvatar>()?.transform;
            if(playerRoot==null) playerRoot=rightHand;
            Collider[] playerColliders=playerRoot.GetComponentsInChildren<Collider>(true);
            if(GrabMode==PickableGrabMode.Stick) ConfigureStickGrip();
            IsHeld=true;
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
            if(GrabMode==PickableGrabMode.Stick||label!=PickableObjectLabel.Irregular)
                AlignHeldContact();
            previousHeldPosition=transform.position;
            previousHeldRotation=transform.rotation;
            heldMotionSampleValid=true;
            heldLinearVelocity=Vector3.zero;
            heldAngularVelocity=Vector3.zero;
            GetComponent<TreeStructuralPart>()?.OnPickedUp();
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
        {
            Vector3 point=bounds.center;
            point[axis]=maximum?bounds.max[axis]:bounds.min[axis];
            float extreme=maximum?float.NegativeInfinity:float.PositiveInfinity;
            float bestLateral=float.PositiveInfinity;
            foreach(MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh=filter.sharedMesh;
                if(mesh==null||!mesh.isReadable) continue;
                foreach(Vector3 vertex in mesh.vertices)
                {
                    Vector3 local=transform.InverseTransformPoint(
                        filter.transform.TransformPoint(vertex));
                    float coordinate=local[axis];
                    Vector3 lateral=local-bounds.center;
                    lateral[axis]=0f;
                    float lateralDistance=lateral.sqrMagnitude;
                    bool farther=maximum?coordinate>extreme+0.0001f:
                        coordinate<extreme-0.0001f;
                    if(!farther&&Mathf.Abs(coordinate-extreme)>0.0001f) continue;
                    if(!farther&&lateralDistance>=bestLateral) continue;
                    extreme=coordinate;
                    bestLateral=lateralDistance;
                    point=local;
                }
            }
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
            float blend=1f-Mathf.Exp(-HeldDirectionAlignmentRate*Time.deltaTime);
            transform.localRotation=Quaternion.Slerp(transform.localRotation,
                Quaternion.Euler(0f,0f,90f),blend);
            // Local X position offset : object-relative Y size = -1 : 2.
            transform.localPosition=Vector3.Lerp(transform.localPosition,
                new Vector3(-measuredSize.y*0.5f,0f,0f),blend);
        }

        private void AlignHeldContact()
        {
            // Rotate around the original grip point as the held pose settles.
            transform.position+=heldHand.position-transform.TransformPoint(heldMeshContact);
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

        private static Vector3 ClosestTrianglePoint(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
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
            transform.SetParent(null,true);
            IsHeld=false;
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
            body.detectCollisions=true;
            body.useGravity=true;
            body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity=velocity;
            body.angularVelocity=Vector3.zero;
            body.WakeUp();
        }

        public void RefreshHeldCollisionGeometry()
        {
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
            // Dense fragments already have a cheap box proxy. Converting it to
            // a convex mesh during proximity checks or pickup recreates the spike.
            if(GetComponent<DestructionFragment>()!=null&&
                GetComponentInChildren<BoxCollider>(true)!=null)
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
            if(IsHeld) active=false;
            if(active) ReplaceSolidCollidersWithMeshColliders();
            if(active) UpdateDetectionTriggerSize();
            if(detectionTrigger!=null) detectionTrigger.enabled=active;
        }

        internal void RefreshDetectionGeometry() => UpdateDetectionTriggerSize();

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
        private const float AimAngleDegrees=16f;
        private const float NearbyTriggerRange=5.04f;
        private const float MaximumThrowChargeSeconds=5f;
        private const float ThrowChargeSpeedMultiplier=2.25f;
        private const float ThrowReleaseDelaySeconds=0.1f;
        private FirstPersonController player;
        private RiggedPlayerAvatar avatar;
        private PickableObject aimedObject;
        private float nextTriggerRefresh;
        private bool chargingDrop;
        private float dropStartedAt;
        private Key chargingDropKey;
        private bool attackConsumedThisFrame;
        private bool throwPending;
        private float throwReleaseAt;
        private Vector3 pendingThrowVelocity;
        [SerializeField] private float minimumThrowSpeed=8f;
        [SerializeField] private float throwSpeedPerSecond=6f;
        private readonly System.Collections.Generic.List<PickableObject> nearbyPickables=new();

        public PickableObject AimedObject => aimedObject;
        public PickableObject HeldObject => avatar!=null?avatar.HeldRightHandItem:null;
        public bool HasRightHandItem => avatar!=null&&avatar.HasRightHandItem;

        public void Initialize(FirstPersonController controller,RiggedPlayerAvatar playerAvatar)
        {
            player=controller;
            avatar=playerAvatar;
        }

        private void Update()
        {
            if(player==null||avatar==null) return;
            if(player.IsPaused||Keyboard.current==null||!Application.isFocused)
            {
                CancelDrop();
                avatar.CancelRightHandPickup();
                avatar.CancelRightHandStrike();
                return;
            }
            attackConsumedThisFrame=false;
            HandleDrop();
            if(Mouse.current!=null&&Mouse.current.rightButton.wasPressedThisFrame)
                HeldObject?.LaunchGoldenStoneCopy(player.PlayerCamera,player.transform);
            if(Time.unscaledTime>=nextTriggerRefresh)
            {
                nextTriggerRefresh=Time.unscaledTime+0.25f;
                RefreshNearbyTriggers();
            }
            bool interacting=Keyboard.current[player.Bindings[FirstPersonController.InteractBindingIndex]].isPressed;
            aimedObject=FindAimedPickable();
            bool attackHeld=Mouse.current!=null&&Mouse.current.leftButton.isPressed;
            bool attackPressed=Mouse.current!=null&&Mouse.current.leftButton.wasPressedThisFrame;
            bool attackReleased=Mouse.current!=null&&Mouse.current.leftButton.wasReleasedThisFrame;
            if(avatar.IsStrikeMode)
                avatar.UpdateRightHandStrikeInput(attackPressed,attackReleased);
            else if(!attackConsumedThisFrame&&!interacting&&attackPressed)
            {
                if(HasRightHandItem) avatar.BeginRightHandStrike();
                else avatar.BeginRightHandPunch();
            }
            bool reaching=!attackConsumedThisFrame&&!HasRightHandItem&&interacting&&attackHeld;
            if(avatar.IsReaching&&!reaching)
                avatar.CancelRightHandPickup();
            if(reaching)
            {
                if(aimedObject==null&&avatar.IsReachingForObject)
                    avatar.CancelRightHandPickup();
                if(aimedObject!=null&&!avatar.IsReachingForObject)
                    avatar.BeginRightHandPickup(aimedObject);
                if(!avatar.IsReaching)
                    avatar.BeginRightHandReach();
            }
        }

        private void CancelDrop()
        {
            chargingDrop=false;
            throwPending=false;
            pendingThrowVelocity=Vector3.zero;
            if(avatar!=null)
            {
                avatar.CancelPendingRightHandThrowAnimation();
                avatar.SetRightHandLiftCharge(0f);
            }
        }

        private void OnDisable()
        {
            CancelDrop();
            if(avatar!=null) avatar.CancelRightHandPickup();
        }

        private void OnApplicationFocus(bool focused)
        {
            if(focused) return;
            CancelDrop();
            if(avatar!=null) avatar.CancelRightHandPickup();
        }

        private void HandleDrop()
        {
            if(!HasRightHandItem) { CancelDrop(); return; }
            if(throwPending)
            {
                if(Time.time<throwReleaseAt) return;
                throwPending=false;
                avatar.ReleaseRightHandItem(pendingThrowVelocity,true);
                pendingThrowVelocity=Vector3.zero;
                avatar.SetRightHandLiftCharge(0f);
                attackConsumedThisFrame=true;
                return;
            }
            Key key=player.Bindings[FirstPersonController.DropBindingIndex];
            var button=Keyboard.current[key];
            if(chargingDrop&&key!=chargingDropKey) { CancelDrop(); return; }
            if(button.wasPressedThisFrame)
            {
                chargingDrop=true;
                chargingDropKey=key;
                dropStartedAt=Time.time;
            }
            if(!chargingDrop) return;
            float duration=Mathf.Max(0f,Time.time-dropStartedAt);
            float chargedDuration=duration*ThrowChargeSpeedMultiplier;
            float cappedDuration=Mathf.Min(chargedDuration,MaximumThrowChargeSeconds);
            avatar.SetRightHandLiftCharge(cappedDuration/MaximumThrowChargeSeconds);
            bool throwReady=chargedDuration>=1f;
            if(throwReady&&Mouse.current!=null&&Mouse.current.leftButton.wasPressedThisFrame)
            {
                float speed=minimumThrowSpeed+(cappedDuration-1f)*throwSpeedPerSecond;
                pendingThrowVelocity=player.PlayerCamera.transform.forward*speed;
                throwReleaseAt=Time.time+ThrowReleaseDelaySeconds;
                throwPending=true;
                chargingDrop=false;
                avatar.BeginRightHandThrowAnimation();
                attackConsumedThisFrame=true;
                return;
            }
            if(button.wasReleasedThisFrame)
            {
                if(!throwReady) avatar.ReleaseRightHandItem(Vector3.zero,false);
                CancelDrop();
            }
            else if(!button.isPressed) CancelDrop();
        }

        private PickableObject FindAimedPickable()
        {
            Camera camera=player.PlayerCamera;
            if(camera==null||avatar.RightPickupRange<=0f) return null;
            PickableObject best=null;
            float bestAim=float.NegativeInfinity;
            float minimumDot=Mathf.Cos(AimAngleDegrees*Mathf.Deg2Rad);
            float rangeSquared=avatar.RightPickupRange*avatar.RightPickupRange;
            foreach(PickableObject candidate in nearbyPickables)
            {
                if(candidate==null||candidate.IsHeld) continue;
                Vector3 target=candidate.AimPoint;
                if((target-avatar.RightShoulderPosition).sqrMagnitude>rangeSquared) continue;
                Vector3 direction=target-camera.transform.position;
                float distance=direction.magnitude;
                if(distance<0.01f) continue;
                float aim=Vector3.Dot(camera.transform.forward,direction/distance);
                if(aim<minimumDot||aim<=bestAim) continue;
                best=candidate;
                bestAim=aim;
            }
            return best;
        }

        private void RefreshNearbyTriggers()
        {
            float rangeSquared=NearbyTriggerRange*NearbyTriggerRange;
            nearbyPickables.Clear();
            foreach(PickableObject pickable in PickableObject.ActiveObjects)
            {
                bool nearby=!pickable.IsHeld&&
                    (pickable.AimPoint-transform.position).sqrMagnitude<=rangeSquared;
                pickable.SetNearbyDetection(nearby);
                if(nearby) nearbyPickables.Add(pickable);
            }
        }

    }
}
