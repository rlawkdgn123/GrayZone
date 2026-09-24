using UnityEngine;
using UnityEngine.Serialization;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 사람의 조작을 받아 플레이어 조작 캐릭터의 행동 의도로 변환하는 컴포넌트입니다.
/// </summary>
/// <remarks>
/// AI 조작 캐릭터의 <see cref="SquadAIController"/>와 대칭입니다. 이쪽은 Input System에서 사람 입력을 받고
/// 저쪽은 AI가 스스로 판단하며, 조종 주체만 다르고 아래 계층은 같은 것을 씁니다.
/// 어느 쪽이 활성인지는 <see cref="SquadMemberController"/>가 정합니다.
/// AI나 스크립트가 이 컴포넌트에 입력을 밀어넣지 않습니다. 여기는 사람 입력 경로 전용입니다.
/// (예외: 멤버 전환 인계에서 <see cref="SquadMemberController"/>가 유지 입력을 복원할 때 세터를 씁니다.)
/// <para>
/// 이 클래스는 입력 액션 콜백에서 받은 값을 내부 필드에 캐싱하고,
/// 이동/시점/점프/전력질주/조준/공격/재장전 상태를 다른 시스템에서 읽을 수 있게 제공합니다.
/// 행동 가능 여부 판정(예: 달리기 중 조준 입력 처리)은 여기가 아니라 소비하는 쪽에 있습니다.
/// </para>
/// <para>
/// 변수 컨벤션은 <c>m_</c> 접두사를 사용하는 private serialized field를 기준으로 하며,
/// 기존 Starter Assets 스타일의 <c>move</c>, <c>look</c>, <c>jump</c> 접근도 호환용 프로퍼티로 유지합니다.
/// </para>
/// </remarks>
public class PlayerInputController : MonoBehaviour
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

    [Tooltip("폭발탄 투척 모드가 활성화됐는지 여부입니다.")]
    [SerializeField] private bool m_throwMode;

    /// <summary>투척물 선택 방향을 소비하기 전까지 누적합니다. 이전은 음수, 다음은 양수입니다.</summary>
    private int m_throwSelectionDelta;

    [Tooltip("재장전 입력이 눌린 상태인지 여부입니다.")]
    [FormerlySerializedAs("reload")]
    [SerializeField] private bool m_reload;

    [Tooltip("웅크리기 유지 상태인지 여부입니다. 토글 방식이면 눌린 순간이 아니라 유지된 결과입니다.")]
    [SerializeField] private bool m_crouch;

    [Tooltip("웅크리기를 토글로 쓸지 여부입니다. 끄면 누르고 있는 동안만 웅크립니다.")]
    [SerializeField] private bool m_crouchToggle = true;

    [Tooltip("상호작용 입력이 눌린 상태(홀드 포함)인지 여부입니다.")]
    [SerializeField] private bool m_interact;

    /// <summary>튜토리얼 닫기 입력이 눌렸는지입니다. 읽는 쪽이 소비합니다.</summary>
    private bool m_tutorialClose;

    /// <summary>상호작용 입력을 손 뗄 때까지 막고 있는지입니다.</summary>
    private bool m_suppressInteractUntilRelease;

    [Tooltip("인벤토리 열기 입력이 눌린 상태인지 여부입니다. 여닫기 판정은 소비 측에서 처리합니다.")]
    [SerializeField] private bool m_inventory;

    // UI 커서 모드에서는 Input System 콜백이 게임플레이 상태를 다시 채우지 않도록 막습니다.
    private bool m_isInputEnabled = true;

