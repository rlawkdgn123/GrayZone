using UnityEngine;
using UnityEngine.Serialization;
using System.Collections.Generic;

/// <summary>
/// 의료 시설의 환자 치료, 헬퍼 배치, 시설 업그레이드 반영을 담당
/// </summary>
/// <remarks>
/// 시설 해금/레벨의 진실원천은 <see cref="FacilityManager"/>이며, 이 컴포넌트는 의료 시설의 표시와 치료 진행 상태를 관리
/// </remarks>
public class MedicalManager : MonoBehaviour, IFacilityUpgradeable, IFuelShortageAffected
{
    private const int MaxLevelIndex = 3; // 레벨 4단계 (인덱스 0,1,2,3)
    private const int TotalPatientSlotCount = MaxLevelIndex + 1;

    [Header("Facility")]
    [SerializeField] private FacilityDefinition definition;
    [SerializeField] private string fallbackFacilityId = "medical_center";
    [SerializeField] private string roomId = "medical_room";

    [Header("Character Access")]
    [SerializeField] private CharacterManager characterManager;

    [Header("Level Visuals")]
    [SerializeField] private FacilityLevelVisuals levelVisuals;

    [Header("Upgrade Cost (레벨 i → i+1, 시설이 자기 비용을 소유)")]
    [SerializeField] private UpgradeCostTier[] upgradeCosts; // 길이 = 최대 업그레이드 횟수(MaxLevelIndex)

    [Header("Recovery")]
    [SerializeField] private int baseRecoveryPerDay = 5; // 일일 기본 회복 %
    [Min(0)]
    [SerializeField] private int fuelShortageEfficiencyDecrease = 2;
    [FormerlySerializedAs("staffHealBonuses")]
    [SerializeField] private HelperRecoveryBonus[] helperRecoveryBonuses = new HelperRecoveryBonus[]
    {
        new HelperRecoveryBonus { type = NPCType.Tanker, bonusPercent = 1 },
        new HelperRecoveryBonus { type = NPCType.Healer, bonusPercent = 2 },
        new HelperRecoveryBonus { type = NPCType.Dealer, bonusPercent = 1 }
    };

    private readonly List<MedicalTreatment> patientTreatments = new List<MedicalTreatment>();
    private readonly List<PatientStatus> patientStatuses = new List<PatientStatus>();
    private readonly List<ShelterMemberRuntimeData> helpers = new List<ShelterMemberRuntimeData>();
    // 임시 빌드 전용: 슬롯별 1회 사용 상태. 세이브하지 않으며 방어전 귀환 연결점에서 재충전한다.
    private readonly bool[] m_patientSlotAvailable = new bool[TotalPatientSlotCount];
    private bool m_isUnlocked = true; // FacilityManager가 세이브 기준으로 덮어씀(의료시설 기본 해금)
    private bool m_isFuelShortageActive;

    /// <summary>헬퍼 배치가 해제됐을 때 발생</summary>
    public event System.Action<ShelterMemberRuntimeData> OnHelperReleased;

    /// <summary>환자 치료가 완료되어 슬롯에서 제거됐을 때 발생</summary>
    public event System.Action<ShelterMemberRuntimeData> OnPatientHealed;

    /// <summary>
    /// 환자 슬롯 표시를 다시 그려야 할 때 발생
    /// </summary>
    public event System.Action OnPatientSlotsChanged;

    /// <summary>현재 치료 중인 환자 표시 상태 목록</summary>
    public IReadOnlyList<PatientStatus> PatientStatuses
    {
        get
        {
            RefreshPatientStatuses();
            return patientStatuses;
        }
    }

    /// <summary>현재 치료 중인 환자 수</summary>
    public int CurrentPatientCount => patientTreatments.Count;

    /// <summary>현재 업그레이드 레벨 기준 환자 슬롯 수</summary>
    public int PatientCapacity => PatientSlotsForLevel(CurrentLevel);

    /// <summary>현재 배치된 헬퍼 수</summary>
    public int CurrentHelperCount => helpers.Count;

    /// <summary>현재 업그레이드 레벨 기준 헬퍼 슬롯 수</summary>
    public int HelperCapacity => HelperSlotsForLevel(CurrentLevel);

