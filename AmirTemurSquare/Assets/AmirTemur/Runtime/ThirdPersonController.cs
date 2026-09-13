using UnityEngine;
using UnityEngine.InputSystem;

namespace AmirTemur
{
    /// <summary>
    /// RDR-style third-person locomotion: camera-relative steering, walk / jog / sprint with damped speed,
    /// weight in the turn rate, root-motion driven movement (with a kinematic fallback), gravity and jump
    /// through a CharacterController. Reads the project's Input System actions ("Player" map).
    /// Animator parameters: Speed (m/s), Turn (-1..1), Grounded, Falling, Jump (trigger).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public class ThirdPersonController : MonoBehaviour
    {
        [Header("Input")]
        [Tooltip("InputSystem_Actions asset with a 'Player' map containing Move, Look, Jump and Sprint. If empty, InputSystem.actions (project-wide actions) is used.")]
        [SerializeField] InputActionAsset inputActions;
        [SerializeField] string actionMapName = "Player";

        [Header("Speeds (m/s). Must match the clip root-motion speeds used as blend-tree thresholds.")]
        [SerializeField] float walkSpeed = 1.5f;
        [SerializeField] float jogSpeed = 3.6f;
        [SerializeField] float sprintSpeed = 6.0f;
        [Tooltip("Stick magnitude below this walks, above jogs.")]
        [SerializeField, Range(0.1f, 0.95f)] float walkStickThreshold = 0.6f;
        [SerializeField] float speedDampTime = 0.12f;
        [SerializeField] float turnDampTime = 0.15f;

        [Header("Turning (deg/s)")]
        [Tooltip("Turn rate when standing still.")]
        [SerializeField] float turnRateIdle = 360f;
        [Tooltip("Turn rate at full sprint (lower = heavier).")]
        [SerializeField] float turnRateSprint = 140f;
        [Tooltip("Fraction of the turn rate available while airborne.")]
        [SerializeField, Range(0f, 1f)] float airControl = 0.2f;

        [Header("Physics")]
        [SerializeField] float gravity = 19.62f;
        [SerializeField] float jumpImpulse = 5.5f;
        [SerializeField] float terminalVelocity = 30f;
        [SerializeField] float groundCheckDistance = 0.12f;
        [SerializeField] LayerMask groundMask = ~0;
        [Tooltip("Seconds airborne (and descending) before the Falling animation is requested.")]
        [SerializeField] float fallingDelay = 0.25f;

        [Header("Root motion")]
        [Tooltip("Ignore animator root motion and move kinematically by Speed * forward.")]
        [SerializeField] bool forceKinematicMovement = false;
        [Tooltip("If root motion delivers less than this fraction of the requested speed for a while, fall back to kinematic movement.")]
        [SerializeField, Range(0f, 1f)] float rootMotionSanityFraction = 0.15f;

        [Header("Cursor")]
        [SerializeField] bool lockCursorOnStart = true;

        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int TurnHash = Animator.StringToHash("Turn");
        static readonly int GroundedHash = Animator.StringToHash("Grounded");
        static readonly int JumpHash = Animator.StringToHash("Jump");
        static readonly int FallingHash = Animator.StringToHash("Falling");

        CharacterController cc;
        Animator animator;
        Transform cameraTransform;

        InputActionMap actionMap;
        InputAction moveAction, sprintAction, jumpAction;

        Vector2 moveInput;
        bool sprintHeld, jumpPressed;
        Vector3 desiredDirection;
        bool hasMoveInput;

        float targetSpeed, currentSpeed, speedVelocity;
        float turnTarget, turnValue, turnVelocity;
        float verticalVelocity;
        bool grounded = true;
        bool falling;
        float airTime;
        Vector3 groundedPlanarVelocity;   // last planar velocity while grounded; carried through the air
        bool useRootMotion = true;
        float rootMotionStarvedTime;
        bool cursorLocked;

        // ------------------------------------------------------------------ public read-outs (HUD etc.)
        public float CurrentSpeed => currentSpeed;
        public float TargetSpeed => targetSpeed;
        public bool IsGrounded => grounded;
        public bool IsFalling => falling;
        public bool IsSprinting => sprintHeld && hasMoveInput;
        public bool UsingRootMotion => useRootMotion && !forceKinematicMovement;
        public bool CursorLocked => cursorLocked;
        public float WalkSpeed => walkSpeed;
        public float JogSpeed => jogSpeed;
        public float SprintSpeed => sprintSpeed;

        /// <summary>Called by the editor setup so the blend-tree thresholds and controller speeds agree.</summary>
        public void SetSpeeds(float walk, float jog, float sprint)
        {
            walkSpeed = walk; jogSpeed = jog; sprintSpeed = sprint;
        }

        public void SetInputActions(InputActionAsset asset) => inputActions = asset;

        // ------------------------------------------------------------------ lifecycle

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            animator = GetComponent<Animator>();
            if (animator != null) animator.applyRootMotion = true;
        }

