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
    /// <summary>현재 GameManager 자식에서 활성화된 전역 데이터 매니저 인스턴스입니다.</summary>
    public static GameDataManager Instance { get; private set; }

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
    [Tooltip("현재 출전 대상으로 선택된 캐릭터 런타임 ID 목록입니다. 최대 3명입니다.")]
    [FormerlySerializedAs("battleSquadNpcDefinitionIds")]
    [FormerlySerializedAs("playableSquadDefinitionIds")]
    [SerializeField] private List<string> playableSquadRuntimeIds = new();
    [Tooltip("셸터 시설에 배치된 캐릭터의 셸터 전용 상태입니다.")]
    [SerializeField] private List<ShelterCharacterAssignmentData> shelterCharacterAssignments = new();
    [Tooltip("시설별 해금 여부와 업그레이드 단계입니다.")]
    [SerializeField] private List<FacilityRuntimeState> facilityStates = new();
    [SerializeField] private ManufacturingRuntimeData manufacturing = new();

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

    private ShelterSceneDataManager activeShelterSceneDataManager;

    /// <summary>현재 보유한 전체 캐릭터 수입니다.</summary>
    public int CharacterCount => characters?.Count ?? 0;
    public IReadOnlyList<CharacterSnapshotData> Characters => characters;
    public IReadOnlyList<ItemStorageEntry> ItemStorageEntries => itemStorageEntries;

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

    public bool FoodShortagePenaltyActive => foodShortagePenaltyActive;

    public bool FuelShortagePenaltyActive => fuelShortagePenaltyActive;

    /// <summary>현재 씬의 ShelterSceneDataManager가 등록되어 있는지 여부입니다.</summary>
    public bool HasActiveShelterSceneDataManager => activeShelterSceneDataManager != null;

    /// <summary>현재 세션 또는 저장 데이터에 반영된 최근 필드 결과가 있는지 여부입니다.</summary>
    public bool HasLastFieldResult => !string.IsNullOrWhiteSpace(lastSettledFieldId);

    /// <summary>마지막으로 정산 반영이 완료된 필드 ID입니다.</summary>
    public string LastSettledFieldId => lastSettledFieldId ?? string.Empty;

    public int TotalFieldKillCount => Mathf.Max(0, totalFieldKillCount);
    public IReadOnlyList<int> FieldKillHistory => fieldKillHistory;

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
            if (snapshot != null && packet.TryAddCharacter(snapshot.Clone(), out ShelterMemberRuntimeData member))
            {
                ShelterCharacterAssignmentData assignment = FindShelterAssignment(member.RuntimeId);
                if (assignment != null && assignment.IsAssigned)
                    member.AssignToFacility(assignment.FacilityId, assignment.RoomId, assignment.Kind);
            }
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
                if (character.IsAssignedToFacility)
                    shelterCharacterAssignments.Add(new ShelterCharacterAssignmentData(character));
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

        if (activeShelterSceneDataManager != null)
        {
            activeShelterSceneDataManager.ApplyRuntimeSnapshot(CreateShelterRuntimeSnapshot());
        }

        return true;
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

    /// <summary>현재 전역 평탄 정본을 지정한 프로필의 독립된 저장 패킷으로 변환합니다.</summary>
    /// <remarks>활성 셸터 작업본은 호출 전에 <see cref="SyncFromShelter()"/>로 정본에 먼저 반영해야 합니다.</remarks>
    public SaveData CreateSaveData(string profileId)
    {
        EnsureRuntimeState();
        SaveData saveData = new SaveData
        {
            profileId = string.IsNullOrWhiteSpace(profileId) ? SaveFilePaths.DefaultProfileId : profileId.Trim(),
            shared = CreateSharedSaveData(),
            shelter = CreateShelterSaveData(),
            lastFieldResult = CreateLastFieldResultSnapshot()
        };

        saveData.MarkSavedNow();
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

        SaveData.SharedSaveData sharedSaveData = saveData.shared ?? new SaveData.SharedSaveData();
        ApplySharedSaveData(sharedSaveData);
        ApplyShelterSaveData(saveData.shelter ?? new SaveData.ShelterSaveData());

        // 시설 해금/레벨과 캐릭터 배치를 먼저 구성한 뒤 제조 슬롯 진행 상태를 복원합니다.
        manufacturing = ManufacturingFacilitySaveDataMapper.ToRuntime(sharedSaveData.manufacturing);

        FieldResultData savedFieldResult = saveData.lastFieldResult ?? saveData.lastBattleResult;
        if (savedFieldResult != null
            && !string.IsNullOrWhiteSpace(savedFieldResult.FieldId))
        {
            ApplyLastFieldResult(savedFieldResult);
        }
        else
        {
            ClearLastFieldResult();
        }

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

    private SaveData.SharedSaveData CreateSharedSaveData()
    {
        SaveData.SharedSaveData saveData = new SaveData.SharedSaveData
        {
            lastStageId = lastStageId,
            shelterStability = ShelterStability,
            foodShortagePenaltyActive = FoodShortagePenaltyActive,
            fuelShortagePenaltyActive = FuelShortagePenaltyActive,
            totalFieldKillCount = TotalFieldKillCount,
            fieldKillHistory = new List<int>(fieldKillHistory),
            manufacturing = ManufacturingFacilitySaveDataMapper.FromRuntime(manufacturing)
        };

        for (int i = 0; i < resourceAmounts.Count; i++)
        {
            ResourceAmountState resource = resourceAmounts[i];
            if (resource != null)
            {
                saveData.resources.Add(new SaveData.ResourceAmountData
                {
                    resourceId = resource.ResourceId,
                    amount = resource.Amount
                });
            }
        }

        for (int i = 0; i < characters.Count; i++)
        {
            CharacterSnapshotData snapshot = characters[i];
            if (snapshot != null)
                saveData.characters.Add(snapshot.Clone());
        }

        return saveData;
    }

    private SaveData.ShelterSaveData CreateShelterSaveData()
    {
        SaveData.ShelterSaveData saveData = new SaveData.ShelterSaveData
        {
            currentDay = CurrentDay,
            battleSquadRuntimeIds = new List<string>(playableSquadRuntimeIds)
        };

        for (int i = 0; i < shelterCharacterAssignments.Count; i++)
        {
            ShelterCharacterAssignmentData assignment = shelterCharacterAssignments[i];
            if (assignment == null || !assignment.IsAssigned)
                continue;

            saveData.characterAssignments.Add(new SaveData.CharacterAssignmentSaveData
            {
                runtimeId = assignment.RuntimeId,
                facilityId = assignment.FacilityId,
                roomId = assignment.RoomId,
                kind = assignment.Kind
            });
        }

        for (int i = 0; i < facilityStates.Count; i++)
        {
            SaveData.FacilitySaveData facilitySaveData = FacilitySaveDataMapper.FromRuntime(facilityStates[i]);
            if (facilitySaveData != null)
            {
                saveData.facilities.Add(facilitySaveData);
            }
        }

        return saveData;
    }

    private void ApplySharedSaveData(SaveData.SharedSaveData saveData)
    {
        lastStageId = saveData.lastStageId?.Trim() ?? string.Empty;
        shelterStability = Mathf.Clamp(saveData.shelterStability, 0, 100);
        foodShortagePenaltyActive = saveData.foodShortagePenaltyActive;
        fuelShortagePenaltyActive = saveData.fuelShortagePenaltyActive;
        totalFieldKillCount = Mathf.Max(0, saveData.totalFieldKillCount);
        fieldKillHistory = saveData.fieldKillHistory != null
            ? new List<int>(saveData.fieldKillHistory)
            : new List<int>();
        resourceAmounts = new List<ResourceAmountState>();
        characters = new List<CharacterSnapshotData>();
        shelterCharacterAssignments = new List<ShelterCharacterAssignmentData>();

        if (saveData.resources != null)
        {
            for (int i = 0; i < saveData.resources.Count; i++)
            {
                SaveData.ResourceAmountData resource = saveData.resources[i];
                if (resource != null)
                {
                    SetResourceAmount(
                        resource.resourceId,
                        resource.amount);
                }
            }
        }

        if (saveData.characters != null && saveData.characters.Count > 0)
        {
            for (int i = 0; i < saveData.characters.Count; i++)
            {
                CharacterSnapshotData snapshot = saveData.characters[i];
                if (snapshot != null && !ContainsCharacter(snapshot.RuntimeId, snapshot.DefinitionId))
                    characters.Add(snapshot.Clone());
            }
        }
        else if (saveData.npcs != null)
        {
            for (int i = 0; i < saveData.npcs.Count; i++)
            {
                ShelterMemberRuntimeData legacyCharacter = LegacyNpcSaveDataMapper.ToRuntime(saveData.npcs[i]);
                if (legacyCharacter != null && !ContainsCharacter(legacyCharacter.RuntimeId, legacyCharacter.DefinitionId))
                {
                    characters.Add(legacyCharacter.CreateSnapshot());
                    if (legacyCharacter.IsAssignedToFacility)
                        shelterCharacterAssignments.Add(new ShelterCharacterAssignmentData(legacyCharacter));
                }
            }
        }
    }

    private void ApplyShelterSaveData(SaveData.ShelterSaveData saveData)
    {
        currentDay = Mathf.Max(1, saveData.currentDay);
        itemStorageEntries = new List<ItemStorageEntry>();
        IEnumerable<string> savedSquadIds = saveData.battleSquadRuntimeIds != null
            && saveData.battleSquadRuntimeIds.Count > 0
            ? saveData.battleSquadRuntimeIds
            : saveData.battleSquadNpcDefinitionIds;
        playableSquadRuntimeIds = NormalizeCharacterIds(
            savedSquadIds,
            ShelterRuntimeData.MaxFieldSquadSize);
        if (saveData.characterAssignments != null && saveData.characterAssignments.Count > 0)
        {
            shelterCharacterAssignments = new List<ShelterCharacterAssignmentData>();
            for (int i = 0; i < saveData.characterAssignments.Count; i++)
            {
                SaveData.CharacterAssignmentSaveData assignment = saveData.characterAssignments[i];
                if (assignment == null)
                    continue;
                ShelterCharacterAssignmentData runtimeAssignment = new ShelterCharacterAssignmentData(
                    assignment.runtimeId,
                    assignment.facilityId,
                    assignment.roomId,
                    assignment.kind);
                if (runtimeAssignment.IsAssigned)
                    shelterCharacterAssignments.Add(runtimeAssignment);
            }
        }
        facilityStates = new List<FacilityRuntimeState>();

        if (saveData.facilities == null)
        {
            return;
        }

        for (int i = 0; i < saveData.facilities.Count; i++)
        {
            FacilityRuntimeState state = FacilitySaveDataMapper.ToRuntime(saveData.facilities[i]);
            if (state != null)
            {
                facilityStates.Add(state);
            }
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
                clone.Add(new FacilityRuntimeState(state.facilityId, state.isUnlocked, state.upgradeLevel));
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
