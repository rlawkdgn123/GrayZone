using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ShelterRuntimeData
{
    public const int MinBattleSquadSize = 1;
    public const int MaxBattleSquadSize = 3;

    [SerializeField] private int currentDay = 1;
    [SerializeField] private List<string> battleSquadNpcRuntimeIds = new List<string>();
    [SerializeField] private List<FacilityRuntimeState> facilityStates = new List<FacilityRuntimeState>();

    public int CurrentDay => Mathf.Max(1, currentDay);
    public IReadOnlyList<string> BattleSquadNpcRuntimeIds => battleSquadNpcRuntimeIds;
    public IReadOnlyList<FacilityRuntimeState> FacilityStates => facilityStates;

    public void EnsureRuntimeContainers()
    {
        currentDay = Mathf.Max(1, currentDay);
        battleSquadNpcRuntimeIds ??= new List<string>();
        facilityStates ??= new List<FacilityRuntimeState>();
        NormalizeBattleSquad();

        for (int i = facilityStates.Count - 1; i >= 0; i--)
        {
            if (facilityStates[i] == null)
                facilityStates.RemoveAt(i);
            else
                facilityStates[i].EnsureValid();
        }
    }

    public ShelterRuntimeData Clone()
    {
        EnsureRuntimeContainers();

        ShelterRuntimeData clone = new ShelterRuntimeData
        {
            currentDay = CurrentDay,
            battleSquadNpcRuntimeIds = new List<string>(battleSquadNpcRuntimeIds),
            facilityStates = CloneFacilityStates(facilityStates)
        };

        return clone;
    }

    public void CopyFrom(ShelterRuntimeData source)
    {
        if (source == null)
            return;

        source.EnsureRuntimeContainers();
        EnsureRuntimeContainers();

        SetCurrentDay(source.CurrentDay);
        battleSquadNpcRuntimeIds = new List<string>(source.battleSquadNpcRuntimeIds);
        NormalizeBattleSquad();
        facilityStates = CloneFacilityStates(source.facilityStates);
    }

    public void SetCurrentDay(int day)
    {
        currentDay = Mathf.Max(1, day);
    }

    public void ApplySavedState(int day, IEnumerable<string> battleSquadRuntimeIds, IEnumerable<FacilityRuntimeState> savedFacilityStates)
    {
        SetCurrentDay(day);

        battleSquadNpcRuntimeIds.Clear();
        if (battleSquadRuntimeIds != null)
        {
            foreach (string runtimeId in battleSquadRuntimeIds)
            {
                TryAddBattleSquadNpc(runtimeId);
            }
        }

        facilityStates.Clear();
        if (savedFacilityStates != null)
        {
            foreach (FacilityRuntimeState state in savedFacilityStates)
            {
                if (state == null)
                    continue;

                state.EnsureValid();
                if (string.IsNullOrWhiteSpace(state.facilityId))
                    continue;

                facilityStates.Add(new FacilityRuntimeState(state.facilityId, state.isUnlocked, state.upgradeLevel));
            }
        }
    }

    public FacilityRuntimeState GetOrCreateFacilityState(string facilityId, bool isUnlockedByDefault)
    {
        EnsureRuntimeContainers();

        string normalizedFacilityId = string.IsNullOrWhiteSpace(facilityId) ? string.Empty : facilityId.Trim();
        if (string.IsNullOrEmpty(normalizedFacilityId))
            return null;

        foreach (FacilityRuntimeState state in facilityStates)
        {
            if (state == null)
                continue;

            state.EnsureValid();
            if (state.facilityId == normalizedFacilityId)
                return state;
        }

        FacilityRuntimeState created = new FacilityRuntimeState(normalizedFacilityId, isUnlockedByDefault);
        facilityStates.Add(created);
        return created;
    }

    public bool TrySetBattleSquad(IEnumerable<string> runtimeIds)
    {
        if (runtimeIds == null)
            return false;

        List<string> normalizedIds = new List<string>();
        foreach (string runtimeId in runtimeIds)
        {
            if (string.IsNullOrWhiteSpace(runtimeId))
                continue;

            string trimmedRuntimeId = runtimeId.Trim();
            if (normalizedIds.Contains(trimmedRuntimeId))
                continue;

            normalizedIds.Add(trimmedRuntimeId);
            if (normalizedIds.Count > MaxBattleSquadSize)
                return false;
        }

        if (normalizedIds.Count < MinBattleSquadSize)
            return false;

        battleSquadNpcRuntimeIds = normalizedIds;
        return true;
    }

    public bool TryAddBattleSquadNpc(string runtimeId)
    {
        if (string.IsNullOrWhiteSpace(runtimeId))
            return false;

        string trimmedRuntimeId = runtimeId.Trim();
        if (battleSquadNpcRuntimeIds.Contains(trimmedRuntimeId))
            return true;

        if (battleSquadNpcRuntimeIds.Count >= MaxBattleSquadSize)
            return false;

        battleSquadNpcRuntimeIds.Add(trimmedRuntimeId);
        return true;
    }

    public bool TryRemoveBattleSquadNpc(string runtimeId)
    {
        if (string.IsNullOrWhiteSpace(runtimeId))
            return false;

        if (battleSquadNpcRuntimeIds.Count <= MinBattleSquadSize)
            return false;

        return battleSquadNpcRuntimeIds.Remove(runtimeId.Trim());
    }

    public void ClearBattleSquad()
    {
        battleSquadNpcRuntimeIds.Clear();
    }

    public void RemoveNpcReferences(string runtimeId)
    {
        if (string.IsNullOrWhiteSpace(runtimeId))
            return;

        battleSquadNpcRuntimeIds.RemoveAll(id => id == runtimeId.Trim());
    }

    private void NormalizeBattleSquad()
    {
        for (int i = battleSquadNpcRuntimeIds.Count - 1; i >= 0; i--)
        {
            string runtimeId = battleSquadNpcRuntimeIds[i];
            if (string.IsNullOrWhiteSpace(runtimeId))
            {
                battleSquadNpcRuntimeIds.RemoveAt(i);
                continue;
            }

            battleSquadNpcRuntimeIds[i] = runtimeId.Trim();
        }

        for (int i = battleSquadNpcRuntimeIds.Count - 1; i >= 0; i--)
        {
            if (battleSquadNpcRuntimeIds.IndexOf(battleSquadNpcRuntimeIds[i]) != i)
            {
                battleSquadNpcRuntimeIds.RemoveAt(i);
            }
        }

        if (battleSquadNpcRuntimeIds.Count > MaxBattleSquadSize)
        {
            battleSquadNpcRuntimeIds.RemoveRange(MaxBattleSquadSize, battleSquadNpcRuntimeIds.Count - MaxBattleSquadSize);
        }
    }

    private static List<FacilityRuntimeState> CloneFacilityStates(List<FacilityRuntimeState> source)
    {
        List<FacilityRuntimeState> clone = new List<FacilityRuntimeState>();
        if (source == null)
            return clone;

        foreach (FacilityRuntimeState state in source)
        {
            if (state == null)
                continue;

            state.EnsureValid();
            clone.Add(new FacilityRuntimeState(state.facilityId, state.isUnlocked, state.upgradeLevel));
        }

        return clone;
    }
}
