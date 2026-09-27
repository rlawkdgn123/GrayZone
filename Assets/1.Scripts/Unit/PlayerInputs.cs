using UnityEngine;
using UnityEngine.Serialization;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 플레이어 입력 상태를 보관하고 Input System 이벤트를 런타임 입력 값으로 변환하는 컴포넌트입니다.
/// </summary>
/// <remarks>
/// 이 클래스는 입력 액션 콜백에서 받은 값을 내부 필드에 캐싱하고,
/// 이동/시점/점프/전력질주/조준/공격/재장전 상태를 다른 시스템에서 읽을 수 있게 제공합니다.
/// <para>
/// 변수 컨벤션은 <c>m_</c> 접두사를 사용하는 private serialized field를 기준으로 하며,
/// 기존 Starter Assets 스타일의 <c>move</c>, <c>look</c>, <c>jump</c> 접근도 호환용 프로퍼티로 유지합니다.
/// </para>
/// </remarks>
public class PlayerInputs : MonoBehaviour
{
    [Header("Character Input Values")]
    [Tooltip("현재 이동 입력값입니다. x는 좌우, y는 전후 입력을 의미합니다.")]
    [FormerlySerializedAs("move")]
    [SerializeField] private Vector2 m_move;

    [Tooltip("현재 시점 입력값입니다. x는 좌우 회전, y는 상하 회전을 의미합니다.")]
    [FormerlySerializedAs("look")]
    [SerializeField] private Vector2 m_look;

    [Tooltip("점프 입력이 눌린 상태인지 여부입니다.")]
    [FormerlySerializedAs("jump")]
    [SerializeField] private bool m_jump;

    [Tooltip("전력질주 입력이 눌린 상태인지 여부입니다.")]
    [FormerlySerializedAs("sprint")]
    [SerializeField] private bool m_sprint;

    [Tooltip("조준 입력이 눌린 상태인지 여부입니다.")]
    [FormerlySerializedAs("aim")]
    [SerializeField] private bool m_aim;

    [Tooltip("발사 입력이 눌린 상태인지 여부입니다.")]
    [FormerlySerializedAs("shoot")]
    [SerializeField] private bool m_shoot;

    [Tooltip("재장전 입력이 눌린 상태인지 여부입니다.")]
    [FormerlySerializedAs("reload")]
    [SerializeField] private bool m_reload;

    [Tooltip("상호작용 입력이 눌린 상태(홀드 포함)인지 여부입니다.")]
    [SerializeField] private bool m_interact;

    // UI 커서 모드에서는 Input System 콜백이 게임플레이 상태를 다시 채우지 않도록 막습니다.
    private bool m_isInputEnabled = true;

#if ENABLE_INPUT_SYSTEM
    private PlayerInput m_playerInput;
    private InputAction m_interactionAction;
#endif

    [Header("Movement Settings")]
    [Tooltip("아날로그 이동 입력을 사용할지 여부입니다. true이면 입력 세기 magnitude를 이동 속도에 반영합니다.")]
    [FormerlySerializedAs("analogMovement")]
    [SerializeField] private bool m_analogMovement;

    [Header("Mouse Cursor Settings")]
    [Tooltip("애플리케이션 포커스 시 커서를 화면 중앙에 잠글지 여부입니다.")]
    [FormerlySerializedAs("cursorLocked")]
    [SerializeField] private bool m_cursorLocked = true;

    [Tooltip("마우스 커서 입력을 시점 회전에 사용할지 여부입니다.")]
    [FormerlySerializedAs("cursorInputForLook")]
    [SerializeField] private bool m_cursorInputForLook = true;

    /// <summary>현재 이동 입력값입니다.</summary>
    public Vector2 Move => m_move;

    /// <summary>현재 시점 입력값입니다.</summary>
    public Vector2 Look => m_look;

    /// <summary>점프 입력 상태입니다.</summary>
    public bool Jump => m_jump;

