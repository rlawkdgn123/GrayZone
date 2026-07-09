using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(CharacterController))]
public class PlayerMove : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CharacterController characterController;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Animator modelAnimator;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 4.5f;
    [SerializeField] private float rotationSmoothTime = 0.12f;
    [SerializeField] private float inputDeadZone = 0.01f;
    [SerializeField] private float animationSpeedChangeRate = 10f;

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
    private bool jumpedThisFrame;

#if ENABLE_INPUT_SYSTEM
    private Vector2 playerInputMove;
#endif

    private int speedAnimationId;
    private int groundedAnimationId;
    private int jumpAnimationId;
    private int freeFallAnimationId;
    private int motionSpeedAnimationId;

    public Vector2 MoveInput { get; private set; }
    public Vector3 CurrentMoveDirection => currentMoveDirection;
    public bool IsGrounded => characterController != null && characterController.isGrounded;
    public bool HasMoveInput => MoveInput.sqrMagnitude > inputDeadZone * inputDeadZone;
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
            MoveCharacter(Vector3.zero);
            UpdateAnimator();
            return;
        }

        MoveInput = ReadMoveInput();
        currentMoveDirection = GetCameraRelativeMoveDirection(MoveInput);

        RotateOnlyWhileMoving(currentMoveDirection);
        ApplyJump();
        ApplyGravity();
        MoveCharacter(currentMoveDirection);
        UpdateAnimator();
    }

    public void SetMoveLocked(bool locked)
    {
        if (m_moveLocked == locked)
            return;

        m_moveLocked = locked;

        if (m_moveLocked)
            ClearMoveState();
    }

#if ENABLE_INPUT_SYSTEM
    public void OnMove(InputValue value)
    {
        playerInputMove = value.Get<Vector2>();
    }

    private void OnDisable()
    {
        playerInputMove = Vector2.zero;
        MoveInput = Vector2.zero;
        currentMoveDirection = Vector3.zero;
        animationBlend = 0f;
        UpdateAnimator();
    }
#endif

    private void CacheReferences()
    {
        if (characterController == null)
            characterController = GetComponent<CharacterController>();

        if (modelAnimator == null)
            modelAnimator = GetComponentInChildren<Animator>();

        if (modelAnimator != null)
            modelAnimator.applyRootMotion = false;

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    private void ClearMoveState()
    {
        MoveInput = Vector2.zero;
        currentMoveDirection = Vector3.zero;
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

    private void MoveCharacter(Vector3 moveDirection)
    {
        Vector3 horizontalMove = moveDirection * moveSpeed;
        Vector3 verticalMove = Vector3.up * verticalVelocity;

        characterController.Move((horizontalMove + verticalMove) * Time.deltaTime);
    }

    private void UpdateAnimator()
    {
        if (modelAnimator == null) return;

        float targetSpeed = HasMoveInput ? moveSpeed : 0f;
        animationBlend = Mathf.Lerp(animationBlend, targetSpeed, Time.deltaTime * animationSpeedChangeRate);

        if (animationBlend < 0.01f)
            animationBlend = 0f;

        float motionSpeed = HasMoveInput ? MoveInput.magnitude : 0f;
        bool isGrounded = IsGrounded;

        modelAnimator.SetFloat(speedAnimationId, animationBlend);
        modelAnimator.SetFloat(motionSpeedAnimationId, motionSpeed);
        modelAnimator.SetBool(groundedAnimationId, isGrounded);
        modelAnimator.SetBool(jumpAnimationId, jumpedThisFrame);
        modelAnimator.SetBool(freeFallAnimationId, !isGrounded && verticalVelocity < groundedStickVelocity);
    }
}
