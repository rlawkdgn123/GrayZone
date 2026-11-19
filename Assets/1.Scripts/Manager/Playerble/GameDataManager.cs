using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 씬 사이에서 유지되어야 하는 게임 런타임 정본을 평탄 필드로 소유하는 전역 데이터 매니저입니다.
/// 각 씬과 저장 시스템에는 소비 목적에 맞는 독립 패킷을 생성해 전달합니다.
/// </summary>
[DefaultExecutionOrder(-300)]
public class GameDataManager : MonoBehaviour
{
    private static readonly PlayerbleCharacterId[] FixedDefenseSquadCharacterIds =
    {
        PlayerbleCharacterId.Narin,
        PlayerbleCharacterId.Cheongsol,
        PlayerbleCharacterId.Seoha
    };

    /// <summary>현재 GameManager 자식에서 활성화된 전역 데이터 매니저 인스턴스입니다.</summary>
    public static GameDataManager Instance { get; private set; }

    // NOTE(TEST BOOTSTRAP): Shelter 단독 실행/반복 테스트를 위한 의도적인 전환 스위치입니다.
    // true면 MainSceneSaveManager가 Shelter 시작 시 DefaultSaveData를 다시 적용하므로,
    // 다른 Scene에서 돌아오기 전의 GameDataManager 데이터가 덮이는 것이 정상적인 테스트 동작입니다.
    // 실제 게임 흐름에서는 false로 두며, 기존 GameDataManager 데이터를 Shelter가 이어서 사용합니다.
    [Header("Shelter Bootstrap")]
    [Tooltip("테스트 전용: 활성화하면 Shelter 진입 시 DefaultSaveData가 GameDataManager를 의도적으로 덮어씁니다. 실제 플레이에서는 비활성화합니다.")]
    [SerializeField] private bool useDefaultSaveDataOnShelterStart;

    [Header("Progress")]
    [Tooltip("마지막으로 진행한 스테이지의 영속 ID입니다.")]
    [SerializeField] private string lastStageId = string.Empty;
    [Tooltip("현재 셸터 안정도입니다. 0~100 범위로 유지됩니다.")]
    [Range(0, 100)][SerializeField] private int shelterStability = 100;
    [Tooltip("현재 셸터 진행 일차입니다. 1 이상으로 유지됩니다.")]
    [Min(1)][SerializeField] private int currentDay = 1;
    [Header("Resource Shortage Penalty")]
    [Min(0)][SerializeField] private int foodShortageHpDecrease = 10;
    [SerializeField] private bool foodShortagePenaltyActive;
    [SerializeField] private bool fuelShortagePenaltyActive;
    [Header("Resources")]
    [Tooltip("자원 종류별 현재 보유량입니다. 같은 종류는 런타임에 하나로 정규화됩니다.")]
    [SerializeField] private List<ResourceAmountState> resourceAmounts = new();
    [SerializeField] private List<ItemStorageEntry> itemStorageEntries = new();

    [Header("Characters & Equipment")]
    [Tooltip("Field와 Shelter가 공통으로 복사해 사용하는 캐릭터 스냅샷 정본입니다.")]
    [FormerlySerializedAs("ownedCharacters")]
    [SerializeField] private List<CharacterSnapshotData> characters = new();

    [Header("Shelter")]
    [Tooltip("게임오버 후 셸터로 복귀할 현재 체크포인트 ID입니다.")]
    [SerializeField] private ShelterCheckpointId shelterCheckpointId = ShelterCheckpointId.BeforeDefense1;
    [Tooltip("셸터 씬이 마지막으로 동기화한 안내 진행 단계입니다.")]
    [SerializeField] private ShelterFlowState shelterFlowState = ShelterFlowState.NotStarted;

    /// <summary>다음 셸터 도착 때 Auto 슬롯에 체크포인트 저장을 해야 하는지 여부입니다. 저장 파일에는 넣지 않습니다.</summary>
    [System.NonSerialized] private bool checkpointAutoSavePending;
    [Tooltip("현재 출전 대상으로 선택된 캐릭터 런타임 ID 목록입니다. 최대 3명입니다.")]
    [FormerlySerializedAs("battleSquadNpcDefinitionIds")]
    [FormerlySerializedAs("playableSquadDefinitionIds")]
    [SerializeField] private List<string> playableSquadRuntimeIds = new();
    [Tooltip("셸터 시설에 배치된 캐릭터의 셸터 전용 상태입니다.")]
    [SerializeField] private List<ShelterCharacterAssignmentData> shelterCharacterAssignments = new();
    [Tooltip("시설별 해금 여부와 업그레이드 단계입니다.")]
    [SerializeField] private List<FacilityRuntimeState> facilityStates = new();
    [SerializeField] private ManufacturingRuntimeData manufacturing = new();

    [Header("필드 배치 업그레이드 레벨")]
    [Min(0)][SerializeField] private int trapUpgradeLevel;
    [Min(0)][SerializeField] private int spikeUpgradeLevel;
    [Min(0)][SerializeField] private int explosiveUpgradeLevel;
    [Min(0)][SerializeField] private int shooterUpgradeLevel;
    [Min(0)][SerializeField] private int wireUpgradeLevel;

    [Header("Last Field Settlement")]
    [Tooltip("마지막으로 정산 반영이 완료된 필드 ID이며 중복 반영 방지 키로 사용합니다.")]
    [FormerlySerializedAs("lastSettledBattleId")]
    [SerializeField] private string lastSettledFieldId = string.Empty;
    [Tooltip("최근 필드가 진행된 스테이지 ID입니다.")]
    [FormerlySerializedAs("lastBattleStageId")]
    [SerializeField] private string lastFieldStageId = string.Empty;
    [Tooltip("최근 필드의 최종 성공, 실패 또는 철수 결과입니다.")]
    [FormerlySerializedAs("lastBattleOutcome")]
    [SerializeField] private FieldOutcome lastFieldOutcome;
    [Tooltip("최근 필드가 종료된 직접적인 사유입니다.")]
    [FormerlySerializedAs("lastBattleEndReason")]
    [SerializeField] private FieldEndReason lastFieldEndReason;
    [Tooltip("최근 필드 종료 전에 임무 목표를 달성했는지 여부입니다.")]
    [FormerlySerializedAs("lastBattleMissionCompleted")]
    [SerializeField] private bool lastFieldMissionCompleted;
    [Tooltip("최근 필드의 총 경과 시간입니다. 단위는 초입니다.")]
    [FormerlySerializedAs("lastBattleElapsedSeconds")]
    [Min(0.0f)][SerializeField] private float lastFieldElapsedSeconds;
    [Tooltip("최근 필드에서 스쿼드 전체가 확정한 적 처치 수입니다.")]
    [FormerlySerializedAs("lastBattleTotalKillCount")]
    [Min(0)][SerializeField] private int lastFieldKillCount;
    [Tooltip("최근 필드의 캐릭터별 최종 상태와 처치 결과입니다.")]
    [FormerlySerializedAs("lastBattleMemberResults")]
    [SerializeField] private List<FieldMemberResultData> lastFieldMemberResults = new();
    [Tooltip("최근 필드에서 획득한 자원별 수량입니다.")]
    [FormerlySerializedAs("lastBattleAcquiredResources")]
    [SerializeField] private List<FieldResourceAmountData> lastFieldAcquiredResources = new();

