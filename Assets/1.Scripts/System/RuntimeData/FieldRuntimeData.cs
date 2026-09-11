using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>필드 씬 데이터가 거치는 생명주기 단계입니다.</summary>
public enum FieldPhase
{
    /// <summary>입장 데이터가 아직 적용되지 않은 상태입니다.</summary>
    Uninitialized,

    /// <summary>입장 데이터와 씬 초기값이 준비된 상태입니다.</summary>
    Ready,

    /// <summary>필드 시간이 흐르고 결과를 수집하는 상태입니다.</summary>
    Running,

    /// <summary>귀환 정산 값을 확정하는 상태입니다.</summary>
    Finalizing,

    /// <summary>최종 결과 스냅샷 생성이 끝난 상태입니다.</summary>
    Completed,

    /// <summary>스쿼드가 전멸하여 정산 없이 게임오버 처리를 기다리는 상태입니다.</summary>
    GameOver
}

/// <summary>귀환 정산에서 사용하는 필드의 최종 결과입니다.</summary>
public enum FieldOutcome
{
    /// <summary>아직 최종 결과가 판정되지 않았습니다.</summary>
    None,

    /// <summary>필드 목표를 달성했습니다.</summary>
    Success,

    /// <summary>필드 목표 달성에 실패했습니다.</summary>
    Failure,

    /// <summary>임무 완료와 무관하게 스쿼드가 귀환했습니다.</summary>
    Evacuated
}

/// <summary>필드가 종료된 직접적인 사유입니다.</summary>
public enum FieldEndReason
{
    /// <summary>아직 종료 사유가 확정되지 않았습니다.</summary>
    None,

    /// <summary>임무 목표 달성으로 종료되었습니다.</summary>
    MissionCompleted,

    /// <summary>탈출 지점을 통한 귀환으로 종료되었습니다.</summary>
    Escaped,

    /// <summary>모든 스쿼드원이 필드에서 이탈하여 종료되었습니다.</summary>
    SquadEliminated,

    /// <summary>개발 또는 시스템 명령으로 중단되었습니다.</summary>
    Aborted
}

/// <summary>필드 입장 또는 획득 자원 한 종류의 수량입니다.</summary>
[Serializable]
public sealed class FieldResourceAmountData
{
    [SerializeField] private string resourceId = string.Empty;
    [Min(0)][SerializeField] private int amount;

    /// <summary>자원 종류입니다.</summary>
    public string ResourceId => ResourceIds.Normalize(resourceId);

    /// <summary>0 이상으로 보정된 자원 수량입니다.</summary>
    public int Amount => Mathf.Max(0, amount);

    /// <summary>지정한 자원 종류와 수량으로 데이터를 생성합니다.</summary>
    public FieldResourceAmountData(string resourceId, int amount)
    {
        this.resourceId = ResourceIds.Normalize(resourceId);
        this.amount = Mathf.Max(0, amount);
    }

    /// <summary>현재 값을 복제한 새 자원 데이터를 반환합니다.</summary>
    public FieldResourceAmountData Clone()
    {
        return new FieldResourceAmountData(ResourceId, Amount);
    }

    /// <summary>현재 수량에 지정한 값을 더하고 0 이상으로 보정합니다.</summary>
    public void Add(int value)
    {
        amount = Mathf.Max(0, amount + value);
    }
}

/// <summary>셸터와 필드 씬이 동일하게 저장하고 전달하는 총기 상태 스냅샷입니다.</summary>
[Serializable]
public sealed class WeaponSnapshotData
{
    [SerializeField] private string weaponId = string.Empty;
    [SerializeField] private WeaponType weaponType;
    [SerializeField] private string displayName = string.Empty;
    [Min(0)][SerializeField] private int upgradeLevel;
    [SerializeField] private List<string> equippedPartIds = new();
    [Min(0.0f)][SerializeField] private float damage;
    [Min(0.0f)][SerializeField] private float fireRate;
    [Min(0.0f)][SerializeField] private float reloadSpeed;
    [Min(0.0f)][SerializeField] private float bulletSpread;
    [Min(0.0f)][SerializeField] private float recoil;
    [Min(0)][SerializeField] private int currentMagazineAmmo;
    [Min(0)][SerializeField] private int magazineCapacity;
    [Min(0)][SerializeField] private int reserveAmmo;
    [Min(0)][SerializeField] private int maxReserveAmmo;
    [Min(0.0f)][SerializeField] private float adsSpeed;
    [Min(0.0f)][SerializeField] private float noiseLevel;
    [Min(0.0f)][SerializeField] private float mobility;
    [Min(0.0f)][SerializeField] private float bulletSpeed;

    /// <summary>장착 총기를 식별하는 영속 ID입니다.</summary>
    public string WeaponId => weaponId ?? string.Empty;

    /// <summary>장착 총기의 종류입니다.</summary>
    public WeaponType WeaponType => weaponType;

    /// <summary>UI와 로그에 표시할 총기 이름입니다.</summary>
    public string DisplayName => displayName ?? string.Empty;

    /// <summary>셸터에서 누적된 총기 업그레이드 단계입니다.</summary>
    public int UpgradeLevel => Mathf.Max(0, upgradeLevel);

    /// <summary>현재 장착된 파츠의 영속 ID 목록입니다.</summary>
    public IReadOnlyList<string> EquippedPartIds => equippedPartIds ??= new List<string>();

    /// <summary>개조와 성장이 반영된 공격력입니다.</summary>
    public float Damage => Mathf.Max(0.0f, damage);

    /// <summary>개조와 성장이 반영된 연사 속도입니다.</summary>
    public float FireRate => Mathf.Max(0.0f, fireRate);

    /// <summary>개조와 성장이 반영된 재장전 속도입니다.</summary>
    public float ReloadSpeed => Mathf.Max(0.0f, reloadSpeed);

    /// <summary>개조와 성장이 반영된 탄 퍼짐 값입니다.</summary>
    public float BulletSpread => Mathf.Max(0.0f, bulletSpread);

    /// <summary>개조와 성장이 반영된 반동 값입니다.</summary>
    public float Recoil => Mathf.Max(0.0f, recoil);

    /// <summary>현재 탄창에 남아 있는 탄약 수입니다.</summary>
    public int CurrentMagazineAmmo => Mathf.Clamp(currentMagazineAmmo, 0, MagazineCapacity);

    /// <summary>현재 총기의 탄창 최대 용량입니다.</summary>
    public int MagazineCapacity => Mathf.Max(0, magazineCapacity);

    /// <summary>탄창 밖에 보유 중인 예비 탄약 수입니다.</summary>
    public int ReserveAmmo => Mathf.Clamp(reserveAmmo, 0, MaxReserveAmmo);

    /// <summary>보유할 수 있는 예비 탄약 최대치입니다.</summary>
    public int MaxReserveAmmo => Mathf.Max(0, maxReserveAmmo);

    /// <summary>개조와 성장이 반영된 조준 전환 속도입니다.</summary>
    public float AdsSpeed => Mathf.Max(0.0f, adsSpeed);

    /// <summary>개조와 성장이 반영된 총기 소음 수치입니다.</summary>
    public float NoiseLevel => Mathf.Max(0.0f, noiseLevel);

    /// <summary>개조와 성장이 반영된 총기 기동성 수치입니다.</summary>
    public float Mobility => Mathf.Max(0.0f, mobility);

    /// <summary>개조와 성장이 반영된 탄환 속도입니다.</summary>
    public float BulletSpeed => Mathf.Max(0.0f, bulletSpeed);

    /// <summary>비어 있는 총기 스냅샷을 생성합니다.</summary>
    public WeaponSnapshotData()
    {
    }

