using System.Collections.Generic;
using UnityEngine;
using VInspector;

/// <summary>
/// 변이체의 근접 공격 시작 조건과 공간 판정을 처리하는 Module입니다.
/// </summary>
/// <remarks>
/// 이 모듈은 "때릴 수 있는가"와 "이 후보를 때려도 되는가"만 답합니다.
/// 언제 때릴지는 <see cref="AttackState"/>가, 무엇이 닿았는지는 손에 달린 판정 콜라이더가 정합니다.
/// 핵심 규칙은 현재 대상의 식별값이 피격 대상을 고정하지 않는다는 것입니다.
/// 대상은 공격 방향을 정하는 데만 쓰이고, 실제로 맞는 것은 판정 중에 콜라이더에 닿은 캐릭터입니다.
/// 그래서 대상이 피하면 빗나가고, 옆에 있던 다른 캐릭터가 대신 맞을 수 있습니다.
/// 설계 근거: 공용 `적 시스템` v0.2 §5.9.1(공격 구간), §5.9.2(판정 범위와 피격 대상).
/// </remarks>
public class EnemyAttack : MonoBehaviour
{
    [Header("Start Condition")]
    [Tooltip("공격을 시작할 수 있는 대상과의 최대 거리입니다. 실제 판정 범위와는 별개입니다.")]
    [SerializeField] private float m_attackRange = 1.8f;

    [Tooltip("공격을 시작할 수 있는 정면 기준 허용 방향각입니다.")]
    [SerializeField] private float m_attackStartAngle = 70f;

    [Header("Impact")]
    [Tooltip("공격 판정이 적중했을 때 적용할 피해량입니다.")]
    [SerializeField] private int m_attackDamage = 1;

    [Tooltip("한 번의 공격으로 피해를 줄 수 있는 최대 캐릭터 수입니다.")]
    [SerializeField] private int m_attackMaxTargets = 1;

    [Tooltip("켜면 한 번 적중한 순간 그 공격을 끝냅니다. 양손 중 어느 쪽이 먼저 닿아도 피해는 정확히 한 번입니다. " +
             "끄면 판정 콜라이더마다 따로 판정해, 같은 캐릭터가 양손에 스치면 두 번 맞습니다.")]
    [SerializeField] private bool m_singleHitPerSwing = true;

    [Tooltip("공격 판정을 가로막는 고정 환경 장애물 레이어입니다. 다른 변이체는 포함하지 않습니다.")]
    [SerializeField] private LayerMask m_obstacleLayer;

    [Header("Melee")]
    [Tooltip("이 변이체가 쓰는 근접 무기입니다. 보통 좌우 손 두 개이며, 각자 자기 판정 콜라이더를 소유합니다.")]
    [SerializeField] private Melee[] m_melees;

    [Tooltip("공격 스테이트를 판별할 애니메이터입니다. 비어 있으면 상위에서 찾습니다.")]
    [SerializeField] private Animator m_animator;

    [Foldout("Debug")]
    [Tooltip("이 개체를 선택했을 때 공격 시작 거리를 Scene 뷰에 원으로 표시합니다. 실제 판정 범위가 아니라 시작 조건입니다.")]
    [SerializeField] private bool m_debugDrawAttackRange = true;

    /// <summary>판정 콜라이더가 현재 켜져 있는지 여부입니다.</summary>
    public bool IsHitboxActive { get; private set; }

    /// <summary>공격 주체(이 변이체)의 진영입니다.</summary>
    private Faction m_ownerFaction = Faction.Enemy;

    /// <summary>
    /// 이번 공격에서 이미 성립한 적중 기록입니다. 어느 판정 콜라이더가 누구를 때렸는지를 함께 기억합니다.
    /// </summary>
    /// <remarks>
    /// 캐릭터만 기억하면 손별 독립 판정을 표현할 수 없어 출처까지 같이 담습니다.
    /// 진입 시점에만 호출하더라도 좌우 콜라이더가 각각 발화하거나 대상이 빠져나갔다 다시 들어오면
    /// 같은 공격에서 여러 번 들어오기 때문에 이 기록이 필요합니다.
    /// </remarks>
    private readonly List<SwingHit> m_swingHits = new List<SwingHit>();

    /// <summary>실제로 사용할 판정 차단 레이어입니다. 지정이 없으면 기본값으로 채웁니다.</summary>
    private int m_resolvedObstacleMask;
    private EnemyController m_owner;
    private DefenseEventHealth m_cachedObjective;
    private readonly List<Collider> m_objectiveColliders = new List<Collider>();

