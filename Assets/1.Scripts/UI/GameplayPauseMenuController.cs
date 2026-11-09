using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 게임플레이 중 Escape 입력으로 일시정지 메뉴와 설정 화면을 전환합니다.
/// </summary>
/// <remarks>
/// Figma의 1920x1080 Gameplay Settings UI를 기준으로 런타임 uGUI 계층을 만들며,
/// 설정 값의 실제 소유권은 <see cref="GameSettingManager"/>에 유지합니다.
/// </remarks>
[DefaultExecutionOrder(-1000)]
public sealed class GameplayPauseMenuController : MonoBehaviour
{
    private const float ReferenceWidth = 1920f;
    private const float ReferenceHeight = 1080f;

    // Figma 'ESC Menu UI 2'(1920x1080)의 메뉴 글자 윗변 y와 글자 크기입니다.
    private const float PauseResumeTop = 381f;
    private const float PauseSettingsTop = 498f;
    private const float PauseTitleTop = 621f;
    private const float PauseButtonSpacing = 120f;
    private const float PauseButtonFontSize = 45f;
    private const float PauseButtonHeight = 60f;
    private const float PauseButtonHitPadding = 24f;

    // 타이틀 메뉴의 선택 장식과 같은 값입니다.
    private const float PauseSelectionTextGap = 12f;
    private const float PauseSelectionFadeDuration = 0.15f;

    [Header("Figma UI Assets")]
    [Tooltip("메뉴와 설정 화면의 한글을 표시할 기본 TMP 폰트입니다.")]
    [SerializeField] private TMP_FontAsset m_regularFont;

    [Tooltip("큰 메뉴 버튼에 사용할 굵은 TMP 폰트입니다. 비어 있으면 기본 폰트를 사용합니다.")]
    [SerializeField] private TMP_FontAsset m_boldFont;

    [Tooltip("Figma Setting Scene에서 내보낸 24x24 설정 아이콘입니다.")]
    [SerializeField] private Sprite m_settingsIcon;

    [Tooltip("Figma Setting Scene에서 내보낸 뒤로가기 버튼 프레임입니다.")]
    [SerializeField] private Sprite m_backButtonFrame;

    [Tooltip("일시정지 메뉴 항목에 마우스를 올리거나 선택했을 때 글자 왼쪽에 띄울 장식입니다. 타이틀 메뉴와 같은 Select_Point_L을 씁니다. " +
             "비어 있으면 장식 없이 글자만 굵게 바뀝니다.")]
    [SerializeField] private Texture2D m_menuSelectionLeftTexture;

    [Tooltip("일시정지 메뉴 항목 글자 오른쪽에 띄울 장식입니다. 타이틀 메뉴와 같은 Select_Point_R을 씁니다.")]
    [SerializeField] private Texture2D m_menuSelectionRightTexture;

    [Header("Navigation")]
    [Tooltip("타이틀로 돌아가기 버튼이 불러올 Build Settings 씬 이름입니다.")]
    [SerializeField] private string m_titleSceneName = "TitleScene";

    [Header("Pause")]
    [Tooltip("메뉴가 열려 있는 동안 Time.timeScale을 0으로 만들어 게임플레이를 멈춥니다.")]
    [SerializeField] private bool m_pauseTime = true;

    [Tooltip("일시정지 메뉴에 설정 버튼을 둘지입니다. 끄면 '이어서 하기'와 '타이틀로 돌아가기'만 남습니다. " +
             "셸터처럼 전투 설정 화면을 쓰지 않는 씬에서 끕니다.")]
    [SerializeField] private bool m_showSettingsButton = true;

    /// <summary>셸터 이동 입력입니다. 스쿼드가 없는 셸터에서 메뉴가 열린 동안 잠급니다.</summary>
    private PlayerMove m_shelterMove;

    /// <summary>셸터 시점 입력입니다. 스쿼드가 없는 셸터에서 메뉴가 열린 동안 잠급니다.</summary>
    private CameraLook m_shelterLook;
    private bool m_previousShelterMoveLocked;
    private bool m_previousShelterLookLocked;

    private readonly List<PlayerInputController> m_fallbackInputControllers = new();
    private readonly Image[] m_tabBackgrounds = new Image[4];
    private readonly Outline[] m_tabOutlines = new Outline[4];
    private readonly GameObject[] m_settingsContentPages = new GameObject[4];
    private readonly Image[] m_combatSubTabBackgrounds = new Image[3];
    private readonly Outline[] m_combatSubTabOutlines = new Outline[3];
    private readonly GameObject[] m_combatSubPages = new GameObject[3];

    private GameObject m_viewRoot;
    private GameObject m_pausePage;
    private GameObject m_settingsPage;
    private Button m_resumeButton;
    private Button m_firstSettingsTab;
    private readonly List<Button> m_pauseButtons = new();
    private MenuSelectionIndicator m_pauseSelectionIndicator;
    private TMP_Dropdown m_resolutionDropdown;
    private readonly List<DisplayOption> m_resolutionOptions = new();

    /// <summary>해상도 드롭다운 한 칸입니다. 해상도와 주사율을 같이 가집니다.</summary>
    private readonly struct DisplayOption
    {
        public readonly Vector2Int Size;
        public readonly RefreshRate Rate;

        public DisplayOption(Vector2Int size, RefreshRate rate)
        {
            Size = size;
            Rate = rate;
        }

        /// <summary>표시용 정수 주사율입니다. 59.94Hz는 60으로 보입니다. 모르면 0입니다.</summary>
        public int Hz => Rate.numerator > 0 && Rate.denominator > 0 ? (int)System.Math.Round(Rate.value) : 0;

        public string Label => Hz > 0 ? $"{Size.x} x {Size.y} ({Hz}Hz)" : $"{Size.x} x {Size.y}";

        /// <summary>크기와 표시 주사율이 같으면 같은 칸으로 봅니다.</summary>
        public bool SameAs(DisplayOption other) => Size == other.Size && Hz == other.Hz;
    }

    private TMP_Dropdown m_monitorDropdown;
    private TextMeshProUGUI m_monitorHelp;
    private readonly List<DisplayInfo> m_displays = new();

    /// <summary>되돌리기 기준 모니터입니다. 설정 화면을 열 때와 저장한 뒤 기억합니다.</summary>
    private DisplayInfo m_displayBaseline;
    private bool m_hasDisplayBaseline;
    private bool m_isMovingWindow;
    private TMP_Dropdown m_screenModeDropdown;
    private Toggle m_vSyncToggle;
    private TMP_Dropdown m_frameLimitDropdown;
    private TextMeshProUGUI m_frameLimitHelp;
    private Slider m_frameLimitSlider;
    private TextMeshProUGUI m_frameLimitValue;

    /// <summary>비디오 탭 화면 모드 목록입니다. 배타적 전체 화면은 Windows에서만 동작하고, 다른 플랫폼에서는 Unity가 테두리 없는 전체 화면으로 바꿉니다.</summary>
    private static readonly (FullScreenMode mode, string label)[] s_screenModes =
    {
        (FullScreenMode.ExclusiveFullScreen, "전체 화면"),
        (FullScreenMode.FullScreenWindow, "테두리 없는 전체 화면"),
        (FullScreenMode.Windowed, "창 모드"),
    };

    // 최대 프레임 제한 슬라이더 범위입니다.
    private const int MinFrameLimit = 60;
    private const int MaxFrameLimit = 500;

    /// <summary>최대 프레임 제한 드롭다운의 프리셋입니다. 드롭다운은 이 뒤에 '사용자 지정', '제한 없음' 칸이 붙습니다.</summary>
    private static readonly int[] s_frameLimitPresets = { 60, 120, 144, 240 };
    private static int FrameLimitCustomIndex => s_frameLimitPresets.Length;
    private static int FrameLimitUnlimitedIndex => s_frameLimitPresets.Length + 1;
    private bool m_isRefreshingVideo;
    private SquadManager m_squadManager;
    private GameSettingManager.SettingSnapshot m_settingsBaseline;
    private SettingData.CrosshairSettingData m_crosshairBaseline;
    private float m_previousTimeScale = 1f;
    private CursorLockMode m_previousCursorLockMode;
    private bool m_previousCursorVisible;
    private bool m_isOpen;
    private bool m_restoreCursorInLateUpdate;
    private int m_selectedCombatSubTab;
    private CrosshairSettingsPanel m_crosshairSettingsPanel;
    private CrosshairSettingsPanel m_throwableCrosshairSettingsPanel;
    private Slider m_mouseSensitivitySlider;
    private TextMeshProUGUI m_mouseSensitivityValue;
    private Toggle m_cameraKickToggle;
    private float m_mouseSensitivity = 1f;
    private float m_committedMouseSensitivity = 1f;
    private bool m_cameraKickEnabled = true;
    private bool m_committedCameraKickEnabled = true;
    private bool m_isRefreshingCombatGeneral;

    /// <summary>현재 일시정지 UI가 열려 있으면 <c>true</c>입니다.</summary>
    public bool IsOpen => m_isOpen;