    /// <summary>총기 식별자, 성장값, 계산 스탯과 현재 탄약을 모두 지정합니다.</summary>
    public WeaponSnapshotData(
        string weaponId,
        WeaponType weaponType,
        string displayName,
        int upgradeLevel,
        IEnumerable<string> equippedPartIds,
        float damage,
        float fireRate,
        float reloadSpeed,
        float bulletSpread,
        float recoil,
        int currentMagazineAmmo,
        int magazineCapacity,
        int reserveAmmo,
        int maxReserveAmmo,
        float adsSpeed,
        float noiseLevel,
        float mobility,
        float bulletSpeed)
    {
        this.weaponId = weaponId?.Trim() ?? string.Empty;
        this.weaponType = weaponType;
        this.displayName = displayName?.Trim() ?? string.Empty;
        this.upgradeLevel = Mathf.Max(0, upgradeLevel);
        this.equippedPartIds = NormalizePartIds(equippedPartIds);
        this.damage = Mathf.Max(0.0f, damage);
        this.fireRate = Mathf.Max(0.0f, fireRate);
        this.reloadSpeed = Mathf.Max(0.0f, reloadSpeed);
        this.bulletSpread = Mathf.Max(0.0f, bulletSpread);
        this.recoil = Mathf.Max(0.0f, recoil);
        this.magazineCapacity = Mathf.Max(0, magazineCapacity);
        this.currentMagazineAmmo = Mathf.Clamp(currentMagazineAmmo, 0, this.magazineCapacity);
        this.maxReserveAmmo = Mathf.Max(0, maxReserveAmmo);
        this.reserveAmmo = Mathf.Clamp(reserveAmmo, 0, this.maxReserveAmmo);
        this.adsSpeed = Mathf.Max(0.0f, adsSpeed);
        this.noiseLevel = Mathf.Max(0.0f, noiseLevel);
        this.mobility = Mathf.Max(0.0f, mobility);
        this.bulletSpeed = Mathf.Max(0.0f, bulletSpeed);
    }

    /// <summary>총기 정의와 현재 씬 탄약값을 합친 스냅샷을 생성합니다.</summary>
    public static WeaponSnapshotData Create(Weapon definition, Gun controller, int reserve, int reserveMaximum)
    {
        int magazineMaximum = controller != null
            ? controller.MaxBullet
            : Mathf.Max(0, Mathf.RoundToInt(definition != null ? definition.baseAmmo : 0.0f));
        float resolvedDamage = controller != null
            ? controller.HitscanDamage
            : definition != null ? definition.baseDamage : 0.0f;
        float resolvedRecoil = controller != null
            ? Mathf.Max(controller.RecoilPitchKick, controller.RecoilYawKick)
            : definition != null ? definition.baseRecoil : 0.0f;

        return new WeaponSnapshotData(
            definition != null ? definition.weaponId : string.Empty,
            definition != null ? definition.weaponType : default,
            definition != null ? definition.weaponName : string.Empty,
            0,
            null,
            resolvedDamage,
            definition != null ? definition.baseFireRate : 0.0f,
            definition != null ? definition.baseReloadSpeed : 0.0f,
            definition != null ? definition.baseBulletSpray : 0.0f,
            resolvedRecoil,
            controller != null ? controller.CurrentBullet : magazineMaximum,
            magazineMaximum,
            reserve,
            reserveMaximum,
            definition != null ? definition.baseADSSpeed : 0.0f,
            definition != null ? definition.baseNoiseLevel : 0.0f,
            definition != null ? definition.baseMobility : 0.0f,
            definition != null ? definition.baseBulletSpeed : 0.0f);
    }

    /// <summary>현재 총기 스냅샷을 깊은 복사하여 반환합니다.</summary>
    public WeaponSnapshotData Clone()
    {
        return new WeaponSnapshotData(
            WeaponId, WeaponType, DisplayName, UpgradeLevel, EquippedPartIds,
            Damage, FireRate, ReloadSpeed, BulletSpread, Recoil,
            CurrentMagazineAmmo, MagazineCapacity, ReserveAmmo, MaxReserveAmmo,
            AdsSpeed, NoiseLevel, Mobility, BulletSpeed);
    }

    /// <summary>장착 총기를 바꾸고 해당 정의의 기본 스탯으로 초기화합니다.</summary>
    public void SetWeaponDefinition(Weapon definition)
    {
        if (definition == null)
        {
            return;
        }

        weaponId = definition.weaponId?.Trim() ?? string.Empty;
        weaponType = definition.weaponType;
        displayName = definition.weaponName?.Trim() ?? string.Empty;
        upgradeLevel = 0;
        equippedPartIds ??= new List<string>();
        equippedPartIds.Clear();
        damage = Mathf.Max(0.0f, definition.baseDamage);
        fireRate = Mathf.Max(0.0f, definition.baseFireRate);
        reloadSpeed = Mathf.Max(0.0f, definition.baseReloadSpeed);
        bulletSpread = Mathf.Max(0.0f, definition.baseBulletSpray);
        recoil = Mathf.Max(0.0f, definition.baseRecoil);
        magazineCapacity = Mathf.Max(0, Mathf.RoundToInt(definition.baseAmmo));
        currentMagazineAmmo = magazineCapacity;
        adsSpeed = Mathf.Max(0.0f, definition.baseADSSpeed);
        noiseLevel = Mathf.Max(0.0f, definition.baseNoiseLevel);
        mobility = Mathf.Max(0.0f, definition.baseMobility);
        bulletSpeed = Mathf.Max(0.0f, definition.baseBulletSpeed);
    }

    /// <summary>현재 탄창과 예비 탄약을 설정합니다.</summary>
    public void SetAmmo(int magazine, int magazineMaximum, int reserve, int reserveMaximum)
    {
        magazineCapacity = Mathf.Max(0, magazineMaximum);
        currentMagazineAmmo = Mathf.Clamp(magazine, 0, magazineCapacity);
        maxReserveAmmo = Mathf.Max(0, reserveMaximum);
        reserveAmmo = Mathf.Clamp(reserve, 0, maxReserveAmmo);
    }

    /// <summary>총기 업그레이드 단계를 설정합니다.</summary>
    public void SetUpgradeLevel(int value)
    {
        upgradeLevel = Mathf.Max(0, value);
    }

    /// <summary>파츠 ID와 스탯 보정값을 현재 스냅샷에 한 번 적용합니다.</summary>
    public bool TryEquipPart(WeaponPart part)
    {
        if (part == null || string.IsNullOrWhiteSpace(part.partId))
        {
            return false;
        }

        string partId = part.partId.Trim();
        equippedPartIds ??= new List<string>();
        if (equippedPartIds.Contains(partId))
        {
            return false;
        }

        equippedPartIds.Add(partId);
        if (part.modifiers != null)
        {
            for (int i = 0; i < part.modifiers.Count; i++)
            {
                ApplyModifier(part.modifiers[i]);
            }
        }

        return true;
    }

    /// <summary>씬에서 바뀐 탄약값을 반영하되 셸터에서 관리하는 성장 스탯은 유지합니다.</summary>
    /// <remarks>현재 스냅샷에 총기 정의가 없을 때만 씬 총기 스냅샷 전체를 받아 초기화합니다.</remarks>
    public void MergeSceneAmmo(WeaponSnapshotData sceneWeapon)
    {
        if (sceneWeapon == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(WeaponId))
        {
            CopyFrom(sceneWeapon);
            return;
        }

        SetAmmo(sceneWeapon.CurrentMagazineAmmo, sceneWeapon.MagazineCapacity, sceneWeapon.ReserveAmmo, sceneWeapon.MaxReserveAmmo);
    }

    /// <summary>지정한 총기 스냅샷 전체를 현재 값으로 깊은 복사합니다.</summary>
    private void CopyFrom(WeaponSnapshotData source)
    {
        WeaponSnapshotData clone = source?.Clone() ?? new WeaponSnapshotData();
        weaponId = clone.WeaponId;
        weaponType = clone.WeaponType;
        displayName = clone.DisplayName;
        upgradeLevel = clone.UpgradeLevel;
        equippedPartIds = NormalizePartIds(clone.EquippedPartIds);
        damage = clone.Damage;
        fireRate = clone.FireRate;
        reloadSpeed = clone.ReloadSpeed;
        bulletSpread = clone.BulletSpread;
        recoil = clone.Recoil;
        currentMagazineAmmo = clone.CurrentMagazineAmmo;
        magazineCapacity = clone.MagazineCapacity;
        reserveAmmo = clone.ReserveAmmo;
        maxReserveAmmo = clone.MaxReserveAmmo;
        adsSpeed = clone.AdsSpeed;
        noiseLevel = clone.NoiseLevel;
        mobility = clone.Mobility;
        bulletSpeed = clone.BulletSpeed;
    }

