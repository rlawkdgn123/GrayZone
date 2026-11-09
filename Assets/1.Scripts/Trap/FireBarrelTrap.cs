using UnityEngine;
using UnityEngine.AI;
using VInspector;

/// <summary>
/// 화염 드럼통입니다. 총에 맞거나 주변 폭발·화염에 휘말리면 터지면서 그 자리에 화염 지대를 남깁니다.
/// </summary>
/// <remarks>
/// <b>밟아서는 터지지 않습니다.</b> 적이 지나가도 아무 일도 없고, 플레이어가 쏘거나 연쇄 폭발에 휘말릴 때만 터집니다.
/// 그래서 트리거 이벤트(<c>OnTriggerEnter</c>)를 쓰지 않습니다. 루트의 트리거 콜라이더는 설치 상호작용 감지용입니다.
///
/// <b>사격 판정</b>: 플레이어 사격은 Trap 레이어를 보지 않습니다. 그래서 탄을 받을 콜라이더(<c>m_shotColliders</c>)는
/// 트리거가 아닌 콜라이더를 Trap이 아닌 레이어(Default 등)에 둡니다. 탄이 여기서 멈추면 총이
/// <see cref="IShotReactive"/>로 알려 줍니다. 청사진 상태에서는 이 콜라이더를 꺼서 탄과 이동을 막지 않게 합니다.
///
/// <b>터진다는 것</b>: 오브젝트를 없애지 않습니다. 다른 함정과 같이 <see cref="Trap.Deplete"/>로 청사진으로 돌아가고,
/// 언제 다시 설치할 수 있는지는 재설치 정책이 정합니다. 내구도는 드럼통의 체력입니다. 기본은 한 발당 1 고정이라
/// "몇 발 맞으면 터지는지"가 되고, 토글로 총기 피해량을 쓸 수도 있습니다. 연쇄에 휘말리면 체력과 관계없이 터집니다.
///
/// 화염은 화염병과 같은 화염 지대 프리팹(<see cref="FireTrap"/>)을 생성하고 지속 시간만 이 함정의 값으로 바꿉니다.
/// 화염 지대도 범위 안의 다른 폭발물과 드럼통에 불을 옮기지만 반경이 작아서, 터질 때 연쇄 반경(<c>m_chainRadius</c>) 안의
/// 폭발물도 직접 터뜨립니다.
/// </remarks>
public sealed class FireBarrelTrap : Trap, IChainDetonatable, IShotReactive
{
    private const string BurstSoundEventPath = "event:/World/Trap/Activate/FireBarrel";
    private const string FireLoopEventPath = "event:/World/Trap/Active/FireBarrel";
    private static bool s_loggedMissingBurstSound;

    /// <inheritdoc />
    protected override string DefaultDisplayName => "화염 드럼통";

    /// <inheritdoc />
    /// <remarks>사격 피해 고정 문구는 실제 규칙과 맞을 때(총기 피해량을 쓰지 않을 때)만 붙입니다.</remarks>
    protected override string DefaultDescription => m_useWeaponDamage
        ? "쏘거나 폭발에 휘말리면 불길을 남깁니다."
        : "쏘거나 폭발에 휘말리면 불길을 남깁니다.\n받는 사격 피해는 1로 고정됩니다.";

    /// <inheritdoc />
    protected override TrapKind CostKind => TrapKind.FireBarrel;

    /// <inheritdoc />
    /// <remarks>
    /// 드럼통은 설치 뒤에는 안내가 뜨지 않으므로, 청사진에서도 터지기까지 맞아야 하는 내구도(최대치)를 보여 줍니다.
    /// </remarks>
    protected override string HudNameSuffix => DurabilityLabel(IsBuilt ? Health : MaxHealth);

    [Header("Fire Barrel")]
    [Tooltip("터질 때 남길 화염 지대 프리팹입니다. 화염병이 쓰는 FireBoom_Trap을 지정합니다.")]
    [SerializeField] private FireTrap m_firePrefab;

