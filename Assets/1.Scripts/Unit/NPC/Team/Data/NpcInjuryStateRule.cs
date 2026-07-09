using UnityEngine;

// 부상게이지 → 부상상태 매핑 (임시 규칙).
// 게이지가 단일 진실원천이며 상태는 여기서 파생된다. Dead 미사용(게임에 사망 없음).
// 임계: 100=Healthy / 61~99 경상 / 31~60 중상 / 0~30 위독.
public static class NpcInjuryStateRule
{
    public static NPCInjuryState FromGauge(float gauge, float maxGauge)
    {
        if (maxGauge <= 0f)
            return NPCInjuryState.NearDeath;

        if (gauge >= maxGauge)
            return NPCInjuryState.Healthy;

        float percent = gauge / maxGauge * 100f;

        if (percent >= 61f)
            return NPCInjuryState.LightInjury;

        if (percent >= 31f)
            return NPCInjuryState.HeavyInjury;

        return NPCInjuryState.NearDeath;
    }
}
