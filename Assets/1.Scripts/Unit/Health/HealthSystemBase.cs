using System;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using VInspector;

/// <summary>
/// HP, 피해, 회복, 사망, 부활, 선택적 HP UI 갱신을 공통으로 처리하는 체력 컴포넌트입니다.
/// </summary>
public class HealthSystemBase : MonoBehaviour, IDamageable
{
    [Foldout("Faction Options")]
    [Tooltip("이 유닛의 진영입니다. None이면 gameObject의 레이어 번호에서 진영을 추론합니다.")]
    [SerializeField] protected Faction m_faction = Faction.None;

    [Foldout("HP Options")]
    [Tooltip("최대 HP입니다.")]
    [Clamp(Min = 1)]
    [FormerlySerializedAs("m_maxHP")]
    [SerializeField] protected int m_maxHp = 10;

    [Tooltip("현재 HP입니다. Start에서 최대 HP로 초기화됩니다. 소수 피해를 정확히 쌓으려고 실수로 저장하고, 표시는 정수(올림)로 합니다.")]
    [FormerlySerializedAs("m_currentHP")]
    [SerializeField] protected float m_currentHp;

    [Foldout("UI Options")]
    [Tooltip("선택 사항인 HP 슬라이더입니다. 이 참조가 없어도 체력 로직은 동작합니다.")]
    [FormerlySerializedAs("hpSlider")]
    [SerializeField] protected Slider m_hpSlider;

    [Tooltip("선택 사항인 HP 텍스트입니다. 이 참조가 없어도 체력 로직은 동작합니다.")]
    [FormerlySerializedAs("hpText")]
    [SerializeField] protected TextMeshProUGUI m_hpText;

    [Tooltip("월드 공간 HP 바가 항상 카메라를 보게 할지 여부입니다. 파생 클래스는 따로 배선하지 않아도 이 설정을 그대로 씁니다.")]
    [SerializeField] protected bool m_billboardHpBar = true;

    [Tooltip("빌보드를 적용할 HP 바 루트입니다. 비우면 HP 슬라이더가 붙은 Canvas를, 그것도 없으면 슬라이더 자신을 씁니다.")]
    [ShowIf(nameof(m_billboardHpBar))]
    [SerializeField] protected Transform m_hpBarRoot;

    [Tooltip("빌보드를 수평 회전(Yaw)만 적용할지 여부입니다. 끄면 카메라 기울기까지 그대로 따라갑니다.")]
    [ShowIf(nameof(m_billboardHpBar))]
    [SerializeField] protected bool m_hpBarBillboardYawOnly = true;
    [EndIf]

#if UNITY_EDITOR
    private const int DebugDownDamage = 9999;

    protected virtual bool DebugLogHealthEnabled => false;

    [Foldout("Debug")]
    [Button("Take 9999 Damage")]
    private void DebugTake9999Damage()
    {
        TakeDamage(DamageInfo.Debug(DebugDownDamage, "Inspector 9999"));
    }
#endif

    /// <summary>디버그 피해 숫자의 색을 정하는 대상 종류입니다. 플레이어와 방어 목표물이 바꿔 씁니다.</summary>
    /// <remarks>자식 클래스가 재정의하므로 에디터 전용 블록 밖에 둡니다. 안에 두면 플레이어 빌드에서 재정의할 대상이 없어 컴파일이 깨집니다.</remarks>
    protected virtual DamageNumberDebug.TargetKind DamageNumberKind => DamageNumberDebug.TargetKind.Enemy;

    protected bool m_isDead;

    /// <summary>표시용 현재 HP입니다. 실제 값을 올림한 정수라, 0.3이 남아도 1로 보이고 0 이하일 때만 0입니다.</summary>
    /// <remarks>부동소수 오차로 10.0000001이 11로 올라가지 않게 아주 작은 값을 빼고 올림합니다.</remarks>
    public int CurrentHP => Mathf.Max(0, Mathf.CeilToInt(m_currentHp - HpDisplayEpsilon));

    /// <summary>소수까지 포함한 실제 현재 HP입니다.</summary>
    public float CurrentHPExact => m_currentHp;

