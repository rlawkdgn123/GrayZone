using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 셸터 플레이어의 카메라 기준 이동, 점프, 중력, 이동 애니메이션 값을 처리
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerMove : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CharacterController characterController;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Animator modelAnimator;

    private ShelterPlayerFootstepEmitter footstepEmitter;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 4.5f;
    [SerializeField] private float sprintSpeed = 6f;
    [SerializeField] private float rotationSmoothTime = 0.12f;
    [SerializeField] private float inputDeadZone = 0.01f;
    [SerializeField] private float animationSpeedChangeRate = 10f;

    [Header("Air Movement")]
    [SerializeField] private float groundAcceleration = 30f;
    [SerializeField] private float groundDeceleration = 35f;
    [Range(0f, 1f)]
    [SerializeField] private float airControl = 0.3f;
    [SerializeField] private float airDrag = 0.5f;

    [Header("Gravity")]
    [SerializeField] private float gravity = -20f;
    [SerializeField] private float groundedStickVelocity = -2f;

    [Header("Jump")]
    [SerializeField] private float jumpHeight = 1.2f;

    [Header("Interactor")]
    [SerializeField] private PlayerInteractor playerInteractor;

    [Header("Control Lock")]
    [SerializeField] private bool m_moveLocked;

    private float verticalVelocity;
    private float rotationVelocity;
    private float animationBlend;
    private Vector3 currentMoveDirection;
    private Vector3 horizontalVelocity;
    private bool jumpedThisFrame;
    private bool jumpAnimationActive;
    private bool groundStateInitialized;
    private bool wasGrounded;

#if ENABLE_INPUT_SYSTEM
    private Vector2 playerInputMove;
