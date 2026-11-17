using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 한 표면 타입에 대응하는 탄착 이펙트·충돌 사운드·데칼 에셋 참조를 보관합니다.
/// </summary>
/// <remarks>
/// 실제 표면 판별과 피드백 생성은 필드 씬의 <see cref="EffectManager"/>가 담당합니다.
/// 이 에셋은 표면 타입별 할당 데이터를 제공하며 아직 실행 로직은 포함하지 않습니다.
/// </remarks>
[CreateAssetMenu(fileName = "SurfaceFeedback", menuName = "GrayZone/Feedback/Surface Feedback")]
public sealed class SurfaceFeedbackSO : ScriptableObject, IFeedbackData
{
    [Header("Surface Identity")]
    [Tooltip("인스펙터와 디버그 로그에서 표면 타입을 구분할 식별 이름입니다.")]
    [SerializeField] private string m_surfaceId = "Default";

    // 재질 식별을 물리 머티리얼에서 SurfaceMaterialType으로 옮겼습니다. 소음 차폐가 같은 축을 읽어야 하는데
    // 물리 머티리얼은 마찰·반발 값이라 식별용으로 전용하면 물리 튜닝과 분류가 서로를 흔들고, 밸런스 SO에
    // Unity Object 참조를 담을 수 없다는 CSV 규칙에도 걸립니다. 자세한 이유는 SurfaceMaterialTag에 적었습니다.
    //
    // [FeedbackReference]도 함께 뗐습니다. 그 어트리뷰트는 에셋 참조의 누락을 잡는 장치인데 열거형은
    // 참조가 아니라 값이라 검사 대상이 아닙니다. 값이 비어 있는 것은 아래 MaterialTypes 자체로 드러납니다.
    [Tooltip("이 Feedback을 적용할 재질 목록입니다. 여러 개를 넣으면 탄착 연출에서는 같은 것으로 퉁쳐집니다.")]
    [SerializeField] private SurfaceMaterialType[] m_materialTypes = Array.Empty<SurfaceMaterialType>();

    [Header("Surface Response")]
    [Tooltip("표면 피격 순간에 재생할 탄착 이펙트 프리팹입니다.")]
    [FeedbackReference(FeedbackReferenceKind.VisualEffect, "표면 탄착 이펙트 프리팹")]
    [SerializeField] private GameObject m_impactEffectPrefab;

    [Tooltip("생성한 표면 탄착 이펙트를 자동 회수하기까지의 시간(초)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_impactEffectLifetime = 1.0f;

    [Tooltip("표면이 피격됐을 때 재생할 FMOD 3D 이벤트입니다. 설정되어 있으면 아래 AudioClip 목록보다 우선합니다.")]
    [SerializeField] private FMODUnity.EventReference m_impactSoundEvent;

    [Tooltip("FMOD 이벤트가 비어 있을 때만 사용하는 구형 Unity AudioClip 후보 목록입니다.")]
    [FeedbackReference(FeedbackReferenceKind.Audio, "표면 피격 사운드 목록")]
    [SerializeField] private AudioClip[] m_impactSounds = Array.Empty<AudioClip>();

    [Tooltip("표면 피격 지점에 남길 데칼 프리팹입니다.")]
    [FeedbackReference(FeedbackReferenceKind.Decal, "표면 데칼 프리팹")]
    [SerializeField] private GameObject m_decalPrefab;

    [Tooltip("생성한 표면 데칼을 자동 제거하기까지의 시간(초)입니다. 0 이하면 자동 제거하지 않습니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_decalLifetime = 20.0f;

    // 데칼 최대 개수는 여기에 두지 않습니다. 개수는 "씬이 얼마나 감당하는가"라는 성능 예산이고,
    // 이 에셋은 "무엇이 보이는가"라는 연출 데이터입니다. 표면마다 상한을 두면 총량이 표면 수에 비례해
    // 늘어나 아무도 씬 전체의 양을 모르게 됩니다. 예산은 EffectPool이 카테고리 단위로 소유합니다.

    /// <summary>표면 타입을 식별하는 이름입니다.</summary>
    public string SurfaceId => m_surfaceId;

    /// <summary>이 Feedback을 적용할 재질 목록입니다.</summary>
    public IReadOnlyList<SurfaceMaterialType> MaterialTypes => m_materialTypes;

    /// <summary>표면 피격 사운드 후보 목록입니다.</summary>
    public IReadOnlyList<AudioClip> ImpactSounds => m_impactSounds;

    /// <summary>표면 피격 위치에서 재생할 FMOD 3D 이벤트입니다.</summary>
    public FMODUnity.EventReference ImpactSoundEvent => m_impactSoundEvent;

    /// <summary>표면 피격 순간에 재생할 탄착 이펙트 프리팹입니다.</summary>
    public GameObject ImpactEffectPrefab => m_impactEffectPrefab;

    /// <summary>표면 탄착 이펙트의 런타임 수명(초)입니다.</summary>
    public float ImpactEffectLifetime => m_impactEffectLifetime;

    /// <summary>표면 피격 데칼 프리팹입니다.</summary>
    public GameObject DecalPrefab => m_decalPrefab;

    /// <summary>표면 피격 데칼의 런타임 수명(초)입니다.</summary>
    public float DecalLifetime => m_decalLifetime;

}
