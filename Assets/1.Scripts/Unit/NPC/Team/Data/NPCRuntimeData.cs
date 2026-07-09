using System;
using UnityEngine;

public class NPCRuntimeData
{
    public NPCChar NPCData { get; }
    public NPCInjuryState CurrentInjuryState { get; private set; }
    public FacilityAssignmentKind AssignmentKind { get; private set; }

    private readonly string definitionId;
    private readonly NPCType npcType;
    private readonly int maxHp;
    private readonly float maxInjuryGauge;
    private float m_InjuryGauge;
    private bool IsAssignedToShelter { get; set; }
    private string AssignedRoomId { get; set; }
    private int CurrentHp { get; set; }

    public string DefinitionId => string.IsNullOrWhiteSpace(definitionId) ? string.Empty : definitionId;
    public NPCType Type => NPCData != null ? NPCData.Type : npcType;
    public int MaxHp => NPCData != null ? NPCData.MaxHP : Mathf.Max(1, maxHp);
    public bool IsDead => CurrentHp <= 0;

    public float InjuryGauge => m_InjuryGauge;
    public float MaxInjuryGauge => NPCData != null ? NPCData.MaxInjuryGauge : Mathf.Max(1f, maxInjuryGauge);

    public NPCRuntimeData(NPCChar npcData)
    {
        NPCData = npcData ?? throw new ArgumentNullException(nameof(npcData));
        definitionId = npcData.DefinitionId ?? string.Empty;
        npcType = npcData.Type;
        maxHp = npcData.MaxHP;
        maxInjuryGauge = npcData.MaxInjuryGauge;
        ResetToBaseState();
    }

    public NPCRuntimeData(string definitionId, NPCType type, int maxHp, int currentHp, bool isAssignedToShelter, string assignedRoomId, float injuryGauge, float maxInjuryGauge)
    {
        this.definitionId = definitionId ?? string.Empty;
        npcType = type;
        this.maxHp = Mathf.Max(1, maxHp);
        this.maxInjuryGauge = Mathf.Max(1f, maxInjuryGauge);
        IsAssignedToShelter = isAssignedToShelter;
        AssignedRoomId = assignedRoomId ?? string.Empty;
        this.m_InjuryGauge = Mathf.Clamp(injuryGauge, 0f, this.maxInjuryGauge);
        CurrentInjuryState = NpcInjuryStateRule.FromGauge(this.m_InjuryGauge, this.maxInjuryGauge);
        SetCurrentHp(currentHp);
    }

    public void ResetToBaseState()
    {
        CurrentHp = MaxHp;
        m_InjuryGauge = Mathf.Clamp(NPCData != null ? NPCData.InjuryGauge : m_InjuryGauge, 0f, MaxInjuryGauge);
        CurrentInjuryState = NpcInjuryStateRule.FromGauge(m_InjuryGauge, MaxInjuryGauge);
        ReleaseFromShelter();
    }

    public NPCInjuryState GetCurrentInjuryState() => CurrentInjuryState;
    public bool GetIsAssignedToShelter() => IsAssignedToShelter;
    public string GetAssignedRoomId() => AssignedRoomId;
    public int GetCurrentHp() => CurrentHp;

    public bool AssignToShelter(string shelterId, string roomId, FacilityAssignmentKind kind)
    {
        if (string.IsNullOrWhiteSpace(shelterId) || string.IsNullOrWhiteSpace(roomId) || kind == FacilityAssignmentKind.None)
            return false;

        string normalizedRoomId = roomId.Trim();
        if (IsAssignedToShelter && AssignedRoomId == normalizedRoomId && AssignmentKind == kind)
            return false;

        IsAssignedToShelter = true;
        AssignedRoomId = normalizedRoomId;
        AssignmentKind = kind;
        return true;
    }

    public bool ReleaseFromShelter()
    {
        if (!IsAssignedToShelter && string.IsNullOrEmpty(AssignedRoomId) && AssignmentKind == FacilityAssignmentKind.None)
            return false;

        IsAssignedToShelter = false;
        AssignedRoomId = string.Empty;
        AssignmentKind = FacilityAssignmentKind.None;
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

    public bool SetInjuryGauge(float value)
    {
        float clamped = Mathf.Clamp(value, 0f, MaxInjuryGauge);
        if (Mathf.Approximately(m_InjuryGauge, clamped))
            return false;

        m_InjuryGauge = clamped;
        return true;
    }

    // 현재 게이지로 부상상태를 재계산한다. 완치/수동해제 시점에만 호출(치료 진행 중 매일 호출하지 않음).
    public bool RefreshInjuryStateFromGauge()
    {
        return SetInjuryState(NpcInjuryStateRule.FromGauge(m_InjuryGauge, MaxInjuryGauge));
    }

    public bool CompleteRecovery()
    {
        bool hpChanged = SetCurrentHp(MaxHp);
        bool injuryChanged = SetInjuryState(NPCInjuryState.Healthy);
        bool gaugeChanged = SetInjuryGauge(MaxInjuryGauge);
        return hpChanged || injuryChanged || gaugeChanged;
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
        NPCRuntimeData clone = new NPCRuntimeData(DefinitionId, Type, MaxHp, CurrentHp, IsAssignedToShelter, AssignedRoomId, InjuryGauge, MaxInjuryGauge);
        clone.AssignmentKind = AssignmentKind;
        return clone;
    }
}
