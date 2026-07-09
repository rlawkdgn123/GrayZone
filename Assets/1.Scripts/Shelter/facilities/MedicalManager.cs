using UnityEngine;
using UnityEngine.Serialization;
using System.Collections.Generic;

public class MedicalManager : MonoBehaviour, IFacilityUpgradeable
{
    private const int MaxLevelIndex = 3; // 레벨 4단계 (인덱스 0,1,2,3)

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
    [FormerlySerializedAs("staffHealBonuses")]
    [SerializeField] private HelperRecoveryBonus[] helperRecoveryBonuses = new HelperRecoveryBonus[]
    {
        new HelperRecoveryBonus { type = NPCType.Tanker, bonusPercent = 1 },
        new HelperRecoveryBonus { type = NPCType.Healer, bonusPercent = 2 },
        new HelperRecoveryBonus { type = NPCType.Dealer, bonusPercent = 1 }
    };

    private readonly List<MedicalTreatment> patientTreatments = new List<MedicalTreatment>();
    private readonly List<PatientStatus> patientStatuses = new List<PatientStatus>();
    private readonly List<NPCRuntimeData> helpers = new List<NPCRuntimeData>();
    private bool m_isUnlocked = true; // FacilityManager가 세이브 기준으로 덮어씀(의료시설 기본 해금)

    public event System.Action<NPCRuntimeData> OnHelperAssigned;
    public event System.Action<NPCRuntimeData> OnHelperReleased;
    public event System.Action<NPCRuntimeData> OnPatientHealed;
    /// <summary>
    /// MedicalUI.Refresh() 호출 함수 ( UI 갱신용 )
    /// </summary>
    public event System.Action OnPatientSlotsChanged;

    public IReadOnlyList<PatientStatus> PatientStatuses
    {
        get
        {
            RefreshPatientStatuses();
            return patientStatuses;
        }
    }
    public int CurrentPatientCount => patientTreatments.Count;
    public int MaxPatientCount => PatientCapacity;
    public int PatientCapacity => PatientSlotsForLevel(CurrentLevel);
    public int MaxPatientCapacity => PatientSlotsForLevel(MaxLevelIndex);
    public int UnlockedPatientSlotCount => PatientCapacity;
    public int LockedPatientSlotCount => MaxPatientCapacity - PatientCapacity;
    public int CurrentHelperCount => helpers.Count;
    public int MaxHelperCount => HelperCapacity;
    public int HelperCapacity => HelperSlotsForLevel(CurrentLevel);
    public int PatientUpgrade => CurrentLevel;
    public int UpgradeLevel => CurrentLevel;
    public int MaxUpgradeLevel => MaxLevelIndex;