    private void ApplyModifier(StatModifier modifier)
    {
        if (modifier == null) return;

        switch (modifier.statType)
        {
            case StatType.Damage: damage = ApplyOperation(damage, modifier); break;
            case StatType.FireRate: fireRate = ApplyOperation(fireRate, modifier); break;
            case StatType.ReloadSpeed: reloadSpeed = ApplyOperation(reloadSpeed, modifier); break;
            case StatType.BulletSpray: bulletSpread = ApplyOperation(bulletSpread, modifier); break;
            case StatType.Recoil: recoil = ApplyOperation(recoil, modifier); break;
            case StatType.Ammo:
                magazineCapacity = Mathf.Max(0, Mathf.RoundToInt(ApplyOperation(magazineCapacity, modifier)));
                currentMagazineAmmo = Mathf.Clamp(currentMagazineAmmo, 0, magazineCapacity);
                break;
            case StatType.ADSSpeed: adsSpeed = ApplyOperation(adsSpeed, modifier); break;
            case StatType.NoiseLevel: noiseLevel = ApplyOperation(noiseLevel, modifier); break;
            case StatType.Mobility: mobility = ApplyOperation(mobility, modifier); break;
            case StatType.BulletSpeed: bulletSpeed = ApplyOperation(bulletSpeed, modifier); break;
        }
    }

    private static float ApplyOperation(float currentValue, StatModifier modifier)
    {
        float result = modifier.modifierType switch
        {
            ModifierOp.Add => currentValue + modifier.value,
            ModifierOp.Multiply => currentValue * modifier.value,
            ModifierOp.Minus => currentValue - modifier.value,
            ModifierOp.Division => Mathf.Approximately(modifier.value, 0.0f) ? currentValue : currentValue / modifier.value,
            _ => currentValue
        };
        return Mathf.Max(0.0f, result);
    }

    private static List<string> NormalizePartIds(IEnumerable<string> source)
    {
        List<string> result = new();
        if (source == null) return result;

        foreach (string partId in source)
        {
            if (string.IsNullOrWhiteSpace(partId)) continue;
            string normalized = partId.Trim();
            if (!result.Contains(normalized)) result.Add(normalized);
        }

        return result;
    }
}

/// <summary>셸터와 필드 씬이 동일하게 저장하고 전달하는 캐릭터 상태 스냅샷입니다.</summary>
[Serializable]
public sealed class CharacterSnapshotData
{
    [SerializeField] private string definitionId = string.Empty;
    [SerializeField] private string runtimeId = string.Empty;
    [SerializeField] private PlayerbleCharacterId characterId;
    [SerializeField] private NPCType npcType;
    [SerializeField] private string displayName = string.Empty;
    [Range(0, 100)][SerializeField] private int reliability;
    [Min(0)][SerializeField] private int currentHp;
    [Min(1)][SerializeField] private int maxHp = 1;
    [Min(0.0f)][SerializeField] private float injurySeverityGauge;
    [Min(1.0f)][SerializeField] private float maxInjuryGauge = 100.0f;
    [SerializeField] private CharacterInjuryState injuryState;
    [SerializeField] private bool isDown;
    [SerializeField] private bool isCombatOut;
    [SerializeField] private bool isPlayerSquadMember;
    [Min(0)][SerializeField] private int killCount;
    [SerializeField] private WeaponSnapshotData weapon = new();

    /// <summary>보유 캐릭터 목록과 저장 데이터에서 사용하는 영속 캐릭터 정의 ID입니다.</summary>
    public string DefinitionId => definitionId ?? string.Empty;

    /// <summary>현재 씬의 캐릭터 인스턴스를 식별하는 런타임 ID입니다.</summary>
    public string RuntimeId => runtimeId ?? string.Empty;

    /// <summary>플레이어블 캐릭터의 고정 식별자입니다.</summary>
    public PlayerbleCharacterId CharacterId => characterId;

    /// <summary>셸터에서 사용하는 NPC 역할 종류입니다.</summary>
    public NPCType NpcType => npcType;

    /// <summary>UI와 로그에 표시할 캐릭터 이름입니다.</summary>
    public string DisplayName => displayName ?? string.Empty;

    /// <summary>0~100 범위로 보정된 현재 신뢰도입니다.</summary>
    public int Reliability => Mathf.Clamp(reliability, 0, 100);

    /// <summary>현재 캐릭터 HP입니다.</summary>
    public int CurrentHp => Mathf.Clamp(currentHp, 0, MaxHp);

    /// <summary>업그레이드가 반영된 최대 HP입니다.</summary>
    public int MaxHp => Mathf.Max(1, maxHp);

    /// <summary>0이 정상이고 최대값이 가장 심한 부상인 누적 부상 게이지입니다.</summary>
    public float InjurySeverityGauge => Mathf.Clamp(injurySeverityGauge, 0.0f, MaxInjuryGauge);

    /// <summary>누적 부상 게이지의 최대값입니다.</summary>
    public float MaxInjuryGauge => Mathf.Max(1.0f, maxInjuryGauge);

    /// <summary>현재 부상 단계입니다.</summary>
    public CharacterInjuryState InjuryState => injuryState;

    /// <summary>현재 구조 가능한 다운 상태인지 여부입니다.</summary>
    public bool IsDown => isDown;

    /// <summary>현재 필드에서 이탈한 상태인지 여부입니다.</summary>
    public bool IsCombatOut => isCombatOut;

    /// <summary>현재 직접 조작 중인 PlayerSquadMember인지 여부입니다.</summary>
    public bool IsPlayerSquadMember => isPlayerSquadMember;

    /// <summary>현재 출격에서 이 캐릭터가 확정한 적 처치 수입니다.</summary>
    public int KillCount => Mathf.Max(0, killCount);

    /// <summary>외부 변경을 막기 위해 깊은 복사한 장착 총기 스냅샷을 반환합니다.</summary>
    public WeaponSnapshotData Weapon => weapon?.Clone() ?? new WeaponSnapshotData();

    /// <summary>비어 있는 캐릭터 스냅샷을 생성합니다.</summary>
    public CharacterSnapshotData()
    {
    }

    /// <summary>캐릭터와 장착 총기의 현재 상태 전체를 생성합니다.</summary>
    public CharacterSnapshotData(
        string definitionId, string runtimeId, PlayerbleCharacterId characterId, NPCType npcType,
        string displayName, int reliability, int currentHp, int maxHp,
        float injurySeverityGauge, float maxInjuryGauge, CharacterInjuryState injuryState,
        bool isDown, bool isCombatOut, bool isPlayerSquadMember, int killCount,
        WeaponSnapshotData weapon)
    {
        this.definitionId = definitionId?.Trim() ?? string.Empty;
        this.runtimeId = runtimeId?.Trim() ?? string.Empty;
        this.characterId = characterId;
        this.npcType = npcType;
        this.displayName = displayName?.Trim() ?? string.Empty;
        this.reliability = Mathf.Clamp(reliability, 0, 100);
        this.maxHp = Mathf.Max(1, maxHp);
        this.currentHp = Mathf.Clamp(currentHp, 0, this.maxHp);
        this.maxInjuryGauge = Mathf.Max(1.0f, maxInjuryGauge);
        this.injurySeverityGauge = Mathf.Clamp(injurySeverityGauge, 0.0f, this.maxInjuryGauge);
        this.injuryState = injuryState;
        this.isDown = isDown;
        this.isCombatOut = isCombatOut;
        this.isPlayerSquadMember = isPlayerSquadMember;
        this.killCount = Mathf.Max(0, killCount);
        this.weapon = weapon?.Clone() ?? new WeaponSnapshotData();
    }

    /// <summary>캐릭터와 장착 총기 상태 전체를 깊은 복사하여 반환합니다.</summary>
    public CharacterSnapshotData Clone()
    {
        return new CharacterSnapshotData(
            DefinitionId, RuntimeId, CharacterId, NpcType, DisplayName, Reliability,
            CurrentHp, MaxHp, InjurySeverityGauge, MaxInjuryGauge, InjuryState,
            IsDown, IsCombatOut, IsPlayerSquadMember, KillCount, weapon);
    }

    /// <summary>보유 캐릭터 목록에서 유지할 영속 캐릭터 식별 정보를 설정합니다.</summary>
    public void SetPersistentIdentity(string value, NPCType type)
    {
        definitionId = value?.Trim() ?? string.Empty;
        npcType = type;
    }