    /// <summary>표시용 올림에서 부동소수 오차를 무시할 크기입니다.</summary>
    private const float HpDisplayEpsilon = 0.0001f;

    /// <summary>최대 HP입니다.</summary>
    public int MaxHP => m_maxHp;

    /// <summary>이 체력 컴포넌트가 사망 상태인지 여부입니다.</summary>
    public bool IsDead => m_isDead;

    /// <summary>
    /// 이 유닛의 진영입니다. 직렬화 값이 <see cref="Faction.None"/>이면 <see cref="DefaultFaction"/>을 씁니다.
    /// </summary>
    public Faction Faction => m_faction != Faction.None
        ? m_faction
        : DefaultFaction;

    /// <summary>
    /// 직렬화 값이 없을 때 사용할 진영입니다.
    /// </summary>
    /// <remarks>
    /// 기본 구현은 레이어 번호에서 추론합니다(레이어 번호 == 진영 enum 값).
    /// 다만 레이어는 히트박스 분리 같은 이유로 바뀔 수 있어서, 진영을 레이어에 묶어 두면
    /// 레이어를 옮기는 순간 조용히 <see cref="Faction.None"/>이 되어 피해가 전혀 오가지 않게 됩니다.
    /// 실제로 그 사고가 있었으므로, 진영이 정해진 파생 타입은 이 값을 고정해 결합을 끊습니다.
    /// </remarks>
    protected virtual Faction DefaultFaction => LayerToFaction(gameObject.layer);

    /// <summary>
    /// 레이어 번호를 진영으로 환산합니다. 진영 enum 값이 레이어 번호와 동일하게
    /// 맞춰져 있어, 알려진 레이어면 그대로 캐스팅합니다.
    /// </summary>
    protected static Faction LayerToFaction(int layer)
    {
        Faction candidate = (Faction)layer;
        return candidate == Faction.Player
            || candidate == Faction.Enemy
            || candidate == Faction.NPC
            ? candidate
            : Faction.None;
    }

    /// <summary>현재 HP나 최대 HP가 변경될 때 발생합니다. 인자는 현재 HP와 최대 HP입니다.</summary>
    public event Action<int, int> OnHPChanged;

    /// <summary>HP가 0에 도달해 컴포넌트가 사망 상태로 진입할 때 발생합니다.</summary>
    public event Action OnDeath;

    /// <summary>부활 또는 전체 회복으로 컴포넌트가 사망 상태에서 벗어날 때 발생합니다.</summary>
    public event Action OnRevive;

    /// <summary>피해로 현재 HP가 감소했을 때 발생합니다.</summary>
    /// <remarks>
    /// 인자는 실제로 감소한 HP와 피해를 입힌 대상입니다. 공격자를 모르는 경로로 들어온 피해는 null입니다.
    /// 공격자를 이벤트에 함께 싣는 이유는, 그 정보가 피해 발생 순간에만 존재하기 때문입니다.
    /// 별도 필드에 보관해 두고 나중에 조회하는 방식은 같은 프레임에 두 발을 맞으면 덮어써집니다.
    /// </remarks>
    public event Action<float, GameObject> OnDamaged;

    /// <summary>마지막으로 실제 들어간 피해의 정보입니다. 피해량은 실제로 깎인 HP입니다. 아직 맞지 않았으면 기본값입니다.</summary>
    public DamageInfo LastDamage { get; private set; }

    /// <summary>
    /// HP를 0으로 만든 피해의 정보(사인)입니다. HP가 0이 된 적이 없거나, 부활·회복으로 다시 살아났으면 기본값입니다.
    /// </summary>
    /// <remarks>플레이어처럼 HP 0이 다운인 대상도 다운시킨 피해가 여기에 남습니다.</remarks>
    public DamageInfo FatalDamage { get; private set; }

    private void Start()
    {
        // 출격 데이터가 이미 체력을 정했으면 다시 가득 채우지 않습니다. 방어전 데이터 매니저는 실행 순서가 앞서
        // (-100) 이 Start보다 먼저 셸터의 HP·부상을 적용하는데, 여기서 무조건 초기화하면 그 값이 매번 지워졌습니다.
        if (!m_stateAppliedBeforeStart)
        {
            InitializeHealth();
        }

        SetupHpBarBillboard();
    }