#if ENABLE_INPUT_SYSTEM
    private PlayerInput m_playerInput;
    private InputAction m_interactionAction;
    private InputAction m_inventoryAction;
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

    /// <summary>발사 입력 상태입니다. 투척 모드에서는 총기 발사로 전달하지 않습니다.</summary>
    public bool Shoot => !m_throwMode && m_shoot;

    /// <summary>폭발탄 투척 모드 활성 상태입니다.</summary>
    public bool ThrowMode => m_throwMode;

    /// <summary>투척 모드에서 좌클릭 입력이 눌린 상태입니다.</summary>
    public bool Throw => m_throwMode && m_shoot;

    /// <summary>G 투척 모드에서 들어온 투척물 선택 방향을 한 번 읽고 비웁니다.</summary>
    public int ConsumeThrowSelectionDelta()
    {
        if (!m_isInputEnabled || !m_throwMode)
        {
            m_throwSelectionDelta = 0;
            return 0;
        }

        int delta = m_throwSelectionDelta;
        m_throwSelectionDelta = 0;
        return delta;
    }

    /// <summary>재장전 입력 상태입니다.</summary>
    public bool Reload => m_reload;

    /// <summary>웅크리기 유지 상태입니다.</summary>
    /// <remarks>
    /// 기본값은 토글입니다. 설계 문서(캐릭터 행동 시스템 §8)가 앉기를 토글로 정의합니다.
    /// 전력질주와 조준은 홀드이고, 나중에 옵션에서 세 입력의 방식을 각각 고를 수 있게 할 예정이라
    /// 방식 판단을 이 컴포넌트 안에 두고 밖으로는 "유지 상태" 하나만 내보냅니다.
    /// 소비 측이 방식을 알아야 하면 같은 판단이 여러 곳에 흩어집니다.
    /// </remarks>
    public bool Crouch => m_crouch;

    /// <summary>웅크리기를 토글로 쓰는지 여부입니다.</summary>
    public bool CrouchToggle => m_crouchToggle;

    /// <summary>상호작용 입력이 눌린 상태(홀드 포함)입니다. 탭/홀드 판정은 소비 측(InteractionController)에서 처리합니다.</summary>
    public bool Interact
    {
        get
        {
            if (!m_isInputEnabled || m_throwMode)
            {
                return false;
            }

            RefreshInteractionInputFromAction();

            // 억제 중이면 키에서 손을 뗄 때까지 눌리지 않은 것으로 봅니다.
            // 손을 뗀 시점에 억제를 스스로 풀어, 다음 입력부터 정상으로 돌아옵니다.
            if (m_suppressInteractUntilRelease)
            {
                if (m_interact)
                {
                    return false;
                }

                m_suppressInteractUntilRelease = false;
            }

            return m_interact;
        }
    }

    /// <summary>
    /// 이번 프레임에 튜토리얼 닫기 입력이 눌렸는지 여부입니다. 한 번 읽으면 소비됩니다.
    /// </summary>
    /// <remarks>
    /// 누르는 순간만 필요한 입력이라 읽을 때 지웁니다. 눌린 상태를 유지해 두면 안내를 닫은 뒤에도
    /// 같은 값이 남아 다음 안내가 뜨자마자 닫힙니다.
    ///
    /// 닫기 액션이 프로젝트에 없으면 이 값은 계속 <c>false</c>입니다. 그 경우 안내는 닫히지 않으므로
    /// 소비 측에서 다른 닫기 수단을 두어야 합니다.
    /// </remarks>
    public bool TutorialClosePressed
    {
        get
        {
            if (!m_isInputEnabled || !m_tutorialClose)
            {
                return false;
            }

            m_tutorialClose = false;
            return true;
        }
    }

    /// <summary>
    /// 상호작용 입력을 키에서 손을 뗄 때까지 막습니다.
    /// </summary>
    /// <remarks>
    /// 튜토리얼 닫기가 상호작용과 같은 키일 때 씁니다. 막지 않으면 안내를 닫은 그 입력이 그대로
    /// 이어져 눈앞의 대상과 상호작용해 버립니다. 시간이 아니라 "뗄 때까지"인 이유는, 얼마나 오래
    /// 누르고 있을지 알 수 없어서입니다.
    /// </remarks>
    public void SuppressInteractUntilRelease()
    {
        m_suppressInteractUntilRelease = true;
    }

    /// <summary>인벤토리 입력이 눌린 상태입니다. 여닫기 전환은 소비 측에서 판정합니다.</summary>
    /// <remarks>
    /// <see cref="Interact"/>와 같은 모양으로 둔 이유: 두 입력 모두 콜백만으로는 조작권이 넘어간 순간의
    /// 상태를 놓칠 수 있어, 소비 측이 읽을 때 액션에서 현재 상태를 다시 확인해야 합니다.
    /// </remarks>
    public bool Inventory
    {
        get
        {
            if (!m_isInputEnabled)
            {
                return false;
            }

            RefreshInventoryInputFromAction();
            return m_inventory;
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
        get => Shoot;
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
    /// 폭발탄 투척 모드를 켜거나 끄는 입력 액션 콜백입니다.
    /// </summary>
    /// <param name="value">Input System에서 전달된 투척 모드 입력 상태입니다.</param>
    public void OnThrowMode(InputValue value)
    {
        if (!m_isInputEnabled || !value.isPressed)
        {
            return;
        }

        m_throwMode = !m_throwMode;
        m_throwSelectionDelta = 0;

        // 모드를 바꾸는 순간 누르고 있던 좌클릭이 다른 행동으로 넘어가지 않게 중립화합니다.
        m_shoot = false;
        m_interact = false;
        SuppressInteractUntilRelease();
    }

    /// <summary>
    /// G 투척 모드에서 Q/E 또는 마우스 휠로 투척물을 선택하는 입력 액션 콜백입니다.
    /// </summary>
    /// <param name="value">음수이면 이전, 양수이면 다음 투척물을 선택합니다.</param>
    public void OnThrowSelection(InputValue value)
    {
        if (!m_isInputEnabled || !m_throwMode)
        {
            return;
        }

        float selectionAxis = value.Get<float>();
        if (Mathf.Abs(selectionAxis) <= 0.0001f)
        {
            return;
        }

        m_throwSelectionDelta += selectionAxis > 0.0f ? 1 : -1;
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
    /// 웅크리기 입력 액션 콜백입니다.
    /// </summary>
    /// <param name="value">Input System에서 전달된 웅크리기 입력 상태입니다.</param>
    public void OnCrouch(InputValue value)
    {
        if (!m_isInputEnabled)
        {
            return;
        }

        // 토글은 누른 순간에만 뒤집습니다. 떼는 콜백까지 반영하면 한 번 누를 때 두 번 뒤집혀 제자리로 돌아옵니다.
        if (m_crouchToggle)
        {
            if (value.isPressed)
            {
                CrouchInput(!m_crouch);
            }

            return;
        }

        CrouchInput(value.isPressed);
    }

    /// <summary>
    /// 상호작용(Interaction) 입력 액션 콜백입니다.
    /// </summary>
    /// <param name="value">Input System에서 전달된 상호작용 입력 상태입니다.</param>
    /// <remarks>버튼 액션이라 누름/뗌 모두 호출되며, <c>isPressed</c>로 홀드 상태를 그대로 보관합니다.</remarks>
    public void OnInteraction(InputValue value)
    {
        if (!m_isInputEnabled || m_throwMode)
        {
            if (m_throwMode)
            {
                InteractInput(false);
            }

            return;
        }

        InteractInput(value.isPressed);
    }

    /// <summary>
    /// 튜토리얼 닫기(TutorialClose) 입력 액션 콜백입니다.
    /// </summary>
    /// <param name="value">Input System에서 전달된 입력 상태입니다.</param>
    /// <remarks>
    /// 액션이 정의되어 있지 않으면 이 콜백은 호출되지 않습니다. 그 경우
    /// <see cref="TutorialClosePressed"/>는 계속 <c>false</c>이고 다른 동작에 영향을 주지 않습니다.
    /// </remarks>
    public void OnTutorialClose(InputValue value)
    {
        if (!m_isInputEnabled || !value.isPressed)
        {
            return;
        }

        m_tutorialClose = true;
    }

    /// <summary>
    /// 인벤토리(Inventory) 입력 액션 콜백입니다.
    /// </summary>
    /// <param name="value">Input System에서 전달된 인벤토리 입력 상태입니다.</param>
    public void OnInventory(InputValue value)
    {
        if (!m_isInputEnabled)
        {
            return;
        }

        InventoryInput(value.isPressed);
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
    /// 웅크리기 입력 상태를 갱신합니다.
    /// </summary>
    /// <param name="newCrouchState">새 웅크리기 입력 상태입니다.</param>
    public void CrouchInput(bool newCrouchState)
    {
        m_crouch = newCrouchState;
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

    /// <summary>
    /// 인벤토리 입력 상태를 갱신합니다.
    /// </summary>
    /// <param name="newInventoryState">새 인벤토리 입력 상태입니다.</param>
    public void InventoryInput(bool newInventoryState)
    {
        m_inventory = newInventoryState;
    }

    private void RefreshInventoryInputFromAction()
    {
#if ENABLE_INPUT_SYSTEM
        InputAction action = ResolveInventoryAction();
        if (action != null)
        {
            m_inventory = action.IsPressed();
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

    private InputAction ResolveInventoryAction()
    {
        if (m_inventoryAction != null)
        {
            return m_inventoryAction;
        }

        CachePlayerInput();
        if (m_playerInput == null || m_playerInput.actions == null)
        {
            return null;
        }

        m_inventoryAction = m_playerInput.actions.FindAction("Inventory", false);

        return m_inventoryAction;
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
    /// 커서를 띄워 둔 채로 게임플레이 입력을 받게 합니다. 마우스 시점 회전만 막습니다.
    /// </summary>
    /// <param name="enabled">true이면 입력을 받고, false이면 UI 커서 모드처럼 모두 막습니다.</param>
    /// <remarks>
    /// 디버그 트레이너처럼 <b>창을 띄운 채로 조작해 봐야 하는</b> 도구를 위한 상태입니다.
    /// <see cref="SetPlayerCursorMode"/>와 달리 커서를 건드리지 않습니다. 커서는 창을 띄운 쪽이 계속 소유합니다.
    ///
    /// 시점 회전만 막는 이유는 커서가 풀려 있기 때문입니다. 창으로 마우스를 옮기는 것만으로 카메라가
    /// 따라 돌면 값을 만지는 동안 화면이 계속 흔들립니다. 사격·조준 같은 버튼 입력은 그대로 받습니다.
    /// </remarks>
    public void SetGameplayInputWithFreeCursor(bool enabled)
    {
        m_isInputEnabled = enabled;
        SetCursorInputForLook(false);

        if (enabled)
        {
            // 창을 누르고 있는 동안 눌린 키가 있으면 지금 상태를 장치에서 다시 읽어 맞춥니다.
            ResyncHeldInputFromDevices();
            return;
        }

        ResetInputState();
    }

    /// <summary>
    /// 필드 플레이어의 TPS 입력과 UI 커서 입력을 전환합니다.
    /// </summary>
    /// <param name="cursorMode">true이면 UI 커서 모드, false이면 TPS 게임플레이 모드입니다.</param>
    /// <remarks>
    /// 이 메서드는 필드 PlayerInputController 전용입니다. 셸터의 입력 구조는 자체 구현으로 같은 입력 모드 계약을 처리합니다.
    /// </remarks>
    public void SetPlayerCursorMode(bool cursorMode)
    {
        SetInputGate(!cursorMode);
        SetCursorLocked(!cursorMode);
        Cursor.visible = cursorMode;
    }

    /// <summary>
    /// 이 멤버의 게임플레이 입력 게이트만 여닫습니다. OS 커서는 건드리지 않습니다.
    /// </summary>
    /// <param name="enabled">입력을 받으면 true, 막으면 false입니다.</param>
    /// <remarks>
    /// <b>조작 중이 아닌 멤버에도 걸기 위한 진입점입니다.</b> 커서는 화면에 하나뿐이라 스쿼드 전체에
    /// 걸 수 없지만, 이 게이트는 멤버마다 따로 있고 <b>컴포넌트가 꺼져도 값이 남습니다</b>.
    /// 그래서 조작 멤버에게만 걸면, 걸 때와 풀 때의 조작 멤버가 다를 경우 한쪽이 막힌 채 남습니다.
    /// 실제로 그 경로로 "전환하면 총이 안 나가고 마우스 시점도 안 먹는" 결함이 보고됐습니다.
    /// <para>
    /// 스쿼드 전체 적용은 <see cref="SquadManager"/>가 멤버를 순회하며 이 함수를 부르는 형태로 합니다.
    /// 커서를 함께 다루는 <see cref="SetPlayerCursorMode"/>는 조작 멤버 하나에만 씁니다.
    /// </para>
    /// </remarks>
    public void SetInputGate(bool enabled)
    {
        m_isInputEnabled = enabled;
        ResetInputState();
        SetCursorInputForLook(enabled);

        if (enabled)
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

        // 웅크림은 토글이면 유지 상태이므로 장치에서 다시 읽지 않습니다. 읽으면 키를 떼고 있는 것만으로 풀립니다.
        if (!m_crouchToggle)
        {
            m_crouch = IsActionPressed("Crouch");
        }

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
        m_throwMode = false;
        m_throwSelectionDelta = 0;
        m_reload = false;
        m_crouch = false;
        m_interact = false;
    }

    /// <summary>
    /// 다운 강제 전환이 끝났을 때 새 조작 캐릭터에 넘길 입력만 인계합니다.
    /// </summary>
    /// <remarks>
    /// 공용 문서 `캐릭터 행동 시스템` §14가 정본입니다. 일반 전환과 다른 규칙을 씁니다.
    /// <para>
    /// <b>이동 방향만 넘깁니다</b>("이동 방향 입력만 전환 완료 시점까지 유지되고 있으면 새 조작
    /// 캐릭터에 적용한다"). 전환 중 계속 앞으로 가고 있었다면 조작권을 받자마자 멈춰 서지 않습니다.
    /// </para>
    /// <para>
    /// <b>달리기·조준·사격은 누르고 있어도 넘기지 않습니다</b>("전환 중 유지되고 있어도 중립 상태로
    /// 처리한다", "전환 완료 후 입력을 놓았다가 다시 해야 적용한다"). 그래서 <see cref="ResyncHeldInputFromDevices"/>를
    /// 쓰지 않습니다. 그쪽은 눌린 것을 전부 다시 읽어 오므로 이 규칙과 정반대입니다.
    /// </para>
    /// <para>
    /// 점프·재장전 같은 시점성 입력은 애초에 유지 상태가 아니라 여기서 다룰 것이 없습니다
    /// ("적용하거나 버퍼링하지 않는다").
    /// </para>
    /// </remarks>
    public void ApplyForcedSwitchInputHandover()
    {
        ResetInputState();

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
#endif
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
