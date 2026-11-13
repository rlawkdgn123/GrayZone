using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 화염병이 터진 자리에 남는 화염 지대입니다. 범위 안에 머무는 대상에게 일정 간격으로 화염 피해를 줍니다.
/// </summary>
/// <remarks>
/// 트리거 콜라이더가 필요합니다. <see cref="TemporaryTrapProjectile"/>의 함정 프리팹으로 지정하면
/// 충돌 지점에 생성되어 곧바로 설치 완료 상태가 됩니다.
///
/// <b>피해 계산</b>: 기반의 <c>m_damage</c>/<c>m_damageMode</c>/<c>m_damageTickInterval</c>은 쓰지 않고
/// 이 클래스의 초당 피해량과 틱 간격을 씁니다. 1틱 피해는 <c>초당 피해 × 간격</c>이고, 정수로 떨어지지
/// 않는 나머지는 대상마다 모아 두었다가 다음 틱에 얹습니다. 반올림으로 버리면 초당 피해가 설정값보다
/// 줄거나 늘어납니다.
///
/// <b>지속 시간</b>: 설치가 끝난 순간부터 셉니다. 시간이 다 되면 게임 오브젝트를 제거합니다.
/// 화염 지대는 재설치 대상이 아니므로 내구도와 재설치 정책은 쓰지 않습니다.
/// </remarks>
[RequireComponent(typeof(Collider))]
public sealed class FireTrap : Trap
{
    /// <inheritdoc />
    protected override string DefaultDisplayName => "화염 지대";

    /// <inheritdoc />
    protected override string DefaultDescription => "범위 안에 머무는 적에게 지속 화염 피해를 줍니다";

    /// <summary>범위 안에 있는 대상 하나의 상태입니다.</summary>
    private sealed class Occupant
    {
        /// <summary>이 대상의 콜라이더 중 지금 범위 안에 들어와 있는 것들입니다.</summary>
        /// <remarks>
        /// 래그돌처럼 콜라이더가 여럿인 대상은 콜라이더마다 Enter/Exit이 옵니다. 하나만 나가도 풀어 버리면
        /// 몸이 아직 불 위에 있는데 피해가 끊깁니다. 마지막 콜라이더가 나갈 때 풉니다.
        /// </remarks>
        public readonly HashSet<Collider> Colliders = new HashSet<Collider>();

        /// <summary>다음 틱까지 남은 시간(초)입니다.</summary>
        public float TickTimer;

        /// <summary>정수 피해로 넣지 못하고 남은 소수점 피해입니다.</summary>
        public float PendingDamage;
    }

    [Header("Fire Trap")]
    [Tooltip("화염 지대를 지나간 것으로 볼 레이어입니다. 이 레이어의 콜라이더가 범위 안에 있으면 피해를 받습니다. 기본은 Enemy만 봅니다. Player를 넣으면 팀원도 줄어든 피해를 받습니다.")]
    [SerializeField] private LayerMask m_targetLayers = 1 << 9;

