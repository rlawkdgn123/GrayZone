using UnityEngine;
using VInspector;

/// <summary>
/// 밟히면 잠시 뒤 한 번 터지고 끝나는 설치형 폭발물의 공통 기반입니다.
/// </summary>
/// <remarks>
/// 트리거 콜라이더가 필요합니다. 이 콜라이더는 <b>밟는 판정</b>이고, 실제 피해 범위는 파생이 정합니다.
/// 둘을 나눈 이유는 밟는 판정은 좁아야 하고 터지는 범위는 그보다 넓어야 하기 때문입니다.
///
/// 폭발 자체는 <see cref="ExplosionDamage"/>가 합니다. 수류탄(<see cref="ExplosiveProjectile"/>)이 쓰는
/// 것과 같은 처리이고, 이 클래스는 언제 터질지와 터진 뒤 자신을 어떻게 할지만 맡습니다.
///
/// <see cref="Trap.DamageMode"/>는 보지 않습니다. 한 번 터지고 끝이라 Tick이 성립하지 않습니다.
/// <c>m_damage</c>는 폭발 한 번의 피해량으로 씁니다.
/// </remarks>
[RequireComponent(typeof(Collider))]
public abstract class ExplosiveTrap : Trap, IChainDetonatable
{
    [Header("Explosive")]
    [Tooltip("이 폭발물을 밟은 것으로 볼 레이어입니다. 기본은 Enemy만 봅니다.")]
    [SerializeField] protected LayerMask m_triggerLayers = 1 << 9;

    [Tooltip("밟힌 뒤 실제로 터지기까지의 시간(초)입니다. 0이면 밟는 즉시 터집니다.")]
    [Min(0.0f)]
    [SerializeField] protected float m_fuseTime = 0.3f;

    [Tooltip("폭발 피해를 받을 레이어입니다. 밟는 레이어와 따로 두어, 적만 밟게 하면서 피해 범위는 더 넓게 잡을 수 있습니다.")]
    [SerializeField] protected LayerMask m_damageTargetLayers;

    [Tooltip("켜면 터지는 순간 실제 피해 범위를 반투명하게 잠깐 띄웁니다. 수치 확인용입니다.")]
    [SerializeField] protected bool m_showExplosionRangeVisual = true;

    [Header("Chain Detonation")]
    [Tooltip("켜면 다른 폭발(수류탄, 다른 함정)이나 화염(용숨결 등)의 범위 안에 들었을 때 함께 터집니다. 청사진 상태에서는 반응하지 않습니다.")]
    [SerializeField] private bool m_chainDetonationEnabled = false;

    [Tooltip("휘말린 뒤 실제로 터지기까지의 시간(초)입니다. 0이면 그 자리에서 터집니다. 조금 두면 연쇄가 차례로 터지는 것이 눈에 보입니다. 이미 밟혀 신관이 타는 중이면 남은 시간과 이 값 중 짧은 쪽을 씁니다.")]
    [ShowIf(nameof(m_chainDetonationEnabled))]
    [Min(0.0f)]
    [SerializeField] private float m_chainFuseTime = 0.15f;
    [EndIf]

    [Header("Explosive Debug")]
    [Tooltip("켜면 터지기 전에도 피해 범위를 계속 반투명하게 띄웁니다. Scene View 기즈모와 달리 게임 화면에서도 보입니다. 확인용이므로 빌드에 넣을 때는 꺼 두세요.")]
    [SerializeField] private bool m_alwaysShowExplosionRange = false;

    /// <summary>실행 중에 계속 띄워 두는 범위 표시 오브젝트입니다. 꺼져 있으면 <c>null</c>입니다.</summary>
    private GameObject m_rangePreview;

    /// <summary>범위 표시를 다시 만들어야 하는지 여부입니다.</summary>
    /// <remarks>
    /// 인스펙터에서 수치를 만지면 표시도 따라와야 합니다. 그런데 <c>OnValidate</c>에서 오브젝트를
    /// 만들거나 지우면 Unity가 경고를 냅니다. 그래서 여기서는 표시만 해 두고 실제 작업은
    /// 다음 <c>Update</c>에서 합니다.
    /// </remarks>
    private bool m_rangePreviewDirty = true;

    /// <summary>신관이 타는 중인지 여부입니다.</summary>
    private bool m_isFuseBurning;

    /// <summary>폭발까지 남은 시간(초)입니다.</summary>
    private float m_fuseTimer;

    /// <summary>이미 터졌는지 여부입니다. 한 번 터진 폭발물은 다시 터지지 않습니다.</summary>
    public bool HasExploded { get; private set; }

    /// <summary>밟혀서 신관이 타고 있는 중인지 여부입니다.</summary>
    public bool IsFuseBurning => m_isFuseBurning;

    /// <inheritdoc />
    /// <remarks>기즈모 색이 바뀌는 시점을 "밟힌 순간"으로 둡니다.</remarks>
    protected override bool HasTargetInRange => m_isFuseBurning;