    /// <summary>의료 시설 업그레이드 레벨</summary>
    public int UpgradeLevel => CurrentLevel;

    /// <summary>의료 시설의 최대 업그레이드 레벨</summary>
    public int MaxUpgradeLevel => MaxLevelIndex;

    // 레벨은 FacilityManager(진실원천)에서 읽는다 — MedicalManager는 캐시하지 않는다.
    private int CurrentLevel =>
        FacilityManager.Instance != null ? FacilityManager.Instance.GetUpgradeLevel(FacilityId) : 0;

    /// <summary>시설 정의 또는 대체값으로 결정한 의료 시설 ID</summary>
    public string FacilityId
    {
        //fallbackFacilityId 나중에 통일 필요( 방지용 ID임 이건 )
        get
        {
            if (definition != null && !string.IsNullOrWhiteSpace(definition.FacilityId))
                return definition.FacilityId;
            return fallbackFacilityId;
        }
    }

    private void Awake()
    {
        CacheCharacterManager();
        RechargeAllPatientSlots();
    }

    private void OnValidate()
    {
        baseRecoveryPerDay = Mathf.Max(1, baseRecoveryPerDay);
        fuelShortageEfficiencyDecrease = Mathf.Max(0, fuelShortageEfficiencyDecrease);
    }

    private void Start()
    {
        // 방어전을 위한 로직 변경
        /*
        if (GameDateManager.Instance != null)
            GameDateManager.Instance.DayAdvanced += OnDayAdvanced;
        */

        // 등록 즉시 FacilityManager가 세이브 기준 해금/레벨을 이 시설에 반영한다.
        FacilityManager.Instance?.Register(this);
    }

    private void OnDestroy()
    {
        // 방어전을 위한 로직 변경
        /*
        if (GameDateManager.Instance != null)
            GameDateManager.Instance.DayAdvanced -= OnDayAdvanced;
        */

        FacilityManager.Instance?.Unregister(this);
    }

    /// <summary>
    /// <see cref="FacilityManager"/>가 레벨 변경 후 호출하는 시설 반영 콜백
    /// </summary>
    /// <param name="level">새 업그레이드 레벨. 실제 값은 <see cref="FacilityManager"/> 기준</param>
    public void ApplyUpgradeLevel(int level)
    {
        RefreshLevelVisuals();
        NotifyPatientSlotsChanged();
    }

    /// <summary>
    /// <see cref="FacilityManager"/>가 저장 데이터에서 복원한 시설 해금 상태를 반영
    /// </summary>
    /// <param name="isUnlocked">시설 해금 여부</param>
    public void ApplyUnlockState(bool isUnlocked)
    {
        m_isUnlocked = isUnlocked;
        RefreshLevelVisuals();
    }

    public void ApplyFuelShortageState(bool isActive)
    {
        if (m_isFuelShortageActive == isActive)
            return;

        m_isFuelShortageActive = isActive;
        RecalculateAllTreatmentPlans();
        NotifyPatientSlotsChanged();
    }

    /// <summary>
    /// 현재 캐릭터 런타임 배치 정보에서 의료시설의 도우미와 환자 치료 목록을 재구성합니다.
    /// 시설 해금/레벨과 부족 패널티가 적용된 뒤 호출해야 합니다.
    /// </summary>
    public void RestoreRuntimeState()
    {
        if (!TryGetCharacterManager(out CharacterManager manager))
            return;

        patientTreatments.Clear();
        helpers.Clear();

        IReadOnlyList<ShelterMemberRuntimeData> characters = manager.Characters;
        for (int i = 0; i < characters.Count; i++)
        {
            ShelterMemberRuntimeData character = characters[i];
            if (character != null
                && character.AssignmentKind == FacilityAssignmentKind.Staff
                && string.Equals(
                    character.AssignedFacilityId,
                    FacilityId,
                    System.StringComparison.Ordinal))
            {
                helpers.Add(character);
            }
        }

        float dailyRecovery = GetDailyRecovery();
        for (int i = 0; i < characters.Count; i++)
        {
            ShelterMemberRuntimeData character = characters[i];
            if (character != null
                && character.AssignmentKind == FacilityAssignmentKind.Patient
                && string.Equals(
                    character.AssignedFacilityId,
                    FacilityId,
                    System.StringComparison.Ordinal))
            {
                patientTreatments.Add(new MedicalTreatment(character, dailyRecovery));
            }
        }

        NotifyPatientSlotsChanged();
    }

