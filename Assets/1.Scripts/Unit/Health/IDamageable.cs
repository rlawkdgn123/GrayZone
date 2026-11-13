using UnityEngine;

/// <summary>
/// 진영을 가지고 피해를 받을 수 있는 대상입니다.
/// </summary>
/// <remarks>
/// 공격 측(<c>Gun</c>, <c>EnemyAttack</c> 등)이 구체 타입
/// (<c>PlayerHealth</c>/<c>EnemyHealth</c>)을 몰라도 진영 판정과 피해 적용을
/// 한 경로로 처리할 수 있게 하는 공통 계약입니다. <see cref="HealthSystemBase"/>가 구현합니다.
/// </remarks>
public interface IDamageable
{
    /// <summary>이 대상의 진영입니다.</summary>
    Faction Faction { get; }

    /// <summary>이 대상이 사망 상태인지 여부입니다.</summary>
    bool IsDead { get; }

    /// <summary>
    /// 피해 정보를 받아 적용합니다. HP가 실제로 변경된 경우에만 true를 반환합니다.
    /// </summary>
    /// <param name="info">피해량과 공격 방식, 세부 종류, 공격자를 담은 정보입니다.</param>
    /// <remarks>공격하는 쪽은 이 경로를 씁니다. 아래 피해량만 받는 오버로드는 공격 방식이 <see cref="DamageCategory.Unknown"/>으로 기록됩니다.</remarks>
    bool TakeDamage(DamageInfo info);

    /// <summary>
    /// 피해를 적용합니다. HP가 실제로 변경된 경우에만 true를 반환합니다. 공격 방식은 <see cref="DamageCategory.Unknown"/>으로 기록됩니다.
    /// </summary>
    /// <param name="amount">적용할 피해량입니다.</param>
    /// <param name="attacker">
    /// 이 피해를 입힌 대상입니다. 모르면 null을 넘깁니다.
    /// 피해를 받은 쪽이 "누가 때렸는지"를 알아야 반격할 수 있는데, 그 정보는 피해가 발생하는 순간에만 있습니다.
    /// 결과 알림 이벤트까지 함께 실어 보내지 않으면 중간에 사라집니다.
    /// </param>
    bool TakeDamage(int amount, GameObject attacker = null);

    /// <summary>
    /// 소수 피해를 적용합니다. HP가 실제로 변경된 경우에만 true를 반환합니다. 공격 방식은 <see cref="DamageCategory.Unknown"/>으로 기록됩니다.
    /// </summary>
    /// <remarks>
    /// HP는 내부적으로 실수로 계산하고 표시만 정수로 합니다. 총기 구간 피해처럼 2.5 같은 값이 그대로 누적됩니다.
    /// </remarks>
    bool TakeDamage(float amount, GameObject attacker = null);
}
