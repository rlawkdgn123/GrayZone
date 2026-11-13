using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 윤형 철조망입니다. 범위 안에 있는 적을 느리게 만들고 머무는 동안 지속 피해를 줍니다.
/// </summary>
/// <remarks>
/// 트리거 콜라이더가 필요합니다. 들어올 때 감속을 걸고 나갈 때 풉니다.
///
/// <b>감속을 값 저장/복원으로 하지 않는 이유</b>: 적의 FSM은 상태가 바뀔 때마다
/// <c>agent.speed</c>를 자기 기준값으로 다시 대입합니다. 들어올 때 속도를 깎아 두어도 다음 상태 전환에서
/// 원래대로 돌아옵니다. 그래서 <see cref="EnemyController.AddForceWalk"/>로 달리기를 막아
/// 속도를 구하는 지점 자체가 걷기 값을 내놓게 합니다.
///
/// 스파이크(<see cref="SpikeTrap"/>)는 이 함정을 그대로 쓰고, 지속 감속 대신 진입 순간 감속과 피해에 무게를 둡니다.
/// </remarks>
[RequireComponent(typeof(Collider))]
public class WireTrap : Trap
{
    private const string ActiveSoundEventPath = "event:/World/Trap/Active/Wire";
    private static bool s_loggedMissingActiveSound;

    /// <inheritdoc />
    protected override string DefaultDisplayName => "윤형 철조망";

    /// <inheritdoc />
    protected override string DefaultDescription => "적의 진입속도를 늦추고 소량의 데미지를 줍니다";

    /// <inheritdoc />
    protected override TrapKind CostKind => TrapKind.Wire;

    /// <inheritdoc />
    /// <remarks>철조망과 스파이크(파생)는 적이 지날 때마다 내구도가 닳으므로 설치된 채로 수리할 수 있습니다.</remarks>
    protected override bool IsRepairable => true;

    /// <summary>범위 안에 있는 대상 하나의 상태입니다.</summary>
    private struct Occupant
    {
        /// <summary>피해를 넣을 대상입니다. 들어올 때 한 번만 찾아 둡니다.</summary>
        public IDamageable Damageable;

        /// <summary>다음 틱까지 남은 시간(초)입니다. Once 방식에서는 쓰지 않습니다.</summary>
        public float TickTimer;

        /// <summary>들어올 때의 적 스폰 세대입니다. 풀로 돌아갔다 다시 나온 개체를 같은 적으로 보지 않기 위해 둡니다.</summary>
        public uint SpawnGeneration;

        /// <summary>같은 적에 연결된 트리거 콜라이더들입니다. 모두 나가야 감속을 풉니다.</summary>
        public HashSet<Collider> Colliders;
    }

    [Header("Wire Trap")]
    [Tooltip("이 함정이 반응할 레이어입니다. 기본은 Enemy만 봅니다.")]
    [SerializeField] private LayerMask m_targetLayers = 1 << 9;

