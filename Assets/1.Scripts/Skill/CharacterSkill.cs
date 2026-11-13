using System;
using UnityEngine;

/// <summary>캐릭터가 보유한 스킬의 공통 상태와 쿨다운을 관리하는 기반 컴포넌트입니다.</summary>
/// <remarks>
/// 스킬마다 자기 밸런스 SO(<see cref="BalanceSource"/>)를 가집니다. <see cref="BalanceFieldAttribute"/>가 붙은 필드는
/// Awake에서 그 SO 값으로 덮입니다. SO 슬롯이 비어 있으면 인스펙터 값을 그대로 씁니다.
/// </remarks>
[RequireComponent(typeof(SquadMemberController))]
public abstract class CharacterSkill : MonoBehaviour
{
    [Tooltip("스킬을 다시 사용할 수 있을 때까지의 시간(초)입니다. 밸런스 SO가 있으면 SO 값으로 덮입니다.")]
    [Min(0.0f)]
    [BalanceField]
    [SerializeField] private float m_cooldownDuration = 30.0f;

    private float m_nextReadyTime;
    private SquadMemberController m_owner;

    /// <summary>UI와 외부 시스템이 구분할 스킬 종류입니다.</summary>
    public abstract CharacterSkillType SkillType { get; }

    /// <summary>UI에 표시할 스킬 이름입니다.</summary>
    public abstract string DisplayName { get; }

    /// <summary>UI에 표시할 짧은 스킬 설명입니다.</summary>
    public abstract string Description { get; }

    /// <summary>이 스킬을 소유한 스쿼드 멤버입니다.</summary>
    public SquadMemberController Owner => m_owner;

    /// <summary>전체 쿨다운 시간입니다.</summary>
    public float CooldownDuration => Mathf.Max(0.0f, m_cooldownDuration);

    /// <summary>남은 쿨다운 시간입니다.</summary>
    public float CooldownRemaining => Mathf.Max(0.0f, m_nextReadyTime - Time.time);

    /// <summary>스킬을 지금 사용할 수 있는지 여부입니다.</summary>
    /// <remarks>
    /// 기본은 효과가 끝나야 다시 쓸 수 있습니다. <see cref="CanRecastWhileActive"/>가 true인 스킬은 효과가 남아 있어도
    /// 쿨타임만 끝나면 다시 쓸 수 있습니다.
    /// </remarks>
    public bool IsReady => CooldownRemaining <= 0.0f && (!IsActive || CanRecastWhileActive);

    /// <summary>효과가 남아 있어도 쿨타임이 끝나면 다시 시전할 수 있는지 여부입니다. 기본은 false입니다.</summary>
    protected virtual bool CanRecastWhileActive => false;

    /// <summary>남은 쿨다운을 없앱니다. 디버그 트레이너 전용입니다. 진행 중인 지속 효과는 건드리지 않습니다.</summary>
    public void DebugResetCooldown()
    {
        m_nextReadyTime = 0.0f;
    }

    /// <summary>지속 효과 또는 장전된 특수탄이 활성 상태인지 여부입니다.</summary>
    public virtual bool IsActive => false;

    /// <summary>지속 효과의 전체 시간입니다. 지속 시간이 없는 스킬은 0입니다.</summary>
    public virtual float ActiveDuration => 0.0f;

    /// <summary>지속 효과의 남은 시간입니다.</summary>
    public virtual float ActiveRemaining => 0.0f;

    /// <summary>스킬 HUD의 쿨타임 아래 줄에 표시할 상태 문구입니다. 표시할 내용이 없으면 null입니다.</summary>
    public virtual string HudDetailText => null;

