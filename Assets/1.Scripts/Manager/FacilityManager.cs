using UnityEngine;
using System.Collections.Generic;

public class FacilityManager : MonoBehaviour
{
    [SerializeField] private List<FacilityDefinition> m_definitions = new();

    private readonly Dictionary<string, FacilityState> m_states = new();

    // 씬의 시설 인스턴스(각자 Register로 등록). facilityId → 시설.
    private readonly Dictionary<string, IFacilityUpgradeable> m_facilities = new();

    public static FacilityManager Instance { get; private set; }

    public IReadOnlyDictionary<string, FacilityState> States => m_states;

    private void Awake()
    {
        if (Instance != null && Instance != this)
            Debug.LogWarning("[FacilityManager] Another instance already exists; overwriting Instance.", this);
        Instance = this;

        m_states.Clear();

        foreach (FacilityDefinition definition in m_definitions)
        {
            if (definition == null)
                continue;

            if (string.IsNullOrWhiteSpace(definition.FacilityId))
            {
                Debug.LogWarning("[FacilityManager] FacilityDefinition has empty FacilityId.", definition);
                continue;
            }

            FacilityRuntimeState runtimeState = GetOrCreateRuntimeState(definition);
            if (runtimeState == null)
                continue;

            m_states[definition.FacilityId] = new FacilityState(definition, runtimeState);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public FacilityState GetState(string facilityId)
        => m_states.TryGetValue(facilityId, out FacilityState state) ? state : null;

    // 시설의 현재 업그레이드 레벨(진실원천). 상태가 없으면 0.
    public int GetUpgradeLevel(string facilityId)
    {
        FacilityState state = GetState(facilityId);
        return state != null ? state.UpgradeLevel : 0;
    }

    // 시설 인스턴스가 스스로 등록한다(보통 자신의 Start에서). 등록 즉시
    // 세이브에서 복원된 해금/레벨 상태를 그 시설에 밀어준다.
    public void Register(IFacilityUpgradeable facility)
    {
        if (facility == null)
            return;

        string facilityId = facility.FacilityId;
        if (string.IsNullOrWhiteSpace(facilityId))
        {
            Debug.LogWarning("[FacilityManager] Facility has empty FacilityId; cannot register.", this);
            return;
        }

        m_facilities[facilityId] = facility;
        ApplyPersistedState(facility);
    }

    public void Unregister(IFacilityUpgradeable facility)
    {
        if (facility == null)
            return;

        string facilityId = facility.FacilityId;
        if (!string.IsNullOrWhiteSpace(facilityId)
            && m_facilities.TryGetValue(facilityId, out IFacilityUpgradeable registered)
            && registered == facility)
        {
            m_facilities.Remove(facilityId);
        }
    }

    // 세이브에서 복원된 시설 상태(해금/레벨)를 해당 시설 인스턴스에 반영한다.
    // 상태(정의)가 없으면 시설의 기본값을 유지한다.
    private void ApplyPersistedState(IFacilityUpgradeable facility)
    {
        FacilityState state = GetState(facility.FacilityId);
        if (state == null)
        {
            Debug.LogWarning($"[FacilityManager] No FacilityState for '{facility.FacilityId}'. " +
                             "Check that its FacilityDefinition is in the definitions list.", this);
            return;
        }

        facility.ApplyUnlockState(state.IsUnlocked);
        facility.ApplyUpgradeLevel(state.UpgradeLevel);
    }

    public bool TryUnlock(string facilityId)
    {
        FacilityState state = GetState(facilityId);
        if (state == null || state.IsUnlocked) return false;

        CostBundle cost = state.Definition.BuildUnlockCost();
        if (!cost.IsFree)
        {
            if (ShelterDataManager.Instance == null)
            {
                Debug.LogWarning("[FacilityManager] ShelterDataManager is not available. Cannot spend unlock cost.", this);
                return false;
            }

            if (!ShelterDataManager.Instance.TrySpendResources(cost))
                return false;
        }

        state.Unlock();
        ShelterDataManager.Instance?.MarkDirty();
        return true;
    }

    // 업그레이드 구조/조건 상태(자원 제외): 해금됨 && 레벨<최대 && 시설 고유 비자원 조건 충족.
    public bool CanUpgrade(string facilityId)
    {
        FacilityState state = GetState(facilityId);
        if (state == null || !state.IsUnlocked)
            return false;

        if (!m_facilities.TryGetValue(facilityId, out IFacilityUpgradeable facility))
            return false;

        if (state.UpgradeLevel >= facility.MaxUpgradeLevel)
            return false;

        return facility.AreUpgradeRequirementsMet(state.UpgradeLevel);
    }

    // 현재 레벨에서 다음 레벨로 올리는 비용(UI 표시용). 대상이 없으면 무료 번들.
    public CostBundle GetUpgradeCost(string facilityId)
    {
        FacilityState state = GetState(facilityId);
        if (state == null || !m_facilities.TryGetValue(facilityId, out IFacilityUpgradeable facility))
            return new CostBundle();

        return facility.GetUpgradeCost(state.UpgradeLevel);
    }

    // 구조/조건 + 자원까지 충족되는가(UI 버튼 활성화 판단용).
    public bool CanAffordUpgrade(string facilityId)
    {
        if (!CanUpgrade(facilityId))
            return false;

        CostBundle cost = GetUpgradeCost(facilityId);
        if (cost.IsFree)
            return true;

        return ShelterDataManager.Instance != null
            && ShelterDataManager.Instance.CanSpendResources(cost);
    }

    // 실제 업그레이드 실행: 조건 검사 → 비용 차감 → 레벨+1(진실 갱신) → 시설 반영 → 저장 표시.
    public bool TryUpgrade(string facilityId)
    {
        if (!CanUpgrade(facilityId))
            return false;

        FacilityState state = GetState(facilityId);
        IFacilityUpgradeable facility = m_facilities[facilityId];

        CostBundle cost = facility.GetUpgradeCost(state.UpgradeLevel);
        if (!cost.IsFree)
        {
            if (ShelterDataManager.Instance == null)
            {
                Debug.LogWarning("[FacilityManager] ShelterDataManager is not available. Cannot spend upgrade cost.", this);
                return false;
            }

            if (!ShelterDataManager.Instance.TrySpendResources(cost))
                return false;
        }

        state.SetUpgradeLevel(state.UpgradeLevel + 1);
        facility.ApplyUpgradeLevel(state.UpgradeLevel);
        ShelterDataManager.Instance?.MarkDirty();
        return true;
    }

    private FacilityRuntimeState GetOrCreateRuntimeState(FacilityDefinition definition)
    {
        if (ShelterDataManager.Instance != null)
        {
            return ShelterDataManager.Instance.GetOrCreateFacilityState(
                definition.FacilityId,
                definition.UnlockedByDefault);
        }

        Debug.LogWarning("[FacilityManager] ShelterDataManager is not available. Facility state will not be persistent.", this);
        return new FacilityRuntimeState(definition.FacilityId, definition.UnlockedByDefault);
    }
}
