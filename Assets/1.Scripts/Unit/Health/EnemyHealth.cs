using System;
using UnityEngine;
using VInspector;

/// <summary>튜토리얼 과녁 모드에서 적을 죽일 수 있는 공격입니다. 그 밖의 공격은 피해는 주되 HP 1까지만 깎습니다.</summary>
/// <remarks>값은 튜토리얼 시퀀스에 저장되므로 기존 항목의 번호를 바꾸지 말고 끝에 추가하세요.</remarks>
public enum TutorialTargetKillRule
{
    /// <summary>총기(원거리 사격)로만 죽습니다.</summary>
    GunOnly = 0,

    /// <summary>스킬(청솔 특수탄과 그 화상 등)로만 죽습니다.</summary>
    SkillOnly = 1,

    /// <summary>투척물(수류탄, 화염병과 그 화상 등)로만 죽습니다.</summary>
    ThrowableOnly = 2,

    /// <summary>어떤 공격으로도 죽지 않습니다.</summary>
    Never = 3,
}

/// <summary>
/// Enemy 전용 체력 컴포넌트입니다. 피해 처리에 더해 경직력 누적과 경직 발동을 소유합니다.
/// </summary>
/// <remarks>
/// HP 관련 동작은 <see cref="HealthSystemBase"/>의 것을 그대로 사용합니다.
/// 여기서 더하는 것은 <see cref="IStaggerable"/> 계약, 즉 경직력 누적 · 한계치 판정 · 면역 윈도우입니다.
/// 경직을 <b>발동</b>시키는 것까지가 이 컴포넌트의 책임이고, 발동 후 얼마나 행동을 잠글지와
/// 어떤 연출을 낼지는 <see cref="OnStaggered"/>를 받는 쪽(EnemyController)이 정합니다.
/// 설계 근거: 공용 `적 시스템` v0.2 §5.7(피격·경직), §6(교전 이탈 시 누적 0).
/// </remarks>
public class EnemyHealth : HealthSystemBase, IStaggerable
{
    [Foldout("Stagger Options")]
    [Tooltip("경직력 누적치가 이 값에 닿으면 경직이 발동합니다. 낮을수록 쉽게 경직됩니다. 저지력이 큰 무기는 한 발로 넘길 수 있습니다.")]
    [Clamp(Min = 0.01f)]
    [SerializeField] private float m_staggerThreshold = 3.0f;

    [Tooltip("경직력 누적치가 초당 줄어드는 양입니다. 사격이 끊기면 이 속도로 빠져 결국 경직되지 않습니다. 소음 인지 게이지와 같은 방식입니다.")]
    [Clamp(Min = 0.0f)]
    [SerializeField] private float m_staggerDecayPerSecond = 1.0f;

    [Tooltip("경직이 끝난 뒤 다시 경직될 수 있기까지의 최소 간격(초)입니다. 연속 피격으로 경직이 무한히 이어지는 것을 막습니다.")]
    [Clamp(Min = 0.0f)]
    [SerializeField] private float m_staggerImmunityDuration = 0.2f;

    [Foldout("Debug")]
    [Tooltip("경직력 누적 게이지를 Scene 뷰에 막대로 표시합니다. 게이지가 0보다 클 때와 경직 중일 때만 그립니다. 소음 인지 게이지 막대보다 아래에 그려집니다.")]
    [SerializeField] private bool m_debugDrawStaggerGauge = false;

    /// <summary>
    /// 이 컴포넌트를 쓰는 유닛은 진영이 정해져 있으므로 레이어 추론을 쓰지 않습니다.
    /// </summary>
    /// <remarks>
    /// 히트박스를 별도 레이어로 분리하면서 레이어와 진영의 결합을 끊기 위한 것입니다.
    /// Inspector에서 진영을 명시하면 그 값이 우선합니다.
    /// </remarks>
    protected override Faction DefaultFaction => Faction.Enemy;

    /// <summary>켜면 <see cref="TargetKillRule"/>에 맞는 공격으로만 죽고, 그 밖의 공격으로는 HP 1까지만 깎입니다.</summary>
    /// <remarks>튜토리얼 과녁 모드가 켭니다.</remarks>
    public bool UseTargetKillRule { get; private set; }

