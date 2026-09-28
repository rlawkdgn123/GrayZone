using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 한 무기 타입이 공유하는 사운드와 시각 피드백 에셋 참조를 보관합니다.
/// </summary>
/// <remarks>
/// 피해량과 연사 속도 같은 수치 밸런스는 <see cref="GunBalanceSO"/>가 담당하고,
/// 이 에셋은 재생하거나 생성할 미디어 에셋의 할당 지점만 제공합니다.
/// </remarks>
[CreateAssetMenu(fileName = "WeaponFeedback", menuName = "GrayZone/Feedback/Weapon Feedback")]
public sealed class WeaponFeedbackSO : ScriptableObject, IFeedbackData
{
    [Header("Audio")]
    [Tooltip("사격할 때 후보 중 하나를 선택해 재생할 사운드 목록입니다.")]
    [FeedbackReference(FeedbackReferenceKind.Audio, "사격 사운드 목록")]
    [SerializeField] private AudioClip[] m_shotSounds = Array.Empty<AudioClip>();

    [Tooltip("탄약이 없거나 사격이 막혔을 때 후보 중 하나를 선택해 재생할 사운드 목록입니다.")]
    [FeedbackReference(FeedbackReferenceKind.Audio, "빈 사격 사운드 목록")]
    [SerializeField] private AudioClip[] m_dryFireSounds = Array.Empty<AudioClip>();

    [Tooltip("재장전할 때 후보 중 하나를 선택해 재생할 사운드 목록입니다.")]
    [FeedbackReference(FeedbackReferenceKind.Audio, "재장전 사운드 목록")]
    [SerializeField] private AudioClip[] m_reloadSounds = Array.Empty<AudioClip>();

    [Header("FMOD Audio (지정 시 AudioClip보다 우선)")]
    [Tooltip("사격할 때 재생할 FMOD 이벤트입니다. 비어 있으면 위 AudioClip 목록을 사용합니다.")]
    [SerializeField] private FMODUnity.EventReference m_shotEvent;

    [Tooltip("탄약이 없거나 사격이 막혔을 때 재생할 FMOD 이벤트입니다. 비어 있으면 위 AudioClip 목록을 사용합니다.")]
    [SerializeField] private FMODUnity.EventReference m_dryFireEvent;

    [Tooltip("재장전할 때 재생할 FMOD 이벤트입니다. 비어 있으면 위 AudioClip 목록을 사용합니다.")]
    [SerializeField] private FMODUnity.EventReference m_reloadEvent;

    [Header("Effects")]
    [Tooltip("총구 소켓에서 재생하거나 생성할 머즐 이펙트 프리팹입니다.")]
    [FeedbackReference(FeedbackReferenceKind.VisualEffect, "머즐 이펙트 프리팹")]
    [SerializeField] private GameObject m_muzzleEffectPrefab;

    [Tooltip("생성한 머즐 이펙트를 자동 제거하기까지의 시간(초)입니다. 0 이하면 자동 제거하지 않습니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_muzzleEffectLifetime = 2.0f;

    [Tooltip("총구에서 히트 지점까지 표시할 레이 또는 트레이서 이펙트 프리팹입니다.")]
    [FeedbackReference(FeedbackReferenceKind.Tracer, "라인/트레이서 이펙트 프리팹")]
    [SerializeField] private GameObject m_tracerEffectPrefab;

    [Tooltip("생성한 레이 또는 트레이서 이펙트를 자동 제거하기까지의 시간(초)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_tracerEffectLifetime = 0.15f;

    [Tooltip("탄이 지형에 멈춘 위치에서 재생할 이 무기 전용 탄착 이펙트 프리팹입니다.")]
    [FeedbackReference(FeedbackReferenceKind.VisualEffect, "총기 전용 탄착 이펙트 프리팹")]
    [SerializeField] private GameObject m_impactEffectPrefab;

    [Tooltip("생성한 총기 전용 탄착 이펙트를 개인 풀로 회수하기까지의 시간(초)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_impactEffectLifetime = 1.0f;

    [Tooltip("탄피 배출 소켓에서 생성할 탄피 프리팹입니다. 반복 생성 시 풀링 대상으로 사용합니다.")]
    [FeedbackReference(FeedbackReferenceKind.Shell, "탄피 프리팹")]
    [SerializeField] private GameObject m_shellPrefab;

    [Tooltip("생성한 탄피 오브젝트를 자동 제거하기까지의 시간(초)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_shellLifetime = 8.0f;

    /// <summary>사격 사운드 후보 목록입니다.</summary>
    public IReadOnlyList<AudioClip> ShotSounds => m_shotSounds;

    /// <summary>빈 사격 사운드 후보 목록입니다.</summary>
    public IReadOnlyList<AudioClip> DryFireSounds => m_dryFireSounds;

    /// <summary>재장전 사운드 후보 목록입니다.</summary>
    public IReadOnlyList<AudioClip> ReloadSounds => m_reloadSounds;

    /// <summary>사격할 때 우선 재생할 FMOD 이벤트입니다.</summary>
    public FMODUnity.EventReference ShotEvent => m_shotEvent;

    /// <summary>빈 사격 때 우선 재생할 FMOD 이벤트입니다.</summary>
    public FMODUnity.EventReference DryFireEvent => m_dryFireEvent;

    /// <summary>재장전할 때 우선 재생할 FMOD 이벤트입니다.</summary>
    public FMODUnity.EventReference ReloadEvent => m_reloadEvent;

    /// <summary>머즐 이펙트 프리팹입니다.</summary>
    public GameObject MuzzleEffectPrefab => m_muzzleEffectPrefab;

    /// <summary>머즐 이펙트의 런타임 수명(초)입니다.</summary>
    public float MuzzleEffectLifetime => m_muzzleEffectLifetime;

    /// <summary>레이 또는 트레이서 이펙트 프리팹입니다.</summary>
    public GameObject TracerEffectPrefab => m_tracerEffectPrefab;

    /// <summary>레이 또는 트레이서 이펙트의 런타임 수명(초)입니다.</summary>
    public float TracerEffectLifetime => m_tracerEffectLifetime;

    /// <summary>이 무기 전용 탄착 이펙트 프리팹입니다.</summary>
    public GameObject ImpactEffectPrefab => m_impactEffectPrefab;

    /// <summary>총기 전용 탄착 이펙트를 개인 풀로 회수하기까지의 시간(초)입니다.</summary>
    public float ImpactEffectLifetime => m_impactEffectLifetime;

    /// <summary>배출할 탄피 프리팹입니다.</summary>
    public GameObject ShellPrefab => m_shellPrefab;

    /// <summary>탄피 오브젝트의 런타임 수명(초)입니다.</summary>
    public float ShellLifetime => m_shellLifetime;
}