    /// <summary>현재 씬 인스턴스에서 사용하는 캐릭터 식별 정보를 설정합니다.</summary>
    public void SetSceneIdentity(string value, PlayerbleCharacterId playableId, string name)
    {
        runtimeId = value?.Trim() ?? string.Empty;
        characterId = playableId;
        if (!string.IsNullOrWhiteSpace(name)) displayName = name.Trim();
    }

    /// <summary>신뢰도를 0~100 범위로 설정합니다.</summary>
    public void SetReliability(int value) => reliability = Mathf.Clamp(value, 0, 100);

    /// <summary>현재 HP, 부상, 다운, 이탈 및 조작 역할 상태를 함께 설정합니다.</summary>
    public void SetCombatState(
        int hp, int hpMaximum, float injurySeverity, float injuryMaximum,
        CharacterInjuryState state, bool down, bool combatOut, bool playerSquadMember)
    {
        maxHp = Mathf.Max(1, hpMaximum);
        currentHp = Mathf.Clamp(hp, 0, maxHp);
        maxInjuryGauge = Mathf.Max(1.0f, injuryMaximum);
        injurySeverityGauge = Mathf.Clamp(injurySeverity, 0.0f, maxInjuryGauge);
        injuryState = state;
        isDown = down;
        isCombatOut = combatOut;
        isPlayerSquadMember = playerSquadMember;
    }

    /// <summary>장착 총기 스냅샷을 깊은 복사하여 설정합니다.</summary>
    public void SetWeapon(WeaponSnapshotData value) => weapon = value?.Clone() ?? new WeaponSnapshotData();

    /// <summary>현재 직접 조작 중인 PlayerSquadMember 여부를 설정합니다.</summary>
    public void SetPlayerSquadMember(bool value) => isPlayerSquadMember = value;

    /// <summary>현재 출격의 캐릭터별 적 처치 수를 1 증가시킵니다.</summary>
    public void RecordKill() => killCount++;

    /// <summary>철수 시점에 다운 상태인 캐릭터를 필드 이탈 상태로 확정합니다.</summary>
    public void ConfirmCombatOutIfDown()
    {
        if (!isDown) return;
        isDown = false;
        isCombatOut = true;
        currentHp = 0;
    }
}

/// <summary>필드 입장 시점에 확정하는 스쿼드원 한 명의 초기 데이터입니다.</summary>
[Serializable]
public sealed class FieldMemberEntryData
{
    [SerializeField] private string definitionId = string.Empty;
    [SerializeField] private string runtimeId = string.Empty;
    [SerializeField] private PlayerbleCharacterId characterId;
    [SerializeField] private NPCType npcType;
    [SerializeField] private string displayName = string.Empty;
    [Range(0, 100)][SerializeField] private int reliability;
    [Min(0)][SerializeField] private int currentHp;
    [Min(1)][SerializeField] private int maxHp = 1;
    [Min(0.0f)][SerializeField] private float injuryGauge;
    [Min(1.0f)][SerializeField] private float maxInjuryGauge = 100.0f;
    [SerializeField] private CharacterInjuryState injuryState;
    [SerializeField] private string weaponId = string.Empty;
    [Min(0)][SerializeField] private int magazineAmmo;
    [Min(0)][SerializeField] private int reserveAmmo;
    [SerializeField] private WeaponSnapshotData weaponSnapshot = new();
    [SerializeField] private bool isPlayerSquadMember;
    [SerializeField] private bool isDown;
    [SerializeField] private bool isCombatOut;
    [Min(0)][SerializeField] private int killCount;
    [Min(0)][SerializeField] private int temporaryHpPenalty;

    /// <summary>영속 보유 캐릭터 목록에서 사용하는 캐릭터 정의 ID입니다.</summary>
    public string DefinitionId => definitionId ?? string.Empty;

    /// <summary>현재 필드 씬 인스턴스를 식별하는 런타임 ID입니다.</summary>
    public string RuntimeId => runtimeId ?? string.Empty;

    /// <summary>플레이어블 캐릭터의 고정 ID입니다.</summary>
    public PlayerbleCharacterId CharacterId => characterId;

    /// <summary>셸터 NPC 역할 종류입니다.</summary>
    public NPCType NpcType => npcType;

    /// <summary>결과 UI와 로그에 표시할 이름입니다.</summary>
    public string DisplayName => displayName ?? string.Empty;

    /// <summary>필드 입장 시점의 신뢰도입니다.</summary>
    public int Reliability => Mathf.Clamp(reliability, 0, 100);

    /// <summary>필드 입장 시점의 현재 HP입니다.</summary>
    public int CurrentHp => Mathf.Clamp(currentHp, 0, MaxHp);

    /// <summary>필드 입장 시점의 최대 HP입니다.</summary>
    public int MaxHp => Mathf.Max(1, maxHp);

    /// <summary>필드 기준으로 환산된 누적 부상 게이지입니다.</summary>
    public float InjuryGauge => Mathf.Clamp(injuryGauge, 0.0f, MaxInjuryGauge);

    /// <summary>누적 부상 게이지의 최대값입니다.</summary>
    public float MaxInjuryGauge => Mathf.Max(1.0f, maxInjuryGauge);

    /// <summary>필드 입장 시점의 부상 단계입니다.</summary>
    public CharacterInjuryState InjuryState => injuryState;

    /// <summary>장착 무기의 정의 ID입니다.</summary>
    public string WeaponId => weaponId ?? string.Empty;

    /// <summary>필드 입장 시점의 탄창 내 탄약 수입니다.</summary>
    public int MagazineAmmo => Mathf.Max(0, magazineAmmo);

    /// <summary>필드 입장 시점의 예비 탄약 수입니다.</summary>
    public int ReserveAmmo => Mathf.Max(0, reserveAmmo);

    /// <summary>필드 시작 시 직접 조작 대상으로 지정된 멤버인지 여부입니다.</summary>
    public bool IsPlayerSquadMember => isPlayerSquadMember;

    /// <summary>필드 입장 시점에 이미 다운 상태인지 여부입니다.</summary>
    public bool IsDown => isDown;

    /// <summary>필드 입장 시점에 이미 필드 이탈 상태인지 여부입니다.</summary>
    public bool IsCombatOut => isCombatOut;

    /// <summary>입장 스냅샷에 포함된 현재 출격의 캐릭터별 처치 수입니다.</summary>
    public int KillCount => Mathf.Max(0, killCount);

    /// <summary>출격 중에만 적용하고 귀환 정산 시 복원할 고정 HP 감소량입니다.</summary>
    public int TemporaryHpPenalty => Mathf.Max(0, temporaryHpPenalty);

    /// <summary>셸터와 필드가 공통으로 사용하는 캐릭터·총기 스냅샷입니다.</summary>
    public CharacterSnapshotData Snapshot => new CharacterSnapshotData(
        DefinitionId,
        RuntimeId,
        CharacterId,
        NpcType,
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
        weaponSnapshot);

    /// <summary>스쿼드원 한 명의 필드 입장 값을 생성합니다.</summary>
    public FieldMemberEntryData(
        string definitionId,
        string runtimeId,
        PlayerbleCharacterId characterId,
        string displayName,
        int currentHp,
        int maxHp,
        float injuryGauge,
        float maxInjuryGauge,
        CharacterInjuryState injuryState,
        string weaponId,
        int magazineAmmo,
        int reserveAmmo,
        bool isPlayerSquadMember)
    {
        this.definitionId = definitionId?.Trim() ?? string.Empty;
        this.runtimeId = runtimeId?.Trim() ?? string.Empty;
        this.characterId = characterId;
        this.displayName = displayName?.Trim() ?? string.Empty;
        this.maxHp = Mathf.Max(1, maxHp);
        this.currentHp = Mathf.Clamp(currentHp, 0, this.maxHp);
        this.maxInjuryGauge = Mathf.Max(1.0f, maxInjuryGauge);
        this.injuryGauge = Mathf.Clamp(injuryGauge, 0.0f, this.maxInjuryGauge);
        this.injuryState = injuryState;
        this.weaponId = weaponId?.Trim() ?? string.Empty;
        this.magazineAmmo = Mathf.Max(0, magazineAmmo);
        this.reserveAmmo = Mathf.Max(0, reserveAmmo);
        weaponSnapshot = new WeaponSnapshotData(
            this.weaponId,
            default,
            string.Empty,
            0,
            null,
            0.0f,
            0.0f,
            0.0f,
            0.0f,
            0.0f,
            this.magazineAmmo,
            this.magazineAmmo,
            this.reserveAmmo,
            this.reserveAmmo,
            0.0f,
            0.0f,
            0.0f,
            0.0f);
        this.isPlayerSquadMember = isPlayerSquadMember;
        isDown = false;
        isCombatOut = false;
        killCount = 0;
    }

