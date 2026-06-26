using System;
using UnityEngine;

public class NPCRuntimeData
{
    public NPCChar NPCData { get; }
    public string RuntimeId { get; }
    public NPCInjuryState CurrentInjuryState { get; private set; }

    private readonly string definitionId;
    private readonly NPCType npcType;
    private readonly int maxHp;
    private bool IsAssignedToShelter { get; set; }
    private string AssignedRoomId { get; set; }
    private int CurrentHp { get; set; }

    public string DefinitionId => string.IsNullOrWhiteSpace(definitionId) ? string.Empty : definitionId;
    public NPCType Type => NPCData != null ? NPCData.Type : npcType;
    public int MaxHp => NPCData != null ? NPCData.MaxHP : Mathf.Max(1, maxHp);
    public bool IsDead => CurrentHp <= 0;

    public NPCRuntimeData(NPCChar npcData, string runtimeId = null)
    {
        NPCData = npcData ?? throw new ArgumentNullException(nameof(npcData));
        definitionId = npcData.DefinitionId ?? string.Empty;
        npcType = npcData.Type;
        maxHp = npcData.MaxHP;
        RuntimeId = string.IsNullOrWhiteSpace(runtimeId) ? DefinitionId : runtimeId.Trim();
        ResetToBaseState();
    }

    public NPCRuntimeData(string definitionId, string runtimeId, NPCType type, int maxHp, int currentHp, NPCInjuryState injuryState, bool isAssignedToShelter, string assignedRoomId)
    {
        this.definitionId = definitionId ?? string.Empty;
        npcType = type;
        this.maxHp = Mathf.Max(1, maxHp);
        RuntimeId = string.IsNullOrWhiteSpace(runtimeId) ? this.definitionId : runtimeId.Trim();
        CurrentInjuryState = injuryState;
        IsAssignedToShelter = isAssignedToShelter;
        AssignedRoomId = assignedRoomId ?? string.Empty;
        SetCurrentHp(currentHp);
    }

    public void ResetToBaseState()
    {
        CurrentHp = MaxHp;
        CurrentInjuryState = NPCInjuryState.Healthy;
        ReleaseFromShelter();
    }

    public NPCInjuryState GetHealthState(NPCHealthRuleSO rule)
    {
        if (rule == null) throw new ArgumentNullException(nameof(rule));
        return rule.Evaluate(CurrentHp, MaxHp);
    }

    public NPCInjuryState GetCurrentInjuryState() => CurrentInjuryState;
    public bool GetIsAssignedToShelter() => IsAssignedToShelter;
    public string GetAssignedRoomId() => AssignedRoomId;
    public int GetCurrentHp() => CurrentHp;

    public bool AssignToShelter(string shelterId, string roomId)
    {
        if (string.IsNullOrWhiteSpace(shelterId) || string.IsNullOrWhiteSpace(roomId))
            return false;

        string normalizedRoomId = roomId.Trim();
        if (IsAssignedToShelter && AssignedRoomId == normalizedRoomId)
            return false;

        IsAssignedToShelter = true;
        AssignedRoomId = normalizedRoomId;
        return true;
    }

    public bool ReleaseFromShelter()
    {
        if (!IsAssignedToShelter && string.IsNullOrEmpty(AssignedRoomId))
            return false;

        IsAssignedToShelter = false;
        AssignedRoomId = string.Empty;
        return true;
    }

    public bool SetCurrentHp(int value)
    {
        int clampedValue = Mathf.Clamp(value, 0, MaxHp);
        if (CurrentHp == clampedValue)
            return false;

        CurrentHp = clampedValue;
        return true;
    }

    public bool SetInjuryState(NPCInjuryState state)
    {
        if (CurrentInjuryState == state)
            return false;

        CurrentInjuryState = state;
        return true;
    }

    public bool CompleteRecovery()
    {
        bool hpChanged = SetCurrentHp(MaxHp);
        bool injuryChanged = SetInjuryState(NPCInjuryState.Healthy);
        return hpChanged || injuryChanged;
    }

    public bool ApplyDamage(int damage)
    {
        if (damage <= 0)
            return false;

        return SetCurrentHp(CurrentHp - damage);
    }

    public bool RecoverHp(int amount)
    {
        if (amount <= 0)
            return false;

        return SetCurrentHp(CurrentHp + amount);
    }

    public bool ReviveToPercent(int percent)
    {
        if (percent <= 0)
            return false;

        int healAmount = MaxHp * percent / 100;
        return SetCurrentHp(CurrentHp + healAmount);
    }

    public NPCRuntimeData Clone()
    {
        return new NPCRuntimeData(DefinitionId, RuntimeId, Type, MaxHp, CurrentHp, CurrentInjuryState, IsAssignedToShelter, AssignedRoomId);
    }
}
