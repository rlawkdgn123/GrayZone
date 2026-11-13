using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 테스트 씬 공용 런타임 디버그 트레이너입니다. 지정한 키로 TPS 조작에서 벗어나 커서로 조작하며,
/// 선택한 캐릭터의 체력/이동/무기 스탯을 조절하고, 좀비(적)를 좌표로 스폰할 수 있습니다.
/// </summary>
/// <remarks>
/// <para>
/// 이 컴포넌트가 들어 있는 공용 프리팹은 필드 씬 시작 시 <see cref="FieldSceneDataManager"/>가 생성하며,
/// 생성된 뒤 주변 런타임 컴포넌트를 자동 탐색합니다. 씬에 직접 배치해도 동작합니다.
/// 개발 모드 자체는 <see cref="GameManager"/>가 소유하고 이 트레이너는 <see cref="GameDevMode"/>를 읽기만 합니다.
/// 동작 여부는 <see cref="GameDevMode.DebugFeaturesEnabled"/>가 정합니다. Editor와 Development Build에서 살아 있고,
/// 정식 빌드에서는 개발 모드가 켜져 있어도 창이 열리지 않습니다.
/// IMGUI(OnGUI) 기반이라 별도의 uGUI 연결은 필요 없습니다.
/// </para>
/// <para>
/// 동작 범위는 필드 씬으로 한정합니다. 판정 기준은 <see cref="FieldManager"/>의 존재이며,
/// 셸터에는 별도의 트레이너가 있으므로 이쪽이 남의 씬 입력을 건드리지 않게 합니다.
/// </para>
/// <para>
/// 값 조절은 기존 컴포넌트의 공개 setter를 그대로 사용하고, 무한 체력/장탄수/예비탄약 같은 치트는
/// 각 컴포넌트가 이미 소유한 디버그 플래그(개발 모드에서만 효과)를 통해 켭니다.
/// </para>
/// </remarks>
[DefaultExecutionOrder(-10000)]
[DisallowMultipleComponent]
public class RuntimeDebugTrainer : MonoBehaviour
{
    private static RuntimeDebugTrainer s_instance;