    /// <inheritdoc />
    /// <remarks>지뢰·클레이모어는 한 번 터지면 끝이라 내구도 대신 일회성임을 표시합니다.</remarks>
    protected override string HudNameSuffix => "(일회성)";

    /// <summary>
    /// 파생이 자기 모양대로 폭발 피해를 적용합니다.
    /// </summary>
    /// <returns>실제로 피해를 준 대상의 수입니다.</returns>
    protected abstract int ApplyExplosionDamage();

    /// <summary>
    /// 파생이 자기 모양대로 범위 표시 오브젝트를 만듭니다. 만들 수 없으면 <c>null</c>을 돌려줍니다.
    /// </summary>
    /// <remarks>
    /// 터질 때 잠깐 뜨는 표시와 같은 모양을 써야 합니다. 둘이 다르면 확인용 표시가 거짓말을 합니다.
    /// </remarks>
    protected abstract GameObject CreateExplosionRangePreview();

    /// <summary>
    /// 스크립트를 처음 붙일 때 폭발물에 맞는 기본값을 잡아 둡니다.
    /// </summary>
    /// <remarks>
    /// 피해 방식을 Once로 고정하는 이유는 폭발이 한 번으로 끝나기 때문입니다. Tick으로 두면
    /// 인스펙터에 아무 일도 하지 않는 틱 간격이 노출됩니다.
    /// </remarks>
    protected virtual void Reset()
    {
        Collider trigger = GetComponent<Collider>();
        if (trigger != null)
        {
            trigger.isTrigger = true;
        }

        m_damageMode = TrapDamageMode.Once;
        m_damageTargetLayers = LayerMask.GetMask("Enemy", "EnemyHitbox");
    }

    protected override void Awake()
    {
        base.Awake();

        if (m_damageTargetLayers.value == 0)
        {
            m_damageTargetLayers = LayerMask.GetMask("Enemy", "EnemyHitbox");
        }
    }

    /// <summary>
    /// Explosive 업그레이드로 폭발 범위에 곱할 배율입니다. 1이면 프리팹 값 그대로입니다.
    /// </summary>
    /// <remarks>파생 함정은 피해 판정, 범위 미리보기, 기즈모에 모두 이 배율을 곱한 값을 씁니다.</remarks>
    protected float RangeMultiplier { get; private set; } = 1.0f;

    /// <inheritdoc />
    protected override void OnApplyUpgrade(TrapUpgradeTableSO table, DefenseSceneDataManager data)
    {
        RangeMultiplier = 1.0f;

        if (table != null
            && data != null
            && table.TryGetExplosive(data.GetUpgradeLevel(ScrambleUpgradeType.Explosive), out TrapUpgradeTableSO.ExplosiveLevel level))
        {
            RangeMultiplier = Mathf.Max(0.0f, level.RangeMultiplier);
        }
    }

    /// <summary>
    /// 트리거 콜라이더가 하나도 없으면 인스펙터에서 경고합니다.
    /// </summary>
    /// <remarks>
    /// <c>Reset</c>은 스크립트를 처음 붙일 때만 돌아서, 콜라이더를 나중에 따로 추가하면 <c>isTrigger</c>가
    /// 꺼진 채 남습니다. 그러면 감지가 조용히 동작하지 않아 원인을 찾기 어렵습니다.
    /// </remarks>
    protected virtual void OnValidate()
    {
        m_rangePreviewDirty = true;

        foreach (Collider collider in GetComponents<Collider>())
        {
            if (collider != null && collider.isTrigger)
            {
                return;
            }
        }

        Debug.LogWarning(
            $"[{GetType().Name}] '{name}': 트리거 콜라이더가 없습니다. Collider의 Is Trigger를 켜야 감지가 동작합니다.",
            this);
    }