    /// <summary>공용 캐릭터 스냅샷을 필드 입장 데이터로 복제합니다.</summary>
    public FieldMemberEntryData(CharacterSnapshotData snapshot)
    {
        CharacterSnapshotData source = snapshot?.Clone() ?? new CharacterSnapshotData();
        definitionId = source.DefinitionId;
        runtimeId = source.RuntimeId;
        characterId = source.CharacterId;
        npcType = source.NpcType;
        displayName = source.DisplayName;
        reliability = source.Reliability;
        maxHp = source.MaxHp;
        currentHp = source.CurrentHp;
        maxInjuryGauge = source.MaxInjuryGauge;
        injuryGauge = source.InjurySeverityGauge;
        injuryState = source.InjuryState;
        weaponSnapshot = source.Weapon;
        weaponId = weaponSnapshot.WeaponId;
        magazineAmmo = weaponSnapshot.CurrentMagazineAmmo;
        reserveAmmo = weaponSnapshot.ReserveAmmo;
        isPlayerSquadMember = source.IsPlayerSquadMember;
        isDown = source.IsDown;
        isCombatOut = source.IsCombatOut;
        killCount = source.KillCount;
    }

    /// <summary>공용 캐릭터 스냅샷에 출격 중 임시 HP 감소를 적용합니다.</summary>
    public FieldMemberEntryData(CharacterSnapshotData snapshot, int requestedTemporaryHpPenalty)
        : this(snapshot)
    {
        temporaryHpPenalty = Mathf.Min(
            Mathf.Max(0, requestedTemporaryHpPenalty),
            currentHp);
        currentHp -= temporaryHpPenalty;
    }

    /// <summary>이미 HP가 감소된 스냅샷에 임시 감소량 메타데이터만 유지합니다.</summary>
    public void PreserveTemporaryHpPenalty(int amount)
    {
        temporaryHpPenalty = Mathf.Max(0, amount);
    }

    /// <summary>현재 값을 복제한 새 멤버 입장 데이터를 반환합니다.</summary>
    public FieldMemberEntryData Clone()
    {
        FieldMemberEntryData clone = new FieldMemberEntryData(Snapshot);
        clone.PreserveTemporaryHpPenalty(TemporaryHpPenalty);
        return clone;
    }
}

/// <summary>셸터 또는 이전 씬에서 필드 씬으로 전달하는 입장 스냅샷입니다.</summary>
[Serializable]
public sealed class FieldEntryData
{
    [SerializeField] private string fieldId = string.Empty;
    [SerializeField] private string stageId = string.Empty;
    [SerializeField] private int randomSeed;
    [SerializeField] private long startedAtUnixMilliseconds;
    [SerializeField] private List<FieldMemberEntryData> members = new();
    [SerializeField] private List<FieldResourceAmountData> startingResources = new();

    /// <summary>출격 한 회를 구분하는 고유 ID입니다.</summary>
    public string FieldId => fieldId ?? string.Empty;

    /// <summary>필드가 진행되는 스테이지 ID입니다.</summary>
    public string StageId => stageId ?? string.Empty;

    /// <summary>필드의 결정적 랜덤 처리에 사용할 시드입니다.</summary>
    public int RandomSeed => randomSeed;

    /// <summary>출격 데이터를 만든 UTC 시각의 Unix 밀리초 값입니다.</summary>
    public long StartedAtUnixMilliseconds => Math.Max(0L, startedAtUnixMilliseconds);

    /// <summary>스쿼드 순서대로 확정된 멤버 입장 데이터입니다.</summary>
    public IReadOnlyList<FieldMemberEntryData> Members => members;

    /// <summary>필드 입장 시점에 보유한 공용 자원 스냅샷입니다.</summary>
    public IReadOnlyList<FieldResourceAmountData> StartingResources => startingResources;

    /// <summary>필드 식별 정보와 생성 시각으로 빈 입장 스냅샷을 만듭니다.</summary>
    public FieldEntryData(string fieldId, string stageId, int randomSeed, long startedAtUnixMilliseconds)
    {
        this.fieldId = fieldId?.Trim() ?? string.Empty;
        this.stageId = stageId?.Trim() ?? string.Empty;
        this.randomSeed = randomSeed;
        this.startedAtUnixMilliseconds = Math.Max(0L, startedAtUnixMilliseconds);
    }

    /// <summary>입장 스냅샷의 마지막에 스쿼드원을 추가합니다.</summary>
    public void AddMember(FieldMemberEntryData member)
    {
        if (member != null)
        {
            members.Add(member.Clone());
        }
    }

    /// <summary>스쿼드 순서를 유지한 채 지정 위치의 멤버 초기값을 교체합니다.</summary>
    public void SetMemberAt(int index, FieldMemberEntryData member)
    {
        if (index < 0 || member == null)
        {
            return;
        }

        if (index < members.Count)
        {
            members[index] = member.Clone();
            return;
        }

        if (index == members.Count)
        {
            members.Add(member.Clone());
        }
    }

    /// <summary>현재 입장 스냅샷의 스쿼드원 목록을 비웁니다.</summary>
    public void ClearMembers()
    {
        members.Clear();
    }

    /// <summary>입장 시점의 공용 자원 수량을 종류별로 누적합니다.</summary>
    public void AddStartingResource(string resourceId, int amount)
    {
        string id = ResourceIds.Normalize(resourceId);
        if (string.IsNullOrEmpty(id) || amount <= 0)
        {
            return;
        }

        FieldResourceAmountData existing = startingResources.Find(
            entry => string.Equals(
                entry.ResourceId,
                id,
                StringComparison.Ordinal));
        if (existing != null)
        {
            existing.Add(amount);
            return;
        }

        startingResources.Add(new FieldResourceAmountData(id, amount));
    }

    /// <summary>입장 스냅샷 전체를 깊은 복사하여 반환합니다.</summary>
    public FieldEntryData Clone()
    {
        FieldEntryData clone = new FieldEntryData(FieldId, StageId, RandomSeed, StartedAtUnixMilliseconds);
        for (int i = 0; i < members.Count; i++)
        {
            clone.AddMember(members[i]);
        }

        for (int i = 0; i < startingResources.Count; i++)
        {
            FieldResourceAmountData resource = startingResources[i];
            if (resource != null)
            {
            clone.AddStartingResource(resource.ResourceId, resource.Amount);
            }
        }

        return clone;
    }
}

/// <summary>필드 진행 중 계속 갱신되는 스쿼드원 한 명의 상태입니다.</summary>
[Serializable]
public sealed class FieldMemberRuntimeData
{
    [SerializeField] private FieldMemberEntryData entryData;
    [Min(0)][SerializeField] private int currentHp;
    [Min(1)][SerializeField] private int maxHp = 1;
    [Min(0.0f)][SerializeField] private float injuryGauge;
    [Min(1.0f)][SerializeField] private float maxInjuryGauge = 100.0f;
    [SerializeField] private CharacterInjuryState injuryState;
    [SerializeField] private bool isDown;
    [SerializeField] private bool isCombatOut;
    [SerializeField] private string weaponId = string.Empty;
    [Min(0)][SerializeField] private int magazineAmmo;
    [Min(0)][SerializeField] private int magazineCapacity;
    [Min(0)][SerializeField] private int reserveAmmo;
    [Min(0)][SerializeField] private int maxReserveAmmo;
    [SerializeField] private WeaponSnapshotData weaponSnapshot = new();
    [SerializeField] private bool isPlayerSquadMember;
    [Min(0)][SerializeField] private int killCount;

    /// <summary>이 런타임 상태의 기준이 된 입장 데이터입니다.</summary>
    public FieldMemberEntryData EntryData => entryData;

    /// <summary>현재 씬 인스턴스를 식별하는 런타임 ID입니다.</summary>
    public string RuntimeId => entryData?.RuntimeId ?? string.Empty;

    /// <summary>플레이어블 캐릭터의 고정 ID입니다.</summary>
    public PlayerbleCharacterId CharacterId => entryData?.CharacterId ?? PlayerbleCharacterId.Unknown;