    /// <summary>목표물 접근 이동의 정지 반경 상한을 정할 때 쓰는 공격 시작 거리입니다.</summary>
    public float AttackStartRange => m_attackRange;

    /// <summary>한 번의 공격에서 성립한 적중 하나입니다.</summary>
    private readonly struct SwingHit
    {
        /// <summary>피해를 발생시킨 근접 무기입니다. 좌우 손을 구분하는 데 씁니다.</summary>
        public readonly Melee Source;

        /// <summary>피해를 입은 캐릭터입니다.</summary>
        public readonly Component Member;

        /// <summary>피해를 발생시킨 무기와 피해를 입은 캐릭터를 묶습니다.</summary>
        /// <param name="source">피해를 발생시킨 근접 무기입니다.</param>
        /// <param name="member">피해를 입은 캐릭터입니다.</param>
        public SwingHit(Melee source, Component member)
        {
            Source = source;
            Member = member;
        }
    }

    /// <summary>
    /// 이번 공격에서 더 이상 판정이 필요 없는지 여부입니다.
    /// </summary>
    /// <remarks>
    /// 스윙당 1회 고정 모드에서 한 번 맞은 뒤 true가 됩니다.
    /// 판정 콜라이더는 이 값이 true면 다음 공격 전까지 스스로 꺼져야 합니다.
    /// 콜라이더를 이 모듈이 직접 들고 있지 않으므로, 끄는 행위는 콜라이더 쪽 책임으로 둡니다.
    /// </remarks>
    public bool IsSwingConsumed => m_singleHitPerSwing && m_swingHits.Count > 0;

    /// <summary>
    /// 적 밸런스 데이터에서 공격 시작 조건과 판정 수치를 적용합니다.
    /// </summary>
    /// <param name="balance">적용할 순수 수치 밸런스 데이터입니다.</param>
    public void ApplyBalance(EnemyBalanceSO balance)
    {
        if (balance == null)
        {
            return;
        }

        m_attackRange = balance.AttackRange;
        m_attackStartAngle = balance.AttackStartAngle;
        m_attackDamage = balance.AttackDamage;
        m_attackMaxTargets = balance.AttackMaxTargets;
    }

    /// <summary>공격 상태 판정에 사용할 실제 모델 Animator를 지정합니다.</summary>
    /// <remarks>
    /// Enemy 루트에 남아 있는 호환용 Animator가 아니라 FBX 골격을 직접 구동하는 Animator를 공유해야
    /// 현재 공격 상태와 전이 정보를 정확히 읽을 수 있습니다.
    /// </remarks>
    public void SetAnimator(Animator animator)
    {
        m_animator = animator;
    }

    private void Awake()
    {
        m_owner = GetComponentInParent<EnemyController>();
        // 공격 주체의 진영을 소유 HealthSystemBase에서 가져옵니다. 없으면 Enemy로 가정합니다.
        HealthSystemBase ownerHealth = GetComponentInParent<HealthSystemBase>();
        m_ownerFaction = ownerHealth != null ? ownerHealth.Faction : Faction.Enemy;

        m_resolvedObstacleMask = EnemyLayers.ResolveObstacleMask(m_obstacleLayer, this, "공격 판정 차단");

        if (m_animator == null)
        {
            m_animator = GetComponentInParent<Animator>();
        }

        // 근접 무기를 지정하지 않았으면 자식에서 찾습니다. 손이 둘이면 둘 다 잡힙니다.
        if (m_melees == null || m_melees.Length == 0)
        {
            m_melees = GetComponentsInChildren<Melee>(true);
        }

        // 판정 콜라이더는 공격 구간에서만 켜집니다. 시작은 항상 꺼진 상태입니다.
        SetHitboxActive(false);
    }

    /// <summary>
    /// 지정한 대상에게 공격을 시작할 수 있는지 확인합니다.
    /// </summary>
    /// <param name="target">현재 대상입니다.</param>
    /// <returns>시작 거리와 허용 방향각을 모두 충족하면 true입니다.</returns>
    /// <remarks>
    /// 시작 조건은 실제 판정 범위와 독립된 값입니다. 공격을 시작한 뒤 대상이 범위를 벗어나도 취소하지 않으며,
    /// 그 경우 빗나간 공격이 됩니다(§5.9.1).
    /// </remarks>
    public bool CanStartAttack(SquadMemberController target)
    {
        if (!IsAliveMember(target))
        {
            return false;
        }

        return CanStartAttackAt(target.transform.position);
    }