    /// <summary>전력질주 입력 상태입니다.</summary>
    public bool Sprint => m_sprint;

    /// <summary>조준 입력 상태입니다.</summary>
    public bool Aim => m_aim;

    /// <summary>발사 입력 상태입니다.</summary>
    public bool Shoot => m_shoot;

    /// <summary>재장전 입력 상태입니다.</summary>
    public bool Reload => m_reload;

    /// <summary>상호작용 입력이 눌린 상태(홀드 포함)입니다. 탭/홀드 판정은 소비 측(InteractionController)에서 처리합니다.</summary>
    public bool Interact
    {
        get
        {
            if (!m_isInputEnabled)
            {
                return false;
            }

            RefreshInteractionInputFromAction();
            return m_interact;
        }
    }

    /// <summary>아날로그 이동 입력 사용 여부입니다.</summary>
    public bool AnalogMovement => m_analogMovement;

    /// <summary>커서 잠금 사용 여부입니다.</summary>
    public bool CursorLocked => m_cursorLocked;

    /// <summary>커서 입력을 시점 회전에 사용할지 여부입니다.</summary>
    public bool CursorInputForLook => m_cursorInputForLook;

    /// <summary>
    /// 기존 Starter Assets 코드와의 호환을 위한 이동 입력 프로퍼티입니다.
    /// </summary>
    public Vector2 move
    {
        get => m_move;
        set => m_move = value;
    }

    /// <summary>
    /// 기존 Starter Assets 코드와의 호환을 위한 시점 입력 프로퍼티입니다.
    /// </summary>
    public Vector2 look
    {
        get => m_look;
        set => m_look = value;
    }

    /// <summary>
    /// 기존 Starter Assets 코드와의 호환을 위한 점프 입력 프로퍼티입니다.
    /// </summary>
    public bool jump
    {
        get => m_jump;
        set => m_jump = value;
    }

    /// <summary>
    /// 기존 Starter Assets 코드와의 호환을 위한 전력질주 입력 프로퍼티입니다.
    /// </summary>
    public bool sprint
    {
        get => m_sprint;
        set => m_sprint = value;
    }

    /// <summary>
    /// 기존 Starter Assets 코드와의 호환을 위한 조준 입력 프로퍼티입니다.
    /// </summary>
    public bool aim
    {
        get => m_aim;
        set => m_aim = value;
    }

    /// <summary>
    /// 기존 Starter Assets 코드와의 호환을 위한 발사 입력 프로퍼티입니다.
    /// </summary>
    public bool shoot
    {
        get => m_shoot;
        set => m_shoot = value;
    }

    /// <summary>
    /// 기존 Starter Assets 코드와의 호환을 위한 재장전 입력 프로퍼티입니다.
    /// </summary>
    public bool reload
    {
        get => m_reload;
        set => m_reload = value;
    }

    /// <summary>
    /// 기존 Starter Assets 코드와의 호환을 위한 아날로그 이동 설정 프로퍼티입니다.
    /// </summary>
    public bool analogMovement
    {
        get => m_analogMovement;
        set => m_analogMovement = value;
    }

    /// <summary>
    /// 기존 Starter Assets 코드와의 호환을 위한 커서 잠금 설정 프로퍼티입니다.
    /// </summary>
    public bool cursorLocked
    {
        get => m_cursorLocked;
        set => m_cursorLocked = value;
    }

    /// <summary>
    /// 기존 Starter Assets 코드와의 호환을 위한 시점 입력 허용 설정 프로퍼티입니다.
    /// </summary>
    public bool cursorInputForLook
    {
        get => m_cursorInputForLook;
        set => m_cursorInputForLook = value;
    }

#if ENABLE_INPUT_SYSTEM
    private void Awake()
    {
        CachePlayerInput();
    }

