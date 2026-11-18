using System;
using UnityEngine;

/// <summary>
/// <see cref="CharacterSnapshotData"/>를 복사해 셸터 씬에서 편집하는 캐릭터 런타임 데이터입니다.
/// FieldMemberRuntimeData와 동일하게 스냅샷은 입출력 경계로만 사용하고, 씬 내부 변경값은 이 객체가 소유합니다.
/// </summary>
[Serializable]
public sealed class ShelterMemberRuntimeData
{
    [SerializeField] private string definitionId = string.Empty;
    [SerializeField] private string runtimeId = string.Empty;
    [SerializeField] private PlayerbleCharacterId characterId;
    [SerializeField] private NPCType npcType;
    [SerializeField] private string displayName = string.Empty;
    [SerializeField] private int reliability;
    [SerializeField] private int currentHp = 1;
    [SerializeField] private int maxHp = 1;
    [SerializeField] private int medicalFacilityMaxHpBonus;
    [SerializeField] private float injuryGauge;
    [SerializeField] private float maxInjuryGauge = 100.0f;
    [SerializeField] private CharacterInjuryState injuryState;
    [SerializeField] private bool isDown;
    [SerializeField] private bool isCombatOut;
    [SerializeField] private bool isPlayerSquadMember;
    [SerializeField] private int killCount;
    [SerializeField] private WeaponSnapshotData weaponSnapshot = new();

    [Header("Shelter Assignment")]
    [SerializeField] private string assignedFacilityId = string.Empty;
    [SerializeField] private string assignedRoomId = string.Empty;
    [SerializeField] private FacilityAssignmentKind assignmentKind;

    public string DefinitionId => definitionId ?? string.Empty;
    public string RuntimeId => string.IsNullOrWhiteSpace(runtimeId) ? DefinitionId : runtimeId;
    public PlayerbleCharacterId CharacterId => characterId;
    public NPCType Type => npcType;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? DefinitionId : displayName;
    public int Reliability => Mathf.Clamp(reliability, 0, 100);
    public int CurrentHp => Mathf.Clamp(currentHp, 0, MaxHp);
    public int MaxHp => Mathf.Max(1, maxHp);
    public int MedicalFacilityMaxHpBonus => Mathf.Max(0, medicalFacilityMaxHpBonus);
    public bool IsDead => CurrentHp <= 0 || isCombatOut;
    public float InjuryGauge => Mathf.Clamp(injuryGauge, 0.0f, MaxInjuryGauge);
    public float MaxInjuryGauge => Mathf.Max(1.0f, maxInjuryGauge);
    public CharacterInjuryState InjuryState => injuryState;
    public bool IsDown => isDown;
    public bool IsCombatOut => isCombatOut;
    public bool IsPlayerSquadMember => isPlayerSquadMember;
    public int KillCount => Mathf.Max(0, killCount);
    public WeaponSnapshotData Weapon => weaponSnapshot?.Clone() ?? new WeaponSnapshotData();
    public bool IsAssignedToFacility => !string.IsNullOrWhiteSpace(assignedFacilityId)
        && assignmentKind != FacilityAssignmentKind.None;
    public string AssignedFacilityId => assignedFacilityId ?? string.Empty;
    public string AssignedRoomId => assignedRoomId ?? string.Empty;
    public FacilityAssignmentKind AssignmentKind => assignmentKind;
    public CharacterSnapshotData Snapshot => CreateSnapshot();

    public ShelterMemberRuntimeData()
    {
    }

    /// <summary>Field와 GameDataManager가 사용하는 공용 캐릭터 스냅샷을 셸터 작업 데이터로 복사합니다.</summary>
    public ShelterMemberRuntimeData(CharacterSnapshotData snapshot)
    {
        if (!ApplySnapshot(snapshot))
            throw new ArgumentException("A valid character snapshot is required.", nameof(snapshot));
    }