    // 현재 해금/레벨 상태를 건물 비주얼에 반영한다(잠금이면 전부 숨김).
    private void RefreshLevelVisuals()
    {
        if (levelVisuals == null)
            return;

        if (m_isUnlocked)
            levelVisuals.ShowLevel(CurrentLevel);
        else
            levelVisuals.HideAll();
    }

    /// <summary>
    /// 현재 레벨에서 다음 레벨로 올릴 때 필요한 비용을 반환
    /// </summary>
    /// <param name="currentLevel">비용을 조회할 현재 업그레이드 레벨</param>
    /// <returns>해당 레벨의 업그레이드 비용 묶음 범위 밖이면 무료 묶음</returns>
    public CostBundle GetUpgradeCost(int currentLevel)
    {
        if (upgradeCosts == null || currentLevel < 0 || currentLevel >= upgradeCosts.Length)
            return new CostBundle();

        UpgradeCostEntry[] entries = upgradeCosts[currentLevel].entries;
        if (entries == null || entries.Length == 0)
            return new CostBundle();

        ResourceCost[] costs = new ResourceCost[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            costs[i] = new ResourceCost(
                entries[i].resourceId,
                entries[i].amount);
        }

        return new CostBundle(costs);
    }

    /// <summary>
    /// 자원 이외의 업그레이드 조건을 검사
    /// </summary>
    /// <param name="currentLevel">검사할 현재 업그레이드 레벨</param>
    /// <returns>현재는 별도 조건이 없으므로 항상 <c>true</c></returns>

    //NOTE : 업그레이드 조건이 자원이 아닌 특수조건일 경우 아래와 같이 구현해서 사용
    public bool AreUpgradeRequirementsMet(int currentLevel) => true;

    /// <summary>
    /// 치료 대상 NPC 후보 채우기
    /// </summary>
    /// <param name="results">후보 결과 목록. 호출 시 기존 내용 비움</param>
    public void FillPatientCandidates(List<ShelterMemberRuntimeData> results)
    {
        if (results == null)
            throw new System.ArgumentNullException(nameof(results));

        results.Clear();

        if (!TryGetCharacterManager(out CharacterManager manager))
            return;

        foreach (ShelterMemberRuntimeData character in manager.Characters)
        {
            if (CanAssignPatient(character))
                results.Add(character);
        }
    }

    /// <summary>
    /// 지정한 NPC가 환자로 배치 가능한지 검사
    /// </summary>
    /// <param name="character">검사할 NPC 런타임 데이터</param>
    /// <returns>환자로 배치 가능하면 <c>true</c></returns>
    public bool CanAssignPatient(ShelterMemberRuntimeData character)
    {
        if (character == null)
            return false;

        // 임시 빌드에서는 치료 배치 목록을 만들지 않고 HP가 감소한 생존자만 즉시 치료 후보로 사용한다.
        return !character.IsDead && character.CurrentHp < character.MaxHp;

        /* 날짜 기반 치료를 다시 사용할 때 복구할 기존 후보 조건.
        if (patientTreatments.Count >= PatientCapacity)
            return false;

        if (FindPatientSlotIndex(character.RuntimeId) >= 0)
            return false;

        if (character.IsDead)
            return false;

        // 완치(Healthy)면 치료 불필요 (enum 기준).
        if (character.InjuryState == CharacterInjuryState.Normal)
            return false;

        return !character.IsAssignedToFacility;
        */
    }

    /// <summary>임시 빌드 전용: 지정 환자 슬롯의 1회 사용 가능 상태를 반환합니다.</summary>
    public bool IsPatientSlotAvailable(int slotIndex)
    {
        return slotIndex >= 0
            && slotIndex < m_patientSlotAvailable.Length
            && m_patientSlotAvailable[slotIndex];
    }

