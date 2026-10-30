using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using VInspector;

/// <summary>
/// 방어전 씬의 데이터 매니저입니다. 전투 씬 공통 데이터에 더해 방어전 전용 입장 데이터·진행 데이터·결과를 소유합니다.
/// </summary>
/// <remarks>
/// 스쿼드 입장, 처치 집계, 전멸 게임오버, 귀환 정산, GameDataManager 반영은 부모 <see cref="CombatSceneDataManager"/>가 맡습니다.
/// 이 클래스는 웨이브 진행과 Scramble 시설 값처럼 방어전에만 있는 값을 다루고, 승리하면 임무 완료와 승리 보상을
/// 같은 정산 경로(<see cref="CombatSceneDataManager.RecordResource"/>)에 기록합니다.
/// <see cref="DefenseManager"/>는 진행 상태가 바뀔 때마다 이 매니저를 통해 데이터를 갱신합니다.
/// 예전에는 같은 씬의 FieldSceneDataManager에 공통 정산을 맡겼지만, 이제 방어전 씬에는 이 매니저 하나만 둡니다.
/// </remarks>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public sealed class DefenseSceneDataManager : CombatSceneDataManager
{
    /// <summary>방어전 승리 때 지급할 자원 한 종류입니다.</summary>
    [Serializable]
    public struct VictoryReward
    {
        [Tooltip("지급할 자원입니다. 셸터에 이미 정의된 자원 중에서 고릅니다.")]
        [Variants(
            ResourceIds.UpgradeMaterial,
            ResourceIds.CraftingMaterial,
            ResourceIds.TrapMaterial,
            ResourceIds.TrapUpgradeMaterial)]
        public string ResourceId;

        [Tooltip("지급할 수량입니다. 0이면 지급하지 않습니다.")]
        [Min(0)] public int Amount;

        [Tooltip("귀환 정산 화면의 자원 슬롯에 표시할 아이콘입니다. 비워 두면 아이콘 없이 수량만 표시합니다.")]
        public Texture2D Icon;
    }

    [Foldout("Victory Reward")]
    [Tooltip("마지막 웨이브를 막았을 때 전리품으로 기록할 자원 목록입니다. 귀환 정산에서 셸터 자원에 더해집니다.")]
    [SerializeField] private VictoryReward[] m_victoryRewards = Array.Empty<VictoryReward>();

    [Foldout("Defense Schedule")]
    [Tooltip("방어전 회차별로 쓸 방어전 SO 표입니다. 입장할 때 회차(클리어 횟수 + 1)로 이번 방어전 구성을 고릅니다.")]
    [SerializeField] private DefenseScheduleSO m_schedule;

    [Tooltip("0 이상이면 클리어 횟수 대신 이 회차로 방어전 구성을 고릅니다. -1이면 GameDataManager의 다음 회차를 씁니다. " +
             "셸터를 거치지 않고 이 씬만 실행해 특정 회차를 확인할 때 씁니다. 0은 1회차로 봅니다.")]
    [Min(-1)]
    [SerializeField] private int m_debugRoundOverride = -1;

    [Foldout("Trap Upgrade")]
    [Tooltip("셸터 Scramble 업그레이드 레벨별로 함정을 얼마나 강화할지 적은 표입니다. 비워 두면 함정은 프리팹 기본값 그대로입니다.")]
    [SerializeField] private TrapUpgradeTableSO m_trapUpgradeTable;

    // 부모의 필드 런타임 상태(m_entryData 등)와 이름이 겹치지 않도록 defense 접두사를 붙입니다.
    // 실행 중에만 쓰는 값이고 Awake에서 비우므로 예전 이름의 저장값은 옮기지 않습니다.
    [Foldout("Defense Runtime State")]
    [ReadOnly][SerializeField] private DefenseEntryData m_defenseEntryData;

    [Foldout("Defense Runtime State")]
    [ReadOnly][SerializeField] private DefenseRuntimeData m_defenseRuntimeData = new();

    [Foldout("Defense Result State")]
    [ReadOnly][SerializeField] private DefenseResultData m_defenseFinalResult;

    /// <summary>현재 방어전 씬의 인스턴스입니다. 방어전 씬이 아니면 <c>null</c>입니다.</summary>
    public static new DefenseSceneDataManager Instance => CombatSceneDataManager.Instance as DefenseSceneDataManager;

    /// <summary>방어전 결과가 승리 또는 패배로 확정되었을 때 한 번 발생합니다.</summary>
    public event Action<DefenseResultData> OnDefenseFinalized;

    /// <summary>방어전 입장 데이터가 적용되었는지 여부입니다.</summary>
    public bool HasDefenseEntryData => m_defenseEntryData != null;

    /// <summary>웨이브 설정까지 적용되어 진행 데이터를 받을 준비가 되었는지 여부입니다.</summary>
    public bool IsDefenseInitialized => m_defenseRuntimeData != null && m_defenseRuntimeData.Phase != DefensePhase.Uninitialized;

    /// <summary>방어전 결과가 확정되었는지 여부입니다.</summary>
    public bool IsDefenseFinalized => m_defenseFinalResult != null;

    /// <summary>현재 진행 단계입니다.</summary>
    public DefensePhase Phase => m_defenseRuntimeData?.Phase ?? DefensePhase.Uninitialized;

    /// <summary>
    /// 지정한 Scramble 업그레이드의 이번 방어전 레벨입니다. 입장 데이터가 없으면 0입니다.
    /// </summary>
    /// <remarks>
    /// 업그레이드 효과를 적용할 쪽이 읽을 값입니다. 출격 시점 값으로 고정되므로, 씬 도착 뒤 전역 데이터가 바뀌어도 이번 판에는 영향이 없습니다.
    /// </remarks>
    public int GetUpgradeLevel(ScrambleUpgradeType type)
    {
        return m_defenseEntryData?.Upgrades.GetLevel(type) ?? 0;
    }

    /// <summary>함정 업그레이드 표입니다. 없으면 null이고, 이때 함정은 강화되지 않습니다.</summary>
    public TrapUpgradeTableSO TrapUpgradeTable => m_trapUpgradeTable;

    /// <summary>이번 방어전 회차입니다. 입장 데이터가 아직 없으면 지금 만들어 정합니다.</summary>
    public int DefenseRound => EnsureDefenseEntryData().Round;

    /// <summary>
    /// 이번 방어전 구성입니다. 입장할 때 회차 표에서 골라 고정합니다.
    /// </summary>
    /// <remarks>
    /// 입장 데이터가 아직 없으면 지금 만들어 정합니다. 회차 표가 비었거나 그 칸이 비었으면 null입니다.
    /// 출격 시점 값으로 고정되므로, 씬 도착 뒤 클리어 횟수가 바뀌어도 이번 판 구성은 바뀌지 않습니다.
    /// </remarks>
    public DefenseStageSO DefenseStage => EnsureDefenseEntryData().Stage;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (m_victoryRewards == null)
        {
            return;
        }

        for (int i = 0; i < m_victoryRewards.Length; i++)
        {
            m_victoryRewards[i].Amount = Mathf.Max(0, m_victoryRewards[i].Amount);
        }
    }
