using UnityEngine;

public enum NPCInjuryState
{
    Healthy,
    LightInjury,
    HeavyInjury,
    NearDeath,
    Dead
}
public enum NPCType
{
    Tanker,
    Healer,
    Dealer
}

// 시설 배치 역할(제네릭). NPCRuntimeData는 의무실 전용 개념을 몰라야 하므로 도메인 중립 이름을 쓴다.
public enum FacilityAssignmentKind
{
    None,
    Patient,
    Staff,
    Guest
}

