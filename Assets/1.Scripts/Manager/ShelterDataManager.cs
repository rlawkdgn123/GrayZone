using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-200)]
public class ShelterDataManager : MonoBehaviour
{
    public static ShelterDataManager Instance { get; private set; }

    [Header("Shelter Working Data")]
    [SerializeField] private SharedRuntimeData sharedWorkingData = new SharedRuntimeData();
    [SerializeField] private ShelterRuntimeData shelterData = new ShelterRuntimeData();

    public event Action ShelterDataChanged;

    private SharedRuntimeData SharedData
    {
        get
        {
            EnsureSharedWorkingData();
            return sharedWorkingData;
        }
    }

    private ShelterRuntimeData ShelterData
    {
        get
        {
            EnsureShelterData();
            return shelterData;
        }
    }

    private ResourceStorage Resources => SharedData.Resources;
    private NpcRoster NpcRoster => SharedData.NpcRoster;
    public IReadOnlyDictionary<CurrencyType, int> ResourceAmounts => Resources.Amounts;
    public IReadOnlyList<NPCRuntimeData> Npcs => NpcRoster.All;
    public int RosterCount => SharedData.RosterCount;
    public int TotalOwnedCharacterCount => SharedData.TotalOwnedCharacterCount;
    public int PlayableCharacterCount => SharedData.PlayableCharacterCount;
    public int NonPlayableNpcCount => SharedData.NonPlayableNpcCount;
    public IReadOnlyList<string> BattleSquadNpcRuntimeIds => ShelterData.BattleSquadNpcRuntimeIds;
    public int CurrentDay => ShelterData.CurrentDay;
    public int ShelterStability => SharedData.ShelterStability;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        EnsureSharedWorkingData();
        EnsureShelterData();