    /// <summary>현재 HP입니다.</summary>
    public int CurrentHp => Mathf.Clamp(currentHp, 0, MaxHp);

    /// <summary>현재 적용 중인 최대 HP입니다.</summary>
    public int MaxHp => Mathf.Max(1, maxHp);

    /// <summary>현재 누적 부상 게이지입니다.</summary>
    public float InjuryGauge => Mathf.Clamp(injuryGauge, 0.0f, MaxInjuryGauge);

    /// <summary>현재 적용 중인 부상 게이지 최대값입니다.</summary>
    public float MaxInjuryGauge => Mathf.Max(1.0f, maxInjuryGauge);

    /// <summary>현재 부상 단계입니다.</summary>
    public CharacterInjuryState InjuryState => injuryState;

    /// <summary>현재 다운 상태인지 여부입니다.</summary>
    public bool IsDown => isDown;

    /// <summary>현재 필드 이탈 상태인지 여부입니다.</summary>
    public bool IsCombatOut => isCombatOut;

    /// <summary>현재 장착한 무기의 정의 ID입니다.</summary>
    public string WeaponId => weaponId ?? string.Empty;

    /// <summary>현재 탄창에 남은 탄약 수입니다.</summary>
    public int MagazineAmmo => Mathf.Max(0, magazineAmmo);

    /// <summary>현재 무기의 탄창 최대 용량입니다.</summary>
    public int MagazineCapacity => Mathf.Max(0, magazineCapacity);

    /// <summary>현재 보유 중인 예비 탄약 수입니다.</summary>
    public int ReserveAmmo => Mathf.Max(0, reserveAmmo);

    /// <summary>현재 보유할 수 있는 예비 탄약 최대치입니다.</summary>
    public int MaxReserveAmmo => Mathf.Max(0, maxReserveAmmo);

    /// <summary>현재 직접 조작 중인 PlayerSquadMember인지 여부입니다.</summary>
    public bool IsPlayerSquadMember => isPlayerSquadMember;

    /// <summary>이 멤버가 확정한 적 처치 수입니다.</summary>
    public int KillCount => Mathf.Max(0, killCount);

    public int TemporaryHpPenalty => entryData?.TemporaryHpPenalty ?? 0;

    /// <summary>GameDataManager와 필드 결과가 그대로 공유하는 현재 캐릭터 스냅샷입니다.</summary>
    public CharacterSnapshotData Snapshot
    {
        get
        {
            CharacterSnapshotData snapshot = entryData?.Snapshot ?? new CharacterSnapshotData();
            snapshot.SetCombatState(
                CurrentHp,
                MaxHp,
                InjuryGauge,
                MaxInjuryGauge,
                InjuryState,
                IsDown,
                IsCombatOut,
                IsPlayerSquadMember);
            snapshot.SetWeapon(weaponSnapshot);
            for (int i = 0; i < KillCount; i++)
            {
                snapshot.RecordKill();
            }

            return snapshot;
        }
    }

    /// <summary>입장 데이터를 기준으로 멤버 런타임 상태를 생성합니다.</summary>
    public FieldMemberRuntimeData(FieldMemberEntryData source)
    {
        entryData = source?.Clone();
        SetSceneSnapshot(source?.Snapshot);
    }

    /// <summary>공용 캐릭터 스냅샷의 현재 필드 상태를 런타임 데이터에 반영합니다.</summary>
    public void SetSceneSnapshot(CharacterSnapshotData snapshot)
    {
        if (snapshot == null)
        {
            return;
        }

        maxHp = snapshot.MaxHp;
        currentHp = snapshot.CurrentHp;
        maxInjuryGauge = snapshot.MaxInjuryGauge;
        injuryGauge = snapshot.InjurySeverityGauge;
        injuryState = snapshot.InjuryState;
        isDown = snapshot.IsDown;
        isCombatOut = snapshot.IsCombatOut;
        isPlayerSquadMember = snapshot.IsPlayerSquadMember;

        WeaponSnapshotData incomingWeapon = snapshot.Weapon;
        if (weaponSnapshot == null || string.IsNullOrWhiteSpace(weaponSnapshot.WeaponId))
        {
            weaponSnapshot = incomingWeapon;
        }
        else
        {
            weaponSnapshot.MergeSceneAmmo(incomingWeapon);
        }

        weaponId = weaponSnapshot.WeaponId;
        magazineAmmo = weaponSnapshot.CurrentMagazineAmmo;
        magazineCapacity = weaponSnapshot.MagazineCapacity;
        reserveAmmo = weaponSnapshot.ReserveAmmo;
        maxReserveAmmo = weaponSnapshot.MaxReserveAmmo;
    }

    /// <summary>씬에서 관찰한 생존 및 부상 상태를 반영합니다.</summary>
    public void SetSceneState(
        int hp,
        int hpMaximum,
        float gauge,
        float gaugeMaximum,
        CharacterInjuryState state,
        bool down,
        bool combatOut,
        string currentWeaponId,
        int currentMagazineAmmo,
        int currentMagazineCapacity,
        int currentReserveAmmo,
        int currentMaxReserveAmmo,
        bool playerSquadMember)
    {
        maxHp = Mathf.Max(1, hpMaximum);
        currentHp = Mathf.Clamp(hp, 0, maxHp);
        maxInjuryGauge = Mathf.Max(1.0f, gaugeMaximum);
        injuryGauge = Mathf.Clamp(gauge, 0.0f, maxInjuryGauge);
        injuryState = state;
        isDown = down;
        isCombatOut = combatOut;
        weaponId = currentWeaponId?.Trim() ?? string.Empty;
        magazineCapacity = Mathf.Max(0, currentMagazineCapacity);
        magazineAmmo = Mathf.Clamp(currentMagazineAmmo, 0, magazineCapacity);
        maxReserveAmmo = Mathf.Max(0, currentMaxReserveAmmo);
        reserveAmmo = Mathf.Clamp(currentReserveAmmo, 0, maxReserveAmmo);
        weaponSnapshot ??= new WeaponSnapshotData();
        weaponSnapshot.SetAmmo(magazineAmmo, magazineCapacity, reserveAmmo, maxReserveAmmo);
        isPlayerSquadMember = playerSquadMember;
    }

    /// <summary>이 멤버의 적 처치 수를 1 증가시킵니다.</summary>
    public void RecordKill()
    {
        killCount++;
    }

    /// <summary>철수 시점에 다운 상태인 멤버를 필드 이탈 상태로 확정합니다.</summary>
    public void ConfirmCombatOutIfDown()
    {
        if (!isDown)
        {
            return;
        }

        isDown = false;
        isCombatOut = true;
        currentHp = 0;
    }

    /// <summary>멤버 런타임 상태 전체를 깊은 복사하여 반환합니다.</summary>
    public FieldMemberRuntimeData Clone()
    {
        FieldMemberRuntimeData clone = new FieldMemberRuntimeData(entryData)
        {
            currentHp = CurrentHp,
            maxHp = MaxHp,
            injuryGauge = InjuryGauge,
            maxInjuryGauge = MaxInjuryGauge,
            injuryState = InjuryState,
            isDown = IsDown,
            isCombatOut = IsCombatOut,
            weaponId = WeaponId,
            magazineAmmo = MagazineAmmo,
            magazineCapacity = MagazineCapacity,
            reserveAmmo = ReserveAmmo,
            maxReserveAmmo = MaxReserveAmmo,
            weaponSnapshot = weaponSnapshot?.Clone() ?? new WeaponSnapshotData(),
            isPlayerSquadMember = IsPlayerSquadMember,
            killCount = KillCount
        };
        return clone;
    }
}

/// <summary>필드 씬이 소유하며 진행 중 계속 변경하는 전체 런타임 데이터입니다.</summary>
[Serializable]
public sealed class FieldRuntimeData
{
    [SerializeField] private FieldPhase phase;
    [SerializeField] private string fieldId = string.Empty;
    [SerializeField] private string stageId = string.Empty;
    [Min(0.0f)][SerializeField] private float elapsedSeconds;
    [SerializeField] private bool missionCompleted;
    [Min(0)][SerializeField] private int totalEnemyCount;
    [Min(0)][SerializeField] private int aliveEnemyCount;
    [Min(0)][SerializeField] private int totalKillCount;
    [SerializeField] private List<FieldMemberRuntimeData> members = new();
    [SerializeField] private List<FieldResourceAmountData> acquiredResources = new();

