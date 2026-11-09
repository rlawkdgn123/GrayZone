using UnityEngine;

/// <summary>
/// 스파이크입니다. 밟은 적을 범위 안에서 걷게 하고, 들어온 순간 잠깐 크게 늦추며 피해를 줍니다.
/// </summary>
/// <remarks>
/// 동작은 <see cref="WireTrap"/>과 같고 프리팹 값만 다릅니다. 지속 감속(이동 속도 배율)은 1로 두어 쓰지 않고,
/// 진입 순간 감속(예: 0.2배, 0.2초)과 걷기 강제, 진입 1회 피해(Once)로 맞춥니다.
/// 철조망처럼 오래 붙잡는 함정이 아니라, 잠깐 멈칫하게 하고 큰 피해를 주는 함정이기 때문입니다.
///
/// 업그레이드는 Spike 레벨을 읽어 피해 배율과 내구도를 올립니다. 철조망의 Wire 레벨은 읽지 않습니다.
/// </remarks>
public sealed class SpikeTrap : WireTrap
{
    /// <inheritdoc />
    protected override string DefaultDisplayName => "스파이크";

    /// <inheritdoc />
    protected override string DefaultDescription => "밟은 적에게 경직과 함께 큰 피해를 줍니다.";

    /// <inheritdoc />
    protected override TrapKind CostKind => TrapKind.Spike;

    /// <inheritdoc />
    protected override void ApplyLevelUpgrade(TrapUpgradeTableSO table, DefenseSceneDataManager data)
    {
        if (table == null
            || data == null
            || !table.TryGetSpike(data.GetUpgradeLevel(ScrambleUpgradeType.Spike), out TrapUpgradeTableSO.SpikeLevel level))
        {
            return;
        }

        SetUpgradeBonus(level.BonusDurability, level.DamageMultiplier);
    }
}