        if (GameDataManager.Instance != null)
        {
            GameDataManager.Instance.RegisterShelterDataManager(this);
        }
        else
        {
            Debug.LogWarning("[ShelterDataManager] GameDataManager.Instance is null.");
        }
    }

    private void OnDestroy()
    {
        if (GameDataManager.Instance != null)
        {
            GameDataManager.Instance.UnregisterShelterDataManager(this);
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void CopySharedDataFromGameDataManager()
    {
        if (GameDataManager.Instance == null)
        {
            Debug.LogWarning("[ShelterDataManager] GameDataManager.Instance is null.");
            return;
        }

        ApplySharedSnapshot(GameDataManager.Instance.CreateSharedSnapshot());
    }

    public void CopyFromDataManager()
    {
        if (GameDataManager.Instance == null)
        {
            Debug.LogWarning("[ShelterDataManager] GameDataManager.Instance is null.");
            return;
        }

        ApplySharedSnapshot(GameDataManager.Instance.CreateSharedSnapshot());
        ApplySnapshot(GameDataManager.Instance.CreateShelterSnapshot());
    }

    public bool PushToDataManager()
    {
        if (GameDataManager.Instance == null)
        {
            Debug.LogWarning("[ShelterDataManager] GameDataManager.Instance is null.");
            return false;
        }

        return GameDataManager.Instance.SyncFromShelter(this);
    }

    public ShelterRuntimeData CreateSnapshot()
    {
        return ShelterData.Clone();
    }

    public SharedRuntimeData CreateSharedSnapshot()
    {
        return SharedData.Clone();
    }

    public void ApplySnapshot(ShelterRuntimeData snapshot)
    {
        if (snapshot == null)
        {
            Debug.LogWarning("[ShelterDataManager] Snapshot is null.");
            return;
        }

        ShelterData.CopyFrom(snapshot);
        NotifyShelterDataChanged();
    }

    public void ApplySharedSnapshot(SharedRuntimeData snapshot)
    {
        if (snapshot == null)
        {
            Debug.LogWarning("[ShelterDataManager] Shared snapshot is null.");
            return;
        }

        SharedData.CopyFrom(snapshot);
        NotifyShelterDataChanged();
    }

    public FacilityRuntimeState GetOrCreateFacilityState(string facilityId, bool isUnlockedByDefault)
    {
        FacilityRuntimeState state = ShelterData.GetOrCreateFacilityState(facilityId, isUnlockedByDefault);
        NotifyShelterDataChanged();
        return state;
    }

    public bool TryRecruitNpc(NPCChar npcData, out NPCRuntimeData runtimeNpc)
    {
        bool result = NpcRoster.TryAdd(npcData, out runtimeNpc);
        if (result)
        {
            SharedData.RefreshCountsFromRosterAsPlayable();
            NotifyShelterDataChanged();
        }

        return result;
    }

    public bool TryGetNpc(string runtimeId, out NPCRuntimeData runtimeNpc)
    {
        return NpcRoster.TryGet(runtimeId, out runtimeNpc);
    }

    public bool TryRemoveNpc(string runtimeId)
    {
        if (!NpcRoster.Remove(runtimeId))
        {
            return false;
        }

        SharedData.RemoveNpcReferences(runtimeId);
        ShelterData.RemoveNpcReferences(runtimeId);
        SharedData.RefreshCountsFromRosterAsPlayable();
        NotifyShelterDataChanged();
        return true;
    }

    public int GetResourceAmount(CurrencyType type)
    {
        return Resources.GetAmount(type);
    }

    public bool CanSpendResource(CurrencyCost cost)
    {
        return Resources.CanSpend(cost);
    }

    public bool CanSpendResources(CostBundle costBundle)
    {
        if (costBundle == null || costBundle.IsFree)
            return true;

        foreach (CurrencyCost cost in costBundle.Costs)
        {
            if (!CanSpendResource(cost))
                return false;
        }

        return true;
    }

    public bool TrySpendResource(CurrencyCost cost)
    {
        bool result = Resources.TrySpend(cost);
        if (result)
            NotifyShelterDataChanged();

        return result;
    }

    public bool TrySpendResources(CostBundle costBundle)
    {
        if (!CanSpendResources(costBundle))
            return false;

        if (costBundle == null || costBundle.IsFree)
            return true;

        foreach (CurrencyCost cost in costBundle.Costs)
        {
            Resources.TrySpend(cost);
        }

        NotifyShelterDataChanged();
        return true;
    }

    public bool TryAddResource(CurrencyType type, int amount)
    {
        bool result = Resources.Add(type, amount);
        if (result)
            NotifyShelterDataChanged();

        return result;
    }

    public void SetResourceAmount(CurrencyType type, int amount)
    {
        Resources.SetAmount(type, amount);
        NotifyShelterDataChanged();
    }

    public bool TrySetBattleSquad(IEnumerable<string> runtimeIds)
    {
        if (!CanUseBattleSquad(runtimeIds))
            return false;

        bool result = ShelterData.TrySetBattleSquad(runtimeIds);
        if (result)
            NotifyShelterDataChanged();

        return result;
    }

    public bool TryAddBattleSquadNpc(string runtimeId)
    {
        if (!CanUseBattleSquadNpc(runtimeId))
            return false;

        bool result = ShelterData.TryAddBattleSquadNpc(runtimeId);
        if (result)
            NotifyShelterDataChanged();

        return result;
    }

    public bool TryRemoveBattleSquadNpc(string runtimeId)
    {
        bool result = ShelterData.TryRemoveBattleSquadNpc(runtimeId);
        if (result)
            NotifyShelterDataChanged();

        return result;
    }

    public void ClearBattleSquad()
    {
        ShelterData.ClearBattleSquad();
        NotifyShelterDataChanged();
    }

    public void SetOwnedCharacterCounts(int playableCount, int nonPlayableNpcCount)
    {
        SharedData.SetOwnedCharacterCounts(playableCount, nonPlayableNpcCount);
        NotifyShelterDataChanged();
    }

    public void RefreshCountsFromRosterAsPlayable()
    {
        SharedData.RefreshCountsFromRosterAsPlayable();
        NotifyShelterDataChanged();
    }

    public void SetLastStageId(string stageId)
    {
        SharedData.SetLastStageId(stageId);
        NotifyShelterDataChanged();
    }

    public void SetCurrentDay(int day)
    {
        ShelterData.SetCurrentDay(day);
        NotifyShelterDataChanged();
    }

    public void SetShelterStability(int stability)
    {
        SharedData.SetShelterStability(stability);
        NotifyShelterDataChanged();
    }

    public void MarkDirty()
    {
        NotifyShelterDataChanged();
    }

    private void NotifyShelterDataChanged()
    {
        ShelterDataChanged?.Invoke();
    }

    private bool CanUseBattleSquad(IEnumerable<string> runtimeIds)
    {
        if (runtimeIds == null)
            return false;

        foreach (string runtimeId in runtimeIds)
        {
            if (string.IsNullOrWhiteSpace(runtimeId))
                continue;

            if (!CanUseBattleSquadNpc(runtimeId))
                return false;
        }

        return true;
    }

    private bool CanUseBattleSquadNpc(string runtimeId)
    {
        return !string.IsNullOrWhiteSpace(runtimeId) && NpcRoster.Contains(runtimeId);
    }

    private void EnsureSharedWorkingData()
    {
        sharedWorkingData ??= new SharedRuntimeData();
        sharedWorkingData.EnsureRuntimeContainers();
    }

    private void EnsureShelterData()
    {
        shelterData ??= new ShelterRuntimeData();
        shelterData.EnsureRuntimeContainers();
    }
}