    [Tooltip("화염 지대가 지속될 시간(초)입니다. 설치가 끝난 순간부터 세며, 다 되면 화염 지대가 사라집니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_duration = 5.0f;

    [Tooltip("범위 안에 머무는 동안 받는 초당 피해량입니다. 1틱 피해는 이 값에 틱 간격을 곱한 값입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_damagePerSecond = 10.0f;

    [Tooltip("피해를 주는 간격(초)입니다. 0.5면 0.5초마다 (초당 피해 × 0.5)만큼 들어갑니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_fireTickInterval = 0.5f;

    [Tooltip("플레이어 진영(팀원)이 받는 피해 배율입니다. 0.5면 적이 받는 피해의 절반입니다. 팀원이 피해를 받으려면 대상 레이어에 Player가 들어 있어야 합니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_allyDamageMultiplier = 0.5f;

    [Tooltip("켜면 범위에 들어온 순간 1틱 분량의 피해를 바로 줍니다. 끄면 첫 틱 간격이 지난 뒤부터 피해가 들어갑니다.")]
    [SerializeField] private bool m_damageOnEnter = true;

    [Tooltip("켜면 범위 안의 폭발물 함정(지뢰·클레이모어 등)에 불이 붙어 연쇄로 터집니다. 점화 순간과 이후 피해 간격마다 확인합니다.")]
    [SerializeField] private bool m_igniteExplosives = true;

    [Header("Burning On Exit")]
    [Tooltip("화염 지대에서 벗어난 적에게 걸 화상입니다. 불이 꺼질 때 안에 있던 대상도 벗어난 것으로 봅니다. 비어 있으면 Resources 기본 화상을 씁니다.")]
    [SerializeField] private StatusEffectDefinitionSO m_exitBurningEffect;

    [Tooltip("화염 지대에서 벗어난 팀원(플레이어 진영)에게 걸 화상입니다. 적보다 약한 값입니다. 비어 있으면 Resources 기본 팀원 화상을 씁니다.")]
    [SerializeField] private StatusEffectDefinitionSO m_allyExitBurningEffect;

    private const string ExitBurningResourcePath = "StatusEffects/Burning";
    private const string AllyExitBurningResourcePath = "StatusEffects/Burning_Ally";

    /// <summary>다음 연쇄 점화 확인까지 남은 시간(초)입니다.</summary>
    private float m_chainTimer;

    /// <summary>지금 범위 안에 있는 대상들입니다.</summary>
    private readonly Dictionary<IDamageable, Occupant> m_occupants =
        new Dictionary<IDamageable, Occupant>();

    /// <summary>이번 프레임에 처리할 키 목록입니다. 순회 도중 사전을 고치면 예외가 나므로 먼저 복사합니다.</summary>
    private readonly List<IDamageable> m_keyBuffer = new List<IDamageable>();

    /// <summary>이번 프레임에 범위에서 뺄 대상들입니다.</summary>
    private readonly List<IDamageable> m_removalBuffer = new List<IDamageable>();

    /// <summary>화염 지대가 사라지기까지 남은 시간(초)입니다. 설치 전에는 의미가 없습니다.</summary>
    private float m_remainingTime;

    /// <summary>점화되어 지속 시간이 흐르고 있는지 여부입니다.</summary>
    private bool m_isIgnited;

    /// <summary>이 화염 지대를 생성한 쪽이 요청한 선택적 3D 지속음입니다.</summary>
    private FMOD.Studio.EventInstance m_loopingSoundInstance;

    private static bool s_loggedMissingLoopingSound;

    /// <summary>적에게만 틱마다 더하는 피해입니다. 생성한 쪽(화염 드럼통 업그레이드)이 정합니다. 팀원 피해에는 더하지 않습니다.</summary>
    private float m_enemyBonusDamagePerTick;

    /// <summary>화염 지대가 사라지기까지 남은 시간(초)입니다.</summary>
    public float RemainingTime => m_remainingTime;

    /// <summary>초당 피해량입니다.</summary>
    public float DamagePerSecond => m_damagePerSecond;

    /// <summary>피해를 주는 간격(초)입니다.</summary>
    public float FireTickInterval => Mathf.Max(0.01f, m_fireTickInterval);

    /// <summary>지금 범위 안에 잡혀 있는 대상의 수입니다.</summary>
    public int OccupantCount => m_occupants.Count;

    /// <inheritdoc />
    protected override bool HasTargetInRange => m_occupants.Count > 0;

    /// <summary>
    /// 화염 지대의 지속 시간을 바꿉니다. 생성한 쪽이 프리팹 값과 다른 시간을 쓰고 싶을 때 부릅니다.
    /// </summary>
    /// <param name="duration">지속 시간(초)입니다.</param>
    /// <remarks>
    /// 점화 전에 부르면 점화 순간부터 이 시간을 셉니다. 이미 점화됐으면 남은 시간을 이 값으로 다시 맞춥니다.
    /// 화염 드럼통(<see cref="FireBarrelTrap"/>)이 같은 화염 프리팹을 쓰면서 자기 시간을 넣는 데 씁니다.
    /// </remarks>
    public void SetDuration(float duration)
    {
        m_duration = Mathf.Max(0.01f, duration);
        if (m_isIgnited)
        {
            m_remainingTime = m_duration;
        }
    }

    /// <summary>
    /// 화염 지대 범위를 배율만큼 넓힙니다. 판정 콜라이더와 불 이펙트가 함께 커지도록 오브젝트 크기를 바꿉니다.
    /// </summary>
    /// <param name="multiplier">범위 배율입니다. 1이면 프리팹 크기 그대로입니다.</param>
    /// <remarks>불 이펙트는 부모 크기를 따르는 설정(Hierarchy)이라 함께 커집니다. 생성 직후, 설치 전에 부릅니다.</remarks>
    public void SetRangeMultiplier(float multiplier)
    {
        transform.localScale *= Mathf.Max(0.01f, multiplier);
    }

    /// <summary>적에게만 틱마다 더할 피해를 정합니다. 팀원이 받는 피해는 바뀌지 않습니다.</summary>
    /// <param name="damagePerTick">틱마다 더할 피해입니다. 0이면 추가 피해가 없습니다.</param>
    public void SetEnemyBonusDamagePerTick(float damagePerTick)
    {
        m_enemyBonusDamagePerTick = Mathf.Max(0.0f, damagePerTick);
    }

    /// <summary>
    /// 이 화염 지대가 살아 있는 동안 지정한 3D 루프를 재생합니다.
    /// </summary>
    /// <remarks>화염 드럼통처럼 별도 지속음이 필요한 생성 경로만 호출합니다. 화염병에는 자동 적용되지 않습니다.</remarks>
    public void StartLoopingSound(string eventPath)
    {
        StopLoopingSound(FMOD.Studio.STOP_MODE.IMMEDIATE);

        if (string.IsNullOrWhiteSpace(eventPath) || !SoundManager.IsReady)
        {
            return;
        }

        try
        {
            m_loopingSoundInstance = SoundManager.Instance.CreateInstance(eventPath, gameObject);
            m_loopingSoundInstance.start();
        }
        catch (FMODUnity.EventNotFoundException exception)
        {
            m_loopingSoundInstance.clearHandle();
            if (!s_loggedMissingLoopingSound)
            {
                s_loggedMissingLoopingSound = true;
                Debug.LogWarning($"[FireTrap] FMOD 이벤트를 찾지 못했습니다: {eventPath}\n{exception.Message}", this);
            }
        }
    }

    /// <inheritdoc />
    protected override void OnBuilt()
    {
        Ignite();
    }

    /// <summary>
    /// 설치된 상태로 시작한 화염 지대를 점화합니다.
    /// </summary>
    /// <remarks>
    /// <c>m_startPlaced</c>가 켜져 있으면 <c>Trap.Awake</c>가 곧바로 설치 완료로 만들어 버려,
    /// 뒤이어 <see cref="Trap.Build"/>를 불러도 <see cref="OnBuilt"/>가 오지 않습니다.
    /// 화염병이 생성하는 화염 지대가 이 경우라 여기서 직접 점화합니다.
    /// </remarks>
    protected override void Start()
    {
        base.Start();

        if (IsBuilt)
        {
            Ignite();
        }
    }

    /// <summary>
    /// 지속 시간을 시작하고, 점화 순간 이미 범위 안에 있던 대상을 잡습니다.
    /// </summary>
    /// <remarks>
    /// Unity는 콜라이더가 이미 겹쳐 있는 상태에서 조건이 바뀌었다고 <c>OnTriggerEnter</c>를 다시 보내지
    /// 않습니다. 화염병은 적 발밑에 떨어지는 경우가 많아 이 처리가 없으면 첫 피해가 빠집니다.
    /// 설치 경로와 <see cref="Start"/> 경로가 겹쳐도 한 번만 점화합니다.
    /// </remarks>
    private void Ignite()
    {
        if (m_isIgnited)
        {
            return;
        }

        m_isIgnited = true;
        m_remainingTime = Mathf.Max(0.01f, m_duration);

        Collider trigger = GetComponent<Collider>();
        if (trigger == null)
        {
            return;
        }

        IgniteExplosivesInRange(trigger);
        m_chainTimer = FireTickInterval;

        Collider[] hits = TrapOverlapQuery.FindOverlaps(
            trigger,
            m_targetLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits.Length; i++)
        {
            OnTriggerEnter(hits[i]);
        }
    }

    private void Reset()
    {
        Collider trigger = GetComponent<Collider>();
        if (trigger != null)
        {
            trigger.isTrigger = true;
        }
    }

    protected override void Awake()
    {
        base.Awake();
        EnsureTriggerBody();
    }

    /// <summary>트리거 콜라이더가 하나도 없으면 인스펙터에서 경고합니다.</summary>
    private void OnValidate()
    {
        foreach (Collider collider in GetComponents<Collider>())
        {
            if (collider != null && collider.isTrigger)
            {
                return;
            }
        }

        Debug.LogWarning(
            $"[FireTrap] '{name}': 트리거 콜라이더가 없습니다. Collider의 Is Trigger를 켜야 감지가 동작합니다.",
            this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (IsBlueprint || !IsTarget(other))
        {
            return;
        }

        IDamageable damageable = other.GetComponentInParent<IDamageable>();
        if (damageable == null || damageable.IsDead)
        {
            return;
        }

        if (m_occupants.TryGetValue(damageable, out Occupant occupant))
        {
            occupant.Colliders.Add(other);
            return;
        }

        occupant = new Occupant { TickTimer = FireTickInterval };
        occupant.Colliders.Add(other);
        m_occupants.Add(damageable, occupant);

        if (m_damageOnEnter)
        {
            ApplyTickDamage(damageable, occupant);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsTarget(other))
        {
            return;
        }

        IDamageable damageable = other.GetComponentInParent<IDamageable>();
        if (damageable == null || !m_occupants.TryGetValue(damageable, out Occupant occupant))
        {
            return;
        }

        occupant.Colliders.Remove(other);
        if (occupant.Colliders.Count == 0)
        {
            m_occupants.Remove(damageable);
            ApplyExitBurning(damageable);
        }
    }

    /// <summary>
    /// 화염 지대에서 벗어난 대상에게 화상을 겁니다. 적은 기본 화상, 팀원은 약한 팀원 화상을 받습니다.
    /// </summary>
    /// <remarks>
    /// 화상 피해는 진영 규칙을 거쳐 들어가므로, 피해를 준 쪽 진영을 받는 쪽의 반대로 넘깁니다.
    /// 같은 진영으로 넘기면 팀원 화상이 아군 피해로 걸러져 들어가지 않습니다.
    /// 죽은 대상과 피해를 받지 않는 대상(함정 등)에는 걸지 않습니다.
    /// </remarks>
    private void ApplyExitBurning(IDamageable damageable)
    {
        if (!IsAliveTarget(damageable))
        {
            return;
        }

        bool isAlly = damageable.Faction == Faction.Player;
        StatusEffectDefinitionSO burning = isAlly ? ResolveAllyExitBurningEffect() : ResolveExitBurningEffect();
        if (burning == null)
        {
            return;
        }

        Faction sourceFaction = isAlly ? Faction.Enemy : Faction.Player;
        StatusEffectContainer.GetOrAdd(damageable)?.Apply(burning, sourceFaction, gameObject, CreateDamageInfo());
    }

    private StatusEffectDefinitionSO ResolveExitBurningEffect()
    {
        if (m_exitBurningEffect == null)
        {
            m_exitBurningEffect = Resources.Load<StatusEffectDefinitionSO>(ExitBurningResourcePath);
        }

        return m_exitBurningEffect;
    }

    private StatusEffectDefinitionSO ResolveAllyExitBurningEffect()
    {
        if (m_allyExitBurningEffect == null)
        {
            m_allyExitBurningEffect = Resources.Load<StatusEffectDefinitionSO>(AllyExitBurningResourcePath);
        }

        return m_allyExitBurningEffect;
    }

    /// <summary>
    /// 지속 시간을 줄이고, 범위 안의 대상에게 틱 간격마다 피해를 넣습니다.
    /// </summary>
    /// <remarks>
    /// 타이머는 대상마다 따로 둡니다. 들어온 시점이 제각각이라 하나로 묶으면 늦게 들어온 대상이
    /// 바로 피해를 받는 일이 생깁니다.
    /// </remarks>
    private void Update()
    {
        if (!IsBuilt || !m_isIgnited)
        {
            return;
        }

        m_remainingTime -= Time.deltaTime;
        if (m_remainingTime <= 0.0f)
        {
            Expire();
            return;
        }

        // 폭발물은 피해 대상이 아니라 m_occupants에 잡히지 않으므로 따로 주기적으로 찾습니다.
        // 불길이 남아 있는 동안 새로 설치된 함정도 불이 붙어야 합니다.
        m_chainTimer -= Time.deltaTime;
        if (m_chainTimer <= 0.0f)
        {
            IgniteExplosivesInRange(GetComponent<Collider>());
            m_chainTimer = FireTickInterval;
        }

        if (m_occupants.Count == 0)
        {
            return;
        }

        float interval = FireTickInterval;

        m_keyBuffer.Clear();
        foreach (IDamageable key in m_occupants.Keys)
        {
            m_keyBuffer.Add(key);
        }

        for (int i = 0; i < m_keyBuffer.Count; i++)
        {
            IDamageable damageable = m_keyBuffer[i];
            Occupant occupant = m_occupants[damageable];

            // 범위 안에서 죽거나 풀로 돌아가면 OnTriggerExit이 오지 않을 수 있어 여기서 걸러 냅니다.
            if (!IsAliveTarget(damageable) || !HasActiveCollider(occupant))
            {
                m_removalBuffer.Add(damageable);
                continue;
            }

            occupant.TickTimer -= Time.deltaTime;

            // 한 프레임이 간격보다 길어도 밀린 틱을 몰아 주지 않고 한 번만 줍니다.
            if (occupant.TickTimer <= 0.0f)
            {
                ApplyTickDamage(damageable, occupant);
                occupant.TickTimer = interval;
            }
        }

        for (int i = 0; i < m_removalBuffer.Count; i++)
        {
            m_occupants.Remove(m_removalBuffer[i]);
        }

        m_removalBuffer.Clear();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        ClearOccupants();
        StopLoopingSound(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
    }

    /// <inheritdoc />
    protected override void OnDepleted()
    {
        ClearOccupants();
        StopLoopingSound(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
    }

    /// <summary>1틱 분량의 피해를 넣습니다. 정수로 떨어지지 않는 나머지는 다음 틱으로 넘깁니다.</summary>
    /// <remarks>
    /// 팀원(플레이어 진영)은 <c>m_allyDamageMultiplier</c>만큼 줄여서 받습니다.
    /// 적은 업그레이드로 정해진 틱당 추가 피해(<see cref="SetEnemyBonusDamagePerTick"/>)를 더 받습니다.
    /// </remarks>
    private void ApplyTickDamage(IDamageable damageable, Occupant occupant)
    {
        bool isAlly = damageable.Faction == Faction.Player;
        float multiplier = isAlly ? m_allyDamageMultiplier : 1.0f;
        occupant.PendingDamage += m_damagePerSecond * FireTickInterval * multiplier;
        if (!isAlly)
        {
            occupant.PendingDamage += m_enemyBonusDamagePerTick;
        }

        int amount = Mathf.FloorToInt(occupant.PendingDamage);
        if (amount <= 0)
        {
            return;
        }

        occupant.PendingDamage -= amount;
        damageable.TakeDamage(CreateDamageInfo(amount));
    }

    /// <summary>화염 지대 범위 안의 폭발물 함정에 불이 붙었음을 알립니다.</summary>
    /// <remarks>
    /// 판정 범위는 트리거 콜라이더의 경계 상자입니다. 피해 판정과 레이어가 달라서(함정은 Trap 레이어)
    /// 트리거 이벤트로는 잡히지 않습니다. 이미 터졌거나 청사진인 함정은 받는 쪽이 무시합니다.
    /// </remarks>
    private void IgniteExplosivesInRange(Collider trigger)
    {
        if (!m_igniteExplosives || trigger == null)
        {
            return;
        }

        Bounds bounds = trigger.bounds;
        ExplosionDamage.TriggerChainDetonation(
            bounds.center,
            bounds.extents,
            Quaternion.identity,
            candidate => candidate.bounds.Intersects(bounds),
            gameObject);
    }

    /// <summary>지속 시간이 끝난 화염 지대를 정리하고 제거합니다.</summary>
    /// <remarks>불이 꺼질 때 안에 있던 대상도 불에서 벗어난 것이므로 화상을 겁니다.</remarks>
    private void Expire()
    {
        foreach (IDamageable damageable in m_occupants.Keys)
        {
            ApplyExitBurning(damageable);
        }

        ClearOccupants();
        enabled = false;
        Destroy(gameObject);
    }

    private void ClearOccupants()
    {
        m_occupants.Clear();
        m_keyBuffer.Clear();
        m_removalBuffer.Clear();
    }

    /// <summary>현재 화염 지속음을 정지하고 FMOD 인스턴스를 해제합니다.</summary>
    private void StopLoopingSound(FMOD.Studio.STOP_MODE stopMode)
    {
        if (!m_loopingSoundInstance.isValid())
        {
            m_loopingSoundInstance.clearHandle();
            return;
        }

        m_loopingSoundInstance.stop(stopMode);
        m_loopingSoundInstance.release();
        m_loopingSoundInstance.clearHandle();
    }

    /// <summary>이 콜라이더가 이 함정이 반응할 대상인지 여부입니다.</summary>
    /// <remarks>
    /// 트리거 콜라이더(구조 감지 구, 적 접촉 센서 등)는 몸이 아니라 감지 범위라 제외합니다. 점화 순간의
    /// 범위 검사(<c>QueryTriggerInteraction.Ignore</c>)와 같은 기준입니다. 넣으면 몸보다 넓은 범위에서 불이 붙습니다.
    /// </remarks>
    private bool IsTarget(Collider other)
    {
        return other != null
            && !other.isTrigger
            && (m_targetLayers.value & (1 << other.gameObject.layer)) != 0;
    }

    /// <summary>
    /// 트리거 감지에 필요한 키네마틱 Rigidbody를 보장합니다.
    /// </summary>
    /// <remarks>
    /// 트리거 이벤트는 둘 중 한쪽에 Rigidbody나 CharacterController가 있어야 옵니다. AI 팀원은 CharacterController를
    /// 끄고 일반 콜라이더만 쓰므로, 화염 쪽에 바디가 없으면 조작 중인 캐릭터만 불에 타고 나머지는 지나가도 멀쩡합니다.
    /// 프리팹마다 붙이는 것을 잊지 않도록 코드에서 보장합니다.
    /// </remarks>
    private void EnsureTriggerBody()
    {
        Rigidbody body = GetComponent<Rigidbody>();
        if (body == null)
        {
            body = gameObject.AddComponent<Rigidbody>();
        }

        body.isKinematic = true;
        body.useGravity = false;
    }

    /// <summary>파괴되지 않았고 죽지도 않은 대상인지 여부입니다.</summary>
    private static bool IsAliveTarget(IDamageable damageable)
    {
        // 인터페이스로 들고 있으면 Unity의 null 비교가 적용되지 않아 파괴된 컴포넌트를 따로 거릅니다.
        if (damageable is Component component && (component == null || !component.gameObject.activeInHierarchy))
        {
            return false;
        }

        return damageable != null && !damageable.IsDead;
    }

    /// <summary>범위 안에 들어와 있던 콜라이더 중 아직 켜져 있는 것이 있는지 확인합니다.</summary>
    private static bool HasActiveCollider(Occupant occupant)
    {
        occupant.Colliders.RemoveWhere(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy);
        return occupant.Colliders.Count > 0;
    }
}