    /// <summary>
    /// 이동 입력 액션 콜백입니다.
    /// </summary>
    /// <param name="value">Input System에서 전달된 이동 입력값입니다.</param>
    public void OnMove(InputValue value)
    {
        if (!m_isInputEnabled)
        {
            return;
        }

        MoveInput(value.Get<Vector2>());
    }

    /// <summary>
    /// 시점 입력 액션 콜백입니다.
    /// </summary>
    /// <param name="value">Input System에서 전달된 시점 입력값입니다.</param>
    public void OnLook(InputValue value)
    {
        if (m_isInputEnabled && m_cursorInputForLook)
        {
            LookInput(value.Get<Vector2>());
        }
    }

    /// <summary>
    /// 점프 입력 액션 콜백입니다.
    /// </summary>
    /// <param name="value">Input System에서 전달된 점프 입력 상태입니다.</param>
    public void OnJump(InputValue value)
    {
        if (!m_isInputEnabled)
        {
            return;
        }

        JumpInput(value.isPressed);
    }

    /// <summary>
    /// 전력질주 입력 액션 콜백입니다.
    /// </summary>
    /// <param name="value">Input System에서 전달된 전력질주 입력 상태입니다.</param>
    public void OnSprint(InputValue value)
    {
        if (!m_isInputEnabled)
        {
            return;
        }

        SprintInput(value.isPressed);
    }

    /// <summary>
    /// 조준 입력 액션 콜백입니다.
    /// </summary>
    /// <param name="value">Input System에서 전달된 조준 입력 상태입니다.</param>
    public void OnAim(InputValue value)
    {
        if (!m_isInputEnabled)
        {
            return;
        }

        AimInput(value.isPressed);
    }

    /// <summary>
    /// 발사 입력 액션 콜백입니다.
    /// </summary>
    /// <param name="value">Input System에서 전달된 발사 입력 상태입니다.</param>
    public void OnShoot(InputValue value)
    {
        if (!m_isInputEnabled)
        {
            return;
        }

        ShootInput(value.isPressed);
    }

    /// <summary>
    /// 재장전 입력 액션 콜백입니다.
    /// </summary>
    /// <param name="value">Input System에서 전달된 재장전 입력 상태입니다.</param>
    public void OnReload(InputValue value)
    {
        if (!m_isInputEnabled)
        {
            return;
        }

        ReloadInput(value.isPressed);
    }

    /// <summary>
    /// 상호작용(Interaction) 입력 액션 콜백입니다.
    /// </summary>
    /// <param name="value">Input System에서 전달된 상호작용 입력 상태입니다.</param>
    /// <remarks>버튼 액션이라 누름/뗌 모두 호출되며, <c>isPressed</c>로 홀드 상태를 그대로 보관합니다.</remarks>
    public void OnInteraction(InputValue value)
    {
        if (!m_isInputEnabled)
        {
            return;
        }

        InteractInput(value.isPressed);
    }
#endif

    /// <summary>
    /// 이동 입력값을 갱신합니다.
    /// </summary>
    /// <param name="newMoveDirection">새 이동 입력 방향입니다.</param>
    public void MoveInput(Vector2 newMoveDirection)
    {
        m_move = newMoveDirection;
    }

    /// <summary>
    /// 시점 입력값을 갱신합니다.
    /// </summary>
    /// <param name="newLookDirection">새 시점 입력 방향입니다.</param>
    public void LookInput(Vector2 newLookDirection)
    {
        m_look = newLookDirection;
    }

    /// <summary>
    /// 점프 입력 상태를 갱신합니다.
    /// </summary>
    /// <param name="newJumpState">새 점프 입력 상태입니다.</param>
    public void JumpInput(bool newJumpState)
    {
        m_jump = newJumpState;
    }

    /// <summary>
    /// 전력질주 입력 상태를 갱신합니다.
    /// </summary>
    /// <param name="newSprintState">새 전력질주 입력 상태입니다.</param>
    public void SprintInput(bool newSprintState)
    {
        m_sprint = newSprintState;
    }