    [Tooltip("범위 안에 있는 동안 적용할 이동 속도 배율입니다. 0.5면 절반 속도이고 1이면 감속이 없습니다. 이동 속도와 애니메이션 재생 속도가 함께 줄어듭니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_moveSpeedMultiplier = 0.7f;

    [Tooltip("켜면 위 배율에 더해 달리기 자체를 막고 걷기 동작으로만 이동하게 합니다.")]
    [SerializeField] private bool m_forceWalk = false;

    [Tooltip("적 하나가 범위에 들어올 때마다 깎을 내구도입니다. 0이면 닳지 않습니다. 내구도가 다 닳으면 재설치 정책에 따라 처리됩니다.")]
    [Min(0)]
    [SerializeField] private int m_durabilityCostPerTarget = 1;

    [Header("Entry Slow")]
    [Tooltip("적이 들어온 순간 잠깐 거는 이동 속도 배율입니다. 0.2면 80% 감속입니다. 위의 지속 배율에 곱해집니다. 1이면 쓰지 않습니다.")]
    [Range(0.0f, 1.0f)]
    [SerializeField] private float m_entrySlowMultiplier = 1.0f;

    [Tooltip("진입 순간 감속을 유지할 시간(초)입니다. 들어온 적마다 한 번씩 걸리고, 시간이 지나면 지속 배율로 돌아갑니다. 0이면 쓰지 않습니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_entrySlowDuration = 0.0f;

    /// <summary>업그레이드로 지속 이동 속도 배율에 곱하는 값입니다. 1이면 프리팹 값 그대로입니다.</summary>
    private float m_upgradeMoveSpeedScale = 1.0f;

    /// <summary>진입 순간 감속이 끝나는 시각(Time.time)입니다. 적마다 따로 셉니다.</summary>
    private readonly Dictionary<EnemyController, float> m_entrySlowEndTimes =
        new Dictionary<EnemyController, float>();

    /// <summary>진입 순간 감속이 끝난 적들입니다. 순회 도중 사전을 고치지 않으려고 따로 모읍니다.</summary>
    private readonly List<EnemyController> m_entrySlowEndedBuffer = new List<EnemyController>();

    /// <summary>범위 안에 있는 동안 걸리는 이동 속도 배율입니다. 업그레이드가 곱해져 있습니다.</summary>
    public float MoveSpeedMultiplier => Mathf.Clamp01(m_moveSpeedMultiplier * m_upgradeMoveSpeedScale);

    /// <summary>진입 순간 감속을 쓰는지 여부입니다.</summary>
    private bool UsesEntrySlow => m_entrySlowDuration > 0.0f && m_entrySlowMultiplier < 1.0f;

    /// <summary>지금 범위 안에 있는 적들입니다.</summary>
    private readonly Dictionary<EnemyController, Occupant> m_occupants =
        new Dictionary<EnemyController, Occupant>();

    /// <summary>한 명 이상 철조망에 걸려 있는 동안만 재생하는 금속 흔들림 루프입니다.</summary>
    private FMOD.Studio.EventInstance m_activeSoundInstance;

    /// <summary>이번 프레임에 처리할 키 목록입니다. 순회 도중 사전을 고치면 예외가 나므로 먼저 복사합니다.</summary>
    private readonly List<EnemyController> m_keyBuffer = new List<EnemyController>();

    /// <summary>이번 프레임에 범위에서 뺄 대상들입니다.</summary>
    private readonly List<EnemyController> m_removalBuffer = new List<EnemyController>();

    /// <summary>지금 범위 안에 잡혀 있는 적의 수입니다.</summary>
    public int OccupantCount => m_occupants.Count;

    /// <inheritdoc />
    protected override bool HasTargetInRange => m_occupants.Count > 0;

    /// <summary>
    /// 설치가 끝나는 순간 이미 범위 안에 있던 적을 잡습니다.
    /// </summary>
    /// <remarks>
    /// Unity는 콜라이더가 이미 겹쳐 있는 상태에서 조건이 바뀌었다고 <c>OnTriggerEnter</c>를 다시 보내지
    /// 않습니다. 이 처리가 없으면 적 위에 설치한 함정이 그 적이 한 번 나갔다 들어올 때까지 아무 일도
    /// 하지 않아 고장 난 것처럼 보입니다.
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

    /// <summary>
    /// 트리거 콜라이더가 하나도 없으면 인스펙터에서 경고합니다.
    /// </summary>
    /// <remarks>
    /// <c>Reset</c>은 스크립트를 처음 붙일 때만 돌아서, 콜라이더를 나중에 따로 추가하면 <c>isTrigger</c>가
    /// 꺼진 채 남습니다. 그러면 감지가 조용히 동작하지 않아 원인을 찾기 어렵습니다.
    /// </remarks>
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
            $"[WireTrap] '{name}': 트리거 콜라이더가 없습니다. Collider의 Is Trigger를 켜야 감지가 동작합니다.",
            this);
    }

    private void OnTriggerEnter(Collider other)
    {
        // 청사진 상태에서는 효과가 없습니다. 콜라이더는 끄지 않습니다.
        // InteractionController가 이 콜라이더로 상호작용 대상을 찾기 때문에, 끄면 설치할 수가 없습니다.
        if (IsBlueprint || !IsTarget(other))
        {
            return;
        }

        EnemyController enemy = other.GetComponentInParent<EnemyController>();
        if (enemy == null)
        {
            return;
        }

        if (m_occupants.TryGetValue(enemy, out Occupant existingOccupant))
        {
            if (existingOccupant.Colliders == null)
            {
                existingOccupant.Colliders = new HashSet<Collider>();
                m_occupants[enemy] = existingOccupant;
            }

            existingOccupant.Colliders.Add(other);
            return;
        }

        IDamageable damageable = enemy.GetComponentInParent<IDamageable>()
            ?? enemy.GetComponentInChildren<IDamageable>(true);

        m_occupants.Add(enemy, new Occupant
        {
            Damageable = damageable,
            TickTimer = DamageInterval,
            SpawnGeneration = enemy.SpawnGeneration,
            Colliders = new HashSet<Collider> { other },
        });

        UpdateActiveSound();

        // 진입 순간 감속이 있으면 먼저 그 값으로 걸고, 시간이 지나면 Update가 지속 배율로 되돌립니다.
        // 같은 키(this)로 다시 걸면 값만 바뀌므로, 해제 경로는 지속 감속과 같습니다.
        if (UsesEntrySlow)
        {
            enemy.AddMoveSpeedMultiplier(this, MoveSpeedMultiplier * m_entrySlowMultiplier);
            m_entrySlowEndTimes[enemy] = Time.time + m_entrySlowDuration;
        }
        else
        {
            enemy.AddMoveSpeedMultiplier(this, MoveSpeedMultiplier);
        }

        if (m_forceWalk)
        {
            enemy.AddForceWalk(this);
        }

        // 방식과 무관하게 들어온 순간 한 번은 줍니다. Tick은 그 뒤로 간격마다 더 주는 것이고,
        // Once는 여기서 끝입니다. 밟자마자 아무 일도 안 일어나면 함정이 고장 난 것처럼 보입니다.
        int damage = Damage;
        if (damageable != null && !damageable.IsDead && damage > 0)
        {
            damageable.TakeDamage(CreateDamageInfo(damage));
        }

        // 내구도는 머무는 시간이 아니라 걸린 적 수로 깎습니다. 시간으로 깎으면 한 마리가 오래 갇혀
        // 있는 것만으로 철조망이 없어져, 여럿을 막는다는 이 함정의 역할과 어긋납니다.
        // 여기서 다 닳으면 OnDepleted가 바로 이어져 방금 넣은 대상까지 함께 풀립니다.
        TakeDurabilityDamage(m_durabilityCostPerTarget);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsTarget(other))
        {
            return;
        }

        EnemyController enemy = other.GetComponentInParent<EnemyController>();
        if (enemy == null || !m_occupants.TryGetValue(enemy, out Occupant occupant))
        {
            return;
        }

        if (occupant.Colliders == null)
        {
            Release(enemy);
            return;
        }

        occupant.Colliders.Remove(other);
        if (occupant.Colliders.Count == 0)
        {
            Release(enemy);
        }
    }

    /// <summary>
    /// Tick 방식일 때 범위 안의 대상에게 <see cref="Trap.DamageInterval"/>마다
    /// <see cref="Trap.m_damage"/>만큼 피해를 넣습니다. 진입 시 1회는 <c>OnTriggerEnter</c>가 이미 줬습니다.
    /// </summary>
    /// <remarks>
    /// 타이머는 대상마다 따로 둡니다. 들어온 시점이 제각각이라 하나로 묶으면 늦게 들어온 적이
    /// 바로 피해를 받는 일이 생깁니다.
    ///
    /// Once 방식이어도 이 함수는 계속 돕니다. 피해는 주지 않지만, 함정 안에서 죽어 풀로 돌아간 대상을
    /// 걸러내 감속이 남지 않게 해야 하기 때문입니다.
    /// </remarks>
    private void Update()
    {
        UpdateEntrySlow();

        if (m_occupants.Count == 0)
        {
            return;
        }

        bool tickMode = m_damageMode == TrapDamageMode.Tick;
        float interval = DamageInterval;
        int damage = Damage;

        m_keyBuffer.Clear();
        foreach (EnemyController key in m_occupants.Keys)
        {
            m_keyBuffer.Add(key);
        }

        for (int i = 0; i < m_keyBuffer.Count; i++)
        {
            EnemyController enemy = m_keyBuffer[i];

            // 함정 안에서 죽어 풀로 돌아가면 OnTriggerExit이 오지 않을 수 있습니다.
            // 그대로 두면 죽은 대상에 계속 피해를 넣고 감속도 남으므로 여기서 걸러 냅니다.
            if (enemy == null || !enemy.isActiveAndEnabled)
            {
                m_removalBuffer.Add(enemy);
                continue;
            }

            Occupant occupant = m_occupants[enemy];

            // 풀로 돌아갔다 같은 프레임에 다시 스폰되면 위 활성 검사를 빠져나갑니다. 세대가 바뀌었으면 다른 적입니다.
            if (occupant.SpawnGeneration != enemy.SpawnGeneration)
            {
                m_removalBuffer.Add(enemy);
                continue;
            }

            if (occupant.Damageable == null || occupant.Damageable.IsDead)
            {
                m_removalBuffer.Add(enemy);
                continue;
            }

            if (!tickMode)
            {
                continue;
            }

            occupant.TickTimer -= Time.deltaTime;

            // 한 프레임이 간격보다 길어도 밀린 틱을 한 번에 몰아 주지 않고 한 번만 줍니다.
            // 프레임이 크게 튀었을 때 피해가 갑자기 몰리는 쪽이 더 나쁩니다.
            if (occupant.TickTimer <= 0.0f)
            {
                if (damage > 0)
                {
                    occupant.Damageable.TakeDamage(CreateDamageInfo(damage));
                }

                occupant.TickTimer = interval;
            }

            m_occupants[enemy] = occupant;
        }

        for (int i = 0; i < m_removalBuffer.Count; i++)
        {
            Release(m_removalBuffer[i]);
        }

        m_removalBuffer.Clear();
    }

    /// <summary>
    /// 진입 순간 감속이 끝난 적을 지속 배율로 되돌립니다.
    /// </summary>
    /// <remarks>
    /// 그 사이 범위를 벗어났거나 죽어서 이미 풀린 적은 목록에서만 뺍니다. 다시 걸면 범위 밖에서 느려진 채로 남습니다.
    /// </remarks>
    private void UpdateEntrySlow()
    {
        if (m_entrySlowEndTimes.Count == 0)
        {
            return;
        }

        float now = Time.time;
        foreach (KeyValuePair<EnemyController, float> pair in m_entrySlowEndTimes)
        {
            if (pair.Key == null || now >= pair.Value)
            {
                m_entrySlowEndedBuffer.Add(pair.Key);
            }
        }

        for (int i = 0; i < m_entrySlowEndedBuffer.Count; i++)
        {
            EnemyController enemy = m_entrySlowEndedBuffer[i];
            m_entrySlowEndTimes.Remove(enemy);

            if (enemy != null && m_occupants.ContainsKey(enemy))
            {
                enemy.AddMoveSpeedMultiplier(this, MoveSpeedMultiplier);
            }
        }

        m_entrySlowEndedBuffer.Clear();
    }

    /// <inheritdoc />
    /// <remarks>철조망은 Wire 업그레이드로 감속과 내구도가 늘어납니다. 스파이크는 이 동작을 바꿉니다.</remarks>
    protected override void OnApplyUpgrade(TrapUpgradeTableSO table, DefenseSceneDataManager data)
    {
        m_upgradeMoveSpeedScale = 1.0f;
        ApplyLevelUpgrade(table, data);
    }

    /// <summary>
    /// 이 함정 종류의 업그레이드 레벨을 적용합니다. 호출 전에 감속 배율과 내구도·피해 보너스는 기본값입니다.
    /// </summary>
    protected virtual void ApplyLevelUpgrade(TrapUpgradeTableSO table, DefenseSceneDataManager data)
    {
        if (table == null
            || data == null
            || !table.TryGetWire(data.GetUpgradeLevel(ScrambleUpgradeType.Wire), out TrapUpgradeTableSO.WireLevel level))
        {
            return;
        }

        m_upgradeMoveSpeedScale = Mathf.Clamp01(level.MoveSpeedScale);
        SetUpgradeBonus(level.BonusDurability, 1.0f);
    }

    /// <summary>함정이 꺼질 때 걸어 둔 감속을 전부 풉니다.</summary>
    protected override void OnDisable()
    {
        base.OnDisable();
        ReleaseAll();
    }

    /// <inheritdoc />
    /// <remarks>
    /// 내구도가 다 닳으면 철조망은 더 이상 아무도 붙잡지 않습니다. 이때 풀지 않으면 함정이 꺼졌는데도
    /// 안에 있던 적이 계속 느린 채로 남습니다.
    /// </remarks>
    protected override void OnDepleted()
    {
        ReleaseAll();
    }

    /// <summary>범위 안의 모든 대상에게 걸어 둔 효과를 풀고 목록을 비웁니다.</summary>
    private void ReleaseAll()
    {
        foreach (EnemyController enemy in m_occupants.Keys)
        {
            if (enemy != null)
            {
                enemy.RemoveMoveSpeedMultiplier(this);
                enemy.RemoveForceWalk(this);
            }
        }

        m_occupants.Clear();
        m_keyBuffer.Clear();
        m_removalBuffer.Clear();
        m_entrySlowEndTimes.Clear();
        m_entrySlowEndedBuffer.Clear();
        StopActiveSound();
    }

    /// <summary>이 콜라이더가 이 함정이 반응할 대상인지 여부입니다.</summary>
    private bool IsTarget(Collider other)
    {
        return other != null && (m_targetLayers.value & (1 << other.gameObject.layer)) != 0;
    }

    /// <summary>한 대상을 범위에서 제외하고 걸어 둔 감속을 풉니다.</summary>
    private void Release(EnemyController enemy)
    {
        if (enemy == null)
        {
            // Unity의 파괴된 객체는 == null이지만 Dictionary의 실제 참조 키는 남아 있습니다.
            if (!ReferenceEquals(enemy, null))
            {
                m_occupants.Remove(enemy);
                m_entrySlowEndTimes.Remove(enemy);
            }
            UpdateActiveSound();
            return;
        }

        m_entrySlowEndTimes.Remove(enemy);
        if (m_occupants.Remove(enemy))
        {
            enemy.RemoveMoveSpeedMultiplier(this);
            enemy.RemoveForceWalk(this);
        }

        UpdateActiveSound();
    }

    /// <summary>윤형 철조망에 적이 걸려 있을 때만 접촉 루프를 유지합니다. 스파이크는 제외합니다.</summary>
    private void UpdateActiveSound()
    {
        if (CostKind != TrapKind.Wire || m_occupants.Count == 0 || IsBlueprint)
        {
            StopActiveSound();
            return;
        }

        if (m_activeSoundInstance.isValid() || !SoundManager.IsReady)
        {
            return;
        }

        try
        {
            m_activeSoundInstance = SoundManager.Instance.CreateInstance(ActiveSoundEventPath, gameObject);
            m_activeSoundInstance.start();
        }
        catch (FMODUnity.EventNotFoundException exception)
        {
            m_activeSoundInstance.clearHandle();
            if (!s_loggedMissingActiveSound)
            {
                s_loggedMissingActiveSound = true;
                Debug.LogWarning($"[WireTrap] FMOD 이벤트를 찾지 못했습니다: {ActiveSoundEventPath}\n{exception.Message}", this);
            }
        }
    }

    /// <summary>접촉이 끝나거나 함정이 비활성화되면 루프를 페이드아웃하고 해제합니다.</summary>
    private void StopActiveSound()
    {
        if (!m_activeSoundInstance.isValid())
        {
            m_activeSoundInstance.clearHandle();
            return;
        }

        m_activeSoundInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        m_activeSoundInstance.release();
        m_activeSoundInstance.clearHandle();
    }
}
