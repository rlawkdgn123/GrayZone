using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 타이틀 메뉴 버튼(새 게임·불러오기·나가기)을 실제 동작에 연결합니다.
/// </summary>
/// <remarks>
/// 버튼은 씬에 미리 배치돼 있고, 이 컴포넌트는 이름으로 찾아 onClick만 붙입니다. 설정은 아직 띄울 화면이 없어
/// 연결하지 않고, 크레딧은 <see cref="TitleCreditsOverlay"/>가 맡습니다.
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

    [Header("Buttons (비워 두면 이름으로 찾습니다)")]
    [Tooltip("\"새 게임\" 버튼입니다. 비어 있으면 씬에서 'New Game'을 찾습니다.")]
    [SerializeField] private Button m_newGameButton;

    [Tooltip("\"불러오기\" 버튼입니다. 비어 있으면 씬에서 'Load Game'을 찾습니다.")]
    [SerializeField] private Button m_loadGameButton;

    [Tooltip("\"나가기\" 버튼입니다. 비어 있으면 씬에서 'Quit'을 찾습니다.")]
    [SerializeField] private Button m_quitButton;

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

        // 씬에서 새 게임 버튼에 MainSceneSaveManager.OnStartNewGameButtonClicked가 이미 연결돼 있으면(새 게임 데이터
        // 생성 + 셸터 이동) 여기서는 붙이지 않습니다. 둘 다 붙으면 한 번 눌러도 셸터 씬을 두 번 불러옵니다.
        if (m_newGameButton != null && m_newGameButton.onClick.GetPersistentEventCount() == 0)
        {
            Bind(m_newGameButton, StartNewGame);
        }

        Bind(m_loadGameButton, LoadSavedGame);
        Bind(m_quitButton, QuitGame);

        if (m_loadGameButton != null && m_disableLoadWithoutSave)
        {
            m_loadGameButton.interactable = HasAutoSaveFile();
        }
    }

    /// <summary>"새 게임": 새 게임 씬으로 이동합니다.</summary>
    public void StartNewGame()
    {
        LeaveTo(m_newGameSceneName, loadSave: false);
    }

    /// <summary>"불러오기": 저장을 불러올 씬으로 이동하고, 로드 직후 저장을 적용합니다.</summary>
    public void LoadSavedGame()
    {
        LeaveTo(m_loadGameSceneName, loadSave: true);
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