    /// <summary>기존 NPC 정의 에셋을 신규 캐릭터 스냅샷 구조로 가져오기 위한 부트스트랩 생성자입니다.</summary>
    public ShelterMemberRuntimeData(PlayerbleCharacterDefinition characterDefinition)
        : this(characterDefinition != null
            ? characterDefinition.CreateSnapshot()
            : throw new ArgumentNullException(nameof(characterDefinition)))
    {
    }

    /// <summary>schemaVersion 6 이하 NPC 저장 데이터를 읽기 위한 레거시 생성자입니다.</summary>
    public ShelterMemberRuntimeData(
        string definitionId,
        NPCType type,
        int maxHp,
        int currentHp,
        bool isAssignedToShelter,
        string assignedRoomId,
        float injuryGauge,
        float maxInjuryGauge)
        : this(new CharacterSnapshotData(
            definitionId,
            definitionId,
            PlayerbleCharacterId.Unknown,
            type,
            definitionId,
            0,
            currentHp,
            maxHp,
            injuryGauge,
            maxInjuryGauge,
            CharacterInjuryStateRule.FromGauge(injuryGauge, maxInjuryGauge),
            false,
            currentHp <= 0,
            false,
            0,
            new WeaponSnapshotData()))
    {
        if (isAssignedToShelter)
        {
            assignedFacilityId = string.IsNullOrWhiteSpace(assignedRoomId)
                ? "legacy"
                : assignedRoomId.Trim();
            this.assignedRoomId = assignedRoomId?.Trim() ?? assignedFacilityId;
            assignmentKind = FacilityAssignmentKind.Staff;
        }
    }

    public bool AssignToFacility(string facilityId, string roomId, FacilityAssignmentKind kind)
    {
        if (string.IsNullOrWhiteSpace(facilityId) || kind == FacilityAssignmentKind.None)
            return false;

        string normalizedFacilityId = facilityId.Trim();
        string normalizedRoomId = string.IsNullOrWhiteSpace(roomId)
            ? normalizedFacilityId
            : roomId.Trim();

        if (AssignedFacilityId == normalizedFacilityId
            && AssignedRoomId == normalizedRoomId
            && assignmentKind == kind)
        {
            return false;
        }

        assignedFacilityId = normalizedFacilityId;
        assignedRoomId = normalizedRoomId;
        assignmentKind = kind;
        return true;
    }

    public bool ReleaseFromFacility()
    {
        if (!IsAssignedToFacility
            && string.IsNullOrEmpty(assignedFacilityId)
            && string.IsNullOrEmpty(assignedRoomId))
        {
            return false;
        }

        assignedFacilityId = string.Empty;
        assignedRoomId = string.Empty;
        assignmentKind = FacilityAssignmentKind.None;
        return true;
    }

    public bool SetCurrentHp(int value)
    {
        int clamped = Mathf.Clamp(value, 0, MaxHp);
        if (currentHp == clamped)
            return false;

        currentHp = clamped;
        if (currentHp > 0)
        {
            isDown = false;
            isCombatOut = false;
        }
        return true;
    }

    public bool SetMedicalFacilityMaxHpBonus(int value)
    {
        int normalizedBonus = Mathf.Max(0, value);
        int previousBonus = MedicalFacilityMaxHpBonus;
        if (previousBonus == normalizedBonus)
            return false;

        int baseMaxHp = Mathf.Max(1, MaxHp - previousBonus);
        medicalFacilityMaxHpBonus = normalizedBonus;
        maxHp = baseMaxHp + normalizedBonus;

        // 보너스가 늘어난 만큼 현재 체력도 함께 올려, 업그레이드만으로 체력이 깎인 것처럼 보이지 않게 합니다.
        // 체력 0(사망) 상태는 보너스로 되살리지 않습니다. 보너스가 줄어들면 최대치로 자르기만 합니다.
        int bonusDelta = normalizedBonus - previousBonus;
        if (bonusDelta > 0 && currentHp > 0)
            currentHp += bonusDelta;

        currentHp = Mathf.Min(currentHp, MaxHp);
        return true;
    }