#endif

    private int speedAnimationId;
    private int groundedAnimationId;
    private int jumpAnimationId;
    private int freeFallAnimationId;
    private int motionSpeedAnimationId;

    /// <summary>현재 프레임에 적용되는 이동 입력값</summary>
    public Vector2 MoveInput { get; private set; }

    /// <summary>월드 기준 현재 이동 방향</summary>
    public Vector3 CurrentMoveDirection => currentMoveDirection;

    /// <summary>캐릭터 컨트롤러가 지면에 닿아 있는지 여부</summary>
    public bool IsGrounded => characterController != null && characterController.isGrounded;

    /// <summary>데드존을 넘는 이동 입력이 있는지 여부</summary>
    public bool HasMoveInput => MoveInput.sqrMagnitude > inputDeadZone * inputDeadZone;

    /// <summary>이동 입력이 잠겨 있는지 여부</summary>
    public bool IsMoveLocked => m_moveLocked;

    private void Reset()
    {
        characterController = GetComponent<CharacterController>();
        modelAnimator = GetComponentInChildren<Animator>();

        if (Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    private void Awake()
    {
        CacheReferences();
        AssignAnimationIds();
    }

    private void Update()
    {
        if (characterController == null) return;

        if (m_moveLocked)
        {
            ClearMoveState();
            ApplyGravity();
            MoveCharacter();
            UpdateLandingState();
            UpdateAnimator();
            return;
        }

        MoveInput = ReadMoveInput();
        currentMoveDirection = GetCameraRelativeMoveDirection(MoveInput);

        UpdateHorizontalVelocity(currentMoveDirection);
        RotateOnlyWhileMoving(horizontalVelocity);
        ApplyJump();
        ApplyGravity();
        MoveCharacter();
        UpdateLandingState();
        UpdateAnimator();
    }

    /// <summary>
    /// UI 열림 등으로 플레이어 이동 입력 잠금 상태를 변경
    /// </summary>
    /// <param name="locked">이동 입력을 잠그려면 <c>true</c></param>
    public void SetMoveLocked(bool locked)
    {
        if (m_moveLocked == locked)
            return;

        m_moveLocked = locked;

        if (m_moveLocked)
            ClearMoveState();
    }

#if ENABLE_INPUT_SYSTEM
    /// <summary>
    /// Unity Input System의 이동 입력 콜백
    /// </summary>
    /// <param name="value">입력 시스템에서 전달한 이동 입력값</param>
    public void OnMove(InputValue value)
    {
        playerInputMove = value.Get<Vector2>();
    }

    private void OnDisable()
    {
        playerInputMove = Vector2.zero;
        MoveInput = Vector2.zero;
        currentMoveDirection = Vector3.zero;
        horizontalVelocity = Vector3.zero;
        animationBlend = 0f;
        jumpAnimationActive = false;
        UpdateAnimator();
    }
#endif

    private void CacheReferences()
    {
        if (characterController == null)
            characterController = GetComponent<CharacterController>();

        if (modelAnimator == null)
            modelAnimator = GetComponentInChildren<Animator>();

        footstepEmitter = GetComponent<ShelterPlayerFootstepEmitter>();

        if (modelAnimator != null)
        {
            modelAnimator.applyRootMotion = false;

            PlayerAnimationEventReceiver receiver = modelAnimator.GetComponent<PlayerAnimationEventReceiver>();
            if (receiver == null)
                receiver = modelAnimator.gameObject.AddComponent<PlayerAnimationEventReceiver>();

            receiver.SetFootstepEmitter(footstepEmitter);
        }

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    private void ClearMoveState()
    {
        MoveInput = Vector2.zero;
        currentMoveDirection = Vector3.zero;
        horizontalVelocity = Vector3.zero;
        rotationVelocity = 0f;
        animationBlend = 0f;
        jumpedThisFrame = false;
        UpdateAnimator();
    }

    private void AssignAnimationIds()
    {
        speedAnimationId = Animator.StringToHash("Speed");
        groundedAnimationId = Animator.StringToHash("Grounded");
        jumpAnimationId = Animator.StringToHash("Jump");
        freeFallAnimationId = Animator.StringToHash("FreeFall");
        motionSpeedAnimationId = Animator.StringToHash("MotionSpeed");
    }

    private Vector2 ReadMoveInput()
    {
        Vector2 input = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
        input = playerInputMove;

        if (Keyboard.current != null)
        {
            Vector2 keyboardInput = Vector2.zero;

            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed)
                keyboardInput.x -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed)
                keyboardInput.x += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed)
                keyboardInput.y -= 1f;
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed)
                keyboardInput.y += 1f;

            if (keyboardInput.sqrMagnitude > input.sqrMagnitude)
                input = keyboardInput;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (input.sqrMagnitude <= inputDeadZone * inputDeadZone)
            input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif

        return Vector2.ClampMagnitude(input, 1f);
    }

    private Vector3 GetCameraRelativeMoveDirection(Vector2 input)
    {
        if (input.sqrMagnitude <= inputDeadZone * inputDeadZone)
            return Vector3.zero;

        Transform reference = cameraTransform;

        if (reference == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
            reference = cameraTransform;
        }

        Vector3 forward = reference != null ? reference.forward : transform.forward;
        Vector3 right = reference != null ? reference.right : transform.right;

        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();

        Vector3 direction = forward * input.y + right * input.x;

        if (direction.sqrMagnitude > 1f)
            direction.Normalize();

        return direction;
    }

    private bool IsSprintPressed()
    {
        if (!IsGrounded)
            return false;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed)
            return true;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKey(KeyCode.LeftShift))
            return true;