    /// <summary>
    /// 설치가 끝나는 순간 이미 밟고 서 있던 대상을 잡습니다.
    /// </summary>
    /// <remarks>
    /// Unity는 콜라이더가 이미 겹쳐 있는 상태에서 조건이 바뀌었다고 <c>OnTriggerEnter</c>를 다시 보내지
    /// 않습니다. 이 처리가 없으면 적 발밑에 설치한 폭발물이 그 적이 한 번 나갔다 들어올 때까지
    /// 아무 일도 하지 않아 고장 난 것처럼 보입니다. <c>WireTrap</c>이 쓰는 방식과 같습니다.
    /// </remarks>
    protected override void OnBuilt()
    {
        Collider trigger = GetComponent<Collider>();
        if (trigger == null)
        {
            return;
        }

        Collider[] hits = TrapOverlapQuery.FindOverlaps(
            trigger,
            m_triggerLayers,
            QueryTriggerInteraction.Ignore);

        if (hits.Length > 0)
        {
            StartFuse();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // 청사진 상태에서는 반응하지 않습니다. 콜라이더는 끄지 않습니다.
        // InteractionController가 이 콜라이더로 상호작용 대상을 찾기 때문에, 끄면 설치할 수가 없습니다.
        if (IsBlueprint || HasExploded || m_isFuseBurning || !IsTrigger(other))
        {
            return;
        }

        StartFuse();
    }

    private void Update()
    {
        UpdateRangePreview();

        if (!m_isFuseBurning)
        {
            return;
        }

        m_fuseTimer -= Time.deltaTime;
        if (m_fuseTimer <= 0.0f)
        {
            Explode();
        }
    }

    /// <summary>
    /// 범위 표시를 켜짐/꺼짐 상태와 현재 수치에 맞춥니다.
    /// </summary>
    /// <remarks>
    /// 위치와 회전은 만들 때 한 번만 잡습니다. 설치된 폭발물은 움직이지 않는다고 보기 때문입니다.
    /// 실행 중에 옮기면 표시가 따라오지 않으므로, 그때는 인스펙터에서 껐다 켜면 다시 잡힙니다.
    /// </remarks>
    private void UpdateRangePreview()
    {
        if (!m_alwaysShowExplosionRange || HasExploded)
        {
            DestroyRangePreview();
            return;
        }

        if (!m_rangePreviewDirty && m_rangePreview != null)
        {
            return;
        }

        DestroyRangePreview();
        m_rangePreview = CreateExplosionRangePreview();
        m_rangePreviewDirty = false;
    }

    /// <summary>띄워 둔 범위 표시를 지웁니다.</summary>
    private void DestroyRangePreview()
    {
        if (m_rangePreview == null)
        {
            return;
        }

        ExplosionDamage.DestroyRangeVisual(m_rangePreview);
        m_rangePreview = null;
    }

    /// <summary>꺼지거나 사라질 때 띄워 둔 범위 표시도 함께 치웁니다.</summary>
    /// <remarks>
    /// 표시 오브젝트는 이 폭발물의 자식이 아니라서, 여기서 지우지 않으면 씬에 그대로 남습니다.
    /// </remarks>
    protected override void OnDisable()
    {
        base.OnDisable();
        DestroyRangePreview();
    }

    /// <summary>신관에 불을 붙입니다. 신관 시간이 0이면 그 자리에서 바로 터집니다.</summary>
    private void StartFuse()
    {
        if (HasExploded || m_isFuseBurning)
        {
            return;
        }

        if (m_fuseTime <= 0.0f)
        {
            Explode();
            return;
        }

        m_isFuseBurning = true;
        m_fuseTimer = m_fuseTime;
    }

    /// <inheritdoc />
    /// <remarks>
    /// 토글이 꺼져 있거나, 청사진 상태이거나, 이미 터졌으면 반응하지 않습니다. 한 번 터진 함정은
    /// <see cref="HasExploded"/>가 먼저 켜지므로 두 함정이 서로를 부르며 되풀이되지 않습니다.
    /// </remarks>
    public bool TryChainDetonate(GameObject source)
    {
        if (!m_chainDetonationEnabled || IsBlueprint || HasExploded)
        {
            return false;
        }

        if (m_chainFuseTime <= 0.0f)
        {
            Explode();
            return true;
        }

        // 이미 밟혀 타는 중이면 더 늦추지 않습니다. 휘말렸는데 오히려 늦게 터지면 어색합니다.
        m_fuseTimer = m_isFuseBurning ? Mathf.Min(m_fuseTimer, m_chainFuseTime) : m_chainFuseTime;
        m_isFuseBurning = true;
        return true;
    }

    /// <summary>
    /// 신관을 기다리지 않고 지금 터뜨립니다.
    /// </summary>
    /// <remarks>
    /// 밟는 것 말고 다른 이유로 터져야 할 때(총에 맞거나 다른 폭발에 유폭되는 등) 쓰는 진입점입니다.
    /// 이미 터졌으면 아무 일도 하지 않습니다.
    /// </remarks>
    public void Explode()
    {
        if (HasExploded)
        {
            return;
        }

        HasExploded = true;
        m_isFuseBurning = false;

        // 터지는 순간의 표시와 겹치지 않도록 계속 띄워 두던 것부터 치웁니다.
        DestroyRangePreview();

        ApplyExplosionDamage();

        // 오브젝트를 없애지 않고 청사진으로 되돌립니다. 언제 다시 설치할 수 있는지는 재설치 정책이
        // 정합니다. 없애 버리면 정책이 무엇이든 다시 지을 대상 자체가 사라집니다.
        Deplete();
    }

    /// <inheritdoc />
    /// <remarks>다시 설치할 수 있게 되면 한 번만 터지는 제한도 함께 풀어야 합니다.</remarks>
    protected override void OnRearmed()
    {
        HasExploded = false;
        m_isFuseBurning = false;
        m_fuseTimer = 0.0f;
        m_rangePreviewDirty = true;
    }

    /// <summary>이 콜라이더가 이 폭발물을 밟은 것으로 볼 대상인지 여부입니다.</summary>
    private bool IsTrigger(Collider other)
    {
        return other != null && (m_triggerLayers.value & (1 << other.gameObject.layer)) != 0;
    }
}
