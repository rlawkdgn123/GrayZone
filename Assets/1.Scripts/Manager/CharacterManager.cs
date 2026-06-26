using System;
using System.Collections.Generic;
using UnityEngine;

public class CharacterManager : MonoBehaviour
{
    private static readonly NPCRuntimeData[] EmptyCharacters = Array.Empty<NPCRuntimeData>();
    private const CharacterEditCapability LegacyAllCapabilities =
        CharacterEditCapability.FacilityAssignment |
        CharacterEditCapability.EquipmentChange |
        CharacterEditCapability.SkillChange |
        CharacterEditCapability.EquipmentUpgrade;

    [SerializeField] private ShelterDataManager dataSource;
    [SerializeField] private bool readOnlyMode;
    [SerializeField] private CharacterEditCapability enabledCapabilities = CharacterEditCapability.All;

    public static CharacterManager Instance { get; private set; }

    public event Action<NPCRuntimeData> CharacterChanged;
    public event Action RosterChanged;

    public IReadOnlyList<NPCRuntimeData> Characters => DataSource != null ? DataSource.Npcs : EmptyCharacters;
    public int CharacterCount => DataSource != null ? DataSource.RosterCount : 0;
    public bool IsReadOnly => readOnlyMode;
    public CharacterEditCapability EnabledCapabilities => enabledCapabilities;

    private ShelterDataManager DataSource
    {
        get
        {
            if (dataSource == null)
                dataSource = ShelterDataManager.Instance;

            return dataSource;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        UpgradeLegacyCapabilities();

        if (dataSource == null)
            dataSource = ShelterDataManager.Instance;
    }

    private void OnValidate()
    {
        UpgradeLegacyCapabilities();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void SetDataSource(ShelterDataManager source)
    {
        dataSource = source;
        RosterChanged?.Invoke();
    }

    public void SetReadOnlyMode(bool value)
    {
        readOnlyMode = value;
    }

    public void SetEnabledCapabilities(CharacterEditCapability capabilities)
    {
        enabledCapabilities = capabilities;
    }

    public bool TryGetCharacter(string runtimeId, out NPCRuntimeData character)
    {
        character = null;
        return DataSource != null && DataSource.TryGetNpc(runtimeId, out character);
    }

    public void FillAllCharacters(List<NPCRuntimeData> results)
    {
        FillCharacters(results, CharacterAssignmentFilter.Any);
    }

    public void FillCharactersByType(NPCType type, List<NPCRuntimeData> results)
    {
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        results.Clear();
        foreach (NPCRuntimeData character in Characters)
        {
            if (character != null && character.Type == type)
                results.Add(character);
        }
    }

    public void FillTreatmentCandidates(List<NPCRuntimeData> results)
    {
        FillCharacters(results, CharacterAssignmentFilter.AvailableInjured);
    }

    public void FillFacilityAssignableCharacters(string facilityId, List<NPCRuntimeData> results, CharacterAssignmentFilter filter = CharacterAssignmentFilter.AvailableAlive)
    {
        if (string.IsNullOrWhiteSpace(facilityId))
            throw new ArgumentException("Facility id is required.", nameof(facilityId));

        FillCharacters(results, filter);
    }

    public bool CanAssignToFacility(NPCRuntimeData character, CharacterAssignmentFilter filter = CharacterAssignmentFilter.AvailableAlive)
    {
        return MatchesFilter(character, filter);
    }

    public bool TryAssignToFacility(string runtimeId, string facilityId, string roomId, out CharacterActionFailure failure)
    {
        return TryAssignToFacility(runtimeId, facilityId, roomId, CharacterAssignmentFilter.AvailableAlive, out failure);
    }

    public bool TryAssignToFacility(string runtimeId, string facilityId, string roomId, CharacterAssignmentFilter filter, out CharacterActionFailure failure)
    {
        return TryAssignToFacility(runtimeId, facilityId, roomId, filter, out _, out failure);
    }

    public bool TryAssignToFacility(string runtimeId, string facilityId, string roomId, CharacterAssignmentFilter filter, out NPCRuntimeData character, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;
        character = null;

        if (!CanUseCapability(CharacterEditCapability.FacilityAssignment, out failure))
            return false;

        if (string.IsNullOrWhiteSpace(facilityId))
        {
            failure = CharacterActionFailure.InvalidFacilityId;
            return false;
        }

        if (!TryGetCharacter(runtimeId, out character))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        if (!MatchesFilter(character, filter))
        {
            failure = character.GetIsAssignedToShelter()
                ? CharacterActionFailure.AlreadyAssigned
                : CharacterActionFailure.CharacterNotEligible;
            return false;
        }

        string normalizedFacilityId = facilityId.Trim();
        string normalizedRoomId = string.IsNullOrWhiteSpace(roomId) ? normalizedFacilityId : roomId.Trim();
        if (!character.AssignToShelter(normalizedFacilityId, normalizedRoomId))
        {
            failure = CharacterActionFailure.InvalidFacilityId;
            return false;
        }

        NotifyCharacterChanged(character);
        return true;
    }

    public bool TryReleaseFromFacility(string runtimeId, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!CanUseCapability(CharacterEditCapability.FacilityAssignment, out failure))
            return false;

        if (!TryGetCharacter(runtimeId, out NPCRuntimeData character))
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
        NotifyCharacterChanged(character);
        return true;
    }

    public bool TrySetCurrentHp(string runtimeId, int currentHp, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!CanUseCapability(CharacterEditCapability.HealthChange, out failure))
            return false;

        if (!TryGetCharacter(runtimeId, out NPCRuntimeData character))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        bool changed = character.SetCurrentHp(currentHp);
        NotifyCharacterChangedIfNeeded(character, changed);
        return true;
    }