    /// <summary>
    /// 초상화 옆 상태 줄(버프)에 띄울 스킬 고유 상태를 돌려줍니다. 띄울 상태가 없으면 false입니다.
    /// </summary>
    /// <param name="fill01">칸 뒤 게이지 채움(0~1)입니다.</param>
    /// <param name="count">칸 우측 하단에 작게 쓸 숫자입니다. 음수면 숫자를 숨깁니다.</param>
    /// <remarks>
    /// 상태효과(<see cref="StatusEffectContainer"/>)로 표현되지 않는 스킬 상태(예: 청솔 특수탄 장전)를 상태 줄에 보여 주기 위한 것입니다.
    /// 스킬 쿨타임과는 별개이며, 쿨타임은 스킬 아이콘이 따로 표시합니다.
    /// </remarks>
    public virtual bool TryGetHudStatus(out float fill01, out int count)
    {
        fill01 = 0.0f;
        count = -1;
        return false;
    }

    /// <summary>활성·종료 등 큰 상태가 변경될 때 발생합니다. 매 프레임 남은 시간은 속성으로 조회합니다.</summary>
    public event Action<CharacterSkill> OnStateChanged;

    /// <summary>어느 캐릭터든 스킬 발동에 성공했을 때 발생합니다. 튜토리얼처럼 특정 캐릭터를 잡지 않고 스킬 사용을 셀 때 씁니다.</summary>
    public static event Action<CharacterSkill> OnAnyActivated;

    /// <summary>이 스킬의 밸런스 수치 SO입니다. 비어 있으면 null이며, 인스펙터 값을 그대로 씁니다.</summary>
    protected abstract ScriptableObject BalanceSource { get; }

    /// <summary>스킬이 밸런스 SO를 물고 있는지 여부입니다.</summary>
    public bool HasBalanceSource => BalanceSource != null;

    protected virtual void Awake()
    {
        m_owner = GetComponent<SquadMemberController>();

        // 쿨타임 등 기본값이 쓰이기 전에 SO 값을 먼저 넣습니다.
        BindBalance(this);
    }

    /// <summary>이 스킬의 밸런스 SO 값을 <paramref name="target"/>의 [BalanceField] 필드에 주입합니다.</summary>
    /// <param name="target">값을 받을 대상입니다. 스킬 자신이거나, 스킬이 따로 쓰는 효과 컴포넌트입니다.</param>
    /// <returns>주입 결과입니다. SO가 없으면 기본값(대입 0건)입니다.</returns>
    /// <remarks>
    /// 스킬 효과가 별도 컴포넌트에 있으면(예: 청솔의 용숨결 판정) 같은 SO를 그 컴포넌트에도 넣어,
    /// 스킬 하나의 수치가 SO 하나에 모이게 합니다. SO 필드 이름은 받는 쪽 타입 이름이 접두사라 서로 겹치지 않습니다.
    /// </remarks>
    protected BalanceBindResult BindBalance(object target)
    {
        ScriptableObject balance = BalanceSource;
        return balance != null && target != null
            ? BindManager.Instance.Bind(balance, target, this)
            : default;
    }

    /// <summary>현재 조작 멤버의 스킬 발동을 시도합니다.</summary>
    public bool TryActivate(SquadManager squadManager)
    {
        if (squadManager == null || m_owner == null || !m_owner.IsAlive || m_owner.IsDown || !IsReady)
        {
            return false;
        }

        if (!ActivateSkill(squadManager))
        {
            return false;
        }

        m_nextReadyTime = Time.time + CooldownDuration;
        NotifyStateChanged();
        OnAnyActivated?.Invoke(this);
        return true;
    }

    /// <summary>캐릭터별 실제 스킬 효과를 실행합니다. 효과가 성립했을 때만 true를 반환합니다.</summary>
    protected abstract bool ActivateSkill(SquadManager squadManager);

    protected void NotifyStateChanged()
    {
        OnStateChanged?.Invoke(this);
    }
}

/// <summary>플레이어블 캐릭터가 보유하는 스킬 종류입니다.</summary>
public enum CharacterSkillType
{
    NarinTeamHaste,
    ChungSolDragonBreath,
    SeoHaTeamHeal,
}
