/// <summary>
/// 캐릭터 변경(커맨드) 전담. 정책(허용 판정)과 조회(대상/필터)를 소비해 실제 변경을 실행하고,
/// 변경 발생 시 컨텍스트를 통해 통지(이벤트 발행)를 위임한다.
/// </summary>
public sealed class CharacterEditor
{
    private readonly ICharacterDataContext context;
    private readonly CharacterEditPolicy policy;
    private readonly CharacterQueryService query;

    public CharacterEditor(ICharacterDataContext context, CharacterEditPolicy policy, CharacterQueryService query)
    {
        this.context = context;
        this.policy = policy;
        this.query = query;
    }

    public bool TryAssignToFacility(string definitionId, string facilityId, string roomId, FacilityAssignmentKind kind, out CharacterActionFailure failure)
    {
        return TryAssignToFacility(definitionId, facilityId, roomId, CharacterAssignmentFilter.AvailableAlive, kind, out failure);
    }

    public bool TryAssignToFacility(string definitionId, string facilityId, string roomId, CharacterAssignmentFilter filter, FacilityAssignmentKind kind, out CharacterActionFailure failure)
    {
        return TryAssignToFacility(definitionId, facilityId, roomId, filter, kind, out _, out failure);
    }

    public bool TryAssignToFacility(string definitionId, string facilityId, string roomId, CharacterAssignmentFilter filter, FacilityAssignmentKind kind, out NPCRuntimeData character, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;
        character = null;

        if (!policy.CanUse(CharacterEditCapability.FacilityAssignment, out failure))
            return false;

        if (string.IsNullOrWhiteSpace(facilityId))
        {
            failure = CharacterActionFailure.InvalidFacilityId;
            return false;
        }

        if (!query.TryGetCharacter(definitionId, out character))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        if (!query.MatchesFilter(character, filter))
        {
            failure = character.GetIsAssignedToShelter()
                ? CharacterActionFailure.AlreadyAssigned
                : CharacterActionFailure.CharacterNotEligible;
            return false;
        }

        string normalizedFacilityId = facilityId.Trim();
        string normalizedRoomId = string.IsNullOrWhiteSpace(roomId) ? normalizedFacilityId : roomId.Trim();
        if (!character.AssignToShelter(normalizedFacilityId, normalizedRoomId, kind))
        {
            failure = CharacterActionFailure.InvalidFacilityId;
            return false;
        }

        context.NotifyCharacterChanged(character);
        return true;
    }

    public bool TryReleaseFromFacility(string definitionId, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!policy.CanUse(CharacterEditCapability.FacilityAssignment, out failure))
            return false;

        if (!query.TryGetCharacter(definitionId, out NPCRuntimeData character))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        if (!character.GetIsAssignedToShelter())
        {
            failure = CharacterActionFailure.NotAssignedToFacility;
            return false;
        }