    [Tooltip("남길 화염 지대의 지속 시간(초)입니다. 화염 프리팹의 값 대신 이 값을 씁니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_fireDuration = 5.0f;

    [Tooltip("화염 지대를 놓을 바닥을 찾을 레이어입니다. 드럼통 아래로 내려 쏴서 찾고, 못 찾으면 드럼통 위치에 놓습니다.")]
    [SerializeField] private LayerMask m_groundLayers = 1 << 3;

    [Tooltip("끄면(기본) 총기와 관계없이 한 발당 체력 1을 깎습니다. 이때 체력(Max Health)은 터지기까지 맞아야 하는 발 수입니다. " +
             "켜면 총기 피해량(거리·관통 감쇠 적용)을 그대로 깎으므로 체력을 그에 맞게 크게 잡아야 합니다.")]
    [SerializeField] private bool m_useWeaponDamage = false;

    [Tooltip("탄을 받을 콜라이더입니다. 트리거가 아니어야 하고 Trap 레이어가 아니어야 합니다. 설치된 동안에만 켜집니다.")]
    [SerializeField] private Collider[] m_shotColliders;

    [Tooltip("설치된 동안만 켜서 길을 막는 NavMesh 장애물입니다. 드럼통은 NavMesh 굽기에서 빠져 있어, 청사진일 때는 AI가 그 자리를 지나갑니다.")]
    [SerializeField] private NavMeshObstacle m_navObstacle;

    [Header("Explosion Effect")]
    [Tooltip("터지는 순간 생성할 이펙트 프리팹입니다. 비워 두면 이펙트 없이 화염만 남깁니다.")]
    [SerializeField] private GameObject m_explosionEffectPrefab;

    [Tooltip("이펙트를 드럼통 위치에서 얼마나 띄울지입니다. 드럼통 기준 로컬이 아니라 월드 축입니다.")]
    [SerializeField] private Vector3 m_explosionEffectOffset = new Vector3(0.0f, 0.5f, 0.0f);

    [Tooltip("생성한 이펙트의 크기 배율입니다. 수류탄 이펙트(Explosion1)를 드럼통 크기로 줄여 쓰려고 둡니다. " +
             "파티클의 Scaling Mode가 Hierarchy여야 자식 파티클까지 함께 줄어듭니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_explosionEffectScale = 0.3f;

    [Tooltip("생성한 이펙트를 지울 때까지의 시간(초)입니다. 0이면 지우지 않습니다(이펙트가 스스로 정리할 때).")]
    [Min(0.0f)]
    [SerializeField] private float m_explosionEffectLifetime = 3.0f;

    [Header("Chain Detonation")]
    [Tooltip("켜면 다른 폭발(수류탄, 지뢰 등)이나 화염(화염병, 용숨결 등)의 범위 안에 들었을 때 함께 터집니다. 청사진 상태에서는 반응하지 않습니다.")]
    [SerializeField] private bool m_chainDetonationEnabled = false;

    [Tooltip("휘말린 뒤 실제로 터지기까지의 시간(초)입니다. 0이면 그 자리에서 터집니다. 조금 두면 연쇄가 차례로 터지는 것이 눈에 보입니다.")]
    [ShowIf(nameof(m_chainDetonationEnabled))]
    [Min(0.0f)]
    [SerializeField] private float m_chainFuseTime = 0.15f;

    [Tooltip("터질 때 이 반경(m) 안의 다른 드럼통과 폭발 함정을 함께 터뜨립니다. 남기는 화염 지대(반경 약 1.2m)보다 멀리 떨어진 드럼통도 이어지게 합니다. 0이면 화염 지대로만 번집니다.")]
    [ShowIf(nameof(m_chainDetonationEnabled))]
    [Min(0.0f)]
    [SerializeField] private float m_chainRadius = 4.0f;
    [EndIf]

    /// <summary>
    /// 켜면 설치된 드럼통마다 연쇄 반경을 바닥 원으로 그립니다(디버그 트레이너 토글).
    /// </summary>
    /// <remarks>Debug.DrawLine 기반이라 Scene 뷰와 Gizmos를 켠 Game 뷰에서 보입니다. 드럼통이 여럿이라 전역 값으로 둡니다.</remarks>
    public static bool DebugShowChainRadius { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetDebugStatics()
    {
        DebugShowChainRadius = false;
    }

    /// <summary>터질 때 함께 터뜨리는 반경(m)입니다.</summary>
    public float ChainRadius => m_chainRadius;

    /// <summary>연쇄 반경을 런타임에 바꿉니다. 디버그 트레이너가 씁니다. 저장되지 않습니다.</summary>
    public void DebugSetChainRadius(float radius)
    {
        m_chainRadius = Mathf.Max(0.0f, radius);
    }

    /// <summary>바닥을 찾을 때 드럼통 위치에서 위로 물러나 쏘는 거리입니다.</summary>
    private const float GroundProbeUp = 0.5f;

    /// <summary>바닥을 찾을 최대 거리입니다.</summary>
    private const float GroundProbeDistance = 3.0f;

    /// <summary>연쇄로 휘말려 터지기를 기다리는 중인지 여부입니다.</summary>
    private bool m_isFuseBurning;

    /// <summary>터지기까지 남은 시간(초)입니다.</summary>
    private float m_fuseTimer;

    /// <summary>Trap 업그레이드로 정해진 화염 범위 배율입니다. 업그레이드가 없으면 1입니다.</summary>
    private float m_upgradeFireRangeMultiplier = 1.0f;

    /// <summary>Trap 업그레이드로 화염 지속 시간에 더할 초입니다.</summary>
    private float m_upgradeFireBonusDuration;

    /// <summary>Trap 업그레이드로 적에게만 틱마다 더할 화염 피해입니다.</summary>
    private float m_upgradeEnemyBonusDamagePerTick;

    /// <inheritdoc />
    /// <remarks>
    /// Trap 업그레이드 레벨을 읽어, 남길 화염의 범위·지속 시간과 적 대상 틱당 추가 피해를 정합니다.
    /// 드럼통 자체(체력, 연쇄 반경)는 바꾸지 않습니다. 레벨 0이면 기본값으로 돌아갑니다.
    /// </remarks>
    protected override void OnApplyUpgrade(TrapUpgradeTableSO table, DefenseSceneDataManager data)
    {
        m_upgradeFireRangeMultiplier = 1.0f;
        m_upgradeFireBonusDuration = 0.0f;
        m_upgradeEnemyBonusDamagePerTick = 0.0f;
        if (table == null
            || data == null
            || !table.TryGetFireBarrel(data.GetUpgradeLevel(ScrambleUpgradeType.Trap), out TrapUpgradeTableSO.FireBarrelLevel level))
        {
            return;
        }

        m_upgradeFireRangeMultiplier = Mathf.Max(0.01f, level.RangeMultiplier);
        m_upgradeFireBonusDuration = Mathf.Max(0.0f, level.BonusDuration);
        m_upgradeEnemyBonusDamagePerTick = Mathf.Max(0.0f, level.EnemyBonusDamagePerTick);
    }

    /// <inheritdoc />
    /// <remarks>기즈모 색이 바뀌는 시점을 "휘말려 곧 터질 때"로 둡니다.</remarks>
    protected override bool HasTargetInRange => m_isFuseBurning;

    /// <summary>스크립트를 처음 붙일 때 드럼통에 맞는 기본값을 잡아 둡니다.</summary>
    /// <remarks>세 발에 터지도록 체력 3, 피해 방식은 의미가 없어 Once로 둡니다.</remarks>
    private void Reset()
    {
        Collider trigger = GetComponent<Collider>();
        if (trigger != null)
        {
            trigger.isTrigger = true;
        }

        m_maxHealth = 3;
        m_damageMode = TrapDamageMode.Once;
        m_rebuildPolicy = TrapRebuildPolicy.OnRest;
    }

    protected override void Awake()
    {
        base.Awake();
        ApplyShotColliderState();
    }

    /// <inheritdoc />
    protected override void OnBuilt()
    {
        ApplyShotColliderState();
    }

    /// <inheritdoc />
    /// <remarks>
    /// 기본은 한 발당 1 고정이고, <c>m_useWeaponDamage</c>를 켜면 총기 피해량을 씁니다(최소 1).
    /// 체력이 다 깎이면 <see cref="OnDepleted"/>에서 터집니다.
    /// </remarks>
    public void OnShotHit(Vector3 hitPoint, int damage, GameObject attacker)
    {
        if (IsBlueprint)
        {
            return;
        }

        TakeDurabilityDamage(m_useWeaponDamage ? Mathf.Max(1, damage) : 1);
    }

    /// <inheritdoc />
    /// <remarks>이미 휘말려 타는 중이면 남은 시간과 이 값 중 짧은 쪽을 씁니다.</remarks>
    public bool TryChainDetonate(GameObject source)
    {
        if (!m_chainDetonationEnabled || IsBlueprint || IsDepleted)
        {
            return false;
        }

        if (m_chainFuseTime <= 0.0f)
        {
            Burst();
            return true;
        }

        m_fuseTimer = m_isFuseBurning ? Mathf.Min(m_fuseTimer, m_chainFuseTime) : m_chainFuseTime;
        m_isFuseBurning = true;
        return true;
    }

    private void Update()
    {
        DrawChainRadiusDebug();

        if (!m_isFuseBurning)
        {
            return;
        }

        m_fuseTimer -= Time.deltaTime;
        if (m_fuseTimer <= 0.0f)
        {
            Burst();
        }
    }

    /// <summary>
    /// 내구도와 관계없이 지금 터뜨립니다.
    /// </summary>
    /// <remarks>
    /// 실제 효과(화염·이펙트)는 <see cref="OnDepleted"/>가 냅니다. 사격으로 내구도가 다 닳는 경로와 같은 곳에서
    /// 터지게 해 두 경로의 결과가 어긋나지 않게 합니다.
    /// </remarks>
    public void Burst()
    {
        m_isFuseBurning = false;
        Deplete();
    }

    /// <inheritdoc />
    /// <remarks>이 시점에 이미 청사진 상태라, 생성한 화염이 이 드럼통을 다시 터뜨리지 않습니다.</remarks>
    protected override void OnDepleted()
    {
        m_isFuseBurning = false;
        ApplyShotColliderState();
        PlayBurstSound();
        SpawnExplosionEffect();
        SpawnFire();
        TriggerNearbyChain();
    }

    /// <summary>드럼통이 터지는 지점에서 폭발음을 한 번 재생합니다.</summary>
    private void PlayBurstSound()
    {
        if (!FMODUnity.RuntimeManager.IsInitialized)
        {
            return;
        }

        try
        {
            FMODUnity.RuntimeManager.PlayOneShot(
                BurstSoundEventPath,
                ResolveBarrelBase() + Vector3.up * 0.5f);
        }
        catch (FMODUnity.EventNotFoundException exception)
        {
            if (!s_loggedMissingBurstSound)
            {
                s_loggedMissingBurstSound = true;
                Debug.LogWarning($"[FireBarrelTrap] FMOD 이벤트를 찾지 못했습니다: {BurstSoundEventPath}\n{exception.Message}", this);
            }
        }
    }

    /// <summary>설치된 드럼통의 연쇄 반경을 바닥 높이의 원으로 한 프레임 그립니다.</summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void DrawChainRadiusDebug()
    {
        if (!DebugShowChainRadius || !IsBuilt || !m_chainDetonationEnabled || m_chainRadius <= 0.0f)
        {
            return;
        }

        const int segments = 32;
        Vector3 center = ResolveBarrelBase() + Vector3.up * 0.1f;
        Color color = new Color(1.0f, 0.5f, 0.1f);
        Vector3 previous = center + new Vector3(m_chainRadius, 0.0f, 0.0f);
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2.0f / segments;
            Vector3 next = center + new Vector3(Mathf.Cos(angle) * m_chainRadius, 0.0f, Mathf.Sin(angle) * m_chainRadius);
            Debug.DrawLine(previous, next, color, 0.0f, false);
            previous = next;
        }
    }

    /// <summary>연쇄 반경 안의 다른 폭발물에 불을 옮깁니다.</summary>
    /// <remarks>
    /// 이 시점에 자신은 이미 청사진이라 다시 휘말리지 않습니다. 받는 쪽은 자기 도화선 시간만큼 뒤에 터지므로
    /// 멀리 늘어선 드럼통도 차례로 터지는 것이 보입니다. 연쇄를 끈 드럼통은 남에게도 옮기지 않습니다.
    /// </remarks>
    private void TriggerNearbyChain()
    {
        if (!m_chainDetonationEnabled || m_chainRadius <= 0.0f)
        {
            return;
        }

        Vector3 center = ResolveBarrelBase() + Vector3.up * 0.5f;
        float radius = m_chainRadius;
        ExplosionDamage.TriggerChainDetonation(
            center,
            Vector3.one * radius,
            Quaternion.identity,
            candidate => candidate.bounds.SqrDistance(center) <= radius * radius,
            gameObject);
    }

    /// <inheritdoc />
    protected override void OnRearmed()
    {
        m_isFuseBurning = false;
        m_fuseTimer = 0.0f;
        ApplyShotColliderState();
    }

    /// <summary>설치된 동안에만 탄을 받는 콜라이더와 NavMesh 장애물을 켭니다. 청사진이 탄과 이동을 막으면 안 됩니다.</summary>
    private void ApplyShotColliderState()
    {
        if (m_navObstacle != null)
        {
            m_navObstacle.enabled = IsBuilt;
        }

        if (m_shotColliders == null)
        {
            return;
        }

        for (int i = 0; i < m_shotColliders.Length; i++)
        {
            if (m_shotColliders[i] != null)
            {
                m_shotColliders[i].enabled = IsBuilt;
            }
        }
    }

    /// <summary>터지는 순간의 이펙트를 생성합니다. 지정하지 않았으면 아무것도 하지 않습니다.</summary>
    private void SpawnExplosionEffect()
    {
        if (m_explosionEffectPrefab == null)
        {
            return;
        }

        GameObject effect = Instantiate(
            m_explosionEffectPrefab,
            ResolveBarrelBase() + m_explosionEffectOffset,
            Quaternion.identity);
        effect.transform.localScale *= m_explosionEffectScale;

        if (m_explosionEffectLifetime > 0.0f)
        {
            Destroy(effect, m_explosionEffectLifetime);
        }
    }

    /// <summary>드럼통 아래 바닥에 화염 지대를 생성하고 점화합니다.</summary>
    /// <remarks>
    /// 화염 지대는 스스로 지속 시간이 끝나면 사라지므로 여기서 지우지 않습니다.
    /// 화염 프리팹이 처음부터 설치된 상태면 <see cref="Trap.Build"/>는 아무 일도 하지 않고, 점화는 화염의 Start가 합니다.
    /// </remarks>
    private void SpawnFire()
    {
        if (m_firePrefab == null)
        {
            Debug.LogWarning($"[FireBarrelTrap] '{name}': 화염 프리팹이 지정되지 않아 불을 남기지 않습니다.", this);
            return;
        }

        FireTrap fire = Instantiate(m_firePrefab, ResolveGroundPoint(), Quaternion.identity);
        fire.SetDuration(m_fireDuration + m_upgradeFireBonusDuration);
        fire.SetRangeMultiplier(m_upgradeFireRangeMultiplier);
        fire.SetEnemyBonusDamagePerTick(m_upgradeEnemyBonusDamagePerTick);
        fire.Build();
        fire.StartLoopingSound(FireLoopEventPath);
    }

    /// <summary>드럼통 아래의 바닥 지점을 찾습니다. 못 찾으면 드럼통 밑면 중심을 씁니다.</summary>
    private Vector3 ResolveGroundPoint()
    {
        Vector3 barrelBase = ResolveBarrelBase();
        Vector3 origin = barrelBase + Vector3.up * GroundProbeUp;
        return Physics.Raycast(
                origin,
                Vector3.down,
                out RaycastHit hit,
                GroundProbeDistance,
                m_groundLayers,
                QueryTriggerInteraction.Ignore)
            ? hit.point
            : barrelBase;
    }

    /// <summary>드럼통 밑면 중심의 월드 좌표입니다.</summary>
    /// <remarks>
    /// 피벗(<c>transform.position</c>)을 쓰지 않는 이유는 MetalBarrel 메시가 피벗에서 수 m 떨어져 있기 때문입니다.
    /// 메시를 감싸는 루트 트리거 콜라이더의 경계를 기준으로 삼습니다. 콜라이더가 없으면 피벗을 씁니다.
    /// </remarks>
    private Vector3 ResolveBarrelBase()
    {
        Collider trigger = GetComponent<Collider>();
        if (trigger == null)
        {
            return transform.position;
        }

        Bounds bounds = trigger.bounds;
        return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
    }
}