    /// <summary>Start 전에 외부(출격 스냅샷)가 체력 상태를 정했는지 여부입니다. 정했으면 Start에서 초기화하지 않습니다.</summary>
    protected bool m_stateAppliedBeforeStart;

    /// <summary>
    /// HP 바가 항상 카메라를 보도록 <see cref="BillboardUI"/>를 붙여 둡니다.
    /// </summary>
    /// <remarks>
    /// 회전 계산을 여기에 다시 쓰지 않고 기존 <see cref="BillboardUI"/>에 맡깁니다. 같은 규칙을 두 벌 두면
    /// 한쪽만 고쳐지기 때문입니다. 이미 붙어 있으면(수동 배선한 씬이 있습니다) 그대로 두고 건드리지 않습니다.
    ///
    /// 파생 클래스는 아무것도 하지 않아도 이 동작을 물려받습니다. 끄려면 <c>m_billboardHpBar</c>를 내리면 됩니다.
    /// </remarks>
    protected void SetupHpBarBillboard()
    {
        if (!m_billboardHpBar)
        {
            return;
        }

        Transform root = ResolveHpBarRoot();
        if (root == null)
        {
            return;
        }

        // 씬에서 이미 배선해 둔 빌보드가 있으면 그 설정을 존중합니다.
        if (root.GetComponent<BillboardUI>() != null)
        {
            return;
        }

        BillboardUI billboard = root.gameObject.AddComponent<BillboardUI>();
        billboard.SetYawOnly(m_hpBarBillboardYawOnly);
    }

    /// <summary>빌보드를 적용할 HP 바 루트를 정합니다. 없으면 <c>null</c>입니다.</summary>
    /// <remarks>
    /// 슬라이더 자체가 아니라 그것이 속한 Canvas를 기본으로 삼습니다. 슬라이더만 돌리면 같은 Canvas 안의
    /// HP 텍스트가 따라 돌지 않아 서로 어긋납니다.
    /// </remarks>
    private Transform ResolveHpBarRoot()
    {
        if (m_hpBarRoot != null)
        {
            return m_hpBarRoot;
        }

        if (m_hpSlider == null)
        {
            return null;
        }

        Canvas canvas = m_hpSlider.GetComponentInParent<Canvas>();
        return canvas != null ? canvas.transform : m_hpSlider.transform;
    }

#if UNITY_EDITOR
    /// <remarks>
    /// 단일 필드 경계는 <see cref="ClampAttribute"/>가 담당하므로 여기 두지 않습니다.
    /// 남은 것은 현재 HP가 최대 HP를 넘지 못한다는 런타임 상태 규칙이며, 다른 필드가 상한이라 선언으로 표현할 수 없습니다.
    /// </remarks>
    protected void OnValidate()
    {
        m_currentHp = Mathf.Clamp(m_currentHp, 0, m_maxHp);
    }
#endif

    /// <summary>
    /// 현재 HP를 최대 HP로 초기화하고 사망 상태를 해제합니다.
    /// </summary>
    public virtual void InitializeHealth()
    {
        m_maxHp = Mathf.Max(1, m_maxHp);
        m_currentHp = m_maxHp;
        m_isDead = false;
        FatalDamage = default;

        NotifyHPChanged();
    }

    /// <summary>
    /// 최대 HP를 설정합니다. 필요하면 현재 HP도 새 최대 HP로 채웁니다.
    /// </summary>
    public void SetMaxHP(int value, bool fillCurrentHp = false)
    {
        m_maxHp = Mathf.Max(1, value);

        if (fillCurrentHp)
        {
            RestoreFull();
            return;
        }

        m_currentHp = Mathf.Clamp(m_currentHp, 0, m_maxHp);
        RefreshDeathState();
        NotifyHPChanged();
    }

    /// <summary>
    /// 현재 HP를 설정하고 필요하면 사망 상태를 갱신합니다.
    /// </summary>
    public void SetCurrentHP(int value)
    {
        m_currentHp = Mathf.Clamp(value, 0, m_maxHp);
        RefreshDeathState();
        NotifyHPChanged();
    }