    private void Awake()
    {
        EnsureViewBuilt();
    }

    private void OnEnable()
    {
        if (Application.isPlaying)
        {
            EnsureViewBuilt();
        }
    }

    private void Update()
    {
        if (!WasEscapePressed())
        {
            return;
        }

        if (m_isOpen)
        {
            if (m_settingsPage.activeSelf)
            {
                ShowPausePage();
            }
            else
            {
                ResumeGameplay();
            }

            return;
        }

        // 다른 차단 UI가 Escape를 먼저 소비해야 하는 프레임에는 일시정지 메뉴를 열지 않습니다.
        if (Time.timeScale <= 0f || HasOpenBlockingUi())
        {
            return;
        }

        OpenPauseMenu();
    }

    private void LateUpdate()
    {
        if (!m_restoreCursorInLateUpdate)
        {
            return;
        }

        m_restoreCursorInLateUpdate = false;
        Cursor.lockState = m_previousCursorLockMode;
        Cursor.visible = m_previousCursorVisible;
    }

    private void OnDestroy()
    {
        if (m_isOpen)
        {
            RestoreGameplayState();
        }
    }

    private void OnDisable()
    {
        // Play Mode 종료, 스크립트 리로드, 씬 전환처럼 메뉴가 열린 채 컴포넌트가
        // 비활성화되어도 스쿼드 입력 게이트와 timeScale이 잠긴 채 남지 않게 합니다.
        if (!m_isOpen)
        {
            return;
        }

        SetViewActive(false);
        RestoreGameplayState();
        m_restoreCursorInLateUpdate = false;
    }

    /// <summary>게임플레이를 멈추고 Figma Gameplay Settings Screen을 엽니다.</summary>
    public void OpenPauseMenu()
    {
        EnsureViewBuilt();
        if (m_pausePage == null || m_settingsPage == null)
        {
            Debug.LogError("[GameplayPauseMenu] 런타임 UI를 생성하지 못해 메뉴를 열지 않습니다.", this);
            return;
        }

        if (m_isOpen)
        {
            SetViewActive(true);
            ShowPausePage();
            return;
        }

        m_previousTimeScale = Time.timeScale;
        m_previousCursorLockMode = Cursor.lockState;
        m_previousCursorVisible = Cursor.visible;
        m_isOpen = true;

        SetGameplayInputEnabled(false);
        if (m_pauseTime)
        {
            Time.timeScale = 0f;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        SetViewActive(true);
        ShowPausePage();
    }

    /// <summary>일시정지 UI를 닫고 이전 게임플레이 상태로 돌아갑니다.</summary>
    public void ResumeGameplay()
    {
        if (!m_isOpen)
        {
            return;
        }

        SetViewActive(false);
        RestoreGameplayState();
        m_restoreCursorInLateUpdate = true;
    }

    /// <summary>현재 설정을 기준점으로 기억하고 Figma Setting Scene 화면을 엽니다.</summary>
    /// <remarks>
    /// 설정 화면 원칙: 값을 바꾸면 바로 적용되지만 파일에는 '변경사항 저장'을 눌러야 남습니다. '되돌리기'는 이 화면을 열었을 때
    /// (또는 마지막으로 저장했을 때)의 값으로 돌아갑니다. 저장하지 않고 닫으면 바꾼 값은 이번 실행 동안만 유지됩니다.
    /// </remarks>
    public void OpenSettings()
    {
        EnsureViewBuilt();
        GameSettingManager settingManager = GameSettingManager.Instance;
        m_settingsBaseline = settingManager != null ? settingManager.CreateSnapshot() : null;
        m_crosshairBaseline = settingManager != null ? settingManager.GetCrosshairSettings() : null;
        m_displayBaseline = Screen.mainWindowDisplayInfo;
        m_hasDisplayBaseline = true;
        SyncCombatGeneralSettings(settingManager, true);
        RefreshVideoControls();

        // 메뉴는 Awake에서 만들어져 저장값을 읽기 전일 수 있으므로 열 때마다 지금 적용된 값을 다시 읽습니다.
        m_crosshairSettingsPanel?.RevertToCommittedValues();
        m_throwableCrosshairSettingsPanel?.RevertToCommittedValues();

        m_pausePage.SetActive(false);
        m_settingsPage.SetActive(true);
        SelectSettingsTab(0);
        SelectUiObject(m_firstSettingsTab);
    }

    /// <summary>설정 화면에서 일시정지 메뉴로 돌아갑니다.</summary>
    public void ReturnToPauseMenu()
    {
        ShowPausePage();
    }

    /// <summary>설정 화면을 열었을 때(또는 마지막으로 저장했을 때)의 값으로 되돌리고 바로 적용합니다.</summary>
    public void RevertSettings()
    {
        GameSettingManager settingManager = GameSettingManager.Instance;

        // 조준선은 바꿀 때마다 바로 적용되므로, 기억해 둔 값으로 통째로 되돌린 뒤 HUD에 다시 적용하고 패널이 그 값을 읽게 합니다.
        if (m_crosshairBaseline != null && settingManager != null)
        {
            settingManager.SetCrosshairSettings(m_crosshairBaseline.Clone());
            CrosshairSettingsService.Instance?.ReapplyFromSettings();
        }

        m_crosshairSettingsPanel?.RevertToCommittedValues();
        m_throwableCrosshairSettingsPanel?.RevertToCommittedValues();

        if (m_settingsBaseline == null || settingManager == null)
        {
            RevertCombatGeneralSettings();
            Debug.LogWarning("[GameplayPauseMenu] 되돌릴 설정 스냅샷이 없습니다.", this);
            return;
        }

        settingManager.ApplySnapshot(m_settingsBaseline);
        SyncCombatGeneralSettings(settingManager, true);
        RefreshVideoControls();

        // 모니터를 옮겼으면 기준 모니터로 다시 옮깁니다. 해상도는 위에서 기준값으로 되돌렸습니다.
        if (m_hasDisplayBaseline && !m_displayBaseline.Equals(Screen.mainWindowDisplayInfo))
        {
            MoveWindowToDisplay(m_displayBaseline);
        }
    }

    /// <summary>지금 적용된 설정을 사용자 설정 파일에 저장하고, 되돌리기 기준점을 지금 값으로 옮깁니다.</summary>
    public void SaveSettings()
    {
        GameSettingManager settingManager = GameSettingManager.Instance;
        if (settingManager == null)
        {
            Debug.LogWarning("[GameplayPauseMenu] GameSettingManager를 찾지 못해 설정을 저장할 수 없습니다.", this);
            return;
        }

        settingManager.ApplySettings();

        // 조준선 값은 편집할 때마다 넘기지만, 패널을 거치지 않은 값이 남지 않도록 파일을 쓰기 전에 한 번 더 넘깁니다.
        m_crosshairSettingsPanel?.ApplyPendingValues();
        m_throwableCrosshairSettingsPanel?.ApplyPendingValues();
        if (settingManager.SaveSettings())
        {
            m_settingsBaseline = settingManager.CreateSnapshot();
            m_crosshairBaseline = settingManager.GetCrosshairSettings();
            m_displayBaseline = Screen.mainWindowDisplayInfo;
            m_hasDisplayBaseline = true;
            m_crosshairSettingsPanel?.CommitCurrentValues();
            m_throwableCrosshairSettingsPanel?.CommitCurrentValues();
            CommitCombatGeneralSettings();
        }
    }

    /// <summary>Build Settings에 등록된 타이틀 씬으로 이동합니다.</summary>
    public void ReturnToTitle()
    {
        if (string.IsNullOrWhiteSpace(m_titleSceneName)
            || !Application.CanStreamedLevelBeLoaded(m_titleSceneName))
        {
            Debug.LogWarning(
                $"[GameplayPauseMenu] 타이틀 씬 '{m_titleSceneName}'이 Build Settings에 없어 이동하지 않습니다.",
                this);
            return;
        }

        SetViewActive(false);
        RestoreGameplayState();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SceneTransitionController.LoadScene(m_titleSceneName);
    }

    private void RestoreGameplayState()
    {
        m_isOpen = false;
        if (m_pauseTime)
        {
            Time.timeScale = m_previousTimeScale;
        }

        SetGameplayInputEnabled(true);
        Cursor.lockState = m_previousCursorLockMode;
        Cursor.visible = m_previousCursorVisible;
    }

    private void SetGameplayInputEnabled(bool enabled)
    {
        if (m_squadManager == null)
        {
            m_squadManager = FindFirstObjectByType<SquadManager>(FindObjectsInactive.Include);
        }

        if (m_squadManager != null)
        {
            m_squadManager.ApplyInputModeToSquad(enabled);
            return;
        }

        // 셸터는 스쿼드 대신 PlayerMove/CameraLook으로 움직입니다. 메뉴가 열린 동안 둘 다 잠그고,
        // 닫을 때는 열기 전 잠금 상태로 돌려놓습니다(시설 UI가 이미 잠가 둔 경우를 풀지 않기 위해서입니다).
        SetShelterInputEnabled(enabled);

        if (!enabled)
        {
            m_fallbackInputControllers.Clear();
            PlayerInputController[] inputs = FindObjectsByType<PlayerInputController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < inputs.Length; i++)
            {
                PlayerInputController input = inputs[i];
                if (input == null || !input.gameObject.activeInHierarchy)
                {
                    continue;
                }

                input.SetInputGate(false);
                m_fallbackInputControllers.Add(input);
            }

            return;
        }

        for (int i = 0; i < m_fallbackInputControllers.Count; i++)
        {
            PlayerInputController input = m_fallbackInputControllers[i];
            if (input != null)
            {
                input.SetInputGate(true);
            }
        }

        m_fallbackInputControllers.Clear();
    }

