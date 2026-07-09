using System;

// 캐릭터 편집/조회 도메인의 공유 어휘(계약). 여러 클래스(Policy/Query/Editor/파사드)가 함께 사용한다.
// enum 자체엔 로직이 없으므로, 값의 '의미 해석 책임'은 각 담당 클래스가 진다:
//   - CharacterEditCapability  → CharacterEditPolicy
//   - CharacterAssignmentFilter → CharacterQueryService
//   - CharacterActionFailure    → CharacterEditor

/// <summary>캐릭터에 허용된 편집 능력(비트 플래그).</summary>
[Flags]
public enum CharacterEditCapability
{
    None = 0,
    FacilityAssignment = 1 << 0,
    EquipmentChange = 1 << 1,
    SkillChange = 1 << 2,
    EquipmentUpgrade = 1 << 3,
    HealthChange = 1 << 4,
    All = FacilityAssignment | EquipmentChange | SkillChange | EquipmentUpgrade | HealthChange
}

/// <summary>캐릭터 조회 시 적용할 필터.</summary>
public enum CharacterAssignmentFilter
{
    Any,
    AliveOnly,
    AvailableAlive,
    Injured
}

/// <summary>캐릭터 편집 커맨드 실패 사유.</summary>
public enum CharacterActionFailure
{
    None,
    DataSourceUnavailable,
    ReadOnlyMode,
    CapabilityDisabled,
    CharacterNotFound,
    CharacterNotEligible,
    AlreadyAssigned,
    NotAssignedToFacility,
    InvalidFacilityId,
    InvalidEquipment,
    InvalidSkill,
    InvalidHealthChange,
    MissingRuntimeModel
}