    /// <summary>
    /// 피해를 적용합니다. HP가 실제로 변경된 경우에만 true를 반환합니다. 공격 방식은 <see cref="DamageCategory.Unknown"/>으로 기록됩니다.
    /// </summary>
    /// <param name="damage">적용할 피해량입니다.</param>
    /// <param name="attacker">피해를 입힌 대상입니다. 디버그처럼 공격자가 없는 경로는 null입니다.</param>
    /// <remarks>피해 처리는 <see cref="TakeDamage(DamageInfo)"/> 한 곳에만 둡니다.</remarks>
    public bool TakeDamage(int damage, GameObject attacker = null)
    {
        return TakeDamage(DamageInfo.Unknown(damage, attacker));
    }

    /// <summary>
    /// 소수 피해를 적용합니다. HP가 실제로 변경된 경우에만 true를 반환합니다. 공격 방식은 <see cref="DamageCategory.Unknown"/>으로 기록됩니다.
    /// </summary>
    /// <param name="damage">적용할 피해량입니다.</param>
    /// <param name="attacker">피해를 입힌 대상입니다. 디버그처럼 공격자가 없는 경로는 null입니다.</param>
    public bool TakeDamage(float damage, GameObject attacker = null)
    {
        return TakeDamage(DamageInfo.Unknown(damage, attacker));
    }

    /// <summary>
    /// 피해 정보를 받아 적용합니다. HP가 실제로 변경된 경우에만 true를 반환합니다.
    /// </summary>
    /// <param name="info">피해량과 공격 방식, 세부 종류, 공격자를 담은 정보입니다.</param>
    /// <remarks>실제로 깎인 HP를 담아 <see cref="LastDamage"/>에, HP를 0으로 만들었으면 <see cref="FatalDamage"/>에도 남깁니다.</remarks>
    public virtual bool TakeDamage(DamageInfo info)
    {
        if (m_isDead)
        {
            return false;
        }

        // int.MaxValue 같은 큰 값이 float로 넘어와도 그대로 HP를 0으로 만듭니다. NaN은 피해로 보지 않습니다.
        float damage = info.Amount;
        if (float.IsNaN(damage) || damage <= 0.0f)
        {
            return false;
        }

        float previousHp = m_currentHp;
        m_currentHp = Mathf.Max(m_currentHp - damage, 0.0f);
        float actualDamage = previousHp - m_currentHp;

        if (actualDamage <= 0.0f)
        {
            return false;
        }

        LastDamage = info.WithAmount(actualDamage);
        OnDamageApplied(actualDamage, previousHp);
        DamageNumberDebug.Report(this, actualDamage, DamageNumberKind);

        LogHealthDebug($"[HealthSystem] Hit. Current HP : {m_currentHp} | {LastDamage}");

        NotifyHPChanged();
        OnDamaged?.Invoke(actualDamage, info.Attacker);

        if (m_currentHp <= 0.0f)
        {
            FatalDamage = LastDamage;
            LogHealthDebug($"[HealthSystem] HP 0 | 사인: {FatalDamage}");
            OnHpDepleted();
        }

        return true;
    }

    /// <summary>
    /// HP를 회복합니다. HP가 실제로 변경된 경우에만 true를 반환합니다.
    /// </summary>
    public bool Heal(int amount)
    {
        if (m_isDead)
        {
            return false;
        }

        amount = Mathf.Max(0, amount);
        if (amount <= 0)
        {
            return false;
        }

        float previousHp = m_currentHp;
        m_currentHp = Mathf.Min(m_currentHp + amount, m_maxHp);

        if (Mathf.Approximately(m_currentHp, previousHp))
        {
            return false;
        }

        NotifyHPChanged();
        return true;
    }

    /// <summary>
    /// 현재 HP를 최대 HP로 회복합니다. 사망 상태라면 컴포넌트도 함께 부활시킵니다.
    /// </summary>
    public void RestoreFull()
    {
        bool wasDead = m_isDead;

        m_currentHp = m_maxHp;
        m_isDead = false;
        FatalDamage = default;

        if (wasDead)
        {
            OnRevive?.Invoke();
        }

        NotifyHPChanged();
    }

