using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 타이틀 메뉴 버튼(새 게임·불러오기·나가기)을 실제 동작에 연결합니다.
/// </summary>
/// <remarks>
/// 버튼은 씬에 미리 배치돼 있고, 이 컴포넌트는 이름으로 찾아 onClick만 붙입니다. 설정은 게임 중과 같은 설정 화면
/// (<see cref="GameplayPauseMenuController"/>를 설정 화면만 쓰도록 둔 것)을 열고, 크레딧은 <see cref="TitleCreditsOverlay"/>가 맡습니다.
///
/// 타이틀 씬에는 <see cref="GameSaveManager"/>가 없습니다. 그래서 불러오기는 셸터 씬을 먼저 연 뒤, 그 씬이
/// 로드된 직후(<see cref="SceneManager.sceneLoaded"/>) 셸터 씬의 저장 관리자로 저장을 읽습니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class TitleMenuActions : MonoBehaviour
{
    private const string NewGameButtonName = "New Game";
    private const string LoadGameButtonName = "Load Game";
    private const string QuitButtonName = "Quit";
    private const string SettingsButtonName = "Settings";

    [Header("Buttons (비워 두면 이름으로 찾습니다)")]
    [Tooltip("\"새 게임\" 버튼입니다. 비어 있으면 씬에서 'New Game'을 찾습니다.")]
    [SerializeField] private Button m_newGameButton;

    [Tooltip("\"불러오기\" 버튼입니다. 비어 있으면 씬에서 'Load Game'을 찾습니다.")]
    [SerializeField] private Button m_loadGameButton;

    [Tooltip("\"나가기\" 버튼입니다. 비어 있으면 씬에서 'Quit'을 찾습니다.")]
    [SerializeField] private Button m_quitButton;

    [Tooltip("\"설정\" 버튼입니다. 비어 있으면 씬에서 'Settings'를 찾습니다.")]
    [SerializeField] private Button m_settingsButton;

    [Tooltip("설정 버튼이 열 설정 화면입니다. 비어 있으면 같은 씬에서 찾습니다. '설정 화면만 사용'을 켠 것이어야 합니다.")]
    [SerializeField] private GameplayPauseMenuController m_settingsMenu;

    [Header("Scenes")]
    [Tooltip("새 게임을 시작할 씬 이름입니다. 빌드 설정에 있어야 합니다.")]
    [SerializeField] private string m_newGameSceneName = "ShelterScene_Jung";

    [Tooltip("저장을 불러온 뒤 들어갈 씬 이름입니다. 빌드 설정에 있어야 하고, 이 씬에 GameSaveManager가 있어야 합니다.")]
    [SerializeField] private string m_loadGameSceneName = "ShelterScene_Jung";

    [Header("Behaviour")]
    [Tooltip("켜면 저장 파일이 없을 때 불러오기 버튼을 누를 수 없게 합니다.")]
    [SerializeField] private bool m_disableLoadWithoutSave = true;

    /// <summary>다음 씬이 로드되면 저장을 불러와야 하는지 여부입니다. 씬을 넘어가도 남도록 정적으로 둡니다.</summary>
    private static bool s_loadSaveOnNextScene;

    private bool m_isLeaving;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // 도메인 리로드를 끈 상태에서 Play Mode를 다시 들어가면 이전 값이 남기 때문에 비웁니다.
        s_loadSaveOnNextScene = false;
        SceneManager.sceneLoaded -= HandleSceneLoadedForSave;
    }

    private void Awake()
    {
        ResolveButtons();

        // 새 게임은 난이도를 고른 뒤에 시작하므로, 씬에서 새 게임 버튼에 바로 시작하는 연결을 걸어 두면 안 됩니다.
        // 걸려 있으면 팝업이 뜨기도 전에 셸터로 넘어갑니다.
        if (m_newGameButton != null && m_newGameButton.onClick.GetPersistentEventCount() > 0)
        {
            Debug.LogWarning("[TitleMenuActions] 새 게임 버튼에 씬 연결이 남아 있습니다. 난이도 선택 전에 시작되지 않게 지워 주세요.", m_newGameButton);
        }

        Bind(m_newGameButton, StartNewGame);
        Bind(m_loadGameButton, LoadSavedGame);
        Bind(m_quitButton, QuitGame);
        Bind(m_settingsButton, OpenSettings);

        RefreshLoadButton();
        SaveFilePaths.GameSavesDeleted += RefreshLoadButton;
    }

    private void OnDestroy()
    {
        SaveFilePaths.GameSavesDeleted -= RefreshLoadButton;
    }

    /// <summary>저장 파일이 없으면 불러오기 버튼을 막습니다. 설정 화면에서 저장을 초기화하면 다시 부릅니다.</summary>
    private void RefreshLoadButton()
    {
        if (m_loadGameButton != null && m_disableLoadWithoutSave)
        {
            m_loadGameButton.interactable = HasAutoSaveFile();
        }
    }

    /// <summary>"새 게임": 난이도 선택 팝업을 띄우고, 고르면 그 난이도로 새 게임을 시작합니다.</summary>
    public void StartNewGame()
    {
        if (m_isLeaving)
        {
            return;
        }

        if (m_settingsMenu == null)
        {
            Debug.LogWarning("[TitleMenuActions] 설정 화면을 찾지 못해 난이도 선택 없이 중간으로 시작합니다.", this);
            BeginNewGame(GameDifficulty.Normal);
            return;
        }

        m_settingsMenu.ShowDifficultySelect(BeginNewGame);
    }

    /// <summary>고른 난이도로 새 게임을 시작합니다.</summary>
    /// <remarks>
    /// 새 게임 데이터(시작 자원·캐릭터·첫 체크포인트)는 <see cref="MainSceneSaveManager"/>가 만듭니다. 없으면 난이도만 정하고 씬을 옮깁니다.
    /// </remarks>
    private void BeginNewGame(GameDifficulty difficulty)
    {
        MainSceneSaveManager saveManager = MainSceneSaveManager.Instance;
        if (saveManager != null)
        {
            m_isLeaving = true;
            saveManager.StartNewGameAndEnterShelter(difficulty);
            return;
        }

        GameDataManager.Instance?.SetDifficulty(difficulty);
        LeaveTo(m_newGameSceneName, loadSave: false);
    }

    /// <summary>"불러오기": 저장을 불러올 씬으로 이동하고, 로드 직후 저장을 적용합니다.</summary>
    public void LoadSavedGame()
    {
        LeaveTo(m_loadGameSceneName, loadSave: true);
    }

    /// <summary>"설정": 설정 화면을 엽니다.</summary>
    public void OpenSettings()
    {
        if (m_settingsMenu == null)
        {
            Debug.LogWarning("[TitleMenuActions] 설정 화면(GameplayPauseMenuController)을 찾지 못해 열지 않습니다.", this);
            return;
        }

        m_settingsMenu.OpenSettings();
    }

    /// <summary>"나가기": 게임을 종료합니다. 에디터에서는 Play Mode를 끝냅니다.</summary>
    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void LeaveTo(string sceneName, bool loadSave)
    {
        if (m_isLeaving)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"[TitleMenuActions] '{sceneName}' 씬이 빌드 설정에 없어 이동할 수 없습니다.", this);
            return;
        }

        m_isLeaving = true;
        // 이전 판의 저장 밖 값(날짜·분대·필드 결과 등)이 새 판에 섞이지 않게 씬을 옮기기 전에 비웁니다.
        GameDataManager.Instance?.ResetSessionState();
        if (loadSave)
        {
            s_loadSaveOnNextScene = true;
            SceneManager.sceneLoaded -= HandleSceneLoadedForSave;
            SceneManager.sceneLoaded += HandleSceneLoadedForSave;
        }

        SceneTransitionController.LoadScene(sceneName);
    }

    /// <summary>불러오기로 연 씬이 로드되면 그 씬의 저장 관리자로 저장을 읽고, 구독을 바로 풉니다.</summary>
    /// <remarks>
    /// sceneLoaded는 새 씬 오브젝트의 Awake 뒤, Start 전에 옵니다. 셸터 데이터는 Start 전에 맞춰 두어야
    /// 첫 화면부터 불러온 값으로 보입니다.
    /// </remarks>
    private static void HandleSceneLoadedForSave(Scene scene, LoadSceneMode mode)
    {
        // 중간에 거치는 로딩 씬은 건너뛰고, 목표 씬이 로드될 때 저장을 읽습니다.
        if (scene.name == SceneTransitionController.LoadingSceneName)
        {
            return;
        }

        SceneManager.sceneLoaded -= HandleSceneLoadedForSave;
        if (!s_loadSaveOnNextScene)
        {
            return;
        }

        s_loadSaveOnNextScene = false;
        GameSaveManager saveManager = GameSaveManager.Instance != null
            ? GameSaveManager.Instance
            : FindFirstObjectByType<GameSaveManager>(FindObjectsInactive.Include);
        if (saveManager == null || !saveManager.LoadAutoGame())
        {
            Debug.LogWarning($"[TitleMenuActions] '{scene.name}'에서 저장을 불러오지 못했습니다. 새 게임 상태로 진행합니다.");
        }
    }

    private static bool HasAutoSaveFile()
    {
        GameSaveManager saveManager = GameSaveManager.Instance != null
            ? GameSaveManager.Instance
            : FindFirstObjectByType<GameSaveManager>(FindObjectsInactive.Include);
        return saveManager != null && saveManager.HasAutoSave();
    }

    private void ResolveButtons()
    {
        if (m_newGameButton == null)
        {
            m_newGameButton = FindButton(NewGameButtonName);
        }

        if (m_loadGameButton == null)
        {
            m_loadGameButton = FindButton(LoadGameButtonName);
        }

        if (m_quitButton == null)
        {
            m_quitButton = FindButton(QuitButtonName);
        }

        if (m_settingsButton == null)
        {
            m_settingsButton = FindButton(SettingsButtonName);
        }

        if (m_settingsMenu == null)
        {
            foreach (GameplayPauseMenuController menu in FindObjectsByType<GameplayPauseMenuController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (menu.gameObject.scene == gameObject.scene)
                {
                    m_settingsMenu = menu;
                    break;
                }
            }
        }
    }

    /// <summary>같은 씬의 버튼 가운데 이름이 일치하는 것을 찾습니다. 메뉴가 이 오브젝트 밖(캔버스)에 있어서 씬 전체를 봅니다.</summary>
    private Button FindButton(string buttonName)
    {
        foreach (Button button in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (button != null && button.gameObject.scene == gameObject.scene && button.name == buttonName)
            {
                return button;
            }
        }

        Debug.LogWarning($"[TitleMenuActions] '{buttonName}' 버튼을 찾지 못해 연결하지 않습니다.", this);
        return null;
    }

    private static void Bind(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }
}
