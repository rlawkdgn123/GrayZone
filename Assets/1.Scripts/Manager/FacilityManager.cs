using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 셸터 시설 정의와 런타임 상태를 연결하고 해금/업그레이드 명령을 처리하는 시설 관리자
/// </summary>
public class FacilityManager : MonoBehaviour
{
    [SerializeField] private List<FacilityDefinition> m_definitions = new();

    private readonly Dictionary<string, FacilityState> m_states = new();

    // 씬의 시설 인스턴스(각자 Register로 등록). facilityId → 시설.
    private readonly Dictionary<string, IFacilityUpgradeable> m_facilities = new();

    /// <summary>현재 씬의 시설 관리자 싱글톤 인스턴스</summary>
    public static FacilityManager Instance { get; private set; }

    /// <summary>시설 ID별 런타임 시설 상태</summary>
    public IReadOnlyDictionary<string, FacilityState> States => m_states;

    private StorageFacility Storage => ShelterSceneDataManager.Instance?.Storage;

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

    /// <summary>
    /// 시설 ID에 해당하는 시설 상태를 반환
    /// </summary>
    /// <param name="facilityId">조회할 시설 ID</param>
    /// <returns>등록된 시설 상태가 있으면 해당 상태, 없으면 <c>null</c></returns>
    public FacilityState GetState(string facilityId)
        => m_states.TryGetValue(facilityId, out FacilityState state) ? state : null;

    /// <summary>
    /// 시설의 현재 업그레이드 레벨을 반환
    /// </summary>
    /// <param name="facilityId">조회할 시설 ID</param>
    /// <returns>시설 상태가 있으면 해당 레벨, 없으면 0</returns>
    public int GetUpgradeLevel(string facilityId)
    {
        FacilityState state = GetState(facilityId);
        return state != null ? state.UpgradeLevel : 0;
    }

    /// <summary>
    /// 씬의 시설 인스턴스를 등록하고 저장된 해금/레벨 상태를 즉시 반영
    /// </summary>
    /// <param name="facility">등록할 시설 인스턴스</param>
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