    /// <summary>과녁 모드에서 이 개체를 죽일 수 있는 공격입니다.</summary>
    public TutorialTargetKillRule TargetKillRule { get; private set; }

    /// <summary>과녁 처치 조건을 켜거나 끕니다.</summary>
    /// <param name="enabled">켜면 처치 조건에 맞는 공격으로만 죽습니다.</param>
    /// <param name="rule">죽을 수 있는 공격입니다.</param>
    public void SetTargetKillRule(bool enabled, TutorialTargetKillRule rule)
    {
        UseTargetKillRule = enabled;
        TargetKillRule = rule;
    }

    /// <summary>
    /// 피해를 적용합니다. 과녁 처치 조건이 켜져 있고 이번 공격이 조건에 맞지 않으면 HP 1까지만 깎습니다.
    /// </summary>
    /// <remarks>상태이상 피해는 그 상태이상을 건 원래 공격(<see cref="DamageInfo.OriginCategory"/>)으로 판정합니다.</remarks>
    public override bool TakeDamage(DamageInfo info)
    {
        if (UseTargetKillRule && !CanKill(TargetKillRule, info))
        {
            float damage = Mathf.Min(info.Amount, m_currentHp - 1.0f);
            if (damage <= 0.0f)
            {
                return false;
            }

            info = info.WithAmount(damage);
        }

        return base.TakeDamage(info);
    }

    /// <summary>처치 조건이 이 공격으로 죽는 것을 허용하는지 여부입니다.</summary>
    private static bool CanKill(TutorialTargetKillRule rule, DamageInfo info)
    {
        return rule switch
        {
            TutorialTargetKillRule.GunOnly => info.OriginCategory == DamageCategory.Ranged,
            TutorialTargetKillRule.SkillOnly => info.OriginCategory == DamageCategory.Skill,
            TutorialTargetKillRule.ThrowableOnly => info.OriginCategory == DamageCategory.Throwable,
            _ => false,
        };
    }

    /// <summary>적 체력 컴포넌트가 활성화되어 필드 집계 대상으로 진입했을 때 발생합니다.</summary>
    public static event Action<EnemyHealth> OnEnemyEnabled;

    /// <summary>경직력 누적치가 한계치에 닿아 경직이 발동했을 때 발생합니다.</summary>
    /// <remarks>
    /// 발동 순간에만 한 번 발생합니다. 경직이 유지되는 동안에는 다시 발생하지 않습니다.
    /// 구독자는 행동을 잠그고 연출을 낸 뒤, 끝났을 때 <see cref="NotifyStaggerEnded"/>를 불러야 합니다.
    /// 그것을 부르지 않으면 이 개체는 영원히 경직 중으로 남아 다시 경직되지 않습니다.
    /// </remarks>
    public event Action OnStaggered;

    /// <summary>
    /// 현재 경직력 누적치입니다.
    /// </summary>
    /// <remarks>큰 저지력 한 발은 한 번에 한계치를 넘고, 약한 탄은 여러 발이 필요합니다.</remarks>
    private float m_staggerGauge;

    /// <summary>지금 경직 중인지 여부입니다.</summary>
    /// <remarks>
    /// 경직 중에는 누적하지 않습니다. 경직 시간 내내 맞은 탄이 그대로 쌓이면
    /// 경직이 풀리는 즉시 다시 경직되어 사실상 영구 경직이 되기 때문입니다.
    /// </remarks>
    private bool m_isStaggered;

    /// <summary>면역 윈도우가 끝나는 시각입니다. 이 시각 전에는 경직력을 누적하지 않습니다.</summary>
    private float m_staggerImmuneUntil;

    /// <summary>현재 경직력 누적 진행도입니다. 0이면 없음, 1이면 한계치입니다.</summary>
    /// <remarks>디버그 표시와 연출 강도 블렌드에 쓸 수 있는 읽기 전용 값입니다.</remarks>
    public float StaggerGauge01 => m_staggerThreshold > 0.0f
        ? Mathf.Clamp01(m_staggerGauge / m_staggerThreshold)
        : 0.0f;