    /// <summary>
    /// 지정한 HP 값으로 사망 상태에서 부활합니다.
    /// </summary>
    public bool Revive(int amount)
    {
        if (!m_isDead)
        {
            return false;
        }

        return ReviveToHp(amount);
    }

    /// <summary>
    /// HP를 지정한 값으로 회복하고 부활 이벤트를 발생시킵니다.
    /// 사망 상태뿐 아니라 다운처럼 HP 0이지만 사망은 아닌 상태에서도 재사용합니다.
    /// </summary>
    protected bool ReviveToHp(int amount)
    {
        m_currentHp = Mathf.Clamp(amount, 1, m_maxHp);
        m_isDead = false;
        FatalDamage = default;

        OnRevive?.Invoke();
        NotifyHPChanged();

        return true;
    }

    /// <summary>
    /// 현재 HP 값에 맞춰 선택 사항인 UI 참조를 갱신합니다.
    /// </summary>
    protected void UpdateUI()
    {
        if (m_hpSlider != null)
        {
            m_hpSlider.value = m_maxHp > 0
                ? m_currentHp / m_maxHp
                : 0.0f;
        }

        if (m_hpText != null)
        {
            m_hpText.text = $"HP {CurrentHP} / {m_maxHp}";
        }
    }

    /// <summary>
    /// UI를 갱신하고 HP 변경 이벤트를 발생시킵니다. 이벤트의 현재 HP는 표시용 정수입니다.
    /// </summary>
    protected void NotifyHPChanged()
    {
        UpdateUI();
        OnHPChanged?.Invoke(CurrentHP, m_maxHp);
    }

    private void RefreshDeathState()
    {
        if (m_currentHp <= 0.0f)
        {
            if (!m_isDead)
            {
                OnHpDepleted();
            }

            return;
        }

        m_isDead = false;
    }

    /// <summary>
    /// HP가 0에 도달했을 때의 처리입니다. 기본 동작은 즉시 사망(<see cref="Death"/>)입니다.
    /// </summary>
    /// <remarks>
    /// 다운(빈사)이 가능한 유닛(플레이어)은 이 메서드를 재정의해 사망 대신 다운으로 분기합니다.
    /// 그 경우 사망은 특수 조건에서만 <see cref="Death"/>를 직접 호출해 발동합니다.
    /// </remarks>
    protected virtual void OnHpDepleted()
    {
        Death();
    }

    /// <summary>
    /// 실제 피해가 HP에 반영된 직후 호출되는 확장 지점입니다.
    /// </summary>
    /// <param name="actualDamage">이번 피격으로 실제 감소한 HP입니다. 소수일 수 있습니다.</param>
    /// <param name="previousHp">피격 전 HP입니다.</param>
    protected virtual void OnDamageApplied(float actualDamage, float previousHp)
    {
    }

    /// <summary>
    /// 사망 상태로 진입하고 사망 이벤트를 발생시킵니다.
    /// </summary>
    /// <remarks>
    /// HP 0 기본 처리(<see cref="OnHpDepleted"/>) 외에, 특수한 사망 조건에서 외부 시스템이 직접 호출할 수 있습니다.
    /// </remarks>
    public virtual void Death()
    {
        if (m_isDead)
        {
            return;
        }

        m_isDead = true;

        LogHealthDebug("[HealthSystem] Dead");
        OnDeath?.Invoke();
    }

    /// <summary>디버그 플래그가 켜져 있을 때만 로그를 남깁니다.</summary>
    /// <remarks>
    /// <c>Conditional</c>이라 비-Editor 빌드에서는 호출 자체가 사라집니다. 문자열 보간 인자도 함께 제거되므로
    /// 로그를 꺼 둔 상태에서 문자열 조립 비용이 남지 않습니다.
    /// 파생 클래스(경직 등)가 같은 플래그로 자기 진단을 남길 수 있도록 protected입니다.
    /// </remarks>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    protected void LogHealthDebug(string message)
    {
#if UNITY_EDITOR
        if (DebugLogHealthEnabled)
        {
            Debug.Log(message, this);
        }
#endif
    }
}