        void OnEnable()
        {
            ResolveActions();
            actionMap?.Enable();
        }

        void OnDisable()
        {
            actionMap?.Disable();
        }

        void Start()
        {
            if (lockCursorOnStart) SetCursorLocked(true);
            RefreshCamera();
        }

        void ResolveActions()
        {
            InputActionAsset asset = inputActions != null ? inputActions : InputSystem.actions;
            if (asset == null)
            {
                Debug.LogWarning("[ThirdPersonController] No InputActionAsset assigned and no project-wide actions found; input disabled.", this);
                return;
            }
            actionMap = asset.FindActionMap(actionMapName, false);
            if (actionMap == null)
            {
                Debug.LogWarning("[ThirdPersonController] Action map '" + actionMapName + "' not found in " + asset.name, this);
                return;
            }
            moveAction = actionMap.FindAction("Move", false);
            sprintAction = actionMap.FindAction("Sprint", false);
            jumpAction = actionMap.FindAction("Jump", false);
        }

        void RefreshCamera()
        {
            Camera main = Camera.main;
            if (main == null) main = FindFirstObjectByType<Camera>();
            cameraTransform = main != null ? main.transform : null;
        }

        // ------------------------------------------------------------------ per-frame logic

        void Update()
        {
            ReadInput();
            HandleCursor();

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (cameraTransform == null) RefreshCamera();

            // Desired planar direction relative to the camera yaw.
            Vector3 camForward = Vector3.forward, camRight = Vector3.right;
            if (cameraTransform != null)
            {
                camForward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
                if (camForward.sqrMagnitude < 1e-4f) camForward = Vector3.ProjectOnPlane(cameraTransform.up, Vector3.up);
                camForward.Normalize();
                camRight = Vector3.Cross(Vector3.up, camForward);
            }

            float stick = Mathf.Clamp01(moveInput.magnitude);
            hasMoveInput = stick > 0.05f;
            if (hasMoveInput)
            {
                desiredDirection = (camForward * moveInput.y + camRight * moveInput.x).normalized;
            }

            // Target speed tier: walk / jog / sprint.
            if (!hasMoveInput) targetSpeed = 0f;
            else if (sprintHeld) targetSpeed = sprintSpeed;
            else if (stick < walkStickThreshold) targetSpeed = walkSpeed;
            else targetSpeed = jogSpeed;

            currentSpeed = Mathf.SmoothDamp(currentSpeed, targetSpeed, ref speedVelocity, speedDampTime, Mathf.Infinity, dt);
            if (currentSpeed < 0.01f && targetSpeed <= 0f) currentSpeed = 0f;

            // Steering: rotate toward the desired direction with a speed-dependent rate limit (weight).
            turnTarget = 0f;
            if (hasMoveInput)
            {
                float angle = Vector3.SignedAngle(transform.forward, desiredDirection, Vector3.up);
                float weight = Mathf.Clamp01(currentSpeed / Mathf.Max(0.01f, sprintSpeed));
                float maxRate = Mathf.Lerp(turnRateIdle, turnRateSprint, weight);
                if (!grounded) maxRate *= airControl;
                float step = Mathf.Clamp(angle, -maxRate * dt, maxRate * dt);
                transform.Rotate(0f, step, 0f, Space.World);
                // Lean amount: how hard we are steering, -1 (left) .. 1 (right).
                turnTarget = Mathf.Clamp(angle / 60f, -1f, 1f);
            }
            turnValue = Mathf.SmoothDamp(turnValue, turnTarget, ref turnVelocity, turnDampTime, Mathf.Infinity, dt);

            // Jump.
            if (jumpPressed && grounded)
            {
                verticalVelocity = jumpImpulse;
                grounded = false;
                falling = false;
                airTime = 0f;
                if (animator != null) animator.SetTrigger(JumpHash);
            }
            jumpPressed = false;

            if (animator != null)
            {
                animator.SetFloat(SpeedHash, currentSpeed);
                animator.SetFloat(TurnHash, turnValue);
                animator.SetBool(GroundedHash, grounded);
                animator.SetBool(FallingHash, falling);
            }
        }