    private const int MaxFieldKillHistory = 20;
    [SerializeField] private int totalFieldKillCount;
    [SerializeField] private List<int> fieldKillHistory = new();

    /// <summary>방어전을 클리어하고 귀환까지 마친 횟수입니다. 다음 방어전 회차를 정하는 기준이며 저장됩니다.</summary>
    [Min(0)][SerializeField] private int defenseClearCount;

    /// <summary>새 게임을 시작할 때 고른 난이도입니다. 저장됩니다.</summary>
    [Tooltip("현재 판의 난이도입니다. 어려움이면 적이 어려움용 밸런스 SO를 씁니다.")]
    [SerializeField] private GameDifficulty difficulty = GameDifficulty.Normal;

    /// <summary>
    /// 방어전에서 승리했지만 아직 귀환 정산이 반영되지 않은 스테이지 ID입니다. 없으면 빈 문자열입니다.
    /// </summary>
    /// <remarks>
    /// 필드 결과의 임무 완료 표시는 일반 필드 임무에서도 쓰여서, 결과만 보고는 방어전인지 알 수 없습니다.
    /// 그래서 방어전 쪽이 승리 시점에 이 값을 남기고, 정산이 성공으로 반영될 때 클리어 횟수를 올립니다.
    /// 저장하지 않습니다. 정산 전에 게임을 끄면 그 판은 클리어로 세지 않습니다.
    /// </remarks>
    private string pendingDefenseVictoryStageId = string.Empty;

    private ShelterSceneDataManager activeShelterSceneDataManager;

    /// <summary>현재 보유한 전체 캐릭터 수입니다.</summary>
    public int CharacterCount => characters?.Count ?? 0;
    public IReadOnlyList<CharacterSnapshotData> Characters => characters;
    public IReadOnlyList<ItemStorageEntry> ItemStorageEntries => itemStorageEntries;

    /// <summary>
    /// Shelter 진입 시 테스트용 DefaultSaveData로 GameDataManager를 의도적으로 덮어쓸지 여부입니다.
    /// false인 실제 게임 흐름에서는 현재 GameDataManager 데이터를 그대로 유지합니다.
    /// </summary>
    public bool UseDefaultSaveDataOnShelterStart => useDefaultSaveDataOnShelterStart;

    /// <summary>Shelter 진입 시 테스트용 DefaultSaveData를 적용할지 설정합니다.</summary>
    public void SetUseDefaultSaveDataOnShelterStart(bool enabled)
    {
        useDefaultSaveDataOnShelterStart = enabled;
    }

    /// <summary>플레이어블 캐릭터와 비플레이어 NPC를 합한 전체 보유 수입니다.</summary>
    public int TotalOwnedCharacterCount => CharacterCount;

    /// <summary>현재 보유한 플레이어블 캐릭터 수입니다.</summary>
    public int PlayerbleCharacterCount => CharacterCount;

    /// <summary>현재 보유한 비플레이어 NPC 수입니다.</summary>
    public int NonPlayerbleNpcCount => 0;

    /// <summary>0~100 범위로 보정된 현재 셸터 안정도입니다.</summary>
    public int ShelterStability => Mathf.Clamp(shelterStability, 0, 100);

    /// <summary>현재 셸터 진행 일차입니다.</summary>
    public int CurrentDay => Mathf.Max(1, currentDay);

    /// <summary>마지막 셸터 동기화 시점의 안내 진행 단계입니다.</summary>
    public ShelterFlowState ShelterFlowState => shelterFlowState;

    /// <summary>게임오버 후 셸터로 복귀할 현재 체크포인트 ID입니다.</summary>
    public ShelterCheckpointId ShelterCheckpointId => shelterCheckpointId;

    /// <summary>다음 셸터 도착 때 체크포인트 Auto 저장이 예약되어 있는지 여부입니다.</summary>
    public bool IsCheckpointAutoSavePending => checkpointAutoSavePending;

    /// <summary>
    /// 다음 셸터 도착 때 현재 상태를 게임오버 복구용 Auto 슬롯에 저장하도록 예약합니다.
    /// 새 게임 시작과 방어전 클리어 귀환에서만 호출합니다.
    /// </summary>
    public void RequestCheckpointAutoSave()
    {
        checkpointAutoSavePending = true;
    }

    /// <summary>체크포인트 Auto 저장 예약을 지웁니다.</summary>
    public void ClearCheckpointAutoSaveRequest()
    {
        checkpointAutoSavePending = false;
    }

    /// <summary>게임오버 후 셸터로 복귀할 체크포인트 ID를 변경합니다.</summary>
    public void SetShelterCheckpointId(ShelterCheckpointId checkpointId)
    {
        shelterCheckpointId = checkpointId;
    }

    public bool FoodShortagePenaltyActive => foodShortagePenaltyActive;

    public bool FuelShortagePenaltyActive => fuelShortagePenaltyActive;

    public int TrapUpgradeLevel => Mathf.Max(0, trapUpgradeLevel);

    public int SpikeUpgradeLevel => Mathf.Max(0, spikeUpgradeLevel);

    public int ExplosiveUpgradeLevel => Mathf.Max(0, explosiveUpgradeLevel);

    public int ShooterUpgradeLevel => Mathf.Max(0, shooterUpgradeLevel);

    public int WireUpgradeLevel => Mathf.Max(0, wireUpgradeLevel);

    /// <summary>현재 씬의 ShelterSceneDataManager가 등록되어 있는지 여부입니다.</summary>
    public bool HasActiveShelterSceneDataManager => activeShelterSceneDataManager != null;

    /// <summary>현재 세션 또는 저장 데이터에 반영된 최근 필드 결과가 있는지 여부입니다.</summary>
    public bool HasLastFieldResult => !string.IsNullOrWhiteSpace(lastSettledFieldId);

    public void SetTrapUpgradeLevel(int level)
    {
        trapUpgradeLevel = Mathf.Max(0, level);
    }

    public void SetSpikeUpgradeLevel(int level)
    {
        spikeUpgradeLevel = Mathf.Max(0, level);
    }

    public void SetExplosiveUpgradeLevel(int level)
    {
        explosiveUpgradeLevel = Mathf.Max(0, level);
    }

    public void SetShooterUpgradeLevel(int level)
    {
        shooterUpgradeLevel = Mathf.Max(0, level);
    }

    public void SetWireUpgradeLevel(int level)
    {
        wireUpgradeLevel = Mathf.Max(0, level);
    }

    /// <summary>마지막으로 정산 반영이 완료된 필드 ID입니다.</summary>
    public string LastSettledFieldId => lastSettledFieldId ?? string.Empty;

    public int TotalFieldKillCount => Mathf.Max(0, totalFieldKillCount);
    public IReadOnlyList<int> FieldKillHistory => fieldKillHistory;