    /// <summary>지정된 방어 목표의 공격 지점이 공격 시작 범위에 있는지 확인합니다.</summary>
    /// <remarks>
    /// 공격 포인트를 골랐으면 그 포인트의 공격 시작 거리 안에 들어와야 시작합니다. 일반 공격 거리로 시작하면
    /// 손이 목표 표면에 닿기 전에 멈춰 서서 헛치기 때문입니다. 다만 NavMesh 때문에 더 다가갈 수 없으면(도착)
    /// 일반 공격 거리로 시작합니다.
    /// </remarks>
    public bool CanStartDefenseAttack(DefenseEventHealth target)
    {
        if (!TryGetDefenseAttackPoint(target, out Vector3 point))
        {
            return false;
        }

        DefenseAttackPoint assigned = GetAssignedAttackPoint(target);
        if (assigned != null)
        {
            Vector3 delta = point - transform.position;
            delta.y = 0.0f;
            float startDistance = Mathf.Min(m_attackRange, assigned.AttackStartDistance);
            bool inReach = delta.sqrMagnitude <= startDistance * startDistance;
            if (!inReach && !(m_owner != null && m_owner.HasSettledDefenseApproach))
            {
                return false;
            }
        }

        return CanStartAttackAt(point);
    }

    /// <summary>
    /// 방어 목표의 접근/회전 지점을 제공합니다. 고른 공격 포인트가 있으면 그 위치, 없으면 가장 가까운 유효 콜라이더 표면입니다.
    /// </summary>
    /// <remarks>콜라이더가 없는 좌표 마커는 공격 대상으로 만들지 않습니다. 캐시는 목표 교체 때 갱신합니다.</remarks>
    public bool TryGetDefenseAttackPoint(DefenseEventHealth target, out Vector3 point)
    {
        point = default;
        if (target == null || target.IsDead || !target.isActiveAndEnabled) return false;
        DefenseAttackPoint assigned = GetAssignedAttackPoint(target);
        if (assigned != null)
        {
            point = assigned.Position;
            return true;
        }
        if (m_cachedObjective != target)
        {
            m_cachedObjective = target;
            m_objectiveColliders.Clear();
            target.GetComponentsInChildren(true, m_objectiveColliders);
        }
        float best = float.PositiveInfinity;
        foreach (Collider candidate in m_objectiveColliders)
        {
            if (candidate == null || !candidate.enabled || !candidate.gameObject.activeInHierarchy ||
                candidate.isTrigger || candidate.GetComponentInParent<DefenseEventHealth>() != target) continue;
            Vector3 candidatePoint = candidate.ClosestPoint(transform.position);
            float distance = (candidatePoint - transform.position).sqrMagnitude;
            if (distance < best)
            {
                best = distance;
                point = candidatePoint;
            }
        }
        return !float.IsPositiveInfinity(best);
    }

    /// <summary>이 개체가 지정 목표에서 고른 공격 포인트를 돌려줍니다. 고르지 않았거나 다른 목표의 포인트면 null입니다.</summary>
    private DefenseAttackPoint GetAssignedAttackPoint(DefenseEventHealth target)
    {
        DefenseAttackPoint assigned = m_owner != null ? m_owner.DefenseAttackPoint : null;
        return assigned != null && assigned.isActiveAndEnabled && assigned.Owner == target ? assigned : null;
    }

    private bool CanStartAttackAt(Vector3 point)
    {
        Vector3 delta = point - transform.position;
        delta.y = 0f;

        if (delta.sqrMagnitude > m_attackRange * m_attackRange)
        {
            return false;
        }

        if (delta.sqrMagnitude <= 0.0001f)
        {
            return true;
        }

        return Vector3.Angle(transform.forward, delta.normalized) <= m_attackStartAngle * 0.5f;
    }