    // 레벨은 FacilityManager(진실원천)에서 읽는다 — MedicalManager는 캐시하지 않는다.
    private int CurrentLevel =>
        FacilityManager.Instance != null ? FacilityManager.Instance.GetUpgradeLevel(FacilityId) : 0;

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
    }

    private void OnValidate()
    {
        baseRecoveryPerDay = Mathf.Max(1, baseRecoveryPerDay);
    }

    private void Start()
    {
        if (GameDateManager.Instance != null)
            GameDateManager.Instance.DayAdvanced += OnDayAdvanced;

        // 등록 즉시 FacilityManager가 세이브 기준 해금/레벨을 이 시설에 반영한다.
        FacilityManager.Instance?.Register(this);
    }

    private void OnDestroy()
    {
        if (GameDateManager.Instance != null)
            GameDateManager.Instance.DayAdvanced -= OnDayAdvanced;

        FacilityManager.Instance?.Unregister(this);
    }

    // FacilityManager가 레벨 변경(로드/업그레이드) 후 호출. 레벨 자체는 저장하지 않고
    // FacilityManager(진실원천)에서 읽으므로, 여기서는 표시(비주얼·슬롯)만 갱신한다.
    public void ApplyUpgradeLevel(int level)
    {
        RefreshLevelVisuals();
        NotifyPatientSlotsChanged();
    }

    // FacilityManager가 세이브에서 복원한 해금 상태를 반영한다.
    public void ApplyUnlockState(bool isUnlocked)
    {
        m_isUnlocked = isUnlocked;
        RefreshLevelVisuals();
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

    // 현재 레벨 → 다음 레벨 업그레이드 비용(FacilityManager가 차감 시 조회). 범위 밖/미설정이면 무료.
    public CostBundle GetUpgradeCost(int currentLevel)
    {
        if (upgradeCosts == null || currentLevel < 0 || currentLevel >= upgradeCosts.Length)
            return new CostBundle();

        UpgradeCostEntry[] entries = upgradeCosts[currentLevel].entries;
        if (entries == null || entries.Length == 0)
            return new CostBundle();

        CurrencyCost[] costs = new CurrencyCost[entries.Length];
        for (int i = 0; i < entries.Length; i++)
            costs[i] = new CurrencyCost(entries[i].type, entries[i].amount);

        return new CostBundle(costs);
    }

    // 자원 이외의 업그레이드 조건. 현재는 없음(항상 허용). 비자원 조건이 생기면 여기서 판단.
    public bool AreUpgradeRequirementsMet(int currentLevel) => true;

    public void FillPatientCandidates(List<NPCRuntimeData> results)
    {
        if (results == null)
            throw new System.ArgumentNullException(nameof(results));

        results.Clear();

        if (!TryGetCharacterManager(out CharacterManager manager))
            return;

        foreach (NPCRuntimeData character in manager.Characters)
        {
            if (CanAssignPatient(character))
                results.Add(character);
        }
    }

    public bool CanAssignPatient(NPCRuntimeData character)
    {
        if (character == null)
            return false;

        if (patientTreatments.Count >= PatientCapacity)
            return false;

        if (FindPatientSlotIndex(character) >= 0)
            return false;

        // 완치(Healthy)면 치료 불필요 (enum 기준).
        if (character.GetCurrentInjuryState() == NPCInjuryState.Healthy)
            return false;

        return !character.GetIsAssignedToShelter();
    }

    public bool TryAssignPatient(NPCRuntimeData target)
    {
        return target != null && TryAssignPatient(target.DefinitionId);
    }

    public bool TryAssignPatient(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId)) return false;
        //몇번째 슬롯에 있는지(슬롯에 없는 id면 -1 return)
        if (FindPatientSlotIndex(definitionId) >= 0) return true;

        //목록에 있는 숫자가 최대치 보다 높을경우 오류상태
        if (patientTreatments.Count >= PatientCapacity) return false;

        if (!TryGetCharacterManager(out CharacterManager manager)) return false;

        //셸터 데이터에서 NPC를 관리하는 CharacterManager로 부터 데이터를 가져오는 함수
        if (!manager.TryGetCharacter(definitionId, out NPCRuntimeData target))
            return false;

        if (!CanAssignPatient(target))
            return false;

        if (!manager.TryAssignToFacility(
                definitionId,
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

    public bool TryReleasePatient(NPCRuntimeData target)
    {
        return target != null && TryReleasePatient(target.DefinitionId);
    }

    public bool TryReleasePatient(string definitionId)
    {
        int slotIndex = FindPatientSlotIndex(definitionId);
        if (slotIndex < 0) return false;
        if (!TryGetCharacterManager(out CharacterManager manager)) return false;

        MedicalTreatment treatment = patientTreatments[slotIndex];
        if (!manager.TryReleaseFromFacility(treatment.Patient.DefinitionId, out _))
            return false;

        // 중도 해제 → 현재 게이지 기준으로 부상상태 갱신
        manager.TryRefreshInjuryState(treatment.Patient.DefinitionId, out _);
        patientTreatments.RemoveAt(slotIndex);
        NotifyPatientSlotsChanged();
        return true;
    }

    public int GetPatientHealDaysRemaining(NPCRuntimeData patient)
    {
        int slotIndex = FindPatientSlotIndex(patient);
        return slotIndex >= 0 ? patientTreatments[slotIndex].RemainingDays : 0;
    }

    public void FillHelperCandidates(List<NPCRuntimeData> results)
    {
        if (results == null)
            throw new System.ArgumentNullException(nameof(results));

        results.Clear();

        if (!TryGetCharacterManager(out CharacterManager manager))
            return;

        foreach (NPCRuntimeData character in manager.Characters)
        {
            if (CanAssignHelper(character))
                results.Add(character);
        }
    }

    public bool CanAssignHelper(NPCRuntimeData character)
    {
        if (character == null)
            return false;

        if (helpers.Count >= HelperCapacity)
            return false;

        if (FindHelperIndex(character) >= 0)
            return false;

        // 도우미는 건강 또는 경상만 가능 (중상·위독 제외).
        NPCInjuryState state = character.GetCurrentInjuryState();
        if (state != NPCInjuryState.Healthy && state != NPCInjuryState.LightInjury)
            return false;

        return !character.GetIsAssignedToShelter();
    }

    public bool TryAssignHelper(NPCRuntimeData target)
    {
        return target != null && TryAssignHelper(target.DefinitionId);
    }

    public bool TryAssignHelper(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId)) return false;
        if (FindHelperIndex(definitionId) >= 0) return true;

        if (helpers.Count >= HelperCapacity) return false;
        if (!TryGetCharacterManager(out CharacterManager manager)) return false;

        if (!manager.TryGetCharacter(definitionId, out NPCRuntimeData target))
            return false;

        if (!CanAssignHelper(target))
            return false;

        if (!manager.TryAssignToFacility(
                definitionId,
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
        OnHelperAssigned?.Invoke(target);
        NotifyPatientSlotsChanged();
        return true;
    }

    public bool TryReleaseHelper(NPCRuntimeData target)
    {
        return target != null && TryReleaseHelper(target.DefinitionId);
    }

    public bool TryReleaseHelper(string definitionId)
    {
        int index = FindHelperIndex(definitionId);
        if (index < 0) return false;
        if (!TryGetCharacterManager(out CharacterManager manager)) return false;

        NPCRuntimeData helper = helpers[index];
        if (!manager.TryReleaseFromFacility(helper.DefinitionId, out _))
            return false;

        helpers.RemoveAt(index);
        RecalculateAllTreatmentPlans();
        OnHelperReleased?.Invoke(helper);
        NotifyPatientSlotsChanged();
        return true;
    }

    private int FindHelperIndex(NPCRuntimeData helper)
    {
        return helper == null ? -1 : FindHelperIndex(helper.DefinitionId);
    }

    private int FindHelperIndex(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            return -1;

        string normalizedDefinitionId = definitionId.Trim();
        for (int i = 0; i < helpers.Count; i++)
        {
            NPCRuntimeData helper = helpers[i];
            if (helper != null && helper.DefinitionId == normalizedDefinitionId)
                return i;
        }

        return -1;
    }

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
            NPCRuntimeData patient = treatment.Patient;

            float amount = treatment.ConsumeDailyRecovery();
            manager.TrySetInjuryGauge(patient.DefinitionId, patient.InjuryGauge + amount, out _);
            changed = true;

            // 완치 판정은 게이지 기준(진실원천). 아이템 등 치료 외 경로로 게이지가 차도 즉시 완치된다.
            if (patient.InjuryGauge >= patient.MaxInjuryGauge)
                CompleteHealing(i);
        }

        if (changed)
            NotifyPatientSlotsChanged();
    }

    private void CompleteHealing(int slotIndex)
    {
        MedicalTreatment treatment = patientTreatments[slotIndex];
        NPCRuntimeData patient = treatment.Patient;

        if (!TryGetCharacterManager(out CharacterManager manager))
            return;

        if (!manager.TryCompleteRecovery(patient.DefinitionId, out _))
            return;

        patientTreatments.RemoveAt(slotIndex);
        OnPatientHealed?.Invoke(patient);
    }

    // 일일 회복량 = 기본 + 배치된 헬퍼들의 타입별 보너스 합 (게이지 %/일).
    private float GetDailyRecovery()
    {
        int bonus = 0;
        foreach (NPCRuntimeData helper in helpers)
            bonus += GetHelperBonus(helper.Type);
        return Mathf.Max(1, baseRecoveryPerDay + bonus);
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

    private int FindPatientSlotIndex(NPCRuntimeData patient)
    {
        if (patient == null)
            return -1;

        return FindPatientSlotIndex(patient.DefinitionId);
    }

    private int FindPatientSlotIndex(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            return -1;

        string normalizedDefinitionId = definitionId.Trim();
        for (int i = 0; i < patientTreatments.Count; i++)
        {
            MedicalTreatment treatment = patientTreatments[i];
            if (treatment.Patient != null && treatment.Patient.DefinitionId == normalizedDefinitionId)
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
        public CurrencyType type;
        public int amount;
    }

    [System.Serializable]
    private struct UpgradeCostTier
    {
        public UpgradeCostEntry[] entries;
    }

    private sealed class MedicalTreatment
    {
        public NPCRuntimeData Patient { get; }
        public int RemainingDays { get; private set; }
        public int TotalDays { get; private set; }

        private readonly float maxGauge;
        private float dailyRecovery;
        private float nextRecoveryAmount;

        public MedicalTreatment(NPCRuntimeData patient, float dailyRecovery)
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
    public PatientStatus(NPCRuntimeData patient, int remainingDays, int totalDays)
    {
        Patient = patient;
        RemainingDays = remainingDays;
        TotalDays = totalDays;
    }

    public NPCRuntimeData Patient { get; }
    public int RemainingDays { get; }
    public int TotalDays { get; }

    // UI 게이지/상태 표시용 (게이지가 진실원천).
    public float InjuryGauge => Patient != null ? Patient.InjuryGauge : 0f;
    public float MaxInjuryGauge => Patient != null ? Patient.MaxInjuryGauge : 1f;
    public float GaugeNormalized => MaxInjuryGauge > 0f ? Mathf.Clamp01(InjuryGauge / MaxInjuryGauge) : 0f;
    public NPCInjuryState InjuryState => Patient != null ? Patient.GetCurrentInjuryState() : NPCInjuryState.Healthy;
    public string DisplayName => Patient != null
        ? (Patient.NPCData != null ? Patient.NPCData.name : Patient.DefinitionId)
        : string.Empty;
}


// 헬퍼 타입별 일일 회복 보너스(%)
[System.Serializable]
public struct HelperRecoveryBonus
{
    public NPCType type;
    [FormerlySerializedAs("daysReduction")]
    public int bonusPercent;
}
