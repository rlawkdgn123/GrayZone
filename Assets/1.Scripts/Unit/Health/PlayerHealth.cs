using System;
using UnityEngine;
using UnityEngine.Serialization;
using VInspector;

/// <summary>부상 게이지에서 환산한 캐릭터의 부상 심각도 단계입니다.</summary>
/// <remarks>
/// 환산 규칙은 <see cref="CharacterInjuryStateRule"/>이 소유하며 Field와 Shelter가 같은 규칙을 씁니다.
/// 단계는 표시와 셸터 치료 판정에 쓰이고, 전투 피해 계산에는 직접 관여하지 않습니다.
/// </remarks>
public enum CharacterInjuryState
{
    /// <summary>부상이 없거나 무시할 수준입니다.</summary>
    Normal,

    /// <summary>경상입니다.</summary>
    Minor,

    /// <summary>중상입니다.</summary>
    Serious,

    /// <summary>치명상입니다.</summary>
    Critical
}

/// <summary>
/// 플레이어블 캐릭터의 체력·부상·다운·부활 상태를 소유하는 체력 컴포넌트입니다.
/// </summary>
/// <remarks>
/// HP가 0이 되면 사망이 아니라 <b>다운</b>으로 갑니다. 사망은 별도 조건에서만 발생합니다.
/// <para>
/// 수치의 첫 초기화는 밸런스 SO가 담당하고(<see cref="ISharedBalanceReceiver"/>),
/// 필드 사이의 관계 정리는 <see cref="IBalancePostProcess.OnBalanceApplied"/>에서 한 번에 수행합니다.
/// 그 정리를 <c>OnValidate</c>에만 두면 빌드에서 돌지 않아 시트에서 들어온 잘못된 값을 막지 못합니다.
/// </para>
/// </remarks>
public class PlayerHealth : HealthSystemBase, IBalancePostProcess, ISharedBalanceReceiver
{
    /// <summary>
    /// 이 컴포넌트를 쓰는 유닛은 진영이 정해져 있으므로 레이어 추론을 쓰지 않습니다.
    /// </summary>
    /// <remarks>
    /// 히트박스를 별도 레이어로 분리하면서 레이어와 진영의 결합을 끊기 위한 것입니다.
    /// Inspector에서 진영을 명시하면 그 값이 우선합니다.
    /// </remarks>
    protected override Faction DefaultFaction => Faction.Player;

    [Tooltip("이 플레이어에 적용할 공용 밸런스 SO입니다. 비어 있으면 Inspector 값을 그대로 씁니다.")]
    [SerializeField] private PlayerCommonBalanceSO m_balanceSO;

    [Foldout("Injury Options")]
    [Tooltip("현재 전투 중 누적된 부상 게이지입니다. 전투 중에는 float로 유지하고 결과 판정 시 반올림합니다.")]
    [FormerlySerializedAs("m_accumulatedDamage")]
    [ReadOnly][SerializeField] private float m_currentInjuryGauge;

    [Tooltip("현재 반올림 부상 게이지 기준으로 판정된 부상 상태입니다.")]
    [ReadOnly][SerializeField] private CharacterInjuryState m_currentInjuryState = CharacterInjuryState.Normal;

    [Tooltip("부상 게이지 최대값입니다. 1차 프로토타입 기준값은 100입니다.")]
    [Clamp(Min = 1)]
    [SerializeField] private float m_maxInjuryGauge = 100.0f;

