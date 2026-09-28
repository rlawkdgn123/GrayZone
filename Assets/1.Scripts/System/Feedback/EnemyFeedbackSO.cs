using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 한 감염체 타입이 공유하는 피격 표현, 혈흔, 행동 사운드 에셋 참조를 보관합니다.
/// </summary>
/// <remarks>
/// 체력과 이동 속도 같은 수치 밸런스는 <see cref="EnemyBalanceSO"/>가 담당하고,
/// 이 에셋은 감염체 프리팹이 사용할 피드백 미디어의 할당 지점만 제공합니다.
/// </remarks>
[CreateAssetMenu(fileName = "EnemyFeedback", menuName = "GrayZone/Feedback/Enemy Feedback")]
public sealed class EnemyFeedbackSO : ScriptableObject, IFeedbackData
{
    [Header("Hit Feedback")]
    [Tooltip("감염체 피격 위치에서 재생하거나 생성할 이펙트 프리팹입니다.")]
    [FeedbackReference(FeedbackReferenceKind.VisualEffect, "피격 이펙트 프리팹")]
    [SerializeField] private GameObject m_hitEffectPrefab;

    [Tooltip("생성한 피격 이펙트를 자동 제거하기까지의 시간(초)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_hitEffectLifetime = 2.0f;

    [Tooltip("감염체가 피격됐을 때 후보 중 하나를 선택해 재생할 사운드 목록입니다.")]
    [FeedbackReference(FeedbackReferenceKind.Audio, "피격 사운드 목록")]
    [SerializeField] private AudioClip[] m_hitSounds = Array.Empty<AudioClip>();

    [Tooltip("감염체가 피격됐을 때 피격 위치에서 재생할 FMOD 이벤트입니다. 비어 있으면 위 AudioClip 목록을 사용합니다.")]
    [SerializeField] private FMODUnity.EventReference m_hitEvent;

    [Tooltip("감염체 피격 지점 또는 주변 표면에 남길 혈흔 데칼 프리팹입니다.")]
    [FeedbackReference(FeedbackReferenceKind.Decal, "혈흔 데칼 프리팹")]
    [SerializeField] private GameObject m_bloodDecalPrefab;

    [Tooltip("생성한 혈흔 데칼을 자동 제거하기까지의 시간(초)입니다. 0 이하면 자동 제거하지 않습니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_bloodDecalLifetime = 20.0f;

    [Header("Action Audio")]
    [Tooltip("대기 또는 배회 중 후보 중 하나를 선택해 재생할 행동 사운드 목록입니다.")]
    [FeedbackReference(FeedbackReferenceKind.Audio, "대기/배회 사운드 목록")]
    [SerializeField] private AudioClip[] m_idleSounds = Array.Empty<AudioClip>();

    [Tooltip("대상을 발견하고 경계 상태로 전환할 때 재생할 사운드 목록입니다.")]
    [FeedbackReference(FeedbackReferenceKind.Audio, "경계 사운드 목록")]
    [SerializeField] private AudioClip[] m_alertSounds = Array.Empty<AudioClip>();

    [Tooltip("대상을 추적하는 동안 재생할 행동 사운드 목록입니다.")]
    [FeedbackReference(FeedbackReferenceKind.Audio, "추적 사운드 목록")]
    [SerializeField] private AudioClip[] m_chaseSounds = Array.Empty<AudioClip>();

    [Tooltip("공격 행동에서 재생할 사운드 목록입니다.")]
    [FeedbackReference(FeedbackReferenceKind.Audio, "공격 사운드 목록")]
    [SerializeField] private AudioClip[] m_attackSounds = Array.Empty<AudioClip>();

    [Tooltip("사망할 때 재생할 사운드 목록입니다.")]
    [FeedbackReference(FeedbackReferenceKind.Audio, "사망 사운드 목록")]
    [SerializeField] private AudioClip[] m_deathSounds = Array.Empty<AudioClip>();

    /// <summary>피격 이펙트 프리팹입니다.</summary>
    public GameObject HitEffectPrefab => m_hitEffectPrefab;

    /// <summary>피격 이펙트의 런타임 수명(초)입니다.</summary>
    public float HitEffectLifetime => m_hitEffectLifetime;

    /// <summary>피격 사운드 후보 목록입니다.</summary>
    public IReadOnlyList<AudioClip> HitSounds => m_hitSounds;

    /// <summary>피격 위치에서 우선 재생할 FMOD 이벤트입니다.</summary>
    public FMODUnity.EventReference HitEvent => m_hitEvent;

    /// <summary>혈흔 데칼 프리팹입니다.</summary>
    public GameObject BloodDecalPrefab => m_bloodDecalPrefab;

    /// <summary>혈흔 데칼의 런타임 수명(초)입니다.</summary>
    public float BloodDecalLifetime => m_bloodDecalLifetime;

    /// <summary>대기 또는 배회 행동 사운드 후보 목록입니다.</summary>
    public IReadOnlyList<AudioClip> IdleSounds => m_idleSounds;

    /// <summary>경계 행동 사운드 후보 목록입니다.</summary>
    public IReadOnlyList<AudioClip> AlertSounds => m_alertSounds;

    /// <summary>추적 행동 사운드 후보 목록입니다.</summary>
    public IReadOnlyList<AudioClip> ChaseSounds => m_chaseSounds;

    /// <summary>공격 행동 사운드 후보 목록입니다.</summary>
    public IReadOnlyList<AudioClip> AttackSounds => m_attackSounds;

    /// <summary>사망 사운드 후보 목록입니다.</summary>
    public IReadOnlyList<AudioClip> DeathSounds => m_deathSounds;
}