        if (facility is IFuelShortageAffected affected)
        {
            affected.ApplyFuelShortageState(
                ShelterSceneDataManager.Instance?.FuelShortagePenaltyActive ?? false);
        }
    }

    public void ApplyFuelShortagePenalty(bool isActive)
    {
        foreach (IFacilityUpgradeable facility in m_facilities.Values)
        {
            if (facility is IFuelShortageAffected affected)
            {
                affected.ApplyFuelShortageState(isActive);
            }
        }
    }

    /// <summary>
    /// 씬의 시설 인스턴스 등록을 해제
    /// </summary>
    /// <param name="facility">해제할 시설 인스턴스</param>
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

    /// <summary>
    /// 잠긴 시설을 해금하고 필요한 자원을 차감
    /// </summary>
    /// <param name="facilityId">해금할 시설 ID</param>
    /// <returns>해금에 성공하면 <c>true</c></returns>
    public bool TryUnlock(string facilityId)
    {
        FacilityState state = GetState(facilityId);
        if (state == null || state.IsUnlocked) return false;

        CostBundle cost = state.Definition.BuildUnlockCost();
        if (!cost.IsFree)
        {
            if (Storage == null)
            {
                Debug.LogWarning("[FacilityManager] StorageFacility is not available. Cannot spend unlock cost.", this);
                return false;
            }

            if (!Storage.TrySpendResources(cost))
                return false;
        }

        state.Unlock();
        ShelterSceneDataManager.Instance?.MarkDirty();
        return true;
    }

    /// <summary>
    /// 자원 보유 여부를 제외한 시설 업그레이드 가능 조건을 검사
    /// </summary>
    /// <param name="facilityId">검사할 시설 ID</param>
    /// <returns>시설이 해금되어 있고 최대 레벨 전이며 고유 조건을 만족하면 <c>true</c></returns>
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

    /// <summary>
    /// 현재 레벨에서 다음 레벨로 올리는 비용을 반환
    /// </summary>
    /// <param name="facilityId">비용을 조회할 시설 ID</param>
    /// <returns>시설이 등록되어 있으면 해당 업그레이드 비용, 없으면 무료 묶음</returns>
    public CostBundle GetUpgradeCost(string facilityId)
    {
        FacilityState state = GetState(facilityId);
        if (state == null || !m_facilities.TryGetValue(facilityId, out IFacilityUpgradeable facility))
            return new CostBundle();

        return facility.GetUpgradeCost(state.UpgradeLevel);
    }

    /// <summary>
    /// 다음 레벨이 제공하는 기능 표시 줄들을 반환한다(표시 전용).
    /// </summary>
    /// <param name="facilityId">조회할 시설 ID</param>
    /// <returns>시설이 등록돼 있으면 해당 기능 줄들, 없거나 최대 레벨이면 빈 리스트</returns>
    public IReadOnlyList<FacilityFeatureLine> GetUpgradeFeatureLines(string facilityId)
    {
        FacilityState state = GetState(facilityId);
        if (state == null || !m_facilities.TryGetValue(facilityId, out IFacilityUpgradeable facility))
            return System.Array.Empty<FacilityFeatureLine>();

        return facility.GetUpgradeFeatureLines(state.UpgradeLevel);
    }

    /// <summary>
    /// 업그레이드 조건과 자원 보유 여부를 모두 검사
    /// </summary>
    /// <param name="facilityId">검사할 시설 ID</param>
    /// <returns>지금 업그레이드 비용까지 지불 가능하면 <c>true</c></returns>
    public bool CanAffordUpgrade(string facilityId)
    {
        if (!CanUpgrade(facilityId))
            return false;

        CostBundle cost = GetUpgradeCost(facilityId);
        if (cost.IsFree)
            return true;

        return Storage != null && Storage.CanSpendResources(cost);
    }

    /// <summary>
    /// 시설 업그레이드를 실행하고 비용 차감, 레벨 증가, 시설 반영, dirty 표시를 수행
    /// </summary>
    /// <param name="facilityId">업그레이드할 시설 ID</param>
    /// <returns>업그레이드에 성공하면 <c>true</c></returns>
    public bool TryUpgrade(string facilityId)
    {
        if (!CanUpgrade(facilityId))
            return false;

        FacilityState state = GetState(facilityId);
        IFacilityUpgradeable facility = m_facilities[facilityId];

        CostBundle cost = facility.GetUpgradeCost(state.UpgradeLevel);
        if (!cost.IsFree)
        {
            if (Storage == null)
            {
                Debug.LogWarning("[FacilityManager] StorageFacility is not available. Cannot spend upgrade cost.", this);
                return false;
            }

            if (!Storage.TrySpendResources(cost))
                return false;
        }

        state.SetUpgradeLevel(state.UpgradeLevel + 1);
        facility.ApplyUpgradeLevel(state.UpgradeLevel);
        ShelterSceneDataManager.Instance?.MarkDirty();
        return true;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>
    /// Development trainer entry point. Forces a facility to an unlocked level
    /// without charging resources, then applies the level to its live visuals.
    /// </summary>
    public bool TrySetUpgradeLevelForDebug(string facilityId, int upgradeLevel)
    {
        FacilityState state = GetState(facilityId);
        if (state == null
            || !m_facilities.TryGetValue(
                facilityId,
                out IFacilityUpgradeable facility))
        {
            return false;
        }

        int clampedLevel = Mathf.Clamp(
            upgradeLevel,
            0,
            facility.MaxUpgradeLevel);

        state.Unlock();
        state.SetUpgradeLevel(clampedLevel);
        facility.ApplyUnlockState(true);
        facility.ApplyUpgradeLevel(clampedLevel);
        ShelterSceneDataManager.Instance?.MarkDirty();
        return true;
    }

    /// <summary>Returns the registered facility's maximum internal level index.</summary>
    public int GetMaxUpgradeLevelForDebug(string facilityId)
    {
        return !string.IsNullOrWhiteSpace(facilityId)
            && m_facilities.TryGetValue(
                facilityId,
                out IFacilityUpgradeable facility)
            ? facility.MaxUpgradeLevel
            : -1;
    }
#endif

    private FacilityRuntimeState GetOrCreateRuntimeState(FacilityDefinition definition)
    {
        if (ShelterSceneDataManager.Instance != null)
        {
            return ShelterSceneDataManager.Instance.GetOrCreateFacilityState(
                definition.FacilityId,
                definition.UnlockedByDefault);
        }

        Debug.LogWarning("[FacilityManager] ShelterSceneDataManager is not available. Facility state will not be persistent.", this);
        return new FacilityRuntimeState(definition.FacilityId, definition.UnlockedByDefault);
    }
}