    /// <summary>
    /// 새로운 공격 행동을 시작합니다.
    /// </summary>
    /// <remarks>
    /// 적중 기록을 비웁니다. 이 시점부터 판정 콜라이더가 다시 피해를 낼 수 있습니다.
    /// 스윙당 1회 고정 모드에서 꺼 두었던 판정 콜라이더를 다시 켜도 되는 시점이기도 합니다.
    ///
    /// 판정 콜라이더도 여기서 끕니다. 앞 클립의 끄기 이벤트가 오기 전에 다음 클립이 시작되면(대기 상한 초과,
    /// 끄기 이벤트가 없는 클립 등) 끄지 않은 콜라이더가 계속 켜진 채로 남습니다. 적중은 진입 순간에만 판정하므로, 그러면
    /// 손이 대상 안에 머무는 동안 두 번째 타부터 맞지 않습니다. 다음 클립의 켜기 이벤트가 다시 켜면
    /// 그 뒤에 들어온 대상은 다시 진입으로 잡힙니다.
    /// </remarks>
    public void BeginSwing()
    {
        m_swingHits.Clear();
        SetHitboxActive(false);
    }

    /// <summary>
    /// 판정 콜라이더를 켜거나 끕니다.
    /// </summary>
    /// <param name="active">켜면 true입니다.</param>
    /// <remarks>
    /// 켜고 끄는 시점은 공격 클립의 애니메이션 이벤트가 정합니다.
    /// 콜라이더가 꺼져 있는 동안에는 <see cref="TryApplyDamageTo"/>가 호출될 일이 없으므로
    /// 휘두르기 전후로 스쳐도 피해가 들어가지 않습니다.
    /// </remarks>
    public void SetHitboxActive(bool active)
    {
        IsHitboxActive = active;

        if (m_melees == null)
        {
            return;
        }

        for (int i = 0; i < m_melees.Length; i++)
        {
            if (m_melees[i] != null)
            {
                m_melees[i].SetActive(active);
            }
        }
    }

    /// <summary>
    /// 지금 재생 중인 공격 스테이트의 짧은 이름 해시를 돌려줍니다.
    /// </summary>
    /// <returns>스테이트 이름 해시이며, 애니메이터가 없으면 0입니다.</returns>
    /// <remarks>
    /// 전이 중에는 아직 이전 스테이트가 현재로 잡히므로 넘어가는 쪽을 봅니다.
    /// 연타 사이의 전이 구간에서 판정이 열리면 앞 타의 피해가 적용되는 것을 막기 위해서입니다.
    /// </remarks>
    private int GetCurrentAttackStateHash()
    {
        if (m_animator == null)
        {
            return 0;
        }

        AnimatorStateInfo info = m_animator.IsInTransition(0)
            ? m_animator.GetNextAnimatorStateInfo(0)
            : m_animator.GetCurrentAnimatorStateInfo(0);

        return info.shortNameHash;
    }