        character.ReleaseFromShelter();
        context.NotifyCharacterChanged(character);
        return true;
    }

    public bool TrySetCurrentHp(string definitionId, int currentHp, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!policy.CanUse(CharacterEditCapability.HealthChange, out failure))
            return false;

        if (!query.TryGetCharacter(definitionId, out NPCRuntimeData character))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        bool changed = character.SetCurrentHp(currentHp);
        NotifyChangedIfNeeded(character, changed);
        return true;
    }

    public bool TrySetInjuryState(string definitionId, NPCInjuryState injuryState, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!policy.CanUse(CharacterEditCapability.HealthChange, out failure))
            return false;

        if (!query.TryGetCharacter(definitionId, out NPCRuntimeData character))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        bool changed = character.SetInjuryState(injuryState);
        NotifyChangedIfNeeded(character, changed);
        return true;
    }

    public bool TrySetInjuryGauge(string definitionId, float injuryGauge, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!policy.CanUse(CharacterEditCapability.HealthChange, out failure))
            return false;

        if (!query.TryGetCharacter(definitionId, out NPCRuntimeData character))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        bool changed = character.SetInjuryGauge(injuryGauge);
        NotifyChangedIfNeeded(character, changed);
        return true;
    }

    // 부상상태를 현재 게이지 기준으로 재계산한다(완치/수동해제 시점 사용).
    public bool TryRefreshInjuryState(string definitionId, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!policy.CanUse(CharacterEditCapability.HealthChange, out failure))
            return false;

        if (!query.TryGetCharacter(definitionId, out NPCRuntimeData character))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        bool changed = character.RefreshInjuryStateFromGauge();
        NotifyChangedIfNeeded(character, changed);
        return true;
    }

    public bool TryApplyDamage(string definitionId, int damage, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!policy.CanUse(CharacterEditCapability.HealthChange, out failure))
            return false;

        if (!query.TryGetCharacter(definitionId, out NPCRuntimeData character))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        if (damage <= 0)
        {
            failure = CharacterActionFailure.InvalidHealthChange;
            return false;
        }

        bool changed = character.ApplyDamage(damage);
        NotifyChangedIfNeeded(character, changed);
        return true;
    }

    public bool TryRecoverHp(string definitionId, int amount, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!policy.CanUse(CharacterEditCapability.HealthChange, out failure))
            return false;

        if (!query.TryGetCharacter(definitionId, out NPCRuntimeData character))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        if (amount <= 0)
        {
            failure = CharacterActionFailure.InvalidHealthChange;
            return false;
        }

        bool changed = character.RecoverHp(amount);
        NotifyChangedIfNeeded(character, changed);
        return true;
    }

    public bool TryReviveToPercent(string definitionId, int percent, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!policy.CanUse(CharacterEditCapability.HealthChange, out failure))
            return false;

        if (!query.TryGetCharacter(definitionId, out NPCRuntimeData character))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        if (percent <= 0)
        {
            failure = CharacterActionFailure.InvalidHealthChange;
            return false;
        }

        bool changed = character.ReviveToPercent(percent);
        NotifyChangedIfNeeded(character, changed);
        return true;
    }

    public bool TryCompleteRecovery(string definitionId, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!policy.CanUse(CharacterEditCapability.HealthChange | CharacterEditCapability.FacilityAssignment, out failure))
            return false;

        if (!query.TryGetCharacter(definitionId, out NPCRuntimeData character))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        bool changed = character.CompleteRecovery();
        if (character.GetIsAssignedToShelter())
        {
            character.ReleaseFromShelter();
            changed = true;
        }

        NotifyChangedIfNeeded(character, changed);
        return true;
    }

    public bool TryChangeWeapon(string definitionId, Weapon weapon, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!policy.CanUse(CharacterEditCapability.EquipmentChange, out failure))
            return false;

        if (!query.TryGetCharacter(definitionId, out _))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        if (weapon == null)
        {
            failure = CharacterActionFailure.InvalidEquipment;
            return false;
        }

        failure = CharacterActionFailure.MissingRuntimeModel;
        return false;
    }

    public bool TryChangeWeaponPart(string definitionId, WeaponPart part, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!policy.CanUse(CharacterEditCapability.EquipmentChange, out failure))
            return false;

        if (!query.TryGetCharacter(definitionId, out _))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        if (part == null)
        {
            failure = CharacterActionFailure.InvalidEquipment;
            return false;
        }

        failure = CharacterActionFailure.MissingRuntimeModel;
        return false;
    }

    public bool TryUpgradeEquipment(string definitionId, string equipmentId, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!policy.CanUse(CharacterEditCapability.EquipmentUpgrade, out failure))
            return false;

        if (!query.TryGetCharacter(definitionId, out _))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        if (string.IsNullOrWhiteSpace(equipmentId))
        {
            failure = CharacterActionFailure.InvalidEquipment;
            return false;
        }

        failure = CharacterActionFailure.MissingRuntimeModel;
        return false;
    }

    public bool TryChangeSkill(string definitionId, string skillId, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!policy.CanUse(CharacterEditCapability.SkillChange, out failure))
            return false;

        if (!query.TryGetCharacter(definitionId, out _))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        if (string.IsNullOrWhiteSpace(skillId))
        {
            failure = CharacterActionFailure.InvalidSkill;
            return false;
        }

        failure = CharacterActionFailure.MissingRuntimeModel;
        return false;
    }

    private void NotifyChangedIfNeeded(NPCRuntimeData character, bool changed)
    {
        if (changed)
            context.NotifyCharacterChanged(character);
    }
}