    private bool SetInjuryState(CharacterInjuryState state)
    {
        if (injuryState == state)
            return false;

        injuryState = state;
        return true;
    }

    public bool SetInjuryGauge(float value)
    {
        float clamped = Mathf.Clamp(value, 0.0f, MaxInjuryGauge);
        bool gaugeChanged = !Mathf.Approximately(injuryGauge, clamped);
        injuryGauge = clamped;
        bool stateChanged = SetInjuryState(CharacterInjuryStateRule.FromGauge(injuryGauge, MaxInjuryGauge));
        return gaugeChanged || stateChanged;
    }

    public bool CompleteRecovery()
    {
        bool hpChanged = SetCurrentHp(MaxHp);
        bool gaugeChanged = SetInjuryGauge(0.0f);
        bool combatStateChanged = isDown || isCombatOut;
        isDown = false;
        isCombatOut = false;
        return hpChanged || gaugeChanged || combatStateChanged;
    }

    public bool ApplyDamage(int damage) => damage > 0 && SetCurrentHp(CurrentHp - damage);
    public bool RecoverHp(int amount) => amount > 0 && SetCurrentHp(CurrentHp + amount);

    public bool ReviveToPercent(int percent)
    {
        return percent > 0 && SetCurrentHp(CurrentHp + MaxHp * percent / 100);
    }

    public CharacterSnapshotData CreateSnapshot()
    {
        return new CharacterSnapshotData(
            DefinitionId,
            RuntimeId,
            CharacterId,
            Type,
            DisplayName,
            Reliability,
            CurrentHp,
            MaxHp,
            InjuryGauge,
            MaxInjuryGauge,
            InjuryState,
            IsDown,
            IsCombatOut,
            IsPlayerSquadMember,
            KillCount,
            weaponSnapshot,
            MedicalFacilityMaxHpBonus);
    }

    public bool ApplySnapshot(CharacterSnapshotData snapshot)
    {
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.DefinitionId))
            return false;

        if (!string.IsNullOrWhiteSpace(definitionId)
            && snapshot.DefinitionId != DefinitionId
            && snapshot.RuntimeId != RuntimeId)
        {
            return false;
        }

        definitionId = snapshot.DefinitionId.Trim();
        runtimeId = string.IsNullOrWhiteSpace(snapshot.RuntimeId)
            ? definitionId
            : snapshot.RuntimeId.Trim();
        characterId = snapshot.CharacterId;
        npcType = snapshot.NpcType;
        displayName = string.IsNullOrWhiteSpace(snapshot.DisplayName)
            ? definitionId
            : snapshot.DisplayName;
        reliability = snapshot.Reliability;
        maxHp = snapshot.MaxHp;
        medicalFacilityMaxHpBonus = snapshot.MedicalFacilityMaxHpBonus;
        currentHp = snapshot.IsCombatOut ? 0 : snapshot.CurrentHp;
        maxInjuryGauge = snapshot.MaxInjuryGauge;
        injuryGauge = snapshot.InjurySeverityGauge;
        injuryState = CharacterInjuryStateRule.FromGauge(injuryGauge, maxInjuryGauge);
        isDown = snapshot.IsDown;
        isCombatOut = snapshot.IsCombatOut;
        isPlayerSquadMember = snapshot.IsPlayerSquadMember;
        killCount = snapshot.KillCount;
        weaponSnapshot = snapshot.Weapon;
        return true;
    }

    public bool SetWeaponSnapshot(WeaponSnapshotData snapshot)
    {
        if (snapshot == null)
            return false;

        weaponSnapshot = snapshot.Clone();
        return true;
    }

    public ShelterMemberRuntimeData Clone()
    {
        ShelterMemberRuntimeData clone = new ShelterMemberRuntimeData(CreateSnapshot());
        if (IsAssignedToFacility)
            clone.AssignToFacility(AssignedFacilityId, AssignedRoomId, AssignmentKind);
        return clone;
    }

}
