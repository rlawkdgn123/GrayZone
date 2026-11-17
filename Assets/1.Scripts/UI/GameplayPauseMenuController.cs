using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
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

    // Figma 'Setting Scene' 프레임(1920x1080) 값입니다.
    /// <summary>위쪽 탭 글자의 가운데 x입니다(비디오·오디오·전투·기타 순서).</summary>
    private static readonly float[] SettingsTabCenters = { 496f, 756f, 999f, 1258.5f };

    /// <summary>위쪽 탭과 전투 왼쪽 메뉴의 선택 칸(Select_mark_01) 크기입니다.</summary>
    private static readonly Vector2 MenuSelectSize = new(234f, 63f);

    /// <summary>테마 이미지가 없을 때 선택 칸과 제목 막대를 칠할 노란색입니다.</summary>
    private static readonly Color MenuSelectFallbackColor = new(0.97f, 0.78f, 0.18f, 1f);

    private static readonly Vector2 DropdownSize = new(639f, 56f);
    private const float DropdownTextInset = 23f;
    private const float DropdownItemTextInset = 27f;

    /// <summary>큰 슬라이더(Gauge) 크기입니다. 손잡이 높이와 같습니다.</summary>
    private static readonly Vector2 GaugeSize = new(643f, 42f);
    private static readonly Vector2 GaugeHandleSize = new(20f, 42f);
    private const float GaugeTrackHeight = 11f;

    [Header("Figma UI Assets")]
    [Tooltip("메뉴와 설정 화면의 한글을 표시할 기본 TMP 폰트입니다.")]
    [SerializeField] private TMP_FontAsset m_regularFont;

    [Tooltip("큰 메뉴 버튼에 사용할 굵은 TMP 폰트입니다. 비어 있으면 기본 폰트를 사용합니다.")]
    [SerializeField] private TMP_FontAsset m_boldFont;

    [Tooltip("설정 화면 이미지 묶음입니다. 비워 두면 Resources의 기본 에셋(" + GameplaySettingsTheme.ResourcesPath + ")을 씁니다.")]
    [SerializeField] private GameplaySettingsTheme m_theme;

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

    [Header("Settings Tabs")]
    [Tooltip("켜면 일시정지 메뉴 없이 설정 화면만 씁니다. 타이틀처럼 다른 버튼이 OpenSettings를 불러 여는 씬에서 켭니다. " +
             "ESC로 열리지 않고, ESC나 뒤로가기를 누르면 닫힙니다. 시간 정지와 입력 잠금도 하지 않습니다.")]
    [SerializeField] private bool m_settingsOnly;

    /// <summary>셸터 이동 입력입니다. 스쿼드가 없는 셸터에서 메뉴가 열린 동안 잠급니다.</summary>
    private PlayerMove m_shelterMove;

    /// <summary>셸터 시점 입력입니다. 스쿼드가 없는 셸터에서 메뉴가 열린 동안 잠급니다.</summary>
    private CameraLook m_shelterLook;
    private bool m_previousShelterMoveLocked;
    private bool m_previousShelterLookLocked;

    private readonly List<PlayerInputController> m_fallbackInputControllers = new();
    private readonly List<Image> m_tabBackgrounds = new();
    private readonly List<TextMeshProUGUI> m_tabLabels = new();
    private readonly List<GameObject> m_settingsContentPages = new();
    private readonly Image[] m_combatSubTabBackgrounds = new Image[3];
    private readonly TextMeshProUGUI[] m_combatSubTabLabels = new TextMeshProUGUI[3];
    private readonly GameObject[] m_combatSubPages = new GameObject[3];

    private GameObject m_viewRoot;
    private GameObject m_pausePage;
    private GameObject m_settingsPage;
    private Button m_resumeButton;
    private Button m_firstSettingsTab;
    private GameObject m_unsavedPopup;
    private Button m_popupSaveButton;
    private GameObject m_resetPopup;
    private Button m_resetPopupBackButton;
    private TextMeshProUGUI m_resetPopupMessage;
    private System.Action m_pendingResetAction;
    private GameObject m_demoResetPopup;
    private Button m_demoResetPopupBackButton;
    private System.Action<bool> m_demoResetDecision;
    private bool m_demoResetPopupStandalone;
    private GameObject m_difficultyPopup;
    private TextMeshProUGUI m_difficultyDescription;
    private System.Action<GameDifficulty> m_difficultyDecision;
    private bool m_difficultyPopupStandalone;
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
    private Toggle m_aimToggleToggle;
    private Toggle m_demoResetToggle;
    private float m_mouseSensitivity = 1f;
    private float m_committedMouseSensitivity = 1f;
    private bool m_cameraKickEnabled = true;
    private bool m_aimToggleEnabled;
    private bool m_committedAimToggleEnabled;
    private bool m_committedCameraKickEnabled = true;
    private bool m_isRefreshingCombatGeneral;
    private readonly Slider[] m_volumeSliders = new Slider[3];
    private readonly TextMeshProUGUI[] m_volumeValueTexts = new TextMeshProUGUI[3];

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

        // 확인 팝업이 떠 있으면 ESC는 팝업의 뒤로가기(돌아가기)와 같습니다. 데모 초기화 팝업은 메뉴가 닫힌 채로도 뜹니다.
        if (IsDemoResetPopupOpen)
        {
            CancelDemoResetPopup();
            return;
        }

        if (IsDifficultyPopupOpen)
        {
            CancelDifficultyPopup();
            return;
        }

        if (m_isOpen && (IsUnsavedPopupOpen || IsResetPopupOpen))
        {
            CloseUnsavedChangesPopup();
            CloseResetConfirmPopup();
            return;
        }

        if (m_settingsOnly)
        {
            if (m_isOpen)
            {
                RequestLeaveSettings();
            }

            return;
        }

        if (m_isOpen)
        {
            if (m_settingsPage.activeSelf)
            {
                RequestLeaveSettings();
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
        if (m_isOpen && !m_settingsOnly)
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
        if (m_settingsOnly)
        {
            m_isOpen = false;
            return;
        }

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
            HoldPlayingCinematics();
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
        SyncAudioSettings(settingManager);
        SyncDemoResetToggle(settingManager);
        RefreshVideoControls();

        // 메뉴는 Awake에서 만들어져 저장값을 읽기 전일 수 있으므로 열 때마다 지금 적용된 값을 다시 읽습니다.
        m_crosshairSettingsPanel?.RevertToCommittedValues();
        m_throwableCrosshairSettingsPanel?.RevertToCommittedValues();

        // 설정 화면만 쓰는 씬(타이틀)은 일시정지 메뉴를 거치지 않고 여기서 바로 화면을 엽니다.
        if (m_settingsOnly && !m_isOpen)
        {
            m_isOpen = true;
            SetViewActive(true);
        }

        CloseUnsavedChangesPopup();
        CloseResetConfirmPopup();
        m_pausePage.SetActive(false);
        m_settingsPage.SetActive(true);
        SelectSettingsTab(0);
        SelectUiObject(m_firstSettingsTab);
    }

    /// <summary>
    /// 설정 화면에서 일시정지 메뉴로 돌아갑니다(설정 화면만 쓰는 씬은 닫습니다). 저장하지 않은 변경이 있으면 확인 팝업을 먼저 띄웁니다.
    /// </summary>
    public void ReturnToPauseMenu()
    {
        RequestLeaveSettings();
    }

    /// <summary>설정 화면만 쓰는 씬에서 설정 화면을 닫습니다. 바꾼 값은 저장하지 않았어도 이번 실행 동안 유지됩니다.</summary>
    private void CloseSettingsOnly()
    {
        SetViewActive(false);
        m_isOpen = false;
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
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
        SyncAudioSettings(settingManager);
        SyncDemoResetToggle(settingManager);
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

        // 설정 기타 탭의 데모용 초기화가 켜져 있으면 초기화할지 먼저 묻습니다.
        if (DemoTitleReset.IsEnabled)
        {
            ShowDemoResetConfirm(LeaveToTitle);
            return;
        }

        LeaveToTitle(false);
    }

    /// <summary>타이틀로 갑니다. <paramref name="demoReset"/>이면 전체 초기화 뒤 부트스트랩부터 다시 시작합니다.</summary>
    private void LeaveToTitle(bool demoReset)
    {
        SetViewActive(false);
        RestoreGameplayState();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SceneTransitionController.LoadScene(demoReset
            ? DemoTitleReset.ResetAndGetNextScene(m_titleSceneName)
            : m_titleSceneName);
    }

    private void RestoreGameplayState()
    {
        m_isOpen = false;
        if (m_pauseTime)
        {
            Time.timeScale = m_previousTimeScale;
            ReleaseHeldCinematics();
        }

        SetGameplayInputEnabled(true);
        Cursor.lockState = m_previousCursorLockMode;
        Cursor.visible = m_previousCursorVisible;
    }

    /// <summary>일시정지 메뉴가 멈춰 둔 컷신 타임라인입니다. 메뉴를 닫을 때 다시 재생합니다.</summary>
    private static readonly List<PlayableDirector> s_heldDirectors = new();

    /// <summary>일시정지 메뉴가 열려 게임 시간을 멈춘 상태인지 여부입니다. 실제 시간으로 흐르는 컷신 대기도 이 동안은 멈춥니다.</summary>
    public static bool IsGameplayPausedByMenu { get; private set; }

    /// <summary>이 타임라인이 일시정지 메뉴 때문에 멈춰 있는지 여부입니다. 컷신이 끝났는지 볼 때 멈춘 상태도 재생 중으로 봐야 합니다.</summary>
    public static bool IsDirectorHeldByMenu(PlayableDirector director)
    {
        return director != null && s_heldDirectors.Contains(director);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_heldDirectors.Clear();
        IsGameplayPausedByMenu = false;
    }

    /// <summary>
    /// 재생 중인 컷신 타임라인을 멈춥니다. 컷신 타임라인은 실제 시간으로 재생돼 게임 시간을 멈춰도 계속 돌기 때문에,
    /// 그대로 두면 캐릭터는 타임라인을 따라 움직이는데 애니메이터(게임 시간)는 멈춰 미끄러지듯 보입니다.
    /// </summary>
    private static void HoldPlayingCinematics()
    {
        IsGameplayPausedByMenu = true;
        foreach (PlayableDirector director in FindObjectsByType<PlayableDirector>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (director.state == PlayState.Playing && !s_heldDirectors.Contains(director))
            {
                director.Pause();
                s_heldDirectors.Add(director);
            }
        }
    }

    /// <summary>일시정지 메뉴가 멈춰 둔 컷신 타임라인을 이어서 재생합니다.</summary>
    private static void ReleaseHeldCinematics()
    {
        IsGameplayPausedByMenu = false;
        foreach (PlayableDirector director in s_heldDirectors)
        {
            if (director != null && director.state == PlayState.Paused)
            {
                director.Resume();
            }
        }

        s_heldDirectors.Clear();
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
        for (int i = 0; i < m_tabBackgrounds.Count; i++)
        {
            bool selected = i == selectedIndex;
            ApplyMenuSelection(m_tabBackgrounds[i], m_tabLabels[i], selected);

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
            if (managers[i] != null && managers[i].isActiveAndEnabled && managers[i].BlocksPauseMenu)
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
        BuildDemoResetPopup();
        BuildDifficultyPopup();
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
        m_unsavedPopup = null;
        m_popupSaveButton = null;
        m_resetPopup = null;
        m_resetPopupBackButton = null;
        m_resetPopupMessage = null;
        m_pendingResetAction = null;
        m_demoResetPopup = null;
        m_demoResetPopupBackButton = null;
        m_demoResetDecision = null;
        m_demoResetPopupStandalone = false;
        m_difficultyPopup = null;
        m_difficultyDescription = null;
        m_difficultyDecision = null;
        m_difficultyPopupStandalone = false;
        m_tabBackgrounds.Clear();
        m_tabLabels.Clear();
        m_settingsContentPages.Clear();
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
        m_aimToggleToggle = null;
        m_demoResetToggle = null;

        ResolveFonts();
        if (m_theme == null)
        {
            m_theme = GameplaySettingsTheme.LoadFromResources();
        }

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

        // Preferences_base_01은 화면 전체를 덮는 반투명 배경이고, 위 탭 줄과 아래 버튼 줄의 구분선이 그려져 있습니다.
        Image background = CreateStretchImage("Preferences Base", m_settingsPage.transform, new Color(0.25f, 0.25f, 0.25f, 0.92f));
        ApplySprite(background, m_theme != null ? m_theme.pageBackground : null);
        background.raycastTarget = true;

        CreateFigmaText("Settings Title", m_settingsPage.transform, 134f, 29f, 68f, 35f, "설정", 35f, TextAlignmentOptions.Center);

        // 탭은 이 목록 순서대로 왼쪽부터 놓입니다. 탭을 빼면 뒤 탭이 앞으로 당겨집니다.
        var tabs = new List<(string Label, System.Action<Transform> BuildPage)>
        {
            ("비디오", BuildVideoSettingsPage),
            ("오디오", BuildAudioSettingsPage),

            // 전투 설정은 타이틀·셸터에서도 바꾸고 저장할 수 있습니다. 조준선 미리보기도 씬과 관계없이 그립니다.
            ("전투", BuildCombatSettingsPage),
        };

        tabs.Add(("기타", BuildEtcSettingsPage));

        for (int i = 0; i < tabs.Count; i++)
        {
            int tabIndex = i;
            string tabLabel = tabs[i].Label;

            // Figma 탭 글자 가운데 x입니다. 탭이 더 많으면 마지막 간격으로 이어 붙입니다.
            float center = i < SettingsTabCenters.Length
                ? SettingsTabCenters[i]
                : SettingsTabCenters[^1] + (SettingsTabCenters[^1] - SettingsTabCenters[^2]) * (i - SettingsTabCenters.Length + 1);
            Button tab = CreateMenuSelectButton(
                $"Tab {tabLabel}",
                m_settingsPage.transform,
                center - (MenuSelectSize.x * 0.5f),
                15f,
                tabLabel,
                35f,
                out TextMeshProUGUI tabText);
            tab.onClick.AddListener(() => SelectSettingsTab(tabIndex));

            m_tabBackgrounds.Add(tab.GetComponent<Image>());
            m_tabLabels.Add(tabText);
            if (i == 0)
            {
                m_firstSettingsTab = tab;
            }
        }

        // 각 탭 화면은 설정 화면 전체(1920x1080) 크기라 Figma 좌표를 그대로 씁니다.
        for (int i = 0; i < tabs.Count; i++)
        {
            GameObject page = CreateStretchObject($"Settings Content {tabs[i].Label}", m_settingsPage.transform);
            m_settingsContentPages.Add(page);
            tabs[i].BuildPage(page.transform);
        }

        // Figma 하단 버튼: Reset Button / Save Button / Back Button.
        Button revertButton = CreateActionButton(
            "Revert",
            m_settingsPage.transform,
            new Rect(965f, 992f, 246f, 71f),
            "되돌리기",
            1004.46f,
            140.65f,
            m_theme != null ? m_theme.resetIcon : null,
            new Rect(1151f, 1008f, 32f, 38f));
        revertButton.onClick.AddListener(RevertSettings);

        Button saveButton = CreateActionButton(
            "Save Changes",
            m_settingsPage.transform,
            new Rect(1250f, 992f, 343f, 71f),
            "변경사항 저장",
            1240f,
            317f,
            m_theme != null ? m_theme.saveIcon : null,
            new Rect(1516f, 1009f, 38f, 37f));
        saveButton.onClick.AddListener(SaveSettings);

        Button backButton = CreateActionButton(
            "Back",
            m_settingsPage.transform,
            new Rect(1622f, 992f, 246f, 71f),
            "뒤로가기",
            1661.46f,
            140.65f,
            m_theme != null ? m_theme.backIcon : null,
            new Rect(1806f, 1010f, 40f, 38f));
        backButton.onClick.AddListener(ReturnToPauseMenu);

        BuildUnsavedChangesPopup();
        BuildResetConfirmPopup();
        SelectSettingsTab(0);
    }

    /// <summary>기타 탭입니다. 데모용 초기화 토글과 모든 설정을 처음 값으로 되돌리는 버튼을 둡니다.</summary>
    private void BuildEtcSettingsPage(Transform parent)
    {
        CreateSectionTitle(parent, 371f, 167f, "기타");

        // 전투 일반 탭의 토글 줄과 같은 모양입니다. 값은 다른 설정처럼 바로 바뀌고 '변경사항 저장'으로 파일에 남습니다.
        m_demoResetToggle = CreateToggle("Demo Reset Toggle", parent, 394f, 248f, "데모용 초기화");
        m_demoResetToggle.onValueChanged.AddListener(OnDemoResetChanged);
        float demoHelpLeft = 394f + ((RectTransform)m_demoResetToggle.transform).sizeDelta.x + 33f;
        CreateHelpText("Demo Reset Help", parent, demoHelpLeft, 280f,
            "켜면 '타이틀로 돌아가기'를 할 때마다 설정과 진행을 처음 켠 상태로 되돌립니다. 시연용입니다.");

        // 하단 '변경사항 저장' 버튼과 같은 크기·모양의 버튼 셋을 한 줄로 둡니다. 누르면 같은 확인 팝업이 문구만 바꿔 뜹니다.
        CreateResetButton(parent, "Reset Settings", 393f, "설정 초기화",
            "모든 설정을 초기화하시겠습니까?", ResetSettingsOnly);
        CreateResetButton(parent, "Reset Saves", 766f, "저장 초기화",
            "모든 저장내역을 초기화하시겠습니까?", ResetSavesOnly);
        CreateResetButton(parent, "Reset Everything", 1139f, "전체 초기화",
            "모든 설정과 저장내역을 초기화하시겠습니까?", ResetEverything);

        CreateHelpText("Reset Settings Help", parent, 393f, 451f,
            "설정 초기화: 비디오·오디오·전투·조준선 설정을 처음 값으로 되돌리고 바로 저장합니다. 데모용 초기화 값은 유지됩니다.");
        CreateHelpText("Reset Saves Help", parent, 393f, 486f,
            "저장 초기화: 게임 저장 내역(자동 저장 포함)을 지워 처음 게임을 시작하는 상태로 만듭니다. 설정은 그대로입니다.");
        CreateHelpText("Reset Everything Help", parent, 393f, 521f,
            "전체 초기화: 설정 초기화와 저장 초기화를 함께 진행합니다.");
    }

    /// <summary>기타 탭의 초기화 버튼입니다. 누르면 <paramref name="confirmMessage"/>로 확인 팝업을 띄우고, 초기화를 고르면 <paramref name="action"/>을 실행합니다.</summary>
    private void CreateResetButton(Transform parent, string name, float left, string label, string confirmMessage, System.Action action)
    {
        Button button = CreateButton(
            name,
            parent,
            new Vector2(left, -360f),
            new Vector2(343f, 71f),
            label,
            36f,
            new Color(0.02f, 0.03f, 0.04f, 1f));
        ApplySprite(button.GetComponent<Image>(), m_theme != null ? m_theme.actionButton : null);
        m_theme?.ApplyActionButtonStates(button);
        TextMeshProUGUI buttonLabel = button.GetComponentInChildren<TextMeshProUGUI>(true);
        buttonLabel.font = m_boldFont != null ? m_boldFont : m_regularFont;
        button.onClick.AddListener(() => OpenResetConfirmPopup(confirmMessage, action));
    }

    /// <summary>기타 탭 초기화 확인 팝업을 문구와 실행할 동작을 바꿔 띄웁니다.</summary>
    private void OpenResetConfirmPopup(string message, System.Action action)
    {
        if (m_resetPopup == null)
        {
            return;
        }

        m_resetPopupMessage.text = message;
        m_pendingResetAction = action;
        m_resetPopup.SetActive(true);
        m_resetPopup.transform.SetAsLastSibling();
        SelectUiObject(m_resetPopupBackButton);
    }

    private void ConfirmPendingReset()
    {
        System.Action action = m_pendingResetAction;
        m_pendingResetAction = null;
        CloseResetConfirmPopup();
        action?.Invoke();
    }

    private void CloseResetConfirmPopup()
    {
        if (m_resetPopup != null)
        {
            m_resetPopup.SetActive(false);
        }
    }

    private bool IsResetPopupOpen => m_resetPopup != null && m_resetPopup.activeSelf;

    private void OnDemoResetChanged(bool enabled)
    {
        GameSettingManager.Instance?.SetDemoResetOnTitle(enabled);
    }

    /// <summary>데모용 초기화 토글 표시를 <see cref="GameSettingManager"/>의 현재 값으로 맞춥니다.</summary>
    private void SyncDemoResetToggle(GameSettingManager settingManager)
    {
        if (settingManager != null && m_demoResetToggle != null)
        {
            m_demoResetToggle.SetIsOnWithoutNotify(settingManager.DemoResetOnTitle);
        }
    }

    /// <summary>설정 초기화: 설정만 처음 값으로 되돌려 저장합니다.</summary>
    private void ResetSettingsOnly()
    {
        ResetSettingsAndRefresh();
    }

    /// <summary>저장 초기화: 게임 저장 내역(자동 저장 포함)만 지웁니다. 설정은 그대로입니다.</summary>
    private void ResetSavesOnly()
    {
        SaveFilePaths.DeleteAllGameSaves();
    }

    /// <summary>전체 초기화: 설정 초기화와 저장 초기화를 함께 진행합니다. 데모용 초기화와 같은 작업입니다.</summary>
    private void ResetEverything()
    {
        ResetSettingsAndRefresh();
        SaveFilePaths.DeleteAllGameSaves();
    }

    /// <summary>
    /// 모든 설정을 처음 값으로 되돌려 저장하고, 화면과 조준선 HUD를 그 값으로 다시 맞춥니다. 되돌리기 기준점도 이 값으로 옮깁니다.
    /// </summary>
    private void ResetSettingsAndRefresh()
    {
        GameSettingManager settingManager = GameSettingManager.Instance;
        if (settingManager == null)
        {
            Debug.LogWarning("[GameplayPauseMenu] GameSettingManager를 찾지 못해 설정을 초기화할 수 없습니다.", this);
            return;
        }

        if (!settingManager.ResetToDefaultsAndSave())
        {
            Debug.LogWarning("[GameplayPauseMenu] 초기화한 설정을 파일에 저장하지 못했습니다.", this);
        }

        CrosshairSettingsService.Instance?.ReapplyFromSettings();
        m_settingsBaseline = settingManager.CreateSnapshot();
        m_crosshairBaseline = settingManager.GetCrosshairSettings();
        m_displayBaseline = Screen.mainWindowDisplayInfo;
        m_hasDisplayBaseline = true;
        SyncCombatGeneralSettings(settingManager, true);
        SyncAudioSettings(settingManager);
        SyncDemoResetToggle(settingManager);
        RefreshVideoControls();
        m_crosshairSettingsPanel?.RevertToCommittedValues();
        m_throwableCrosshairSettingsPanel?.RevertToCommittedValues();
    }

    /// <summary>
    /// Figma Setting Scene Battle Popup입니다. 저장하지 않은 변경이 있는데 설정 화면을 나가려 하면 띄웁니다.
    /// </summary>
    /// <remarks>
    /// 저장하기는 저장한 뒤 나가고, 나가기는 설정 화면을 열었을 때(또는 마지막 저장) 값으로 되돌린 뒤 나가고, 뒤로가기는 팝업만 닫습니다.
    /// </remarks>
    private void BuildUnsavedChangesPopup()
    {
        m_unsavedPopup = CreateConfirmPopup(
            m_settingsPage.transform,
            "Unsaved Changes Popup",
            "저장이 되지 않았습니다. 저장 하시겠습니까?",
            out Button[] buttons,
            ("Save Button", "저장하기", SaveAndLeaveSettings),
            ("Out Button", "나가기", DiscardAndLeaveSettings),
            ("Back Button", "뒤로가기", CloseUnsavedChangesPopup));
        m_popupSaveButton = buttons[0];
    }

    /// <summary>
    /// 기타 탭의 초기화 버튼(설정·저장·전체)을 눌렀을 때 한 번 더 묻는 팝업입니다. 저장 확인 팝업과 같은 모양이고, 문구는 연 버튼이 정합니다.
    /// </summary>
    private void BuildResetConfirmPopup()
    {
        m_resetPopup = CreateConfirmPopup(
            m_settingsPage.transform,
            "Reset Confirm Popup",
            string.Empty,
            out Button[] buttons,
            ("Reset Button", "초기화", ConfirmPendingReset),
            ("Back Button", "뒤로가기", CloseResetConfirmPopup));
        m_resetPopupBackButton = buttons[1];
        m_resetPopupMessage = m_resetPopup.transform.Find("Popup Message").GetComponent<TextMeshProUGUI>();
    }

    /// <summary>
    /// 데모용 초기화가 켜진 채 '타이틀로 돌아가기'를 누르면 띄우는 팝업입니다. 일시정지 메뉴와 게임오버 화면이 같이 씁니다.
    /// </summary>
    /// <remarks>
    /// 예는 전체 초기화(설정·저장 내역) 뒤 부트스트랩부터, 아니오는 초기화 없이 타이틀로 가고, 돌아가기는 팝업만 닫습니다.
    /// 설정 화면 밖에서도 뜨도록 화면 루트 바로 아래에 둡니다.
    /// </remarks>
    private void BuildDemoResetPopup()
    {
        m_demoResetPopup = CreateConfirmPopup(
            m_viewRoot.transform,
            "Demo Reset Popup",
            DemoTitleReset.ConfirmMessage,
            out Button[] buttons,
            ("Yes Button", "예", () => DecideDemoReset(true)),
            ("No Button", "아니오", () => DecideDemoReset(false)),
            ("Back Button", "돌아가기", CancelDemoResetPopup));
        m_demoResetPopupBackButton = buttons[2];
    }

    private bool IsDemoResetPopupOpen => m_demoResetPopup != null && m_demoResetPopup.activeSelf;

    /// <summary>
    /// 데모용 초기화 확인 팝업을 띄웁니다. <paramref name="onDecide"/>는 예(true)·아니오(false)를 고르면 불립니다. 돌아가기를 고르면 불리지 않습니다.
    /// </summary>
    /// <remarks>일시정지 메뉴가 닫혀 있을 때(게임오버 화면)는 메뉴 화면 없이 팝업만 띄우고, 돌아가기로 닫으면 다시 숨깁니다.</remarks>
    public void ShowDemoResetConfirm(System.Action<bool> onDecide)
    {
        EnsureViewBuilt();
        m_demoResetDecision = onDecide;
        m_demoResetPopupStandalone = !m_isOpen;
        if (m_demoResetPopupStandalone)
        {
            m_pausePage.SetActive(false);
            m_settingsPage.SetActive(false);
            SetViewActive(true);
        }

        m_demoResetPopup.SetActive(true);
        m_demoResetPopup.transform.SetAsLastSibling();
        SelectUiObject(m_demoResetPopupBackButton);
    }

    private void DecideDemoReset(bool reset)
    {
        System.Action<bool> decision = m_demoResetDecision;
        m_demoResetDecision = null;
        m_demoResetPopup.SetActive(false);
        decision?.Invoke(reset);
    }

    private void CancelDemoResetPopup()
    {
        m_demoResetDecision = null;
        m_demoResetPopup.SetActive(false);
        if (m_demoResetPopupStandalone)
        {
            m_demoResetPopupStandalone = false;
            SetViewActive(false);
        }
    }

    /// <summary>
    /// 타이틀에서 새 게임을 누르면 띄우는 난이도 선택 팝업입니다. 저장 확인 팝업과 같은 모양입니다.
    /// </summary>
    /// <remarks>
    /// 중간·어려움은 고른 난이도로 새 게임을 시작합니다. 오른쪽 위 돌아가기와 ESC는 팝업만 닫습니다.
    /// 버튼에 마우스를 올리면(또는 선택하면) 제목과 버튼 사이에 그 난이도 설명이 뜹니다. 설정 화면 밖에서 뜨도록 화면 루트 바로 아래에 둡니다.
    /// </remarks>
    private void BuildDifficultyPopup()
    {
        m_difficultyPopup = CreateConfirmPopup(
            m_viewRoot.transform,
            "Difficulty Popup",
            "난이도를 선택하세요",
            out Button[] buttons,
            ("Normal Button", GameDifficultyBalance.DisplayName(GameDifficulty.Normal), () => DecideDifficulty(GameDifficulty.Normal)),
            ("Hard Button", GameDifficultyBalance.DisplayName(GameDifficulty.Hard), () => DecideDifficulty(GameDifficulty.Hard)));

        // 제목(32pt)의 3분의 2 크기입니다. 어려움 설명은 한 줄에 다 들어가지 않아 두 줄까지 받도록 높이를 둡니다.
        m_difficultyDescription = CreateFigmaText(
            "Difficulty Description",
            m_difficultyPopup.transform,
            PopupLeft + 30f,
            450f,
            PopupWidth - 60f,
            80f,
            string.Empty,
            DifficultyDescriptionFontSize,
            TextAlignmentOptions.Center);
        m_difficultyDescription.textWrappingMode = TextWrappingModes.Normal;

        AddDifficultyHover(buttons[0], GameDifficulty.Normal);
        AddDifficultyHover(buttons[1], GameDifficulty.Hard);

        // 아래 버튼 줄은 난이도만 두고, 돌아가기는 화면 오른쪽 위에 둡니다. ESC와 같습니다.
        // 셸터 의료·제조 UI의 '돌아가기'(MedicalUI/cancel)와 같은 자리·모양입니다: 배경 없는 흰 글자 36pt 굵은 글꼴, 오른쪽에 Arrow_mark.
        Button backButton = CreateButton(
            "Back Button",
            m_difficultyPopup.transform,
            new Vector2(1496f, -112f),
            new Vector2(400f, 40f),
            "돌아가기",
            36f,
            Color.clear);
        TextMeshProUGUI backLabel = backButton.GetComponentInChildren<TextMeshProUGUI>();
        backLabel.color = Color.white;
        backLabel.font = m_boldFont != null ? m_boldFont : m_regularFont;
        backButton.targetGraphic = backLabel;
        backButton.onClick.AddListener(CancelDifficultyPopup);

        Image backArrow = CreateTopLeftImage(
            "Back Arrow",
            backButton.transform,
            new Vector2(200f + 83.6f - 15f, -(20f - 1f - 16f)),
            new Vector2(30f, 32f),
            Color.white);
        ApplySprite(backArrow, m_theme != null ? m_theme.backIcon : null);
        backArrow.raycastTarget = false;
    }

    private const float DifficultyDescriptionFontSize = 32f * 2f / 3f;

    private void AddDifficultyHover(Button button, GameDifficulty difficulty)
    {
        AddEventTrigger(button, EventTriggerType.PointerEnter, _ => ShowDifficultyDescription(difficulty));
        AddEventTrigger(button, EventTriggerType.Select, _ => ShowDifficultyDescription(difficulty));
        AddEventTrigger(button, EventTriggerType.PointerExit, _ => m_difficultyDescription.text = string.Empty);
    }

    private void ShowDifficultyDescription(GameDifficulty difficulty)
    {
        m_difficultyDescription.text = difficulty == GameDifficulty.Hard
            ? "보다 적들이 더 많고 강해집니다. TPS가 비교적 익숙한 사람에게 추천합니다."
            : "지스타에 맞춘 누구나 즐길 수 있는 난이도입니다.";
    }

    private bool IsDifficultyPopupOpen => m_difficultyPopup != null && m_difficultyPopup.activeSelf;

    /// <summary>
    /// 난이도 선택 팝업을 띄웁니다. <paramref name="onDecide"/>는 난이도를 고르면 불립니다. 돌아가기를 고르면 불리지 않습니다.
    /// </summary>
    /// <remarks>설정 화면이 닫혀 있으면(타이틀) 설정 화면 없이 팝업만 띄우고, ESC로 닫으면 다시 숨깁니다.</remarks>
    public void ShowDifficultySelect(System.Action<GameDifficulty> onDecide)
    {
        EnsureViewBuilt();
        m_difficultyDecision = onDecide;
        m_difficultyPopupStandalone = !m_isOpen;
        if (m_difficultyPopupStandalone)
        {
            m_pausePage.SetActive(false);
            m_settingsPage.SetActive(false);
            SetViewActive(true);
        }

        // 설명은 마우스를 올린 버튼 것만 보입니다. 열 때 버튼을 미리 선택하면 올리지 않은 중간 설명이 먼저 떠서 선택하지 않습니다.
        m_difficultyDescription.text = string.Empty;
        m_difficultyPopup.SetActive(true);
        m_difficultyPopup.transform.SetAsLastSibling();
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    private void DecideDifficulty(GameDifficulty difficulty)
    {
        System.Action<GameDifficulty> decision = m_difficultyDecision;
        CancelDifficultyPopup();
        decision?.Invoke(difficulty);
    }

    private void CancelDifficultyPopup()
    {
        m_difficultyDecision = null;
        m_difficultyPopup.SetActive(false);
        if (m_difficultyPopupStandalone)
        {
            m_difficultyPopupStandalone = false;
            SetViewActive(false);
        }
    }

    /// <summary>
    /// Figma Setting Scene Battle Popup 모양의 확인 팝업을 만듭니다. 처음에는 꺼져 있습니다.
    /// </summary>
    /// <remarks>
    /// 버튼이 셋이면 Figma 위치(왼쪽 620·896·1172)를 그대로 쓰고, 그보다 적으면 같은 간격으로 팝업 가운데에 모읍니다.
    /// </remarks>
    private GameObject CreateConfirmPopup(
        Transform parent,
        string name,
        string messageText,
        out Button[] buttons,
        params (string Name, string Label, UnityEngine.Events.UnityAction OnClick)[] buttonSpecs)
    {
        GameObject popup = CreateStretchObject(name, parent);

        // 덮개가 레이캐스트를 받아 팝업이 떠 있는 동안 뒤의 설정 화면을 누를 수 없게 합니다.
        Image cover = CreateStretchImage("Popup_background_cover", popup.transform, new Color(0f, 0f, 0f, 0.6f));
        ApplySprite(cover, m_theme != null ? m_theme.popupCover : null);
        cover.raycastTarget = true;

        Image popupBase = CreateTopLeftImage(
            "Popup_base",
            popup.transform,
            new Vector2(PopupLeft, -304f),
            new Vector2(PopupWidth, 364f),
            new Color(0.12f, 0.12f, 0.12f, 0.95f));
        ApplySprite(popupBase, m_theme != null ? m_theme.popupBase : null);

        TextMeshProUGUI message = CreateFigmaText(
            "Popup Message",
            popup.transform,
            PopupLeft + 30f,
            396f,
            PopupWidth - 60f,
            32f,
            messageText,
            32f,
            TextAlignmentOptions.Center);
        message.textWrappingMode = TextWrappingModes.NoWrap;
        message.overflowMode = TextOverflowModes.Overflow;

        buttons = new Button[buttonSpecs.Length];
        float rowWidth = (PopupButtonSize.x * buttonSpecs.Length) + (PopupButtonGap * (buttonSpecs.Length - 1));
        float left = PopupLeft + ((PopupWidth - rowWidth) * 0.5f);
        for (int i = 0; i < buttonSpecs.Length; i++)
        {
            Button button = CreateButton(
                buttonSpecs[i].Name,
                popup.transform,
                new Vector2(left + ((PopupButtonSize.x + PopupButtonGap) * i), -557f),
                PopupButtonSize,
                buttonSpecs[i].Label,
                32f,
                new Color(0.02f, 0.03f, 0.04f, 1f));
            ApplySprite(button.GetComponent<Image>(), m_theme != null ? m_theme.actionButton : null);
            m_theme?.ApplyActionButtonStates(button);
            button.onClick.AddListener(buttonSpecs[i].OnClick);
            buttons[i] = button;
        }

        popup.SetActive(false);
        return popup;
    }

    // Figma Setting Scene Battle Popup의 Popup_base(553, 304, 863x364)와 button 3(178x58, 98px 간격)입니다.
    private const float PopupLeft = 553f;
    private const float PopupWidth = 863f;
    private const float PopupButtonGap = 98f;
    private static readonly Vector2 PopupButtonSize = new(178f, 58f);

    /// <summary>
    /// 설정 화면을 열었을 때(또는 마지막 저장) 이후 바뀐 값이 있으면 <c>true</c>입니다.
    /// </summary>
    /// <remarks>조준선 편집은 바꿀 때마다 <see cref="GameSettingManager"/>로 넘어가므로 매니저 값과 기준값만 비교하면 됩니다.</remarks>
    private bool HasUnsavedChanges()
    {
        GameSettingManager settingManager = GameSettingManager.Instance;
        if (settingManager == null)
        {
            return false;
        }

        if (m_settingsBaseline != null
            && JsonUtility.ToJson(settingManager.CreateSnapshot()) != JsonUtility.ToJson(m_settingsBaseline))
        {
            return true;
        }

        if (m_crosshairBaseline != null
            && JsonUtility.ToJson(settingManager.GetCrosshairSettings()) != JsonUtility.ToJson(m_crosshairBaseline))
        {
            return true;
        }

        return m_hasDisplayBaseline && !m_displayBaseline.Equals(Screen.mainWindowDisplayInfo);
    }

    /// <summary>뒤로가기·ESC로 설정 화면을 나갈 때 부릅니다. 저장하지 않은 변경이 있으면 나가기 전에 확인 팝업을 띄웁니다.</summary>
    private void RequestLeaveSettings()
    {
        if (IsUnsavedPopupOpen)
        {
            return;
        }

        if (HasUnsavedChanges())
        {
            m_unsavedPopup.SetActive(true);
            m_unsavedPopup.transform.SetAsLastSibling();
            SelectUiObject(m_popupSaveButton);
            return;
        }

        LeaveSettings();
    }

    private bool IsUnsavedPopupOpen => m_unsavedPopup != null && m_unsavedPopup.activeSelf;

    private void LeaveSettings()
    {
        CloseUnsavedChangesPopup();
        if (m_settingsOnly)
        {
            CloseSettingsOnly();
        }
        else
        {
            ShowPausePage();
        }
    }

    private void SaveAndLeaveSettings()
    {
        SaveSettings();

        // 파일 쓰기에 실패하면 기준값이 그대로라 변경이 남아 있습니다. 그때는 팝업을 닫지 않고 설정 화면에 머뭅니다.
        if (!HasUnsavedChanges())
        {
            LeaveSettings();
        }
    }

    private void DiscardAndLeaveSettings()
    {
        RevertSettings();
        LeaveSettings();
    }

    private void CloseUnsavedChangesPopup()
    {
        if (m_unsavedPopup != null)
        {
            m_unsavedPopup.SetActive(false);
        }
    }

    /// <summary>
    /// 위쪽 탭과 전투 왼쪽 메뉴처럼 고르면 노란 칸(Select_mark_01) 위에 검정 굵은 글자로 바뀌는 버튼을 만듭니다.
    /// </summary>
    /// <param name="left">Figma에서 칸의 왼쪽 x입니다.</param>
    /// <param name="top">Figma에서 칸의 위쪽 y입니다.</param>
    private Button CreateMenuSelectButton(
        string name,
        Transform parent,
        float left,
        float top,
        string label,
        float fontSize,
        out TextMeshProUGUI labelText)
    {
        Button button = CreateButton(
            name,
            parent,
            new Vector2(left, -top),
            MenuSelectSize,
            label,
            fontSize,
            Color.clear);
        Image image = button.GetComponent<Image>();
        image.sprite = m_theme != null ? m_theme.tabSelected : null;
        labelText = button.GetComponentInChildren<TextMeshProUGUI>(true);
        return button;
    }

    /// <summary>고른 칸은 노란 바탕에 검정 굵은 글자, 나머지는 바탕 없이 흰 글자로 둡니다.</summary>
    private void ApplyMenuSelection(Image background, TextMeshProUGUI label, bool selected)
    {
        if (background != null)
        {
            background.color = !selected ? Color.clear
                : background.sprite != null ? Color.white
                : MenuSelectFallbackColor;
        }

        if (label != null)
        {
            label.color = selected ? Color.black : Color.white;
            label.font = selected && m_boldFont != null ? m_boldFont : m_regularFont;
        }
    }

    /// <summary>
    /// Figma 하단의 Preferences_button 버튼을 만듭니다. 글자 칸과 아이콘은 Figma 화면 좌표로 받습니다.
    /// </summary>
    private Button CreateActionButton(
        string name,
        Transform parent,
        Rect frame,
        string label,
        float textLeft,
        float textWidth,
        Sprite icon,
        Rect iconFrame)
    {
        Button button = CreateButton(
            name,
            parent,
            new Vector2(frame.x, -frame.y),
            frame.size,
            label,
            36f,
            new Color(0.02f, 0.03f, 0.04f, 1f));
        ApplySprite(button.GetComponent<Image>(), m_theme != null ? m_theme.actionButton : null);
        m_theme?.ApplyActionButtonStates(button);

        // 글자와 아이콘은 버튼 안 좌표로 옮깁니다. Figma 글자 칸 높이(36)를 버튼 세로 가운데에 둡니다.
        TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>(true);
        text.font = m_boldFont != null ? m_boldFont : m_regularFont;
        SetTopLeftRect(text.rectTransform, new Vector2(textLeft - frame.x, -((frame.height - 36f) * 0.5f)), new Vector2(textWidth, 36f));

        if (icon != null)
        {
            Image iconImage = CreateTopLeftImage(
                "Icon",
                button.transform,
                new Vector2(iconFrame.x - frame.x, -(iconFrame.y - frame.y)),
                iconFrame.size,
                Color.white);
            iconImage.sprite = icon;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
        }

        return button;
    }

    /// <summary>비디오 탭입니다. Figma Setting Scene Video 1(닫힌 드롭다운)·Video 2(열린 해상도 목록) 프레임을 따릅니다.</summary>
    /// <remarks>
    /// 값은 다른 설정과 같이 <see cref="GameSettingManager"/>가 가집니다. 고르면 바로 적용되고, '되돌리기'는 설정 화면을
    /// 열 때의 해상도로, '변경사항 저장'은 설정 파일에 남깁니다. 에디터 Game 뷰에서는 해상도가 바뀌지 않습니다.
    /// </remarks>
    private void BuildVideoSettingsPage(Transform parent)
    {
        CreateSectionTitle(parent, 371f, 167f, "모니터");
        m_monitorHelp = CreateHelpText("Monitor Help", parent, 546f, 190f, "모니터가 하나뿐이면 바꿀 수 없습니다.");
        m_monitorDropdown = CreateDropdown("Monitor Dropdown", parent, 393f, 220f, true);
        m_monitorDropdown.onValueChanged.AddListener(OnMonitorChanged);

        CreateSectionTitle(parent, 371f, 333f, "해상도");
        CreateHelpText("Resolution Help", parent, 546f, 356f, "주사율은 전체 화면에서만 그대로 적용됩니다.");
        m_resolutionDropdown = CreateDropdown("Resolution Dropdown", parent, 393f, 386f, true);
        m_resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);

        CreateSectionTitle(parent, 371f, 499f, "화면 모드");
        m_screenModeDropdown = CreateDropdown("Screen Mode Dropdown", parent, 393f, 552f, true);
        List<string> screenModeLabels = new(s_screenModes.Length);
        for (int i = 0; i < s_screenModes.Length; i++)
        {
            screenModeLabels.Add(s_screenModes[i].label);
        }

        m_screenModeDropdown.ClearOptions();
        m_screenModeDropdown.AddOptions(screenModeLabels);
        m_screenModeDropdown.onValueChanged.AddListener(OnScreenModeChanged);

        m_vSyncToggle = CreateToggle("VSync Toggle", parent, 394f, 628f, "수직 동기화");
        m_vSyncToggle.onValueChanged.AddListener(OnVSyncChanged);
        m_frameLimitHelp = CreateHelpText("Frame Limit Help", parent, 639f, 660f, "수직 동기화가 켜져 있으면 모니터 주사율을 따릅니다.");

        // 슬라이더가 우선입니다. 슬라이더를 움직이면 드롭다운은 '사용자 지정'으로 바뀌고,
        // 드롭다운에서 프리셋을 고르면 슬라이더가 그 값으로 옮겨 갑니다.
        m_frameLimitSlider = CreateGauge(
            "Frame Limit",
            parent,
            393f,
            718f,
            "최대 프레임 제한",
            MinFrameLimit,
            MaxFrameLimit,
            true,
            out m_frameLimitValue);
        m_frameLimitSlider.onValueChanged.AddListener(OnFrameLimitSliderChanged);

        // Figma에서 프레임 제한 칸은 회색(Drop_box_02) 바탕에 화살표가 없습니다.
        m_frameLimitDropdown = CreateDropdown("Frame Limit Dropdown", parent, 397f, 810f, false);
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

    /// <summary>
    /// TMP 기본 드롭다운을 Figma Drop Box 모양(639x56)으로 만듭니다.
    /// </summary>
    /// <remarks>
    /// 닫힌 칸은 Drop_box_01(검정)에 오른쪽 화살표, 펼친 목록은 Drop_box_03(짙은 회색) 칸이고,
    /// 지금 고른 칸과 마우스를 올린 칸은 Drop_box_02(회색)로 바뀝니다.
    /// </remarks>
    /// <param name="left">Figma에서 칸의 왼쪽 x입니다.</param>
    /// <param name="top">Figma에서 칸의 위쪽 y입니다.</param>
    /// <param name="primary">거짓이면 프레임 제한 칸처럼 회색 바탕에 화살표 없이 그립니다.</param>
    private TMP_Dropdown CreateDropdown(string name, Transform parent, float left, float top, bool primary)
    {
        GameObject dropdownObject = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
        dropdownObject.name = name;
        RectTransform rect = (RectTransform)dropdownObject.transform;
        rect.SetParent(parent, false);
        SetTopLeftRect(rect, new Vector2(left, -top), DropdownSize);

        TMP_Dropdown dropdown = dropdownObject.GetComponent<TMP_Dropdown>();
        Image background = dropdownObject.GetComponent<Image>();
        background.color = primary ? Color.black : new Color(0.32f, 0.32f, 0.32f, 1f);
        background.type = Image.Type.Simple;
        ApplySprite(background, m_theme == null ? null : primary ? m_theme.dropdown : m_theme.dropdownSelectedItem);
        ColorBlock colors = dropdown.colors;
        colors.highlightedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        colors.selectedColor = Color.white;
        dropdown.colors = colors;

        // Figma Drop_box_arrow(29x25)는 칸 오른쪽 끝에서 12px 안쪽, 세로 가운데입니다.
        Transform arrow = rect.Find("Arrow");
        if (arrow != null)
        {
            Image arrowImage = arrow.GetComponent<Image>();
            Sprite arrowSprite = m_theme != null ? m_theme.dropdownArrow : null;
            arrow.gameObject.SetActive(primary && arrowSprite != null);
            if (arrowImage != null && arrowSprite != null)
            {
                arrowImage.sprite = arrowSprite;
                arrowImage.color = Color.white;
                arrowImage.raycastTarget = false;
                SetTopLeftRect((RectTransform)arrow, new Vector2(598f, -19f), new Vector2(29f, 25f));
            }
        }

        RectTransform caption = (RectTransform)dropdown.captionText.transform;
        caption.offsetMin = new Vector2(DropdownTextInset, 0f);
        caption.offsetMax = new Vector2(-50f, 0f);

        // 펼친 목록은 칸 바로 아래에 같은 폭으로 6칸까지 보이고, 더 많으면 스크롤합니다.
        RectTransform template = dropdown.template;
        template.sizeDelta = new Vector2(template.sizeDelta.x, DropdownSize.y * 6f);
        Image templateImage = template.GetComponent<Image>();
        templateImage.color = new Color(0.12f, 0.12f, 0.12f, 1f);
        templateImage.type = Image.Type.Simple;
        ApplySprite(templateImage, m_theme != null ? m_theme.dropdownItem : null);

        Toggle item = template.GetComponentInChildren<Toggle>(true);
        RectTransform itemRect = (RectTransform)item.transform;
        itemRect.sizeDelta = new Vector2(itemRect.sizeDelta.x, DropdownSize.y);
        RectTransform content = (RectTransform)itemRect.parent;
        content.sizeDelta = new Vector2(content.sizeDelta.x, DropdownSize.y);

        Image itemBackground = (Image)item.targetGraphic;
        itemBackground.color = new Color(0.12f, 0.12f, 0.12f, 1f);
        ApplySprite(itemBackground, m_theme != null ? m_theme.dropdownItem : null);
        Sprite itemHighlight = m_theme != null ? m_theme.dropdownSelectedItem : null;
        if (itemHighlight != null)
        {
            // 마우스를 올린 칸은 Drop_box_02 이미지로 바꿉니다. 짙은 칸에 색을 곱해서는 밝아지지 않으므로 이미지 교체를 씁니다.
            item.transition = Selectable.Transition.SpriteSwap;
            SpriteState state = item.spriteState;
            state.highlightedSprite = itemHighlight;
            state.selectedSprite = itemHighlight;
            state.pressedSprite = itemHighlight;
            item.spriteState = state;
        }
        else
        {
            ColorBlock itemColors = item.colors;
            itemColors.highlightedColor = new Color(2.6f, 2.6f, 2.6f, 1f);
            itemColors.selectedColor = new Color(2.6f, 2.6f, 2.6f, 1f);
            itemColors.colorMultiplier = 3f;
            item.colors = itemColors;
        }

        // 기본 체크 표시 자리를 칸 전체로 넓혀, 지금 고른 칸을 회색(Drop_box_02) 칸으로 보이게 합니다.
        if (item.graphic is Image checkmark)
        {
            RectTransform checkRect = checkmark.rectTransform;
            checkRect.anchorMin = Vector2.zero;
            checkRect.anchorMax = Vector2.one;
            checkRect.offsetMin = Vector2.zero;
            checkRect.offsetMax = Vector2.zero;
            checkmark.color = new Color(0.32f, 0.32f, 0.32f, 1f);
            ApplySprite(checkmark, itemHighlight);
            checkmark.raycastTarget = false;
        }

        RectTransform itemLabel = (RectTransform)dropdown.itemText.transform;
        itemLabel.offsetMin = new Vector2(DropdownItemTextInset, 0f);
        itemLabel.offsetMax = new Vector2(-20f, 0f);

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
            text.fontSize = 25f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Left;
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
        // 화면 비율은 16:9로 고정이라, 모니터가 16:9가 아니면 첫 칸은 모니터 안에 들어가는 가장 큰 16:9 해상도이고 목록도 16:9만 보여 줍니다.
        DisplayInfo display = Screen.mainWindowDisplayInfo;
        Vector2Int nativeSize = new(display.width, display.height);
        RefreshRate nativeRate = display.refreshRate;
        if (nativeSize.x <= 0 || nativeSize.y <= 0)
        {
            nativeSize = new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height);
            nativeRate = Screen.currentResolution.refreshRateRatio;
        }

        DisplayOption native = new(GameSettingManager.FitToTargetAspect(nativeSize.x, nativeSize.y), nativeRate);

        List<DisplayOption> modes = new();
        Resolution[] resolutions = Screen.resolutions;
        for (int i = 0; i < resolutions.Length; i++)
        {
            if (!GameSettingManager.IsTargetAspect(resolutions[i].width, resolutions[i].height))
            {
                continue;
            }

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

    /// <summary>Figma Setting Scene Battle 프레임입니다. 왼쪽 메뉴(전투 일반·캐릭터 조준선·투척물 조준선)로 아래 화면을 바꿉니다.</summary>
    private void BuildCombatSettingsPage(Transform parent)
    {
        Image sideBar = CreateTopLeftImage(
            "Side Menu Bar",
            parent,
            new Vector2(300f, -160f),
            new Vector2(2f, 740f),
            Color.white);
        sideBar.raycastTarget = false;
        ApplySprite(sideBar, m_theme != null ? m_theme.sideMenuBar : null);

        string[] labels = { "전투 일반", "캐릭터 조준선", "투척물 조준선" };
        for (int i = 0; i < labels.Length; i++)
        {
            int subTabIndex = i;
            Button button = CreateMenuSelectButton(
                $"Combat SubTab {labels[i]}",
                parent,
                47f,
                166f + (65f * i),
                labels[i],
                30f,
                out TextMeshProUGUI label);
            button.onClick.AddListener(() => SelectCombatSubTab(subTabIndex));
            m_combatSubTabBackgrounds[i] = button.GetComponent<Image>();
            m_combatSubTabLabels[i] = label;
        }

        m_combatSubPages[0] = CreateStretchObject("Combat General Page", parent);
        m_combatSubPages[1] = CreateStretchObject("Crosshair Settings Page", parent);
        m_combatSubPages[2] = CreateStretchObject("Throwable Crosshair Settings Page", parent);
        m_combatSubPages[0].transform.SetAsFirstSibling();
        m_combatSubPages[1].transform.SetAsFirstSibling();
        m_combatSubPages[2].transform.SetAsFirstSibling();

        BuildCombatGeneralSettings(m_combatSubPages[0].transform);

        // 조준선 패널도 다른 탭과 같은 슬라이더(클릭 영역·아트·핸들 크기)와 이미지를 쓰게 합니다.
        m_crosshairSettingsPanel = m_combatSubPages[1].AddComponent<CrosshairSettingsPanel>();
        m_crosshairSettingsPanel.SliderFactory = CreateHorizontalSlider;
        m_crosshairSettingsPanel.Theme = m_theme;
        m_crosshairSettingsPanel.Build(
            m_combatSubPages[1].transform as RectTransform,
            m_regularFont,
            m_boldFont);

        m_throwableCrosshairSettingsPanel = m_combatSubPages[2].AddComponent<CrosshairSettingsPanel>();
        m_throwableCrosshairSettingsPanel.SliderFactory = CreateHorizontalSlider;
        m_throwableCrosshairSettingsPanel.Theme = m_theme;
        m_throwableCrosshairSettingsPanel.BuildSharedThrowable(
            m_combatSubPages[2].transform as RectTransform,
            m_regularFont,
            m_boldFont);

        SelectCombatSubTab(0);
    }

    /// <summary>Figma Setting Scene Battle Generel 프레임입니다.</summary>
    private void BuildCombatGeneralSettings(Transform parent)
    {
        CreateSectionTitle(parent, 371f, 166f, "전투 일반");

        m_mouseSensitivitySlider = CreateGauge(
            "Mouse Sensitivity",
            parent,
            393f,
            248f,
            "마우스 감도",
            0.01f,
            10f,
            false,
            out m_mouseSensitivityValue);
        m_mouseSensitivitySlider.onValueChanged.AddListener(OnMouseSensitivityChanged);

        m_cameraKickToggle = CreateToggle("Camera Kick Toggle", parent, 394f, 383f, "카메라 킥 사용");
        m_cameraKickToggle.onValueChanged.AddListener(OnCameraKickChanged);
        CreateHelpText("Camera Kick Help", parent, 668f, 415f, "끄면 사격 시 카메라 롤·FOV 시각 효과를 표시하지 않습니다.");

        // Figma 전투 일반 프레임에는 없는 항목이라, 카메라 킥 줄과 같은 모양·간격(70px 아래)으로 둡니다.
        m_aimToggleToggle = CreateToggle("Aim Toggle Toggle", parent, 394f, 453f, "조준(ADS) 토글");
        m_aimToggleToggle.onValueChanged.AddListener(OnAimToggleChanged);
        float aimHelpLeft = 394f + ((RectTransform)m_aimToggleToggle.transform).sizeDelta.x + 33f;
        CreateHelpText("Aim Toggle Help", parent, aimHelpLeft, 485f, "켜면 우클릭을 한 번 눌러 조준하고, 다시 누르면 풉니다. 끄면 누르고 있는 동안만 조준합니다.");

        RefreshCombatGeneralControls();
        CommitCombatGeneralSettings();
    }

    /// <summary>
    /// Figma Setting Scene Audio 프레임입니다. 전체·배경음·효과음 볼륨을 0~100 슬라이더로 조절합니다.
    /// </summary>
    /// <remarks>
    /// 값은 즉시 <see cref="GameSettingManager"/>에 반영되고, 저장·되돌리기는 다른 설정과 같은 흐름을 따릅니다.
    /// </remarks>
    private void BuildAudioSettingsPage(Transform parent)
    {
        CreateSectionTitle(parent, 372f, 167f, "오디오");

        string[] labels = { "전체 음량", "배경음", "효과음" };
        for (int i = 0; i < labels.Length; i++)
        {
            Slider slider = CreateGauge(
                labels[i],
                parent,
                394f,
                265f + (114f * i),
                labels[i],
                0f,
                100f,
                true,
                out TextMeshProUGUI valueText);

            int channel = i;
            slider.onValueChanged.AddListener(value => OnVolumeSliderChanged(channel, value));
            m_volumeSliders[i] = slider;
            m_volumeValueTexts[i] = valueText;
        }

        SyncAudioSettings(GameSettingManager.Instance);
    }

    /// <param name="channel">0 = 전체, 1 = 배경음, 2 = 효과음</param>
    /// <param name="value">0~100 슬라이더 값</param>
    private void OnVolumeSliderChanged(int channel, float value)
    {
        int percent = Mathf.RoundToInt(value);
        m_volumeValueTexts[channel].text = percent.ToString();

        GameSettingManager settingManager = GameSettingManager.Instance;
        if (settingManager == null)
        {
            return;
        }

        float volume = percent / 100f;
        switch (channel)
        {
            case 0: settingManager.SetMasterVolume(volume); break;
            case 1: settingManager.SetBgmVolume(volume); break;
            case 2: settingManager.SetSfxVolume(volume); break;
        }
    }

    /// <summary>슬라이더 표시를 <see cref="GameSettingManager"/>의 현재 볼륨으로 맞춥니다.</summary>
    private void SyncAudioSettings(GameSettingManager settingManager)
    {
        if (settingManager == null || m_volumeSliders[0] == null)
        {
            return;
        }

        float[] volumes = { settingManager.MasterVolume, settingManager.BgmVolume, settingManager.SfxVolume };
        for (int i = 0; i < volumes.Length; i++)
        {
            int percent = Mathf.RoundToInt(Mathf.Clamp01(volumes[i]) * 100f);
            m_volumeSliders[i].SetValueWithoutNotify(percent);
            m_volumeValueTexts[i].text = percent.ToString();
        }
    }

    /// <summary>
    /// 설정 화면 공통 가로 슬라이더를 만듭니다. 조준선 패널도 이 함수로 만듭니다.
    /// </summary>
    /// <remarks>
    /// Figma Gauge 비율대로 그립니다. 손잡이(Handle 20x42)는 슬라이더 높이에 맞추고, 바탕·채움 막대는 높이의 11/42 두께로
    /// 세로 가운데에 둡니다. 큰 슬라이더(643x42)와 조준선 슬라이더(408x26.65)가 같은 비율입니다.
    /// 채움 막대는 손잡이 가운데까지 옵니다. 테마 이미지가 없으면 단색(회색 바탕·초록 채움·흰 손잡이)으로 그립니다.
    /// </remarks>
    private Slider CreateHorizontalSlider(
        string name,
        Transform parent,
        Vector2 topLeftPosition,
        Vector2 size,
        float minValue,
        float maxValue,
        bool wholeNumbers)
    {
        GameObject sliderObject = CreateTopLeftObject(name, parent, topLeftPosition, size);
        Slider slider = sliderObject.AddComponent<Slider>();
        slider.minValue = minValue;
        slider.maxValue = maxValue;
        slider.wholeNumbers = wholeNumbers;

        // 보이는 막대는 얇게 그립니다. 슬라이더 높이 전체에서 클릭과 드래그를 받도록 투명한 영역을 깝니다.
        Image hitArea = sliderObject.AddComponent<Image>();
        hitArea.color = Color.clear;

        float trackInset = size.y * (1f - (GaugeTrackHeight / GaugeHandleSize.y)) * 0.5f;
        Vector2 handleSize = new(size.y * (GaugeHandleSize.x / GaugeHandleSize.y), size.y);
        float halfHandle = handleSize.x * 0.5f;

        Image sliderBackground = CreateStretchImage("Background", sliderObject.transform, new Color(0f, 0f, 0f, 1f));
        sliderBackground.rectTransform.offsetMin = new Vector2(0f, trackInset);
        sliderBackground.rectTransform.offsetMax = new Vector2(0f, -trackInset);
        sliderBackground.raycastTarget = false;
        ApplySprite(sliderBackground, m_theme != null ? m_theme.sliderTrack : null);

        // 채움 영역을 손잡이 이동 범위와 같게 잡고, 채움 막대 왼쪽만 손잡이 반 폭만큼 늘려 막대 왼쪽 끝부터 손잡이 가운데까지 칠합니다.
        GameObject fillArea = CreateStretchObject("Fill Area", sliderObject.transform);
        RectTransform fillAreaRect = (RectTransform)fillArea.transform;
        fillAreaRect.offsetMin = new Vector2(halfHandle, trackInset);
        fillAreaRect.offsetMax = new Vector2(-halfHandle, -trackInset);
        Image fill = CreateStretchImage("Fill", fillArea.transform, new Color(0f, 1f, 0.08f, 1f));
        fill.rectTransform.offsetMin = new Vector2(-halfHandle, 0f);
        fill.raycastTarget = false;
        ApplySprite(fill, m_theme != null ? m_theme.sliderFill : null);

        GameObject handleArea = CreateStretchObject("Handle Slide Area", sliderObject.transform);
        RectTransform handleAreaRect = (RectTransform)handleArea.transform;
        handleAreaRect.offsetMin = new Vector2(halfHandle, 0f);
        handleAreaRect.offsetMax = new Vector2(-halfHandle, 0f);
        Image handle = CreateTopLeftImage(
            "Handle",
            handleArea.transform,
            Vector2.zero,
            handleSize,
            Color.white);
        // Slider는 손잡이 앵커를 세로 0~1로 바꿔 슬라이더 높이만큼 늘립니다. 그래서 세로 크기는 0으로 둬야 손잡이가 슬라이더 높이와 같아집니다.
        RectTransform handleRect = handle.rectTransform;
        handleRect.anchorMin = new Vector2(0.5f, 0f);
        handleRect.anchorMax = new Vector2(0.5f, 1f);
        handleRect.pivot = new Vector2(0.5f, 0.5f);
        handleRect.anchoredPosition = Vector2.zero;
        handleRect.sizeDelta = new Vector2(handleSize.x, 0f);
        ApplySprite(handle, m_theme != null ? m_theme.sliderHandle : null);

        slider.fillRect = fill.rectTransform;
        slider.handleRect = handleRect;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        return slider;
    }

    /// <summary>
    /// Figma Gauge 묶음(이름·값·슬라이더)을 만듭니다. 이름은 위쪽 왼쪽, 값은 오른쪽 끝에 맞추고, 슬라이더는 이름 39px 아래에 둡니다.
    /// </summary>
    /// <param name="left">Figma에서 이름 글자의 왼쪽 x입니다.</param>
    /// <param name="top">Figma에서 이름 글자의 위쪽 y입니다.</param>
    private Slider CreateGauge(
        string name,
        Transform parent,
        float left,
        float top,
        string label,
        float minValue,
        float maxValue,
        bool wholeNumbers,
        out TextMeshProUGUI valueText)
    {
        CreateFigmaText($"{name} Label", parent, left, top, 360f, 30f, label, 30f, TextAlignmentOptions.Left);

        // 값 글자는 오른쪽 끝을 슬라이더 끝(left + 643)에 맞춥니다.
        valueText = CreateFigmaText(
            $"{name} Value",
            parent,
            left + GaugeSize.x - 200f,
            top + 11f,
            200f,
            24f,
            string.Empty,
            24f,
            TextAlignmentOptions.Right);

        return CreateHorizontalSlider(
            $"{name} Slider",
            parent,
            new Vector2(left, -(top + 39f)),
            GaugeSize,
            minValue,
            maxValue,
            wholeNumbers);
    }

    /// <summary>
    /// 아트 스프라이트를 원본 색 그대로 보이게 적용합니다. Border가 있으면 9-slice로 늘립니다. 스프라이트가 없으면 지금 색을 그대로 둡니다.
    /// </summary>
    private static void ApplySprite(Image image, Sprite sprite)
    {
        if (image == null || sprite == null)
        {
            return;
        }

        image.sprite = sprite;
        image.color = Color.white;
        image.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
    }

    /// <summary>
    /// Figma 항목 제목(Table)을 만듭니다. 왼쪽에 노란 세로 막대(Table_mark), 그 오른쪽에 36px 굵은 제목을 둡니다.
    /// </summary>
    /// <param name="left">Figma에서 세로 막대의 왼쪽 x입니다.</param>
    /// <param name="top">Figma에서 세로 막대의 위쪽 y입니다.</param>
    private void CreateSectionTitle(Transform parent, float left, float top, string title)
    {
        Image mark = CreateTopLeftImage(
            $"{title} Mark",
            parent,
            new Vector2(left, -top),
            new Vector2(8f, 38f),
            MenuSelectFallbackColor);
        mark.raycastTarget = false;
        ApplySprite(mark, m_theme != null ? m_theme.titleMark : null);

        TextMeshProUGUI text = CreateFigmaText($"{title} Title", parent, left + 22f, top + 2f, 400f, 36f, title, 36f, TextAlignmentOptions.Left);
        text.font = m_boldFont != null ? m_boldFont : m_regularFont;
    }

    /// <summary>제목이나 체크박스 옆에 붙는 15px 안내 글자를 만듭니다.</summary>
    private TextMeshProUGUI CreateHelpText(string name, Transform parent, float left, float top, string value)
    {
        return CreateFigmaText(name, parent, left, top, 600f, 15f, value, 15f, TextAlignmentOptions.Left);
    }

    /// <summary>
    /// Figma Chack Box 묶음을 만듭니다. 체크 칸(Chack_box 31x31)보다 큰 체크 표시(Chack 51x42)가 칸 왼쪽 위로 삐져나오게 겹칩니다.
    /// </summary>
    /// <param name="left">Figma 묶음의 왼쪽 x(체크 표시 왼쪽)입니다.</param>
    /// <param name="top">Figma 묶음의 위쪽 y(체크 표시 위쪽)입니다.</param>
    private Toggle CreateToggle(string name, Transform parent, float left, float top, string label)
    {
        GameObject toggleObject = CreateTopLeftObject(name, parent, new Vector2(left, -top), new Vector2(360f, 48f));
        Toggle toggle = toggleObject.AddComponent<Toggle>();

        // 글자를 눌러도 켜고 끌 수 있게 줄 전체에서 클릭을 받습니다.
        Image hitArea = toggleObject.AddComponent<Image>();
        hitArea.color = Color.clear;

        Image background = CreateTopLeftImage(
            "Background",
            toggleObject.transform,
            new Vector2(4f, -16f),
            new Vector2(31f, 31f),
            new Color(1f, 1f, 1f, 0.16f));
        background.raycastTarget = false;
        ApplySprite(background, m_theme != null ? m_theme.checkBox : null);

        Image checkmark = CreateTopLeftImage(
            "Checkmark",
            toggleObject.transform,
            Vector2.zero,
            new Vector2(51f, 42f),
            new Color(0f, 1f, 0.08f, 1f));
        checkmark.raycastTarget = false;
        if (m_theme != null && m_theme.checkMark != null)
        {
            ApplySprite(checkmark, m_theme.checkMark);
        }
        else
        {
            SetTopLeftRect(checkmark.rectTransform, new Vector2(10f, -22f), new Vector2(19f, 19f));
        }

        TextMeshProUGUI text = CreateTopLeftText(
            "Label",
            toggleObject.transform,
            new Vector2(51f, -18f),
            new Vector2(300f, 30f),
            label,
            30f,
            TextAlignmentOptions.Left);
        text.ForceMeshUpdate();
        ((RectTransform)toggleObject.transform).sizeDelta = new Vector2(51f + Mathf.Ceil(text.preferredWidth), 48f);

        toggle.targetGraphic = background;
        toggle.graphic = checkmark;
        return toggle;
    }

    /// <summary>Figma 좌표(왼쪽 위 x, y와 크기)로 글자를 만듭니다.</summary>
    private TextMeshProUGUI CreateFigmaText(
        string name,
        Transform parent,
        float left,
        float top,
        float width,
        float height,
        string value,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        return CreateTopLeftText(name, parent, new Vector2(left, -top), new Vector2(width, height), value, fontSize, alignment);
    }

    private static void SetTopLeftRect(RectTransform rect, Vector2 topLeftPosition, Vector2 size)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = topLeftPosition;
        rect.sizeDelta = size;
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

    private void OnAimToggleChanged(bool enabled)
    {
        if (!m_isRefreshingCombatGeneral)
        {
            m_aimToggleEnabled = enabled;
            GameSettingManager.Instance?.SetAimToggleEnabled(enabled);
        }
    }

    private void RefreshCombatGeneralControls()
    {
        if (m_mouseSensitivitySlider == null || m_cameraKickToggle == null || m_aimToggleToggle == null)
        {
            return;
        }

        m_isRefreshingCombatGeneral = true;
        m_mouseSensitivitySlider.SetValueWithoutNotify(m_mouseSensitivity);
        m_cameraKickToggle.SetIsOnWithoutNotify(m_cameraKickEnabled);
        m_aimToggleToggle.SetIsOnWithoutNotify(m_aimToggleEnabled);
        m_mouseSensitivityValue.text = m_mouseSensitivity.ToString("0.00");
        m_isRefreshingCombatGeneral = false;
    }

    private void CommitCombatGeneralSettings()
    {
        m_committedMouseSensitivity = m_mouseSensitivity;
        m_committedCameraKickEnabled = m_cameraKickEnabled;
        m_committedAimToggleEnabled = m_aimToggleEnabled;
    }

    private void SyncCombatGeneralSettings(GameSettingManager settingManager, bool commit)
    {
        if (settingManager == null)
        {
            return;
        }

        m_mouseSensitivity = settingManager.MouseSensitivity;
        m_cameraKickEnabled = settingManager.CameraKickEnabled;
        m_aimToggleEnabled = settingManager.AimToggleEnabled;
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
        m_aimToggleEnabled = m_committedAimToggleEnabled;
        RefreshCombatGeneralControls();
    }

    /// <summary>아직 내용이 없는 탭입니다. 다른 탭과 같은 항목 제목 아래에 안내 글자만 둡니다.</summary>
    private void BuildPlaceholderPage(Transform parent, string title, string description)
    {
        CreateSectionTitle(parent, 371f, 167f, title);

        TextMeshProUGUI descriptionText = CreateFigmaText(
            $"{title} Placeholder Description",
            parent,
            393f,
            232f,
            900f,
            30f,
            description,
            25f,
            TextAlignmentOptions.Left);
        descriptionText.color = new Color(1f, 1f, 1f, 0.55f);
    }

    private void SelectCombatSubTab(int selectedIndex)
    {
        m_selectedCombatSubTab = Mathf.Clamp(selectedIndex, 0, m_combatSubPages.Length - 1);
        for (int i = 0; i < m_combatSubPages.Length; i++)
        {
            bool selected = i == m_selectedCombatSubTab;
            ApplyMenuSelection(m_combatSubTabBackgrounds[i], m_combatSubTabLabels[i], selected);

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