    private void SetShelterInputEnabled(bool enabled)
    {
        if (!enabled)
        {
            m_shelterMove = FindFirstObjectByType<PlayerMove>();
            m_shelterLook = FindFirstObjectByType<CameraLook>();
            m_previousShelterMoveLocked = m_shelterMove != null && m_shelterMove.IsMoveLocked;
            m_previousShelterLookLocked = m_shelterLook != null && m_shelterLook.IsLookLocked;
            m_shelterMove?.SetMoveLocked(true);
            m_shelterLook?.SetLookLocked(true);
            return;
        }

        if (m_shelterMove != null)
        {
            m_shelterMove.SetMoveLocked(m_previousShelterMoveLocked);
        }

        if (m_shelterLook != null)
        {
            m_shelterLook.SetLookLocked(m_previousShelterLookLocked);
        }

        m_shelterMove = null;
        m_shelterLook = null;
    }

    private void ShowPausePage()
    {
        m_settingsPage.SetActive(false);
        m_pausePage.SetActive(true);
        ResetPauseSelection();

        // 타이틀처럼 미리 고른 항목 없이 엽니다. 설정 화면 탭 같은 이전 선택이 남아 있으면 비웁니다.
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    private void SelectSettingsTab(int selectedIndex)
    {
        for (int i = 0; i < m_tabBackgrounds.Length; i++)
        {
            bool selected = i == selectedIndex;
            m_tabBackgrounds[i].color = selected
                ? new Color(1f, 1f, 1f, 0.53f)
                : Color.clear;
            m_tabOutlines[i].enabled = selected;

            if (m_settingsContentPages[i] != null)
            {
                m_settingsContentPages[i].SetActive(selected);
            }
        }
    }

    private bool HasOpenBlockingUi()
    {
        UIManager[] managers = FindObjectsByType<UIManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < managers.Length; i++)
        {
            if (managers[i] != null && managers[i].isActiveAndEnabled && managers[i].HasOpenBlockingUI)
            {
                return true;
            }
        }

        return false;
    }

    private void BuildView()
    {
        Canvas canvas = GetOrAddComponent<Canvas>(gameObject);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10000;

        CanvasScaler scaler = GetOrAddComponent<CanvasScaler>(gameObject);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        // Figma의 하단 1019px 액션 바가 초광폭 Game View에서도 잘리지 않도록 높이를 기준으로 맞춥니다.
        scaler.matchWidthOrHeight = 1f;

        GetOrAddComponent<GraphicRaycaster>(gameObject);

        m_viewRoot = CreateStretchObject("RuntimeView", transform);
        BuildPausePage();
        BuildSettingsPage();
    }

    private void EnsureViewBuilt()
    {
        if (m_viewRoot != null && m_pausePage != null && m_settingsPage != null)
        {
            return;
        }

        if (m_viewRoot != null)
        {
            m_viewRoot.SetActive(false);
            if (Application.isPlaying)
            {
                Destroy(m_viewRoot);
            }
            else
            {
                DestroyImmediate(m_viewRoot);
            }
        }

        m_viewRoot = null;
        m_pausePage = null;
        m_settingsPage = null;
        m_resumeButton = null;
        m_firstSettingsTab = null;
        m_pauseButtons.Clear();
        m_pauseSelectionIndicator = null;
        m_resolutionDropdown = null;
        m_monitorDropdown = null;
        m_monitorHelp = null;
        m_screenModeDropdown = null;
        m_vSyncToggle = null;
        m_frameLimitDropdown = null;
        m_frameLimitHelp = null;
        m_frameLimitSlider = null;
        m_frameLimitValue = null;
        m_crosshairSettingsPanel = null;
        m_throwableCrosshairSettingsPanel = null;
        m_mouseSensitivitySlider = null;
        m_mouseSensitivityValue = null;
        m_cameraKickToggle = null;

        ResolveFonts();
        BuildView();
        SetViewActive(false);
    }

    /// <summary>Figma 'ESC Menu UI 2' 기준 일시정지 메뉴를 만듭니다.</summary>
    /// <remarks>
    /// 버튼은 배경 없이 글자만 보입니다. 선택 표시는 타이틀 메뉴와 같은 <see cref="MenuSelectionIndicator"/>가 맡습니다:
    /// 마우스를 올린 항목, 없으면 키보드로 고른 항목의 글자 양옆에 장식이 뜨고, 둘 다 없으면 사라집니다.
    /// 타이틀처럼 열 때 미리 선택해 두는 항목은 없습니다.
    /// </remarks>
    private void BuildPausePage()
    {
        m_pausePage = CreateStretchObject("Gameplay Settings Screen", m_viewRoot.transform);
        // Figma esc 1 이미지는 검정 60% 단색이라 텍스처 대신 같은 색으로 깝니다.
        CreateStretchImage("Dim Background", m_pausePage.transform, new Color(0f, 0f, 0f, 0.6f));

        // Figma 글자 윗변 y입니다. 설정 버튼을 빼면 남은 두 버튼을 세 버튼일 때의 가운데(설정 자리)를 중심으로 벌려 놓습니다.
        float resumeTop = m_showSettingsButton ? PauseResumeTop : PauseSettingsTop - (PauseButtonSpacing * 0.5f);
        float titleTop = m_showSettingsButton ? PauseTitleTop : PauseSettingsTop + (PauseButtonSpacing * 0.5f);

        m_resumeButton = CreatePauseButton("Resume", "이어서 하기", resumeTop);
        m_resumeButton.onClick.AddListener(ResumeGameplay);

        if (m_showSettingsButton)
        {
            Button settingsButton = CreatePauseButton("Settings", "설정", PauseSettingsTop);
            settingsButton.onClick.AddListener(OpenSettings);
        }

        Button titleButton = CreatePauseButton("Return To Title", "타이틀로 돌아가기", titleTop);
        titleButton.onClick.AddListener(ReturnToTitle);

        if (m_menuSelectionLeftTexture != null && m_menuSelectionRightTexture != null)
        {
            m_pauseSelectionIndicator = m_pausePage.AddComponent<MenuSelectionIndicator>();
            m_pauseSelectionIndicator.Configure(
                m_menuSelectionLeftTexture,
                m_menuSelectionRightTexture,
                m_pauseButtons.ToArray(),
                PauseSelectionTextGap,
                PauseSelectionFadeDuration);
        }
    }

    /// <summary>글자만 보이는 일시정지 메뉴 버튼을 만듭니다. 글자 윗변(Figma top)을 기준으로 가운데에 놓습니다.</summary>
    /// <remarks>
    /// 선택 장식은 글자 칸의 왼쪽 끝과 글자 폭으로 위치를 잡습니다. 그래서 글자 칸을 글자 폭에 딱 맞춰 버튼 가운데에 두고,
    /// 마우스 판정을 위해 버튼만 양옆으로 조금 넓힙니다.
    /// </remarks>
    private Button CreatePauseButton(string name, string label, float textTop)
    {
        float top = textTop - ((PauseButtonHeight - PauseButtonFontSize) * 0.5f);
        Button button = CreateButton(
            name,
            m_pausePage.transform,
            new Vector2(0f, -top),
            new Vector2(0f, PauseButtonHeight),
            label,
            PauseButtonFontSize,
            Color.clear);

        TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>(true);
        text.font = m_regularFont;
        text.ForceMeshUpdate();
        float textWidth = Mathf.Ceil(text.preferredWidth);

        RectTransform labelRect = text.rectTransform;
        labelRect.anchorMin = new Vector2(0.5f, 0f);
        labelRect.anchorMax = new Vector2(0.5f, 1f);
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = Vector2.zero;
        labelRect.sizeDelta = new Vector2(textWidth, 0f);

        float width = textWidth + (PauseButtonHitPadding * 2f);
        RectTransform rect = (RectTransform)button.transform;
        rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
        rect.anchoredPosition = new Vector2((ReferenceWidth - width) * 0.5f, rect.anchoredPosition.y);

        m_pauseButtons.Add(button);
        return button;
    }

