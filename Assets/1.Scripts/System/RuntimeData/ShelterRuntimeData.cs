using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ShelterRuntimeData
{
    public const int MinBattleSquadSize = 1;
    public const int MaxBattleSquadSize = 3;

    [SerializeField] private int currentDay = 1;
    [SerializeField] private List<string> battleSquadNpcDefinitionIds = new List<string>();
    [SerializeField] private List<FacilityRuntimeState> facilityStates = new List<FacilityRuntimeState>();

    public int CurrentDay => Mathf.Max(1, currentDay);
    public IReadOnlyList<string> BattleSquadNpcDefinitionIds => battleSquadNpcDefinitionIds;
    public IReadOnlyList<FacilityRuntimeState> FacilityStates => facilityStates;

    public void EnsureRuntimeContainers()
    {
        currentDay = Mathf.Max(1, currentDay);
        battleSquadNpcDefinitionIds ??= new List<string>();
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
            battleSquadNpcDefinitionIds = new List<string>(battleSquadNpcDefinitionIds),
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
        battleSquadNpcDefinitionIds = new List<string>(source.battleSquadNpcDefinitionIds);
        NormalizeBattleSquad();
        facilityStates = CloneFacilityStates(source.facilityStates);
    }

    public void SetCurrentDay(int day)
    {
        currentDay = Mathf.Max(1, day);
    }

    public void ApplySavedState(int day, IEnumerable<string> battleSquadDefinitionIds, IEnumerable<FacilityRuntimeState> savedFacilityStates)
    {
        SetCurrentDay(day);

        battleSquadNpcDefinitionIds.Clear();
        if (battleSquadDefinitionIds != null)
        {
            foreach (string definitionId in battleSquadDefinitionIds)
            {
                TryAddBattleSquadNpc(definitionId);
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

    public bool TrySetBattleSquad(IEnumerable<string> definitionIds)
    {
        if (definitionIds == null)
            return false;

        List<string> normalizedIds = new List<string>();
        foreach (string definitionId in definitionIds)
        {
            if (string.IsNullOrWhiteSpace(definitionId))
                continue;

            string trimmedDefinitionId = definitionId.Trim();
            if (normalizedIds.Contains(trimmedDefinitionId))
                continue;

            normalizedIds.Add(trimmedDefinitionId);
            if (normalizedIds.Count > MaxBattleSquadSize)
                return false;
        }

        if (normalizedIds.Count < MinBattleSquadSize)
            return false;

        battleSquadNpcDefinitionIds = normalizedIds;
        return true;
    }

    public bool TryAddBattleSquadNpc(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            return false;

        string trimmedDefinitionId = definitionId.Trim();
        if (battleSquadNpcDefinitionIds.Contains(trimmedDefinitionId))
            return true;

        if (battleSquadNpcDefinitionIds.Count >= MaxBattleSquadSize)
            return false;

        battleSquadNpcDefinitionIds.Add(trimmedDefinitionId);
        return true;
    }

    public bool TryRemoveBattleSquadNpc(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            return false;

        if (battleSquadNpcDefinitionIds.Count <= MinBattleSquadSize)
            return false;

        return battleSquadNpcDefinitionIds.Remove(definitionId.Trim());
    }

    public void ClearBattleSquad()
    {
        battleSquadNpcDefinitionIds.Clear();
    }

    public void RemoveNpcReferences(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            return;

        battleSquadNpcDefinitionIds.RemoveAll(id => id == definitionId.Trim());
    }

    private void NormalizeBattleSquad()
    {
        for (int i = battleSquadNpcDefinitionIds.Count - 1; i >= 0; i--)
        {
            string definitionId = battleSquadNpcDefinitionIds[i];
            if (string.IsNullOrWhiteSpace(definitionId))
            {
                battleSquadNpcDefinitionIds.RemoveAt(i);
                continue;
            }

            battleSquadNpcDefinitionIds[i] = definitionId.Trim();
        }

        for (int i = battleSquadNpcDefinitionIds.Count - 1; i >= 0; i--)
        {
            if (battleSquadNpcDefinitionIds.IndexOf(battleSquadNpcDefinitionIds[i]) != i)
            {
                battleSquadNpcDefinitionIds.RemoveAt(i);
            }
        }

        if (battleSquadNpcDefinitionIds.Count > MaxBattleSquadSize)
        {
            battleSquadNpcDefinitionIds.RemoveRange(MaxBattleSquadSize, battleSquadNpcDefinitionIds.Count - MaxBattleSquadSize);
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
