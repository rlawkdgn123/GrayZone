using UnityEngine;

/// <summary>
/// 근접 무기 하나를 나타내는 컴포넌트입니다. 자기 판정 콜라이더를 소유하고 적중을 공격 모듈에 넘깁니다.
/// </summary>
/// <remarks>
/// 총기의 <see cref="Gun"/>에 대응하는 자리입니다.
/// 수치는 <see cref="MeleeBalanceSO"/>가, 지금 켜져 있는지 같은 런타임 상태는 이 컴포넌트가 소유합니다.
///
/// 이 컴포넌트는 판정 콜라이더와 같은 GameObject에 붙여야 합니다.
/// 복합 콜라이더에서 트리거 콜백이 어느 쪽으로 오는지 모호해지는 것을 피하기 위해서이며,
/// 좌우 손을 구분해 적중을 기록하려면 어느 무기가 닿았는지도 알아야 하기 때문입니다.
/// 콜라이더를 직접 찾으므로 바깥에서 지정할 필요가 없습니다.
///
/// 켜고 끄는 시점은 이 컴포넌트가 정하지 않습니다.
/// 공격 클립의 애니메이션 이벤트가 <see cref="EnemyAttack.SetHitboxActive"/>를 거쳐 지시합니다.
/// </remarks>
[RequireComponent(typeof(Collider))]
public class Melee : MonoBehaviour
{
    [Tooltip("이 근접 무기의 밸런스 수치입니다. 연타별 피해량과 약점 판정 사용 여부를 담습니다.")]
    [SerializeField] private MeleeBalanceSO m_balanceSO;

    [Tooltip("어려움 난이도에서 위 밸런스 대신 쓸 데이터입니다. 비어 있으면 어려움에서도 위 데이터를 씁니다.")]
    [SerializeField] private MeleeBalanceSO m_hardBalanceSO;

    [Tooltip("적중을 넘겨 줄 공격 모듈입니다. 비어 있으면 상위에서 찾습니다.")]
    [SerializeField] private EnemyAttack m_attack;

    /// <summary>이 무기가 담당하는 판정 콜라이더입니다.</summary>
    private Collider m_collider;

    /// <summary>이 근접 무기의 밸런스 수치입니다.</summary>
    public MeleeBalanceSO Balance => m_balanceSO;

    /// <summary>이 무기가 담당하는 판정 콜라이더입니다.</summary>
    public Collider HitCollider => m_collider;

    /// <summary>판정 콜라이더가 현재 켜져 있는지 여부입니다.</summary>
    public bool IsActive => m_collider != null && m_collider.enabled;

    private void Awake()
    {
        m_collider = GetComponent<Collider>();

        // 난이도는 판 단위로 정해지고 무기는 적과 함께 새로 만들어지므로 생성 때 한 번만 고릅니다.
        m_balanceSO = GameDifficultyBalance.Select(m_balanceSO, m_hardBalanceSO);

        if (m_attack == null)
        {
            m_attack = GetComponentInParent<EnemyAttack>();
        }

        if (m_collider != null && !m_collider.isTrigger)
        {
            // 트리거가 아니면 CharacterController인 플레이어와의 접촉을 받지 못해 조용히 동작하지 않습니다.
            Debug.LogWarning($"[{name}] 근접 판정 콜라이더가 트리거가 아닙니다. Is Trigger를 켜야 동작합니다.", this);
        }

        if (m_balanceSO == null)
        {
            Debug.LogWarning($"[{name}] 근접 무기에 밸런스 SO가 없습니다. 피해량은 공격 모듈의 기본값을 씁니다.", this);
        }
    }

    /// <summary>판정 콜라이더를 켜거나 끕니다.</summary>
    /// <param name="active">켜면 true입니다.</param>
    public void SetActive(bool active)
    {
        if (m_collider != null)
        {
            m_collider.enabled = active;
        }
    }

    /// <summary>
    /// 지정한 애니메이터 스테이트에 해당하는 피해량을 돌려줍니다.
    /// </summary>
    /// <param name="stateNameHash">현재 재생 중인 공격 스테이트의 짧은 이름 해시입니다.</param>
    /// <param name="fallbackDamage">밸런스 SO가 없을 때 사용할 피해량입니다.</param>
    /// <returns>해당 타의 피해량입니다.</returns>
    /// <remarks>
    /// 몇 타째인지를 이 무기가 직접 세지 않고 애니메이터가 재생 중인 스테이트로 판단합니다.
    /// 연타 분기를 애니메이터가 소유하고 있어, 코드가 따로 세면 둘이 어긋날 수 있기 때문입니다.
    /// </remarks>
    public int ResolveDamage(int stateNameHash, int fallbackDamage)
    {
        if (m_balanceSO == null)
        {
            return fallbackDamage;
        }

        int index = m_balanceSO.FindIndexByStateHash(stateNameHash);
        return index >= 0 ? m_balanceSO.GetDamage(index) : m_balanceSO.FallbackDamage;
    }