    /// <summary>지금 경직 중인지 여부입니다.</summary>
    public bool IsStaggered => m_isStaggered;

    /// <summary>현재 적 인스턴스의 활성화를 필드 씬 데이터 수집기에 알립니다.</summary>
    private void OnEnable()
    {
        OnEnemyEnabled?.Invoke(this);
    }

    /// <summary>
    /// 적 밸런스 데이터에서 경직 수치를 적용합니다.
    /// </summary>
    /// <param name="balance">적용할 순수 수치 밸런스 데이터입니다.</param>
    public void ApplyBalance(EnemyBalanceSO balance)
    {
        if (balance == null)
        {
            return;
        }

        m_staggerThreshold = balance.StaggerThreshold;
        m_staggerDecayPerSecond = balance.StaggerDecayPerSecond;
        m_staggerImmunityDuration = balance.HitStunCooldown;
    }

    /// <summary>
    /// 경직력 누적치를 시간에 따라 줄입니다.
    /// </summary>
    /// <remarks>
    /// 감소를 매 프레임 돌리는 것은 소음 인지 게이지와 같은 이유입니다. 판정 주기에 묶으면 감소가 뚝뚝 끊겨
    /// "사격을 멈추면 서서히 버틴다"가 성립하지 않습니다.
    /// 경직 중에는 이미 0이므로 굳이 돌리지 않습니다.
    /// </remarks>
    private void Update()
    {
        if (m_isStaggered || m_staggerGauge <= 0.0f)
        {
            return;
        }

        m_staggerGauge = Mathf.Max(0.0f, m_staggerGauge - m_staggerDecayPerSecond * Time.deltaTime);
    }

    /// <summary>
    /// 경직력을 누적하고, 한계치에 닿았으면 경직을 발동합니다.
    /// </summary>
    /// <param name="staggerPower">이번 공격이 더할 경직력입니다.</param>
    /// <returns>이번 누적으로 경직이 발동했으면 true입니다.</returns>
    /// <remarks>
    /// 사망·경직 중·면역 윈도우에서는 누적 자체를 하지 않습니다. 누적만 해 두면 면역이 풀리는 순간
    /// 그동안 쌓인 값으로 즉시 경직되어, 면역 윈도우를 둔 의미가 사라지기 때문입니다.
    /// </remarks>
    public bool ApplyStagger(float staggerPower)
    {
        if (m_isDead || m_isStaggered || staggerPower <= 0.0f)
        {
            return false;
        }

        if (Time.time < m_staggerImmuneUntil)
        {
            return false;
        }

        m_staggerGauge += staggerPower;

        LogHealthDebug($"[Stagger] +{staggerPower:F2} -> {m_staggerGauge:F2} / {m_staggerThreshold:F2}");

        if (m_staggerGauge < m_staggerThreshold)
        {
            return false;
        }

        // 발동과 동시에 누적을 비웁니다(§5.7). 넘긴 만큼을 남기면 다음 경직이 앞당겨져
        // 저지력이 큰 무기가 연속 경직을 만듭니다.
        m_staggerGauge = 0.0f;
        m_isStaggered = true;

        LogHealthDebug($"[Stagger] 발동 (구독자 {(OnStaggered != null ? OnStaggered.GetInvocationList().Length : 0)}명)");

        OnStaggered?.Invoke();
        return true;
    }

    /// <summary>
    /// 경직이 끝났음을 알리고 면역 윈도우를 시작합니다.
    /// </summary>
    /// <remarks>
    /// 경직 지속 시간은 이 컴포넌트가 아니라 연출을 소유한 쪽이 압니다. 그래서 끝을 밖에서 알려 줍니다.
    /// 면역을 발동 시점이 아니라 <b>종료 시점</b>부터 재는 이유는, 경직이 길면 발동 기준 면역이
    /// 경직 중에 이미 끝나 버려 풀리자마자 다시 걸리기 때문입니다.
    /// </remarks>
    public void NotifyStaggerEnded()
    {
        if (!m_isStaggered)
        {
            return;
        }

        m_isStaggered = false;
        m_staggerImmuneUntil = Time.time + m_staggerImmunityDuration;
    }