    private void BuildSettingsPage()
    {
        m_settingsPage = CreateStretchObject("Setting Scene", m_viewRoot.transform);
        CreateStretchImage("Dim Background", m_settingsPage.transform, new Color(74f / 255f, 74f / 255f, 74f / 255f, 0.86f));

        Image header = CreateTopLeftImage(
            "Setting Popup",
            m_settingsPage.transform,
            new Vector2(45f, 0f),
            new Vector2(1920f, 48f),
            new Color(217f / 255f, 217f / 255f, 217f / 255f, 0.46f));
        header.raycastTarget = true;

        Image icon = CreateTopLeftImage(
            "Settings Icon",
            m_settingsPage.transform,
            new Vector2(60f, -13f),
            new Vector2(24f, 24f),
            Color.white);
        icon.sprite = m_settingsIcon;
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        CreateTopLeftText(
            "Settings Title",
            m_settingsPage.transform,
            new Vector2(145f, -8f),
            new Vector2(110f, 32f),
            "설정",
            24f,
            TextAlignmentOptions.Left);

        CreateTopLeftImage(
            "Tabs Background",
            m_settingsPage.transform,
            new Vector2(399f, 0f),
            new Vector2(1566f, 48f),
            new Color(217f / 255f, 217f / 255f, 217f / 255f, 0.56f));

        string[] tabLabels = { "비디오", "오디오", "전투", "고급 설정" };
        for (int i = 0; i < tabLabels.Length; i++)
        {
            int tabIndex = i;
            Button tab = CreateButton(
                $"Tab {tabLabels[i]}",
                m_settingsPage.transform,
                new Vector2(399f + (200f * i), 0f),
                new Vector2(200f, 48f),
                tabLabels[i],
                24f,
                Color.clear);

            Image tabImage = tab.GetComponent<Image>();
            Outline outline = tab.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(242f / 255f, 0f, 0f, 1f);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = false;
            tab.onClick.AddListener(() => SelectSettingsTab(tabIndex));

            m_tabBackgrounds[i] = tabImage;
            m_tabOutlines[i] = outline;
            if (i == 0)
            {
                m_firstSettingsTab = tab;
            }
        }

        BuildSettingsContent();

        Button revertButton = CreateButton(
            "Revert",
            m_settingsPage.transform,
            new Vector2(1455f, -1019f),
            new Vector2(102f, 32f),
            "되돌리기",
            16f,
            new Color(147f / 255f, 147f / 255f, 147f / 255f, 0.85f));
        revertButton.onClick.AddListener(RevertSettings);

        Button saveButton = CreateButton(
            "Save Changes",
            m_settingsPage.transform,
            new Vector2(1627f, -1019f),
            new Vector2(102f, 32f),
            "변경사항 저장",
            16f,
            new Color(147f / 255f, 147f / 255f, 147f / 255f, 0.85f));
        saveButton.onClick.AddListener(SaveSettings);

        Button backButton = CreateButton(
            "Back",
            m_settingsPage.transform,
            new Vector2(1786f, -1019f),
            new Vector2(102f, 32f),
            "뒤로가기",
            24f,
            Color.clear,
            m_backButtonFrame);
        backButton.onClick.AddListener(ReturnToPauseMenu);

        SelectSettingsTab(0);
    }

    private void BuildSettingsContent()
    {
        for (int i = 0; i < m_settingsContentPages.Length; i++)
        {
            m_settingsContentPages[i] = CreateTopLeftObject(
                $"Settings Content {i}",
                m_settingsPage.transform,
                new Vector2(0f, -48f),
                new Vector2(ReferenceWidth, 971f));
        }

        BuildVideoSettingsPage(m_settingsContentPages[0].transform);
        BuildPlaceholderPage(
            m_settingsContentPages[1].transform,
            "오디오",
            "기존 오디오 설정이 배치될 영역입니다.");
        BuildCombatSettingsPage(m_settingsContentPages[2].transform);
        BuildPlaceholderPage(
            m_settingsContentPages[3].transform,
            "고급 설정",
            "고급 그래픽 및 접근성 설정이 배치될 영역입니다.");
    }

    /// <summary>비디오 탭입니다. 지금은 해상도 드롭다운만 있습니다.</summary>
    /// <remarks>
    /// 값은 다른 설정과 같이 <see cref="GameSettingManager"/>가 가집니다. 고르면 바로 적용되고, '되돌리기'는 설정 화면을
    /// 열 때의 해상도로, '변경사항 저장'은 설정 파일에 남깁니다. 에디터 Game 뷰에서는 해상도가 바뀌지 않습니다.
    /// </remarks>
    private void BuildVideoSettingsPage(Transform parent)
    {
        TextMeshProUGUI title = CreateTopLeftText(
            "Video Title",
            parent,
            new Vector2(145f, -34f),
            new Vector2(500f, 50f),
            "비디오",
            32f,
            TextAlignmentOptions.Left);
        title.font = m_boldFont != null ? m_boldFont : m_regularFont;

        TextMeshProUGUI notice = CreateTopLeftText(
            "Video Notice",
            parent,
            new Vector2(145f, -82f),
            new Vector2(900f, 32f),
            "고르면 바로 바뀌고, 변경사항 저장을 눌러야 다음 실행에도 유지됩니다.",
            17f,
            TextAlignmentOptions.Left);
        notice.color = new Color(1f, 1f, 1f, 0.6f);

        Image panel = CreateTopLeftImage(
            "Video Controls",
            parent,
            new Vector2(145f, -132f),
            new Vector2(650f, 590f),
            new Color(0.035f, 0.035f, 0.035f, 0.92f));
        Outline outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.16f);
        outline.effectDistance = new Vector2(1f, -1f);

        CreateTopLeftText(
            "Monitor Label",
            panel.transform,
            new Vector2(32f, -30f),
            new Vector2(220f, 30f),
            "모니터",
            18f,
            TextAlignmentOptions.Left);
        m_monitorHelp = CreateTopLeftText(
            "Monitor Help",
            panel.transform,
            new Vector2(254f, -32f),
            new Vector2(360f, 26f),
            "모니터가 하나뿐이면 바꿀 수 없습니다.",
            15f,
            TextAlignmentOptions.Right);
        m_monitorHelp.color = new Color(1f, 1f, 1f, 0.5f);
        m_monitorDropdown = CreateDropdown("Monitor Dropdown", panel.transform, new Vector2(32f, -76f), new Vector2(582f, 44f));
        m_monitorDropdown.onValueChanged.AddListener(OnMonitorChanged);

        TextMeshProUGUI resolutionHelp = CreateTopLeftText(
            "Resolution Help",
            panel.transform,
            new Vector2(254f, -142f),
            new Vector2(360f, 26f),
            "주사율은 전체 화면에서만 그대로 적용됩니다.",
            15f,
            TextAlignmentOptions.Right);
        resolutionHelp.color = new Color(1f, 1f, 1f, 0.5f);

        CreateTopLeftText(
            "Resolution Label",
            panel.transform,
            new Vector2(32f, -140f),
            new Vector2(220f, 30f),
            "해상도",
            18f,
            TextAlignmentOptions.Left);