    /// <summary>방어전을 클리어하고 귀환까지 마친 횟수입니다.</summary>
    public int DefenseClearCount => Mathf.Max(0, defenseClearCount);

    /// <summary>다음에 진행할 방어전 회차입니다. 클리어 횟수 + 1이며, 실패하면 같은 회차를 다시 합니다.</summary>
    public int NextDefenseRound => DefenseClearCount + 1;

    /// <summary>
    /// 방어전 승리를 기록해 두고, 이어지는 귀환 정산이 성공이면 클리어 횟수를 올리게 합니다.
    /// </summary>
    /// <param name="stageId">승리한 방어전의 스테이지 ID(씬 이름)입니다.</param>
    /// <remarks>승리한 뒤 귀환하지 못하고 실패하면 클리어로 세지 않습니다.</remarks>
    public void MarkDefenseVictoryPending(string stageId)
    {
        pendingDefenseVictoryStageId = stageId?.Trim() ?? string.Empty;
    }

    /// <summary>현재 판의 난이도입니다.</summary>
    public GameDifficulty Difficulty => difficulty;

    /// <summary>현재 판의 난이도를 정합니다. 적은 생성될 때 난이도를 읽으므로 전투 씬에 들어가기 전에 정해야 합니다.</summary>
    public void SetDifficulty(GameDifficulty value)
    {
        difficulty = value;
    }

    /// <summary>검증용으로 방어전 클리어 횟수를 직접 정합니다. 0보다 작으면 0으로 봅니다.</summary>
    /// <param name="count">설정할 클리어 횟수입니다.</param>
    public void SetDefenseClearCount(int count)
    {
        defenseClearCount = Mathf.Max(0, count);
        shelterCheckpointId = defenseClearCount > 0
            ? ShelterCheckpointId.AfterDefense1Clear
            : ShelterCheckpointId.BeforeDefense1;
    }

    /// <summary>중복 인스턴스를 거부하고 모든 평탄 정본 필드를 정규화합니다.</summary>
    private void Awake()
    {
        if (TryRejectDuplicateOrInvalidRoot())
        {
            return;
        }

        EnsureRuntimeState();
        Instance = this;
    }

    /// <summary>현재 인스턴스가 파괴될 때 전역 접근자를 해제합니다.</summary>
    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>현재 셸터 씬의 작업 데이터 매니저를 동기화 대상으로 등록합니다.</summary>
    public void RegisterShelterSceneDataManager(ShelterSceneDataManager shelterDataManager)
    {
        if (shelterDataManager != null)
        {
            activeShelterSceneDataManager = shelterDataManager;
        }
    }

    /// <summary>지정한 셸터 씬 데이터 매니저가 현재 등록 대상이면 연결을 해제합니다.</summary>
    public void UnregisterShelterSceneDataManager(ShelterSceneDataManager shelterDataManager)
    {
        if (activeShelterSceneDataManager == shelterDataManager)
        {
            activeShelterSceneDataManager = null;
        }
    }

    /// <summary>현재 등록된 셸터 씬 작업 패킷을 전역 평탄 정본에 반영합니다.</summary>
    public bool SyncFromShelter()
    {
        if (activeShelterSceneDataManager == null)
        {
            Debug.LogWarning("[GameDataManager] Active ShelterSceneDataManager is not registered.");
            return false;
        }

        return SyncFromShelter(activeShelterSceneDataManager);
    }

    /// <summary>지정한 셸터 씬 데이터 매니저의 작업 패킷을 전역 평탄 정본에 반영합니다.</summary>
    public bool SyncFromShelter(ShelterSceneDataManager shelterDataManager)
    {
        if (shelterDataManager == null)
        {
            Debug.LogWarning("[GameDataManager] ShelterSceneDataManager is null.");
            return false;
        }

        ApplyShelterRuntimeSnapshot(shelterDataManager.CreateRuntimeSnapshot());
        return true;
    }

    /// <summary>
    /// 지정한 셸터 작업본의 자원 수량만 전역 정본에 반영합니다.
    /// </summary>
    /// <remarks>
    /// 방어전 씬은 함정 설치·수리에 <see cref="ShelterSceneDataManager.Storage"/>를 쓰지만,
    /// 캐릭터·시설·제조 상태까지 소유하지는 않습니다. 그래서 방어전 귀환 정산 직전에는
    /// <see cref="SyncFromShelter(ShelterSceneDataManager)"/> 대신 이 경로로 소비한 자원만 반영합니다.
    /// </remarks>
    public bool SyncResourcesFromShelter(ShelterSceneDataManager shelterDataManager)
    {
        if (shelterDataManager == null)
        {
            Debug.LogWarning("[GameDataManager] 자원을 동기화할 ShelterSceneDataManager가 없습니다.");
            return false;
        }

        ShelterRuntimeData snapshot = shelterDataManager.CreateRuntimeSnapshot();
        snapshot.EnsureRuntimeContainers();

        resourceAmounts = new List<ResourceAmountState>();
        foreach (KeyValuePair<string, int> resource in snapshot.Resources.Amounts)
        {
            SetResourceAmount(resource.Key, resource.Value);
        }

        NormalizeResources();
        return true;
    }

    /// <summary>FieldEntryData와 동일한 역할의 셸터 씬 진입 패킷을 생성합니다.</summary>
    public ShelterEntryData CreateShelterEntryData()
    {
        return new ShelterEntryData(CreateShelterRuntimeSnapshot());
    }

    /// <summary>현재 평탄 정본에서 셸터 씬이 사용할 독립 작업 패킷을 생성합니다.</summary>
    public ShelterRuntimeData CreateShelterRuntimeSnapshot()
    {
        EnsureRuntimeState();
        ShelterRuntimeData packet = new ShelterRuntimeData();
        packet.SetShelterStability(shelterStability);
        packet.SetFlowState(shelterFlowState);
        packet.SetResourceShortagePenaltyState(
            foodShortagePenaltyActive,
            fuelShortagePenaltyActive);
        packet.ApplySavedState(currentDay, playableSquadRuntimeIds, facilityStates);
        packet.Manufacturing.CopyFrom(manufacturing);
        packet.SetItemStorageEntries(itemStorageEntries);

        for (int i = 0; i < resourceAmounts.Count; i++)
        {
            ResourceAmountState resource = resourceAmounts[i];
            if (resource != null)
            {
                packet.Resources.SetAmount(resource.ResourceId, resource.Amount);
            }
        }

        for (int i = 0; i < characters.Count; i++)
        {
            CharacterSnapshotData snapshot = characters[i];
            if (snapshot != null)
            {
                packet.TryAddCharacter(snapshot.Clone(), out _);
            }

            // 시설 도우미 배치 개념이 빠져 셸터 배치 정보를 씬 사이에 이어 주지 않습니다.
            // 의료 환자 배치는 셸터 안에서 배치 → 치료로 끝나는 임시 상태라, 셸터를 다시 열면 배치 없음으로 시작합니다.
            // 다시 필요해지면 아래 복원과 ApplyShelterRuntimeSnapshot의 기록을 함께 되살립니다.
            /*
            if (snapshot != null && packet.TryAddCharacter(snapshot.Clone(), out ShelterMemberRuntimeData member))
            {
                ShelterCharacterAssignmentData assignment = FindShelterAssignment(member.RuntimeId);
                if (assignment != null && assignment.IsAssigned)
                    member.AssignToFacility(assignment.FacilityId, assignment.RoomId, assignment.Kind);
            }
            */
        }

        return packet;
    }