    /// <summary>
    /// 지정한 애니메이터 스테이트에 해당하는 경직력을 돌려줍니다.
    /// </summary>
    /// <param name="stateNameHash">현재 재생 중인 공격 스테이트의 짧은 이름 해시입니다.</param>
    /// <param name="fallbackStaggerPower">밸런스 SO가 없을 때 사용할 경직력입니다.</param>
    /// <returns>해당 공격의 경직력입니다.</returns>
    /// <remarks>
    /// 경직 누적치와 한계치는 맞는 쪽이 소유합니다. 이 무기는 더할 값만 알려 줍니다.
    /// 경직 시스템이 아직 없어 지금은 호출되지 않으며, 붙일 때 이 값을 씁니다.
    /// </remarks>
    public int ResolveStaggerPower(int stateNameHash, int fallbackStaggerPower)
    {
        if (m_balanceSO == null)
        {
            return fallbackStaggerPower;
        }

        int index = m_balanceSO.FindIndexByStateHash(stateNameHash);
        return index >= 0 ? m_balanceSO.GetStaggerPower(index) : m_balanceSO.FallbackStaggerPower;
    }

    /// <summary>
    /// 지정한 애니메이터 스테이트에 해당하는 넉백 충격량을 돌려줍니다.
    /// </summary>
    /// <param name="stateNameHash">현재 재생 중인 공격 스테이트의 짧은 이름 해시입니다.</param>
    /// <param name="fallbackKnockbackImpulse">밸런스 SO가 없을 때 사용할 충격량(N·s)입니다.</param>
    /// <returns>해당 공격의 넉백 충격량입니다.</returns>
    /// <remarks>
    /// 얼마나 밀릴지는 맞는 쪽이 정합니다. 이 무기는 실어 보낼 세기만 알려 줍니다.
    /// 기획 문서에 넉백 개념이 없어 기본값은 0이며, 값이 정해지면 이 경로가 그대로 전달합니다.
    /// </remarks>
    public float ResolveKnockbackImpulse(int stateNameHash, float fallbackKnockbackImpulse)
    {
        if (m_balanceSO == null)
        {
            return fallbackKnockbackImpulse;
        }

        int index = m_balanceSO.FindIndexByStateHash(stateNameHash);
        return index >= 0
            ? m_balanceSO.GetKnockbackImpulse(index)
            : m_balanceSO.FallbackKnockbackImpulse;
    }

    /// <summary>
    /// 판정 콜라이더에 무언가 들어온 순간 피해를 넘깁니다.
    /// </summary>
    /// <remarks>
    /// 진입 순간에만 판정합니다. 겹쳐 있는 동안 계속 보면 맞은 시점이 흐려집니다.
    /// 실측에서 유지 판정이 진입보다 더 많이 성립했고, 그 적중들은 손이 닿는 순간이 아니라
    /// 이미 몸 안에 있던 손이 뒤늦게 세어진 것이었습니다.
    ///
    /// 판정이 켜지기 전부터 겹쳐 있었다면 진입 이벤트가 오지 않지만,
    /// 그때는 애초에 휘두르는 궤적이 스친 것이 아니므로 맞지 않는 편이 맞습니다.
    /// </remarks>
    private void OnTriggerEnter(Collider other)
    {
        if (m_attack == null || other == null)
        {
            return;
        }

        m_attack.TryApplyDamageTo(this, other);
    }

    /// <summary>
    /// 방어 목표(성문 등)에 한해 판정 콜라이더가 겹쳐 있는 동안에도 피해를 넘깁니다.
    /// </summary>
    /// <remarks>
    /// 방어 목표는 크고 움직이지 않아, 붙어서 때리는 동안 손이 계속 그 안에 있습니다. 그러면 판정이 켜지기
    /// 전부터 겹쳐 있어 진입 이벤트가 오지 않을 때가 많습니다(실측 10타 중 4타만 적중).
    /// 스쿼드원은 위 진입 규칙을 그대로 따르도록 여기서 제외합니다.
    /// 한 공격에 여러 번 맞지 않는 것은 <see cref="EnemyAttack.TryApplyDamageTo"/>의 적중 기록이 보장합니다.
    /// </remarks>
    private void OnTriggerStay(Collider other)
    {
        if (m_attack == null || other == null || other.GetComponentInParent<DefenseEventHealth>() == null)
        {
            return;
        }

        m_attack.TryApplyDamageTo(this, other);
    }
}