    /// <summary>현재 필드 생명주기 단계입니다.</summary>
    public FieldPhase Phase => phase;

    /// <summary>출격 한 회를 구분하는 고유 ID입니다.</summary>
    public string FieldId => fieldId ?? string.Empty;

    /// <summary>현재 필드의 스테이지 ID입니다.</summary>
    public string StageId => stageId ?? string.Empty;

    /// <summary>필드 진행 상태로 누적한 경과 시간입니다.</summary>
    public float ElapsedSeconds => Mathf.Max(0.0f, elapsedSeconds);

    /// <summary>현재 임무 목표를 달성했는지 여부입니다.</summary>
    public bool MissionCompleted => missionCompleted;

    /// <summary>필드 시작 시점에 집계한 전체 적 수입니다.</summary>
    public int TotalEnemyCount => Mathf.Max(0, totalEnemyCount);

    /// <summary>현재 살아 있는 것으로 집계된 적 수입니다.</summary>
    public int AliveEnemyCount => Mathf.Max(0, aliveEnemyCount);

    /// <summary>스쿼드 전체의 적 처치 수입니다.</summary>
    public int TotalKillCount => Mathf.Max(0, totalKillCount);

    /// <summary>스쿼드원별 현재 런타임 상태입니다.</summary>
    public IReadOnlyList<FieldMemberRuntimeData> Members => members;

    /// <summary>이번 필드에서 획득한 자원 수량입니다.</summary>
    public IReadOnlyList<FieldResourceAmountData> AcquiredResources => acquiredResources;

    /// <summary>입장 데이터와 시작 적 수로 런타임 상태를 초기화합니다.</summary>
    public void Initialize(FieldEntryData entryData, int enemyCount)
    {
        phase = FieldPhase.Ready;
        fieldId = entryData?.FieldId ?? string.Empty;
        stageId = entryData?.StageId ?? string.Empty;
        elapsedSeconds = 0.0f;
        missionCompleted = false;
        totalEnemyCount = Mathf.Max(0, enemyCount);
        aliveEnemyCount = totalEnemyCount;
        totalKillCount = 0;
        members.Clear();
        acquiredResources.Clear();

        if (entryData != null)
        {
            for (int i = 0; i < entryData.Members.Count; i++)
            {
                members.Add(new FieldMemberRuntimeData(entryData.Members[i]));
            }
        }
    }

    /// <summary>준비된 필드를 진행 상태로 전환합니다.</summary>
    public void StartField()
    {
        if (phase == FieldPhase.Ready)
        {
            phase = FieldPhase.Running;
        }
    }

    /// <summary>진행 중인 필드의 값 변경을 멈추고 결과 확정 단계로 전환합니다.</summary>
    public bool BeginFinalization()
    {
        if (phase == FieldPhase.Uninitialized
            || phase == FieldPhase.Finalizing
            || phase == FieldPhase.Completed
            || phase == FieldPhase.GameOver)
        {
            return false;
        }

        phase = FieldPhase.Finalizing;
        return true;
    }

    /// <summary>결과 스냅샷 생성이 끝난 필드를 완료 상태로 전환합니다.</summary>
    public void CompleteFinalization()
    {
        if (phase == FieldPhase.Finalizing)
        {
            phase = FieldPhase.Completed;
        }
    }

    /// <summary>스쿼드 전멸 시 정산 단계를 거치지 않고 게임오버 대기 상태로 전환합니다.</summary>
    public bool EnterGameOver()
    {
        if (phase != FieldPhase.Ready && phase != FieldPhase.Running)
        {
            return false;
        }

        phase = FieldPhase.GameOver;
        return true;
    }

    /// <summary>필드 진행 중일 때만 경과 시간을 누적합니다.</summary>
    public void AddElapsedTime(float deltaSeconds)
    {
        if (phase == FieldPhase.Running && deltaSeconds > 0.0f)
        {
            elapsedSeconds += deltaSeconds;
        }
    }

    /// <summary>임무 달성 여부를 현재 런타임 상태에 기록합니다.</summary>
    public void SetMissionCompleted(bool value)
    {
        if (!CanAcceptRuntimeChanges())
        {
            return;
        }

        missionCompleted = value;
    }

    /// <summary>전체 적 처치 수를 증가시키고 생존 적 수를 감소시킵니다.</summary>
    public void RecordEnemyKill()
    {
        if (!CanAcceptRuntimeChanges())
        {
            return;
        }

        totalKillCount++;
        aliveEnemyCount = Mathf.Max(0, aliveEnemyCount - 1);
    }

    /// <summary>필드 시작 후 새로 활성화된 적을 전체 및 생존 적 수에 추가합니다.</summary>
    public void RecordEnemySpawned()
    {
        if (!CanAcceptRuntimeChanges())
        {
            return;
        }

        totalEnemyCount++;
        aliveEnemyCount++;
    }

    /// <summary>철수 시점에 다운 상태로 남은 모든 스쿼드원을 필드 이탈로 확정합니다.</summary>
    public void ConfirmDownMembersAsCombatOut()
    {
        if (!CanAcceptRuntimeChanges())
        {
            return;
        }

        for (int i = 0; i < members.Count; i++)
        {
            members[i]?.ConfirmCombatOutIfDown();
        }
    }

    /// <summary>런타임 ID 또는 캐릭터 ID가 일치하는 멤버의 처치 수를 증가시킵니다.</summary>
    public void RecordMemberKill(string runtimeId, PlayerbleCharacterId characterId)
    {
        if (!CanAcceptRuntimeChanges())
        {
            return;
        }

        FieldMemberRuntimeData member = FindMember(runtimeId, characterId);
        member?.RecordKill();
    }

    /// <summary>런타임 ID 또는 캐릭터 ID가 일치하는 멤버의 현재 씬 상태를 갱신합니다.</summary>
    public void UpdateMemberState(
        string runtimeId,
        PlayerbleCharacterId characterId,
        int currentHp,
        int maxHp,
        float injuryGauge,
        float maxInjuryGauge,
        CharacterInjuryState injuryState,
        bool isDown,
        bool isCombatOut,
        string weaponId,
        int magazineAmmo,
        int magazineCapacity,
        int reserveAmmo,
        int maxReserveAmmo,
        bool isPlayerSquadMember)
    {
        if (!CanAcceptRuntimeChanges())
        {
            return;
        }

        FieldMemberRuntimeData member = FindMember(runtimeId, characterId);
        member?.SetSceneState(
            currentHp,
            maxHp,
            injuryGauge,
            maxInjuryGauge,
            injuryState,
            isDown,
            isCombatOut,
            weaponId,
            magazineAmmo,
            magazineCapacity,
            reserveAmmo,
            maxReserveAmmo,
            isPlayerSquadMember);
    }

    /// <summary>동일한 공용 캐릭터 스냅샷 구조로 멤버의 현재 상태를 갱신합니다.</summary>
    public void UpdateMemberSnapshot(CharacterSnapshotData snapshot)
    {
        if (!CanAcceptRuntimeChanges() || snapshot == null)
        {
            return;
        }

        FieldMemberRuntimeData member = FindMember(snapshot.RuntimeId, snapshot.CharacterId);
        member?.SetSceneSnapshot(snapshot);
    }

    /// <summary>이번 필드에서 획득한 자원 수량을 종류별로 누적합니다.</summary>
    public void RecordResource(string resourceId, int amount)
    {
        string id = ResourceIds.Normalize(resourceId);
        if (!CanAcceptRuntimeChanges()
            || string.IsNullOrEmpty(id)
            || amount <= 0)
        {
            return;
        }

        FieldResourceAmountData existing = acquiredResources.Find(
            entry => string.Equals(
                entry.ResourceId,
                id,
                StringComparison.Ordinal));
        if (existing != null)
        {
            existing.Add(amount);
            return;
        }

        acquiredResources.Add(new FieldResourceAmountData(id, amount));
    }

