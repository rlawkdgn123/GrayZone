using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-300)]
public class GameDataManager : MonoBehaviour
{
    public static GameDataManager Instance { get; private set; }

    [Header("Shared Runtime Baseline Data")]
    [SerializeField] private SharedRuntimeData sharedData = new SharedRuntimeData();

    [Header("Scene Runtime Backup Data")]
    [SerializeField] private ShelterRuntimeData shelterData = new ShelterRuntimeData();

    private ShelterDataManager activeShelterDataManager;

    private SharedRuntimeData SharedData
    {
        get
        {
            EnsureSharedData();
            return sharedData;
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

    public int RosterCount => SharedData.RosterCount;
    public int TotalOwnedCharacterCount => SharedData.TotalOwnedCharacterCount;
    public int PlayableCharacterCount => SharedData.PlayableCharacterCount;
    public int NonPlayableNpcCount => SharedData.NonPlayableNpcCount;
    public int ShelterStability => Mathf.Clamp(SharedData.ShelterStability, 0, 100);
    public int CurrentDay => ShelterData.CurrentDay;
    public bool HasActiveShelterDataManager => activeShelterDataManager != null;

    private void Awake()
    {
        if (TryRejectDuplicateOrInvalidRoot())
        {
            return;
        }

        EnsureSharedData();
        EnsureShelterData();
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void RegisterShelterDataManager(ShelterDataManager shelterDataManager)
    {
        if (shelterDataManager == null)
            return;

        activeShelterDataManager = shelterDataManager;
    }

    public void UnregisterShelterDataManager(ShelterDataManager shelterDataManager)
    {
        if (activeShelterDataManager == shelterDataManager)
        {
            activeShelterDataManager = null;
        }
    }

    public bool SyncFromShelter()
    {
        if (activeShelterDataManager == null)
        {
            Debug.LogWarning("[GameDataManager] Active ShelterDataManager is not registered.");
            return false;
        }

        return SyncFromShelter(activeShelterDataManager);
    }

    public bool SyncFromShelter(ShelterDataManager shelterDataManager)
    {
        if (shelterDataManager == null)
        {
            Debug.LogWarning("[GameDataManager] ShelterDataManager is null.");
            return false;
        }

        ApplySnapshot(shelterDataManager.CreateSharedSnapshot());
        ApplyShelterSnapshot(shelterDataManager.CreateSnapshot());
        return true;
    }

    public SharedRuntimeData CreateSharedSnapshot()
    {
        return SharedData.Clone();
    }

    public ShelterRuntimeData CreateShelterSnapshot()
    {
        return ShelterData.Clone();
    }

    public SaveData CreateSaveData(string profileId)
    {
        SaveData saveData = new SaveData
        {
            profileId = string.IsNullOrWhiteSpace(profileId) ? SaveFilePaths.DefaultProfileId : profileId
        };

        SharedRuntimeData sharedSnapshot = activeShelterDataManager != null ? activeShelterDataManager.CreateSharedSnapshot() : SharedData.Clone();
        ShelterRuntimeData shelterSnapshot = activeShelterDataManager != null ? activeShelterDataManager.CreateSnapshot() : ShelterData.Clone();
        saveData.shared = CreateSharedSaveData(sharedSnapshot);
        saveData.shelter = CreateShelterSaveData(shelterSnapshot);
        saveData.MarkSavedNow();
        return saveData;
    }

    public void ApplySaveData(SaveData saveData)
    {
        if (saveData == null)
        {
            Debug.LogWarning("[GameDataManager] SaveData is null.");
            return;
        }

        ApplySharedSaveData(saveData.shared ?? new SaveData.SharedSaveData());
        ApplyShelterSaveData(saveData.shelter ?? new SaveData.ShelterSaveData());
    }

    public void ApplySnapshot(SharedRuntimeData snapshot)
    {
        if (snapshot == null)
        {
            Debug.LogWarning("[GameDataManager] Snapshot is null.");
            return;
        }

        SharedData.CopyFrom(snapshot);
    }

    public void ApplyShelterSnapshot(ShelterRuntimeData snapshot)
    {
        if (snapshot == null)
        {
            Debug.LogWarning("[GameDataManager] Shelter snapshot is null.");
            return;
        }

        ShelterData.CopyFrom(snapshot);
    }

    private void EnsureSharedData()
    {
        sharedData ??= new SharedRuntimeData();
        sharedData.EnsureRuntimeContainers();
    }

    private void EnsureShelterData()
    {
        shelterData ??= new ShelterRuntimeData();
        shelterData.EnsureRuntimeContainers();
    }

    private SaveData.SharedSaveData CreateSharedSaveData(SharedRuntimeData source)
    {
        source.EnsureRuntimeContainers();

        SaveData.SharedSaveData saveData = new SaveData.SharedSaveData
        {
            lastStageId = source.LastStageId,
            shelterStability = source.ShelterStability,
            playableCharacterCount = source.PlayableCharacterCount,
            nonPlayableNpcCount = source.NonPlayableNpcCount
        };

        foreach (KeyValuePair<CurrencyType, int> resource in source.Resources.Amounts)
        {
            saveData.resources.Add(new SaveData.ResourceAmountData
            {
                type = resource.Key,
                amount = Mathf.Max(0, resource.Value)
            });
        }

        foreach (NPCRuntimeData npc in source.NpcRoster.All)
        {
            SaveData.NpcSaveData npcSaveData = NpcSaveDataMapper.FromRuntime(npc);
            if (npcSaveData == null)
                continue;

            saveData.npcs.Add(npcSaveData);
        }

        return saveData;
    }

    private SaveData.ShelterSaveData CreateShelterSaveData(ShelterRuntimeData source)
    {
        source.EnsureRuntimeContainers();

        SaveData.ShelterSaveData saveData = new SaveData.ShelterSaveData
        {
            currentDay = source.CurrentDay,
            battleSquadNpcRuntimeIds = new List<string>(source.BattleSquadNpcRuntimeIds)
        };

        foreach (FacilityRuntimeState state in source.FacilityStates)
        {
            SaveData.FacilitySaveData facilitySaveData = FacilitySaveDataMapper.FromRuntime(state);
            if (facilitySaveData == null)
                continue;

            saveData.facilities.Add(facilitySaveData);
        }

        return saveData;
    }

    private void ApplySharedSaveData(SaveData.SharedSaveData saveData)
    {
        SharedData.SetLastStageId(saveData.lastStageId);
        SharedData.SetShelterStability(saveData.shelterStability);
        SharedData.SetOwnedCharacterCounts(saveData.playableCharacterCount, saveData.nonPlayableNpcCount);

        SharedData.Resources.Clear();
        if (saveData.resources != null)
        {
            foreach (SaveData.ResourceAmountData resource in saveData.resources)
            {
                if (resource == null)
                    continue;

                SharedData.Resources.SetAmount(resource.type, resource.amount);
            }
        }

        SharedData.NpcRoster.Clear();
        if (saveData.npcs != null)
        {
            foreach (SaveData.NpcSaveData npc in saveData.npcs)
            {
                if (npc == null)
                    continue;

                NPCRuntimeData runtimeNpc = NpcSaveDataMapper.ToRuntime(npc);
                if (runtimeNpc != null)
                {
                    SharedData.NpcRoster.Add(runtimeNpc);
                }
            }
        }
    }

    private void ApplyShelterSaveData(SaveData.ShelterSaveData saveData)
    {
        List<FacilityRuntimeState> facilityStates = new List<FacilityRuntimeState>();
        if (saveData.facilities != null)
        {
            foreach (SaveData.FacilitySaveData facility in saveData.facilities)
            {
                if (facility == null)
                    continue;

                FacilityRuntimeState runtimeState = FacilitySaveDataMapper.ToRuntime(facility);
                if (runtimeState != null)
                {
                    facilityStates.Add(runtimeState);
                }
            }
        }

        ShelterData.ApplySavedState(saveData.currentDay, saveData.battleSquadNpcRuntimeIds, facilityStates);
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