#endif

        return false;
    }

    private void UpdateHorizontalVelocity(Vector3 inputDirection)
    {
        bool hasInput = inputDirection.sqrMagnitude > inputDeadZone * inputDeadZone;

        if (IsGrounded)
        {
            float targetSpeed = hasInput
                ? (IsSprintPressed() ? sprintSpeed : moveSpeed)
                : 0f;
            Vector3 targetVelocity = inputDirection * targetSpeed;
            float acceleration = targetVelocity.sqrMagnitude > horizontalVelocity.sqrMagnitude
                ? groundAcceleration
                : groundDeceleration;

            horizontalVelocity = Vector3.MoveTowards(
                horizontalVelocity,
                targetVelocity,
                acceleration * Time.deltaTime
            );
            return;
        }

        if (hasInput && horizontalVelocity.sqrMagnitude > 0.0001f)
        {
            float preservedSpeed = horizontalVelocity.magnitude;
            Vector3 targetVelocity = inputDirection * preservedSpeed;

            horizontalVelocity = Vector3.MoveTowards(
                horizontalVelocity,
                targetVelocity,
                groundAcceleration * airControl * Time.deltaTime
            );
        }

        horizontalVelocity = Vector3.MoveTowards(
            horizontalVelocity,
            Vector3.zero,
            airDrag * Time.deltaTime
        );
    }

    private void RotateOnlyWhileMoving(Vector3 moveDirection)
    {
        if (moveDirection.sqrMagnitude <= inputDeadZone * inputDeadZone)
            return;

        float targetAngle = Mathf.Atan2(moveDirection.x, moveDirection.z) * Mathf.Rad2Deg;
        float smoothAngle = Mathf.SmoothDampAngle(
            transform.eulerAngles.y,
            targetAngle,
            ref rotationVelocity,
            rotationSmoothTime
        );

        transform.rotation = Quaternion.Euler(0f, smoothAngle, 0f);
    }

    private void ApplyGravity()
    {
        if (characterController.isGrounded && verticalVelocity < 0f)
            verticalVelocity = groundedStickVelocity;

        verticalVelocity += gravity * Time.deltaTime;
    }

    private void ApplyJump()
    {
        jumpedThisFrame = false;

        if (!characterController.isGrounded)
            return;

        if (!WasJumpPressed())
            return;

        verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        jumpedThisFrame = true;
        footstepEmitter?.PlayJump();
    }

    private bool WasJumpPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            return true;

        if (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame)
            return true;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetButtonDown("Jump") || Input.GetKeyDown(KeyCode.Space))
            return true;
#endif

        return false;
    }

    private void MoveCharacter()
    {
        Vector3 verticalMove = Vector3.up * verticalVelocity;

        characterController.Move((horizontalVelocity + verticalMove) * Time.deltaTime);
    }

    /// <summary>CharacterController 이동 결과를 기준으로 공중에서 지면으로 바뀐 순간을 한 번만 알립니다.</summary>
    private void UpdateLandingState()
    {
        bool isGrounded = IsGrounded;

        // CharacterController의 접지 값은 첫 Move 이후에 유효해지므로 시작 프레임의 가짜 착지음을 막습니다.
        if (!groundStateInitialized)
        {
            wasGrounded = isGrounded;
            groundStateInitialized = true;
            return;
        }

        if (isGrounded && !wasGrounded)
            footstepEmitter?.PlayLand();

        wasGrounded = isGrounded;
    }

    private void UpdateAnimator()
    {
        if (modelAnimator == null) return;

        float targetSpeed = horizontalVelocity.magnitude;
        animationBlend = Mathf.Lerp(animationBlend, targetSpeed, Time.deltaTime * animationSpeedChangeRate);

        if (animationBlend < 0.01f)
            animationBlend = 0f;

        // MotionSpeed는 Idle Walk Run Blend 상태의 재생 배율이므로 0이 되면 상태와 전환이 멈춘다
        float motionSpeed = HasMoveInput ? Mathf.Clamp01(MoveInput.magnitude) : 1f;
        bool isGrounded = IsGrounded;

        // Jump는 점프 시점부터 착지할 때까지 유지해 Animator가 전환 신호를 놓치지 않게 한다
        if (jumpedThisFrame)
            jumpAnimationActive = true;
        else if (isGrounded)
            jumpAnimationActive = false;

        modelAnimator.SetFloat(speedAnimationId, animationBlend);
        modelAnimator.SetFloat(motionSpeedAnimationId, motionSpeed);
        modelAnimator.SetBool(groundedAnimationId, isGrounded);
        modelAnimator.SetBool(jumpAnimationId, jumpAnimationActive);
        modelAnimator.SetBool(freeFallAnimationId, !isGrounded && verticalVelocity < groundedStickVelocity);
    }
}