    /// <summary>
    /// 경직력 누적치와 경직 상태를 모두 초기화합니다.
    /// </summary>
    /// <remarks>
    /// 교전에서 이탈하면 누적은 0으로 돌아갑니다(§6). 체력과 달리 경직은 교전 안에서만 의미가 있어,
    /// 한참 뒤에 다시 마주친 개체가 예전에 맞은 값 때문에 한 발에 경직되면 어긋납니다.
    /// </remarks>
    public void ResetStagger()
    {
        m_staggerGauge = 0.0f;
        m_isStaggered = false;
        m_staggerImmuneUntil = 0.0f;
    }

    /// <summary>
    /// 경직력 누적 게이지를 머리 위 막대로 표시합니다.
    /// </summary>
    /// <remarks>
    /// 표시 방식을 <see cref="EnemyTargetSensor"/>의 소음 인지 게이지와 맞췄습니다. 두 게이지는 "쌓이다
    /// 한계치에서 터진다"는 같은 구조라, 다르게 그리면 읽는 사람이 매번 규칙을 새로 익혀야 합니다.
    /// 높이만 낮춰 두 막대가 겹치지 않게 했습니다.
    ///
    /// 저지력과 감쇠는 서로 상쇄하는 값이라 수치만으로는 "몇 발째에 터지는지"를 계산으로만 알 수 있습니다.
    /// 실제로 쏘면서 차오르는 속도를 봐야 감이 잡히므로 실수치도 함께 적습니다.
    ///
    /// <see cref="OnDrawGizmos"/>인 이유는 게이지가 차는 것을 <b>쏘는 도중에</b> 봐야 하기 때문입니다.
    /// 선택 시에만 그리면 조준하는 동안 대상을 선택 상태로 유지할 수 없습니다.
    /// </remarks>
    private void OnDrawGizmos()
    {
        if (!m_debugDrawStaggerGauge || (m_staggerGauge <= 0.0f && !m_isStaggered))
        {
            return;
        }

        float fill = m_isStaggered ? 1.0f : StaggerGauge01;

        // 경직 중이면 빨강, 차오르는 중이면 파랑입니다. 소음 게이지(흰->노랑)와 색 계열을 갈라
        // 두 막대를 동시에 켜도 어느 쪽인지 헷갈리지 않게 했습니다.
        Color color = m_isStaggered ? Color.red : Color.Lerp(Color.white, new Color(0.3f, 0.6f, 1.0f), fill);

        const float BarWidth = 1.0f;
        const float BarHeight = 0.12f;
        Vector3 center = transform.position + Vector3.up * 2.05f;

        // 막대가 카메라를 향하도록 회전시킵니다. 월드 축에 고정하면 보는 각도에 따라 선으로 납작해집니다.
        Camera camera = Camera.current;
        Quaternion facing = camera != null
            ? Quaternion.LookRotation(camera.transform.forward, Vector3.up)
            : Quaternion.identity;

        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(center, facing, Vector3.one);

        Gizmos.color = new Color(0.0f, 0.0f, 0.0f, 0.5f);
        Gizmos.DrawCube(Vector3.zero, new Vector3(BarWidth, BarHeight, 0.01f));

        Gizmos.color = color;
        float filled = BarWidth * fill;
        Gizmos.DrawCube(
            new Vector3(-(BarWidth - filled) * 0.5f, 0.0f, -0.01f),
            new Vector3(filled, BarHeight, 0.01f));

        Gizmos.matrix = previous;

#if UNITY_EDITOR
        UnityEditor.Handles.color = color;
        UnityEditor.Handles.Label(
            center + Vector3.up * 0.2f,
            m_isStaggered
                ? "STAGGERED"
                : $"{m_staggerGauge:F2} / {m_staggerThreshold:F2}");
#endif
    }

#if UNITY_EDITOR
    [Foldout("Debug")]
    [Tooltip("켜면 Editor에서 피해/사망 로그를 출력합니다. Player 빌드에서는 호출 자체가 제거됩니다.")]
    [SerializeField] private bool m_debugLogHealth = false;

    protected override bool DebugLogHealthEnabled => m_debugLogHealth;
#endif
}
