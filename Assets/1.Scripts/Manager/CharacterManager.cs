using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 캐릭터 접근의 진입점(파사드). Unity 수명주기·직렬화 필드·이벤트·싱글턴만 소유하고,
/// 실제 로직은 순수 클래스(CharacterEditPolicy / CharacterQueryService / CharacterEditor)에 위임한다.
/// 외부 public API 시그니처는 리팩터 이전과 동일하게 유지한다.
/// </summary>
public class CharacterManager : MonoBehaviour, ICharacterDataContext
{
    [SerializeField] private ShelterDataManager dataSource;
    [SerializeField] private bool readOnlyMode;
    [SerializeField] private CharacterEditCapability enabledCapabilities = CharacterEditCapability.All;

    public static CharacterManager Instance { get; private set; }

    public event Action<NPCRuntimeData> CharacterChanged;
    public event Action RosterChanged;

    private CharacterEditPolicy policy;
    private CharacterQueryService query;
    private CharacterEditor editor;

    private CharacterEditPolicy Policy => policy ??= new CharacterEditPolicy(this);
    private CharacterQueryService Query => query ??= new CharacterQueryService(this);
    private CharacterEditor Editor => editor ??= new CharacterEditor(this, Policy, Query);

    public IReadOnlyList<NPCRuntimeData> Characters => Query.Characters;
    public int CharacterCount => Query.CharacterCount;
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

    // ── ICharacterDataContext (순수 클래스에 상태를 제공하는 통로) ─────────────
    ShelterDataManager ICharacterDataContext.DataSource => DataSource;
    bool ICharacterDataContext.IsReadOnly => readOnlyMode;
    CharacterEditCapability ICharacterDataContext.EnabledCapabilities => enabledCapabilities;
    void ICharacterDataContext.NotifyCharacterChanged(NPCRuntimeData character) => NotifyCharacterChanged(character);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        enabledCapabilities = CharacterEditPolicy.NormalizeCapabilities(enabledCapabilities);

        if (dataSource == null)
            dataSource = ShelterDataManager.Instance;
    }

    private void OnValidate()
    {
        enabledCapabilities = CharacterEditPolicy.NormalizeCapabilities(enabledCapabilities);
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

    // ── 조회 (CharacterQueryService 위임) ────────────────────────────────────
    public bool TryGetCharacter(string definitionId, out NPCRuntimeData character)
        => Query.TryGetCharacter(definitionId, out character);

    public void FillAllCharacters(List<NPCRuntimeData> results)
        => Query.FillAllCharacters(results);

    public void FillCharactersByType(NPCType type, List<NPCRuntimeData> results)
        => Query.FillCharactersByType(type, results);

    public void FillFacilityAssignableCharacters(string facilityId, List<NPCRuntimeData> results, CharacterAssignmentFilter filter = CharacterAssignmentFilter.AvailableAlive)
        => Query.FillFacilityAssignableCharacters(facilityId, results, filter);

    public bool CanAssignToFacility(NPCRuntimeData character, CharacterAssignmentFilter filter = CharacterAssignmentFilter.AvailableAlive)
        => Query.CanAssignToFacility(character, filter);

    // ── 변경 (CharacterEditor 위임) ──────────────────────────────────────────
    public bool TryAssignToFacility(string definitionId, string facilityId, string roomId, FacilityAssignmentKind kind, out CharacterActionFailure failure)
        => Editor.TryAssignToFacility(definitionId, facilityId, roomId, kind, out failure);

    public bool TryAssignToFacility(string definitionId, string facilityId, string roomId, CharacterAssignmentFilter filter, FacilityAssignmentKind kind, out CharacterActionFailure failure)
        => Editor.TryAssignToFacility(definitionId, facilityId, roomId, filter, kind, out failure);

    public bool TryAssignToFacility(string definitionId, string facilityId, string roomId, CharacterAssignmentFilter filter, FacilityAssignmentKind kind, out NPCRuntimeData character, out CharacterActionFailure failure)
        => Editor.TryAssignToFacility(definitionId, facilityId, roomId, filter, kind, out character, out failure);

    public bool TryReleaseFromFacility(string definitionId, out CharacterActionFailure failure)
        => Editor.TryReleaseFromFacility(definitionId, out failure);

    public bool TrySetCurrentHp(string definitionId, int currentHp, out CharacterActionFailure failure)
        => Editor.TrySetCurrentHp(definitionId, currentHp, out failure);

    public bool TrySetInjuryState(string definitionId, NPCInjuryState injuryState, out CharacterActionFailure failure)
        => Editor.TrySetInjuryState(definitionId, injuryState, out failure);

    public bool TrySetInjuryGauge(string definitionId, float injuryGauge, out CharacterActionFailure failure)
        => Editor.TrySetInjuryGauge(definitionId, injuryGauge, out failure);

    public bool TryRefreshInjuryState(string definitionId, out CharacterActionFailure failure)
        => Editor.TryRefreshInjuryState(definitionId, out failure);

    public bool TryApplyDamage(string definitionId, int damage, out CharacterActionFailure failure)
        => Editor.TryApplyDamage(definitionId, damage, out failure);

    public bool TryRecoverHp(string definitionId, int amount, out CharacterActionFailure failure)
        => Editor.TryRecoverHp(definitionId, amount, out failure);

    public bool TryReviveToPercent(string definitionId, int percent, out CharacterActionFailure failure)
        => Editor.TryReviveToPercent(definitionId, percent, out failure);

    public bool TryCompleteRecovery(string definitionId, out CharacterActionFailure failure)
        => Editor.TryCompleteRecovery(definitionId, out failure);

    public bool TryChangeWeapon(string definitionId, Weapon weapon, out CharacterActionFailure failure)
        => Editor.TryChangeWeapon(definitionId, weapon, out failure);

    public bool TryChangeWeaponPart(string definitionId, WeaponPart part, out CharacterActionFailure failure)
        => Editor.TryChangeWeaponPart(definitionId, part, out failure);

    public bool TryUpgradeEquipment(string definitionId, string equipmentId, out CharacterActionFailure failure)
        => Editor.TryUpgradeEquipment(definitionId, equipmentId, out failure);

    public bool TryChangeSkill(string definitionId, string skillId, out CharacterActionFailure failure)
        => Editor.TryChangeSkill(definitionId, skillId, out failure);

    private void NotifyCharacterChanged(NPCRuntimeData character)
    {
        DataSource?.MarkDirty();
        CharacterChanged?.Invoke(character);
        RosterChanged?.Invoke();
    }
}