    public bool TrySetInjuryState(string runtimeId, NPCInjuryState injuryState, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!CanUseCapability(CharacterEditCapability.HealthChange, out failure))
            return false;

        if (!TryGetCharacter(runtimeId, out NPCRuntimeData character))
        {
            failure = CharacterActionFailure.CharacterNotFound;
            return false;
        }

        bool changed = character.SetInjuryState(injuryState);
        NotifyCharacterChangedIfNeeded(character, changed);
        return true;
    }

    public bool TryApplyDamage(string runtimeId, int damage, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!CanUseCapability(CharacterEditCapability.HealthChange, out failure))
            return false;

        if (!TryGetCharacter(runtimeId, out NPCRuntimeData character))
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
        NotifyCharacterChangedIfNeeded(character, changed);
        return true;
    }

    public bool TryRecoverHp(string runtimeId, int amount, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!CanUseCapability(CharacterEditCapability.HealthChange, out failure))
            return false;

        if (!TryGetCharacter(runtimeId, out NPCRuntimeData character))
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
        NotifyCharacterChangedIfNeeded(character, changed);
        return true;
    }

    public bool TryReviveToPercent(string runtimeId, int percent, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!CanUseCapability(CharacterEditCapability.HealthChange, out failure))
            return false;

        if (!TryGetCharacter(runtimeId, out NPCRuntimeData character))
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
        NotifyCharacterChangedIfNeeded(character, changed);
        return true;
    }

    public bool TryCompleteRecovery(string runtimeId, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!CanUseCapability(CharacterEditCapability.HealthChange | CharacterEditCapability.FacilityAssignment, out failure))
            return false;

        if (!TryGetCharacter(runtimeId, out NPCRuntimeData character))
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

        NotifyCharacterChangedIfNeeded(character, changed);
        return true;
    }

    public bool TryChangeWeapon(string runtimeId, Weapon weapon, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!CanUseCapability(CharacterEditCapability.EquipmentChange, out failure))
            return false;

        if (!TryGetCharacter(runtimeId, out _))
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

    public bool TryChangeWeaponPart(string runtimeId, WeaponPart part, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!CanUseCapability(CharacterEditCapability.EquipmentChange, out failure))
            return false;

        if (!TryGetCharacter(runtimeId, out _))
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

    public bool TryUpgradeEquipment(string runtimeId, string equipmentId, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!CanUseCapability(CharacterEditCapability.EquipmentUpgrade, out failure))
            return false;

        if (!TryGetCharacter(runtimeId, out _))
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

    public bool TryChangeSkill(string runtimeId, string skillId, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (!CanUseCapability(CharacterEditCapability.SkillChange, out failure))
            return false;

        if (!TryGetCharacter(runtimeId, out _))
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

    private void FillCharacters(List<NPCRuntimeData> results, CharacterAssignmentFilter filter)
    {
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        results.Clear();
        foreach (NPCRuntimeData character in Characters)
        {
            if (MatchesFilter(character, filter))
                results.Add(character);
        }
    }

    private bool MatchesFilter(NPCRuntimeData character, CharacterAssignmentFilter filter)
    {
        if (character == null)
            return false;

        bool alive = !character.IsDead && character.GetCurrentInjuryState() != NPCInjuryState.Dead;
        bool injured = alive && character.GetCurrentInjuryState() != NPCInjuryState.Healthy;
        bool available = !character.GetIsAssignedToShelter();

        switch (filter)
        {
            case CharacterAssignmentFilter.Any:
                return true;
            case CharacterAssignmentFilter.AliveOnly:
                return alive;
            case CharacterAssignmentFilter.AvailableAlive:
                return alive && available;
            case CharacterAssignmentFilter.Injured:
                return injured;
            case CharacterAssignmentFilter.AvailableInjured:
                return injured && available;
            default:
                return false;
        }
    }

    private bool CanUseCapability(CharacterEditCapability capability, out CharacterActionFailure failure)
    {
        failure = CharacterActionFailure.None;

        if (DataSource == null)
        {
            failure = CharacterActionFailure.DataSourceUnavailable;
            return false;
        }

        if (readOnlyMode)
        {
            failure = CharacterActionFailure.ReadOnlyMode;
            return false;
        }

        if ((enabledCapabilities & capability) != capability)
        {
            failure = CharacterActionFailure.CapabilityDisabled;
            return false;
        }

        return true;
    }

    private void NotifyCharacterChanged(NPCRuntimeData character)
    {
        DataSource?.MarkDirty();
        CharacterChanged?.Invoke(character);
        RosterChanged?.Invoke();
    }

    private void NotifyCharacterChangedIfNeeded(NPCRuntimeData character, bool changed)
    {
        if (changed)
            NotifyCharacterChanged(character);
    }

    private void UpgradeLegacyCapabilities()
    {
        if (enabledCapabilities == LegacyAllCapabilities)
            enabledCapabilities = CharacterEditCapability.All;
    }
}

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

public enum CharacterAssignmentFilter
{
    Any,
    AliveOnly,
    AvailableAlive,
    Injured,
    AvailableInjured
}

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