    private bool m_isDuplicate;
    private IInputModeController m_sceneInputModeController;
    private bool m_usesSceneInputModeController;
    private InputMode m_inputModeBeforeTrainerOpen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        s_instance = null;
    }

    // ─────────────────────────────────────────────────────────────
    // 상태
    // ─────────────────────────────────────────────────────────────

    [Header("Activation")]
    [SerializeField, Tooltip("개발 모드와 독립적으로 트레이너 UI와 토글 입력만 활성화합니다.")]
    private bool m_trainerEnabled = true;

    [SerializeField, Tooltip("트레이너 창을 열고 닫을 키입니다. None이면 키 입력으로 열 수 없습니다.")]
    private Key m_toggleKey = Key.F9;

    /// <summary>개발 모드 상태를 바꾸지 않고 트레이너 UI와 입력만 켜거나 끕니다.</summary>
    public bool TrainerEnabled
    {
        get => m_trainerEnabled;
        set
        {
            m_trainerEnabled = value;
            if (!m_trainerEnabled && m_open)
            {
                SetMenuOpen(false);
            }
        }
    }

    /// <summary>트레이너 창을 열고 닫는 키입니다.</summary>
    public Key ToggleKey
    {
        get => m_toggleKey;
        set => m_toggleKey = value;
    }

    /// <summary>트레이너 패널(메뉴)이 열려 있는지 여부입니다. 열려 있으면 TPS 조작이 잠깁니다.</summary>
    private bool m_open;

    /// <summary>
    /// 트레이너 창이 지금 열려 있는지 여부입니다.
    /// </summary>
    /// <remarks>
    /// 다른 디버그 창(<see cref="CrosshairDebugInspector"/> 등)이 커서와 플레이어 입력을 겹쳐 잡지 않도록 알려 줍니다.
    /// 둘이 각자 잠그고 각자 풀면, 먼저 닫은 쪽이 아직 열려 있는 창의 커서까지 다시 잠가 버립니다.
    /// </remarks>
    public static bool IsMenuOpen => s_instance != null && s_instance.m_open;

    /// <summary>인스펙터 대상 인덱스입니다. -1이면 "현재 조작 중인 캐릭터"를 계속 따라갑니다.</summary>
    private int m_targetIndex = -1;

    private Vector2 m_scroll;

    /// <summary>역동기화 버튼을 누른 결과를 창에 남겨 두는 문자열입니다.</summary>
    private string m_reverseSyncMessage;

    /// <summary>반동·킥 엔벨로프 항목을 펼쳐 둘지 여부입니다. 항목이 많아 기본은 접어 둡니다.</summary>
    private bool m_showRecoilEnvelope;

    // 곡선 생성기 입력값입니다. 축마다 따로 두지 않고 하나를 공유하고,
    // 어느 축에 넣을지는 누르는 버튼으로 고릅니다. 축별로 두면 슬라이더가 열다섯 줄이 됩니다.
    private float m_envelopePeakRatio = 0.25f;
    private float m_envelopePeakValue = 1.0f;
    private float m_envelopeEasePower = 1.0f;

    private bool m_showWeaponHitscanLayers;
    private bool m_showAimTargetLayers;

    /// <summary>전멸 게임오버 버튼이 확인 단계에 들어가 있는지 여부입니다.</summary>
    /// <remarks>되돌릴 수 없는 조작이라 한 번 누르면 실행하지 않고 확인을 먼저 받습니다.</remarks>
    private bool m_confirmForceGameOver;

    /// <summary>
    /// 최상위 탭 이름입니다. 순서가 곧 화면에 놓이는 순서입니다.
    /// </summary>
    /// <remarks>
    /// 조절하는 대상이 가까운 것끼리 묶고, 자주 만지는 것을 앞에 둡니다.
    /// 캐릭터·애니메이션·무기·조준은 지금 조작 중인 유닛을 만지는 탭이고, 필드·디버그는 그 바깥입니다.
    /// </remarks>
    /// <summary>투척물 구역의 버튼이 한 번에 넣는 수량입니다.</summary>
    private const int DebugThrowableGrantAmount = 999;

    /// <summary>마지막 투척물 지급 결과 문구입니다. 공간이 모자라 못 넣은 수량을 알려 줍니다.</summary>
    private string m_throwableGrantResult = string.Empty;

    private static readonly string[] MainTabNames =
    {
        "캐릭터",
        "애니메이션",
        "무기",
        "조준·반동",
        "필드",
        "디버그",
    };

    /// <summary>지금 보고 있는 최상위 탭입니다.</summary>
    private int m_mainTabIndex;

    /// <summary>트레이너 창이 지금 선택(하이라이트)되어 있는지 여부입니다.</summary>
    /// <remarks>창 안을 누르면 참, 배경을 누르면 거짓이 됩니다. 창을 막 열었을 때는 창을 보고 있다고 봅니다.</remarks>
    private bool m_windowFocused = true;

    /// <summary>배경을 눌러 키보드를 게임에 넘겨 준 상태인지 여부입니다.</summary>
    private bool m_keyboardPassthrough;

    /// <summary>
    /// 배경 조작을 켜면서 이쪽이 직접 활성화한 입력 컴포넌트들입니다.
    /// </summary>
    /// <remarks>
    /// 되돌릴 때 이 목록만 다시 끕니다. 원래 켜져 있던 것까지 끄면 트레이너를 닫은 뒤 조작이 죽습니다.
    /// 씬에 따라 입력을 끄는 방식이 달라(계약 방식 / 컴포넌트 비활성) 무엇을 건드렸는지 기억해야 합니다.
    /// </remarks>
    private readonly List<PlayerInput> m_passthroughActivatedInputs = new List<PlayerInput>();

    /// <summary>배경 조작 중 시점 입력을 막아 둔 조작 멤버의 PlayerInput입니다. 멤버가 바뀌면 새 멤버에 다시 겁니다.</summary>
    private PlayerInput m_passthroughBlockedInput;

    /// <summary>
    /// 배경 조작을 넘겨 준 동안에도 막아 둘 액션 이름입니다.
    /// </summary>
    /// <remarks>
    /// 트레이너가 열려 있으면 커서가 풀려 있어야 슬라이더를 만질 수 있습니다. 그 상태에서 시점 회전이
    /// 살아 있으면 창으로 마우스를 옮기는 것만으로 카메라가 따라 돌아 값을 만지는 내내 화면이 흔들립니다.
    /// 그래서 시점만 막습니다. 사격·조준 같은 버튼 입력은 그대로 받습니다.
    /// </remarks>
    private static readonly string[] BlockedActionNames = { "Look" };

    // IMGUI 런타임 창은 기본 리사이즈 핸들이 없으므로 우하단 그립으로 크기를 바꿉니다.
    private Rect m_windowRect = new Rect(10f, 10f, 560f, 720f);
    private Vector2 m_resizeStartMouse;
    private Vector2 m_resizeStartSize;
    private bool m_isResizing;

    private const float MinWindowWidth = 440f;
    private const float MinWindowHeight = 360f;
    private const float WindowMargin = 10f;
    private const float ResizeGripSize = 20f;

    /// <summary>창 테두리에서 크기 조절을 잡을 수 있는 두께입니다.</summary>
    private const float ResizeBorderSize = 6f;

    /// <summary>글자 배율 1을 적용할 기준 창 너비입니다. 기본 창 크기와 같게 두어 처음에는 배율이 1이 됩니다.</summary>
    private const float ReferenceWindowWidth = 500f;

    /// <summary>글자가 읽을 수 없을 만큼 작아지지 않게 하는 하한입니다.</summary>
    private const float MinUiScale = 0.75f;

    /// <summary>창을 넓혀도 글자가 지나치게 커지지 않게 하는 상한입니다.</summary>
    private const float MaxUiScale = 2.0f;

    /// <summary>지금 스타일에 반영된 배율입니다.</summary>
    private float m_appliedUiScale = -1f;

    /// <summary>배율을 적용한 스킨과 그 배율입니다.</summary>
    private GUISkin m_scaledSkin;
    private float m_scaledSkinScale = -1f;

    /// <summary>창을 그리기 직전의 전역 스킨입니다. 다 그린 뒤 되돌립니다.</summary>
    private GUISkin m_previousSkin;

    // 좀비 스폰 좌표 입력 버퍼입니다.
    private string m_spawnX = "0";
    private string m_spawnY = "0";
    private string m_spawnZ = "0";
    private int m_spawnCount = 1;

    // 스폰 원본(씬의 기존 좀비를 비활성 복제로 보관해 두어 원본이 죽어도 계속 스폰 가능).
    private GameObject m_enemyTemplate;
    private GameObject m_enemyTemplateHolder;

    // 스폰 원본 후보입니다. 씬에 여러 종류의 적이 있으면 어느 것을 복제할지 직접 고를 수 있게 합니다.
    // 고르지 않았거나 고른 대상이 사라졌으면 종전대로 씬에서 가장 먼저 찾은 적을 씁니다.
    private readonly List<EnemyController> m_enemyTemplateChoices = new List<EnemyController>();
    private int m_enemyTemplateIndex = -1;

    private GUIStyle m_titleStyle;
    protected GUIStyle m_headerStyle;

    private void Awake()
    {
        // 빌드에서 오브젝트를 지우지 않습니다.
        // 포함 여부는 GameDevMode.DebugFeaturesEnabled가 정하며, 그 조건에 Debug.isDebugBuild가 이미 들어 있어
        // Development Build에서는 살아 있고 정식 빌드에서는 꺼집니다.
        // 컴파일 단계에서 잘라내면 그 런타임 판단이 도달하지 못해, 개발자용 빌드에서도 트레이너를 쓸 수 없습니다.
        if (s_instance != null && s_instance != this)
        {
            m_isDuplicate = true;
            Destroy(gameObject);
            return;
        }

        s_instance = this;

        // 여기서 개발 모드를 보고 스스로 끄지 않습니다.
        // 개발 모드는 GameManager가 자기 Awake에서 켜는데, Awake 실행 순서는 보장되지 않습니다.
        // 트레이너가 먼저 깨면 아직 꺼져 있는 값을 보고 자신을 끄고, 그 뒤에 켜져도 다시 살아나지 않습니다.
        // 실제로 개발 모드가 켜져 있는데도 F9가 먹지 않는 상태가 이렇게 만들어졌습니다.
        //
        // Update가 매 프레임 개발 모드를 확인해 열려 있던 창을 닫고 입력을 무시하므로,
        // 컴포넌트를 켜 둔 채로도 꺼진 것과 같이 동작합니다.
    }

    private void OnEnable()
    {
        if (m_isDuplicate)
        {
            return;
        }

        if (s_instance != null && s_instance != this)
        {
            m_isDuplicate = true;
            Destroy(gameObject);
            return;
        }

        s_instance = this;
    }

    private void OnDisable()
    {
        if (m_open)
        {
            m_open = false;
            UnlockControls();
        }
    }

    private void OnDestroy()
    {
        if (s_instance == this)
        {
            s_instance = null;
        }

        // 씬 전환으로 파괴될 때는 OnDisable의 복구가 이미 끝난 뒤이지만, 그 복구는 함께 파괴되는
        // FieldManager를 거치므로 실패할 수 있습니다. 커서만은 남기지 않도록 여기서 마지막으로 되돌립니다.
        // 커서 상태는 씬을 넘어 유지되는 전역 값이라, 열린 채로 전환하면 다음 씬이 잠긴 커서를 물려받습니다.
        RestoreCursorForSceneExit();

        // DontDestroyOnLoad로 남겨둔 스폰 원본 홀더를 함께 정리합니다.
        // 이 홀더는 씬을 넘어 살아남으므로, 트레이너가 사라질 때 반드시 같이 지워야 셸터로 따라가지 않습니다.
        if (m_enemyTemplateHolder != null)
        {
            Destroy(m_enemyTemplateHolder);
            m_enemyTemplateHolder = null;
            m_enemyTemplate = null;
        }

        // 후보는 씬의 오브젝트를 가리키므로 씬과 함께 사라집니다. 참조를 남기지 않도록 같이 비웁니다.
        m_enemyTemplateChoices.Clear();
        m_enemyTemplateIndex = -1;

        m_sceneInputModeController = null;
        m_usesSceneInputModeController = false;
    }

    /// <summary>
    /// 트레이너가 사라질 때 커서를 게임플레이 기본값으로 되돌립니다.
    /// </summary>
    /// <remarks>
    /// 다음 씬이 셸터처럼 커서를 쓰는 씬이면 그 씬의 UI가 다시 풀어 줍니다.
    /// 반대로 잠긴 채 넘어가면 커서를 되돌릴 주체가 없어 조작이 막히므로, 잠그는 쪽이 아니라 푸는 쪽으로 둡니다.
    /// </remarks>
    private void RestoreCursorForSceneExit()
    {
        if (!m_open)
        {
            return;
        }

        m_open = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }


    private void Update()
    {
        if (!m_trainerEnabled || !GameDevMode.DebugFeaturesEnabled || !IsSupportedScene())
        {
            if (m_open)
            {
                SetMenuOpen(false);
            }
            return;
        }

        if (m_toggleKey != Key.None
            && Keyboard.current != null
            && Keyboard.current[m_toggleKey].wasPressedThisFrame)
        {
            SetMenuOpen(!m_open);
        }

        if (!m_open)
        {
            return;
        }

        // 창을 눌러 두었으면 키보드는 트레이너 것이고, 배경을 눌렀으면 게임이 받습니다.
        // 마우스는 어느 쪽이든 커서로 남습니다(슬라이더를 만져야 하므로).
        SetKeyboardPassthrough(!m_windowFocused);
        EnforcePassthroughBlocks();

        // 씬이 입력 모드 계약을 제공하면 그 구현이 커서·플레이어 입력을 소유합니다.
        // 계약이 없는 레거시/독립 테스트 씬만 아래 범용 안전장치를 사용합니다.
        if (m_usesSceneInputModeController)
        {
            return;
        }

        // 메뉴가 열려 있는 동안에는 커서를 항상 풀어 둡니다.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // 조작 캐릭터의 입력이 다시 켜졌으면(캐릭터 전환 등) 다시 끕니다.
        // 키보드를 넘겨 준 동안에는 켜져 있는 것이 정상이므로 건드리지 않습니다.
        if (!m_keyboardPassthrough)
        {
            SuppressActivePlayerInput();
        }
    }

    /// <summary>
    /// 창이 선택되지 않은 동안 키보드 입력만 게임으로 넘깁니다.
    /// </summary>
    /// <param name="allow">넘길지 여부입니다. 창을 누르면 거짓이 됩니다.</param>
    /// <remarks>
    /// 트레이너를 열어 둔 채로 걷거나 앉아 보면서 값을 만질 수 있게 하는 장치입니다.
    /// 시점 회전만 막습니다(<see cref="BlockedActionNames"/> 참고).
    ///
    /// 막는 지점이 두 군데인 이유는 씬마다 입력을 끄는 방식이 다르기 때문입니다.
    /// 필드 씬은 <see cref="IInputModeController"/> 계약으로 <see cref="PlayerInputController"/>의
    /// 입력 플래그를 내리고, 계약이 없는 테스트 씬은 <see cref="PlayerInput"/> 자체를 비활성화합니다.
    /// 한쪽만 풀면 다른 씬에서 조용히 아무 일도 일어나지 않습니다.
    ///
    /// 끌 때 액션을 먼저 되살린 뒤 입력을 내리는 순서가 중요합니다. 반대로 하면 개별 액션이
    /// 꺼진 채로 맵이 비활성화되어, 트레이너를 닫고 입력을 되살려도 시점 회전이 돌아오지 않습니다.
    /// </remarks>
    private void SetKeyboardPassthrough(bool allow)
    {
        if (m_keyboardPassthrough == allow)
        {
            return;
        }

        m_keyboardPassthrough = allow;

        if (allow)
        {
            m_passthroughActivatedInputs.Clear();
            m_passthroughBlockedInput = ResolveControlledPlayerInput();

            foreach (PlayerInput playerInput in FindObjectsByType<PlayerInput>(FindObjectsSortMode.None))
            {
                if (!playerInput.inputIsActive)
                {
                    playerInput.ActivateInput();
                    m_passthroughActivatedInputs.Add(playerInput);
                }

                SetBlockedActionsEnabled(playerInput, false);

                // 필드 씬은 PlayerInput이 켜져 있어도 이 플래그가 내려가 있으면 아무 입력도 받지 않습니다.
                playerInput.GetComponent<PlayerInputController>()?.SetGameplayInputWithFreeCursor(true);
            }

            return;
        }

        foreach (PlayerInput playerInput in FindObjectsByType<PlayerInput>(FindObjectsSortMode.None))
        {
            SetBlockedActionsEnabled(playerInput, true);
            playerInput.GetComponent<PlayerInputController>()?.SetGameplayInputWithFreeCursor(false);
        }

        // 이쪽이 켠 것만 되돌립니다. 다만 켜 둔 사이 조작 멤버가 된 입력은 끄지 않습니다. 켤 때는 AI 멤버였어도
        // 지금 조작 중이면 끄는 순간 그 멤버가 움직이지 않습니다.
        PlayerInput controlledInput = ResolveControlledPlayerInput();
        for (int i = 0; i < m_passthroughActivatedInputs.Count; i++)
        {
            PlayerInput playerInput = m_passthroughActivatedInputs[i];
            if (playerInput != null && playerInput.inputIsActive && playerInput != controlledInput)
            {
                playerInput.DeactivateInput();
            }
        }

        m_passthroughActivatedInputs.Clear();
    }

    /// <summary>지금 조작 중인 스쿼드 멤버의 PlayerInput입니다. 스쿼드가 없는 씬이면 <c>null</c>입니다.</summary>
    private static PlayerInput ResolveControlledPlayerInput()
    {
        SquadMemberController member = SquadManager.Instance != null ? SquadManager.Instance.PlayerSquadMember : null;
        return member != null ? member.GetComponent<PlayerInput>() : null;
    }

    /// <summary>
    /// 배경 조작 중에 조작 멤버가 바뀌면 새 멤버의 시점 입력도 막습니다.
    /// </summary>
    /// <remarks>
    /// 켤 때 막은 것은 그 순간의 액션들입니다. 멤버를 바꾸면 새 조작 멤버는 액션맵을 통째로 다시 켜서 막아 둔 시점 회전이
    /// 되살아납니다. 그대로 두면 창을 만지는 동안 마우스로 카메라가 돕니다.
    /// </remarks>
    private void EnforcePassthroughBlocks()
    {
        if (!m_keyboardPassthrough)
        {
            return;
        }

        PlayerInput controlled = ResolveControlledPlayerInput();
        if (controlled != null && controlled != m_passthroughBlockedInput)
        {
            SetBlockedActionsEnabled(controlled, false);
            controlled.GetComponent<PlayerInputController>()?.SetGameplayInputWithFreeCursor(true);
            m_passthroughBlockedInput = controlled;
        }
    }

    /// <summary>배경 조작 중 막아 둘 액션만 켜거나 끕니다.</summary>
    /// <param name="playerInput">대상 입력 컴포넌트입니다.</param>
    /// <param name="enabled">켤지 여부입니다.</param>
    private static void SetBlockedActionsEnabled(PlayerInput playerInput, bool enabled)
    {
        if (playerInput == null || playerInput.actions == null)
        {
            return;
        }

        for (int i = 0; i < BlockedActionNames.Length; i++)
        {
            InputAction action = playerInput.actions.FindAction(BlockedActionNames[i], false);
            if (action == null)
            {
                continue;
            }

            if (enabled)
            {
                action.Enable();
            }
            else
            {
                action.Disable();
            }
        }
    }

    /// <summary>지금 씬이 이 트레이너가 동작해도 되는 씬인지 확인합니다. 필드 트레이너는 필드 씬에서만 동작합니다.</summary>
    /// <remarks>
    /// 판정 기준은 씬 컨트롤러(<see cref="CombatSceneManager"/>)의 종류입니다. 씬 이름으로 판정하면 테스트 씬이 늘어날 때마다
    /// 목록을 고쳐야 하지만, 전투 씬이라면 반드시 이 컨트롤러를 두므로 존재 여부와 종류가 더 안정적인 기준입니다.
    /// 방어전 씬은 <see cref="DefenseDebugTrainer"/>가 맡습니다.
    /// </remarks>
    protected virtual bool IsSupportedScene()
    {
        return TryResolveSceneInputModeController() && CombatSceneManager.Instance is FieldManager;
    }

    /// <summary>씬 컨트롤러를 찾았는지 확인합니다. 파생 트레이너가 자기 씬 판정에 씁니다.</summary>
    protected bool HasSceneInputModeController()
    {
        return TryResolveSceneInputModeController();
    }

    /// <summary>창 상단 탭 이름입니다. 앞의 네 탭(캐릭터·애니메이션·무기·조준)은 모든 트레이너가 같습니다.</summary>
    protected virtual string[] TabNames => MainTabNames;

    /// <summary>
    /// 다섯 번째 탭(씬 탭)의 내용을 그립니다. 필드 트레이너는 좀비 스폰·팀 AI·필드 제어를 그립니다.
    /// 투척물 보급은 무기 탭에 있습니다.
    /// </summary>
    protected virtual void DrawSceneTab()
    {
        DrawEnemySpawnSection();
        GUILayout.Space(6);
        DrawSquadAiControlSection();
        GUILayout.Space(6);
        DrawFieldControlSection();
    }

    // ─────────────────────────────────────────────────────────────
    // 조작 잠금 / 해제
    // ─────────────────────────────────────────────────────────────

    /// <summary>창을 닫고 조작을 돌려줍니다. 결과·게임오버 화면으로 넘어가는 버튼은 먼저 이것을 불러야 합니다.</summary>
    /// <remarks>닫지 않고 넘어가면 창만 가려지고 열린 상태가 남아, 다음 F9가 결과 화면 위에서 조작 모드를 되돌립니다.</remarks>
    protected void CloseMenu()
    {
        SetMenuOpen(false);
    }

    private void SetMenuOpen(bool open)
    {
        if (m_open == open)
        {
            return;
        }

        m_open = open;

        if (m_open)
        {
            // 막 연 창은 선택된 상태로 봅니다. 열자마자 키보드가 게임으로 새면 F9를 누른 손이 그대로 조작이 됩니다.
            m_windowFocused = true;
            m_keyboardPassthrough = false;

            LockControls();
            PrefillSpawnCoordsFromPlayer();
        }
        else
        {
            // 넘겨 준 키보드를 먼저 회수해야 마우스 액션이 꺼진 채로 남지 않습니다.
            SetKeyboardPassthrough(false);
            UnlockControls();
        }
    }

    /// <summary>커서를 풀고 모든 활성 플레이어 입력을 비활성화해 TPS 조작에서 벗어납니다.</summary>
    private void LockControls()
    {
        m_usesSceneInputModeController = TryEnterSceneInputMode();
        if (m_usesSceneInputModeController)
        {
            return;
        }

        LockControlsFallback();
    }

    /// <summary>입력 모드 계약이 없는 테스트 씬을 위한 기존 범용 잠금 처리입니다.</summary>
    private void LockControlsFallback()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        foreach (PlayerInput playerInput in FindObjectsByType<PlayerInput>(FindObjectsSortMode.None))
        {
            playerInput.GetComponent<PlayerInputController>()?.ResetInputState();

            if (playerInput.inputIsActive)
            {
                playerInput.DeactivateInput();
            }
        }

        // 셸터 카메라(CameraLook)가 있으면 회전 입력·재잠금 로직도 함께 잠급니다.
        foreach (CameraLook cameraLook in FindObjectsByType<CameraLook>(FindObjectsSortMode.None))
        {
            cameraLook.SetLookLocked(true);
        }
    }

    /// <summary>커서를 다시 잠그고 현재 조작 캐릭터의 입력을 복구해 TPS 조작으로 돌아갑니다.</summary>
    /// <remarks>
    /// 씬을 벗어나는 중이라면 범용 안전장치를 쓰지 않습니다. 그 처리는 커서를 잠그는데,
    /// 씬이 사라지는 시점에는 되돌려 줄 주체가 없어 다음 씬이 잠긴 커서를 물려받기 때문입니다.
    /// </remarks>
    private void UnlockControls()
    {
        // 조준선 인스펙터가 아직 열려 있으면 게임플레이로 되돌리지 않습니다.
        // 되돌리면 커서가 잠겨 그 창을 클릭할 수 없게 되고, 창이 떠 있는 채로 플레이어 입력이 살아납니다.
        if (CrosshairDebugInspector.IsWindowOpen)
        {
            m_usesSceneInputModeController = false;
            return;
        }

        if (m_usesSceneInputModeController && TryRestoreSceneInputMode())
        {
            m_usesSceneInputModeController = false;
            return;
        }

        m_usesSceneInputModeController = false;

        // 필드 씬이 이미 사라진 뒤라면 복구할 대상도 없습니다. 커서만 풀어 두고 끝냅니다.
        if (!IsSupportedScene())
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        UnlockControlsFallback();
    }

    /// <summary>입력 모드 계약이 없는 테스트 씬을 위한 기존 범용 복구 처리입니다.</summary>
    private void UnlockControlsFallback()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        SquadManager squadManager = FindFirstObjectByType<SquadManager>();
        SquadMemberController player = squadManager != null ? squadManager.PlayerSquadMember : null;

        // 입력 플래그는 스쿼드 전원에게 되돌립니다. PlayerInput만 켜면 컴포넌트 쪽 게이트가 내려간 채로
        // 남아 조작이 통째로 죽습니다(배경 조작을 쓰다 닫은 경우가 그렇습니다). 그리고 배경 조작은
        // 전원에게 걸리므로 복구도 전원에게 가야 합니다. 조작 멤버 하나만 되돌리면 나머지는 내려간 채
        // 남고, 나중에 그 멤버로 전환했을 때 입력이 죽어 있습니다.
        if (squadManager != null)
        {
            squadManager.ApplyInputModeToSquad(true);
        }

        if (player != null)
        {
            PlayerInput playerInput = player.GetComponent<PlayerInput>();
            if (playerInput != null)
            {
                playerInput.ActivateInput();
                playerInput.SwitchCurrentActionMap("Player");
            }

            // 커서는 화면에 하나뿐이라 조작 멤버 쪽에서 한 번만 다룹니다.
            player.GetComponent<PlayerInputController>()?.SetPlayerCursorMode(false);
        }

        foreach (CameraLook cameraLook in FindObjectsByType<CameraLook>(FindObjectsSortMode.None))
        {
            cameraLook.SetLookLocked(false);
        }
    }

    /// <summary>
    /// 씬별 입력 모드 컨트롤러를 한 번 찾고, 이후 F9 토글은 그 계약을 통해 처리합니다.
    /// </summary>
    /// <remarks>
    /// 반환 규약은 <see cref="IInputModeController.SetInputMode"/>와 같습니다.
    /// 정상 실패/비정상 실패면 디버그 트레이너의 범용 안전장치로 되돌아갑니다.
    /// </remarks>
    private bool TrySetSceneInputMode(InputMode mode)
    {
        if (!TryResolveSceneInputModeController())
        {
            return false;
        }

        int result = m_sceneInputModeController.SetInputMode(mode);
        if (result == 1)
        {
            return true;
        }

        Debug.LogWarning($"[RuntimeDebugTrainer] 씬 입력 모드 전환에 실패했습니다. mode={mode}, result={result}. 범용 안전장치로 처리합니다.", this);
        return false;
    }

    /// <summary>트레이너를 열기 전 씬 입력 모드를 기억하고 UI 모드를 요청합니다.</summary>
    private bool TryEnterSceneInputMode()
    {
        if (!TryResolveSceneInputModeController())
        {
            return false;
        }

        m_inputModeBeforeTrainerOpen = m_sceneInputModeController.CurrentInputMode;
        return m_inputModeBeforeTrainerOpen == InputMode.UI
               || TrySetSceneInputMode(InputMode.UI);
    }

    /// <summary>트레이너가 열기 전의 씬 입력 모드로 복구합니다.</summary>
    private bool TryRestoreSceneInputMode()
    {
        return TrySetSceneInputMode(m_inputModeBeforeTrainerOpen);
    }

    /// <summary>현재 씬의 <see cref="FieldManager"/>를 입력 모드 계약 구현체로 캐시합니다.</summary>
    /// <remarks>
    /// 이 트레이너는 필드 전용이므로 계약 구현체를 아무거나 받지 않고 <see cref="FieldManager"/>로 한정합니다.
    /// 셸터에는 <c>ShelterTrainerOverlay</c>가 따로 있어, 임의의 구현체를 잡으면 남의 씬 입력을 건드리게 됩니다.
    /// 캐시는 파괴된 오브젝트를 계속 붙잡지 않도록 Unity의 null 판정으로 매번 확인합니다.
    /// 씬이 바뀌면 이전 씬의 FieldManager가 파괴되므로 여기서 자연히 재탐색이 일어납니다.
    /// </remarks>
    private bool TryResolveSceneInputModeController()
    {
        // MonoBehaviour의 == null은 파괴된 오브젝트도 true를 주므로, 씬 전환으로 죽은 캐시가 걸러집니다.
        if (m_sceneInputModeController is MonoBehaviour cached && cached != null)
        {
            return true;
        }

        m_sceneInputModeController = null;

        CombatSceneManager fieldManager = CombatSceneManager.Instance;
        if (fieldManager == null)
        {
            return false;
        }

        m_sceneInputModeController = fieldManager;
        return true;
    }

    /// <summary>메뉴가 열린 동안 현재 조작 캐릭터의 입력이 다시 활성화됐으면(전환 등) 즉시 비활성화합니다.</summary>
    /// <summary>
    /// 이번 프레임의 마우스 누름 위치로 창이 선택됐는지 판정합니다.
    /// </summary>
    /// <remarks>
    /// IMGUI에는 "이 창이 선택됐는가"를 묻는 API가 없어 누른 지점이 창 안인지로 대신합니다.
    /// 리사이즈 그립과 제목줄도 창 사각형 안이라 함께 선택으로 잡힙니다.
    /// </remarks>
    private void UpdateWindowFocus()
    {
        if (Event.current == null || Event.current.type != EventType.MouseDown)
        {
            return;
        }

        m_windowFocused = m_windowRect.Contains(Event.current.mousePosition);
    }

    private void SuppressActivePlayerInput()
    {
        SquadManager squadManager = FindFirstObjectByType<SquadManager>();
        SquadMemberController player = squadManager != null ? squadManager.PlayerSquadMember : null;
        if (player == null)
        {
            return;
        }

        PlayerInput playerInput = player.GetComponent<PlayerInput>();
        if (playerInput != null && playerInput.inputIsActive)
        {
            player.GetComponent<PlayerInputController>()?.ResetInputState();
            playerInput.DeactivateInput();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // GUI
    // ─────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        // 작전 실패·귀환 오버레이가 떠 있으면 그리지 않습니다. 전역 스킨을 바꾸기 전에 빠져나가므로 되돌릴 것도 없습니다.
        if (!m_trainerEnabled || !GameDevMode.DebugFeaturesEnabled || MissionOverlayVisibility.IsShown)
        {
            return;
        }

        EnsureStyles();

        if (!m_open)
        {
            // 창을 그리지 않고 빠져나갈 때도 전역 스킨은 되돌립니다. 남겨 두면 다른 IMGUI 창이 이 배율을 물려받습니다.
            RestoreSkin();
            return;
        }

        ClampWindowRectToScreen();
        UpdateWindowFocus();

        try
        {
            Rect movedWindow = GUI.Window(GetInstanceID(), m_windowRect, DrawWindow, "런타임 디버그 트레이너");
            m_windowRect.position = movedWindow.position;
        }
        finally
        {
            // 전역 스킨은 반드시 되돌립니다. 남겨 두면 다른 IMGUI 창이 이 배율을 물려받습니다.
            RestoreSkin();
        }
    }

    private void DrawWindow(int windowId)
    {
        GUILayout.Label($"{m_toggleKey} 또는 아래 버튼으로 게임플레이 복귀. (개발 모드에서만 표시)");
        GUILayout.Label(m_keyboardPassthrough
            ? "조작: 게임 (창을 누르면 트레이너로 돌아옵니다. 시점 회전만 잠금)"
            : "조작: 트레이너 (배경을 누르면 이동·사격이 살아납니다. 시점 회전은 잠금)");

        SquadManager squadManager = FindFirstObjectByType<SquadManager>();
        if (squadManager == null)
        {
            GUILayout.Label("SquadManager를 찾을 수 없습니다.");
            if (GUILayout.Button("닫기(게임플레이 복귀)"))
            {
                SetMenuOpen(false);
            }
            DrawWindowFooter();
            DrawWindowChrome(windowId);
            return;
        }

        // 캐릭터 선택과 탭 줄은 스크롤 밖에 둡니다. 어느 탭에 있든 "지금 누구를 보고 있는지"와
        // "어디로 갈 수 있는지"는 늘 보여야 합니다.
        DrawTargetPicker(squadManager);
        GUILayout.Space(4);

        int previousTab = m_mainTabIndex;
        m_mainTabIndex = GUILayout.Toolbar(
            Mathf.Clamp(m_mainTabIndex, 0, TabNames.Length - 1), TabNames);

        // 탭을 옮기면 스크롤을 처음으로 되돌립니다. 그대로 두면 짧은 탭에서 빈 화면이 나옵니다.
        if (m_mainTabIndex != previousTab)
        {
            m_scroll = Vector2.zero;
        }

        GUILayout.Space(4);

        m_scroll = GUILayout.BeginScrollView(m_scroll);

        PlayerbleUnitData target = ResolveTarget(squadManager);
        bool needsTarget = m_mainTabIndex <= 3;

        if (needsTarget && target == null)
        {
            GUILayout.Label("대상 캐릭터 데이터를 찾을 수 없습니다.");
        }
        else
        {
            switch (m_mainTabIndex)
            {
                case 0:
                    DrawCharacterSection(target);
                    break;

                case 1:
                    DrawPlayerAnimationSection(target);
                    break;

                case 2:
                    DrawWeaponSection(target);
                    GUILayout.Space(6);
                    DrawShotRangeDebugSection(target);
                    GUILayout.Space(6);
                    DrawThrowableSection();
                    GUILayout.Space(6);
                    DrawPotionSection();
                    break;

                case 3:
                    DrawCombatFeedbackSection(target);
                    break;

                case 4:
                    DrawSceneTab();
                    break;

                default:
                    DrawDebugFieldSection();
                    break;
            }
        }

        GUILayout.EndScrollView();

        DrawWindowFooter();
        DrawWindowChrome(windowId);
    }

    private void DrawWindowFooter()
    {
        GUILayout.Space(4);
        if (GUILayout.Button("닫기 (게임플레이 복귀)", GUILayout.Height(28)))
        {
            SetMenuOpen(false);
        }

        string exitLabel = Application.isEditor ? "Play Mode 종료" : "빌드 종료";
        if (GUILayout.Button(exitLabel, GUILayout.Height(26)))
        {
            ExitPlayTest();
        }
    }

    private static void ExitPlayTest()
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void DrawWindowChrome(int windowId)
    {
        HandleWindowResize();
        // 좌우 위 테두리는 크기 조절이 먼저 잡아야 하므로 그만큼 안쪽에서 시작합니다.
        GUI.DragWindow(new Rect(
            ResizeBorderSize,
            ResizeBorderSize,
            m_windowRect.width - ResizeBorderSize * 2f - ResizeGripSize,
            24f));
    }

    /// <summary>어느 테두리를 잡고 있는지입니다.</summary>
    /// <remarks>가로와 세로를 따로 두어 모서리를 잡으면 두 축이 함께 움직입니다.</remarks>
    private int m_resizeEdgeX;
    private int m_resizeEdgeY;

    /// <summary>크기 조절을 시작한 시점의 창 위치와 크기입니다.</summary>
    private Rect m_resizeStartRect;

    /// <summary>
    /// 창 테두리를 잡아 크기를 바꿉니다.
    /// </summary>
    /// <remarks>
    /// 일반 창처럼 네 변과 네 모서리 어디를 잡아도 조절됩니다.
    /// 왼쪽이나 위쪽을 잡으면 반대쪽 변이 제자리에 남아야 하므로 위치도 함께 옮깁니다.
    ///
    /// 마우스 좌표는 창 안쪽 기준이라 창을 옮기는 도중에도 값이 흔들리지 않도록
    /// 시작 시점의 화면 좌표를 따로 기억해 두고 그 차이로 계산합니다.
    /// </remarks>
    private void HandleWindowResize()
    {
        Rect inner = new Rect(0f, 0f, m_windowRect.width, m_windowRect.height);

        // 테두리 판정 영역. 제목 표시줄은 창을 옮기는 데 쓰므로 위쪽만 조금 안쪽에서 시작합니다.
        bool onLeft = Event.current.mousePosition.x <= ResizeBorderSize;
        bool onRight = Event.current.mousePosition.x >= inner.width - ResizeBorderSize;
        bool onTop = Event.current.mousePosition.y <= ResizeBorderSize;
        bool onBottom = Event.current.mousePosition.y >= inner.height - ResizeBorderSize;
        bool onEdge = (onLeft || onRight || onTop || onBottom) && inner.Contains(Event.current.mousePosition);

        // 잡을 수 있는 곳임을 알 수 있도록 모서리에 표시를 남깁니다.
        GUI.Label(new Rect(inner.width - ResizeGripSize, inner.height - ResizeGripSize,
                           ResizeGripSize, ResizeGripSize), "↘");

        Event currentEvent = Event.current;
        switch (currentEvent.type)
        {
            case EventType.MouseDown when onEdge:
                m_isResizing = true;
                m_resizeEdgeX = onLeft ? -1 : (onRight ? 1 : 0);
                m_resizeEdgeY = onTop ? -1 : (onBottom ? 1 : 0);
                m_resizeStartMouse = GUIUtility.GUIToScreenPoint(currentEvent.mousePosition);
                m_resizeStartRect = m_windowRect;
                currentEvent.Use();
                break;

            case EventType.MouseDrag when m_isResizing:
                Vector2 delta = GUIUtility.GUIToScreenPoint(currentEvent.mousePosition) - m_resizeStartMouse;
                ApplyResize(delta);
                GUI.changed = true;
                currentEvent.Use();
                break;

            case EventType.MouseUp when m_isResizing:
                m_isResizing = false;
                m_resizeEdgeX = 0;
                m_resizeEdgeY = 0;
                currentEvent.Use();
                break;
        }
    }

    /// <summary>잡은 테두리에 따라 창의 위치와 크기를 갱신합니다.</summary>
    /// <param name="delta">크기 조절을 시작한 지점에서 마우스가 움직인 거리입니다.</param>
    private void ApplyResize(Vector2 delta)
    {
        Rect r = m_resizeStartRect;

        if (m_resizeEdgeX > 0)
        {
            float maxWidth = Mathf.Max(MinWindowWidth, Screen.width - r.x - WindowMargin);
            m_windowRect.width = Mathf.Clamp(r.width + delta.x, MinWindowWidth, maxWidth);
        }
        else if (m_resizeEdgeX < 0)
        {
            // 오른쪽 변을 제자리에 두고 왼쪽만 움직입니다.
            float right = r.x + r.width;
            float maxWidth = Mathf.Max(MinWindowWidth, right - WindowMargin);
            m_windowRect.width = Mathf.Clamp(r.width - delta.x, MinWindowWidth, maxWidth);
            m_windowRect.x = right - m_windowRect.width;
        }

        if (m_resizeEdgeY > 0)
        {
            float maxHeight = Mathf.Max(MinWindowHeight, Screen.height - r.y - WindowMargin);
            m_windowRect.height = Mathf.Clamp(r.height + delta.y, MinWindowHeight, maxHeight);
        }
        else if (m_resizeEdgeY < 0)
        {
            // 아래쪽 변을 제자리에 두고 위쪽만 움직입니다.
            float bottom = r.y + r.height;
            float maxHeight = Mathf.Max(MinWindowHeight, bottom - WindowMargin);
            m_windowRect.height = Mathf.Clamp(r.height - delta.y, MinWindowHeight, maxHeight);
            m_windowRect.y = bottom - m_windowRect.height;
        }
    }

    private void ClampWindowRectToScreen()
    {
        float maxWidth = Mathf.Max(MinWindowWidth, Screen.width - WindowMargin * 2f);
        float maxHeight = Mathf.Max(MinWindowHeight, Screen.height - WindowMargin * 2f);
        m_windowRect.width = Mathf.Clamp(m_windowRect.width, MinWindowWidth, maxWidth);
        m_windowRect.height = Mathf.Clamp(m_windowRect.height, MinWindowHeight, maxHeight);
        m_windowRect.x = Mathf.Clamp(m_windowRect.x, WindowMargin - m_windowRect.width, Screen.width - WindowMargin);
        m_windowRect.y = Mathf.Clamp(m_windowRect.y, WindowMargin, Screen.height - WindowMargin);
    }

    private void DrawTargetPicker(SquadManager squadManager)
    {
        GUILayout.Label("■ 캐릭터 선택 (인스펙터 대상)", m_headerStyle);

        IReadOnlyList<SquadMemberController> members = squadManager.SquadMembers;

        GUILayout.BeginHorizontal();
        if (GUILayout.Toggle(m_targetIndex < 0, "자동(조작중)", "Button") && m_targetIndex >= 0)
        {
            m_targetIndex = -1;
        }

        for (int i = 0; i < members.Count; i++)
        {
            SquadMemberController member = members[i];
            if (member == null)
            {
                continue;
            }

            string label = member.MemberName
                + (member.IsPlayerSquadMember ? " (조작중)" : string.Empty)
                + (!member.IsAlive ? " (사망)" : member.IsDown ? " (다운)" : string.Empty);

            bool selected = m_targetIndex == i;
            if (GUILayout.Toggle(selected, label, "Button") && !selected)
            {
                m_targetIndex = i;
            }
        }
        GUILayout.EndHorizontal();

        // 선택한 캐릭터를 실제로 조작 대상으로 전환.
        if (m_targetIndex >= 0 && m_targetIndex < members.Count && GUILayout.Button("선택한 캐릭터 조작하기"))
        {
            squadManager.SwitchToMember(m_targetIndex);
        }
    }

    private PlayerbleUnitData ResolveTarget(SquadManager squadManager)
    {
        IReadOnlyList<SquadMemberController> members = squadManager.SquadMembers;
        if (m_targetIndex < 0 || m_targetIndex >= members.Count)
        {
            return squadManager.PlayerSquadMemberData;
        }

        SquadMemberController member = members[m_targetIndex];
        return member != null ? member.GetComponent<PlayerbleUnitData>() : null;
    }

    // ─────────────────────────────────────────────────────────────
    // 캐릭터 섹션
    // ─────────────────────────────────────────────────────────────

    private void DrawCharacterSection(PlayerbleUnitData target)
    {
        GUILayout.Label($"■ 캐릭터: {target.DisplayName}", m_headerStyle);

        PlayerHealth health = target.GetComponent<PlayerHealth>();
        if (health != null)
        {
            GUILayout.Label($"HP {health.CurrentHP} / {health.MaxHP}  " +
                $"(다운={health.IsDowned}, 사망={health.IsDead}, 부활 {health.ReviveCount}/{health.MaxReviveCount})");

            target.DebugInfiniteHealth = GUILayout.Toggle(target.DebugInfiniteHealth, " 무한 체력(무적)");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("체력 풀회복"))
            {
                health.SetCurrentHP(health.MaxHP);
            }
            if (GUILayout.Button("즉시 기절"))
            {
                health.Debug_InstantDown();
            }
            if (GUILayout.Button("전투 이탈"))
            {
                health.Debug_InstantCombatOut();
            }
            if (GUILayout.Button("즉시 부활"))
            {
                health.Debug_InstantRevive();
            }
            GUILayout.EndHorizontal();
        }
        else
        {
            GUILayout.Label("PlayerHealth를 찾을 수 없습니다.");
        }

        ThirdPersonController controller = target.GetComponent<ThirdPersonController>();
        if (controller != null)
        {
            controller.SetMoveSpeed(SliderRow("이동 속도", controller.MoveSpeed, 0f, 15f));
            controller.SetSprintSpeed(SliderRow("질주 속도", controller.SprintSpeed, 0f, 20f));
            controller.SetMovementDirectionTurnSpeed(
                SliderRow("이동 방향 각속도", controller.MovementDirectionTurnSpeed, 0f, 1080f));
            controller.SetMovementDirectionSmoothMaxAngle(
                SliderRow("일반 방향 보간 최대 각도", controller.MovementDirectionSmoothMaxAngle, 0f, 180f));
            controller.SetJumpHeight(SliderRow("점프 높이", controller.JumpHeight, 0f, 5f));

            DrawReverseSyncButton(controller, "이동/반동");
        }
        else
        {
            GUILayout.Label("ThirdPersonController를 찾을 수 없습니다(이동 스탯 조절 불가).");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 무기 섹션
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 플레이어 애니메이션 블렌드 시간을 조절하는 구역입니다.
    /// </summary>
    /// <remarks>
    /// 값을 레이어별로 묶어 둡니다. 어느 레이어를 만지는지가 곧 "무엇이 바뀌는지"이기 때문입니다.
    /// 하체는 Base Layer의 블렌드 트리 파라미터라 걷기 자체의 이음새를 정하고,
    /// 상체는 재장전·반동 레이어의 weight라 그 동작이 들고 나는 속도를 정합니다.
    ///
    /// 여기서 바꾼 값은 Play Mode를 벗어나면 사라집니다. 마음에 드는 값이 나오면 인스펙터에 옮겨야 합니다.
    /// </remarks>
    private void DrawPlayerAnimationSection(PlayerbleUnitData target)
    {
        GUILayout.Label("■ 플레이어 애니메이션", m_headerStyle);

        ThirdPersonController controller = target.GetComponent<ThirdPersonController>();
        AimController aimController = target.GetComponent<AimController>();

        if (controller == null && aimController == null)
        {
            GUILayout.Label("ThirdPersonController와 AimController를 찾을 수 없습니다.");
            return;
        }

        if (controller != null)
        {
            GUILayout.Label("하체 이동 보간 (Base 레이어)", m_headerStyle);
            controller.SetMoveStartStopDuration(
                SliderRow("정지↔이동", controller.MoveStartStopDuration, 0f, 0.6f));
            controller.SetMoveDirectionDamp(
                SliderRow("방향 조정", controller.MoveDirectionDamp, 0f, 0.6f));
            controller.SetMoveDirectionFlipDuration(
                SliderRow("방향 교체(좌우 반전)", controller.MoveDirectionFlipDuration, 0f, 0.4f));
            controller.SetMoveDirectionSnapAngle(
                SliderRow("교체 판정 각도", controller.MoveDirectionSnapAngle, 0f, 180f));
            controller.SetMoveStateBlendDuration(
                SliderRow("웅크림/조깅/달리기", controller.MoveStateBlendDuration, 0f, 0.8f));
        }
        else
        {
            GUILayout.Label("ThirdPersonController를 찾을 수 없습니다(하체 보간 조절 불가).");
        }

        if (aimController != null)
        {
            GUILayout.Space(3);
            GUILayout.Label("상체 레이어", m_headerStyle);
            // 이 값은 재장전이 아니라 전투 자세 전환(리그·상체 레이어 가중치) 블렌드 시간입니다.
            // 총을 들고 내리는 속도를 정하고, 그래서 "총 드는 중 사격 차단"이 풀리는 체감에도 영향을 줍니다.
            aimController.SetStanceBlendDuration(
                SliderRow("전투 자세 전환(총 들기/내리기)", aimController.StanceBlendDuration, 0f, 1f));
            aimController.SetRecoilAnimationWeight(
                SliderRow("반동 동작 세기", aimController.RecoilAnimationWeight, 0f, 1f));
        }
        else
        {
            GUILayout.Label("AimController를 찾을 수 없습니다(상체 레이어 조절 불가).");
        }
    }

    private void DrawWeaponSection(PlayerbleUnitData target)
    {
        GUILayout.Label($"■ 무기: {target.CurrentWeaponName}", m_headerStyle);

        Gun weapon = target.Gun;
        if (weapon == null)
        {
            GUILayout.Label("Gun를 찾을 수 없습니다.");
            return;
        }

        GUILayout.Label($"장전 {weapon.CurrentBullet} / {weapon.MaxBullet},  예비 {target.ReserveAmmo} / {target.MaxReserveAmmo}");

        GUILayout.BeginHorizontal();
        target.DebugInfiniteMagazine = GUILayout.Toggle(target.DebugInfiniteMagazine, " 무한 장탄수");
        target.DebugInfiniteReserveAmmo = GUILayout.Toggle(target.DebugInfiniteReserveAmmo, " 무한 예비탄");
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("탄창 가득"))
        {
            weapon.SetCurrentBullet(weapon.MaxBullet);
        }
        if (GUILayout.Button("예비탄 가득"))
        {
            target.SetReserveAmmo(target.MaxReserveAmmo);
        }
        GUILayout.EndHorizontal();

        weapon.SetMaxBullet(Mathf.RoundToInt(SliderRow("탄창 용량", weapon.MaxBullet, 1f, 200f, "0")));
        weapon.SetCurrentBullet(Mathf.RoundToInt(SliderRow("현재 탄약", weapon.CurrentBullet, 0f, weapon.MaxBullet, "0")));
        target.SetMaxReserveAmmo(Mathf.RoundToInt(SliderRow("예비탄 최대", target.MaxReserveAmmo, 0f, 999f, "0")));
        weapon.SetAllowFullMagReload(GUILayout.Toggle(weapon.AllowFullMagReload, " 풀 탄창 재장전 허용"));

        DrawDamageRows(weapon);
        weapon.SetHeadshotDamageMultiplier(SliderRow("헤드샷 배율", weapon.HeadshotDamageMultiplier, 1f, 5f));
        weapon.SetShootDelay(SliderRow("사격 딜레이(초)", weapon.ShootDelay, 0.02f, 1f));
        weapon.SetReloadTime(SliderRow("재장전(초)", weapon.ReloadTime, 0f, 5f));
        // 재장전 시간이 정본이고 애니메이션 배속이 거기에 맞춰 역산됩니다. 조정 결과를 바로 볼 수 있게 함께 표시합니다.
        AimController reloadAim = target.GetComponent<AimController>();
        if (reloadAim != null)
        {
            GUILayout.Label($"   └ 재장전 애니메이션 배속: {reloadAim.ReloadAnimationSpeed:0.##}배 (1 = 클립 원래 속도)");
        }
        weapon.SetHitscanRange(SliderRow("사거리", weapon.HitscanRange, 10f, 300f, "0"));
        weapon.SetHitscanLayerMask(DrawLayerMaskRow("무기 명중 레이어", weapon.HitscanLayerMask, ref m_showWeaponHitscanLayers));

        weapon.SetRecoilPitchKick(SliderRow("반동(상하)", weapon.RecoilPitchKick, 0f, 5f));
        weapon.SetRecoilYawKick(SliderRow("반동(좌우)", weapon.RecoilYawKick, 0f, 5f));
        weapon.SetYawKickPattern(DrawKickSidePattern("좌우 반동 패턴", weapon.YawKickPattern));
        weapon.SetRecoilRoll(SliderRow("카메라 롤 킥", weapon.RecoilRoll, 0f, 5f));
        weapon.SetRollKickPattern(DrawKickSidePattern("카메라 롤 패턴", weapon.RollKickPattern));
        weapon.SetRecoilFovPunch(SliderRow("카메라 FOV 펀치(힙)", weapon.RecoilFovPunch, 0f, 10f));
        // 조준 중에는 화면이 확대돼 같은 값도 더 크게 보이므로 무기가 자세별로 따로 들고 있습니다.
        weapon.SetRecoilFovPunchAds(SliderRow("카메라 FOV 펀치(ADS)", weapon.RecoilFovPunchAds, 0f, 10f));

        GUILayout.Label("힙파이어 자세별 탄퍼짐", m_headerStyle);
        float hipMin = SliderRow("힙/서기 최소", weapon.HipfireMinSpread, 0f, 20f);
        float hipMax = SliderRow("힙/서기 최대", weapon.HipfireMaxSpread, 0f, 20f);
        weapon.SetHipfireSpread(hipMin, hipMax);
        float hipCrouchMin = SliderRow("힙/앉기 최소", weapon.HipfireCrouchMinSpread, 0f, 20f);
        float hipCrouchMax = SliderRow("힙/앉기 최대", weapon.HipfireCrouchMaxSpread, 0f, 20f);
        weapon.SetHipfireCrouchSpread(hipCrouchMin, hipCrouchMax);
        float hipAirborneMin = SliderRow("힙/공중 최소", weapon.HipfireAirborneMinSpread, 0f, 20f);
        float hipAirborneMax = SliderRow("힙/공중 최대", weapon.HipfireAirborneMaxSpread, 0f, 20f);
        weapon.SetHipfireAirborneSpread(hipAirborneMin, hipAirborneMax);

        GUILayout.Label("ADS 자세별 탄퍼짐", m_headerStyle);
        float adsMin = SliderRow("ADS/서기 최소", weapon.AdsMinSpread, 0f, 20f);
        float adsMax = SliderRow("ADS/서기 최대", weapon.AdsMaxSpread, 0f, 20f);
        weapon.SetAdsSpread(adsMin, adsMax);
        float adsCrouchMin = SliderRow("ADS/앉기 최소", weapon.AdsCrouchMinSpread, 0f, 20f);
        float adsCrouchMax = SliderRow("ADS/앉기 최대", weapon.AdsCrouchMaxSpread, 0f, 20f);
        weapon.SetAdsCrouchSpread(adsCrouchMin, adsCrouchMax);
        float adsAirborneMin = SliderRow("ADS/공중 최소", weapon.AdsAirborneMinSpread, 0f, 20f);
        float adsAirborneMax = SliderRow("ADS/공중 최대", weapon.AdsAirborneMaxSpread, 0f, 20f);
        weapon.SetAdsAirborneSpread(adsAirborneMin, adsAirborneMax);

        DrawBurstSpreadSection(weapon);
    }

    /// <summary>
    /// 사격 경로·범위를 그리는 디버그 선들을 켜고 끕니다.
    /// </summary>
    /// <remarks>
    /// 기본값은 모두 꺼져 있습니다. 대원을 바꿔도 같은 상태가 유지되도록 선택한 대원의 값을 보여 주고,
    /// 바꾸면 모든 대원과 무기에 같이 적용합니다. 선은 Scene 뷰와 Gizmos를 켠 Game 뷰에서 보입니다.
    /// </remarks>
    private void DrawShotRangeDebugSection(PlayerbleUnitData target)
    {
        AimController aim = target.GetComponent<AimController>();
        Gun weapon = target.Gun;
        if (aim == null && weapon == null)
        {
            return;
        }

        GUILayout.Label("사격 범위 표시 (Scene 뷰 / Gizmos 켠 Game 뷰)", m_headerStyle);

        if (weapon != null)
        {
            bool shotRay = GUILayout.Toggle(weapon.DebugDrawShotRay, " 발사 레이 (총구→탄착점, 1초)");
            if (shotRay != weapon.DebugDrawShotRay)
            {
                foreach (Gun gun in FindObjectsByType<Gun>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    gun.DebugDrawShotRay = shotRay;
                }
            }
        }

        if (aim == null)
        {
            return;
        }

        AimController[] aims = FindObjectsByType<AimController>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        bool hitscanRay = GUILayout.Toggle(aim.HitscanDebugRayEnabled, " 조준 히트스캔 레이 (총구→조준 탄착점)");
        if (hitscanRay != aim.HitscanDebugRayEnabled)
        {
            foreach (AimController other in aims)
            {
                other.SetDrawHitscanDebugRay(hitscanRay);
            }
        }

        bool traceLine = GUILayout.Toggle(aim.DrawAimTraceLine, " 조준 트레이스 선 (카메라→조준점)");
        if (traceLine != aim.DrawAimTraceLine)
        {
            foreach (AimController other in aims)
            {
                other.SetDrawAimTraceLine(traceLine);
            }
        }

        bool forwardRay = GUILayout.Toggle(aim.DrawCameraForwardRay, " 카메라 전방 레이 (지향점/렌더 방향)");
        if (forwardRay != aim.DrawCameraForwardRay)
        {
            foreach (AimController other in aims)
            {
                other.SetDrawCameraForwardRay(forwardRay);
            }
        }
    }

    /// <summary>
    /// 무기 피해를 조절하는 줄을 그립니다. 거리 감쇠 표가 고정 피해 모드면 구간별 고정 피해를, 아니면 기본 피해를 조절합니다.
    /// </summary>
    /// <remarks>
    /// 고정 피해 모드에서는 기본 피해(<see cref="Gun.HitscanDamage"/>)가 실제 피해에 쓰이지 않습니다. 그 슬라이더를 보여 주면
    /// 움직여도 아무 일도 없어서 고장으로 보입니다. 기본 피해가 0이면 사격 판정이 꺼지는 점은 같아 그 경우만 안내합니다.
    /// </remarks>
    private void DrawDamageRows(Gun weapon)
    {
        DamageFalloffTable falloff = weapon.DamageFalloff;
        if (falloff == null || falloff.IsEmpty || falloff.Mode != DamageFalloffMode.FlatDamage)
        {
            weapon.SetHitscanDamage(Mathf.RoundToInt(SliderRow("데미지", weapon.HitscanDamage, 0f, 100f, "0")));
            return;
        }

        GUILayout.Label("데미지: 거리 구간별 고정 피해 (기본 피해 값은 쓰이지 않음)");
        for (int i = 0; i < falloff.Steps.Count; i++)
        {
            DamageFalloffStep step = falloff.Steps[i];
            // 움직였을 때만 0.1 단위로 맞춰 넣습니다. 슬라이더 그대로면 2.4837 같은 값이 들어가 수치를 맞추기 어렵고,
            // 움직이지 않았는데 반올림하면 원래 값(예: 2.45)이 바뀝니다.
            float raw = SliderRow($"  ~{step.MaxDistance:0}m", step.FlatDamage, 0f, 50f, "0.0");
            if (!Mathf.Approximately(raw, step.FlatDamage))
            {
                falloff.DebugSetStepFlatDamage(i, Mathf.Round(raw * 10.0f) / 10.0f);
            }
        }

        if (weapon.HitscanDamage <= 0)
        {
            GUILayout.Label("  기본 피해가 0이라 사격 판정이 꺼져 있습니다.");
        }
    }

    private void DrawBurstSpreadSection(Gun weapon)
    {
        GUILayout.Space(3);
        GUILayout.Label("연사 탄퍼짐 (실제 탄착)", m_headerStyle);
        GUILayout.Label($"현재 힙 {weapon.GetCurrentSpread(false):0.##}° / ADS {weapon.GetCurrentSpread(true):0.##}°");

        weapon.SetHipfireBurstSpread(
            Mathf.RoundToInt(SliderRow("힙 정확 발수", weapon.HipfireMinSpreadShotCount, 0f, 20f, "0")),
            SliderRow("힙 발당 증가", weapon.HipfireSpreadIncreasePerShot, 0f, 10f),
            SliderRow("힙 회복 지연", weapon.HipfireSpreadRecoveryDelay, 0f, 3f),
            SliderRow("힙 초당 회복", weapon.HipfireSpreadRecoveryPerSecond, 0f, 30f));

        weapon.SetAdsBurstSpread(
            Mathf.RoundToInt(SliderRow("ADS 정확 발수", weapon.AdsMinSpreadShotCount, 0f, 20f, "0")),
            SliderRow("ADS 발당 증가", weapon.AdsSpreadIncreasePerShot, 0f, 10f),
            SliderRow("ADS 회복 지연", weapon.AdsSpreadRecoveryDelay, 0f, 3f),
            SliderRow("ADS 초당 회복", weapon.AdsSpreadRecoveryPerSecond, 0f, 30f));

        SpreadDistribution distribution =
            (SpreadDistribution)GUILayout.SelectionGrid(
                (int)weapon.Distribution,
                new[] { "균일 분포", "중심 집중" },
                2);
        weapon.SetSpreadDistribution(distribution);
        weapon.SetSpreadConcentration(SliderRow("중심 집중도", weapon.SpreadConcentration, 1f, 10f));

        DrawReverseSyncButton(weapon, "이 무기");
    }

    /// <summary>
    /// 지금 값을 밸런스 SO와 CSV로 되돌려 쓰는 버튼을 그립니다.
    /// </summary>
    /// <remarks>
    /// 에디터에서만 그립니다. SO와 CSV는 프로젝트 자산이라 빌드에서는 쓸 수 없어,
    /// 버튼을 보여 주면 눌러도 아무 일이 없는 것처럼 보입니다.
    ///
    /// 플레이 중에 쓴 값은 Play Mode를 끝내면 사라지므로, 마음에 드는 값을 찾았으면
    /// 끝내기 전에 눌러 정본으로 올려야 합니다.
    /// </remarks>
    private void DrawReverseSyncButton(Component target, string label)
    {
        if (target == null || !BalanceReverseSyncHook.IsAvailable)
        {
            return;
        }

        if (!BalanceReverseSyncHook.CanRun(target))
        {
            GUILayout.Label($"{label}에 개별 SO도, 엔티티 통합 SO도 없어 갱신할 수 없습니다.");
            return;
        }

        GUILayout.Space(3);
        if (GUILayout.Button($"{label} 현재 값 → SO + CSV 갱신", GUILayout.Height(24)))
        {
            m_reverseSyncMessage = BalanceReverseSyncHook.Run(target);
            Debug.Log($"[BalanceReverseSync] {m_reverseSyncMessage}", target);
        }

        if (!string.IsNullOrEmpty(m_reverseSyncMessage))
        {
            GUILayout.Label(m_reverseSyncMessage);
        }
    }

    private void DrawCombatFeedbackSection(PlayerbleUnitData target)
    {
        AimController aimController = target.GetComponent<AimController>();
        if (aimController == null)
        {
            GUILayout.Label("AimController를 찾을 수 없습니다. 반동/카메라 킥/임팩트 마커 조절은 사용할 수 없습니다.");
            return;
        }

        GUILayout.Label("사격 피드백", m_headerStyle);

        DrawAimHandlingSection(aimController);
        DrawLogicalRecoilSection(target);

        aimController.SetAimRecoilEnabled(
            GUILayout.Toggle(aimController.AimRecoilEnabled, " 반동 적용 (탄착 영향)"));
        aimController.SetVisualKickEnabled(
            GUILayout.Toggle(aimController.VisualKickEnabled, " 카메라 킥 적용 (롤/FOV, 탄착 무영향)"));

        AimController.VisualKickRecoveryMode selectedRecoveryMode =
            (AimController.VisualKickRecoveryMode)GUILayout.SelectionGrid(
                (int)aimController.CurrentVisualKickRecoveryMode,
                new[] { "반동 회복 공유", "발당 리셋" },
                2);
        aimController.SetVisualKickRecoveryMode(selectedRecoveryMode);
        aimController.SetVisualKickRecoverShots(
            SliderRow("카메라 킥 회복 발수", aimController.VisualKickRecoverShots, 0.1f, 10f));
        aimController.SetVisualKickMaxRoll(
            SliderRow("카메라 롤 상한", aimController.VisualKickMaxRoll, 0f, 15f));
        aimController.SetVisualKickMaxFovPunch(
            SliderRow("FOV 펀치 상한", aimController.VisualKickMaxFovPunch, 0f, 20f));

        GUILayout.Space(3);
        aimController.SetImpactMarkerEnabled(
            GUILayout.Toggle(aimController.ImpactMarkerEnabled, " 임팩트 마커 생성"));
        if (aimController.ImpactMarkerEnabled)
        {
            GUILayout.Label("마커 프리팹이 없으면 작은 마젠타 스피어를 임시 생성합니다.");
            aimController.SetImpactMarkerLifetime(
                SliderRow("마커 유지 시간", aimController.ImpactMarkerLifetime, 0.05f, 10f));
            aimController.SetImpactMarkerSize(
                SliderRow("마커 크기", aimController.ImpactMarkerSize, 0.01f, 2f));
        }

        DrawAimDebugSection(aimController);
        DrawCrosshairFeedbackSection(aimController.CrosshairController, aimController);
    }

    private void DrawAimHandlingSection(AimController aimController)
    {
        GUILayout.Label("조준 / 히트스캔", m_headerStyle);
        aimController.SetTargetLayer(DrawLayerMaskRow("조준점 레이어", aimController.TargetLayer, ref m_showAimTargetLayers));
        aimController.SetHipfireHoldDuration(SliderRow("힙파이어 자세 유지", aimController.HipfireHoldDuration, 0f, 5f));
        aimController.SetAdsFov(SliderRow("ADS FOV", aimController.AdsFov, 1f, 90f));
        aimController.SetHipfireFov(SliderRow("힙파이어 FOV", aimController.HipfireFov, 1f, 120f));
        // 이 속도는 줌 곡선을 끈 경우에만 쓰입니다. 곡선을 쓰는 동안 슬라이더를 보여 주면
        // 움직여도 아무 변화가 없어 "고장난 값"처럼 보입니다.
        if (aimController.UseZoomEnvelope)
        {
            GUILayout.Label("FOV 전환 속도: 미사용 (ADS 확대·축소 곡선 사용 중)");
        }
        else
        {
            aimController.SetZoomLerpSpeed(SliderRow("FOV 전환 속도", aimController.ZoomLerpSpeed, 0f, 40f));
        }

        GUILayout.Label(
            $"총 드는 중 사격 차단: {(aimController.IsRaisingWeapon ? "차단 중" : "해제")} " +
            $"(최대 {aimController.WeaponRaiseDuration:0.###}초)");
    }

    /// <summary>
    /// 조준 관련 표시 설정을 그립니다.
    /// </summary>
    /// <remarks>
    /// 여기 남은 둘은 <b>기즈모가 아닙니다.</b> 조준선 상시 표시는 UI 설정이고, 지향점 거리는 조준 계산에
    /// 실제로 쓰이는 값입니다. 둘 다 Debug 구역 소속이 아니라 빌드에서도 의미가 있으므로 트레이너에 남깁니다.
    /// 차단 마커는 월드 오브젝트에서 화면 UI로 옮겨가면서 표면 오프셋이 사라졌고, 크기·색은
    /// <see cref="CrosshairController"/>의 Block Marker 구역이 소유합니다.
    ///
    /// 기즈모 토글(총구→탄착점 레이, 각종 스피어 등)은 <see cref="DebugSectionRegistry"/>가 모아
    /// 아래 디버그 항목 섹션에서 한 번에 다룹니다. 기즈모는 소유 컴포넌트의 Debug 구역에 선언되어 있어
    /// 여기에 손으로 다시 나열하면 필드가 늘 때마다 두 곳을 고쳐야 하고, 빠뜨리면 조용히 누락됩니다.
    /// </remarks>
    private void DrawAimDebugSection(AimController aimController)
    {
        GUILayout.Space(3);
        GUILayout.Label("조준 표시", m_headerStyle);
        aimController.SetShowAimImageAlways(
            GUILayout.Toggle(aimController.ShowAimImageAlways, " 조준선을 항상 표시"));
        aimController.SetLookDistance(SliderRow("지향점 거리", aimController.LookDistance, 0f, 500f, "0"));
    }

    private void DrawLogicalRecoilSection(PlayerbleUnitData target)
    {
        ThirdPersonController controller = target.GetComponent<ThirdPersonController>();
        if (controller == null)
        {
            return;
        }

        GUILayout.Space(3);
        GUILayout.Label("실제 조준 반동 (탄착 영향)", m_headerStyle);
        controller.SetRecoilRecoverySpeed(SliderRow("반동 회복 속도", controller.RecoilRecoverySpeed, 0f, 40f));
        controller.SetRecoilRecoveryDelay(SliderRow("반동 회복 지연", controller.RecoilRecoveryDelay, 0f, 2f));
        controller.SetRecoilOnsetInterpolationEnabled(
            GUILayout.Toggle(controller.RecoilOnsetInterpolationEnabled, " 반동 온셋 보간"));
        if (controller.RecoilOnsetInterpolationEnabled)
        {
            controller.SetRecoilOnsetSpeed(SliderRow("온셋 보간 속도", controller.RecoilOnsetSpeed, 0f, 100f));
        }

        controller.SetRecoilCompensationAbsorbEnabled(
            GUILayout.Toggle(controller.RecoilCompensationAbsorbEnabled, " 되잡기 입력 우선 상쇄"));
        controller.SetPitchRecoveryRatio(SliderRow("피치 자동 회복 비율", controller.PitchRecoveryRatio, 0f, 1f));
        controller.SetYawRecoveryRatio(SliderRow("요 자동 회복 비율", controller.YawRecoveryRatio, 0f, 1f));
        controller.SetUsePitchOffsetCap(GUILayout.Toggle(controller.UsePitchOffsetCap, " 피치 반동 상한 사용"));
        if (controller.UsePitchOffsetCap)
        {
            controller.SetRecoilMaxPitch(SliderRow("피치 반동 상한", controller.RecoilMaxPitch, 0f, 30f));
        }

        controller.SetUseYawOffsetCap(GUILayout.Toggle(controller.UseYawOffsetCap, " 요 반동 상한 사용"));
        if (controller.UseYawOffsetCap)
        {
            controller.SetRecoilMaxYaw(SliderRow("요 반동 상한", controller.RecoilMaxYaw, 0f, 30f));
        }

        // 엔벨로프 섹션은 상한 토글·슬라이더 쌍을 모두 그린 뒤에 옵니다.
        // 사이에 끼어 있으면 "요 반동 상한" 슬라이더가 자기 토글과 떨어져 다른 항목처럼 보입니다.
        DrawRecoilEnvelopeSection(controller);

        GUILayout.Label($"현재 반동 오프셋: Pitch {controller.CurrentRecoilPitchOffset:0.##} / Yaw {controller.CurrentRecoilYawOffset:0.##}");
        if (GUILayout.Button("현재 자동 반동 오프셋 제거"))
        {
            controller.ClearRecoilOffsets();
        }
    }

    private void DrawCrosshairFeedbackSection(CrosshairController crosshairController, AimController aimController)
    {
        if (crosshairController == null)
        {
            return;
        }

        GUILayout.Space(3);
        GUILayout.Label("크로스헤어 피드백 (표시 전용)", m_headerStyle);
        crosshairController.SetSpreadAccuracyEnabled(
            GUILayout.Toggle(crosshairController.UseSpreadAccuracy, " 탄퍼짐 간격 표시"));

        // 표시 기준은 캐릭터(AimController)가 소유하고 HUD는 매 프레임 그 값으로 덮어씁니다. HUD를 직접 바꾸면 다음 프레임에 돌아갑니다.
        var basis = (CrosshairController.SpreadDisplayBasis)GUILayout.SelectionGrid(
            (int)crosshairController.CurrentSpreadDisplayBasis,
            new[] { "콘 경계", "대부분", "일반", "코어" },
            2);
        if (aimController != null)
        {
            aimController.CrosshairSpreadDisplayBasis = basis;
        }

        crosshairController.SetSpreadDisplayBasis(basis);
        crosshairController.SetClampToMaxGap(
            GUILayout.Toggle(crosshairController.ClampToMaxGap, " 최대 간격 제한"));
        if (crosshairController.ClampToMaxGap)
        {
            crosshairController.SetMaxGapPixels(SliderRow("최대 간격(px)", crosshairController.MaxGapPixels, 0f, 600f, "0"));
        }

        crosshairController.SetSpreadLerpSpeed(SliderRow("간격 보간 속도", crosshairController.SpreadLerpSpeed, 0f, 50f));

        crosshairController.HitMarkerRandomRollEnabled =
            GUILayout.Toggle(crosshairController.HitMarkerRandomRollEnabled, " 히트마커 발당 무작위 회전");
        crosshairController.HitMarkerRollBaseDegrees =
            SliderRow("히트마커 기준 각도", crosshairController.HitMarkerRollBaseDegrees, -180f, 180f);
        if (crosshairController.HitMarkerRandomRollEnabled)
        {
            crosshairController.HitMarkerRollRandomRangeDegrees =
                SliderRow("히트마커 흔들림 범위(±)", crosshairController.HitMarkerRollRandomRangeDegrees, 0f, 180f);
        }

        crosshairController.HitMarkerDamageScaleEnabled =
            GUILayout.Toggle(crosshairController.HitMarkerDamageScaleEnabled, " 히트마커 길이 딜량 비례");
        if (crosshairController.HitMarkerDamageScaleEnabled)
        {
            crosshairController.HitMarkerMinDamage =
                SliderRow("비례 최소 딜량", crosshairController.HitMarkerMinDamage, 0f, 50f);
            crosshairController.HitMarkerMaxDamage =
                SliderRow("비례 최대 딜량", crosshairController.HitMarkerMaxDamage, 0f, 50f);
            crosshairController.HitMarkerLengthAtMinDamagePixels =
                SliderRow("최소 딜량 길이(px)", crosshairController.HitMarkerLengthAtMinDamagePixels, 0f, 120f);
            crosshairController.HitMarkerLengthAtMaxDamagePixels =
                SliderRow("최대 딜량 길이(px)", crosshairController.HitMarkerLengthAtMaxDamagePixels, 0f, 120f);
        }
    }

    /// <summary>
    /// 반동·킥 엔벨로프의 사용 여부와 지속시간, 그리고 곡선 생성기를 그립니다.
    /// </summary>
    /// <remarks>
    /// IMGUI에는 곡선 편집기가 없어 곡선 모양은 여기서 직접 못 만집니다.
    /// 대신 피크 위치·피크 세기·완급을 숫자로 두고, 적용 버튼을 눌렀을 때만 3키 곡선을 다시 만듭니다.
    /// 버튼을 눌러야만 바뀌게 한 이유는, 매 프레임 재생성하면 인스펙터에서 손으로 그린 곡선을
    /// 조용히 덮어써 저작 결과가 사라지기 때문입니다.
    ///
    /// 지속시간과 세기는 곡선을 재생성하지 않고 시간축·진폭만 늘리고 줄이므로 실시간으로 계속 돌려볼 수 있습니다.
    /// </remarks>
    private void DrawRecoilEnvelopeSection(ThirdPersonController controller)
    {
        GUILayout.Space(4);
        m_showRecoilEnvelope = GUILayout.Toggle(m_showRecoilEnvelope,
            m_showRecoilEnvelope ? " ▼ 반동·킥 엔벨로프" : " ▶ 반동·킥 엔벨로프", m_headerStyle);

        if (!m_showRecoilEnvelope)
        {
            return;
        }

        controller.SetUsePitchRecoilEnvelope(
            GUILayout.Toggle(controller.UsePitchRecoilEnvelope, " 상하 반동 곡선 사용 (끄면 즉발+속도 회복)"));
        if (controller.UsePitchRecoilEnvelope)
        {
            controller.SetPitchRecoilEnvelopeDuration(
                SliderRow("상하 곡선 길이(초)", controller.PitchRecoilEnvelopeDuration, 0.02f, 2f));
        }

        controller.SetUseYawRecoilEnvelope(
            GUILayout.Toggle(controller.UseYawRecoilEnvelope, " 좌우 반동 곡선 사용"));
        if (controller.UseYawRecoilEnvelope)
        {
            controller.SetYawRecoilEnvelopeDuration(
                SliderRow("좌우 곡선 길이(초)", controller.YawRecoilEnvelopeDuration, 0.02f, 2f));
        }

        AimController aimController = controller.GetComponent<AimController>();
        if (aimController != null)
        {
            aimController.SetUseRollKickEnvelope(
                GUILayout.Toggle(aimController.UseRollKickEnvelope, " 카메라 롤 곡선 사용"));
            if (aimController.UseRollKickEnvelope)
            {
                aimController.SetRollKickEnvelopeDuration(
                    SliderRow("롤 곡선 길이(초)", aimController.RollKickEnvelopeDuration, 0.02f, 2f));
            }

            aimController.SetUseFovPunchEnvelope(
                GUILayout.Toggle(aimController.UseFovPunchEnvelope, " FOV 펀치 곡선 사용"));
            if (aimController.UseFovPunchEnvelope)
            {
                aimController.SetHipfireFovPunchEnvelopeDuration(
                    SliderRow("힙 펀치 길이(초)", aimController.HipfireFovPunchEnvelopeDuration, 0.02f, 2f));
                aimController.SetAdsFovPunchEnvelopeDuration(
                    SliderRow("ADS 펀치 길이(초)", aimController.AdsFovPunchEnvelopeDuration, 0.02f, 2f));
            }

            aimController.SetUseZoomEnvelope(
                GUILayout.Toggle(aimController.UseZoomEnvelope, " ADS 확대·축소 곡선 사용 (끄면 속도 공용)"));
            if (aimController.UseZoomEnvelope)
            {
                aimController.SetZoomInDuration(
                    SliderRow("확대 시간(초)", aimController.ZoomInDuration, 0.01f, 1f));
                aimController.SetZoomOutDuration(
                    SliderRow("축소 시간(초)", aimController.ZoomOutDuration, 0.01f, 1f));
            }
        }

        GUILayout.Space(4);
        GUILayout.Label("곡선 생성기 (누른 축의 곡선을 다시 만듭니다)");
        m_envelopePeakRatio = SliderRow("피크 위치(0~1)", m_envelopePeakRatio, 0.01f, 0.99f);
        m_envelopePeakValue = SliderRow("피크 세기(1=목표)", m_envelopePeakValue, 0.1f, 2f);
        m_envelopeEasePower = SliderRow("완급(1=선형)", m_envelopeEasePower, 0.2f, 4f);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("상하"))
        {
            controller.SetPitchRecoilEnvelopeCurve(BuildEnvelopeCurveFromFields());
        }

        if (GUILayout.Button("좌우"))
        {
            controller.SetYawRecoilEnvelopeCurve(BuildEnvelopeCurveFromFields());
        }

        if (aimController != null)
        {
            if (GUILayout.Button("롤"))
            {
                aimController.SetRollKickEnvelopeCurve(BuildEnvelopeCurveFromFields());
            }

            if (GUILayout.Button("힙 펀치"))
            {
                aimController.SetHipfireFovPunchEnvelopeCurve(BuildEnvelopeCurveFromFields());
            }

            if (GUILayout.Button("ADS 펀치"))
            {
                aimController.SetAdsFovPunchEnvelopeCurve(BuildEnvelopeCurveFromFields());
            }
        }
        GUILayout.EndHorizontal();
    }

    /// <summary>지금 생성기 값으로 엔벨로프 곡선을 만듭니다.</summary>
    private AnimationCurve BuildEnvelopeCurveFromFields()
    {
        return ImpulseEnvelope.BuildCurve(m_envelopePeakRatio, m_envelopePeakValue, m_envelopeEasePower);
    }

    private KickSidePattern DrawKickSidePattern(string label, KickSidePattern current)
    {
        GUILayout.Label(label);
        return (KickSidePattern)GUILayout.SelectionGrid(
            (int)current,
            new[] { "무작위", "좌→우", "우→좌" },
            3);
    }

    private LayerMask DrawLayerMaskRow(string label, LayerMask value, ref bool expanded)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"{label}: {GetLayerMaskSummary(value)}", GUILayout.ExpandWidth(true));
        if (GUILayout.Button(expanded ? "접기" : "레이어 선택", GUILayout.Width(94f)))
        {
            expanded = !expanded;
        }
        GUILayout.EndHorizontal();

        if (!expanded)
        {
            return value;
        }

        int mask = value.value;
        for (int layer = 0; layer < 32; layer++)
        {
            string layerName = LayerMask.LayerToName(layer);
            if (string.IsNullOrEmpty(layerName))
            {
                continue;
            }

            int bit = 1 << layer;
            bool included = (mask & bit) != 0;
            bool requested = GUILayout.Toggle(included, $"  {layerName}");
            if (requested != included)
            {
                mask = requested ? mask | bit : mask & ~bit;
            }
        }

        value.value = mask;
        return value;
    }

    private static string GetLayerMaskSummary(LayerMask value)
    {
        if (value.value == 0)
        {
            return "Nothing";
        }

        if (value.value == ~0)
        {
            return "Everything";
        }

        List<string> names = new List<string>();
        for (int layer = 0; layer < 32; layer++)
        {
            if ((value.value & (1 << layer)) == 0)
            {
                continue;
            }

            string layerName = LayerMask.LayerToName(layer);
            names.Add(string.IsNullOrEmpty(layerName) ? $"Layer {layer}" : layerName);
        }

        return names.Count == 0 ? "Nothing" : string.Join(", ", names);
    }

    // ─────────────────────────────────────────────────────────────
    // 적(좀비) 스폰 섹션
    // ─────────────────────────────────────────────────────────────

    /// <summary>탈출 지점까지 가지 않고 필드를 끝내는 디버그 조작을 그립니다.</summary>
    /// <remarks>
    /// 정산과 결과 UI를 확인하는 데 쓰는 지름길입니다. 판정만 건너뛰고 이후 절차는
    /// <see cref="EscapeSystem.ForceEscape"/>를 통해 실제 탈출과 같은 경로를 타므로 결과가 달라지지 않습니다.
    /// 전멸 게임오버도 같은 원칙으로, 스쿼드원을 실제 전투 이탈시켜 정상 감지 경로를 타게 합니다.
    /// </remarks>
    // =========================
    // 디버그 항목 (DebugSectionRegistry 공용 수집)
    // =========================

    /// <summary>디버그 항목 서브탭에서 지금 보고 있는 탭입니다.</summary>
    private int m_debugTabIndex;

    /// <summary>디버그 탭 정수 칸에 입력 중인 문자열입니다. 숫자로 확정되면 지웁니다.</summary>
    private readonly Dictionary<string, string> m_intFieldDrafts = new Dictionary<string, string>();

    /// <summary>수집한 디버그 항목입니다. 매 프레임 훑지 않도록 들고 있습니다.</summary>
    private List<DebugFieldEntry> m_debugEntries = new List<DebugFieldEntry>();

    /// <summary>다음으로 목록을 다시 훑을 시각입니다.</summary>
    private float m_nextDebugScanTime;

    /// <summary>디버그 항목 목록을 다시 훑는 주기(초)입니다.</summary>
    /// <remarks>
    /// 스폰으로 대상이 늘어나므로 한 번만 훑으면 새 개체가 목록에 없습니다.
    /// 반대로 매 프레임 훑으면 씬 전체 리플렉션이라 트레이너를 여는 동안 프레임이 눈에 띄게 떨어집니다.
    /// </remarks>
    private const float DebugScanInterval = 2.0f;

    /// <summary>
    /// 각 컴포넌트의 Debug 구역 필드를 탭으로 모아 그립니다.
    /// </summary>
    /// <remarks>
    /// 목록을 손으로 나열하지 않고 <see cref="DebugSectionRegistry"/>가 수집한 것을 그대로 씁니다.
    /// 에디터 창(<c>Tools/GrayZone/Debug Toggles</c>)과 같은 수집기라 두 화면의 목록이 어긋나지 않고,
    /// 새 디버그 필드를 Debug 구역에 선언하면 양쪽에 자동으로 나타납니다.
    ///
    /// <b>기즈모 항목은 빌드에서 아무것도 그리지 않습니다.</b> 그래도 목록에 남기는 이유는, 에디터 Play 중에는
    /// Scene 뷰에 그려져 쓸모가 있고, 빌드에서만 숨기면 같은 화면이 환경에 따라 달라져 더 헷갈리기 때문입니다.
    /// </remarks>
    private void DrawDebugFieldSection()
    {
        GUILayout.Label("■ 디버그 항목", m_headerStyle);

        if (Time.unscaledTime >= m_nextDebugScanTime)
        {
            m_nextDebugScanTime = Time.unscaledTime + DebugScanInterval;
            m_debugEntries = DebugSectionRegistry.Collect(
                FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        }

        string[] tabNames = DebugSectionRegistry.TabNames;
        m_debugTabIndex = GUILayout.Toolbar(Mathf.Clamp(m_debugTabIndex, 0, tabNames.Length - 1), tabNames);

        string activeTab = tabNames[Mathf.Clamp(m_debugTabIndex, 0, tabNames.Length - 1)];
        Component currentOwner = null;
        int drawn = 0;

        foreach (DebugFieldEntry entry in m_debugEntries)
        {
            if (entry.Owner == null || entry.TabName != activeTab)
            {
                continue;
            }

            if (entry.Owner != currentOwner)
            {
                currentOwner = entry.Owner;
                GUILayout.Space(3);
                GUILayout.Label($"{currentOwner.gameObject.name} · {currentOwner.GetType().Name}");
            }

            DrawDebugFieldRow(entry);
            drawn++;
        }

        if (drawn == 0)
        {
            GUILayout.Label("이 탭에 표시할 디버그 항목이 없습니다.");
        }
    }

    /// <summary>
    /// 디버그 항목 하나를 타입에 맞는 위젯으로 그립니다.
    /// </summary>
    /// <remarks>
    /// 런타임이라 <c>SerializedObject</c>를 쓸 수 없어 리플렉션으로 직접 읽고 씁니다.
    /// 그래서 인스펙터처럼 모든 타입을 다루지는 않습니다. bool·float·int·enum만 편집하고
    /// 나머지(프리팹 참조, 배열 등)는 값만 보여 줍니다. 편집할 수 없는 것을 편집할 수 있는 것처럼
    /// 그리면 눌러도 반응이 없어 고장으로 보입니다.
    /// </remarks>
    private void DrawDebugFieldRow(DebugFieldEntry entry)
    {
        object value = entry.Field.GetValue(entry.Owner);
        Type fieldType = entry.Field.FieldType;

        if (fieldType == typeof(bool))
        {
            bool current = (bool)value;
            bool next = GUILayout.Toggle(current, $" {entry.DisplayName}");

            if (next != current)
            {
                entry.Field.SetValue(entry.Owner, next);
            }

            return;
        }

        if (fieldType == typeof(float))
        {
            float current = (float)value;
            float next = SliderRow(entry.DisplayName, current, 0f, Mathf.Max(1f, current * 4f));

            if (!Mathf.Approximately(next, current))
            {
                entry.Field.SetValue(entry.Owner, next);
            }

            return;
        }

        if (fieldType == typeof(int))
        {
            int current = (int)value;

            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label(entry.DisplayName, GUILayout.Width(200));

                // 입력 중인 문자열을 따로 둡니다. 매 프레임 현재 값으로 다시 그리면 칸을 비우거나 "-"만 친 상태가
                // 곧바로 이전 값으로 돌아가 새 숫자를 입력할 수 없습니다.
                string key = $"{entry.Owner.GetHashCode()}:{entry.Field.Name}";
                string shown = m_intFieldDrafts.TryGetValue(key, out string draft) ? draft : current.ToString();
                string text = GUILayout.TextField(shown, GUILayout.Width(70));
                if (text != shown)
                {
                    m_intFieldDrafts[key] = text;
                }

                if (int.TryParse(text, out int parsed))
                {
                    if (parsed != current)
                    {
                        entry.Field.SetValue(entry.Owner, parsed);
                    }

                    if (text == parsed.ToString())
                    {
                        m_intFieldDrafts.Remove(key);
                    }
                }
            }

            return;
        }

        if (fieldType.IsEnum)
        {
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label(entry.DisplayName, GUILayout.Width(200));

                if (GUILayout.Button(value != null ? value.ToString() : "(null)", GUILayout.Width(140)))
                {
                    CycleEnumValue(entry, fieldType, value);
                }
            }

            return;
        }

        using (new GUILayout.HorizontalScope())
        {
            GUILayout.Label(entry.DisplayName, GUILayout.Width(200));
            GUILayout.Label(value != null ? value.ToString() : "(없음)");
        }
    }

    /// <summary>enum 값을 다음 항목으로 넘깁니다.</summary>
    /// <remarks>런타임 IMGUI에는 드롭다운이 없어 눌러서 순환시키는 방식으로 둡니다.</remarks>
    private static void CycleEnumValue(DebugFieldEntry entry, Type fieldType, object value)
    {
        Array values = Enum.GetValues(fieldType);
        int index = Array.IndexOf(values, value);
        entry.Field.SetValue(entry.Owner, values.GetValue((index + 1) % values.Length));
    }

    /// <summary>
    /// 팀 AI의 사격 허용 여부와 팀원 간격을 조절합니다.
    /// </summary>
    /// <remarks>
    /// <b>이 탭의 값은 스쿼드 전체 설정입니다.</b> 지금 어떤 캐릭터를 조작 중인지와 무관하게 걸리고,
    /// 조작 캐릭터를 전환해도 그대로 유지됩니다. 값을 <see cref="SquadManager"/>가 소유하므로
    /// 여기서는 그 하나를 읽고 쓰기만 합니다. 멤버를 순회하며 각자에게 복사해 넣던 방식은
    /// 전환이나 구조 복귀로 멤버 구성이 바뀔 때 일부만 옛 값을 쥐게 됩니다.
    /// <para>
    /// 사격 허용을 꺼도 <b>조작 중인 캐릭터는 그대로 쏠 수 있습니다</b>. 이 값은
    /// <see cref="SquadAIController"/>만 읽고 그 컴포넌트는 조작 멤버에서 꺼져 있기 때문입니다.
    /// </para>
    /// </remarks>
    protected void DrawSquadAiControlSection()
    {
        GUILayout.Label("■ 팀 AI 제어 (스쿼드 전체 · 전환과 무관)", m_headerStyle);

        SquadManager squadManager = SquadManager.Instance;
        if (squadManager == null || squadManager.SquadMembers == null)
        {
            GUILayout.Label("SquadManager를 찾을 수 없습니다.");
            return;
        }

        bool requested = GUILayout.Toggle(
            squadManager.AiFiringAllowed,
            " 팀 AI 사격 허용 (끄면 조준만 하고 쏘지 않음 / 조작 캐릭터는 영향 없음)");
        if (requested != squadManager.AiFiringAllowed)
        {
            squadManager.AiFiringAllowed = requested;
        }

        DrawSquadAiSpacingSlider(squadManager);

        foreach (SquadMemberController member in squadManager.SquadMembers)
        {
            if (member == null)
            {
                continue;
            }

            SquadAIController ai = member.GetComponent<SquadAIController>();
            if (ai == null)
            {
                continue;
            }

            string role = member.IsPlayerSquadMember ? "조작중" : "AI";
            string targetName = ai.Targeting != null && ai.Targeting.CurrentTarget != null
                ? ai.Targeting.CurrentTarget.name
                : "없음";

            GUILayout.Label($"  {ai.name} [{role}]  대상={targetName}");
        }
    }

    /// <summary>
    /// AI 팀원끼리 유지할 간격을 조절합니다(§6.1).
    /// </summary>
    /// <param name="squadManager">값을 소유한 스쿼드 매니저입니다.</param>
    /// <remarks>
    /// 캡슐 반지름이 0.3이라 실제 몸 사이 여유는 이 값에서 0.6을 뺀 만큼입니다. 값을 너무 키우면
    /// 합류 완료 반경 안에 두 자리가 다 들어가지 못해 AI가 자리를 못 찾고 제자리에 섭니다.
    /// </remarks>
    private void DrawSquadAiSpacingSlider(SquadManager squadManager)
    {
        float spacing = squadManager.AiMemberSpacing;

        GUILayout.BeginHorizontal();
        GUILayout.Label($"AI 팀원 간격  {spacing:F2}m  (몸 사이 여유 {Mathf.Max(0.0f, spacing - 0.6f):F2}m)",
            GUILayout.Width(300));
        float next = GUILayout.HorizontalSlider(spacing, 0.0f, 4.0f);
        GUILayout.EndHorizontal();

        if (!Mathf.Approximately(next, spacing))
        {
            squadManager.AiMemberSpacing = next;
        }
    }

    private void DrawFieldControlSection()
    {
        GUILayout.Label("■ 필드 제어", m_headerStyle);

        EscapeSystem escapeSystem = FindFirstObjectByType<EscapeSystem>(FindObjectsInactive.Include);
        CombatSceneDataManager fieldData = CombatSceneDataManager.Instance;

        if (fieldData != null && fieldData.IsFinalized)
        {
            GUILayout.Label($"이미 정산이 끝났습니다. (처치 {fieldData.KillCount})");
            return;
        }

        GUILayout.Label(fieldData != null
            ? $"현재 처치 {fieldData.KillCount} / 임무 {(fieldData.MissionCompleted ? "달성" : "미달성")}"
            : "필드 데이터 매니저를 찾을 수 없습니다. 정산 없이 결과 UI만 열릴 수 있습니다.");

        if (escapeSystem == null)
        {
            GUILayout.Label("EscapeSystem을 찾을 수 없어 즉시 탈출을 쓸 수 없습니다.");
        }
        else if (GUILayout.Button("즉시 탈출 (정산 후 결과 UI)", GUILayout.Height(26)))
        {
            // 결과 UI가 입력을 가져가므로 트레이너를 먼저 닫습니다.
            // 열어 둔 채로 두면 트레이너와 결과 UI가 같은 커서를 두고 다툽니다.
            SetMenuOpen(false);
            escapeSystem.ForceEscape();
        }

        DrawForceGameOverButton();
    }

    /// <summary>
    /// 스쿼드 전원을 전투 이탈시켜 전멸 게임오버를 일으키는 디버그 버튼을 그립니다.
    /// </summary>
    /// <remarks>
    /// 게임오버 상태를 직접 세우지 않고 <see cref="PlayerHealth.Death"/>를 스쿼드원마다 호출합니다.
    /// 그래야 전멸 감지(<c>SquadManager.OnSquadEliminated</c>)부터 화면 표시까지 실제와 같은 경로를 타고,
    /// 정산을 건너뛰는지(<c>FinalizeField</c> 미호출)까지 함께 확인할 수 있습니다.
    /// 되돌릴 수 없는 조작이라 확인 단계를 한 번 둡니다.
    /// </remarks>
    protected void DrawForceGameOverButton()
    {
        SquadManager squadManager = SquadManager.Instance != null
            ? SquadManager.Instance
            : FindFirstObjectByType<SquadManager>();

        if (squadManager == null || squadManager.SquadMembers == null)
        {
            GUILayout.Label("SquadManager를 찾을 수 없어 전멸 게임오버를 쓸 수 없습니다.");
            return;
        }

        int aliveCount = 0;
        for (int i = 0; i < squadManager.SquadMembers.Count; i++)
        {
            SquadMemberController member = squadManager.SquadMembers[i];
            if (member != null && member.IsAlive)
            {
                aliveCount++;
            }
        }

        if (aliveCount == 0)
        {
            GUILayout.Label("스쿼드 전원이 이미 전투 이탈했습니다.");
            m_confirmForceGameOver = false;
            return;
        }

        if (!m_confirmForceGameOver)
        {
            if (GUILayout.Button($"전멸 게임오버 (생존 {aliveCount}명 전투 이탈)", GUILayout.Height(26)))
            {
                m_confirmForceGameOver = true;
            }

            return;
        }

        GUILayout.Label($"생존 {aliveCount}명을 전투 이탈시킵니다. 되돌릴 수 없습니다.");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("실행", GUILayout.Height(26)))
        {
            m_confirmForceGameOver = false;
            // 게임오버 화면이 입력을 가져가므로 트레이너를 먼저 닫습니다.
            SetMenuOpen(false);
            ForceSquadElimination(squadManager);
        }

        if (GUILayout.Button("취소", GUILayout.Height(26)))
        {
            m_confirmForceGameOver = false;
        }
        GUILayout.EndHorizontal();
    }

    /// <summary>스쿼드원 전원을 실제 사망 경로로 전투 이탈시킵니다.</summary>
    /// <param name="squadManager">대상 스쿼드 매니저입니다.</param>
    private static void ForceSquadElimination(SquadManager squadManager)
    {
        // 순회 중 전멸 감지가 멤버 전환을 일으킬 수 있어 목록을 먼저 복사합니다.
        List<SquadMemberController> members = new List<SquadMemberController>(squadManager.SquadMembers);

        int killed = 0;
        for (int i = 0; i < members.Count; i++)
        {
            SquadMemberController member = members[i];
            if (member == null || !member.IsAlive)
            {
                continue;
            }

            PlayerHealth health = member.GetComponent<PlayerHealth>();
            if (health == null)
            {
                Debug.LogWarning($"[RuntimeDebugTrainer] {member.name}에 PlayerHealth가 없어 전투 이탈시키지 못했습니다.", member);
                continue;
            }

            health.Death();
            killed++;
        }

        Debug.Log($"[RuntimeDebugTrainer] 전멸 게임오버 요청: {killed}명 전투 이탈 처리.", squadManager);
    }

    /// <summary>
    /// 스쿼드 공용 인벤토리의 투척물(수류탄·화염병) 수량을 보여 주고 늘리는 버튼을 그립니다.
    /// </summary>
    /// <remarks>
    /// 투척물 종류는 씬의 <see cref="ExplosiveProjectileShooter"/>가 들고 있는 Prefab 목록에서 모읍니다.
    /// 슬롯 수와 스택 한도를 넘는 수량은 들어가지 않으므로 넣지 못한 수량을 함께 표시합니다.
    /// 조준선 창(F10)에 있던 것을 옮겨 왔습니다. 조준선 창은 조준선만 다룹니다.
    /// </remarks>
    protected void DrawThrowableSection()
    {
        GUILayout.Label("■ 투척물", m_headerStyle);

        SquadInventoryManager inventory = FindFirstObjectByType<SquadInventoryManager>(FindObjectsInactive.Include);
        if (inventory == null)
        {
            GUILayout.Label("SquadInventoryManager를 찾을 수 없습니다.");
            return;
        }

        List<ProjectileBase> projectiles = CollectSceneThrowables();
        if (projectiles.Count == 0)
        {
            GUILayout.Label("씬에 등록된 투척물 Prefab이 없습니다. 투척기 목록과 아이템 정의 연결을 확인하세요.");
            return;
        }

        for (int i = 0; i < projectiles.Count; i++)
        {
            ProjectileBase projectile = projectiles[i];
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{ResolveThrowableName(projectile)}: {inventory.CountOf(projectile.InventoryItemDefinitionId)}");
            if (GUILayout.Button($"+{DebugThrowableGrantAmount}", GUILayout.Width(80)))
            {
                m_throwableGrantResult = GrantThrowable(inventory, projectile);
            }
            GUILayout.EndHorizontal();
        }

        if (GUILayout.Button($"투척물 전부 +{DebugThrowableGrantAmount}", GUILayout.Height(26)))
        {
            m_throwableGrantResult = string.Empty;
            for (int i = 0; i < projectiles.Count; i++)
            {
                m_throwableGrantResult += GrantThrowable(inventory, projectiles[i]);
            }
        }

        // 아이템 정의가 없는 투척물은 인벤토리로 셀 수 없어 위 목록에서 빠집니다. 빠진 이유를 보이게 남깁니다.
        List<string> unlinked = CollectUnlinkedThrowableNames();
        if (unlinked.Count > 0)
        {
            GUILayout.Label($"아이템 정의가 없어 보급 불가: {string.Join(", ", unlinked)}");
        }

        if (!string.IsNullOrEmpty(m_throwableGrantResult))
        {
            GUILayout.Label(m_throwableGrantResult);
        }
    }

    /// <summary>회복약 보유 수량과 무제한 토글을 그립니다. 씬에 <see cref="PotionManager"/>가 없으면 안내만 표시합니다.</summary>
    private void DrawPotionSection()
    {
        GUILayout.Label("■ 회복약", m_headerStyle);
        PotionManager potion = FindFirstObjectByType<PotionManager>();
        if (potion == null)
        {
            GUILayout.Label("씬에 PotionManager가 없습니다.");
            return;
        }

        GUILayout.Label($"보유 {potion.Count}개 · 사용 키 {potion.UseKey}");
        potion.DebugInfinitePotions = GUILayout.Toggle(potion.DebugInfinitePotions, " 회복약 무제한");
    }

    /// <summary>씬의 투척기에 들어 있지만 아이템 정의가 연결되지 않은 투척물 Prefab 이름을 모읍니다.</summary>
    private static List<string> CollectUnlinkedThrowableNames()
    {
        var result = new List<string>();
        ExplosiveProjectileShooter[] shooters = FindObjectsByType<ExplosiveProjectileShooter>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < shooters.Length; i++)
        {
            IReadOnlyList<ProjectileBase> prefabs = shooters[i].ProjectilePrefabs;
            if (prefabs == null)
            {
                continue;
            }

            for (int j = 0; j < prefabs.Count; j++)
            {
                ProjectileBase projectile = prefabs[j];
                if (projectile != null
                    && string.IsNullOrWhiteSpace(projectile.InventoryItemDefinitionId)
                    && !result.Contains(projectile.name))
                {
                    result.Add(projectile.name);
                }
            }
        }

        return result;
    }

    /// <summary>투척물을 지정 수량만큼 넣고, 공간이 모자라 남은 수량이 있으면 안내 문구를 돌려줍니다.</summary>
    private static string GrantThrowable(SquadInventoryManager inventory, ProjectileBase projectile)
    {
        inventory.TryAcquire(projectile.InventoryItemDefinitionId, DebugThrowableGrantAmount, out int leftover);
        return leftover > 0
            ? $"{ResolveThrowableName(projectile)} {leftover}개 공간 부족  "
            : string.Empty;
    }

    /// <summary>씬의 투척기들이 쓰는 투척물 Prefab을 아이템 ID 기준으로 중복 없이 모읍니다.</summary>
    private static List<ProjectileBase> CollectSceneThrowables()
    {
        var result = new List<ProjectileBase>();
        var itemIds = new HashSet<string>();
        ExplosiveProjectileShooter[] shooters = FindObjectsByType<ExplosiveProjectileShooter>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < shooters.Length; i++)
        {
            IReadOnlyList<ProjectileBase> prefabs = shooters[i].ProjectilePrefabs;
            if (prefabs == null)
            {
                continue;
            }

            for (int j = 0; j < prefabs.Count; j++)
            {
                ProjectileBase projectile = prefabs[j];
                if (projectile != null
                    && !string.IsNullOrWhiteSpace(projectile.InventoryItemDefinitionId)
                    && itemIds.Add(projectile.InventoryItemDefinitionId))
                {
                    result.Add(projectile);
                }
            }
        }

        return result;
    }

    private static string ResolveThrowableName(ProjectileBase projectile)
    {
        return projectile.InventoryItemDefinition != null
            ? projectile.InventoryItemDefinition.DisplayName
            : projectile.name;
    }

    protected void DrawEnemySpawnSection()
    {
        int enemyCount = FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Length;
        GUILayout.Label($"■ 좀비 스폰 (현재 적 {enemyCount}마리)", m_headerStyle);

        GUILayout.BeginHorizontal();
        GUILayout.Label("원본", GUILayout.Width(40));
        if (GUILayout.Button("◀", GUILayout.Width(30)))
        {
            CycleEnemyTemplateChoice(-1);
        }
        GUILayout.Label(DescribeSelectedEnemyTemplate(), GUILayout.Width(230));
        if (GUILayout.Button("▶", GUILayout.Width(30)))
        {
            CycleEnemyTemplateChoice(1);
        }
        if (GUILayout.Button("목록 갱신", GUILayout.Width(90)))
        {
            RefreshEnemyTemplateChoices();
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("좌표", GUILayout.Width(40));
        GUILayout.Label("X", GUILayout.Width(14));
        m_spawnX = GUILayout.TextField(m_spawnX, GUILayout.Width(90));
        GUILayout.Label("Y", GUILayout.Width(14));
        m_spawnY = GUILayout.TextField(m_spawnY, GUILayout.Width(90));
        GUILayout.Label("Z", GUILayout.Width(14));
        m_spawnZ = GUILayout.TextField(m_spawnZ, GUILayout.Width(90));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label($"마리 수: {m_spawnCount}", GUILayout.Width(90));
        if (GUILayout.Button("-", GUILayout.Width(30)))
        {
            m_spawnCount = Mathf.Max(1, m_spawnCount - 1);
        }
        if (GUILayout.Button("+", GUILayout.Width(30)))
        {
            m_spawnCount = Mathf.Min(50, m_spawnCount + 1);
        }
        if (GUILayout.Button("플레이어 위치로 좌표 채우기"))
        {
            PrefillSpawnCoordsFromPlayer();
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("좌표에 스폰"))
        {
            if (TryParseSpawnPosition(out Vector3 position))
            {
                SpawnEnemies(position, m_spawnCount);
            }
        }
        if (GUILayout.Button("플레이어 앞에 스폰"))
        {
            SpawnEnemies(GetPlayerFrontPosition(), m_spawnCount);
        }
        if (GUILayout.Button("모든 적 제거"))
        {
            KillAllEnemies();
        }
        GUILayout.EndHorizontal();
    }

    private bool TryParseSpawnPosition(out Vector3 position)
    {
        position = Vector3.zero;

        bool ok = float.TryParse(m_spawnX, out float x)
            & float.TryParse(m_spawnY, out float y)
            & float.TryParse(m_spawnZ, out float z);

        if (!ok)
        {
            Debug.LogWarning("[RuntimeDebugTrainer] 좌표 입력이 올바르지 않습니다.");
            return false;
        }

        position = new Vector3(x, y, z);
        return true;
    }

    private void PrefillSpawnCoordsFromPlayer()
    {
        Vector3 position = GetPlayerFrontPosition();
        m_spawnX = position.x.ToString("0.##");
        m_spawnY = position.y.ToString("0.##");
        m_spawnZ = position.z.ToString("0.##");
    }

    private Vector3 GetPlayerFrontPosition()
    {
        SquadManager squadManager = FindFirstObjectByType<SquadManager>();
        SquadMemberController player = squadManager != null ? squadManager.PlayerSquadMember : null;
        if (player == null)
        {
            return Vector3.zero;
        }

        Transform playerTransform = player.transform;
        return playerTransform.position + playerTransform.forward * 4f;
    }

    /// <summary>씬의 기존 좀비를 원본 삼아 지정 위치에 지정 마리 수만큼 복제 스폰합니다.</summary>
    private void SpawnEnemies(Vector3 origin, int count)
    {
        GameObject template = ResolveEnemyTemplate();
        if (template == null)
        {
            Debug.LogWarning("[RuntimeDebugTrainer] 스폰할 좀비 원본(EnemyController)을 씬에서 찾지 못했습니다.");
            return;
        }

        for (int i = 0; i < count; i++)
        {
            // 여러 마리는 겹치지 않도록 원 둘레로 살짝 흩뿌립니다(마리 수에 따라).
            Vector3 offset = count <= 1
                ? Vector3.zero
                : Quaternion.Euler(0f, 360f * i / count, 0f) * Vector3.forward * 1.5f;
            Vector3 spawnPosition = origin + offset;

            // NavMesh 위로 보정해 배회/추적이 정상 동작하도록 합니다.
            if (NavMesh.SamplePosition(spawnPosition, out NavMeshHit hit, 8f, NavMesh.AllAreas))
            {
                spawnPosition = hit.position;
            }

            GameObject spawned = Instantiate(template, spawnPosition, template.transform.rotation);
            spawned.name = "Enemy(Trainer)";
            spawned.SetActive(true);
        }
    }

    /// <summary>스폰 원본을 마련합니다. 씬의 기존 좀비를 비활성 복제로 보관해, 원본이 죽어도 계속 스폰할 수 있게 합니다.</summary>
    private GameObject ResolveEnemyTemplate()
    {
        if (m_enemyTemplate != null)
        {
            return m_enemyTemplate;
        }

        // 한 번 원본 확보를 시도했고 실패했더라도, 이후 씬에 좀비가 생겼을 수 있으니 매번 재시도합니다.
        EnemyController source = ResolveSelectedEnemySource();
        if (source == null)
        {
            return null;
        }

        // 비활성 홀더의 자식으로 복제하면 원본 클론이 활성화되지 않아 Awake/OnEnable(필드 집계 등)이 실행되지 않습니다.
        // 스폰 시에는 부모 없이 다시 Instantiate하므로 그때 비로소 정상 활성화됩니다.
        if (m_enemyTemplateHolder == null)
        {
            m_enemyTemplateHolder = new GameObject("[TrainerEnemyTemplateHolder]");
            m_enemyTemplateHolder.SetActive(false);
            DontDestroyOnLoad(m_enemyTemplateHolder);
        }

        m_enemyTemplate = Instantiate(source.gameObject, m_enemyTemplateHolder.transform);
        m_enemyTemplate.name = "EnemyTemplate(Trainer)";
        return m_enemyTemplate;
    }

    /// <summary>고른 스폰 원본을 돌려줍니다. 고르지 않았거나 고른 대상이 사라졌으면 씬에서 가장 먼저 찾은 적으로 되돌립니다.</summary>
    private EnemyController ResolveSelectedEnemySource()
    {
        if (m_enemyTemplateIndex >= 0 && m_enemyTemplateIndex < m_enemyTemplateChoices.Count)
        {
            EnemyController selected = m_enemyTemplateChoices[m_enemyTemplateIndex];
            if (selected != null)
            {
                return selected;
            }
        }

        return FindFirstObjectByType<EnemyController>(FindObjectsInactive.Include);
    }

    /// <summary>씬의 적을 훑어 스폰 원본 후보 목록을 다시 만듭니다. 트레이너가 만든 사본은 후보에서 제외합니다.</summary>
    private void RefreshEnemyTemplateChoices()
    {
        EnemyController previous = null;
        if (m_enemyTemplateIndex >= 0 && m_enemyTemplateIndex < m_enemyTemplateChoices.Count)
        {
            previous = m_enemyTemplateChoices[m_enemyTemplateIndex];
        }

        m_enemyTemplateChoices.Clear();
        foreach (EnemyController enemy in FindObjectsByType<EnemyController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (enemy == null)
            {
                continue;
            }

            // 트레이너가 보관 중인 원본 사본과 스폰 결과물은 후보에서 뺍니다. 사본의 사본이 쌓이는 걸 막기 위함입니다.
            string name = enemy.gameObject.name;
            if (name.StartsWith("Enemy(Trainer)", StringComparison.Ordinal)
                || name.StartsWith("EnemyTemplate(Trainer)", StringComparison.Ordinal))
            {
                continue;
            }

            m_enemyTemplateChoices.Add(enemy);
        }

        m_enemyTemplateChoices.Sort((a, b) => string.CompareOrdinal(a.gameObject.name, b.gameObject.name));
        m_enemyTemplateIndex = previous != null ? m_enemyTemplateChoices.IndexOf(previous) : -1;
    }

    /// <summary>원본 선택을 앞뒤로 옮깁니다. 목록 양 끝을 넘어가면 자동(씬에서 첫 적)으로 돌아옵니다.</summary>
    private void CycleEnemyTemplateChoice(int step)
    {
        if (m_enemyTemplateChoices.Count == 0)
        {
            RefreshEnemyTemplateChoices();
        }

        if (m_enemyTemplateChoices.Count == 0)
        {
            m_enemyTemplateIndex = -1;
            return;
        }

        int count = m_enemyTemplateChoices.Count;
        int next = m_enemyTemplateIndex + step;
        if (next < -1)
        {
            next = count - 1;
        }
        else if (next >= count)
        {
            next = -1;
        }

        if (next == m_enemyTemplateIndex)
        {
            return;
        }

        m_enemyTemplateIndex = next;

        // 원본이 바뀌었으니 보관 중이던 사본은 버리고, 다음 스폰 때 새 원본으로 다시 복제하게 합니다.
        if (m_enemyTemplate != null)
        {
            Destroy(m_enemyTemplate);
            m_enemyTemplate = null;
        }
    }

    /// <summary>현재 선택을 창에 표시할 문구로 만듭니다.</summary>
    private string DescribeSelectedEnemyTemplate()
    {
        if (m_enemyTemplateIndex < 0 || m_enemyTemplateIndex >= m_enemyTemplateChoices.Count)
        {
            return "자동 (씬에서 첫 적)";
        }

        EnemyController selected = m_enemyTemplateChoices[m_enemyTemplateIndex];
        if (selected == null)
        {
            return "자동 (고른 적이 사라짐)";
        }

        return $"{m_enemyTemplateIndex + 1}/{m_enemyTemplateChoices.Count}  {selected.gameObject.name}";
    }

    protected void KillAllEnemies()
    {
        foreach (EnemyHealth enemyHealth in FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None))
        {
            enemyHealth.TakeDamage(DamageInfo.Debug(int.MaxValue, "RuntimeDebugTrainer.KillAllEnemies"));
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 헬퍼
    // ─────────────────────────────────────────────────────────────

    /// <summary>라벨·슬라이더·값 한 줄을 그리고, 사용자가 움직였을 때만 새 값을 돌려줍니다.</summary>
    /// <remarks>
    /// 호출부는 매 OnGUI마다 돌려받은 값을 setter에 넣습니다. HorizontalSlider는 범위 밖 값을 잘라 돌려주므로,
    /// 그대로 쓰면 범위를 벗어난 원래 값이 탭을 열기만 해도 바뀝니다. 그래서 움직이지 않았으면 원래 값을 돌려줍니다.
    /// </remarks>
    protected float SliderRow(string label, float value, float min, float max, string format = "0.##")
    {
        GUILayout.BeginHorizontal();
        float labelWidth = Mathf.Clamp(m_windowRect.width * 0.30f, 130f, 210f);
        GUILayout.Label(label, GUILayout.Width(labelWidth));

        bool changedBefore = GUI.changed;
        GUI.changed = false;
        float result = GUILayout.HorizontalSlider(value, min, max, GUILayout.MinWidth(140f), GUILayout.ExpandWidth(true));
        bool moved = GUI.changed;
        GUI.changed = changedBefore || moved;

        float shown = moved ? result : value;
        GUILayout.Label(shown.ToString(format), GUILayout.Width(60));
        GUILayout.EndHorizontal();
        return shown;
    }

    private void EnsureStyles()
    {
        // 창 크기를 바꾸면 글자와 위젯도 같이 커지고 작아져야 인스펙터처럼 보입니다.
        // 기준 너비에서 얼마나 벗어났는지로 배율을 구하고, 읽을 수 없을 만큼 작아지지 않게 제한합니다.
        float scale = Mathf.Clamp(m_windowRect.width / ReferenceWindowWidth, MinUiScale, MaxUiScale);

        // 배율이 바뀌지 않았으면 스타일을 다시 만들지 않습니다. OnGUI는 프레임마다 여러 번 불립니다.
        if (m_titleStyle != null && Mathf.Approximately(scale, m_appliedUiScale))
        {
            ApplyScaledSkin(scale);
            return;
        }

        m_appliedUiScale = scale;

        m_titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(18f * scale),
            fontStyle = FontStyle.Bold,
        };

        m_headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(GUI.skin.label.fontSize <= 0 ? 12f * scale : GUI.skin.label.fontSize * scale),
            fontStyle = FontStyle.Bold,
        };

        ApplyScaledSkin(scale);
    }

    /// <summary>
    /// 이번 창에 쓸 기본 위젯 크기를 배율에 맞게 바꿉니다.
    /// </summary>
    /// <remarks>
    /// <see cref="GUI.skin"/>은 전역이라 창을 그리는 동안만 바꾸고 <see cref="RestoreSkin"/>으로 되돌립니다.
    /// 되돌리지 않으면 다른 IMGUI 창까지 이 배율을 물려받습니다.
    ///
    /// 글꼴 크기가 0인 스타일은 스킨 기본값을 쓰겠다는 뜻이라, 그런 경우에만 기준값을 곱해 넣습니다.
    /// 0이 아닌 값을 그대로 곱하면 매 프레임 누적되어 글자가 계속 커집니다.
    /// </remarks>
    private void ApplyScaledSkin(float scale)
    {
        if (m_scaledSkin == null || !Mathf.Approximately(scale, m_scaledSkinScale))
        {
            // `using System;`이 들어오면서 Object가 모호해져 UnityEngine 쪽으로 한정합니다.
            m_scaledSkin = UnityEngine.Object.Instantiate(GUI.skin);
            m_scaledSkinScale = scale;

            int baseSize = Mathf.RoundToInt(12f * scale);

            foreach (GUIStyle style in m_scaledSkin.customStyles)
            {
                ScaleStyle(style, scale, baseSize);
            }

            ScaleStyle(m_scaledSkin.label, scale, baseSize);
            ScaleStyle(m_scaledSkin.button, scale, baseSize);
            ScaleStyle(m_scaledSkin.box, scale, baseSize);
            ScaleStyle(m_scaledSkin.textField, scale, baseSize);
            ScaleStyle(m_scaledSkin.toggle, scale, baseSize);
            ScaleStyle(m_scaledSkin.window, scale, baseSize);

            m_scaledSkin.horizontalSlider.fixedHeight = GUI.skin.horizontalSlider.fixedHeight * scale;
            m_scaledSkin.horizontalSliderThumb.fixedWidth = GUI.skin.horizontalSliderThumb.fixedWidth * scale;
            m_scaledSkin.horizontalSliderThumb.fixedHeight = GUI.skin.horizontalSliderThumb.fixedHeight * scale;
        }

        m_previousSkin = GUI.skin;
        GUI.skin = m_scaledSkin;

        static void ScaleStyle(GUIStyle style, float s, int baseSize)
        {
            if (style == null)
            {
                return;
            }

            style.fontSize = style.fontSize <= 0 ? baseSize : Mathf.RoundToInt(style.fontSize * s);

            if (style.fixedHeight > 0f)
            {
                style.fixedHeight *= s;
            }
        }
    }

    /// <summary>창을 다 그린 뒤 전역 스킨을 되돌립니다.</summary>
    private void RestoreSkin()
    {
        if (m_previousSkin != null)
        {
            GUI.skin = m_previousSkin;
            m_previousSkin = null;
        }
    }
}