    /// <summary>
    /// 임시 빌드 전용: 환자를 즉시 완전 회복시키고 성공한 슬롯을 사용 완료 상태로 전환합니다.
    /// 의료 의뢰 비용은 이 임시 흐름에서 0입니다.
    /// </summary>
    public bool TryUsePatientSlot(int slotIndex, string runtimeId)
    {
        if (!IsPatientSlotAvailable(slotIndex)
            || slotIndex >= PatientCapacity
            || string.IsNullOrWhiteSpace(runtimeId)
            || !TryGetCharacterManager(out CharacterManager manager)
            || !manager.TryGetCharacter(runtimeId, out ShelterMemberRuntimeData target)
            || !CanAssignPatient(target))
        {
            return false;
        }

        if (!manager.TryCompleteRecovery(runtimeId, out _))
            return false;

        m_patientSlotAvailable[slotIndex] = false;
        NotifyPatientSlotsChanged();
        return true;
    }

    /// <summary>
    /// 임시 빌드 전용 방어전 귀환 연결점입니다. 방어전 결과 확정 후 셸터 진입 시 호출합니다.
    /// </summary>
    public void RechargeAllPatientSlots()
    {
        for (int i = 0; i < m_patientSlotAvailable.Length; i++)
            m_patientSlotAvailable[i] = true;

        NotifyPatientSlotsChanged();
    }

    /// <summary>
    /// NPC 식별자로 환자를 배치
    /// </summary>
    /// <param name="runtimeId">배치할 NPC 정의 ID</param>
    /// <returns>배치에 성공했거나 이미 배치되어 있으면 <c>true</c></returns>
    public bool TryAssignPatient(string runtimeId)
    {
        if (string.IsNullOrWhiteSpace(runtimeId)) return false;
        //몇번째 슬롯에 있는지(슬롯에 없는 id면 -1 return)
        if (FindPatientSlotIndex(runtimeId) >= 0) return true;

        //목록에 있는 숫자가 최대치 보다 높을경우 오류상태
        if (patientTreatments.Count >= PatientCapacity) return false;

        if (!TryGetCharacterManager(out CharacterManager manager)) return false;

        //셸터 데이터에서 NPC를 관리하는 CharacterManager로 부터 데이터를 가져오는 함수
        if (!manager.TryGetCharacter(runtimeId, out ShelterMemberRuntimeData target))
            return false;

        if (!CanAssignPatient(target))
            return false;

        if (!manager.TryAssignToFacility(
                runtimeId,
                FacilityId,
                roomId,
                CharacterAssignmentFilter.AvailableAlive,
                FacilityAssignmentKind.Patient,
                out target,
                out _))
        {
            return false;
        }

        patientTreatments.Add(new MedicalTreatment(target, GetDailyRecovery()));
        NotifyPatientSlotsChanged();
        return true;
    }

    /// <summary>
    /// NPC 식별자로 환자 배치를 해제
    /// </summary>
    /// <param name="runtimeId">해제할 NPC 정의 ID</param>
    /// <returns>실제로 해제됐으면 <c>true</c></returns>
    public bool TryReleasePatient(string runtimeId)
    {
        int slotIndex = FindPatientSlotIndex(runtimeId);
        if (slotIndex < 0) return false;
        if (!TryGetCharacterManager(out CharacterManager manager)) return false;

        MedicalTreatment treatment = patientTreatments[slotIndex];
        if (!manager.TryReleaseFromFacility(treatment.Patient.RuntimeId, out _))
            return false;

        patientTreatments.RemoveAt(slotIndex);
        NotifyPatientSlotsChanged();
        return true;
    }

    /// <summary>
    /// 의료 헬퍼 NPC 후보 채우기
    /// </summary>
    /// <param name="results">후보 결과 목록. 호출 시 기존 내용 비움</param>
    public void FillHelperCandidates(List<ShelterMemberRuntimeData> results)
    {
        if (results == null)
            throw new System.ArgumentNullException(nameof(results));

        results.Clear();

        if (!TryGetCharacterManager(out CharacterManager manager))
            return;

        foreach (ShelterMemberRuntimeData character in manager.Characters)
        {
            if (CanAssignHelper(character))
                results.Add(character);
        }
    }