    /// <summary>
    /// 판정 콜라이더에 닿은 후보 하나에 대해 피해를 적용할지 판단하고 적용합니다.
    /// </summary>
    /// <param name="source">피해를 낼 근접 무기입니다. 좌우 손을 구분하고 피해량을 가져오는 데 씁니다.</param>
    /// <param name="other">판정 콜라이더에 닿은 상대 콜라이더입니다.</param>
    /// <returns>실제로 피해를 입혔으면 true입니다.</returns>
    /// <remarks>
    /// 현재 대상을 인자로 받지 않는 것이 의도입니다. 닿은 것이 곧 피격 후보이며,
    /// 처음 겨눈 대상이 빠져나갔다면 맞지 않습니다(§5.9.2).
    /// 진입 시점에만 부르더라도 같은 공격에서 중복 적중은 생깁니다.
    /// 좌우 콜라이더가 각각 발화하고, 대상이 빠져나갔다 다시 들어와도 또 발화하기 때문입니다.
    /// 그 중복을 어떻게 취급할지는 스윙당 1회 고정 토글이 정합니다.
    /// 다른 변이체는 피해 대상이 아니고 판정을 가리지도 않습니다. 고정 환경 장애물만 판정을 막습니다.
    ///
    /// 피해량은 무기가 정합니다. 몇 타째인지에 따라 값이 달라질 수 있어 매번 무기에 물어봅니다.
    /// </remarks>
    public bool TryApplyDamageTo(Melee source, Collider other)
    {
        if (source == null || other == null || !IsHitboxActive || IsSwingConsumed)
        {
            return false;
        }

        Collider sourceCollider = source.HitCollider;
        if (sourceCollider == null)
        {
            return false;
        }

        SquadMemberController member = other.GetComponentInParent<SquadMemberController>();
        Component victim = member;
        if (member != null)
        {
            // 방어 대상 우선과, 아직 전환하지 않은 혼합형은 스쿼드원을 맞히지 않습니다.
            if (!IsAliveMember(member) || (m_owner != null && m_owner.IsIgnoringSquad)) return false;
        }
        else
        {
            DefenseEventHealth objective = other.GetComponentInParent<DefenseEventHealth>();
            if (objective == null || objective.IsDead || m_owner == null ||
                !m_owner.IsAttackingDefenseObjective || m_owner.DefenseObjective != objective) return false;
            victim = objective;
        }
        if (IsAlreadyHit(source, victim))
        {
            return false;
        }

        if (CountDistinctMembersHit() >= m_attackMaxTargets && !IsMemberAlreadyCounted(victim))
        {
            return false;
        }

        if (IsBlockedByObstacle(sourceCollider.bounds.center, other))
        {
            return false;
        }

        // 몇 타째인지는 재생 중인 스테이트가 정하고, 그 타의 피해량은 무기가 정합니다.
        int attackStateHash = GetCurrentAttackStateHash();
        int damage = source.ResolveDamage(attackStateHash, m_attackDamage);

        // 경직력도 타별로 무기에 물어봅니다. 현재 대상인 플레이어·아군은 경직이 없어 이 값은 무시되지만,
        // 경직을 가진 대상을 때리는 경로가 생기면 분기 없이 그대로 성립합니다.
        float staggerPower = source.ResolveStaggerPower(attackStateHash, 0);

        // 진영 판정과 실제 피해 적용은 공용 경로로 처리합니다.
        // 맞은 쪽이 누구에게 맞았는지 알 수 있도록 이 변이체를 함께 넘깁니다.
        MeleeBalanceSO balance = source.Balance;
        DamageInfo hit = new DamageInfo(damage, DamageCategory.Melee, balance != null ? balance.MeleeId : source.name, gameObject);
        if (balance != null && balance.AllowHeadshot)
        {
            // 약점 판정을 쓰는 무기만 Hitbox를 요구하는 경로로 갑니다.
            // 변이체의 공격은 약점 없이 정배수로 확정되어 있어 보통 아래 경로를 씁니다.
            CombatDamage.HitFeedback feedback = CombatDamage.ResolveHit(
                other, m_ownerFaction, hit, balance.HeadshotDamageMultiplier, true, staggerPower);

            if (!feedback.Applied)
            {
                return false;
            }
        }
        else
        {
            if (!CombatDamage.TryApplyDamage(other, m_ownerFaction, hit))
            {
                return false;
            }

            CombatDamage.TryApplyStagger(other, staggerPower);
        }

        ApplyMeleeKnockback(source, other);

        m_swingHits.Add(new SwingHit(source, victim));
        return true;
    }

    /// <summary>
    /// 적중한 대상에 근접 공격의 넉백을 전달합니다.
    /// </summary>
    /// <param name="source">이번 적중을 만든 근접 무기입니다.</param>
    /// <param name="other">맞은 대상의 콜라이더입니다.</param>
    /// <remarks>
    /// 피해가 실제로 들어간 뒤에만 부릅니다. 막히거나 이미 맞은 대상에는 밀어내기도 없어야 합니다.
    ///
    /// 방향은 <b>공격자에서 피격 지점으로 향하는 수평 벡터</b>입니다. 총기와 같은 규칙이며, 근접에서는
    /// "때린 쪽에서 멀어지게 밀린다"는 뜻이 됩니다. 피격 지점은 판정 콜라이더에서 가장 가까운 표면을
    /// 씁니다. 근접은 히트스캔이 없어 정확한 접점이 없기 때문입니다.
    ///
    /// 받는 쪽이 <see cref="IKnockbackReceiver"/>를 구현하지 않았으면 조용히 넘어갑니다.
    /// 현재 스쿼드 멤버는 구현하지 않아 실제로는 아무 일도 일어나지 않으며, 값도 0이 기본입니다.
    /// 배선을 미리 이어 두는 이유는 넉백 규칙이 정해질 때 호출부를 다시 찾지 않아도 되게 하기 위해서입니다.
    /// </remarks>
    private void ApplyMeleeKnockback(Melee source, Collider other)
    {
        IKnockbackReceiver receiver = other.GetComponentInParent<IKnockbackReceiver>();
        if (receiver == null)
        {
            return;
        }

        MeleeBalanceSO balance = source.Balance;
        float impulse = source.ResolveKnockbackImpulse(
            GetCurrentAttackStateHash(),
            balance != null ? balance.FallbackKnockbackImpulse : 0.0f);

        if (impulse <= 0.0f)
        {
            return;
        }

        Vector3 hitPoint = other.ClosestPoint(transform.position);
        Vector3 direction = hitPoint - transform.position;
        if (direction.sqrMagnitude <= Mathf.Epsilon)
        {
            // 완전히 겹친 상태입니다. 이 변이체가 보는 방향으로 밀어냅니다.
            direction = transform.forward;
        }

        receiver.ApplyKnockback(direction, hitPoint, impulse, other.attachedRigidbody);
    }

