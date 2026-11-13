using UnityEngine;

/// <summary>피해가 어떤 방식의 공격에서 왔는지 나타냅니다.</summary>
/// <remarks>값은 저장·비교에 쓰일 수 있으므로 기존 항목의 번호를 바꾸지 말고 끝에 추가하세요.</remarks>
public enum DamageCategory
{
    /// <summary>공격 방식을 모르는 경로입니다(구조체 없이 피해만 넘긴 호출).</summary>
    Unknown = 0,

    /// <summary>근접 공격입니다(변이체 할퀴기 등).</summary>
    Melee = 1,

    /// <summary>원거리 사격입니다(총기, 탄환).</summary>
    Ranged = 2,

    /// <summary>투척물입니다(수류탄, 화염병 등). 종류는 <see cref="DamageInfo.SourceId"/>에 아이템 ID로 담습니다.</summary>
    Throwable = 3,

    /// <summary>함정입니다. 종류는 <see cref="DamageInfo.SourceId"/>에 함정 타입 이름으로 담습니다.</summary>
    Trap = 4,

    /// <summary>상태이상의 주기 피해입니다(화상 등). 상태이상을 건 공격은 <see cref="DamageInfo.OriginCategory"/>에 담습니다.</summary>
    StatusEffect = 5,

    /// <summary>캐릭터 스킬입니다(청솔 특수탄 등).</summary>
    Skill = 6,

    /// <summary>디버그 도구가 넣은 피해입니다.</summary>
    Debug = 7,
}

/// <summary>
/// 피해 한 번의 정보입니다. 피해량과 함께 공격 방식, 세부 종류, 공격자를 담아 <see cref="IDamageable.TakeDamage(DamageInfo)"/>로 넘깁니다.
/// </summary>
/// <remarks>
/// 받는 쪽은 필요한 값만 꺼내 씁니다. 체력 컴포넌트는 마지막 피해와 사망 원인을 이 구조체로 보관하므로,
/// 디버깅이나 처치 조건(예: 투척물로만 죽는 과녁)을 피해가 들어온 뒤에 확인할 수 있습니다.
///
/// 공격자 이름은 만들 때 따로 저장합니다. 화염 지대나 투척물처럼 공격자 오브젝트가 먼저 사라져도 누가 했는지 남기기 위해서입니다.
/// </remarks>
public readonly struct DamageInfo
{
    /// <summary>적용할 피해량입니다.</summary>
    public readonly float Amount;

    /// <summary>공격 방식입니다.</summary>
    public readonly DamageCategory Category;

    /// <summary>세부 종류입니다. 투척물은 아이템 ID, 함정은 함정 타입 이름, 상태이상은 효과 이름, 스킬은 스킬 종류, 총기는 무기 이름입니다.</summary>
    public readonly string SourceId;

    /// <summary>피해를 입힌 오브젝트입니다. 모르면 null이고, 나중에 파괴되어 있을 수 있습니다.</summary>
    public readonly GameObject Attacker;

    /// <summary>피해를 입힌 쪽의 이름입니다. 만들 때 저장해 두므로 공격자가 사라져도 남습니다.</summary>
    public readonly string AttackerName;

    /// <summary>
    /// 이 피해를 일으킨 원래 공격 방식입니다. 직접 공격은 <see cref="Category"/>와 같고,
    /// 상태이상 피해는 그 상태이상을 건 공격(예: 청솔 특수탄이면 <see cref="DamageCategory.Skill"/>)입니다.
    /// </summary>
    public readonly DamageCategory OriginCategory;

    /// <summary>원래 공격의 세부 종류입니다. 직접 공격은 <see cref="SourceId"/>와 같습니다.</summary>
    public readonly string OriginSourceId;

    /// <summary>직접 공격 한 번의 정보를 만듭니다. 원래 공격은 이 공격 자신입니다.</summary>
    /// <param name="amount">피해량입니다.</param>
    /// <param name="category">공격 방식입니다.</param>
    /// <param name="sourceId">세부 종류입니다.</param>
    /// <param name="attacker">피해를 입힌 오브젝트입니다. 모르면 null입니다.</param>
    /// <param name="attackerName">공격자 이름입니다. 비우면 <paramref name="attacker"/>의 이름을 씁니다.</param>
    public DamageInfo(float amount, DamageCategory category, string sourceId, GameObject attacker, string attackerName = null)
        : this(amount, category, sourceId, attacker, attackerName, category, sourceId)
    {
    }

    private DamageInfo(
        float amount,
        DamageCategory category,
        string sourceId,
        GameObject attacker,
        string attackerName,
        DamageCategory originCategory,
        string originSourceId)
    {
        Amount = amount;
        Category = category;
        SourceId = sourceId ?? string.Empty;
        Attacker = attacker;
        AttackerName = !string.IsNullOrEmpty(attackerName)
            ? attackerName
            : attacker != null ? attacker.name : string.Empty;
        OriginCategory = originCategory;
        OriginSourceId = originSourceId ?? string.Empty;
    }

    /// <summary>공격 방식을 모르는 피해입니다. 구조체 없이 피해량만 넘기는 예전 경로가 씁니다.</summary>
    public static DamageInfo Unknown(float amount, GameObject attacker = null)
    {
        return new DamageInfo(amount, DamageCategory.Unknown, string.Empty, attacker);
    }

    /// <summary>디버그 도구가 넣는 피해입니다.</summary>
    public static DamageInfo Debug(float amount, string tool)
    {
        return new DamageInfo(amount, DamageCategory.Debug, tool, null, tool);
    }

    /// <summary>값이 채워진 정보인지 여부입니다. 기본값(아무 피해도 받지 않음)이면 false입니다.</summary>
    public bool IsValid => Amount > 0.0f || Category != DamageCategory.Unknown || !string.IsNullOrEmpty(AttackerName);

    /// <summary>피해량만 바꾼 정보를 돌려줍니다. 약점 배율이나 거리 감쇠, 반무적처럼 양만 달라질 때 씁니다.</summary>
    public DamageInfo WithAmount(float amount)
    {
        return new DamageInfo(amount, Category, SourceId, Attacker, AttackerName, OriginCategory, OriginSourceId);
    }

    /// <summary>
    /// 이 공격이 건 상태이상의 주기 피해 정보를 만듭니다. 공격자와 원래 공격은 이 공격의 것을 그대로 이어받습니다.
    /// </summary>
    /// <param name="amount">이번 주기 피해량입니다.</param>
    /// <param name="effectId">상태이상 이름입니다.</param>
    public DamageInfo ToStatusEffect(float amount, string effectId)
    {
        return new DamageInfo(amount, DamageCategory.StatusEffect, effectId, Attacker, AttackerName, OriginCategory, OriginSourceId);
    }

    /// <summary>디버그 로그용 한 줄 설명입니다. 예: "Throwable/Grenade_01 by Narin (35)".</summary>
    public override string ToString()
    {
        string attacker = string.IsNullOrEmpty(AttackerName) ? "(알 수 없음)" : AttackerName;
        string text = $"{Category}/{SourceId} by {attacker} ({Amount:0.##})";
        return OriginCategory != Category || OriginSourceId != SourceId
            ? $"{text} ← {OriginCategory}/{OriginSourceId}"
            : text;
    }
}