    /// <summary>
    /// 지정한 NPC가 의료 헬퍼로 배치 가능한지 검사
    /// </summary>
    /// <param name="character">검사할 NPC 런타임 데이터</param>
    /// <returns>헬퍼로 배치 가능하면 <c>true</c></returns>
    public bool CanAssignHelper(ShelterMemberRuntimeData character)
    {
        if (character == null)
            return false;

        if (helpers.Count >= HelperCapacity)
            return false;

        if (FindHelperIndex(character.RuntimeId) >= 0)
            return false;

        if (character.IsDead)
            return false;

        // 도우미는 건강 또는 경상만 가능 (중상·위독 제외).
        CharacterInjuryState state = character.InjuryState;
        if (state != CharacterInjuryState.Normal && state != CharacterInjuryState.Minor)
            return false;

        return !character.IsAssignedToFacility;
    }

    /// <summary>
    /// NPC 식별자로 의료 헬퍼를 배치
    /// </summary>
    /// <param name="runtimeId">배치할 NPC 정의 ID</param>
    /// <returns>배치에 성공했거나 이미 배치되어 있으면 <c>true</c></returns>
    public bool TryAssignHelper(string runtimeId)
    {
        if (string.IsNullOrWhiteSpace(runtimeId)) return false;
        if (FindHelperIndex(runtimeId) >= 0) return true;

        if (helpers.Count >= HelperCapacity) return false;
        if (!TryGetCharacterManager(out CharacterManager manager)) return false;

        if (!manager.TryGetCharacter(runtimeId, out ShelterMemberRuntimeData target))
            return false;

        if (!CanAssignHelper(target))
            return false;

        if (!manager.TryAssignToFacility(
                runtimeId,
                FacilityId,
                roomId,
                CharacterAssignmentFilter.AvailableAlive,
                FacilityAssignmentKind.Staff,
                out target,
                out _))
        {
            return false;
        }

        helpers.Add(target);
        RecalculateAllTreatmentPlans();
        NotifyPatientSlotsChanged();
        return true;
    }

    /// <summary>
    /// NPC 식별자로 헬퍼 배치를 해제
    /// </summary>
    /// <param name="runtimeId">해제할 NPC 정의 ID</param>
    /// <returns>실제로 해제됐으면 <c>true</c></returns>
    public bool TryReleaseHelper(string runtimeId)
    {
        int index = FindHelperIndex(runtimeId);
        if (index < 0) return false;
        if (!TryGetCharacterManager(out CharacterManager manager)) return false;

        ShelterMemberRuntimeData helper = helpers[index];
        if (!manager.TryReleaseFromFacility(helper.RuntimeId, out _))
            return false;

        helpers.RemoveAt(index);
        RecalculateAllTreatmentPlans();
        OnHelperReleased?.Invoke(helper);
        NotifyPatientSlotsChanged();
        return true;
    }

    private int FindHelperIndex(string runtimeId)
    {
        if (string.IsNullOrWhiteSpace(runtimeId))
            return -1;

        string normalizedRuntimeId = runtimeId.Trim();
        for (int i = 0; i < helpers.Count; i++)
        {
            ShelterMemberRuntimeData helper = helpers[i];
            if (helper != null && helper.RuntimeId == normalizedRuntimeId)
                return i;
        }

        return -1;
    }

    // 방어전을 위한 로직 변경
    /*
    private void OnDayAdvanced(int prev, int next)
    {
        if (patientTreatments.Count == 0)
            return;

        if (!TryGetCharacterManager(out CharacterManager manager))
            return;

        // 다중일 스킵은 현재 스코프 밖 — 하루 단위로만 진행한다.
        bool changed = false;
        for (int i = patientTreatments.Count - 1; i >= 0; i--)
        {
            MedicalTreatment treatment = patientTreatments[i];
            ShelterMemberRuntimeData patient = treatment.Patient;

            float amount = treatment.ConsumeDailyRecovery();
            manager.TrySetInjuryGauge(patient.RuntimeId, patient.InjuryGauge - amount, out _);
            changed = true;

            // 완치 판정은 0이 건강한 심각도 게이지 기준(진실원천).
            if (patient.InjuryGauge <= 0.0f)
                CompleteHealing(i);
        }

        if (changed)
            NotifyPatientSlotsChanged();
    }
    */