    /// <summary>
    /// 조준 입력 상태를 갱신합니다.
    /// </summary>
    /// <param name="newAimState">새 조준 입력 상태입니다.</param>
    public void AimInput(bool newAimState)
    {
        m_aim = newAimState;
    }

    /// <summary>
    /// 발사 입력 상태를 갱신합니다.
    /// </summary>
    /// <param name="newShootState">새 발사 입력 상태입니다.</param>
    public void ShootInput(bool newShootState)
    {
        m_shoot = newShootState;
    }

    /// <summary>
    /// 재장전 입력 상태를 갱신합니다.
    /// </summary>
    /// <param name="newReloadState">새 재장전 입력 상태입니다.</param>
    public void ReloadInput(bool newReloadState)
    {
        m_reload = newReloadState;
    }

    /// <summary>
    /// 상호작용 입력 상태를 갱신합니다.
    /// </summary>
    /// <param name="newInteractState">새 상호작용 입력 상태입니다.</param>
    public void InteractInput(bool newInteractState)
    {
        m_interact = newInteractState;
    }

    private void RefreshInteractionInputFromAction()
    {
#if ENABLE_INPUT_SYSTEM
        InputAction action = ResolveInteractionAction();
        if (action != null)
        {
            m_interact = action.IsPressed();
        }
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private void CachePlayerInput()
    {
        if (m_playerInput == null)
        {
            m_playerInput = GetComponent<PlayerInput>();
        }
    }

    private InputAction ResolveInteractionAction()
    {
        if (m_interactionAction != null)
        {
            return m_interactionAction;
        }

        CachePlayerInput();
        if (m_playerInput == null || m_playerInput.actions == null)
        {
            return null;
        }

        m_interactionAction = m_playerInput.actions.FindAction("Interaction", false)
            ?? m_playerInput.actions.FindAction("Interact", false);

        return m_interactionAction;
    }
#endif

    /// <summary>
    /// 커서 잠금 사용 여부를 설정합니다.
    /// </summary>
    /// <param name="value">커서를 잠그려면 true, 해제하려면 false입니다.</param>
    public void SetCursorLocked(bool value)
    {
        m_cursorLocked = value;
        SetCursorState(m_cursorLocked);
    }

    /// <summary>
    /// 시점 회전에 커서 입력을 사용할지 설정합니다.
    /// </summary>
    /// <param name="value">커서 입력을 시점 회전에 사용하려면 true입니다.</param>
    public void SetCursorInputForLook(bool value)
    {
        m_cursorInputForLook = value;
    }

    /// <summary>
    /// 필드 플레이어의 TPS 입력과 UI 커서 입력을 전환합니다.
    /// </summary>
    /// <param name="cursorMode">true이면 UI 커서 모드, false이면 TPS 게임플레이 모드입니다.</param>
    /// <remarks>
    /// 이 메서드는 필드 PlayerInputs 전용입니다. 셸터의 입력 구조는 자체 구현으로 같은 입력 모드 계약을 처리합니다.
    /// </remarks>
    public void SetPlayerCursorMode(bool cursorMode)
    {
        m_isInputEnabled = !cursorMode;
        ResetInputState();
        SetCursorInputForLook(!cursorMode);
        SetCursorLocked(!cursorMode);
        Cursor.visible = cursorMode;

        if (m_isInputEnabled)
        {
            ResyncHeldInputFromDevices();
        }
    }

    /// <summary>
    /// 지금 눌려 있는 입력을 장치에서 다시 읽어 상태에 반영합니다.
    /// </summary>
    /// <remarks>
    /// 입력을 다시 켤 때 필요합니다. 이 컴포넌트는 액션맵을 끄지 않고 콜백마다
    /// <see cref="m_isInputEnabled"/>로 걸러내므로, 꺼져 있는 동안 들어온 입력은 버려집니다.
    /// 그리고 <see cref="ResetInputState"/>가 상태를 0으로 밀어 놓는데, 키를 계속 누르고 있으면
    /// 키 상태가 변하지 않아 콜백이 다시 오지 않습니다. 그래서 다시 켠 뒤에도 키를 떼고
    /// 다시 누를 때까지 입력이 먹지 않습니다. 트레이너를 이동 중에 닫으면 캐릭터가 멈춰 있는 증상이 이것입니다.
    ///
    /// 시점 입력은 다시 읽지 않습니다. 그 값은 누적된 상태가 아니라 프레임당 변화량이라,
    /// 다시 읽으면 지난 프레임의 변화량을 한 번 더 적용하는 셈이 됩니다.
    /// </remarks>
    private void ResyncHeldInputFromDevices()
    {
#if ENABLE_INPUT_SYSTEM
        CachePlayerInput();
        if (m_playerInput == null || m_playerInput.actions == null)
        {
            return;
        }

        InputAction move = m_playerInput.actions.FindAction("Move", false);
        if (move != null)
        {
            m_move = move.ReadValue<Vector2>();
        }

        m_jump = IsActionPressed("Jump");
        m_sprint = IsActionPressed("Sprint");
        m_aim = IsActionPressed("Aim");
        m_shoot = IsActionPressed("Shoot");
        m_reload = IsActionPressed("Reload");

        InputAction interaction = ResolveInteractionAction();
        if (interaction != null)
        {
            m_interact = interaction.IsPressed();
        }
#endif
    }

#if ENABLE_INPUT_SYSTEM
    /// <summary>이름으로 찾은 액션이 지금 눌려 있는지 확인합니다. 없는 액션은 눌리지 않은 것으로 봅니다.</summary>
    private bool IsActionPressed(string actionName)
    {
        InputAction action = m_playerInput.actions.FindAction(actionName, false);
        return action != null && action.IsPressed();
    }
#endif

    /// <summary>
    /// 아날로그 이동 입력 사용 여부를 설정합니다.
    /// </summary>
    /// <param name="value">입력 세기를 이동 속도에 반영하려면 true입니다.</param>
    public void SetAnalogMovement(bool value)
    {
        m_analogMovement = value;
    }

    /// <summary>
    /// 애플리케이션 포커스 상태가 바뀔 때 커서 상태를 갱신합니다.
    /// </summary>
    /// <param name="hasFocus">애플리케이션이 포커스를 얻었으면 true입니다.</param>
    private void OnApplicationFocus(bool hasFocus)
    {
        SetCursorState(m_cursorLocked);
    }

    /// <summary>
    /// Unity 커서 잠금 상태를 적용합니다.
    /// </summary>
    /// <param name="newState">커서를 잠그려면 true, 해제하려면 false입니다.</param>
    private void SetCursorState(bool newState)
    {
        Cursor.lockState = newState ? CursorLockMode.Locked : CursorLockMode.None;
    }

    /// <summary>
    /// 모든 런타임 입력 상태를 기본값으로 초기화합니다.
    /// </summary>
    /// <remarks>
    /// 이동과 시점 입력은 <see cref="Vector2.zero"/>로 초기화하고,
    /// 버튼형 입력은 모두 false로 초기화합니다.
    /// </remarks>
    public void ResetInputState()
    {
        m_move = Vector2.zero;
        m_look = Vector2.zero;
        m_jump = false;
        m_sprint = false;
        m_aim = false;
        m_shoot = false;
        m_reload = false;
        m_interact = false;
    }

    /// <summary>
    /// 상호작용 상태를 유지한 채 이동·시점·행동 입력만 초기화합니다.
    /// </summary>
    public void ResetNonInteractionInputState()
    {
        bool wasInteracting = m_interact;

        ResetInputState();
        m_interact = wasInteracting;
    }
}