    /// <summary>셸터 씬 작업 패킷을 깊은 복사해 전역 평탄 정본에 반영합니다.</summary>
    public void ApplyShelterRuntimeSnapshot(ShelterRuntimeData packet)
    {
        if (packet == null)
        {
            Debug.LogWarning("[GameDataManager] ShelterRuntimeData is null.");
            return;
        }

        packet.EnsureRuntimeContainers();
        shelterStability = packet.ShelterStability;
        currentDay = packet.CurrentDay;
        shelterFlowState = packet.FlowState;
        foodShortagePenaltyActive = packet.FoodShortagePenaltyActive;
        fuelShortagePenaltyActive = packet.FuelShortagePenaltyActive;
        playableSquadRuntimeIds = new List<string>(packet.FieldSquadRuntimeIds);
        facilityStates = CloneFacilityStates(packet.FacilityStates);
        manufacturing ??= new ManufacturingRuntimeData();
        manufacturing.CopyFrom(packet.Manufacturing);
        itemStorageEntries = CloneItemStorageEntries(packet.ItemStorageEntries);

        resourceAmounts = new List<ResourceAmountState>();
        foreach (KeyValuePair<string, int> resource in packet.Resources.Amounts)
        {
            resourceAmounts.Add(new ResourceAmountState(resource.Key, resource.Value));
        }

        characters = new List<CharacterSnapshotData>();
        shelterCharacterAssignments = new List<ShelterCharacterAssignmentData>();
        for (int i = 0; i < packet.Characters.Count; i++)
        {
            ShelterMemberRuntimeData character = packet.Characters[i];
            if (character != null)
            {
                characters.Add(character.CreateSnapshot());
                // 셸터 배치 정보는 더 이상 전역 정본에 기록하지 않습니다(CreateShelterRuntimeSnapshot 주석 참고).
                /*
                if (character.IsAssignedToFacility)
                    shelterCharacterAssignments.Add(new ShelterCharacterAssignmentData(character));
                */
            }
        }

        EnsureRuntimeState();
    }

    /// <summary>지정한 영속 캐릭터 ID의 캐릭터·총기 스냅샷을 깊은 복사하여 반환합니다.</summary>
    public bool TryGetCharacterSnapshot(string runtimeId, out CharacterSnapshotData snapshot)
    {
        snapshot = null;
        if (!TryGetCharacterIndex(runtimeId, out int index))
        {
            return false;
        }

        snapshot = characters[index].Clone();
        return true;
    }