#endif

    protected override void Awake()
    {
        base.Awake();

        // 부모가 중복으로 판단해 이 컴포넌트를 지웠으면 더 진행하지 않습니다.
        if (CombatSceneDataManager.Instance != this)
        {
            return;
        }

        // 인스펙터 표시용 직렬화 필드라 Unity가 빈 객체를 채워 넣습니다. 그대로 두면 입장 데이터가 이미
        // 있는 것으로 보여 GameDataManager 값을 읽지 않고, 결과도 확정된 것으로 보이므로 시작 전에 비웁니다.
        m_defenseEntryData = null;
        m_defenseFinalResult = null;
    }

    protected override void OnEnable()
    {
        base.OnEnable();

        // 거점 파괴와 스쿼드 전멸은 모두 RequestGameOver로 모이므로 이 알림 하나로 패배를 확정합니다.
        OnGameOverRequested -= HandleGameOverRequested;
        OnGameOverRequested += HandleGameOverRequested;
    }

    protected override void OnDisable()
    {
        OnGameOverRequested -= HandleGameOverRequested;
        base.OnDisable();
    }

    /// <summary>공통 입장 데이터를 준비한 뒤, 외부 입장 데이터가 없으면 GameDataManager 값으로 방어전 입장 데이터를 만듭니다.</summary>
    protected override void Start()
    {
        base.Start();

        if (!HasDefenseEntryData)
        {
            InitializeEntry(CreateSceneEntryData());
        }
    }

    protected override void Update()
    {
        base.Update();
        m_defenseRuntimeData?.AddElapsedTime(Time.deltaTime);
    }

    /// <summary>방어전 입장 데이터를 복제해 적용합니다.</summary>
    /// <remarks>외부에서 넘긴 입장 데이터에 방어전 구성이 없으면 그 회차로 회차 표에서 채웁니다.</remarks>
    public void InitializeEntry(DefenseEntryData entryData)
    {
        DefenseEntryData entry = entryData?.Clone() ?? CreateEmptyEntryData();
        if (entry.Stage == null)
        {
            entry = new DefenseEntryData(entry.StageId, entry.Upgrades, entry.Round, ResolveScheduledStage(entry.Round));
        }

        m_defenseEntryData = entry;
    }

    /// <summary>입장 데이터가 없으면 GameDataManager 값으로 만들고, 있는 입장 데이터를 돌려줍니다.</summary>
    private DefenseEntryData EnsureDefenseEntryData()
    {
        if (!HasDefenseEntryData)
        {
            InitializeEntry(CreateSceneEntryData());
        }

        return m_defenseEntryData;
    }

    /// <summary>
    /// <see cref="DefenseManager"/>의 웨이브 설정으로 진행 데이터를 시작 대기 상태로 초기화합니다.
    /// </summary>
    /// <remarks>방어전을 처음부터 다시 시작할 때도 이 경로로 기록을 비웁니다.</remarks>
    public void ConfigureWaves(int totalWaveCount)
    {
        EnsureDefenseEntryData();

        m_defenseFinalResult = null;
        m_defenseRuntimeData ??= new DefenseRuntimeData();
        m_defenseRuntimeData.Initialize(totalWaveCount);
    }

    /// <summary>웨이브 전투 구간 시작을 기록합니다.</summary>
    public void RecordWaveStarted(int waveNumber)
    {
        if (IsDefenseFinalized || m_defenseRuntimeData == null)
        {
            return;
        }

        if (!m_defenseRuntimeData.BeginWave(waveNumber, KillCount))
        {
            Debug.LogWarning($"[DefenseSceneDataManager] 웨이브 시작을 기록하지 못했습니다. phase={Phase}, wave={waveNumber}", this);
        }
    }

    /// <summary>웨이브를 막고 휴식 구간으로 넘어간 것을 기록합니다.</summary>
    public void RecordRestStarted()
    {
        if (IsDefenseFinalized || m_defenseRuntimeData == null)
        {
            return;
        }

        m_defenseRuntimeData.BeginRest(KillCount);
    }

    /// <summary>
    /// 마지막 웨이브를 막아 방어전 결과를 승리로 확정하고, 공통 정산에 임무 완료와 승리 보상을 기록합니다.
    /// </summary>
    /// <remarks>
    /// 보상은 <see cref="CombatSceneDataManager.RecordResource"/>로 기록합니다. 그래서 귀환 정산 화면의 자원 슬롯과
    /// <see cref="GameDataManager.ApplyFieldResult"/>의 자원 반영이 필드 획득 자원과 같은 경로를 탑니다.
    /// </remarks>
    public DefenseResultData CompleteVictory()
    {
        if (IsDefenseFinalized)
        {
            return m_defenseFinalResult.Clone();
        }

        if (m_defenseRuntimeData == null || !m_defenseRuntimeData.CompleteVictory(KillCount))
        {
            Debug.LogWarning($"[DefenseSceneDataManager] 승리를 확정하지 못했습니다. phase={Phase}", this);
            return null;
        }

        // 클리어 횟수는 여기서 올리지 않습니다. 귀환 정산이 성공으로 반영될 때 올라가도록 표시만 남깁니다.
        // 승리 뒤 귀환하지 못하고 실패한 판을 클리어로 세지 않기 위해서입니다.
        GameDataManager.Instance?.MarkDefenseVictoryPending(m_defenseEntryData?.StageId);

        // 귀환 구역에 들어가 FinalizeField가 불릴 때 결과가 Success로 정산되도록 먼저 표시합니다.
        SetMissionCompleted(true);
        RecordVictoryRewards();

        return FinalizeDefense(DefenseOutcome.Victory);
    }

    /// <summary>현재 방어전 진행 데이터를 외부 변경으로부터 분리된 복사본으로 반환합니다.</summary>
    public DefenseRuntimeData CreateDefenseRuntimeSnapshot()
    {
        return m_defenseRuntimeData?.Clone();
    }

    /// <summary>현재 방어전 입장 데이터를 외부 변경으로부터 분리된 복사본으로 반환합니다.</summary>
    public DefenseEntryData CreateDefenseEntrySnapshot()
    {
        return m_defenseEntryData?.Clone();
    }

    /// <summary>확정된 방어전 결과의 복사본을 반환합니다. 확정 전이면 <c>null</c>입니다.</summary>
    public DefenseResultData CreateDefenseFinalResultSnapshot()
    {
        return m_defenseFinalResult?.Clone();
    }

    /// <summary>
    /// 거점 파괴나 스쿼드 전멸로 게임오버가 요청되면 방어전 결과를 패배로 확정합니다.
    /// </summary>
    /// <remarks>
    /// 패배 사유별 호출부(<see cref="DefenseEventHealth"/>, 스쿼드 전멸)는 이미
    /// <see cref="CombatSceneDataManager.RequestGameOver"/>로 모여 있으므로, 그 알림 하나만 구독합니다.
    /// </remarks>
    private void HandleGameOverRequested()
    {
        if (IsDefenseFinalized || m_defenseRuntimeData == null)
        {
            return;
        }

        if (m_defenseRuntimeData.CompleteDefeat(KillCount))
        {
            FinalizeDefense(DefenseOutcome.Defeat);
        }
    }

    private void RecordVictoryRewards()
    {
        if (m_victoryRewards == null)
        {
            return;
        }

        for (int i = 0; i < m_victoryRewards.Length; i++)
        {
            VictoryReward reward = m_victoryRewards[i];
            // RecordResource가 빈 ID와 0 이하 수량을 걸러 냅니다.
            RecordResource(reward.ResourceId, reward.Amount, reward.Icon);
        }
    }

    private DefenseResultData FinalizeDefense(DefenseOutcome outcome)
    {
        m_defenseFinalResult = new DefenseResultData(m_defenseEntryData, m_defenseRuntimeData, outcome);
        OnDefenseFinalized?.Invoke(m_defenseFinalResult.Clone());
        return m_defenseFinalResult.Clone();
    }

    /// <summary>GameDataManager의 Scramble 업그레이드 레벨, 다음 방어전 회차와 현재 씬 이름으로 입장 데이터를 만듭니다.</summary>
    private DefenseEntryData CreateSceneEntryData()
    {
        GameDataManager gameData = GameDataManager.Instance;
        if (gameData == null)
        {
            Debug.LogWarning("[DefenseSceneDataManager] GameDataManager가 없어 업그레이드를 모두 0레벨, 1회차로 시작합니다.");
            return CreateEmptyEntryData();
        }

        int round = ResolveRound(gameData.NextDefenseRound);
        return new DefenseEntryData(
            GetActiveSceneName(),
            DefenseUpgradeLevels.FromGameData(gameData),
            round,
            ResolveScheduledStage(round));
    }

    private DefenseEntryData CreateEmptyEntryData()
    {
        int round = ResolveRound(1);
        return new DefenseEntryData(GetActiveSceneName(), DefenseUpgradeLevels.None, round, ResolveScheduledStage(round));
    }

    /// <summary>디버그 회차가 지정돼 있으면 그 값을, 아니면 주어진 회차를 씁니다.</summary>
    private int ResolveRound(int round)
    {
        return m_debugRoundOverride >= 0 ? Mathf.Max(1, m_debugRoundOverride) : Mathf.Max(1, round);
    }

    /// <summary>회차 표에서 이 회차의 방어전 구성을 고릅니다. 표가 없으면 경고하고 null을 돌려줍니다.</summary>
    private DefenseStageSO ResolveScheduledStage(int round)
    {
        if (m_schedule == null)
        {
            Debug.LogWarning("[DefenseSceneDataManager] 방어전 회차 표(Defense Schedule)가 비어 있어 방어전 구성을 정하지 못했습니다.", this);
            return null;
        }

        DefenseStageSO stage = m_schedule.GetStage(round);
        if (stage == null)
        {
            Debug.LogWarning($"[DefenseSceneDataManager] 회차 표 '{m_schedule.name}'의 {round}회차 칸이 비어 있습니다.", this);
        }

        return stage;
    }

    private static string GetActiveSceneName()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        return activeScene.IsValid() ? activeScene.name : string.Empty;
    }
}