        m_resolutionDropdown = CreateDropdown("Resolution Dropdown", panel.transform, new Vector2(32f, -186f), new Vector2(582f, 44f));
        m_resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);

        CreateTopLeftText(
            "Screen Mode Label",
            panel.transform,
            new Vector2(32f, -250f),
            new Vector2(220f, 30f),
            "화면 모드",
            18f,
            TextAlignmentOptions.Left);
        m_screenModeDropdown = CreateDropdown("Screen Mode Dropdown", panel.transform, new Vector2(32f, -296f), new Vector2(582f, 44f));
        List<string> screenModeLabels = new(s_screenModes.Length);
        for (int i = 0; i < s_screenModes.Length; i++)
        {
            screenModeLabels.Add(s_screenModes[i].label);
        }

        m_screenModeDropdown.ClearOptions();
        m_screenModeDropdown.AddOptions(screenModeLabels);
        m_screenModeDropdown.onValueChanged.AddListener(OnScreenModeChanged);

        m_vSyncToggle = CreateToggle("VSync Toggle", panel.transform, new Vector2(32f, -366f), "수직 동기화");
        m_vSyncToggle.onValueChanged.AddListener(OnVSyncChanged);

        CreateTopLeftText(
            "Frame Limit Label",
            panel.transform,
            new Vector2(32f, -426f),
            new Vector2(220f, 30f),
            "최대 프레임 제한",
            18f,
            TextAlignmentOptions.Left);
        m_frameLimitHelp = CreateTopLeftText(
            "Frame Limit Help",
            panel.transform,
            new Vector2(254f, -428f),
            new Vector2(360f, 26f),
            "수직 동기화가 켜져 있으면 모니터 주사율을 따릅니다.",
            15f,
            TextAlignmentOptions.Right);
        m_frameLimitHelp.color = new Color(1f, 1f, 1f, 0.5f);

        // 슬라이더가 우선입니다. 슬라이더를 움직이면 드롭다운은 '사용자 지정'으로 바뀌고,
        // 드롭다운에서 프리셋을 고르면 슬라이더가 그 값으로 옮겨 갑니다.
        m_frameLimitSlider = CreateSlider("Frame Limit Slider", panel.transform, new Vector2(32f, -470f), new Vector2(470f, 24f), MinFrameLimit, MaxFrameLimit);
        m_frameLimitSlider.wholeNumbers = true;
        m_frameLimitSlider.onValueChanged.AddListener(OnFrameLimitSliderChanged);
        m_frameLimitValue = CreateTopLeftText(
            "Frame Limit Value",
            panel.transform,
            new Vector2(510f, -467f),
            new Vector2(104f, 30f),
            string.Empty,
            18f,
            TextAlignmentOptions.Right);
        m_frameLimitValue.font = m_boldFont != null ? m_boldFont : m_regularFont;

        m_frameLimitDropdown = CreateDropdown("Frame Limit Dropdown", panel.transform, new Vector2(32f, -514f), new Vector2(582f, 44f));
        List<string> frameLabels = new(s_frameLimitPresets.Length + 2);
        for (int i = 0; i < s_frameLimitPresets.Length; i++)
        {
            frameLabels.Add($"{s_frameLimitPresets[i]} FPS");
        }

        frameLabels.Add("사용자 지정");
        frameLabels.Add("제한 없음");
        m_frameLimitDropdown.ClearOptions();
        m_frameLimitDropdown.AddOptions(frameLabels);
        m_frameLimitDropdown.onValueChanged.AddListener(OnFrameLimitDropdownChanged);

        RefreshVideoControls();
    }

    /// <summary>설정 화면 공통 모양의 가로 슬라이더를 만듭니다(마우스 감도 슬라이더와 같은 색과 구성).</summary>
    private Slider CreateSlider(string name, Transform parent, Vector2 topLeftPosition, Vector2 size, float min, float max)
    {
        GameObject sliderObject = CreateTopLeftObject(name, parent, topLeftPosition, size);
        Slider slider = sliderObject.AddComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;

        Image background = CreateStretchImage("Background", sliderObject.transform, new Color(1f, 1f, 1f, 0.13f));
        background.rectTransform.offsetMin = new Vector2(0f, 8f);
        background.rectTransform.offsetMax = new Vector2(0f, -8f);

        GameObject fillArea = CreateStretchObject("Fill Area", sliderObject.transform);
        RectTransform fillAreaRect = (RectTransform)fillArea.transform;
        fillAreaRect.offsetMin = new Vector2(0f, 8f);
        fillAreaRect.offsetMax = new Vector2(-12f, -8f);
        Image fill = CreateStretchImage("Fill", fillArea.transform, new Color(0.95f, 0.08f, 0.08f, 1f));

        GameObject handleArea = CreateStretchObject("Handle Slide Area", sliderObject.transform);
        RectTransform handleAreaRect = (RectTransform)handleArea.transform;
        handleAreaRect.offsetMin = new Vector2(8f, 0f);
        handleAreaRect.offsetMax = new Vector2(-8f, 0f);
        Image handle = CreateTopLeftImage("Handle", handleArea.transform, Vector2.zero, new Vector2(18f, 18f), Color.white);
        RectTransform handleRect = handle.rectTransform;
        handleRect.anchorMin = new Vector2(0.5f, 0.5f);
        handleRect.anchorMax = new Vector2(0.5f, 0.5f);
        handleRect.pivot = new Vector2(0.5f, 0.5f);
        handleRect.anchoredPosition = Vector2.zero;

        slider.fillRect = fill.rectTransform;
        slider.handleRect = handleRect;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        return slider;
    }

    /// <summary>TMP 기본 드롭다운을 만들고 설정 화면 색과 폰트에 맞춥니다.</summary>
    private TMP_Dropdown CreateDropdown(string name, Transform parent, Vector2 topLeftPosition, Vector2 size)
    {
        GameObject dropdownObject = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
        dropdownObject.name = name;
        RectTransform rect = (RectTransform)dropdownObject.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = topLeftPosition;
        rect.sizeDelta = size;

        TMP_Dropdown dropdown = dropdownObject.GetComponent<TMP_Dropdown>();
        dropdownObject.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.13f);
        ColorBlock colors = dropdown.colors;
        colors.highlightedColor = new Color(1f, 1f, 1f, 1.4f);
        colors.selectedColor = Color.white;
        dropdown.colors = colors;

        // 기본 화살표는 스프라이트가 없어 흰 네모로 보이므로 숨깁니다.
        Transform arrow = rect.Find("Arrow");
        if (arrow != null)
        {
            arrow.gameObject.SetActive(false);
        }

        RectTransform template = dropdown.template;
        template.sizeDelta = new Vector2(template.sizeDelta.x, 240f);
        template.GetComponent<Image>().color = new Color(0.06f, 0.06f, 0.06f, 0.98f);

        Toggle item = template.GetComponentInChildren<Toggle>(true);
        RectTransform itemRect = (RectTransform)item.transform;
        itemRect.sizeDelta = new Vector2(itemRect.sizeDelta.x, 40f);
        RectTransform content = (RectTransform)itemRect.parent;
        content.sizeDelta = new Vector2(content.sizeDelta.x, 40f);
        item.targetGraphic.color = new Color(0.12f, 0.12f, 0.12f, 1f);
        ColorBlock itemColors = item.colors;
        itemColors.highlightedColor = new Color(0.95f, 0.08f, 0.08f, 1f);
        itemColors.selectedColor = new Color(0.95f, 0.08f, 0.08f, 1f);
        item.colors = itemColors;
        if (item.graphic != null)
        {
            item.graphic.color = new Color(0.95f, 0.08f, 0.08f, 1f);
        }

        // 기본 스크롤바도 스프라이트가 없어 흰 막대로 보이므로 어두운 바탕에 회색 손잡이로 맞춥니다.
        Scrollbar scrollbar = template.GetComponentInChildren<Scrollbar>(true);
        if (scrollbar != null)
        {
            scrollbar.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f);
            if (scrollbar.targetGraphic != null)
            {
                scrollbar.targetGraphic.color = new Color(1f, 1f, 1f, 0.35f);
            }

            RectTransform scrollbarRect = (RectTransform)scrollbar.transform;
            scrollbarRect.sizeDelta = new Vector2(8f, scrollbarRect.sizeDelta.y);
        }

        foreach (TMP_Text text in dropdownObject.GetComponentsInChildren<TMP_Text>(true))
        {
            text.font = m_regularFont;
            text.fontSize = 18f;
            text.color = Color.white;
        }

        return dropdown;
    }

    /// <summary>모니터가 지원하는 해상도로 목록을 다시 만들고, 지금 설정 해상도를 고릅니다.</summary>
    private void RefreshVideoControls()
    {
        if (m_resolutionDropdown == null)
        {
            return;
        }

        GameSettingManager settingManager = GameSettingManager.Instance;
        Vector2Int current = settingManager != null
            ? new Vector2Int(settingManager.ResolutionWidth, settingManager.ResolutionHeight)
            : new Vector2Int(Screen.width, Screen.height);
        RefreshRate currentRate = settingManager != null ? settingManager.RefreshRate : default;

        // 첫 칸은 게임 창이 떠 있는 모니터의 기본(바탕화면) 해상도와 주사율입니다. 나머지는 그 모니터가 지원하는
        // 해상도·주사율 조합을 큰 해상도, 높은 주사율 순으로 모두 보여 줍니다. 같은 해상도라도 주사율마다 따로 고를 수 있습니다.
        DisplayInfo display = Screen.mainWindowDisplayInfo;
        DisplayOption native = new(new Vector2Int(display.width, display.height), display.refreshRate);
        if (native.Size.x <= 0 || native.Size.y <= 0)
        {
            native = new DisplayOption(
                new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height),
                Screen.currentResolution.refreshRateRatio);
        }

        List<DisplayOption> modes = new();
        Resolution[] resolutions = Screen.resolutions;
        for (int i = 0; i < resolutions.Length; i++)
        {
            DisplayOption option = new(new Vector2Int(resolutions[i].width, resolutions[i].height), resolutions[i].refreshRateRatio);
            if (!option.SameAs(native) && !modes.Exists(existing => existing.SameAs(option)))
            {
                modes.Add(option);
            }
        }

        // 정렬 기준은 해상도입니다. 해상도별로 묶어 큰 해상도부터 보여 주고, 한 해상도 안에서만 주사율을 높은 순으로 나열합니다.
        // 모니터 해상도 묶음은 첫 칸(모니터 해상도) 바로 아래에 같은 해상도의 다른 주사율이 이어지도록 맨 앞에 둡니다.
        modes.Sort((a, b) =>
        {
            bool aNative = a.Size == native.Size;
            bool bNative = b.Size == native.Size;
            if (aNative != bNative)
            {
                return aNative ? -1 : 1;
            }

            return a.Size.x != b.Size.x ? b.Size.x.CompareTo(a.Size.x)
                : a.Size.y != b.Size.y ? b.Size.y.CompareTo(a.Size.y)
                : b.Hz.CompareTo(a.Hz);
        });

        m_resolutionOptions.Clear();
        m_resolutionOptions.Add(native);
        m_resolutionOptions.AddRange(modes);

        // 지금 설정과 맞는 칸을 고릅니다. 주사율을 지정하지 않았으면(이전 저장값) 크기만 맞춰 첫 칸을 고르고,
        // 목록에 없는 조합이면 맨 아래에 넣어 그대로 보여 줍니다.
        DisplayOption saved = new(current, currentRate);
        int selected = currentRate.numerator > 0
            ? m_resolutionOptions.FindIndex(option => option.SameAs(saved))
            : m_resolutionOptions.FindIndex(option => option.Size == current);
        if (selected < 0)
        {
            m_resolutionOptions.Add(saved);
            selected = m_resolutionOptions.Count - 1;
        }

        List<string> labels = new(m_resolutionOptions.Count);
        for (int i = 0; i < m_resolutionOptions.Count; i++)
        {
            string label = m_resolutionOptions[i].Label;
            labels.Add(i == 0 ? $"모니터 해상도 {label}" : label);
        }

        m_isRefreshingVideo = true;
        RefreshMonitorControls();
        m_resolutionDropdown.ClearOptions();
        m_resolutionDropdown.AddOptions(labels);
        m_resolutionDropdown.SetValueWithoutNotify(selected);
        m_resolutionDropdown.RefreshShownValue();

        FullScreenMode screenMode = settingManager != null ? settingManager.ScreenMode : Screen.fullScreenMode;
        bool vSync = settingManager != null ? settingManager.VSyncEnabled : QualitySettings.vSyncCount > 0;
        int frameLimit = settingManager != null ? settingManager.TargetFrameRate : Application.targetFrameRate;

        if (m_screenModeDropdown != null)
        {
            int screenModeIndex = System.Array.FindIndex(s_screenModes, entry => entry.mode == screenMode);
            m_screenModeDropdown.SetValueWithoutNotify(screenModeIndex >= 0 ? screenModeIndex : 1);
            m_screenModeDropdown.RefreshShownValue();
        }

        if (m_vSyncToggle != null)
        {
            m_vSyncToggle.SetIsOnWithoutNotify(vSync);
        }

        if (m_frameLimitDropdown != null && m_frameLimitSlider != null)
        {
            // 저장값이 프리셋과 같으면 그 칸, 아니면 '사용자 지정', 0 이하면 '제한 없음'을 보여 줍니다.
            // 제한 없음일 때 슬라이더는 마지막 위치를 그대로 둡니다.
            if (frameLimit > 0)
            {
                m_frameLimitSlider.SetValueWithoutNotify(Mathf.Clamp(frameLimit, MinFrameLimit, MaxFrameLimit));
                int presetIndex = System.Array.IndexOf(s_frameLimitPresets, frameLimit);
                m_frameLimitDropdown.SetValueWithoutNotify(presetIndex >= 0 ? presetIndex : FrameLimitCustomIndex);
            }
            else
            {
                m_frameLimitDropdown.SetValueWithoutNotify(FrameLimitUnlimitedIndex);
            }

            m_frameLimitDropdown.RefreshShownValue();
            RefreshFrameLimitValueText(frameLimit);
        }

        RefreshFrameLimitInteractable(vSync);
        m_isRefreshingVideo = false;
    }

    private void RefreshFrameLimitValueText(int frameLimit)
    {
        if (m_frameLimitValue != null)
        {
            m_frameLimitValue.text = frameLimit > 0 ? $"{frameLimit} FPS" : "제한 없음";
        }
    }

    /// <summary>수직 동기화가 켜지면 프레임 제한은 적용되지 않으므로 고를 수 없게 하고 안내를 보여 줍니다.</summary>
    private void RefreshFrameLimitInteractable(bool vSync)
    {
        if (m_frameLimitDropdown != null)
        {
            m_frameLimitDropdown.interactable = !vSync;
            m_frameLimitDropdown.captionText.alpha = vSync ? 0.4f : 1f;
        }

        if (m_frameLimitSlider != null)
        {
            m_frameLimitSlider.interactable = !vSync;
        }

        if (m_frameLimitValue != null)
        {
            m_frameLimitValue.alpha = vSync ? 0.4f : 1f;
        }

        if (m_frameLimitHelp != null)
        {
            m_frameLimitHelp.gameObject.SetActive(vSync);
        }
    }

    private void OnScreenModeChanged(int index)
    {
        if (m_isRefreshingVideo || index < 0 || index >= s_screenModes.Length)
        {
            return;
        }

        GameSettingManager settingManager = GameSettingManager.Instance;
        if (settingManager == null)
        {
            Debug.LogWarning("[GameplayPauseMenu] GameSettingManager를 찾지 못해 화면 모드를 바꿀 수 없습니다.", this);
            return;
        }

        settingManager.SetScreenMode(s_screenModes[index].mode);
    }

    private void OnVSyncChanged(bool enabled)
    {
        if (m_isRefreshingVideo)
        {
            return;
        }

        GameSettingManager.Instance?.SetVSync(enabled);
        RefreshFrameLimitInteractable(enabled);
    }

    /// <summary>슬라이더를 움직이면 그 값으로 바로 제한하고, 드롭다운은 '사용자 지정'으로 바꿉니다.</summary>
    private void OnFrameLimitSliderChanged(float value)
    {
        if (m_isRefreshingVideo)
        {
            return;
        }

        int frameLimit = Mathf.RoundToInt(value);
        GameSettingManager.Instance?.SetTargetFrameRate(frameLimit);
        m_frameLimitDropdown.SetValueWithoutNotify(FrameLimitCustomIndex);
        m_frameLimitDropdown.RefreshShownValue();
        RefreshFrameLimitValueText(frameLimit);
    }

    /// <summary>
    /// 드롭다운에서 프리셋을 고르면 슬라이더를 그 값으로 옮깁니다. '사용자 지정'은 지금 슬라이더 값을, '제한 없음'은 제한을 풉니다.
    /// </summary>
    private void OnFrameLimitDropdownChanged(int index)
    {
        if (m_isRefreshingVideo)
        {
            return;
        }

        int frameLimit;
        if (index < s_frameLimitPresets.Length)
        {
            frameLimit = s_frameLimitPresets[index];
            m_frameLimitSlider.SetValueWithoutNotify(frameLimit);
        }
        else if (index == FrameLimitCustomIndex)
        {
            frameLimit = Mathf.RoundToInt(m_frameLimitSlider.value);
        }
        else
        {
            frameLimit = -1;
        }

        GameSettingManager.Instance?.SetTargetFrameRate(frameLimit);
        RefreshFrameLimitValueText(frameLimit);
    }

    /// <summary>연결된 모니터 목록을 다시 읽고, 지금 게임 창이 떠 있는 모니터를 고릅니다.</summary>
    private void RefreshMonitorControls()
    {
        if (m_monitorDropdown == null)
        {
            return;
        }

        m_displays.Clear();
        Screen.GetDisplayLayout(m_displays);
        DisplayInfo current = Screen.mainWindowDisplayInfo;
        if (m_displays.Count == 0)
        {
            m_displays.Add(current);
        }

        List<string> labels = new(m_displays.Count);
        int currentIndex = 0;
        for (int i = 0; i < m_displays.Count; i++)
        {
            DisplayInfo info = m_displays[i];
            string name = string.IsNullOrWhiteSpace(info.name) ? "모니터" : info.name;
            labels.Add($"{i + 1}. {name} ({info.width} x {info.height})");
            if (info.Equals(current))
            {
                currentIndex = i;
            }
        }

        m_monitorDropdown.ClearOptions();
        m_monitorDropdown.AddOptions(labels);
        m_monitorDropdown.SetValueWithoutNotify(currentIndex);
        m_monitorDropdown.RefreshShownValue();

        bool canChoose = m_displays.Count > 1 && !m_isMovingWindow;
        m_monitorDropdown.interactable = canChoose;
        m_monitorDropdown.captionText.alpha = canChoose ? 1f : 0.4f;
        if (m_monitorHelp != null)
        {
            m_monitorHelp.gameObject.SetActive(m_displays.Count <= 1);
        }
    }

    private void OnMonitorChanged(int index)
    {
        if (m_isRefreshingVideo || index < 0 || index >= m_displays.Count)
        {
            return;
        }

        MoveWindowToDisplay(m_displays[index]);
    }

    /// <summary>
    /// 게임 창을 다른 모니터로 옮깁니다. 옮기기는 다음 프레임 이후에 끝나므로 끝난 뒤 그 모니터 기준으로 해상도 목록을 다시 채웁니다.
    /// </summary>
    /// <remarks>
    /// 전체 화면이면 모니터 왼쪽 위에, 창 모드면 그 모니터 가운데에 둡니다. Windows/macOS/Linux 빌드에서만 동작하고
    /// 에디터에서는 옮기지 않습니다(Game 뷰는 에디터 창 안에 있어서).
    /// </remarks>
    private void MoveWindowToDisplay(DisplayInfo display)
    {
        if (display.Equals(Screen.mainWindowDisplayInfo))
        {
            return;
        }

#if UNITY_EDITOR
        Debug.Log($"[GameplayPauseMenu] 에디터에서는 모니터를 옮기지 않습니다. 빌드에서 '{display.name}'(으)로 옮겨집니다.", this);
        RefreshVideoControls();
#else
        Vector2Int position = Screen.fullScreenMode == FullScreenMode.Windowed
            ? new Vector2Int((display.width - Screen.width) / 2, (display.height - Screen.height) / 2)
            : Vector2Int.zero;

        m_isMovingWindow = true;
        AsyncOperation operation = Screen.MoveMainWindowTo(display, position);
        RefreshVideoControls();
        operation.completed += _ =>
        {
            m_isMovingWindow = false;
            RefreshVideoControls();
        };
#endif
    }

    private void OnResolutionChanged(int index)
    {
        if (m_isRefreshingVideo || index < 0 || index >= m_resolutionOptions.Count)
        {
            return;
        }

        GameSettingManager settingManager = GameSettingManager.Instance;
        if (settingManager == null)
        {
            Debug.LogWarning("[GameplayPauseMenu] GameSettingManager를 찾지 못해 해상도를 바꿀 수 없습니다.", this);
            return;
        }

        DisplayOption option = m_resolutionOptions[index];
        settingManager.SetDisplayMode(option.Size.x, option.Size.y, option.Rate);
    }

    private void BuildCombatSettingsPage(Transform parent)
    {
        CreateTopLeftText(
            "Combat Section Label",
            parent,
            new Vector2(145f, -38f),
            new Vector2(220f, 38f),
            "전투",
            24f,
            TextAlignmentOptions.Left).font = m_boldFont != null ? m_boldFont : m_regularFont;

        string[] labels = { "전투 일반", "캐릭터 조준선", "투척물 조준선" };
        for (int i = 0; i < labels.Length; i++)
        {
            int subTabIndex = i;
            Button button = CreateButton(
                $"Combat SubTab {labels[i]}",
                parent,
                new Vector2(145f, -96f - (58f * i)),
                new Vector2(220f, 46f),
                labels[i],
                19f,
                Color.clear);
            Image background = button.GetComponent<Image>();
            Outline outline = button.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(242f / 255f, 0f, 0f, 1f);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = false;
            button.onClick.AddListener(() => SelectCombatSubTab(subTabIndex));
            m_combatSubTabBackgrounds[i] = background;
            m_combatSubTabOutlines[i] = outline;
        }

        m_combatSubPages[0] = CreateStretchObject("Combat General Page", parent);
        m_combatSubPages[1] = CreateStretchObject("Crosshair Settings Page", parent);
        m_combatSubPages[2] = CreateStretchObject("Throwable Crosshair Settings Page", parent);
        m_combatSubPages[0].transform.SetAsFirstSibling();
        m_combatSubPages[1].transform.SetAsFirstSibling();
        m_combatSubPages[2].transform.SetAsFirstSibling();

        BuildCombatGeneralSettings(m_combatSubPages[0].transform);

        m_crosshairSettingsPanel = m_combatSubPages[1].AddComponent<CrosshairSettingsPanel>();
        m_crosshairSettingsPanel.Build(
            m_combatSubPages[1].transform as RectTransform,
            m_regularFont,
            m_boldFont);

        m_throwableCrosshairSettingsPanel = m_combatSubPages[2].AddComponent<CrosshairSettingsPanel>();
        m_throwableCrosshairSettingsPanel.BuildSharedThrowable(
            m_combatSubPages[2].transform as RectTransform,
            m_regularFont,
            m_boldFont);

        SelectCombatSubTab(0);
    }

    private void BuildCombatGeneralSettings(Transform parent)
    {
        TextMeshProUGUI title = CreateTopLeftText(
            "Combat General Title",
            parent,
            new Vector2(430f, -34f),
            new Vector2(500f, 50f),
            "전투 일반",
            32f,
            TextAlignmentOptions.Left);
        title.font = m_boldFont != null ? m_boldFont : m_regularFont;

        TextMeshProUGUI notice = CreateTopLeftText(
            "Combat General Notice",
            parent,
            new Vector2(430f, -82f),
            new Vector2(760f, 32f),
            "입력 감도와 전투 화면 효과 설정 초안입니다.",
            17f,
            TextAlignmentOptions.Left);
        notice.color = new Color(1f, 1f, 1f, 0.6f);

        Image panel = CreateTopLeftImage(
            "Combat General Controls",
            parent,
            new Vector2(430f, -132f),
            new Vector2(650f, 270f),
            new Color(0.035f, 0.035f, 0.035f, 0.92f));
        Outline outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.16f);
        outline.effectDistance = new Vector2(1f, -1f);

        CreateTopLeftText(
            "Mouse Sensitivity Label",
            panel.transform,
            new Vector2(32f, -30f),
            new Vector2(220f, 30f),
            "마우스 감도",
            18f,
            TextAlignmentOptions.Left);
        m_mouseSensitivityValue = CreateTopLeftText(
            "Mouse Sensitivity Value",
            panel.transform,
            new Vector2(518f, -30f),
            new Vector2(96f, 30f),
            string.Empty,
            18f,
            TextAlignmentOptions.Right);
        m_mouseSensitivityValue.font = m_boldFont != null ? m_boldFont : m_regularFont;

        GameObject sliderObject = CreateTopLeftObject(
            "Mouse Sensitivity Slider",
            panel.transform,
            new Vector2(32f, -76f),
            new Vector2(582f, 24f));
        m_mouseSensitivitySlider = sliderObject.AddComponent<Slider>();
        m_mouseSensitivitySlider.minValue = 0.01f;
        m_mouseSensitivitySlider.maxValue = 10f;

        Image sliderBackground = CreateStretchImage(
            "Background",
            sliderObject.transform,
            new Color(1f, 1f, 1f, 0.13f));
        sliderBackground.rectTransform.offsetMin = new Vector2(0f, 8f);
        sliderBackground.rectTransform.offsetMax = new Vector2(0f, -8f);

        GameObject fillArea = CreateStretchObject("Fill Area", sliderObject.transform);
        RectTransform fillAreaRect = (RectTransform)fillArea.transform;
        fillAreaRect.offsetMin = new Vector2(0f, 8f);
        fillAreaRect.offsetMax = new Vector2(-12f, -8f);
        Image fill = CreateStretchImage("Fill", fillArea.transform, new Color(0.95f, 0.08f, 0.08f, 1f));

        GameObject handleArea = CreateStretchObject("Handle Slide Area", sliderObject.transform);
        RectTransform handleAreaRect = (RectTransform)handleArea.transform;
        handleAreaRect.offsetMin = new Vector2(8f, 0f);
        handleAreaRect.offsetMax = new Vector2(-8f, 0f);
        Image handle = CreateTopLeftImage(
            "Handle",
            handleArea.transform,
            Vector2.zero,
            new Vector2(18f, 18f),
            Color.white);
        RectTransform handleRect = handle.rectTransform;
        handleRect.anchorMin = new Vector2(0.5f, 0.5f);
        handleRect.anchorMax = new Vector2(0.5f, 0.5f);
        handleRect.pivot = new Vector2(0.5f, 0.5f);
        handleRect.anchoredPosition = Vector2.zero;

        m_mouseSensitivitySlider.fillRect = fill.rectTransform;
        m_mouseSensitivitySlider.handleRect = handleRect;
        m_mouseSensitivitySlider.targetGraphic = handle;
        m_mouseSensitivitySlider.direction = Slider.Direction.LeftToRight;
        m_mouseSensitivitySlider.onValueChanged.AddListener(OnMouseSensitivityChanged);

        m_cameraKickToggle = CreateToggle(
            "Camera Kick Toggle",
            panel.transform,
            new Vector2(32f, -150f),
            "카메라 킥 사용");
        m_cameraKickToggle.onValueChanged.AddListener(OnCameraKickChanged);
        TextMeshProUGUI help = CreateTopLeftText(
            "Camera Kick Help",
            panel.transform,
            new Vector2(80f, -192f),
            new Vector2(520f, 26f),
            "끄면 사격 시 카메라 롤·FOV 시각 효과를 표시하지 않습니다.",
            15f,
            TextAlignmentOptions.Left);
        help.color = new Color(1f, 1f, 1f, 0.5f);

        RefreshCombatGeneralControls();
        CommitCombatGeneralSettings();
    }

    private Toggle CreateToggle(string name, Transform parent, Vector2 position, string label)
    {
        GameObject toggleObject = CreateTopLeftObject(name, parent, position, new Vector2(582f, 38f));
        Toggle toggle = toggleObject.AddComponent<Toggle>();
        Image background = CreateTopLeftImage(
            "Background",
            toggleObject.transform,
            Vector2.zero,
            new Vector2(30f, 30f),
            new Color(1f, 1f, 1f, 0.16f));
        Image checkmark = CreateTopLeftImage(
            "Checkmark",
            background.transform,
            new Vector2(6f, -6f),
            new Vector2(18f, 18f),
            new Color(0.95f, 0.08f, 0.08f, 1f));
        CreateTopLeftText(
            "Label",
            toggleObject.transform,
            new Vector2(48f, -1f),
            new Vector2(360f, 32f),
            label,
            18f,
            TextAlignmentOptions.Left);
        toggle.targetGraphic = background;
        toggle.graphic = checkmark;
        return toggle;
    }

    private void OnMouseSensitivityChanged(float value)
    {
        if (m_isRefreshingCombatGeneral)
        {
            return;
        }

        m_mouseSensitivity = Mathf.Clamp(Mathf.Round(value * 100f) / 100f, 0.01f, 10f);
        m_mouseSensitivitySlider.SetValueWithoutNotify(m_mouseSensitivity);
        m_mouseSensitivityValue.text = m_mouseSensitivity.ToString("0.00");
        GameSettingManager.Instance?.SetMouseSensitivity(m_mouseSensitivity);
    }

    private void OnCameraKickChanged(bool enabled)
    {
        if (!m_isRefreshingCombatGeneral)
        {
            m_cameraKickEnabled = enabled;
            GameSettingManager.Instance?.SetCameraKickEnabled(enabled);
        }
    }

    private void RefreshCombatGeneralControls()
    {
        if (m_mouseSensitivitySlider == null || m_cameraKickToggle == null)
        {
            return;
        }

        m_isRefreshingCombatGeneral = true;
        m_mouseSensitivitySlider.SetValueWithoutNotify(m_mouseSensitivity);
        m_cameraKickToggle.SetIsOnWithoutNotify(m_cameraKickEnabled);
        m_mouseSensitivityValue.text = m_mouseSensitivity.ToString("0.00");
        m_isRefreshingCombatGeneral = false;
    }

    private void CommitCombatGeneralSettings()
    {
        m_committedMouseSensitivity = m_mouseSensitivity;
        m_committedCameraKickEnabled = m_cameraKickEnabled;
    }

    private void SyncCombatGeneralSettings(GameSettingManager settingManager, bool commit)
    {
        if (settingManager == null)
        {
            return;
        }

        m_mouseSensitivity = settingManager.MouseSensitivity;
        m_cameraKickEnabled = settingManager.CameraKickEnabled;
        RefreshCombatGeneralControls();

        if (commit)
        {
            CommitCombatGeneralSettings();
        }
    }

    private void RevertCombatGeneralSettings()
    {
        m_mouseSensitivity = m_committedMouseSensitivity;
        m_cameraKickEnabled = m_committedCameraKickEnabled;
        RefreshCombatGeneralControls();
    }

    private void BuildPlaceholderPage(Transform parent, string title, string description, float left = 145f)
    {
        TextMeshProUGUI titleText = CreateTopLeftText(
            $"{title} Placeholder Title",
            parent,
            new Vector2(left, -72f),
            new Vector2(760f, 48f),
            title,
            32f,
            TextAlignmentOptions.Left);
        titleText.font = m_boldFont != null ? m_boldFont : m_regularFont;

        TextMeshProUGUI descriptionText = CreateTopLeftText(
            $"{title} Placeholder Description",
            parent,
            new Vector2(left, -132f),
            new Vector2(900f, 36f),
            description,
            18f,
            TextAlignmentOptions.Left);
        descriptionText.color = new Color(1f, 1f, 1f, 0.55f);
    }

    private void SelectCombatSubTab(int selectedIndex)
    {
        m_selectedCombatSubTab = Mathf.Clamp(selectedIndex, 0, m_combatSubPages.Length - 1);
        for (int i = 0; i < m_combatSubPages.Length; i++)
        {
            bool selected = i == m_selectedCombatSubTab;
            if (m_combatSubTabBackgrounds[i] != null)
            {
                m_combatSubTabBackgrounds[i].color = selected
                    ? new Color(1f, 1f, 1f, 0.16f)
                    : Color.clear;
            }

            if (m_combatSubTabOutlines[i] != null)
            {
                m_combatSubTabOutlines[i].enabled = selected;
            }

            if (m_combatSubPages[i] != null)
            {
                m_combatSubPages[i].SetActive(selected);
            }
        }
    }

    private Button CreateButton(
        string name,
        Transform parent,
        Vector2 topLeftPosition,
        Vector2 size,
        string label,
        float fontSize,
        Color backgroundColor,
        Sprite backgroundSprite = null)
    {
        GameObject buttonObject = CreateTopLeftObject(name, parent, topLeftPosition, size);
        Image image = buttonObject.AddComponent<Image>();
        image.color = backgroundColor;
        image.sprite = backgroundSprite;
        image.raycastTarget = true;

        Button button = buttonObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(1f, 1f, 1f, 0.4f);
        colors.colorMultiplier = 1f;
        button.colors = colors;

        TextMeshProUGUI text = CreateStretchText("Label", buttonObject.transform, label, fontSize, TextAlignmentOptions.Center);
        text.font = fontSize >= 40f && m_boldFont != null ? m_boldFont : m_regularFont;
        return button;
    }

    /// <summary>열 때마다 이전 선택 장식이 남지 않게 비웁니다.</summary>
    private void ResetPauseSelection()
    {
        m_pauseSelectionIndicator?.ResetSelection();
    }

    private static void AddEventTrigger(
        Button button,
        EventTriggerType eventType,
        UnityEngine.Events.UnityAction<BaseEventData> callback)
    {
        EventTrigger trigger = button.GetComponent<EventTrigger>();
        if (trigger == null)
        {
            trigger = button.gameObject.AddComponent<EventTrigger>();
        }

        trigger.triggers ??= new List<EventTrigger.Entry>();
        EventTrigger.Entry entry = new() { eventID = eventType, callback = new EventTrigger.TriggerEvent() };
        entry.callback.AddListener(callback);
        trigger.triggers.Add(entry);
    }

    private TextMeshProUGUI CreateTopLeftText(
        string name,
        Transform parent,
        Vector2 topLeftPosition,
        Vector2 size,
        string value,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        GameObject textObject = CreateTopLeftObject(name, parent, topLeftPosition, size);
        return ConfigureText(textObject.AddComponent<TextMeshProUGUI>(), value, fontSize, alignment);
    }

    private TextMeshProUGUI CreateStretchText(
        string name,
        Transform parent,
        string value,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        GameObject textObject = CreateStretchObject(name, parent);
        return ConfigureText(textObject.AddComponent<TextMeshProUGUI>(), value, fontSize, alignment);
    }

    private TextMeshProUGUI ConfigureText(
        TextMeshProUGUI text,
        string value,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        text.text = value;
        text.font = m_regularFont;
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Normal;
        text.color = Color.white;
        text.alignment = alignment;
        text.overflowMode = TextOverflowModes.Overflow;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    private static Image CreateStretchImage(string name, Transform parent, Color color)
    {
        GameObject imageObject = CreateStretchObject(name, parent);
        Image image = imageObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static Image CreateTopLeftImage(
        string name,
        Transform parent,
        Vector2 topLeftPosition,
        Vector2 size,
        Color color)
    {
        GameObject imageObject = CreateTopLeftObject(name, parent, topLeftPosition, size);
        Image image = imageObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static GameObject CreateStretchObject(string name, Transform parent)
    {
        GameObject child = new(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)child.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        return child;
    }

    private static GameObject CreateTopLeftObject(
        string name,
        Transform parent,
        Vector2 topLeftPosition,
        Vector2 size)
    {
        GameObject child = new(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)child.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = topLeftPosition;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        return child;
    }

    private void ResolveFonts()
    {
        TMP_FontAsset[] loadedFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        if (m_regularFont == null)
        {
            m_regularFont = FindFont(loadedFonts, "GmarketSansTTFMedium")
                ?? TMP_Settings.defaultFontAsset;
        }

        if (m_boldFont == null)
        {
            m_boldFont = FindFont(loadedFonts, "GmarketSansTTFBold")
                ?? m_regularFont;
        }
    }

    private static TMP_FontAsset FindFont(TMP_FontAsset[] fonts, string namePart)
    {
        for (int i = 0; i < fonts.Length; i++)
        {
            TMP_FontAsset font = fonts[i];
            if (font != null && font.name.Contains(namePart, System.StringComparison.OrdinalIgnoreCase))
            {
                return font;
            }
        }

        return null;
    }

    private static T GetOrAddComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    private void SetViewActive(bool active)
    {
        if (m_viewRoot != null && m_viewRoot.activeSelf != active)
        {
            m_viewRoot.SetActive(active);
        }
    }

    private static void SelectUiObject(Selectable selectable)
    {
        if (selectable == null || EventSystem.current == null)
        {
            return;
        }

        EventSystem.current.SetSelectedGameObject(selectable.gameObject);
    }

    private static bool WasEscapePressed()
    {
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
    }
}