    /// <summary>
    /// 이 조합이 이번 공격에서 이미 적중했는지 확인합니다.
    /// </summary>
    /// <remarks>
    /// 스윙당 1회 고정이면 출처와 무관하게 같은 캐릭터를 다시 때리지 않습니다.
    /// 손별 독립이면 같은 손이 같은 캐릭터를 다시 때리는 것만 막고, 반대 손은 따로 판정합니다.
    /// </remarks>
    private bool IsAlreadyHit(Melee source, Component member)
    {
        for (int i = 0; i < m_swingHits.Count; i++)
        {
            SwingHit hit = m_swingHits[i];
            if (hit.Member != member)
            {
                continue;
            }

            if (m_singleHitPerSwing || hit.Source == source)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>이번 공격에서 피해를 입은 서로 다른 캐릭터 수입니다.</summary>
    /// <remarks>다중 대상 수는 적중 횟수가 아니라 캐릭터 수로 셉니다. 양손에 맞아도 한 명입니다.</remarks>
    private int CountDistinctMembersHit()
    {
        int count = 0;
        for (int i = 0; i < m_swingHits.Count; i++)
        {
            if (!IsMemberCountedBefore(i))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>같은 캐릭터가 앞선 기록에 이미 있는지 확인합니다.</summary>
    private bool IsMemberCountedBefore(int index)
    {
        for (int i = 0; i < index; i++)
        {
            if (m_swingHits[i].Member == m_swingHits[index].Member)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>이 캐릭터가 이번 공격에서 이미 한 번이라도 맞았는지 확인합니다.</summary>
    /// <remarks>이미 센 캐릭터라면 다중 대상 수를 넘겼더라도 반대 손의 추가 적중은 허용합니다.</remarks>
    private bool IsMemberAlreadyCounted(Component member)
    {
        for (int i = 0; i < m_swingHits.Count; i++)
        {
            if (m_swingHits[i].Member == member)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 판정 지점과 대상 사이를 고정 환경 장애물이 가로막는지 확인합니다.
    /// </summary>
    /// <remarks>벽 너머로 겹친 캐릭터가 맞지 않게 하려는 것입니다(§5.9.2).</remarks>
    private bool IsBlockedByObstacle(Vector3 origin, Collider targetCollider)
    {
        Vector3 target = targetCollider.bounds.center;
        Vector3 delta = target - origin;
        float distance = delta.magnitude;

        if (distance <= 0.0001f)
        {
            return false;
        }

        if (!Physics.Raycast(origin, delta / distance, out RaycastHit hit, distance,
            m_resolvedObstacleMask, QueryTriggerInteraction.Ignore)) return false;

        // 방어 목표 자체가 환경 레이어여도 자기 표면을 '앞을 가로막는 벽'으로 취급하지 않습니다.
        if (hit.collider == targetCollider) return false;
        DefenseEventHealth objective = targetCollider.GetComponentInParent<DefenseEventHealth>();
        return objective == null || hit.collider.GetComponentInParent<DefenseEventHealth>() != objective;
    }

    /// <summary>공격 대상으로 삼을 수 있는 살아 있는 스쿼드 캐릭터인지 확인합니다.</summary>
    private static bool IsAliveMember(SquadMemberController member)
    {
        return member != null && member.IsAlive && !member.IsDown;
    }

    /// <summary>
    /// 공격 시작 거리를 Scene 뷰에서 확인하기 위한 Gizmo입니다.
    /// </summary>
    /// <remarks>
    /// 판정 범위는 손에 달린 판정 콜라이더가 직접 그려 주므로 여기서는 시작 조건만 표시합니다.
    /// 둘은 별개의 값이라 같이 보면 오히려 헷갈립니다.
    /// </remarks>
    private void OnDrawGizmosSelected()
    {
        if (!m_debugDrawAttackRange)
        {
            return;
        }

        Gizmos.color = new Color(1f, 0.5f, 0f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, m_attackRange);
    }
}