    /// <summary>전체 필드 런타임 상태를 깊은 복사하여 반환합니다.</summary>
    public FieldRuntimeData Clone()
    {
        FieldRuntimeData clone = new FieldRuntimeData
        {
            phase = Phase,
            fieldId = FieldId,
            stageId = StageId,
            elapsedSeconds = ElapsedSeconds,
            missionCompleted = MissionCompleted,
            totalEnemyCount = TotalEnemyCount,
            aliveEnemyCount = AliveEnemyCount,
            totalKillCount = TotalKillCount
        };

        for (int i = 0; i < members.Count; i++)
        {
            if (members[i] != null)
            {
                clone.members.Add(members[i].Clone());
            }
        }

        for (int i = 0; i < acquiredResources.Count; i++)
        {
            if (acquiredResources[i] != null)
            {
                clone.acquiredResources.Add(acquiredResources[i].Clone());
            }
        }

        return clone;
    }

    /// <summary>현재 단계에서 진행 중 데이터 변경을 허용하는지 확인합니다.</summary>
    private bool CanAcceptRuntimeChanges()
    {
        return phase == FieldPhase.Ready || phase == FieldPhase.Running;
    }

    /// <summary>런타임 ID를 우선 사용하고 캐릭터 ID를 보조로 사용해 런타임 멤버를 찾습니다.</summary>
    private FieldMemberRuntimeData FindMember(string runtimeId, PlayerbleCharacterId characterId)
    {
        if (!string.IsNullOrWhiteSpace(runtimeId))
        {
            FieldMemberRuntimeData byRuntimeId = members.Find(member => member != null && member.RuntimeId == runtimeId);
            if (byRuntimeId != null)
            {
                return byRuntimeId;
            }
        }

        return characterId != PlayerbleCharacterId.Unknown
            ? members.Find(member => member != null && member.CharacterId == characterId)
            : null;
    }
}

/// <summary>귀환 정산에 저장하는 스쿼드원 한 명의 최종 결과입니다.</summary>
[Serializable]
public sealed class FieldMemberResultData
{
    [SerializeField] private CharacterSnapshotData snapshot;
    [Min(0)][SerializeField] private int temporaryHpPenalty;

    /// <summary>GameDataManager에 변환 없이 전달할 공용 캐릭터·총기 최종 스냅샷입니다.</summary>
    public CharacterSnapshotData Snapshot => snapshot?.Clone();

    public int TemporaryHpPenalty => Mathf.Max(0, temporaryHpPenalty);

    /// <summary>멤버 런타임 상태에서 공용 캐릭터·총기 최종 스냅샷만 복제해 결과로 고정합니다.</summary>
    public FieldMemberResultData(FieldMemberRuntimeData runtimeData)
    {
        snapshot = runtimeData?.Snapshot;
        temporaryHpPenalty = runtimeData?.TemporaryHpPenalty ?? 0;
    }

    /// <summary>이미 생성된 공용 캐릭터·총기 스냅샷을 멤버 최종 결과로 고정합니다.</summary>
    public FieldMemberResultData(CharacterSnapshotData snapshot, int temporaryHpPenalty = 0)
    {
        this.snapshot = snapshot?.Clone();
        this.temporaryHpPenalty = Mathf.Max(0, temporaryHpPenalty);
    }

    /// <summary>멤버 최종 결과를 깊은 복사하여 반환합니다.</summary>
    public FieldMemberResultData Clone()
    {
        return new FieldMemberResultData(snapshot, TemporaryHpPenalty);
    }
}

/// <summary>필드 종료 후 GameDataManager와 저장 시스템에 전달할 최종 결과 스냅샷입니다.</summary>
[Serializable]
public sealed class FieldResultData
{
    [FormerlySerializedAs("battleId")]
    [SerializeField] private string fieldId = string.Empty;
    [SerializeField] private string stageId = string.Empty;
    [SerializeField] private FieldOutcome outcome;
    [SerializeField] private FieldEndReason endReason;
    [SerializeField] private bool missionCompleted;
    [Min(0.0f)][SerializeField] private float elapsedSeconds;
    [Min(0)][SerializeField] private int totalKillCount;
    [SerializeField] private List<FieldMemberResultData> members = new();
    [SerializeField] private List<FieldResourceAmountData> acquiredResources = new();

    /// <summary>종료된 출격 한 회의 고유 ID입니다.</summary>
    public string FieldId => fieldId ?? string.Empty;

    /// <summary>종료된 필드의 스테이지 ID입니다.</summary>
    public string StageId => stageId ?? string.Empty;

    /// <summary>귀환 정산에 표시할 최종 성공·실패 결과입니다.</summary>
    public FieldOutcome Outcome => outcome;

    /// <summary>필드가 종료된 직접적인 사유입니다.</summary>
    public FieldEndReason EndReason => endReason;

    /// <summary>필드 종료 전에 임무 목표를 달성했는지 여부입니다.</summary>
    public bool MissionCompleted => missionCompleted;

    /// <summary>필드 종료까지 누적된 경과 시간입니다.</summary>
    public float ElapsedSeconds => Mathf.Max(0.0f, elapsedSeconds);

    /// <summary>필드 전체에서 확정된 적 처치 수입니다.</summary>
    public int TotalKillCount => Mathf.Max(0, totalKillCount);

    /// <summary>스쿼드원별 최종 결과입니다.</summary>
    public IReadOnlyList<FieldMemberResultData> Members => members;

    /// <summary>필드에서 최종 획득한 자원 수량입니다.</summary>
    public IReadOnlyList<FieldResourceAmountData> AcquiredResources => acquiredResources;

    /// <summary>현재 런타임 상태와 종료 판정을 복제해 최종 결과를 생성합니다.</summary>
    public FieldResultData(FieldRuntimeData runtimeData, FieldOutcome outcome, FieldEndReason endReason)
    {
        fieldId = runtimeData?.FieldId ?? string.Empty;
        stageId = runtimeData?.StageId ?? string.Empty;
        this.outcome = outcome;
        this.endReason = endReason;
        missionCompleted = runtimeData?.MissionCompleted ?? false;
        elapsedSeconds = runtimeData?.ElapsedSeconds ?? 0.0f;
        totalKillCount = runtimeData?.TotalKillCount ?? 0;

        if (runtimeData == null)
        {
            return;
        }

        for (int i = 0; i < runtimeData.Members.Count; i++)
        {
            members.Add(new FieldMemberResultData(runtimeData.Members[i]));
        }

        for (int i = 0; i < runtimeData.AcquiredResources.Count; i++)
        {
            acquiredResources.Add(runtimeData.AcquiredResources[i].Clone());
        }
    }

    /// <summary>평탄화된 영속 정산값을 모아 씬 전달 또는 저장용 결과 패킷을 생성합니다.</summary>
    public FieldResultData(
        string fieldId,
        string stageId,
        FieldOutcome outcome,
        FieldEndReason endReason,
        bool missionCompleted,
        float elapsedSeconds,
        int totalKillCount,
        IEnumerable<FieldMemberResultData> members,
        IEnumerable<FieldResourceAmountData> acquiredResources)
    {
        this.fieldId = fieldId?.Trim() ?? string.Empty;
        this.stageId = stageId?.Trim() ?? string.Empty;
        this.outcome = outcome;
        this.endReason = endReason;
        this.missionCompleted = missionCompleted;
        this.elapsedSeconds = Mathf.Max(0.0f, elapsedSeconds);
        this.totalKillCount = Mathf.Max(0, totalKillCount);

        if (members != null)
        {
            foreach (FieldMemberResultData member in members)
            {
                if (member != null)
                {
                    this.members.Add(member.Clone());
                }
            }
        }

        if (acquiredResources != null)
        {
            foreach (FieldResourceAmountData resource in acquiredResources)
            {
                if (resource != null)
                {
                    this.acquiredResources.Add(resource.Clone());
                }
            }
        }
    }

    /// <summary>최종 결과 전체를 깊은 복사하여 반환합니다.</summary>
    public FieldResultData Clone()
    {
        FieldResultData clone = new FieldResultData
        {
            fieldId = FieldId,
            stageId = StageId,
            outcome = Outcome,
            endReason = EndReason,
            missionCompleted = MissionCompleted,
            elapsedSeconds = ElapsedSeconds,
            totalKillCount = TotalKillCount
        };

        for (int i = 0; i < members.Count; i++)
        {
            if (members[i] != null)
            {
                clone.members.Add(members[i].Clone());
            }
        }

        for (int i = 0; i < acquiredResources.Count; i++)
        {
            if (acquiredResources[i] != null)
            {
                clone.acquiredResources.Add(acquiredResources[i].Clone());
            }
        }

        return clone;
    }

    private FieldResultData()
    {
    }
}
