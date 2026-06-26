using UnityEngine;

//이건 TestScene용도 순서보장용도 나중에 삭제
[DefaultExecutionOrder(-100)]
public class ShelterSceneManager : MonoBehaviour
{
    public static ShelterSceneManager Instance { get; private set; }

    [Header("Managers")]
    [SerializeField] private ShelterDataManager m_ShelterDataManager;
    [SerializeField] private UIManager m_UIManager;
    [SerializeField] private MedicalManager m_MedicalManager;
    [SerializeField] private bool m_copyDataFromGameDataManagerOnAwake = true;


    //ToDo : 캐릭터 컨트롤러 매니저 크게 렙핑 필요
    [Header("Player Control")]
    [SerializeField] private PlayerMove m_PlayerMove;
    [SerializeField] private CameraLook m_CameraLook;
    [SerializeField] private bool m_autoFindReferences = true;

    public ShelterDataManager ShelterDataManager => m_ShelterDataManager;
    public UIManager UIManager => m_UIManager;
    public MedicalManager MedicalManager => m_MedicalManager;
    public bool IsPlayerControlLocked { get; private set; }

    private void Reset()
    {
        CacheSceneReferences();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (m_autoFindReferences)
            CacheSceneReferences();

        if (m_copyDataFromGameDataManagerOnAwake)
            CopyShelterDataFromGameDataManager();
    }

    private void OnEnable()
    {
        if (Instance != this)
            return;

        if (m_UIManager != null)
            m_UIManager.ActiveUIChanged += HandleActiveUIChanged;

        ApplyPlayerControlLock(m_UIManager != null && ShouldLockPlayerControl(m_UIManager.ActiveUI));
    }

    private void OnDisable()
    {
        if (m_UIManager != null)
            m_UIManager.ActiveUIChanged -= HandleActiveUIChanged;

        if (Instance == this)
            ApplyPlayerControlLock(false);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void CopyShelterDataFromGameDataManager()
    {
        if (m_ShelterDataManager == null)
            m_ShelterDataManager = ShelterDataManager.Instance;

        if (m_ShelterDataManager == null)
        {
            Debug.LogWarning("[ShelterSceneManager] ShelterDataManager is not available.", this);
            return;
        }

        m_ShelterDataManager.CopyFromDataManager();
    }

    private void HandleActiveUIChanged(ShelterUIType activeUI)
    {
        ApplyPlayerControlLock(ShouldLockPlayerControl(activeUI));
    }

    private bool ShouldLockPlayerControl(ShelterUIType activeUI)
    {
        return activeUI != ShelterUIType.None;
    }

    private void ApplyPlayerControlLock(bool locked)
    {
        IsPlayerControlLocked = locked;

        if (m_PlayerMove != null)
            m_PlayerMove.SetMoveLocked(locked);

        if (m_CameraLook != null)
            m_CameraLook.SetLookLocked(locked);
    }

    private void CacheSceneReferences()
    {
        if (m_ShelterDataManager == null)
            m_ShelterDataManager = FindFirstObjectByType<ShelterDataManager>();

        if (m_UIManager == null)
            m_UIManager = FindFirstObjectByType<UIManager>();

        if (m_PlayerMove == null)
            m_PlayerMove = FindFirstObjectByType<PlayerMove>();

        if (m_CameraLook == null)
            m_CameraLook = FindFirstObjectByType<CameraLook>();
    }
}