    private void CompleteHealing(int slotIndex)
    {
        MedicalTreatment treatment = patientTreatments[slotIndex];
        ShelterMemberRuntimeData patient = treatment.Patient;

        if (!TryGetCharacterManager(out CharacterManager manager))
            return;

        if (!manager.TryCompleteRecovery(patient.RuntimeId, out _))
            return;

        patientTreatments.RemoveAt(slotIndex);
        OnPatientHealed?.Invoke(patient);
    }

    // 일일 회복량 = 기본 + 배치된 헬퍼들의 타입별 보너스 합 (게이지 %/일).
    private float GetDailyRecovery()
    {
        int bonus = 0;
        foreach (ShelterMemberRuntimeData helper in helpers)
            bonus += GetHelperBonus(helper.Type);
        int shortageDecrease = m_isFuelShortageActive
            ? fuelShortageEfficiencyDecrease
            : 0;
        return Mathf.Max(1, baseRecoveryPerDay + bonus - shortageDecrease);
    }

    private int GetHelperBonus(NPCType type)
    {
        foreach (var b in helperRecoveryBonuses)
            if (b.type == type) return b.bonusPercent;
        return 0;
    }

    // 헬퍼 배치/해제로 회복률이 바뀌면 활성 환자 계획을 현재 게이지 기준으로 재산출한다.
    private void RecalculateAllTreatmentPlans()
    {
        float daily = GetDailyRecovery();
        for (int i = 0; i < patientTreatments.Count; i++)
            patientTreatments[i].Recalculate(daily);
    }

    private int FindPatientSlotIndex(string runtimeId)
    {
        if (string.IsNullOrWhiteSpace(runtimeId))
            return -1;

        string normalizedRuntimeId = runtimeId.Trim();
        for (int i = 0; i < patientTreatments.Count; i++)
        {
            MedicalTreatment treatment = patientTreatments[i];
            if (treatment.Patient != null && treatment.Patient.RuntimeId == normalizedRuntimeId)
                return i;
        }

        return -1;
    }

    private bool TryGetCharacterManager(out CharacterManager manager)
    {
        manager = CacheCharacterManager();
        if (manager != null)
            return true;

        Debug.LogWarning("[MedicalManager] CharacterManager is not available.", this);
        return false;
    }

    private CharacterManager CacheCharacterManager()
    {
        if (characterManager == null)
            characterManager = CharacterManager.Instance;

        if (characterManager == null)
            characterManager = FindFirstObjectByType<CharacterManager>();

        return characterManager;
    }

    // 레벨별 효과는 시설 특수 정보이므로 코드에 하드코딩(절대값). 확장 = case 추가 + MaxLevelIndex.
    private static int PatientSlotsForLevel(int level) => level switch
    {
        0 => 1,
        1 => 2,
        2 => 3,
        3 => 4,
        _ => 1
    };

    private static int HelperSlotsForLevel(int level) => level switch
    {
        0 => 1,
        1 => 1,
        2 => 2,
        3 => 2,
        _ => 1
    };

    // 다음 레벨이 제공하는 기능 표시 줄들. 값이 바뀌는 항목만 노출(shown=actual — 슬롯 함수에서 파생).
    public System.Collections.Generic.IReadOnlyList<FacilityFeatureLine> GetUpgradeFeatureLines(int currentLevel)
    {
        var lines = new System.Collections.Generic.List<FacilityFeatureLine>();
        int next = currentLevel + 1;
        if (next > MaxLevelIndex)
            return lines;

        if (PatientSlotsForLevel(next) != PatientSlotsForLevel(currentLevel))
            lines.Add(new FacilityFeatureLine("환자 슬롯",
                $"{PatientSlotsForLevel(currentLevel)} → {PatientSlotsForLevel(next)}"));

        if (HelperSlotsForLevel(next) != HelperSlotsForLevel(currentLevel))
            lines.Add(new FacilityFeatureLine("헬퍼 슬롯",
                $"{HelperSlotsForLevel(currentLevel)} → {HelperSlotsForLevel(next)}"));

        return lines;
    }

    private void NotifyPatientSlotsChanged()
    {
        RefreshPatientStatuses();
        OnPatientSlotsChanged?.Invoke();
    }