        void ReadInput()
        {
            moveInput = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
            if (moveInput.sqrMagnitude > 1f) moveInput.Normalize();
            sprintHeld = sprintAction != null && sprintAction.IsPressed();
            if (jumpAction != null && jumpAction.WasPressedThisFrame()) jumpPressed = true;
        }

        void HandleCursor()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) SetCursorLocked(!cursorLocked);
            Mouse mouse = Mouse.current;
            if (!cursorLocked && lockCursorOnStart && mouse != null && mouse.leftButton.wasPressedThisFrame && Application.isFocused)
                SetCursorLocked(true);
        }

        public void SetCursorLocked(bool locked)
        {
            cursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        /// <summary>Applies root motion + gravity + jump through the CharacterController. Unity calls this after the
        /// animator has evaluated (because it exists, the animator does not move the transform itself).</summary>
        void OnAnimatorMove()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || cc == null || !cc.enabled) return;

            Vector3 planar;
            if (grounded)
            {
                Vector3 rootDelta = animator != null ? animator.deltaPosition : Vector3.zero;
                rootDelta.y = 0f;
                bool rootMotionOk = useRootMotion && !forceKinematicMovement && animator != null && animator.hasRootMotion;
                if (rootMotionOk)
                {
                    planar = rootDelta;
                    // Sanity: if the clips deliver far less than the requested speed for a while, switch to kinematic.
                    if (currentSpeed > 0.5f && rootDelta.magnitude / dt < currentSpeed * rootMotionSanityFraction)
                    {
                        rootMotionStarvedTime += dt;
                        if (rootMotionStarvedTime > 0.75f)
                        {
                            useRootMotion = false;
                            Debug.LogWarning("[ThirdPersonController] Root motion is not moving the character; switching to kinematic movement.", this);
                        }
                    }
                    else rootMotionStarvedTime = 0f;
                }
                else
                {
                    planar = transform.forward * (currentSpeed * dt);
                }
                groundedPlanarVelocity = planar / dt;
            }
            else
            {
                // Airborne: carry the take-off velocity, with a little steering.
                Vector3 wanted = hasMoveInput ? desiredDirection * Mathf.Max(currentSpeed, walkSpeed) : Vector3.zero;
                groundedPlanarVelocity = Vector3.MoveTowards(groundedPlanarVelocity, wanted, airControl * 4f * dt);
                planar = groundedPlanarVelocity * dt;
            }

            // Gravity.
            if (grounded && verticalVelocity < 0f) verticalVelocity = -2f;      // keep pressed to the ground on slopes / steps
            else verticalVelocity = Mathf.Max(verticalVelocity - gravity * dt, -terminalVelocity);

            cc.Move(planar + Vector3.up * (verticalVelocity * dt));

            // Ground state after the move.
            bool wasGrounded = grounded;
            if (verticalVelocity > 0.1f) grounded = false;
            else grounded = cc.isGrounded || ProbeGround();

            if (grounded)
            {
                airTime = 0f;
                falling = false;
                if (!wasGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            }
            else
            {
                airTime += dt;
                falling = verticalVelocity < 0f && airTime > fallingDelay;
            }
        }

        bool ProbeGround()
        {
            float radius = Mathf.Max(0.05f, cc.radius * 0.9f);
            Vector3 origin = transform.position + Vector3.up * (radius + 0.05f);
            return Physics.SphereCast(origin, radius, Vector3.down, out _, groundCheckDistance + 0.05f, groundMask, QueryTriggerInteraction.Ignore);
        }

        void OnDrawGizmosSelected()
        {
            if (cc == null) cc = GetComponent<CharacterController>();
            if (cc == null) return;
            Gizmos.color = grounded ? Color.green : Color.red;
            float radius = Mathf.Max(0.05f, cc.radius * 0.9f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * (radius + 0.05f) + Vector3.down * (groundCheckDistance + 0.05f), radius);
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(transform.position + Vector3.up, desiredDirection * 1.5f);
        }
    }
}
