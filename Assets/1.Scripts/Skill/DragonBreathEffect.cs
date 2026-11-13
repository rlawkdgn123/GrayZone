using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>짧게 활성화되어 용숨결 시각 효과와 전방 원뿔형 피해, 착탄 범위 피해를 함께 처리하는 재사용 프리팹 컴포넌트입니다.</summary>
/// <remarks>
/// 판정은 시각 효과에 맞춥니다. 불기둥은 총구에서 가늘게 시작해 앞으로 갈수록 퍼지고, 벽에서 끊기며,
/// 맞은 자리에서 스파크가 터집니다. 그래서 원뿔 판정은 벽 앞에서 끝나고, 착탄 지점 주변에도 피해를 줍니다.
/// 적의 몸은 관통해 원뿔 안의 적을 모두 맞힙니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class DragonBreathEffect : MonoBehaviour, IBalancePostProcess
{
    private const string BurningEffectResourcePath = "StatusEffects/Burning";

    [Tooltip("총구 쪽 원뿔 판정의 반지름(m)입니다. 불기둥이 총구에서 가늘게 시작하는 폭에 맞춥니다.")]
    [Min(0.01f)]
    [BalanceField]
    [SerializeField] private float m_damageStartRadius = 0.3f;

    [Tooltip("판정 길이 끝 지점의 원뿔 반지름(m)입니다. 스파크가 앞으로 갈수록 퍼지는 폭에 맞춥니다. " +
             "벽에 막혀 판정이 짧아져도 원뿔 모양은 그대로이고 앞부분만 잘립니다.")]
    [FormerlySerializedAs("m_damageRadius")]
    [Min(0.01f)]
    [BalanceField]
    [SerializeField] private float m_damageEndRadius = 0.8f;

    [Tooltip("총구 앞쪽으로 뻗는 원뿔 판정의 길이(m)이자 착탄 지점을 찾는 거리입니다. 불기둥이 실제로 닿는 거리에 맞춥니다. " +
             "지형(벽)에 막히면 그 앞에서 끝납니다.")]
    [Min(0.01f)]
    [BalanceField]
    [SerializeField] private float m_damageRange = 10.0f;

    [Tooltip("가까운 착탄(크기 배율 시작 거리 이내)에서 착탄 지점 주변에 피해와 화상을 주는 반경(m)입니다. 착탄 스파크가 퍼지는 범위에 맞춥니다. " +
             "멀리서 맞으면 착탄 시각 효과와 같은 배율로 커집니다. 원뿔에서 이미 맞은 대상은 다시 맞지 않습니다. 0이면 착탄 범위 피해가 없습니다.")]
    [Min(0.0f)]
    [BalanceField]
    [SerializeField] private float m_impactDamageRadius = 1.7f;

    [Tooltip("기본 피해입니다. 아래 거리 구간 표가 비어 있거나 배율 모드일 때 기준으로 씁니다.")]
    [Min(1)]
    [BalanceField]
    [SerializeField] private int m_damage = 10;

    [Tooltip("총구에서의 거리에 따른 구간별 피해 표입니다. 원뿔 피격은 총구에서 대상까지의 거리, " +
             "착탄 범위 피격은 총구에서 착탄 지점까지의 거리로 구간을 고릅니다. 비어 있으면 기본 피해를 그대로 씁니다.")]
    [BalanceField]
    [SerializeField] private DamageFalloffTable m_damageFalloff = new DamageFalloffTable();

    [Tooltip("판정과 임시 불꽃 이펙트가 유지되는 시간(초)입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_lifetime = 0.18f;

    [Tooltip("용숨결 피해 후보를 검색할 레이어입니다. 실제 피해 여부는 진영 규칙이 한 번 더 검사합니다.")]
    [SerializeField] private LayerMask m_damageLayerMask = ~0;

    [Tooltip("용숨결 직격 피해가 성립한 대상에게 적용할 화상 상태이상입니다. 비어 있으면 Resources 기본 에셋을 사용합니다.")]
    [SerializeField] private StatusEffectDefinitionSO m_burningEffect;

    [Tooltip("켜면 용숨결에 맞은 대상에게 화상(불에서 벗어나도 남는 지속 피해)을 겁니다. 화상 수치는 화상 상태이상 에셋이 정합니다.")]
    [SerializeField] private bool m_applyBurning = true;

    [Tooltip("켜면 발사 여부와 오브젝트 선택 여부에 관계없이 Scene 뷰에 원뿔 판정 범위를 계속 표시하고, 발사할 때 실제로 쓴 판정(벽에 잘린 원뿔·착탄 범위)을 잠깐 그립니다.")]
    [SerializeField] private bool m_drawDebugRange = true;

    [Tooltip("용숨결탄 발사 시 총구에서 재생할 시각 효과 프리팹입니다(총구 화염과 원뿔형 스파크 확산). " +
             "비어 있으면 임시 파티클을 생성합니다.")]
    [SerializeField] private GameObject m_visualEffectPrefab;

    [Tooltip("시각 효과 프리팹에 줄 회전(도)입니다. 발사 방향(+Z) 기준입니다. " +
             "Fire 계열 VFX는 위(+Y)로 솟으므로 X 90을 줘서 눕혀야 총구 앞으로 퍼집니다. +Z로 만든 프리팹은 0으로 둡니다.")]
    [SerializeField] private Vector3 m_visualEffectRotation = new Vector3(90.0f, 0.0f, 0.0f);

    [Tooltip("불길이 닿은 지점에서 재생할 착탄 스파크 프리팹입니다(선택). +Z가 맞은 면의 바깥쪽을 향하도록 생성합니다. " +
             "비어 있으면 착탄 효과를 재생하지 않습니다.")]
    [SerializeField] private GameObject m_impactEffectPrefab;

    [Tooltip("불길이 착탄 지점까지 날아가는 속도(m/s)입니다. 거리 ÷ 속도만큼 착탄 시각 효과를 늦게 재생합니다(피해는 발사 순간 적용). " +
             "0이면 지연 없이 바로 재생합니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_impactTravelSpeed = 60.0f;

    [Tooltip("착탄 시각 효과가 커지기 시작하는 거리(m)입니다. 이보다 가까우면 원래 크기로 재생합니다.")]
    [Min(0.0f)]
    [BalanceField]
    [SerializeField] private float m_impactScaleStartDistance = 6.0f;

    [Tooltip("최대 거리(판정 길이)에서 착탄 시각 효과와 착탄 범위 피해 반경의 배율입니다. 시작 거리부터 최대 거리까지 1배에서 이 값까지 커집니다. " +
             "멀리서 맞을수록 퍼진 불길이 넓게 부딪히는 모습이고, 피해 범위도 같이 넓어집니다.")]
    [Min(0.1f)]
    [BalanceField]
    [SerializeField] private float m_impactFarScale = 1.6f;

    /// <summary>지운 뒤 남은 파티클이 갑자기 끊기지 않도록 수명에 더하는 여유(초)입니다.</summary>
    private const float VisualCleanupMargin = 0.1f;

    /// <summary>착탄 효과가 맞은 면 속에 묻히지 않도록 면 바깥으로 띄우는 거리(m)입니다.</summary>
    private const float ImpactSurfaceOffset = 0.05f;

    /// <summary>총구가 벽에 붙어 있을 때도 남기는 최소 원뿔 판정 길이(m)입니다.</summary>
    private const float MinimumReach = 0.3f;

    private readonly RaycastHit[] m_traceHits = new RaycastHit[16];

    private readonly HashSet<IDamageable> m_damagedTargets = new HashSet<IDamageable>();
    private ParticleSystem m_particles;
    private Material m_runtimeParticleMaterial;
    private Faction m_ownerFaction;
    private GameObject m_attacker;
    private float m_disableTime;

    public int Damage => Mathf.Max(1, m_damage);

    /// <summary>총구 쪽 원뿔 판정 반지름(m)입니다.</summary>
    public float DamageStartRadius => Mathf.Max(0.01f, m_damageStartRadius);

    /// <summary>판정 길이 끝 지점의 원뿔 판정 반지름(m)입니다.</summary>
    public float DamageEndRadius => Mathf.Max(0.01f, m_damageEndRadius);

    /// <summary>벽에 막히지 않았을 때의 원뿔 판정 길이(m)이자 착탄 지점을 찾는 거리입니다.</summary>
    public float DamageRange => Mathf.Max(0.01f, m_damageRange);

    /// <summary>가까운 착탄의 범위 피해 반경(m)입니다. 실제 반경은 착탄 거리 배율이 곱해집니다(<see cref="GetImpactDamageRadius"/>). 0이면 범위 피해가 없습니다.</summary>
    public float ImpactDamageRadius => Mathf.Max(0.0f, m_impactDamageRadius);
    public float Lifetime => Mathf.Max(0.01f, m_lifetime);
    public bool DrawDebugRange => m_drawDebugRange;

    private void Awake()
    {
        // 표 조회는 거리 오름차순을 전제하므로 시작할 때 한 번 정렬합니다.
        m_damageFalloff?.Sort();
        EnsureTemporaryParticles();
    }

    /// <summary>청솔 스킬 SO에서 피해·판정 값을 받은 직후 호출됩니다.</summary>
    /// <remarks>
    /// 구간 표는 참조로 대입되므로 그대로 두면 SO와 같은 목록을 공유합니다. 런타임에 표를 고치면 프로젝트 에셋인
    /// SO까지 바뀌므로 복제해서 씁니다. 복제본은 정렬된 상태입니다.
    /// </remarks>
    public void OnBalanceApplied()
    {
        m_damageFalloff = m_damageFalloff != null ? m_damageFalloff.Clone() : new DamageFalloffTable();
    }

    private void Update()
    {
        if (Time.time >= m_disableTime)
        {
            gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (m_runtimeParticleMaterial != null)
        {
            Destroy(m_runtimeParticleMaterial);
        }
    }

    /// <summary>총구 위치와 실제 발사 방향으로 시각 효과를 내고, 원뿔 판정과 착탄 범위 판정을 즉시 수행합니다.</summary>
    /// <remarks>
    /// 피해는 발사 프레임에 한 번에 끝납니다. 착탄 시각 효과만 불길이 날아가는 시간만큼 늦게 나옵니다.
    /// 같은 대상은 원뿔과 착탄 범위에 모두 걸려도 한 번만 피해를 받습니다.
    /// </remarks>
    public void Play(Vector3 origin, Vector3 direction, Faction ownerFaction, GameObject attacker)
    {
        if (direction.sqrMagnitude <= 0.0f)
        {
            return;
        }

        direction.Normalize();
        transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction, Vector3.up));
        gameObject.SetActive(true);
        EnsureTemporaryParticles();

        m_ownerFaction = ownerFaction;
        m_attacker = attacker;
        m_damagedTargets.Clear();
        m_disableTime = Time.time + Lifetime;

        if (m_visualEffectPrefab != null)
        {
            Quaternion muzzleRotation = Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Euler(m_visualEffectRotation);
            SpawnDetachedVisual(m_visualEffectPrefab, origin, muzzleRotation, 0.0f);
        }
        else if (m_particles != null)
        {
            m_particles.Clear(true);
            m_particles.Play(true);
        }

        // 사격 프레임에만 판정하므로 생성 전에 범위 안에 있던 대상도 확실히 잡습니다.
        Physics.SyncTransforms();
        TraceBreath(origin, direction, out float reach, out bool hasImpact, out RaycastHit impact);

        // 레이는 조준선 한 줄이라 원뿔에 걸린 적을 빗나가기 쉽습니다. 원뿔에 맞은 적이 레이 착탄보다 가까우면
        // 그 적의 맞은 지점을 착탄으로 씁니다. 그래야 벽이 아니라 적에게 쏴도 착탄 폭발이 납니다.
        if (ApplyConeDamage(origin, direction, reach, out Vector3 nearestHitPoint, out float nearestHitDistance)
            && (!hasImpact || nearestHitDistance < impact.distance))
        {
            impact = new RaycastHit
            {
                point = nearestHitPoint,
                normal = -direction,
                distance = nearestHitDistance,
            };
            hasImpact = true;
        }

        if (hasImpact)
        {
            Vector3 impactPoint = GetImpactPoint(origin, direction, impact);
            ApplyImpactSplashDamage(impactPoint, impact.distance);

            if (m_impactEffectPrefab != null)
            {
                SpawnImpactVisual(origin, direction, impact, impactPoint);
            }
        }

        if (m_drawDebugRange)
        {
            float duration = Lifetime + 0.25f;
            DrawDebugCone(origin, direction, reach, duration);
            if (hasImpact && ImpactDamageRadius > 0.0f)
            {
                DrawDebugSphere(GetImpactPoint(origin, direction, impact), GetImpactDamageRadius(impact.distance), duration);
            }
        }
    }

    /// <summary>
    /// 발사 방향으로 레이를 한 번 쏴서 원뿔 판정이 끝나는 거리(첫 지형까지)와 착탄 지점을 함께 구합니다.
    /// </summary>
    /// <param name="reach">원뿔 판정 길이입니다. 지형에 막히면 그 거리, 아니면 <see cref="DamageRange"/>입니다.</param>
    /// <param name="hasImpact">판정 길이 안에서 불길이 처음 닿는 대상(적 또는 지형)이 있는지 여부입니다.</param>
    /// <param name="impact">그 첫 대상의 레이캐스트 결과입니다.</param>
    /// <remarks>
    /// 쏜 사람의 콜라이더와 트리거는 건너뜁니다. 아군 몸은 일반 총탄과 같은 규칙(<see cref="CombatDamage.BlocksShot"/>)으로 통과합니다.
    /// 적의 몸은 착탄 지점이 되지만 원뿔 판정을 끊지는 않습니다(원뿔 안의 적은 모두 맞습니다). 원뿔을 끊는 것은 지형뿐입니다.
    /// </remarks>
    private void TraceBreath(Vector3 origin, Vector3 direction, out float reach, out bool hasImpact, out RaycastHit impact)
    {
        reach = DamageRange;
        hasImpact = false;
        impact = default;

        int count = Physics.RaycastNonAlloc(
            origin, direction, m_traceHits, DamageRange, m_damageLayerMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = m_traceHits[i];
            if (m_attacker != null && hit.collider.transform.IsChildOf(m_attacker.transform))
            {
                continue;
            }

            // 일반 총탄과 같은 아군 통과 규칙입니다. 앞에 선 팀원 몸에서 착탄되거나 판정이 끊기지 않습니다.
            if (!CombatDamage.BlocksShot(hit.collider, m_ownerFaction, true))
            {
                continue;
            }

            if (!hasImpact || hit.distance < impact.distance)
            {
                impact = hit;
                hasImpact = true;
            }

            bool isTerrain = hit.collider.GetComponentInParent<IDamageable>() == null;
            if (isTerrain && hit.distance < reach)
            {
                reach = hit.distance;
            }
        }

        // 총구가 이미 벽에 붙어 있으면 길이가 0이 되어 판정이 사라지므로 아주 짧게라도 남깁니다.
        reach = Mathf.Max(reach, MinimumReach);
    }

    /// <summary>OverlapBox로 후보를 모은 뒤 축 방향과 원뿔 반지름을 검사해 원뿔 안 대상만 피해 처리합니다.</summary>
    /// <remarks>
    /// 트리거는 후보에서 뺍니다. 적에게는 몸보다 훨씬 큰 감지용 트리거(HitDetectVolume, 반경 약 1.7m)와 접촉 센서가 있어,
    /// 트리거까지 보면 원뿔 밖의 적이 맞습니다. 실제 몸 콜라이더만 판정합니다(부위 히트박스는 평소 꺼져 있습니다).
    /// </remarks>
    /// <returns>원뿔 안에 맞을 수 있는 대상(적 몸)이 있었으면 true입니다.</returns>
    /// <param name="nearestHitPoint">그중 총구에서 가장 가까운 대상의, 총구 축에 가장 가까운 표면 지점입니다.</param>
    /// <param name="nearestHitDistance">그 지점의 총구로부터 축 방향 거리(m)입니다.</param>
    private bool ApplyConeDamage(Vector3 origin, Vector3 direction, float reach, out Vector3 nearestHitPoint, out float nearestHitDistance)
    {
        bool hasHit = false;
        nearestHitPoint = Vector3.zero;
        nearestHitDistance = float.MaxValue;

        float maxRadius = Mathf.Max(DamageStartRadius, GetConeRadius(reach));
        Vector3 center = origin + direction * (reach * 0.5f);
        Vector3 halfExtents = new Vector3(maxRadius, maxRadius, reach * 0.5f);
        Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);
        Collider[] overlaps = Physics.OverlapBox(
            center,
            halfExtents,
            rotation,
            m_damageLayerMask,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider candidate = overlaps[i];
            if (candidate == null || !IntersectsCone(candidate, origin, direction, reach, out float axial))
            {
                continue;
            }

            TryDamage(candidate, axial);

            // 착탄 지점 후보는 피해를 받을 수 있는 몸(적 등)만 봅니다. 쏜 사람과 아군은 일반 총탄처럼 통과합니다.
            if (axial < nearestHitDistance
                && candidate.GetComponentInParent<IDamageable>() != null
                && (m_attacker == null || !candidate.transform.IsChildOf(m_attacker.transform))
                && CombatDamage.BlocksShot(candidate, m_ownerFaction, true))
            {
                hasHit = true;
                nearestHitDistance = axial;
                nearestHitPoint = candidate.ClosestPoint(origin + direction * axial);
            }
        }

        // 화염 범위 안의 폭발물 함정도 불이 붙어 터집니다. 피해 레이어와 상관없이 따로 찾습니다.
        ExplosionDamage.TriggerChainDetonation(
            center,
            halfExtents,
            rotation,
            candidate => IntersectsCone(candidate, origin, direction, reach, out _),
            m_attacker);

        return hasHit;
    }

    /// <summary>착탄 지점 반경 안의 대상에게 피해와 화상을 주고, 폭발물 함정에도 불을 붙입니다.</summary>
    private void ApplyImpactSplashDamage(Vector3 point, float impactDistance)
    {
        float radius = GetImpactDamageRadius(impactDistance);
        if (radius <= 0.0f)
        {
            return;
        }

        // 원뿔과 같은 이유로 트리거는 빼고 몸 콜라이더만 봅니다.
        Collider[] overlaps = Physics.OverlapSphere(point, radius, m_damageLayerMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlaps.Length; i++)
        {
            if (overlaps[i] != null)
            {
                TryDamage(overlaps[i], impactDistance);
            }
        }

        float sqrRadius = radius * radius;
        ExplosionDamage.TriggerChainDetonation(
            point,
            new Vector3(radius, radius, radius),
            Quaternion.identity,
            candidate => (candidate.ClosestPoint(point) - point).sqrMagnitude <= sqrRadius,
            m_attacker);
    }

    /// <summary>총구에서 <paramref name="axial"/>m 떨어진 지점의 원뿔 반지름입니다.</summary>
    /// <remarks>벽에 잘려 판정이 짧아져도 기울기는 <see cref="DamageRange"/> 기준 그대로입니다.</remarks>
    private float GetConeRadius(float axial)
    {
        return Mathf.Lerp(DamageStartRadius, DamageEndRadius, Mathf.Clamp01(axial / DamageRange));
    }

    /// <summary>콜라이더가 원뿔 안에 걸리는지 검사합니다.</summary>
    /// <param name="axial">걸린 지점의 총구로부터 축 방향 거리(m)입니다. 거리 구간 피해를 고를 때 씁니다.</param>
    private bool IntersectsCone(Collider candidate, Vector3 origin, Vector3 direction, float reach, out float axial)
    {
        float centerDistance = Mathf.Clamp(Vector3.Dot(candidate.bounds.center - origin, direction), 0.0f, reach);
        Vector3 axisPoint = origin + direction * centerDistance;
        Vector3 closest = candidate.ClosestPoint(axisPoint);
        axial = Vector3.Dot(closest - origin, direction);
        if (axial < 0.0f || axial > reach)
        {
            return false;
        }

        float radius = GetConeRadius(axial);
        Vector3 radial = closest - (origin + direction * axial);
        return radial.sqrMagnitude <= radius * radius;
    }

    /// <summary>
    /// 착탄 범위 피해와 시각 효과의 중심입니다. 맞은 면에서 조금 띄웁니다.
    /// 총구가 콜라이더 안에서 시작해 거리 0으로 잡힌 경우는 총구 위치를 씁니다.
    /// </summary>
    private static Vector3 GetImpactPoint(Vector3 origin, Vector3 direction, RaycastHit impact)
    {
        if (impact.distance <= 0.0f)
        {
            return origin;
        }

        Vector3 normal = impact.normal.sqrMagnitude > 0.0f ? impact.normal : -direction;
        return impact.point + normal * ImpactSurfaceOffset;
    }

    /// <summary>총구로부터 <paramref name="distance"/>m에 해당하는 구간 피해를 줍니다. 같은 대상은 한 발에 한 번만 맞습니다.</summary>
    /// <remarks>
    /// 같은 대상이 원뿔과 착탄 범위에 모두 걸리면 먼저 처리되는 원뿔 쪽 거리로 피해가 정해집니다.
    /// 구간 피해가 0이면 피해와 화상을 모두 주지 않습니다.
    /// </remarks>
    private void TryDamage(Collider other, float distance)
    {
        IDamageable target = other != null ? other.GetComponentInParent<IDamageable>() : null;
        if (target == null || !m_damagedTargets.Add(target))
        {
            return;
        }

        int damage = ResolveDamage(distance);
        DamageInfo hit = new DamageInfo(damage, DamageCategory.Skill, nameof(CharacterSkillType.ChungSolDragonBreath), m_attacker);
        if (!CombatDamage.TryApplyDamage(target, m_ownerFaction, hit) || target.IsDead)
        {
            return;
        }

        if (!m_applyBurning)
        {
            return;
        }

        StatusEffectDefinitionSO burning = ResolveBurningEffect();
        if (burning != null)
        {
            StatusEffectContainer.GetOrAdd(target)?.Apply(burning, m_ownerFaction, m_attacker, hit);
        }
    }

    /// <summary>총구로부터의 거리에 해당하는 피해입니다. 구간 표가 비어 있으면 기본 피해입니다.</summary>
    public int ResolveDamage(float distance)
    {
        float damage = m_damageFalloff != null ? m_damageFalloff.ResolveDamage(distance, Damage) : Damage;
        return Mathf.Max(0, Mathf.RoundToInt(damage));
    }

    private StatusEffectDefinitionSO ResolveBurningEffect()
    {
        if (m_burningEffect == null)
        {
            m_burningEffect = Resources.Load<StatusEffectDefinitionSO>(BurningEffectResourcePath);
        }

        return m_burningEffect;
    }

    private static readonly Color DebugRangeColor = new Color(1.0f, 0.35f, 0.02f, 1.0f);

    /// <summary>이번 발사에 실제로 쓴 원뿔(벽에 잘린 길이)을 그립니다.</summary>
    private void DrawDebugCone(Vector3 origin, Vector3 direction, float reach, float duration)
    {
        GetPerpendicularAxes(direction, out Vector3 right, out Vector3 up);
        Vector3 end = origin + direction * reach;
        float startRadius = DamageStartRadius;
        float endRadius = GetConeRadius(reach);
        const int segments = 20;

        for (int i = 0; i < segments; i++)
        {
            float a0 = Mathf.PI * 2.0f * i / segments;
            float a1 = Mathf.PI * 2.0f * (i + 1) / segments;
            Vector3 dir0 = right * Mathf.Cos(a0) + up * Mathf.Sin(a0);
            Vector3 dir1 = right * Mathf.Cos(a1) + up * Mathf.Sin(a1);
            Debug.DrawLine(origin + dir0 * startRadius, origin + dir1 * startRadius, DebugRangeColor, duration, false);
            Debug.DrawLine(end + dir0 * endRadius, end + dir1 * endRadius, DebugRangeColor, duration, false);

            if (i % 5 == 0)
            {
                Debug.DrawLine(origin + dir0 * startRadius, end + dir0 * endRadius, DebugRangeColor, duration, false);
            }
        }
    }

    /// <summary>착탄 범위 피해 구를 세 축의 원으로 그립니다.</summary>
    private static void DrawDebugSphere(Vector3 center, float radius, float duration)
    {
        const int segments = 20;
        for (int i = 0; i < segments; i++)
        {
            float a0 = Mathf.PI * 2.0f * i / segments;
            float a1 = Mathf.PI * 2.0f * (i + 1) / segments;
            float c0 = Mathf.Cos(a0) * radius, s0 = Mathf.Sin(a0) * radius;
            float c1 = Mathf.Cos(a1) * radius, s1 = Mathf.Sin(a1) * radius;
            Debug.DrawLine(center + new Vector3(c0, s0, 0.0f), center + new Vector3(c1, s1, 0.0f), DebugRangeColor, duration, false);
            Debug.DrawLine(center + new Vector3(c0, 0.0f, s0), center + new Vector3(c1, 0.0f, s1), DebugRangeColor, duration, false);
            Debug.DrawLine(center + new Vector3(0.0f, c0, s0), center + new Vector3(0.0f, c1, s1), DebugRangeColor, duration, false);
        }
    }

    /// <summary>발사 방향에 수직인 두 축을 구합니다. 원뿔 단면 원을 그릴 때 씁니다.</summary>
    public static void GetPerpendicularAxes(Vector3 direction, out Vector3 right, out Vector3 up)
    {
        right = Vector3.Cross(direction, Vector3.up);
        if (right.sqrMagnitude < 0.0001f)
        {
            right = Vector3.Cross(direction, Vector3.forward);
        }

        right.Normalize();
        up = Vector3.Cross(right, direction).normalized;
    }

    /// <summary>
    /// 발사 위치와 방향에 시각 효과를 새로 만들고 총과 분리해 둡니다. 다 타면 스스로 지워집니다.
    /// </summary>
    /// <remarks>
    /// 총의 자식으로 두면 쏜 뒤 캐릭터가 돌거나 반동으로 총이 흔들릴 때 이미 나간 불길까지 같이 휘어집니다.
    /// 피해 판정은 발사 순간 한 번에 끝나므로, 불길이 휘면 판정 범위와 화면이 어긋납니다. 그래서 발사 순간의
    /// 자리에 고정합니다. 매 발 새로 만들어 연속 발사해도 앞 불길이 끊기지 않습니다.
    ///
    /// 반복 재생으로 만든 VFX(Fire 계열 모닥불)는 반복을 끄고 판정 시간 동안만 내보냅니다. 처음부터 한 번만
    /// 재생하도록 만든 VFX는 만든 그대로의 길이를 씁니다. 이미 나간 파티클은 수명을 다 채운 뒤 오브젝트와 함께 지워집니다.
    /// </remarks>
    private void SpawnDetachedVisual(GameObject prefab, Vector3 position, Quaternion rotation, float extraDelay, float scale = 1.0f)
    {
        GameObject visual = Instantiate(prefab, position, rotation);
        visual.name = prefab.name;

        ParticleSystem[] systems = visual.GetComponentsInChildren<ParticleSystem>(true);

        // 서브 이미터는 부모 파티클이 대신 내보냅니다. 직접 재생하면 생성 위치에서 따로 한 번 더 터집니다.
        HashSet<ParticleSystem> subEmitters = CollectSubEmitters(systems);

        if (!Mathf.Approximately(scale, 1.0f))
        {
            for (int i = 0; i < systems.Length; i++)
            {
                // 재생 중에는 바꿀 수 없는 값이 있어 먼저 멈춥니다.
                systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ScaleParticleSystem(systems[i], scale);
            }
        }

        float endTime = 0.0f;
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem system = systems[i];
            if (subEmitters.Contains(system))
            {
                continue;
            }

            // 재생 중에는 길이·반복·지연을 바꿀 수 없어 먼저 멈춥니다.
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = system.main;
            if (main.loop)
            {
                main.loop = false;
                main.duration = Lifetime;
            }

            if (extraDelay > 0.0f)
            {
                ParticleSystem.MinMaxCurve delay = main.startDelay;
                main.startDelay = delay.mode == ParticleSystemCurveMode.TwoConstants
                    ? new ParticleSystem.MinMaxCurve(delay.constantMin + extraDelay, delay.constantMax + extraDelay)
                    : new ParticleSystem.MinMaxCurve(GetCurveMax(delay) + extraDelay);
            }

            float systemEnd = GetCurveMax(main.startDelay) + main.duration + GetCurveMax(main.startLifetime);
            endTime = Mathf.Max(endTime, systemEnd + GetLongestSubEmitterLifetime(system));
        }

        for (int i = 0; i < systems.Length; i++)
        {
            if (!subEmitters.Contains(systems[i]))
            {
                systems[i].Play(false);
            }
        }

        Destroy(visual, endTime + VisualCleanupMargin);
    }

    /// <summary>
    /// <see cref="TraceBreath"/>가 찾은 착탄 지점에 착탄 스파크를 불길이 도착하는 시간에 맞춰 재생합니다. +Z가 맞은 면 바깥을 향합니다.
    /// </summary>
    private void SpawnImpactVisual(Vector3 origin, Vector3 direction, RaycastHit impact, Vector3 impactPoint)
    {
        // 총구와 겹쳐 시작한 콜라이더는 normal이 0이라 발사 반대 방향을 대신 씁니다.
        Vector3 normal = impact.normal.sqrMagnitude > 0.0f ? impact.normal : -direction;
        Vector3 up = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.99f ? -direction : Vector3.up;
        float delay = m_impactTravelSpeed > 0.0f ? impact.distance / m_impactTravelSpeed : 0.0f;

        SpawnDetachedVisual(m_impactEffectPrefab, impactPoint, Quaternion.LookRotation(normal, up), delay,
            GetImpactVisualScale(impact.distance));
    }

    /// <summary>착탄 거리에 따른 착탄 범위 피해 반경(m)입니다. 시각 효과와 같은 배율을 씁니다.</summary>
    /// <remarks>보이는 스파크 범위와 피해 범위가 어긋나지 않게 두 값을 같은 배율 하나로 묶습니다.</remarks>
    public float GetImpactDamageRadius(float impactDistance)
    {
        return ImpactDamageRadius * GetImpactVisualScale(impactDistance);
    }

    /// <summary>착탄 거리에 따른 착탄 시각 효과·범위 피해 배율입니다. 시작 거리까지 1, 판정 길이에서 <see cref="m_impactFarScale"/>입니다.</summary>
    public float GetImpactVisualScale(float distance)
    {
        float start = Mathf.Min(m_impactScaleStartDistance, DamageRange);
        float t = DamageRange > start ? Mathf.Clamp01((distance - start) / (DamageRange - start)) : 1.0f;
        return Mathf.Lerp(1.0f, Mathf.Max(0.1f, m_impactFarScale), t);
    }

    /// <summary>
    /// 파티클이 퍼지는 공간 전체를 <paramref name="scale"/>배로 키웁니다.
    /// </summary>
    /// <remarks>
    /// 속도와 중력을 같은 배율로 곱하면 같은 시간에 궤적의 모든 위치가 그 배율만큼 커집니다(x = v·t + ½·g·t²).
    /// 그래서 튀어 오르는 높이와 떨어지는 모양이 비율 그대로 유지됩니다. 크기·생성 반경·감속 상한·흔들림·조명 범위도 같이 곱합니다.
    /// 시간(수명·지연)은 바꾸지 않습니다. 오브젝트 스케일로 키우는 방법은 월드 공간 파티클의 속도와 중력에 적용되지 않아 쓰지 않습니다.
    /// </remarks>
    private static void ScaleParticleSystem(ParticleSystem system, float scale)
    {
        ParticleSystem.MainModule main = system.main;
        main.startSpeedMultiplier *= scale;
        main.gravityModifierMultiplier *= scale;
        if (main.startSize3D)
        {
            main.startSizeXMultiplier *= scale;
            main.startSizeYMultiplier *= scale;
            main.startSizeZMultiplier *= scale;
        }
        else
        {
            main.startSizeMultiplier *= scale;
        }

        ParticleSystem.ShapeModule shape = system.shape;
        if (shape.enabled)
        {
            shape.radius *= scale;
            shape.position *= scale;
            shape.scale *= scale;
        }

        ParticleSystem.LimitVelocityOverLifetimeModule limit = system.limitVelocityOverLifetime;
        if (limit.enabled)
        {
            limit.limitMultiplier *= scale;
        }

        ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
        if (velocity.enabled)
        {
            velocity.xMultiplier *= scale;
            velocity.yMultiplier *= scale;
            velocity.zMultiplier *= scale;
        }

        ParticleSystem.NoiseModule noise = system.noise;
        if (noise.enabled)
        {
            noise.strengthMultiplier *= scale;
        }

        ParticleSystem.LightsModule lights = system.lights;
        if (lights.enabled)
        {
            lights.rangeMultiplier *= scale;
        }
    }

    private static HashSet<ParticleSystem> CollectSubEmitters(ParticleSystem[] systems)
    {
        HashSet<ParticleSystem> result = new HashSet<ParticleSystem>();
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem.SubEmittersModule subModule = systems[i].subEmitters;
            if (!subModule.enabled)
            {
                continue;
            }

            for (int j = 0; j < subModule.subEmittersCount; j++)
            {
                ParticleSystem sub = subModule.GetSubEmitterSystem(j);
                if (sub != null)
                {
                    result.Add(sub);
                }
            }
        }

        return result;
    }

    /// <summary>부모 파티클이 마지막 순간에 서브 이미터를 터뜨려도 그 파티클이 다 탈 때까지 남겨 두기 위한 시간입니다.</summary>
    private static float GetLongestSubEmitterLifetime(ParticleSystem system)
    {
        ParticleSystem.SubEmittersModule subModule = system.subEmitters;
        if (!subModule.enabled)
        {
            return 0.0f;
        }

        float longest = 0.0f;
        for (int i = 0; i < subModule.subEmittersCount; i++)
        {
            ParticleSystem sub = subModule.GetSubEmitterSystem(i);
            if (sub != null)
            {
                ParticleSystem.MainModule subMain = sub.main;
                longest = Mathf.Max(longest, subMain.duration + GetCurveMax(subMain.startLifetime));
            }
        }

        return longest;
    }

    private static float GetCurveMax(ParticleSystem.MinMaxCurve curve)
    {
        switch (curve.mode)
        {
            case ParticleSystemCurveMode.Constant:
                return curve.constant;
            case ParticleSystemCurveMode.TwoConstants:
                return curve.constantMax;
            default:
                // 곡선 모드는 곡선 값이 0~1이고 실제 값은 배율이 곱해진 것이라 배율을 상한으로 봅니다.
                return curve.curveMultiplier;
        }
    }

    /// <summary>시각 효과 프리팹이 없을 때 쓸 주황색 원뿔형 임시 파티클을 런타임에 구성합니다.</summary>
    private void EnsureTemporaryParticles()
    {
        // 프리팹이 있으면 발사할 때마다 따로 만들어 분리하므로(SpawnDetachedVisual) 미리 만들어 둘 것이 없습니다.
        if (m_particles != null || m_visualEffectPrefab != null)
        {
            return;
        }

        Transform existing = transform.Find("TemporaryFlameParticles");
        bool createdParticleObject = existing == null;
        GameObject particleObject = existing != null ? existing.gameObject : new GameObject("TemporaryFlameParticles");
        particleObject.transform.SetParent(transform, false);
        m_particles = particleObject.GetComponent<ParticleSystem>();
        if (m_particles == null)
        {
            m_particles = particleObject.AddComponent<ParticleSystem>();
        }

        m_particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = m_particles.main;
        main.duration = Lifetime;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.10f, 0.22f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(12.0f, 22.0f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.5f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1.0f, 0.18f, 0.01f, 0.95f),
            new Color(1.0f, 0.82f, 0.08f, 0.95f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 96;
        main.playOnAwake = false;

        ParticleSystem.EmissionModule emission = m_particles.emission;
        emission.rateOverTime = 0.0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0.0f, 64) });

        ParticleSystem.ShapeModule shape = m_particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12.0f;
        shape.radius = 0.12f;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = m_particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1.0f, 0.9f, 0.2f), 0.0f),
                new GradientColorKey(new Color(1.0f, 0.08f, 0.01f), 1.0f),
            },
            new[]
            {
                new GradientAlphaKey(1.0f, 0.0f),
                new GradientAlphaKey(0.0f, 1.0f),
            });
        colorOverLifetime.color = gradient;

        ParticleSystemRenderer particleRenderer = m_particles.GetComponent<ParticleSystemRenderer>();
        if (particleRenderer != null && createdParticleObject)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Particles/Standard Unlit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader != null)
            {
                m_runtimeParticleMaterial = new Material(shader)
                {
                    name = "DragonBreathTemporaryMaterial",
                };
                particleRenderer.sharedMaterial = m_runtimeParticleMaterial;
            }
        }
    }
}