    /// <summary>
    /// 외부 노출용 투영본인 patientStatuses 삭제 및 재생성( 원본에서 매번 재생성 함 )
    /// </summary>
    private void RefreshPatientStatuses()
    {
        patientStatuses.Clear();
        for (int i = 0; i < patientTreatments.Count; i++)
        {
            MedicalTreatment treatment = patientTreatments[i];
            patientStatuses.Add(new PatientStatus(treatment.Patient, treatment.RemainingDays, treatment.TotalDays));
        }
    }

    // 업그레이드 비용 데이터(인스펙터 편집용). 한 단계(레벨 i→i+1)에서 요구하는 자원들.
    [System.Serializable]
    private struct UpgradeCostEntry
    {
        public string resourceId;
        public int amount;
    }

    [System.Serializable]
    private struct UpgradeCostTier
    {
        public UpgradeCostEntry[] entries;
    }

    private sealed class MedicalTreatment
    {
        public ShelterMemberRuntimeData Patient { get; }
        public int RemainingDays { get; private set; }
        public int TotalDays { get; private set; }

        private readonly float maxGauge;
        private float dailyRecovery;
        private float nextRecoveryAmount;

        public MedicalTreatment(ShelterMemberRuntimeData patient, float dailyRecovery)
        {
            Patient = patient;
            maxGauge = patient.MaxInjuryGauge;
            Recalculate(dailyRecovery);
        }

        // 현재 게이지 기준으로 치료 계획을 (재)산출한다. 계산은 TreatmentDurationCalculator에 위임한다.
        public void Recalculate(float daily)
        {
            TreatmentPlan plan = TreatmentDurationCalculator.Calculate(Patient.InjuryGauge, maxGauge, daily);
            dailyRecovery = plan.DailyRecovery;
            TotalDays = plan.TotalDays;
            RemainingDays = plan.TotalDays;
            nextRecoveryAmount = plan.FirstTickRecovery;
        }

        // 이번 날 회복량을 반환하고 남은 일수/다음 회복량을 진행시킨다.
        public float ConsumeDailyRecovery()
        {
            float amount = nextRecoveryAmount;
            nextRecoveryAmount = dailyRecovery;
            RemainingDays = Mathf.Max(0, RemainingDays - 1);
            return amount;
        }
    }
}


public readonly struct PatientStatus
{
    /// <summary>
    /// 의료 UI에 표시할 환자 상태 투영본을 생성
    /// </summary>
    /// <param name="patient">표시할 환자 NPC</param>
    /// <param name="remainingDays">남은 치료 일수</param>
    /// <param name="totalDays">총 치료 일수</param>
    public PatientStatus(ShelterMemberRuntimeData patient, int remainingDays, int totalDays)
    {
        Patient = patient;
        RemainingDays = remainingDays;
        TotalDays = totalDays;
    }

    /// <summary>표시 대상 환자 NPC</summary>
    public ShelterMemberRuntimeData Patient { get; }

    /// <summary>남은 치료 일수</summary>
    public int RemainingDays { get; }

    /// <summary>처음 산출된 총 치료 일수</summary>
    public int TotalDays { get; }

    /// <summary>현재 환자 부상 게이지</summary>
    public float InjuryGauge => Patient != null ? Patient.InjuryGauge : 0f;

    /// <summary>환자 부상 게이지의 최대값</summary>
    public float MaxInjuryGauge => Patient != null ? Patient.MaxInjuryGauge : 1f;

    /// <summary>UI 게이지 표시용 0~1 정규화 값</summary>
    public float GaugeNormalized => MaxInjuryGauge > 0f ? Mathf.Clamp01(InjuryGauge / MaxInjuryGauge) : 0f;

    /// <summary>현재 환자 부상 상태</summary>
    public CharacterInjuryState InjuryState => Patient != null ? Patient.InjuryState : CharacterInjuryState.Normal;

    /// <summary>UI에 표시할 환자 이름</summary>
    public string DisplayName => Patient != null
        ? Patient.DisplayName
        : string.Empty;
}


/// <summary>
/// NPC 타입별 의료 헬퍼 일일 회복 보너스 설정
/// </summary>
[System.Serializable]
public struct HelperRecoveryBonus
{
    /// <summary>보너스를 적용할 NPC 타입</summary>
    public NPCType type;

    /// <summary>일일 회복량에 더할 보너스 수치</summary>
    [FormerlySerializedAs("daysReduction")]
    public int bonusPercent;
}