    /// <summary>셸터 또는 필드 씬에서 받은 캐릭터·총기 스냅샷을 전역 캐릭터 정본에 반영합니다.</summary>
    public bool TryApplyCharacterSnapshot(CharacterSnapshotData snapshot)
    {
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.DefinitionId))
        {
            return false;
        }

        string id = string.IsNullOrWhiteSpace(snapshot.RuntimeId) ? snapshot.DefinitionId : snapshot.RuntimeId;
        if (!TryGetCharacterIndex(id, out int index))
        {
            Debug.LogWarning($"[GameDataManager] 캐릭터 스냅샷을 반영할 캐릭터를 찾지 못했습니다. definitionId={snapshot.DefinitionId}");
            return false;
        }

        CharacterSnapshotData normalizedSnapshot = snapshot.Clone();
        if (string.IsNullOrWhiteSpace(normalizedSnapshot.RuntimeId))
        {
            normalizedSnapshot.SetSceneIdentity(
                normalizedSnapshot.DefinitionId,
                normalizedSnapshot.CharacterId,
                normalizedSnapshot.DisplayName);
        }

        characters[index] = normalizedSnapshot;
        return true;
    }

    /// <summary>
    /// 필드 씬에서 구성된 캐릭터 스냅샷을 전역 정본에 바인딩합니다.
    /// 이미 같은 런타임 ID 또는 정의 ID가 있으면 갱신하고, 아직 없으면 새 보유 캐릭터로 등록합니다.
    /// </summary>
    public bool TryBindFieldCharacterSnapshot(CharacterSnapshotData snapshot)
    {
        if (snapshot == null)
        {
            Debug.LogWarning("[GameDataManager] 필드 캐릭터 바인딩에는 유효한 스냅샷이 필요합니다.");
            return false;
        }

        EnsureRuntimeState();
        CharacterSnapshotData normalizedSnapshot = snapshot.Clone();
        string runtimeId = string.IsNullOrWhiteSpace(normalizedSnapshot.RuntimeId)
            ? normalizedSnapshot.DefinitionId
            : normalizedSnapshot.RuntimeId;
        if (string.IsNullOrWhiteSpace(runtimeId))
        {
            Debug.LogWarning("[GameDataManager] 필드 캐릭터 바인딩에는 RuntimeId 또는 DefinitionId가 필요합니다.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(normalizedSnapshot.DefinitionId))
        {
            // TODO(Field 테스트): 정식 씬/세이브 진입에서는 안정적인 DefinitionId를 주입하고 이 fallback 경고를 제거해야 합니다.
            Debug.LogWarning(
                $"[GameDataManager] DefinitionId가 없는 필드 캐릭터를 RuntimeId로 임시 바인딩합니다. runtimeId={runtimeId}");
            normalizedSnapshot.SetPersistentIdentity(runtimeId, normalizedSnapshot.NpcType);
        }

        if (string.IsNullOrWhiteSpace(normalizedSnapshot.RuntimeId))
        {
            normalizedSnapshot.SetSceneIdentity(
                runtimeId,
                normalizedSnapshot.CharacterId,
                normalizedSnapshot.DisplayName);
        }

        if (TryGetCharacterIndex(normalizedSnapshot.DefinitionId, out int index)
            || TryGetCharacterIndex(runtimeId, out index))
        {
            characters[index] = normalizedSnapshot;
            return true;
        }

        characters.Add(normalizedSnapshot);
        return true;
    }

    /// <summary>방어전에 출격할 나린, 청솔, 서하를 고정 스쿼드로 설정합니다.</summary>
    public bool TrySetFixedDefenseSquad()
    {
        EnsureRuntimeState();
        List<string> fixedSquadRuntimeIds = new(FixedDefenseSquadCharacterIds.Length);

        for (int i = 0; i < FixedDefenseSquadCharacterIds.Length; i++)
        {
            PlayerbleCharacterId characterId = FixedDefenseSquadCharacterIds[i];
            CharacterSnapshotData character = characters.Find(
                candidate => candidate != null && candidate.CharacterId == characterId);
            if (character == null)
            {
                Debug.LogWarning(
                    $"[GameDataManager] 방어전 고정 스쿼드 캐릭터를 찾지 못했습니다. characterId={characterId}");
                return false;
            }

            // 전투 이탈한 뒤 셸터에서 살리지 않은 대원은 출격하지 않습니다.
            if (character.IsCombatOut || character.CurrentHp <= 0)
            {
                continue;
            }

            fixedSquadRuntimeIds.Add(character.RuntimeId);
        }

        if (fixedSquadRuntimeIds.Count == 0)
        {
            Debug.LogWarning("[GameDataManager] 출격할 수 있는 대원이 없습니다. 전투 이탈한 대원을 먼저 회복시켜야 합니다.");
            return false;
        }

        playableSquadRuntimeIds = fixedSquadRuntimeIds;
        return true;
    }

    /// <summary>현재 평탄 정본에서 필드 씬이 필요로 하는 출전 패킷을 생성합니다.</summary>
    public FieldEntryData CreateFieldEntryData(string fieldId, string stageId, int randomSeed)
    {
        if (activeShelterSceneDataManager != null)
        {
            SyncFromShelter(activeShelterSceneDataManager);
        }

        EnsureRuntimeState();
        string resolvedFieldId = string.IsNullOrWhiteSpace(fieldId)
            ? Guid.NewGuid().ToString("N")
            : fieldId.Trim();
        string resolvedStageId = string.IsNullOrWhiteSpace(stageId)
            ? lastStageId
            : stageId.Trim();

        FieldEntryData entryData = new FieldEntryData(
            resolvedFieldId,
            resolvedStageId,
            randomSeed,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        for (int i = 0; i < resourceAmounts.Count; i++)
        {
            ResourceAmountState resource = resourceAmounts[i];
            if (resource != null)
            {
                entryData.AddStartingResource(resource.ResourceId, resource.Amount);
            }
        }

        for (int i = 0; i < playableSquadRuntimeIds.Count; i++)
        {
            string runtimeId = playableSquadRuntimeIds[i];
            if (!TryGetCharacterIndex(runtimeId, out int characterIndex))
            {
                Debug.LogWarning($"[GameDataManager] 출전 스쿼드 캐릭터를 찾지 못했습니다. runtimeId={runtimeId}");
                continue;
            }

            CharacterSnapshotData characterSnapshot = characters[characterIndex].Clone();
            characterSnapshot.SetPlayerSquadMember(i == 0);
            int temporaryHpPenalty = FoodShortagePenaltyActive
                ? foodShortageHpDecrease
                : 0;
            entryData.AddMember(new FieldMemberEntryData(
                characterSnapshot,
                temporaryHpPenalty));
        }

        return entryData;
    }

    /// <summary>
    /// 확정된 필드 결과를 전역 평탄 정본에 한 번만 반영합니다.
    /// 성공 또는 탈출일 때만 획득 자원을 보존하며, 캐릭터 최종 상태는 모든 결과에 반영합니다.
    /// </summary>
    public bool ApplyFieldResult(FieldResultData resultData)
    {
        if (resultData == null || string.IsNullOrWhiteSpace(resultData.FieldId))
        {
            Debug.LogWarning("[GameDataManager] 유효한 FieldResultData가 필요합니다.");
            return false;
        }

        string fieldId = resultData.FieldId.Trim();
        if (fieldId == LastSettledFieldId)
        {
            return true;
        }

        if (ShouldApplyAcquiredResources(resultData.Outcome))
        {
            for (int i = 0; i < resultData.AcquiredResources.Count; i++)
            {
                FieldResourceAmountData resource = resultData.AcquiredResources[i];
                if (resource != null)
                {
                    AddResource(resource.ResourceId, resource.Amount);
                }
            }
        }

        for (int i = 0; i < resultData.Members.Count; i++)
        {
            ApplyFieldMemberResult(resultData.Members[i]);
        }

        if (!string.IsNullOrWhiteSpace(resultData.StageId))
        {
            lastStageId = resultData.StageId.Trim();
        }

        ApplyLastFieldResult(resultData);
        RecordFieldKillHistory(resultData.TotalKillCount);
        ApplyPendingDefenseVictory(resultData);

        if (activeShelterSceneDataManager != null)
        {
            activeShelterSceneDataManager.ApplyRuntimeSnapshot(CreateShelterRuntimeSnapshot());
        }

        return true;
    }

    /// <summary>
    /// 기록해 둔 방어전 승리가 이번 정산에서 성공으로 끝났으면 클리어 횟수를 올리고, 기록을 비웁니다.
    /// </summary>
    /// <remarks>
    /// 스테이지 ID가 같고 결과가 성공이며 임무 완료일 때만 셉니다. 다른 스테이지의 정산이거나 실패면 세지 않고 기록만 지웁니다.
    /// </remarks>
    private void ApplyPendingDefenseVictory(FieldResultData resultData)
    {
        if (string.IsNullOrEmpty(pendingDefenseVictoryStageId))
        {
            return;
        }

        string stageId = resultData.StageId?.Trim() ?? string.Empty;
        if (resultData.Outcome == FieldOutcome.Success
            && resultData.MissionCompleted
            && string.Equals(stageId, pendingDefenseVictoryStageId, StringComparison.Ordinal))
        {
            defenseClearCount = DefenseClearCount + 1;
            if (DefenseClearCount > 0)
                shelterCheckpointId = ShelterCheckpointId.AfterDefense1Clear;

            // 클리어 귀환 직후 상태가 다음 게임오버의 복구 지점이 되도록 셸터 도착 때 저장합니다.
            RequestCheckpointAutoSave();
        }

        pendingDefenseVictoryStageId = string.Empty;
    }

    /// <summary>최근 필드 결과의 평탄 필드를 독립된 결과 패킷으로 조립해 반환합니다.</summary>
    public FieldResultData CreateLastFieldResultSnapshot()
    {
        if (!HasLastFieldResult)
        {
            return null;
        }

        return new FieldResultData(
            lastSettledFieldId,
            lastFieldStageId,
            lastFieldOutcome,
            lastFieldEndReason,
            lastFieldMissionCompleted,
            lastFieldElapsedSeconds,
            lastFieldKillCount,
            lastFieldMemberResults,
            lastFieldAcquiredResources);
    }

    /// <summary>현재 전역 정본에서 Auto 복구에 필요한 최소 상태만 저장 패킷으로 변환합니다.</summary>
    /// <remarks>활성 셸터 작업본은 호출 전에 <see cref="SyncFromShelter()"/>로 정본에 먼저 반영해야 합니다.</remarks>
    public SaveData CreateSaveData()
    {
        EnsureRuntimeState();
        SaveData saveData = new SaveData
        {
            shelterCheckpointId = ShelterCheckpointId,
            defenseClearCount = DefenseClearCount,
            difficulty = Difficulty,
            // 새 게임 첫 체크포인트는 인트로 컷신 재생 중에 저장되어 NotStarted로 남습니다.
            // 그대로 저장하면 복구할 때마다 컷신이 다시 나오므로, 컷신을 본 뒤의 첫 안내 단계로 기록합니다.
            shelterFlowState = ShelterFlowState == ShelterFlowState.NotStarted
                ? ShelterFlowState.GuideToManufacturing
                : ShelterFlowState,
            trapUpgradeLevel = TrapUpgradeLevel,
            spikeUpgradeLevel = SpikeUpgradeLevel,
            explosiveUpgradeLevel = ExplosiveUpgradeLevel,
            shooterUpgradeLevel = ShooterUpgradeLevel,
            wireUpgradeLevel = WireUpgradeLevel,
            facilities = CloneFacilityStates(facilityStates),
            items = CloneItemStorageEntries(itemStorageEntries),
            manufacturing = manufacturing.Clone()
        };

        for (int i = 0; i < resourceAmounts.Count; i++)
        {
            ResourceAmountState resource = resourceAmounts[i];
            if (resource == null)
            {
                continue;
            }

            saveData.resources.Add(new SaveData.ResourceAmountData
            {
                resourceId = resource.ResourceId,
                amount = resource.Amount
            });
        }

        for (int i = 0; i < characters.Count; i++)
        {
            CharacterSnapshotData snapshot = characters[i];
            if (snapshot == null || snapshot.CharacterId == PlayerbleCharacterId.Unknown)
            {
                continue;
            }

            saveData.characters.Add(new SaveData.CharacterStateSaveData
            {
                characterId = snapshot.CharacterId,
                currentHp = snapshot.CurrentHp,
                maxHp = snapshot.MaxHp,
                injurySeverityGauge = snapshot.InjurySeverityGauge,
                maxInjuryGauge = snapshot.MaxInjuryGauge,
                injuryState = snapshot.InjuryState,
                isDown = snapshot.IsDown,
                isCombatOut = snapshot.IsCombatOut
            });
        }

        return saveData;
    }

    /// <summary>불러온 저장 패킷의 영속값을 전역 평탄 정본에 적용합니다.</summary>
    public void ApplySaveData(SaveData saveData)
    {
        if (saveData == null)
        {
            Debug.LogWarning("[GameDataManager] SaveData is null.");
            return;
        }

        shelterCheckpointId = saveData.shelterCheckpointId;
        defenseClearCount = Mathf.Max(0, saveData.defenseClearCount);
        difficulty = saveData.difficulty;
        shelterFlowState = saveData.shelterFlowState;
        // 불러오기 전 판에서 정산 대기 중이던 승리가 불러온 진행 상태에 더해지지 않게 비웁니다.
        pendingDefenseVictoryStageId = string.Empty;
        // 불러온 상태가 곧 저장된 체크포인트이므로 이전 판의 저장 예약은 버립니다.
        checkpointAutoSavePending = false;
        SetTrapUpgradeLevel(saveData.trapUpgradeLevel);
        SetSpikeUpgradeLevel(saveData.spikeUpgradeLevel);
        SetExplosiveUpgradeLevel(saveData.explosiveUpgradeLevel);
        SetShooterUpgradeLevel(saveData.shooterUpgradeLevel);
        SetWireUpgradeLevel(saveData.wireUpgradeLevel);
        // 저장에 없는 시설은 셸터 진입 시 FacilityManager가 시설 기본값(해금 여부, 레벨 0)으로 만듭니다.
        facilityStates = CloneFacilityStates(saveData.facilities);
        ApplySavedResources(saveData.resources);
        // 자원만 되돌리고 제작 아이템을 남기면 "재료 복구 + 제작품 유지"가 되므로 함께 되돌립니다.
        itemStorageEntries = CloneItemStorageEntries(saveData.items);
        manufacturing = saveData.manufacturing != null
            ? saveData.manufacturing.Clone()
            : new ManufacturingRuntimeData();
        ApplySavedCharacterStates(saveData.characters);

        EnsureRuntimeState();
    }

    private void ApplyLastFieldResult(FieldResultData resultData)
    {
        lastSettledFieldId = resultData.FieldId.Trim();
        lastFieldStageId = resultData.StageId;
        lastFieldOutcome = resultData.Outcome;
        lastFieldEndReason = resultData.EndReason;
        lastFieldMissionCompleted = resultData.MissionCompleted;
        lastFieldElapsedSeconds = resultData.ElapsedSeconds;
        lastFieldKillCount = resultData.TotalKillCount;
        lastFieldMemberResults = new List<FieldMemberResultData>();
        lastFieldAcquiredResources = new List<FieldResourceAmountData>();

        for (int i = 0; i < resultData.Members.Count; i++)
        {
            if (resultData.Members[i] != null)
            {
                lastFieldMemberResults.Add(resultData.Members[i].Clone());
            }
        }

        for (int i = 0; i < resultData.AcquiredResources.Count; i++)
        {
            if (resultData.AcquiredResources[i] != null)
            {
                lastFieldAcquiredResources.Add(resultData.AcquiredResources[i].Clone());
            }
        }
    }

    /// <summary>
    /// 저장 파일에 들어가지 않는 판 단위 값을 처음 값으로 비웁니다. 타이틀에서 새 게임을 시작하거나 저장을 불러오기 전에 부릅니다.
    /// </summary>
    /// <remarks>
    /// GameDataManager는 씬을 넘어 유지되므로, 일시정지 메뉴로 타이틀에 돌아간 뒤 다른 판을 시작하면 이전 판의 날짜·안정도·출전 분대·
    /// 마지막 필드 결과 같은 값이 남습니다. <see cref="ApplySaveData"/>는 저장에 있는 값만 덮어쓰므로 그 전에 이 함수로 비웁니다.
    /// 게임오버 재시도처럼 같은 판을 체크포인트로 되돌리는 경로에서는 부르지 않습니다.
    /// </remarks>
    public void ResetSessionState()
    {
        lastStageId = string.Empty;
        shelterStability = 100;
        currentDay = 1;
        foodShortagePenaltyActive = false;
        fuelShortagePenaltyActive = false;
        playableSquadRuntimeIds = new List<string>();
        shelterCharacterAssignments = new List<ShelterCharacterAssignmentData>();
        ClearLastFieldResult();
        totalFieldKillCount = 0;
        fieldKillHistory = new List<int>();
        pendingDefenseVictoryStageId = string.Empty;
        checkpointAutoSavePending = false;
    }

    private void ClearLastFieldResult()
    {
        lastSettledFieldId = string.Empty;
        lastFieldStageId = string.Empty;
        lastFieldOutcome = default;
        lastFieldEndReason = FieldEndReason.None;
        lastFieldMissionCompleted = false;
        lastFieldElapsedSeconds = 0.0f;
        lastFieldKillCount = 0;
        lastFieldMemberResults = new List<FieldMemberResultData>();
        lastFieldAcquiredResources = new List<FieldResourceAmountData>();
    }

    /// <summary>새 게임용 고정 캐릭터 원본을 런타임 정본에 설정합니다.</summary>
    public void InitializeCharacters(IEnumerable<CharacterSnapshotData> source)
    {
        characters = new List<CharacterSnapshotData>();
        if (source == null)
        {
            return;
        }

        foreach (CharacterSnapshotData snapshot in source)
        {
            if (snapshot == null || snapshot.CharacterId == PlayerbleCharacterId.Unknown)
            {
                continue;
            }

            if (!TryGetCharacterIndex(snapshot.CharacterId, out _))
            {
                characters.Add(snapshot.Clone());
            }
        }
    }

    private void ApplySavedResources(IEnumerable<SaveData.ResourceAmountData> savedResources)
    {
        resourceAmounts = new List<ResourceAmountState>();
        if (savedResources == null)
        {
            return;
        }

        foreach (SaveData.ResourceAmountData resource in savedResources)
        {
            if (resource != null)
            {
                SetResourceAmount(resource.resourceId, resource.amount);
            }
        }
    }

    private void ApplySavedCharacterStates(IEnumerable<SaveData.CharacterStateSaveData> savedCharacters)
    {
        if (savedCharacters == null)
        {
            return;
        }

        foreach (SaveData.CharacterStateSaveData savedCharacter in savedCharacters)
        {
            if (savedCharacter == null
                || savedCharacter.characterId == PlayerbleCharacterId.Unknown
                || !TryGetCharacterIndex(savedCharacter.characterId, out int index))
            {
                continue;
            }

            CharacterSnapshotData snapshot = characters[index].Clone();
            snapshot.SetCombatState(
                savedCharacter.currentHp,
                savedCharacter.maxHp,
                savedCharacter.injurySeverityGauge,
                savedCharacter.maxInjuryGauge,
                savedCharacter.injuryState,
                savedCharacter.isDown,
                savedCharacter.isCombatOut,
                snapshot.IsPlayerSquadMember);
            characters[index] = snapshot;
        }
    }

    private void ApplyFieldMemberResult(FieldMemberResultData memberResult)
    {
        CharacterSnapshotData snapshot = memberResult?.Snapshot;
        if (snapshot != null)
        {
            int temporaryHpPenalty = memberResult.TemporaryHpPenalty;
            if (temporaryHpPenalty > 0
                && snapshot.CurrentHp > 0
                && !snapshot.IsDown
                && !snapshot.IsCombatOut)
            {
                snapshot.SetCombatState(
                    Mathf.Min(snapshot.MaxHp, snapshot.CurrentHp + temporaryHpPenalty),
                    snapshot.MaxHp,
                    snapshot.InjurySeverityGauge,
                    snapshot.MaxInjuryGauge,
                    snapshot.InjuryState,
                    snapshot.IsDown,
                    snapshot.IsCombatOut,
                    snapshot.IsPlayerSquadMember);
            }

            TryBindFieldCharacterSnapshot(snapshot);
        }
    }

    private bool TryGetCharacterIndex(PlayerbleCharacterId characterId, out int index)
    {
        index = -1;
        if (characterId == PlayerbleCharacterId.Unknown)
        {
            return false;
        }

        for (int i = 0; i < characters.Count; i++)
        {
            CharacterSnapshotData character = characters[i];
            if (character != null && character.CharacterId == characterId)
            {
                index = i;
                return true;
            }
        }

        return false;
    }

    private bool TryGetCharacterIndex(string id, out int index)
    {
        index = -1;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        string normalizedId = id.Trim();
        for (int i = 0; i < characters.Count; i++)
        {
            CharacterSnapshotData character = characters[i];
            if (character != null
                && (character.RuntimeId == normalizedId || character.DefinitionId == normalizedId))
            {
                index = i;
                return true;
            }
        }

        return false;
    }

    private bool ContainsCharacter(string runtimeId, string definitionId)
    {
        return TryGetCharacterIndex(
            string.IsNullOrWhiteSpace(runtimeId) ? definitionId : runtimeId,
            out _);
    }

    private ShelterCharacterAssignmentData FindShelterAssignment(string runtimeId)
    {
        if (string.IsNullOrWhiteSpace(runtimeId))
            return null;
        return shelterCharacterAssignments.Find(assignment => assignment != null
            && assignment.RuntimeId == runtimeId.Trim());
    }

    private int GetResourceAmount(string resourceId)
    {
        ResourceAmountState state = FindResource(resourceId);
        return state?.Amount ?? 0;
    }

    private void SetResourceAmount(string resourceId, int amount)
    {
        string id = ResourceIds.Normalize(resourceId);
        if (string.IsNullOrEmpty(id))
            return;

        ResourceAmountState state = FindResource(id);
        if (state == null)
        {
            resourceAmounts.Add(new ResourceAmountState(id, amount));
            return;
        }

        state.SetAmount(amount);
    }

    private void AddResource(string resourceId, int amount)
    {
        if (amount > 0)
        {
            SetResourceAmount(
                resourceId,
                GetResourceAmount(resourceId) + amount);
        }
    }

    private ResourceAmountState FindResource(string resourceId)
    {
        string id = ResourceIds.Normalize(resourceId);
        if (string.IsNullOrEmpty(id))
            return null;

        for (int i = 0; i < resourceAmounts.Count; i++)
        {
            if (resourceAmounts[i] != null
                && string.Equals(
                    resourceAmounts[i].ResourceId,
                    id,
                    StringComparison.Ordinal))
            {
                return resourceAmounts[i];
            }
        }

        return null;
    }

    private void EnsureRuntimeState()
    {
        lastStageId ??= string.Empty;
        shelterStability = Mathf.Clamp(shelterStability, 0, 100);
        currentDay = Mathf.Max(1, currentDay);
        foodShortageHpDecrease = Mathf.Max(0, foodShortageHpDecrease);
        resourceAmounts ??= new List<ResourceAmountState>();
        itemStorageEntries ??= new List<ItemStorageEntry>();
        characters ??= new List<CharacterSnapshotData>();
        playableSquadRuntimeIds ??= new List<string>();
        shelterCharacterAssignments ??= new List<ShelterCharacterAssignmentData>();
        facilityStates ??= new List<FacilityRuntimeState>();
        manufacturing ??= new ManufacturingRuntimeData();
        lastFieldMemberResults ??= new List<FieldMemberResultData>();
        lastFieldAcquiredResources ??= new List<FieldResourceAmountData>();
        totalFieldKillCount = Mathf.Max(0, totalFieldKillCount);
        fieldKillHistory ??= new List<int>();
        NormalizeFieldKillHistory();

        NormalizeResources();
        NormalizeItemStorageEntries();
        NormalizeOwnedCharacters();
        playableSquadRuntimeIds = NormalizeCharacterIds(
            playableSquadRuntimeIds,
            ShelterRuntimeData.MaxFieldSquadSize);
        NormalizePlayerbleSquadRuntimeIds();
        NormalizeShelterAssignments();
        facilityStates = CloneFacilityStates(facilityStates);
        manufacturing.EnsureValid();

        if (!HasLastFieldResult)
        {
            ClearLastFieldResult();
        }
    }

    private void NormalizeResources()
    {
        List<ResourceAmountState> normalized = new();
        for (int i = 0; i < resourceAmounts.Count; i++)
        {
            ResourceAmountState source = resourceAmounts[i];
            if (source == null
                || string.IsNullOrEmpty(source.ResourceId))
            {
                continue;
            }

            ResourceAmountState existing = null;
            for (int j = 0; j < normalized.Count; j++)
            {
                if (string.Equals(
                        normalized[j].ResourceId,
                        source.ResourceId,
                        StringComparison.Ordinal))
                {
                    existing = normalized[j];
                    break;
                }
            }

            if (existing == null)
            {
                normalized.Add(source.Clone());
            }
            else
            {
                existing.SetAmount(existing.Amount + source.Amount);
            }
        }

        resourceAmounts = normalized;
    }

    private void NormalizeItemStorageEntries()
    {
        List<ItemStorageEntry> normalized = new();
        Dictionary<string, int> entryIndexes = new(StringComparer.Ordinal);
        for (int i = 0; i < itemStorageEntries.Count; i++)
        {
            ItemStorageEntry entry = itemStorageEntries[i];
            if (entry == null)
                continue;

            entry.EnsureValid();
            if (!entry.IsValid)
                continue;

            if (!entryIndexes.TryGetValue(entry.ItemDefinitionId, out int existingIndex))
            {
                entryIndexes.Add(entry.ItemDefinitionId, normalized.Count);
                normalized.Add(entry.Clone());
                continue;
            }

            long combinedQuantity = (long)normalized[existingIndex].Quantity + entry.Quantity;
            normalized[existingIndex] = new ItemStorageEntry(
                entry.ItemDefinitionId,
                combinedQuantity > int.MaxValue ? int.MaxValue : (int)combinedQuantity);
        }

        itemStorageEntries = normalized;
    }

    private void NormalizeOwnedCharacters()
    {
        HashSet<string> runtimeIds = new();
        List<CharacterSnapshotData> normalized = new();
        for (int i = 0; i < characters.Count; i++)
        {
            CharacterSnapshotData character = characters[i];
            string runtimeId = character == null || string.IsNullOrWhiteSpace(character.RuntimeId)
                ? character?.DefinitionId
                : character.RuntimeId;
            if (character != null
                && !string.IsNullOrWhiteSpace(runtimeId)
                && runtimeIds.Add(runtimeId))
            {
                CharacterSnapshotData normalizedCharacter = character.Clone();
                if (string.IsNullOrWhiteSpace(normalizedCharacter.RuntimeId))
                {
                    normalizedCharacter.SetSceneIdentity(
                        runtimeId,
                        normalizedCharacter.CharacterId,
                        normalizedCharacter.DisplayName);
                }

                normalized.Add(normalizedCharacter);
            }
        }

        characters = normalized;
    }

    private void NormalizePlayerbleSquadRuntimeIds()
    {
        for (int i = 0; i < playableSquadRuntimeIds.Count; i++)
        {
            if (TryGetCharacterIndex(playableSquadRuntimeIds[i], out int characterIndex))
            {
                CharacterSnapshotData character = characters[characterIndex];
                playableSquadRuntimeIds[i] = string.IsNullOrWhiteSpace(character.RuntimeId)
                    ? character.DefinitionId
                    : character.RuntimeId;
            }
        }

        playableSquadRuntimeIds = NormalizeCharacterIds(
            playableSquadRuntimeIds,
            ShelterRuntimeData.MaxFieldSquadSize);
    }

    private void NormalizeShelterAssignments()
    {
        List<ShelterCharacterAssignmentData> normalized = new();
        HashSet<string> assignedRuntimeIds = new();
        for (int i = 0; i < shelterCharacterAssignments.Count; i++)
        {
            ShelterCharacterAssignmentData assignment = shelterCharacterAssignments[i];
            if (assignment == null
                || !assignment.IsAssigned
                || !TryGetCharacterIndex(assignment.RuntimeId, out int characterIndex))
            {
                continue;
            }

            CharacterSnapshotData character = characters[characterIndex];
            string runtimeId = string.IsNullOrWhiteSpace(character.RuntimeId)
                ? character.DefinitionId
                : character.RuntimeId;
            if (!assignedRuntimeIds.Add(runtimeId))
                continue;

            normalized.Add(new ShelterCharacterAssignmentData(
                runtimeId,
                assignment.FacilityId,
                assignment.RoomId,
                assignment.Kind));
        }

        shelterCharacterAssignments = normalized;
    }

    private static bool ShouldApplyAcquiredResources(FieldOutcome outcome)
    {
        return outcome == FieldOutcome.Success || outcome == FieldOutcome.Evacuated;
    }

    private void RecordFieldKillHistory(int fieldKillCount)
    {
        int normalizedCount = Mathf.Max(0, fieldKillCount);
        totalFieldKillCount += normalizedCount;
        fieldKillHistory.Add(normalizedCount);
        NormalizeFieldKillHistory();
    }

    private void NormalizeFieldKillHistory()
    {
        for (int i = fieldKillHistory.Count - 1; i >= 0; i--)
        {
            if (fieldKillHistory[i] < 0)
            {
                fieldKillHistory[i] = 0;
            }
        }

        int overflow = fieldKillHistory.Count - MaxFieldKillHistory;
        if (overflow > 0)
        {
            fieldKillHistory.RemoveRange(0, overflow);
        }
    }

    private static List<string> NormalizeCharacterIds(IEnumerable<string> source, int maximumCount)
    {
        List<string> normalized = new();
        if (source == null)
        {
            return normalized;
        }

        foreach (string definitionId in source)
        {
            if (string.IsNullOrWhiteSpace(definitionId))
            {
                continue;
            }

            string normalizedId = definitionId.Trim();
            if (!normalized.Contains(normalizedId))
            {
                normalized.Add(normalizedId);
            }

            if (normalized.Count >= maximumCount)
            {
                break;
            }
        }

        return normalized;
    }

    private static List<FacilityRuntimeState> CloneFacilityStates(IEnumerable<FacilityRuntimeState> source)
    {
        List<FacilityRuntimeState> clone = new();
        if (source == null)
        {
            return clone;
        }

        HashSet<string> facilityIds = new();
        foreach (FacilityRuntimeState state in source)
        {
            if (state == null)
            {
                continue;
            }

            state.EnsureValid();
            if (!string.IsNullOrWhiteSpace(state.facilityId) && facilityIds.Add(state.facilityId))
            {
                clone.Add(new FacilityRuntimeState(
                    state.facilityId,
                    state.isUnlocked,
                    state.upgradeLevel));
            }
        }

        return clone;
    }

    private static List<ItemStorageEntry> CloneItemStorageEntries(IEnumerable<ItemStorageEntry> source)
    {
        List<ItemStorageEntry> clone = new();
        if (source == null)
            return clone;

        foreach (ItemStorageEntry entry in source)
        {
            if (entry != null && entry.IsValid)
                clone.Add(entry.Clone());
        }

        return clone;
    }

    private bool TryRejectDuplicateOrInvalidRoot()
    {
        GameManager rootManager = GetComponentInParent<GameManager>();
        if (rootManager == null)
        {
            Debug.LogWarning("[GameDataManager] Parent GameManager not found. Destroying duplicate/orphan instance.");
            Destroy(gameObject);
            return true;
        }

        if (GameManager.Instance != null && rootManager != GameManager.Instance)
        {
            Destroy(gameObject);
            return true;
        }

        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return true;
        }

        return false;
    }
}
