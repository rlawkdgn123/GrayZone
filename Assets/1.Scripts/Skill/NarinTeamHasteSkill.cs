using System.Collections;
using UnityEngine;

/// <summary>일정 시간 동안 전 스쿼드의 이동·행동 속도를 높이는 나린의 스킬입니다.</summary>
public sealed class NarinTeamHasteSkill : CharacterSkill
{
    private const string HasteResourcePath = "StatusEffects/NarinTeamHaste";
    private const string ActivationVisualResourcePath = "StatusEffectVisuals/NarinHasteActivation";

    [Tooltip("이 스킬의 밸런스 수치 SO입니다(쿨타임). 비어 있으면 인스펙터 값을 그대로 씁니다. " +
             "가속 수치와 지속시간은 아래 상태효과 SO가 소유합니다.")]
    [SerializeField] private NarinTeamHasteSkillSO m_balanceSO;

    [Tooltip("전 스쿼드에 적용할 이동·행동속도 상태효과입니다. 비어 있으면 Resources 기본 에셋을 사용합니다.")]
    [SerializeField] private StatusEffectDefinitionSO m_hasteEffect;

    [Tooltip("전술 가속 발동 순간 각 팀원에게 한 번 재생할 VFX입니다. 비어 있으면 Resources 기본 에셋을 사용합니다.")]
    [SerializeField] private StatusEffectVisualSO m_activationVisual;

    private Coroutine m_effectRoutine;
    private float m_effectEndTime;

    protected override ScriptableObject BalanceSource => m_balanceSO;

    public override CharacterSkillType SkillType => CharacterSkillType.NarinTeamHaste;
    public override string DisplayName => "전술 가속";
    public override string Description => "팀원 전체의 이동속도와 행동속도를 증가시킵니다";
    public override bool IsActive => m_effectRoutine != null;
    public override float ActiveDuration => ResolveHasteEffect() != null ? ResolveHasteEffect().Duration : 0.0f;
    public override float ActiveRemaining => IsActive ? Mathf.Max(0.0f, m_effectEndTime - Time.time) : 0.0f;
    public override string HudDetailText => IsActive
        ? $"지속 시간 {ActiveRemaining:0.0} / {ActiveDuration:0.0}초"
        : $"지속 시간 {ActiveDuration:0.0}초";

    protected override bool ActivateSkill(SquadManager squadManager)
    {
        StatusEffectDefinitionSO effect = ResolveHasteEffect();
        if (effect == null || squadManager.SquadMembers == null || squadManager.SquadMembers.Count == 0)
        {
            return false;
        }

        m_effectEndTime = Time.time + ActiveDuration;
        squadManager.ApplySquadStatusEffect(effect, Faction.Player, gameObject);
        PlayActivationVisual(squadManager);
        m_effectRoutine = StartCoroutine(RunEffect());
        return true;
    }

    private IEnumerator RunEffect()
    {
        if (ActiveDuration > 0.0f)
        {
            yield return new WaitForSeconds(ActiveDuration);
        }

        FinishEffect();
    }

    private void OnDisable()
    {
        if (m_effectRoutine != null)
        {
            StopCoroutine(m_effectRoutine);
            FinishEffect();
        }
    }

    private void FinishEffect()
    {
        m_effectRoutine = null;
        m_effectEndTime = 0.0f;
        NotifyStateChanged();
    }

    private StatusEffectDefinitionSO ResolveHasteEffect()
    {
        if (m_hasteEffect == null)
        {
            m_hasteEffect = Resources.Load<StatusEffectDefinitionSO>(HasteResourcePath);
        }

        return m_hasteEffect;
    }

    private void PlayActivationVisual(SquadManager squadManager)
    {
        StatusEffectVisualSO visual = ResolveActivationVisual();
        if (visual == null || squadManager == null || squadManager.SquadMembers == null)
        {
            return;
        }

        for (int i = 0; i < squadManager.SquadMembers.Count; i++)
        {
            SquadMemberController member = squadManager.SquadMembers[i];
            if (member != null)
            {
                member.StatusEffects.PlayVisual(visual);
            }
        }
    }

    private StatusEffectVisualSO ResolveActivationVisual()
    {
        if (m_activationVisual == null)
        {
            m_activationVisual = Resources.Load<StatusEffectVisualSO>(ActivationVisualResourcePath);
        }

        return m_activationVisual;
    }
}