    [Tooltip("실제 HP 피해량을 부상 게이지로 변환할 때 곱하는 비율입니다. 기획 공식의 r 값입니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_injuryConversionRatio = 0.35f;

    [Tooltip("정상 상태로 판정되는 반올림 부상 게이지 최대값입니다. 1차 프로토타입 기준 0~10입니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private int m_normalInjuryMaxGauge = 10;

    [Tooltip("경상 상태로 판정되는 반올림 부상 게이지 최대값입니다. 1차 프로토타입 기준 11~40입니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private int m_minorInjuryMaxGauge = 40;

    [Tooltip("치명상 상태로 판정되기 시작하는 반올림 부상 게이지 최소값입니다. 1차 프로토타입 기준 71입니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private int m_criticalInjuryMinGauge = 71;

    [Tooltip("아직 구조된 적이 없을 때 적용되는 부상 게이지 배율입니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_baseInjuryMultiplier = 1.0f;

    [Tooltip("첫 번째 구조 이후 적용되는 부상 게이지 배율입니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_revivedOnceInjuryMultiplier = 1.2f;

    [Tooltip("두 번째 구조 이후 적용되는 부상 게이지 배율입니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_revivedTwiceInjuryMultiplier = 1.5f;

    [Tooltip("세 번째 구조 이후 적용되는 부상 게이지 배율입니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_revivedThreeTimesInjuryMultiplier = 1.8f;
    [Foldout("Down Options")]
    [Tooltip("다운된 플레이어가 구조되지 않았을 때 전투 이탈 처리되기까지 걸리는 시간입니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private float m_downDuration = 30.0f;

    [Tooltip("이 횟수만큼 구조된 뒤 다시 HP가 0이 되면 즉시 전투 이탈 처리됩니다.")]
    [BalanceField]
    [Clamp(Min = 0)]
    [SerializeField] private int m_maxReviveCount = 3;

    [Tooltip("현재 출격에서 첫 번째 구조 시 최대 HP 기준으로 회복되는 비율입니다.")]
    [Range(1, 100)]
    [BalanceField]
    [Clamp(Min = 0, Max = 100)]
    [SerializeField] private int m_firstReviveHpPercent = 50;

    [Tooltip("현재 출격에서 두 번째 구조 시 최대 HP 기준으로 회복되는 비율입니다.")]
    [Range(1, 100)]
    [BalanceField]
    [Clamp(Min = 0, Max = 100)]
    [SerializeField] private int m_secondReviveHpPercent = 25;

    [Tooltip("현재 출격에서 세 번째 구조 시 최대 HP 기준으로 회복되는 비율입니다.")]
    [Range(1, 100)]
    [BalanceField]
    [Clamp(Min = 0, Max = 100)]
    [SerializeField] private int m_thirdReviveHpPercent = 10;

    private bool m_isDowned;
    private bool m_downTimerPaused;
    private float m_downTimeRemaining;
    private int m_reviveCount;
    private bool m_isDeathProcessing;

    /// <summary>현재 전투 중 누적된 부상 게이지입니다.</summary>
    public float CurrentInjuryGauge => m_currentInjuryGauge;

    /// <summary>결과 판정에 사용하는 반올림 부상 게이지입니다.</summary>
    public int RoundedInjuryGauge => Mathf.RoundToInt(m_currentInjuryGauge);

    /// <summary>현재까지 누적된 부상 게이지입니다. 기존 호출부 호환을 위해 반올림 값을 반환합니다.</summary>
    public int AccumulatedDamage => RoundedInjuryGauge;

    /// <summary>부상 게이지 최대값입니다.</summary>
    public float MaxInjuryGauge => Mathf.Max(1.0f, m_maxInjuryGauge);

    /// <summary>치명상 판정이 시작되는 반올림 부상 게이지 값입니다.</summary>
    public int CriticalInjuryMinGauge => Mathf.Clamp(m_criticalInjuryMinGauge, 0, Mathf.RoundToInt(MaxInjuryGauge));

    /// <summary>현재 부상 게이지 기준 부상 상태입니다.</summary>
    public CharacterInjuryState CurrentInjuryState => m_currentInjuryState;

    /// <summary>현재 부상 게이지를 최대 부상 게이지 기준 0~1 범위로 환산한 값입니다.</summary>
    public float InjuryGaugeNormalized => Mathf.Clamp01(m_currentInjuryGauge / MaxInjuryGauge);

    /// <summary>현재 다운(빈사) 상태인지 여부입니다.</summary>
    public bool IsDowned => m_isDowned;

    /// <summary>다운 타이머가 멈춰 있는지 여부입니다.</summary>
    /// <remarks>구조 상호작용이 진행되는 동안 멈춥니다. 멈춘 사이에는 남은 시간이 줄지 않습니다.</remarks>
    public bool IsDownTimerPaused => m_downTimerPaused;

    /// <summary>다운 상태를 버틸 수 있는 전체 시간(초)입니다.</summary>
    public float DownDuration => m_downDuration;

    /// <summary>다운 상태가 끝나기까지 남은 시간(초)입니다. 음수로 내려가지 않습니다.</summary>
    public float DownTimeRemaining => Mathf.Max(0.0f, m_downTimeRemaining);

    /// <summary>남은 다운 시간을 0~1 범위로 환산한 값입니다.</summary>
    /// <remarks>HUD 게이지 표시용입니다. 전체 시간이 0 이하이면 나눌 수 없으므로 0을 돌려줍니다.</remarks>
    public float DownTimeNormalized => m_downDuration > 0.0f
        ? Mathf.Clamp01(m_downTimeRemaining / m_downDuration)
        : 0.0f;

    /// <summary>이번 출격에서 이미 부활한 횟수입니다.</summary>
    public int ReviveCount => m_reviveCount;

    /// <summary>부활할 수 있는 최대 횟수입니다. 이 횟수를 넘기면 다시 살아날 수 없습니다.</summary>
    public int MaxReviveCount => m_maxReviveCount;

    /// <summary>부상 게이지가 변경될 때 발생합니다. 인자는 현재 부상 게이지와 정규화된 게이지 값입니다.</summary>
    public event Action<float, float> OnInjuryGaugeChanged;

    /// <summary>부상 게이지 기준 부상 상태가 변경될 때 발생합니다.</summary>
    public event Action<CharacterInjuryState> OnInjuryStateChanged;

    /// <summary>HP가 0에 도달해 다운(빈사) 상태로 진입해야 할 때 발생합니다.</summary>
    public event Action OnDown;

    /// <summary>
    /// 디버그 즉시 부활이 정상 부활 절차를 우회해 완료됐을 때 발생합니다.
    /// </summary>
    /// <remarks>
    /// 일반 구조는 구조 상호작용이 기립 애니메이션을 마무리하므로 이 이벤트를 발신하지 않습니다.
    /// 트레이너/Inspector의 즉시 부활은 그 상호작용을 건너뛰므로, 구독자는 다운 포즈에 남지 않도록
    /// 즉시 기립 상태를 정리해야 합니다.
    /// </remarks>
    public event Action OnDebugInstantRevive;

    /// <summary>다운 타이머가 바뀔 때 (남은 시간, 전체 시간) 순서로 알립니다.</summary>
    public event Action<float, float> OnDownTimerChanged;

    /// <summary>부활 횟수가 바뀔 때 (사용한 횟수, 최대 횟수) 순서로 알립니다.</summary>
    public event Action<int, int> OnReviveCountChanged;

#if UNITY_EDITOR
    protected new void OnValidate()
    {
        base.OnValidate();

        // 인스펙터에서 값을 만졌을 때도 같은 규칙을 적용합니다. 규칙 본문은 한 곳에만 둡니다.
        OnBalanceApplied();
    }
#endif

    /// <summary>
    /// 밸런스 값이 대입된 직후 필드 간 관계를 정리합니다.
    /// </summary>
    /// <remarks>
    /// 단일 필드 경계는 <see cref="ClampAttribute"/>가 담당하므로 여기 두지 않습니다.
    /// 여기 남은 것은 다른 필드가 경계라서 선언 하나로 표현할 수 없는 규칙들입니다.
    /// 부상 단계는 정상 &lt;= 경상 &lt;= 치명상 순서를 지켜야 하고, 현재 게이지는 최대치를 넘을 수 없습니다.
    ///
    /// 이 정리를 <c>OnValidate</c>에만 두면 안 됩니다. <c>OnValidate</c>는 에디터에서 값을 만질 때만 돌고
    /// 빌드에서는 아예 실행되지 않아, 시트에서 뒤집힌 단계 값이 들어와도 그대로 게임에 적용됩니다.
    /// Bind 직후에 한 번 도는 이 지점이 시트 경로를 막는 자리입니다.
    /// </remarks>
    public void OnBalanceApplied()
    {
        int maxRoundedGauge = Mathf.Max(1, Mathf.RoundToInt(m_maxInjuryGauge));
        m_normalInjuryMaxGauge = Mathf.Clamp(m_normalInjuryMaxGauge, 0, maxRoundedGauge);
        m_minorInjuryMaxGauge = Mathf.Clamp(m_minorInjuryMaxGauge, m_normalInjuryMaxGauge, maxRoundedGauge);
        int criticalMinLowerBound = Mathf.Min(m_minorInjuryMaxGauge + 1, maxRoundedGauge);
        m_criticalInjuryMinGauge = Mathf.Clamp(m_criticalInjuryMinGauge, criticalMinLowerBound, maxRoundedGauge);
        m_currentInjuryGauge = Mathf.Clamp(m_currentInjuryGauge, 0.0f, MaxInjuryGauge);

        // 클램프가 아니라 표시용 상태 재계산입니다.
        m_currentInjuryState = GetInjuryState(RoundedInjuryGauge);
    }

    private void Update()
    {
        if (!m_isDowned || m_isDead || m_downTimerPaused)
        {
            return;
        }

        m_downTimeRemaining = Mathf.Max(0.0f, m_downTimeRemaining - Time.deltaTime);
        NotifyDownTimerChanged();

        if (m_downTimeRemaining <= 0.0f)
        {
            ExpireDownTimer();
        }
    }

    /// <summary>
    /// 체력·부상 규칙 값을 SO에서 받아옵니다. 기반 클래스의 Start가 값을 쓰기 전에 실행됩니다.
    /// </summary>
    private void Awake()
    {
        BindConfiguredBalance();
    }

    /// <summary>
    /// 지정된 SO가 있을 때 공용 BindManager로 같은 이름의 필드 값을 적용합니다.
    /// </summary>
    /// <returns>이번 바인딩의 집계 결과입니다. SO가 없으면 기본값입니다.</returns>
    /// <remarks>
    /// 같은 SO를 이 오브젝트의 다른 컴포넌트도 각자 바인드합니다. 대상이 요구한 필드만 가져가므로
    /// 서로 간섭하지 않고, 컴포넌트 간 Awake 실행 순서에도 의존하지 않습니다.
    /// </remarks>
    private BalanceBindResult BindConfiguredBalance()
    {
        return BindFrom(m_balanceSO);
    }

    /// <summary>개별 밸런스 SO를 직접 물고 있는지 여부입니다.</summary>
    /// <remarks><c>true</c>면 <see cref="SOBinder"/>가 통합 SO 주입을 건너뜁니다.</remarks>
    public bool HasOwnBalance => m_balanceSO != null;

    /// <summary>엔티티 통합 밸런스 SO의 값을 적용합니다.</summary>
    /// <param name="balance">통합 밸런스 SO입니다.</param>
    /// <returns>이번 바인딩의 집계 결과입니다.</returns>
    /// <remarks>
    /// 개별 SO 슬롯은 비운 채로 둡니다. 비어 있다는 것 자체가 "개별 지정 없음"을 뜻합니다.
    /// 어느 경로로 들어오든 기반 클래스의 Start가 값을 쓰기 전에 끝나야 하므로, 호출은 Awake 단계에서 이루어집니다.
    /// </remarks>
    public BalanceBindResult BindSharedBalance(ScriptableObject balance)
    {
        return BindFrom(balance);
    }

    /// <summary>주어진 원본 SO에서 밸런스 값을 대입합니다.</summary>
    /// <param name="balance">값을 읽어올 밸런스 SO입니다. 개별 SO일 수도, 엔티티 통합 SO일 수도 있습니다.</param>
    /// <returns>이번 바인딩의 집계 결과입니다. 원본이 없으면 기본값입니다.</returns>
    private BalanceBindResult BindFrom(ScriptableObject balance)
    {
        if (balance == null)
        {
            return default;
        }

        return BindManager.Instance.Bind(balance, this, this);
    }

    /// <summary>체력과 다운·부활 상태를 출격 시작 시점으로 되돌립니다.</summary>
    /// <remarks>기반 클래스의 체력 초기화에 더해 이 컴포넌트가 소유한 다운·부활·부상 상태까지 함께 리셋합니다.</remarks>
    public override void InitializeHealth()
    {
        base.InitializeHealth();
        m_isDowned = false;
        m_downTimerPaused = false;
        m_downTimeRemaining = 0.0f;
        m_reviveCount = 0;
        m_isDeathProcessing = false;
        ResetInjuryDamage();
        NotifyDownTimerChanged();
        OnReviveCountChanged?.Invoke(m_reviveCount, MaxReviveCount);
    }

    /// <summary>
    /// 플레이어는 HP가 0에 도달해도 즉시 사망하지 않고 다운(빈사) 상태로 진입합니다.
    /// </summary>
    /// <remarks>
    /// 사망(<see cref="HealthSystemBase.Death"/>)은 구조 실패 등 특수 조건에서만 별도로 발동합니다.
    /// 따라서 여기서는 사망 플래그를 세우지 않고 다운 진입 이벤트만 발신합니다.
    /// </remarks>
    protected override void OnHpDepleted()
    {
        if (m_isDead || m_isDowned)
        {
            return;
        }

        if (m_reviveCount >= Mathf.Max(1, m_maxReviveCount))
        {
            Death();
            return;
        }

        m_isDowned = true;
        m_downTimerPaused = false;
        m_downTimeRemaining = DownDuration;
        OnDown?.Invoke();
        NotifyDownTimerChanged();

        if (m_downTimeRemaining <= 0.0f)
        {
            ExpireDownTimer();
        }
    }

    protected override void OnDamageApplied(float actualDamage, float previousHp)
    {
        AddInjuryGaugeFromDamage(actualDamage);
    }

    protected override DamageNumberDebug.TargetKind DamageNumberKind => DamageNumberDebug.TargetKind.Player;

    /// <summary>피해를 적용하고 그 결과 다운으로 넘어갔는지 판단합니다.</summary>
    /// <param name="info">피해량과 공격 방식, 공격자를 담은 정보입니다.</param>
    /// <returns>피해가 실제로 들어갔으면 <c>true</c>입니다.</returns>
    /// <remarks>HP가 0이 되어도 사망이 아니라 다운으로 갑니다. 사망은 별도 조건에서만 처리합니다.</remarks>
    public override bool TakeDamage(DamageInfo info)
    {
        // 무한 체력 디버그는 런타임 트레이너가 활성화된 Editor/Development Build에서만 효과가 있습니다.
        if (m_debugInfiniteHealth && GameDevMode.DebugFeaturesEnabled)
        {
            return false;
        }

        // 반무적 중에는 HP 1까지만 깎습니다. 이미 1 이하이면 피해를 무시합니다.
        if (KeepMinimumHp)
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

    /// <summary>
    /// 켜면 모든 대원의 HP가 1 아래로 내려가지 않습니다(반무적). 튜토리얼의 "반무적" 이벤트가 켜고 끕니다.
    /// </summary>
    public static bool KeepMinimumHp { get; set; }

    /// <summary>Play를 다시 시작할 때 반무적을 끕니다. 도메인 리로드를 끈 설정에서도 이전 값이 남지 않게 합니다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetKeepMinimumHp()
    {
        KeepMinimumHp = false;
    }

    /// <summary>
    /// 다운 상태에서 지정한 HP로 전투에 복귀시킵니다.
    /// </summary>
    /// <remarks>
    /// 플레이어 다운은 사망 플래그를 세우지 않는 HP 0 상태이므로,
    /// 기본 <see cref="HealthSystemBase.Revive"/> 대신 이 경로를 사용합니다.
    /// </remarks>
    /// <summary>지정한 체력으로 다운 상태에서 일으켜 세웁니다.</summary>
    /// <param name="amount">부활 후 회복시킬 체력입니다.</param>
    /// <returns>부활에 성공했으면 <c>true</c>입니다. 사망했거나 다운 상태가 아니면 <c>false</c>입니다.</returns>
    public bool ReviveFromDown(int amount)
    {
        if (m_isDead || m_currentHp > 0 || !m_isDowned)
        {
            return false;
        }

        if (m_reviveCount >= MaxReviveCount)
        {
            // 부활 횟수를 이미 다 쓴 상태입니다. 구조를 시도해도 되살리지 않고 즉시 전투 이탈(사망) 처리합니다.
            m_isDowned = false;
            m_downTimerPaused = false;
            m_downTimeRemaining = 0.0f;
            NotifyDownTimerChanged();
            Death();
            return false;
        }

        m_isDowned = false;
        m_downTimerPaused = false;
        m_downTimeRemaining = 0.0f;
        m_reviveCount = Mathf.Min(m_reviveCount + 1, MaxReviveCount);
        OnReviveCountChanged?.Invoke(m_reviveCount, MaxReviveCount);
        NotifyDownTimerChanged();

        return ReviveToHp(amount);
    }

    /// <summary>부활 횟수에 따라 정해진 체력으로 다운 상태에서 일으켜 세웁니다.</summary>
    /// <returns>부활에 성공했으면 <c>true</c>입니다.</returns>
    /// <remarks>회복량은 <see cref="CalculateNextReviveHp"/>가 정합니다. 구조 상호작용이 쓰는 기본 경로입니다.</remarks>
    public bool ReviveFromDown()
    {
        return ReviveFromDown(CalculateNextReviveHp());
    }

    /// <summary>다운 타이머를 멈추거나 다시 흐르게 합니다.</summary>
    /// <param name="value">멈추려면 <c>true</c>입니다.</param>
    /// <remarks>구조 상호작용이 진행되는 동안 멈춥니다. 다운 상태가 아니거나 사망했으면 항상 해제됩니다.</remarks>
    public void SetDownTimerPaused(bool value)
    {
        if (!m_isDowned || m_isDead)
        {
            m_downTimerPaused = false;
            return;
        }

        m_downTimerPaused = value;
    }

    /// <summary>다음 부활에서 회복될 체력을 미리 계산합니다.</summary>
    /// <returns>부활 횟수에 따른 회복 체력입니다. 최소 1이며 최대 체력을 넘지 않습니다.</returns>
    /// <remarks>구조 UI가 "살리면 얼마나 회복되는지"를 미리 보여줄 때 씁니다. 상태를 바꾸지 않습니다.</remarks>
    public int CalculateNextReviveHp()
    {
        int percent = GetReviveHpPercent(m_reviveCount + 1);
        return Mathf.Clamp(Mathf.CeilToInt(m_maxHp * (percent / 100.0f)), 1, m_maxHp);
    }

    /// <summary>캐릭터를 사망 처리합니다.</summary>
    /// <remarks>다운과 달리 되돌릴 수 없습니다. 중복 호출과 처리 중 재진입을 막습니다.</remarks>
    public override void Death()
    {
        if (m_isDead || m_isDeathProcessing)
        {
            return;
        }

        m_isDeathProcessing = true;

        try
        {
            SetCurrentInjuryGauge(Mathf.Max(m_currentInjuryGauge, CriticalInjuryMinGauge));

            bool wasDowned = m_isDowned;

            m_isDowned = false;
            m_downTimerPaused = false;
            m_downTimeRemaining = 0.0f;

            if (wasDowned)
            {
                NotifyDownTimerChanged();
            }

            base.Death();
        }
        finally
        {
            m_isDeathProcessing = false;
        }
    }

    private void ExpireDownTimer()
    {
        if (!m_isDowned || m_isDead)
        {
            return;
        }

        Death();
    }

    private void NotifyDownTimerChanged()
    {
        OnDownTimerChanged?.Invoke(DownTimeRemaining, DownDuration);
    }

    private int GetReviveHpPercent(int reviveNumber)
    {
        return reviveNumber switch
        {
            1 => m_firstReviveHpPercent,
            2 => m_secondReviveHpPercent,
            _ => m_thirdReviveHpPercent,
        };
    }

    /// <summary>
    /// 전달된 반올림 부상 게이지 기준의 부상 상태를 반환합니다.
    /// </summary>
    public CharacterInjuryState GetInjuryState(int roundedGauge)
    {
        roundedGauge = Mathf.Clamp(roundedGauge, 0, Mathf.RoundToInt(MaxInjuryGauge));

        if (roundedGauge >= CriticalInjuryMinGauge)
        {
            return CharacterInjuryState.Critical;
        }

        if (roundedGauge > m_minorInjuryMaxGauge)
        {
            return CharacterInjuryState.Serious;
        }

        if (roundedGauge > m_normalInjuryMaxGauge)
        {
            return CharacterInjuryState.Minor;
        }

        return CharacterInjuryState.Normal;
    }

    /// <summary>공용 캐릭터 스냅샷의 최대 HP, 현재 HP와 누적 부상 게이지를 전투 체력 상태에 적용합니다.</summary>
    /// <remarks>씬 입장 초기화 경로에서 사용하며 구조 횟수와 다운 타이머는 새 출격의 기본값으로 초기화합니다.</remarks>
    public void ApplySnapshotState(int currentHp, int maxHp, float injurySeverityGauge, float maxInjuryGauge)
    {
        m_isDowned = false;
        m_downTimerPaused = false;
        m_downTimeRemaining = 0.0f;
        m_reviveCount = 0;
        m_maxInjuryGauge = Mathf.Max(1.0f, maxInjuryGauge);
        m_isDead = false;
        m_stateAppliedBeforeStart = true;
        SetMaxHP(maxHp);
        SetCurrentHP(currentHp);
        m_currentInjuryGauge = Mathf.Clamp(injurySeverityGauge, 0.0f, MaxInjuryGauge);
        UpdateInjuryStateAndNotify();
        NotifyDownTimerChanged();
        OnReviveCountChanged?.Invoke(m_reviveCount, MaxReviveCount);
    }

    /// <summary>현재 출격에서 누적된 부상 게이지를 0으로 초기화합니다.</summary>
    public void ResetInjuryDamage()
    {
        SetCurrentInjuryGauge(0.0f);
    }

    /// <summary>현재 출격에서 누적된 부상 게이지를 0으로 초기화합니다.</summary>
    public void ResetInjuryGauge()
    {
        SetCurrentInjuryGauge(0.0f);
    }

    private void AddInjuryGaugeFromDamage(float actualDamage)
    {
        actualDamage = Mathf.Max(0, actualDamage);
        if (actualDamage <= 0 || m_maxHp <= 0)
        {
            return;
        }

        float healthDamageRatio = actualDamage / (float)m_maxHp;
        float deltaGauge = healthDamageRatio
            * MaxInjuryGauge
            * m_injuryConversionRatio
            * GetCurrentInjuryMultiplier();

        if (deltaGauge <= 0.0f)
        {
            return;
        }

        CharacterInjuryState previousState = m_currentInjuryState;
        float previousGauge = m_currentInjuryGauge;

        SetCurrentInjuryGauge(m_currentInjuryGauge + deltaGauge);
        LogInjuryStateChangeFromDamage(actualDamage, deltaGauge, previousGauge, previousState);
    }

    private float GetCurrentInjuryMultiplier()
    {
        return m_reviveCount switch
        {
            <= 0 => m_baseInjuryMultiplier,
            1 => m_revivedOnceInjuryMultiplier,
            2 => m_revivedTwiceInjuryMultiplier,
            _ => m_revivedThreeTimesInjuryMultiplier,
        };
    }

    private void SetCurrentInjuryGauge(float value)
    {
        float clampedValue = Mathf.Clamp(value, 0.0f, MaxInjuryGauge);
        if (Mathf.Approximately(m_currentInjuryGauge, clampedValue))
        {
            return;
        }

        m_currentInjuryGauge = clampedValue;
        UpdateInjuryStateAndNotify();
    }

    private void UpdateInjuryStateAndNotify()
    {
        CharacterInjuryState previousState = m_currentInjuryState;
        m_currentInjuryState = GetInjuryState(RoundedInjuryGauge);

        OnInjuryGaugeChanged?.Invoke(m_currentInjuryGauge, InjuryGaugeNormalized);

        if (m_currentInjuryState != previousState)
        {
            OnInjuryStateChanged?.Invoke(m_currentInjuryState);
        }
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void LogInjuryStateChangeFromDamage(
        float actualDamage,
        float deltaGauge,
        float previousGauge,
        CharacterInjuryState previousState)
    {
#if UNITY_EDITOR
        if (!m_debugLogInjuryStateChange || m_currentInjuryState == previousState)
        {
            return;
        }

        Debug.Log(
            $"[PlayerHealth] Injury state changed by damage. {previousState} -> {m_currentInjuryState}, " +
            $"damage={actualDamage}, gauge={previousGauge:0.##}->{m_currentInjuryGauge:0.##}, " +
            $"delta={deltaGauge:0.##}, rounded={RoundedInjuryGauge}, reviveCount={m_reviveCount}",
            this);
#endif
    }

#if UNITY_EDITOR
    [Foldout("Debug")]
    [Tooltip("켜면 Editor에서 피해/사망 로그를 출력합니다. Player 빌드에서는 호출 자체가 제거됩니다.")]
    [SerializeField] private bool m_debugLogHealth = false;

    [Foldout("Debug")]
    [Tooltip("켜면 피격으로 부상 상태가 바뀔 때 Editor 로그를 출력합니다. Player 빌드에서는 호출 자체가 제거됩니다.")]
    [SerializeField] private bool m_debugLogInjuryStateChange = false;

    protected override bool DebugLogHealthEnabled => m_debugLogHealth;
#endif

    // 아래 무한 체력/즉시 상태 전환은 플레이테스트 트레이너에서도 쓰기 위해 빌드에도 컴파일하고,
    // 실제 효과는 런타임 트레이너가 활성화된 Editor/Development Build에서만 동작합니다.
    [Foldout("Debug")]
    [Tooltip("켜면 피해를 전혀 받지 않습니다(무한 체력). 개발 모드에서만 효과가 있습니다.")]
    [SerializeField] private bool m_debugInfiniteHealth = false;

    [Foldout("Debug")]
    [Button("즉시 기절시키기")]
    /// <summary>즉시 다운 상태로 만듭니다. 개발 모드에서만 동작합니다.</summary>
    /// <remarks>런타임 트레이너 버튼용입니다. <see cref="GameDevMode.DebugFeaturesEnabled"/>가 꺼져 있으면 아무 일도 하지 않습니다.</remarks>
    public void Debug_InstantDown()
    {
        if (!GameDevMode.DebugFeaturesEnabled)
        {
            return;
        }

        if (m_isDead || m_isDowned)
        {
            return;
        }

        // OnHpDepleted의 다운 진입 분기와 동일한 처리입니다(부활 횟수 소진 판정은 디버그 목적상 건너뜁니다).
        m_currentHp = 0;
        m_isDowned = true;
        m_downTimerPaused = false;
        m_downTimeRemaining = DownDuration;
        NotifyHPChanged();
        OnDown?.Invoke();
        NotifyDownTimerChanged();
    }

    [Foldout("Debug")]
    [Button("즉시 전투 이탈")]
    /// <summary>즉시 전투 불능(사망) 상태로 만듭니다. 개발 모드에서만 동작합니다.</summary>
    /// <remarks>런타임 트레이너 버튼용입니다.</remarks>
    public void Debug_InstantCombatOut()
    {
        if (!GameDevMode.DebugFeaturesEnabled)
        {
            return;
        }

        // 실제 사망 경로(Death)를 그대로 재사용해 OnDeath 구독자(조작 전환, 애니메이터 등)가 정상 동작합니다.
        Death();
    }

    [Foldout("Debug")]
    [Button("즉시 살리기")]
    /// <summary>구조 상호작용을 건너뛰고 즉시 부활시킵니다. 개발 모드에서만 동작합니다.</summary>
    /// <remarks>기립 애니메이션을 거치지 않으므로 <see cref="OnDebugInstantRevive"/>로 구독자에게 자세 정리를 알립니다.</remarks>
    public void Debug_InstantRevive()
    {
        if (!GameDevMode.DebugFeaturesEnabled)
        {
            return;
        }

        // 디버그 전용: 사망(전투 이탈)·부활 횟수 소진 상태에서도 강제로 되살립니다.
        // 정상 게임플레이에서는 사망은 되돌릴 수 없고 부활 횟수도 이렇게 초기화되지 않습니다.
        m_isDowned = false;
        m_downTimerPaused = false;
        m_downTimeRemaining = 0.0f;
        NotifyDownTimerChanged();

        m_reviveCount = 0;
        OnReviveCountChanged?.Invoke(m_reviveCount, MaxReviveCount);

        ResetInjuryDamage();
        ReviveToHp(m_maxHp);
        OnDebugInstantRevive?.Invoke();
    }

    /// <summary>켜면 피해를 전혀 받지 않습니다(무한 체력). 디버그 트레이너에서 사용하며 개발 모드에서만 효과가 있습니다.</summary>
    public bool DebugInfiniteHealth
    {
        get => m_debugInfiniteHealth;
        set => m_debugInfiniteHealth = value;
    }
}
