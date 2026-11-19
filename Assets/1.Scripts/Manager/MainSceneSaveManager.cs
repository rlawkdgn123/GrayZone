using UnityEngine;
using UnityEngine.SceneManagement;

public class MainSceneSaveManager : MonoBehaviour
{
    private const string ShelterSceneName = "ShelterScene_Jung";

    public static MainSceneSaveManager Instance { get; private set; }

    [Header("New Game")]
    [SerializeField] private DefaultSaveData defaultSaveData;

    public DefaultSaveData DefaultSaveData => defaultSaveData;

    private void Awake()
    {
        if (TryRejectDuplicate())
        {
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        if (GameDataManager.Instance == null)
        {
            Debug.LogWarning("[MainSceneSaveManager] GameDataManager.Instance is null.");
            return;
        }

        if (!GameDataManager.Instance.UseDefaultSaveDataOnShelterStart)
        {
            // 실제 게임 흐름: ShelterSceneManager.Awake에서 복사한 기존 GameDataManager 데이터를 유지한다.
            // Shelter 복귀 데이터가 초기화되는 문제를 점검할 때는 이 플래그가 false인지 먼저 확인할 것.
            return;
        }

        // 테스트 흐름: Shelter 단독 실행/반복 테스트를 위해 DefaultSaveData를 의도적으로 다시 적용한다.
        // 따라서 이 플래그가 true인 동안 Scene 복귀 전 데이터가 덮이는 것은 미수정 버그가 아니라 예상 동작이다.
        StartNewGame();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>새 게임 데이터를 만듭니다.</summary>
    /// <remarks>
    /// 난이도는 지금 판의 값을 그대로 씁니다. 셸터 단독 테스트 흐름(<see cref="GameDataManager.UseDefaultSaveDataOnShelterStart"/>)이
    /// 셸터에 들어올 때마다 이 함수를 불러, 중간으로 고정하면 타이틀에서 고른 난이도가 덮이기 때문입니다.
    /// </remarks>
    public bool StartNewGame()
    {
        return StartNewGame(GameDifficultyBalance.Current);
    }

    /// <summary>고른 난이도로 새 게임 데이터를 만듭니다. 난이도는 첫 체크포인트 저장에 함께 들어갑니다.</summary>
    public bool StartNewGame(GameDifficulty difficulty)
    {
        if (GameDataManager.Instance == null)
        {
            Debug.LogWarning("[MainSceneSaveManager] GameDataManager.Instance is null.");
            return false;
        }

        SaveData newGameSaveData = CreateNewGameSaveData();
        if (newGameSaveData == null)
        {
            return false;
        }

        newGameSaveData.difficulty = difficulty;

        // 일시정지 메뉴로 타이틀에 돌아온 경우 이전 판의 저장 밖 값(날짜·분대·필드 결과 등)이 남지 않게 먼저 비웁니다.
        GameDataManager.Instance.ResetSessionState();
        GameDataManager.Instance.InitializeCharacters(defaultSaveData.CreateStartingCharacterSnapshots());
        GameDataManager.Instance.ApplySaveData(newGameSaveData);
        // 새 게임으로 셸터에 처음 배치된 상태를 첫 게임오버 복구 지점으로 저장합니다.
        GameDataManager.Instance.RequestCheckpointAutoSave();

        // Temporary test-scene sync. Scene managers should copy from GameDataManager on scene load later.
        if (ShelterSceneDataManager.Instance != null)
        {
            ShelterSceneDataManager.Instance.InitializeFromGameData();
        }

        return true;
    }

    public void OnStartNewGameButtonClicked()
    {
        StartNewGameAndEnterShelter(GameDifficulty.Normal);
    }

    /// <summary>고른 난이도로 새 게임 데이터를 만들고 셸터로 이동합니다. 타이틀의 난이도 선택 팝업이 부릅니다.</summary>
    public void StartNewGameAndEnterShelter(GameDifficulty difficulty)
    {
        if (StartNewGame(difficulty))
        {
            SceneTransitionController.LoadScene(ShelterSceneName);
            return;
        }

        // 타이틀 씬을 에디터에서 단독 실행하면 GameManager(부트스트랩 씬)가 없어 새 게임을 만들 수 없습니다.
        // 버튼이 아무 반응 없이 멈추지 않도록 부트스트랩부터 시작합니다.
        if (GameManager.Instance == null && Application.CanStreamedLevelBeLoaded(DemoTitleReset.BootSceneName))
        {
            Debug.LogWarning("[MainSceneSaveManager] GameManager가 없어 부트스트랩 씬부터 시작합니다.", this);
            SceneTransitionController.LoadScene(DemoTitleReset.BootSceneName);
        }
    }

    public SaveData CreateNewGameSaveData()
    {
        if (defaultSaveData == null)
        {
            Debug.LogWarning("[MainSceneSaveManager] DefaultSaveData is not assigned.", this);
            return null;
        }

        return defaultSaveData.CreateSaveData();
    }

    private bool TryRejectDuplicate()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return true;
        }

        return false;
    }
}
